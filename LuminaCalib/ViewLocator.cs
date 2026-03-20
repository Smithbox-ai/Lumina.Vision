using Avalonia.Controls;
using Avalonia.Controls.Templates;
using LuminaCalib.ViewModels;
using System.Diagnostics.CodeAnalysis;

namespace LuminaCalib
{
    /// <summary>
    /// Локатор представлений — по модели представления находит и создаёт соответствующее представление.
    /// </summary>
    /// <remarks>
    /// Используется конвенция именования: имя класса ViewModel заменяется на View.
    /// Например, <c>MainWindowViewModel</c> → <c>MainWindowView</c>.
    /// Реализация основана на рефлексии и может быть обрезана при AOT-компиляции.
    /// </remarks>
    [RequiresUnreferencedCode(
        "Default implementation of ViewLocator involves reflection which may be trimmed away.",
        Url = "https://docs.avaloniaui.net/docs/concepts/view-locator")]
    public class ViewLocator : IDataTemplate
    {
        /// <summary>
        /// Создаёт экземпляр представления (View), соответствующего переданной модели представления (ViewModel).
        /// </summary>
        /// <param name="param">Объект модели представления (ViewModel).</param>
        /// <returns>
        /// Экземпляр <see cref="Control"/> соответствующего View, или <see cref="TextBlock"/> с сообщением об ошибке,
        /// если View не найден. Возвращает <c>null</c>, если <paramref name="param"/> равен <c>null</c>.
        /// </returns>
        public Control? Build(object? param)
        {
            if (param is null)
                return null;

            // Заменяем "ViewModel" на "View" в полном имени типа
            var name = param.GetType().FullName!.Replace("ViewModel", "View", StringComparison.Ordinal);
            var type = Type.GetType(name);

            if (type != null)
            {
                // Создаём экземпляр найденного типа представления
                return (Control)Activator.CreateInstance(type)!;
            }

            // Представление не найдено — возвращаем текстовый блок с ошибкой
            return new TextBlock { Text = "Not Found: " + name };
        }

        /// <summary>
        /// Определяет, может ли данный шаблон данных обработать указанный объект.
        /// </summary>
        /// <param name="data">Объект данных для проверки.</param>
        /// <returns><c>true</c>, если объект является наследником <see cref="ViewModelBase"/>; иначе <c>false</c>.</returns>
        public bool Match(object? data)
        {
            return data is ViewModelBase;
        }
    }
}
