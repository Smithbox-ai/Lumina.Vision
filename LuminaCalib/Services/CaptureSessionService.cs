using System.Text.Json;
using System.Text.Json.Serialization;
using Emgu.CV;
using LuminaCalib.Models;

namespace LuminaCalib.Services;

/// <summary>
/// Сервис сохранения захваченных калибровочных кадров в структурированные сессии на диске.
/// </summary>
/// <remarks>
/// Каждая сессия сохраняется в отдельную директорию со структурой:
/// <code>
///   {SessionId}/
///     session.json    — манифест сессии (JSON) с настройками и списком захваченных кадров
///     frames/         — папка с PNG-изображениями кадров
/// </code>
/// Имена файлов кадров: <c>{Index:D4}_{Timestamp}_L|R_{FrameId}.png</c>.
/// </remarks>
public sealed class CaptureSessionService
{
    /// <summary>Настройки сериализации JSON с отступами и camelCase-именами свойств.</summary>
    private readonly JsonSerializerOptions _jsonOptions;

    /// <summary>Корневой каталог для хранения сессий захвата.</summary>
    public string SessionsRootPath { get; private set; }

    /// <summary>
    /// Инициализирует сервис сессий захвата.
    /// </summary>
    /// <param name="sessionsRootPath">    /// Путь к корневому каталогу сессий. Если <c>null</c>, используется <c>./CaptureSessions</c>.
    /// </param>
    public CaptureSessionService(string? sessionsRootPath = null)
    {
        // Настраиваем JSON-сериализатор с отступами, camelCase и поддержкой enum-строк
        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter() }
        };

        // Разрешаем корневой путь и создаём каталог при необходимости
        SessionsRootPath = ResolveRootPath(sessionsRootPath);
        Directory.CreateDirectory(SessionsRootPath);
    }

    /// <summary>
    /// Обновляет корневой каталог сессий и создаёт его при необходимости.
    /// </summary>
    /// <param name="sessionsRootPath">Новый путь. Если <c>null</c>, используется путь по умолчанию.</param>
    public void UpdateRootPath(string? sessionsRootPath)
    {
        SessionsRootPath = ResolveRootPath(sessionsRootPath);
        Directory.CreateDirectory(SessionsRootPath);
    }

    /// <summary>
    /// Создаёт новую сессию захвата: генерирует уникальный ID, создаёт директории и манифест.
    /// </summary>
    /// <param name="settings">Текущие настройки приложения (размеры доски, URL камер и т. д.).</param>
    /// <param name="mode">Режим калибровки (моно / стерео).</param>
    /// <returns>Манифест созданной сессии.</returns>
    public CaptureSessionManifest StartSession(AppSettings settings, CalibrationMode mode)
    {
        ArgumentNullException.ThrowIfNull(settings);

        // Формируем уникальный идентификатор сессии на основе даты, режима и настроек
        var nowLocal = DateTime.Now;
        var sessionId = BuildUniqueSessionId(nowLocal, mode, settings);
        var sessionDir = Path.Combine(SessionsRootPath, sessionId);
        var framesDir = Path.Combine(sessionDir, "frames");

        // Создаём каталоги сессии и кадров
        Directory.CreateDirectory(sessionDir);
        Directory.CreateDirectory(framesDir);

        // Заполняем манифест всеми параметрами сессии
        var manifest = new CaptureSessionManifest
        {
            SessionId = sessionId,
            CreatedAtUtc = nowLocal.ToUniversalTime(),
            CreatedAtLocal = nowLocal,
            CaptureMode = mode,
            BoardType = settings.BoardType,
            CharucoDictionary = settings.CharucoDictionary,
            PatternWidth = settings.PatternWidth,
            PatternHeight = settings.PatternHeight,
            SquareSizeMm = settings.SquareSize,
            MarkerSizeRatio = settings.MarkerSizeRatio,
            SyncToleranceMs = settings.SyncToleranceMs,
            RequiredFrames = settings.RequiredFrames,
            LeftCameraUrl = settings.LeftCameraUrl,
            RightCameraUrl = settings.RightCameraUrl,
            SessionDirectory = sessionDir
        };

        // Сохраняем манифест на диск (session.json)
        SaveManifest(manifest);
        return manifest;
    }

    /// <summary>
    /// Добавляет захваченную пару кадров в сессию.
    /// </summary>
    /// <param name="session">Манифест текущей сессии.</param>
    /// <param name="pair">Стерео-пара кадров для сохранения.</param>
    /// <param name="leftPatternFound">Обнаружен ли паттерн на левом кадре.</param>
    /// <param name="rightPatternFound">Обнаружен ли паттерн на правом кадре.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <remarks>
    /// Сохраняет оба изображения в PNG-формате.
    /// Именование: <c>{Index:D4}_{yyyyMMdd_HHmmss_fff}_{L|R}_{FrameId}.png</c>.
    /// После сохранения обновляет манифест сессии на диске.
    /// </remarks>
    public async Task AppendCaptureAsync(
        CaptureSessionManifest session,
        StereoFramePair pair,
        bool leftPatternFound,
        bool rightPatternFound,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(pair);
        EnsureSessionDirectory(session);

        // Формируем порядковый номер и токен временной метки для имени файла
        var index = session.Captures.Count + 1;
        var timestampToken = pair.Timestamp.ToString("yyyyMMdd_HHmmss_fff");
        var leftName = $"{index:D4}_{timestampToken}_L_{pair.Left.FrameId}.png";
        var rightName = $"{index:D4}_{timestampToken}_R_{pair.Right.FrameId}.png";

        var leftPath = Path.Combine(session.FramesDirectory, leftName);
        var rightPath = Path.Combine(session.FramesDirectory, rightName);

        // Сохраняем изображения левой и правой камеры в PNG
        await SaveImageAsync(pair.Left.Image, leftPath, cancellationToken);
        await SaveImageAsync(pair.Right.Image, rightPath, cancellationToken);

        // Добавляем запись о захваченном кадре в манифест
        session.Captures.Add(new CaptureFrameEntry
        {
            Index = index,
            CapturedAtUtc = pair.Timestamp.ToUniversalTime(),
            LeftTimestampUtc = pair.Left.Timestamp.ToUniversalTime(),
            RightTimestampUtc = pair.Right.Timestamp.ToUniversalTime(),
            LeftFrameId = pair.Left.FrameId,
            RightFrameId = pair.Right.FrameId,
            TimeDeltaMs = pair.TimeDeltaMs,
            LeftImageFile = leftName,
            RightImageFile = rightName,
            LeftPatternFound = leftPatternFound,
            RightPatternFound = rightPatternFound,
            Width = pair.Left.Image.Width,
            Height = pair.Left.Image.Height
        });

        // Обновляем манифест на диске
        await SaveManifestAsync(session, cancellationToken);
    }

    /// <summary>
    /// Асинхронно сохраняет манифест сессии в файл session.json.
    /// </summary>
    /// <param name="session">Манифест сессии для сохранения.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    public async Task SaveManifestAsync(CaptureSessionManifest session, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        EnsureSessionDirectory(session);

        // Сериализуем в JSON и записываем на диск
        var json = JsonSerializer.Serialize(session, _jsonOptions);
        await File.WriteAllTextAsync(session.ManifestPath, json, cancellationToken);
    }

    /// <summary>
    /// Синхронно сохраняет манифест сессии в файл session.json.
    /// </summary>
    /// <param name="session">Манифест сессии для сохранения.</param>
    public void SaveManifest(CaptureSessionManifest session)
    {
        ArgumentNullException.ThrowIfNull(session);
        EnsureSessionDirectory(session);

        // Сериализуем в JSON и записываем на диск (sync)
        var json = JsonSerializer.Serialize(session, _jsonOptions);
        File.WriteAllText(session.ManifestPath, json);
    }

    /// <summary>
    /// Помечает сессию как завершённую и сохраняет манифест.
    /// </summary>
    /// <param name="session">Манифест сессии для завершения.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    public async Task CompleteSessionAsync(CaptureSessionManifest session, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        session.IsCompleted = true;
        session.CompletedAtUtc = DateTime.UtcNow;
        await SaveManifestAsync(session, cancellationToken);
    }

    /// <summary>
    /// Загружает сессию захвата из указанного каталога или файла манифеста.
    /// </summary>
    /// <param name="sessionDirectoryOrManifestPath">Путь к каталогу сессии либо к файлу session.json.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Загруженный манифест сессии с отсортированными записями по индексу.</returns>
    public async Task<CaptureSessionManifest> LoadSessionAsync(
        string sessionDirectoryOrManifestPath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sessionDirectoryOrManifestPath))
        {
            throw new ArgumentException("Необходимо указать путь к сессии.", nameof(sessionDirectoryOrManifestPath));
        }

        var fullPath = Path.GetFullPath(sessionDirectoryOrManifestPath);
        string manifestPath;
        string sessionDirectory;

        // Определяем, передан каталог или путь к файлу манифеста
        if (Directory.Exists(fullPath))
        {
            sessionDirectory = fullPath;
            manifestPath = Path.Combine(fullPath, "session.json");
        }
        else if (File.Exists(fullPath))
        {
            manifestPath = fullPath;
            sessionDirectory = Path.GetDirectoryName(fullPath)
                ?? throw new InvalidOperationException("Каталог манифеста сессии некорректен.");
        }
        else
        {
            throw new FileNotFoundException($"Путь сессии не найден: {fullPath}");
        }

        if (!File.Exists(manifestPath))
        {
            throw new FileNotFoundException($"Манифест сессии не найден: {manifestPath}");
        }

        // Читаем и десериализуем манифест
        var json = await File.ReadAllTextAsync(manifestPath, cancellationToken);
        var manifest = JsonSerializer.Deserialize<CaptureSessionManifest>(json, _jsonOptions)
            ?? throw new InvalidOperationException("Не удалось десериализовать манифест сессии.");

        // Устанавливаем каталог и сортируем записи по индексу
        manifest.SessionDirectory = sessionDirectory;
        manifest.Captures = manifest.Captures
            .OrderBy(entry => entry.Index)
            .ToList();

        return manifest;
    }

    /// <summary>
    /// Разрешает корневой путь сессий: относительный путь приводится к абсолютному относительно BaseDirectory.
    /// </summary>
    /// <param name="sessionsRootPath">Путь (может быть <c>null</c>).</param>
    /// <returns>Абсолютный путь к корневому каталогу.</returns>
    public static string ResolveRootPath(string? sessionsRootPath)
    {
        // Если путь не указан — используем каталог по умолчанию
        var configured = string.IsNullOrWhiteSpace(sessionsRootPath)
            ? "./CaptureSessions"
            : sessionsRootPath.Trim();

        // Абсолютный путь возвращаем как есть; относительный — разрешаем от BaseDirectory
        if (Path.IsPathRooted(configured))
        {
            return Path.GetFullPath(configured);
        }

        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, configured));
    }

    /// <summary>
    /// Формирует уникальный идентификатор сессии, гарантируя отсутствие коллизий имён каталогов.
    /// </summary>
    /// <param name="nowLocal">Локальное время создания.</param>
    /// <param name="mode">Режим калибровки.</param>
    /// <param name="settings">Настройки приложения.</param>
    /// <returns>Уникальный ID сессии (имя каталога).</returns>
    private string BuildUniqueSessionId(DateTime nowLocal, CalibrationMode mode, AppSettings settings)
    {
        // Базовое имя: дата_режим_тип_размер
        var baseName = $"{nowLocal:yyyyMMdd_HHmmss}_{mode}_{settings.BoardType}_{settings.PatternWidth}x{settings.PatternHeight}";
        var sanitized = SanitizeFileName(baseName);

        // Если каталог не существует — используем базовое имя
        if (!Directory.Exists(Path.Combine(SessionsRootPath, sanitized)))
        {
            return sanitized;
        }

        // При коллизии добавляем числовой суффикс
        for (var index = 1; index < 1000; index++)
        {
            var candidate = $"{sanitized}_{index:D3}";
            if (!Directory.Exists(Path.Combine(SessionsRootPath, candidate)))
            {
                return candidate;
            }
        }

        throw new IOException("Не удалось сформировать уникальное имя каталога сессии.");
    }

    /// <summary>
    /// Очищает строку от недопустимых символов для имени файла/каталога.
    /// </summary>
    /// <param name="value">Исходная строка.</param>
    /// <returns>Очищенное имя.</returns>
    private static string SanitizeFileName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "session";
        }

        // Заменяем недопустимые символы на дефисы, пробелы на подчёркивания
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value
            .Select(ch => invalid.Contains(ch) ? '-' : ch)
            .ToArray();

        return new string(chars).Replace(' ', '_');
    }

    /// <summary>
    /// Асинхронно сохраняет изображение (Mat) на диск в формате PNG.
    /// </summary>
    /// <param name="image">Изображение для сохранения.</param>
    /// <param name="filePath">Путь к выходному файлу.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    private static async Task SaveImageAsync(Mat image, string filePath, CancellationToken cancellationToken)
    {
        if (image.IsEmpty)
        {
            throw new InvalidOperationException($"Невозможно сохранить пустое изображение: {filePath}");
        }

        // Запись через CvInvoke.Imwrite в фоновом потоке
        var saved = await Task.Run(() => CvInvoke.Imwrite(filePath, image), cancellationToken);
        if (!saved)
        {
            throw new IOException($"Не удалось сохранить файл изображения: {filePath}");
        }
    }

    /// <summary>
    /// Проверяет и создаёт каталоги сессии, если они отсутствуют.
    /// </summary>
    /// <param name="session">Манифест сессии.</param>
    private static void EnsureSessionDirectory(CaptureSessionManifest session)
    {
        if (string.IsNullOrWhiteSpace(session.SessionDirectory))
        {
            throw new InvalidOperationException("Каталог сессии (SessionDirectory) не инициализирован.");
        }

        // Создаём каталог сессии и подкаталог кадров
        Directory.CreateDirectory(session.SessionDirectory);
        Directory.CreateDirectory(session.FramesDirectory);
    }
}
