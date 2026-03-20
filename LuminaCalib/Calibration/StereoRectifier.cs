using System.Drawing;
using Emgu.CV;
using Emgu.CV.CvEnum;
using LuminaCalib.Models;

namespace LuminaCalib.Calibration;

/// <summary>
/// Применяет результаты калибровки для ректификации стерео-изображений в реальном времени.
/// </summary>
/// <remarks>
/// При инициализации предварительно вычисляет карты трансформации через
/// <c>InitUndistortRectifyMap</c>, что позволяет быстро применять <c>Remap</c>
/// к каждому кадру без повторного вычисления.
/// </remarks>
public sealed class StereoRectifier : IDisposable
{
    private Mat? _mapLeftX;   // Карта X-координат для левого изображения
    private Mat? _mapLeftY;   // Карта Y-координат для левого изображения
    private Mat? _mapRightX;  // Карта X-координат для правого изображения
    private Mat? _mapRightY;  // Карта Y-координат для правого изображения
    private bool _isInitialized;
    private bool _disposed;

    /// <summary>
    /// Инициализирован ли ректификатор данными калибровки.
    /// </summary>
    public bool IsInitialized => _isInitialized;

    /// <summary>
    /// Инициализирует карты ректификации на основе результатов калибровки.
    /// </summary>
    /// <param name="calibration">Результат стерео-калибровки с матрицами R1, R2, P1, P2.</param>
    /// <exception cref="ArgumentNullException">Если <paramref name="calibration"/> равен <c>null</c>.</exception>
    /// <remarks>
    /// Вычисляет карты <c>InitUndistortRectifyMap</c> один раз для обеих камер.
    /// После этого можно многократно вызывать <see cref="Rectify"/> без накладных расходов.
    /// </remarks>
    public void Initialize(CalibrationResult calibration)
    {
        ArgumentNullException.ThrowIfNull(calibration);

        // Очищаем предыдущие карты
        Cleanup();

        _mapLeftX = new Mat();
        _mapLeftY = new Mat();
        _mapRightX = new Mat();
        _mapRightY = new Mat();

        // Вычисляем карты ректификации для левой камеры
        CvInvoke.InitUndistortRectifyMap(
            calibration.CameraMatrixLeft,
            calibration.DistCoeffsLeft,
            calibration.R1,
            calibration.P1,
            calibration.ImageSize,
            DepthType.Cv32F,
            1, // Один канал
            _mapLeftX,
            _mapLeftY);

        // Вычисляем карты ректификации для правой камеры
        CvInvoke.InitUndistortRectifyMap(
            calibration.CameraMatrixRight,
            calibration.DistCoeffsRight,
            calibration.R2,
            calibration.P2,
            calibration.ImageSize,
            DepthType.Cv32F,
            1, // Один канал
            _mapRightX,
            _mapRightY);

        _isInitialized = true;
    }

    /// <summary>
    /// Ректифицирует стерео-пару изображений.
    /// </summary>
    /// <param name="leftImage">Изображение левой камеры (вход).</param>
    /// <param name="rightImage">Изображение правой камеры (вход).</param>
    /// <param name="rectifiedLeft">Ректифицированное левое изображение (выход).</param>
    /// <param name="rectifiedRight">Ректифицированное правое изображение (выход).</param>
    /// <exception cref="InvalidOperationException">Если ректификатор не инициализирован.</exception>
    public void Rectify(Mat leftImage, Mat rightImage, Mat rectifiedLeft, Mat rectifiedRight)
    {
        if (!_isInitialized)
        {
            throw new InvalidOperationException("Rectifier not initialized. Call Initialize() first.");
        }

        CvInvoke.Remap(leftImage, rectifiedLeft, _mapLeftX!, _mapLeftY!, Inter.Linear);
        CvInvoke.Remap(rightImage, rectifiedRight, _mapRightX!, _mapRightY!, Inter.Linear);
    }

    /// <summary>
    /// Ректифицирует одиночное изображение левой камеры.
    /// </summary>
    /// <param name="input">Входное изображение.</param>
    /// <param name="output">Ректифицированное изображение (выход).</param>
    /// <exception cref="InvalidOperationException">Если ректификатор не инициализирован.</exception>
    public void RectifyLeft(Mat input, Mat output)
    {
        if (!_isInitialized)
        {
            throw new InvalidOperationException("Rectifier not initialized.");
        }

        CvInvoke.Remap(input, output, _mapLeftX!, _mapLeftY!, Inter.Linear);
    }

