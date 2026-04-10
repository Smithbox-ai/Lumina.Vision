using System.Drawing;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using Emgu.CV.Util;
using LuminaCalib.Models;

namespace LuminaCalib.Calibration;

/// <summary>
/// Основной движок калибровки стереопары камер.
/// Поддерживает как калибровку одиночной камеры, так и полную стерео-калибровку.
/// </summary>
/// <remarks>
/// Калибровка выполняется в три этапа:
/// <list type="number">
///   <item><description>Этап 1: параллельная калибровка каждой камеры по отдельности (intrinsics — внутренние параметры).</description></item>
///   <item><description>Этап 2: стерео-калибровка с флагом <c>FixIntrinsic</c> — оптимизируются только внешние параметры (extrinsics: R, T, E, F).</description></item>
///   <item><description>Этап 3: <c>StereoRectify</c> — вычисление матриц ректификации (R1, R2, P1, P2, Q).</description></item>
/// </list>
/// Для ChArUco-досок поддерживается сопоставление угловых точек по идентификаторам маркеров.
/// </remarks>
public sealed class CalibrationEngine : IDisposable
{
    internal readonly record struct StereoPairSelection(int[] InlierIndices, int FilteredOutPairCount);

    internal const int ZeroDisparityStereoRectifyFlagValue = 0x400;
    internal static StereoRectifyType StereoRectifyFlags => (StereoRectifyType)((int)StereoRectifyType.Default | ZeroDisparityStereoRectifyFlagValue);

    private const int MaxCalibrationIterations = 3;
    private const int MinPairsFloor = 12;
    private const double IterativeYErrorThreshold = 1.0;

    private readonly CornerDetector _cornerDetector;
    private readonly List<VectorOfPointF> _leftImagePoints = [];      // 2D-точки левой камеры
    private readonly List<VectorOfPointF> _rightImagePoints = [];     // 2D-точки правой камеры
    private readonly List<VectorOfPoint3D32F> _stereoObjectPoints = []; // 3D-точки для стерео-пар
    private readonly List<VectorOfPoint3D32F> _leftObjectPoints = [];   // 3D-точки для одиночной калибровки левой камеры
    private readonly List<VectorOfPoint3D32F> _rightObjectPoints = [];  // 3D-точки для одиночной калибровки правой камеры
    private readonly MCvPoint3D32f[] _objectPointTemplate;             // Шаблон 3D-координат углов паттерна
    private Size _imageSize;  // Размер изображения (устанавливается по первому кадру)
    private bool _disposed;

    /// <summary>
    /// Событие обновления прогресса калибровки.
    /// </summary>
    /// <remarks>
    /// Первый параметр — текущий прогресс, второй — максимальное значение (обычно 100).
    /// </remarks>
    public event Action<int, int>? OnProgress;

    /// <summary>
    /// Событие смены этапа калибровки.
    /// </summary>
    /// <remarks>
    /// Строковый параметр содержит описание текущего этапа (например, "Вычисление внешних параметров...").
    /// </remarks>
    public event Action<string>? OnStageChanged;

    /// <summary>
    /// Флаги калибровки внутренних параметров камеры.
    /// По умолчанию — <see cref="CalibType.Default"/>.
    /// Установите <see cref="CalibType.RationalModel"/> для линз со значительной дисторсией (k4, k5, k6).
    /// </summary>
    public CalibType IntrinsicCalibrationFlags { get; set; } = CalibType.Default;

    /// <summary>
    /// Количество захваченных стерео-пар изображений.
    /// </summary>
    public int CapturedPairCount => _leftImagePoints.Count;

    /// <summary>
    /// Детектор углов, используемый данным движком.
    /// </summary>
    public CornerDetector CornerDetector => _cornerDetector;

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="CalibrationEngine"/>.
    /// </summary>
    /// <param name="cornerDetector">Детектор углов калибровочного паттерна.</param>
    /// <exception cref="ArgumentNullException">Если <paramref name="cornerDetector"/> равен <c>null</c>.</exception>
    public CalibrationEngine(CornerDetector cornerDetector)
    {
        _cornerDetector = cornerDetector ?? throw new ArgumentNullException(nameof(cornerDetector));
        // Генерируем шаблон 3D-координат углов один раз при создании
        _objectPointTemplate = _cornerDetector.GenerateObjectPoints();
    }

