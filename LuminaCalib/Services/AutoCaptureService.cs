using System.Drawing;
using Emgu.CV;
using LuminaCalib.Calibration;
using LuminaCalib.Models;

namespace LuminaCalib.Services;

/// <summary>
/// Сервис автоматического захвата калибровочных кадров из стерео-потока.
/// </summary>
/// <remarks>
/// Анализирует поток стерео-кадров, обнаруживает калибровочные паттерны (шахматная доска / ChArUco)
/// и собирает валидные пары кадров с фильтрацией стабильности. Алгоритм стабильности: паттерн
/// должен непрерывно обнаруживаться в течение <see cref="StabilityThresholdMs"/> миллисекунд,
/// а между двумя последовательными захватами должно пройти не менее <see cref="MinCaptureIntervalMs"/> мс.
/// Метод <see cref="ProcessFrame"/> реализует механизм обратного давления (backpressure):
/// если предыдущий кадр ещё обрабатывается, новый кадр просто пропускается.
/// </remarks>
public sealed class AutoCaptureService : IDisposable
{
    /// <summary>Детектор углов калибровочного паттерна (для левого кадра или единственный).</summary>
    private readonly CornerDetector _cornerDetector;

    /// <summary>Второй детектор углов для правого кадра (параллельная детекция). Может быть null.</summary>
    private readonly CornerDetector? _cornerDetectorRight;

    /// <summary>Режим калибровки (моно / стерео).</summary>
    private readonly CalibrationMode _mode;

    /// <summary>Объект блокировки для защиты разделяемого состояния.</summary>
    private readonly object _lock = new();

    /// <summary>Время последнего успешного захвата пары.</summary>
    private DateTime _lastCaptureTime = DateTime.MinValue;

    /// <summary>Момент первого непрерывного обнаружения паттерна (для порога стабильности).</summary>
    private DateTime _patternFirstSeenTime = DateTime.MinValue;

    /// <summary>Флаг: паттерн обнаруживается непрерывно в текущей серии кадров.</summary>
    private bool _patternCurrentlyDetected;

    /// <summary>Время (Utc ticks) последней попытки детекции паттерна.</summary>
    private long _lastDetectionAttemptTicks;

    /// <summary>Флаг освобождения ресурсов.</summary>
    private bool _disposed;

    /// <summary>Атомарный флаг обратного давления: 1 — кадр обрабатывается, 0 — свободен.</summary>
    private int _isProcessing;

    // ───── Конфигурация ─────

    /// <summary>Минимальный интервал между двумя захватами (мс).</summary>
    public double MinCaptureIntervalMs { get; set; } = 2000;

    /// <summary>Время (мс), в течение которого паттерн должен обнаруживаться непрерывно перед захватом.</summary>
    public double StabilityThresholdMs { get; set; } = 1000;

    /// <summary>Максимальное количество захваченных пар кадров.</summary>
    public int MaxCaptures { get; set; } = 20;

    /// <summary>Минимальный интервал между попытками детекции (мс). 0 — без троттлинга.</summary>
    public double MinDetectionIntervalMs { get; set; }

    /// <summary>Масштаб изображения для детекции паттерна (0.1–1.0). Меньше — быстрее.</summary>
    public float DetectionScale { get; set; } = 1.0f;

    // ───── Состояние (только для чтения извне) ─────

    /// <summary>Количество уже захваченных пар.</summary>
    public int CapturedCount { get; private set; }

    /// <summary>Сервис запущен и обрабатывает кадры.</summary>
    public bool IsRunning { get; private set; }

    /// <summary>Паттерн обнаружен на последнем кадре левой камеры.</summary>
    public bool PatternDetectedLeft { get; private set; }

    /// <summary>Паттерн обнаружен на последнем кадре правой камеры.</summary>
    public bool PatternDetectedRight { get; private set; }

    /// <summary>
    /// Делегат-заглушка для тестируемости. Если задан, подменяет стандартное обнаружение углов.
    /// </summary>
    public Func<Mat, (bool found, PointF[]? corners)>? DetectOverride { get; set; }

    // ───── События ─────

    /// <summary>
    /// Вызывается при захвате валидной пары кадров после прохождения порога стабильности.
    /// </summary>
    /// <remarks>
    /// Параметры: (клонированная StereoFramePair, углы слева, углы справа).
    /// Потребитель отвечает за освобождение (Dispose) полученной пары.
    /// </remarks>
    public event Action<StereoFramePair, PointF[]?, PointF[]?>? OnValidPairCaptured;

    /// <summary>
    /// Вызывается на каждом обработанном кадре с результатами обнаружения для отображения оверлея.
    /// </summary>
    /// <remarks>
    /// Параметры: (найден_слева, найден_справа, углы_слева, углы_справа).
    /// </remarks>
    public event Action<bool, bool, PointF[]?, PointF[]?>? OnDetectionUpdate;

