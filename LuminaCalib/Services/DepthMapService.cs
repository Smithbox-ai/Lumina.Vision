using System.Diagnostics;
using Emgu.CV;
using LuminaCalib.Calibration;
using LuminaCalib.Models;

namespace LuminaCalib.Services;

/// <summary>
/// Сервис реального времени для построения карты глубины из стерео-потока.
/// </summary>
/// <remarks>
/// Принимает синхронизированные стерео-пары, выполняет конвейер
/// (ректификация → вычисление диспаратности → раскраска) и уведомляет подписчиков.
/// Реализует механизм обратного давления (backpressure): если предыдущий кадр ещё
/// обрабатывается, новый кадр пропускается без блокировки.
/// </remarks>
public sealed class DepthMapService : IDisposable
{
    private sealed class WorkerState
    {
        public required DepthMapService Service { get; init; }
        public required StereoFramePair Pair { get; init; }
    }

    private readonly StereoRectifier _rectifier;
    private readonly DepthMapSettings _settings;

    /// <summary>Атомарный флаг обратного давления: 1 — обрабатывается, 0 — свободен.</summary>
    private int _isProcessing;

    /// <summary>Флаг освобождения ресурсов.</summary>
    private bool _disposed;

    /// <summary>Количество фоновых обработчиков, находящихся в полёте.</summary>
    private int _inFlightWorkers;

    /// <summary>Сигнал отсутствия фоновых обработчиков (для безопасного Dispose).</summary>
    private readonly ManualResetEventSlim _noInFlightWorkers = new(true);

    /// <summary>Блокировка для безопасного клонирования снимков из другого потока.</summary>
    private readonly object _snapshotLock = new();

    /// <summary>Последняя раскрашенная карта глубины (владелец — сервис).</summary>
    private Mat? _latestColorized;

    /// <summary>Последняя карта диспаратности (CV_16S, владелец — сервис).</summary>
    private Mat? _latestDisparity;

    /// <summary>Матрица Q для перепроекции диспаратности в 3D (4×4).</summary>
    private Mat? _qMatrix;

    /// <summary>Последнее ректифицированное левое изображение (для PLY-экспорта).</summary>
    private Mat? _latestRectifiedLeft;

    /// <summary>Последнее ректифицированное правое изображение (для full-res экспорта).</summary>
    private Mat? _latestRectifiedRight;

    /// <summary>Счётчик пропуска кадров для троттлинга.</summary>
    private int _frameSkipCounter;

    // ───── Pre-allocated Mats (избежание аллокаций в steady state) ─────

    /// <summary>Предваллоцированный буфер для ректифицированного левого.</summary>
    private Mat _preRectLeft = new();

    /// <summary>Предваллоцированный буфер для ректифицированного правого.</summary>
    private Mat _preRectRight = new();

    // ───── Конфигурация ─────

    /// <summary>
    /// Включён ли сервис. Если <c>false</c>, вызовы <see cref="ProcessStereoFrame"/> игнорируются.
    /// </summary>
    public bool IsEnabled { get; set; }

    /// <summary>
    /// Обрабатывать каждый N-й кадр (1 = каждый, 2 = через один и т.д.).
    /// </summary>
    public int ProcessEveryNthFrame { get; set; } = 1;

    /// <summary>
    /// Делегат-заглушка для тестируемости: подменяет реальный конвейер ректификация → диспаратность → раскраска.
    /// </summary>
    /// <remarks>
    /// Принимает (leftClone, rightClone) и возвращает (disparity, colorized).
    /// Вызывающий код (сервис) владеет возвращёнными Mat и освобождает disparity.
    /// </remarks>
    internal Func<Mat, Mat, (Mat disparity, Mat colorized)>? ProcessOverride { get; set; }

    /// <summary>
    /// Тестовый хелпер: инъекция «ректифицированных» изображений для <see cref="ComputeFullResolutionDisparity"/>.
    /// </summary>
    internal void InjectRectifiedImages(Mat left, Mat right)
    {
        lock (_snapshotLock)
        {
            Interlocked.Exchange(ref _latestRectifiedLeft, left.Clone())?.Dispose();
            Interlocked.Exchange(ref _latestRectifiedRight, right.Clone())?.Dispose();
        }
    }

