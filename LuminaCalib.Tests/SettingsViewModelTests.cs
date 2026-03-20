using LuminaCalib.Models;
using LuminaCalib.Services;
using LuminaCalib.ViewModels;
using Xunit;

namespace LuminaCalib.Tests;

/// <summary>
/// Unit tests for SettingsViewModel to validate settings management,
/// data binding, and command execution.
/// </summary>
public class SettingsViewModelTests
{
    // Helper to create temp settings path for test isolation
    private static string CreateTempSettingsPath()
    {
        var tempFile = Path.GetTempFileName();
        File.Delete(tempFile); // Delete it so test starts fresh
        return tempFile;
    }

    /// <summary>
    /// Validates that Language binding can be set and retrieved correctly.
    /// </summary>
    [Fact]
    public void SettingsViewModel_LanguageBinding_Roundtrips()
    {
        // Arrange
        var settingsPath = CreateTempSettingsPath();
        var settingsService = new SettingsService(settingsPath);
        var viewModel = new SettingsViewModel(settingsService);

        // Assert - Verify AvailableLanguages is exposed
        Assert.NotNull(viewModel.AvailableLanguages);
        Assert.Contains("ru", viewModel.AvailableLanguages);
        Assert.Contains("en", viewModel.AvailableLanguages);

        // Act - Change language to English
        viewModel.Settings.Language = "en";

        // Assert - Verify change
        Assert.Equal("en", viewModel.Settings.Language);

        // Act - Change language to Russian
        viewModel.Settings.Language = "ru";

        // Assert - Verify change
        Assert.Equal("ru", viewModel.Settings.Language);
    }

    /// <summary>
    /// Validates that advanced camera settings enum properties are exposed and bindable.
    /// </summary>
    [Fact]
    public void SettingsViewModel_AdvancedSettings_Bind()
    {
        // Arrange
        var settingsPath = CreateTempSettingsPath();
        var settingsService = new SettingsService(settingsPath);
        var viewModel = new SettingsViewModel(settingsService);

        // Assert - Verify AvailableTransportProtocols
        Assert.NotNull(viewModel.AvailableTransportProtocols);
        Assert.Contains(TransportProtocol.Auto, viewModel.AvailableTransportProtocols);
        Assert.Contains(TransportProtocol.UDP, viewModel.AvailableTransportProtocols);
        Assert.Contains(TransportProtocol.TCP, viewModel.AvailableTransportProtocols);

        // Assert - Verify AvailableBackends
        Assert.NotNull(viewModel.AvailableBackends);
        Assert.Contains(CameraBackend.Auto, viewModel.AvailableBackends);
        Assert.Contains(CameraBackend.FFmpeg, viewModel.AvailableBackends);
        Assert.Contains(CameraBackend.GStreamer, viewModel.AvailableBackends);

        // Assert - Verify AvailableAccelerations
        Assert.NotNull(viewModel.AvailableAccelerations);
        Assert.Contains(HwAcceleration.Auto, viewModel.AvailableAccelerations);
        Assert.Contains(HwAcceleration.None, viewModel.AvailableAccelerations);
        Assert.Contains(HwAcceleration.NVDEC, viewModel.AvailableAccelerations);

        // Act - Change TransportProtocol
        viewModel.Settings.TransportProtocol = TransportProtocol.UDP;
        Assert.Equal(TransportProtocol.UDP, viewModel.Settings.TransportProtocol);

        // Act - Change CameraBackend
        viewModel.Settings.CameraBackend = CameraBackend.FFmpeg;
        Assert.Equal(CameraBackend.FFmpeg, viewModel.Settings.CameraBackend);

        // Act - Change HwAcceleration
        viewModel.Settings.HwAcceleration = HwAcceleration.NVDEC;
        Assert.Equal(HwAcceleration.NVDEC, viewModel.Settings.HwAcceleration);
    }

