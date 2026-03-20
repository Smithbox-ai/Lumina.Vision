using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LuminaCalib.Models;
using LuminaCalib.Services;

namespace LuminaCalib.ViewModels;

/// <summary>
/// ViewModel окна «О программе».
/// </summary>
public partial class AboutWindowViewModel : ViewModelBase, IDisposable
{
    /// <summary>URL оригинального репозитория проекта.</summary>
    private const string OriginalProjectUrlValue = "https://git.tocan.com.ua/Stanislav_Maslenkov/stand_msi.git";

    /// <summary>Настройки приложения (для отслеживания смены языка).</summary>
    private readonly AppSettings _settings;
    /// <summary>Флаг освобождения ресурсов.</summary>
    private bool _disposed;

    /// <summary>Событие запроса закрытия окна.</summary>
    public event Action? RequestClose;

    /// <summary>Локализованный заголовок окна.</summary>
    [ObservableProperty]
    private string _windowTitleText = "About";

    /// <summary>Локализованный заголовок раздела.</summary>
    [ObservableProperty]
    private string _headerText = "About";

    /// <summary>Локализованная метка «Приложение».</summary>
    [ObservableProperty]
    private string _applicationLabelText = "Application:";

    /// <summary>Название приложения.</summary>
    [ObservableProperty]
    private string _applicationNameText = "LuminaCalib";

    /// <summary>Локализованная метка «Версия».</summary>
    [ObservableProperty]
    private string _versionLabelText = "Version:";

    /// <summary>Текст версии приложения.</summary>
    [ObservableProperty]
    private string _versionValueText = string.Empty;

    /// <summary>Локализованная метка «Автор».</summary>
    [ObservableProperty]
    private string _authorLabelText = "Author:";

    /// <summary>Имя автора.</summary>
    [ObservableProperty]
    private string _authorNameText = "Maslenkov Stanislav";

    /// <summary>Локализованная метка «Лицензия».</summary>
    [ObservableProperty]
    private string _licenseLabelText = "License:";

    /// <summary>Название лицензии.</summary>
    [ObservableProperty]
    private string _licenseValueText = "Apache License 2.0";

    /// <summary>Локализованная метка «Оригинальный проект».</summary>
    [ObservableProperty]
    private string _originalProjectLabelText = "Original project:";

    /// <summary>URL оригинального проекта для отображения.</summary>
    [ObservableProperty]
    private string _originalProjectUrlText = OriginalProjectUrlValue;

    /// <summary>Локализованный текст кнопки «Открыть ссылку».</summary>
    [ObservableProperty]
    private string _openLinkButtonText = "Open Link";

    /// <summary>Локализованный текст кнопки «Закрыть».</summary>
    [ObservableProperty]
    private string _closeButtonText = "Close";

    /// <summary>
    /// Инициализирует ViewModel и подписывается на изменения настроек.
    /// </summary>
    /// <param name="settings">Настройки приложения.</param>
    public AboutWindowViewModel(AppSettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _settings.PropertyChanged += OnSettingsChanged;

        VersionValueText = ResolveVersionText();
        UpdateLocalizedUiTexts();
    }

    /// <summary>Открывает URL оригинального проекта в браузере по умолчанию.</summary>
    [RelayCommand]
    private void OpenOriginalProjectLink()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = OriginalProjectUrlValue,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            ExceptionLogger.LogException(ex, "AboutWindow.OpenOriginalProjectLink");
        }
    }

    /// <summary>Запрашивает закрытие окна через <see cref="RequestClose"/>.</summary>
    [RelayCommand]
    private void Close()
    {
        RequestClose?.Invoke();
    }

    /// <summary>Обрабатывает изменение настроек — обновляет UI при смене языка.</summary>
    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AppSettings.Language))
        {
            UpdateLocalizedUiTexts();
        }
    }

    /// <summary>Обновляет все локализованные тексты UI согласно текущему языку.</summary>
    private void UpdateLocalizedUiTexts()
    {
        WindowTitleText = L("About", "О программе");
        HeaderText = L("About LuminaCalib", "О программе LuminaCalib");
        ApplicationLabelText = L("Application:", "Приложение:");
        VersionLabelText = L("Version:", "Версия:");
        AuthorLabelText = L("Author:", "Автор:");
        LicenseLabelText = L("License:", "Лицензия:");
        OriginalProjectLabelText = L("Original project:", "Оригинальный проект:");
        OpenLinkButtonText = L("Open Link", "Открыть ссылку");
        CloseButtonText = L("Close", "Закрыть");

        ApplicationNameText = "LuminaCalib";
        AuthorNameText = "Maslenkov Stanislav";
        LicenseValueText = "Apache License 2.0";
        OriginalProjectUrlText = OriginalProjectUrlValue;
    }

    /// <summary>Возвращает строку с версией приложения из атрибутов сборки.</summary>
    /// <returns>Строка версии или <c>"n/a"</c>, если определить невозможно.</returns>
    private static string ResolveVersionText()
    {
        var assembly = Assembly.GetEntryAssembly() ?? typeof(AboutWindowViewModel).Assembly;
        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informationalVersion))
        {
            return informationalVersion;
        }

        return assembly.GetName().Version?.ToString(3) ?? "n/a";
    }

    /// <summary>Освобождает ресурсы и отписывается от событий настроек.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _settings.PropertyChanged -= OnSettingsChanged;
    }
}
