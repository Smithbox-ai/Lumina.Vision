using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LuminaCalib.Calibration;
using LuminaCalib.Devices;
using LuminaCalib.Models;
using LuminaCalib.Services;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using System.Linq;

namespace LuminaCalib.ViewModels;

/// <summary>
/// Calibration workflow states.
/// </summary>
public enum CalibrationState
{
    Setup,
    Connecting,
    Capturing,
    Processing,
    Result,
    Saving
}

/// <summary>
/// ViewModel for the calibration workflow.
/// Implements a state machine for guiding users through calibration.
/// </summary>
public partial class CalibrationViewModel : ViewModelBase, IDisposable
{
    /// <summary>Сервис настроек приложения.</summary>
    private readonly SettingsService _settingsService;
    /// <summary>Синхронизатор стереопар кадров.</summary>
    private readonly StereoSynchronizer _synchronizer;
    /// <summary>Пул памяти для матриц OpenCV.</summary>
    private readonly MatPool _matPool;
    
    /// <summary>Детектор углов калибровочной доски.</summary>
    private CornerDetector? _cornerDetector;
    /// <summary>Движок калибровки камер.</summary>
    private CalibrationEngine? _calibrationEngine;
    /// <summary>Ректификатор стереопар.</summary>
    private StereoRectifier? _rectifier;
    
    /// <summary>Источник кадров левой камеры.</summary>
    private EmguCameraSource? _leftCamera;
    /// <summary>Источник кадров правой камеры.</summary>
    private EmguCameraSource? _rightCamera;
    /// <summary>Асинхронный читатель кадров левой камеры.</summary>
    private AsyncFrameReader? _leftReader;
    /// <summary>Асинхронный читатель кадров правой камеры.</summary>
    private AsyncFrameReader? _rightReader;
    /// <summary>Токен отмены захвата.</summary>
    private CancellationTokenSource? _captureCts;
    
    /// <summary>Коллекция захваченных стереопар кадров.</summary>
    private readonly List<StereoFramePair> _capturedPairs = [];
    /// <summary>Текущий результат калибровки.</summary>
    private CalibrationResult? _currentCalibration;
    /// <summary>Момент начала стабильного обнаружения паттерна обеими камерами.</summary>
    private DateTime? _bothPatternsSinceUtc;
    /// <summary>Флаг освобождения ресурсов.</summary>
    private bool _disposed;

    // === State Machine ===
    
    /// <summary>Текущее состояние машины калибровки.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentStepIndex))]
    [NotifyPropertyChangedFor(nameof(CanGoBack))]
    [NotifyPropertyChangedFor(nameof(CanGoNext))]
    [NotifyPropertyChangedFor(nameof(IsSetupState))]
    [NotifyPropertyChangedFor(nameof(IsCapturingState))]
    [NotifyPropertyChangedFor(nameof(IsProcessingState))]
    [NotifyPropertyChangedFor(nameof(IsResultState))]
    private CalibrationState _currentState = CalibrationState.Setup;

    /// <summary>Индекс текущего шага мастера (0–3).</summary>
    public int CurrentStepIndex => CurrentState switch
    {
        CalibrationState.Setup => 0,
        CalibrationState.Connecting => 0,
        CalibrationState.Capturing => 1,
        CalibrationState.Processing => 2,
        CalibrationState.Result => 3,
        CalibrationState.Saving => 3,
        _ => 0
    };

    /// <summary>Индикатор: состояние — настройка или подключение.</summary>
    public bool IsSetupState => CurrentState is CalibrationState.Setup or CalibrationState.Connecting;
    /// <summary>Индикатор: состояние — захват кадров.</summary>
    public bool IsCapturingState => CurrentState == CalibrationState.Capturing;
    /// <summary>Индикатор: состояние — обработка калибровки.</summary>
    public bool IsProcessingState => CurrentState == CalibrationState.Processing;
    /// <summary>Индикатор: состояние — результат или сохранение.</summary>
    public bool IsResultState => CurrentState is CalibrationState.Result or CalibrationState.Saving;
    /// <summary>Можно ли вернуться назад в мастере.</summary>
    public bool CanGoBack => CurrentState != CalibrationState.Setup && CurrentState != CalibrationState.Processing;
    /// <summary>Можно ли перейти дальше в мастере.</summary>
    public bool CanGoNext => CurrentState == CalibrationState.Result;

