using LuminaCalib.Models;

namespace LuminaCalib.Devices;

/// <summary>
/// Интерфейс источника видеопотока камеры.
/// </summary>
/// <remarks>
/// Определяет контракт для реализаций захвата видео с камер (RTSP, HTTP, USB и др.).
/// Поддерживает асинхронный запуск/остановку, события кадров, подключения и ошибок.
/// Реализует <see cref="IDisposable"/> для освобождения нативных ресурсов.
/// </remarks>
public interface ICameraSource : IDisposable
{
    /// <summary>
    /// Уникальный идентификатор источника камеры.
    /// </summary>
    string SourceId { get; }

    /// <summary>
    /// Подключена ли камера и идёт ли потоковое видео.
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// Текущая частота кадров (кадров в секунду).
    /// </summary>
    double CurrentFps { get; }

    /// <summary>
    /// Ширина кадра в пикселях.
    /// </summary>
    int FrameWidth { get; }

    /// <summary>
    /// Высота кадра в пикселях.
    /// </summary>
    int FrameHeight { get; }

    /// <summary>
    /// Событие получения нового кадра.
    /// </summary>
    event Action<FrameRaw>? OnFrameReceived;

    /// <summary>
    /// Событие изменения статуса подключения.
    /// </summary>
    /// <remarks>Параметр <c>true</c> — подключено, <c>false</c> — отключено.</remarks>
    event Action<bool>? OnConnectionChanged;

    /// <summary>
    /// Событие ошибки в процессе захвата.
    /// </summary>
    /// <remarks>Строковый параметр содержит описание ошибки.</remarks>
    event Action<string>? OnError;

    /// <summary>
    /// Запускает захват видео с камеры.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Останавливает захват видео.
    /// </summary>
    Task StopAsync();
}
