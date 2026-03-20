using System.Text.Json.Serialization;

namespace LuminaCalib.Models;

/// <summary>
/// Метаданные и индекс файлов сохранённой сессии захвата кадров.
/// </summary>
public sealed class CaptureSessionManifest
{
    /// <summary>Уникальный идентификатор сессии.</summary>
    public string SessionId { get; set; } = string.Empty;

    /// <summary>Дата и время создания сессии (UTC).</summary>
    public DateTime CreatedAtUtc { get; set; }

    /// <summary>Дата и время создания сессии (локальное).</summary>
    public DateTime CreatedAtLocal { get; set; }

    /// <summary>Режим захвата (полная калибровка, стерео или одиночная камера).</summary>
    public CalibrationMode CaptureMode { get; set; } = CalibrationMode.Auto;

    /// <summary>Тип калибровочной доски, использованный при захвате.</summary>
    public BoardType BoardType { get; set; } = BoardType.Chessboard;

    /// <summary>Словарь ChArUco-маркеров, использованный при захвате.</summary>
    public CharucoDictionary CharucoDictionary { get; set; } = CharucoDictionary.Dict6x6_250;

    /// <summary>Ширина паттерна (количество внутренних углов по горизонтали).</summary>
    public int PatternWidth { get; set; }

    /// <summary>Высота паттерна (количество внутренних углов по вертикали).</summary>
    public int PatternHeight { get; set; }

    /// <summary>Размер стороны квадрата в миллиметрах.</summary>
    public float SquareSizeMm { get; set; }

    /// <summary>Отношение размера маркера к размеру квадрата для ChArUco.</summary>
    public float MarkerSizeRatio { get; set; }

    /// <summary>Допустимое расхождение по времени между кадрами левой и правой камер (мс).</summary>
    public double SyncToleranceMs { get; set; }

    /// <summary>Необходимое количество кадров для сессии.</summary>
    public int RequiredFrames { get; set; }

    /// <summary>Флаг завершённости сессии (достигнут лимит кадров).</summary>
    public bool IsCompleted { get; set; }

    /// <summary>Дата и время завершения сессии (UTC).</summary>
    public DateTime? CompletedAtUtc { get; set; }

    /// <summary>URL-адрес левой камеры.</summary>
    public string LeftCameraUrl { get; set; } = string.Empty;

    /// <summary>URL-адрес правой камеры.</summary>
    public string RightCameraUrl { get; set; } = string.Empty;

    /// <summary>Список захваченных стереопар кадров.</summary>
    public List<CaptureFrameEntry> Captures { get; set; } = [];

    /// <summary>Путь к директории сессии (не сериализуется в JSON).</summary>
    [JsonIgnore]
    public string SessionDirectory { get; set; } = string.Empty;

    /// <summary>Путь к поддиректории с кадрами (не сериализуется в JSON).</summary>
    [JsonIgnore]
    public string FramesDirectory => Path.Combine(SessionDirectory, "frames");

    /// <summary>Путь к файлу манифеста сессии (не сериализуется в JSON).</summary>
    [JsonIgnore]
    public string ManifestPath => Path.Combine(SessionDirectory, "session.json");
}

/// <summary>
/// Одна сохранённая стереопара кадров.
/// </summary>
public sealed class CaptureFrameEntry
{
    /// <summary>Порядковый номер кадра в сессии.</summary>
    public int Index { get; set; }

    /// <summary>Время захвата кадра (UTC).</summary>
    public DateTime CapturedAtUtc { get; set; }

    /// <summary>Временная метка левого кадра (UTC).</summary>
    public DateTime LeftTimestampUtc { get; set; }

    /// <summary>Временная метка правого кадра (UTC).</summary>
    public DateTime RightTimestampUtc { get; set; }

    /// <summary>Уникальный идентификатор левого кадра.</summary>
    public long LeftFrameId { get; set; }

    /// <summary>Уникальный идентификатор правого кадра.</summary>
    public long RightFrameId { get; set; }

    /// <summary>Разница во времени между левым и правым кадрами (мс).</summary>
    public double TimeDeltaMs { get; set; }

    /// <summary>Имя файла изображения левой камеры.</summary>
    public string LeftImageFile { get; set; } = string.Empty;

    /// <summary>Имя файла изображения правой камеры.</summary>
    public string RightImageFile { get; set; } = string.Empty;

    /// <summary>Паттерн обнаружен на левом изображении.</summary>
    public bool LeftPatternFound { get; set; }

    /// <summary>Паттерн обнаружен на правом изображении.</summary>
    public bool RightPatternFound { get; set; }

    /// <summary>Ширина изображения в пикселях.</summary>
    public int Width { get; set; }

    /// <summary>Высота изображения в пикселях.</summary>
    public int Height { get; set; }
}