    // === Camera Status ===
    
    /// <summary>Подключена ли левая камера.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AreCamerasConnected))]
    [NotifyPropertyChangedFor(nameof(CanStartCapture))]
    private bool _leftCameraConnected;

    /// <summary>Подключена ли правая камера.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AreCamerasConnected))]
    [NotifyPropertyChangedFor(nameof(CanStartCapture))]
    private bool _rightCameraConnected;

    /// <summary>FPS левой камеры.</summary>
    [ObservableProperty]
    private double _leftCameraFps;

    /// <summary>FPS правой камеры.</summary>
    [ObservableProperty]
    private double _rightCameraFps;

    /// <summary>Подключены ли обе камеры.</summary>
    public bool AreCamerasConnected => LeftCameraConnected && RightCameraConnected;

    // === Capture Progress ===
    
    /// <summary>Количество захваченных стереопар.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCalibrate))]
    [NotifyPropertyChangedFor(nameof(CaptureProgress))]
    private int _capturedPairCount;

    /// <summary>Требуемое количество стереопар для калибровки.</summary>
    [ObservableProperty]
    private int _requiredPairs = 20;

    /// <summary>Процент выполнения захвата (0–100).</summary>
    public double CaptureProgress => RequiredPairs > 0 ? (double)CapturedPairCount / RequiredPairs * 100 : 0;
    
    /// <summary>Можно ли начинать захват (<c>true</c>, если камеры подключены и состояние — захват).</summary>
    public bool CanStartCapture => AreCamerasConnected && CurrentState == CalibrationState.Capturing;
    /// <summary>Подключены ли обе камеры.</summary>
    public bool CanCapture => AreCamerasConnected;
    /// <summary>Достаточно ли кадров для начала калибровки (мин. 10).</summary>
    public bool CanCalibrate => CapturedPairCount >= 10;

    // === Pattern Detection ===
    
    /// <summary>Обнаружен ли паттерн на последнем кадре левой камеры.</summary>
    [ObservableProperty]
    private bool _leftPatternFound;

    /// <summary>Обнаружен ли паттерн на последнем кадре правой камеры.</summary>
    [ObservableProperty]
    private bool _rightPatternFound;

    /// <summary>Стабильно ли положение доски (паттерн виден достаточно долго).</summary>
    [ObservableProperty]
    private bool _patternsStable;

    /// <summary>Секунд непрерывного стабильного обнаружения паттерна.</summary>
    [ObservableProperty]
    private double _stabilitySeconds;

    // === Calibration Result ===
    
    /// <summary>Ошибка репроекции последней калибровки (пиксели).</summary>
    [ObservableProperty]
    private double _reprojectionError;

    /// <summary>Оценка качества калибровки.</summary>
    [ObservableProperty]
    private CalibrationQuality _calibrationQuality;

    /// <summary>Строка статуса / сообщения о текущем этапе калибровки.</summary>
    [ObservableProperty]
    private string _calibrationStatusMessage = "";

    /// <summary>Цвет индикатора качества калибровки.</summary>
    [ObservableProperty]
    private IBrush _qualityColor = Brushes.Gray;

    /// <summary>Показывать ли ректифицированный предпросмотр вместо сырого.</summary>
    [ObservableProperty]
    private bool _showRectified;

    // === Mode Selection ===
    
    /// <summary>Текущий режим калибровки.</summary>
    [ObservableProperty]
    private CalibrationMode _calibrationMode = CalibrationMode.Auto;

    // === Events for View ===
    
