using System.Collections.Concurrent;
using System.Drawing;
using Emgu.CV;
using Emgu.CV.CvEnum;

namespace LuminaCalib.Services;

/// <summary>
/// Потокобезопасный пул объектов <see cref="Mat"/> для снижения нагрузки на GC.
/// </summary>
/// <remarks>
/// При высокочастотном захвате кадров создание/уничтожение Mat-объектов вызывает
/// значительное давление на сборщик мусора (крупные нативные буферы).
/// Пул позволяет повторно использовать Mat-объекты через паттерн Rent/Return.
/// Размер пула ограничен <see cref="_maxPoolSize"/>; лишние объекты уничтожаются.
/// </remarks>
public sealed class MatPool : IDisposable
{
    /// <summary>Потокобезопасная коллекция свободных Mat-объектов.</summary>
    private readonly ConcurrentBag<Mat> _pool = [];

    /// <summary>Размер Mat-объектов в пуле.</summary>
    private readonly Size _size;

    /// <summary>Тип глубины пикселей.</summary>
    private readonly DepthType _depth;

    /// <summary>Количество каналов изображения.</summary>
    private readonly int _channels;

    /// <summary>Максимальный размер пула (свыше него объекты уничтожаются).</summary>
    private readonly int _maxPoolSize;

    /// <summary>Количество арендованных в данный момент объектов.</summary>
    private int _rentedCount;

    /// <summary>Флаг освобождения ресурсов.</summary>
    private bool _disposed;

    /// <summary>
    /// Создаёт пул Mat-объектов с указанными параметрами.
    /// </summary>
    /// <param name="size">Размер изображения (Width x Height).</param>
    /// <param name="depth">Тип глубины пикселей (по умолчанию <see cref="DepthType.Cv8U"/>).</param>
    /// <param name="channels">Количество каналов (по умолчанию 3 — BGR).</param>
    /// <param name="maxPoolSize">Максимальное количество объектов в пуле (по умолчанию 20).</param>
    public MatPool(Size size, DepthType depth = DepthType.Cv8U, int channels = 3, int maxPoolSize = 20)
    {
        _size = size;
        _depth = depth;
        _channels = channels;
        _maxPoolSize = maxPoolSize;
    }

    /// <summary>
    /// Создаёт пул с параметрами по умолчанию: Full HD, BGR, 20 объектов.
    /// </summary>
    public MatPool() : this(new Size(1920, 1080), DepthType.Cv8U, 3, 20)
    {
    }

    /// <summary>Количество Mat-объектов, арендованных в данный момент.</summary>
    public int RentedCount => _rentedCount;

    /// <summary>Количество свободных Mat-объектов в пуле.</summary>
    public int AvailableCount => _pool.Count;

    /// <summary>
    /// Арендует Mat из пула или создаёт новый, если пул пуст.
    /// </summary>
    /// <returns>Объект Mat, очищенный нулями, готовый к использованию.</returns>
    public Mat Rent()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        
        // Увеличиваем счётчик арендованных объектов атомарно
        Interlocked.Increment(ref _rentedCount);

        if (_pool.TryTake(out var mat))
        {
            // Очищаем Mat перед повторным использованием
            mat.SetTo(new Emgu.CV.Structure.MCvScalar(0, 0, 0));
            return mat;
        }

        // Пул пуст — создаём новый Mat
        return new Mat(_size, _depth, _channels);
    }

    /// <summary>
    /// Арендует Mat с указанным размером. Если размер отличается от пула, создаёт новый.
    /// </summary>
    /// <param name="size">Требуемый размер изображения.</param>
    /// <returns>Объект Mat заданного размера.</returns>
    public Mat Rent(Size size)
    {
        // Если размер совпадает с пулом — используем стандартную аренду
        if (size == _size)
        {
            return Rent();
        }

        Interlocked.Increment(ref _rentedCount);
        return new Mat(size, _depth, _channels);
    }

    /// <summary>
    /// Возвращает Mat в пул для повторного использования.
    /// </summary>
    /// <param name="mat">Возвращаемый Mat. Если параметры не совпадают или пул полон — объект уничтожается.</param>
    public void Return(Mat? mat)
    {
        if (mat == null || mat.IsEmpty)
        {
            mat?.Dispose();
            return;
        }

        // Уменьшаем счётчик арендованных
        Interlocked.Decrement(ref _rentedCount);

        if (_disposed)
        {
            mat.Dispose();
            return;
        }

        // Проверяем соответствие параметров Mat параметрам пула
        if (mat.Size == _size && mat.Depth == _depth && mat.NumberOfChannels == _channels)
        {
            if (_pool.Count < _maxPoolSize)
            {
                _pool.Add(mat);
                return;
            }
        }

        // Уничтожаем, если параметры не совпадают или пул полон
        mat.Dispose();
    }

    /// <summary>
    /// Очищает пул, уничтожая все свободные Mat-объекты.
    /// </summary>
    public void Clear()
    {
        // Извлекаем и уничтожаем все Mat-объекты из пула
        while (_pool.TryTake(out var mat))
        {
            mat.Dispose();
        }
    }

    /// <summary>
    /// Освобождает все ресурсы пула.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Clear();
    }
}
