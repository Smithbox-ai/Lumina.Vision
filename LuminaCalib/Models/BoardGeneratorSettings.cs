using CommunityToolkit.Mvvm.ComponentModel;

namespace LuminaCalib.Models;

/// <summary>
/// Настройки генерации калибровочной доски ChArUco.
/// </summary>
public partial class BoardGeneratorSettings : ObservableObject
{
    /// <summary>
    /// Количество квадратов по оси X (горизонталь).
    /// </summary>
    [ObservableProperty]
    private int _squaresX = 9;

    /// <summary>
    /// Количество квадратов по оси Y (вертикаль).
    /// </summary>
    [ObservableProperty]
    private int _squaresY = 6;

    /// <summary>
    /// Размер стороны квадрата в миллиметрах.
    /// </summary>
    [ObservableProperty]
    private float _squareLength = 30f;

    /// <summary>
    /// Размер маркера как доля от размера квадрата (0.0–1.0).
    /// </summary>
    [ObservableProperty]
    private float _markerLengthRatio = 0.73f;

    /// <summary>
    /// Словарь ArUco-маркеров.
    /// </summary>
    [ObservableProperty]
    private CharucoDictionary _dictionary = CharucoDictionary.Dict6x6_250;

    /// <summary>
    /// Разрешение выходного изображения (DPI).
    /// </summary>
    [ObservableProperty]
    private int _dpi = 300;

    /// <summary>
    /// Белое поле вокруг доски в пикселях.
    /// </summary>
    [ObservableProperty]
    private int _margin = 50;

    /// <summary>
    /// Размер маркера в миллиметрах (вычисляется из размера квадрата и коэффициента).
    /// </summary>
    public float MarkerLength => SquareLength * MarkerLengthRatio;

    /// <summary>
    /// Общая ширина доски в миллиметрах.
    /// </summary>
    public float BoardWidthMm => SquaresX * SquareLength;

    /// <summary>
    /// Общая высота доски в миллиметрах.
    /// </summary>
    public float BoardHeightMm => SquaresY * SquareLength;

    /// <summary>
    /// Ширина доски в пикселях при указанном DPI.
    /// </summary>
    public int BoardWidthPx => (int)(BoardWidthMm / 25.4f * Dpi) + 2 * Margin;

    /// <summary>
    /// Высота доски в пикселях при указанном DPI.
    /// </summary>
    public int BoardHeightPx => (int)(BoardHeightMm / 25.4f * Dpi) + 2 * Margin;
}
