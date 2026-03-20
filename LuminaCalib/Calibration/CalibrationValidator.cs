using System.Drawing;
using Emgu.CV;
using Emgu.CV.Structure;
using LuminaCalib.Models;

namespace LuminaCalib.Calibration;

/// <summary>
/// Валидатор результатов калибровки. Вычисляет метрики качества и проверяет корректность матриц.
/// </summary>
/// <remarks>
/// Статический класс с методами <see cref="Validate"/> для проверки результатов
/// и <see cref="GetRecommendations"/> для получения рекомендаций по улучшению.
/// </remarks>
public static class CalibrationValidator
{
    /// <summary>
    /// Проверяет результат калибровки и возвращает детальные метрики.
    /// </summary>
    /// <param name="calibration">Результат калибровки для валидации.</param>
    /// <returns>Объект <see cref="CalibrationValidation"/> с детальными метриками качества.</returns>
    /// <exception cref="ArgumentNullException">Если <paramref name="calibration"/> равен <c>null</c>.</exception>
    public static CalibrationValidation Validate(CalibrationResult calibration)
    {
        ArgumentNullException.ThrowIfNull(calibration);

        var validation = new CalibrationValidation
        {
            ReprojectionError = calibration.ReprojectionError,
            Quality = calibration.Quality,
            ImagePairCount = calibration.ImagePairCount,
            ImageSize = calibration.ImageSize
        };

        // Проверка корректности матриц камер
        validation.HasValidCameraMatrices = !calibration.CameraMatrixLeft.IsEmpty 
                                          && !calibration.CameraMatrixRight.IsEmpty;
        
        // Проверка коэффициентов дисторсии
        validation.HasValidDistortion = !calibration.DistCoeffsLeft.IsEmpty 
                                       && !calibration.DistCoeffsRight.IsEmpty;
        
        // Проверка внешних параметров (R, T)
        validation.HasValidExtrinsics = !calibration.R.IsEmpty && !calibration.T.IsEmpty;
        
        // Проверка матриц ректификации
        validation.HasValidRectification = !calibration.R1.IsEmpty 
                                          && !calibration.R2.IsEmpty 
                                          && !calibration.P1.IsEmpty 
                                          && !calibration.P2.IsEmpty 
                                          && !calibration.Q.IsEmpty;

        // Вычисляем базовое расстояние (норма вектора смещения)
        if (!calibration.T.IsEmpty)
        {
            var tData = new double[3];
            System.Runtime.InteropServices.Marshal.Copy(calibration.T.DataPointer, tData, 0, 3);
            validation.BaselineDistance = Math.Sqrt(tData[0] * tData[0] + tData[1] * tData[1] + tData[2] * tData[2]);
        }

        // Извлекаем фокусные расстояния и главную точку левой камеры
        if (!calibration.CameraMatrixLeft.IsEmpty)
        {
            var camData = new double[9];
            System.Runtime.InteropServices.Marshal.Copy(calibration.CameraMatrixLeft.DataPointer, camData, 0, 9);
            validation.FocalLengthLeftX = camData[0];   // fx
            validation.FocalLengthLeftY = camData[4];   // fy
            validation.PrincipalPointLeftX = camData[2]; // cx
            validation.PrincipalPointLeftY = camData[5]; // cy
        }

        // Извлекаем фокусные расстояния и главную точку правой камеры
        if (!calibration.CameraMatrixRight.IsEmpty)
        {
            var camData = new double[9];
            System.Runtime.InteropServices.Marshal.Copy(calibration.CameraMatrixRight.DataPointer, camData, 0, 9);
            validation.FocalLengthRightX = camData[0];   // fx
            validation.FocalLengthRightY = camData[4];   // fy
            validation.PrincipalPointRightX = camData[2]; // cx
            validation.PrincipalPointRightY = camData[5]; // cy
        }

        validation.EpipolarYError = calibration.EpipolarYError;
        validation.FilteredOutPairCount = calibration.FilteredOutPairCount;

        // Общая проверка валидности: все матрицы корректны, ошибка < 2.0, минимум 10 пар
        validation.IsValid = validation.HasValidCameraMatrices 
                           && validation.HasValidDistortion 
                           && validation.HasValidExtrinsics 
                           && validation.HasValidRectification
                           && validation.ReprojectionError < 2.0
                           && validation.ImagePairCount >= 10;

        return validation;
    }

