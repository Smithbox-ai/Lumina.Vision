using LuminaCalib.Services;
using LuminaCalib.Models;
using Xunit;

namespace LuminaCalib.Tests;

public sealed class SettingsServiceMigrationTests
{
    [Fact]
    public void Load_OldSettingsWithoutDepthCalibrationPath_UsesDefaultAndPersists()
    {
        var root = Path.Combine(Path.GetTempPath(), $"LuminaCalib_SettingsMigration_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        var settingsPath = Path.Combine(root, "settings.json");
        var legacyJson = """
        {
          "language": "en",
          "activeAutoCalibrationPath": "auto.xml",
          "activeStereoOnlyCalibrationPath": "stereo.xml"
        }
        """;

        try
        {
            File.WriteAllText(settingsPath, legacyJson);

            var settingsService = new SettingsService(settingsPath);
            Assert.Equal(string.Empty, settingsService.Settings.ActiveDepthMapCalibrationPath);

            settingsService.Settings.ActiveDepthMapCalibrationPath = "depth.xml";
            settingsService.Save();

            var persistedJson = File.ReadAllText(settingsPath);
            Assert.Contains("activeDepthMapCalibrationPath", persistedJson, StringComparison.Ordinal);

            var reloaded = new SettingsService(settingsPath);
            Assert.Equal("depth.xml", reloaded.Settings.ActiveDepthMapCalibrationPath);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void SaveLoad_BoardPatternSettings_RoundTrips()
    {
        var root = Path.Combine(Path.GetTempPath(), $"LuminaCalib_SettingsRoundTrip_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var settingsPath = Path.Combine(root, "settings.json");

        try
        {
            var settingsService = new SettingsService(settingsPath);
            settingsService.Settings.BoardType = BoardType.ChArUco;
            settingsService.Settings.PatternWidth = 11;
            settingsService.Settings.PatternHeight = 8;
            settingsService.Save();

            var reloaded = new SettingsService(settingsPath);
            Assert.Equal(BoardType.ChArUco, reloaded.Settings.BoardType);
            Assert.Equal(11, reloaded.Settings.PatternWidth);
            Assert.Equal(8, reloaded.Settings.PatternHeight);
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
}
