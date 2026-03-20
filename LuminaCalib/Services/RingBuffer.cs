namespace LuminaCalib.Services;

/// <summary>
/// Потокобезопасный кольцевой буфер для хранения последних элементов с автоматическим освобождением.
/// </summary>
/// <typeparam name="T">Тип хранимых элементов. Должен реализовывать <see cref="IDisposable"/>.</typeparam>
/// <remarks>
/// Ключевая особенность: при перезаписи старые элементы <b>автоматически Dispose-ятся</b>
/// (семантика dispose-on-overwrite), что критично для предотвращения
/// утечек памяти при высокочастотном захвате кадров.
/// Потокобезопасность обеспечивается через <c>lock</c> на всех операциях.
/// </remarks>
public sealed class RingBuffer<T> : IDisposable where T : class, IDisposable
{
    /// <summary>Внутренний массив элементов.</summary>
    private readonly T?[] _buffer;

    /// <summary>Объект блокировки для потокобезопасности.</summary>
    private readonly object _lock = new();

    /// <summary>Позиция следующей записи (голова).</summary>
    private int _head;

    /// <summary>Текущее количество элементов в буфере.</summary>
    private int _count;

    /// <summary>Флаг освобождения.</summary>
    private bool _disposed;

    /// <summary>
    /// Создаёт кольцевой буфер с указанной ёмкостью.
    /// </summary>
    /// <param name="capacity">Максимальное количество хранимых элементов (минимум 1).</param>
    public RingBuffer(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _buffer = new T?[capacity];
    }

    /// <summary>Максимальная ёмкость буфера.</summary>
    public int Capacity => _buffer.Length;

    /// <summary>Текущее количество элементов в буфере.</summary>
    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _count;
            }
        }
    }

    /// <summary>
    /// Добавляет элемент в буфер. Если буфер полон, самый старый элемент
    /// освобождается (Dispose) и перезаписывается.
    /// </summary>
    /// <param name="item">Элемент для добавления.</param>
    public void Push(T item)
    {
        ArgumentNullException.ThrowIfNull(item);
        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_lock)
        {
            // Освобождаем перезаписываемый элемент
            var existing = _buffer[_head];
            existing?.Dispose();

            _buffer[_head] = item;
            // Перемещаем голову по кольцу
            _head = (_head + 1) % _buffer.Length;

            if (_count < _buffer.Length)
            {
                _count++;
            }
        }
    }

    /// <summary>
    /// Возвращает самый новый элемент без извлечения.
    /// </summary>
    /// <returns>Новейший элемент или <c>null</c>, если буфер пуст.</returns>
    public T? PeekLatest()
    {
        lock (_lock)
        {
            if (_count == 0) return null;
            
            // Голова указывает на следующую позицию записи, поэтому последний на head-1
            int latestIndex = (_head - 1 + _buffer.Length) % _buffer.Length;
            return _buffer[latestIndex];
        }
    }

    /// <summary>
    /// Возвращает элемент по возрасту (0 = новейший, 1 = предпоследний и т. д.).
    /// </summary>
    /// <param name="age">Возраст элемента (0 — новейший).</param>
    /// <returns>Элемент или <c>null</c>, если возраст выходит за пределы.</returns>
    public T? PeekAt(int age)
    {
        lock (_lock)
        {
            if (age < 0 || age >= _count) return null;
            
            // Вычисляем индекс от головы назад по кольцу
            int index = (_head - 1 - age + _buffer.Length * 2) % _buffer.Length;
            return _buffer[index];
        }
    }

    /// <summary>
    /// Возвращает копию всех элементов буфера от старейшего к новейшему.
    /// </summary>
    /// <returns>Массив элементов.</returns>
    public T[] ToArray()
    {
        lock (_lock)
        {
            var result = new T[_count];
            for (int i = 0; i < _count; i++)
            {
                // Обходим от старейшего к новейшему
                int index = (_head - _count + i + _buffer.Length) % _buffer.Length;
                result[i] = _buffer[index]!;
            }
            return result;
        }
    }

    /// <summary>
    /// Ищет первый элемент, удовлетворяющий предикату, от новейшего к старейшему.
    /// </summary>
    /// <param name="predicate">Условие поиска.</param>
    /// <returns>Найденный элемент или <c>null</c>.</returns>
    public T? FindNewestMatching(Func<T, bool> predicate)
    {
        lock (_lock)
        {
            // Перебираем от новейшего к старейшему
            for (int i = 0; i < _count; i++)
            {
                int index = (_head - 1 - i + _buffer.Length) % _buffer.Length;
                var item = _buffer[index];
                if (item != null && predicate(item))
                {
                    return item;
                }
            }
            return null;
        }
    }

    /// <summary>
    /// Ищет элемент с минимальным значением по указанному селектору среди всех элементов.
    /// </summary>
    /// <typeparam name="TKey">Тип ключа для сравнения.</typeparam>
    /// <param name="selector">Функция извлечения ключа для сравнения.</param>
    /// <returns>Элемент с минимальным ключом или <c>null</c>, если буфер пуст.</returns>
    public T? FindMinBy<TKey>(Func<T, TKey> selector) where TKey : IComparable<TKey>
    {
        lock (_lock)
        {
            if (_count == 0) return null;

            T? best = null;
            TKey? bestKey = default;

            // Перебираем все элементы для поиска минимума
            for (int i = 0; i < _count; i++)
            {
                int index = (_head - 1 - i + _buffer.Length) % _buffer.Length;
                var item = _buffer[index];
                if (item != null)
                {
                    var key = selector(item);
                    if (best == null || key.CompareTo(bestKey!) < 0)
                    {
                        best = item;
                        bestKey = key;
                    }
                }
            }

            return best;
        }
    }

    /// <summary>
    /// Очищает буфер, освобождая (Dispose) каждый элемент.
    /// </summary>
    public void Clear()
    {
        lock (_lock)
        {
            // Освобождаем и обнуляем каждый слот
            for (int i = 0; i < _buffer.Length; i++)
            {
                _buffer[i]?.Dispose();
                _buffer[i] = null;
            }
            _head = 0;
            _count = 0;
        }
    }

    /// <summary>
    /// Освобождает все элементы буфера.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Clear();
    }
}
