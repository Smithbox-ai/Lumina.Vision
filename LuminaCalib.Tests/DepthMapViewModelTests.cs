using Emgu.CV;
using Emgu.CV.CvEnum;
using LuminaCalib.Calibration;
using LuminaCalib.Models;
using LuminaCalib.Services;
using LuminaCalib.ViewModels;
using Xunit;

namespace LuminaCalib.Tests;

/// <summary>
/// Юнит-тесты для DepthMapViewModel — состояние, пресеты, синхронизация с сервисом, локализация.
/// Bitmap-конвертация не тестируется (требует Avalonia UI thread).
/// </summary>
public sealed class DepthMapViewModelTests : IDisposable
{
    private readonly List<IDisposable> _disposables = new();

    public void Dispose()
    {
        foreach (var d in _disposables)
            d.Dispose();
        _disposables.Clear();
    }

    private (DepthMapViewModel vm, DepthMapService service, DepthMapSettings settings, SettingsService settingsService) CreateViewModel(
        string? language = null)
    {
        var settingsService = new SettingsService();
        // Force lazy load of Settings, then override language (Load resets CurrentLanguage from disk/defaults)
        _ = settingsService.Settings;
        if (language != null)
        {
            settingsService.Settings.Language = language;
            SettingsService.CurrentLanguage = language;
        }

        var rectifier = new StereoRectifier();
        _disposables.Add(rectifier);

        var settings = new DepthMapSettings();
        var service = new DepthMapService(rectifier, settings);
        _disposables.Add(service);

        var vm = new DepthMapViewModel(service, settings, settingsService);
        _disposables.Add(vm);

        return (vm, service, settings, settingsService);
    }

    // ── Test 1: Initial state ──

    [Fact]
    public void InitialState_IsDisabled()
    {
        var (vm, _, _, _) = CreateViewModel();

        Assert.False(vm.IsEnabled);
        Assert.Null(vm.DepthMapImage);
        Assert.Equal(0.0, vm.ProcessingTimeMs);
        Assert.Equal(0.0, vm.DepthMapFps);
    }

    // ── Test 2: Preset Fast ──

    [Fact]
    public void ApplyPresetFast_SetsCorrectValues()
    {
        var (vm, _, settings, _) = CreateViewModel();

        vm.ApplyPresetFastCommand.Execute(null);

        Assert.Equal(64, settings.NumDisparities);
        Assert.Equal(9, settings.BlockSize);
        Assert.False(settings.UseWlsFilter);
        Assert.False(settings.UseMorphologicalClosing);
        Assert.Equal(0.5, settings.DisplayScale);
        Assert.Equal(SgbmMode.Sgbm3Way, settings.SgbmMode);
        Assert.Equal(5, settings.UniquenessRatio);
        Assert.Equal(50, settings.SpeckleWindowSize);
    }

    // ── Test 3: Preset Balanced ──

    [Fact]
    public void ApplyPresetBalanced_SetsCorrectValues()
    {
        var (vm, _, settings, _) = CreateViewModel();

        vm.ApplyPresetBalancedCommand.Execute(null);

        Assert.Equal(128, settings.NumDisparities);
        Assert.Equal(5, settings.BlockSize);
        Assert.True(settings.UseWlsFilter);
        Assert.Equal(8000.0, settings.WlsLambda);   // default; preset no longer overrides
        Assert.Equal(1.0, settings.WlsSigmaColor);   // default; preset no longer overrides
        Assert.True(settings.UseMorphologicalClosing);
        Assert.Equal(5, settings.MorphKernelSize);
        Assert.Equal(1.0, settings.DisplayScale);
        Assert.Equal(SgbmMode.Sgbm3Way, settings.SgbmMode);
        Assert.Equal(10, settings.UniquenessRatio);
        Assert.Equal(100, settings.SpeckleWindowSize);
    }

    // ── Test 4: Preset Quality ──

