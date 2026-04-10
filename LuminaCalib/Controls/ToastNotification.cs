using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using LuminaCalib.Models;
using LuminaCalib.Services;

namespace LuminaCalib.Controls;

/// <summary>
/// Компонент отображения одного toast-уведомления с цветовой индикацией типа и кнопкой закрытия.
/// </summary>
public class ToastNotification : UserControl
{
    /// <summary>Attached property that holds the <see cref="NotificationItem"/> to display.</summary>
    public static readonly StyledProperty<NotificationItem?> NotificationProperty =
        AvaloniaProperty.Register<ToastNotification, NotificationItem?>(nameof(Notification));

    /// <summary>Привязанное уведомление для отображения.</summary>
    public NotificationItem? Notification
    {
        get => GetValue(NotificationProperty);
        set => SetValue(NotificationProperty, value);
    }

    static ToastNotification()
    {
        NotificationProperty.Changed.AddClassHandler<ToastNotification>((s, _) => s.UpdateUi());
    }

    /// <summary>Initialises the component and renders the current notification.</summary>
    public ToastNotification()
    {
        UpdateUi();
    }

    private void UpdateUi()
    {
        var item = Notification;
        if (item == null) { Content = null; return; }

        var accentColor = item.Type switch
        {
            NotificationType.Success => Color.FromRgb(0, 200, 83),
            NotificationType.Warning => Color.FromRgb(255, 214, 0),
            NotificationType.Error   => Color.FromRgb(255, 82, 82),
            _                        => Color.FromRgb(0, 229, 255), // Info — cyan
        };

        var closeBtn = new Button
        {
            Content = "\u2715",
            Background = Brushes.Transparent,
            Foreground = new SolidColorBrush(Color.FromRgb(176, 176, 176)),
            Padding = new Thickness(4),
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Top,
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
            [Grid.ColumnProperty] = 1
        };
        closeBtn.Click += (_, _) => NotificationService.Instance.Dismiss(item.Id);

        var messageBlock = new TextBlock
        {
            Text = item.Message,
            FontSize = 13,
            Foreground = Brushes.White,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
            [Grid.ColumnProperty] = 0
        };

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Children = { messageBlock, closeBtn }
        };

        Content = new Border
        {
            Width = 320,
            Background = new SolidColorBrush(Color.FromRgb(44, 44, 44)),
            CornerRadius = new CornerRadius(6),
            BorderBrush = new SolidColorBrush(accentColor),
            BorderThickness = new Thickness(2, 0, 0, 0),
            Padding = new Thickness(12, 10),
            Margin = new Thickness(0, 0, 0, 8),
            BoxShadow = BoxShadows.Parse("0 4 12 0 #40000000"),
            Child = grid
        };
    }
}
