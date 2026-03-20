using System.ComponentModel;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Emgu.CV;
using Emgu.CV.CvEnum;
using LuminaCalib.Calibration;
using LuminaCalib.Models;
using LuminaCalib.Services;

namespace LuminaCalib.ViewModels;

/// <summary>
/// ViewModel для управления картой глубины: настройки SGBM, пресеты,
/// статистика производительности и конвертация Mat → WriteableBitmap.
/// </summary>
public partial class DepthMapViewModel : ViewModelBase, IDisposable
{
    // ───── Зависимости ─────

    private readonly DepthMapService _depthMapService;
    private readonly DepthMapSettings _settings;
    private readonly SettingsService _settingsService;

    // ───── Состояние ─────

    /// <summary>Включена ли карта глубины.</summary>
    [ObservableProperty]
    private bool _isEnabled;

    /// <summary>Текущее изображение карты глубины для привязки к Image.Source.</summary>
    [ObservableProperty]
    private WriteableBitmap? _depthMapImage;

    /// <summary>Время обработки последнего кадра в миллисекундах.</summary>
    [ObservableProperty]
    private double _processingTimeMs;

    /// <summary>Текущий FPS обработки карты глубины (скользящее среднее).</summary>
    [ObservableProperty]
    private double _depthMapFps;

    // ───── FPS ─────

    /// <summary>Кольцевой буфер отметок времени для расчёта FPS.</summary>
    private readonly Queue<DateTime> _fpsTimestamps = new();

    /// <summary>Размер окна скользящего среднего FPS.</summary>
    private const int FpsWindowSize = 10;

    // ───── Bitmap ─────

    /// <summary>Внутренний WriteableBitmap для отрисовки кадров.</summary>
    private WriteableBitmap? _bitmap;

    /// <summary>Объект синхронизации для доступа к bitmap.</summary>
    private readonly object _bitmapLock = new();

    /// <summary>Объект синхронизации для доступа к ожидающему кадру карты глубины.</summary>
    private readonly object _pendingDepthFrameLock = new();

    /// <summary>Последний ожидающий кадр для отрисовки в UI-потоке.</summary>
    private Mat? _pendingDepthFrame;

    /// <summary>Флаг запланированной отрисовки depth-кадра (0/1).</summary>
    private int _depthFrameRenderScheduled;

    // ───── Привязки ─────

    /// <summary>Настройки карты глубины (привязка через Settings.NumDisparities и т.д.).</summary>
    public DepthMapSettings Settings => _settings;

    /// <summary>Доступные цветовые карты для ComboBox.</summary>
    public DepthColormap[] AvailableColormaps { get; } = Enum.GetValues<DepthColormap>();

    /// <summary>Доступные режимы SGBM для ComboBox.</summary>
    public SgbmMode[] AvailableSgbmModes { get; } = Enum.GetValues<SgbmMode>();

    // ───── Локализованные тексты UI ─────

    /// <summary>Заголовок панели.</summary>
    [ObservableProperty]
    private string _titleText = "Depth Map";

    /// <summary>Текст переключателя включения.</summary>
    [ObservableProperty]
    private string _enableToggleText = "Enable";

    /// <summary>Заголовок секции SGBM.</summary>
    [ObservableProperty]
    private string _sgbmSettingsText = "SGBM Settings";

    /// <summary>Метка «Кол-во диспаратностей».</summary>
    [ObservableProperty]
    private string _numDisparitiesText = "Num Disparities:";

    /// <summary>Метка «Размер блока».</summary>
    [ObservableProperty]
    private string _blockSizeText = "Block Size:";

    /// <summary>Метка «Уникальность».</summary>
    [ObservableProperty]
    private string _uniquenessRatioText = "Uniqueness Ratio:";

    /// <summary>Метка «Окно спеклов».</summary>
    [ObservableProperty]
    private string _speckleWindowSizeText = "Speckle Window Size:";

    /// <summary>Метка «Диапазон спеклов».</summary>
    [ObservableProperty]
    private string _speckleRangeText = "Speckle Range:";

    /// <summary>Метка «Режим SGBM».</summary>
    [ObservableProperty]
    private string _sgbmModeText = "SGBM Mode:";

    /// <summary>Заголовок секции WLS.</summary>
    [ObservableProperty]
    private string _wlsSettingsText = "WLS Filter";

    /// <summary>Текст переключателя WLS.</summary>
    [ObservableProperty]
    private string _useWlsFilterText = "Enable WLS Filter";

