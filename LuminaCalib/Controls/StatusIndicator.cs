using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;

namespace LuminaCalib.Controls;

/// <summary>
/// Индикатор статуса подключения камеры с отображением FPS.
/// </summary>
/// <remarks>
/// Отображает цветной индикатор (зелёный/красный), метку камеры,
/// частоту кадров и дополнительную информацию.
/// </remarks>
public class StatusIndicator : UserControl
{
    // Cached brushes for indicator colors
    private static readonly SolidColorBrush ConnectedBrush = new(Color.FromRgb(0, 200, 83));
    private static readonly SolidColorBrush DisconnectedBrush = new(Color.FromRgb(255, 82, 82));

    /// <summary>Свойство Avalonia: метка камеры (Label).</summary>
    public static readonly StyledProperty<string> LabelProperty =
        AvaloniaProperty.Register<StatusIndicator, string>(nameof(Label), "Camera");

    /// <summary>Свойство Avalonia: признак подключения камеры.</summary>
    public static readonly StyledProperty<bool> IsConnectedProperty =
        AvaloniaProperty.Register<StatusIndicator, bool>(nameof(IsConnected));

    /// <summary>Свойство Avalonia: текущая частота кадров.</summary>
    public static readonly StyledProperty<double> FpsProperty =
        AvaloniaProperty.Register<StatusIndicator, double>(nameof(Fps));

    /// <summary>Свойство Avalonia: дополнительная информация (например, разрешение).</summary>
    public static readonly StyledProperty<string?> ExtraInfoProperty =
        AvaloniaProperty.Register<StatusIndicator, string?>(nameof(ExtraInfo));

    /// <summary>Свойство Avalonia: текст при отключённой камере.</summary>
    public static readonly StyledProperty<string> DisconnectedTextProperty =
        AvaloniaProperty.Register<StatusIndicator, string>(nameof(DisconnectedText), "Disconnected");

    /// <summary>Свойство Avalonia: единицы измерения FPS.</summary>
    public static readonly StyledProperty<string> FpsUnitTextProperty =
        AvaloniaProperty.Register<StatusIndicator, string>(nameof(FpsUnitText), "fps");

    /// <summary>
    /// Статический конструктор — регистрирует обработчики изменения свойств.
    /// </summary>
    static StatusIndicator()
    {
        LabelProperty.Changed.AddClassHandler<StatusIndicator>((s, _) => AutomationProperties.SetName(s, s.Label));
        IsConnectedProperty.Changed.AddClassHandler<StatusIndicator>((s, e) => s.UpdateIndicator());
        FpsProperty.Changed.AddClassHandler<StatusIndicator>((s, e) => s.UpdateFps());
        ExtraInfoProperty.Changed.AddClassHandler<StatusIndicator>((s, e) => s.UpdateExtraInfo());
        DisconnectedTextProperty.Changed.AddClassHandler<StatusIndicator>((s, e) => s.UpdateFps());
        FpsUnitTextProperty.Changed.AddClassHandler<StatusIndicator>((s, e) => s.UpdateFps());
    }

    /// <summary>Метка камеры (например, «Левая» или «Правая»).</summary>
    public string Label
    {
        get => GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    /// <summary>Признак подключения камеры.</summary>
    public bool IsConnected
    {
        get => GetValue(IsConnectedProperty);
        set => SetValue(IsConnectedProperty, value);
    }

    /// <summary>Текущая частота кадров (кадр/с).</summary>
    public double Fps
    {
        get => GetValue(FpsProperty);
        set => SetValue(FpsProperty, value);
    }

    /// <summary>Дополнительная информация (resolution, кодек и т.д.).</summary>
    public string? ExtraInfo
    {
        get => GetValue(ExtraInfoProperty);
        set => SetValue(ExtraInfoProperty, value);
    }

    /// <summary>Текст при отключённой камере.</summary>
    public string DisconnectedText
    {
        get => GetValue(DisconnectedTextProperty);
        set => SetValue(DisconnectedTextProperty, value);
    }

    /// <summary>Единицы измерения частоты кадров (например, «кадр/с»).</summary>
    public string FpsUnitText
    {
        get => GetValue(FpsUnitTextProperty);
        set => SetValue(FpsUnitTextProperty, value);
    }

    /// <summary>Текстовый блок с отображением FPS.</summary>
    private TextBlock? _fpsText;
    /// <summary>Текстовый блок с дополнительной информацией.</summary>
    private TextBlock? _extraInfoText;
    /// <summary>Эллипс-индикатор подключения (зелёный/красный).</summary>
    private Avalonia.Controls.Shapes.Ellipse? _indicator;

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="StatusIndicator"/>.
    /// </summary>
    public StatusIndicator()
    {
        InitializeUi();
        AutomationProperties.SetName(this, Label);
    }

    /// <summary>
    /// Строит визуальное дерево элемента управления программно.
    /// </summary>
    private void InitializeUi()
    {
        var panel = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Spacing = 6
        };

        // Индикатор подключения (эллипс)
        _indicator = new Avalonia.Controls.Shapes.Ellipse
        {
            Width = 8,
            Height = 8,
            Fill = DisconnectedBrush, // Красный по умолчанию
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };
        panel.Children.Add(_indicator);

        // Метка камеры
        var labelText = new TextBlock
        {
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(176, 176, 176)),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };
        labelText.Bind(TextBlock.TextProperty, this.GetObservable(LabelProperty));
        panel.Children.Add(labelText);

        // Разделитель
        panel.Children.Add(new TextBlock
        {
            Text = ": ",
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(100, 100, 100)),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        });

        // Частота кадров
        _fpsText = new TextBlock
        {
            Text = "0 fps",
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(176, 176, 176)),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };
        panel.Children.Add(_fpsText);

        _extraInfoText = new TextBlock
        {
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(120, 120, 120)),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            IsVisible = false
        };
        panel.Children.Add(_extraInfoText);

        Content = panel;
        UpdateIndicator();
        UpdateFps();
        UpdateExtraInfo();
    }

    /// <summary>
    /// Обновляет цвет индикатора подключения.
    /// </summary>
    private void UpdateIndicator()
    {
        if (_indicator == null) return;
        
        _indicator.Fill = IsConnected
            ? ConnectedBrush   // Зелёный
            : DisconnectedBrush; // Красный
    }

    /// <summary>
    /// Обновляет текст FPS или статус отключения.
    /// </summary>
    private void UpdateFps()
    {
        if (_fpsText == null) return;
        
        _fpsText.Text = IsConnected 
            ? $"{Fps:F1} {FpsUnitText}" 
            : DisconnectedText;
    }

    /// <summary>
    /// Обновляет видимость и текст дополнительной информации.
    /// </summary>
    private void UpdateExtraInfo()
    {
        if (_extraInfoText == null)
        {
            return;
        }

        var hasInfo = !string.IsNullOrWhiteSpace(ExtraInfo);
        _extraInfoText.IsVisible = hasInfo;
        _extraInfoText.Text = hasInfo ? $" ({ExtraInfo})" : string.Empty;
    }
}
