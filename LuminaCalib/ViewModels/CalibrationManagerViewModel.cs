using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LuminaCalib.Calibration;
using LuminaCalib.Models;
using LuminaCalib.Services;

namespace LuminaCalib.ViewModels;

/// <summary>
/// ViewModel для управления библиотекой калибровок и сессий захвата.
/// </summary>
/// <remarks>
/// Управляет сохранёнными XML-файлами калибровок и сессиями захвата,
/// разделёнными по режимам калибровки (Auto, StereoOnly, SingleCamera).
/// Позволяет назначать активные калибровки/сессии, удалять сессии
/// и открывать папки в проводнике.
/// </remarks>
public partial class CalibrationManagerViewModel : ViewModelBase, IDisposable
{
    /// <summary>Сервис настроек приложения.</summary>
    private readonly SettingsService _settingsService;

    /// <summary>Настройки сериализации JSON для чтения манифестов сессий.</summary>
    private readonly JsonSerializerOptions _jsonOptions;

    /// <summary>Флаг освобождения ресурсов.</summary>
    private bool _disposed;

    /// <summary>Сервис отслеживания изменений в папке калибровок.</summary>
    private FileWatcherService? _fileWatcher;

    /// <summary>Путь к отслеживаемой папке калибровок.</summary>
    private string _calibrationFolderPath = string.Empty;

    /// <summary>Выбранный режим калибровки.</summary>
    [ObservableProperty]
    private CalibrationMode _selectedMode = CalibrationMode.Auto;

    /// <summary>Загруженные детали выбранной калибровки.</summary>
    [ObservableProperty]
    private CalibrationResult? _loadedCalibration;

    /// <summary>Унифицированная выбранная калибровка для текущего режима.</summary>
    [ObservableProperty]
    private CalibrationLibraryItem? _selectedCalibration;

    /// <summary>Унифицированная выбранная сессия для текущего режима.</summary>
    [ObservableProperty]
    private CaptureSessionLibraryItem? _selectedSession;

    /// <summary>Список калибровок режима Auto.</summary>
    [ObservableProperty]
    private List<CalibrationLibraryItem> _autoCalibrations = [];

    /// <summary>Список калибровок режима StereoOnly.</summary>
    [ObservableProperty]
    private List<CalibrationLibraryItem> _stereoOnlyCalibrations = [];

    /// <summary>Список калибровок режима SingleCamera.</summary>
    [ObservableProperty]
    private List<CalibrationLibraryItem> _singleCameraCalibrations = [];

    /// <summary>Выбранная калибровка Auto.</summary>
    [ObservableProperty]
    private CalibrationLibraryItem? _selectedAutoCalibration;

    /// <summary>Выбранная калибровка StereoOnly.</summary>
    [ObservableProperty]
    private CalibrationLibraryItem? _selectedStereoOnlyCalibration;

    /// <summary>Выбранная калибровка SingleCamera.</summary>
    [ObservableProperty]
    private CalibrationLibraryItem? _selectedSingleCameraCalibration;

    /// <summary>Список сессий захвата режима Auto.</summary>
    [ObservableProperty]
    private List<CaptureSessionLibraryItem> _autoSessions = [];

    /// <summary>Список сессий захвата режима StereoOnly.</summary>
    [ObservableProperty]
    private List<CaptureSessionLibraryItem> _stereoOnlySessions = [];

    /// <summary>Список сессий захвата режима SingleCamera.</summary>
    [ObservableProperty]
    private List<CaptureSessionLibraryItem> _singleCameraSessions = [];

    /// <summary>Выбранная сессия Auto.</summary>
    [ObservableProperty]
    private CaptureSessionLibraryItem? _selectedAutoSession;

    /// <summary>Выбранная сессия StereoOnly.</summary>
    [ObservableProperty]
    private CaptureSessionLibraryItem? _selectedStereoOnlySession;

    /// <summary>Выбранная сессия SingleCamera.</summary>
    [ObservableProperty]
    private CaptureSessionLibraryItem? _selectedSingleCameraSession;

    /// <summary>Текст активной калибровки режима Auto.</summary>
    [ObservableProperty]
    private string _activeAutoCalibrationText = string.Empty;

    /// <summary>Текст активной калибровки режима StereoOnly.</summary>
    [ObservableProperty]
    private string _activeStereoOnlyCalibrationText = string.Empty;

    /// <summary>Текст активной калибровки режима SingleCamera.</summary>
    [ObservableProperty]
    private string _activeSingleCameraCalibrationText = string.Empty;

    /// <summary>Текст активной сессии режима Auto.</summary>
    [ObservableProperty]
    private string _activeAutoSessionText = string.Empty;

    /// <summary>Текст активной сессии режима StereoOnly.</summary>
    [ObservableProperty]
    private string _activeStereoOnlySessionText = string.Empty;

    /// <summary>Текст активной сессии режима SingleCamera.</summary>
    [ObservableProperty]
    private string _activeSingleCameraSessionText = string.Empty;

    /// <summary>Текст статусного сообщения.</summary>
    [ObservableProperty]
    private string _statusMessage = string.Empty;

    /// <summary>Делегат запроса подтверждения у пользователя (title, message, confirmText, cancelText → bool).</summary>
    public Func<string, string, string, string, Task<bool>>? RequestConfirmation { get; set; }

    /// <summary>Локализованный текст кнопки «Открыть папку калибровок».</summary>
    [ObservableProperty]
    private string _openCalibrationsFolderText = "Open Calibrations Folder";