    /// <summary>Событие с обработанным кадром левой камеры: кадр, флаг обнаружения паттерна и массив углов.</summary>
    public event Action<FrameRaw, bool, System.Drawing.PointF[]?>? OnLeftFrameProcessed;
    /// <summary>Событие с обработанным кадром правой камеры: кадр, флаг обнаружения паттерна и массив углов.</summary>
    public event Action<FrameRaw, bool, System.Drawing.PointF[]?>? OnRightFrameProcessed;

    /// <summary>
    /// Инициализирует ViewModel с зависимостями по умолчанию (для дизайнера).
    /// </summary>
    public CalibrationViewModel() 
        : this(new SettingsService(), new StereoSynchronizer(), new MatPool())
    {
    }

    /// <summary>
    /// Инициализирует ViewModel с инекцируемыми зависимостями.
    /// </summary>
    /// <param name="settingsService">Сервис настроек.</param>
    /// <param name="synchronizer">Синхронизатор стереопар.</param>
    /// <param name="matPool">Пул памяти для матриц OpenCV.</param>
    public CalibrationViewModel(
        SettingsService settingsService,
        StereoSynchronizer synchronizer,
        MatPool matPool)
    {
        _settingsService = settingsService;
        _synchronizer = synchronizer;
        _matPool = matPool;
        
        var settings = _settingsService.Settings;
        RequiredPairs = settings.RequiredFrames;
        CalibrationMode = settings.CalibrationMode;
        _synchronizer.ToleranceMs = settings.SyncToleranceMs;

        InitializeDetector();
    }

    /// <summary>Инициализирует детектор углов, движок калибровки и ректификатор на основе текущих настроек.</summary>
    private void InitializeDetector()
    {
        var settings = _settingsService.Settings;
        
        _cornerDetector = new CornerDetector(
            settings.PatternWidth,
            settings.PatternHeight,
            settings.BoardType,
            settings.CharucoDictionary,
            settings.SquareSize,
            settings.SquareSize * settings.MarkerSizeRatio);

        _calibrationEngine = new CalibrationEngine(_cornerDetector);
        if (settings.UseRationalModel)
            _calibrationEngine.IntrinsicCalibrationFlags = CalibType.RationalModel;
        _calibrationEngine.OnStageChanged += stage =>
            RunOnUiThread(() => CalibrationStatusMessage = stage);
        _rectifier = new StereoRectifier();
    }

    // === Commands ===

    /// <summary>Подключает обе камеры и переводит в состояние <see cref="CalibrationState.Capturing"/>.</summary>
    [RelayCommand]
    private async Task ConnectCamerasAsync()
    {
        if (AreCamerasConnected) return;
        
        CurrentState = CalibrationState.Connecting;
        CalibrationStatusMessage = "Connecting to cameras...";

        var settings = _settingsService.Settings;
        
        if (string.IsNullOrEmpty(settings.LeftCameraUrl) || string.IsNullOrEmpty(settings.RightCameraUrl))
        {
            CalibrationStatusMessage = "Camera URLs not configured. Check Settings.";
            CurrentState = CalibrationState.Setup;
            return;
        }

        _captureCts = new CancellationTokenSource();

        try
        {
            _leftCamera = CreateCameraSource("Left", settings.LeftCameraUrl, 
                settings.LeftCameraLogin, settings.LeftCameraPassword, settings);
            _rightCamera = CreateCameraSource("Right", settings.RightCameraUrl, 
                settings.RightCameraLogin, settings.RightCameraPassword, settings);

            var queueCapacity = Math.Clamp(settings.BufferSize, 1, 32);
            _leftReader = new AsyncFrameReader(_leftCamera, queueCapacity, _matPool);
            _rightReader = new AsyncFrameReader(_rightCamera, queueCapacity, _matPool);

            WireCameraEvents();

            await Task.WhenAll(
                _leftReader.StartAsync(_captureCts.Token),
                _rightReader.StartAsync(_captureCts.Token));

            CurrentState = CalibrationState.Capturing;
            CalibrationStatusMessage = "Cameras connected. Start capturing calibration frames.";
        }
        catch (Exception ex)
        {
            CalibrationStatusMessage = $"Connection failed: {ex.Message}";
            await DisconnectCamerasAsync();
            CurrentState = CalibrationState.Setup;
        }
    }

