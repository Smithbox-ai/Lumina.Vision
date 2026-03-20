using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using Emgu.CV.Util;
using LuminaCalib.Calibration;
using LuminaCalib.Devices;
using LuminaCalib.Models;
using LuminaCalib.Services;
using PointF = System.Drawing.PointF;

namespace LuminaCalib.ViewModels;

/// <summary>
/// Корневой ViewModel главного окна приложения.
/// </summary>
/// <remarks>
/// Оркестрирует все подчинённые ViewModel (<see cref="SettingsVm"/>, <see cref="BoardGeneratorVm"/>,
/// <see cref="CalibrationManagerVm"/>). Управляет:
/// <list type="bullet">
///   <item>Жизненным циклом камер (подключение, отключение, переподключение)</item>
///   <item>Машиной состояний калибровочного процесса (Setup → Capture → Calibrate → Save)</item>
///   <item>Конвейером обработки кадров и синхронизацией стереопар</item>
///   <item>Автозахватом и ручным захватом кадров</item>
///   <item>Сессиями захвата и сохранением результатов калибровки</item>
///   <item>Локализацией интерфейса (ru/en) с защитой от моджибаке</item>
///   <item>Ректификацией предпросмотра на основе активной калибровки</item>
/// </list>
/// </remarks>
public partial class MainWindowViewModel : ViewModelBase
{
    /// <summary>
    /// Статический словарь русской локализации.
    /// Ключ — английский текст, значение — русский перевод.
    /// Используется методом <see cref="L(string, string)"/> для защиты от моджибаке.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> RuLocalization =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Active calibration file is missing"] = "Файл активной калибровки отсутствует",
            ["Active session"] = "Активная сессия",
            ["Active session is invalid"] = "Активная сессия повреждена",
            ["Active session mode is {0}, expected {1}."] = "Активная сессия имеет режим {0}, ожидается {1}.",
            ["Active session: not selected"] = "Активная сессия: не выбрана",
            ["Active XML"] = "Активный XML",
            ["Active XML: not selected"] = "Активный XML: не выбран",
            ["Auto"] = "Auto",
            ["Auto (Full Calibration)"] = "Auto (полная калибровка)",
            ["Board Generator"] = "Генератор доски",
            ["Calibrate"] = "Калибровать",
            ["Calibrated ({0:F2} px)"] = "Откалибровано ({0:F2} px)",
            ["Calibration"] = "Калибровка",
            ["Calibration failed"] = "Ошибка калибровки",
            ["Calibration Manager"] = "Менеджер калибровок",
            ["Calibration returned no result."] = "Калибровка не вернула результат.",
            ["Auto-save is disabled"] = "Автосохранение отключено",
            ["Capture"] = "Захват",
            ["Capture Frame"] = "Захват кадра",
            ["Capture session"] = "Сессия захвата",
            ["Capture session: not started."] = "Сессия захвата: не начата.",
            ["Capture sessions folder"] = "Папка сессий снимков",
            ["Captured pair {0}/{1}"] = "Захвачена пара {0}/{1}",
            ["Captured!"] = "Захвачено!",
            ["Captured:"] = "Захвачено:",
            ["Captures"] = "Кадры",
            ["Close"] = "Закрыть",
            ["Computing calibration"] = "Выполняется калибровка",
            ["Connect Cameras"] = "Подключить камеры",
            ["Connected"] = "Подключено",
            ["Partially connected"] = "Подключено частично",
            ["Cancel Connection"] = "Прервать подключение",
            ["Connecting..."] = "Подключение...",
            ["Disconnect"] = "Отключить",
            ["Disconnected"] = "Отключено",
            ["Current pattern"] = "Текущий паттерн",
            ["Chessboard {0}x{1} inner corners"] = "Chessboard {0}x{1} внутренних углов",
            ["ChArUco {0}x{1} cells ({2})"] = "ChArUco {0}x{1} клеток ({2})",
            ["Depth calibration"] = "Калибровка глубины",
            ["Depth map camera status"] = "Статус камер карты глубины",
            ["Depth map prerequisites are not met."] = "Не выполнены условия для запуска карты глубины.",
            ["Depth map requires an active calibration file."] = "Для карты глубины нужна активная калибровка.",
            ["Depth map requires connected cameras."] = "Для карты глубины нужно подключить камеры.",
            ["Depth map uses the same camera stream as Calibration."] = "Карта глубины использует тот же поток камер, что и «Калибровка».",
            ["Error"] = "Ошибка",
            ["Failed to open sessions folder"] = "Не удалось открыть папку сессий",
            ["fps"] = "кадр/с",
            ["from active session"] = "по активной сессии",
            ["History"] = "История",
            ["Hold Still..."] = "Не двигайте камеру...",
            ["Left"] = "Левая",
            ["LEFT CAMERA"] = "ЛЕВАЯ КАМЕРА",
            ["Log"] = "Лог",
            ["miss"] = "нет",
            ["Mode:"] = "Режим:",
            ["No active calibration selected."] = "Активная калибровка не выбрана.",
            ["No Signal"] = "Нет сигнала",
            ["Not Calibrated"] = "Не откалибровано",
            ["ok"] = "ok",
            ["Open Sessions Folder"] = "Открыть папку сессий",
            ["Open Calibration Manager"] = "Открыть менеджер калибровок",
            ["Right"] = "Правая",
            ["RIGHT CAMERA"] = "ПРАВАЯ КАМЕРА",
            ["Save"] = "Сохранение",
            ["Saved"] = "Сохранено",
            ["Clear Log"] = "Очистить лог",
            ["Session calibration failed"] = "Ошибка калибровки по сессии",
            ["Session image is empty or unreadable"] = "Изображение сессии пустое или не читается",
            ["Session image not found"] = "Изображение сессии не найдено",
            ["Session save warning"] = "Предупреждение сохранения сессии",
            ["Settings"] = "Настройки",
            ["Setup"] = "Настройка",
            ["Show Rectified"] = "Показывать ректификацию",
            ["Single Camera"] = "Одна камера",
            ["Start Detection"] = "Начать поиск",
            ["Stop Detection"] = "Остановить поиск",
            ["Continue Detection"] = "Продолжить поиск",
            ["Detection completed"] = "Поиск завершен",
            ["Stereo Only"] = "Только Stereo",
            ["Use Saved Session For Calibration"] = "Использовать сохраненную сессию для калибровки",
            ["Calibrating..."] = "Калибровка...",
            ["Recalibrate"] = "Перекалибровать",
            ["Camera controls are disabled while session source is selected."] = "Управление камерами отключено, пока выбран источник \"сессия\".",
            ["Camera URLs are not configured. Check Settings."] = "URL камер не настроены. Проверьте настройки.",
            ["Cameras connected. Start capturing."] = "Камеры подключены. Начните захват.",
            ["Manual capture fallback used: Δ={0:F1} ms (strict tolerance {1:F1} ms)."] = "Ручной захват выполнен fallback-парой: Δ={0:F1} мс (строгий допуск {1:F1} мс).",
            ["Camera connection was cancelled."] = "Подключение к камерам прервано.",
            ["Camera connection cancelled by user."] = "Подключение к камерам отменено пользователем.",
            ["Connection timeout ({0}ms). Check camera URLs and network."] = "Таймаут подключения ({0}мс). Проверьте URL камер и сеть.",
            ["Capture is unavailable in current source mode."] = "Захват недоступен в текущем режиме источника.",
            ["Calibration engine is not initialized."] = "Калибровочный модуль не инициализирован.",
            ["No synchronized frame pair available."] = "Нет синхронизированной пары кадров.",
            ["No synchronized frame pair available. Nearest delta is {0:F1} ms, limit is {1:F1} ms."] = "Нет синхронной пары кадров. Ближайшая дельта {0:F1} мс, лимит {1:F1} мс.",
            ["Active session (incompatible)"] = "Активная сессия (несовместима)",
            ["Session is incompatible with current board settings."] = "Сессия несовместима с текущими настройками доски.",
            ["Pattern not detected in frames. Try again."] = "Паттерн не найден в кадрах. Повторите попытку.",
            ["Captured set {0}/{1} (L:{2}, R:{3})"] = "Захвачен набор {0}/{1} (Л:{2}, П:{3})",
            ["Pattern not detected in both frames. Try again."] = "Паттерн не найден в обоих кадрах. Повторите попытку.",
            ["At least 10 captures are required before calibration."] = "Перед калибровкой требуется минимум 10 захватов.",
            ["No active capture session selected for current mode."] = "Для текущего режима не выбрана активная сессия.",
            ["Only {0} valid frame set(s) were accepted from session. At least 10 are required."] = "Из сессии принято только {0} валидных наборов кадров. Требуется минимум 10.",
            ["Stereo Only mode requires an existing calibration file."] = "Для режима Stereo Only требуется существующий файл калибровки.",
            ["Calibration pattern changed. Captured frames were reset."] = "Паттерн калибровки изменён. Захваченные кадры сброшены.",
            ["Auto-captured pair {0}/{1}"] = "Автозахват пары {0}/{1}",
            ["Auto-capture complete. Ready to calibrate."] = "Автозахват завершён. Готово к калибровке.",
            ["Move the board to a new position"] = "Переместите доску в новое положение",
            ["Reconnect in {0} ms"] = "Переподключение через {0} мс",
            ["Ready. Connect cameras to begin."] = "Готово. Подключите камеры для начала.",
            ["Depth Map"] = "Карта глубины",
            ["missing"] = "отсутствует",
            ["not selected"] = "не выбрано",
            ["Camera stream lost. Auto-capture paused."] = "Поток камеры потерян. Автозахват приостановлен.",
            ["Camera stream restored. Auto-capture resumed."] = "Поток камеры восстановлен. Автозахват возобновлён.",
            ["Cameras connected. Press 'Start Detection' to begin."] = "Камеры подключены. Нажмите 'Начать поиск' для начала.",
            ["Detection stopped. Preview only."] = "Детекция остановлена. Режим просмотра.",
            ["About"] = "О программе",
            ["No log entries"] = "Нет записей",
            ["Computing calibration..."] = "Выполняется калибровка...",
            ["Capture is unavailable in current source mode."] = "Захват недоступен в текущем режиме источника."
        };
    /// <summary>Сервис настроек приложения.</summary>
    private readonly SettingsService _settingsService;

    /// <summary>Синхронизатор стереопар кадров левой и правой камер.</summary>
    private readonly StereoSynchronizer _synchronizer;

    /// <summary>Сервис управления сессиями захвата (сохранение кадров на диск).</summary>
    private readonly CaptureSessionService _captureSessionService;

    /// <summary>Ректификатор для предпросмотра скорректированных кадров.</summary>
    private readonly StereoRectifier _previewRectifier = new();

    /// <summary>Сервис построения карты глубины в реальном времени.</summary>
    private readonly DepthMapService _depthMapService;

    /// <summary>Блокировка доступа к ректификатору из разных потоков.</summary>
    private readonly object _rectificationLock = new();

    /// <summary>Текущие применённые настройки.</summary>
    private AppSettings? _activeSettings;

    /// <summary>Флаг защиты от рекурсивного обновления режимов калибровки.</summary>
    private bool _isUpdatingModeSelection;

    /// <summary>Флаг защиты от рекурсивного обновления навигационных свойств.</summary>
    private bool _isUpdatingNavigation;

    /// <summary>Флаг, указывающий что настройки применяются программно (не пользователем).</summary>
    private bool _isApplyingSettings;

    /// <summary>Флаг защиты от рекурсивного изменения состояния карты глубины.</summary>
    private bool _isUpdatingDepthMapState;

    /// <summary>Детектор углов калибровочной доски.</summary>
    private CornerDetector? _cornerDetector;

    /// <summary>Второй детектор углов для параллельной детекции правого кадра.</summary>
    private CornerDetector? _cornerDetectorRight;

    /// <summary>Калибровочный движок (OpenCV).</summary>
    private CalibrationEngine? _calibrationEngine;

    /// <summary>Сервис автоматического захвата кадров.</summary>
    private AutoCaptureService? _autoCaptureService;

    /// <summary>Пул Mat-объектов для повторного использования памяти камер.</summary>
    private MatPool? _matPool;

    /// <summary>Источник левой камеры.</summary>
    private EmguCameraSource? _leftCamera;

    /// <summary>Источник правой камеры.</summary>
    private EmguCameraSource? _rightCamera;

    /// <summary>Асинхронный читатель кадров левой камеры.</summary>
    private AsyncFrameReader? _leftReader;

    /// <summary>Асинхронный читатель кадров правой камеры.</summary>
    private AsyncFrameReader? _rightReader;

    /// <summary>Токен отмены для остановки захвата камер.</summary>
    private CancellationTokenSource? _captureCts;

    /// <summary>Временная метка последнего обновления FPS левой камеры в UI.</summary>
    private long _lastLeftFpsUiUpdateTicks;

    /// <summary>Временная метка последнего обновления FPS правой камеры в UI.</summary>
    private long _lastRightFpsUiUpdateTicks;

    /// <summary>Ширина последней полученной стереопары (volatile).</summary>
    private int _lastStereoPairWidth;

    /// <summary>Высота последней полученной стереопары (volatile).</summary>
    private int _lastStereoPairHeight;

    /// <summary>Интервал обновления FPS в UI (250 мс).</summary>
    private const long FpsUiUpdateIntervalTicks = TimeSpan.TicksPerMillisecond * 250;

    /// <summary>Максимально допустимая дельта fallback-пары для ручного захвата (мс).</summary>
    private const double ManualCaptureFallbackMaxDeltaMs = 120.0;

    /// <summary>Список захваченных стереопар.</summary>
    private readonly List<StereoFramePair> _capturedPairsList = [];

    /// <summary>Текущий результат калибровки.</summary>
    private CalibrationResult? _currentCalibration;

    /// <summary>Калибровка для предпросмотра ректификации.</summary>
    private CalibrationResult? _previewCalibration;

    /// <summary>Путь к файлу калибровки предпросмотра (для избежания повторной загрузки).</summary>
    private string? _previewCalibrationPath;

    /// <summary>Текущая активная сессия захвата.</summary>
    private CaptureSessionManifest? _activeCaptureSession;

    /// <summary>Запланированное время переподключения левой камеры (UTC).</summary>
    private DateTime? _leftReconnectAttemptUtc;

    /// <summary>Запланированное время переподключения правой камеры (UTC).</summary>
    private DateTime? _rightReconnectAttemptUtc;

    /// <summary>Таймер обновления индикаторов обратного отсчёта переподключения.</summary>
    private readonly DispatcherTimer _reconnectStatusTimer = new() { Interval = TimeSpan.FromMilliseconds(200) };

    // === Навигация ===

    /// <summary>Флаг: активно представление калибровки.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBoardGeneratorView))]
    [NotifyPropertyChangedFor(nameof(IsHistoryView))]
    [NotifyPropertyChangedFor(nameof(IsLogView))]
    private bool _isCalibrationView = true;

    /// <summary>Локализованный текст вкладки «Калибровка».</summary>
    [ObservableProperty]
    private string _calibrationTabText = "Calibration";

    /// <summary>Флаг: активно представление карты глубины.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBoardGeneratorView))]
    private bool _isDepthMapView;

    /// <summary>Локализованный текст вкладки «Карта глубины».</summary>
    [ObservableProperty]
    private string _depthMapTabText = "Depth Map";

    /// <summary>Флаг: активно представление истории.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBoardGeneratorView))]
    [NotifyPropertyChangedFor(nameof(IsLogView))]
    private bool _isHistoryView;

    /// <summary>Локализованный текст вкладки «Менеджер калибровок».</summary>
    [ObservableProperty]
    private string _managerTabText = "Calibration Manager";

    /// <summary>Флаг: активно представление генератора доски (вычисляемое свойство).</summary>
    public bool IsBoardGeneratorView
    {
        get => !IsCalibrationView && !IsHistoryView && !IsLogView && !IsDepthMapView;
        set
        {
            if (value)
            {
                IsCalibrationView = false;
                IsHistoryView = false;
                IsLogView = false;
                IsDepthMapView = false;
                OnPropertyChanged(nameof(IsBoardGeneratorView));
            }
        }
    }

    /// <summary>Локализованный текст вкладки «Генератор доски».</summary>
    [ObservableProperty]
    private string _boardGeneratorTabText = "Board Generator";

    /// <summary>Флаг: активно представление лога.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBoardGeneratorView))]
    private bool _isLogView;

    /// <summary>Локализованный текст вкладки «Лог».</summary>
    [ObservableProperty]
    private string _logTabText = "Log";

    /// <summary>Флаг: открыто окно настроек.</summary>
    [ObservableProperty]
    private bool _isSettingsOpen;

    // === Режим калибровки ===

    /// <summary>Флаг: выбран режим Auto (полная калибровка).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStereoOnlyMode))]
    [NotifyPropertyChangedFor(nameof(IsSingleCameraMode))]
    private bool _isAutoMode = true;

    public bool IsStereoOnlyMode
    {
        get => !IsAutoMode && !IsSingleCameraMode;
        set
        {
            if (!value || IsStereoOnlyMode == value)
            {
                return;
            }

            _isUpdatingModeSelection = true;
            try
            {
                IsAutoMode = false;
                IsSingleCameraMode = false;
                OnPropertyChanged(nameof(IsStereoOnlyMode));
            }
            finally
            {
                _isUpdatingModeSelection = false;
            }

            UpdateCalibrationModeSetting(CalibrationMode.StereoOnly);
        }
    }

    /// <summary>Флаг: выбран режим SingleCamera.</summary>
    [ObservableProperty]
    private bool _isSingleCameraMode;

    // === Рабочий процесс ===

    /// <summary>Текущий шаг рабочего процесса (0=Setup, 1=Capture, 2=Calibrate, 3=Save).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanShowCaptureButton))]
    [NotifyPropertyChangedFor(nameof(CanShowDetectionButton))]
    [NotifyPropertyChangedFor(nameof(CanToggleDetection))]
    [NotifyPropertyChangedFor(nameof(DetectionToggleText))]
    [NotifyPropertyChangedFor(nameof(CanChangeModeSelection))]
    private int _currentStep;

    /// <summary>Флаг: режим детекции паттерна активен.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanShowCaptureButton))]
    [NotifyPropertyChangedFor(nameof(CanToggleDetection))]
    private bool _isDetectionActive;

    /// <summary>Флаг: активен автозахват.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCalibrate))]
    [NotifyPropertyChangedFor(nameof(CanShowCaptureButton))]
    [NotifyPropertyChangedFor(nameof(CanToggleDetection))]
    [NotifyPropertyChangedFor(nameof(DetectionToggleText))]
    [NotifyPropertyChangedFor(nameof(CanChangeModeSelection))]
    private bool _autoCapturing;

    /// <summary>Паттерн обнаружен в левом кадре.</summary>
    [ObservableProperty]
    private bool _patternDetectedLeft;

    /// <summary>Паттерн обнаружен в правом кадре.</summary>
    [ObservableProperty]
    private bool _patternDetectedRight;

    /// <summary>Количество захваченных пар кадров.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCalibrate))]
    [NotifyPropertyChangedFor(nameof(CanToggleDetection))]
    [NotifyPropertyChangedFor(nameof(DetectionToggleText))]
    private int _capturedPairsCount;

    /// <summary>Требуемое количество пар для калибровки.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanToggleDetection))]
    [NotifyPropertyChangedFor(nameof(DetectionToggleText))]
    private int _requiredPairs = 20;

    /// <summary>Флаг: отображать ректифицированные кадры.</summary>
    [ObservableProperty]
    private bool _showRectified;

    /// <summary>Флаг: использовать сохранённую сессию вместо камер.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCapture))]
    [NotifyPropertyChangedFor(nameof(CanCalibrate))]
    [NotifyPropertyChangedFor(nameof(CanUseCameraControls))]
    [NotifyPropertyChangedFor(nameof(CanShowCaptureButton))]
    [NotifyPropertyChangedFor(nameof(CanShowDetectionButton))]
    [NotifyPropertyChangedFor(nameof(CanToggleDetection))]
    private bool _useSavedSessionForCalibration;

    /// <summary>Количество захватов в активной сессии текущего режима.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCalibrate))]
    private int _currentModeActiveSessionCaptureCount;

    /// <summary>Признак завершённости активной сессии текущего режима.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCalibrate))]
    private bool _currentModeActiveSessionIsCompleted;

    /// <summary>Текст активной калибровки текущего режима.</summary>
    [ObservableProperty]
    private string _currentModeActiveCalibrationText = string.Empty;

    /// <summary>Текст активной сессии текущего режима.</summary>
    [ObservableProperty]
    private string _currentModeActiveSessionText = string.Empty;

    /// <summary>Текстовое описание текущего типа доски и размеров паттерна.</summary>
    [ObservableProperty]
    private string _currentPatternDescriptorText = string.Empty;

    [ObservableProperty]
    private string _useSavedSessionForCalibrationText = "Use Saved Session For Calibration";

    [ObservableProperty]
    private string _openSessionsFolderText = "Open Sessions Folder";

    [ObservableProperty]
    private string _settingsTooltipText = "Settings";

    /// <summary>Локализованный текст подсказки кнопки «О программе».</summary>
    [ObservableProperty]
    private string _aboutTooltipText = "About";

    [ObservableProperty]
    private string _closeTooltipText = "Close";

    [ObservableProperty]
    private string _settingsHeaderText = "Settings";

    [ObservableProperty]
    private string _modeLabelText = "Mode:";

    [ObservableProperty]
    private string _autoModeText = "Auto (Full Calibration)";

    [ObservableProperty]
    private string _stereoOnlyModeText = "Stereo Only";

    [ObservableProperty]
    private string _singleCameraModeText = "Single Camera";

    [ObservableProperty]
    private string _leftCameraOverlayText = "LEFT CAMERA";

    [ObservableProperty]
    private string _rightCameraOverlayText = "RIGHT CAMERA";

    [ObservableProperty]
    private string _capturedLabelText = "Captured:";

    [ObservableProperty]
    private string _showRectifiedText = "Show Rectified";

    [ObservableProperty]
    private string _connectCamerasText = "Connect Cameras";

    [ObservableProperty]
    private string _disconnectText = "Disconnect";

    [ObservableProperty]
    private string _cancelConnectionText = "Cancel Connection";

    /// <summary>Локализованный заголовок статуса камер в панели карты глубины.</summary>
    [ObservableProperty]
    private string _depthMapCameraStatusLabelText = "Depth map camera status";

    /// <summary>Локализованный текст статуса: камеры подключены.</summary>
    [ObservableProperty]
    private string _depthMapCameraConnectedText = "Connected";

    /// <summary>Локализованный текст статуса: подключена только одна камера.</summary>
    [ObservableProperty]
    private string _depthMapCameraPartiallyConnectedText = "Partially connected";

    /// <summary>Локализованный текст статуса: камеры отключены.</summary>
    [ObservableProperty]
    private string _depthMapCameraDisconnectedText = "Disconnected";

    /// <summary>Локализованный текст статуса: идёт подключение камер.</summary>
    [ObservableProperty]
    private string _depthMapCameraConnectingText = "Connecting...";

    /// <summary>Локализованный заголовок активной калибровки карты глубины.</summary>
    [ObservableProperty]
    private string _depthMapCalibrationLabelText = "Depth calibration";

    /// <summary>Текст с текущей активной калибровкой для depth map.</summary>
    [ObservableProperty]
    private string _depthMapActiveCalibrationText = string.Empty;

    /// <summary>Подсказка о том, что вкладки используют общий поток камер.</summary>
    [ObservableProperty]
    private string _depthMapSharedCameraHintText = "Depth map uses the same camera stream as Calibration.";

    /// <summary>Локализованный текст кнопки перехода в менеджер калибровок.</summary>
    [ObservableProperty]
    private string _depthMapOpenCalibrationManagerText = "Open Calibration Manager";

    [ObservableProperty]
    private string _captureFrameText = "Capture Frame";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CalibrateActionText))]
    private string _calibrateButtonText = "Calibrate";

    [ObservableProperty]
    private string _leftStatusLabelText = "Left";

    [ObservableProperty]
    private string _rightStatusLabelText = "Right";

    [ObservableProperty]
    private string _statusDisconnectedText = "Disconnected";

    [ObservableProperty]
    private string _statusFpsUnitText = "fps";

    [ObservableProperty]
    private string _videoNoSignalText = "No Signal";

    [ObservableProperty]
    private string _videoConnectingText = "Connecting...";

    [ObservableProperty]
    private string _videoErrorText = "Error";

    [ObservableProperty]
    private string _videoHoldStillText = "Hold Still...";

    [ObservableProperty]
    private string _videoCapturedText = "Captured!";

    [ObservableProperty]
    private string _workflowSetupText = "Setup";

    [ObservableProperty]
    private string _workflowCaptureText = "Capture";

    [ObservableProperty]
    private string _workflowCalibrateText = "Calibrate";

    [ObservableProperty]
    private string _workflowSaveText = "Save";

    [ObservableProperty]
    private string _logHeaderText = "Log";

    [ObservableProperty]
    private string _clearLogText = "Clear Log";

    /// <summary>Флаг: есть записи в логе.</summary>
    [ObservableProperty]
    private bool _hasLogEntries;

    /// <summary>Локализованный текст пустого лога.</summary>
    [ObservableProperty]
    private string _logEmptyText = "No log entries";

    /// <summary>Коллекция строк лога для привязки к UI.</summary>
    public ObservableCollection<string> LogEntries { get; } = [];

    /// <summary>Максимальное количество записей в логе.</summary>
    private const int MaxLogEntries = 300;

    // === Статус камер ===

    /// <summary>Флаг: левая камера подключена.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AreCamerasConnected))]
    [NotifyPropertyChangedFor(nameof(CanCapture))]
    [NotifyPropertyChangedFor(nameof(CanShowCaptureButton))]
    [NotifyPropertyChangedFor(nameof(CanShowDetectionButton))]
    [NotifyPropertyChangedFor(nameof(CanToggleDetection))]
    [NotifyPropertyChangedFor(nameof(CanChangeModeSelection))]
    [NotifyPropertyChangedFor(nameof(HasAnyCameraConnected))]
    [NotifyPropertyChangedFor(nameof(CanShowConnectButton))]
    [NotifyPropertyChangedFor(nameof(CanShowDisconnectButton))]
    [NotifyPropertyChangedFor(nameof(DepthMapCameraStatusText))]
    private bool _leftCameraConnected;

    /// <summary>Флаг: правая камера подключена.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AreCamerasConnected))]
    [NotifyPropertyChangedFor(nameof(CanCapture))]
    [NotifyPropertyChangedFor(nameof(CanShowCaptureButton))]
    [NotifyPropertyChangedFor(nameof(CanShowDetectionButton))]
    [NotifyPropertyChangedFor(nameof(CanToggleDetection))]
    [NotifyPropertyChangedFor(nameof(CanChangeModeSelection))]
    [NotifyPropertyChangedFor(nameof(HasAnyCameraConnected))]
    [NotifyPropertyChangedFor(nameof(CanShowConnectButton))]
    [NotifyPropertyChangedFor(nameof(CanShowDisconnectButton))]
    [NotifyPropertyChangedFor(nameof(DepthMapCameraStatusText))]
    private bool _rightCameraConnected;

    /// <summary>Флаг: идёт процесс подключения к камерам.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanShowConnectButton))]
    [NotifyPropertyChangedFor(nameof(CanShowDisconnectButton))]
    [NotifyPropertyChangedFor(nameof(DepthMapCameraStatusText))]
    private bool _isConnectingCameras;

    /// <summary>FPS левой камеры.</summary>
    [ObservableProperty]
    private double _leftCameraFps;

    /// <summary>FPS правой камеры.</summary>
    [ObservableProperty]
    private double _rightCameraFps;

    /// <summary>Дополнительная информация левой камеры (напр., обратный отсчёт переподключения).</summary>
    [ObservableProperty]
    private string _leftCameraExtraInfoText = string.Empty;

    /// <summary>Дополнительная информация правой камеры.</summary>
    [ObservableProperty]
    private string _rightCameraExtraInfoText = string.Empty;

    /// <summary>Флаг: идёт процесс калибровки (вычисление).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCalibrate))]
    [NotifyPropertyChangedFor(nameof(CalibrateActionText))]
    private bool _isCalibrating;

    /// <summary>Текст индикатора процесса калибровки.</summary>
    [ObservableProperty]
    private string _calibratingText = "Computing calibration...";

    /// <summary>Обе камеры подключены.</summary>
    public bool AreCamerasConnected => LeftCameraConnected && RightCameraConnected;

    /// <summary>Подключена хотя бы одна камера.</summary>
    public bool HasAnyCameraConnected => LeftCameraConnected || RightCameraConnected;

    /// <summary>Доступно ли управление камерами (недоступно в режиме сессии).</summary>
    public bool CanUseCameraControls => !UseSavedSessionForCalibration;

    /// <summary>Можно ли показать кнопку «Подключить камеры» (не в процессе подключения и обе камеры отключены).</summary>
    public bool CanShowConnectButton => !IsConnectingCameras && !HasAnyCameraConnected;

    /// <summary>Можно ли показать кнопку «Отключить» (подключена хотя бы одна камера и не идёт подключение).</summary>
    public bool CanShowDisconnectButton => HasAnyCameraConnected && !IsConnectingCameras;

    /// <summary>Статус подключения камер для панели карты глубины.</summary>
    public string DepthMapCameraStatusText => IsConnectingCameras
        ? DepthMapCameraConnectingText
        : AreCamerasConnected
            ? DepthMapCameraConnectedText
            : HasAnyCameraConnected
                ? DepthMapCameraPartiallyConnectedText
                : DepthMapCameraDisconnectedText;

    /// <summary>Можно ли захватывать кадры.</summary>
    public bool CanCapture => AreCamerasConnected && CanUseCameraControls;

    /// <summary>Можно ли запустить калибровку (требуется ≥ 10 захватов, сессия должна быть завершена).</summary>
    public bool CanCalibrate => !IsCalibrating && !AutoCapturing && (UseSavedSessionForCalibration
        ? CurrentModeActiveSessionIsCompleted && CurrentModeActiveSessionCaptureCount >= 10
        : CapturedPairsCount >= 10);

    /// <summary>Есть ли результат калибровки.</summary>
    public bool HasCalibration => _currentCalibration != null;

    /// <summary>Можно ли показать кнопку управления поиском паттерна.</summary>
    public bool CanShowDetectionButton => AreCamerasConnected && CurrentStep == 1 && CanUseCameraControls;

    /// <summary>Можно ли переключать поиск паттерна.</summary>
    public bool CanToggleDetection => CanShowDetectionButton
        && CapturedPairsCount < RequiredPairs
        && _autoCaptureService != null;

    /// <summary>Текст кнопки поиска паттерна в зависимости от состояния этапа захвата.</summary>
    public string DetectionToggleText => AutoCapturing
        ? L("Stop Detection", "Остановить поиск")
        : CapturedPairsCount >= RequiredPairs
            ? L("Detection completed", "Поиск завершен")
            : CapturedPairsCount > 0
                ? L("Continue Detection", "Продолжить поиск")
                : L("Start Detection", "Начать поиск");

    /// <summary>Текст кнопки калибровки в зависимости от текущего состояния процесса.</summary>
    public string CalibrateActionText => IsCalibrating
        ? L("Calibrating...", "Калибровка...")
        : HasCalibration
            ? L("Recalibrate", "Перекалибровать")
            : CalibrateButtonText;

    /// <summary>
    /// Управляет видимостью кнопки ручного захвата.
    /// Скрыта когда: камеры не подключены, автозахват активен или активен не шаг «Захват».
    /// </summary>
    public bool CanShowCaptureButton => AreCamerasConnected && !AutoCapturing && CurrentStep == 1;

    /// <summary>
    /// Управляет доступностью переключения режимов калибровки.
    /// Отключено при подключённых камерах и идущем процессе.
    /// </summary>
    public bool CanChangeModeSelection => !AreCamerasConnected || CurrentStep == 0;

    // === Настройки ===

    /// <summary>ViewModel настроек приложения.</summary>
    public SettingsViewModel SettingsVm { get; }

    // === Генератор доски ===

    /// <summary>ViewModel генератора калибровочных досок.</summary>
    public BoardGeneratorViewModel BoardGeneratorVm { get; }

    // === Менеджер калибровок ===

    /// <summary>ViewModel менеджера калибровок.</summary>
    public CalibrationManagerViewModel CalibrationManagerVm { get; }

    // === Карта глубины ===

    /// <summary>ViewModel карты глубины.</summary>
    public DepthMapViewModel DepthMapVm { get; }

    // === Уведомления ===

    /// <summary>Коллекция toast-уведомлений для отображения в UI.</summary>
    public ObservableCollection<NotificationItem> Notifications => NotificationService.Instance.Notifications;

    // === Статус калибровки ===

    /// <summary>Текст статуса калибровки.</summary>
    [ObservableProperty]
    private string _calibrationStatusText = "Not Calibrated";

    /// <summary>Цвет индикатора статуса калибровки (красный/жёлтый/зелёный).</summary>
    [ObservableProperty]
    private IBrush _calibrationStatusColor = new SolidColorBrush(Color.FromRgb(255, 82, 82));

    /// <summary>Информация о хранилище сессий захвата.</summary>
    [ObservableProperty]
    private string _captureStorageInfoText = string.Empty;

    /// <summary>Информация о текущей сессии захвата.</summary>
    [ObservableProperty]
    private string _captureSessionInfoText = "Capture session: not started.";

    /// <summary>Конструктор по умолчанию (design-time).</summary>
    public MainWindowViewModel() : this(new SettingsService(), new StereoSynchronizer(), null, null)
    {
    }

    /// <summary>
    /// Основной конструктор. Создаёт все подчинённые ViewModel и настраивает подписки.
    /// </summary>
    /// <param name="settingsService">Сервис настроек.</param>
    /// <param name="synchronizer">Синхронизатор стереопар.</param>
    /// <param name="captureSessionService">Сервис сессий захвата (опционально).</param>
    public MainWindowViewModel(
        SettingsService settingsService,
        StereoSynchronizer synchronizer,
        CaptureSessionService? captureSessionService = null,
        DepthMapService? depthMapService = null)
    {
        _settingsService = settingsService;
        _synchronizer = synchronizer;
        _captureSessionService = captureSessionService ?? new CaptureSessionService();

        _depthMapService = depthMapService ?? new DepthMapService(new StereoRectifier(), settingsService.Settings.DepthMapSettings);
        DepthMapVm = new DepthMapViewModel(_depthMapService, settingsService.Settings.DepthMapSettings, settingsService);
        DepthMapVm.PropertyChanged += OnDepthMapViewModelPropertyChanged;

        SettingsVm = new SettingsViewModel(_settingsService);
        BoardGeneratorVm = new BoardGeneratorViewModel(_settingsService);
        CalibrationManagerVm = new CalibrationManagerViewModel(_settingsService);

        _synchronizer.OnStereoFrameReady += OnStereoFrameReady;
        SettingsVm.PropertyChanged += OnSettingsViewModelPropertyChanged;
        CalibrationManagerVm.CalibrationFilesChanged += OnCalibrationFilesChanged;
        ExceptionLogger.OnLogLine += OnExceptionLogLine;
        _reconnectStatusTimer.Tick += OnReconnectStatusTimerTick;

        AttachSettings(_settingsService.Settings);
    }

    // === Обработчики событий кадров ===

    /// <summary>Событие получения кадра левой камеры.</summary>
    public event Action<FrameRaw>? OnLeftFrameReceived;

    /// <summary>Событие получения кадра правой камеры.</summary>
    public event Action<FrameRaw>? OnRightFrameReceived;

    /// <summary>Событие обновления оверлея детекции паттерна.</summary>
    public event Action<bool, bool, PointF[]?, PointF[]?, BoardType>? OnDetectionOverlayUpdate;

    /// <summary>
    /// Обработчик синхронизированной стереопары. Передаёт кадры в автозахват и в UI.
    /// </summary>
    private void OnStereoFrameReady(StereoFramePair pair)
    {
        try
        {
            Volatile.Write(ref _lastStereoPairWidth, pair.Left.Image.Width);
            Volatile.Write(ref _lastStereoPairHeight, pair.Left.Image.Height);

            // Подача кадра в сервис автозахвата (клон для независимого владения)
            var autoCapture = _autoCaptureService;
            if (autoCapture != null && autoCapture.IsRunning && IsDetectionActive)
            {
                try
                {
                    autoCapture.ProcessFrame(pair);
                }
                catch (Exception ex)
                {
                    ExceptionLogger.LogException(ex, "AutoCapture.ProcessFrame");
                }
            }

            // Подача кадра в сервис карты глубины
            if (_depthMapService.IsEnabled)
            {
                try
                {
                    if (TryEnsureDepthMapCalibrationLoaded(out var depthMapEnsureError))
                    {
                        _depthMapService.ProcessStereoFrame(pair);
                    }
                    else
                    {
                        DisableDepthMapWithError(depthMapEnsureError);
                    }
                }
                catch (Exception ex)
                {
                    ExceptionLogger.LogException(ex, "DepthMapService.ProcessFrame");
                }
            }
        }
        finally
        {
            pair.Dispose();
        }
    }

    /// <summary>
    /// Обработчик изменения списка файлов истории. Обновляет менеджер и ректификатор.
    /// </summary>
    private void OnCalibrationFilesChanged()
    {
        RunOnUiThread(() =>
        {
            ResetPreviewRectifier();
            UpdateCurrentModeAssetInfo();
            UpdateDepthMapDependencyInfo();
        });
    }

    /// <summary>
    /// Реагирует на включение карты глубины и валидирует обязательные зависимости.
    /// </summary>
    private void OnDepthMapViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(DepthMapViewModel.IsEnabled))
        {
            return;
        }

        if (_isUpdatingDepthMapState || !DepthMapVm.IsEnabled)
        {
            return;
        }

        if (!TryPrepareDepthMapPipeline(out var error))
        {
            DisableDepthMapWithError(error);
        }
    }

    /// <summary>
    /// Создаёт кадры предпросмотра стереопары. При включённой ректификации
    /// применяет коррекцию искажений на основе активной калибровки.
    /// </summary>
    /// <param name="pair">Стереопара кадров.</param>
    /// <param name="leftPreview">Выходной кадр левой камеры.</param>
    /// <param name="rightPreview">Выходной кадр правой камеры.</param>
    /// <returns>true в случае успеха.</returns>
    public bool TryCreatePreviewFrames(StereoFramePair pair, out Mat leftPreview, out Mat rightPreview, out bool ownsPreview)
    {
        if (!ShowRectified)
        {
            leftPreview = pair.Left.Image;
            rightPreview = pair.Right.Image;
            ownsPreview = false;
            return true;
        }

        leftPreview = pair.Left.Image.Clone();
        rightPreview = pair.Right.Image.Clone();
        ownsPreview = true;

        if (!TryEnsureRectifierForCurrentMode(out _))
        {
            return true;
        }

        lock (_rectificationLock)
        {
            if (_previewCalibration == null || !_previewRectifier.IsInitialized)
            {
                return true;
            }

            if (_previewCalibration.ImageSize.Width <= 0 || _previewCalibration.ImageSize.Height <= 0)
            {
                return true;
            }

            if (pair.Left.Image.Width != _previewCalibration.ImageSize.Width
                || pair.Left.Image.Height != _previewCalibration.ImageSize.Height)
            {
                return true;
            }

            leftPreview.Dispose();
            rightPreview.Dispose();

            leftPreview = new Mat();
            rightPreview = new Mat();
            _previewRectifier.Rectify(pair.Left.Image, pair.Right.Image, leftPreview, rightPreview);
            return true;
        }
    }

    /// <summary>
    /// Создаёт кадр предпросмотра для одиночной камеры с опциональной ректификацией.
    /// </summary>
    /// <param name="frame">Исходный кадр.</param>
    /// <param name="isLeft">true для левой камеры.</param>
    /// <param name="preview">Выходной кадр.</param>
    /// <returns>true в случае успеха.</returns>
    public bool TryCreatePreviewFrame(FrameRaw frame, bool isLeft, out Mat preview, out bool ownsPreview)
    {
        if (!ShowRectified)
        {
            preview = frame.Image;
            ownsPreview = false;
            return true;
        }

        preview = frame.Image.Clone();
        ownsPreview = true;

        if (!TryEnsureRectifierForCurrentMode(out _))
        {
            return true;
        }

        lock (_rectificationLock)
        {
            if (_previewCalibration == null || !_previewRectifier.IsInitialized)
            {
                return true;
            }

            if (_previewCalibration.ImageSize.Width <= 0 || _previewCalibration.ImageSize.Height <= 0)
            {
                return true;
            }

            if (frame.Image.Width != _previewCalibration.ImageSize.Width
                || frame.Image.Height != _previewCalibration.ImageSize.Height)
            {
                return true;
            }

            preview.Dispose();
            preview = new Mat();
            if (isLeft)
            {
                _previewRectifier.RectifyLeft(frame.Image, preview);
            }
            else
            {
                _previewRectifier.RectifyRight(frame.Image, preview);
            }

            return true;
        }
    }

    // === Синхронизация навигации ===

    /// <summary>При смене режима калибровки — сбросить конфликтующие флаги.</summary>
    partial void OnIsCalibrationViewChanged(bool value)
    {
        if (_isUpdatingNavigation)
        {
            return;
        }

        _isUpdatingNavigation = true;
        try
        {
            if (value && IsHistoryView)
            {
                IsHistoryView = false;
            }

            if (value && IsLogView)
            {
                IsLogView = false;
            }

            if (value && IsDepthMapView)
            {
                IsDepthMapView = false;
            }

            OnPropertyChanged(nameof(IsBoardGeneratorView));
        }
        finally
        {
            _isUpdatingNavigation = false;
        }
    }

    partial void OnIsHistoryViewChanged(bool value)
    {
        if (_isUpdatingNavigation)
        {
            return;
        }

        _isUpdatingNavigation = true;
        try
        {
            if (value && IsCalibrationView)
            {
                IsCalibrationView = false;
            }

            if (value && IsLogView)
            {
                IsLogView = false;
            }

            if (value && IsDepthMapView)
            {
                IsDepthMapView = false;
            }

            OnPropertyChanged(nameof(IsBoardGeneratorView));
        }
        finally
        {
            _isUpdatingNavigation = false;
        }
    }

    partial void OnIsLogViewChanged(bool value)
    {
        if (_isUpdatingNavigation)
        {
            return;
        }

        _isUpdatingNavigation = true;
        try
        {
            if (value && IsCalibrationView)
            {
                IsCalibrationView = false;
            }

            if (value && IsHistoryView)
            {
                IsHistoryView = false;
            }

            if (value && IsDepthMapView)
            {
                IsDepthMapView = false;
            }

            OnPropertyChanged(nameof(IsBoardGeneratorView));
        }
        finally
        {
            _isUpdatingNavigation = false;
        }
    }

    partial void OnIsDepthMapViewChanged(bool value)
    {
        if (_isUpdatingNavigation)
        {
            return;
        }

        _isUpdatingNavigation = true;
        try
        {
            if (value && IsCalibrationView)
            {
                IsCalibrationView = false;
            }

            if (value && IsHistoryView)
            {
                IsHistoryView = false;
            }

            if (value && IsLogView)
            {
                IsLogView = false;
            }

            OnPropertyChanged(nameof(IsBoardGeneratorView));
        }
        finally
        {
            _isUpdatingNavigation = false;
        }

        if (value)
        {
            UpdateDepthMapDependencyInfo();
        }

        // Отключаем обработку карты глубины при уходе с вкладки
        if (!value)
        {
            _depthMapService.IsEnabled = false;
            DepthMapVm.IsEnabled = false;
        }
    }

    // === Синхронизация режимов ===

    partial void OnIsAutoModeChanged(bool value)
    {
        OnPropertyChanged(nameof(IsStereoOnlyMode));

        if (_isUpdatingModeSelection)
        {
            return;
        }

        if (value)
        {
            _isUpdatingModeSelection = true;
            try
            {
                if (IsSingleCameraMode)
                {
                    IsSingleCameraMode = false;
                }
            }
            finally
            {
                _isUpdatingModeSelection = false;
            }

            UpdateCalibrationModeSetting(CalibrationMode.Auto);
        }

        OnModeChanged();
    }

    partial void OnIsSingleCameraModeChanged(bool value)
    {
        OnPropertyChanged(nameof(IsStereoOnlyMode));

        if (_isUpdatingModeSelection)
        {
            return;
        }

        if (value)
        {
            _isUpdatingModeSelection = true;
            try
            {
                if (IsAutoMode)
                {
                    IsAutoMode = false;
                }
            }
            finally
            {
                _isUpdatingModeSelection = false;
            }

            UpdateCalibrationModeSetting(CalibrationMode.SingleCamera);
        }

        OnModeChanged();
    }

    partial void OnUseSavedSessionForCalibrationChanged(bool value)
    {
        if (_isApplyingSettings)
        {
            return;
        }

        _settingsService.Settings.UseSavedSessionForCalibration = value;
        _settingsService.Save();

        if (value && AreCamerasConnected)
        {
            _ = DisconnectCamerasAsync();
        }

        OnPropertyChanged(nameof(CanCapture));
        OnPropertyChanged(nameof(CanCalibrate));
        OnPropertyChanged(nameof(CanUseCameraControls));
    }

    partial void OnShowRectifiedChanged(bool value)
    {
        if (!value)
        {
            return;
        }

        ResetPreviewRectifier();
    }

    private void OnModeChanged()
    {
        ResetActiveCaptureSession();
        ResetPreviewRectifier();
        UpdateCurrentModeAssetInfo();
        OnPropertyChanged(nameof(CanCalibrate));
    }

    /// <summary>Текущий выбранный режим калибровки (вычисляемое).</summary>
    private CalibrationMode SelectedCalibrationMode => IsAutoMode
        ? CalibrationMode.Auto
        : IsSingleCameraMode
            ? CalibrationMode.SingleCamera
            : CalibrationMode.StereoOnly;

    // === Команды ===

    /// <summary>Открывает/закрывает окно настроек.</summary>
    [RelayCommand]
    private void OpenSettings()
    {
        IsSettingsOpen = !IsSettingsOpen;
    }

    /// <summary>Событие запроса открытия окна «О программе».</summary>
    public event EventHandler? RequestOpenAbout;

    /// <summary>Открывает окно «О программе».</summary>
    [RelayCommand]
    private void OpenAbout()
    {
        RequestOpenAbout?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Открывает менеджер калибровок из панели карты глубины.
    /// </summary>
    [RelayCommand]
    private void OpenDepthMapCalibrationManager()
    {
        IsHistoryView = true;
    }

    /// <summary>Переключает режим детекции паттерна.</summary>
    [RelayCommand]
    private void ToggleDetection()
    {
        if (IsDetectionActive)
        {
            StopDetectionAndAutoCapture(
                L("Detection stopped. Preview only.", "Детекция остановлена. Режим просмотра."),
                new SolidColorBrush(Color.FromRgb(255, 214, 0)));
            return;
        }

        if (!CanToggleDetection)
        {
            return;
        }

        // Start detection
        _autoCaptureService?.Start();
        IsDetectionActive = true;
        AutoCapturing = true;
        CalibrationStatusText = L(
            "Detection started. Position the calibration board.",
            "Детекция запущена. Расположите калибровочную доску.");
        CalibrationStatusColor = new SolidColorBrush(Color.FromRgb(0, 200, 83));
    }

    /// <summary>
    /// Отменяет текущий процесс подключения к камерам.
    /// </summary>
    [RelayCommand]
    private void CancelCameraConnection()
    {
        if (!IsConnectingCameras) return;
        ExceptionLogger.LogMessage("Camera connection cancelled by user.", "Cameras.Connect");
        _captureCts?.Cancel();
    }

    /// <summary>
    /// Подключает обе камеры, инициализирует конвейер калибровки и запускает автозахват.
    /// </summary>
    [RelayCommand]
    private async Task ConnectCamerasAsync()
    {
        if (!CanUseCameraControls)
        {
            CalibrationStatusText = L(
                "Camera controls are disabled while session source is selected.",
                "Управление камерами отключено, пока выбран источник \"сессия\".");
            CalibrationStatusColor = new SolidColorBrush(Color.FromRgb(255, 214, 0));
            return;
        }

        var settings = _settingsService.Settings;

        if (string.IsNullOrEmpty(settings.LeftCameraUrl) || string.IsNullOrEmpty(settings.RightCameraUrl))
        {
            CalibrationStatusText = L(
                "Camera URLs are not configured. Check Settings.",
                "URL камер не настроены. Проверьте настройки.");
            CalibrationStatusColor = new SolidColorBrush(Color.FromRgb(255, 82, 82));
            NotificationService.Instance.ShowError(L("Camera URLs are not configured. Check Settings.", "URL камер не настроены. Проверьте настройки."));
            return;
        }

        await DisconnectCamerasAsync();
        InitializeCalibrationPipeline();

        _captureCts?.Dispose();
        _captureCts = new CancellationTokenSource();
        _lastLeftFpsUiUpdateTicks = 0;
        _lastRightFpsUiUpdateTicks = 0;
        ResetReconnectStatusIndicators();
        IsConnectingCameras = true;

        var queueCapacity = Math.Clamp(settings.BufferSize, 1, 32);
        ExceptionLogger.LogMessage(
            $"Connecting cameras. mode={SelectedCalibrationMode}, queue={queueCapacity}, syncToleranceMs={_synchronizer.ToleranceMs:0.###}, noFrameTimeoutMs={settings.CameraNoFrameTimeoutMs}, reconnectInitialMs={settings.CameraReconnectInitialDelayMs}, reconnectMaxMs={settings.CameraReconnectMaxDelayMs}",
            "Cameras.Connect");

        try
        {
            _matPool?.Dispose();
            _matPool = new MatPool();

            _leftCamera = new EmguCameraSource(
                "Left",
                settings.LeftCameraUrl,
                settings.LeftCameraLogin,
                settings.LeftCameraPassword,
                settings.CameraBackend,
                settings.HwAcceleration,
                settings.TransportProtocol,
                settings.CameraNoFrameTimeoutMs,
                settings.CameraReconnectInitialDelayMs,
                settings.CameraReconnectMaxDelayMs,
                settings.CameraConnectionTimeoutMs,
                matPool: _matPool);

            _rightCamera = new EmguCameraSource(
                "Right",
                settings.RightCameraUrl,
                settings.RightCameraLogin,
                settings.RightCameraPassword,
                settings.CameraBackend,
                settings.HwAcceleration,
                settings.TransportProtocol,
                settings.CameraNoFrameTimeoutMs,
                settings.CameraReconnectInitialDelayMs,
                settings.CameraReconnectMaxDelayMs,
                settings.CameraConnectionTimeoutMs,
                matPool: _matPool);

            _leftCamera.OnConnectionChanged += OnLeftCameraConnectionChanged;
            _rightCamera.OnConnectionChanged += OnRightCameraConnectionChanged;
            _leftCamera.OnError += OnLeftCameraError;
            _rightCamera.OnError += OnRightCameraError;
            _leftCamera.OnReconnectScheduleChanged += OnLeftCameraReconnectScheduleChanged;
            _rightCamera.OnReconnectScheduleChanged += OnRightCameraReconnectScheduleChanged;

            _leftReader = new AsyncFrameReader(_leftCamera, queueCapacity, _matPool);
            _rightReader = new AsyncFrameReader(_rightCamera, queueCapacity, _matPool);
            _leftReader.OnFrameReady += OnLeftFrameReady;
            _rightReader.OnFrameReady += OnRightFrameReady;

            await Task.WhenAll(
                _leftReader.StartAsync(_captureCts.Token),
                _rightReader.StartAsync(_captureCts.Token));

            if (_captureCts?.IsCancellationRequested == true)
            {
                ExceptionLogger.LogMessage("Camera connection was cancelled.", "Cameras.Connect");
                await DisconnectCamerasAsync();
                return;
            }

            CurrentStep = 1;

        // Запуск сервиса автозахвата
            if (_calibrationEngine != null)
            {
                _autoCaptureService?.Stop();
                _autoCaptureService?.Dispose();
                _autoCaptureService = new AutoCaptureService(
                    _calibrationEngine.CornerDetector,
                    SelectedCalibrationMode,
                    _cornerDetectorRight);
                // Лимит захватов контролируется по реально принятым кадрам (_capturedPairsList),
                // поэтому внутренний лимит сервиса отключаем.
                _autoCaptureService.MaxCaptures = 0;
                _autoCaptureService.StabilityThresholdMs = settings.StabilityThresholdSeconds * 1000;
                _autoCaptureService.MinDetectionIntervalMs = Math.Max(0, settings.DetectionIntervalMs);
                _autoCaptureService.DetectionScale = Math.Clamp(settings.DetectionScale, 0.1f, 1.0f);
                _autoCaptureService.OnValidPairCaptured += OnAutoCapturePairCaptured;
                _autoCaptureService.OnDetectionUpdate += OnAutoDetectionUpdate;
                // Detection is NOT started automatically - user toggles it via button
                IsDetectionActive = false;
                AutoCapturing = false;
                ExceptionLogger.LogMessage(
                    $"Auto-capture service created (not started). required={RequiredPairs}, stabilityMs={_autoCaptureService.StabilityThresholdMs:0}, minIntervalMs={_autoCaptureService.MinCaptureIntervalMs:0}, detectionIntervalMs={_autoCaptureService.MinDetectionIntervalMs:0}",
                    "AutoCapture");
                OnPropertyChanged(nameof(CanToggleDetection));
            }

            // После старта pipeline фиксируем активную сессию под текущие настройки доски,
            // чтобы UI не показывал устаревший (например, ChArUco) контекст.
            EnsureActiveCaptureSession(SelectedCalibrationMode);
            UpdateCurrentModeAssetInfo();

            CalibrationStatusText = L(
                "Cameras connected. Press 'Start Detection' to begin.",
                "Камеры подключены. Нажмите 'Начать поиск' для начала.");
            CalibrationStatusColor = new SolidColorBrush(Color.FromRgb(255, 214, 0));
            NotificationService.Instance.ShowSuccess(L("Cameras connected. Press 'Start Detection' to begin.", "Камеры подключены. Нажмите 'Начать поиск' для начала."));
            ExceptionLogger.LogMessage("Camera pipeline started.", "Cameras.Connect");
        }
        catch (OperationCanceledException)
        {
            ExceptionLogger.LogMessage("Camera connection was cancelled.", "Cameras.Connect");
            await DisconnectCamerasAsync();
            CalibrationStatusText = L(
                "Camera connection cancelled by user.",
                "Подключение к камерам отменено пользователем.");
            CalibrationStatusColor = new SolidColorBrush(Color.FromRgb(255, 214, 0));
            NotificationService.Instance.ShowWarning(L("Camera connection cancelled by user.", "Подключение к камерам отменено пользователем."));
        }
        catch (Exception ex)
        {
            ExceptionLogger.LogException(ex, "ConnectCamerasAsync");
            await DisconnectCamerasAsync();
        }
        finally
        {
            IsConnectingCameras = false;
        }
    }

    /// <summary>
    /// Отключает обе камеры, останавливает сервис автозахвата и сбрасывает состояние.
    /// </summary>
    [RelayCommand]
    private async Task DisconnectCamerasAsync()
    {
        ExceptionLogger.LogMessage("Disconnecting cameras.", "Cameras.Disconnect");

        var leftReader = _leftReader;
        var rightReader = _rightReader;
        var leftCamera = _leftCamera;
        var rightCamera = _rightCamera;
        var captureCts = _captureCts;

        _leftReader = null;
        _rightReader = null;
        _leftCamera = null;
        _rightCamera = null;
        _captureCts = null;

        if (leftReader != null)
        {
            leftReader.OnFrameReady -= OnLeftFrameReady;
        }

        if (rightReader != null)
        {
            rightReader.OnFrameReady -= OnRightFrameReady;
        }

        if (leftCamera != null)
        {
            leftCamera.OnConnectionChanged -= OnLeftCameraConnectionChanged;
            leftCamera.OnError -= OnLeftCameraError;
            leftCamera.OnReconnectScheduleChanged -= OnLeftCameraReconnectScheduleChanged;
        }

        if (rightCamera != null)
        {
            rightCamera.OnConnectionChanged -= OnRightCameraConnectionChanged;
            rightCamera.OnError -= OnRightCameraError;
            rightCamera.OnReconnectScheduleChanged -= OnRightCameraReconnectScheduleChanged;
        }

        captureCts?.Cancel();

        if (leftReader != null)
        {
            try
            {
                await leftReader.StopAsync();
            }
            catch (Exception ex)
            {
                ExceptionLogger.LogException(ex, "DisconnectCamerasAsync.LeftReaderStop");
            }
            finally
            {
                leftReader.Dispose();
            }
        }

        if (rightReader != null)
        {
            try
            {
                await rightReader.StopAsync();
            }
            catch (Exception ex)
            {
                ExceptionLogger.LogException(ex, "DisconnectCamerasAsync.RightReaderStop");
            }
            finally
            {
                rightReader.Dispose();
            }
        }

        try
        {
            leftCamera?.Dispose();
        }
        catch (Exception ex)
        {
            ExceptionLogger.LogException(ex, "DisconnectCamerasAsync.LeftCameraDispose");
        }

        try
        {
            rightCamera?.Dispose();
        }
        catch (Exception ex)
        {
            ExceptionLogger.LogException(ex, "DisconnectCamerasAsync.RightCameraDispose");
        }
        captureCts?.Dispose();

        // Очистка сервиса автозахвата
        if (_autoCaptureService != null)
        {
            _autoCaptureService.OnValidPairCaptured -= OnAutoCapturePairCaptured;
            _autoCaptureService.OnDetectionUpdate -= OnAutoDetectionUpdate;
            _autoCaptureService.Stop();
            _autoCaptureService.Dispose();
            _autoCaptureService = null;
            ExceptionLogger.LogMessage("Auto-capture stopped.", "AutoCapture");
            OnPropertyChanged(nameof(CanToggleDetection));
        }

        _synchronizer.Clear();

        _matPool?.Dispose();
        _matPool = null;

        RunOnUiThread(() =>
        {
            LeftCameraConnected = false;
            RightCameraConnected = false;
            LeftCameraFps = 0;
            RightCameraFps = 0;
            ResetReconnectStatusIndicators();
            ResetWorkflowState();
        });

        ExceptionLogger.LogMessage("Camera pipeline stopped.", "Cameras.Disconnect");
    }

    private void OnLeftCameraConnectionChanged(bool connected)
    {
        HandleCameraConnectionChanged(isLeft: true, connected);
    }

    private void OnRightCameraConnectionChanged(bool connected)
    {
        HandleCameraConnectionChanged(isLeft: false, connected);
    }

    private void OnLeftCameraReconnectScheduleChanged(DateTime? nextAttemptUtc)
    {
        HandleReconnectScheduleChanged(isLeft: true, nextAttemptUtc);
    }

    private void OnRightCameraReconnectScheduleChanged(DateTime? nextAttemptUtc)
    {
        HandleReconnectScheduleChanged(isLeft: false, nextAttemptUtc);
    }

    private void OnLeftCameraError(string message)
    {
        HandleCameraError(isLeft: true, message);
    }

    private void OnRightCameraError(string message)
    {
        HandleCameraError(isLeft: false, message);
    }

    /// <summary>Обработчик изменения подключения камеры. Управляет автозахватом при потере/восстановлении потока.</summary>
    private void HandleCameraConnectionChanged(bool isLeft, bool connected)
    {
        var side = isLeft ? "Left" : "Right";
        ExceptionLogger.LogMessage(
            connected ? "Camera connected." : "Camera disconnected.",
            $"Camera.{side}.Connection");

        RunOnUiThread(() =>
        {
            if (isLeft)
            {
                LeftCameraConnected = connected;
                if (!connected)
                {
                    LeftCameraFps = 0;
                }
            }
            else
            {
                RightCameraConnected = connected;
                if (!connected)
                {
                    RightCameraFps = 0;
                }
            }

            if (!connected && AutoCapturing)
            {
                _autoCaptureService?.Stop();
                AutoCapturing = false;
                ClearDetectionOverlay();
                CalibrationStatusText = L(
                    "Camera stream lost. Auto-capture paused.",
                    "Поток камеры потерян. Автозахват приостановлен.");
                CalibrationStatusColor = new SolidColorBrush(Color.FromRgb(255, 214, 0));
                NotificationService.Instance.ShowWarning(L("Camera stream lost. Auto-capture paused.", "Поток камеры потерян. Автозахват приостановлен."));
            }

            if (!AreCamerasConnected && DepthMapVm.IsEnabled)
            {
                DisableDepthMapWithError(L("Depth map requires connected cameras.", "Для карты глубины нужно подключить камеры."));
            }

            if (connected
                && !AutoCapturing
                && _autoCaptureService != null
                && !_autoCaptureService.IsRunning
                && CurrentStep == 1
                && CapturedPairsCount < RequiredPairs
                && IsAutoCaptureAllowedForCurrentConnections()
                && IsDetectionActive)
            {
                _autoCaptureService.Start();
                AutoCapturing = true;
                CalibrationStatusText = L(
                    "Camera stream restored. Auto-capture resumed.",
                    "Поток камеры восстановлен. Автозахват возобновлён.");
                CalibrationStatusColor = new SolidColorBrush(Color.FromRgb(255, 214, 0));
                NotificationService.Instance.ShowInfo(L("Camera stream restored. Auto-capture resumed.", "Поток камеры восстановлен. Автозахват возобновлён."));
                ExceptionLogger.LogMessage("Auto-capture resumed after reconnect.", "AutoCapture");
            }

            if (connected)
            {
                ClearReconnectSchedule(isLeft);
            }
        });
    }

    /// <summary>Обработчик изменения расписания переподключения камеры.</summary>
    private void HandleReconnectScheduleChanged(bool isLeft, DateTime? nextAttemptUtc)
    {
        RunOnUiThread(() =>
        {
            if (isLeft)
            {
                _leftReconnectAttemptUtc = nextAttemptUtc;
            }
            else
            {
                _rightReconnectAttemptUtc = nextAttemptUtc;
            }

            UpdateReconnectExtraInfo(isLeft);
            UpdateReconnectStatusTimerState();
        });
    }

    private void OnReconnectStatusTimerTick(object? sender, EventArgs e)
    {
        UpdateReconnectExtraInfo(isLeft: true);
        UpdateReconnectExtraInfo(isLeft: false);
        UpdateReconnectStatusTimerState();
    }

    /// <summary>Обновляет текст обратного отсчёта переподключения камеры.</summary>
    private void UpdateReconnectExtraInfo(bool isLeft)
    {
        var nextAttemptUtc = isLeft ? _leftReconnectAttemptUtc : _rightReconnectAttemptUtc;
        var text = string.Empty;

        if (nextAttemptUtc.HasValue)
        {
            var remainingMs = Math.Max(0, (int)Math.Ceiling((nextAttemptUtc.Value - DateTime.UtcNow).TotalMilliseconds));
            text = string.Format(L("Reconnect in {0} ms", "Переподключение через {0} мс"), remainingMs);
        }

        if (isLeft)
        {
            LeftCameraExtraInfoText = text;
        }
        else
        {
            RightCameraExtraInfoText = text;
        }
    }

    /// <summary>Очищает расписание переподключения для указанной камеры.</summary>
    private void ClearReconnectSchedule(bool isLeft)
    {
        if (isLeft)
        {
            _leftReconnectAttemptUtc = null;
            LeftCameraExtraInfoText = string.Empty;
        }
        else
        {
            _rightReconnectAttemptUtc = null;
            RightCameraExtraInfoText = string.Empty;
        }

        UpdateReconnectStatusTimerState();
    }

    private void ResetReconnectStatusIndicators()
    {
        _leftReconnectAttemptUtc = null;
        _rightReconnectAttemptUtc = null;
        LeftCameraExtraInfoText = string.Empty;
        RightCameraExtraInfoText = string.Empty;
        UpdateReconnectStatusTimerState();
    }

    private void UpdateReconnectStatusTimerState()
    {
        var hasPendingReconnect = _leftReconnectAttemptUtc.HasValue || _rightReconnectAttemptUtc.HasValue;
        if (hasPendingReconnect)
        {
            if (!_reconnectStatusTimer.IsEnabled)
            {
                _reconnectStatusTimer.Start();
            }

            return;
        }

        if (_reconnectStatusTimer.IsEnabled)
        {
            _reconnectStatusTimer.Stop();
        }
    }

    /// <summary>Проверяет, разрешён ли автозахват при текущем состоянии подключений.</summary>
    private bool IsAutoCaptureAllowedForCurrentConnections()
    {
        return SelectedCalibrationMode == CalibrationMode.SingleCamera
            ? (LeftCameraConnected || RightCameraConnected)
            : (LeftCameraConnected && RightCameraConnected);
    }

    /// <summary>Обработчик ошибки камеры. Логирует сообщение.</summary>
    private void HandleCameraError(bool isLeft, string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        var side = isLeft ? "Left" : "Right";
        ExceptionLogger.LogMessage(message, $"Camera.{side}.Error");
    }

    /// <summary>Обработчик кадра левой камеры: пушит в синхронизатор, излучает событие, обновляет FPS.</summary>
    private void OnLeftFrameReady(FrameRaw frame)
    {
        try
        {
            _synchronizer.PushLeft(frame.Clone());
        }
        catch (Exception ex)
        {
            ExceptionLogger.LogException(ex, "StereoSynchronizer.PushLeft");
        }

        EmitLeftFrame(frame);
        TryUpdateFps(isLeft: true);
    }

    /// <summary>Обработчик кадра правой камеры.</summary>
    private void OnRightFrameReady(FrameRaw frame)
    {
        try
        {
            _synchronizer.PushRight(frame.Clone());
        }
        catch (Exception ex)
        {
            ExceptionLogger.LogException(ex, "StereoSynchronizer.PushRight");
        }

        EmitRightFrame(frame);
        TryUpdateFps(isLeft: false);
    }

    /// <summary>Излучает событие кадра левой камеры.
    /// Кадр жив на протяжении синхронного вызова; подписчик (View) клонирует сам.</summary>
    private void EmitLeftFrame(FrameRaw frame)
    {
        if (!IsCalibrationView)
        {
            return;
        }

        OnLeftFrameReceived?.Invoke(frame);
    }

    /// <summary>Излучает событие кадра правой камеры.
    /// Кадр жив на протяжении синхронного вызова; подписчик (View) клонирует сам.</summary>
    private void EmitRightFrame(FrameRaw frame)
    {
        if (!IsCalibrationView)
        {
            return;
        }

        OnRightFrameReceived?.Invoke(frame);
    }

    /// <summary>Обновляет показатель FPS в UI с ограничением частоты (250 мс).</summary>
    private void TryUpdateFps(bool isLeft)
    {
        var camera = isLeft ? _leftCamera : _rightCamera;
        if (camera == null)
        {
            return;
        }

        var nowTicks = DateTime.UtcNow.Ticks;
        if (isLeft)
        {
            var previousTicks = Interlocked.Read(ref _lastLeftFpsUiUpdateTicks);
            if (nowTicks - previousTicks < FpsUiUpdateIntervalTicks)
            {
                return;
            }

            Interlocked.Exchange(ref _lastLeftFpsUiUpdateTicks, nowTicks);
        }
        else
        {
            var previousTicks = Interlocked.Read(ref _lastRightFpsUiUpdateTicks);
            if (nowTicks - previousTicks < FpsUiUpdateIntervalTicks)
            {
                return;
            }

            Interlocked.Exchange(ref _lastRightFpsUiUpdateTicks, nowTicks);
        }

        var fps = camera.CurrentFps;

        RunOnUiThread(() =>
        {
            if (isLeft)
            {
                LeftCameraFps = fps;
            }
            else
            {
                RightCameraFps = fps;
            }
        });
    }

    /// <summary>Переопределённый метод выполнения действия в потоке UI.</summary>
    private new static void RunOnUiThread(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
        }
        else
        {
            Dispatcher.UIThread.Post(action);
        }
    }

    /// <summary>
    /// Обработчик автозахвата: добавляет Пару в калибровочный движок, обновляет счётчик,
    /// сохраняет на диск. При достижении цели останавливает автозахват.
    /// </summary>
    private void OnAutoCapturePairCaptured(StereoFramePair pair, PointF[]? leftCorners, PointF[]? rightCorners)
    {
        if (_calibrationEngine == null)
        {
            pair.Dispose();
            return;
        }

        var mode = SelectedCalibrationMode;
        bool added;

        if (mode == CalibrationMode.SingleCamera)
        {
            bool leftAdded = _calibrationEngine.AddSingleImage(pair.Left.Image, true);
            bool rightAdded = _calibrationEngine.AddSingleImage(pair.Right.Image, false);
            added = leftAdded || rightAdded;
        }
        else
        {
            added = _calibrationEngine.AddImagePair(pair.Left.Image, pair.Right.Image);
        }

        if (added)
        {
            _capturedPairsList.Add(pair);
            var captureCompleted = _capturedPairsList.Count >= RequiredPairs;
            ExceptionLogger.LogMessage(
                $"Captured pair {_capturedPairsList.Count}/{RequiredPairs}.",
                "AutoCapture");

            if (captureCompleted)
            {
            // Остановить немедленно, чтобы не пропустить дополнительное
                // обновление детекции до обновления состояния в UI-потоке.
                _autoCaptureService?.Stop();
                ExceptionLogger.LogMessage("Capture target reached. Auto-capture stopped.", "AutoCapture");
            }

            RunOnUiThread(() =>
            {
                CapturedPairsCount = _capturedPairsList.Count;
                CalibrationStatusText = string.Format(
                    L("Auto-captured pair {0}/{1}", "Автозахват пары {0}/{1}"),
                    CapturedPairsCount, RequiredPairs);
                CalibrationStatusColor = new SolidColorBrush(Color.FromRgb(0, 200, 83));

                if (captureCompleted)
                {
                    CurrentStep = 2;
                    StopDetectionAndAutoCapture(
                        L("Auto-capture complete. Ready to calibrate.", "Автозахват завершён. Готово к калибровке."),
                        new SolidColorBrush(Color.FromRgb(0, 200, 83)));
                    NotificationService.Instance.ShowSuccess(L("Auto-capture complete. Ready to calibrate.", "Автозахват завершён. Готово к калибровке."));
                }

                OnPropertyChanged(nameof(CanCalibrate));
            });

            _ = PersistCapturedPairAsync(mode, pair, true, true);

            if (captureCompleted && _activeCaptureSession != null)
            {
                _ = _captureSessionService.CompleteSessionAsync(_activeCaptureSession);
            }
        }
        else
        {
            pair.Dispose();
        }
    }

    /// <summary>Обработчик обновления детекции паттерна от автозахвата.</summary>
    private void OnAutoDetectionUpdate(bool leftFound, bool rightFound, PointF[]? leftCorners, PointF[]? rightCorners)
    {
        if (_autoCaptureService is null || !_autoCaptureService.IsRunning || !AutoCapturing || CapturedPairsCount >= RequiredPairs)
        {
            RunOnUiThread(ClearDetectionOverlay);
            return;
        }

        RunOnUiThread(() =>
        {
            PatternDetectedLeft = leftFound;
            PatternDetectedRight = rightFound;
        });

        PointF[]? overlayLeft = leftCorners;
        PointF[]? overlayRight = rightCorners;

        if (ShowRectified && TryBuildRectifiedOverlayCorners(leftCorners, rightCorners, out var rectifiedLeft, out var rectifiedRight))
        {
            overlayLeft = rectifiedLeft;
            overlayRight = rectifiedRight;
        }

        OnDetectionOverlayUpdate?.Invoke(leftFound, rightFound, overlayLeft, overlayRight, SettingsVm.Settings.BoardType);
    }

    /// <summary>Очищает оверлей детекции паттерна.</summary>
    private void ClearDetectionOverlay()
    {
        PatternDetectedLeft = false;
        PatternDetectedRight = false;
        OnDetectionOverlayUpdate?.Invoke(false, false, null, null, SettingsVm.Settings.BoardType);
    }

    /// <summary>
    /// Принудительно завершает поиск паттерна и автозахват.
    /// Используется при переходе между этапами workflow, чтобы не оставлять «залипшее» состояние кнопки.
    /// </summary>
    private void StopDetectionAndAutoCapture(string? statusText = null, IBrush? statusColor = null)
    {
        _autoCaptureService?.Stop();
        IsDetectionActive = false;
        AutoCapturing = false;
        ClearDetectionOverlay();

        if (!string.IsNullOrWhiteSpace(statusText))
        {
            CalibrationStatusText = statusText;
            CalibrationStatusColor = statusColor ?? new SolidColorBrush(Color.FromRgb(255, 214, 0));
        }
    }

    /// <summary>Ручной захват кадра. Добавляет стереопару в калибровочный движок.</summary>
    [RelayCommand]
    private async Task CaptureFrameAsync()
    {
        if (!CanCapture)
        {
            CalibrationStatusText = L(
                "Capture is unavailable in current source mode.",
                "Захват недоступен в текущем режиме источника.");
            CalibrationStatusColor = new SolidColorBrush(Color.FromRgb(255, 214, 0));
            return;
        }

        if (_calibrationEngine == null)
        {
            CalibrationStatusText = L(
                "Calibration engine is not initialized.",
                "Калибровочный модуль не инициализирован.");
            CalibrationStatusColor = new SolidColorBrush(Color.FromRgb(255, 82, 82));
            return;
        }

        var pair = _synchronizer.TryGetLatestPair();
        var fallbackCaptureWarning = string.Empty;
        if (pair == null)
        {
            if (_synchronizer.TryGetClosestPair(out var closestPair, out var closestDeltaMs) && closestPair != null)
            {
                if (closestDeltaMs <= ManualCaptureFallbackMaxDeltaMs)
                {
                    pair = closestPair;
                    fallbackCaptureWarning = string.Format(
                        L(
                            "Manual capture fallback used: Δ={0:F1} ms (strict tolerance {1:F1} ms).",
                            "Ручной захват выполнен fallback-парой: Δ={0:F1} мс (строгий допуск {1:F1} мс)."),
                        closestDeltaMs,
                        _synchronizer.ToleranceMs);
                    ExceptionLogger.LogMessage(fallbackCaptureWarning, "Capture");
                }
                else
                {
                    closestPair.Dispose();
                    CalibrationStatusText = string.Format(
                        L(
                            "No synchronized frame pair available. Nearest delta is {0:F1} ms, limit is {1:F1} ms.",
                            "Нет синхронной пары кадров. Ближайшая дельта {0:F1} мс, лимит {1:F1} мс."),
                        closestDeltaMs,
                        ManualCaptureFallbackMaxDeltaMs);
                    CalibrationStatusColor = new SolidColorBrush(Color.FromRgb(255, 82, 82));
                    return;
                }
            }
            else
            {
                CalibrationStatusText = L(
                    "No synchronized frame pair available.",
                    "Нет синхронизированной пары кадров.");
                CalibrationStatusColor = new SolidColorBrush(Color.FromRgb(255, 82, 82));
                return;
            }
        }

        if (pair == null)
        {
            CalibrationStatusText = L(
                "No synchronized frame pair available.",
                "Нет синхронизированной пары кадров.");
            CalibrationStatusColor = new SolidColorBrush(Color.FromRgb(255, 82, 82));
            return;
        }

        var mode = SelectedCalibrationMode;
        bool leftPatternFound;
        bool rightPatternFound;

        if (mode == CalibrationMode.SingleCamera)
        {
            bool leftAdded = _calibrationEngine.AddSingleImage(pair.Left.Image, true);
            bool rightAdded = _calibrationEngine.AddSingleImage(pair.Right.Image, false);
            leftPatternFound = leftAdded;
            rightPatternFound = rightAdded;

            if (!leftAdded && !rightAdded)
            {
                pair.Dispose();
                CalibrationStatusText = L(
                    "Pattern not detected in frames. Try again.",
                    "Паттерн не найден в кадрах. Повторите попытку.");
                CalibrationStatusColor = new SolidColorBrush(Color.FromRgb(255, 82, 82));
                return;
            }

            _capturedPairsList.Add(pair);
            CapturedPairsCount = _capturedPairsList.Count;
            ExceptionLogger.LogMessage(
                $"Manual capture {CapturedPairsCount}/{RequiredPairs}. singleCamera L={leftAdded}, R={rightAdded}",
                "Capture");
            CalibrationStatusText = string.Format(
                L(
                    "Captured set {0}/{1} (L:{2}, R:{3})",
                    "Захвачен набор {0}/{1} (Л:{2}, П:{3})"),
                CapturedPairsCount,
                RequiredPairs,
                leftAdded ? L("ok", "ok") : L("miss", "нет"),
                rightAdded ? L("ok", "ok") : L("miss", "нет"));
            CalibrationStatusColor = new SolidColorBrush(Color.FromRgb(255, 214, 0));
        }
        else
        {
            bool added = _calibrationEngine.AddImagePair(pair.Left.Image, pair.Right.Image);
            leftPatternFound = added;
            rightPatternFound = added;
            if (!added)
            {
                pair.Dispose();
                CalibrationStatusText = L(
                    "Pattern not detected in both frames. Try again.",
                    "Паттерн не найден в обоих кадрах. Повторите попытку.");
                CalibrationStatusColor = new SolidColorBrush(Color.FromRgb(255, 82, 82));
                return;
            }

            _capturedPairsList.Add(pair);
            CapturedPairsCount = _capturedPairsList.Count;
            ExceptionLogger.LogMessage(
                $"Manual capture {CapturedPairsCount}/{RequiredPairs}. stereoPair=true",
                "Capture");
            CalibrationStatusText = string.Format(
                L("Captured pair {0}/{1}", "Захвачена пара {0}/{1}"),
                CapturedPairsCount,
                RequiredPairs);
            CalibrationStatusColor = new SolidColorBrush(Color.FromRgb(255, 214, 0));
        }

        if (!string.IsNullOrWhiteSpace(fallbackCaptureWarning))
        {
            CalibrationStatusText = $"{CalibrationStatusText} | {fallbackCaptureWarning}";
        }

        var persistError = await PersistCapturedPairAsync(mode, pair, leftPatternFound, rightPatternFound);
        if (!string.IsNullOrWhiteSpace(persistError))
        {
            CalibrationStatusText = $"{CalibrationStatusText} | {L("Session save warning", "Предупреждение сохранения сессии")}: {persistError}";
            CalibrationStatusColor = new SolidColorBrush(Color.FromRgb(255, 214, 0));
        }

        if (CapturedPairsCount >= RequiredPairs)
        {
            CurrentStep = 2;
            StopDetectionAndAutoCapture(
                L("Detection completed", "Поиск завершен"),
                new SolidColorBrush(Color.FromRgb(255, 214, 0)));

            if (_activeCaptureSession != null)
            {
                _ = _captureSessionService.CompleteSessionAsync(_activeCaptureSession);
            }
        }

        OnPropertyChanged(nameof(CanCalibrate));
    }

    /// <summary>
    /// Запускает калибровку. Поддерживает калибровку по захваченным кадрам
    /// и по сохранённой сессии.
    /// </summary>
    [RelayCommand]
    private async Task CalibrateAsync()
    {
        // Перед переходом к этапу «Калибровка» всегда сбрасываем поиск паттерна.
        StopDetectionAndAutoCapture();

        var mode = SelectedCalibrationMode;

        if (UseSavedSessionForCalibration)
        {
            await CalibrateFromActiveSessionAsync(mode);
            return;
        }

        if (CapturedPairsCount < 10)
        {
            CalibrationStatusText = L(
                "At least 10 captures are required before calibration.",
                "Перед калибровкой требуется минимум 10 захватов.");
            CalibrationStatusColor = new SolidColorBrush(Color.FromRgb(255, 82, 82));
            return;
        }

        if (_calibrationEngine == null)
        {
            CalibrationStatusText = L(
                "Calibration engine is not initialized.",
                "Калибровочный модуль не инициализирован.");
            CalibrationStatusColor = new SolidColorBrush(Color.FromRgb(255, 82, 82));
            return;
        }

        CurrentStep = 2;
        IsCalibrating = true;
        CalibrationStatusText = $"{L("Computing calibration", "Выполняется калибровка")} ({GetModeLabel(mode)})...";
        CalibrationStatusColor = new SolidColorBrush(Color.FromRgb(255, 214, 0));
        ExceptionLogger.LogMessage($"Calibration started. mode={mode}, captures={CapturedPairsCount}", "Calibration");

        try
        {
            _currentCalibration?.Dispose();
            _currentCalibration = await Task.Run(() => RunCalibrationForMode(mode, _calibrationEngine));
            if (_currentCalibration == null)
            {
                throw new InvalidOperationException(
                    L("Calibration returned no result.", "Калибровка не вернула результат."));
            }

            UpdateCalibrationStatus();
            CurrentStep = 3;
            OnPropertyChanged(nameof(HasCalibration));
            OnPropertyChanged(nameof(CalibrateActionText));
            ExceptionLogger.LogMessage(
                $"Calibration completed. mode={mode}, reprojectionError={_currentCalibration.ReprojectionError:F4}, quality={_currentCalibration.Quality}",
                "Calibration");

            await TryAutoSaveCalibrationAsync(mode);
        }
        catch (Exception ex)
        {
            ExceptionLogger.LogException(ex, "CalibrateAsync");
            CalibrationStatusText = $"{L("Calibration failed", "Ошибка калибровки")}: {ex.Message}";
            CalibrationStatusColor = new SolidColorBrush(Color.FromRgb(255, 82, 82));
            NotificationService.Instance.ShowError($"{L("Calibration failed", "Ошибка калибровки")}: {ex.Message}");
        }
        finally
        {
            IsCalibrating = false;
        }
    }

    /// <summary>Открывает папку сессий захвата в проводнике.</summary>
    [RelayCommand]
    private void OpenCaptureSessionsFolder()
    {
        try
        {
            Directory.CreateDirectory(_captureSessionService.SessionsRootPath);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = _captureSessionService.SessionsRootPath,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            ExceptionLogger.LogException(ex, "OpenCaptureSessionsFolder");
            CalibrationStatusText = $"{L("Failed to open sessions folder", "Не удалось открыть папку сессий")}: {ex.Message}";
            CalibrationStatusColor = new SolidColorBrush(Color.FromRgb(255, 82, 82));
        }
    }

    /// <summary>Калибровка по активной сессии захвата (без камер).</summary>
    private async Task CalibrateFromActiveSessionAsync(CalibrationMode mode)
    {
        StopDetectionAndAutoCapture();

        var sessionPath = GetActiveSessionPath(mode);
        if (string.IsNullOrWhiteSpace(sessionPath))
        {
            CalibrationStatusText = L(
                "No active capture session selected for current mode.",
                "Для текущего режима не выбрана активная сессия.");
            CalibrationStatusColor = new SolidColorBrush(Color.FromRgb(255, 82, 82));
            return;
        }

        CurrentStep = 2;
        CalibrationStatusText = $"{L("Computing calibration", "Выполняется калибровка")} ({GetModeLabel(mode)}) {L("from active session", "по активной сессии")}...";
        CalibrationStatusColor = new SolidColorBrush(Color.FromRgb(255, 214, 0));
        IsCalibrating = true;

        try
        {
            var session = await _captureSessionService.LoadSessionAsync(sessionPath);
            if (!session.IsCompleted)
            {
                CalibrationStatusText = L(
                    "Active session is not completed. Finish capturing before calibrating.",
                    "\u0410\u043a\u0442\u0438\u0432\u043d\u0430\u044f \u0441\u0435\u0441\u0441\u0438\u044f \u043d\u0435 \u0437\u0430\u0432\u0435\u0440\u0448\u0435\u043d\u0430. \u0417\u0430\u0432\u0435\u0440\u0448\u0438\u0442\u0435 \u0437\u0430\u0445\u0432\u0430\u0442 \u043f\u0435\u0440\u0435\u0434 \u043a\u0430\u043b\u0438\u0431\u0440\u043e\u0432\u043a\u043e\u0439.");
                CalibrationStatusColor = new SolidColorBrush(Color.FromRgb(255, 214, 0));
                NotificationService.Instance.ShowWarning(CalibrationStatusText);
                IsCalibrating = false;
                return;
            }
            if (session.CaptureMode != mode)
            {
                var expectedModeText = GetModeLabel(mode);
                var actualModeText = GetModeLabel(session.CaptureMode);
                throw new InvalidOperationException(
                    string.Format(
                        L(
                            "Active session mode is {0}, expected {1}.",
                            "Активная сессия имеет режим {0}, ожидается {1}."),
                        actualModeText,
                        expectedModeText));
            }
            var (calibration, acceptedCount) = await Task.Run(() => RunCalibrationFromSession(session, mode));

            _currentCalibration?.Dispose();
            _currentCalibration = calibration;

            CapturedPairsCount = acceptedCount;
            OnPropertyChanged(nameof(CanCalibrate));
            UpdateCalibrationStatus();
            CurrentStep = 3;
            OnPropertyChanged(nameof(HasCalibration));
            OnPropertyChanged(nameof(CalibrateActionText));

            await TryAutoSaveCalibrationAsync(mode);
        }
        catch (Exception ex)
        {
            ExceptionLogger.LogException(ex, "CalibrateFromActiveSessionAsync");
            CalibrationStatusText = $"{L("Session calibration failed", "Ошибка калибровки по сессии")}: {ex.Message}";
            CalibrationStatusColor = new SolidColorBrush(Color.FromRgb(255, 82, 82));
            NotificationService.Instance.ShowError($"{L("Session calibration failed", "Ошибка калибровки по сессии")}: {ex.Message}");
        }
        finally
        {
            IsCalibrating = false;
        }
    }

    /// <summary>Запускает калибровку для указанного режима.</summary>
    private CalibrationResult RunCalibrationForMode(CalibrationMode mode, CalibrationEngine calibrationEngine)
    {
        return mode switch
        {
            CalibrationMode.Auto => calibrationEngine.CalibrateFullStereo(),
            CalibrationMode.StereoOnly => CalibrateStereoOnly(calibrationEngine),
            CalibrationMode.SingleCamera => CalibrateSingleCameraMode(calibrationEngine),
            _ => calibrationEngine.CalibrateFullStereo()
        };
    }

    /// <summary>Запускает калибровку по сохранённой сессии: загружает изображения, создаёт детектор и движок.</summary>
    private (CalibrationResult Calibration, int AcceptedCount) RunCalibrationFromSession(
        CaptureSessionManifest session,
        CalibrationMode mode)
    {
        var markerSize = session.SquareSizeMm * session.MarkerSizeRatio;
        var detector = new CornerDetector(
            session.PatternWidth,
            session.PatternHeight,
            session.BoardType,
            session.CharucoDictionary,
            session.SquareSizeMm,
            markerSize);

        using var engine = new CalibrationEngine(detector);

        var acceptedCount = 0;
        foreach (var capture in session.Captures.OrderBy(item => item.Index))
        {
            var leftPath = Path.Combine(session.FramesDirectory, capture.LeftImageFile);
            var rightPath = Path.Combine(session.FramesDirectory, capture.RightImageFile);

            using var left = LoadSessionImage(leftPath);
            using var right = LoadSessionImage(rightPath);

            bool added;
            if (mode == CalibrationMode.SingleCamera)
            {
                var leftAdded = engine.AddSingleImage(left, true);
                var rightAdded = engine.AddSingleImage(right, false);
                added = leftAdded || rightAdded;
            }
            else
            {
                added = engine.AddImagePair(left, right);
            }

            if (added)
            {
                acceptedCount++;
            }
        }

        if (acceptedCount < 10)
        {
            throw new InvalidOperationException(
                string.Format(
                    L(
                        "Only {0} valid frame set(s) were accepted from session. At least 10 are required.",
                        "Из сессии принято только {0} валидных наборов кадров. Требуется минимум 10."),
                    acceptedCount));
        }

        var calibration = RunCalibrationForMode(mode, engine);
        return (calibration, acceptedCount);
    }

    /// <summary>Загружает изображение сессии с диска.</summary>
    private Mat LoadSessionImage(string imagePath)
    {
        if (!File.Exists(imagePath))
        {
            throw new FileNotFoundException(
                $"{L("Session image not found", "Изображение сессии не найдено")}: {imagePath}");
        }

        var image = CvInvoke.Imread(imagePath, ImreadModes.AnyColor);
        if (image.IsEmpty)
        {
            image.Dispose();
            throw new InvalidOperationException(
                $"{L("Session image is empty or unreadable", "Изображение сессии пустое или не читается")}: {imagePath}");
        }

        return image;
    }

    /// <summary>Калибровка в режиме StereoOnly с использованием существующих intrinsics.</summary>
    private CalibrationResult CalibrateStereoOnly(CalibrationEngine calibrationEngine)
    {
        var calibrationFile = GetIntrinsicsCalibrationFile();
        if (calibrationFile == null)
        {
            throw new InvalidOperationException(
                L(
                    "Stereo Only mode requires an existing calibration file.",
                    "Для режима Stereo Only требуется существующий файл калибровки."));
        }

        using var intrinsics = CalibrationResult.LoadFromXml(calibrationFile);
        return calibrationEngine.CalibrateStereoWithIntrinsics(
            intrinsics.CameraMatrixLeft,
            intrinsics.DistCoeffsLeft,
            intrinsics.CameraMatrixRight,
            intrinsics.DistCoeffsRight);
    }

    /// <summary>Получает файл intrinsics для режима StereoOnly (приоритет: активный > последний).</summary>
    private string? GetIntrinsicsCalibrationFile()
    {
        var settings = _settingsService.Settings;
        var preferred = new[]
        {
            settings.ActiveStereoOnlyCalibrationPath,
            settings.ActiveAutoCalibrationPath,
            settings.ActiveSingleCameraCalibrationPath
        }
        .Where(path => !string.IsNullOrWhiteSpace(path))
        .Select(path => Path.GetFullPath(path))
        .Where(File.Exists)
        .FirstOrDefault();

        if (!string.IsNullOrWhiteSpace(preferred))
        {
            return preferred;
        }

        return CalibrationHelpers.GetLatestCalibrationFile(settings.CalibrationDataPath);
    }

    /// <summary>Калибровка в режиме SingleCamera: калибрует каждую камеру отдельно и объединяет результат.</summary>
    private CalibrationResult CalibrateSingleCameraMode(CalibrationEngine calibrationEngine)
    {
        using var left = calibrationEngine.CalibrateSingleCamera(isLeft: true);
        using var right = calibrationEngine.CalibrateSingleCamera(isLeft: false);

        return new CalibrationResult
        {
            CameraMatrixLeft = left.CameraMatrix.Clone(),
            CameraMatrixRight = right.CameraMatrix.Clone(),
            DistCoeffsLeft = left.DistCoeffs.Clone(),
            DistCoeffsRight = right.DistCoeffs.Clone(),
            R = CalibrationHelpers.CreateIdentity(3),
            T = CalibrationHelpers.CreateZero(3, 1),
            E = CalibrationHelpers.CreateZero(3, 3),
            F = CalibrationHelpers.CreateZero(3, 3),
            R1 = CalibrationHelpers.CreateIdentity(3),
            R2 = CalibrationHelpers.CreateIdentity(3),
            P1 = CalibrationHelpers.CreateProjection(),
            P2 = CalibrationHelpers.CreateProjection(),
            Q = CalibrationHelpers.CreateIdentity(4),
            ReprojectionError = (left.ReprojectionError + right.ReprojectionError) / 2.0,
            ImageSize = left.ImageSize,
            ImagePairCount = Math.Min(left.ImageCount, right.ImageCount),
            CalibrationDate = DateTime.Now
        };
    }

    /// <summary>Сохраняет результат калибровки в XML-файл и обновляет настройки.</summary>
    private async Task<string?> SaveCalibrationAsync(CalibrationMode mode)
    {
        if (_currentCalibration == null)
        {
            return null;
        }

        try
        {
            var settings = _settingsService.Settings;
            var directory = CalibrationHelpers.ResolvePath(settings.CalibrationDataPath, "./CalibrationData");

            Directory.CreateDirectory(directory);

            var prefix = mode switch
            {
                CalibrationMode.Auto => "StereoAuto",
                CalibrationMode.StereoOnly => "StereoOnly",
                CalibrationMode.SingleCamera => "SingleCamera",
                _ => "Calibration"
            };

            var fileName = $"{prefix}_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.xml";
            var fullPath = Path.Combine(directory, fileName);

            await Task.Run(() => _currentCalibration.SaveToXml(fullPath));

            SetActiveCalibrationPath(mode, fullPath, persist: false);
            _settingsService.Save();

            CalibrationManagerVm.RefreshCommand.Execute(null);
            ResetPreviewRectifier();
            UpdateCurrentModeAssetInfo();
            return fileName;
        }
        catch (Exception ex)
        {
            ExceptionLogger.LogException(ex, "SaveCalibrationAsync");
            return null;
        }
    }

    /// <summary>Пытается автоматически сохранить результат калибровки, если включено автосохранение.</summary>
    private async Task TryAutoSaveCalibrationAsync(CalibrationMode mode)
    {
        if (!_settingsService.Settings.AutoSaveCalibration)
        {
            CalibrationStatusText = $"{CalibrationStatusText} | {L("Auto-save is disabled", "Автосохранение отключено")}.";
            NotificationService.Instance.ShowInfo(L("Auto-save is disabled", "Автосохранение отключено"));
            return;
        }

        var savedFile = await SaveCalibrationAsync(mode);
        if (!string.IsNullOrEmpty(savedFile))
        {
            CalibrationStatusText = $"{CalibrationStatusText} | {L("Saved", "Сохранено")}: {savedFile}";
            NotificationService.Instance.ShowSuccess($"{L("Saved", "Сохранено")}: {savedFile}");
            CurrentStep = 4;
        }
    }

    /// <summary>Сохраняет захваченную пару в сессию на диск.</summary>
    private async Task<string?> PersistCapturedPairAsync(
        CalibrationMode mode,
        StereoFramePair pair,
        bool leftPatternFound,
        bool rightPatternFound)
    {
        try
        {
            var session = EnsureActiveCaptureSession(mode);
            await _captureSessionService.AppendCaptureAsync(session, pair, leftPatternFound, rightPatternFound);
            CaptureSessionInfoText = $"{L("Capture session", "Сессия захвата")}: {session.SessionId} | {L("Captures", "Кадры")}: {session.Captures.Count}";
            return null;
        }
        catch (Exception ex)
        {
            ExceptionLogger.LogException(ex, "PersistCapturedPairAsync");
            return ex.Message;
        }
    }

    /// <summary>
    /// Создаёт активную сессию захвата, если её нет или она несовместима с текущими настройками,
    /// и сразу фиксирует путь активной сессии в настройках.
    /// </summary>
    private CaptureSessionManifest EnsureActiveCaptureSession(CalibrationMode mode)
    {
        var settings = _settingsService.Settings;
        if (_activeCaptureSession == null || !IsCompatibleSession(_activeCaptureSession, settings, mode))
        {
            _activeCaptureSession = _captureSessionService.StartSession(settings, mode);
            // Новая сессия автоматически становится активной для выбранного режима.
            SetActiveSessionPath(mode, _activeCaptureSession.SessionDirectory, persist: false);
            _settingsService.Save();
            CaptureSessionInfoText = $"{L("Capture session", "Сессия захвата")}: {_activeCaptureSession.SessionId} | {L("Captures", "Кадры")}: 0";
        }

        return _activeCaptureSession;
    }

    /// <summary>Проверяет совместимость сессии с текущими настройками и режимом.</summary>
    private static bool IsCompatibleSession(
        CaptureSessionManifest session,
        AppSettings settings,
        CalibrationMode mode)
    {
        return session.CaptureMode == mode
            && session.BoardType == settings.BoardType
            && session.PatternWidth == settings.PatternWidth
            && session.PatternHeight == settings.PatternHeight
            && Math.Abs(session.SquareSizeMm - settings.SquareSize) < 0.0001f
            && Math.Abs(session.MarkerSizeRatio - settings.MarkerSizeRatio) < 0.0001f
            && session.CharucoDictionary == settings.CharucoDictionary;
    }

    /// <summary>Сбрасывает активную сессию захвата.</summary>
    private void ResetActiveCaptureSession()
    {
        _activeCaptureSession = null;
        CaptureSessionInfoText = L("Capture session: not started.", "Сессия захвата: не начата.");
    }

    /// <summary>
    /// Проверяет зависимости карты глубины и подготавливает pipeline перед запуском.
    /// </summary>
    private bool TryPrepareDepthMapPipeline(out string error)
    {
        error = string.Empty;

        if (!AreCamerasConnected)
        {
            error = L("Depth map requires connected cameras.", "Для карты глубины нужно подключить камеры.");
            return false;
        }

        return TryEnsureDepthMapCalibrationLoaded(out error);
    }

    /// <summary>
    /// Гарантирует, что калибровка для карты глубины загружена в rectifier/depth service.
    /// </summary>
    private bool TryEnsureDepthMapCalibrationLoaded(out string error)
    {
        var depthCalibrationPath = GetActiveDepthCalibrationPath();
        if (string.IsNullOrWhiteSpace(depthCalibrationPath))
        {
            error = L("Depth map requires an active calibration file.", "Для карты глубины нужна активная калибровка.");
            return false;
        }

        return TryEnsureRectifierByPath(depthCalibrationPath, out error);
    }

    /// <summary>
    /// Отключает карту глубины и показывает пользователю понятную причину отказа.
    /// </summary>
    private void DisableDepthMapWithError(string error)
    {
        var message = string.IsNullOrWhiteSpace(error)
            ? L("Depth map prerequisites are not met.", "Не выполнены условия для запуска карты глубины.")
            : error;

        RunOnUiThread(() =>
        {
            if (_isUpdatingDepthMapState)
            {
                return;
            }

            // Защита от рекурсивных срабатываний PropertyChanged при изменении IsEnabled.
            _isUpdatingDepthMapState = true;
            try
            {
                _depthMapService.IsEnabled = false;
                if (DepthMapVm.IsEnabled)
                {
                    DepthMapVm.IsEnabled = false;
                }
            }
            finally
            {
                _isUpdatingDepthMapState = false;
            }

            DepthMapVm.StatusText = message;
            CalibrationStatusText = message;
            CalibrationStatusColor = new SolidColorBrush(Color.FromRgb(255, 82, 82));
            NotificationService.Instance.ShowError(message);
        });
    }

    /// <summary>
    /// Обеспечивает инициализацию ректификатора для текущего режима калибровки.
    /// Кэширует загруженную калибровку по пути файла.
    /// </summary>
    private bool TryEnsureRectifierForCurrentMode(out string error)
    {
        var activeCalibrationPath = GetActiveCalibrationPath(SelectedCalibrationMode);
        if (string.IsNullOrWhiteSpace(activeCalibrationPath))
        {
            error = L("No active calibration selected.", "Активная калибровка не выбрана.");
            return false;
        }

        return TryEnsureRectifierByPath(activeCalibrationPath, out error);
    }

    /// <summary>
    /// Загружает калибровку по указанному пути и инициализирует общий rectifier/depth pipeline.
    /// </summary>
    /// <param name="calibrationPath">Путь к XML-файлу калибровки.</param>
    /// <param name="error">Текст ошибки при неуспехе.</param>
    /// <returns>true, если калибровка валидна и pipeline готов.</returns>
    private bool TryEnsureRectifierByPath(string calibrationPath, out string error)
    {
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(calibrationPath))
        {
            error = L("No active calibration selected.", "Активная калибровка не выбрана.");
            return false;
        }

        var fullPath = Path.GetFullPath(calibrationPath);
        if (!File.Exists(fullPath))
        {
            error = $"{L("Active calibration file is missing", "Файл активной калибровки отсутствует")}: {fullPath}";
            return false;
        }

        lock (_rectificationLock)
        {
            // Если уже загружен этот же файл и ректификатор готов, повторная загрузка не нужна.
            if (_previewCalibrationPath != null
                && string.Equals(_previewCalibrationPath, fullPath, StringComparison.OrdinalIgnoreCase)
                && _previewRectifier.IsInitialized)
            {
                return true;
            }

            try
            {
                // Общий путь загрузки: одна калибровка используется и для предпросмотра, и для карты глубины.
                _previewCalibration?.Dispose();
                _previewCalibration = CalibrationResult.LoadFromXml(fullPath);
                _previewRectifier.Initialize(_previewCalibration);
                _depthMapService.LoadCalibration(_previewCalibration);
                _previewCalibrationPath = fullPath;
                return true;
            }
            catch (Exception ex)
            {
                ExceptionLogger.LogException(ex, "TryEnsureRectifierByPath");
                _previewCalibration?.Dispose();
                _previewCalibration = null;
                _previewCalibrationPath = null;
                error = ex.Message;
                return false;
            }
        }
    }

    /// <summary>Строит ректифицированные координаты углов для оверлея детекции.</summary>
    private bool TryBuildRectifiedOverlayCorners(
        PointF[]? leftCorners,
        PointF[]? rightCorners,
        out PointF[]? rectifiedLeftCorners,
        out PointF[]? rectifiedRightCorners)
    {
        rectifiedLeftCorners = leftCorners;
        rectifiedRightCorners = rightCorners;

        if (!TryEnsureRectifierForCurrentMode(out _))
        {
            return false;
        }

        lock (_rectificationLock)
        {
            if (_previewCalibration == null || !_previewRectifier.IsInitialized)
            {
                return false;
            }

            if (_previewCalibration.ImageSize.Width <= 0 || _previewCalibration.ImageSize.Height <= 0)
            {
                return false;
            }

            if (!IsLastStereoPairCompatibleWithCalibration(_previewCalibration.ImageSize))
            {
                return false;
            }

            rectifiedLeftCorners = RectifyCornerPoints(
                leftCorners,
                _previewCalibration.CameraMatrixLeft,
                _previewCalibration.DistCoeffsLeft,
                _previewCalibration.R1,
                _previewCalibration.P1);

            rectifiedRightCorners = RectifyCornerPoints(
                rightCorners,
                _previewCalibration.CameraMatrixRight,
                _previewCalibration.DistCoeffsRight,
                _previewCalibration.R2,
                _previewCalibration.P2);

            return true;
        }
    }

    /// <summary>Проверяет совместимость размера последней стереопары с калибровкой.</summary>
    private bool IsLastStereoPairCompatibleWithCalibration(System.Drawing.Size calibrationSize)
    {
        var width = Volatile.Read(ref _lastStereoPairWidth);
        var height = Volatile.Read(ref _lastStereoPairHeight);

        return width > 0
               && height > 0
               && width == calibrationSize.Width
               && height == calibrationSize.Height;
    }

    /// <summary>Ректифицирует координаты углов через UndistortPoints.</summary>
    private static PointF[]? RectifyCornerPoints(
        PointF[]? corners,
        Mat cameraMatrix,
        Mat distCoeffs,
        Mat rectificationMatrix,
        Mat projectionMatrix)
    {
        if (corners == null || corners.Length == 0)
        {
            return corners;
        }

        using var src = new VectorOfPointF(corners);
        using var dst = new VectorOfPointF();
        CvInvoke.UndistortPoints(src, dst, cameraMatrix, distCoeffs, rectificationMatrix, projectionMatrix);
        return dst.ToArray();
    }

    /// <summary>Сбрасывает кэшированный ректификатор предпросмотра.</summary>
    private void ResetPreviewRectifier()
    {
        lock (_rectificationLock)
        {
            _previewCalibration?.Dispose();
            _previewCalibration = null;
            _previewCalibrationPath = null;
        }
    }

    /// <summary>Возвращает путь активной калибровки для режима.</summary>
    private string GetActiveCalibrationPath(CalibrationMode mode) => mode switch
    {
        CalibrationMode.Auto => _settingsService.Settings.ActiveAutoCalibrationPath,
        CalibrationMode.StereoOnly => _settingsService.Settings.ActiveStereoOnlyCalibrationPath,
        CalibrationMode.SingleCamera => _settingsService.Settings.ActiveSingleCameraCalibrationPath,
        _ => string.Empty
    };

    /// <summary>
    /// Возвращает путь активной калибровки для карты глубины по цепочке fallback.
    /// </summary>
    private string GetActiveDepthCalibrationPath()
    {
        var settings = _settingsService.Settings;

        // Приоритет: отдельная depth-калибровка -> Auto -> StereoOnly.
        if (!string.IsNullOrWhiteSpace(settings.ActiveDepthMapCalibrationPath))
        {
            return settings.ActiveDepthMapCalibrationPath;
        }

        if (!string.IsNullOrWhiteSpace(settings.ActiveAutoCalibrationPath))
        {
            return settings.ActiveAutoCalibrationPath;
        }

        if (!string.IsNullOrWhiteSpace(settings.ActiveStereoOnlyCalibrationPath))
        {
            return settings.ActiveStereoOnlyCalibrationPath;
        }

        return string.Empty;
    }

    /// <summary>Устанавливает путь активной калибровки для режима.</summary>
    private void SetActiveCalibrationPath(CalibrationMode mode, string path, bool persist)
    {
        switch (mode)
        {
            case CalibrationMode.Auto:
                _settingsService.Settings.ActiveAutoCalibrationPath = path;
                _settingsService.Settings.ActiveDepthMapCalibrationPath = path;
                break;
            case CalibrationMode.StereoOnly:
                _settingsService.Settings.ActiveStereoOnlyCalibrationPath = path;
                _settingsService.Settings.ActiveDepthMapCalibrationPath = path;
                break;
            case CalibrationMode.SingleCamera:
                _settingsService.Settings.ActiveSingleCameraCalibrationPath = path;
                break;
        }

        if (persist)
        {
            _settingsService.Save();
        }
    }

    /// <summary>Возвращает путь активной сессии для режима.</summary>
    private string GetActiveSessionPath(CalibrationMode mode) => mode switch
    {
        CalibrationMode.Auto => _settingsService.Settings.ActiveAutoSessionPath,
        CalibrationMode.StereoOnly => _settingsService.Settings.ActiveStereoOnlySessionPath,
        CalibrationMode.SingleCamera => _settingsService.Settings.ActiveSingleCameraSessionPath,
        _ => string.Empty
    };

    /// <summary>
    /// Устанавливает путь активной сессии для режима и при необходимости сохраняет настройки.
    /// </summary>
    private void SetActiveSessionPath(CalibrationMode mode, string path, bool persist)
    {
        switch (mode)
        {
            case CalibrationMode.Auto:
                _settingsService.Settings.ActiveAutoSessionPath = path;
                break;
            case CalibrationMode.StereoOnly:
                _settingsService.Settings.ActiveStereoOnlySessionPath = path;
                break;
            case CalibrationMode.SingleCamera:
                _settingsService.Settings.ActiveSingleCameraSessionPath = path;
                break;
        }

        if (persist)
        {
            _settingsService.Save();
        }
    }

    /// <summary>Обновляет тексты активной калибровки и сессии для текущего режима.</summary>
    private void UpdateCurrentModeAssetInfo()
    {
        var settings = _settingsService.Settings;
        var mode = SelectedCalibrationMode;
        var calibrationPath = GetActiveCalibrationPath(mode);
        var sessionPath = GetActiveSessionPath(mode);

        CurrentModeActiveCalibrationText = string.IsNullOrWhiteSpace(calibrationPath)
            ? L("Active XML: not selected", "Активный XML: не выбран")
            : $"{L("Active XML", "Активный XML")}: {Path.GetFileName(calibrationPath)}";
        CurrentPatternDescriptorText = GetCurrentPatternDescriptorText(settings);

        if (string.IsNullOrWhiteSpace(sessionPath) || !Directory.Exists(sessionPath))
        {
            CurrentModeActiveSessionText = L("Active session: not selected", "Активная сессия: не выбрана");
            CurrentModeActiveSessionCaptureCount = 0;
            CurrentModeActiveSessionIsCompleted = false;
        }
        else
        {
            if (TryReadSessionSummary(
                    sessionPath,
                    out var sessionId,
                    out var captureCount,
                    out var isCompleted,
                    out var manifest))
            {
                var completedMark = isCompleted ? " \u2713" : "";
                if (manifest != null && IsCompatibleSession(manifest, settings, mode))
                {
                    CurrentModeActiveSessionCaptureCount = captureCount;
                    CurrentModeActiveSessionIsCompleted = isCompleted;
                    CurrentModeActiveSessionText = $"{L("Active session", "Активная сессия")}: {sessionId} ({captureCount}{completedMark})";
                }
                else
                {
                    CurrentModeActiveSessionCaptureCount = 0;
                    CurrentModeActiveSessionIsCompleted = false;
                    CurrentModeActiveSessionText =
                        $"{L("Active session (incompatible)", "Активная сессия (несовместима)")}: {sessionId} ({captureCount}{completedMark}) | " +
                        L("Session is incompatible with current board settings.", "Сессия несовместима с текущими настройками доски.");
                }
            }
            else
            {
                CurrentModeActiveSessionText = $"{L("Active session is invalid", "Активная сессия повреждена")}: {sessionPath}";
                CurrentModeActiveSessionCaptureCount = 0;
                CurrentModeActiveSessionIsCompleted = false;
            }
        }

        UpdateDepthMapDependencyInfo();
        OnPropertyChanged(nameof(CanCalibrate));
    }

    /// <summary>
    /// Формирует пользовательское описание текущего паттерна калибровочной доски.
    /// </summary>
    private string GetCurrentPatternDescriptorText(AppSettings settings)
    {
        var descriptor = settings.BoardType switch
        {
            BoardType.Chessboard => string.Format(
                L(
                    "Chessboard {0}x{1} inner corners",
                    "Chessboard {0}x{1} внутренних углов"),
                settings.PatternWidth,
                settings.PatternHeight),
            BoardType.ChArUco => string.Format(
                L(
                    "ChArUco {0}x{1} cells ({2})",
                    "ChArUco {0}x{1} клеток ({2})"),
                settings.PatternWidth,
                settings.PatternHeight,
                settings.CharucoDictionary),
            _ => $"{settings.BoardType} {settings.PatternWidth}x{settings.PatternHeight}"
        };

        return $"{L("Current pattern", "Текущий паттерн")}: {descriptor}";
    }

    /// <summary>
    /// Обновляет текст активной depth-калибровки и отмечает отсутствие файла, если он удалён.
    /// </summary>
    private void UpdateDepthMapDependencyInfo()
    {
        var depthCalibrationPath = GetActiveDepthCalibrationPath();
        if (string.IsNullOrWhiteSpace(depthCalibrationPath))
        {
            DepthMapActiveCalibrationText = $"{DepthMapCalibrationLabelText}: {L("not selected", "не выбрано")}";
            return;
        }

        try
        {
            var fullPath = Path.GetFullPath(depthCalibrationPath);
            var displayName = Path.GetFileName(fullPath);
            if (string.IsNullOrWhiteSpace(displayName))
            {
                displayName = fullPath;
            }

            DepthMapActiveCalibrationText = File.Exists(fullPath)
                ? $"{DepthMapCalibrationLabelText}: {displayName}"
                : $"{DepthMapCalibrationLabelText}: {displayName} ({L("missing", "отсутствует")})";
        }
        catch
        {
            DepthMapActiveCalibrationText = $"{DepthMapCalibrationLabelText}: {depthCalibrationPath} ({L("missing", "отсутствует")})";
        }
    }

    private static bool TryReadSessionSummary(
        string sessionPath,
        out string sessionId,
        out int captureCount,
        out bool isCompleted,
        out CaptureSessionManifest? manifest)
    {
        sessionId = string.Empty;
        captureCount = 0;
        isCompleted = false;
        manifest = null;

        try
        {
            var manifestPath = Path.Combine(sessionPath, "session.json");
            if (!File.Exists(manifestPath))
            {
                return false;
            }

            var json = File.ReadAllText(manifestPath);
            var loadedManifest = JsonSerializer.Deserialize<CaptureSessionManifest>(
                json,
                new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    Converters = { new JsonStringEnumConverter() }
                });

            if (loadedManifest == null)
            {
                return false;
            }

            manifest = loadedManifest;
            sessionId = string.IsNullOrWhiteSpace(loadedManifest.SessionId)
                ? Path.GetFileName(sessionPath)
                : loadedManifest.SessionId;
            captureCount = loadedManifest.Captures.Count;
            isCompleted = loadedManifest.IsCompleted;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void InitializeCalibrationPipeline()
    {
        _cornerDetector = null;
        _cornerDetectorRight = null;
        _calibrationEngine?.Dispose();

        var settings = _settingsService.Settings;
        _cornerDetector = new CornerDetector(
            settings.PatternWidth,
            settings.PatternHeight,
            settings.BoardType,
            settings.CharucoDictionary,
            settings.SquareSize,
            settings.SquareSize * settings.MarkerSizeRatio);
        // Второй детектор с идентичными параметрами для параллельной детекции L/R
        _cornerDetectorRight = new CornerDetector(
            settings.PatternWidth,
            settings.PatternHeight,
            settings.BoardType,
            settings.CharucoDictionary,
            settings.SquareSize,
            settings.SquareSize * settings.MarkerSizeRatio);
        _calibrationEngine = new CalibrationEngine(_cornerDetector);
        if (settings.UseRationalModel)
            _calibrationEngine.IntrinsicCalibrationFlags = CalibType.RationalModel;
    }

    private void ResetWorkflowState(bool preserveCaptured = false)
    {
        StopDetectionAndAutoCapture();
        CurrentStep = 0;

        if (!preserveCaptured)
        {
            foreach (var pair in _capturedPairsList)
            {
                pair.Dispose();
            }
            _capturedPairsList.Clear();
            CapturedPairsCount = 0;
            ResetActiveCaptureSession();
        }

        _currentCalibration?.Dispose();
        _currentCalibration = null;
        OnPropertyChanged(nameof(HasCalibration));
        OnPropertyChanged(nameof(CalibrateActionText));
        OnPropertyChanged(nameof(CanCalibrate));

        CalibrationStatusText = L(
            "Ready. Connect cameras to begin.",
            "Готово. Подключите камеры для начала.");
        CalibrationStatusColor = new SolidColorBrush(Color.FromRgb(150, 150, 150));
    }

    private void ReinitializePipelineForPatternChange()
    {
        ResetWorkflowState();
        CurrentStep = AreCamerasConnected ? 1 : 0;
        InitializeCalibrationPipeline();
        CalibrationStatusText = L(
            "Calibration pattern changed. Captured frames were reset.",
            "Паттерн калибровки изменён. Захваченные кадры сброшены.");
        CalibrationStatusColor = new SolidColorBrush(Color.FromRgb(255, 214, 0));
        UpdateCurrentModeAssetInfo();
    }

    private void UpdateCalibrationStatus()
    {
        if (_currentCalibration == null)
        {
            CalibrationStatusText = L("Not Calibrated", "Не откалибровано");
            CalibrationStatusColor = new SolidColorBrush(Color.FromRgb(255, 82, 82));
            return;
        }

        var error = _currentCalibration.ReprojectionError;
        CalibrationStatusText = string.Format(
            L("Calibrated ({0:F2} px)", "Откалибровано ({0:F2} px)"),
            error);

        CalibrationStatusColor = _currentCalibration.Quality switch
        {
            CalibrationQuality.Excellent => new SolidColorBrush(Color.FromRgb(0, 200, 83)),
            CalibrationQuality.Good => new SolidColorBrush(Color.FromRgb(255, 214, 0)),
            _ => new SolidColorBrush(Color.FromRgb(255, 82, 82))
        };

        NotificationService.Instance.ShowSuccess(CalibrationStatusText);
    }

    private void AttachSettings(AppSettings settings)
    {
        if (_activeSettings != null)
        {
            _activeSettings.PropertyChanged -= OnSettingsChanged;
        }

        _activeSettings = settings;
        _activeSettings.PropertyChanged += OnSettingsChanged;

        ApplySettings(settings);
    }

    private void ApplySettings(AppSettings settings)
    {
        _isApplyingSettings = true;
        try
        {
            RequiredPairs = settings.RequiredFrames;
            _synchronizer.ToleranceMs = settings.SyncToleranceMs;
            ApplyLanguage(settings.Language);
            UpdateLocalizedUiTexts();
            ApplyCalibrationModeFromSettings(settings.CalibrationMode);
            UseSavedSessionForCalibration = settings.UseSavedSessionForCalibration;

            _captureSessionService.UpdateRootPath(settings.CaptureSessionsPath);
            UpdateCaptureStorageInfo();
            UpdateCurrentModeAssetInfo();
            InitializeCalibrationPipeline();
        }
        finally
        {
            _isApplyingSettings = false;
        }
    }

    private static void ApplyLanguage(string languageCode)
    {
        var normalized = languageCode?.Trim().ToLowerInvariant();
        var cultureName = normalized switch
        {
            "en" => "en-US",
            "ru" => "ru-RU",
            _ => "ru-RU"
        };

        var culture = CultureInfo.GetCultureInfo(cultureName);
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }

    private void ApplyCalibrationModeFromSettings(CalibrationMode mode)
    {
        if (!Enum.IsDefined(mode))
        {
            mode = CalibrationMode.Auto;

            if (_settingsService.Settings.CalibrationMode != CalibrationMode.Auto)
            {
                _settingsService.Settings.CalibrationMode = CalibrationMode.Auto;
                _settingsService.Save();
            }
        }

        _isUpdatingModeSelection = true;
        try
        {
            switch (mode)
            {
                case CalibrationMode.Auto:
                    IsAutoMode = true;
                    IsSingleCameraMode = false;
                    break;
                case CalibrationMode.SingleCamera:
                    IsAutoMode = false;
                    IsSingleCameraMode = true;
                    break;
                case CalibrationMode.StereoOnly:
                    IsAutoMode = false;
                    IsSingleCameraMode = false;
                    break;
                default:
                    IsAutoMode = true;
                    IsSingleCameraMode = false;
                    break;
            }

            OnPropertyChanged(nameof(IsStereoOnlyMode));
        }
        finally
        {
            _isUpdatingModeSelection = false;
        }
    }

    private void UpdateCalibrationModeSetting(CalibrationMode mode)
    {
        if (_isUpdatingModeSelection || _isApplyingSettings)
        {
            return;
        }

        if (_settingsService.Settings.CalibrationMode != mode)
        {
            _settingsService.Settings.CalibrationMode = mode;
            _settingsService.Save();
        }
    }

    private void OnSettingsViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsViewModel.Settings))
        {
            AttachSettings(_settingsService.Settings);
        }
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        var settings = _settingsService.Settings;

        switch (e.PropertyName)
        {
            case nameof(AppSettings.RequiredFrames):
                RequiredPairs = settings.RequiredFrames;
                break;

            case nameof(AppSettings.SyncToleranceMs):
                _synchronizer.ToleranceMs = settings.SyncToleranceMs;
                break;

            case nameof(AppSettings.DetectionIntervalMs):
                if (_autoCaptureService != null)
                {
                    _autoCaptureService.MinDetectionIntervalMs = Math.Max(0, settings.DetectionIntervalMs);
                }
                break;

            case nameof(AppSettings.DetectionScale):
                if (_autoCaptureService != null)
                {
                    _autoCaptureService.DetectionScale = Math.Clamp(settings.DetectionScale, 0.1f, 1.0f);
                }
                break;

            case nameof(AppSettings.Language):
                ApplyLanguage(settings.Language);
                UpdateLocalizedUiTexts();
                UpdateCurrentModeAssetInfo();
                break;

            case nameof(AppSettings.CalibrationMode):
                ApplyCalibrationModeFromSettings(settings.CalibrationMode);
                UpdateCurrentModeAssetInfo();
                break;

            case nameof(AppSettings.CalibrationDataPath):
                UpdateDepthMapDependencyInfo();
                break;

            case nameof(AppSettings.CaptureSessionsPath):
                _captureSessionService.UpdateRootPath(settings.CaptureSessionsPath);
                UpdateCaptureStorageInfo();
                UpdateCurrentModeAssetInfo();
                break;

            case nameof(AppSettings.UseSavedSessionForCalibration):
                _isApplyingSettings = true;
                UseSavedSessionForCalibration = settings.UseSavedSessionForCalibration;
                _isApplyingSettings = false;
                break;

            case nameof(AppSettings.ActiveAutoCalibrationPath):
            case nameof(AppSettings.ActiveStereoOnlyCalibrationPath):
            case nameof(AppSettings.ActiveSingleCameraCalibrationPath):
            case nameof(AppSettings.ActiveDepthMapCalibrationPath):
            case nameof(AppSettings.ActiveAutoSessionPath):
            case nameof(AppSettings.ActiveStereoOnlySessionPath):
            case nameof(AppSettings.ActiveSingleCameraSessionPath):
                ResetPreviewRectifier();
                UpdateCurrentModeAssetInfo();
                break;

            case nameof(AppSettings.PatternWidth):
            case nameof(AppSettings.PatternHeight):
            case nameof(AppSettings.BoardType):
            case nameof(AppSettings.CharucoDictionary):
            case nameof(AppSettings.SquareSize):
            case nameof(AppSettings.MarkerSizeRatio):
                ReinitializePipelineForPatternChange();
                break;
        }
    }

    private void UpdateCaptureStorageInfo()
    {
        CaptureStorageInfoText = $"{L("Capture sessions folder", "Папка сессий снимков")}: {_captureSessionService.SessionsRootPath}";
    }

    private void UpdateLocalizedUiTexts()
    {
        CalibrationTabText = L("Calibration", "Калибровка");
        BoardGeneratorTabText = L("Board Generator", "Генератор доски");
        ManagerTabText = L("Calibration Manager", "Менеджер калибровок");
        LogTabText = L("Log", "Лог");
        DepthMapTabText = L("Depth Map", "Карта глубины");

        UseSavedSessionForCalibrationText = L(
            "Use Saved Session For Calibration",
            "Использовать сохраненную сессию для калибровки");
        OpenSessionsFolderText = L("Open Sessions Folder", "Открыть папку сессий");

        SettingsTooltipText = L("Settings", "Настройки");
        AboutTooltipText = L("About", "О программе");
        CloseTooltipText = L("Close", "Закрыть");
        SettingsHeaderText = L("Settings", "Настройки");

        ModeLabelText = L("Mode:", "Режим:");
        AutoModeText = L("Auto (Full Calibration)", "Auto (полная калибровка)");
        StereoOnlyModeText = L("Stereo Only", "Только Stereo");
        SingleCameraModeText = L("Single Camera", "Одна камера");

        LeftCameraOverlayText = L("LEFT CAMERA", "ЛЕВАЯ КАМЕРА");
        RightCameraOverlayText = L("RIGHT CAMERA", "ПРАВАЯ КАМЕРА");
        CapturedLabelText = L("Captured:", "Захвачено:");
        ShowRectifiedText = L("Show Rectified", "Показывать ректификацию");
        ConnectCamerasText = L("Connect Cameras", "Подключить камеры");
        DisconnectText = L("Disconnect", "Отключить");
        CancelConnectionText = L("Cancel Connection", "Прервать подключение");
        DepthMapCameraStatusLabelText = L("Depth map camera status", "Статус камер карты глубины");
        DepthMapCameraConnectedText = L("Connected", "Подключено");
        DepthMapCameraPartiallyConnectedText = L("Partially connected", "Подключено частично");
        DepthMapCameraDisconnectedText = L("Disconnected", "Отключено");
        DepthMapCameraConnectingText = L("Connecting...", "Подключение...");
        DepthMapCalibrationLabelText = L("Depth calibration", "Калибровка глубины");
        DepthMapSharedCameraHintText = L(
            "Depth map uses the same camera stream as Calibration.",
            "Карта глубины использует тот же поток камер, что и «Калибровка».");
        DepthMapOpenCalibrationManagerText = L("Open Calibration Manager", "Открыть менеджер калибровок");
        CaptureFrameText = L("Capture Frame", "Захват кадра");
        CalibrateButtonText = L("Calibrate", "Калибровать");
        CalibratingText = L("Computing calibration...", "Выполняется калибровка...");

        LeftStatusLabelText = L("Left", "Левая");
        RightStatusLabelText = L("Right", "Правая");
        StatusDisconnectedText = L("Disconnected", "Отключено");
        StatusFpsUnitText = L("fps", "кадр/с");
        UpdateReconnectExtraInfo(isLeft: true);
        UpdateReconnectExtraInfo(isLeft: false);

        VideoNoSignalText = L("No Signal", "Нет сигнала");
        VideoConnectingText = L("Connecting...", "Подключение...");
        VideoErrorText = L("Error", "Ошибка");
        VideoHoldStillText = L("Hold Still...", "Не двигайте камеру...");
        VideoCapturedText = L("Captured!", "Захвачено!");

        WorkflowSetupText = L("Setup", "Настройка");
        WorkflowCaptureText = L("Capture", "Захват");
        WorkflowCalibrateText = L("Calibrate", "Калибровка");
        WorkflowSaveText = L("Save", "Сохранение");
        LogHeaderText = L("Log", "Лог");
        ClearLogText = L("Clear Log", "Очистить лог");
        LogEmptyText = L("No log entries", "Нет записей");

        if (_activeCaptureSession == null)
        {
            CaptureSessionInfoText = L("Capture session: not started.", "Сессия захвата: не начата.");
        }
        else
        {
            CaptureSessionInfoText = $"{L("Capture session", "Сессия захвата")}: {_activeCaptureSession.SessionId} | {L("Captures", "Кадры")}: {_activeCaptureSession.Captures.Count}";
        }

        UpdateCaptureStorageInfo();
        UpdateCalibrationStatus();
        UpdateDepthMapDependencyInfo();
        OnPropertyChanged(nameof(DepthMapCameraStatusText));
        OnPropertyChanged(nameof(DetectionToggleText));
        OnPropertyChanged(nameof(CanShowDetectionButton));
        OnPropertyChanged(nameof(CanToggleDetection));
        OnPropertyChanged(nameof(CalibrateActionText));
    }

    private string GetModeLabel(CalibrationMode mode)
    {
        return mode switch
        {
            CalibrationMode.Auto => L("Auto", "Auto"),
            CalibrationMode.StereoOnly => L("Stereo Only", "Только Stereo"),
            CalibrationMode.SingleCamera => L("Single Camera", "Одна камера"),
            _ => mode.ToString()
        };
    }

    private new string L(string en, string ru)
    {
        var lang = _settingsService.Settings.Language?.Trim().ToLowerInvariant();
        if (lang == "en")
        {
            return en;
        }

        if (RuLocalization.TryGetValue(en, out var localized))
        {
            return localized;
        }

        return ru;
    }

    private static string TryDecodeMojibake(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        if (!value.Contains("Р") && !value.Contains("С") && !value.Contains("Ð") && !value.Contains("Ñ"))
        {
            return value;
        }

        try
        {
            var decoded = Encoding.UTF8.GetString(Encoding.GetEncoding(1251).GetBytes(value));
            return string.IsNullOrWhiteSpace(decoded) ? value : decoded;
        }
        catch
        {
            return value;
        }
    }

    [RelayCommand]
    private void ClearLog()
    {
        LogEntries.Clear();
        HasLogEntries = false;
    }

    public void AppendLogLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        RunOnUiThread(() =>
        {
            LogEntries.Add(line);
            while (LogEntries.Count > MaxLogEntries)
            {
                LogEntries.RemoveAt(0);
            }

            HasLogEntries = true;
        });
    }

    private void OnExceptionLogLine(string line)
    {
        AppendLogLine(line);
    }

    public void Cleanup()
    {
        ExceptionLogger.OnLogLine -= OnExceptionLogLine;
        SettingsVm.PropertyChanged -= OnSettingsViewModelPropertyChanged;
        SettingsVm.Dispose();
        _reconnectStatusTimer.Tick -= OnReconnectStatusTimerTick;
        _reconnectStatusTimer.Stop();

        if (_activeSettings != null)
        {
            _activeSettings.PropertyChanged -= OnSettingsChanged;
            _activeSettings = null;
        }

        _synchronizer.OnStereoFrameReady -= OnStereoFrameReady;

        _captureCts?.Cancel();
        _captureCts?.Dispose();
        _captureCts = null;

        if (_leftReader != null)
        {
            _leftReader.OnFrameReady -= OnLeftFrameReady;
        }

        if (_rightReader != null)
        {
            _rightReader.OnFrameReady -= OnRightFrameReady;
        }

        _leftReader?.Dispose();
        _rightReader?.Dispose();
        _leftReader = null;
        _rightReader = null;

        if (_leftCamera != null)
        {
            _leftCamera.OnConnectionChanged -= OnLeftCameraConnectionChanged;
            _leftCamera.OnError -= OnLeftCameraError;
            _leftCamera.OnReconnectScheduleChanged -= OnLeftCameraReconnectScheduleChanged;
        }

        if (_rightCamera != null)
        {
            _rightCamera.OnConnectionChanged -= OnRightCameraConnectionChanged;
            _rightCamera.OnError -= OnRightCameraError;
            _rightCamera.OnReconnectScheduleChanged -= OnRightCameraReconnectScheduleChanged;
        }

        _leftCamera?.Dispose();
        _rightCamera?.Dispose();
        _leftCamera = null;
        _rightCamera = null;

        _matPool?.Dispose();
        _matPool = null;

        CalibrationManagerVm.CalibrationFilesChanged -= OnCalibrationFilesChanged;
        _synchronizer.Dispose();
        _previewRectifier.Dispose();
        _calibrationEngine?.Dispose();
        _autoCaptureService?.Dispose();
        _autoCaptureService = null;
        _currentCalibration?.Dispose();
        _previewCalibration?.Dispose();
        BoardGeneratorVm.Dispose();
        CalibrationManagerVm.Dispose();
        DepthMapVm.PropertyChanged -= OnDepthMapViewModelPropertyChanged;
        DepthMapVm.Dispose();
        _depthMapService.Dispose();

        foreach (var pair in _capturedPairsList)
        {
            pair.Dispose();
        }
        _capturedPairsList.Clear();

        _activeCaptureSession = null;
    }
}









