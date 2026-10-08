using Avalonia.Controls;

namespace LuminaCalib.Views;

/// <summary>
/// Представление менеджера калибровки — отображает процесс захвата и выполнения стереокалибровки.
/// </summary>
/// <remarks>
/// Код-бихайнд минимален — вся логика находится в <see cref="ViewModels.CalibrationManagerViewModel"/>.
/// </remarks>
public partial class CalibrationManagerView : UserControl
{
    /// <summary>
    /// Инициализирует новый экземпляр <see cref="CalibrationManagerView"/>.
    /// </summary>
    public CalibrationManagerView()
    {
        InitializeComponent();
    }
}
