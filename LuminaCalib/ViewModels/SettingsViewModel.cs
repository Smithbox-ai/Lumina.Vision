using System.ComponentModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LuminaCalib.Devices;
using LuminaCalib.Models;
using LuminaCalib.Services;

namespace LuminaCalib.ViewModels;

/// <summary>
/// ViewModel окна настроек приложения.
/// </summary>
public partial class SettingsViewModel : ViewModelBase, IDisposable
{
    /// <summary>Сервис чтения и сохранения настроек.</summary>
    private readonly SettingsService _settingsService;
    /// <summary>Флаг освобождения ресурсов.</summary>
    private bool _disposed;

    /// <summary>Настройки приложения для прямой привязки к UI.</summary>
    // Expose settings for binding
    public AppSettings Settings => _settingsService.Settings;

    // === Binding Collections for ComboBoxes ===

    /// <summary>Доступные варианты языка интерфейса.</summary>
    public List<string> AvailableLanguages { get; } = ["ru", "en"];
    
    /// <summary>Доступные значения транспортного протокола.</summary>
    public TransportProtocol[] AvailableTransportProtocols { get; } = Enum.GetValues<TransportProtocol>();
    
    /// <summary>Доступные бэкенды захвата видео.</summary>
    public CameraBackend[] AvailableBackends { get; } = Enum.GetValues<CameraBackend>();
    
    /// <summary>Доступные режимы аппаратного ускорения.</summary>
    public HwAcceleration[] AvailableAccelerations { get; } = Enum.GetValues<HwAcceleration>();
    
    /// <summary>Доступные типы калибровочных досок.</summary>
    public BoardType[] AvailableBoardTypes { get; } = Enum.GetValues<BoardType>();

    // === Events for Code-Behind Dialog Handling ===

    /// <summary>Запрос открытия диалога выбора папки данных калибровки.</summary>
    public event Action? RequestFolderPicker;
    /// <summary>Запрос открытия диалога выбора папки сессий захвата.</summary>
    public event Action? RequestCaptureSessionsFolderPicker;
    
    /// <summary>Запрос перехода на вкладку генератора досок.</summary>
    public event Action? RequestNavigateToBoardGenerator;
    /// <summary>Запрос закрытия окна настроек.</summary>
    public event Action? RequestClose;

    // === Tab Navigation ===

    /// <summary>Индекс активной вкладки настроек.</summary>
    [ObservableProperty]
    private int _selectedTabIndex;

    /// <summary>Показывать ли расширенные настройки камеры.</summary>
    [ObservableProperty]
    private bool _showAdvancedSettings;
    
    /// <summary>Строка статуса для отображения результата сохранения настроек.</summary>
    [ObservableProperty]
    private string _statusMessage = string.Empty;

    /// <summary>Локализованный заголовок окна настроек.</summary>
    [ObservableProperty]
    private string _settingsHeaderText = "Settings";

    /// <summary>Локализованное название вкладки «Общие».</summary>
    [ObservableProperty]
    private string _generalTabText = "General";

    /// <summary>Локализованное название вкладки «Камеры».</summary>
    [ObservableProperty]
    private string _camerasTabText = "Cameras";

    /// <summary>Локализованное название вкладки «Доска».</summary>
    [ObservableProperty]
    private string _boardTabText = "Board";

    /// <summary>Локализованный заголовок секции общих настроек приложения.</summary>
    [ObservableProperty]
    private string _applicationSettingsTitleText = "Application Settings";

    /// <summary>Локализованная метка «Язык».</summary>
    [ObservableProperty]
    private string _languageLabelText = "Language:";

    /// <summary>Локализованная метка «Автосохранение».</summary>
    [ObservableProperty]
    private string _autoSaveLabelText = "Auto-save:";

    /// <summary>Локализованный текст чекбокса автосохранения калибровки.</summary>
    [ObservableProperty]
    private string _autoSaveCheckText = "Save calibration automatically";

