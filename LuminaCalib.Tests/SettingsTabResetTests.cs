using System.Text.Json;
using LuminaCalib.Models;
using LuminaCalib.Services;
using LuminaCalib.ViewModels;
using Xunit;

namespace LuminaCalib.Tests;

public sealed class SettingsTabResetTests : IDisposable
{
    private readonly string _settingsPath = Path.Combine(Path.GetTempPath(), $"LuminaSettingsTab_{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        if (File.Exists(_settingsPath))
            File.Delete(_settingsPath);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public Task Reset_ActiveTab_RestoresOnlyItsSettings(int tabIndex) => UiTest.Run(() =>
    {
        var service = new SettingsService(_settingsPath);
        var settings = service.Settings;
        SetCustomSettings(settings);
        var before = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!;
        var defaults = new AppSettings();
        var depthMap = settings.DepthMapSettings;
        using var vm = new SettingsViewModel(service) { SelectedTabIndex = tabIndex };

        vm.ResetCommand.Execute(null);

        Assert.Same(settings, service.Settings);
        Assert.Same(settings, vm.Settings);
        Assert.Equal(tabIndex, vm.SelectedTabIndex);
        AssertGeneralSettings(settings, tabIndex == 0 ? defaults : before);
        AssertCameraSettings(settings, tabIndex == 1 ? defaults : before);
        AssertBoardSettings(settings, tabIndex == 2 ? defaults : before);
        Assert.Equal(before.CalibrationMode, settings.CalibrationMode);
        Assert.Equal(before.StabilityThresholdSeconds, settings.StabilityThresholdSeconds);
        Assert.Equal(tabIndex == 2 ? defaults.UseRationalModel : before.UseRationalModel, settings.UseRationalModel);
        Assert.Equal(tabIndex == 2 ? defaults.RectificationAlpha : before.RectificationAlpha, settings.RectificationAlpha);
        Assert.Equal(before.UseSavedSessionForCalibration, settings.UseSavedSessionForCalibration);
        Assert.Equal(before.ActiveAutoCalibrationPath, settings.ActiveAutoCalibrationPath);
        Assert.Equal(before.ActiveStereoOnlyCalibrationPath, settings.ActiveStereoOnlyCalibrationPath);
        Assert.Equal(before.ActiveSingleCameraCalibrationPath, settings.ActiveSingleCameraCalibrationPath);
        Assert.Equal(before.ActiveDepthMapCalibrationPath, settings.ActiveDepthMapCalibrationPath);
        Assert.Equal(before.ActiveAutoSessionPath, settings.ActiveAutoSessionPath);
        Assert.Equal(before.ActiveStereoOnlySessionPath, settings.ActiveStereoOnlySessionPath);
        Assert.Equal(before.ActiveSingleCameraSessionPath, settings.ActiveSingleCameraSessionPath);
        Assert.Same(depthMap, settings.DepthMapSettings);
        Assert.False(File.Exists(_settingsPath));
    });

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    public Task Reset_UnknownTab_PreservesAllSettings(int tabIndex) => UiTest.Run(() =>
    {
        var service = new SettingsService(_settingsPath);
        SetCustomSettings(service.Settings);
        string before = JsonSerializer.Serialize(service.Settings);
        using var vm = new SettingsViewModel(service) { SelectedTabIndex = tabIndex };

        vm.ResetCommand.Execute(null);

        Assert.Equal(before, JsonSerializer.Serialize(service.Settings));
    });

    [Fact]
    public Task Reset_BoardTab_RefreshesValidationAndPreservesOtherErrors() => UiTest.Run(() =>
    {
        var service = new SettingsService(_settingsPath);
        service.Settings.SquareSize = -1;
        service.Settings.LeftCameraUrl = "invalid-url";
        using var vm = new SettingsViewModel(service) { SelectedTabIndex = 2 };
        Assert.NotNull(vm.SquareSizeError);
        Assert.NotNull(vm.LeftUrlError);

        vm.ResetCommand.Execute(null);

        Assert.Null(vm.SquareSizeError);
        Assert.NotNull(vm.LeftUrlError);
        Assert.False(vm.CanSave);
    });

    private static void SetCustomSettings(AppSettings settings)
    {
        settings.Language = "en";
        settings.AutoSaveCalibration = false;
        settings.CalibrationDataPath = "custom-data";
        settings.CaptureSessionsPath = "custom-sessions";
        settings.LeftCameraUrl = "rtsp://left.test/stream";
        settings.LeftCameraLogin = "left-user";
        settings.LeftCameraPassword = "test-left-password";
        settings.RightCameraUrl = "rtsp://right.test/stream";
        settings.RightCameraLogin = "right-user";
        settings.RightCameraPassword = "test-right-password";
        settings.TransportProtocol = TransportProtocol.TCP;
        settings.CameraBackend = CameraBackend.FFmpeg;
        settings.HwAcceleration = HwAcceleration.NVDEC;
        settings.BufferSize = 5;
        settings.CameraNoFrameTimeoutMs = 2500;
        settings.CameraReconnectInitialDelayMs = 2000;
        settings.CameraReconnectMaxDelayMs = 20000;
        settings.CameraConnectionTimeoutMs = 30000;
        settings.BoardType = BoardType.Chessboard;
        settings.PatternWidth = 9;
        settings.PatternHeight = 6;
        settings.SquareSize = 25f;
        settings.MarkerSizeRatio = 0.73f;
        settings.CharucoDictionary = CharucoDictionary.Dict6x6_250;
        settings.RequiredFrames = 30;
        settings.SyncToleranceMs = 25;
        settings.DetectionIntervalMs = 150;
        settings.DetectionScale = 1f;
        settings.CalibrationMode = CalibrationMode.SingleCamera;
        settings.StabilityThresholdSeconds = 2;
        settings.UseRationalModel = true;
        settings.RectificationAlpha = 1;
        settings.UseSavedSessionForCalibration = true;
        settings.ActiveAutoCalibrationPath = "auto-calibration";
        settings.ActiveStereoOnlyCalibrationPath = "stereo-calibration";
        settings.ActiveSingleCameraCalibrationPath = "single-calibration";
        settings.ActiveDepthMapCalibrationPath = "depth-calibration";
        settings.ActiveAutoSessionPath = "auto-session";
        settings.ActiveStereoOnlySessionPath = "stereo-session";
        settings.ActiveSingleCameraSessionPath = "single-session";
    }

    private static void AssertGeneralSettings(AppSettings actual, AppSettings expected)
    {
        Assert.Equal(expected.Language, actual.Language);
        Assert.Equal(expected.AutoSaveCalibration, actual.AutoSaveCalibration);
        Assert.Equal(expected.CalibrationDataPath, actual.CalibrationDataPath);
        Assert.Equal(expected.CaptureSessionsPath, actual.CaptureSessionsPath);
    }

    private static void AssertCameraSettings(AppSettings actual, AppSettings expected)
    {
        Assert.Equal(expected.LeftCameraUrl, actual.LeftCameraUrl);
        Assert.Equal(expected.LeftCameraLogin, actual.LeftCameraLogin);
        Assert.Equal(expected.LeftCameraPassword, actual.LeftCameraPassword);
        Assert.Equal(expected.RightCameraUrl, actual.RightCameraUrl);
        Assert.Equal(expected.RightCameraLogin, actual.RightCameraLogin);
        Assert.Equal(expected.RightCameraPassword, actual.RightCameraPassword);
        Assert.Equal(expected.TransportProtocol, actual.TransportProtocol);
        Assert.Equal(expected.CameraBackend, actual.CameraBackend);
        Assert.Equal(expected.HwAcceleration, actual.HwAcceleration);
        Assert.Equal(expected.BufferSize, actual.BufferSize);
        Assert.Equal(expected.CameraNoFrameTimeoutMs, actual.CameraNoFrameTimeoutMs);
        Assert.Equal(expected.CameraReconnectInitialDelayMs, actual.CameraReconnectInitialDelayMs);
        Assert.Equal(expected.CameraReconnectMaxDelayMs, actual.CameraReconnectMaxDelayMs);
        Assert.Equal(expected.CameraConnectionTimeoutMs, actual.CameraConnectionTimeoutMs);
    }

    private static void AssertBoardSettings(AppSettings actual, AppSettings expected)
    {
        Assert.Equal(expected.BoardType, actual.BoardType);
        Assert.Equal(expected.PatternWidth, actual.PatternWidth);
        Assert.Equal(expected.PatternHeight, actual.PatternHeight);
        Assert.Equal(expected.SquareSize, actual.SquareSize);
        Assert.Equal(expected.MarkerSizeRatio, actual.MarkerSizeRatio);
        Assert.Equal(expected.CharucoDictionary, actual.CharucoDictionary);
        Assert.Equal(expected.RequiredFrames, actual.RequiredFrames);
        Assert.Equal(expected.SyncToleranceMs, actual.SyncToleranceMs);
        Assert.Equal(expected.DetectionIntervalMs, actual.DetectionIntervalMs);
        Assert.Equal(expected.DetectionScale, actual.DetectionScale);
    }
}
