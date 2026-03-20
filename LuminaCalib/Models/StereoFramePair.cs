namespace LuminaCalib.Models;

/// <summary>
/// Синхронизированная пара кадров от левой и правой камер.
/// </summary>
/// <remarks>
/// Реализует IDisposable для освобождения обоих кадров (Left и Right).
/// </remarks>
public sealed class StereoFramePair : IDisposable
{
    /// <summary>
    /// Кадр левой камеры.
    /// </summary>
    public FrameRaw Left { get; }

    /// <summary>
    /// Кадр правой камеры.
    /// </summary>
    public FrameRaw Right { get; }

    /// <summary>
    /// Разница во времени между левым и правым кадрами в миллисекундах.
    /// </summary>
    public double TimeDeltaMs { get; }

    /// <summary>
    /// Средняя временная метка пары (середина между временем левого и правого кадров).
    /// </summary>
    public DateTime Timestamp => Left.Timestamp.AddMilliseconds(TimeDeltaMs / 2);

    private bool _disposed;

    /// <summary>
    /// Создаёт новую стереопару из левого и правого кадров.
    /// </summary>
    /// <param name="left">Кадр левой камеры. Не должен быть null.</param>
    /// <param name="right">Кадр правой камеры. Не должен быть null.</param>
    /// <exception cref="ArgumentNullException">Если <paramref name="left"/> или <paramref name="right"/> равен null.</exception>
    public StereoFramePair(FrameRaw left, FrameRaw right)
    {
        Left = left ?? throw new ArgumentNullException(nameof(left));
        Right = right ?? throw new ArgumentNullException(nameof(right));
        // Вычисляем разницу во времени между кадрами (по модулю)
        TimeDeltaMs = Math.Abs((left.Timestamp - right.Timestamp).TotalMilliseconds);
    }

    /// <summary>
    /// Создаёт глубокую копию стереопары (клонирует оба изображения Mat).
    /// </summary>
    /// <returns>Новый экземпляр <see cref="StereoFramePair"/> с копиями кадров.</returns>
    /// <exception cref="ObjectDisposedException">Если объект уже освобождён.</exception>
    public StereoFramePair Clone()
    {
        // Проверяем, что объект не был освобождён
        ObjectDisposedException.ThrowIf(_disposed, this);
        // Клонируем оба кадра и создаём новую пару
        return new StereoFramePair(Left.Clone(), Right.Clone());
    }

    /// <summary>
    /// Освобождает оба кадра (левый и правый).
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        // Освобождаем ресурсы обоих кадров
        Left.Dispose();
        Right.Dispose();
    }
}
