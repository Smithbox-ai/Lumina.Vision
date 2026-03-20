using Avalonia;
using LuminaCalib.Services;

namespace LuminaCalib
{
    /// <summary>
    /// Точка входа в приложение LuminaCalib.
    /// </summary>
    /// <remarks>
    /// Инициализирует логирование исключений и запускает Avalonia-приложение
    /// с классическим жизненным циклом рабочего стола.
    /// </remarks>
    internal sealed class Program
    {
        /// <summary>
        /// Главная точка входа приложения.
        /// </summary>
        /// <param name="args">Аргументы командной строки.</param>
        /// <remarks>
        /// Код инициализации. Не используйте Avalonia, сторонние API или код,
        /// зависящий от SynchronizationContext, до вызова AppMain — всё ещё не инициализировано.
        /// </remarks>
        [STAThread]
        public static void Main(string[] args)
        {
            // Устанавливаем путь для файла логирования исключений
            ExceptionLogger.SetLogFilePath(Path.Combine(AppContext.BaseDirectory, "Exceptions.txt"));

            try
            {
                // Запускаем Avalonia-приложение с классическим жизненным циклом
                BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            }
            catch (Exception ex)
            {
                // Логируем необработанное исключение верхнего уровня
                ExceptionLogger.LogException(ex, "Program.Main");
            }
        }

        /// <summary>
        /// Конфигурация Avalonia-приложения.
        /// </summary>
        /// <returns>Настроенный экземпляр <see cref="AppBuilder"/>.</returns>
        /// <remarks>
        /// Используется также визуальным дизайнером — не удалять.
        /// </remarks>
        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .WithInterFont()
                .LogToTrace();
    }
}
