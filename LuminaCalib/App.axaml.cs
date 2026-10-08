using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using LuminaCalib.Calibration;
using LuminaCalib.Services;
using LuminaCalib.ViewModels;
using LuminaCalib.Views;
using System.Globalization;

namespace LuminaCalib
{
    /// <summary>
    /// Главный класс Avalonia-приложения LuminaCalib.
    /// </summary>
    /// <remarks>
    /// Управляет жизненным циклом приложения: загрузка XAML-ресурсов,
    /// настройка DI-сервисов, язык интерфейса, глобальная обработка исключений.
    /// </remarks>
    public partial class App : Application
    {
        /// <summary>
        /// Загружает XAML-ресурсы приложения.
        /// </summary>
        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        /// <summary>
        /// Вызывается после завершения инициализации фреймворка Avalonia.
        /// Настраивает сервисы, язык интерфейса, обработчик исключений и создаёт главное окно.
        /// </summary>
        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                // Создаём общие сервисы
                var settingsService = new SettingsService();
                var settings = settingsService.Settings;

                // Применяем языковые настройки из конфигурации
                ApplyLanguage(settings.Language);

                // Инициализируем синхронизатор стереопар
                var stereoSynchronizer = new StereoSynchronizer(toleranceMs: settings.SyncToleranceMs);

                // Настраиваем глобальное логирование исключений
                ExceptionLogger.SetLogFilePath(Path.Combine(AppContext.BaseDirectory, "Exceptions.txt"));
                ExceptionLogger.InstallGlobalHandlers();

                // Диагностика CUDA (фоновая, не блокирует запуск)
                Task.Run(() => DepthMapProcessor.LogCudaInfo());

                // Создаём главную модель представления и окно
                var mainVm = new MainWindowViewModel(settingsService, stereoSynchronizer);

                desktop.MainWindow = new MainWindow
                {
                    DataContext = mainVm,
                };
            }

            base.OnFrameworkInitializationCompleted();
        }

        /// <summary>
        /// Применяет языковые настройки к текущему потоку (Culture и UICulture).
        /// </summary>
        /// <param name="languageCode">Код языка: <c>"en"</c> или <c>"ru"</c>. По умолчанию — <c>"ru-RU"</c>.</param>
        private static void ApplyLanguage(string languageCode)
        {
            // Нормализуем код языка
            var normalized = languageCode?.Trim().ToLowerInvariant();
            var cultureName = normalized switch
            {
                "en" => "en-US",
                "ru" => "ru-RU",
                _ => "ru-RU"
            };

            // Устанавливаем культуру для текущего потока
            var culture = CultureInfo.GetCultureInfo(cultureName);
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
        }
    }
}
