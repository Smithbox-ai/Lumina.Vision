using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace LuminaCalib.Controls;

/// <summary>
/// Статический помощник для отображения диалогов подтверждения.
/// </summary>
public static class ConfirmationDialog
{
    /// <summary>
    /// Показывает модальный диалог подтверждения и возвращает результат выбора пользователя.
    /// </summary>
    /// <param name="owner">Окно-владелец для модального диалога.</param>
    /// <param name="title">Заголовок диалога.</param>
    /// <param name="message">Текст сообщения.</param>
    /// <param name="confirmText">Текст кнопки подтверждения.</param>
    /// <param name="cancelText">Текст кнопки отмены.</param>
    /// <returns><c>true</c>, если пользователь подтвердил действие; иначе <c>false</c>.</returns>
    public static async Task<bool> ShowAsync(Window owner, string title, string message,
        string confirmText = "Confirm", string cancelText = "Cancel")
    {
        var tcs = new TaskCompletionSource<bool>();

        var window = new Window
        {
            Title = title,
            Width = 400,
            Height = 200,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            WindowDecorations = WindowDecorations.BorderOnly,
            Background = new SolidColorBrush(Color.Parse("#252525")),
        };

        window.Content = new Border
            {
                Padding = new Thickness(24),
                Child = new StackPanel
                {
                    Spacing = 20,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = title,
                            FontSize = 18,
                            FontWeight = FontWeight.SemiBold,
                            Foreground = Brushes.White
                        },
                        new TextBlock
                        {
                            Text = message,
                            FontSize = 14,
                            Foreground = new SolidColorBrush(Color.Parse("#B0B0B0")),
                            TextWrapping = TextWrapping.Wrap
                        },
                        new StackPanel
                        {
                            Orientation = Orientation.Horizontal,
                            HorizontalAlignment = HorizontalAlignment.Right,
                            Spacing = 12,
                            Children =
                            {
                                CreateButton(cancelText, isDanger: false, tcs, result: false, window),
                                CreateButton(confirmText, isDanger: true, tcs, result: true, window),
                            }
                        }
                    }
                }
            };

        window.Closing += (_, _) => tcs.TrySetResult(false);

        await window.ShowDialog(owner);
        return await tcs.Task;
    }

    /// <summary>
    /// Создаёт кнопку диалога с указанным стилем и результатом.
    /// </summary>
    private static Button CreateButton(string text, bool isDanger, TaskCompletionSource<bool> tcs, bool result, Window window)
    {
        var btn = new Button
        {
            Content = text,
            Padding = new Thickness(16, 10),
            CornerRadius = new CornerRadius(6),
            FontWeight = FontWeight.SemiBold,
            MinWidth = 100,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Background = isDanger
                ? new SolidColorBrush(Color.Parse("#D32F2F"))
                : new SolidColorBrush(Color.Parse("#3C3C3C")),
            Foreground = Brushes.White
        };
        btn.Click += (_, _) =>
        {
            tcs.TrySetResult(result);
            window.Close();
        };
        return btn;
    }
}