    /// <summary>Локализованная метка поля пути к данным калибровки.</summary>
    [ObservableProperty]
    private string _dataPathLabelText = "Data Path:";

    /// <summary>Локализованная метка поля пути к сессиям захвата.</summary>
    [ObservableProperty]
    private string _captureSessionsPathLabelText = "Capture Sessions Path:";

    /// <summary>Локализованный текст кнопки «Обзор».</summary>
    [ObservableProperty]
    private string _browseButtonText = "Browse";

    /// <summary>Локализованный заголовок секции левой камеры.</summary>
    [ObservableProperty]
    private string _leftCameraTitleText = "Left Camera";

    /// <summary>Локализованный заголовок секции правой камеры.</summary>
    [ObservableProperty]
    private string _rightCameraTitleText = "Right Camera";

    /// <summary>Локализованный текст кнопки «Тестовое подключение».</summary>
    [ObservableProperty]
    private string _testButtonText = "Test";

    /// <summary>Локализованная метка поля URL камеры.</summary>
    [ObservableProperty]
    private string _urlLabelText = "URL:";

    /// <summary>Локализованная метка поля логина.</summary>
    [ObservableProperty]
    private string _loginLabelText = "Login:";

    /// <summary>Локализованная метка поля пароля.</summary>
    [ObservableProperty]
    private string _passwordLabelText = "Password:";

    /// <summary>Локализованный текст чекбокса показа расширенных настроек камеры.</summary>
    [ObservableProperty]
    private string _showAdvancedSettingsText = "Show Advanced Settings";

    /// <summary>Локализованный заголовок секции расширенных настроек камеры.</summary>
    [ObservableProperty]
    private string _advancedCameraSettingsTitleText = "Advanced Camera Settings";

    /// <summary>Локализованная метка «Транспорт».</summary>
    [ObservableProperty]
    private string _transportLabelText = "Transport:";

    /// <summary>Локализованная метка «Бэкенд».</summary>
    [ObservableProperty]
    private string _backendLabelText = "Backend:";

    /// <summary>Локализованная метка «Аппаратное ускорение».</summary>
    [ObservableProperty]
    private string _hwAccelerationLabelText = "HW Acceleration:";

    /// <summary>Локализованная метка «Размер буфера».</summary>
    [ObservableProperty]
    private string _bufferSizeLabelText = "Buffer Size:";

    /// <summary>Локализованная метка «Таймаут отсутствия кадра».</summary>
    [ObservableProperty]
    private string _noFrameTimeoutLabelText = "No-Frame Timeout (ms):";

    /// <summary>Локализованная метка «Интервал детекции».</summary>
    [ObservableProperty]
    private string _detectionIntervalLabelText = "Detection Interval (ms):";

    /// <summary>Локализованная метка «Масштаб детекции».</summary>
    [ObservableProperty]
    private string _detectionScaleLabelText = "Detection Scale:";

    /// <summary>Локализованная метка «Начальная задержка переподключения».</summary>
    [ObservableProperty]
    private string _reconnectInitialDelayLabelText = "Reconnect Initial Delay (ms):";

    /// <summary>Локализованная метка «Макс. задержка переподключения».</summary>
    [ObservableProperty]
    private string _reconnectMaxDelayLabelText = "Reconnect Max Delay (ms):";

    /// <summary>Локализованная метка «Таймаут подключения».</summary>
    [ObservableProperty]
    private string _connectionTimeoutLabelText = "Connection Timeout (ms):";

    /// <summary>Локализованный заголовок секции калибровочной доски.</summary>
    [ObservableProperty]
    private string _calibrationBoardSettingsTitleText = "Calibration Board Settings";

    /// <summary>Локализованная метка «Тип доски».</summary>
    [ObservableProperty]
    private string _boardTypeLabelText = "Board Type:";

    /// <summary>Локализованная метка «Ширина паттерна».</summary>
    [ObservableProperty]
    private string _patternWidthLabelText = "Pattern Width:";

