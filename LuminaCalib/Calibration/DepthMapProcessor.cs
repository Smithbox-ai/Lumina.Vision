using System.Drawing;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Emgu.CV;
using Emgu.CV.Cuda;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using Emgu.CV.XImgproc;
using LuminaCalib.Models;
using LuminaCalib.Services;

namespace LuminaCalib.Calibration;

/// <summary>
/// Статический процессор карты глубины: конвейер StereoSGBM → WLS-фильтр → пост-обработка → раскраска.
/// </summary>
/// <remarks>
/// Все методы являются потокобезопасными (stateless).
/// Возвращаемые <see cref="Mat"/> принадлежат вызывающему коду — вызывающий обязан вызвать <c>Dispose()</c>.
/// </remarks>
public static class DepthMapProcessor
{
    /// <summary>
    /// Вычисляет карту диспаратности из ректифицированной стерео-пары.
    /// </summary>
    /// <param name="rectifiedLeft">Ректифицированное изображение левой камеры.</param>
    /// <param name="rectifiedRight">Ректифицированное изображение правой камеры.</param>
    /// <param name="settings">Параметры алгоритма SGBM, WLS-фильтра и пост-обработки.</param>
    /// <returns>
    /// Карта диспаратности (CV_16S, значения масштабированы ×16).
    /// Вызывающий код обязан вызвать <c>Dispose()</c> на результате.
    /// </returns>
    /// <remarks>
    /// Output is CV_16S ×16 subpixel format (matching StereoSGBM convention).
    /// When DisplayScale &lt; 1.0, the disparity is computed at reduced resolution and resized back —
    /// suitable for visualization only, NOT for ReprojectImageTo3D (which requires Q-matrix-matched resolution).
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// Если <paramref name="rectifiedLeft"/>, <paramref name="rectifiedRight"/> или <paramref name="settings"/> равен <c>null</c>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Если <see cref="DepthMapSettings.NumDisparities"/> не кратен 16 или меньше/равен 0,
    /// <see cref="DepthMapSettings.BlockSize"/> чётный или меньше 1,
    /// или P2 ≤ P1 (при обоих > 0).
    /// </exception>
    public static Mat ComputeDisparity(Mat rectifiedLeft, Mat rectifiedRight, DepthMapSettings settings)
    {
        ArgumentNullException.ThrowIfNull(rectifiedLeft);
        ArgumentNullException.ThrowIfNull(rectifiedRight);
        ArgumentNullException.ThrowIfNull(settings);

        ValidateSettings(settings);

        // === Подготовка: конвертация в градации серого ===
        using var grayLeft = ConvertToGray(rectifiedLeft);
        using var grayRight = ConvertToGray(rectifiedRight);

        // === Опциональное уменьшение для производительности ===
        bool needResize = settings.DisplayScale < 1.0 && settings.DisplayScale > 0.0;
        using var workLeft = needResize ? Downscale(grayLeft, settings.DisplayScale) : null;
        using var workRight = needResize ? Downscale(grayRight, settings.DisplayScale) : null;

        var left = workLeft ?? grayLeft;
        var right = workRight ?? grayRight;

        // === Автовычисление P1/P2 ===
        int cn = 1; // серое изображение
        int bs = settings.BlockSize;
        int p1 = settings.P1 > 0 ? settings.P1 : 8 * cn * bs * bs;
        int p2 = settings.P2 > 0 ? settings.P2 : 32 * cn * bs * bs;

        // === Создание StereoSGBM ===
        using var leftMatcher = new StereoSGBM(
            settings.MinDisparity,
            settings.NumDisparities,
            settings.BlockSize,
            p1,
            p2,
            settings.Disp12MaxDiff,
            settings.PreFilterCap,
            settings.UniquenessRatio,
            settings.SpeckleWindowSize,
            settings.SpeckleRange,
            MapSgbmMode(settings.SgbmMode));

        using var leftDisparity = new Mat();
        leftMatcher.Compute(left, right, leftDisparity);

        Mat result;

        if (settings.UseWlsFilter)
        {
            // === WLS-фильтрация ===
            using var rightMatcher = new RightMatcher(leftMatcher);
            using var rightDisparity = new Mat();
            rightMatcher.Compute(right, left, rightDisparity);

            using var wlsFilter = new DisparityWLSFilter(leftMatcher);
            // EmguCV 4.12 (cvextern.dll) не экспортирует cveDisparityWLSFilterSetLambda /
            // cveDisparityWLSFilterSetSigmaColor. Параметры WLS-фильтра фиксированы:
            // Lambda=8000, SigmaColor=1.0 (значения OpenCV по умолчанию).
            // UI-слайдеры для этих параметров скрыты.

            result = new Mat();
            wlsFilter.Filter(leftDisparity, left, result, rightDisparity);
        }
        else
        {
            result = new Mat();
            leftDisparity.CopyTo(result);
        }

        // === Морфологическое закрытие ===
        if (settings.UseMorphologicalClosing)
        {
            using var kernel = CvInvoke.GetStructuringElement(
                MorphShapes.Ellipse,
                new Size(settings.MorphKernelSize, settings.MorphKernelSize),
                new Point(-1, -1));
            CvInvoke.MorphologyEx(result, result, MorphOp.Close, kernel, new Point(-1, -1), 1, BorderType.Default, new MCvScalar(0));
        }

        // === Масштабирование обратно ===
        if (needResize)
        {
            var originalSize = new Size(grayLeft.Width, grayLeft.Height);
            using var temp = result;
            result = new Mat();
            CvInvoke.Resize(temp, result, originalSize, 0, 0, Inter.Linear);
            // NOTE: Do NOT rescale disparity values by 1/scale. The Q matrix from StereoRectify
            // encodes focal length and principal point at full resolution. Artificially inflating
            // disparity values would produce incorrect 3D coordinates in ReprojectImageTo3D.
            // This downscaled disparity is suitable for ColorizeDisparity (which normalizes
            // to min-max range) but NOT for ReprojectImageTo3D. For 3D reprojection,
            // always use full-resolution disparity (DisplayScale=1.0).
        }

        return result;
    }

