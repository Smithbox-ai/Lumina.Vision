using System.Text;
using Avalonia.Threading;

namespace LuminaCalib.Services;

/// <summary>
/// Централизованный логгер исключений с записью в файл и уведомлением UI.
/// </summary>
/// <remarks>
/// Статический класс, который перехватывает необработанные исключения на уровне AppDomain,
/// TaskScheduler и Avalonia UI Dispatcher. В файл <c>Exceptions.txt</c> сохраняются только исключения,
/// а информационные сообщения публикуются только через событие <see cref="OnLogLine"/> для UI-лога.
/// </remarks>
public static class ExceptionLogger
{
    /// <summary>Объект блокировки для потокобезопасной записи в файл.</summary>
    private static readonly object SyncRoot = new();

    /// <summary>Флаг: обработчики AppDomain/TaskScheduler уже установлены.</summary>
    private static bool _coreHandlersInstalled;

    /// <summary>Флаг: обработчик UI-диспетчера уже установлен.</summary>
    private static bool _uiHandlerInstalled;

    /// <summary>Путь к файлу лога исключений.</summary>
    private static string _logFilePath = Path.Combine(AppContext.BaseDirectory, "Exceptions.txt");

    /// <summary>
    /// Вызывается при появлении новой строки лога для отображения в UI-окне логов.
    /// </summary>
    public static event Action<string>? OnLogLine;

    /// <summary>
    /// Устанавливает глобальные обработчики необработанных исключений.
    /// </summary>
    /// <remarks>
    /// Регистрирует три обработчика:
    /// <list type="bullet">
    /// <item><description><see cref="AppDomain.UnhandledException"/> — неперехваченные исключения домена.</description></item>
    /// <item><description><see cref="TaskScheduler.UnobservedTaskException"/> — ненаблюдаемые исключения в задачах.</description></item>
    /// <item><description><see cref="Dispatcher.UIThread"/>.UnhandledException — исключения в UI-потоке Avalonia.</description></item>
    /// </list>
    /// UI-обработчик может быть недоступен на ранних этапах запуска приложения.
    /// </remarks>
    public static void InstallGlobalHandlers()
    {
        if (!_coreHandlersInstalled)
        {
            _coreHandlersInstalled = true;
            // Подписываемся на необработанные исключения домена и задач
            AppDomain.CurrentDomain.UnhandledException += CurrentDomainOnUnhandledException;
            TaskScheduler.UnobservedTaskException += TaskSchedulerOnUnobservedTaskException;
        }

        if (_uiHandlerInstalled)
        {
            return;
        }

        try
        {
            // Пытаемся подписаться на UI-диспетчер (может быть недоступен на ранних этапах)
            Dispatcher.UIThread.UnhandledException += UiThreadOnUnhandledException;
            _uiHandlerInstalled = true;
        }
        catch
        {
            // UI-диспетчер может быть ещё не готов.
        }
    }

    /// <summary>
    /// Устанавливает путь к файлу журнала исключений.
    /// </summary>
    /// <param name="path">Полный путь к файлу лога. Игнорируется, если пустой или <c>null</c>.</param>
    public static void SetLogFilePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        _logFilePath = path;
    }

    /// <summary>
    /// Записывает исключение в файл журнала и отправляет короткую строку в UI.
    /// </summary>
    /// <param name="exception">Исключение для логирования.</param>
    /// <param name="context">Контекст, в котором произошло исключение (например, имя обработчика).</param>
    public static void LogException(Exception exception, string context)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var stamp = DateTime.Now;
        // Формируем заголовок и короткую строку для UI
        var header = $"[{stamp:yyyy-MM-dd HH:mm:ss.fff}] [{context}]";
        var uiLine = $"{stamp:HH:mm:ss} [{context}] {exception.GetType().Name}: {exception.Message}";

        // Полная запись с трассировкой стека в файл
        var builder = new StringBuilder();
        builder.AppendLine(header);
        builder.AppendLine(exception.ToString());
        builder.AppendLine(new string('-', 90));

        AppendToFile(builder.ToString());
        PublishToUi(uiLine);
    }

    /// <summary>
    /// Публикует информационное сообщение в UI-лог без записи в <c>Exceptions.txt</c>.
    /// </summary>
    /// <param name="message">Текст сообщения.</param>
    /// <param name="context">Контекст (по умолчанию <c>"Info"</c>).</param>
    public static void LogMessage(string message, string context = "Info")
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        var stamp = DateTime.Now;
        // Для UI оставляем короткий формат, без записи в файл исключений.
        var uiLine = $"{stamp:HH:mm:ss} [{context}] {message}";

        PublishToUi(uiLine);
    }

    /// <summary>
    /// Обработчик необработанных исключений домена приложения.
    /// </summary>
    private static void CurrentDomainOnUnhandledException(object? sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            LogException(exception, "AppDomain.UnhandledException");
            return;
        }

        // Получен объект, не являющийся исключением
        LogMessage("Получен необработанный объект, не являющийся исключением, от AppDomain.", "AppDomain.UnhandledException");
    }

    /// <summary>
    /// Обработчик ненаблюдаемых исключений в задачах (Task).
    /// </summary>
    private static void TaskSchedulerOnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        LogException(e.Exception, "TaskScheduler.UnobservedTaskException");
        // Помечаем исключение как обработанное, чтобы не завершить процесс
        e.SetObserved();
    }

    /// <summary>
    /// Обработчик необработанных исключений в UI-потоке Avalonia.
    /// </summary>
    private static void UiThreadOnUnhandledException(object? sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogException(e.Exception, "Dispatcher.UIThread.UnhandledException");
        // Помечаем как обработанное, чтобы не обрушить приложение
        e.Handled = true;
    }

    /// <summary>
    /// Потокобезопасно дописывает текст в файл лога.
    /// </summary>
    /// <param name="content">Текст для записи.</param>
    private static void AppendToFile(string content)
    {
        try
        {
            lock (SyncRoot)
            {
                // Создаём каталог при необходимости
                var directory = Path.GetDirectoryName(_logFilePath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.AppendAllText(_logFilePath, content, Encoding.UTF8);
            }
        }
        catch
        {
            // Логирование не должно пробрасывать исключения в бизнес-логику.
        }
    }

    /// <summary>
    /// Публикует строку лога в подписчикам события <see cref="OnLogLine"/>.
    /// </summary>
    /// <param name="line">Строка лога.</param>
    private static void PublishToUi(string line)
    {
        try
        {
            OnLogLine?.Invoke(line);
        }
        catch
        {
            // Игнорируем ошибки UI-подписчиков.
        }
    }
}
