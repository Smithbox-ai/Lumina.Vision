using CommunityToolkit.Mvvm.ComponentModel;

namespace LuminaCalib.Models;

/// <summary>
/// Тип уведомления.
/// </summary>
public enum NotificationType
{
    /// <summary>Информационное сообщение.</summary>
    Info,
    /// <summary>Успешное завершение операции.</summary>
    Success,
    /// <summary>Предупреждение.</summary>
    Warning,
    /// <summary>Ошибка.</summary>
    Error
}

/// <summary>
/// Модель одного уведомления приложения.
/// </summary>
public partial class NotificationItem : ObservableObject
{
    /// <summary>Уникальный идентификатор уведомления.</summary>
    public string Id { get; } = Guid.NewGuid().ToString();
    /// <summary>Тип уведомления (информация, успех, предупреждение, ошибка).</summary>
    public NotificationType Type { get; init; }
    /// <summary>Текст сообщения.</summary>
    public string Message { get; init; } = string.Empty;
    /// <summary>Дата и время создания уведомления.</summary>
    public DateTime CreatedAt { get; } = DateTime.Now;
}
