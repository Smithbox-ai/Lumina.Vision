using Avalonia;
using Avalonia.Animation;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;

namespace LuminaCalib.Controls;

/// <summary>
/// Элемент управления «пошаговый индикатор» для отображения прогресса калибровки.
/// </summary>
/// <remarks>
/// Отображает 4 шага калибровки: Настройка, Захват, Калибровка, Сохранение.
/// Каждый шаг может быть в состоянии: завершён (зелёный), активный (голубой), ожидает (серый).
/// </remarks>
public class WorkflowStepper : UserControl
{
    /// <summary>Общее количество шагов процесса калибровки.</summary>
    private const int TotalSteps = 4;

    // Cached brushes for step visuals
    private static readonly SolidColorBrush StepCompleteBrush = new(Color.FromRgb(0, 200, 83));
    private static readonly SolidColorBrush StepActiveBrush = new(Color.FromRgb(0, 229, 255));
    private static readonly SolidColorBrush StepPendingBrush = new(Color.FromRgb(60, 60, 60));
    private static readonly SolidColorBrush StepDarkTextBrush = new(Color.FromRgb(30, 30, 30));
    private static readonly SolidColorBrush StepLightTextBrush = new(Color.FromRgb(150, 150, 150));
    private static readonly SolidColorBrush LabelActiveBrush = new(Color.FromRgb(255, 255, 255));
    private static readonly SolidColorBrush LabelPendingBrush = new(Color.FromRgb(128, 128, 128));
    private static readonly SolidColorBrush ConnectorPendingBrush = new(Color.FromRgb(76, 76, 76));
    private static readonly SolidColorBrush ContainerBrush = new(Color.FromRgb(44, 44, 44));

    /// <summary>Кэшированные индикаторы шагов (Border-бэджи).</summary>
    private Border[]? _stepIndicators;
    /// <summary>Кэшированные номера шагов (TextBlock).</summary>
    private TextBlock[]? _stepNumbers;
    /// <summary>Кэшированные надписи шагов (TextBlock).</summary>
    private TextBlock[]? _stepLabels;
    /// <summary>Кэшированные соединители между шагами (Border).</summary>
    private Border[]? _connectors;
    /// <summary>Признак однократной инициализации визуального дерева.</summary>
    private bool _isInitialized;

    /// <summary>Свойство Avalonia: текущий шаг (значение 0–<see cref="TotalSteps"/>).</summary>
    public static readonly StyledProperty<int> CurrentStepProperty =
        AvaloniaProperty.Register<WorkflowStepper, int>(nameof(CurrentStep), 
            coerce: (_, value) => Math.Clamp(value, 0, TotalSteps));

    /// <summary>Свойство Avalonia: текст шага «Настройка».</summary>
    public static readonly StyledProperty<string> SetupTextProperty =
        AvaloniaProperty.Register<WorkflowStepper, string>(nameof(SetupText), "Setup");

    /// <summary>Свойство Avalonia: текст шага «Захват».</summary>
    public static readonly StyledProperty<string> CaptureTextProperty =
        AvaloniaProperty.Register<WorkflowStepper, string>(nameof(CaptureText), "Capture");

    /// <summary>Свойство Avalonia: текст шага «Калибровка».</summary>
    public static readonly StyledProperty<string> CalibrateTextProperty =
        AvaloniaProperty.Register<WorkflowStepper, string>(nameof(CalibrateText), "Calibrate");

    /// <summary>Свойство Avalonia: текст шага «Сохранение».</summary>
    public static readonly StyledProperty<string> SaveTextProperty =
        AvaloniaProperty.Register<WorkflowStepper, string>(nameof(SaveText), "Save");

    /// <summary>
    /// Статический конструктор — регистрирует обработчики изменения свойств.
    /// </summary>
    static WorkflowStepper()
    {
        CurrentStepProperty.Changed.AddClassHandler<WorkflowStepper>((s, e) => s.UpdateStepVisuals());
        SetupTextProperty.Changed.AddClassHandler<WorkflowStepper>((s, e) => s.UpdateTexts());
        CaptureTextProperty.Changed.AddClassHandler<WorkflowStepper>((s, e) => s.UpdateTexts());
        CalibrateTextProperty.Changed.AddClassHandler<WorkflowStepper>((s, e) => s.UpdateTexts());
        SaveTextProperty.Changed.AddClassHandler<WorkflowStepper>((s, e) => s.UpdateTexts());
    }

    /// <summary>Текущий шаг процесса калибровки (0–4).</summary>
    public int CurrentStep
    {
        get => GetValue(CurrentStepProperty);
        set => SetValue(CurrentStepProperty, value);
    }

    /// <summary>Локализованный текст шага «Настройка».</summary>
    public string SetupText
    {
        get => GetValue(SetupTextProperty);
        set => SetValue(SetupTextProperty, value);
    }

    /// <summary>Локализованный текст шага «Захват».</summary>
    public string CaptureText
    {
        get => GetValue(CaptureTextProperty);
        set => SetValue(CaptureTextProperty, value);
    }

    /// <summary>Локализованный текст шага «Калибровка».</summary>
    public string CalibrateText
    {
        get => GetValue(CalibrateTextProperty);
        set => SetValue(CalibrateTextProperty, value);
    }

