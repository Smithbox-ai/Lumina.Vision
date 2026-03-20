using System.Globalization;
using Emgu.CV;

namespace LuminaCalib.Models;

/// <summary>
/// Результат стереокалибровки камер.
/// </summary>
/// <remarks>
/// Содержит все матрицы внутренних и внешних параметров, коэффициенты дисторсии,
/// матрицы ректификации и проекции, а также матрицу перевода диспаратности в глубину.
/// Реализует IDisposable для освобождения неуправляемых ресурсов матриц OpenCV.
/// </remarks>
public sealed class CalibrationResult : IDisposable
{
    /// <summary>
    /// Матрица внутренних параметров левой камеры (3×3). Содержит фокусные расстояния и координаты главной точки.
    /// </summary>
    public Mat CameraMatrixLeft { get; init; } = new();

    /// <summary>
    /// Матрица внутренних параметров правой камеры (3×3). Содержит фокусные расстояния и координаты главной точки.
    /// </summary>
    public Mat CameraMatrixRight { get; init; } = new();

    /// <summary>
    /// Коэффициенты дисторсии левой камеры (радиальные и тангенциальные искажения).
    /// </summary>
    public Mat DistCoeffsLeft { get; init; } = new();

    /// <summary>
    /// Коэффициенты дисторсии правой камеры (радиальные и тангенциальные искажения).
    /// </summary>
    public Mat DistCoeffsRight { get; init; } = new();

    /// <summary>
    /// Матрица вращения между камерами (3×3). Описывает ориентацию правой камеры относительно левой.
    /// </summary>
    public Mat R { get; init; } = new();

    /// <summary>
    /// Вектор смещения между камерами (3×1). Описывает положение правой камеры относительно левой.
    /// </summary>
    public Mat T { get; init; } = new();

    /// <summary>
    /// Существенная матрица (3×3). Связывает соответствующие точки в нормализованных координатах.
    /// </summary>
    public Mat E { get; init; } = new();

    /// <summary>
    /// Фундаментальная матрица (3×3). Связывает соответствующие точки в пиксельных координатах.
    /// </summary>
    public Mat F { get; init; } = new();

    /// <summary>
    /// Матрица ректификации левой камеры (3×3). Поворот для выравнивания эпиполярных линий.
    /// </summary>
    public Mat R1 { get; init; } = new();

    /// <summary>
    /// Матрица ректификации правой камеры (3×3). Поворот для выравнивания эпиполярных линий.
    /// </summary>
    public Mat R2 { get; init; } = new();

    /// <summary>
    /// Матрица проекции левой камеры (3×4). Объединяет внутренние параметры и ректификацию.
    /// </summary>
    public Mat P1 { get; init; } = new();

    /// <summary>
    /// Матрица проекции правой камеры (3×4). Объединяет внутренние параметры и ректификацию.
    /// </summary>
    public Mat P2 { get; init; } = new();

    /// <summary>
    /// Матрица перевода диспаратности в глубину (4×4). Используется для построения карты глубины.
    /// </summary>
    public Mat Q { get; init; } = new();

    /// <summary>
    /// Ошибка репроекции в пикселях. Чем меньше, тем точнее калибровка.
    /// </summary>
    public double ReprojectionError { get; set; }

    /// <summary>
    /// Размер изображения, использованный при калибровке.
    /// </summary>
    public System.Drawing.Size ImageSize { get; set; }

    /// <summary>
    /// Дата и время проведения калибровки.
    /// </summary>
    public DateTime CalibrationDate { get; set; } = DateTime.Now;

    /// <summary>
    /// Количество пар изображений, использованных для калибровки.
    /// </summary>
    public int ImagePairCount { get; set; }

    /// <summary>
    /// Ошибка репроекции внутренних параметров левой камеры (Этап 1 полной калибровки).
    /// </summary>
    public double LeftIntrinsicError { get; set; }

    /// <summary>
    /// Ошибка репроекции внутренних параметров правой камеры (Этап 1 полной калибровки).
    /// </summary>
    public double RightIntrinsicError { get; set; }

    /// <summary>
    /// Per-view ошибка репроекции по каждой стерео-паре (среднее между левой и правой камерой).
    /// </summary>
    public double[] PerViewErrors { get; set; } = [];

    /// <summary>
    /// Количество стерео-пар, отфильтрованных как выбросы перед этапом StereoCalibrate.
    /// </summary>
    public int FilteredOutPairCount { get; set; }

    /// <summary>
    /// Средняя абсолютная ошибка по оси Y (в пикселях) между соответствующими точками после ректификации.
    /// </summary>
    public double EpipolarYError { get; set; }

    private bool _disposed;

    /// <summary>
    /// Оценка качества калибровки на основе ошибки репроекции.
    /// </summary>
    public CalibrationQuality Quality => ReprojectionError switch
    {
        < 0.5 => CalibrationQuality.Excellent,
        < 1.0 => CalibrationQuality.Good,
        _ => CalibrationQuality.Poor
    };