    /// <summary>Локализованный текст кнопки «Открыть папку сессий».</summary>
    [ObservableProperty]
    private string _openSessionsFolderText = "Open Sessions Folder";

    /// <summary>Локализованный заголовок режима Auto.</summary>
    [ObservableProperty]
    private string _autoModeTitleText = "Auto Mode";

    /// <summary>Локализованный заголовок режима StereoOnly.</summary>
    [ObservableProperty]
    private string _stereoOnlyModeTitleText = "StereoOnly Mode";

    /// <summary>Локализованный заголовок режима SingleCamera.</summary>
    [ObservableProperty]
    private string _singleCameraModeTitleText = "SingleCamera Mode";

    /// <summary>Локализованный заголовок столбца калибровок.</summary>
    [ObservableProperty]
    private string _calibrationsColumnTitleText = "Calibrations (XML)";

    /// <summary>Локализованный заголовок столбца сессий.</summary>
    [ObservableProperty]
    private string _sessionsColumnTitleText = "Capture Sessions";

    /// <summary>Локализованный текст кнопки «Сделать калибровку активной».</summary>
    [ObservableProperty]
    private string _setActiveCalibrationButtonText = "Set Active Calibration";

    /// <summary>Локализованный текст кнопки «Сделать сессию активной».</summary>
    [ObservableProperty]
    private string _setActiveSessionButtonText = "Set Active Session";

    /// <summary>Локализованный текст кнопки «Удалить сессию».</summary>
    [ObservableProperty]
    private string _deleteSessionButtonText = "Delete Session";

    /// <summary>Локализованный текст кнопки «Обновить».</summary>
    [ObservableProperty]
    private string _refreshButtonText = "Refresh";

    /// <summary>Локализованный текст при отсутствии данных.</summary>
    [ObservableProperty]
    private string _noDataText = "No data";

    /// <summary>Локализованный текст метки ошибки репроекции.</summary>
    [ObservableProperty]
    private string _reprojectionErrorLabelText = "Reprojection Error";

    /// <summary>Локализованный текст метки качества.</summary>
    [ObservableProperty]
    private string _qualityLabelText = "Quality";

    /// <summary>Локализованный текст метки пар изображений.</summary>
    [ObservableProperty]
    private string _imagePairsLabelText = "Image Pairs";

    /// <summary>Локализованный текст метки разрешения.</summary>
    [ObservableProperty]
    private string _resolutionLabelText = "Resolution";

    /// <summary>Локализованный текст кнопки удаления калибровки.</summary>
    [ObservableProperty]
    private string _deleteCalibrationButtonText = "Delete Calibration";

    /// <summary>Локализованный текст кнопки загрузки деталей.</summary>
    [ObservableProperty]
    private string _loadCalibrationButtonText = "Load Details";

    /// <summary>Сводная информация об активных элементах режима Auto.</summary>
    [ObservableProperty]
    private string _autoActiveInfoText = string.Empty;

    /// <summary>Сводная информация об активных элементах режима StereoOnly.</summary>
    [ObservableProperty]
    private string _stereoOnlyActiveInfoText = string.Empty;

    /// <summary>Сводная информация об активных элементах режима SingleCamera.</summary>
    [ObservableProperty]
    private string _singleCameraActiveInfoText = string.Empty;

    /// <summary>Событие изменения файлов калибровок (для синхронизации между VM).</summary>
    public event Action? CalibrationFilesChanged;

    /// <summary>Калибровки текущего выбранного режима.</summary>
    public List<CalibrationLibraryItem> CurrentCalibrations => SelectedMode switch
    {
        CalibrationMode.Auto => AutoCalibrations,
        CalibrationMode.StereoOnly => StereoOnlyCalibrations,
        CalibrationMode.SingleCamera => SingleCameraCalibrations,
        _ => []
    };

    /// <summary>Сессии текущего выбранного режима.</summary>
    public List<CaptureSessionLibraryItem> CurrentSessions => SelectedMode switch
    {
        CalibrationMode.Auto => AutoSessions,
        CalibrationMode.StereoOnly => StereoOnlySessions,
        CalibrationMode.SingleCamera => SingleCameraSessions,
        _ => []
    };

    /// <summary>Указывает, есть ли данные для текущего режима.</summary>
    public bool HasCurrentModeData => CurrentCalibrations.Count > 0 || CurrentSessions.Count > 0;

    /// <summary>Сводная информация об активных элементах текущего режима.</summary>
    public string CurrentActiveInfoText => SelectedMode switch
    {
        CalibrationMode.Auto => AutoActiveInfoText,
        CalibrationMode.StereoOnly => StereoOnlyActiveInfoText,
        CalibrationMode.SingleCamera => SingleCameraActiveInfoText,
        _ => string.Empty
    };

    /// <summary>Указывает, есть ли данные для режима Auto.</summary>
    public bool HasAutoData => AutoCalibrations.Count > 0 || AutoSessions.Count > 0;

    /// <summary>Указывает, есть ли данные для режима StereoOnly.</summary>
    public bool HasStereoOnlyData => StereoOnlyCalibrations.Count > 0 || StereoOnlySessions.Count > 0;

    /// <summary>Указывает, есть ли данные для режима SingleCamera.</summary>
    public bool HasSingleCameraData => SingleCameraCalibrations.Count > 0 || SingleCameraSessions.Count > 0;

