using System.ComponentModel;
using System.Drawing;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Emgu.CV;
using Emgu.CV.CvEnum;
using LuminaCalib.Models;
using LuminaCalib.Services;

namespace LuminaCalib.ViewModels;

/// <summary>
/// ViewModel для представления генератора калибровочных досок (ChArUco / Chessboard).
/// </summary>
/// <remarks>
/// Рабочий процесс генерации доски:
/// 1. Пользователь задаёт параметры (размер сетки, размер клетки, словарь ArUco, DPI и т.д.).
/// 2. При изменении любого параметра автоматически запускается <see cref="GeneratePreviewAsync"/>.
/// 3. Результат генерации отображается в <see cref="PreviewImage"/>.
/// 4. Пользователь может экспортировать доску в PNG, PDF или текстовую спецификацию.
/// </remarks>
public partial class BoardGeneratorViewModel : ViewModelBase, IDisposable
{
    /// <summary>Сервис настроек приложения.</summary>
    private readonly SettingsService _settingsService;

    /// <summary>Генератор калибровочных досок ChArUco/Chessboard.</summary>
    private readonly CharucoBoardGenerator _generator;

    /// <summary>Сервис экспорта в PDF.</summary>
    private readonly PdfExportService _pdfExporter;

    /// <summary>Семафор для предотвращения одновременной генерации досок.</summary>
    private readonly SemaphoreSlim _generationLock = new(1, 1);

    /// <summary>Текущая сгенерированная доска (матрица OpenCV).</summary>
    private Mat? _currentBoard;

    /// <summary>Флаг, указывающий что объект был освобождён.</summary>
    private bool _disposed;

    /// <summary>Токен отмены текущей генерации предпросмотра.</summary>
    private CancellationTokenSource? _previewCts;

    /// <summary>Флаг отложенной регенерации (если параметры изменились во время генерации).</summary>
    private bool _pendingRegeneration;

    // === Настройки доски ===

    /// <summary>Количество квадратов по горизонтали.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BoardWidthMm))]
    [NotifyPropertyChangedFor(nameof(BoardHeightMm))]
    private int _squaresX = 9;

    /// <summary>Количество квадратов по вертикали.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BoardWidthMm))]
    [NotifyPropertyChangedFor(nameof(BoardHeightMm))]
    private int _squaresY = 6;

    /// <summary>Размер одного квадрата в миллиметрах.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BoardWidthMm))]
    [NotifyPropertyChangedFor(nameof(BoardHeightMm))]
    [NotifyPropertyChangedFor(nameof(MarkerLength))]
    private float _squareLength = 30f;

    /// <summary>Соотношение размера маркера к размеру квадрата (0..1).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MarkerLength))]
    private float _markerLengthRatio = 0.73f;

    /// <summary>Выбранный словарь ArUco для генерации маркеров.</summary>
    [ObservableProperty]
    private CharucoDictionary _dictionary = CharucoDictionary.Dict6x6_250;

    /// <summary>Разрешение генерации (точек на дюйм) для печати.</summary>
    [ObservableProperty]
    private int _dpi = 300;

    /// <summary>Отступ от краёв доски в пикселях.</summary>
    [ObservableProperty]
    private int _margin = 50;

    /// <summary>Тип калибровочной доски (ChArUco или Chessboard).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCharucoBoard))]
    private BoardType _boardType = BoardType.ChArUco;

    // === Вычисляемые свойства ===

    /// <summary>Размер маркера в мм, рассчитанный как <see cref="SquareLength"/> × <see cref="MarkerLengthRatio"/>.</summary>
    public float MarkerLength => SquareLength * MarkerLengthRatio;

    /// <summary>Ширина доски в миллиметрах.</summary>
    public float BoardWidthMm => SquaresX * SquareLength;

    /// <summary>Высота доски в миллиметрах.</summary>
    public float BoardHeightMm => SquaresY * SquareLength;

    /// <summary>Указывает, является ли текущий тип доски ChArUco.</summary>
    public bool IsCharucoBoard => BoardType == BoardType.ChArUco;

    // === Предпросмотр ===

    /// <summary>Изображение предпросмотра сгенерированной доски.</summary>
    [ObservableProperty]
    private WriteableBitmap? _previewImage;