    /// <summary>
    /// Сохраняет результат калибровки в XML-файл OpenCV FileStorage.
    /// Метаданные сохраняются в отдельном текстовом файле с тем же базовым именем.
    /// </summary>
    /// <param name="filePath">Путь к выходному XML-файлу.</param>
    public void SaveToXml(string filePath)
    {
        // Открываем файл для записи в формате XML (OpenCV FileStorage)
        using (var fs = new FileStorage(filePath, FileStorage.Mode.Write | FileStorage.Mode.FormatXml))
        {
            // Записываем все матрицы калибровки в XML-файл
            fs.Write(CameraMatrixLeft, "CameraMatrixLeft");
            fs.Write(CameraMatrixRight, "CameraMatrixRight");
            fs.Write(DistCoeffsLeft, "DistCoeffsLeft");
            fs.Write(DistCoeffsRight, "DistCoeffsRight");
            fs.Write(R, "R");
            fs.Write(T, "T");
            fs.Write(E, "E");
            fs.Write(F, "F");
            fs.Write(R1, "R1");
            fs.Write(R2, "R2");
            fs.Write(P1, "P1");
            fs.Write(P2, "P2");
            fs.Write(Q, "Q");
        }

        // Сохраняем метаданные (ошибка репроекции, дата и т.д.) в текстовый файл-спутник
        SaveMetadataToText(filePath);
    }

    /// <summary>
    /// Загружает результат калибровки из XML-файла OpenCV FileStorage.
    /// Метаданные загружаются из текстового файла-спутника при его наличии.
    /// </summary>
    /// <param name="filePath">Путь к XML-файлу калибровки.</param>
    /// <returns>Объект <see cref="CalibrationResult"/> с загруженными данными.</returns>
    /// <exception cref="ArgumentException">Путь к файлу пуст или состоит из пробелов.</exception>
    /// <exception cref="FileNotFoundException">Файл калибровки не найден.</exception>
    public static CalibrationResult LoadFromXml(string filePath)
    {
        // Проверяем, что путь к файлу указан и не пуст
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("Calibration path is empty.", nameof(filePath));
        }