    /// <summary>Создаёт источник кадров для камеры с заданными параметрами подключения.</summary>
    private EmguCameraSource CreateCameraSource(string id, string url, 
        string login, string password, AppSettings settings)
    {
        return new EmguCameraSource(
            id, url, login, password,
            settings.CameraBackend,
            settings.HwAcceleration,
            settings.TransportProtocol,
            matPool: _matPool);
    }

    /// <summary>Подписывает ViewModel на события камер (смена подключения, новые кадры).</summary>
    private void WireCameraEvents()
    {
        if (_leftCamera == null || _rightCamera == null || _leftReader == null || _rightReader == null) return;

        _leftCamera.OnConnectionChanged += connected => 
        {
            RunOnUiThread(() =>
            {
                LeftCameraConnected = connected;
                if (!connected) CalibrationStatusMessage = "Left camera disconnected";
            });
        };
        
        _rightCamera.OnConnectionChanged += connected => 
        {
            RunOnUiThread(() =>
            {
                RightCameraConnected = connected;
                if (!connected) CalibrationStatusMessage = "Right camera disconnected";
            });
        };

        _leftReader.OnFrameReady += ProcessLeftFrame;
        _rightReader.OnFrameReady += ProcessRightFrame;
    }

    /// <summary>Обрабатывает новый кадр левой камеры: детекция углов, обновление UI.</summary>
    private void ProcessLeftFrame(FrameRaw frame)
    {
        var fps = _leftCamera?.CurrentFps ?? 0;
        _synchronizer.PushLeft(frame.Clone());

        // Detect corners for overlay
        System.Drawing.PointF[]? corners = null;
        bool found = false;
        
        if (_cornerDetector != null)
        {
            found = _cornerDetector.DetectCorners(frame.Image, out corners!, out _);
        }

        var frameForUi = frame.Clone();
        RunOnUiThread(() =>
        {
            try
            {
                LeftCameraFps = fps;
                LeftPatternFound = found;
                UpdatePatternStability();
                OnLeftFrameProcessed?.Invoke(frameForUi, found, corners);
            }
            finally
            {
                frameForUi.Dispose();
            }
        });
    }

    /// <summary>Обрабатывает новый кадр правой камеры: детекция углов, обновление UI.</summary>
    private void ProcessRightFrame(FrameRaw frame)
    {
        var fps = _rightCamera?.CurrentFps ?? 0;
        _synchronizer.PushRight(frame.Clone());

        System.Drawing.PointF[]? corners = null;
        bool found = false;
        
        if (_cornerDetector != null)
        {
            found = _cornerDetector.DetectCorners(frame.Image, out corners!, out _);
        }

        var frameForUi = frame.Clone();
        RunOnUiThread(() =>
        {
            try
            {
                RightCameraFps = fps;
                RightPatternFound = found;
                UpdatePatternStability();
                OnRightFrameProcessed?.Invoke(frameForUi, found, corners);
            }
            finally
            {
                frameForUi.Dispose();
            }
        });
    }

    /// <summary>Обновляет счётчик стабильности: сколько секунд паттерн виден одновременно на обеих камерах.</summary>
    private void UpdatePatternStability()
    {
        var settings = _settingsService.Settings;
        var threshold = settings.StabilityThresholdSeconds <= 0 ? 1.0 : settings.StabilityThresholdSeconds;

        if (LeftPatternFound && RightPatternFound)
        {
            _bothPatternsSinceUtc ??= DateTime.UtcNow;
            StabilitySeconds = (DateTime.UtcNow - _bothPatternsSinceUtc.Value).TotalSeconds;
            PatternsStable = StabilitySeconds >= threshold;
        }
        else
        {
            _bothPatternsSinceUtc = null;
            StabilitySeconds = 0;
            PatternsStable = false;
        }
    }