    /// <summary>Кэш результата проверки доступности CUDA.</summary>
    private static bool? _isCudaAvailableCache;

    /// <summary>Возвращает <c>true</c>, если CUDA-ускорение доступно на текущей системе. Результат кэшируется.</summary>
    public static bool IsCudaAvailable => _isCudaAvailableCache ??= CheckCudaAvailable();

    private static bool CheckCudaAvailable()
    {
        try
        {
            return CudaInvoke.HasCuda;
        }
        catch (Exception ex)
        {
            ExceptionLogger.LogMessage($"CUDA availability check failed: {ex.Message}", "DepthMapProcessor.IsCudaAvailable");
            return false;
        }
    }

    /// <summary>
    /// Вычисляет карту диспаратности с использованием CUDA (GPU-ускорение).
    /// При недоступности CUDA возвращает пустой Mat (вызывающий код должен проверить IsEmpty и fallback к CPU).
    /// Output is converted to CV_16S ×16 subpixel format to match StereoSGBM convention.
    /// </summary>
    /// <param name="rectifiedLeft">Ректифицированное изображение левой камеры.</param>
    /// <param name="rectifiedRight">Ректифицированное изображение правой камеры.</param>
    /// <param name="settings">Параметры (используются только <see cref="DepthMapSettings.NumDisparities"/> и <see cref="DepthMapSettings.BlockSize"/>).</param>
    /// <returns>
    /// Карта диспаратности (CV_16S ×16 subpixel) или пустой Mat при SDK/GPU ошибке.
    /// Вызывающий код обязан вызвать Dispose().
    /// </returns>
    /// <remarks>
    /// <para><b>CUDA path is experimental and uses <c>CudaStereoBM</c> (NOT StereoSGBM).</b></para>
    /// <para>
    /// Only Block Matching (BM) is available on the CUDA path. This means:
    /// <list type="bullet">
    ///   <item>No semi-global matching — quality is noticeably lower than <see cref="ComputeDisparity"/>.</item>
    ///   <item>Parameters <c>P1</c>, <c>P2</c> (smoothness), <c>Disp12MaxDiff</c>, <c>UniquenessRatio</c>,
    ///         <c>SpeckleWindowSize</c>, and <c>SpeckleRange</c> are <b>IGNORED</b> — they are StereoSGBM-specific.</item>
    ///   <item>WLS filter is <b>NOT supported</b> in the CUDA path because it requires a CPU-based left matcher
    ///         (<see cref="StereoSGBM"/>) to create the right matcher via <c>CreateRightMatcher</c>.</item>
    ///   <item>DisplayScale down-sampling and morphological closing are not applied.</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Recommended for real-time preview only, not for final exports.</b>
    /// For high-quality results use the CPU path (<see cref="ComputeDisparity"/>).
    /// </para>
    /// </remarks>
    public static Mat ComputeDisparityCuda(Mat rectifiedLeft, Mat rectifiedRight, DepthMapSettings settings)
    {
        ArgumentNullException.ThrowIfNull(rectifiedLeft);
        ArgumentNullException.ThrowIfNull(rectifiedRight);
        ArgumentNullException.ThrowIfNull(settings);

        ValidateSettings(settings);

        try
        {
            if (!IsCudaAvailable)
                return new Mat();

            using var grayLeft = ConvertToGray(rectifiedLeft);
            using var grayRight = ConvertToGray(rectifiedRight);

            using var gpuLeft = new GpuMat(grayLeft);
            using var gpuRight = new GpuMat(grayRight);
            using var gpuDisparity = new GpuMat();

            using var stereoBm = new CudaStereoBM(settings.NumDisparities, settings.BlockSize);
            stereoBm.FindStereoCorrespondence(gpuLeft, gpuRight, gpuDisparity);

            var result = new Mat();
            gpuDisparity.Download(result);

            // CudaStereoBM returns raw CV_8U disparity (not ×16 subpixel).
            // Convert to CV_16S ×16 format to match StereoSGBM output expected by ReprojectImageTo3D.
            if (result.Depth == DepthType.Cv8U)
            {
                var converted = new Mat();
                result.ConvertTo(converted, DepthType.Cv16S, 16.0);
                result.Dispose();
                result = converted;
            }

            return result;
        }
        catch (Exception ex)
        {
            ExceptionLogger.LogMessage($"CUDA disparity computation failed: {ex.Message}", "DepthMapProcessor.ComputeDisparityCuda");
            return new Mat();
        }
    }