    /// <summary>Текст статусного сообщения.</summary>
    [ObservableProperty]
    private string _statusMessage = string.Empty;

    /// <summary>Флаг: идёт генерация доски.</summary>
    [ObservableProperty]
    private bool _isGenerating;

    /// <summary>Текст индикатора процесса генерации доски.</summary>
    [ObservableProperty]
    private string _generatingText = "Generating...";

    // === Локализованные тексты UI ===

    /// <summary>Локализованный заголовок генератора.</summary>
    [ObservableProperty]
    private string _headerText = "ChArUco Board Generator";

    /// <summary>Локализованная метка «Тип доски».</summary>
    [ObservableProperty]
    private string _boardTypeLabelText = "Board Type";

    /// <summary>Локализованная метка «Размер сетки».</summary>
    [ObservableProperty]
    private string _gridSizeLabelText = "Grid Size (Squares)";

    /// <summary>Локализованная метка «Размер квадрата».</summary>
    [ObservableProperty]
    private string _squareSizeLabelText = "Square Size (mm)";

    /// <summary>Локализованный префикс метки размера маркера.</summary>
    [ObservableProperty]
    private string _markerSizeLabelPrefixText = "Marker Size:";

    /// <summary>Открывающая скобка для отображения коэффициента маркера.</summary>
    [ObservableProperty]
    private string _markerSizeRatioOpenText = "(";

    /// <summary>Закрывающая скобка для отображения коэффициента маркера.</summary>
    [ObservableProperty]
    private string _markerSizeRatioCloseText = ")";

    /// <summary>Локализованная метка «Словарь ArUco».</summary>
    [ObservableProperty]
    private string _arucoDictionaryLabelText = "ArUco Dictionary";

    /// <summary>Локализованная метка «DPI».</summary>
    [ObservableProperty]
    private string _dpiLabelText = "DPI (for printing)";

    /// <summary>Локализованная метка «Поля (px)».</summary>
    [ObservableProperty]
    private string _marginLabelText = "Margin (px)";

    /// <summary>Локализованная метка «Физический размер».</summary>
    [ObservableProperty]
    private string _physicalSizeLabelText = "Physical Size:";

    /// <summary>Локализованная единица измерения «мм».</summary>
    [ObservableProperty]
    private string _millimetersUnitText = "mm";

    /// <summary>Локализованный текст кнопки генерации предпросмотра.</summary>
    [ObservableProperty]
    private string _generatePreviewButtonText = "Generate Preview";

    /// <summary>Локализованный заголовок секции экспорта.</summary>
    [ObservableProperty]
    private string _exportHeaderText = "Export";

    /// <summary>Локализованный текст кнопки «Сохранить как PNG».</summary>
    [ObservableProperty]
    private string _saveAsPngButtonText = "Save as PNG";

    /// <summary>Локализованный текст кнопки «Сохранить как PDF».</summary>
    [ObservableProperty]
    private string _saveAsPdfButtonText = "Save as PDF (for printing)";

    /// <summary>Локализованный текст кнопки «Сохранить спецификацию».</summary>
    [ObservableProperty]
    private string _saveSpecificationButtonText = "Save Specification";

    /// <summary>Локализованный текст кнопки «Применить в калибровку».</summary>
    [ObservableProperty]
    private string _applyToCalibrationText = "Apply to Calibration";

    // === Доступные варианты выбора ===

    /// <summary>Список доступных словарей ArUco.</summary>
    public CharucoDictionary[] AvailableDictionaries { get; } = Enum.GetValues<CharucoDictionary>();

    /// <summary>Список допустимых типов досок.</summary>
    public BoardType[] AvailableBoardTypes { get; } = [BoardType.ChArUco, BoardType.Chessboard];

    /// <summary>Список допустимых значений DPI для печати.</summary>
    public int[] AvailableDpis { get; } = [72, 96, 150, 300, 600];

    /// <summary>
    /// Конструктор по умолчанию (design-time).
    /// </summary>
    public BoardGeneratorViewModel() : this(new SettingsService())
    {
    }

