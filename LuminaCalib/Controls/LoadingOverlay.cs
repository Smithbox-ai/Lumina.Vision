using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;

namespace LuminaCalib.Controls;

/// <summary>
/// Semi-transparent overlay with animated pulsing indicator dots and optional text.
/// Set <see cref="IsLoading"/> to <c>true</c> to show the overlay; it blocks interaction
/// with underlying controls while visible.
/// </summary>
public class LoadingOverlay : UserControl
{
    /// <summary>Loading text shown below the pulsing dots.</summary>
    public static readonly StyledProperty<string> LoadingTextProperty =
        AvaloniaProperty.Register<LoadingOverlay, string>(nameof(LoadingText), "Loading...");

    /// <summary>Controls overlay visibility. When <c>false</c> the control is hidden.</summary>
    public static readonly StyledProperty<bool> IsLoadingProperty =
        AvaloniaProperty.Register<LoadingOverlay, bool>(nameof(IsLoading));

    /// <summary>Текст загрузки, отображаемый под анимированными точками.</summary>
    public string LoadingText
    {
        get => GetValue(LoadingTextProperty);
        set => SetValue(LoadingTextProperty, value);
    }

    /// <summary>Управляет видимостью оверлея. При <c>false</c> контрол скрыт.</summary>
    public bool IsLoading
    {
        get => GetValue(IsLoadingProperty);
        set => SetValue(IsLoadingProperty, value);
    }

    static LoadingOverlay()
    {
        IsLoadingProperty.Changed.AddClassHandler<LoadingOverlay>((s, _) => s.IsVisible = s.IsLoading);
        LoadingTextProperty.Changed.AddClassHandler<LoadingOverlay>((s, _) => s.UpdateText());
    }

    private TextBlock? _textBlock;

    public LoadingOverlay()
    {
        IsVisible = false;
        IsHitTestVisible = true; // Block interaction with content beneath
        BuildUi();
    }

    private void UpdateText()
    {
        if (_textBlock != null)
            _textBlock.Text = LoadingText;
    }

    private void BuildUi()
    {
        var accentColor = Color.FromRgb(0, 229, 255);

        // Three pulsating dots
        var dotsPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Spacing = 8
        };

        for (int i = 0; i < 3; i++)
        {
            var dot = new Ellipse
            {
                Width = 12,
                Height = 12,
                Fill = new SolidColorBrush(accentColor),
                Opacity = 0.3
            };

            dot.Transitions =
            [
                new Avalonia.Animation.DoubleTransition
                {
                    Property = Visual.OpacityProperty,
                    Duration = TimeSpan.FromMilliseconds(600)
                }
            ];

            dotsPanel.Children.Add(dot);
        }

        _textBlock = new TextBlock
        {
            Text = LoadingText,
            FontSize = 14,
            Foreground = new SolidColorBrush(Color.FromRgb(176, 176, 176)),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 16, 0, 0)
        };

        Content = new Panel
        {
            Background = new SolidColorBrush(Color.FromArgb(128, 0, 0, 0)),
            Children =
            {
                new StackPanel
                {
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Children =
                    {
                        dotsPanel,
                        _textBlock
                    }
                }
            }
        };

        // Start pulsation animation using a timer
        StartPulsation(dotsPanel);
    }

    private void StartPulsation(StackPanel dotsPanel)
    {
        int tick = 0;
        var timer = new Avalonia.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(300)
        };
        timer.Tick += (_, _) =>
        {
            if (!IsLoading) return;
            for (int i = 0; i < dotsPanel.Children.Count; i++)
            {
                if (dotsPanel.Children[i] is Ellipse e)
                {
                    e.Opacity = (tick % 3 == i) ? 1.0 : 0.3;
                }
            }
            tick++;
        };
        timer.Start();
    }
}