    /// <summary>
    /// Логирует информацию о CUDA-устройстве для диагностики.
    /// </summary>
    public static void LogCudaInfo()
    {
        try
        {
            if (CudaInvoke.HasCuda)
            {
                var info = new CudaDeviceInfo(0);
                ExceptionLogger.LogMessage($"CUDA available: {info.Name}, Compute {info.CudaComputeCapability}", "DepthMapProcessor.CudaDiagnostic");
            }
            else
            {
                ExceptionLogger.LogMessage("CUDA not available: CudaInvoke.HasCuda returned false", "DepthMapProcessor.CudaDiagnostic");
            }
        }
        catch (Exception ex)
        {
            ExceptionLogger.LogMessage($"CUDA diagnostic failed: {ex.Message}", "DepthMapProcessor.CudaDiagnostic");
        }
    }

    /// <summary>
    /// Раскрашивает карту диспаратности в указанную цветовую карту.
    /// </summary>
    /// <param name="disparity">Карта диспаратности (CV_16S, масштаб ×16).</param>
    /// <param name="colormap">Цветовая карта для визуализации.</param>
    /// <returns>
    /// Цветное изображение (CV_8UC3). Пиксели с нулевой/отрицательной диспаратностью — чёрные.
    /// Вызывающий код обязан вызвать <c>Dispose()</c> на результате.
    /// </returns>
    /// <exception cref="ArgumentNullException">Если <paramref name="disparity"/> равен <c>null</c>.</exception>
    public static Mat ColorizeDisparity(Mat disparity, DepthColormap colormap)
    {
        ArgumentNullException.ThrowIfNull(disparity);

        // Создаём маску валидных пикселей (диспаратность > 0)
        using var mask = new Mat();
        CvInvoke.Compare(disparity, new ScalarArray(new MCvScalar(0)), mask, CmpType.GreaterThan);

        // Находим min/max валидных значений
        double minVal = 0, maxVal = 1;
        var minLoc = new Point();
        var maxLoc = new Point();

        if (CvInvoke.CountNonZero(mask) > 0)
        {
            CvInvoke.MinMaxLoc(disparity, ref minVal, ref maxVal, ref minLoc, ref maxLoc, mask);
        }

        // Нормализация в 0–255
        double range = maxVal - minVal;
        double scale = range > 0 ? 255.0 / range : 0;
        double shift = range > 0 ? -minVal * scale : 0;

        using var normalized = new Mat();
        disparity.ConvertTo(normalized, DepthType.Cv8U, scale, shift);

        // Применяем цветовую карту
        using var colored = new Mat();
        CvInvoke.ApplyColorMap(normalized, colored, MapColormap(colormap));

        // Маскируем невалидные пиксели (делаем чёрными)
        var result = new Mat(colored.Size, DepthType.Cv8U, 3);
        result.SetTo(new MCvScalar(0, 0, 0));
        colored.CopyTo(result, mask);

        return result;
    }

