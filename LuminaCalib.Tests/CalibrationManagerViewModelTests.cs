using System.Text.Json;
using System.Text.Json.Serialization;
using LuminaCalib.Models;
using LuminaCalib.Services;
using LuminaCalib.ViewModels;
using Xunit;

namespace LuminaCalib.Tests;

public sealed class CalibrationManagerViewModelTests
{
    [Fact]
    public void Localization_UpdatesLabels_WhenLanguageChanges()
    {
        var root = Path.Combine(Path.GetTempPath(), $"LuminaCalib_Manager_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        var settingsPath = Path.Combine(root, "settings.json");
        try
        {
            var settingsService = new SettingsService(settingsPath);
            settingsService.Settings.Language = "ru";

            using var vm = new CalibrationManagerViewModel(settingsService);
            Assert.NotEqual("Open Calibrations Folder", vm.OpenCalibrationsFolderText);
            Assert.NotEqual("Delete Session", vm.DeleteSessionButtonText);

            settingsService.Settings.Language = "en";
            Assert.Equal("Open Calibrations Folder", vm.OpenCalibrationsFolderText);
            Assert.Equal("Delete Session", vm.DeleteSessionButtonText);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Refresh_GroupsSessionsByCalibrationMode()
    {
        var root = Path.Combine(Path.GetTempPath(), $"LuminaCalib_Manager_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        var settingsPath = Path.Combine(root, "settings.json");
        var calibrationPath = Path.Combine(root, "calibrations");
        var sessionsPath = Path.Combine(root, "sessions");
        Directory.CreateDirectory(calibrationPath);
        Directory.CreateDirectory(sessionsPath);

        WriteSession(sessionsPath, "auto_session", CalibrationMode.Auto);
        WriteSession(sessionsPath, "stereo_session", CalibrationMode.StereoOnly);
        WriteSession(sessionsPath, "single_session", CalibrationMode.SingleCamera);

        try
        {
            var settingsService = new SettingsService(settingsPath);
            settingsService.Settings.CalibrationDataPath = calibrationPath;
            settingsService.Settings.CaptureSessionsPath = sessionsPath;

            using var vm = new CalibrationManagerViewModel(settingsService);

            Assert.Single(vm.AutoSessions);
            Assert.Single(vm.StereoOnlySessions);
            Assert.Single(vm.SingleCameraSessions);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void SetActiveCalibration_UpdatesSettingsForMode()
    {
        var root = Path.Combine(Path.GetTempPath(), $"LuminaCalib_Manager_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        var settingsPath = Path.Combine(root, "settings.json");
        var calibrationPath = Path.Combine(root, "calibrations");
        var sessionsPath = Path.Combine(root, "sessions");
        Directory.CreateDirectory(calibrationPath);
        Directory.CreateDirectory(sessionsPath);

        var calibrationFile = Path.Combine(calibrationPath, "StereoAuto_2026-02-08_10-10-10.xml");
        File.WriteAllText(calibrationFile, "<opencv_storage/>");

        try
        {
            var settingsService = new SettingsService(settingsPath);
            settingsService.Settings.CalibrationDataPath = calibrationPath;
            settingsService.Settings.CaptureSessionsPath = sessionsPath;

            using var vm = new CalibrationManagerViewModel(settingsService);
            vm.SelectedAutoCalibration = vm.AutoCalibrations.FirstOrDefault();

            vm.SetActiveAutoCalibrationCommand.Execute(null);

            Assert.Equal(calibrationFile, settingsService.Settings.ActiveAutoCalibrationPath);
            Assert.Equal(calibrationFile, settingsService.Settings.ActiveDepthMapCalibrationPath);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void SetActiveStereoOnlyCalibration_AlsoUpdatesDepthMapCalibrationPath()
    {
        var root = Path.Combine(Path.GetTempPath(), $"LuminaCalib_Manager_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        var settingsPath = Path.Combine(root, "settings.json");
        var calibrationPath = Path.Combine(root, "calibrations");
        var sessionsPath = Path.Combine(root, "sessions");
        Directory.CreateDirectory(calibrationPath);
        Directory.CreateDirectory(sessionsPath);

        var calibrationFile = Path.Combine(calibrationPath, "StereoOnly_2026-02-08_10-10-10.xml");
        File.WriteAllText(calibrationFile, "<opencv_storage/>");

        try
        {
            var settingsService = new SettingsService(settingsPath);
            settingsService.Settings.CalibrationDataPath = calibrationPath;
            settingsService.Settings.CaptureSessionsPath = sessionsPath;

            using var vm = new CalibrationManagerViewModel(settingsService);
            vm.SelectedStereoOnlyCalibration = vm.StereoOnlyCalibrations.FirstOrDefault();

            vm.SetActiveStereoOnlyCalibrationCommand.Execute(null);

            Assert.Equal(calibrationFile, settingsService.Settings.ActiveStereoOnlyCalibrationPath);
            Assert.Equal(calibrationFile, settingsService.Settings.ActiveDepthMapCalibrationPath);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task DeleteSelectedSession_RemovesDirectoryAndClearsActive()
    {
        var root = Path.Combine(Path.GetTempPath(), $"LuminaCalib_Manager_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        var settingsPath = Path.Combine(root, "settings.json");
        var calibrationPath = Path.Combine(root, "calibrations");
        var sessionsPath = Path.Combine(root, "sessions");
        Directory.CreateDirectory(calibrationPath);
        Directory.CreateDirectory(sessionsPath);

        var sessionDirectory = Path.Combine(sessionsPath, "auto_session");
        Directory.CreateDirectory(sessionDirectory);
        Directory.CreateDirectory(Path.Combine(sessionDirectory, "frames"));

        var manifest = new CaptureSessionManifest
        {
            SessionId = "auto_session",
            CaptureMode = CalibrationMode.Auto,
            CreatedAtLocal = DateTime.Now,
            CreatedAtUtc = DateTime.UtcNow,
            PatternWidth = 9,
            PatternHeight = 6,
            SquareSizeMm = 25,
            MarkerSizeRatio = 0.73f
        };

        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter() }
        };
        File.WriteAllText(
            Path.Combine(sessionDirectory, "session.json"),
            JsonSerializer.Serialize(manifest, jsonOptions));

        try
        {
            var settingsService = new SettingsService(settingsPath);
            settingsService.Settings.CalibrationDataPath = calibrationPath;
            settingsService.Settings.CaptureSessionsPath = sessionsPath;
            settingsService.Settings.ActiveAutoSessionPath = sessionDirectory;

            using var vm = new CalibrationManagerViewModel(settingsService);
            vm.SelectedAutoSession = vm.AutoSessions.FirstOrDefault();

            await vm.DeleteSelectedAutoSessionCommand.ExecuteAsync(null);

            Assert.False(Directory.Exists(sessionDirectory));
            Assert.Equal(string.Empty, settingsService.Settings.ActiveAutoSessionPath);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void SelectedMode_ChangesCurrentCalibrations_ToMatchingMode()
    {
        var root = Path.Combine(Path.GetTempPath(), $"LuminaCalib_Manager_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        var settingsPath = Path.Combine(root, "settings.json");
        var calibrationPath = Path.Combine(root, "calibrations");
        var sessionsPath = Path.Combine(root, "sessions");
        Directory.CreateDirectory(calibrationPath);
        Directory.CreateDirectory(sessionsPath);

        // Create calibration files for each mode
        File.WriteAllText(Path.Combine(calibrationPath, "StereoAuto_2026-01-01_00-00-00.xml"), "<opencv_storage/>");
        File.WriteAllText(Path.Combine(calibrationPath, "StereoOnly_2026-01-01_00-00-00.xml"), "<opencv_storage/>");
        File.WriteAllText(Path.Combine(calibrationPath, "SingleCamera_2026-01-01_00-00-00.xml"), "<opencv_storage/>");

        try
        {
            var settingsService = new SettingsService(settingsPath);
            settingsService.Settings.CalibrationDataPath = calibrationPath;
            settingsService.Settings.CaptureSessionsPath = sessionsPath;

            using var vm = new CalibrationManagerViewModel(settingsService);

            // Default mode is Auto
            vm.SelectedMode = CalibrationMode.Auto;
            Assert.Equal(vm.AutoCalibrations, vm.CurrentCalibrations);

            vm.SelectedMode = CalibrationMode.StereoOnly;
            Assert.Equal(vm.StereoOnlyCalibrations, vm.CurrentCalibrations);

            vm.SelectedMode = CalibrationMode.SingleCamera;
            Assert.Equal(vm.SingleCameraCalibrations, vm.CurrentCalibrations);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void SelectedMode_ChangesCurrentSessions_ToMatchingMode()
    {
        var root = Path.Combine(Path.GetTempPath(), $"LuminaCalib_Manager_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        var settingsPath = Path.Combine(root, "settings.json");
        var calibrationPath = Path.Combine(root, "calibrations");
        var sessionsPath = Path.Combine(root, "sessions");
        Directory.CreateDirectory(calibrationPath);
        Directory.CreateDirectory(sessionsPath);

        WriteSession(sessionsPath, "auto_session", CalibrationMode.Auto);
        WriteSession(sessionsPath, "stereo_session", CalibrationMode.StereoOnly);
        WriteSession(sessionsPath, "single_session", CalibrationMode.SingleCamera);

        try
        {
            var settingsService = new SettingsService(settingsPath);
            settingsService.Settings.CalibrationDataPath = calibrationPath;
            settingsService.Settings.CaptureSessionsPath = sessionsPath;

            using var vm = new CalibrationManagerViewModel(settingsService);

            vm.SelectedMode = CalibrationMode.Auto;
            Assert.Equal(vm.AutoSessions, vm.CurrentSessions);
            Assert.Single(vm.CurrentSessions);

            vm.SelectedMode = CalibrationMode.StereoOnly;
            Assert.Equal(vm.StereoOnlySessions, vm.CurrentSessions);
            Assert.Single(vm.CurrentSessions);

            vm.SelectedMode = CalibrationMode.SingleCamera;
            Assert.Equal(vm.SingleCameraSessions, vm.CurrentSessions);
            Assert.Single(vm.CurrentSessions);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task DeleteSelectedCalibration_RemovesFileAndRefreshes()
    {
        var root = Path.Combine(Path.GetTempPath(), $"LuminaCalib_Manager_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        var settingsPath = Path.Combine(root, "settings.json");
        var calibrationPath = Path.Combine(root, "calibrations");
        var sessionsPath = Path.Combine(root, "sessions");
        Directory.CreateDirectory(calibrationPath);
        Directory.CreateDirectory(sessionsPath);

        var calibrationFile = Path.Combine(calibrationPath, "StereoAuto_2026-02-08_10-10-10.xml");
        File.WriteAllText(calibrationFile, "<opencv_storage/>");
        // Also create companion .txt
        File.WriteAllText(Path.ChangeExtension(calibrationFile, ".txt"), "metadata");

        try
        {
            var settingsService = new SettingsService(settingsPath);
            settingsService.Settings.CalibrationDataPath = calibrationPath;
            settingsService.Settings.CaptureSessionsPath = sessionsPath;

            using var vm = new CalibrationManagerViewModel(settingsService);
            Assert.Single(vm.AutoCalibrations);

            // Set unified selection
            vm.SelectedCalibration = vm.AutoCalibrations.First();

            // No confirmation delegate set → deletes immediately
            await vm.DeleteSelectedCalibrationCommand.ExecuteAsync(null);

            Assert.False(File.Exists(calibrationFile));
            Assert.False(File.Exists(Path.ChangeExtension(calibrationFile, ".txt")));
            Assert.Empty(vm.AutoCalibrations);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task DeleteSelectedCalibration_ClearsActivePathIfDeleted()
    {
        var root = Path.Combine(Path.GetTempPath(), $"LuminaCalib_Manager_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        var settingsPath = Path.Combine(root, "settings.json");
        var calibrationPath = Path.Combine(root, "calibrations");
        var sessionsPath = Path.Combine(root, "sessions");
        Directory.CreateDirectory(calibrationPath);
        Directory.CreateDirectory(sessionsPath);

        var calibrationFile = Path.Combine(calibrationPath, "StereoAuto_2026-02-08_10-10-10.xml");
        File.WriteAllText(calibrationFile, "<opencv_storage/>");

        try
        {
            var settingsService = new SettingsService(settingsPath);
            settingsService.Settings.CalibrationDataPath = calibrationPath;
            settingsService.Settings.CaptureSessionsPath = sessionsPath;
            // Make this file the active calibration for Auto + DepthMap
            settingsService.Settings.ActiveAutoCalibrationPath = calibrationFile;
            settingsService.Settings.ActiveDepthMapCalibrationPath = calibrationFile;

            using var vm = new CalibrationManagerViewModel(settingsService);
            vm.SelectedCalibration = vm.AutoCalibrations.First();

            await vm.DeleteSelectedCalibrationCommand.ExecuteAsync(null);

            Assert.Equal(string.Empty, settingsService.Settings.ActiveAutoCalibrationPath);
            Assert.Equal(string.Empty, settingsService.Settings.ActiveDepthMapCalibrationPath);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void SelectedMode_ResetsCrossSelections()
    {
        var root = Path.Combine(Path.GetTempPath(), $"LuminaCalib_Manager_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        var settingsPath = Path.Combine(root, "settings.json");
        var calibrationPath = Path.Combine(root, "calibrations");
        var sessionsPath = Path.Combine(root, "sessions");
        Directory.CreateDirectory(calibrationPath);
        Directory.CreateDirectory(sessionsPath);

        File.WriteAllText(Path.Combine(calibrationPath, "StereoAuto_2026-01-01_00-00-00.xml"), "<opencv_storage/>");

        try
        {
            var settingsService = new SettingsService(settingsPath);
            settingsService.Settings.CalibrationDataPath = calibrationPath;
            settingsService.Settings.CaptureSessionsPath = sessionsPath;

            using var vm = new CalibrationManagerViewModel(settingsService);

            // Select a calibration in Auto mode
            vm.SelectedCalibration = vm.AutoCalibrations.FirstOrDefault();
            Assert.NotNull(vm.SelectedCalibration);

            // Switch mode → selections should be cleared
            vm.SelectedMode = CalibrationMode.StereoOnly;
            Assert.Null(vm.SelectedCalibration);
            Assert.Null(vm.SelectedSession);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void HasCurrentModeData_ReflectsCurrentMode()
    {
        var root = Path.Combine(Path.GetTempPath(), $"LuminaCalib_Manager_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        var settingsPath = Path.Combine(root, "settings.json");
        var calibrationPath = Path.Combine(root, "calibrations");
        var sessionsPath = Path.Combine(root, "sessions");
        Directory.CreateDirectory(calibrationPath);
        Directory.CreateDirectory(sessionsPath);

        // Only create an Auto calibration
        File.WriteAllText(Path.Combine(calibrationPath, "StereoAuto_2026-01-01_00-00-00.xml"), "<opencv_storage/>");

        try
        {
            var settingsService = new SettingsService(settingsPath);
            settingsService.Settings.CalibrationDataPath = calibrationPath;
            settingsService.Settings.CaptureSessionsPath = sessionsPath;

            using var vm = new CalibrationManagerViewModel(settingsService);

            vm.SelectedMode = CalibrationMode.Auto;
            Assert.True(vm.HasCurrentModeData);

            vm.SelectedMode = CalibrationMode.StereoOnly;
            Assert.False(vm.HasCurrentModeData);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void CalibrationFilesChanged_FiredOnRefresh()
    {
        var root = Path.Combine(Path.GetTempPath(), $"LuminaCalib_Manager_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        var settingsPath = Path.Combine(root, "settings.json");
        var calibrationPath = Path.Combine(root, "calibrations");
        var sessionsPath = Path.Combine(root, "sessions");
        Directory.CreateDirectory(calibrationPath);
        Directory.CreateDirectory(sessionsPath);

        try
        {
            var settingsService = new SettingsService(settingsPath);
            settingsService.Settings.CalibrationDataPath = calibrationPath;
            settingsService.Settings.CaptureSessionsPath = sessionsPath;

            using var vm = new CalibrationManagerViewModel(settingsService);

            var fired = false;
            vm.CalibrationFilesChanged += () => fired = true;

            vm.RefreshCommand.Execute(null);

            Assert.True(fired);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
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

    private static void WriteSession(string sessionsRoot, string name, CalibrationMode mode)
    {
        var sessionDirectory = Path.Combine(sessionsRoot, name);
        Directory.CreateDirectory(sessionDirectory);
        Directory.CreateDirectory(Path.Combine(sessionDirectory, "frames"));

        var manifest = new CaptureSessionManifest
        {
            SessionId = name,
            CaptureMode = mode,
            CreatedAtLocal = DateTime.Now,
            CreatedAtUtc = DateTime.UtcNow,
            PatternWidth = 9,
            PatternHeight = 6,
            SquareSizeMm = 25,
            MarkerSizeRatio = 0.73f
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
    }
}