    /// <summary>Режим Auto выбран.</summary>
    public bool IsAutoModeSelected
    {
        get => SelectedMode == CalibrationMode.Auto;
        set { if (value) SelectedMode = CalibrationMode.Auto; }
    }

    /// <summary>Режим StereoOnly выбран.</summary>
    public bool IsStereoOnlyModeSelected
    {
        get => SelectedMode == CalibrationMode.StereoOnly;
        set { if (value) SelectedMode = CalibrationMode.StereoOnly; }
    }

    /// <summary>Режим SingleCamera выбран.</summary>
    public bool IsSingleCameraModeSelected
    {
        get => SelectedMode == CalibrationMode.SingleCamera;
        set { if (value) SelectedMode = CalibrationMode.SingleCamera; }
    }

    /// <summary>Обработка смены режима калибровки.</summary>
    partial void OnSelectedModeChanged(CalibrationMode value)
    {
        SelectedCalibration = null;
        SelectedSession = null;
        LoadedCalibration?.Dispose();
        LoadedCalibration = null;

        OnPropertyChanged(nameof(IsAutoModeSelected));
        OnPropertyChanged(nameof(IsStereoOnlyModeSelected));
        OnPropertyChanged(nameof(IsSingleCameraModeSelected));
        OnPropertyChanged(nameof(CurrentCalibrations));
        OnPropertyChanged(nameof(CurrentSessions));
        OnPropertyChanged(nameof(HasCurrentModeData));
        OnPropertyChanged(nameof(CurrentActiveInfoText));
    }

    /// <summary>Обновление HasAutoData при изменении списка калибровок Auto.</summary>
    partial void OnAutoCalibrationsChanged(List<CalibrationLibraryItem> value)
    {
        OnPropertyChanged(nameof(HasAutoData));
        if (SelectedMode == CalibrationMode.Auto)
        {
            OnPropertyChanged(nameof(CurrentCalibrations));
            OnPropertyChanged(nameof(HasCurrentModeData));
        }
    }

    /// <summary>Обновление HasAutoData при изменении списка сессий Auto.</summary>
    partial void OnAutoSessionsChanged(List<CaptureSessionLibraryItem> value)
    {
        OnPropertyChanged(nameof(HasAutoData));
        if (SelectedMode == CalibrationMode.Auto)
        {
            OnPropertyChanged(nameof(CurrentSessions));
            OnPropertyChanged(nameof(HasCurrentModeData));
        }
    }

    /// <summary>Обновление HasStereoOnlyData при изменении списка калибровок StereoOnly.</summary>
    partial void OnStereoOnlyCalibrationsChanged(List<CalibrationLibraryItem> value)
    {
        OnPropertyChanged(nameof(HasStereoOnlyData));
        if (SelectedMode == CalibrationMode.StereoOnly)
        {
            OnPropertyChanged(nameof(CurrentCalibrations));
            OnPropertyChanged(nameof(HasCurrentModeData));
        }
    }

    /// <summary>Обновление HasStereoOnlyData при изменении списка сессий StereoOnly.</summary>
    partial void OnStereoOnlySessionsChanged(List<CaptureSessionLibraryItem> value)
    {
        OnPropertyChanged(nameof(HasStereoOnlyData));
        if (SelectedMode == CalibrationMode.StereoOnly)
        {
            OnPropertyChanged(nameof(CurrentSessions));
            OnPropertyChanged(nameof(HasCurrentModeData));
        }
    }

    /// <summary>Обновление HasSingleCameraData при изменении списка калибровок SingleCamera.</summary>
    partial void OnSingleCameraCalibrationsChanged(List<CalibrationLibraryItem> value)
    {
        OnPropertyChanged(nameof(HasSingleCameraData));
        if (SelectedMode == CalibrationMode.SingleCamera)
        {
            OnPropertyChanged(nameof(CurrentCalibrations));
            OnPropertyChanged(nameof(HasCurrentModeData));
        }
    }

    /// <summary>Обновление HasSingleCameraData при изменении списка сессий SingleCamera.</summary>
    partial void OnSingleCameraSessionsChanged(List<CaptureSessionLibraryItem> value)
    {
        OnPropertyChanged(nameof(HasSingleCameraData));
        if (SelectedMode == CalibrationMode.SingleCamera)
        {
            OnPropertyChanged(nameof(CurrentSessions));
            OnPropertyChanged(nameof(HasCurrentModeData));
        }
    }

    /// <summary>Конструктор по умолчанию (design-time).</summary>
    public CalibrationManagerViewModel() : this(new SettingsService())
    {
    }