    /// <summary>Локализованная метка «Высота паттерна».</summary>
    [ObservableProperty]
    private string _patternHeightLabelText = "Pattern Height:";

    /// <summary>Локализованная метка «Размер квадрата».</summary>
    [ObservableProperty]
    private string _squareSizeLabelText = "Square Size (mm):";

    /// <summary>Локализованная метка «Требуемые кадры».</summary>
    [ObservableProperty]
    private string _requiredFramesLabelText = "Required Frames:";

    /// <summary>Локализованная метка «Допуск синхронизации».</summary>
    [ObservableProperty]
    private string _syncToleranceLabelText = "Sync Tolerance (ms):";

    /// <summary>Локализованный текст кнопки «Сгенерировать доску».</summary>
    [ObservableProperty]
    private string _generateBoardButtonText = "Generate ChArUco Board...";

    /// <summary>Локализованный текст кнопки «Сбросить по умолчанию».</summary>
    [ObservableProperty]
    private string _resetDefaultsButtonText = "Reset to Defaults";

    /// <summary>Локализованный текст кнопки «Сохранить».</summary>
    [ObservableProperty]
    private string _saveButtonText = "Save";

    /// <summary>Локализованный текст кнопки «Применить».</summary>
    [ObservableProperty]
    private string _applyButtonText = "Apply";

    /// <summary>Водяной знак для поля URL левой камеры.</summary>
    [ObservableProperty]
    private string _leftCameraUrlWatermark = "rtsp://192.168.1.100:554/stream";

    /// <summary>Водяной знак для поля URL правой камеры.</summary>
    [ObservableProperty]
    private string _rightCameraUrlWatermark = "rtsp://192.168.1.101:554/stream";

    // === Validation Properties ===

    /// <summary>Можно ли сохранить настройки (все поля валидны).</summary>
    [ObservableProperty]
    private bool _canSave = true;

    /// <summary>Ошибка валидации URL левой камеры.</summary>
    [ObservableProperty]
    private string? _leftUrlError;

    /// <summary>Ошибка валидации URL правой камеры.</summary>
    [ObservableProperty]
    private string? _rightUrlError;

    /// <summary>Ошибка валидации размера квадрата.</summary>
    [ObservableProperty]
    private string? _squareSizeError;

    /// <summary>Ошибка валидации ширины паттерна.</summary>
    [ObservableProperty]
    private string? _patternWidthError;

    /// <summary>Ошибка валидации высоты паттерна.</summary>
    [ObservableProperty]
    private string? _patternHeightError;

    /// <summary>Ошибка валидации допуска синхронизации.</summary>
    [ObservableProperty]
    private string? _syncToleranceError;

    [ObservableProperty]
    private string? _detectionIntervalError;

    [ObservableProperty]
    private string? _detectionScaleError;

    /// <summary>Конструктор по умолчанию (design-time).</summary>
    public SettingsViewModel() : this(new SettingsService())
    {
    }

    /// <summary>
    /// Инициализирует ViewModel с указанным сервисом настроек.
    /// </summary>
    /// <param name="settingsService">Сервис настроек приложения.</param>
    public SettingsViewModel(SettingsService settingsService)
    {
        _settingsService = settingsService;
        _settingsService.Settings.PropertyChanged += OnSettingsChanged;
        UpdateLocalizedUiTexts();
        ValidateSettings();
    }

    /// <summary>Сохраняет настройки и закрывает окно.</summary>
    [RelayCommand]
    private async Task SaveAsync()
    {
        await _settingsService.SaveAsync();
        StatusMessage = L("Settings saved", "Настройки сохранены");
        RequestClose?.Invoke();
    }

    /// <summary>Применяет настройки без закрытия окна.</summary>
    [RelayCommand]
    private async Task ApplyAsync()
    {
        await _settingsService.SaveAsync();
        StatusMessage = L("Settings applied", "Настройки применены");
    }