    /// <summary>
    /// Инициализирует сервис автозахвата с указанным детектором углов и режимом калибровки.
    /// </summary>
    /// <param name="cornerDetector">Детектор углов калибровочного паттерна.</param>
    /// <param name="mode">Режим калибровки (моно / стерео).</param>
    /// <param name="cornerDetectorRight">
    /// Опциональный второй детектор для параллельной детекции правого кадра.
    /// Если задан, левый и правый кадры обрабатываются параллельно.
    /// </param>
    public AutoCaptureService(CornerDetector cornerDetector, CalibrationMode mode, CornerDetector? cornerDetectorRight = null)
    {
        _cornerDetector = cornerDetector ?? throw new ArgumentNullException(nameof(cornerDetector));
        _cornerDetectorRight = cornerDetectorRight;
        _mode = mode;
    }

    /// <summary>
    /// Запускает автозахват. После вызова <see cref="ProcessFrame"/> начнёт обрабатывать кадры.
    /// </summary>
    public void Start()
    {
        lock (_lock)
        {
            IsRunning = true;
        }
    }

    /// <summary>
    /// Останавливает автозахват. Кадры, переданные в <see cref="ProcessFrame"/>, будут игнорироваться.
    /// </summary>
    public void Stop()
    {
        lock (_lock)
        {
            IsRunning = false;
        }
    }

    /// <summary>
    /// Сбрасывает состояние автозахвата: счётчик, таймеры и флаги обнаружения.
    /// </summary>
    public void Reset()
    {
        lock (_lock)
        {
            // Обнуляем счётчик захваченных пар
            CapturedCount = 0;
            // Сбрасываем таймеры
            _lastCaptureTime = DateTime.MinValue;
            _patternFirstSeenTime = DateTime.MinValue;
            _patternCurrentlyDetected = false;
            // Сбрасываем флаги обнаружения
            PatternDetectedLeft = false;
            PatternDetectedRight = false;
        }

        Interlocked.Exchange(ref _lastDetectionAttemptTicks, 0);
    }

    /// <summary>
    /// Обрабатывает стерео-пару кадров: обнаружение паттерна и автоматический захват.
    /// </summary>
    /// <param name="pair">
    /// Стерео-пара. Вызывающий код сохраняет владение; метод клонирует пару при захвате.
    /// </param>
    /// <remarks>
    /// Реализует механизм обратного давления (backpressure): если предыдущий кадр ещё
    /// обрабатывается, текущий кадр будет пропущен без блокировки вызывающего потока.
    /// </remarks>
    public void ProcessFrame(StereoFramePair pair)
    {
        if (pair == null) return;

        var minDetectionIntervalMs = MinDetectionIntervalMs;
        if (minDetectionIntervalMs > 0)
        {
            var minIntervalTicks = (long)(minDetectionIntervalMs * TimeSpan.TicksPerMillisecond);
            var nowTicks = DateTime.UtcNow.Ticks;
            var previousTicks = Interlocked.Read(ref _lastDetectionAttemptTicks);
            if (nowTicks - previousTicks < minIntervalTicks)
            {
                return;
            }

            Interlocked.Exchange(ref _lastDetectionAttemptTicks, nowTicks);
        }

        // Обратное давление: пропускаем кадр, если предыдущий ещё обрабатывается
        if (Interlocked.CompareExchange(ref _isProcessing, 1, 0) != 0)
            return;

        StereoFramePair? ownedPair = null;
        try
        {
            // Фоновая обработка должна владеть собственной копией кадра
            ownedPair = new StereoFramePair(pair.Left.Clone(), pair.Right.Clone());
        }
        catch
        {
            Interlocked.Exchange(ref _isProcessing, 0);
            return;
        }

        _ = Task.Run(() =>
        {
            bool ownershipTransferred = false;
            try
            {
                ownershipTransferred = ProcessFrameCore(ownedPair);
            }
            finally
            {
                // Если владение передано подписчику OnValidPairCaptured,
                // не освобождаем пару — получатель отвечает за Dispose.
                if (!ownershipTransferred)
                    ownedPair.Dispose();
                // Освобождаем флаг обработки
                Interlocked.Exchange(ref _isProcessing, 0);
            }
        });
    }

