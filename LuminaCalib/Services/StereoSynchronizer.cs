using LuminaCalib.Models;

namespace LuminaCalib.Services;

/// <summary>
/// Синхронизатор кадров левой и правой камер по временным меткам.
/// </summary>
/// <remarks>
/// Реализует <b>двунаправленный алгоритм наилучшего соответствия</b>:
/// <list type="number">
/// <item><description>Направление 1: последний левый кадр → ближайший правый по времени.</description></item>
/// <item><description>Направление 2: последний правый кадр → ближайший левый по времени.</description></item>
/// <item><description>Выбирается пара с наименьшей разницей во времени.</description></item>
/// </list>
/// Пара считается синхронизированной, если разница временных меток
/// не превышает <see cref="ToleranceMs"/> миллисекунд.
/// </remarks>
public sealed class StereoSynchronizer : IDisposable
{
    /// <summary>Кольцевой буфер кадров левой камеры.</summary>
    private readonly RingBuffer<FrameRaw> _leftBuffer;

    /// <summary>Кольцевой буфер кадров правой камеры.</summary>
    private readonly RingBuffer<FrameRaw> _rightBuffer;

    /// <summary>Допуск синхронизации (мс).</summary>
    private double _toleranceMs;

    /// <summary>Объект блокировки для потокобезопасного доступа к буферам.</summary>
    private readonly object _syncLock = new();

    /// <summary>Флаг освобождения ресурсов.</summary>
    private bool _disposed;

    /// <summary>
    /// Вызывается при появлении синхронизированной пары кадров.
    /// </summary>
    public event Action<StereoFramePair>? OnStereoFrameReady;

    /// <summary>
    /// Создаёт синхронизатор стерео-кадров.
    /// </summary>
    /// <param name="bufferCapacity">Размер кольцевого буфера для каждой камеры (по умолчанию ~2 секунды при 30 fps = 60 кадров).</param>
    /// <param name="toleranceMs">Максимальная допустимая разница временных меток (по умолчанию 20 мс).</param>
    public StereoSynchronizer(int bufferCapacity = 60, double toleranceMs = 20.0)
    {
        _leftBuffer = new RingBuffer<FrameRaw>(bufferCapacity);
        _rightBuffer = new RingBuffer<FrameRaw>(bufferCapacity);
        _toleranceMs = toleranceMs;
    }

    /// <summary>
    /// Допуск синхронизации в миллисекундах.
    /// </summary>
    /// <remarks>
    /// Определяет максимальную разницу между временными метками левого и правого кадров,
    /// при которой они считаются синхронизированными. Значение не может быть ≤ 0 (сбрасывается на 20).
    /// </remarks>
    public double ToleranceMs
    {
        get => _toleranceMs;
        set => _toleranceMs = value > 0 ? value : 20.0;
    }

    /// <summary>Количество кадров в буфере левой камеры.</summary>
    public int LeftBufferCount => _leftBuffer.Count;

    /// <summary>Количество кадров в буфере правой камеры.</summary>
    public int RightBufferCount => _rightBuffer.Count;

    /// <summary>
    /// Добавляет новый кадр от левой камеры и пытается найти синхронную пару.
    /// </summary>
    /// <param name="frame">Кадр левой камеры.</param>
    /// <remarks>
    /// После добавления в буфер выполняется попытка сопоставления.
    /// Если пара найдена, вызывается <see cref="OnStereoFrameReady"/> вне блокировки.
    /// </remarks>
    public void PushLeft(FrameRaw frame)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        
        StereoFramePair? pair = null;
        lock (_syncLock)
        {
            // Добавляем кадр в левый буфер и пытаемся найти пару
            _leftBuffer.Push(frame);
            pair = TryMatchInternal(enforceTolerance: true, out _);
        }
        