    /// <summary>Тестовый хелпер: инъекция Q-матрицы.</summary>
    internal void InjectQMatrix(Mat q)
    {
        Interlocked.Exchange(ref _qMatrix, q.Clone())?.Dispose();
    }

    // ───── События ─────

    /// <summary>
    /// Вызывается при готовности раскрашенной карты глубины (CV_8UC3).
    /// </summary>
    /// <remarks>
    /// Потребитель НЕ должен вызывать Dispose() — сервис владеет временем жизни Mat.
    /// </remarks>
    public event Action<Mat>? OnDepthMapReady;

    /// <summary>
    /// Вызывается с длительностью обработки кадра в миллисекундах.
    /// </summary>
    public event Action<double>? OnProcessingTimeMs;

    // ───── Конструктор ─────

    /// <summary>
    /// Инициализирует сервис карты глубины.
    /// </summary>
    /// <param name="rectifier">Ректификатор стерео-пары (должен быть инициализирован калибровкой).</param>
    /// <param name="settings">Параметры вычисления диспаратности и раскраски.</param>
    public DepthMapService(StereoRectifier rectifier, DepthMapSettings settings)
    {
        _rectifier = rectifier ?? throw new ArgumentNullException(nameof(rectifier));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    /// <summary>
    /// Загружает калибровочные данные: сохраняет Q-матрицу и инициализирует ректификатор.
    /// </summary>
    /// <param name="calibration">Результат калибровки со стерео-параметрами.</param>
    public void LoadCalibration(CalibrationResult calibration)
    {
        ArgumentNullException.ThrowIfNull(calibration);
        Interlocked.Exchange(ref _qMatrix, calibration.Q.Clone())?.Dispose();
        _rectifier.Initialize(calibration);
    }

    // ───── Основной вход ─────

    /// <summary>
    /// Обрабатывает стерео-пару: ректификация, вычисление диспаратности, раскраска.
    /// </summary>
    /// <param name="pair">
    /// Стерео-пара. Вызывающий код сохраняет владение; метод клонирует изображения.
    /// </param>
    /// <remarks>
    /// Реализует обратное давление: если предыдущий кадр ещё обрабатывается,
    /// текущий кадр пропускается без блокировки.
    /// </remarks>
    public void ProcessStereoFrame(StereoFramePair pair)
    {
        if (pair == null) return;
        if (!IsEnabled || _disposed) return;

        // Троттлинг: обрабатываем каждый N-й кадр
        int counter = Interlocked.Increment(ref _frameSkipCounter);
        if (counter % ProcessEveryNthFrame != 0)
            return;

        // Обратное давление: пропускаем кадр, если предыдущий ещё обрабатывается
        if (Interlocked.CompareExchange(ref _isProcessing, 1, 0) != 0)
            return;

        StereoFramePair ownedPair;
        try
        {
            ownedPair = new StereoFramePair(pair.Left.Clone(), pair.Right.Clone());
        }
        catch
        {
            Interlocked.Exchange(ref _isProcessing, 0);
            return;
        }

        _noInFlightWorkers.Reset();
        Interlocked.Increment(ref _inFlightWorkers);

        try
        {
            var state = new WorkerState { Service = this, Pair = ownedPair };
            _ = Task.Factory.StartNew(
                static s =>
                {
                    var ws = (WorkerState)s!;
                    ws.Service.RunBackgroundWorker(ws.Pair);
                },
                state,
                CancellationToken.None,
                TaskCreationOptions.DenyChildAttach,
                TaskScheduler.Default);
        }
        catch
        {
            ownedPair.Dispose();
            Interlocked.Exchange(ref _isProcessing, 0);
            if (Interlocked.Decrement(ref _inFlightWorkers) == 0)
                _noInFlightWorkers.Set();
            throw;
        }
    }

    private void RunBackgroundWorker(StereoFramePair ownedPair)
    {
        try
        {
            ProcessFrameCore(ownedPair);
        }
        catch (Exception ex)
        {
            ExceptionLogger.LogException(ex, "DepthMapService.ProcessStereoFrame.BackgroundWorker");
        }
        finally
        {
            ownedPair.Dispose();
            Interlocked.Exchange(ref _isProcessing, 0);
            if (Interlocked.Decrement(ref _inFlightWorkers) == 0)
                _noInFlightWorkers.Set();
        }
    }

    // ───── Ядро конвейера ─────

    /// <summary>
    /// Внутренняя обработка кадра: ректификация → диспаратность → раскраска → события.
    /// </summary>
    private void ProcessFrameCore(StereoFramePair pair)
    {
        // Проверяем инициализацию ректификатора (нет калибровки → пропускаем)
        if (ProcessOverride == null && !_rectifier.IsInitialized)
            return;

        var sw = Stopwatch.StartNew();

        Mat colorized;

        if (ProcessOverride != null)
        {
            // Тестовая заглушка
            var (disparity, col) = ProcessOverride(pair.Left.Image, pair.Right.Image);
            lock (_snapshotLock)
                Interlocked.Exchange(ref _latestDisparity, disparity)?.Dispose();
            colorized = col;
        }
        else
        {
            // Реальный конвейер
            _rectifier.Rectify(pair.Left.Image, pair.Right.Image, _preRectLeft, _preRectRight);

            // Попытка CUDA → fallback к CPU
            var disparity = TryComputeDisparityWithCudaFallback(_preRectLeft, _preRectRight);
            colorized = DepthMapProcessor.ColorizeDisparity(disparity, _settings.Colormap);

            // Сохраняем снимки под блокировкой (защита от use-after-dispose при клонировании)
            lock (_snapshotLock)
            {
                Interlocked.Exchange(ref _latestRectifiedLeft, _preRectLeft.Clone())?.Dispose();
                Interlocked.Exchange(ref _latestRectifiedRight, _preRectRight.Clone())?.Dispose();
                Interlocked.Exchange(ref _latestDisparity, disparity)?.Dispose();
            }
        }

        // Заменяем _latestColorized, освобождая предыдущую
        lock (_snapshotLock)
            Interlocked.Exchange(ref _latestColorized, colorized)?.Dispose();

        sw.Stop();

        // Уведомляем подписчиков (вне блокировок)
        OnDepthMapReady?.Invoke(colorized);
        OnProcessingTimeMs?.Invoke(sw.Elapsed.TotalMilliseconds);
    }

    /// <summary>
    /// Пытается вычислить карту диспаратности через CUDA, при неудаче возвращает CPU-результат.
    /// </summary>
    private Mat TryComputeDisparityWithCudaFallback(Mat rectLeft, Mat rectRight)
    {
        // CUDA path only when WLS is disabled (WLS requires left matcher from CPU StereoSGBM)
        if (_settings.UseCuda && !_settings.UseWlsFilter && DepthMapProcessor.IsCudaAvailable)
        {
            var cudaResult = DepthMapProcessor.ComputeDisparityCuda(rectLeft, rectRight, _settings);
            if (!cudaResult.IsEmpty)
                return cudaResult;
            cudaResult.Dispose();
            ExceptionLogger.LogMessage("CUDA returned empty disparity, falling back to CPU.", "DepthMapService.CudaFallback");
        }

        return DepthMapProcessor.ComputeDisparity(rectLeft, rectRight, _settings);
    }

    // ───── Snapshot / Export ─────

    /// <summary>Потокобезопасный клон последней раскрашенной карты. Вызывающий обязан вызвать Dispose().</summary>
    public Mat? GetLatestColorizedSnapshot()
    {
        lock (_snapshotLock)
        {
            var mat = _latestColorized;
            return mat != null && !mat.IsEmpty ? mat.Clone() : null;
        }
    }

    /// <summary>Потокобезопасный клон последней карты диспаратности. Вызывающий обязан вызвать Dispose().</summary>
    public Mat? GetLatestDisparitySnapshot()
    {
        lock (_snapshotLock)
        {
            var mat = _latestDisparity;
            return mat != null && !mat.IsEmpty ? mat.Clone() : null;
        }
    }

    /// <summary>Потокобезопасный клон последнего ректифицированного левого изображения. Вызывающий обязан вызвать Dispose().</summary>
    public Mat? GetLatestRectifiedLeftSnapshot()
    {
        lock (_snapshotLock)
        {
            var mat = _latestRectifiedLeft;
            return mat != null && !mat.IsEmpty ? mat.Clone() : null;
        }
    }

    /// <summary>Клон Q-матрицы. Вызывающий обязан вызвать Dispose().</summary>
    public Mat? GetQMatrix()
    {
        lock (_snapshotLock)
        {
            var mat = _qMatrix;
            return mat != null && !mat.IsEmpty ? mat.Clone() : null;
        }
    }

    // ───── Full-Resolution Export ─────

    /// <summary>
    /// Вычисляет карту диспаратности в полном разрешении из последних ректифицированных изображений.
    /// Используется для экспорта (PLY/TIFF) — всегда DisplayScale=1.0.
    /// </summary>
    /// <returns>Карта диспаратности (CV_16S) или <c>null</c> если данные недоступны. Вызывающий обязан вызвать Dispose().</returns>
    public Mat? ComputeFullResolutionDisparity()
    {
        Mat rectLeftClone;
        Mat rectRightClone;

        lock (_snapshotLock)
        {
            var rl = _latestRectifiedLeft;
            var rr = _latestRectifiedRight;
            if (rl == null || rl.IsEmpty || rr == null || rr.IsEmpty)
                return null;
            rectLeftClone = rl.Clone();
            rectRightClone = rr.Clone();
        }

        try
        {
            if (ProcessOverride != null)
            {
                var (disparity, colorized) = ProcessOverride(rectLeftClone, rectRightClone);
                colorized.Dispose();
                return disparity;
            }

            var exportSettings = CloneSettingsForExport(_settings);
            return DepthMapProcessor.ComputeDisparity(rectLeftClone, rectRightClone, exportSettings);
        }
        finally
        {
            rectLeftClone.Dispose();
            rectRightClone.Dispose();
        }
    }

    /// <summary>Creates a settings copy with DisplayScale=1.0 for full-resolution export.</summary>
    internal static DepthMapSettings CloneSettingsForExport(DepthMapSettings source)
    {
        return new DepthMapSettings
        {
            MinDisparity = source.MinDisparity,
            NumDisparities = source.NumDisparities,
            BlockSize = source.BlockSize,
            P1 = source.P1,
            P2 = source.P2,
            Disp12MaxDiff = source.Disp12MaxDiff,
            PreFilterCap = source.PreFilterCap,
            UniquenessRatio = source.UniquenessRatio,
            SpeckleWindowSize = source.SpeckleWindowSize,
            SpeckleRange = source.SpeckleRange,
            SgbmMode = source.SgbmMode,
            UseWlsFilter = source.UseWlsFilter,
            WlsLambda = source.WlsLambda,
            WlsSigmaColor = source.WlsSigmaColor,
            UseMorphologicalClosing = source.UseMorphologicalClosing,
            MorphKernelSize = source.MorphKernelSize,
            Colormap = source.Colormap,
            DisplayScale = 1.0,
            UseCuda = false
        };
    }

    // ───── Dispose ─────

    /// <summary>
    /// Освобождает ресурсы сервиса.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // Дожидаемся завершения фоновой обработки, чтобы избежать гонки dispose/use.
        _noInFlightWorkers.Wait();

        // Освобождаем последнюю раскрашенную карту и связанные ресурсы
        Interlocked.Exchange(ref _latestColorized, null)?.Dispose();
        Interlocked.Exchange(ref _latestDisparity, null)?.Dispose();
        Interlocked.Exchange(ref _qMatrix, null)?.Dispose();
        Interlocked.Exchange(ref _latestRectifiedLeft, null)?.Dispose();
        Interlocked.Exchange(ref _latestRectifiedRight, null)?.Dispose();
        _preRectLeft.Dispose();
        _preRectRight.Dispose();
        _noInFlightWorkers.Dispose();
    }
}
