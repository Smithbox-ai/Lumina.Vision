using System.Collections.ObjectModel;
using Avalonia.Threading;
using LuminaCalib.Models;

namespace LuminaCalib.Services;

/// <summary>
/// Сервис отображения toast-уведомлений. Реализован как синглтон.
/// </summary>
public class NotificationService
{
    /// <summary>Ленивый экземпляр синглтона.</summary>
    private static readonly Lazy<NotificationService> _instance = new(() => new NotificationService());

    /// <summary>Глобальный экземпляр сервиса уведомлений.</summary>
    public static NotificationService Instance => _instance.Value;

    /// <summary>Коллекция активных уведомлений для привязки к UI.</summary>
    public ObservableCollection<NotificationItem> Notifications { get; } = new();

    /// <summary>Максимальное количество одновременно отображаемых уведомлений.</summary>
    private const int MaxNotifications = 5;

    /// <summary>Приватный конструктор (синглтон).</summary>
    private NotificationService() { }

    /// <summary>Показывает информационное уведомление.</summary>
    /// <param name="message">Текст сообщения.</param>
    public void ShowInfo(string message) => Show(NotificationType.Info, message);

    /// <summary>Показывает уведомление об успешном завершении операции.</summary>
    /// <param name="message">Текст сообщения.</param>
    public void ShowSuccess(string message) => Show(NotificationType.Success, message);

    /// <summary>Показывает предупреждение.</summary>
    /// <param name="message">Текст сообщения.</param>
    public void ShowWarning(string message) => Show(NotificationType.Warning, message);

    /// <summary>Показывает сообщение об ошибке.</summary>
    /// <param name="message">Текст сообщения.</param>
    public void ShowError(string message) => Show(NotificationType.Error, message);

    /// <summary>Удаляет уведомление по идентификатору.</summary>
    /// <param name="id">Идентификатор уведомления.</param>
    public void Dismiss(string id)
    {
        var item = Notifications.FirstOrDefault(n => n.Id == id);
        if (item != null)
            Dispatcher.UIThread.Post(() => Notifications.Remove(item));
    }

    /// <summary>Создаёт и отображает уведомление указанного типа.</summary>
    /// <param name="type">Тип уведомления.</param>
    /// <param name="message">Текст сообщения.</param>
    private void Show(NotificationType type, string message)
    {
        var item = new NotificationItem { Type = type, Message = message };

        Dispatcher.UIThread.Post(() =>
        {
            Notifications.Add(item);
            while (Notifications.Count > MaxNotifications)
                Notifications.RemoveAt(0);
        });

        var delay = type is NotificationType.Info or NotificationType.Success ? 4000 : 8000;
        _ = AutoDismissAsync(item.Id, delay);
    }

    /// <summary>Автоматически скрывает уведомление после задержки.</summary>
    /// <param name="id">Идентификатор уведомления.</param>
    /// <param name="delayMs">Задержка в миллисекундах перед скрытием.</param>
    private async Task AutoDismissAsync(string id, int delayMs)
    {
        await Task.Delay(delayMs);
        Dismiss(id);
    }
}