    /// <summary>
    /// Добавляет стерео-пару изображений для калибровки.
    /// </summary>
    /// <param name="leftImage">Изображение с левой камеры.</param>
    /// <param name="rightImage">Изображение с правой камеры.</param>
    /// <returns><c>true</c>, если углы паттерна найдены на обоих изображениях и успешно сопоставлены.</returns>
    /// <remarks>
    /// Для ChArUco-досок выполняется пересечение идентификаторов точек между левым и правым кадрами
    /// (метод <see cref="TryBuildStereoPoints"/>). Размер изображения фиксируется по первому вызову.
    /// </remarks>
    public bool AddImagePair(Mat leftImage, Mat rightImage)
    {
        // Фиксируем размер изображения по первому кадру
        if (_imageSize.IsEmpty)
        {
            _imageSize = leftImage.Size;
        }

        // Детектируем углы на левом и правом изображениях
        bool leftFound = _cornerDetector.DetectCorners(leftImage, out var leftCorners, out var leftIds);
        bool rightFound = _cornerDetector.DetectCorners(rightImage, out var rightCorners, out var rightIds);

        if (!leftFound || !rightFound)
        {
            return false;
        }

        // Строим согласованные массивы точек для стерео-калибровки
        if (!TryBuildStereoPoints(
                leftCorners, leftIds,
                rightCorners, rightIds,
                out var alignedLeftPoints,
                out var alignedRightPoints,
                out var alignedObjectPoints))
        {
            return false;
        }

        // Сохраняем согласованные наборы точек
        _leftImagePoints.Add(new VectorOfPointF(alignedLeftPoints));
        _rightImagePoints.Add(new VectorOfPointF(alignedRightPoints));
        _stereoObjectPoints.Add(new VectorOfPoint3D32F(alignedObjectPoints));
        return true;
    }

    /// <summary>
    /// Добавляет изображение одиночной камеры для калибровки внутренних параметров.
    /// </summary>
    /// <param name="image">Изображение с камеры.</param>
    /// <param name="isLeft"><c>true</c> — левая камера, <c>false</c> — правая.</param>
    /// <returns><c>true</c>, если углы паттерна найдены и точки успешно сопоставлены.</returns>
    public bool AddSingleImage(Mat image, bool isLeft)
    {
        // Фиксируем размер изображения по первому кадру
        if (_imageSize.IsEmpty)
        {
            _imageSize = image.Size;
        }

        // Детектируем углы паттерна
        bool found = _cornerDetector.DetectCorners(image, out var corners, out var ids);

        if (!found)
        {
            return false;
        }

        // Строим согласованные массивы 2D- и 3D-точек
        if (!TryBuildImageAndObjectPoints(corners, ids, out var alignedImagePoints, out var alignedObjectPoints))
        {
            return false;
        }

        // Выбираем целевые списки в зависимости от камеры
        var targetImagePoints = isLeft ? _leftImagePoints : _rightImagePoints;
        var targetObjectPoints = isLeft ? _leftObjectPoints : _rightObjectPoints;

        targetImagePoints.Add(new VectorOfPointF(alignedImagePoints));
        targetObjectPoints.Add(new VectorOfPoint3D32F(alignedObjectPoints));

        return true;
    }

    /// <summary>
    /// Выполняет калибровку одиночной камеры (только внутренние параметры).
    /// </summary>
    /// <param name="isLeft"><c>true</c> — калибровать левую камеру, <c>false</c> — правую.</param>
    /// <returns>Результат калибровки с матрицей камеры и коэффициентами дисторсии.</returns>
    /// <exception cref="InvalidOperationException">Если добавлено менее 10 изображений или данные несогласованы.</exception>
    public SingleCameraCalibrationResult CalibrateSingleCamera(bool isLeft)
    {
        var imagePoints = isLeft ? _leftImagePoints : _rightImagePoints;
        var objectPoints = isLeft ? _leftObjectPoints : _rightObjectPoints;

        // Проверка минимального количества изображений
        if (imagePoints.Count < 10)
        {
            throw new InvalidOperationException("At least 10 images required for calibration.");
        }

        // Проверка согласованности 2D- и 3D-наборов
        if (objectPoints.Count != imagePoints.Count)
        {
            throw new InvalidOperationException(
                $"Inconsistent calibration data: image sets={imagePoints.Count}, object sets={objectPoints.Count}.");
        }

        // Инициализация выходных матриц
        var cameraMatrix = new Mat(3, 3, DepthType.Cv64F, 1);
        var distCoeffs = new Mat();
        var rvecs = new VectorOfMat();
        var tvecs = new VectorOfMat();

        using var objPtsVec = new VectorOfVectorOfPoint3D32F(objectPoints.ToArray());
        using var imgPtsVec = new VectorOfVectorOfPointF(imagePoints.ToArray());

        // Выполняем калибровку с параметрами по умолчанию

        double error = CvInvoke.CalibrateCamera(
            objPtsVec,
            imgPtsVec,
            _imageSize,
            cameraMatrix,
            distCoeffs,
            rvecs,
            tvecs,
            IntrinsicCalibrationFlags,
            new MCvTermCriteria(100, 1e-6));

        return new SingleCameraCalibrationResult
        {
            CameraMatrix = cameraMatrix,
            DistCoeffs = distCoeffs,
            ReprojectionError = error,
            ImageSize = _imageSize,
            ImageCount = imagePoints.Count
        };
    }

