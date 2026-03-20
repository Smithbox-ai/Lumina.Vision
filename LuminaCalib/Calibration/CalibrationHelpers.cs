using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;

namespace LuminaCalib.Calibration;

/// <summary>
/// Вспомогательные методы для калибровки.
/// </summary>
/// <remarks>
/// Содержит фабричные методы для создания матриц (CV_64F), поиск файлов калибровки
/// и конвертацию перечислений ArUco-словарей.
/// </remarks>
internal static class CalibrationHelpers
{
    /// <summary>
    /// Создаёт нулевую матрицу заданного размера (CV_64F).
    /// </summary>
    /// <param name="rows">Количество строк.</param>
    /// <param name="cols">Количество столбцов.</param>
    /// <returns>Нулевая матрица типа CV_64F.</returns>
    public static Mat CreateZero(int rows, int cols)
    {
        var mat = new Mat(rows, cols, DepthType.Cv64F, 1);
        mat.SetTo(new MCvScalar(0));
        return mat;
    }

    /// <summary>
    /// Создаёт единичную матрицу заданного размера (CV_64F).
    /// </summary>
    /// <param name="size">Размер квадратной матрицы.</param>
    /// <returns>Единичная матрица size×size.</returns>
    public static Mat CreateIdentity(int size)
    {
        var mat = CreateZero(size, size);
        CvInvoke.SetIdentity(mat, new MCvScalar(1));
        return mat;
    }

    /// <summary>
    /// Создаёт проекционную матрицу 3×4 (единичная в левом верхнем углу 3×3).
    /// </summary>
    /// <returns>Проекционная матрица 3×4 типа CV_64F.</returns>
    public static Mat CreateProjection()
    {
        var mat = CreateZero(3, 4);
        CvInvoke.SetIdentity(mat, new MCvScalar(1));
        return mat;
    }

    /// <summary>
    /// Разрешает путь к директории: относительный путь (начинающийся с <c>"./"</c>) приводится к абсолютному
    /// относительно каталога исполняемого файла (<see cref="AppContext.BaseDirectory"/>).
    /// Абсолютный путь возвращается без изменений.
    /// </summary>
    /// <param name="path">Путь (может быть <c>null</c> или пустым).</param>
    /// <param name="defaultRelative">Значение по умолчанию (относительный путь), используемое если <paramref name="path"/> пуст или <c>null</c>.
    /// Например: <c>"./CalibrationData"</c>.</param>
    /// <returns>Абсолютный путь к директории.</returns>
    public static string ResolvePath(string? path, string defaultRelative)
    {
        var configured = string.IsNullOrWhiteSpace(path) ? defaultRelative : path.Trim();

        if (Path.IsPathRooted(configured))
            return Path.GetFullPath(configured);

        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, configured));
    }

    /// <summary>
    /// Возвращает последний по дате XML-файл калибровки из указанной папки.
    /// Относительный путь разрешается относительно каталога исполняемого файла.
    /// </summary>
    /// <param name="calibrationPath">Путь к папке с файлами калибровки.</param>
    /// <returns>Полный путь к последнему XML-файлу или <c>null</c>, если папка не существует или пуста.</returns>
    public static string? GetLatestCalibrationFile(string calibrationPath)
    {
        var fullPath = ResolvePath(calibrationPath, "./CalibrationData");
        if (!Directory.Exists(fullPath)) return null;
        return Directory
            .GetFiles(fullPath, "*.xml")
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    /// <summary>
    /// Преобразует перечисление <see cref="Models.CharucoDictionary"/> в числовой идентификатор словаря ArUco.
    /// </summary>
    /// <param name="dict">Перечисление словаря.</param>
    /// <returns>Числовой id для <c>PredefinedDictionaryName</c>. По умолчанию — 10 (Dict6x6_250).</returns>
    public static int GetArucoDictId(Models.CharucoDictionary dict) => dict switch
    {
        Models.CharucoDictionary.Dict4x4_50 => 0,
        Models.CharucoDictionary.Dict4x4_100 => 1,
        Models.CharucoDictionary.Dict4x4_250 => 2,
        Models.CharucoDictionary.Dict4x4_1000 => 3,
        Models.CharucoDictionary.Dict5x5_50 => 4,
        Models.CharucoDictionary.Dict5x5_100 => 5,
        Models.CharucoDictionary.Dict5x5_250 => 6,
        Models.CharucoDictionary.Dict5x5_1000 => 7,
        Models.CharucoDictionary.Dict6x6_50 => 8,
        Models.CharucoDictionary.Dict6x6_100 => 9,
        Models.CharucoDictionary.Dict6x6_250 => 10,
        Models.CharucoDictionary.Dict6x6_1000 => 11,
        Models.CharucoDictionary.Dict7x7_50 => 12,
        Models.CharucoDictionary.Dict7x7_100 => 13,
        Models.CharucoDictionary.Dict7x7_250 => 14,
        Models.CharucoDictionary.Dict7x7_1000 => 15,
        _ => 10,
    };
}