    /// <summary>Сбрасывает настройки к значениям по умолчанию.</summary>
    [RelayCommand]
    private void Reset()
    {
        _settingsService.Reset();
        OnPropertyChanged(nameof(Settings));
    }

    /// <summary>Тестирует подключение к левой камере.</summary>
    [RelayCommand]
    private async Task TestLeftCameraAsync()
    {
        await TestCameraAsync(
            Settings.LeftCameraUrl, 
            Settings.LeftCameraLogin, 
            Settings.LeftCameraPassword,
            L("Left camera", "Левая камера"));
    }

    /// <summary>Тестирует подключение к правой камере.</summary>
    [RelayCommand]
    private async Task TestRightCameraAsync()
    {
        await TestCameraAsync(
            Settings.RightCameraUrl, 
            Settings.RightCameraLogin, 
            Settings.RightCameraPassword,
            L("Right camera", "Правая камера"));
    }

    /// <summary>
    /// Тестирует подключение к камере с таймаутом 5 секунд.
    /// Использует TaskCompletionSource для гонки между событием подключения и таймаутом.
    /// </summary>
    /// <param name="url">URL камеры.</param>
    /// <param name="login">Логин.</param>
    /// <param name="password">Пароль.</param>
    /// <param name="cameraName">Локализованное название камеры для статуса.</param>
    private async Task TestCameraAsync(string url, string login, string password, string cameraName)
    {
        if (string.IsNullOrEmpty(url))
        {
            StatusMessage = $"{cameraName}: {L("camera URL is empty", "URL камеры не задан")}";
            return;
        }

        StatusMessage = $"{L("Testing", "Проверка")} {cameraName}...";

        var timeout = TimeSpan.FromSeconds(5);
        var cts = new CancellationTokenSource();
        // TaskCompletionSource для ожидания события подключения
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        EmguCameraSource? camera = null;
        AsyncFrameReader? reader = null;
        
        try
        {
            camera = new EmguCameraSource(
                sourceId: $"test-{cameraName.ToLower()}",
                url: url,
                login: login,
                password: password,
                backend: Settings.CameraBackend,
                hwAcceleration: Settings.HwAcceleration,
                transportProtocol: Settings.TransportProtocol,
                noFrameTimeoutMs: Settings.CameraNoFrameTimeoutMs,
                reconnectInitialDelayMs: Settings.CameraReconnectInitialDelayMs,
                reconnectMaxDelayMs: Settings.CameraReconnectMaxDelayMs,
                connectionTimeoutMs: Settings.CameraConnectionTimeoutMs);

            // Подписка на событие подключения камеры
            camera.OnConnectionChanged += (connected) => 
            { 
                if (connected) tcs.TrySetResult(true); 
            };

            reader = new AsyncFrameReader(camera);
            
            // Запуск камеры (может блокировать дольше токена отмены)
            var startTask = reader.StartAsync(cts.Token);
            
            // Наблюдаем за startTask, чтобы избежать unobserved exceptions
            _ = startTask.ContinueWith(t => 
            {
                if (t.IsFaulted) 
                {
                    System.Diagnostics.Debug.WriteLine($"Camera start faulted: {t.Exception.GetBaseException().Message}");
                }
            }, TaskScheduler.Default);
            
            // Гонка: событие подключения ИЛИ таймаут
            var timeoutTask = Task.Delay(timeout);
            await Task.WhenAny(tcs.Task, timeoutTask);
            
            // Проверяем, получено ли событие подключения (высший приоритет)
            if (tcs.Task.IsCompletedSuccessfully && tcs.Task.Result)
            {
                StatusMessage = $"{cameraName}: {L("Connection successful!", "Подключение успешно!")}";
            }
            else if (startTask.IsCompleted && startTask.IsFaulted)
            {
                // startTask завершился с ошибкой до таймаута/подключения
                StatusMessage = $"{cameraName}: {L("Failed", "Ошибка")} - {startTask.Exception?.GetBaseException().Message}";
            }
            else
            {
                // Таймаут — отменяем и кратко ждём очистки
                cts.Cancel();
                await Task.WhenAny(startTask, Task.Delay(1000));
                
                StatusMessage = $"{cameraName}: {L("Test timeout", "Таймаут проверки")}";
            }
        }
        catch (OperationCanceledException)
        {
            StatusMessage = $"{cameraName}: {L("Test timeout", "Таймаут проверки")}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"{cameraName}: {L("Failed", "Ошибка")} - {ex.Message}";
        }
        finally
        {
            if (reader != null)
            {
                try
                {
                    await reader.StopAsync();
                }
                catch
                {
                    // Игнорируем исключения при очистке
                }
                reader.Dispose();
            }
            
            camera?.Dispose();
            cts.Dispose();
        }
    }