    /// <summary>
    /// Выполняет полную стерео-калибровку с использованием рекомендованного двухэтапного подхода.
    /// </summary>
    /// <returns>Результат калибровки с внутренними и внешними параметрами, матрицами ректификации.</returns>
    /// <exception cref="InvalidOperationException">Если добавлено менее 10 стерео-пар или данные несогласованы.</exception>
    /// <remarks>
    /// Этап 1: параллельная калибровка каждой камеры (intrinsics) — <c>CvInvoke.CalibrateCamera</c>.
    /// Этап 2: стерео-калибровка с <c>FixIntrinsic</c> — <c>CvInvoke.StereoCalibrate</c>.
    /// Этап 3: вычисление ректификации — <see cref="CvInvoke.StereoRectify"/>.
    /// </remarks>
    public CalibrationResult CalibrateFullStereo()
    {
        // Проверка минимального количества данных
        if (_leftImagePoints.Count < 10
            || _rightImagePoints.Count < 10
            || _stereoObjectPoints.Count < 10)
        {
            throw new InvalidOperationException("At least 10 image pairs required for stereo calibration.");
        }

        if (_leftImagePoints.Count != _rightImagePoints.Count || _leftImagePoints.Count != _stereoObjectPoints.Count)
        {
            throw new InvalidOperationException(
                $"Inconsistent stereo calibration data: left={_leftImagePoints.Count}, right={_rightImagePoints.Count}, object={_stereoObjectPoints.Count}.");
        }

        OnProgress?.Invoke(0, 100);
        OnStageChanged?.Invoke("Calibrating individual camera intrinsics...");

        // ====== ЭТАП 1: Параллельная калибровка внутренних параметров каждой камеры ======

        // Преобразуем списки в массивы для безопасного параллельного доступа
        var objectPointsArray = _stereoObjectPoints.ToArray();
        var leftPointsArray = _leftImagePoints.ToArray();
        var rightPointsArray = _rightImagePoints.ToArray();

        // Матрицы камер и коэффициенты дисторсии
        var cameraMatrixLeft = new Mat(3, 3, DepthType.Cv64F, 1);
        var distCoeffsLeft = new Mat();
        double leftError = 0;
        using var leftRvecs = new VectorOfMat();
        using var leftTvecs = new VectorOfMat();

        var cameraMatrixRight = new Mat(3, 3, DepthType.Cv64F, 1);
        var distCoeffsRight = new Mat();
        double rightError = 0;
        using var rightRvecs = new VectorOfMat();
        using var rightTvecs = new VectorOfMat();

        // Параллельно калибруем левую и правую камеры
        Parallel.Invoke(
            () =>
            {
                using var objPts = new VectorOfVectorOfPoint3D32F(objectPointsArray);
                using var imgPts = new VectorOfVectorOfPointF(leftPointsArray);

                leftError = CvInvoke.CalibrateCamera(
                    objPts, imgPts, _imageSize,
                    cameraMatrixLeft, distCoeffsLeft,
                    leftRvecs, leftTvecs,
                    IntrinsicCalibrationFlags,
                    new MCvTermCriteria(100, 1e-6));
            },
            () =>
            {
                using var objPts = new VectorOfVectorOfPoint3D32F(objectPointsArray);
                using var imgPts = new VectorOfVectorOfPointF(rightPointsArray);

                rightError = CvInvoke.CalibrateCamera(
                    objPts, imgPts, _imageSize,
                    cameraMatrixRight, distCoeffsRight,
                    rightRvecs, rightTvecs,
                    IntrinsicCalibrationFlags,
                    new MCvTermCriteria(100, 1e-6));
            });

        double[] leftPerViewErrors;
        double[] rightPerViewErrors;
        StereoPairSelection pairSelection;

        using (var objectPointsVec = new VectorOfVectorOfPoint3D32F(objectPointsArray))
        using (var leftPointsVec = new VectorOfVectorOfPointF(leftPointsArray))
        using (var rightPointsVec = new VectorOfVectorOfPointF(rightPointsArray))
        {
            leftPerViewErrors = ComputePerViewReprojectionErrors(
                objectPointsVec,
                leftPointsVec,
                cameraMatrixLeft,
                distCoeffsLeft,
                leftRvecs,
                leftTvecs);

            rightPerViewErrors = ComputePerViewReprojectionErrors(
                objectPointsVec,
                rightPointsVec,
                cameraMatrixRight,
                distCoeffsRight,
                rightRvecs,
                rightTvecs);

            pairSelection = SelectPairsForStereoCalibration(leftPerViewErrors, rightPerViewErrors);
        }

        var perViewErrors = leftPerViewErrors
            .Zip(rightPerViewErrors, (left, right) => (left + right) / 2.0)
            .ToArray();

        var inlierObjectPoints = pairSelection.InlierIndices
            .Select(index => objectPointsArray[index])
            .ToArray();

        var inlierLeftPoints = pairSelection.InlierIndices
            .Select(index => leftPointsArray[index])
            .ToArray();

        var inlierRightPoints = pairSelection.InlierIndices
            .Select(index => rightPointsArray[index])
            .ToArray();

        // Convert inlier arrays to Lists for easy removal during iteration
        var currentObjectPoints = new List<VectorOfPoint3D32F>(inlierObjectPoints);
        var currentLeftPoints = new List<VectorOfPointF>(inlierLeftPoints);
        var currentRightPoints = new List<VectorOfPointF>(inlierRightPoints);

        Mat R = null!, T = null!, E = null!, F = null!;
        Mat R1 = null!, R2 = null!, P1 = null!, P2 = null!, Q = null!;
        double stereoError = 0;
        double epipolarYError = double.NaN;
        int iterationsPerformed = 0;

        for (int iteration = 0; iteration <= MaxCalibrationIterations; iteration++)
        {
            // Dispose previous iteration results (if any)
            if (iteration > 0)
            {
                R?.Dispose(); T?.Dispose(); E?.Dispose(); F?.Dispose();
                R1?.Dispose(); R2?.Dispose(); P1?.Dispose(); P2?.Dispose(); Q?.Dispose();
            }

            OnProgress?.Invoke(50, 100);
            OnStageChanged?.Invoke(iteration == 0
                ? "Computing stereo extrinsics..."
                : $"Iterative recalibration (iteration {iteration})...");

            // ====== ЭТАП 2: Стерео-калибровка с фиксированными внутренними параметрами ======

            R = new Mat(); T = new Mat(); E = new Mat(); F = new Mat();

            using (var objPtsVec = new VectorOfVectorOfPoint3D32F(currentObjectPoints.ToArray()))
            using (var leftPtsVec = new VectorOfVectorOfPointF(currentLeftPoints.ToArray()))
            using (var rightPtsVec = new VectorOfVectorOfPointF(currentRightPoints.ToArray()))
            {
                stereoError = CvInvoke.StereoCalibrate(
                    objPtsVec,
                    leftPtsVec,
                    rightPtsVec,
                    cameraMatrixLeft,
                    distCoeffsLeft,
                    cameraMatrixRight,
                    distCoeffsRight,
                    _imageSize,
                    R, T, E, F,
                    CalibType.FixIntrinsic,
                    new MCvTermCriteria(100, 1e-6));
            }

            OnProgress?.Invoke(80, 100);
            OnStageChanged?.Invoke("Computing rectification...");

            // ====== ЭТАП 3: Вычисление матриц ректификации ======

            R1 = new Mat(); R2 = new Mat(); P1 = new Mat(); P2 = new Mat(); Q = new Mat();
            Rectangle validPixRoi1 = default;
            Rectangle validPixRoi2 = default;

            CvInvoke.StereoRectify(
                cameraMatrixLeft,
                distCoeffsLeft,
                cameraMatrixRight,
                distCoeffsRight,
                _imageSize,
                R, T,
                R1, R2, P1, P2, Q,
                StereoRectifyFlags,
                -1,
                _imageSize,
                ref validPixRoi1,
                ref validPixRoi2);

            // Check if we should attempt to iterate
            if (iteration >= MaxCalibrationIterations || currentLeftPoints.Count <= MinPairsFloor)
                break;

            // Compute per-view epipolar Y-errors to find worst pair
            try
            {
                using var mlx = new Mat();
                using var mly = new Mat();
                using var mrx = new Mat();
                using var mry = new Mat();

                CvInvoke.InitUndistortRectifyMap(
                    cameraMatrixLeft, distCoeffsLeft, R1, P1,
                    _imageSize, DepthType.Cv32F, 1, mlx, mly);
                CvInvoke.InitUndistortRectifyMap(
                    cameraMatrixRight, distCoeffsRight, R2, P2,
                    _imageSize, DepthType.Cv32F, 1, mrx, mry);

                var lSets = currentLeftPoints.Select(v => v.ToArray()).ToArray();
                var rSets = currentRightPoints.Select(v => v.ToArray()).ToArray();
                var perViewYErrors = CalibrationValidator.ComputePerViewEpipolarYErrors(
                    lSets, rSets, mlx, mly, mrx, mry);

                int worstIndex = Array.IndexOf(perViewYErrors, perViewYErrors.Max());
                if (perViewYErrors[worstIndex] < IterativeYErrorThreshold)
                    break;  // All pairs are good enough

                // Remove worst pair
                currentObjectPoints.RemoveAt(worstIndex);
                currentLeftPoints.RemoveAt(worstIndex);
                currentRightPoints.RemoveAt(worstIndex);
                iterationsPerformed++;
            }
            catch
            {
                break;  // Can't compute Y-errors, stop iterating
            }
        }

        // Compute final epipolar Y-error on the final calibration
        try
        {
            using var mapLeftX = new Mat();
            using var mapLeftY = new Mat();
            using var mapRightX = new Mat();
            using var mapRightY = new Mat();

            CvInvoke.InitUndistortRectifyMap(
                cameraMatrixLeft, distCoeffsLeft, R1, P1,
                _imageSize, DepthType.Cv32F, 1, mapLeftX, mapLeftY);
            CvInvoke.InitUndistortRectifyMap(
                cameraMatrixRight, distCoeffsRight, R2, P2,
                _imageSize, DepthType.Cv32F, 1, mapRightX, mapRightY);

            var leftPointSets = currentLeftPoints.Select(v => v.ToArray()).ToArray();
            var rightPointSets = currentRightPoints.Select(v => v.ToArray()).ToArray();

            epipolarYError = CalibrationValidator.ComputeEpipolarYError(
                leftPointSets, rightPointSets,
                mapLeftX, mapLeftY, mapRightX, mapRightY);
        }
        catch
        {
            // Non-critical metric — if it fails, leave NaN
        }

        OnProgress?.Invoke(100, 100);

        return new CalibrationResult
        {
            CameraMatrixLeft = cameraMatrixLeft,
            CameraMatrixRight = cameraMatrixRight,
            DistCoeffsLeft = distCoeffsLeft,
            DistCoeffsRight = distCoeffsRight,
            R = R,
            T = T,
            E = E,
            F = F,
            R1 = R1,
            R2 = R2,
            P1 = P1,
            P2 = P2,
            Q = Q,
            ReprojectionError = stereoError,
            LeftIntrinsicError = leftError,
            RightIntrinsicError = rightError,
            PerViewErrors = perViewErrors,
            FilteredOutPairCount = pairSelection.FilteredOutPairCount + iterationsPerformed,
            EpipolarYError = epipolarYError,
            ImageSize = _imageSize,
            ImagePairCount = _leftImagePoints.Count,
            CalibrationDate = DateTime.Now
        };
    }