    /// <summary>
    /// Экспортирует 3D-облако точек в формат PLY (ASCII).
    /// </summary>
    /// <param name="filePath">Путь к выходному PLY-файлу.</param>
    /// <param name="points3d">Облако точек CV_32FC3 (из <see cref="ReprojectTo3D"/>).</param>
    /// <param name="colorImage">Ректифицированное левое изображение (BGR, опционально). Если null — серый (128,128,128).</param>
    /// <param name="maxDepth">Максимальная глубина для фильтрации точек.</param>
    public static void ExportPly(string filePath, Mat points3d, Mat? colorImage, double maxDepth = 10000.0)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        ArgumentNullException.ThrowIfNull(points3d);

        int rows = points3d.Rows;
        int cols = points3d.Cols;
        int totalPixels = rows * cols;

        // Читаем 3D-точки
        var pointData = new float[totalPixels * 3];
        Marshal.Copy(points3d.DataPointer, pointData, 0, pointData.Length);

        // Читаем цвет (если есть)
        byte[]? colorData = null;
        int channels = 0;
        bool hasColor = colorImage != null && !colorImage.IsEmpty
                        && colorImage.Rows == rows && colorImage.Cols == cols
                        && colorImage.NumberOfChannels >= 3;
        if (hasColor)
        {
            channels = colorImage!.NumberOfChannels;
            colorData = new byte[totalPixels * channels];
            Marshal.Copy(colorImage.DataPointer, colorData, 0, colorData.Length);
        }

        // Первый проход: собираем валидные индексы
        var validIndices = new List<int>(totalPixels);
        for (int i = 0; i < totalPixels; i++)
        {
            float z = pointData[i * 3 + 2];
            if (z > 0 && z < maxDepth && !float.IsInfinity(z))
                validIndices.Add(i);
        }

        // Запись PLY
        using var writer = new StreamWriter(filePath, false, new UTF8Encoding(false));
        writer.NewLine = "\n";
        writer.WriteLine("ply");
        writer.WriteLine("format ascii 1.0");
        writer.WriteLine($"element vertex {validIndices.Count}");
        writer.WriteLine("property float x");
        writer.WriteLine("property float y");
        writer.WriteLine("property float z");
        writer.WriteLine("property uchar red");
        writer.WriteLine("property uchar green");
        writer.WriteLine("property uchar blue");
        writer.WriteLine("end_header");

