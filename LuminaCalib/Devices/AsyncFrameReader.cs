using System.Threading.Channels;
using LuminaCalib.Models;
using LuminaCalib.Services;

namespace LuminaCalib.Devices;

/// <summary>
/// Асинхронный читатель кадров, использующий <c>BoundedChannel&lt;T&gt;</c> для паттерна производитель–потребитель.
/// Оборачивает <see cref="ICameraSource"/> и обеспечивает обратное давление (backpressure),
/// сбрасывая кадры при переполнении очереди для снижения задержки.
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><description>Производитель (камера) пишет кадры через <c>TryWrite</c>.</description></item>
///   <item><description>Потребитель читает через <c>ReadAllAsync</c> и диспетчеризует обработчики.</description></item>
///   <item><description>При переполнении очереди новые кадры сбрасываются.</description></item>
/// </list>
/// </remarks>
public sealed class AsyncFrameReader : IDisposable
{
    private readonly ICameraSource _cameraSource;  // Источник кадров
    private readonly Channel<FrameRaw> _channel;    // Ограниченный канал для кадров
    private readonly MatPool? _matPool;             // Опциональный пул Mat-объектов
    private CancellationTokenSource? _cts;          // Источник токенов отмены
    private Task? _consumerTask;                    // Задача потребителя
    private bool _disposed;

    /// <summary>
    /// Событие: кадр готов к обработке.
    /// </summary>
    public event Action<FrameRaw>? OnFrameReady;

    /// <summary>
    /// Создаёт новый экземпляр <see cref="AsyncFrameReader"/>.
    /// </summary>
    /// <param name="cameraSource">Источник кадров камеры.</param>
    /// <param name="capacity">Емкость канала (по умолчанию 2; при переполнении кадры сбрасываются).</param>
    /// <param name="matPool">Опциональный пул Mat-объектов для управления памятью.</param>
    /// <exception cref="ArgumentNullException">Если <paramref name="cameraSource"/> равен <c>null</c>.</exception>
    public AsyncFrameReader(ICameraSource cameraSource, int capacity = 2, MatPool? matPool = null)
    {
        _cameraSource = cameraSource ?? throw new ArgumentNullException(nameof(cameraSource));
        _matPool = matPool;

        var options = new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = true
        };

        _channel = Channel.CreateBounded<FrameRaw>(options);
    }

    /// <summary>
    /// Идентификатор источника от базовой камеры.
    /// </summary>
    public string SourceId => _cameraSource.SourceId;

    /// <summary>
    /// Подключена ли камера.
    /// </summary>
    public bool IsConnected => _cameraSource.IsConnected;

    /// <summary>
    /// Текущая частота кадров от камеры.
    /// </summary>
    public double CurrentFps => _cameraSource.CurrentFps;

    /// <summary>
    /// Запускает чтение кадров с камеры.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <exception cref="ObjectDisposedException">Если объект уже удалён.</exception>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(AsyncFrameReader));
        if (_cts != null) return; // Уже запущен

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        // Подписываемся на кадры камеры (производитель)
        _cameraSource.OnFrameReceived += OnCameraFrame;

        // Запускаем задачу потребителя
        _consumerTask = Task.Run(() => ConsumeFramesAsync(_cts.Token), _cts.Token);

        // Запускаем камеру
        await _cameraSource.StartAsync(_cts.Token);
    }

    /// <summary>
    /// Останавливает чтение кадров и освобождает очередь.
    /// </summary>
    public async Task StopAsync()
    {
        // Отписываемся от кадров камеры
        _cameraSource.OnFrameReceived -= OnCameraFrame;
        
        // Отменяем и завершаем канал
        _cts?.Cancel();
        _channel.Writer.TryComplete();

        // Останавливаем камеру
        await _cameraSource.StopAsync();
        
        // Дожидаемся завершения задачи потребителя
        if (_consumerTask != null)
        {
            try
            {
                await _consumerTask;
            }
            catch (OperationCanceledException)
            {
                // Ожидаемое поведение при отмене
            }
        }

        // Очищаем и освобождаем оставшиеся кадры
        while (_channel.Reader.TryRead(out var frame))
        {
            ReturnOrDispose(frame);
        }

        _consumerTask = null;
        _cts?.Dispose();
        _cts = null;
    }

    /// <summary>
    /// Обработчик кадра от камеры (производитель).
    /// При переполнении канала кадр сбрасывается для минимизации задержки.
    /// </summary>
    /// <param name="frame">Полученный кадр.</param>
    private void OnCameraFrame(FrameRaw frame)
    {
        if (_disposed)
        {
            frame.Dispose();
            return;
        }

        // Сбрасываем кадр при переполнении очереди для снижения задержки
        try
        {
            if (_channel.Writer.TryWrite(frame))
            {
                return;
            }
        }
        catch (ChannelClosedException)
        {
            // Читатель завершает работу
        }

        ReturnOrDispose(frame);
    }

    /// <summary>
    /// Цикл потребителя: читает кадры из канала и вызывает обработчики.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    private async Task ConsumeFramesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var frame in _channel.Reader.ReadAllAsync(cancellationToken))
            {
                try
                {
                    // Вызываем обработчик, если он подписан
                    var handler = OnFrameReady;
                    if (handler != null)
                    {
                        handler.Invoke(frame);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Ошибка обработки кадра: {ex.Message}");
                }
                finally
                {
                    // Освобождаем кадр после обработки (возвращаем в пул или Dispose)
                    ReturnOrDispose(frame);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Ожидаемое поведение при завершении
        }
    }

    /// <summary>
    /// Возвращает Mat кадра в пул (если доступен) или освобождает ресурсы через Dispose.
    /// </summary>
    /// <param name="frame">Кадр для возврата/освобождения.</param>
    private void ReturnOrDispose(FrameRaw frame)
    {
        if (_matPool != null && !frame.Image.IsEmpty)
        {
            // Возвращаем Mat в пул для повторного использования.
            // FrameRaw.Dispose() не вызываем, чтобы не освободить Mat, возвращённый в пул.
            _matPool.Return(frame.Image);
        }
        else
        {
            frame.Dispose();
        }
    }

    /// <summary>Освобождает ресурсы, включая камеру и канал кадров.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _cameraSource.OnFrameReceived -= OnCameraFrame;
        _channel.Writer.TryComplete();
        _cts?.Cancel();
        _cts?.Dispose();
        _cameraSource.Dispose();
    }
}