    internal static double[] ComputePerViewReprojectionErrors(
        VectorOfVectorOfPoint3D32F objectPoints,
        VectorOfVectorOfPointF imagePoints,
        Mat cameraMatrix,
        Mat distCoeffs,
        VectorOfMat rvecs,
        VectorOfMat tvecs)
    {
        if (objectPoints.Size != imagePoints.Size || objectPoints.Size != rvecs.Size || objectPoints.Size != tvecs.Size)
        {
            throw new ArgumentException("Per-view calibration vectors must have matching lengths.");
        }

        var viewCount = objectPoints.Size;
        var errors = new double[viewCount];

        for (var i = 0; i < viewCount; i++)
        {
            using var objectView = objectPoints[i];
            using var imageView = imagePoints[i];
            using var rvec = rvecs[i];
            using var tvec = tvecs[i];
            using var projected = new VectorOfPointF();

            CvInvoke.ProjectPoints(objectView, rvec, tvec, cameraMatrix, distCoeffs, projected);

            var observedPoints = imageView.ToArray();
            var projectedPoints = projected.ToArray();

            if (observedPoints.Length == 0 || observedPoints.Length != projectedPoints.Length)
            {
                throw new InvalidOperationException("Per-view points are empty or misaligned.");
            }

            double sumSquared = 0;
            for (var pointIndex = 0; pointIndex < observedPoints.Length; pointIndex++)
            {
                var dx = observedPoints[pointIndex].X - projectedPoints[pointIndex].X;
                var dy = observedPoints[pointIndex].Y - projectedPoints[pointIndex].Y;
                sumSquared += (dx * dx) + (dy * dy);
            }

            errors[i] = Math.Sqrt(sumSquared / observedPoints.Length);
        }

        return errors;
    }