        foreach (int i in validIndices)
        {
            float x = pointData[i * 3];
            float y = pointData[i * 3 + 1];
            float z = pointData[i * 3 + 2];

            byte r, g, b;
            if (hasColor && colorData != null)
            {
                b = colorData[i * channels];
                g = colorData[i * channels + 1];
                r = colorData[i * channels + 2];
            }
            else
            {
                r = g = b = 128;
            }

            writer.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "{0:G} {1:G} {2:G} {3} {4} {5}", x, y, z, r, g, b));
        }
    }

    /// <summary>
    /// Reprojections disparity map to 3D point cloud using the Q matrix from stereo calibration.
    /// </summary>
    /// <param name="disparity">
    /// Disparity map. CV_16S format: values are ×16 subpixel (OpenCV auto-divides by 16).
    /// CV_32F format: values are real disparity in pixels.
    /// Must be computed at the same resolution as the Q matrix.
    /// </param>
    /// <param name="Q">4×4 disparity-to-depth mapping matrix from StereoRectify.</param>
    /// <returns>CV_32FC3 3D point cloud where each pixel is (X, Y, Z) in mm.</returns>
    /// <exception cref="ArgumentNullException">Если <paramref name="disparity"/> или <paramref name="Q"/> равен <c>null</c>.</exception>
    public static Mat ReprojectTo3D(Mat disparity, Mat Q)
    {
        ArgumentNullException.ThrowIfNull(disparity);
        ArgumentNullException.ThrowIfNull(Q);

        var points = new Mat();
        CvInvoke.ReprojectImageTo3D(disparity, points, Q, true, DepthType.Cv32F);
        return points;
    }

    /// <summary>
    /// Преобразует <see cref="SgbmMode"/> в <see cref="StereoSGBM.Mode"/>.
    /// </summary>
    internal static StereoSGBM.Mode MapSgbmMode(SgbmMode mode) => mode switch
    {
        SgbmMode.Sgbm => StereoSGBM.Mode.SGBM,
        SgbmMode.HH => StereoSGBM.Mode.HH,
        // EmguCV 4.12 не объявляет SGBM3Way (2) и HH4 (3), но нативная OpenCV их поддерживает.
        SgbmMode.Sgbm3Way => (StereoSGBM.Mode)2,
        SgbmMode.HH4 => (StereoSGBM.Mode)3,
        _ => (StereoSGBM.Mode)2 // SGBM3Way по умолчанию
    };

    /// <summary>
    /// Преобразует <see cref="DepthColormap"/> в <see cref="ColorMapType"/>.
    /// </summary>
    internal static ColorMapType MapColormap(DepthColormap colormap) => colormap switch
    {
        DepthColormap.Jet => ColorMapType.Jet,
        DepthColormap.Turbo => ColorMapType.Turbo,
        DepthColormap.Inferno => ColorMapType.Inferno,
        DepthColormap.Magma => ColorMapType.Magma,
        DepthColormap.Plasma => ColorMapType.Plasma,
        DepthColormap.Hot => ColorMapType.Hot,
        DepthColormap.Cool => ColorMapType.Cool,
        DepthColormap.Rainbow => ColorMapType.Rainbow,
        DepthColormap.Bone => ColorMapType.Bone,
        DepthColormap.Winter => ColorMapType.Winter,
        _ => ColorMapType.Turbo
    };

    /// <summary>
    /// Проверяет корректность параметров SGBM.
    /// </summary>
    private static void ValidateSettings(DepthMapSettings settings)
    {
        if (settings.NumDisparities <= 0 || settings.NumDisparities % 16 != 0)
        {
            throw new ArgumentException(
                $"NumDisparities должен быть положительным и кратным 16, получено: {settings.NumDisparities}.",
                nameof(settings));
        }

        if (settings.BlockSize < 1 || settings.BlockSize % 2 == 0)
        {
            throw new ArgumentException(
                $"BlockSize должен быть нечётным и ≥ 1, получено: {settings.BlockSize}.",
                nameof(settings));
        }

        // StereoSGBM: для режимов SGBM и SGBM_3WAY BlockSize ≤ 11 (ограничение OpenCV)
        if (settings.BlockSize > 11 &&
            (settings.SgbmMode == SgbmMode.Sgbm || settings.SgbmMode == SgbmMode.Sgbm3Way))
        {
            throw new ArgumentException(
                $"BlockSize must be ≤ 11 for SGBM/SGBM_3WAY modes (got {settings.BlockSize}).",
                nameof(settings));
        }

        if (settings.P1 > 0 && settings.P2 > 0 && settings.P2 <= settings.P1)
        {
            throw new ArgumentException(
                $"P2 ({settings.P2}) должен быть больше P1 ({settings.P1}).",
                nameof(settings));
        }
    }

    /// <summary>
    /// Конвертирует изображение в градации серого, если оно цветное.
    /// </summary>
    private static Mat ConvertToGray(Mat input)
    {
        if (input.NumberOfChannels == 1)
        {
            var clone = new Mat();
            input.CopyTo(clone);
            return clone;
        }

        var gray = new Mat();
        CvInvoke.CvtColor(input, gray, ColorConversion.Bgr2Gray);
        return gray;
    }

    /// <summary>
    /// Уменьшает изображение по указанному коэффициенту.
    /// </summary>
    private static Mat Downscale(Mat input, double scale)
    {
        var newSize = new Size(
            (int)(input.Width * scale),
            (int)(input.Height * scale));

        var resized = new Mat();
        CvInvoke.Resize(input, resized, newSize, 0, 0, Inter.Area);
        return resized;
    }
}
