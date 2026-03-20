using System.Diagnostics;
using Emgu.CV;
using Emgu.CV.Cuda;
using LuminaCalib.Models;
using LuminaCalib.Services;

namespace LuminaCalib.Devices;

/// <summary>
/// Реализация источника камеры на основе EmguCV VideoCapture.
/// Поддерживает RTSP/HTTP-потоки с автоопределением аппаратного ускорения.
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><description>Автоматическое переподключение с экспоненциальным откатом (exponential backoff).</description></item>
///   <item><description>Поддержка HW-ускорения: NVDEC, VAAPI, DXVA2 (с фолбэком на CPU).</description></item>
///   <item><description>Оптимизация FFmpeg-опций для низкой задержки (low-delay, nobuffer).</description></item>
///   <item><description>Встраивание логина/пароля в URL для RTSP/HTTP.</description></item>
/// </list>
/// </remarks>
public sealed class EmguCameraSource : ICameraSource
{
    private const int DefaultNoFrameTimeoutMs = 1500;         // Тайм-аут отсутствия кадров (мс)
    private const int DefaultInitialReconnectDelayMs = 1000;   // Начальная задержка переподключения (мс)
    private const int DefaultMaxReconnectDelayMs = 10000;      // Максимальная задержка переподключения (мс)
    private const int DefaultConnectionTimeoutMs = 15000;       // Таймаут подключения (мс)

    private readonly string _url;                              // URL камеры (RTSP/HTTP/устройство)
    private readonly string _login;                            // Логин аутентификации
    private readonly string _password;                         // Пароль аутентификации
    private readonly CameraBackend _backend;                   // Бэкенд захвата (FFmpeg/GStreamer/Auto)
    private readonly HwAcceleration _hwAcceleration;           // Тип HW-ускорения
    private readonly TransportProtocol _transportProtocol;     // Транспортный протокол (TCP/UDP/Auto)
    private readonly int _noFrameTimeoutMs;                    // Тайм-аут отсутствия кадров
    private readonly int _initialReconnectDelayMs;             // Начальная задержка переподключения
    private readonly int _maxReconnectDelayMs;                 // Максимальная задержка переподключения
    private readonly int _connectionTimeoutMs;             // Таймаут подключения (0 = без таймаута)
    private readonly MatPool? _matPool;                     // Опциональный пул Mat-объектов

    private VideoCapture? _capture;                // Объект захвата EmguCV
    private CancellationTokenSource? _cts;          // Токен отмены
    private Task? _captureTask;                     // Задача цикла захвата
    private long _frameId;                          // Счётчик кадров
    private readonly Stopwatch _fpsStopwatch = new(); // Таймер для подсчёта FPS
    private int _frameCount;                        // Количество кадров за интервал
    private double _currentFps;                     // Текущая частота кадров
    private bool _isConnected;                      // Флаг подключения
    private bool _disposed;                         // Флаг удаления
    private DateTime? _publishedReconnectAttemptUtc; // Время следующей попытки переподключения

    /// <inheritdoc />
    public event Action<FrameRaw>? OnFrameReceived;
    /// <inheritdoc />
    public event Action<bool>? OnConnectionChanged;
    /// <inheritdoc />
    public event Action<string>? OnError;

    /// <summary>
    /// Событие изменения расписания переподключения.
    /// </summary>
    /// <remarks>Параметр <c>DateTime?</c> — время следующей попытки или <c>null</c> если не запланировано.</remarks>
    public event Action<DateTime?>? OnReconnectScheduleChanged;

    /// <inheritdoc />
    public string SourceId { get; }
    /// <inheritdoc />
    public bool IsConnected => _isConnected;
    /// <inheritdoc />
    public double CurrentFps => _currentFps;
    /// <inheritdoc />
    public int FrameWidth => _capture?.Width ?? 0;
    /// <inheritdoc />
    public int FrameHeight => _capture?.Height ?? 0;

