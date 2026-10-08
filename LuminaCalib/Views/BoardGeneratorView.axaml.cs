using Avalonia.Controls;

namespace LuminaCalib.Views;

/// <summary>
/// Представление для генератора калибровочных шаблонов (шахматных досок).
/// </summary>
/// <remarks>
/// Код-бихайнд минимален — вся логика находится в <see cref="ViewModels.BoardGeneratorViewModel"/>.
/// </remarks>
public partial class BoardGeneratorView : UserControl
{
    /// <summary>
    /// Инициализирует новый экземпляр <see cref="BoardGeneratorView"/>.
    /// </summary>
    public BoardGeneratorView()
    {
        InitializeComponent();
    }
}
