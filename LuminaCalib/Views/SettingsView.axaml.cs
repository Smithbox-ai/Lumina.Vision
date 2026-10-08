using Avalonia.Controls;

namespace LuminaCalib.Views;

/// <summary>
/// Представление настроек приложения (камеры, пути, язык и т.д.).
/// </summary>
/// <remarks>
/// Код-бихайнд минимален — вся логика находится в <see cref="ViewModels.SettingsViewModel"/>.
/// </remarks>
public partial class SettingsView : UserControl
{
    /// <summary>
    /// Инициализирует новый экземпляр <see cref="SettingsView"/>.
    /// </summary>
    public SettingsView()
    {
        InitializeComponent();
    }
}