    /// <summary>
    /// Возвращает рекомендации по улучшению качества калибровки.
    /// </summary>
    /// <param name="validation">Результат валидации, полученный из <see cref="Validate"/>.</param>
    /// <returns>Перечисление строковых рекомендаций.</returns>
    public static IEnumerable<string> GetRecommendations(CalibrationValidation validation)
    {
        if (validation.ReprojectionError > 1.0)
        {
            yield return "High reprojection error. Try recapturing images with better board visibility.";
        }

        if (validation.ImagePairCount < 15)
        {
            yield return "Consider capturing more image pairs (15-25 recommended) for better accuracy.";
        }

        if (validation.ReprojectionError > 0.5 && validation.ImagePairCount > 20)
        {
            yield return "Consider using the two-stage calibration (intrinsics first, then stereo) for better results.";
        }

        if (!validation.HasValidRectification)
        {
            yield return "Rectification matrices are invalid. The stereo calibration may have failed.";
        }

        if (validation.BaselineDistance < 10)
        {
            yield return "Warning: Very small baseline distance detected. Check camera separation.";
        }

        // Проверка на неправдоподобные фокусные расстояния
        if ((validation.FocalLengthLeftX > 0 && validation.FocalLengthLeftX < 100) ||
            (validation.FocalLengthLeftX > 10000))
        {
            yield return "Warning: Unusual focal length values. Verify square size is specified in millimeters.";
        }

        // Phase 2: Outlier filtering info
        if (validation.FilteredOutPairCount > 0)
        {
            yield return $"{validation.FilteredOutPairCount} image pair(s) were filtered as outliers during calibration.";
        }

        // Phase 4: Epipolar Y-error recommendations
        if (!double.IsNaN(validation.EpipolarYError))
        {
            if (validation.EpipolarYError > 2.0)
            {
                yield return "Critical: Large Y-axis misalignment (>2px). Recalibrate with higher-quality images and more diverse board positions.";
            }
            else if (validation.EpipolarYError > 0.5)
            {
                yield return "Moderate Y-axis misalignment detected. Consider adding more diverse board positions covering image edges.";
            }
        }

        // Overall quality assessment
        if (!double.IsNaN(validation.EpipolarYError) && validation.EpipolarYError <= 0.3 && validation.ReprojectionError < 0.5)
        {
            yield return "Excellent rectification quality. Stereo matching should work well.";
        }
    }

    /// <summary>
    /// Вычисляет среднюю абсолютную разницу по Y между соответствующими точками после ректификации.
    /// </summary>
    /// <param name="leftPointSets">Массивы 2D-точек левой камеры (по изображениям).</param>
    /// <param name="rightPointSets">Массивы 2D-точек правой камеры (по изображениям).</param>
    /// <param name="mapLeftX">Карта ремаппинга X для левой камеры (CV_32FC1).</param>
    /// <param name="mapLeftY">Карта ремаппинга Y для левой камеры (CV_32FC1).</param>
    /// <param name="mapRightX">Карта ремаппинга X для правой камеры (CV_32FC1).</param>
    /// <param name="mapRightY">Карта ремаппинга Y для правой камеры (CV_32FC1).</param>
    /// <returns>Средняя |Δy| в пикселях, или <see cref="double.NaN"/> если нет точек.</returns>
    internal static double ComputeEpipolarYError(
        PointF[][] leftPointSets,
        PointF[][] rightPointSets,
        Mat mapLeftX, Mat mapLeftY,
        Mat mapRightX, Mat mapRightY)
    {
        var perView = ComputePerViewEpipolarYErrors(leftPointSets, rightPointSets,
            mapLeftX, mapLeftY, mapRightX, mapRightY);
        if (perView.Length == 0)
            return double.NaN;

        // Weighted average by point count per view
        double totalYError = 0;
        int totalPointCount = 0;
        var count = Math.Min(leftPointSets.Length, rightPointSets.Length);

        for (int i = 0; i < count; i++)
        {
            var ptCount = Math.Min(leftPointSets[i].Length, rightPointSets[i].Length);
            totalYError += perView[i] * ptCount;
            totalPointCount += ptCount;
        }

        return totalPointCount > 0 ? totalYError / totalPointCount : double.NaN;
    }

    /// <summary>
    /// Вычисляет среднюю абсолютную разницу по Y для каждой пары изображений по отдельности.
    /// </summary>
    /// <returns>Массив mean |Δy| по каждой паре (один элемент на пару).</returns>
    internal static double[] ComputePerViewEpipolarYErrors(
        PointF[][] leftPointSets,
        PointF[][] rightPointSets,
        Mat mapLeftX, Mat mapLeftY,
        Mat mapRightX, Mat mapRightY)
    {
        var count = Math.Min(leftPointSets.Length, rightPointSets.Length);
        var errors = new double[count];

        for (int i = 0; i < count; i++)
        {
            var leftPts = leftPointSets[i];
            var rightPts = rightPointSets[i];
            var ptCount = Math.Min(leftPts.Length, rightPts.Length);

            if (ptCount == 0)
            {
                errors[i] = 0;
                continue;
            }

            double viewYError = 0;
            for (int j = 0; j < ptCount; j++)
            {
                var rectLeft = RemapPoint(leftPts[j], mapLeftX, mapLeftY);
                var rectRight = RemapPoint(rightPts[j], mapRightX, mapRightY);
                viewYError += Math.Abs(rectLeft.Y - rectRight.Y);
            }

            errors[i] = viewYError / ptCount;
        }

        return errors;
    }

