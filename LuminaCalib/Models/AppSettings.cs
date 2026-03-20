using CommunityToolkit.Mvvm.ComponentModel;

namespace LuminaCalib.Models;

/// <summary>
/// Модель настроек приложения LuminaCalib.
/// </summary>
public partial class AppSettings : ObservableObject
{
    // === Общие настройки ===
    
    /// <summary>
    /// Код языка интерфейса (например, "ru", "en").
    /// </summary>
    [ObservableProperty]
    private string _language = "ru";

    /// <summary>
    /// Автоматически сохранять результат калибровки после завершения.
    /// </summary>
    [ObservableProperty]
    private bool _autoSaveCalibration = true;

    /// <summary>
    /// Путь к директории для хранения данных калибровки.
    /// </summary>
    [ObservableProperty]
    private string _calibrationDataPath = "./CalibrationData";

    /// <summary>
    /// Путь к директории для хранения сессий захвата кадров.
    /// </summary>
    [ObservableProperty]
    private string _captureSessionsPath = "./CaptureSessions";

    // === Настройки левой камеры ===
    
    /// <summary>
    /// URL-адрес потока левой камеры (RTSP/HTTP).
    /// </summary>
    [ObservableProperty]
    private string _leftCameraUrl = "";

    /// <summary>
    /// Логин для подключения к левой камере.
    /// </summary>
    [ObservableProperty]
    private string _leftCameraLogin = "";

    /// <summary>
    /// Пароль для подключения к левой камере.
    /// </summary>
    [ObservableProperty]
    private string _leftCameraPassword = "";

    // === Настройки правой камеры ===
    
    /// <summary>
    /// URL-адрес потока правой камеры (RTSP/HTTP).
    /// </summary>
    [ObservableProperty]
    private string _rightCameraUrl = "";

    /// <summary>
    /// Логин для подключения к правой камере.
    /// </summary>
    [ObservableProperty]
    private string _rightCameraLogin = "";

    /// <summary>
    /// Пароль для подключения к правой камере.
    /// </summary>
    [ObservableProperty]
    private string _rightCameraPassword = "";

    // === Настройки калибровочной доски ===
    
    /// <summary>
    /// Тип калибровочной доски (шахматная или ChArUco).
    /// </summary>
    [ObservableProperty]
    private BoardType _boardType = BoardType.Chessboard;

    /// <summary>
    /// Ширина паттерна.
    /// Для шахматной доски — количество внутренних углов по горизонтали (OpenCV boardSize.width).
    /// Для ChArUco — количество клеток (квадратов) по горизонтали.
    /// </summary>
    [ObservableProperty]
    private int _patternWidth = 9;

    /// <summary>
    /// Высота паттерна.
    /// Для шахматной доски — количество внутренних углов по вертикали (OpenCV boardSize.height).
    /// Для ChArUco — количество клеток (квадратов) по вертикали.
    /// </summary>
    [ObservableProperty]
    private int _patternHeight = 6;

    /// <summary>
    /// Размер стороны квадрата в миллиметрах.
    /// </summary>
    [ObservableProperty]
    private float _squareSize = 25f; // мм

    /// <summary>
    /// Отношение размера маркера к размеру квадрата для ChArUco (0.0–1.0).
    /// </summary>
    [ObservableProperty]
    private float _markerSizeRatio = 0.73f; // Для ChArUco: размер маркера как доля от квадрата

    /// <summary>
    /// Словарь ArUco-маркеров для доски ChArUco.
    /// </summary>
    [ObservableProperty]
    private CharucoDictionary _charucoDictionary = CharucoDictionary.Dict6x6_250;

    // === Расширенные настройки камер ===
    
    /// <summary>
    /// Транспортный протокол для подключения к камерам (UDP/TCP/Auto).
    /// </summary>
    [ObservableProperty]
    private TransportProtocol _transportProtocol = TransportProtocol.Auto;

    /// <summary>
    /// Размер внутреннего буфера кадров камеры.
    /// </summary>
    [ObservableProperty]
    private int _bufferSize = 1;

    /// <summary>
    /// Бэкенд захвата видео (FFmpeg, GStreamer или Auto).
    /// </summary>
    [ObservableProperty]
    private CameraBackend _cameraBackend = CameraBackend.Auto;

