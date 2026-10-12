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
    private int _squaresX = CalibrationBoardDefaults.SquaresX;

    /// <summary>
    /// Количество квадратов по оси Y (вертикаль).
    /// </summary>
    [ObservableProperty]
    private int _squaresY = CalibrationBoardDefaults.SquaresY;

    /// <summary>
    /// Размер стороны квадрата в миллиметрах.
    /// </summary>
    [ObservableProperty]
    private float _squareLength = CalibrationBoardDefaults.SquareLengthMm;

    /// <summary>
    /// Размер маркера как доля от размера квадрата (0.0–1.0).
    /// </summary>
    [ObservableProperty]
    private float _markerLengthRatio = CalibrationBoardDefaults.MarkerLengthRatio;

    /// <summary>
    /// Словарь ArUco-маркеров.
    /// </summary>
    [ObservableProperty]
    private CharucoDictionary _dictionary = CalibrationBoardDefaults.Dictionary;

    /// <summary>
    /// Разрешение выходного изображения (DPI).
    /// </summary>
    [ObservableProperty]
    private int _dpi = CalibrationBoardDefaults.Dpi;

    /// <summary>
    /// Белое поле вокруг доски в пикселях.
    /// </summary>
    [ObservableProperty]
    private int _margin = CalibrationBoardDefaults.MarginPx;

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
    /// Целочисленная сторона клетки в пикселях, используемая при отрисовке.
    /// </summary>
    public int SquareLengthPx => (int)(SquareLength / 25.4f * Dpi);

    /// <summary>
    /// Ширина доски в пикселях при указанном DPI.
    /// </summary>
    public int BoardWidthPx => SquaresX * SquareLengthPx + 2 * Margin;

    /// <summary>
    /// Высота доски в пикселях при указанном DPI.
    /// </summary>
    public int BoardHeightPx => SquaresY * SquareLengthPx + 2 * Margin;
}