    /// <summary>
    /// Инициализирует ViewModel с указанным сервисом настроек.
    /// </summary>
    /// <param name="settingsService">Сервис настроек приложения.</param>
    public BoardGeneratorViewModel(SettingsService settingsService)
    {
        _settingsService = settingsService;
        _generator = new CharucoBoardGenerator();
        _pdfExporter = new PdfExportService();

        _settingsService.Settings.PropertyChanged += OnSettingsChanged;
        UpdateLocalizedUiTexts();
        StatusMessage = L("Configure board settings and click Generate", "Настройте параметры доски и нажмите Сгенерировать");
    }

    /// <summary>
    /// Создаёт объект настроек доски на основе текущих значений свойств.
    /// </summary>
    /// <returns>Настройки для генерации доски.</returns>
    private BoardGeneratorSettings CreateSettings()
    {
        return new BoardGeneratorSettings
        {
            SquaresX = SquaresX,
            SquaresY = SquaresY,
            SquareLength = SquareLength,
            MarkerLengthRatio = MarkerLengthRatio,
            Dictionary = Dictionary,
            Dpi = Dpi,
            Margin = Margin
        };
    }

    /// <summary>
    /// Формирует базовое имя файла для экспорта (без расширения).
    /// </summary>
    /// <returns>Строка вида "ChArUco_9x6_Dict6x6_250" или "Chessboard_9x6".</returns>
    private string BuildExportFileStem()
    {
        if (BoardType == BoardType.ChArUco)
        {
            return $"ChArUco_{SquaresX}x{SquaresY}_{Dictionary}";
        }

        return $"Chessboard_{SquaresX}x{SquaresY}";
    }

    /// <summary>
    /// Генерирует предпросмотр доски асинхронно.
    /// Использует семафор для предотвращения параллельных генераций.
    /// При изменении параметров во время генерации автоматически перегенерирует доску.
    /// </summary>
    [RelayCommand]
    private async Task GeneratePreviewAsync()
    {
        // Попытка захватить семафор без ожидания
        if (!await _generationLock.WaitAsync(0))
        {
            // Генерация уже идёт — отложить повторную
            _pendingRegeneration = true;
            return;
        }

        try
        {
            do
            {
                _pendingRegeneration = false;

                _previewCts?.Cancel();
                _previewCts?.Dispose();
                _previewCts = new CancellationTokenSource();
                var token = _previewCts.Token;

                IsGenerating = true;
                StatusMessage = L("Generating preview...", "Генерация предпросмотра...");

                try
                {
                    var settings = CreateSettings();

                    var result = await Task.Run(() =>
                    {
                        token.ThrowIfCancellationRequested();
                        return BoardType == BoardType.ChArUco
                            ? _generator.GenerateCharucoBoard(settings)
                            : _generator.GenerateChessboard(settings);
                    }, token);

                    if (token.IsCancellationRequested)
                    {
                        result?.Dispose();
                        continue;
                    }

                    _currentBoard?.Dispose();
                    _currentBoard = result;
                    UpdatePreviewImage();
                    StatusMessage = string.Format(
                        L("Generated {0}mm x {1}mm board", "Сгенерирована доска {0}мм x {1}мм"),
                        BoardWidthMm,
                        BoardHeightMm);
                }
                catch (OperationCanceledException)
                {
                    // Ожидаемое исключение при быстром изменении параметров.
                }
                catch (Exception ex)
                {
                    StatusMessage = string.Format(
                        L("Error: {0}", "Ошибка: {0}"),
                        ex.Message);
                }
                finally
                {
                    IsGenerating = false;
                }
            } while (_pendingRegeneration);
        }
        finally
        {
            _generationLock.Release();
        }
    }

