namespace LuminaCalib.Services;

/// <summary>
/// Сервис отслеживания изменений калибровочных файлов в указанном каталоге.
/// </summary>
/// <remarks>
/// Использует <see cref="FileSystemWatcher"/> для мониторинга событий создания, удаления,
/// изменения и переименования файлов с указанным фильтром (по умолчанию <c>*.xml</c>).
/// При любом изменении вызывается событие <see cref="OnFilesChanged"/>.
/// </remarks>
public sealed class FileWatcherService : IDisposable
{
    /// <summary>FileSystemWatcher для мониторинга каталога.</summary>
    private readonly FileSystemWatcher? _watcher;

    /// <summary>Путь к отслеживаемому каталогу.</summary>
    private readonly string _watchPath;

    /// <summary>Фильтр файлов (например, <c>*.xml</c>).</summary>
    private readonly string _filter;

    /// <summary>Флаг освобождения ресурсов.</summary>
    private bool _disposed;

    /// <summary>
    /// Вызывается при добавлении, удалении, изменении или переименовании файлов в отслеживаемом каталоге.
    /// </summary>
    public event Action? OnFilesChanged;

    /// <summary>
    /// Создаёт сервис отслеживания файлов.
    /// </summary>
    /// <param name="path">Каталог для мониторинга. Будет создан, если не существует.</param>
    /// <param name="filter">Фильтр файлов (по умолчанию <c>*.xml</c>).</param>
    public FileWatcherService(string path, string filter = "*.xml")
    {
        _watchPath = path;
        _filter = filter;

        try
        {
            // Создаём каталог при необходимости
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }

            // Настраиваем FileSystemWatcher: отслеживаем имя файла, последнюю запись и дату создания
            _watcher = new FileSystemWatcher(path, filter)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime,
                EnableRaisingEvents = true
            };

            // Подписываемся на все типы изменений
            _watcher.Created += OnFileSystemEvent;
            _watcher.Deleted += OnFileSystemEvent;
            _watcher.Changed += OnFileSystemEvent;
            _watcher.Renamed += OnFileSystemEvent;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Не удалось создать FileSystemWatcher: {ex.Message}");
        }
    }

    /// <summary>
    /// Возвращает список калибровочных файлов в отслеживаемом каталоге, отсортированных по дате создания (новые сначала).
    /// </summary>
    /// <returns>Перечисление <see cref="CalibrationFileInfo"/> с метаданными каждого файла.</returns>
    public IEnumerable<CalibrationFileInfo> GetCalibrationFiles()
    {
        // Если каталог не существует — пустой список
        if (!Directory.Exists(_watchPath))
        {
            yield break;
        }

        // Получаем файлы по фильтру и сортируем по дате создания (новые сначала)
        var files = Directory.GetFiles(_watchPath, _filter);
        foreach (var file in files.OrderByDescending(f => File.GetCreationTime(f)))
        {
            CalibrationFileInfo? info = null;
            try
            {
                var fileInfo = new FileInfo(file);
                // Пытаемся извлечь тип калибровки из имени файла
                // Ожидаемый формат: Stereo_2026-02-05_16-30.xml
                var fileName = Path.GetFileNameWithoutExtension(file);
                var nameParts = fileName.Split('_');
                var calibrationType = nameParts.Length >= 1 ? nameParts[0] : "Unknown";

                info = new CalibrationFileInfo
                {
                    FilePath = file,
                    FileName = fileName,
                    CreatedDate = fileInfo.CreationTime,
                    FileSizeBytes = fileInfo.Length,
                    CalibrationType = calibrationType
                };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка чтения информации о файле: {ex.Message}");
            }

            if (info != null)
            {
                yield return info;
            }
        }
    }

    /// <summary>
    /// Обработчик событий файловой системы. Перенаправляет в <see cref="OnFilesChanged"/>.
    /// </summary>
    private void OnFileSystemEvent(object sender, FileSystemEventArgs e)
    {
        // Оповещаем подписчиков об изменении файлов
        OnFilesChanged?.Invoke();
    }

    /// <summary>
    /// Освобождает ресурсы FileSystemWatcher.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_watcher != null)
        {
            // Останавливаем мониторинг и освобождаем watcher
            _watcher.EnableRaisingEvents = false;
            _watcher.Dispose();
        }
    }
}

/// <summary>
/// Информация о калибровочном файле.
/// </summary>
public record CalibrationFileInfo
{
    /// <summary>Полный путь к файлу.</summary>
    public required string FilePath { get; init; }

    /// <summary>Имя файла без расширения.</summary>
    public required string FileName { get; init; }

    /// <summary>Дата создания файла.</summary>
    public DateTime CreatedDate { get; init; }

    /// <summary>Размер файла в байтах.</summary>
    public long FileSizeBytes { get; init; }

    /// <summary>Тип калибровки (извлекается из имени файла).</summary>
    public string CalibrationType { get; init; } = "Unknown";

    /// <summary>Ошибка репроекции (если известна).</summary>
    public double? ReprojectionError { get; init; }

    /// <summary>Разрешение изображения (если известно).</summary>
    public string? Resolution { get; init; }
}