    /// <summary>Открывает диалог выбора папки калибровок.</summary>
    [RelayCommand]
    private void BrowseCalibrationPath()
    {
        RequestFolderPicker?.Invoke();
    }

    /// <summary>Открывает диалог выбора папки сессий.</summary>
    [RelayCommand]
    private void BrowseCaptureSessionsPath()
    {
        RequestCaptureSessionsFolderPicker?.Invoke();
    }

    /// <summary>Инициирует переход к генератору калибровочной доски.</summary>
    [RelayCommand]
    private void OpenBoardGenerator()
    {
        RequestNavigateToBoardGenerator?.Invoke();
    }

    /// <summary>Обработчик изменения настроек; обновляет локализацию при смене языка и перепроверяет валидацию.</summary>
    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AppSettings.Language))
        {
            if (Dispatcher.UIThread.CheckAccess())
            {
                UpdateLocalizedUiTexts();
                ValidateSettings();
            }
            else
            {
                Dispatcher.UIThread.Post(() => { UpdateLocalizedUiTexts(); ValidateSettings(); });
            }
        }
        else
        {
            if (Dispatcher.UIThread.CheckAccess())
                ValidateSettings();
            else
                Dispatcher.UIThread.Post(ValidateSettings);
        }
    }

    /// <summary>
    /// Проверяет текущие значения настроек и обновляет ошибки валидации + CanSave.
    /// </summary>
    private void ValidateSettings()
    {
        // URL validation
        LeftUrlError = string.IsNullOrWhiteSpace(Settings.LeftCameraUrl)
            ? L("URL cannot be empty", "URL не может быть пустым")
            : !Uri.TryCreate(Settings.LeftCameraUrl, UriKind.Absolute, out _)
                ? L("Invalid URL format", "Неверный формат URL")
                : null;

        RightUrlError = string.IsNullOrWhiteSpace(Settings.RightCameraUrl)
            ? L("URL cannot be empty", "URL не может быть пустым")
            : !Uri.TryCreate(Settings.RightCameraUrl, UriKind.Absolute, out _)
                ? L("Invalid URL format", "Неверный формат URL")
                : null;

        // Numeric validation
        SquareSizeError = Settings.SquareSize <= 0
            ? L("Must be greater than 0", "Должно быть больше 0") : null;
        PatternWidthError = Settings.PatternWidth < 3
            ? L("Must be at least 3", "Должно быть не менее 3") : null;
        PatternHeightError = Settings.PatternHeight < 3
            ? L("Must be at least 3", "Должно быть не менее 3") : null;
        SyncToleranceError = Settings.SyncToleranceMs < 20
            ? L("Must be at least 20 ms", "Должно быть не менее 20 мс") : null;
        DetectionIntervalError = Settings.DetectionIntervalMs < 0
            ? L("Must be at least 0 ms", "Должно быть не менее 0 мс") : null;
        DetectionScaleError = Settings.DetectionScale is < 0.1f or > 1.0f
            ? L("Must be between 0.1 and 1.0", "Должно быть от 0.1 до 1.0") : null;

        CanSave = LeftUrlError == null && RightUrlError == null &&
                  SquareSizeError == null && PatternWidthError == null &&
                  PatternHeightError == null && SyncToleranceError == null &&
                  DetectionIntervalError == null && DetectionScaleError == null;
    }

    /// <summary>Обновляет все локализованные тексты UI.</summary>
    private void UpdateLocalizedUiTexts()
    {
        SettingsHeaderText = L("Settings", "Настройки");
        GeneralTabText = L("General", "Общие");
        CamerasTabText = L("Cameras", "Камеры");
        BoardTabText = L("Board", "Доска");

        ApplicationSettingsTitleText = L("Application Settings", "Настройки приложения");
        LanguageLabelText = L("Language:", "Язык:");
        AutoSaveLabelText = L("Auto-save:", "Автосохранение:");
        AutoSaveCheckText = L("Save calibration automatically", "Сохранять калибровку автоматически");
        DataPathLabelText = L("Data Path:", "Путь к калибровкам:");
        CaptureSessionsPathLabelText = L("Capture Sessions Path:", "Путь к сессиям снимков:");
        BrowseButtonText = L("Browse", "Обзор");

        LeftCameraTitleText = L("Left Camera", "Левая камера");
        RightCameraTitleText = L("Right Camera", "Правая камера");
        TestButtonText = L("Test", "Проверить");
        UrlLabelText = "URL:";
        LoginLabelText = L("Login:", "Логин:");
        PasswordLabelText = L("Password:", "Пароль:");
        ShowAdvancedSettingsText = L("Show Advanced Settings", "Показать расширенные настройки");

        AdvancedCameraSettingsTitleText = L("Advanced Camera Settings", "Расширенные настройки камеры");
        TransportLabelText = L("Transport:", "Транспорт:");
        BackendLabelText = L("Backend:", "Бэкенд:");
        HwAccelerationLabelText = L("HW Acceleration:", "HW-ускорение:");
        BufferSizeLabelText = L("Buffer Size:", "Размер буфера:");
        NoFrameTimeoutLabelText = L("No-Frame Timeout (ms):", "Таймаут без кадров (мс):");
        ReconnectInitialDelayLabelText = L("Reconnect Initial Delay (ms):", "Начальная задержка переподключения (мс):");
        ReconnectMaxDelayLabelText = L("Reconnect Max Delay (ms):", "Максимальная задержка переподключения (мс):");
        ConnectionTimeoutLabelText = L("Connection Timeout (ms):", "Таймаут подключения (мс):");

        CalibrationBoardSettingsTitleText = L("Calibration Board Settings", "Настройки калибровочной доски");
        BoardTypeLabelText = L("Board Type:", "Тип доски:");
        PatternWidthLabelText = L(
            "Pattern Width (inner corners for Chessboard):",
            "Ширина паттерна (внутр. углы для шахматной доски):");
        PatternHeightLabelText = L(
            "Pattern Height (inner corners for Chessboard):",
            "Высота паттерна (внутр. углы для шахматной доски):");
        SquareSizeLabelText = L("Square Size (mm):", "Размер квадрата (мм):");
        RequiredFramesLabelText = L("Required Frames:", "Требуемые кадры:");
        SyncToleranceLabelText = L("Sync Tolerance (ms):", "Допуск синхронизации (мс):");
        DetectionIntervalLabelText = L("Detection Interval (ms):", "Интервал детекции (мс):");
        DetectionScaleLabelText = L("Detection Scale:", "Масштаб детекции:");
        GenerateBoardButtonText = L("Generate ChArUco Board...", "Сгенерировать доску ChArUco...");

        ResetDefaultsButtonText = L("Reset to Defaults", "Сбросить по умолчанию");
        SaveButtonText = L("Save", "Сохранить");
        ApplyButtonText = L("Apply", "Применить");

        LeftCameraUrlWatermark = "rtsp://192.168.1.100:554/stream";
        RightCameraUrlWatermark = "rtsp://192.168.1.101:554/stream";
    }

    /// <summary>Освобождает ресурсы и отписывается от событий.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _settingsService.Settings.PropertyChanged -= OnSettingsChanged;
    }
}
