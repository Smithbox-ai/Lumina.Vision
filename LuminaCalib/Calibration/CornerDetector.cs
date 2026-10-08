using System.Drawing;
using Emgu.CV;
using Emgu.CV.Aruco;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using Emgu.CV.Util;
using LuminaCalib.Models;

namespace LuminaCalib.Calibration;

/// <summary>
/// Сервис для обнаружения углов калибровочного паттерна (ChArUco или шахматная доска) на изображениях.
/// </summary>
/// <remarks>
/// Для ChArUco используется двухэтапный подход: сначала ArUco-маркеры, затем интерполяция углов.
/// Для шахматной доски — стандартный <c>FindChessboardCorners</c> с уточнением до суб-пиксельной точности.
/// </remarks>
public sealed class CornerDetector
{
    private readonly Size _patternSize;                     // Размер паттерна (ширина × высота)
    private readonly BoardType _boardType;                   // Тип доски (шахматная / ChArUco)
    private readonly CharucoDictionary _charucoDictionary;   // Словарь ArUco-маркеров
    private readonly float _squareLength;                    // Размер клетки (мм)
    private readonly float _markerLength;                    // Размер маркера (мм)

    private Dictionary? _arucoDictionary;       // Инициализированный словарь ArUco
    private CharucoBoard? _charucoBoard;        // ChArUco-доска
    private DetectorParameters? _detectorParams; // Параметры детектора

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="CornerDetector"/>.
    /// </summary>
    /// <param name="patternWidth">
    /// Для <see cref="BoardType.Chessboard"/> — количество внутренних углов по горизонтали (OpenCV boardSize.width).
    /// Для <see cref="BoardType.ChArUco"/> — количество клеток по горизонтали.
    /// </param>
    /// <param name="patternHeight">
    /// Для <see cref="BoardType.Chessboard"/> — количество внутренних углов по вертикали (OpenCV boardSize.height).
    /// Для <see cref="BoardType.ChArUco"/> — количество клеток по вертикали.
    /// </param>
    /// <param name="boardType">Тип калибровочной доски.</param>
    /// <param name="dictionary">Словарь ArUco-маркеров (ActualType для ChArUco).</param>
    /// <param name="squareLength">Размер клетки в миллиметрах.</param>
    /// <param name="markerLength">Размер маркера в миллиметрах (ActualType для ChArUco).</param>
    public CornerDetector(
        int patternWidth,
        int patternHeight,
        BoardType boardType = BoardType.Chessboard,
        CharucoDictionary dictionary = CharucoDictionary.Dict6x6_250,
        float squareLength = 30f,
        float markerLength = 22f)
    {
        _patternSize = new Size(patternWidth, patternHeight);
        _boardType = boardType;
        _charucoDictionary = dictionary;
        _squareLength = squareLength;
        _markerLength = markerLength;

        InitializeCharuco();
    }

    /// <summary>
    /// Инициализирует ArUco-словарь, ChArUco-доску и параметры детектора.
    /// Вызывается только для типа доски <see cref="BoardType.ChArUco"/>.
    /// </summary>
    private void InitializeCharuco()
    {
        if (_boardType == BoardType.ChArUco)
        {
            // Получаем числовой id словаря для совместимости с EmguCV
            int dictId = CalibrationHelpers.GetArucoDictId(_charucoDictionary);
            _arucoDictionary = new Dictionary((Dictionary.PredefinedDictionaryName)dictId);
            // Создаём ChArUco-доску с заданными размерами
            _charucoBoard = new CharucoBoard(
                _patternSize.Width,
                _patternSize.Height,
                _squareLength,
                _markerLength,
                _arucoDictionary);
            _detectorParams = DetectorParameters.GetDefault();
        }
    }