    internal static int[] SelectInlierPairIndices(double[] leftErrors, double[] rightErrors, double madMultiplier = 2.5)
    {
        if (leftErrors is null)
        {
            throw new ArgumentNullException(nameof(leftErrors));
        }

        if (rightErrors is null)
        {
            throw new ArgumentNullException(nameof(rightErrors));
        }

        if (leftErrors.Length != rightErrors.Length)
        {
            throw new ArgumentException("Left and right error arrays must have the same length.");
        }

        if (leftErrors.Length == 0)
        {
            return [];
        }

        var leftMedian = ComputeMedian(leftErrors);
        var rightMedian = ComputeMedian(rightErrors);
        var leftMad = ComputeMedian(leftErrors.Select(error => Math.Abs(error - leftMedian)).ToArray());
        var rightMad = ComputeMedian(rightErrors.Select(error => Math.Abs(error - rightMedian)).ToArray());

        var leftThreshold = leftMedian + (madMultiplier * Math.Max(leftMad, 0.1));
        var rightThreshold = rightMedian + (madMultiplier * Math.Max(rightMad, 0.1));

        return Enumerable.Range(0, leftErrors.Length)
            .Where(index => leftErrors[index] <= leftThreshold && rightErrors[index] <= rightThreshold)
            .ToArray();
    }