    /// <summary>
    /// Создаёт новый экземпляр <see cref="EmguCameraSource"/>.
    /// </summary>
    /// <param name="sourceId">Уникальный идентификатор источника.</param>
    /// <param name="url">URL камеры (RTSP, HTTP или локальное устройство).</param>
    /// <param name="login">Логин для аутентификации (пустая строка по умолчанию).</param>
    /// <param name="password">Пароль для аутентификации.</param>
    /// <param name="backend">Бэкенд захвата (FFmpeg, GStreamer или Auto).</param>
    /// <param name="hwAcceleration">Тип аппаратного ускорения.</param>
    /// <param name="transportProtocol">Транспортный протокол (TCP/UDP/Auto).</param>
    /// <param name="noFrameTimeoutMs">Тайм-аут отсутствия кадров (мс). Минимум 250.</param>
    /// <param name="reconnectInitialDelayMs">Начальная задержка переподключения (мс).</param>
    /// <param name="reconnectMaxDelayMs">Максимальная задержка переподключения (мс).</param>
    /// <param name="matPool">Опциональный пул Mat-объектов для снижения нагрузки на GC при захвате кадров.</param>
    public EmguCameraSource(
        string sourceId,
        string url,
        string login = "",
        string password = "",
        CameraBackend backend = CameraBackend.Auto,
        HwAcceleration hwAcceleration = HwAcceleration.Auto,
        TransportProtocol transportProtocol = TransportProtocol.Auto,
        int noFrameTimeoutMs = DefaultNoFrameTimeoutMs,
        int reconnectInitialDelayMs = DefaultInitialReconnectDelayMs,
        int reconnectMaxDelayMs = DefaultMaxReconnectDelayMs,
        int connectionTimeoutMs = DefaultConnectionTimeoutMs,
        MatPool? matPool = null)
    {
        SourceId = sourceId;
        _url = url;
        _login = login;
        _password = password;
        _backend = backend;
        _hwAcceleration = hwAcceleration;
        _transportProtocol = transportProtocol;
        _noFrameTimeoutMs = Math.Max(250, noFrameTimeoutMs);
        _initialReconnectDelayMs = Math.Max(100, reconnectInitialDelayMs);
        _maxReconnectDelayMs = Math.Max(_initialReconnectDelayMs, reconnectMaxDelayMs);
        _connectionTimeoutMs = Math.Max(0, connectionTimeoutMs);
        _matPool = matPool;
    }

    /// <summary>
    /// Запускает захват видео с камеры.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <exception cref="ObjectDisposedException">Если объект уже удалён.</exception>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(EmguCameraSource));
        if (_capture != null) return; // Уже запущен

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        // Инициализируем захват в фоновом потоке с таймаутом
        var initTask = Task.Run(() => InitializeCapture(), _cts.Token);

        if (_connectionTimeoutMs > 0)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
            var timeoutTask = Task.Delay(_connectionTimeoutMs, timeoutCts.Token);
            var completed = await Task.WhenAny(initTask, timeoutTask);

            if (completed == timeoutTask && !initTask.IsCompleted)
            {
                // Таймаут подключения — отменяем и выбрасываем
                _cts.Cancel();
                OnError?.Invoke($"Connection timeout after {_connectionTimeoutMs}ms for camera {SourceId}");
                throw new OperationCanceledException(
                    $"Camera {SourceId}: connection timeout after {_connectionTimeoutMs}ms");
            }