    /// <summary>
    /// Обновляет изображение предпросмотра на основе текущей сгенерированной доски.
    /// Масштабирует изображение для отображения в UI (макс. 600px) и конвертирует в BGRA.
    /// </summary>
    private void UpdatePreviewImage()
    {
        if (_currentBoard == null || _currentBoard.IsEmpty)
        {
            return;
        }

        var maxPreviewSize = 600;
        var scale = Math.Min(
            (double)maxPreviewSize / _currentBoard.Width,
            (double)maxPreviewSize / _currentBoard.Height);
        scale = Math.Min(scale, 1.0);

        var previewWidth = (int)(_currentBoard.Width * scale);
        var previewHeight = (int)(_currentBoard.Height * scale);

        using var resized = new Mat();
        CvInvoke.Resize(_currentBoard, resized, new Size(previewWidth, previewHeight));

        using var bgra = new Mat();
        if (resized.NumberOfChannels == 1)
        {
            CvInvoke.CvtColor(resized, bgra, ColorConversion.Gray2Bgra);
        }
        else if (resized.NumberOfChannels == 3)
        {
            CvInvoke.CvtColor(resized, bgra, ColorConversion.Bgr2Bgra);
        }
        else
        {
            resized.CopyTo(bgra);
        }

        var oldPreview = PreviewImage;
        PreviewImage = new WriteableBitmap(
            new Avalonia.PixelSize(previewWidth, previewHeight),
            new Avalonia.Vector(96, 96),
            PixelFormats.Bgra8888,
            AlphaFormat.Premul);
        oldPreview?.Dispose();

        using var buffer = PreviewImage.Lock();
        var srcPtr = bgra.DataPointer;
        var dstPtr = buffer.Address;
        var rowBytes = previewWidth * 4;

        for (var y = 0; y < previewHeight; y++)
        {
            unsafe
            {
                Buffer.MemoryCopy(
                    (void*)(srcPtr + y * bgra.Step),
                    (void*)(dstPtr + y * buffer.RowBytes),
                    rowBytes,
                    rowBytes);
            }
        }
    }

    /// <summary>
    /// Сохраняет сгенерированную доску в PNG-файл на рабочий стол.
    /// </summary>
    [RelayCommand]
    private async Task SaveAsPngAsync()
    {
        if (_currentBoard == null)
        {
            StatusMessage = L("Generate a board first", "Сначала сгенерируйте доску");
            return;
        }

        try
        {
            var filename = $"{BuildExportFileStem()}.png";
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), filename);