    /// <summary>Метка «Лямбда».</summary>
    [ObservableProperty]
    private string _wlsLambdaText = "Lambda:";

    /// <summary>Метка «Сигма цвета».</summary>
    [ObservableProperty]
    private string _wlsSigmaColorText = "Sigma Color:";

    /// <summary>Заголовок секции визуализации.</summary>
    [ObservableProperty]
    private string _visualizationText = "Visualization";

    /// <summary>Метка «Цветовая карта».</summary>
    [ObservableProperty]
    private string _colormapText = "Colormap:";

    /// <summary>Заголовок секции действий.</summary>
    [ObservableProperty]
    private string _actionsText = "Actions";

    /// <summary>Текст кнопки «Сохранить снимок».</summary>
    [ObservableProperty]
    private string _saveSnapshotText = "Save Snapshot";

    /// <summary>Текст кнопки «Экспорт облака точек».</summary>
    [ObservableProperty]
    private string _exportPointCloudText = "Export Point Cloud";

    /// <summary>Текст пресета «Быстрый».</summary>
    [ObservableProperty]
    private string _presetFastText = "Fast";

    /// <summary>Текст пресета «Сбалансированный».</summary>
    [ObservableProperty]
    private string _presetBalancedText = "Balanced";

    /// <summary>Текст пресета «Качество».</summary>
    [ObservableProperty]
    private string _presetQualityText = "Quality";

    /// <summary>Текст строки статуса.</summary>
    [ObservableProperty]
    private string _statusText = "";

    /// <summary>Заголовок секции пресетов.</summary>
    [ObservableProperty]
    private string _presetsText = "Presets";

    /// <summary>Заголовок секции пост-обработки.</summary>
    [ObservableProperty]
    private string _postProcessingText = "Post-Processing";

    /// <summary>Текст переключателя морфологического закрытия.</summary>
    [ObservableProperty]
    private string _useMorphClosingText = "Morphological Closing";

    /// <summary>Метка «Масштаб отображения».</summary>
    [ObservableProperty]
    private string _displayScaleText = "Display Scale:";

    /// <summary>Метка «Pre-filter Cap».</summary>
    [ObservableProperty]
    private string _preFilterCapText = "Pre-filter Cap:";

    /// <summary>Метка «Мин. диспаратность».</summary>
    [ObservableProperty]
    private string _minDisparityText = "Min Disparity:";

    /// <summary>Метка «Размер ядра».</summary>
    [ObservableProperty]
    private string _morphKernelSizeText = "Kernel Size:";

    /// <summary>Метка «Обработка».</summary>
    [ObservableProperty]
    private string _processingTimeLabelText = "Processing:";

    /// <summary>Метка «FPS».</summary>
    [ObservableProperty]
    private string _fpsLabelText = "FPS:";

    /// <summary>Текст заглушки «Калибровка не загружена».</summary>
    [ObservableProperty]
    private string _noCalibrationLoadedText = "No calibration loaded";

    /// <summary>Текст статуса CUDA.</summary>
    [ObservableProperty]
    private string _cudaStatusText = "";

    /// <summary>Заголовок секции ускорения.</summary>
    [ObservableProperty]
    private string _cudaAccelerationText = "Acceleration";

    /// <summary>Текст переключателя CUDA.</summary>
    [ObservableProperty]
    private string _useCudaText = "GPU (CUDA)";

    /// <summary>Предупреждение об ограничениях CUDA-пути (BM вместо SGBM).</summary>
    [ObservableProperty]
    private string _cudaWarningText = "";

    // ───── События для code-behind ─────

    /// <summary>Запрос на сохранение снимка карты глубины (обрабатывается в View).</summary>
    public event Action? RequestSaveSnapshot;

    /// <summary>Запрос на экспорт облака 3D-точек (обрабатывается в View).</summary>
    public event Action? RequestExportPointCloud;

    /// <summary>
    /// Сигнал для View о готовности нового кадра в <see cref="DepthMapImage"/>.
    /// Используется для принудительной инвалидации Image и стабильной перерисовки.
    /// </summary>
    internal event Action? DepthFramePresented;

    // ───── Конструкторы ─────