        // Вызываем событие вне блокировки
        if (pair != null)
        {
            OnStereoFrameReady?.Invoke(pair);
        }
    }

    /// <summary>
    /// Добавляет новый кадр от правой камеры и пытается найти синхронную пару.
    /// </summary>
    /// <param name="frame">Кадр правой камеры.</param>
    /// <remarks>
    /// Аналогично <see cref="PushLeft"/>, но для правого буфера.
    /// </remarks>
    public void PushRight(FrameRaw frame)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        
        StereoFramePair? pair = null;
        lock (_syncLock)
        {
            // Добавляем кадр в правый буфер и пытаемся найти пару
            _rightBuffer.Push(frame);
            pair = TryMatchInternal(enforceTolerance: true, out _);
        }
        
        // Вызываем событие вне блокировки
        if (pair != null)
        {
            OnStereoFrameReady?.Invoke(pair);
        }
    }

    /// <summary>
    /// Внутренняя логика сопоставления. Должна вызываться под блокировкой <c>_syncLock</c>.
    /// </summary>
    /// <param name="enforceTolerance">
    /// Если <c>true</c>, применяет фильтр по <see cref="ToleranceMs"/>.
    /// Если <c>false</c>, возвращает ближайшую пару независимо от допуска.
    /// </param>
    /// <param name="deltaMs">Фактическая разница времени между выбранными кадрами (мс).</param>
    /// <returns>Подходящая пара или <c>null</c>, если данные в буферах отсутствуют.</returns>
    /// <remarks>
    /// Алгоритм двунаправленного сопоставления:
    /// 1. Последний левый → ближайший правый.
    /// 2. Последний правый → ближайший левый.
    /// 3. Выбирается кандидат с минимальной дельтой времени.
    /// </remarks>
    private StereoFramePair? TryMatchInternal(bool enforceTolerance, out double deltaMs)
    {
        var leftLatest = _leftBuffer.PeekLatest();
        var rightLatest = _rightBuffer.PeekLatest();
        
        // Недостаточно данных для сопоставления
        if (leftLatest == null || rightLatest == null)
        {
            deltaMs = double.PositiveInfinity;
            return null;
        }

        // Двунаправленное сопоставление: пробуем оба направления и выбираем лучшее
        FrameRaw? bestLeft = null;
        FrameRaw? bestRight = null;
        double bestDelta = double.MaxValue;

        // Направление 1: последний левый → ближайший правый
        var rightMatchForLeft = _rightBuffer.FindMinBy(f => 
            Math.Abs((f.Timestamp - leftLatest.Timestamp).TotalMilliseconds));
        
        if (rightMatchForLeft != null)
        {
            var delta1 = Math.Abs((leftLatest.Timestamp - rightMatchForLeft.Timestamp).TotalMilliseconds);
            if ((!enforceTolerance || delta1 <= _toleranceMs) && delta1 < bestDelta)
            {
                bestLeft = leftLatest;
                bestRight = rightMatchForLeft;
                bestDelta = delta1;
            }
        }

        // Направление 2: последний правый → ближайший левый
        var leftMatchForRight = _leftBuffer.FindMinBy(f => 
            Math.Abs((f.Timestamp - rightLatest.Timestamp).TotalMilliseconds));
        
        if (leftMatchForRight != null)
        {
            var delta2 = Math.Abs((leftMatchForRight.Timestamp - rightLatest.Timestamp).TotalMilliseconds);
            if ((!enforceTolerance || delta2 <= _toleranceMs) && delta2 < bestDelta)
            {
                bestLeft = leftMatchForRight;
                bestRight = rightLatest;
                bestDelta = delta2;
            }
        }

        // Создаём пару с клонами, если найдено соответствие
        if (bestLeft != null && bestRight != null)
        {
            deltaMs = bestDelta;
            return new StereoFramePair(bestLeft.Clone(), bestRight.Clone());
        }

        deltaMs = double.PositiveInfinity;
        return null;
    }

    /// <summary>
    /// Получает последнюю синхронизированную пару без ожидания новых кадров.
    /// </summary>
    /// <returns>Синхронизированная пара или <c>null</c>, если совпадение не найдено в пределах допуска.</returns>
    /// <remarks>
    /// Использует двунаправленный алгоритм сопоставления для поиска наилучшей пары.
    /// </remarks>
    public StereoFramePair? TryGetLatestPair()
    {
        lock (_syncLock)
        {
            return TryMatchInternal(enforceTolerance: true, out _);
        }
    }

    /// <summary>
    /// Возвращает ближайшую пару кадров без фильтра по <see cref="ToleranceMs"/>.
    /// </summary>
    /// <param name="pair">Найденная пара кадров или <c>null</c>, если хотя бы один буфер пуст.</param>
    /// <param name="deltaMs">Разница между кадрами в миллисекундах.</param>
    /// <returns><c>true</c>, если пара найдена; иначе <c>false</c>.</returns>
    public bool TryGetClosestPair(out StereoFramePair? pair, out double deltaMs)
    {
        lock (_syncLock)
        {
            pair = TryMatchInternal(enforceTolerance: false, out deltaMs);
            return pair != null;
        }
    }

    /// <summary>
    /// Очищает оба буфера кадров.
    /// </summary>
    public void Clear()
    {
        lock (_syncLock)
        {
            _leftBuffer.Clear();
            _rightBuffer.Clear();
        }
    }

    /// <summary>
    /// Освобождает оба кольцевых буфера.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _leftBuffer.Dispose();
        _rightBuffer.Dispose();
    }
}