    /// <summary>Отключает обе камеры и освобождает ресурсы.</summary>
    [RelayCommand]
    private async Task DisconnectCamerasAsync()
    {
        var captureCts = _captureCts;
        var leftReader = _leftReader;
        var rightReader = _rightReader;
        var leftCamera = _leftCamera;
        var rightCamera = _rightCamera;

        _captureCts = null;
        _leftReader = null;
        _rightReader = null;
        _leftCamera = null;
        _rightCamera = null;

        captureCts?.Cancel();

        if (leftReader != null)
        {
            leftReader.OnFrameReady -= ProcessLeftFrame;
        }
        if (rightReader != null)
        {
            rightReader.OnFrameReady -= ProcessRightFrame;
        }

        if (leftReader != null)
        {
            try
            {
                await leftReader.StopAsync();
            }
            catch
            {
                // Ignore stop errors during cleanup.
            }
            leftReader.Dispose();
        }

        if (rightReader != null)
        {
            try
            {
                await rightReader.StopAsync();
            }
            catch
            {
                // Ignore stop errors during cleanup.
            }
            rightReader.Dispose();
        }

        leftCamera?.Dispose();
        rightCamera?.Dispose();
        captureCts?.Dispose();

        LeftCameraConnected = false;
        RightCameraConnected = false;
        LeftCameraFps = 0;
        RightCameraFps = 0;

        if (CurrentState == CalibrationState.Capturing)
        {
            CurrentState = CalibrationState.Setup;
        }
    }

    /// <summary>Захватывает текущую стереопару кадров и добавляет её в набор для калибровки.</summary>
    [RelayCommand]
    private void CaptureFrame()
    {
        if (!CanCapture)
        {
            return;
        }

        var pair = _synchronizer.TryGetLatestPair();
        if (pair == null) return;

        bool added;
        if (CalibrationMode == CalibrationMode.SingleCamera)
        {
            var leftAdded = _calibrationEngine!.AddSingleImage(pair.Left.Image, isLeft: true);
            var rightAdded = _calibrationEngine!.AddSingleImage(pair.Right.Image, isLeft: false);
            added = leftAdded || rightAdded;
        }
        else
        {
            if (!LeftPatternFound || !RightPatternFound)
            {
                pair.Dispose();
                CalibrationStatusMessage = "Pattern not detected in both frames. Try again.";
                return;
            }

            added = _calibrationEngine!.AddImagePair(pair.Left.Image, pair.Right.Image);
        }

        if (added)
        {
            _capturedPairs.Add(pair);
            CapturedPairCount = _capturedPairs.Count;
            CalibrationStatusMessage = $"Captured pair {CapturedPairCount}/{RequiredPairs}";

            if (CapturedPairCount >= RequiredPairs)
            {
                CalibrationStatusMessage = $"Ready to calibrate! ({CapturedPairCount} pairs captured)";
            }
        }
        else
        {
            pair.Dispose();
            CalibrationStatusMessage = "Pattern not detected in both frames. Try again.";
        }
    }

    /// <summary>Запускает процесс вычисления калибровки на основе захваченных пар кадров.</summary>
    [RelayCommand(CanExecute = nameof(CanCalibrate))]
    private async Task CalibrateAsync()
    {
        CurrentState = CalibrationState.Processing;
        CalibrationStatusMessage = "Computing calibration...";

        try
        {
            await Task.Run(() =>
            {
                _currentCalibration = CalibrationMode switch
                {
                    CalibrationMode.Auto => _calibrationEngine!.CalibrateFullStereo(),
                    CalibrationMode.StereoOnly => CalibrateStereoOnlyMode(),
                    CalibrationMode.SingleCamera => CalibrateSingleCameraMode(),
                    _ => _calibrationEngine!.CalibrateFullStereo()
                };
            });

            // Update result UI
            ReprojectionError = _currentCalibration!.ReprojectionError;
            CalibrationQuality = _currentCalibration.Quality;
            
            UpdateQualityDisplay();

            // Initialize rectifier
            _rectifier!.Initialize(_currentCalibration);

            CurrentState = CalibrationState.Result;

            if (_settingsService.Settings.AutoSaveCalibration)
            {
                await SaveCalibrationAsync();
            }
        }
        catch (Exception ex)
        {
            CalibrationStatusMessage = $"Calibration failed: {ex.Message}";
            CurrentState = CalibrationState.Capturing;
        }
    }

