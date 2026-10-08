using Avalonia.Controls;
using LuminaCalib.Models;
using LuminaCalib.ViewModels;

namespace LuminaCalib.Views;

/// <summary>
/// Окно «О программе»: отображает сведения о версии, авторе и лицензии.
/// </summary>
public partial class AboutWindow : Window
{
    /// <summary>ViewModel, связанный с данным окном.</summary>
    private readonly AboutWindowViewModel _viewModel;

    /// <summary>
    /// Создаёт окно с настройками по умолчанию (используется дизайнером Avalonia).
    /// </summary>
    public AboutWindow() : this(new AppSettings())
    {
    }

    /// <summary>
    /// Создаёт окно и передаёт настройки в ViewModel.
    /// </summary>
    /// <param name="settings">Настройки приложения.</param>
    public AboutWindow(AppSettings settings)
    {
        InitializeComponent();

        _viewModel = new AboutWindowViewModel(settings);
        _viewModel.RequestClose += OnRequestClose;
        DataContext = _viewModel;
    }

    /// <summary>Обрабатывает запрос закрытия окна от ViewModel.</summary>
    private void OnRequestClose()
    {
        Close();
    }

    /// <summary>Отписывается от события и освобождает ViewModel при закрытии окна.</summary>
    protected override void OnClosed(EventArgs e)
    {
        _viewModel.RequestClose -= OnRequestClose;
        _viewModel.Dispose();
        base.OnClosed(e);
    }
}