    /// <summary>
    /// Validates that BoardType enum values are exposed and bindable.
    /// </summary>
    [Fact]
    public void SettingsViewModel_BoardTypeBinding_Works()
    {
        // Arrange
        var settingsPath = CreateTempSettingsPath();
        var settingsService = new SettingsService(settingsPath);
        var viewModel = new SettingsViewModel(settingsService);

        // Assert - Verify AvailableBoardTypes
        Assert.NotNull(viewModel.AvailableBoardTypes);
        Assert.Contains(BoardType.Chessboard, viewModel.AvailableBoardTypes);
        Assert.Contains(BoardType.ChArUco, viewModel.AvailableBoardTypes);

        // Act - Change BoardType
        viewModel.Settings.BoardType = BoardType.ChArUco;

        // Assert - Verify change
        Assert.Equal(BoardType.ChArUco, viewModel.Settings.BoardType);

        // Act - Change back to Chessboard
        viewModel.Settings.BoardType = BoardType.Chessboard;

        // Assert - Verify change
        Assert.Equal(BoardType.Chessboard, viewModel.Settings.BoardType);
    }

    /// <summary>
    /// Validates that BrowseCalibrationPath command raises the RequestFolderPicker event.
    /// </summary>
    [Fact]
    public void SettingsViewModel_BrowseCommand_RaisesEvent()
    {
        // Arrange
        var settingsPath = CreateTempSettingsPath();
        var settingsService = new SettingsService(settingsPath);
        var viewModel = new SettingsViewModel(settingsService);
        var eventRaised = false;

        viewModel.RequestFolderPicker += () => eventRaised = true;

        // Act
        viewModel.BrowseCalibrationPathCommand.Execute(null);

        // Assert
        Assert.True(eventRaised, "RequestFolderPicker event should be raised");
    }

    /// <summary>
    /// Validates that BrowseCaptureSessionsPath command raises the RequestCaptureSessionsFolderPicker event.
    /// </summary>
    [Fact]
    public void SettingsViewModel_BrowseCaptureSessionsCommand_RaisesEvent()
    {
        // Arrange
        var settingsPath = CreateTempSettingsPath();
        var settingsService = new SettingsService(settingsPath);
        var viewModel = new SettingsViewModel(settingsService);
        var eventRaised = false;

        viewModel.RequestCaptureSessionsFolderPicker += () => eventRaised = true;

        // Act
        viewModel.BrowseCaptureSessionsPathCommand.Execute(null);

        // Assert
        Assert.True(eventRaised, "RequestCaptureSessionsFolderPicker event should be raised");
    }

    /// <summary>
    /// Validates that OpenBoardGenerator command raises the RequestNavigateToBoardGenerator event.
    /// </summary>
    [Fact]
    public void SettingsViewModel_OpenBoardGenerator_RaisesEvent()
    {
        // Arrange
        var settingsPath = CreateTempSettingsPath();
        var settingsService = new SettingsService(settingsPath);
        var viewModel = new SettingsViewModel(settingsService);
        var eventRaised = false;

        viewModel.RequestNavigateToBoardGenerator += () => eventRaised = true;

        // Act
        viewModel.OpenBoardGeneratorCommand.Execute(null);

        // Assert
        Assert.True(eventRaised, "RequestNavigateToBoardGenerator event should be raised");
    }

    /// <summary>
    /// Validates that localized UI labels update when language changes.
    /// </summary>
    [Fact]
    public void SettingsViewModel_Localization_UpdatesWhenLanguageChanges()
    {
        var settingsPath = CreateTempSettingsPath();
        var settingsService = new SettingsService(settingsPath);
        var viewModel = new SettingsViewModel(settingsService);

        settingsService.Settings.Language = "ru";
        Assert.NotEqual("Save", viewModel.SaveButtonText);
        Assert.NotEqual("General", viewModel.GeneralTabText);

        settingsService.Settings.Language = "en";
        Assert.Equal("Save", viewModel.SaveButtonText);
        Assert.Equal("General", viewModel.GeneralTabText);
        Assert.Equal("Detection Interval (ms):", viewModel.DetectionIntervalLabelText);
    }

    [Fact]
    public void SettingsViewModel_DetectionIntervalLocalization_UsesRussianLabel()
    {
        var settingsPath = CreateTempSettingsPath();
        var settingsService = new SettingsService(settingsPath);
        var viewModel = new SettingsViewModel(settingsService);

        settingsService.Settings.Language = "ru";
        Assert.Contains("Интервал", viewModel.DetectionIntervalLabelText, StringComparison.Ordinal);
    }