    /// <summary>
    /// Применяет билинейную интерполяцию в картах ремаппинга для получения ректифицированных координат точки.
    /// </summary>
    /// <param name="point">Исходная точка в пиксельных координатах.</param>
    /// <param name="mapX">Карта X (CV_32FC1, размер imageHeight × imageWidth).</param>
    /// <param name="mapY">Карта Y (CV_32FC1, размер imageHeight × imageWidth).</param>
    /// <returns>Ректифицированная позиция точки.</returns>
    internal static PointF RemapPoint(PointF point, Mat mapX, Mat mapY)
    {
        using var imgX = mapX.ToImage<Gray, float>();
        using var imgY = mapY.ToImage<Gray, float>();

        int maxCol = imgX.Width - 1;
        int maxRow = imgX.Height - 1;

        int ix = (int)point.X;
        int iy = (int)point.Y;
        float fx = point.X - ix;
        float fy = point.Y - iy;

        int x0 = Math.Clamp(ix, 0, maxCol);
        int y0 = Math.Clamp(iy, 0, maxRow);
        int x1 = Math.Clamp(ix + 1, 0, maxCol);
        int y1 = Math.Clamp(iy + 1, 0, maxRow);

        float newX = (1 - fx) * (1 - fy) * (float)imgX[y0, x0].Intensity
                   + fx * (1 - fy) * (float)imgX[y0, x1].Intensity
                   + (1 - fx) * fy * (float)imgX[y1, x0].Intensity
                   + fx * fy * (float)imgX[y1, x1].Intensity;

        float newY = (1 - fx) * (1 - fy) * (float)imgY[y0, x0].Intensity
                   + fx * (1 - fy) * (float)imgY[y0, x1].Intensity
                   + (1 - fx) * fy * (float)imgY[y1, x0].Intensity
                   + fx * fy * (float)imgY[y1, x1].Intensity;

        return new PointF(newX, newY);
    }
}

/// <summary>
/// Детальные результаты валидации калибровки.
/// </summary>
/// <remarks>
/// Содержит метрики качества: ошибку репроекции, базовое расстояние,
/// фокусные расстояния, главные точки и флаги валидности матриц.
/// </remarks>
public record CalibrationValidation
{
    /// <summary>Средняя ошибка репроекции (в пикселях).</summary>
    public double ReprojectionError { get; set; }

    /// <summary>Качество калибровки (перечисление).</summary>
    public CalibrationQuality Quality { get; set; }

    /// <summary>Количество стерео-пар, использованных при калибровке.</summary>
    public int ImagePairCount { get; set; }

    /// <summary>Размер изображения калибровки (в пикселях).</summary>
    public System.Drawing.Size ImageSize { get; set; }
    
    /// <summary>Матрицы камер корректны (не пусты).</summary>
    public bool HasValidCameraMatrices { get; set; }

    /// <summary>Коэффициенты дисторсии корректны (не пусты).</summary>
    public bool HasValidDistortion { get; set; }

    /// <summary>Внешние параметры (R, T) корректны.</summary>
    public bool HasValidExtrinsics { get; set; }

    /// <summary>Матрицы ректификации (R1, R2, P1, P2, Q) корректны.</summary>
    public bool HasValidRectification { get; set; }

    /// <summary>Общая валидность калибровки.</summary>
    public bool IsValid { get; set; }

    /// <summary>Базовое расстояние между камерами (норма вектора T).</summary>
    public double BaselineDistance { get; set; }
    
    /// <summary>Фокусное расстояние левой камеры по оси X (fx).</summary>
    public double FocalLengthLeftX { get; set; }

    /// <summary>Фокусное расстояние левой камеры по оси Y (fy).</summary>
    public double FocalLengthLeftY { get; set; }

    /// <summary>Фокусное расстояние правой камеры по оси X (fx).</summary>
    public double FocalLengthRightX { get; set; }

    /// <summary>Фокусное расстояние правой камеры по оси Y (fy).</summary>
    public double FocalLengthRightY { get; set; }
    
    /// <summary>Координата X главной точки левой камеры (cx).</summary>
    public double PrincipalPointLeftX { get; set; }

    /// <summary>Координата Y главной точки левой камеры (cy).</summary>
    public double PrincipalPointLeftY { get; set; }

    /// <summary>Координата X главной точки правой камеры (cx).</summary>
    public double PrincipalPointRightX { get; set; }

    /// <summary>Координата Y главной точки правой камеры (cy).</summary>
    public double PrincipalPointRightY { get; set; }

    /// <summary>Средняя ошибка по Y после ректификации (в пикселях).</summary>
    public double EpipolarYError { get; set; }

    /// <summary>Количество пар, отфильтрованных как выбросы.</summary>
    public int FilteredOutPairCount { get; set; }
}