    /// <summary>
    /// Режим аппаратного ускорения декодирования видео.
    /// </summary>
    [ObservableProperty]
    private HwAcceleration _hwAcceleration = HwAcceleration.Auto;

    /// <summary>
    /// Таймаут ожидания кадра от камеры (мс). По истечении — переподключение.
    /// </summary>
    [ObservableProperty]
    private int _cameraNoFrameTimeoutMs = 1500;

    /// <summary>
    /// Начальная задержка перед первой попыткой переподключения к камере (мс).
    /// </summary>
    [ObservableProperty]
    private int _cameraReconnectInitialDelayMs = 1000;

    /// <summary>
    /// Максимальная задержка между попытками переподключения к камере (мс).
    /// </summary>
    [ObservableProperty]
    private int _cameraReconnectMaxDelayMs = 10000;

    /// <summary>Таймаут подключения к камере (мс). 0 = без таймаута.</summary>
    [ObservableProperty] private int _cameraConnectionTimeoutMs = 15000;

    // === Настройки калибровки ===
    
    /// <summary>
    /// Режим калибровки (полная, только стерео или одиночная камера).
    /// </summary>
    [ObservableProperty]
    private CalibrationMode _calibrationMode = CalibrationMode.Auto;

    /// <summary>
    /// Необходимое количество кадров для проведения калибровки.
    /// </summary>
    [ObservableProperty]
    private int _requiredFrames = 20;

    /// <summary>
    /// Допустимое расхождение по времени между левым и правым кадрами (мс).
    /// </summary>
    [ObservableProperty]
    private double _syncToleranceMs = 50.0;

    /// <summary>
    /// Минимальное время стабильного положения доски для автозахвата (секунды).
    /// </summary>
    [ObservableProperty]
    private double _stabilityThresholdSeconds = 1.0;

    /// <summary>
    /// Минимальный интервал между попытками детекции паттерна (мс). 0 — без троттлинга.
    /// </summary>
    [ObservableProperty]
    private int _detectionIntervalMs = 75;

    /// <summary>
    /// Масштаб изображения для детекции паттерна (0.1–1.0). Меньше — быстрее, но менее точно.
    /// </summary>
    [ObservableProperty]
    private float _detectionScale = 0.5f;

    /// <summary>
    /// Использовать рациональную модель дисторсии (k4, k5, k6) для линз со значительной бочкообразной/подушкообразной дисторсией.
    /// </summary>
    [ObservableProperty]
    private bool _useRationalModel;

    // === Активные ресурсы ===

    /// <summary>
    /// Путь к активной калибровке в режиме «Авто» (полная калибровка).
    /// </summary>
    [ObservableProperty]
    private string _activeAutoCalibrationPath = "";

    /// <summary>
    /// Путь к активной калибровке в режиме «Только стерео».
    /// </summary>
    [ObservableProperty]
    private string _activeStereoOnlyCalibrationPath = "";

    /// <summary>
    /// Путь к активной калибровке в режиме «Одиночная камера».
    /// </summary>
    [ObservableProperty]
    private string _activeSingleCameraCalibrationPath = "";

    /// <summary>
    /// Путь к активной калибровке для карты глубины.
    /// </summary>
    [ObservableProperty]
    private string _activeDepthMapCalibrationPath = "";

    /// <summary>
    /// Путь к активной сессии захвата в режиме «Авто».
    /// </summary>
    [ObservableProperty]
    private string _activeAutoSessionPath = "";

    /// <summary>
    /// Путь к активной сессии захвата в режиме «Только стерео».
    /// </summary>
    [ObservableProperty]
    private string _activeStereoOnlySessionPath = "";

    /// <summary>
    /// Путь к активной сессии захвата в режиме «Одиночная камера».
    /// </summary>
    [ObservableProperty]
    private string _activeSingleCameraSessionPath = "";

    /// <summary>
    /// Использовать сохранённую сессию захвата для калибровки вместо живого потока.
    /// </summary>
    [ObservableProperty]
    private bool _useSavedSessionForCalibration;

    // === Настройки карты глубины ===

    /// <summary>
    /// Настройки вычисления карты глубины в реальном времени.
    /// </summary>
    [ObservableProperty]
    private DepthMapSettings _depthMapSettings = new();
}

