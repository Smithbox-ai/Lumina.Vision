using LuminaCalib.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace LuminaCalib.Services;

/// <summary>
/// Сервис экспорта калибровочных досок в PDF-формат.
/// </summary>
/// <remarks>
/// Использует библиотеку QuestPDF (лицензия Community) для генерации PDF.
/// Поддерживает экспорт с сохранением физических размеров доски и вывод спецификации.
/// </remarks>
public sealed class PdfExportService
{
    /// <summary>
    /// Статический конструктор: настраивает лицензию QuestPDF.
    /// </summary>
    static PdfExportService()
    {
        // Настраиваем лицензию QuestPDF (Community для open source)
        QuestPDF.Settings.License = LicenseType.Community;
    }

    /// <summary>
    /// Экспортирует доску ChArUco в PDF с корректными физическими размерами.
    /// </summary>
    /// <param name="settings">Настройки доски.</param>
    /// <param name="boardImagePath">Путь к изображению доски (PNG).</param>
    /// <param name="outputPath">Путь к выходному PDF-файлу.</param>
    /// <remarks>
    /// Компоновка страницы:
    /// <list type="bullet">
    /// <item><description>Заголовок: «LuminaCalib ChArUco Board».</description></item>
    /// <item><description>Изображение доски с физическими размерами в мм.</description></item>
    /// <item><description>Таблица спецификаций (сетка, размер квадрата, маркер, словарь).</description></item>
    /// <item><description>Нижний колонтитул: физический размер и дата генерации.</description></item>
    /// </list>
    /// </remarks>
    public void ExportToPdf(BoardGeneratorSettings settings, string boardImagePath, string outputPath)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentException.ThrowIfNullOrEmpty(boardImagePath);
        ArgumentException.ThrowIfNullOrEmpty(outputPath);

        if (!File.Exists(boardImagePath))
        {
            throw new FileNotFoundException("Изображение доски не найдено", boardImagePath);
        }

        // Вычисляем размер страницы: размер доски + отступы + место для информации
        float pageMarginMm = 15f;
        float pageWidthMm = settings.BoardWidthMm + 2 * pageMarginMm;
        float pageHeightMm = settings.BoardHeightMm + 2 * pageMarginMm + 30; // Дополнительное место для спецификаций

        Document.Create(container =>
        {
            container.Page(page =>
            {
                // Устанавливаем размер страницы и отступы в миллиметрах
                page.Size(pageWidthMm, pageHeightMm, Unit.Millimetre);
                page.Margin(pageMarginMm, Unit.Millimetre);
                page.DefaultTextStyle(x => x.FontSize(9));

                // Заголовок страницы
                page.Header()
                    .Text("LuminaCalib ChArUco Board")
                    .FontSize(12)
                    .Bold()
                    .AlignCenter();

                page.Content()
                    .Column(column =>
                    {
                        // Изображение доски с точными физическими размерами
                        column.Item()
                            .AlignCenter()
                            .Width(settings.BoardWidthMm, Unit.Millimetre)
                            .Height(settings.BoardHeightMm, Unit.Millimetre)
                            .Image(boardImagePath);

                        column.Item().Height(5, Unit.Millimetre);

                        // Таблица спецификаций доски
                        column.Item().Table(table =>
                        {
                            table.ColumnsDefinition(cols =>
                            {
                                cols.RelativeColumn(1);
                                cols.RelativeColumn(1);
                                cols.RelativeColumn(1);
                                cols.RelativeColumn(1);
                            });

                            table.Cell().Text($"Grid: {settings.SquaresX}x{settings.SquaresY}");
                            table.Cell().Text($"Square: {settings.SquareLength}mm");
                            table.Cell().Text($"Marker: {settings.MarkerLength:F1}mm");
                            table.Cell().Text($"Dict: {settings.Dictionary}");
                        });
                    });

                // Нижний колонтитул: физический размер и дата генерации
                page.Footer()
                    .AlignCenter()
                    .Text(text =>
                    {
                        text.Span($"Physical Size: {settings.BoardWidthMm}mm x {settings.BoardHeightMm}mm | ");
                        text.Span($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm}");
                    });
            });
        }).GeneratePdf(outputPath);
    }

    /// <summary>
    /// Экспортирует спецификацию доски в текстовый файл.
    /// </summary>
    /// <param name="settings">Настройки доски.</param>
    /// <param name="outputPath">Путь к выходному файлу спецификации.</param>
    /// <remarks>
    /// Включает информацию о сетке, размерах, словаре, DPI, а также инструкции по печати.
    /// </remarks>
    public void ExportSpecification(BoardGeneratorSettings settings, string outputPath)
    {
        var content = $@"LuminaCalib ChArUco Board Specification
=======================================

Grid Size:       {settings.SquaresX} x {settings.SquaresY} squares
Square Size:     {settings.SquareLength} mm
Marker Size:     {settings.MarkerLength:F1} mm ({settings.MarkerLengthRatio * 100:F0}% of square)
Dictionary:      {settings.Dictionary}

Physical Board Size:
  Width:         {settings.BoardWidthMm} mm
  Height:        {settings.BoardHeightMm} mm

Rendering Settings:
  DPI:           {settings.Dpi}
  Margin:        {settings.Margin} px
  Image Size:    {settings.BoardWidthPx} x {settings.BoardHeightPx} px

Generated:       {DateTime.Now:yyyy-MM-dd HH:mm:ss}
Generator:       LuminaCalib Board Generator

Instructions:
1. Print this board at 100% scale (no scaling/fit-to-page)
2. Measure the printed squares to verify {settings.SquareLength}mm size
3. Mount on flat, rigid surface
4. Avoid glossy paper (matte preferred for camera detection)
";

        File.WriteAllText(outputPath, content);
    }
}