    internal static StereoPairSelection SelectPairsForStereoCalibration(
        double[] leftErrors,
        double[] rightErrors,
        int minInlierPairs = 10,
        double madMultiplier = 2.5)
    {
        var inlierIndices = SelectInlierPairIndices(leftErrors, rightErrors, madMultiplier);

        if (inlierIndices.Length < minInlierPairs)
        {
            return new StereoPairSelection(Enumerable.Range(0, leftErrors.Length).ToArray(), 0);
        }

        return new StereoPairSelection(inlierIndices, leftErrors.Length - inlierIndices.Length);
    }

    private static double ComputeMedian(double[] values)
    {
        if (values.Length == 0)
        {
            return 0;
        }

        var sorted = values.OrderBy(value => value).ToArray();
        var middle = sorted.Length / 2;

        if ((sorted.Length % 2) == 0)
        {
            return (sorted[middle - 1] + sorted[middle]) / 2.0;
        }

        return sorted[middle];
    }

    /// <summary>
    /// Выполняет стерео-калибровку с использованием предварительно вычисленных внутренних параметров (только внешние параметры).
    /// </summary>
    /// <param name="cameraMatrixLeft">Матрица левой камеры (3×3, CV_64F).</param>
    /// <param name="distCoeffsLeft">Коэффициенты дисторсии левой камеры.</param>
    /// <param name="cameraMatrixRight">Матрица правой камеры (3×3, CV_64F).</param>
    /// <param name="distCoeffsRight">Коэффициенты дисторсии правой камеры.</param>
    /// <returns>Результат калибровки с внешними параметрами и матрицами ректификации.</returns>
    /// <exception cref="InvalidOperationException">Если добавлено менее 10 стерео-пар или данные несогласованы.</exception>
    /// <remarks>
    /// Полезно, когда внутренние параметры уже получены методом <see cref="CalibrateSingleCamera"/>.
    /// Используется <c>CalibType.FixIntrinsic</c> для оптимизации только внешних параметров.
    /// </remarks>
    public CalibrationResult CalibrateStereoWithIntrinsics(
        Mat cameraMatrixLeft, Mat distCoeffsLeft,
        Mat cameraMatrixRight, Mat distCoeffsRight)
    {
        // Проверка минимального количества данных
        if (_leftImagePoints.Count < 10
            || _rightImagePoints.Count < 10
            || _stereoObjectPoints.Count < 10)
        {
            throw new InvalidOperationException("At least 10 image pairs required for stereo calibration.");
        }

        // Проверка согласованности наборов данных
        if (_leftImagePoints.Count != _rightImagePoints.Count || _leftImagePoints.Count != _stereoObjectPoints.Count)
        {
            throw new InvalidOperationException(
                $"Inconsistent stereo calibration data: left={_leftImagePoints.Count}, right={_rightImagePoints.Count}, object={_stereoObjectPoints.Count}.");
        }

        // Матрицы внешних параметров
        var R = new Mat();
        var T = new Mat();
        var E = new Mat();
        var F = new Mat();

        using var objPtsVec = new VectorOfVectorOfPoint3D32F(_stereoObjectPoints.ToArray());
        using var leftPtsVec = new VectorOfVectorOfPointF(_leftImagePoints.ToArray());
        using var rightPtsVec = new VectorOfVectorOfPointF(_rightImagePoints.ToArray());

        OnProgress?.Invoke(0, 100);

        // Стерео-калибровка с фиксированными внутренними параметрами — оптимизируем только extrinsics
        double error = CvInvoke.StereoCalibrate(
            objPtsVec,
            leftPtsVec,
            rightPtsVec,
            cameraMatrixLeft,
            distCoeffsLeft,
            cameraMatrixRight,
            distCoeffsRight,
            _imageSize,
            R,
            T,
            E,
            F,
            CalibType.FixIntrinsic,
            new MCvTermCriteria(100, 1e-6));

        OnProgress?.Invoke(70, 100);

        // Вычисление матриц ректификации
        var R1 = new Mat();
        var R2 = new Mat();
        var P1 = new Mat();
        var P2 = new Mat();
        var Q = new Mat();
        Rectangle validPixRoi1 = default;
        Rectangle validPixRoi2 = default;

        CvInvoke.StereoRectify(
            cameraMatrixLeft,
            distCoeffsLeft,
            cameraMatrixRight,
            distCoeffsRight,
            _imageSize,
            R,
            T,
            R1,
            R2,
            P1,
            P2,
            Q,
            StereoRectifyFlags,
            -1,
            _imageSize,
            ref validPixRoi1,
            ref validPixRoi2);

        OnProgress?.Invoke(100, 100);

        return new CalibrationResult
        {
            CameraMatrixLeft = cameraMatrixLeft.Clone(),
            CameraMatrixRight = cameraMatrixRight.Clone(),
            DistCoeffsLeft = distCoeffsLeft.Clone(),
            DistCoeffsRight = distCoeffsRight.Clone(),
            R = R,
            T = T,
            E = E,
            F = F,
            R1 = R1,
            R2 = R2,
            P1 = P1,
            P2 = P2,
            Q = Q,
            ReprojectionError = error,
            ImageSize = _imageSize,
            ImagePairCount = _leftImagePoints.Count,
            CalibrationDate = DateTime.Now
        };
    }