    /// <summary>
    /// Ректифицирует одиночное изображение правой камеры.
    /// </summary>
    /// <param name="input">Входное изображение.</param>
    /// <param name="output">Ректифицированное изображение (выход).</param>
    /// <exception cref="InvalidOperationException">Если ректификатор не инициализирован.</exception>
    public void RectifyRight(Mat input, Mat output)
    {
        if (!_isInitialized)
        {
            throw new InvalidOperationException("Rectifier not initialized.");
        }

        CvInvoke.Remap(input, output, _mapRightX!, _mapRightY!, Inter.Linear);
    }

    /// <summary>
    /// Рисует горизонтальные эпиполярные линии на изображении для визуальной проверки ректификации.
    /// </summary>
    /// <param name="image">Изображение для отрисовки (модифицируется на месте).</param>
    /// <param name="lineSpacing">Расстояние между линиями в пикселях.</param>
    /// <param name="color">Цвет линий (BGR). По умолчанию — зелёный.</param>
    /// <remarks>
    /// При корректной ректификации одинаковые объекты на левом и правом изображении
    /// должны лежать на одной горизонтальной линии.
    /// </remarks>
    public static void DrawEpipolarLines(Mat image, int lineSpacing = 30, 
        Emgu.CV.Structure.MCvScalar? color = null)
    {
        var lineColor = color ?? new Emgu.CV.Structure.MCvScalar(0, 255, 0); // Зелёный

        for (int y = lineSpacing; y < image.Height; y += lineSpacing)
        {
            CvInvoke.Line(
                image,
                new Point(0, y),
                new Point(image.Width, y),
                lineColor,
                1);
        }
    }

    /// <summary>
    /// Создаёт изображение для сравнения: исходная стерео-пара и ректифицированная стерео-пара
    /// с эпиполярными линиями.
    /// </summary>
    /// <param name="leftOriginal">Исходное левое изображение.</param>
    /// <param name="rightOriginal">Исходное правое изображение.</param>
    /// <returns>Изображение 2×2: верх — оригинал, низ — ректифицированное с эпиполярными линиями.</returns>
    /// <exception cref="InvalidOperationException">Если ректификатор не инициализирован.</exception>
    public Mat CreateComparisonImage(Mat leftOriginal, Mat rightOriginal)
    {
        if (!_isInitialized)
        {
            throw new InvalidOperationException("Rectifier not initialized.");
        }

        int width = leftOriginal.Width;
        int height = leftOriginal.Height;

        // Создаём выходное изображение (2 строки × 2 столбца: оригинал сверху, ректифицированное снизу)
        var output = new Mat(height * 2, width * 2, leftOriginal.Depth, leftOriginal.NumberOfChannels);

        using var rectLeft = new Mat();
        using var rectRight = new Mat();
        Rectify(leftOriginal, rightOriginal, rectLeft, rectRight);

        // Определяем области (ROI) для копирования
        var roiTopLeft = new Rectangle(0, 0, width, height);
        var roiTopRight = new Rectangle(width, 0, width, height);
        var roiBottomLeft = new Rectangle(0, height, width, height);
        var roiBottomRight = new Rectangle(width, height, width, height);

        using var matTopLeft = new Mat(output, roiTopLeft);
        using var matTopRight = new Mat(output, roiTopRight);
        using var matBottomLeft = new Mat(output, roiBottomLeft);
        using var matBottomRight = new Mat(output, roiBottomRight);

        // Копируем изображения в соответствующие области
        leftOriginal.CopyTo(matTopLeft);
        rightOriginal.CopyTo(matTopRight);
        rectLeft.CopyTo(matBottomLeft);
        rectRight.CopyTo(matBottomRight);

        // Рисуем эпиполярные линии на ректифицированных изображениях
        DrawEpipolarLines(new Mat(output, roiBottomLeft));
        DrawEpipolarLines(new Mat(output, roiBottomRight));

        return output;
    }

    /// <summary>
    /// Освобождает карты ректификации и сбрасывает состояние.
    /// </summary>
    private void Cleanup()
    {
        _mapLeftX?.Dispose();
        _mapLeftY?.Dispose();
        _mapRightX?.Dispose();
        _mapRightY?.Dispose();
        _mapLeftX = null;
        _mapLeftY = null;
        _mapRightX = null;
        _mapRightY = null;
        _isInitialized = false;
    }

    /// <summary>Освобождает ресурсы, используемые для стереоректификации.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Cleanup();
    }
}