        // Проверяем, что файл существует на диске
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("Calibration file was not found.", filePath);
        }

        // Создаём объект результата калибровки
        var result = new CalibrationResult();

        // Открываем XML-файл для чтения через OpenCV FileStorage
        using (var fs = new FileStorage(filePath, FileStorage.Mode.Read))
        {
            // Считываем все матрицы калибровки из XML
            fs["CameraMatrixLeft"].ReadMat(result.CameraMatrixLeft);
            fs["CameraMatrixRight"].ReadMat(result.CameraMatrixRight);
            fs["DistCoeffsLeft"].ReadMat(result.DistCoeffsLeft);
            fs["DistCoeffsRight"].ReadMat(result.DistCoeffsRight);
            fs["R"].ReadMat(result.R);
            fs["T"].ReadMat(result.T);
            fs["E"].ReadMat(result.E);
            fs["F"].ReadMat(result.F);
            fs["R1"].ReadMat(result.R1);
            fs["R2"].ReadMat(result.R2);
            fs["P1"].ReadMat(result.P1);
            fs["P2"].ReadMat(result.P2);
            fs["Q"].ReadMat(result.Q);

            // Обратная совместимость: метаданные из старых XML-файлов, где они хранились внутри XML
            result.ReprojectionError = fs["ReprojectionError"].ReadDouble(result.ReprojectionError);
            var width = fs["ImageWidth"].ReadInt(result.ImageSize.Width);
            var height = fs["ImageHeight"].ReadInt(result.ImageSize.Height);
            result.ImageSize = new System.Drawing.Size(width, height);

            var dateText = fs["CalibrationDate"].ReadString(string.Empty);
            if (DateTime.TryParse(dateText, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsedDate))
            {
                result.CalibrationDate = parsedDate;
            }

            result.ImagePairCount = fs["ImagePairCount"].ReadInt(result.ImagePairCount);
        }

        // Загружаем метаданные из текстового файла-спутника (если есть)
        LoadMetadataFromText(filePath, result);

        return result;
    }

    /// <summary>
    /// Сохраняет метаданные калибровки в текстовый файл-спутник (.txt).
    /// </summary>
    /// <param name="xmlPath">Путь к основному XML-файлу калибровки.</param>
    private void SaveMetadataToText(string xmlPath)
    {
        // Формируем путь к файлу метаданных (.txt рядом с .xml)
        var metadataPath = GetMetadataPath(xmlPath);
        // Формируем массив строк с метаданными калибровки
        var lines = new[]
        {
            "# LuminaCalib calibration metadata",
            $"ReprojectionError={ReprojectionError.ToString(CultureInfo.InvariantCulture)}",
            $"ImageWidth={ImageSize.Width}",
            $"ImageHeight={ImageSize.Height}",
            $"CalibrationDate={CalibrationDate.ToString("o", CultureInfo.InvariantCulture)}",
            $"ImagePairCount={ImagePairCount}",
            $"LeftIntrinsicError={LeftIntrinsicError.ToString(CultureInfo.InvariantCulture)}",
            $"RightIntrinsicError={RightIntrinsicError.ToString(CultureInfo.InvariantCulture)}",
            $"FilteredOutPairCount={FilteredOutPairCount}",
            $"EpipolarYError={EpipolarYError.ToString(CultureInfo.InvariantCulture)}",
            $"PerViewErrors={string.Join(",", PerViewErrors.Select(v => v.ToString(CultureInfo.InvariantCulture)))}"
        };

        // Записываем метаданные в текстовый файл
        File.WriteAllLines(metadataPath, lines);
    }

    /// <summary>
    /// Загружает метаданные калибровки из текстового файла-спутника.
    /// </summary>
    /// <param name="xmlPath">Путь к основному XML-файлу.</param>
    /// <param name="result">Объект результата, в который записываются метаданные.</param>
    private static void LoadMetadataFromText(string xmlPath, CalibrationResult result)
    {
        // Формируем путь к файлу метаданных
        var metadataPath = GetMetadataPath(xmlPath);
        // Если файл метаданных не существует — выходим
        if (!File.Exists(metadataPath))
        {
            return;
        }

        // Читаем файл построчно и разбираем пары ключ=значение
        foreach (var rawLine in File.ReadLines(metadataPath))
        {
            var line = rawLine.Trim();
            // Пропускаем пустые строки и комментарии
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            // Ищем разделитель «=» для разбора ключа и значения
            var separator = line.IndexOf('=');
            if (separator <= 0 || separator == line.Length - 1)
            {
                continue;
            }

            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();

            // Заполняем соответствующие поля по имени ключа
            switch (key)
            {
                case "ReprojectionError":
                    if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var error))
                    {
                        result.ReprojectionError = error;
                    }
                    break;
                case "ImageWidth":
                    if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var width))
                    {
                        result.ImageSize = new System.Drawing.Size(width, result.ImageSize.Height);
                    }
                    break;
                case "ImageHeight":
                    if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var height))
                    {
                        result.ImageSize = new System.Drawing.Size(result.ImageSize.Width, height);
                    }
                    break;
                case "CalibrationDate":
                    if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var date))
                    {
                        result.CalibrationDate = date;
                    }
                    break;
                case "ImagePairCount":
                    if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count))
                    {
                        result.ImagePairCount = count;
                    }
                    break;
                case "LeftIntrinsicError":
                    if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var leftErr))
                    {
                        result.LeftIntrinsicError = leftErr;
                    }
                    break;
                case "RightIntrinsicError":
                    if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var rightErr))
                    {
                        result.RightIntrinsicError = rightErr;
                    }
                    break;
                case "FilteredOutPairCount":
                    if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var filteredCount))
                    {
                        result.FilteredOutPairCount = filteredCount;
                    }
                    break;
                case "EpipolarYError":
                    if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var yError))
                    {
                        result.EpipolarYError = yError;
                    }
                    break;
                case "PerViewErrors":
                    if (value.Length == 0)
                    {
                        result.PerViewErrors = [];
                        break;
                    }

                    var parsedErrors = value
                        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Select(token => double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                            ? parsed
                            : double.NaN)
                        .Where(parsed => !double.IsNaN(parsed))
                        .ToArray();

                    result.PerViewErrors = parsedErrors;
                    break;
            }
        }
    }

    /// <summary>
    /// Формирует путь к файлу метаданных, заменяя расширение на .txt.
    /// </summary>
    /// <param name="xmlPath">Путь к XML-файлу.</param>
    /// <returns>Путь к соответствующему .txt-файлу.</returns>
    private static string GetMetadataPath(string xmlPath)
    {
        return Path.ChangeExtension(xmlPath, ".txt");
    }

    /// <summary>
    /// Освобождает все неуправляемые ресурсы матриц OpenCV.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // Освобождаем все Mat-объекты, содержащие неуправляемую память OpenCV

        CameraMatrixLeft.Dispose();
        CameraMatrixRight.Dispose();
        DistCoeffsLeft.Dispose();
        DistCoeffsRight.Dispose();
        R.Dispose();
        T.Dispose();
        E.Dispose();
        F.Dispose();
        R1.Dispose();
        R2.Dispose();
        P1.Dispose();
        P2.Dispose();
        Q.Dispose();
    }
}

/// <summary>
/// Оценка качества калибровки на основе ошибки репроекции.
/// </summary>
public enum CalibrationQuality
{
    /// <summary>Отличное качество (ошибка &lt; 0.5 пикселя).</summary>
    Excellent,  // < 0.5 пкс
    /// <summary>Хорошее качество (ошибка 0.5–1.0 пикселя).</summary>
    Good,       // 0.5 – 1.0 пкс
    /// <summary>Низкое качество (ошибка &gt; 1.0 пикселя).</summary>
    Poor        // > 1.0 пкс
}