    /// <summary>
    /// Инициализирует ViewModel с указанным сервисом настроек.
    /// </summary>
    /// <param name="settingsService">Сервис настроек приложения.</param>
    public CalibrationManagerViewModel(SettingsService settingsService)
    {
        _settingsService = settingsService;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter() }
        };

        _settingsService.Settings.PropertyChanged += OnSettingsChanged;
        UpdateLocalizedUiTexts();
        Refresh();
        AttachWatcher(ResolveCalibrationPath());
    }

    /// <summary>Обновляет все списки калибровок и сессий.</summary>
    [RelayCommand]
    private void Refresh()
    {
        try
        {
            LoadCalibrations();
            LoadSessions();
            UpdateActiveSelectionsText();
            StatusMessage = L("Library refreshed.", "Библиотека обновлена.");
            CalibrationFilesChanged?.Invoke();
        }
        catch (Exception ex)
        {
            StatusMessage = $"{L("Failed to refresh library", "Не удалось обновить библиотеку")}: {ex.Message}";
        }
    }

    /// <summary>Назначает активную калибровку для режима Auto.</summary>
    [RelayCommand]
    private void SetActiveAutoCalibration()
    {
        SetActiveCalibration(CalibrationMode.Auto, SelectedAutoCalibration?.FilePath);
    }

    /// <summary>Назначает активную калибровку для режима StereoOnly.</summary>
    [RelayCommand]
    private void SetActiveStereoOnlyCalibration()
    {
        SetActiveCalibration(CalibrationMode.StereoOnly, SelectedStereoOnlyCalibration?.FilePath);
    }

    /// <summary>Назначает активную калибровку для режима SingleCamera.</summary>
    [RelayCommand]
    private void SetActiveSingleCameraCalibration()
    {
        SetActiveCalibration(CalibrationMode.SingleCamera, SelectedSingleCameraCalibration?.FilePath);
    }

    /// <summary>Назначает активную сессию для режима Auto.</summary>
    [RelayCommand]
    private void SetActiveAutoSession()
    {
        SetActiveSession(CalibrationMode.Auto, SelectedAutoSession?.SessionDirectory);
    }

    /// <summary>Назначает активную сессию для режима StereoOnly.</summary>
    [RelayCommand]
    private void SetActiveStereoOnlySession()
    {
        SetActiveSession(CalibrationMode.StereoOnly, SelectedStereoOnlySession?.SessionDirectory);
    }

    /// <summary>Назначает активную сессию для режима SingleCamera.</summary>
    [RelayCommand]
    private void SetActiveSingleCameraSession()
    {
        SetActiveSession(CalibrationMode.SingleCamera, SelectedSingleCameraSession?.SessionDirectory);
    }

    /// <summary>Удаляет выбранную сессию режима Auto.</summary>
    [RelayCommand]
    private async Task DeleteSelectedAutoSessionAsync()
    {
        await DeleteSelectedSessionAsync(CalibrationMode.Auto, SelectedAutoSession);
    }

    /// <summary>Удаляет выбранную сессию режима StereoOnly.</summary>
    [RelayCommand]
    private async Task DeleteSelectedStereoOnlySessionAsync()
    {
        await DeleteSelectedSessionAsync(CalibrationMode.StereoOnly, SelectedStereoOnlySession);
    }

    /// <summary>Удаляет выбранную сессию режима SingleCamera.</summary>
    [RelayCommand]
    private async Task DeleteSelectedSingleCameraSessionAsync()
    {
        await DeleteSelectedSessionAsync(CalibrationMode.SingleCamera, SelectedSingleCameraSession);
    }

    /// <summary>Загружает детали выбранной калибровки.</summary>
    [RelayCommand]
    private async Task LoadSelectedCalibrationAsync()
    {
        if (SelectedCalibration == null) return;
        try
        {
            StatusMessage = L("Loading calibration...", "Загрузка калибровки...");
            LoadedCalibration?.Dispose();
            LoadedCalibration = await Task.Run(() =>
                CalibrationResult.LoadFromXml(SelectedCalibration.FilePath));
            StatusMessage = string.Format(
                L("Loaded: {0} (Error: {1:F3} px)", "Загружено: {0} (Ошибка: {1:F3} px)"),
                SelectedCalibration.FileName, LoadedCalibration.ReprojectionError);
        }
        catch (Exception ex)
        {
            StatusMessage = string.Format(
                L("Failed to load: {0}", "Не удалось загрузить: {0}"), ex.Message);
        }
    }

    /// <summary>Удаляет выбранную калибровку (XML + .txt метаданные).</summary>
    [RelayCommand]
    private async Task DeleteSelectedCalibrationAsync()
    {
        if (SelectedCalibration == null)
        {
            StatusMessage = L("Select a calibration to delete.", "Выберите калибровку для удаления.");
            return;
        }

        if (RequestConfirmation != null)
        {
            var confirmed = await RequestConfirmation(
                L("Delete Calibration", "Удалить калибровку"),
                string.Format(
                    L("Are you sure you want to delete '{0}'?", "Вы уверены, что хотите удалить '{0}'?"),
                    SelectedCalibration.FileName),
                L("Delete", "Удалить"),
                L("Cancel", "Отмена"));
            if (!confirmed) return;
        }

        try
        {
            var filePath = SelectedCalibration.FilePath;
            var fileName = SelectedCalibration.FileName;
            await Task.Run(() =>
            {
                File.Delete(filePath);
                var metadataPath = Path.ChangeExtension(filePath, ".txt");
                if (!string.IsNullOrWhiteSpace(metadataPath) && File.Exists(metadataPath))
                    File.Delete(metadataPath);
            });

            ClearDeletedCalibrationFromActiveSelections(filePath);

            if (LoadedCalibration != null)
            {
                LoadedCalibration.Dispose();
                LoadedCalibration = null;
            }

            Refresh();
            StatusMessage = string.Format(L("Deleted: {0}", "Удалено: {0}"), fileName);
        }
        catch (Exception ex)
        {
            StatusMessage = string.Format(
                L("Failed to delete: {0}", "Не удалось удалить: {0}"), ex.Message);
        }
    }

    /// <summary>Назначает активную калибровку для текущего режима.</summary>
    [RelayCommand]
    private void SetActiveCalibrationForCurrentMode()
    {
        SetActiveCalibration(SelectedMode, SelectedCalibration?.FilePath);
    }

    /// <summary>Назначает активную сессию для текущего режима.</summary>
    [RelayCommand]
    private void SetActiveSessionForCurrentMode()
    {
        SetActiveSession(SelectedMode, SelectedSession?.SessionDirectory);
    }

    /// <summary>Удаляет выбранную сессию для текущего режима.</summary>
    [RelayCommand]
    private async Task DeleteSelectedSessionForCurrentModeAsync()
    {
        await DeleteSelectedSessionAsync(SelectedMode, SelectedSession);
    }

    /// <summary>Открывает папку калибровок в проводнике.</summary>
    [RelayCommand]
    private void OpenCalibrationsFolder()
    {
        OpenDirectory(ResolveCalibrationPath());
    }

    /// <summary>Открывает папку сессий в проводнике.</summary>
    [RelayCommand]
    private void OpenSessionsFolder()
    {
        OpenDirectory(ResolveSessionsPath());
    }

    /// <summary>
    /// Назначает активную калибровку для указанного режима.
    /// </summary>
    /// <param name="mode">Режим калибровки.</param>
    /// <param name="filePath">Путь к файлу калибровки.</param>
    private void SetActiveCalibration(CalibrationMode mode, string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            StatusMessage = L(
                "Select a valid calibration file first.",
                "Сначала выберите корректный файл калибровки.");
            return;
        }

        switch (mode)
        {
            case CalibrationMode.Auto:
                _settingsService.Settings.ActiveAutoCalibrationPath = filePath;
                // Для карты глубины используем ту же активную калибровку, что выбрана для Auto.
                _settingsService.Settings.ActiveDepthMapCalibrationPath = filePath;
                break;
            case CalibrationMode.StereoOnly:
                _settingsService.Settings.ActiveStereoOnlyCalibrationPath = filePath;
                // Для StereoOnly также синхронизируем активную depth-калибровку.
                _settingsService.Settings.ActiveDepthMapCalibrationPath = filePath;
                break;
            case CalibrationMode.SingleCamera:
                _settingsService.Settings.ActiveSingleCameraCalibrationPath = filePath;
                break;
        }

        _settingsService.Save();
        UpdateActiveSelectionsText();
        StatusMessage = $"{L("Active calibration updated for", "Активная калибровка обновлена для")} {mode}.";
    }

    /// <summary>
    /// Назначает активную сессию для указанного режима.
    /// </summary>
    /// <param name="mode">Режим калибровки.</param>
    /// <param name="sessionDirectory">Путь к папке сессии.</param>
    private void SetActiveSession(CalibrationMode mode, string? sessionDirectory)
    {
        if (string.IsNullOrWhiteSpace(sessionDirectory) || !Directory.Exists(sessionDirectory))
        {
            StatusMessage = L("Select a valid session first.", "Сначала выберите корректную сессию.");
            return;
        }

        switch (mode)
        {
            case CalibrationMode.Auto:
                _settingsService.Settings.ActiveAutoSessionPath = sessionDirectory;
                break;
            case CalibrationMode.StereoOnly:
                _settingsService.Settings.ActiveStereoOnlySessionPath = sessionDirectory;
                break;
            case CalibrationMode.SingleCamera:
                _settingsService.Settings.ActiveSingleCameraSessionPath = sessionDirectory;
                break;
        }

        _settingsService.Save();
        UpdateActiveSelectionsText();
        StatusMessage = $"{L("Active session updated for", "Активная сессия обновлена для")} {mode}.";
    }

    /// <summary>
    /// Удаляет выбранную сессию с диска и обновляет списки.
    /// </summary>
    /// <param name="mode">Режим калибровки.</param>
    /// <param name="session">Элемент сессии для удаления.</param>
    private async Task DeleteSelectedSessionAsync(CalibrationMode mode, CaptureSessionLibraryItem? session)
    {
        if (session == null)
        {
            StatusMessage = L("Select a session to delete.", "Выберите сессию для удаления.");
            return;
        }

        if (RequestConfirmation != null)
        {
            var confirmed = await RequestConfirmation(
                L("Delete Session", "Удалить сессию"),
                string.Format(
                    L("Are you sure you want to delete session '{0}'?", "Вы уверены, что хотите удалить сессию '{0}'?"),
                    session.SessionId),
                L("Delete", "Удалить"),
                L("Cancel", "Отмена"));
            if (!confirmed) return;
        }

        try
        {
            if (Directory.Exists(session.SessionDirectory))
            {
                Directory.Delete(session.SessionDirectory, recursive: true);
            }

            var currentActive = GetActiveSessionPath(mode);
            if (!string.IsNullOrWhiteSpace(currentActive)
                && string.Equals(
                    Path.GetFullPath(currentActive),
                    Path.GetFullPath(session.SessionDirectory),
                    StringComparison.OrdinalIgnoreCase))
            {
                SetActiveSessionPath(mode, string.Empty);
                _settingsService.Save();
            }

            Refresh();
            StatusMessage = $"{L("Deleted session", "Удалена сессия")}: {session.SessionId}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"{L("Failed to delete session", "Не удалось удалить сессию")}: {ex.Message}";
        }
    }

    /// <summary>
    /// Загружает список XML-калибровок из папки и распределяет по режимам.
    /// </summary>
    private void LoadCalibrations()
    {
        var calibrationPath = ResolveCalibrationPath();
        Directory.CreateDirectory(calibrationPath);

        var allItems = Directory
            .GetFiles(calibrationPath, "*.xml")
            .Select(path => BuildCalibrationItem(path))
            .Where(item => item != null)
            .Cast<CalibrationLibraryItem>()
            .OrderByDescending(item => item.CreatedAt)
            .ToList();

        AutoCalibrations = allItems.Where(item => item.Mode == CalibrationMode.Auto).ToList();
        StereoOnlyCalibrations = allItems.Where(item => item.Mode == CalibrationMode.StereoOnly).ToList();
        SingleCameraCalibrations = allItems.Where(item => item.Mode == CalibrationMode.SingleCamera).ToList();

        MarkActiveCalibrations(AutoCalibrations, _settingsService.Settings.ActiveAutoCalibrationPath);
        MarkActiveCalibrations(StereoOnlyCalibrations, _settingsService.Settings.ActiveStereoOnlyCalibrationPath);
        MarkActiveCalibrations(SingleCameraCalibrations, _settingsService.Settings.ActiveSingleCameraCalibrationPath);
    }

    /// <summary>
    /// Загружает список сессий захвата из папки и распределяет по режимам.
    /// </summary>
    private void LoadSessions()
    {
        var sessionsPath = ResolveSessionsPath();
        Directory.CreateDirectory(sessionsPath);

        var sessionItems = new List<CaptureSessionLibraryItem>();

        foreach (var sessionDirectory in Directory.GetDirectories(sessionsPath))
        {
            var manifestPath = Path.Combine(sessionDirectory, "session.json");
            if (!File.Exists(manifestPath))
            {
                continue;
            }

            try
            {
                var json = File.ReadAllText(manifestPath);
                var manifest = JsonSerializer.Deserialize<CaptureSessionManifest>(json, _jsonOptions);
                if (manifest == null)
                {
                    continue;
                }

                sessionItems.Add(new CaptureSessionLibraryItem
                {
                    SessionDirectory = sessionDirectory,
                    SessionId = string.IsNullOrWhiteSpace(manifest.SessionId)
                        ? Path.GetFileName(sessionDirectory)
                        : manifest.SessionId,
                    Mode = manifest.CaptureMode,
                    CreatedAtLocal = manifest.CreatedAtLocal == default
                        ? Directory.GetCreationTime(sessionDirectory)
                        : manifest.CreatedAtLocal,
                    CaptureCount = manifest.Captures.Count,
                    RequiredFrames = manifest.RequiredFrames,
                    IsCompleted = manifest.IsCompleted
                });
            }
            catch
            {
                // Пропускаем повреждённые сессии, сохраняя просмотр корректных.
            }
        }

        sessionItems = sessionItems
            .OrderByDescending(item => item.CreatedAtLocal)
            .ToList();

        AutoSessions = sessionItems.Where(item => item.Mode == CalibrationMode.Auto).ToList();
        StereoOnlySessions = sessionItems.Where(item => item.Mode == CalibrationMode.StereoOnly).ToList();
        SingleCameraSessions = sessionItems.Where(item => item.Mode == CalibrationMode.SingleCamera).ToList();

        MarkActiveSessions(AutoSessions, _settingsService.Settings.ActiveAutoSessionPath);
        MarkActiveSessions(StereoOnlySessions, _settingsService.Settings.ActiveStereoOnlySessionPath);
        MarkActiveSessions(SingleCameraSessions, _settingsService.Settings.ActiveSingleCameraSessionPath);
    }

    /// <summary>
    /// Создаёт элемент библиотеки калибровок на основе XML-файла.
    /// </summary>
    /// <param name="filePath">Путь к XML-файлу калибровки.</param>
    /// <returns>Элемент библиотеки или null, если файл не распознан.</returns>
    private CalibrationLibraryItem? BuildCalibrationItem(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return null;
        }

        var mode = ResolveModeFromCalibrationFile(filePath);
        if (mode == null)
        {
            return null;
        }

        var fileInfo = new FileInfo(filePath);
        return new CalibrationLibraryItem
        {
            FilePath = filePath,
            FileName = fileInfo.Name,
            Mode = mode.Value,
            CreatedAt = fileInfo.CreationTime,
            FileSizeBytes = fileInfo.Length
        };
    }

    /// <summary>
    /// Определяет режим калибровки по префиксу имени файла.
    /// </summary>
    /// <param name="filePath">Путь к файлу калибровки.</param>
    /// <returns>Режим калибровки или null, если префикс не распознан.</returns>
    private static CalibrationMode? ResolveModeFromCalibrationFile(string filePath)
    {
        var fileName = Path.GetFileNameWithoutExtension(filePath).ToLowerInvariant();
        if (fileName.StartsWith("stereoauto_"))
        {
            return CalibrationMode.Auto;
        }

        if (fileName.StartsWith("stereoonly_"))
        {
            return CalibrationMode.StereoOnly;
        }

        if (fileName.StartsWith("singlecamera_"))
        {
            return CalibrationMode.SingleCamera;
        }

        return null;
    }

    /// <summary>
    /// Обновляет тексты активных калибровок/сессий для каждого режима.
    /// </summary>
    private void UpdateActiveSelectionsText()
    {
        var settings = _settingsService.Settings;

        ActiveAutoCalibrationText = BuildActivePathText(
            settings.ActiveAutoCalibrationPath,
            L("Auto active XML", "Активный XML Auto"));
        ActiveStereoOnlyCalibrationText = BuildActivePathText(
            settings.ActiveStereoOnlyCalibrationPath,
            L("StereoOnly active XML", "Активный XML StereoOnly"));
        ActiveSingleCameraCalibrationText = BuildActivePathText(
            settings.ActiveSingleCameraCalibrationPath,
            L("SingleCamera active XML", "Активный XML SingleCamera"));

        ActiveAutoSessionText = BuildActivePathText(
            settings.ActiveAutoSessionPath,
            L("Auto active session", "Активная сессия Auto"));
        ActiveStereoOnlySessionText = BuildActivePathText(
            settings.ActiveStereoOnlySessionPath,
            L("StereoOnly active session", "Активная сессия StereoOnly"));
        ActiveSingleCameraSessionText = BuildActivePathText(
            settings.ActiveSingleCameraSessionPath,
            L("SingleCamera active session", "Активная сессия SingleCamera"));

        AutoActiveInfoText = BuildActiveInfoLine(
            settings.ActiveAutoCalibrationPath,
            settings.ActiveAutoSessionPath);
        StereoOnlyActiveInfoText = BuildActiveInfoLine(
            settings.ActiveStereoOnlyCalibrationPath,
            settings.ActiveStereoOnlySessionPath);
        SingleCameraActiveInfoText = BuildActiveInfoLine(
            settings.ActiveSingleCameraCalibrationPath,
            settings.ActiveSingleCameraSessionPath);
    }

    /// <summary>
    /// Формирует текст для отображения активного пути («не выбрано» / «отсутствует» / имя файла).
    /// </summary>
    private string BuildActivePathText(string path, string label)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return $"{label}: {L("not selected", "не выбрано")}";
        }

        var exists = File.Exists(path) || Directory.Exists(path);
        var display = File.Exists(path)
            ? Path.GetFileName(path)
            : Path.GetFileName(Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(display))
        {
            display = path;
        }

        return exists
            ? $"{label}: {display}"
            : $"{label}: {display} ({L("missing", "отсутствует")})";
    }

    /// <summary>
    /// Формирует сводную строку с информацией об активном XML и сессии.
    /// </summary>
    private string BuildActiveInfoLine(string calibrationPath, string sessionPath)
    {
        var xmlDisplay = string.IsNullOrWhiteSpace(calibrationPath)
            ? L("not selected", "не выбрано")
            : Path.GetFileName(calibrationPath);
        var sessionDisplay = string.IsNullOrWhiteSpace(sessionPath)
            ? L("not selected", "не выбрано")
            : Path.GetFileName(Path.GetFullPath(sessionPath).TrimEnd(Path.DirectorySeparatorChar));

        if (string.IsNullOrWhiteSpace(xmlDisplay)) xmlDisplay = calibrationPath ?? "";
        if (string.IsNullOrWhiteSpace(sessionDisplay)) sessionDisplay = sessionPath ?? "";

        return $"XML: {xmlDisplay}  |  {L("Session", "Сессия")}: {sessionDisplay}";
    }

    /// <summary>Помечает активную калибровку в списке.</summary>
    private static void MarkActiveCalibrations(List<CalibrationLibraryItem> items, string? activePath)
    {
        if (string.IsNullOrWhiteSpace(activePath)) return;
        var fullActivePath = Path.GetFullPath(activePath);
        foreach (var item in items)
        {
            if (string.Equals(Path.GetFullPath(item.FilePath), fullActivePath, StringComparison.OrdinalIgnoreCase))
                item.IsActive = true;
        }
    }

    /// <summary>Помечает активную сессию в списке.</summary>
    private static void MarkActiveSessions(List<CaptureSessionLibraryItem> items, string? activePath)
    {
        if (string.IsNullOrWhiteSpace(activePath)) return;
        var fullActivePath = Path.GetFullPath(activePath);
        foreach (var item in items)
        {
            if (string.Equals(Path.GetFullPath(item.SessionDirectory), fullActivePath, StringComparison.OrdinalIgnoreCase))
                item.IsActive = true;
        }
    }

    /// <summary>Разрешает путь к папке калибровок. Относительный путь приводится к абсолютному относительно BaseDirectory.</summary>
    private string ResolveCalibrationPath()
    {
        var configured = _settingsService.Settings.CalibrationDataPath;
        return CalibrationHelpers.ResolvePath(configured, "./CalibrationData");
    }

    /// <summary>Разрешает путь к папке сессий захвата.</summary>
    private string ResolveSessionsPath()
    {
        var configured = _settingsService.Settings.CaptureSessionsPath;
        return CaptureSessionService.ResolveRootPath(configured);
    }

    /// <summary>Возвращает путь к активной сессии для режима.</summary>
    private string GetActiveSessionPath(CalibrationMode mode) => mode switch
    {
        CalibrationMode.Auto => _settingsService.Settings.ActiveAutoSessionPath,
        CalibrationMode.StereoOnly => _settingsService.Settings.ActiveStereoOnlySessionPath,
        CalibrationMode.SingleCamera => _settingsService.Settings.ActiveSingleCameraSessionPath,
        _ => string.Empty
    };

    /// <summary>Устанавливает путь к активной сессии для режима в настройках.</summary>
    private void SetActiveSessionPath(CalibrationMode mode, string value)
    {
        switch (mode)
        {
            case CalibrationMode.Auto:
                _settingsService.Settings.ActiveAutoSessionPath = value;
                break;
            case CalibrationMode.StereoOnly:
                _settingsService.Settings.ActiveStereoOnlySessionPath = value;
                break;
            case CalibrationMode.SingleCamera:
                _settingsService.Settings.ActiveSingleCameraSessionPath = value;
                break;
        }
    }

    /// <summary>Очищает активные пути калибровки, если удалённый файл совпадает.</summary>
    private void ClearDeletedCalibrationFromActiveSelections(string deletedFilePath)
    {
        string deletedFullPath;
        try { deletedFullPath = Path.GetFullPath(deletedFilePath); }
        catch { return; }

        var settings = _settingsService.Settings;
        var changed = false;

        if (PathEquals(settings.ActiveAutoCalibrationPath, deletedFullPath))
        { settings.ActiveAutoCalibrationPath = string.Empty; changed = true; }
        if (PathEquals(settings.ActiveStereoOnlyCalibrationPath, deletedFullPath))
        { settings.ActiveStereoOnlyCalibrationPath = string.Empty; changed = true; }
        if (PathEquals(settings.ActiveSingleCameraCalibrationPath, deletedFullPath))
        { settings.ActiveSingleCameraCalibrationPath = string.Empty; changed = true; }
        if (PathEquals(settings.ActiveDepthMapCalibrationPath, deletedFullPath))
        { settings.ActiveDepthMapCalibrationPath = string.Empty; changed = true; }

        if (changed) _settingsService.Save();
    }

    /// <summary>Сравнивает пути файлов с нормализацией.</summary>
    private static bool PathEquals(string? configuredPath, string targetFullPath)
    {
        if (string.IsNullOrWhiteSpace(configuredPath)) return false;
        try
        {
            return string.Equals(Path.GetFullPath(configuredPath), targetFullPath, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    /// <summary>Подключает FileWatcher к указанной папке.</summary>
    private void AttachWatcher(string path)
    {
        _calibrationFolderPath = path;
        _fileWatcher = new FileWatcherService(path);
        _fileWatcher.OnFilesChanged += OnWatchedFilesChanged;
    }

    /// <summary>Отключает и освобождает FileWatcher.</summary>
    private void DetachWatcher()
    {
        if (_fileWatcher == null) return;
        _fileWatcher.OnFilesChanged -= OnWatchedFilesChanged;
        _fileWatcher.Dispose();
        _fileWatcher = null;
    }

    /// <summary>Обработчик изменений в отслеживаемой папке.</summary>
    private void OnWatchedFilesChanged()
    {
        RunOnUiThread(Refresh);
    }

    /// <summary>Открывает директорию в проводнике ОС.</summary>
    private void OpenDirectory(string directoryPath)
    {
        try
        {
            Directory.CreateDirectory(directoryPath);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = directoryPath,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            StatusMessage = $"{L("Failed to open folder", "Не удалось открыть папку")}: {ex.Message}";
        }
    }

    /// <summary>Обновляет все локализованные тексты UI.</summary>
    private void UpdateLocalizedUiTexts()
    {
        OpenCalibrationsFolderText = L("Open Calibrations Folder", "Открыть папку калибровок");
        OpenSessionsFolderText = L("Open Sessions Folder", "Открыть папку сессий");

        AutoModeTitleText = L("Auto Mode", "Режим Auto");
        StereoOnlyModeTitleText = L("StereoOnly Mode", "Режим StereoOnly");
        SingleCameraModeTitleText = L("SingleCamera Mode", "Режим SingleCamera");

        CalibrationsColumnTitleText = L("Calibrations (XML)", "Калибровки (XML)");
        SessionsColumnTitleText = L("Capture Sessions", "Сессии снимков");

        SetActiveCalibrationButtonText = L("Set Active Calibration", "Сделать калибровку активной");
        SetActiveSessionButtonText = L("Set Active Session", "Сделать сессию активной");
        DeleteSessionButtonText = L("Delete Session", "Удалить сессию");
        RefreshButtonText = L("Refresh", "Обновить");
        NoDataText = L("No calibrations or sessions", "Нет калибровок и сессий");

        ReprojectionErrorLabelText = L("Reprojection Error", "Ошибка репроекции");
        QualityLabelText = L("Quality", "Качество");
        ImagePairsLabelText = L("Image Pairs", "Пары изображений");
        ResolutionLabelText = L("Resolution", "Разрешение");
        DeleteCalibrationButtonText = L("Delete Calibration", "Удалить калибровку");
        LoadCalibrationButtonText = L("Load Details", "Загрузить детали");
    }

    /// <summary>Обработчик изменения настроек: обновляет локализацию и списки при смене настроек.</summary>
    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(AppSettings.Language):
                UpdateLocalizedUiTexts();
                UpdateActiveSelectionsText();
                break;

            case nameof(AppSettings.CalibrationDataPath):
                DetachWatcher();
                AttachWatcher(ResolveCalibrationPath());
                Refresh();
                break;

            case nameof(AppSettings.CaptureSessionsPath):
            case nameof(AppSettings.ActiveAutoCalibrationPath):
            case nameof(AppSettings.ActiveStereoOnlyCalibrationPath):
            case nameof(AppSettings.ActiveSingleCameraCalibrationPath):
            case nameof(AppSettings.ActiveDepthMapCalibrationPath):
            case nameof(AppSettings.ActiveAutoSessionPath):
            case nameof(AppSettings.ActiveStereoOnlySessionPath):
            case nameof(AppSettings.ActiveSingleCameraSessionPath):
                Refresh();
                break;
        }
    }

    /// <summary>Освобождает ресурсы и отписывается от событий.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _settingsService.Settings.PropertyChanged -= OnSettingsChanged;
        DetachWatcher();
        LoadedCalibration?.Dispose();
    }
}