            await Task.Run(() => _generator.SaveAsPng(_currentBoard, path));
            StatusMessage = string.Format(
                L("Saved to {0}", "Сохранено в {0}"),
                filename);
        }
        catch (Exception ex)
        {
            StatusMessage = string.Format(
                L("Save failed: {0}", "Ошибка сохранения: {0}"),
                ex.Message);
        }
    }

    /// <summary>
    /// Сохраняет сгенерированную доску в PDF-файл (готовый для печати) на рабочий стол.
    /// </summary>
    [RelayCommand]
    private async Task SaveAsPdfAsync()
    {
        if (_currentBoard == null)
        {
            StatusMessage = L("Generate a board first", "Сначала сгенерируйте доску");
            return;
        }

        try
        {
            var settings = CreateSettings();
            var tempPng = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.png");
            var filename = $"{BuildExportFileStem()}.pdf";
            var pdfPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), filename);

            await Task.Run(() =>
            {
                try
                {
                    _generator.SaveAsPng(_currentBoard, tempPng);
                    _pdfExporter.ExportToPdf(settings, tempPng, pdfPath);
                }
                finally
                {
                    if (File.Exists(tempPng))
                    {
                        File.Delete(tempPng);
                    }
                }
            });

            StatusMessage = string.Format(
                L("Saved PDF to {0}", "PDF сохранён в {0}"),
                filename);
        }
        catch (Exception ex)
        {
            StatusMessage = string.Format(
                L("PDF export failed: {0}", "Ошибка экспорта PDF: {0}"),
                ex.Message);
        }
    }

    /// <summary>
    /// Сохраняет текстовую спецификацию доски на рабочий стол.
    /// </summary>
    [RelayCommand]
    private async Task SaveSpecificationAsync()
    {
        try
        {
            var settings = CreateSettings();
            var filename = $"{BuildExportFileStem()}_spec.txt";
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), filename);

            await Task.Run(() => _pdfExporter.ExportSpecification(settings, path));
            StatusMessage = string.Format(
                L("Saved specification to {0}", "Спецификация сохранена в {0}"),
                filename);
        }
        catch (Exception ex)
        {
            StatusMessage = string.Format(
                L("Save failed: {0}", "Ошибка сохранения: {0}"),
                ex.Message);
        }
    }

    /// <summary>
    /// Обработчик изменения настроек приложения. Перезагружает локализованные тексты при смене языка.
    /// </summary>
    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AppSettings.Language))
        {
            UpdateLocalizedUiTexts();
        }
    }

    /// <summary>
    /// Обновляет все локализованные тексты UI в соответствии с текущим языком.
    /// </summary>
    private void UpdateLocalizedUiTexts()
    {
        HeaderText = L("ChArUco Board Generator", "Генератор доски ChArUco");
        BoardTypeLabelText = L("Board Type", "Тип доски");
        GridSizeLabelText = L("Grid Size (Squares)", "Размер сетки (клетки)");
        SquareSizeLabelText = L("Square Size (mm)", "Размер квадрата (мм)");

        MarkerSizeLabelPrefixText = L("Marker Size:", "Размер маркера:");
        MarkerSizeRatioOpenText = "(";
        MarkerSizeRatioCloseText = ")";

        ArucoDictionaryLabelText = L("ArUco Dictionary", "Словарь ArUco");
        DpiLabelText = L("DPI (for printing)", "DPI (для печати)");
        MarginLabelText = L("Margin (px)", "Поля (px)");
        PhysicalSizeLabelText = L("Physical Size:", "Физический размер:");
        MillimetersUnitText = L("mm", "мм");

        GeneratePreviewButtonText = L("Generate Preview", "Сгенерировать предпросмотр");
        ExportHeaderText = L("Export", "Экспорт");
        SaveAsPngButtonText = L("Save as PNG", "Сохранить как PNG");
        SaveAsPdfButtonText = L("Save as PDF (for printing)", "Сохранить как PDF (для печати)");
        SaveSpecificationButtonText = L("Save Specification", "Сохранить спецификацию");
        ApplyToCalibrationText = L("Apply to Calibration", "Применить в калибровку");
        GeneratingText = L("Generating...", "Генерация...");
    }

    /// <summary>
    /// Копирует текущие параметры доски в настройки калибровки.
    /// Для шахматной доски конвертирует размеры из клеток в внутренние углы (OpenCV).
    /// </summary>
    [RelayCommand]
    private void ApplyToCalibration()
    {
        var appSettings = _settingsService.Settings;
        var selectedBoardType = BoardType;
        appSettings.BoardType = selectedBoardType;

        if (selectedBoardType == BoardType.Chessboard)
        {
            appSettings.PatternWidth = Math.Max(2, SquaresX - 1);
            appSettings.PatternHeight = Math.Max(2, SquaresY - 1);
        }
        else
        {
            appSettings.PatternWidth = SquaresX;
            appSettings.PatternHeight = SquaresY;
            appSettings.CharucoDictionary = Dictionary;
            appSettings.MarkerSizeRatio = MarkerLengthRatio;
        }

        appSettings.SquareSize = SquareLength;
    }

    // Автоперегенерация предпросмотра при изменении настроек

    /// <summary>Перегенерация при изменении количества клеток по X.</summary>
    partial void OnSquaresXChanged(int oldValue, int newValue) => _ = GeneratePreviewAsync();

    /// <summary>Перегенерация при изменении количества клеток по Y.</summary>
    partial void OnSquaresYChanged(int oldValue, int newValue) => _ = GeneratePreviewAsync();

    /// <summary>Перегенерация при изменении размера квадрата.</summary>
    partial void OnSquareLengthChanged(float oldValue, float newValue) => _ = GeneratePreviewAsync();

    /// <summary>Перегенерация при изменении коэффициента размера маркера.</summary>
    partial void OnMarkerLengthRatioChanged(float oldValue, float newValue) => _ = GeneratePreviewAsync();

    /// <summary>Перегенерация при смене словаря ArUco.</summary>
    partial void OnDictionaryChanged(CharucoDictionary oldValue, CharucoDictionary newValue) => _ = GeneratePreviewAsync();

    /// <summary>Перегенерация при смене типа доски.</summary>
    partial void OnBoardTypeChanged(BoardType oldValue, BoardType newValue) => _ = GeneratePreviewAsync();

    /// <summary>Освобождает ресурсы: матрицу доски, изображение предпросмотра, генератор досок.</summary>

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _settingsService.Settings.PropertyChanged -= OnSettingsChanged;
        _previewCts?.Cancel();
        _previewCts?.Dispose();
        _currentBoard?.Dispose();
        PreviewImage?.Dispose();
        _generator.Dispose();
        _generationLock.Dispose();
    }
}