    /// <summary>Производит калибровку в режиме StereoOnly, используя существующий файл внутренних параметров.</summary>
    private CalibrationResult CalibrateStereoOnlyMode()
    {
        var settings = _settingsService.Settings;
        var calibrationFile = GetLatestCalibrationFile(settings.CalibrationDataPath);
        if (calibrationFile == null)
        {
            throw new InvalidOperationException(
                $"StereoOnly mode requires an existing calibration file in '{settings.CalibrationDataPath}'.");
        }

        using var intrinsics = CalibrationResult.LoadFromXml(calibrationFile);
        return _calibrationEngine!.CalibrateStereoWithIntrinsics(
            intrinsics.CameraMatrixLeft,
            intrinsics.DistCoeffsLeft,
            intrinsics.CameraMatrixRight,
            intrinsics.DistCoeffsRight);
    }

    /// <summary>Производит калибровку каждой камеры отдельно и объединяет результаты (SingleCamera mode).</summary>
    private CalibrationResult CalibrateSingleCameraMode()
    {
        using var left = _calibrationEngine!.CalibrateSingleCamera(isLeft: true);
        using var right = _calibrationEngine.CalibrateSingleCamera(isLeft: false);

        return new CalibrationResult
        {
            CameraMatrixLeft = left.CameraMatrix.Clone(),
            CameraMatrixRight = right.CameraMatrix.Clone(),
            DistCoeffsLeft = left.DistCoeffs.Clone(),
            DistCoeffsRight = right.DistCoeffs.Clone(),
            R = CreateIdentity(3),
            T = CreateZero(3, 1),
            E = CreateZero(3, 3),
            F = CreateZero(3, 3),
            R1 = CreateIdentity(3),
            R2 = CreateIdentity(3),
            P1 = CreateProjection(),
            P2 = CreateProjection(),
            Q = CreateIdentity(4),
            ReprojectionError = (left.ReprojectionError + right.ReprojectionError) / 2.0,
            ImageSize = left.ImageSize,
            ImagePairCount = Math.Min(left.ImageCount, right.ImageCount),
            CalibrationDate = DateTime.Now
        };
    }

    /// <summary>Создаёт нулевую матрицу (CV_64F).</summary>
    private static Mat CreateZero(int rows, int cols)
    {
        var mat = new Mat(rows, cols, DepthType.Cv64F, 1);
        mat.SetTo(new MCvScalar(0));
        return mat;
    }

    /// <summary>Создаёт единичную матрицу (CV_64F).</summary>
    private static Mat CreateIdentity(int size)
    {
        var mat = CreateZero(size, size);
        CvInvoke.SetIdentity(mat, new MCvScalar(1));
        return mat;
    }

    /// <summary>Создаёт проекционную матрицу 3×4 (CV_64F).</summary>
    private static Mat CreateProjection()
    {
        var mat = CreateZero(3, 4);
        CvInvoke.SetIdentity(mat, new MCvScalar(1));
        return mat;
    }