    [Fact]
    public void SettingsViewModel_DetectionIntervalValidation_BlocksNegativeValues()
    {
        var settingsPath = CreateTempSettingsPath();
        var settingsService = new SettingsService(settingsPath);
        var viewModel = new SettingsViewModel(settingsService);

        viewModel.Settings.LeftCameraUrl = "rtsp://192.168.1.100/stream";
        viewModel.Settings.RightCameraUrl = "rtsp://192.168.1.101/stream";
        viewModel.Settings.SquareSize = 25;
        viewModel.Settings.PatternWidth = 9;
        viewModel.Settings.PatternHeight = 6;
        viewModel.Settings.SyncToleranceMs = 50;
        viewModel.Settings.DetectionIntervalMs = 50;
        Assert.True(viewModel.CanSave);

        viewModel.Settings.DetectionIntervalMs = -1;
        Assert.False(viewModel.CanSave);
        Assert.NotNull(viewModel.DetectionIntervalError);
    }

    /// <summary>
    /// Validates that TestLeftCamera command updates status message during test.
    /// </summary>
    [Fact]
    public async Task SettingsViewModel_TestLeftCamera_UpdatesStatusMessage()
    {
        // Arrange
        var settingsPath = CreateTempSettingsPath();
        var settingsService = new SettingsService(settingsPath);
        var viewModel = new SettingsViewModel(settingsService);
        
        // Set a dummy camera URL to avoid null/empty URL
        viewModel.Settings.Language = "en";
        viewModel.Settings.LeftCameraUrl = "rtsp://dummy.test/stream";

        // Act
        await viewModel.TestLeftCameraCommand.ExecuteAsync(null);
        
        // Assert - Command executed successfully if StatusMessage was updated
        // Accept any status outcome: "Success", "Failed", or "timeout"
        Assert.False(string.IsNullOrEmpty(viewModel.StatusMessage), 
            "StatusMessage should be updated after test execution");
        Assert.True(
            viewModel.StatusMessage.Contains("Success", StringComparison.OrdinalIgnoreCase) ||
            viewModel.StatusMessage.Contains("Failed", StringComparison.OrdinalIgnoreCase) ||
            viewModel.StatusMessage.Contains("timeout", StringComparison.OrdinalIgnoreCase),
            $"Expected status message to contain 'Success', 'Failed', or 'timeout', but got: {viewModel.StatusMessage}");
    }

    /// <summary>
    /// Validates that TestRightCamera command updates status message during test.
    /// </summary>
    [Fact]
    public async Task SettingsViewModel_TestRightCamera_UpdatesStatusMessage()
    {
        // Arrange
        var settingsPath = CreateTempSettingsPath();
        var settingsService = new SettingsService(settingsPath);
        var viewModel = new SettingsViewModel(settingsService);
        
        // Set a dummy camera URL to avoid null/empty URL
        viewModel.Settings.Language = "en";
        viewModel.Settings.RightCameraUrl = "rtsp://dummy.test/stream";

        // Act
        await viewModel.TestRightCameraCommand.ExecuteAsync(null);
        
        // Assert - Command executed successfully if StatusMessage was updated
        // Accept any status outcome: "Success", "Failed", or "timeout"
        Assert.False(string.IsNullOrEmpty(viewModel.StatusMessage), 
            "StatusMessage should be updated after test execution");
        Assert.True(
            viewModel.StatusMessage.Contains("Success", StringComparison.OrdinalIgnoreCase) ||
            viewModel.StatusMessage.Contains("Failed", StringComparison.OrdinalIgnoreCase) ||
            viewModel.StatusMessage.Contains("timeout", StringComparison.OrdinalIgnoreCase),
            $"Expected status message to contain 'Success', 'Failed', or 'timeout', but got: {viewModel.StatusMessage}");
    }

    /// <summary>
    /// Validates that Save command persists settings to temp file, not %AppData%.
    /// </summary>
    [Fact]
    public async Task SettingsViewModel_SaveCommand_PersistsSettings()
    {
        // Arrange
        var settingsPath = CreateTempSettingsPath();
        var settingsService = new SettingsService(settingsPath);
        var viewModel = new SettingsViewModel(settingsService);
        
        // Modify a setting
        var originalLanguage = viewModel.Settings.Language;
        viewModel.Settings.Language = originalLanguage == "en" ? "ru" : "en";

        // Act
        await viewModel.SaveCommand.ExecuteAsync(null);

        // Assert - Settings file should exist at temp path, not %AppData%
        Assert.True(File.Exists(settingsPath), $"Settings should be saved to temp file: {settingsPath}");
        Assert.NotEqual(originalLanguage, viewModel.Settings.Language);
        
        // Verify the temp file contains the modified language setting
        var content = await File.ReadAllTextAsync(settingsPath);
        Assert.Contains(viewModel.Settings.Language, content);
        
        // Cleanup
        try { File.Delete(settingsPath); } 
        catch (IOException) { /* temp file cleanup can fail */ }
        catch (UnauthorizedAccessException) { /* ignore permission errors */ }
    }