    /// <summary>
    /// Инициализирует ViewModel с указанным сервисом и настройками карты глубины.
    /// </summary>
    /// <param name="depthMapService">Сервис обработки карты глубины.</param>
    /// <param name="settings">Настройки SGBM / WLS / визуализации.</param>
    /// <param name="settingsService">Сервис настроек приложения.</param>
    public DepthMapViewModel(DepthMapService depthMapService, DepthMapSettings settings, SettingsService settingsService)
    {
        _depthMapService = depthMapService ?? throw new ArgumentNullException(nameof(depthMapService));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));

        _depthMapService.OnDepthMapReady += OnDepthMapReady;
        _depthMapService.OnProcessingTimeMs += OnProcessingTimeMs;
        _settingsService.Settings.PropertyChanged += OnAppSettingsPropertyChanged;
        _settings.PropertyChanged += OnDepthMapSettingsPropertyChanged;

        UpdateLocalizedUiTexts();
    }

    /// <summary>Конструктор по умолчанию (design-time).</summary>
    public DepthMapViewModel()
        : this(CreateDesignTime())
    {
    }

    private static (DepthMapService svc, DepthMapSettings s, SettingsService ss) CreateDesignTime()
    {
        var ss = new SettingsService();
        var s = ss.Settings.DepthMapSettings;
        return (new DepthMapService(new StereoRectifier(), s), s, ss);
    }

    private DepthMapViewModel((DepthMapService svc, DepthMapSettings s, SettingsService ss) args)
        : this(args.svc, args.s, args.ss)
    {
    }

    // ───── Синхронизация IsEnabled ─────

    partial void OnIsEnabledChanged(bool value)
    {
        _depthMapService.IsEnabled = value;
        UpdateStatusText();
    }

    // ───── Обработка событий сервиса ─────

    /// <summary>Обработчик готовности раскрашенной карты глубины.</summary>
    private void OnDepthMapReady(Mat colorized)
    {
        if (colorized == null || colorized.IsEmpty)
        {
            return;
        }

        // Клонируем кадр сразу в worker-потоке, чтобы не зависеть от времени жизни Mat в сервисе.
        QueueLatestDepthFrame(colorized.Clone());

        double? newFps = null;
        lock (_fpsTimestamps)
        {
            var now = DateTime.UtcNow;
            _fpsTimestamps.Enqueue(now);
            while (_fpsTimestamps.Count > FpsWindowSize)
                _fpsTimestamps.Dequeue();

            if (_fpsTimestamps.Count >= 2)
            {
                var oldest = _fpsTimestamps.Peek();
                var elapsed = (now - oldest).TotalSeconds;
                if (elapsed > 0)
                    newFps = Math.Round((_fpsTimestamps.Count - 1) / elapsed, 1);
            }
        }

        if (newFps.HasValue)
            RunOnUiThread(() => DepthMapFps = newFps.Value);
    }

    /// <summary>
    /// Помещает depth-кадр в очередь отрисовки по стратегии latest-frame.
    /// Старый ожидающий кадр заменяется и освобождается.
    /// </summary>
    /// <param name="frame">Кадр, владение которым переходит в очередь.</param>
    /// <param name="scheduleRender">Нужно ли планировать отрисовку в UI-потоке.</param>
    private void QueueLatestDepthFrame(Mat frame, bool scheduleRender = true)
    {
        Mat? staleFrame;
        lock (_pendingDepthFrameLock)
        {
            staleFrame = _pendingDepthFrame;
            _pendingDepthFrame = frame;
        }

        staleFrame?.Dispose();

        if (scheduleRender && Interlocked.Exchange(ref _depthFrameRenderScheduled, 1) == 0)
        {
            RunOnUiThread(RenderPendingDepthFrame);
        }
    }

    /// <summary>
    /// Отрисовывает последний доступный depth-кадр в UI.
    /// Если во время отрисовки пришёл новый кадр, планирует следующий проход.
    /// </summary>
    private void RenderPendingDepthFrame()
    {
        try
        {
            if (TryTakePendingDepthFrame(out var frame))
            {
                try
                {
                    UpdateBitmap(frame);
                }
                finally
                {
                    frame.Dispose();
                }
            }
        }
        finally
        {
            Interlocked.Exchange(ref _depthFrameRenderScheduled, 0);
        }

        if (HasPendingDepthFrame() && Interlocked.Exchange(ref _depthFrameRenderScheduled, 1) == 0)
        {
            RunOnUiThread(RenderPendingDepthFrame);
        }
    }

    /// <summary>Извлекает ожидающий depth-кадр из очереди.</summary>
    private bool TryTakePendingDepthFrame(out Mat frame)
    {
        lock (_pendingDepthFrameLock)
        {
            if (_pendingDepthFrame == null)
            {
                frame = null!;
                return false;
            }

            frame = _pendingDepthFrame;
            _pendingDepthFrame = null;
            return true;
        }
    }

    /// <summary>Проверяет, есть ли ожидающий depth-кадр в очереди.</summary>
    private bool HasPendingDepthFrame()
    {
        lock (_pendingDepthFrameLock)
        {
            return _pendingDepthFrame != null;
        }
    }

    /// <summary>
    /// Тестовый хук: помещает кадр в очередь latest-frame без планирования UI-отрисовки.
    /// </summary>
    internal void QueueDepthFrameForTests(Mat frame)
    {
        if (frame == null || frame.IsEmpty)
        {
            return;
        }

        QueueLatestDepthFrame(frame.Clone(), scheduleRender: false);
    }

    /// <summary>
    /// Тестовый хук: извлекает текущий ожидающий кадр из очереди.
    /// </summary>
    /// <returns>Ожидающий кадр или <c>null</c>, если очередь пуста.</returns>
    internal Mat? TakePendingDepthFrameForTests()
    {
        return TryTakePendingDepthFrame(out var frame) ? frame : null;
    }

    /// <summary>Обработчик времени обработки кадра.</summary>
    private void OnProcessingTimeMs(double ms)
    {
        RunOnUiThread(() =>
        {
            ProcessingTimeMs = Math.Round(ms, 1);
            UpdateStatusText();
        });
    }

    // ───── Mat → WriteableBitmap ─────

    /// <summary>
    /// Конвертирует Mat (CV_8UC3) в WriteableBitmap для отображения в Avalonia Image.
    /// </summary>
    /// <param name="frame">Раскрашенная карта глубины.</param>
    private void UpdateBitmap(Mat frame)
    {
        if (frame == null || frame.IsEmpty) return;

        int width = frame.Width;
        int height = frame.Height;

        lock (_bitmapLock)
        {
            if (_bitmap == null || _bitmap.PixelSize.Width != width || _bitmap.PixelSize.Height != height)
            {
                _bitmap = new WriteableBitmap(
                    new PixelSize(width, height),
                    new Vector(96, 96),
                    PixelFormats.Bgra8888,
                    AlphaFormat.Premul);
            }

            using var frameBuffer = _bitmap.Lock();

            using var bgraMat = new Mat();
            if (frame.NumberOfChannels == 3)
                CvInvoke.CvtColor(frame, bgraMat, ColorConversion.Bgr2Bgra);
            else if (frame.NumberOfChannels == 1)
                CvInvoke.CvtColor(frame, bgraMat, ColorConversion.Gray2Bgra);
            else
                frame.CopyTo(bgraMat);

            var srcPtr = bgraMat.DataPointer;
            var dstPtr = frameBuffer.Address;
            var rowBytes = width * 4;

            for (int y = 0; y < height; y++)
            {
                unsafe
                {
                    Buffer.MemoryCopy(
                        (void*)(srcPtr + y * bgraMat.Step),
                        (void*)(dstPtr + y * frameBuffer.RowBytes),
                        rowBytes, rowBytes);
                }
            }
        }

        DepthMapImage = _bitmap;
        DepthFramePresented?.Invoke();
    }

    // ───── Пресеты ─────

    /// <summary>Применяет пресет «Быстрый» (минимальное качество, максимальная скорость).</summary>
    [RelayCommand]
    private void ApplyPresetFast()
    {
        _settings.NumDisparities = 64;
        _settings.BlockSize = 9;
        _settings.UseWlsFilter = false;
        _settings.UseMorphologicalClosing = false;
        _settings.DisplayScale = 0.5;
        _settings.SgbmMode = SgbmMode.Sgbm3Way;
        _settings.UniquenessRatio = 5;
        _settings.SpeckleWindowSize = 50;
    }

    /// <summary>Применяет пресет «Сбалансированный» (адекватное качество и скорость).</summary>
    [RelayCommand]
    private void ApplyPresetBalanced()
    {
        _settings.NumDisparities = 128;
        _settings.BlockSize = 5;
        _settings.UseWlsFilter = true;
        _settings.UseMorphologicalClosing = true;
        _settings.MorphKernelSize = 5;
        _settings.DisplayScale = 1.0;
        _settings.SgbmMode = SgbmMode.Sgbm3Way;
        _settings.UniquenessRatio = 10;
        _settings.SpeckleWindowSize = 100;
    }

    /// <summary>Применяет пресет «Качество» (максимальное качество, низкая скорость).</summary>
    [RelayCommand]
    private void ApplyPresetQuality()
    {
        _settings.NumDisparities = 256;
        _settings.BlockSize = 5;
        _settings.UseWlsFilter = true;
        _settings.UseMorphologicalClosing = true;
        _settings.MorphKernelSize = 7;
        _settings.DisplayScale = 1.0;
        _settings.SgbmMode = SgbmMode.Sgbm3Way;
        _settings.UniquenessRatio = 15;
        _settings.SpeckleWindowSize = 200;
    }

    // ───── Действия ─────

    /// <summary>Инициирует сохранение снимка карты глубины.</summary>
    [RelayCommand]
    private void SaveSnapshot()
    {
        RequestSaveSnapshot?.Invoke();
    }

    /// <summary>Инициирует экспорт облака 3D-точек.</summary>
    [RelayCommand]
    private void ExportPointCloud()
    {
        RequestExportPointCloud?.Invoke();
    }

    /// <summary>Сохраняет текущий снимок карты глубины в указанную директорию.</summary>
    /// <param name="directory">Путь к директории для сохранения.</param>
    /// <returns>Путь к сохранённому PNG-файлу или <c>null</c>, если снимок недоступен.</returns>
    public string? SaveSnapshotToPath(string directory)
    {
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        var baseName = $"depthmap_{timestamp}";

        // Сохраняем раскрашенный PNG
        using var colorized = _depthMapService.GetLatestColorizedSnapshot();
        if (colorized == null) return null;

        var pngPath = Path.Combine(directory, $"{baseName}.png");
        CvInvoke.Imwrite(pngPath, colorized);

        // Сохраняем сырую диспаратность TIFF (16-bit) в полном разрешении
        using var disparity = _depthMapService.ComputeFullResolutionDisparity();
        if (disparity != null)
        {
            var tiffPath = Path.Combine(directory, $"{baseName}.tiff");
            CvInvoke.Imwrite(tiffPath, disparity);
        }

        return pngPath;
    }

    /// <summary>Экспортирует 3D-облако точек в PLY-файл.</summary>
    /// <param name="filePath">Путь к выходному PLY-файлу.</param>
    /// <returns><c>true</c>, если экспорт выполнен.</returns>
    public bool ExportPointCloudToPath(string filePath)
    {
        // Вычисляем диспаратность в полном разрешении для экспорта
        using var disparity = _depthMapService.ComputeFullResolutionDisparity();
        using var qMatrix = _depthMapService.GetQMatrix();
        if (disparity == null || qMatrix == null) return false;

        using var points3d = DepthMapProcessor.ReprojectTo3D(disparity, qMatrix);
        using var rectLeft = _depthMapService.GetLatestRectifiedLeftSnapshot();

        DepthMapProcessor.ExportPly(filePath, points3d, rectLeft);
        return true;
    }

    // ───── Строка статуса ─────

    /// <summary>Обновляет текст строки статуса на основе текущего состояния.</summary>
    private void UpdateStatusText()
    {
        if (!IsEnabled)
        {
            StatusText = L("Disabled", "Отключено");
            return;
        }

        var modeStr = _settings.SgbmMode.ToString();
        var wlsStr = _settings.UseWlsFilter ? " + WLS" : "";
        var cudaStr = _settings.UseCuda && DepthMapProcessor.IsCudaAvailable && !_settings.UseWlsFilter ? " | CUDA" : "";
        StatusText = $"{ProcessingTimeMs:F1} ms | {DepthMapFps:F1} fps | {modeStr}{wlsStr}{cudaStr}";
    }

    // ───── Локализация ─────

    /// <summary>Обновляет все локализованные тексты UI.</summary>
    private void UpdateLocalizedUiTexts()
    {
        TitleText = L("Depth Map", "Карта глубины");
        EnableToggleText = L("Enable", "Включить");
        SgbmSettingsText = L("SGBM Settings", "Настройки SGBM");
        NumDisparitiesText = L("Num Disparities:", "Кол-во диспаратностей:");
        BlockSizeText = L("Block Size:", "Размер блока:");
        UniquenessRatioText = L("Uniqueness Ratio:", "Уникальность:");
        SpeckleWindowSizeText = L("Speckle Window:", "Окно спеклов:");
        SpeckleRangeText = L("Speckle Range:", "Диапазон спеклов:");
        SgbmModeText = L("Mode:", "Режим:");
        WlsSettingsText = L("WLS Filter", "WLS Фильтр");
        UseWlsFilterText = L("Enable WLS Filter", "Включить WLS фильтр");
        WlsLambdaText = L("Lambda:", "Лямбда:");
        WlsSigmaColorText = L("Sigma Color:", "Сигма цвета:");
        VisualizationText = L("Visualization", "Визуализация");
        ColormapText = L("Colormap:", "Цветовая карта:");
        ActionsText = L("Actions", "Действия");
        SaveSnapshotText = L("Save Snapshot", "Сохранить снимок");
        ExportPointCloudText = L("Export Point Cloud", "Экспорт облака точек");
        PresetFastText = L("Fast", "Быстрый");
        PresetBalancedText = L("Balanced", "Сбалансированный");
        PresetQualityText = L("Quality", "Качество");
        PresetsText = L("Presets", "Пресеты");
        PostProcessingText = L("Post-Processing", "Пост-обработка");
        UseMorphClosingText = L("Morphological Closing", "Морфологическое закрытие");
        DisplayScaleText = L("Display Scale:", "Масштаб отображения:");
        PreFilterCapText = L("Pre-filter Cap:", "Pre-filter Cap:");
        MinDisparityText = L("Min Disparity:", "Мин. диспаратность:");
        MorphKernelSizeText = L("Kernel Size:", "Размер ядра:");
        ProcessingTimeLabelText = L("Processing:", "Обработка:");
        FpsLabelText = L("FPS:", "FPS:");
        NoCalibrationLoadedText = L("No calibration loaded", "Калибровка не загружена");
        CudaAccelerationText = L("Acceleration", "Ускорение");
        UseCudaText = L("GPU (CUDA)", "GPU (CUDA)");
        UpdateCudaStatus();
    }

    /// <summary>Обновляет текст статуса CUDA.</summary>
    private void UpdateCudaStatus()
    {
        if (!DepthMapProcessor.IsCudaAvailable)
            CudaStatusText = L("CUDA: Not available", "CUDA: Недоступен");
        else if (_settings.UseWlsFilter)
            CudaStatusText = L("CUDA: Disabled (WLS active)", "CUDA: Отключён (WLS активен)");
        else if (!_settings.UseCuda)
            CudaStatusText = L("CUDA: Disabled", "CUDA: Отключён");
        else
            CudaStatusText = L("CUDA: Active", "CUDA: Активен");

        // Show warning when CUDA is effectively active
        bool cudaActive = DepthMapProcessor.IsCudaAvailable && _settings.UseCuda && !_settings.UseWlsFilter;
        CudaWarningText = cudaActive
            ? L("⚠ CUDA uses basic StereoBM (lower quality than SGBM). Recommended for preview only.",
                "⚠ CUDA использует базовый StereoBM (качество ниже SGBM). Рекомендуется только для превью.")
            : "";
    }

    // ───── Обработчик изменения настроек ─────

    /// <summary>
    /// Обработчик изменения настроек приложения. Перезагружает локализованные тексты при смене языка.
    /// </summary>
    private void OnAppSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AppSettings.Language))
        {
            UpdateLocalizedUiTexts();
        }
    }

    /// <summary>
    /// Обработчик изменений настроек карты глубины. Обновляет статус CUDA при смене UseCuda/UseWlsFilter.
    /// </summary>
    private void OnDepthMapSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DepthMapSettings.UseCuda) or nameof(DepthMapSettings.UseWlsFilter))
            UpdateCudaStatus();
    }

    // ───── Dispose ─────

    /// <summary>Флаг освобождения ресурсов.</summary>
    private bool _disposed;

    /// <summary>Освобождает ресурсы и отписывается от событий сервиса.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _settingsService.Settings.PropertyChanged -= OnAppSettingsPropertyChanged;
        _settings.PropertyChanged -= OnDepthMapSettingsPropertyChanged;
        _depthMapService.OnDepthMapReady -= OnDepthMapReady;
        _depthMapService.OnProcessingTimeMs -= OnProcessingTimeMs;

        lock (_pendingDepthFrameLock)
        {
            _pendingDepthFrame?.Dispose();
            _pendingDepthFrame = null;
        }

        lock (_bitmapLock)
        {
            _bitmap?.Dispose();
            _bitmap = null;
            DepthMapImage = null;
        }
    }
}