    /// <summary>Находит последний XML-файл калибровки в указанной папке. Относительный путь разрешается относительно BaseDirectory.</summary>
    private static string? GetLatestCalibrationFile(string calibrationPath)
    {
        var fullPath = CalibrationHelpers.ResolvePath(calibrationPath, "./CalibrationData");
        if (!Directory.Exists(fullPath))
        {
            return null;
        }

        return Directory
            .GetFiles(fullPath, "*.xml")
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    /// <summary>Обновляет цвет и строку статуса согласно текущему качеству калибровки.</summary>
    private void UpdateQualityDisplay()
    {
        QualityColor = CalibrationQuality switch
        {
            CalibrationQuality.Excellent => new SolidColorBrush(Color.FromRgb(0, 200, 83)),
            CalibrationQuality.Good => new SolidColorBrush(Color.FromRgb(255, 214, 0)),
            _ => new SolidColorBrush(Color.FromRgb(255, 82, 82))
        };

        CalibrationStatusMessage = CalibrationQuality switch
        {
            CalibrationQuality.Excellent => $"Excellent calibration! Error: {ReprojectionError:F3} px",
            CalibrationQuality.Good => $"Good calibration. Error: {ReprojectionError:F3} px",
            _ => $"Poor calibration (Error: {ReprojectionError:F3} px). Consider recapturing."
        };
    }

    /// <summary>Сохраняет результат калибровки в XML-файл в папке CalibrationDataPath.</summary>
    [RelayCommand]
    private async Task SaveCalibrationAsync()
    {
        if (_currentCalibration == null) return;

        CurrentState = CalibrationState.Saving;
        CalibrationStatusMessage = "Saving calibration...";

        try
        {
            var settings = _settingsService.Settings;
            var path = CalibrationHelpers.ResolvePath(settings.CalibrationDataPath, "./CalibrationData");
            Directory.CreateDirectory(path);

            var prefix = CalibrationMode switch
            {
                CalibrationMode.Auto => "StereoAuto",
                CalibrationMode.StereoOnly => "StereoOnly",
                CalibrationMode.SingleCamera => "SingleCamera",
                _ => "Calibration"
            };
            var filename = $"{prefix}_{DateTime.Now:yyyy-MM-dd_HH-mm}.xml";
            var fullPath = Path.Combine(path, filename);

            await Task.Run(() => _currentCalibration.SaveToXml(fullPath));

            CalibrationStatusMessage = $"Saved to {filename}";
        }
        catch (Exception ex)
        {
            CalibrationStatusMessage = $"Save failed: {ex.Message}";
        }
        finally
        {
            CurrentState = CalibrationState.Result;
        }
    }

    /// <summary>Сбрасывает все захваченные пары и очищает движок калибровки.</summary>
    [RelayCommand]
    private void ClearCapturedFrames()
    {
        foreach (var pair in _capturedPairs)
        {
            pair.Dispose();
        }
        _capturedPairs.Clear();
        
        _calibrationEngine?.Clear();
        CapturedPairCount = 0;
        CalibrationStatusMessage = "Captured frames cleared.";
    }

    /// <summary>Сбрасывает состояние и запускает новую калибровку с нуля.</summary>
    [RelayCommand]
    private void StartNewCalibration()
    {
        ClearCapturedFrames();
        _currentCalibration?.Dispose();
        _currentCalibration = null;
        
        ReprojectionError = 0;
        CurrentState = AreCamerasConnected ? CalibrationState.Capturing : CalibrationState.Setup;
        CalibrationStatusMessage = "Ready to capture new calibration frames.";
    }

    /// <summary>Выполняет переключение режима предпросмотра (ректифицированный / исходный).</summary>
    partial void OnShowRectifiedChanged(bool value)
    {
        // Toggle between rectified/raw view
        CalibrationStatusMessage = value ? "Showing rectified view" : "Showing raw view";
    }

    /// <summary>Освобождает все управляемые ресурсы.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _captureCts?.Cancel();
        _captureCts?.Dispose();
        _leftReader?.Dispose();
        _rightReader?.Dispose();
        _synchronizer.Dispose();
        _matPool.Dispose();
        _calibrationEngine?.Dispose();
        _rectifier?.Dispose();
        _currentCalibration?.Dispose();

        foreach (var pair in _capturedPairs)
        {
            pair.Dispose();
        }
        _capturedPairs.Clear();
    }
}