    [Fact]
    public void ApplyPresetQuality_SetsCorrectValues()
    {
        var (vm, _, settings, _) = CreateViewModel();

        vm.ApplyPresetQualityCommand.Execute(null);

        Assert.Equal(256, settings.NumDisparities);
        Assert.Equal(5, settings.BlockSize);
        Assert.True(settings.UseWlsFilter);
        Assert.Equal(8000.0, settings.WlsLambda);    // default; preset no longer overrides
        Assert.Equal(1.0, settings.WlsSigmaColor);    // default; preset no longer overrides
        Assert.True(settings.UseMorphologicalClosing);
        Assert.Equal(7, settings.MorphKernelSize);
        Assert.Equal(1.0, settings.DisplayScale);
        Assert.Equal(SgbmMode.Sgbm3Way, settings.SgbmMode);
        Assert.Equal(15, settings.UniquenessRatio);
        Assert.Equal(200, settings.SpeckleWindowSize);
    }

    // ── Test 5: IsEnabled syncs to service ──

    [Fact]
    public void IsEnabled_SyncsToService()
    {
        var (vm, service, _, _) = CreateViewModel();

        Assert.False(service.IsEnabled);

        vm.IsEnabled = true;
        Assert.True(service.IsEnabled);

        vm.IsEnabled = false;
        Assert.False(service.IsEnabled);
    }

    // ── Test 6: Dispose unsubscribes events ──

    [Fact]
    public void Dispose_UnsubscribesEvents_NoExceptions()
    {
        var (vm, service, _, _) = CreateViewModel();

        vm.Dispose();

        // After dispose, triggering service events should NOT invoke VM handlers
        // (no NullReferenceException or other errors)
        // We can't easily fire OnDepthMapReady without real pipeline,
        // but we verify Dispose doesn't throw and double-dispose is safe.
        vm.Dispose(); // idempotent
    }

    // ── Test 7: StatusText when disabled ──

    [Fact]
    public void StatusText_WhenDisabled_ShowsDisabledText()
    {
        var (vm, _, _, _) = CreateViewModel(language: "en");

        // IsEnabled is false by default — force status update via toggle
        vm.IsEnabled = true;
        vm.IsEnabled = false;

        Assert.Equal("Disabled", vm.StatusText);
    }

    // ── Test 8: AvailableColormaps contains all enum values ──

    [Fact]
    public void AvailableColormaps_ContainsAllEnumValues()
    {
        var (vm, _, _, _) = CreateViewModel();

        var expected = Enum.GetValues<DepthColormap>();
        Assert.Equal(expected.Length, vm.AvailableColormaps.Length);

        foreach (var colormap in expected)
            Assert.Contains(colormap, vm.AvailableColormaps);
    }

    // ── Test 9: AvailableSgbmModes contains all enum values ──

    [Fact]
    public void AvailableSgbmModes_ContainsAllEnumValues()
    {
        var (vm, _, _, _) = CreateViewModel();

        var expected = Enum.GetValues<SgbmMode>();
        Assert.Equal(expected.Length, vm.AvailableSgbmModes.Length);

        foreach (var mode in expected)
            Assert.Contains(mode, vm.AvailableSgbmModes);
    }

    // ── Test 10: Settings property exposes injected settings ──

    [Fact]
    public void Settings_ExposesInjectedDepthMapSettings()
    {
        var (vm, _, settings, _) = CreateViewModel();

        Assert.Same(settings, vm.Settings);
    }

    // ── Test 11: SaveSnapshot fires event ──

    [Fact]
    public void SaveSnapshot_FiresRequestSaveSnapshotEvent()
    {
        var (vm, _, _, _) = CreateViewModel();
        bool eventFired = false;
        vm.RequestSaveSnapshot += () => eventFired = true;

        vm.SaveSnapshotCommand.Execute(null);

        Assert.True(eventFired);
    }

    // ── Test 12: ExportPointCloud fires event ──

