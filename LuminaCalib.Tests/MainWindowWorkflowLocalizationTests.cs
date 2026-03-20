using System.Text.Json;
using System.Text.Json.Serialization;
using LuminaCalib.Models;
using LuminaCalib.Services;
using LuminaCalib.ViewModels;
using Xunit;

namespace LuminaCalib.Tests;

public sealed class MainWindowWorkflowLocalizationTests
{
    [Fact]
    public void Localization_UpdatesNewUiTexts_WhenLanguageChanges()
    {
        var root = CreateTempRoot();
        try
        {
            var settingsPath = Path.Combine(root, "settings.json");
            var settingsService = new SettingsService(settingsPath);
            settingsService.Settings.Language = "ru";

            var vm = new MainWindowViewModel(settingsService, new StereoSynchronizer());
            try
            {
                Assert.NotEqual("Calibration Manager", vm.ManagerTabText);
                Assert.NotEqual("Use Saved Session For Calibration", vm.UseSavedSessionForCalibrationText);
                Assert.NotEqual("Show Rectified", vm.ShowRectifiedText);

                settingsService.Settings.Language = "en";
                Assert.Equal("Calibration Manager", vm.ManagerTabText);
                Assert.Equal("Use Saved Session For Calibration", vm.UseSavedSessionForCalibrationText);
                Assert.Equal("Show Rectified", vm.ShowRectifiedText);
            }
            finally
            {
                vm.Cleanup();
            }
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void SourceMode_DisablesCameraControls_WhenSavedSessionSelected()
    {
        var root = CreateTempRoot();
        try
        {
            var settingsPath = Path.Combine(root, "settings.json");
            var settingsService = new SettingsService(settingsPath);
            var vm = new MainWindowViewModel(settingsService, new StereoSynchronizer());
            try
            {
                Assert.True(vm.CanUseCameraControls);
                Assert.False(vm.UseSavedSessionForCalibration);

                vm.UseSavedSessionForCalibration = true;
                Assert.False(vm.CanUseCameraControls);
                Assert.False(vm.CanCapture);
            }
            finally
            {
                vm.Cleanup();
            }
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void ActiveSessionSummary_EnablesCalibration_WhenEnoughCaptures()
    {
        var root = CreateTempRoot();
        try
        {
            var settingsPath = Path.Combine(root, "settings.json");
            var sessionsPath = Path.Combine(root, "sessions");
            Directory.CreateDirectory(sessionsPath);

            var sessionDirectory = Path.Combine(sessionsPath, "auto_session");
            Directory.CreateDirectory(sessionDirectory);

            var manifest = new CaptureSessionManifest
            {
                SessionId = "auto_session",
                CaptureMode = CalibrationMode.Auto,
                CreatedAtLocal = DateTime.Now,
                CreatedAtUtc = DateTime.UtcNow,
                PatternWidth = 9,
                PatternHeight = 6,
                SquareSizeMm = 25,
                MarkerSizeRatio = 0.73f,
                IsCompleted = true,
                Captures = Enumerable.Range(1, 12)
                    .Select(index => new CaptureFrameEntry
                    {
                        Index = index,
                        LeftImageFile = $"L_{index}.png",
                        RightImageFile = $"R_{index}.png"
                    })
                    .ToList()
            };

            File.WriteAllText(
                Path.Combine(sessionDirectory, "session.json"),
                JsonSerializer.Serialize(
                    manifest,
                    new JsonSerializerOptions
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                        Converters = { new JsonStringEnumConverter() }
                    }));

            var settingsService = new SettingsService(settingsPath);
            settingsService.Settings.CaptureSessionsPath = sessionsPath;
            settingsService.Settings.ActiveAutoSessionPath = sessionDirectory;
            settingsService.Settings.UseSavedSessionForCalibration = true;

            var vm = new MainWindowViewModel(settingsService, new StereoSynchronizer());
            try
            {
                Assert.Equal(12, vm.CurrentModeActiveSessionCaptureCount);
                Assert.True(vm.CanCalibrate);
            }
            finally
            {
                vm.Cleanup();
            }
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"LuminaCalib_MainWindow_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // best-effort cleanup
        }
    }
}
