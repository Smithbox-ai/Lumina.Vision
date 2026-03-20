using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using LuminaCalib.Services;

namespace LuminaCalib.ViewModels;

/// <summary>
/// Абстрактный базовый класс для всех ViewModel в приложении.
/// Предоставляет вспомогательные методы для локализации и работы с UI-потоком.
/// </summary>
public abstract class ViewModelBase : ObservableValidator
{
    /// <summary>
    /// Возвращает локализованную строку в зависимости от текущего языка приложения.
    /// </summary>
    /// <param name="en">Текст на английском языке.</param>
    /// <param name="ru">Текст на русском языке.</param>
    /// <returns>Строка, соответствующая текущему языку приложения.</returns>
    protected static string L(string en, string ru)
    {
        var lang = SettingsService.CurrentLanguage;
        return lang == "en" ? en : ru;
    }

    /// <summary>
    /// Выполняет действие в потоке UI (Dispatcher).
    /// Если вызов уже происходит из UI-потока, действие выполняется синхронно.
    /// </summary>
    /// <param name="action">Делегат, который нужно выполнить в UI-потоке.</param>
    protected static void RunOnUiThread(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
            action();
        else
            Dispatcher.UIThread.Post(action);
    }
}
