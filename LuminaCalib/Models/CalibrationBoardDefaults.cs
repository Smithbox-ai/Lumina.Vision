namespace LuminaCalib.Models;

/// <summary>
/// Общий стартовый пресет доски для показа на экране смартфона в альбомной ориентации.
/// Физический размер клетки необходимо сверить с изображением на экране.
/// </summary>
public static class CalibrationBoardDefaults
{
    /// <summary>Тип доски.</summary>
    public const BoardType Type = BoardType.ChArUco;
    /// <summary>Количество клеток по горизонтали.</summary>
    public const int SquaresX = 10;
    /// <summary>Количество клеток по вертикали.</summary>
    public const int SquaresY = 5;
    /// <summary>Номинальная сторона клетки в миллиметрах.</summary>
    public const float SquareLengthMm = 10f;
    /// <summary>Доля клетки, занимаемая маркером.</summary>
    public const float MarkerLengthRatio = 0.7f;
    /// <summary>Словарь для 25 маркеров выбранной сетки.</summary>
    public const CharucoDictionary Dictionary = CharucoDictionary.Dict4x4_50;
    /// <summary>Разрешение экспорта.</summary>
    public const int Dpi = 300;
    /// <summary>Белое поле вокруг сетки в пикселях.</summary>
    public const int MarginPx = 50;
}
