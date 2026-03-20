using Emgu.CV;

namespace LuminaCalib.Models;

/// <summary>
/// Кадр-сырец, захваченный с камеры, с информацией о временной метке.
/// </summary>
/// <remarks>
/// Реализует IDisposable для освобождения неуправляемой памяти изображения (Mat).
/// </remarks>
public sealed class FrameRaw : IDisposable
{
    /// <summary>
    /// Данные захваченного изображения (OpenCV Mat).
    /// </summary>
    public Mat Image { get; }

    /// <summary>
    /// Временная метка захвата кадра.
    /// </summary>
    public DateTime Timestamp { get; }

    /// <summary>
    /// Уникальный идентификатор кадра.
    /// </summary>
    public long FrameId { get; }

    /// <summary>
    /// Идентификатор источника камеры (например, "Left", "Right").
    /// </summary>
    public string SourceId { get; }

    private bool _disposed;

    /// <summary>
    /// Создаёт новый экземпляр кадра-сырца.
    /// </summary>
    /// <param name="image">Изображение кадра (Mat). Не должно быть null.</param>
    /// <param name="timestamp">Временная метка захвата.</param>
    /// <param name="frameId">Уникальный идентификатор кадра.</param>
    /// <param name="sourceId">Идентификатор камеры-источника.</param>
    /// <exception cref="ArgumentNullException">Если <paramref name="image"/> равен null.</exception>
    public FrameRaw(Mat image, DateTime timestamp, long frameId, string sourceId = "")
    {
        Image = image ?? throw new ArgumentNullException(nameof(image));
        Timestamp = timestamp;
        FrameId = frameId;
        SourceId = sourceId;
    }

    /// <summary>
    /// Создаёт глубокую копию кадра (клонирует Mat).
    /// </summary>
    /// <returns>Новый экземпляр <see cref="FrameRaw"/> с копией изображения.</returns>
    /// <exception cref="ObjectDisposedException">Если объект уже освобождён.</exception>
    public FrameRaw Clone()
    {
        // Проверяем, что объект не был освобождён
        ObjectDisposedException.ThrowIf(_disposed, this);
        // Клонируем изображение и создаём новый экземпляр с теми же метаданными
        return new FrameRaw(Image.Clone(), Timestamp, FrameId, SourceId);
    }

    /// <summary>
    /// Освобождает неуправляемые ресурсы изображения (Mat).
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        // Освобождаем неуправляемую память OpenCV
        Image.Dispose();
    }
}