    /// <summary>Локализованный текст шага «Сохранение».</summary>
    public string SaveText
    {
        get => GetValue(SaveTextProperty);
        set => SetValue(SaveTextProperty, value);
    }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="WorkflowStepper"/>.
    /// </summary>
    public WorkflowStepper()
    {
        AutomationProperties.SetName(this, "Workflow Stepper");
        BuildUi();
        UpdateStepVisuals();
    }

    /// <summary>
    /// Строит визуальное дерево элемента управления один раз, кэшируя ссылки на дочерние контролы.
    /// </summary>
    private void BuildUi()
    {
        if (_isInitialized) return;

        _stepIndicators = new Border[TotalSteps];
        _stepNumbers = new TextBlock[TotalSteps];
        _stepLabels = new TextBlock[TotalSteps];
        _connectors = new Border[TotalSteps - 1];

        var steps = new[] { SetupText, CaptureText, CalibrateText, SaveText };

        var mainPanel = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center
        };

        for (int i = 0; i < steps.Length; i++)
        {
            var stepPanel = new StackPanel
            {
                Orientation = Avalonia.Layout.Orientation.Horizontal,
                Spacing = 4
            };

            // Номер шага
            var numberText = new TextBlock
            {
                Text = (i + 1).ToString(),
                FontSize = 14,
                FontWeight = FontWeight.SemiBold,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
            };
            _stepNumbers[i] = numberText;

            // Индикатор шага (круглый бэдж)
            var indicator = new Border
            {
                Width = 28,
                Height = 28,
                CornerRadius = new CornerRadius(14),
                Child = numberText,
                Transitions = new Transitions
                {
                    new BrushTransition { Property = Border.BackgroundProperty, Duration = TimeSpan.FromMilliseconds(300) }
                }
            };
            _stepIndicators[i] = indicator;

            // Надпись шага
            var label = new TextBlock
            {
                Text = steps[i],
                FontSize = 14,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                Margin = new Thickness(4, 0, 0, 0)
            };
            _stepLabels[i] = label;

            stepPanel.Children.Add(indicator);
            stepPanel.Children.Add(label);
            mainPanel.Children.Add(stepPanel);

            // Соединительная линия (кроме последнего шага)
            if (i < steps.Length - 1)
            {
                var connector = new Border
                {
                    Width = 32,
                    Height = 2,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                    Transitions = new Transitions
                    {
                        new BrushTransition { Property = Border.BackgroundProperty, Duration = TimeSpan.FromMilliseconds(300) }
                    }
                };
                _connectors[i] = connector;
                mainPanel.Children.Add(connector);
            }
        }

        Content = new Border
        {
            Background = ContainerBrush,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16, 12),
            Child = mainPanel
        };

        _isInitialized = true;
    }

    /// <summary>
    /// Обновляет визуальные свойства (фон, цвет текста) существующих контролов при изменении шага.
    /// </summary>
    private void UpdateStepVisuals()
    {
        if (!_isInitialized || _stepIndicators == null || _stepNumbers == null || 
            _stepLabels == null || _connectors == null) return;

        for (int i = 0; i < TotalSteps; i++)
        {
            _stepIndicators[i].Background = GetStepBackground(i);
            _stepNumbers[i].Foreground = GetStepForeground(i);
            _stepLabels[i].Foreground = GetLabelForeground(i);
        }

        for (int i = 0; i < _connectors.Length; i++)
        {
            _connectors[i].Background = i < CurrentStep ? StepCompleteBrush : ConnectorPendingBrush;
        }
    }

    /// <summary>
    /// Обновляет текстовые надписи шагов при изменении локализованных свойств.
    /// </summary>
    private void UpdateTexts()
    {
        if (!_isInitialized || _stepLabels == null) return;

        var steps = new[] { SetupText, CaptureText, CalibrateText, SaveText };
        for (int i = 0; i < TotalSteps; i++)
        {
            _stepLabels[i].Text = steps[i];
        }
    }

    /// <summary>
    /// Возвращает цвет фона индикатора шага.
    /// </summary>
    /// <param name="stepIndex">Индекс шага (0-based).</param>
    /// <returns>Кисть фона индикатора.</returns>
    private IBrush GetStepBackground(int stepIndex)
    {
        if (stepIndex < CurrentStep)
            return StepCompleteBrush;   // Завершён — зелёный
        if (stepIndex == CurrentStep && CurrentStep < TotalSteps)
            return StepActiveBrush; // Активный — голубой
        return StepPendingBrush;     // Ожидает — тёмный
    }

    /// <summary>
    /// Возвращает цвет текста номера шага.
    /// </summary>
    /// <param name="stepIndex">Индекс шага (0-based).</param>
    /// <returns>Кисть текста номера.</returns>
    private IBrush GetStepForeground(int stepIndex)
    {
        if (stepIndex < CurrentStep || (stepIndex == CurrentStep && CurrentStep < TotalSteps))
            return StepDarkTextBrush;  // Тёмный текст для активных/завершённых
        return StepLightTextBrush;  // Светлый текст для ожидающих
    }

    /// <summary>
    /// Возвращает цвет надписи шага.
    /// </summary>
    /// <param name="stepIndex">Индекс шага (0-based).</param>
    /// <returns>Кисть надписи.</returns>
    private IBrush GetLabelForeground(int stepIndex)
    {
        if (stepIndex < CurrentStep || (stepIndex == CurrentStep && CurrentStep < TotalSteps))
            return LabelActiveBrush; // Белый для активных/завершённых
        return LabelPendingBrush;    // Серый для ожидающих
    }
}