    /// <summary>
    /// Обнаруживает углы калибровочного паттерна на изображении.
    /// Диспетчеризует вызов между ChArUco и шахматной доской.
    /// </summary>
    /// <param name="image">Входное изображение (BGR или градации серого).</param>
    /// <param name="corners">Обнаруженные угловые точки (выход).</param>
    /// <param name="ids">Идентификаторы ArUco-маркеров для ChArUco (<c>null</c> для шахматной доски).</param>
    /// <param name="detectionScale">Масштаб (0..1) для ускорения детекции. 1.0 — полное разрешение.</param>
    /// <returns><c>true</c>, если паттерн найден.</returns>
    public bool DetectCorners(Mat image, out PointF[] corners, out int[]? ids, float detectionScale = 1.0f)
    {
        corners = [];
        ids = null;

        if (image == null || image.IsEmpty) return false;

        // Преобразуем в градации серого, если необходимо
        using var gray = new Mat();
        if (image.NumberOfChannels == 3)
        {
            CvInvoke.CvtColor(image, gray, ColorConversion.Bgr2Gray);
        }
        else
        {
            image.CopyTo(gray);
        }

        // Уменьшение разрешения для ускорения детекции
        bool useDownscale = detectionScale is > 0f and < 1f;
        Mat detectionGray = gray;
        Mat? smallGray = null;
        try
        {
            if (useDownscale)
            {
                smallGray = new Mat();
                CvInvoke.Resize(gray, smallGray, new Size(0, 0), detectionScale, detectionScale, Inter.Area);
                detectionGray = smallGray;
            }

            bool found;
            // Диспетчеризация по типу доски
            if (_boardType == BoardType.ChArUco)
            {
                found = DetectCharucoCorners(detectionGray, out corners, out ids);
            }
            else
            {
                // При уменьшении — пропускаем CornerSubPix (будет только грубая детекция для проверки наличия)
                found = DetectChessboardCorners(detectionGray, out corners, skipSubPix: useDownscale);
            }

            // Масштабирование координат углов обратно к исходному разрешению
            if (found && useDownscale && corners.Length > 0)
            {
                float invScale = 1f / detectionScale;
                for (int i = 0; i < corners.Length; i++)
                {
                    corners[i] = new PointF(corners[i].X * invScale, corners[i].Y * invScale);
                }
            }

            return found;
        }
        finally
        {
            smallGray?.Dispose();
        }
    }

    /// <summary>
    /// Обнаруживает углы шахматной доски с уточнением до суб-пиксельной точности.
    /// </summary>
    /// <param name="gray">Изображение в градациях серого.</param>
    /// <param name="corners">Обнаруженные углы (выход).</param>
    /// <param name="skipSubPix">Пропустить уточнение CornerSubPix (при детекции на уменьшенном изображении).</param>
    /// <returns><c>true</c>, если шахматная доска найдена.</returns>
    private bool DetectChessboardCorners(Mat gray, out PointF[] corners, bool skipSubPix = false)
    {
        corners = [];

        using var cornerMat = new Mat();
        // Для шахматной доски ожидаются внутренние углы (как в OpenCV boardSize).
        var innerCornerSize = _patternSize;
        bool found = CvInvoke.FindChessboardCorners(
            gray,
            innerCornerSize,
            cornerMat,
            CalibCbType.AdaptiveThresh | CalibCbType.NormalizeImage);

        if (found && !cornerMat.IsEmpty)
        {
            if (!skipSubPix)
            {
                // Уточняем координаты углов до суб-пиксельной точности
                var termCriteria = new MCvTermCriteria(30, 0.001);
                CvInvoke.CornerSubPix(gray, cornerMat, new Size(11, 11), new Size(-1, -1), termCriteria);
            }

            // Преобразуем Mat в массив PointF
            corners = new PointF[cornerMat.Rows * cornerMat.Cols];
            var data = cornerMat.GetData();
            for (int i = 0; i < corners.Length; i++)
            {
                corners[i] = new PointF(
                    (float)data.GetValue(i, 0, 0)!,
                    (float)data.GetValue(i, 0, 1)!);
            }
        }

        return found;
    }

