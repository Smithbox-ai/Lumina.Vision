using CommunityToolkit.Mvvm.ComponentModel;

namespace LuminaCalib.Models;

/// <summary>
/// Настройки вычисления карты глубины (disparity map) из стереопары.
/// </summary>
public partial class DepthMapSettings : ObservableObject
{
    // === Параметры StereoSGBM ===

    /// <summary>
    /// Минимальное значение диспаратности (обычно 0).
    /// </summary>
    [ObservableProperty]
    private int _minDisparity = 0;

    /// <summary>
    /// Диапазон диспаратностей (должен быть кратен 16).
    /// </summary>
    [ObservableProperty]
    private int _numDisparities = 128;

    /// <summary>
    /// Размер блока сопоставления (нечётное число, диапазон 3–11).
    /// </summary>
    [ObservableProperty]
    private int _blockSize = 5;

    /// <summary>
    /// Штраф за изменение диспаратности на ±1 между соседними пикселями.
    /// Если 0, вычисляется автоматически: 8 × channels × blockSize².
    /// </summary>
    [ObservableProperty]
    private int _p1 = 0;

    /// <summary>
    /// Штраф за изменение диспаратности более чем на ±1 между соседними пикселями.
    /// Если 0, вычисляется автоматически: 32 × channels × blockSize².
    /// </summary>
    [ObservableProperty]
    private int _p2 = 0;

    /// <summary>
    /// Максимально допустимая разница между левым и правым диспаратностными значениями.
    /// </summary>
    [ObservableProperty]
    private int _disp12MaxDiff = 1;

    /// <summary>
    /// Значение усечения предварительного фильтра (ограничивает производные).
    /// </summary>
    [ObservableProperty]
    private int _preFilterCap = 63;

    /// <summary>
    /// Процент уникальности лучшего совпадения (отсекает неоднозначные пиксели).
    /// </summary>
    [ObservableProperty]
    private int _uniquenessRatio = 10;

    /// <summary>
    /// Размер окна для подавления мелких пятен шума (speckle filter).
    /// </summary>
    [ObservableProperty]
    private int _speckleWindowSize = 100;

    /// <summary>
    /// Максимальный перепад диспаратности внутри одного пятна.
    /// </summary>
    [ObservableProperty]
    private int _speckleRange = 2;

    /// <summary>
    /// Режим алгоритма StereoSGBM.
    /// </summary>
    [ObservableProperty]
    private SgbmMode _sgbmMode = SgbmMode.Sgbm3Way;

    // === Параметры WLS-фильтра ===

    /// <summary>
    /// Использовать WLS-фильтр (Weighted Least Squares) для сглаживания диспаратности.
    /// </summary>
    [ObservableProperty]
    private bool _useWlsFilter = true;

    /// <summary>
    /// Параметр Lambda WLS-фильтра (сила сглаживания, типичные значения: 1000–80000).
    /// </summary>
    [ObservableProperty]
    private double _wlsLambda = 8000.0;

    /// <summary>
    /// Параметр SigmaColor WLS-фильтра (чувствительность к цветовым границам, 0.8–2.0).
    /// </summary>
    [ObservableProperty]
    private double _wlsSigmaColor = 1.0;

    // === Пост-обработка ===

    /// <summary>
    /// Применять морфологическое закрытие для заполнения мелких дыр в карте глубины.
    /// </summary>
    [ObservableProperty]
    private bool _useMorphologicalClosing = true;

    /// <summary>
    /// Размер ядра для морфологического закрытия (нечётное число).
    /// </summary>
    [ObservableProperty]
    private int _morphKernelSize = 5;

    // === Визуализация ===

    /// <summary>
    /// Цветовая карта для отображения диспаратности.
    /// </summary>
    [ObservableProperty]
    private DepthColormap _colormap = DepthColormap.Turbo;

    /// <summary>
    /// Коэффициент уменьшения для повышения производительности (0.25–1.0).
    /// Значение 1.0 — без уменьшения, 0.5 — изображение уменьшено в 2 раза.
    /// </summary>
    [ObservableProperty]
    private double _displayScale = 1.0;

    // === Ускорение ===

    /// <summary>
    /// Использовать GPU (CUDA) для вычисления диспаратности, если доступно.
    /// CUDA-путь используется только когда WLS-фильтр отключён.
    /// </summary>
    [ObservableProperty]
    private bool _useCuda = false;
}

/// <summary>Режим алгоритма StereoSGBM.</summary>
public enum SgbmMode
{
    /// <summary>Стандартный SGBM (1 проход).</summary>
    Sgbm,
    /// <summary>Полный 2-проходный алгоритм (высокое качество, высокое потребление памяти).</summary>
    HH,
    /// <summary>Оптимизированный 3-проходный вариант (хороший баланс качества и скорости).</summary>
    Sgbm3Way,
    /// <summary>4-проходный вариант полного алгоритма.</summary>
    HH4
}

/// <summary>Цветовая карта для визуализации глубины.</summary>
public enum DepthColormap
{
    /// <summary>Цветовая карта Jet (синий → зелёный → красный).</summary>
    Jet,
    /// <summary>Цветовая карта Turbo (улучшенная Jet с лучшей перцептивной линейностью).</summary>
    Turbo,
    /// <summary>Цветовая карта Inferno (тёмно-фиолетовый → оранжевый → жёлтый).</summary>
    Inferno,
    /// <summary>Цветовая карта Magma (чёрный → фиолетовый → оранжевый).</summary>
    Magma,
    /// <summary>Цветовая карта Plasma (фиолетовый → оранжевый → жёлтый).</summary>
    Plasma,
    /// <summary>Цветовая карта Hot (чёрный → красный → жёлтый → белый).</summary>
    Hot,
    /// <summary>Цветовая карта Cool (голубой → пурпурный).</summary>
    Cool,
    /// <summary>Цветовая карта Rainbow (радуга).</summary>
    Rainbow,
    /// <summary>Цветовая карта Bone (чёрно-белая с синим оттенком).</summary>
    Bone,
    /// <summary>Цветовая карта Winter (синий → зелёный).</summary>
    Winter
}