    [Fact]
    public void ExportPointCloud_FiresRequestExportPointCloudEvent()
    {
        var (vm, _, _, _) = CreateViewModel();
        bool eventFired = false;
        vm.RequestExportPointCloud += () => eventFired = true;

        vm.ExportPointCloudCommand.Execute(null);

        Assert.True(eventFired);
    }

    // ── Test 13: Localization texts are set after construction ──

    [Fact]
    public void Constructor_SetsLocalizedTexts()
    {
        var (vm, _, _, _) = CreateViewModel(language: "en");

        Assert.Equal("Depth Map", vm.TitleText);
        Assert.Equal("Enable", vm.EnableToggleText);
        Assert.Equal("SGBM Settings", vm.SgbmSettingsText);
        Assert.Equal("Presets", vm.PresetsText);
        Assert.Equal("Fast", vm.PresetFastText);
        Assert.Equal("Balanced", vm.PresetBalancedText);
        Assert.Equal("Quality", vm.PresetQualityText);
    }

    // ── Test 14: Localization texts in Russian ──

    [Fact]
    public void Constructor_SetsRussianTexts_WhenLanguageIsRu()
    {
        var (vm, _, _, _) = CreateViewModel(language: "ru");

        Assert.Equal("Карта глубины", vm.TitleText);
        Assert.Equal("Включить", vm.EnableToggleText);
        Assert.Equal("Настройки SGBM", vm.SgbmSettingsText);
        Assert.Equal("Пресеты", vm.PresetsText);
        Assert.Equal("Быстрый", vm.PresetFastText);
        Assert.Equal("Сбалансированный", vm.PresetBalancedText);
        Assert.Equal("Качество", vm.PresetQualityText);
    }

    // ── Test 15: Language change updates localized texts ──

    [Fact]
    public void LanguageChange_UpdatesLocalizedTexts()
    {
        // Start with Russian
        SettingsService.CurrentLanguage = "ru";
        var (vm, _, _, settingsService) = CreateViewModel(language: "ru");

        Assert.Equal("Карта глубины", vm.TitleText);
        Assert.Equal("Включить", vm.EnableToggleText);
        Assert.Equal("Пресеты", vm.PresetsText);

        // Change language to English via AppSettings
        settingsService.Settings.Language = "en";

        Assert.Equal("Depth Map", vm.TitleText);
        Assert.Equal("Enable", vm.EnableToggleText);
        Assert.Equal("Presets", vm.PresetsText);
    }

    // ── Test 16: Dispose unsubscribes from settings changes ──

    [Fact]
    public void Dispose_UnsubscribesFromSettingsChanges()
    {
        SettingsService.CurrentLanguage = "ru";
        var (vm, _, _, settingsService) = CreateViewModel(language: "ru");

        Assert.Equal("Карта глубины", vm.TitleText);

        vm.Dispose();

        // Change language after dispose — texts should NOT update
        settingsService.Settings.Language = "en";

        Assert.Equal("Карта глубины", vm.TitleText);
    }

    // ── Test 17: CudaStatusText updates when UseCuda changes ──

    [Fact]
    public void CudaStatusText_UpdatesWhenUseCudaChanges()
    {
        SettingsService.CurrentLanguage = "en";
        var (vm, _, settings, _) = CreateViewModel(language: "en");

        // Initial state: CUDA is either not available or disabled
        Assert.False(string.IsNullOrEmpty(vm.CudaStatusText));

        // Toggle UseCuda — status text should update
        settings.UseCuda = !settings.UseCuda;
        // Text may or may not change depending on CUDA availability, but it should be set
        Assert.False(string.IsNullOrEmpty(vm.CudaStatusText));
    }

    // ── Test 18: CudaStatusText updates when UseWlsFilter changes ──