    /// <summary>
    /// Обнаруживает углы ChArUco-доски через двухэтапный процесс:
    /// сначала ArUco-маркеры, затем интерполяция ChArUco-углов.
    /// </summary>
    /// <param name="gray">Изображение в градациях серого.</param>
    /// <param name="corners">Обнаруженные углы (выход).</param>
    /// <param name="ids">Идентификаторы углов (выход).</param>
    /// <returns><c>true</c>, если найдено не менее 4 углов.</returns>
    private bool DetectCharucoCorners(Mat gray, out PointF[] corners, out int[]? ids)
    {
        corners = [];
        ids = null;

        if (_arucoDictionary == null || _charucoBoard == null || _detectorParams == null)
            return false;

        using var charucoCorners = new VectorOfPointF();
        using var charucoIds = new VectorOfInt();
        using var parameters = new CharucoParameters(2, false, true);
        using var detector = new CharucoDetector(_charucoBoard, parameters,
            (DetectorParameters)_detectorParams, new RefineParameters(10f, 3f, true));
        EmguCharucoDetection.DetectBoard(detector, gray, charucoCorners, charucoIds);

        // Минимум 4 угла для корректной калибровки
        if (charucoCorners.Size < 4) return false;

        // Преобразуем результаты в массивы
        corners = charucoCorners.ToArray();
        ids = charucoIds.ToArray();

        return true;
    }

    /// <summary>
    /// Отрисовывает обнаруженные углы на изображении для визуального контроля.
    /// </summary>
    /// <param name="image">Изображение для отрисовки (модифицируется на месте).</param>
    /// <param name="corners">Массив обнаруженных углов.</param>
    /// <param name="patternFound">Флаг, найден ли паттерн (влияет на цвет отрисовки).</param>
    /// <param name="ids">Идентификаторы ChArUco-углов (опционально).</param>
    public void DrawCorners(Mat image, PointF[] corners, bool patternFound, int[]? ids = null)
    {
        if (image == null || image.IsEmpty || corners.Length == 0) return;

        if (_boardType == BoardType.ChArUco && ids != null)
        {
            using var cornersMat = new VectorOfPointF(corners);
            using var idsMat = new VectorOfInt(ids);
            ArucoInvoke.DrawDetectedCornersCharuco(image, cornersMat, idsMat, new MCvScalar(0, 255, 0));
        }
        else
        {
            var innerSize = _patternSize;
            using var cornersMat = new VectorOfPointF(corners);
            CvInvoke.DrawChessboardCorners(image, innerSize, cornersMat, patternFound);
        }
    }

    /// <summary>
    /// Генерирует массив 3D-объектных точек для калибровочного паттерна.
    /// </summary>
    /// <returns>Массив 3D-точек в миллиметрах (плоскость Z=0).</returns>
    /// <remarks>
    /// Для <see cref="BoardType.ChArUco"/> углы располагаются на внутренних пересечениях клеток.
    /// Для <see cref="BoardType.Chessboard"/> точки формируются как регулярная сетка внутренних углов.
    /// </remarks>
    public MCvPoint3D32f[] GenerateObjectPoints()
    {
        var points = new List<MCvPoint3D32f>();

        if (_boardType == BoardType.ChArUco)
        {
            // ChArUco: углы на чередующихся клетках
            for (int y = 0; y < _patternSize.Height - 1; y++)
            {
                for (int x = 0; x < _patternSize.Width - 1; x++)
                {
                    points.Add(new MCvPoint3D32f(
                        x * _squareLength + _squareLength,
                        y * _squareLength + _squareLength,
                        0));
                }
            }
        }
        else
        {
            // Шахматная доска: W*H внутренних углов (OpenCV boardSize).
            for (int y = 0; y < _patternSize.Height; y++)
            {
                for (int x = 0; x < _patternSize.Width; x++)
                {
                    points.Add(new MCvPoint3D32f(
                        x * _squareLength,
                        y * _squareLength,
                        0));
                }
            }
        }

        return points.ToArray();
    }
}