    /// <summary>
    /// Основная логика обработки кадра: обнаружение углов, проверка стабильности, захват.
    /// </summary>
    /// <param name="pair">Стерео-пара для обработки.</param>
    /// <returns><c>true</c>, если владение парой передано подписчику события захвата.</returns>
    private bool ProcessFrameCore(StereoFramePair pair)
    {
        // Быстрая проверка под блокировкой: запущен ли сервис и не достигнут ли лимит
        lock (_lock)
        {
            if (!IsRunning || _disposed) return false;
            if (MaxCaptures > 0 && CapturedCount >= MaxCaptures) return false;
        }

        // Обнаружение углов для обеих камер (вне блокировки — это медленная часть).
        // При наличии второго детектора используется параллельная обработка L/R.
        bool leftFound = false;
        PointF[]? leftCorners = null;
        bool rightFound = false;
        PointF[]? rightCorners = null;

        if (DetectOverride != null)
        {
            // Используем тестовую заглушку вместо реального детектора.
            // Ошибки не должны блокировать цикл автозахвата.
            try
            {
                (leftFound, leftCorners) = DetectOverride(pair.Left.Image);
            }
            catch (Exception ex)
            {
                leftFound = false;
                leftCorners = null;
                ExceptionLogger.LogException(ex, "AutoCapture.DetectOverride.Left");
            }

            try
            {
                (rightFound, rightCorners) = DetectOverride(pair.Right.Image);
            }
            catch (Exception ex)
            {
                rightFound = false;
                rightCorners = null;
                ExceptionLogger.LogException(ex, "AutoCapture.DetectOverride.Right");
            }
        }
        else if (_cornerDetectorRight != null)
        {
            // Параллельная детекция: два независимых экземпляра CornerDetector — без shared state.
            var scale = DetectionScale;
            Parallel.Invoke(
                () =>
                {
                    try
                    {
                        leftFound = _cornerDetector.DetectCorners(pair.Left.Image, out var lc, out _, scale);
                        leftCorners = lc;
                    }
                    catch (Exception ex)
                    {
                        leftFound = false;
                        leftCorners = null;
                        ExceptionLogger.LogException(ex, "AutoCapture.DetectCorners.Left");
                    }
                },
                () =>
                {
                    try
                    {
                        rightFound = _cornerDetectorRight.DetectCorners(pair.Right.Image, out var rc, out _, scale);
                        rightCorners = rc;
                    }
                    catch (Exception ex)
                    {
                        rightFound = false;
                        rightCorners = null;
                        ExceptionLogger.LogException(ex, "AutoCapture.DetectCorners.Right");
                    }
                });
        }
        else
        {
            // Последовательная детекция (один детектор — fallback).
            try
            {
                leftFound = _cornerDetector.DetectCorners(pair.Left.Image, out var lc, out _, DetectionScale);
                leftCorners = lc;
            }
            catch (Exception ex)
            {
                leftFound = false;
                leftCorners = null;
                ExceptionLogger.LogException(ex, "AutoCapture.DetectCorners.Left");
            }

            try
            {
                rightFound = _cornerDetector.DetectCorners(pair.Right.Image, out var rc, out _, DetectionScale);
                rightCorners = rc;
            }
            catch (Exception ex)
            {
                rightFound = false;
                rightCorners = null;
                ExceptionLogger.LogException(ex, "AutoCapture.DetectCorners.Right");
            }
        }

        // Отправляем событие обновления детекции для оверлея (вне блокировки)
        OnDetectionUpdate?.Invoke(leftFound, rightFound,
            leftFound ? leftCorners : null,
            rightFound ? rightCorners : null);

        // Определяем, считается ли паттерн «найденным» в зависимости от режима
        bool patternFound = _mode == CalibrationMode.SingleCamera
            ? (leftFound || rightFound)    // В моно-режиме достаточно одной камеры
            : (leftFound && rightFound);   // В стерео-режиме нужны обе

        StereoFramePair? capturedPair = null;
        PointF[]? capturedLeftCorners = null;
        PointF[]? capturedRightCorners = null;

        lock (_lock)
        {
            // Обновляем флаги обнаружения для внешних наблюдателей
            PatternDetectedLeft = leftFound;
            PatternDetectedRight = rightFound;

            var now = DateTime.UtcNow;

            if (patternFound)
            {
                if (!_patternCurrentlyDetected)
                {
                    // Паттерн появился — фиксируем момент первого обнаружения
                    _patternFirstSeenTime = now;
                    _patternCurrentlyDetected = true;
                }

                // Вычисляем длительность непрерывного обнаружения и время с последнего захвата
                var stableMs = (now - _patternFirstSeenTime).TotalMilliseconds;
                var sinceLast = (now - _lastCaptureTime).TotalMilliseconds;

                // Захват допускается, если паттерн стабилен и прошёл минимальный интервал
                if (stableMs >= StabilityThresholdMs && sinceLast >= MinCaptureIntervalMs)
                {
                    CapturedCount++;
                    _lastCaptureTime = now;
                    // Сбрасываем детектор стабильности для следующего цикла
                    _patternCurrentlyDetected = false;
                    _patternFirstSeenTime = DateTime.MinValue;

                    // Передаём владение ownedPair подписчику вместо клонирования.
                    // Вызывающий код (Task.Run) не будет вызывать Dispose благодаря флагу.
                    capturedPair = pair;
                    capturedLeftCorners = leftFound ? leftCorners : null;
                    capturedRightCorners = rightFound ? rightCorners : null;
                }
            }
            else
            {
                // Паттерн пропал — сбрасываем таймер стабильности
                _patternCurrentlyDetected = false;
                _patternFirstSeenTime = DateTime.MinValue;
            }
        }

        // Вызываем событие захвата вне блокировки, чтобы не держать lock
        if (capturedPair != null)
        {
            OnValidPairCaptured?.Invoke(capturedPair, capturedLeftCorners, capturedRightCorners);
            return true; // Владение передано подписчику
        }

        return false;
    }

    /// <summary>
    /// Освобождает ресурсы сервиса и останавливает обработку кадров.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        // Останавливаем обработку при освобождении
        IsRunning = false;
    }
}
