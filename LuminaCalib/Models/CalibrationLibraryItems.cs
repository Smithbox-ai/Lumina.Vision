namespace LuminaCalib.Models;

/// <summary>
/// Метаданные существующего XML-файла калибровки.
/// </summary>
public sealed class CalibrationLibraryItem
{
    /// <summary>Полный путь к файлу калибровки.</summary>
    public string FilePath { get; init; } = string.Empty;

    /// <summary>Имя файла калибровки (без директории).</summary>
    public string FileName { get; init; } = string.Empty;

    /// <summary>Режим калибровки, использованный при создании.</summary>
    public CalibrationMode Mode { get; init; }

    /// <summary>Дата и время создания файла.</summary>
    public DateTime CreatedAt { get; init; }

    /// <summary>Размер файла в байтах.</summary>
    public long FileSizeBytes { get; init; }

    /// <summary>Ошибка репроекции (может отсутствовать).</summary>
    public double? ReprojectionError { get; init; }

    /// <summary>Признак того, что эта калибровка является активной (выбранной).</summary>
    public bool IsActive { get; set; }
}

/// <summary>
/// Метаданные существующей директории сессии захвата кадров.
/// </summary>
public sealed class CaptureSessionLibraryItem
{
    /// <summary>Полный путь к директории сессии.</summary>
    public string SessionDirectory { get; init; } = string.Empty;

    /// <summary>Уникальный идентификатор сессии.</summary>
    public string SessionId { get; init; } = string.Empty;

    /// <summary>Режим калибровки, использованный при захвате.</summary>
    public CalibrationMode Mode { get; init; }

    /// <summary>Дата и время создания сессии (локальное время).</summary>
    public DateTime CreatedAtLocal { get; init; }

    /// <summary>Количество захваченных кадров в сессии.</summary>
    public int CaptureCount { get; init; }

    /// <summary>Требуемое количество кадров для завершения сессии.</summary>
    public int RequiredFrames { get; init; }

    /// <summary>Признак завершённости сессии.</summary>
    public bool IsCompleted { get; init; }

    /// <summary>Признак того, что эта сессия является активной (выбранной).</summary>
    public bool IsActive { get; set; }

    /// <summary>Отображаемый текст с количеством захваченных изображений.</summary>
    public string CaptureCountDisplay => IsCompleted
        ? $"({CaptureCount}/{RequiredFrames} \u2713)"
        : RequiredFrames > 0
            ? $"({CaptureCount}/{RequiredFrames})"
            : $"({CaptureCount} img)";
}