    /// <summary>
    /// Пытается построить согласованные массивы 2D-изображений и 3D-объектных точек для одиночной камеры.
    /// </summary>
    /// <param name="imagePoints">Обнаруженные 2D-углы на изображении.</param>
    /// <param name="ids">Идентификаторы ChArUco-углов (<c>null</c> для шахматной доски).</param>
    /// <param name="alignedImagePoints">Согласованный массив 2D-точек (выход).</param>
    /// <param name="alignedObjectPoints">Согласованный массив 3D-точек (выход).</param>
    /// <returns><c>true</c>, если удалось построить корректные наборы точек (минимум 4).</returns>
    /// <remarks>
    /// Для ChArUco: идентификаторы определяют соответствие 2D-углов и 3D-координат из шаблона.
    /// Для шахматной доски: ожидается полная фиксированная сетка углов.
    /// </remarks>
    private bool TryBuildImageAndObjectPoints(
        PointF[] imagePoints,
        int[]? ids,
        out PointF[] alignedImagePoints,
        out MCvPoint3D32f[] alignedObjectPoints)
    {
        alignedImagePoints = [];
        alignedObjectPoints = [];

        if (imagePoints.Length == 0)
        {
            return false;
        }

        // ChArUco: идентификаторы определяют соответствие 2D-углов и 3D-объектных точек
        if (ids is { Length: > 0 })
        {
            // Количество id должно совпадать с количеством углов
            if (ids.Length != imagePoints.Length)
            {
                return false;
            }

            var matchedImagePoints = new List<PointF>(ids.Length);
            var matchedObjectPoints = new List<MCvPoint3D32f>(ids.Length);

            // Сопоставляем каждый id с соответствующей 3D-точкой из шаблона
            for (var i = 0; i < ids.Length; i++)
            {
                var id = ids[i];
                // Пропускаем некорректные идентификаторы
                if ((uint)id >= (uint)_objectPointTemplate.Length)
                {
                    continue;
                }

                matchedImagePoints.Add(imagePoints[i]);
                matchedObjectPoints.Add(_objectPointTemplate[id]);
            }

            // Минимум 4 точки для калибровки
            if (matchedImagePoints.Count < 4)
            {
                return false;
            }

            alignedImagePoints = matchedImagePoints.ToArray();
            alignedObjectPoints = matchedObjectPoints.ToArray();
            return true;
        }

        // Шахматная доска: ожидается полная фиксированная сетка углов
        if (imagePoints.Length != _objectPointTemplate.Length)
        {
            return false;
        }

        alignedImagePoints = imagePoints;
        alignedObjectPoints = _objectPointTemplate;
        return true;
    }