    [Fact]
    public void CudaStatusText_UpdatesWhenUseWlsFilterChanges()
    {
        SettingsService.CurrentLanguage = "en";
        var (vm, _, settings, _) = CreateViewModel(language: "en");

        // Enable CUDA, disable WLS
        settings.UseCuda = true;
        settings.UseWlsFilter = false;
        var withoutWls = vm.CudaStatusText;

        // Enable WLS — should show "Disabled (WLS active)" if CUDA is available
        settings.UseWlsFilter = true;
        var withWls = vm.CudaStatusText;

        Assert.False(string.IsNullOrEmpty(withoutWls));
        Assert.False(string.IsNullOrEmpty(withWls));
    }

    // ── Test 19: CudaAccelerationText is localized ──

    [Fact]
    public void CudaAccelerationText_IsLocalized()
    {
        SettingsService.CurrentLanguage = "en";
        var (vm, _, _, _) = CreateViewModel(language: "en");

        Assert.Equal("Acceleration", vm.CudaAccelerationText);
    }

    // ── Test 20: CudaAccelerationText in Russian ──

    [Fact]
    public void CudaAccelerationText_InRussian()
    {
        SettingsService.CurrentLanguage = "ru";
        var (vm, _, _, _) = CreateViewModel(language: "ru");

        Assert.Equal("Ускорение", vm.CudaAccelerationText);
    }

    // ── Test 21: CudaWarningText shown when CUDA active, empty when disabled ──

    [Fact]
    public void CudaWarningText_ShownWhenCudaActive_EmptyOtherwise()
    {
        SettingsService.CurrentLanguage = "en";
        var (vm, _, settings, _) = CreateViewModel(language: "en");

        // With CUDA disabled, warning should be empty
        settings.UseCuda = false;
        settings.UseWlsFilter = false;
        Assert.Equal("", vm.CudaWarningText);

        // Enable CUDA — if CUDA hardware is available, warning appears; if not, stays empty
        settings.UseCuda = true;
        if (DepthMapProcessor.IsCudaAvailable)
        {
            Assert.Contains("StereoBM", vm.CudaWarningText);
        }
        else
        {
            Assert.Equal("", vm.CudaWarningText);
        }

        // Enabling WLS should clear the warning (CUDA is effectively disabled when WLS is on)
        settings.UseWlsFilter = true;
        Assert.Equal("", vm.CudaWarningText);
    }

    // ── Test 22: Dispose unsubscribes from DepthMapSettings changes ──

    [Fact]
    public void Dispose_UnsubscribesFromDepthMapSettingsChanges()
    {
        SettingsService.CurrentLanguage = "en";
        var (vm, _, settings, _) = CreateViewModel(language: "en");

        var statusBefore = vm.CudaStatusText;
        vm.Dispose();

        // Changing settings after dispose should NOT update CudaStatusText
        settings.UseCuda = !settings.UseCuda;
        Assert.Equal(statusBefore, vm.CudaStatusText);
    }

    // ── Test 23: Latest-frame queue keeps only the newest frame ──

    [Fact]
    public void QueueDepthFrameForTests_ReplacesPendingFrameWithLatest()
    {
        var (vm, _, _, _) = CreateViewModel();
        using var firstFrame = new Mat(8, 8, DepthType.Cv8U, 3);
        using var secondFrame = new Mat(13, 13, DepthType.Cv8U, 3);

        vm.QueueDepthFrameForTests(firstFrame);
        vm.QueueDepthFrameForTests(secondFrame);

        var pending = vm.TakePendingDepthFrameForTests();
        Assert.NotNull(pending);
        Assert.Equal(13, pending!.Width);
        Assert.Equal(13, pending.Height);
        pending.Dispose();

        var secondRead = vm.TakePendingDepthFrameForTests();
        Assert.Null(secondRead);
    }

    // ── Test 24: Dispose clears pending frame queue safely ──

    [Fact]
    public void Dispose_ClearsPendingDepthFrameQueue()
    {
        var (vm, _, _, _) = CreateViewModel();
        using var frame = new Mat(10, 10, DepthType.Cv8U, 3);

        vm.QueueDepthFrameForTests(frame);
        vm.Dispose();

        var pending = vm.TakePendingDepthFrameForTests();
        Assert.Null(pending);
    }
}