    /// <summary>
    /// Validates that Save command requests closing settings after successful save.
    /// </summary>
    [Fact]
    public async Task SettingsViewModel_SaveCommand_RaisesRequestClose()
    {
        var settingsPath = CreateTempSettingsPath();
        var settingsService = new SettingsService(settingsPath);
        var viewModel = new SettingsViewModel(settingsService);

        viewModel.Settings.Language = "en";
        var closeRequested = false;
        viewModel.RequestClose += () => closeRequested = true;

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.True(closeRequested, "Save should request closing settings dialog.");
        Assert.Contains("saved", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Validates that Apply command saves settings but keeps dialog open.
    /// </summary>
    [Fact]
    public async Task SettingsViewModel_ApplyCommand_DoesNotRaiseRequestClose()
    {
        var settingsPath = CreateTempSettingsPath();
        var settingsService = new SettingsService(settingsPath);
        var viewModel = new SettingsViewModel(settingsService);

        viewModel.Settings.Language = "en";
        var closeRequested = false;
        viewModel.RequestClose += () => closeRequested = true;

        await viewModel.ApplyCommand.ExecuteAsync(null);

        Assert.False(closeRequested, "Apply should not request closing settings dialog.");
        Assert.Contains("applied", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Validates that Reset command restores defaults.
    /// </summary>
    [Fact]
    public void SettingsViewModel_ResetCommand_RestoresDefaults()
    {
        // Arrange
        var settingsPath = CreateTempSettingsPath();
        var settingsService = new SettingsService(settingsPath);
        var viewModel = new SettingsViewModel(settingsService);
        
        // Modify settings
        viewModel.Settings.Language = "test";
        viewModel.Settings.PatternWidth = 999;

        // Act
        viewModel.ResetCommand.Execute(null);

        // Assert - Settings should be reset to defaults
        Assert.Equal("ru", viewModel.Settings.Language); // Default language
        Assert.NotEqual(999, viewModel.Settings.PatternWidth);
    }

    // === DetectionScale Validation Tests (Phase 5) ===

    /// <summary>
    /// DetectionScale below 0.1 should produce a validation error.
    /// </summary>
    [Theory]
    [InlineData(0.0f)]
    [InlineData(0.05f)]
    [InlineData(-1.0f)]
    public void DetectionScale_BelowMinimum_ShowsError(float value)
    {
        var settingsPath = CreateTempSettingsPath();
        var settingsService = new SettingsService(settingsPath);
        var viewModel = new SettingsViewModel(settingsService);

        viewModel.Settings.DetectionScale = value;

        Assert.NotNull(viewModel.DetectionScaleError);
        Assert.False(viewModel.CanSave);
    }

    /// <summary>
    /// DetectionScale above 1.0 should produce a validation error.
    /// </summary>
    [Theory]
    [InlineData(1.1f)]
    [InlineData(2.0f)]
    public void DetectionScale_AboveMaximum_ShowsError(float value)
    {
        var settingsPath = CreateTempSettingsPath();
        var settingsService = new SettingsService(settingsPath);
        var viewModel = new SettingsViewModel(settingsService);

        viewModel.Settings.DetectionScale = value;

        Assert.NotNull(viewModel.DetectionScaleError);
        Assert.False(viewModel.CanSave);
    }

    /// <summary>
    /// DetectionScale within [0.1, 1.0] should not produce a validation error.
    /// </summary>
    [Theory]
    [InlineData(0.1f)]
    [InlineData(0.5f)]
    [InlineData(0.75f)]
    [InlineData(1.0f)]
    public void DetectionScale_InRange_NoError(float value)
    {
        var settingsPath = CreateTempSettingsPath();
        var settingsService = new SettingsService(settingsPath);
        var viewModel = new SettingsViewModel(settingsService);

        viewModel.Settings.DetectionScale = value;

        Assert.Null(viewModel.DetectionScaleError);
    }
}
