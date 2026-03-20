using System.Drawing;
using Emgu.CV;
using Emgu.CV.Aruco;
using Emgu.CV.CvEnum;
using LuminaCalib.Calibration;
using LuminaCalib.Models;

namespace LuminaCalib.Services;

/// <summary>
/// Сервис генерации калибровочных паттернов (ChArUco и шахматная доска).
/// </summary>
/// <remarks>
/// Генерирует изображения досок с физическими размерами, пересчитанными в пиксели на основе DPI.
/// Сохраняет внутренние объекты ArUco-словаря и CharucoBoard для последующего обнаружения.
/// Реализует <see cref="IDisposable"/> для освобождения нативных ресурсов.
/// </remarks>
public sealed class CharucoBoardGenerator : IDisposable
{
    /// <summary>ArUco-словарь, используемый для генерации и детекции маркеров.</summary>
    private Dictionary? _arucoDictionary;

    /// <summary>Объект доски ChArUco для обнаружения.</summary>
    private CharucoBoard? _charucoBoard;

    /// <summary>Флаг освобождения ресурсов.</summary>
    private bool _disposed;

    /// <summary>
    /// Генерирует изображение доски ChArUco.
    /// </summary>
    /// <param name="settings">Настройки генерации (размеры, DPI, словарь и т. д.).</param>
    /// <returns>Сгенерированное изображение доски в виде <see cref="Mat"/>.</returns>
    /// <remarks>
    /// Алгоритм:
    /// 1. Создаётся ArUco-словарь по указанному типу.
    /// 2. Размеры квадратов и маркеров пересчитываются из мм в пиксели через DPI.
    /// 3. Создаётся <see cref="CharucoBoard"/> и генерируется изображение.
    /// После вызова объекты словаря и доски сохраняются для <see cref="GetDetectionObjects"/>.
    /// </remarks>
    public Mat GenerateCharucoBoard(BoardGeneratorSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        // Получаем идентификатор словаря и создаём ArUco-словарь
        int dictId = CalibrationHelpers.GetArucoDictId(settings.Dictionary);
        _arucoDictionary = new Dictionary((Dictionary.PredefinedDictionaryName)dictId);

        // Пересчитываем размеры из мм в пиксели через DPI
        int squarePx = (int)(settings.SquareLength / 25.4f * settings.Dpi);

        // Создаём объект CharucoBoard с физическими размерами
        _charucoBoard = new CharucoBoard(
            settings.SquaresX,
            settings.SquaresY,
            settings.SquareLength,
            settings.MarkerLength,
            _arucoDictionary);

        var boardImage = new Mat();
        
        // Генерируем изображение доски с указанными отступами
        _charucoBoard.GenerateImage(
            new Size(settings.SquaresX * squarePx, settings.SquaresY * squarePx),
            boardImage,
            settings.Margin,
            1);

        return boardImage;
    }

    /// <summary>
    /// Генерирует стандартный шахматный паттерн (chessboard).
    /// </summary>
    /// <param name="settings">Настройки генерации (размеры, DPI и т. д.).</param>
    /// <returns>Сгенерированное изображение шахматной доски в виде <see cref="Mat"/>.</returns>
    public Mat GenerateChessboard(BoardGeneratorSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        // Пересчитываем размеры из мм в пиксели
        int squarePx = (int)(settings.SquareLength / 25.4f * settings.Dpi);
        int widthPx = settings.SquaresX * squarePx + 2 * settings.Margin;
        int heightPx = settings.SquaresY * squarePx + 2 * settings.Margin;

        // Создаём одноканальное изображение и заливаем белым
        var board = new Mat(heightPx, widthPx, DepthType.Cv8U, 1);
        board.SetTo(new Emgu.CV.Structure.MCvScalar(255)); // Белый фон

        // Рисуем чёрные клетки в шахматном порядке
        for (int y = 0; y < settings.SquaresY; y++)
        {
            for (int x = 0; x < settings.SquaresX; x++)
            {
                if ((x + y) % 2 == 0)
                {
                    // Рисуем чёрную клетку
                    var rect = new Rectangle(
                        settings.Margin + x * squarePx,
                        settings.Margin + y * squarePx,
                        squarePx,
                        squarePx);

                    CvInvoke.Rectangle(board, rect, new Emgu.CV.Structure.MCvScalar(0), -1);
                }
            }
        }

        return board;
    }

    /// <summary>
    /// Сохраняет изображение доски в формате PNG.
    /// </summary>
    /// <param name="board">Изображение доски.</param>
    /// <param name="filePath">Путь к выходному файлу.</param>
    /// <param name="compressionLevel">Уровень сжатия PNG (0–9, по умолчанию 9).</param>
    public void SaveAsPng(Mat board, string filePath, int compressionLevel = 9)
    {
        // Формируем параметры сжатия PNG
        var compressionParams = new KeyValuePair<ImwriteFlags, int>[]
        {
            new(ImwriteFlags.PngCompression, compressionLevel)
        };

        CvInvoke.Imwrite(filePath, board, compressionParams);
    }

    /// <summary>
    /// Сохраняет изображение доски в формате JPEG.
    /// </summary>
    /// <param name="board">Изображение доски.</param>
    /// <param name="filePath">Путь к выходному файлу.</param>
    /// <param name="quality">Качество JPEG (0–100, по умолчанию 95).</param>
    public void SaveAsJpeg(Mat board, string filePath, int quality = 95)
    {
        // Формируем параметры качества JPEG
        var qualityParams = new KeyValuePair<ImwriteFlags, int>[]
        {
            new(ImwriteFlags.JpegQuality, quality)
        };

        CvInvoke.Imwrite(filePath, board, qualityParams);
    }

    /// <summary>
    /// Возвращает ArUco-словарь и объект CharucoBoard для обнаружения паттерна.
    /// </summary>
    /// <returns>
    /// Кортеж из словаря и доски, или <c>null</c>, если доска ещё не была сгенерирована.
    /// </returns>
    public (Dictionary Dict, CharucoBoard Board)? GetDetectionObjects()
    {
        // Объекты доступны только после вызова GenerateCharucoBoard
        if (_arucoDictionary == null || _charucoBoard == null)
            return null;

        return (_arucoDictionary, _charucoBoard);
    }

    /// <summary>
    /// Освобождает нативные ресурсы ArUco-словаря и CharucoBoard.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _arucoDictionary?.Dispose();
        _charucoBoard?.Dispose();
    }
}
