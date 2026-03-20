using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace LuminaCalib.Controls;

/// <summary>
/// Конвертер логического значения в цвет.
/// <c>true</c>  → Зелёный (#00C853) — шаблон обнаружен.
/// <c>false</c> → Красный (#FF5252) — шаблон не обнаружен.
/// </summary>
public sealed class BoolToColorConverter : IValueConverter
{
    /// <summary>
    /// Единственный экземпляр конвертера (синглтон).
    /// </summary>
    public static readonly BoolToColorConverter Instance = new();

    /// <summary>Цвет для значения <c>true</c> (зелёный).</summary>
    private static readonly Color TrueColor = Color.FromRgb(0, 200, 83);
    /// <summary>Цвет для значения <c>false</c> (красный).</summary>
    private static readonly Color FalseColor = Color.FromRgb(255, 82, 82);

    /// <summary>
    /// Преобразует логическое значение в соответствующий цвет.
    /// </summary>
    /// <param name="value">Исходное логическое значение.</param>
    /// <param name="targetType">Целевой тип (не используется).</param>
    /// <param name="parameter">Параметр конвертера (не используется).</param>
    /// <param name="culture">Культура (не используется).</param>
    /// <returns>Зелёный цвет при <c>true</c>, красный при <c>false</c>.</returns>
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is true ? TrueColor : FalseColor;
    }

    /// <summary>
    /// Обратное преобразование не поддерживается.
    /// </summary>
    /// <exception cref="NotSupportedException">Всегда выбрасывается.</exception>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