            // Инициализация завершилась раньше таймаута — отменяем таймер
            timeoutCts.Cancel();
            await initTask; // Проброс возможных исключений
        }
        else
        {
            await initTask;
        }

        if (_capture == null || !_capture.IsOpened)
        {
            OnError?.Invoke($"Failed to open camera: {_url}. Reconnect loop started.");
        }
        else
        {
            SetConnectionState(true);
            if (!_fpsStopwatch.IsRunning)
            {
                _fpsStopwatch.Start();
            }
        }

        // Запускаем цикл захвата
        _captureTask = Task.Run(() => CaptureLoopAsync(_cts.Token), _cts.Token);
    }

    /// <summary>
    /// Останавливает захват видео и освобождает ресурсы захвата.
    /// </summary>
    public async Task StopAsync()
    {
        // Отменяем захват
        _cts?.Cancel();

        if (_captureTask != null)
        {
            try
            {
                await _captureTask;
            }
            catch (OperationCanceledException)
            {
                // Ожидаемое поведение при отмене
            }
        }

        // Освобождаем захват
        _capture?.Dispose();
        _capture = null;
        SetConnectionState(false);
        PublishReconnectSchedule(null);
    }

    /// <summary>
    /// Инициализирует объект <see cref="VideoCapture"/> с попыткой HW-ускорения.
    /// При неудаче откатывается на программное декодирование (CPU).
    /// </summary>
    private void InitializeCapture()
    {
        var fullUrl = BuildUrl();
        SetFFmpegOptions();

        try
        {
            // Сначала пробуем с аппаратным ускорением
            if (ShouldTryHwAcceleration())
            {
                Debug.WriteLine($"[{SourceId}] Attempting HW accelerated capture...");
                _capture = CreateCaptureWithHwAccel(fullUrl);
                
                if (_capture?.IsOpened == true)
                {
                    Debug.WriteLine($"[{SourceId}] HW accelerated capture successful");
                    return;
                }
                
                _capture?.Dispose();
            }

            // Откат на программный декодер (CPU)
            Debug.WriteLine($"[{SourceId}] Используется CPU-захват");
            _capture = new VideoCapture(fullUrl, GetVideoCaptureApi());
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[{SourceId}] Ошибка инициализации захвата: {ex.Message}");
            OnError?.Invoke(ex.Message);
        }
    }

    /// <summary>
    /// Создаёт <see cref="VideoCapture"/> с аппаратным ускорением.
    /// </summary>
    /// <param name="url">Полный URL с аутентификацией.</param>
    /// <returns>Объект захвата или <c>null</c> при ошибке.</returns>
    private VideoCapture? CreateCaptureWithHwAccel(string url)
    {
        try
        {
            var api = GetVideoCaptureApi();
            var capture = new VideoCapture(url, api);
            
            // Пытаемся установить аппаратное ускорение
            var accelType = GetVideoAccelerationType();
            if (accelType != Emgu.CV.CvEnum.VideoAccelerationType.None)
            {
                capture.Set(Emgu.CV.CvEnum.CapProp.HwAcceleration, (double)accelType);
            }
            
            return capture;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Определяет, нужно ли пытаться использовать HW-ускорение.
    /// Проверяет доступность CUDA для NVDEC/Auto.
    /// </summary>
    /// <returns><c>true</c>, если следует попробовать HW-ускорение.</returns>
    private bool ShouldTryHwAcceleration()
    {
        if (_hwAcceleration == HwAcceleration.None) return false;
        
        if (_hwAcceleration == HwAcceleration.Auto || _hwAcceleration == HwAcceleration.NVDEC)
        {
            try
            {
                return CudaInvoke.HasCuda;
            }
            catch
            {
                return false;
            }
        }

        return _hwAcceleration != HwAcceleration.None;
    }

    /// <summary>
    /// Преобразует настройку HW-ускорения в тип OpenCV.
    /// </summary>
    private Emgu.CV.CvEnum.VideoAccelerationType GetVideoAccelerationType()
    {
        return _hwAcceleration switch
        {
            HwAcceleration.Auto => Emgu.CV.CvEnum.VideoAccelerationType.Any,
            // Используем Any для всех типов — OpenCV сам выберет
            HwAcceleration.NVDEC => Emgu.CV.CvEnum.VideoAccelerationType.Any,
            HwAcceleration.VAAPI => Emgu.CV.CvEnum.VideoAccelerationType.Any,
            HwAcceleration.DXVA2 => Emgu.CV.CvEnum.VideoAccelerationType.Any,
            _ => Emgu.CV.CvEnum.VideoAccelerationType.None
        };
    }

    /// <summary>
    /// Преобразует настройку бэкенда в API VideoCapture.
    /// </summary>
    private VideoCapture.API GetVideoCaptureApi()
    {
        return _backend switch
        {
            CameraBackend.FFmpeg => VideoCapture.API.Ffmpeg,
            CameraBackend.GStreamer => VideoCapture.API.Gstreamer,
            _ => VideoCapture.API.Any
        };
    }

    /// <summary>
    /// Строит полный URL с аутентификацией.
    /// </summary>
    /// <returns>URL с внедрёнными логином и паролем (user:pass@host).</returns>
    private string BuildUrl()
    {
        if (string.IsNullOrEmpty(_login) || string.IsNullOrEmpty(_password))
        {
            return _url;
        }

        // Внедряем логин/пароль в URL
        // rtsp://ip:port/path -> rtsp://user:pass@ip:port/path
        if (_url.StartsWith("rtsp://", StringComparison.OrdinalIgnoreCase))
        {
            return _url.Insert(7, $"{Uri.EscapeDataString(_login)}:{Uri.EscapeDataString(_password)}@");
        }
        
        if (_url.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            return _url.Insert(7, $"{Uri.EscapeDataString(_login)}:{Uri.EscapeDataString(_password)}@");
        }

        return _url;
    }

    /// <summary>
    /// Устанавливает FFmpeg-опции для низкой задержки через переменную окружения.
    /// </summary>
    private void SetFFmpegOptions()
    {
        // Настройки оптимизации RTSP через переменную окружения
        var options = new List<string>();

        if (_transportProtocol == TransportProtocol.UDP)
        {
            options.Add("rtsp_transport;udp");
        }
        else if (_transportProtocol == TransportProtocol.TCP)
        {
            options.Add("rtsp_transport;tcp");
        }

        // Опции низкой задержки
        options.Add("analyzeduration;0");
        options.Add("fflags;nobuffer");
        options.Add("flags;low_delay");
        options.Add("max_delay;0");
        options.Add("probesize;32");

        Environment.SetEnvironmentVariable("OPENCV_FFMPEG_CAPTURE_OPTIONS", string.Join("|", options));
    }

    /// <summary>
    /// Основной цикл захвата кадров с автоматическим переподключением.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <remarks>
    /// При отсутствии кадров в течение <c>_noFrameTimeoutMs</c> запускается
    /// переподключение с экспоненциальным откатом (backoff).
    /// </remarks>
    private async Task CaptureLoopAsync(CancellationToken cancellationToken)
    {
        var frame = new Mat();
        var lastFrameUtc = DateTime.UtcNow;
        var noFrameReported = false;
        var reconnectDelayMs = _initialReconnectDelayMs;
        var nextReconnectAttemptUtc = DateTime.MinValue;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (_capture == null || !_capture.IsOpened)
                {
                    if (TryReconnectIfDue(ref nextReconnectAttemptUtc, ref reconnectDelayMs))
                    {
                        lastFrameUtc = DateTime.UtcNow;
                        noFrameReported = false;
                    }

                    await Task.Delay(25, cancellationToken);
                    continue;
                }

                var hasFrame = false;
                try
                {
                    hasFrame = _capture.Grab();
                    if (hasFrame)
                    {
                        _capture.Retrieve(frame);
                        hasFrame = !frame.IsEmpty;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[{SourceId}] Capture read error: {ex.Message}");
                    OnError?.Invoke($"Capture read error: {ex.Message}");
                    MarkDisconnected();
                    hasFrame = false;
                }

                if (hasFrame)
                {
                    if (!_isConnected)
                    {
                        SetConnectionState(true);
                    }

                    if (!_fpsStopwatch.IsRunning)
                    {
                        _fpsStopwatch.Start();
                    }

                    lastFrameUtc = DateTime.UtcNow;
                    noFrameReported = false;
                    reconnectDelayMs = _initialReconnectDelayMs;
                    nextReconnectAttemptUtc = DateTime.MinValue;
                    PublishReconnectSchedule(null);

                    var timestamp = DateTime.UtcNow;
                    var frameId = Interlocked.Increment(ref _frameId);

                    // Создаём копию кадра для события, т.к. Mat переиспользуется capture.
                    // Если доступен MatPool — арендуем буфер из пула (без malloc),
                    // иначе — Clone() (аллокация нового нативного буфера).
                    Mat cloned;
                    if (_matPool != null)
                    {
                        cloned = _matPool.Rent(
                            new System.Drawing.Size(frame.Width, frame.Height));
                        frame.CopyTo(cloned);
                    }
                    else
                    {
                        cloned = frame.Clone();
                    }

                    var frameRaw = new FrameRaw(cloned, timestamp, frameId, SourceId);
                    OnFrameReceived?.Invoke(frameRaw);

                    UpdateFps();
                    await Task.Delay(1, cancellationToken);
                    continue;
                }

                var noFrameDurationMs = (DateTime.UtcNow - lastFrameUtc).TotalMilliseconds;
                if (noFrameDurationMs >= _noFrameTimeoutMs)
                {
                    MarkDisconnected();

                    if (!noFrameReported)
                    {
                        noFrameReported = true;
                        OnError?.Invoke($"No frames received for {_noFrameTimeoutMs} ms.");
                    }

                    if (TryReconnectIfDue(ref nextReconnectAttemptUtc, ref reconnectDelayMs))
                    {
                        lastFrameUtc = DateTime.UtcNow;
                        noFrameReported = false;
                    }
                }

                // Кадров нет — уступаем процессор для предотвращения busy-loop
                await Task.Delay(5, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Ожидаемое поведение при отмене
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[{SourceId}] Ошибка захвата: {ex.Message}");
            OnError?.Invoke(ex.Message);
        }
        finally
        {
            frame.Dispose();
            SetConnectionState(false);
            PublishReconnectSchedule(null);
        }
    }

    /// <summary>
    /// Пытается выполнить переподключение, если подошло время.
    /// При неудаче удваивает задержку до <c>_maxReconnectDelayMs</c>.
    /// </summary>
    /// <param name="nextReconnectAttemptUtc">Время следующей попытки (ref).</param>
    /// <param name="reconnectDelayMs">Текущая задержка переподключения в мс (ref).</param>
    /// <returns><c>true</c>, если переподключение успешно.</returns>
    private bool TryReconnectIfDue(ref DateTime nextReconnectAttemptUtc, ref int reconnectDelayMs)
    {
        var nowUtc = DateTime.UtcNow;
        if (nowUtc < nextReconnectAttemptUtc)
        {
            return false;
        }

        OnError?.Invoke("Attempting camera reconnect...");
        var reconnected = TryReconnectCapture();
        if (reconnected)
        {
            reconnectDelayMs = _initialReconnectDelayMs;
            nextReconnectAttemptUtc = DateTime.MinValue;
            PublishReconnectSchedule(null);
            OnError?.Invoke("Camera stream reconnected.");
            return true;
        }

        OnError?.Invoke($"Reconnect failed. Next retry in {reconnectDelayMs} ms.");
        nextReconnectAttemptUtc = nowUtc.AddMilliseconds(reconnectDelayMs);
        PublishReconnectSchedule(nextReconnectAttemptUtc);
        reconnectDelayMs = Math.Min(reconnectDelayMs * 2, _maxReconnectDelayMs);
        return false;
    }

    /// <summary>
    /// Пытается пересоздать объект захвата.
    /// </summary>
    /// <returns><c>true</c>, если новый захват успешно открыт.</returns>
    private bool TryReconnectCapture()
    {
        try
        {
            _capture?.Dispose();
            _capture = null;
            SetConnectionState(false);

            InitializeCapture();
            return _capture?.IsOpened == true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[{SourceId}] Reconnect failed: {ex.Message}");
            OnError?.Invoke($"Reconnect failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Отмечает камеру как отключённую.
    /// </summary>
    private void MarkDisconnected()
    {
        if (_isConnected)
        {
            SetConnectionState(false);
        }
    }

    /// <summary>
    /// Публикует изменение расписания переподключения (дедупликация).
    /// </summary>
    /// <param name="nextAttemptUtc">Время следующей попытки или <c>null</c>.</param>
    private void PublishReconnectSchedule(DateTime? nextAttemptUtc)
    {
        if (_publishedReconnectAttemptUtc == nextAttemptUtc)
        {
            return;
        }

        _publishedReconnectAttemptUtc = nextAttemptUtc;
        OnReconnectScheduleChanged?.Invoke(nextAttemptUtc);
    }

    /// <summary>
    /// Обновляет текущую частоту кадров каждую секунду.
    /// </summary>
    private void UpdateFps()
    {
        _frameCount++;

        if (_fpsStopwatch.ElapsedMilliseconds >= 1000)
        {
            _currentFps = _frameCount * 1000.0 / _fpsStopwatch.ElapsedMilliseconds;
            _frameCount = 0;
            _fpsStopwatch.Restart();
        }
    }

    /// <summary>
    /// Устанавливает состояние подключения и уведомляет подписчиков.
    /// </summary>
    /// <param name="connected">Новое состояние подключения.</param>
    private void SetConnectionState(bool connected)
    {
        if (_isConnected != connected)
        {
            _isConnected = connected;
            OnConnectionChanged?.Invoke(connected);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _cts?.Cancel();
        _cts?.Dispose();
        _capture?.Dispose();
        PublishReconnectSchedule(null);
    }
}