/// <summary>
/// Тип калибровочной доски.
/// </summary>
public enum BoardType
{
    /// <summary>Классическая шахматная доска.</summary>
    Chessboard,

    /// <summary>Доска ChArUco (шахматная + ArUco-маркеры).</summary>
    ChArUco
}

/// <summary>
/// Словарь ArUco-маркеров для генерации доски ChArUco.
/// </summary>
public enum CharucoDictionary
{
    /// <summary>Словарь 4×4, 50 маркеров.</summary>
    Dict4x4_50,
    /// <summary>Словарь 4×4, 100 маркеров.</summary>
    Dict4x4_100,
    /// <summary>Словарь 4×4, 250 маркеров.</summary>
    Dict4x4_250,
    /// <summary>Словарь 4×4, 1000 маркеров.</summary>
    Dict4x4_1000,
    /// <summary>Словарь 5×5, 50 маркеров.</summary>
    Dict5x5_50,
    /// <summary>Словарь 5×5, 100 маркеров.</summary>
    Dict5x5_100,
    /// <summary>Словарь 5×5, 250 маркеров.</summary>
    Dict5x5_250,
    /// <summary>Словарь 5×5, 1000 маркеров.</summary>
    Dict5x5_1000,
    /// <summary>Словарь 6×6, 50 маркеров.</summary>
    Dict6x6_50,
    /// <summary>Словарь 6×6, 100 маркеров.</summary>
    Dict6x6_100,
    /// <summary>Словарь 6×6, 250 маркеров.</summary>
    Dict6x6_250,
    /// <summary>Словарь 6×6, 1000 маркеров.</summary>
    Dict6x6_1000,
    /// <summary>Словарь 7×7, 50 маркеров.</summary>
    Dict7x7_50,
    /// <summary>Словарь 7×7, 100 маркеров.</summary>
    Dict7x7_100,
    /// <summary>Словарь 7×7, 250 маркеров.</summary>
    Dict7x7_250,
    /// <summary>Словарь 7×7, 1000 маркеров.</summary>
    Dict7x7_1000
}

/// <summary>
/// Транспортный протокол для подключения к IP-камерам.
/// </summary>
public enum TransportProtocol
{
    /// <summary>Автоматический выбор протокола.</summary>
    Auto,
    /// <summary>Протокол UDP (низкая задержка, возможна потеря пакетов).</summary>
    UDP,
    /// <summary>Протокол TCP (надёжная доставка, возможна бо́льшая задержка).</summary>
    TCP
}

/// <summary>
/// Бэкенд для захвата видеопотока с камеры.
/// </summary>
public enum CameraBackend
{
    /// <summary>Автоматический выбор бэкенда.</summary>
    Auto,
    /// <summary>Использовать FFmpeg для захвата и декодирования.</summary>
    FFmpeg,
    /// <summary>Использовать GStreamer для захвата и декодирования.</summary>
    GStreamer
}

/// <summary>
/// Режим аппаратного ускорения декодирования видео.
/// </summary>
public enum HwAcceleration
{
    /// <summary>Автоматический выбор доступного ускорителя.</summary>
    Auto,
    /// <summary>Без аппаратного ускорения (программное декодирование).</summary>
    None,
    /// <summary>NVIDIA NVDEC (аппаратное декодирование на GPU NVIDIA).</summary>
    NVDEC,
    /// <summary>VA-API (аппаратное ускорение на Linux, Intel/AMD).</summary>
    VAAPI,
    /// <summary>Intel Quick Sync Video.</summary>
    QuickSync,
    /// <summary>Microsoft DXVA2 (аппаратное декодирование на Windows).</summary>
    DXVA2
}

/// <summary>
/// Режим калибровки камер.
/// </summary>
public enum CalibrationMode
{
    /// <summary>
    /// Полная калибровка: Этап 1 — вычисление внутренних параметров каждой камеры (параллельно),
    /// Этап 2 — стереокалибровка внешних параметров с фиксированными внутренними.
    /// </summary>
    Auto,

    /// <summary>
    /// Только стереокалибровка (внешние параметры) с использованием предзагруженных внутренних параметров.
    /// </summary>
    StereoOnly,

    /// <summary>
    /// Калибровка внутренних параметров только одной камеры.
    /// </summary>
    SingleCamera
}