    /// <summary>
    /// Пытается построить согласованные массивы точек для стерео-калибровки.
    /// </summary>
    /// <param name="leftPoints">Углы, обнаруженные на левом изображении.</param>
    /// <param name="leftIds">Идентификаторы ChArUco-углов левого изображения.</param>
    /// <param name="rightPoints">Углы, обнаруженные на правом изображении.</param>
    /// <param name="rightIds">Идентификаторы ChArUco-углов правого изображения.</param>
    /// <param name="alignedLeftPoints">Согласованные 2D-точки левой камеры (выход).</param>
    /// <param name="alignedRightPoints">Согласованные 2D-точки правой камеры (выход).</param>
    /// <param name="alignedObjectPoints">Согласованные 3D-объектные точки (выход).</param>
    /// <returns><c>true</c>, если найдено достаточно общих точек (минимум 4) для калибровки.</returns>
    /// <remarks>
    /// Для ChArUco выполняется пересечение идентификаторов между левым и правым изображениями — это обеспечивает
    /// корректное соответствие точек даже при частичном обнаружении паттерна.
    /// Для шахматной доски обе камеры должны видеть полную фиксированную сетку углов.
    /// </remarks>
    private bool TryBuildStereoPoints(
        PointF[] leftPoints,
        int[]? leftIds,
        PointF[] rightPoints,
        int[]? rightIds,
        out PointF[] alignedLeftPoints,
        out PointF[] alignedRightPoints,
        out MCvPoint3D32f[] alignedObjectPoints)
    {
        alignedLeftPoints = [];
        alignedRightPoints = [];
        alignedObjectPoints = [];

        // ChArUco: сопоставляем только id, видимые на обоих изображениях
        if (leftIds is { Length: > 0 } || rightIds is { Length: > 0 })
        {
            // Проверка корректности входных данных
            if (leftIds == null || rightIds == null
                || leftIds.Length != leftPoints.Length
                || rightIds.Length != rightPoints.Length)
            {
                return false;
            }

            // Строим словарь id -> точка для левого изображения
            var leftById = new Dictionary<int, PointF>(leftIds.Length);
            for (var i = 0; i < leftIds.Length; i++)
            {
                var id = leftIds[i];
                if ((uint)id < (uint)_objectPointTemplate.Length && !leftById.ContainsKey(id))
                {
                    leftById[id] = leftPoints[i];
                }
            }

            // Строим словарь id -> точка для правого изображения
            var rightById = new Dictionary<int, PointF>(rightIds.Length);
            for (var i = 0; i < rightIds.Length; i++)
            {
                var id = rightIds[i];
                if ((uint)id < (uint)_objectPointTemplate.Length && !rightById.ContainsKey(id))
                {
                    rightById[id] = rightPoints[i];
                }
            }

            if (leftById.Count == 0 || rightById.Count == 0)
            {
                return false;
            }

            // Находим пересечение идентификаторов, видимых на обоих кадрах
            var commonIds = leftById.Keys
                .Where(rightById.ContainsKey)
                .OrderBy(id => id)
                .ToArray();

            // Минимум 4 общих точки
            if (commonIds.Length < 4)
            {
                return false;
            }

            // Формируем согласованные массивы по общим id
            alignedLeftPoints = new PointF[commonIds.Length];
            alignedRightPoints = new PointF[commonIds.Length];
            alignedObjectPoints = new MCvPoint3D32f[commonIds.Length];

            for (var i = 0; i < commonIds.Length; i++)
            {
                var id = commonIds[i];
                alignedLeftPoints[i] = leftById[id];
                alignedRightPoints[i] = rightById[id];
                alignedObjectPoints[i] = _objectPointTemplate[id];
            }

            return true;
        }

        // Шахматная доска: обе камеры должны видеть полную сетку
        if (leftPoints.Length != rightPoints.Length || leftPoints.Length != _objectPointTemplate.Length)
        {
            return false;
        }

        alignedLeftPoints = leftPoints;
        alignedRightPoints = rightPoints;
        alignedObjectPoints = _objectPointTemplate;
        return true;
    }

    /// <summary>
    /// Очищает все захваченные данные калибровки и освобождает нативные ресурсы.
    /// </summary>
    public void Clear()
    {
        // Освобождаем нативные VectorOf-объекты
        foreach (var pts in _leftImagePoints) pts.Dispose();
        foreach (var pts in _rightImagePoints) pts.Dispose();
        foreach (var pts in _stereoObjectPoints) pts.Dispose();
        foreach (var pts in _leftObjectPoints) pts.Dispose();
        foreach (var pts in _rightObjectPoints) pts.Dispose();
        
        // Очищаем списки и сбрасываем размер изображения
        _leftImagePoints.Clear();
        _rightImagePoints.Clear();
        _stereoObjectPoints.Clear();
        _leftObjectPoints.Clear();
        _rightObjectPoints.Clear();
        _imageSize = Size.Empty;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Clear();
    }
}

/// <summary>
/// Результат калибровки одиночной камеры (внутренние параметры).
/// </summary>
/// <remarks>
/// Содержит матрицу камеры, коэффициенты дисторсии, ошибку репроекции
/// и метаданные калибровки. Реализует <see cref="IDisposable"/> для освобождения нативных Mat-объектов.
/// </remarks>
public sealed class SingleCameraCalibrationResult : IDisposable
{
    /// <summary>Матрица камеры 3×3 (fx, fy, cx, cy).</summary>
    public Mat CameraMatrix { get; init; } = new();

    /// <summary>Коэффициенты дисторсии объектива.</summary>
    public Mat DistCoeffs { get; init; } = new();

    /// <summary>Средняя ошибка репроекции (в пикселях).</summary>
    public double ReprojectionError { get; init; }

    /// <summary>Размер изображения, использованного при калибровке.</summary>
    public Size ImageSize { get; init; }

    /// <summary>Количество изображений, использованных при калибровке.</summary>
    public int ImageCount { get; init; }

    private bool _disposed;

    /// <summary>Освобождает ресурсы, занятые матрицей камеры и коэффициентами дисторсии.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        CameraMatrix.Dispose();
        DistCoeffs.Dispose();
    }
}
