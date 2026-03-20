using System.ComponentModel;
using System.Text.Json;
using LuminaCalib.Models;

namespace LuminaCalib.Services;

/// <summary>
/// Сервис сохранения и загрузки настроек приложения.
/// </summary>
/// <remarks>
/// Настройки сериализуются в JSON и хранятся на диске (settings.json).
/// Загрузка происходит лениво (при первом обращении к <see cref="Settings"/>).
/// Поддерживает асинхронное и синхронное сохранение, а также сброс к значениям по умолчанию.
/// </remarks>
public sealed class SettingsService
{
    /// <summary>Путь к файлу настроек на диске.</summary>
    private readonly string _settingsPath;

    /// <summary>Настройки JSON-сериализатора.</summary>
    private readonly JsonSerializerOptions _jsonOptions;

    /// <summary>Кэшированные настройки (ленивая загрузка).</summary>
    private AppSettings? _cachedSettings;

    /// <summary>
    /// Текущий язык приложения.
    /// </summary>
    /// <remarks>
    /// Статическое свойство для быстрого доступа из ViewModelBase.L() без необходимости
    /// инжектировать сервис настроек. Обновляется автоматически при загрузке/сохранении/сбросе.
    /// </remarks>
    public static string CurrentLanguage { get; set; } = "ru";

    /// <summary>
    /// Инициализирует сервис настроек.
    /// </summary>
    /// <param name="settingsPath">Путь к файлу настроек. Если <c>null</c>, используется <c>settings.json</c> в каталоге приложения.</param>
    public SettingsService(string? settingsPath = null)
    {
        _settingsPath = settingsPath ?? GetDefaultSettingsPath();
        // Настраиваем JSON-сериализатор с отступами и camelCase
        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
        };
    }

    /// <summary>
    /// Текущие настройки приложения. Ленивая загрузка при первом обращении.
    /// </summary>
    public AppSettings Settings
    {
        get
        {
            if (_cachedSettings == null)
            {
                // Первое обращение — загружаем с диска
                Load();
            }
            return _cachedSettings!;
        }
    }

    /// <summary>
    /// Подписывается на изменения настроек для автообновления <see cref="CurrentLanguage"/>.
    /// </summary>
    /// <param name="settings">Объект настроек для подписки.</param>
    private void SubscribeToSettingsChanges(AppSettings settings)
    {
        settings.PropertyChanged += OnSettingsPropertyChanged;
    }

    /// <summary>
    /// Обработчик изменения свойств настроек: обновляет <see cref="CurrentLanguage"/> при смене языка.
    /// </summary>
    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AppSettings.Language) && sender is AppSettings s)
        {
            CurrentLanguage = s.Language?.Trim().ToLowerInvariant() ?? "ru";
        }
    }

    /// <summary>
    /// Загружает настройки с диска или возвращает значения по умолчанию, если файл отсутствует.
    /// </summary>
    /// <returns>Загруженный объект настроек.</returns>
    public AppSettings Load()
    {
        // Отписываемся от старых настроек, если были
        if (_cachedSettings != null)
            _cachedSettings.PropertyChanged -= OnSettingsPropertyChanged;

        try
        {
            if (File.Exists(_settingsPath))
            {
                // Читаем и десериализуем JSON
                var json = File.ReadAllText(_settingsPath);
                _cachedSettings = JsonSerializer.Deserialize<AppSettings>(json, _jsonOptions) ?? new AppSettings();
            }
            else
            {
                // Файл не найден — используем настройки по умолчанию
                _cachedSettings = new AppSettings();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Не удалось загрузить настройки: {ex.Message}");
            _cachedSettings = new AppSettings();
        }

        // Синхронизируем статическое свойство языка и подписываемся на изменения
        CurrentLanguage = _cachedSettings.Language?.Trim().ToLowerInvariant() ?? "ru";
        SubscribeToSettingsChanges(_cachedSettings);
        return _cachedSettings;
    }

    /// <summary>
    /// Асинхронно сохраняет текущие настройки на диск.
    /// </summary>
    public async Task SaveAsync()
    {
        if (_cachedSettings == null) return;

        // Синхронизируем статическое свойство языка
        CurrentLanguage = _cachedSettings.Language?.Trim().ToLowerInvariant() ?? "ru";

        try
        {
            // Создаём каталог при необходимости
            var directory = Path.GetDirectoryName(_settingsPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // Сериализуем и записываем асинхронно
            var json = JsonSerializer.Serialize(_cachedSettings, _jsonOptions);
            await File.WriteAllTextAsync(_settingsPath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Не удалось сохранить настройки: {ex.Message}");
        }
    }

    /// <summary>
    /// Синхронно сохраняет текущие настройки на диск.
    /// </summary>
    public void Save()
    {
        if (_cachedSettings == null) return;

        // Синхронизируем статическое свойство языка
        CurrentLanguage = _cachedSettings.Language?.Trim().ToLowerInvariant() ?? "ru";

        try
        {
            // Создаём каталог при необходимости
            var directory = Path.GetDirectoryName(_settingsPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // Сериализуем и записываем синхронно
            var json = JsonSerializer.Serialize(_cachedSettings, _jsonOptions);
            File.WriteAllText(_settingsPath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Не удалось сохранить настройки: {ex.Message}");
        }
    }

    /// <summary>
    /// Сбрасывает настройки к значениям по умолчанию.
    /// </summary>
    public void Reset()
    {
        // Отписываемся от старых настроек
        if (_cachedSettings != null)
            _cachedSettings.PropertyChanged -= OnSettingsPropertyChanged;

        // Создаём новый объект с значениями по умолчанию
        _cachedSettings = new AppSettings();
        CurrentLanguage = _cachedSettings.Language?.Trim().ToLowerInvariant() ?? "ru";
        SubscribeToSettingsChanges(_cachedSettings);
    }

    /// <summary>
    /// Возвращает путь к файлу настроек по умолчанию: settings.json в каталоге приложения.
    /// </summary>
    /// <returns>Полный путь к файлу настроек.</returns>
    private static string GetDefaultSettingsPath()
    {
        return Path.Combine(AppContext.BaseDirectory, "settings.json");
    }
}
