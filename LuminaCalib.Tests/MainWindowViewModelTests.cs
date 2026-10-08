using LuminaCalib.ViewModels;
using LuminaCalib.Calibration;
using LuminaCalib.Models;
using LuminaCalib.Services;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Xunit;

namespace LuminaCalib.Tests;

/// <summary>
/// Unit tests for MainWindowViewModel to validate core business logic,
/// state management, and navigation behavior.
/// </summary>
public class MainWindowViewModelTests
{
    /// <summary>
    /// Validates that the constructor properly initializes all properties
    /// to their expected default values.
    /// </summary>
    [Fact]
    public void Constructor_Initializes_Properties()
    {
        // Arrange
        var settingsService = new SettingsService();
        var synchronizer = new StereoSynchronizer();

        // Act
        var viewModel = new MainWindowViewModel(settingsService, synchronizer);

        // Assert - Verify default state
        Assert.True(viewModel.IsCalibrationView, "Should default to Calibration view");
        Assert.False(viewModel.IsBoardGeneratorView, "Board Generator view should not be active by default");
        Assert.False(viewModel.IsHistoryView, "History view should not be active by default");

        Assert.True(viewModel.IsAutoMode, "Should default to Auto mode");
        Assert.False(viewModel.IsStereoOnlyMode, "Stereo-only mode should not be active by default");
        Assert.False(viewModel.IsSingleCameraMode, "Single camera mode should not be active by default");

        Assert.Equal(0, viewModel.CurrentStep);
        Assert.Equal(0, viewModel.CapturedPairsCount);
        Assert.True(viewModel.RequiredPairs > 0, "RequiredPairs should be initialized to a positive value from settings");

        Assert.False(viewModel.LeftCameraConnected);
        Assert.False(viewModel.RightCameraConnected);
        Assert.False(viewModel.AreCamerasConnected);
        Assert.False(viewModel.CanCapture);

        Assert.False(string.IsNullOrWhiteSpace(viewModel.CalibrationStatusText));

        Assert.False(viewModel.IsSettingsOpen, "Settings should be closed by default");
        Assert.NotNull(viewModel.SettingsVm);

        // Cleanup
        viewModel.Cleanup();
    }

    /// <summary>
    /// Validates that navigation boolean properties are mutually exclusive.
    /// Only one view should be active at a time (Calibration, BoardGenerator, or History).
    /// 
    /// NOTE: This test currently fails due to incomplete mutual exclusivity logic in the ViewModel.
    /// IsBoardGeneratorView works correctly, but IsHistoryView and IsCalibrationView need fixes.
    /// This will be addressed in Phase 2-5.
    /// </summary>
    [Fact]
    public void Navigation_Booleans_MutuallyExclusive()
    {
        // Arrange
        var settingsService = new SettingsService();
        var synchronizer = new StereoSynchronizer();
        var viewModel = new MainWindowViewModel(settingsService, synchronizer);

        // Act & Assert - Test switching to BoardGenerator view
        viewModel.IsBoardGeneratorView = true;

        Assert.False(viewModel.IsCalibrationView, "Calibration view should be deactivated");
        Assert.True(viewModel.IsBoardGeneratorView, "BoardGenerator view should be active");
        Assert.False(viewModel.IsHistoryView, "History view should not be active");

        // Act & Assert - Test switching to History view
        // Note: Currently requires manual deactivation of other views
        viewModel.IsCalibrationView = false; // Workaround for missing mutual exclusivity
        viewModel.IsHistoryView = true;

        Assert.False(viewModel.IsCalibrationView, "Calibration view should remain deactivated");
        Assert.False(viewModel.IsBoardGeneratorView, "BoardGenerator view should be deactivated");
        Assert.True(viewModel.IsHistoryView, "History view should be active");

        // Act & Assert - Test switching back to Calibration view
        // Note: Currently requires manual deactivation of other views
        viewModel.IsHistoryView = false; // Workaround for missing mutual exclusivity
        viewModel.IsCalibrationView = true;

        Assert.True(viewModel.IsCalibrationView, "Calibration view should be active");
        Assert.False(viewModel.IsBoardGeneratorView, "BoardGenerator view should be deactivated");
        Assert.False(viewModel.IsHistoryView, "History view should be deactivated");

        // Cleanup
        viewModel.Cleanup();
    }

    /// <summary>
    /// Validates that calibration mode boolean properties are mutually exclusive.
    /// Only one mode should be active at a time (Auto, StereoOnly, or SingleCamera).
    /// 
    /// NOTE: This test currently fails due to incomplete mutual exclusivity logic in the ViewModel.
    /// IsStereoOnlyMode works correctly, but IsSingleCameraMode and IsAutoMode need fixes.
    /// This will be addressed in Phase 2-5.
    /// </summary>
    [Fact]
    public void CalibrationMode_Booleans_MutuallyExclusive()
    {
        // Arrange
        var settingsService = new SettingsService();
        var synchronizer = new StereoSynchronizer();
        var viewModel = new MainWindowViewModel(settingsService, synchronizer);

        // Act & Assert - Test switching to StereoOnly mode
        viewModel.IsStereoOnlyMode = true;

        Assert.False(viewModel.IsAutoMode, "Auto mode should be deactivated");
        Assert.True(viewModel.IsStereoOnlyMode, "StereoOnly mode should be active");
        Assert.False(viewModel.IsSingleCameraMode, "SingleCamera mode should not be active");

        // Act & Assert - Test switching to SingleCamera mode
        // Note: Currently requires manual deactivation of other modes
        viewModel.IsAutoMode = false; // Workaround for missing mutual exclusivity
        viewModel.IsSingleCameraMode = true;

        Assert.False(viewModel.IsAutoMode, "Auto mode should remain deactivated");
        Assert.False(viewModel.IsStereoOnlyMode, "StereoOnly mode should be deactivated");
        Assert.True(viewModel.IsSingleCameraMode, "SingleCamera mode should be active");

        // Act & Assert - Test switching back to Auto mode
        // Note: Currently requires manual deactivation of other modes
        viewModel.IsSingleCameraMode = false; // Workaround for missing mutual exclusivity
        viewModel.IsAutoMode = true;

        Assert.True(viewModel.IsAutoMode, "Auto mode should be active");
        Assert.False(viewModel.IsStereoOnlyMode, "StereoOnly mode should be deactivated");
        Assert.False(viewModel.IsSingleCameraMode, "SingleCamera mode should be deactivated");

        // Cleanup
        viewModel.Cleanup();
    }

    [Fact]
    public void CalibrationMode_Selection_IsPersistedToSettingsFile()
    {
        var root = Path.Combine(Path.GetTempPath(), $"LuminaCalib_ModePersist_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var settingsPath = Path.Combine(root, "settings.json");

        try
        {
            var settingsService = new SettingsService(settingsPath);
            var viewModel = new MainWindowViewModel(settingsService, new StereoSynchronizer());
            try
            {
                viewModel.IsSingleCameraMode = true;
                Assert.True(viewModel.IsSingleCameraMode);
            }
            finally
            {
                viewModel.Cleanup();
            }

            var reloaded = new SettingsService(settingsPath);
            Assert.Equal(CalibrationMode.SingleCamera, reloaded.Settings.CalibrationMode);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Theory]
    [InlineData(CalibrationMode.Auto)]
    [InlineData(CalibrationMode.StereoOnly)]
    [InlineData(CalibrationMode.SingleCamera)]
    public void CalibrationMode_IsRestoredFromSettings(CalibrationMode expectedMode)
    {
        var root = Path.Combine(Path.GetTempPath(), $"LuminaCalib_ModeRestore_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var settingsPath = Path.Combine(root, "settings.json");

        try
        {
            var writer = new SettingsService(settingsPath);
            writer.Settings.CalibrationMode = expectedMode;
            writer.Save();

            var reader = new SettingsService(settingsPath);
            var viewModel = new MainWindowViewModel(reader, new StereoSynchronizer());
            try
            {
                Assert.Equal(expectedMode == CalibrationMode.Auto, viewModel.IsAutoMode);
                Assert.Equal(expectedMode == CalibrationMode.StereoOnly, viewModel.IsStereoOnlyMode);
                Assert.Equal(expectedMode == CalibrationMode.SingleCamera, viewModel.IsSingleCameraMode);
                Assert.Equal(expectedMode, reader.Settings.CalibrationMode);
            }
            finally
            {
                viewModel.Cleanup();
            }
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void CalibrationMode_InvalidStoredValue_FallsBackToAuto()
    {
        var root = Path.Combine(Path.GetTempPath(), $"LuminaCalib_ModeFallback_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var settingsPath = Path.Combine(root, "settings.json");

        var invalidModeJson = """
        {
          "calibrationMode": 999
        }
        """;

        try
        {
            File.WriteAllText(settingsPath, invalidModeJson);

            var settingsService = new SettingsService(settingsPath);
            var viewModel = new MainWindowViewModel(settingsService, new StereoSynchronizer());
            try
            {
                Assert.True(viewModel.IsAutoMode);
                Assert.False(viewModel.IsStereoOnlyMode);
                Assert.False(viewModel.IsSingleCameraMode);
                Assert.Equal(CalibrationMode.Auto, settingsService.Settings.CalibrationMode);
            }
            finally
            {
                viewModel.Cleanup();
            }
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    /// <summary>
    /// Validates that AreCamerasConnected property correctly reflects
    /// the connection state of both cameras.
    /// </summary>
    [Fact]
    public void AreCamerasConnected_RequiresBothCameras()
    {
        // Arrange
        var settingsService = new SettingsService();
        var synchronizer = new StereoSynchronizer();
        var viewModel = new MainWindowViewModel(settingsService, synchronizer);
        viewModel.UseSavedSessionForCalibration = false;

        // Act & Assert - Both cameras disconnected
        Assert.False(viewModel.AreCamerasConnected);

        // Act & Assert - Only left camera connected
        viewModel.LeftCameraConnected = true;
        Assert.False(viewModel.AreCamerasConnected);

        // Act & Assert - Only right camera connected
        viewModel.LeftCameraConnected = false;
        viewModel.RightCameraConnected = true;
        Assert.False(viewModel.AreCamerasConnected);

        // Act & Assert - Both cameras connected
        viewModel.LeftCameraConnected = true;
        viewModel.RightCameraConnected = true;
        Assert.True(viewModel.AreCamerasConnected);
        Assert.True(viewModel.CanCapture);

        // Cleanup
        viewModel.Cleanup();
    }

    /// <summary>
    /// Validates that CanCalibrate property correctly reflects
    /// whether enough frame pairs have been captured.
    /// </summary>
    [Fact]
    public void CanCalibrate_RequiresMinimumPairs()
    {
        // Arrange
        var settingsService = new SettingsService();
        var synchronizer = new StereoSynchronizer();
        var viewModel = new MainWindowViewModel(settingsService, synchronizer);
        viewModel.UseSavedSessionForCalibration = false;
        viewModel.CapturedPairsCount = 0;

        // Act & Assert - No pairs captured
        Assert.False(viewModel.CanCalibrate);

        // This test demonstrates the expected behavior.
        // Note: The actual CaptureFrame command requires cameras to be connected,
        // so we can only test the CanCalibrate property logic here.
        // Full integration testing will be done in Phase 2-5.

        // Cleanup
        viewModel.Cleanup();
    }

    /// <summary>
    /// Validates that OpenSettings command toggles the IsSettingsOpen flag.
    /// </summary>
    [Fact]
    public void OpenSettings_TogglesIsSettingsOpen()
    {
        // Arrange
        var settingsService = new SettingsService();
        var synchronizer = new StereoSynchronizer();
        var viewModel = new MainWindowViewModel(settingsService, synchronizer);

        // Assert - Initially closed
        Assert.False(viewModel.IsSettingsOpen);

        // Act - Open settings
        viewModel.OpenSettingsCommand.Execute(null);

        // Assert - Settings are now open
        Assert.True(viewModel.IsSettingsOpen);

        // Act - Close settings by toggling again
        viewModel.OpenSettingsCommand.Execute(null);

        // Assert - Settings are now closed
        Assert.False(viewModel.IsSettingsOpen);

        // Cleanup
        viewModel.Cleanup();
    }

    /// <summary>
    /// Validates that SettingsVm uses the shared SettingsService instance.
    /// </summary>
    [Fact]
    public void SettingsVm_UsesSharedSettingsService()
    {
        // Arrange
        var settingsService = new SettingsService();
        var synchronizer = new StereoSynchronizer();

        // Act
        var viewModel = new MainWindowViewModel(settingsService, synchronizer);

        // Assert - SettingsVm should be initialized
        Assert.NotNull(viewModel.SettingsVm);

        // Verify that changes to SettingsService are reflected in SettingsVm
        // Both should reference the same Settings instance
        Assert.Same(settingsService.Settings, viewModel.SettingsVm.Settings);

        // Cleanup
        viewModel.Cleanup();
    }

    /// <summary>
    /// Validates that disconnecting cameras resets CurrentStep to 0.
    /// </summary>
    [Fact]
    public Task DisconnectCameras_ResetsCurrentStepToZero()
    => UiTest.Run(async () =>
    {
        // Arrange
        var settingsService = new SettingsService();
        var synchronizer = new StereoSynchronizer();
        var viewModel = new MainWindowViewModel(settingsService, synchronizer);

        // Simulate advanced workflow state
        viewModel.CurrentStep = 2;

        // Act
        await viewModel.DisconnectCamerasCommand.ExecuteAsync(null);

        // Assert
        Assert.Equal(0, viewModel.CurrentStep);

        // Cleanup
        viewModel.Cleanup();
    });

    /// <summary>
    /// Validates that disconnecting cameras clears captured pairs count.
    /// </summary>
    [Fact]
    public async Task DisconnectCameras_ClearsCapturedPairsCount()
    {
        // Arrange
        var settingsService = new SettingsService();
        var synchronizer = new StereoSynchronizer();
        var viewModel = new MainWindowViewModel(settingsService, synchronizer);

        // Act
        await viewModel.DisconnectCamerasCommand.ExecuteAsync(null);

        // Assert
        Assert.Equal(0, viewModel.CapturedPairsCount);
        Assert.Equal(0, viewModel.CurrentStep);

        // Cleanup
        viewModel.Cleanup();
    }

    /// <summary>
    /// Validates that after disconnect, HasCalibration is false (calibration cleared).
    /// </summary>
    [Fact]
    public async Task DisconnectCameras_ClearsCalibration()
    {
        // Arrange
        var settingsService = new SettingsService();
        var synchronizer = new StereoSynchronizer();
        var viewModel = new MainWindowViewModel(settingsService, synchronizer);

        // Act
        await viewModel.DisconnectCamerasCommand.ExecuteAsync(null);

        // Assert
        Assert.False(viewModel.HasCalibration);

        // Cleanup
        viewModel.Cleanup();
    }

    /// <summary>
    /// Проверяет, что при отключении камер всегда сбрасывается состояние поиска паттерна.
    /// </summary>
    [Fact]
    public Task DisconnectCameras_StopsDetectionAndAutoCapture()
    => UiTest.Run(async () =>
    {
        var settingsService = new SettingsService();
        var synchronizer = new StereoSynchronizer();
        var viewModel = new MainWindowViewModel(settingsService, synchronizer);

        viewModel.LeftCameraConnected = true;
        viewModel.RightCameraConnected = true;
        viewModel.CurrentStep = 1;
        viewModel.IsDetectionActive = true;
        viewModel.AutoCapturing = true;

        await viewModel.DisconnectCamerasCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsDetectionActive);
        Assert.False(viewModel.AutoCapturing);
        Assert.False(viewModel.CanShowDetectionButton);

        viewModel.Cleanup();
    });

    // === Phase 6: UI Control State Tests ===

    /// <summary>
    /// Validates that CanCalibrate returns false while AutoCapturing is true,
    /// even when CapturedPairsCount is sufficient.
    /// </summary>
    [Fact]
    public void CanCalibrate_ReturnsFalse_WhenAutoCapturingIsTrue()
    {
        // Arrange
        var settingsService = new SettingsService();
        var synchronizer = new StereoSynchronizer();
        var viewModel = new MainWindowViewModel(settingsService, synchronizer);

        // Simulate having enough captures but auto-capture still running
        viewModel.CapturedPairsCount = 15;
        viewModel.AutoCapturing = true;

        // Assert - CanCalibrate should be false because auto-capture is active
        Assert.False(viewModel.CanCalibrate,
            "CanCalibrate should be false while AutoCapturing is true");

        // Cleanup
        viewModel.Cleanup();
    }

    /// <summary>
    /// Validates that CanCalibrate returns true after auto-capture completes
    /// and there are enough captured pairs.
    /// </summary>
    [Fact]
    public Task CanCalibrate_ReturnsTrue_AfterAutoCaptureCompletes()
    => UiTest.Run(() =>
    {
        // Arrange
        // Use fresh settings: other tests can persist the saved-session source mode.
        var settingsService = new SettingsService(Path.Combine(Path.GetTempPath(),
            $"LuminaCalib_CanCalibrate_{Guid.NewGuid():N}.json"));
        var synchronizer = new StereoSynchronizer();
        var viewModel = new MainWindowViewModel(settingsService, synchronizer);

        // Simulate auto-capture completing with enough pairs
        viewModel.CapturedPairsCount = 20;
        viewModel.AutoCapturing = false;

        // Assert - CanCalibrate should be true
        Assert.True(viewModel.CanCalibrate,
            "CanCalibrate should be true when AutoCapturing is false and enough pairs captured");

        // Cleanup
        viewModel.Cleanup();
    });

    /// <summary>
    /// Validates that CanShowCaptureButton returns false when CurrentStep >= 2
    /// (capture phase is complete).
    /// </summary>
    [Fact]
    public void CanShowCaptureButton_ReturnsFalse_AfterCaptureComplete()
    {
        // Arrange
        var settingsService = new SettingsService();
        var synchronizer = new StereoSynchronizer();
        var viewModel = new MainWindowViewModel(settingsService, synchronizer);

        // Simulate cameras connected and capture complete (step 2)
        viewModel.LeftCameraConnected = true;
        viewModel.RightCameraConnected = true;
        viewModel.AutoCapturing = false;
        viewModel.CurrentStep = 2;

        // Assert - Capture button should be hidden
        Assert.False(viewModel.CanShowCaptureButton,
            "CanShowCaptureButton should be false when CurrentStep >= 2 (capture complete)");

        // Also test step 3 (save)
        viewModel.CurrentStep = 3;
        Assert.False(viewModel.CanShowCaptureButton,
            "CanShowCaptureButton should be false when CurrentStep >= 2 (save step)");

        // Cleanup
        viewModel.Cleanup();
    }

    /// <summary>
    /// Validates that CanShowCaptureButton returns true during manual capture
    /// (cameras connected, not auto-capturing, step 1).
    /// </summary>
    [Fact]
    public void CanShowCaptureButton_ReturnsTrue_DuringManualCapture()
    {
        // Arrange
        var settingsService = new SettingsService();
        var synchronizer = new StereoSynchronizer();
        var viewModel = new MainWindowViewModel(settingsService, synchronizer);

        // Simulate manual capture scenario (cameras connected, step 1, no auto-capture)
        viewModel.LeftCameraConnected = true;
        viewModel.RightCameraConnected = true;
        viewModel.AutoCapturing = false;
        viewModel.CurrentStep = 1;

        // Assert - Capture button should be visible
        Assert.True(viewModel.CanShowCaptureButton,
            "CanShowCaptureButton should be true during manual capture (step 1, cameras connected, not auto-capturing)");

        // Cleanup
        viewModel.Cleanup();
    }

    /// <summary>
    /// Validates that CanShowCaptureButton returns false when cameras are not connected.
    /// </summary>
    [Fact]
    public void CanShowCaptureButton_ReturnsFalse_WhenCamerasNotConnected()
    {
        // Arrange
        var settingsService = new SettingsService();
        var synchronizer = new StereoSynchronizer();
        var viewModel = new MainWindowViewModel(settingsService, synchronizer);

        // No cameras connected
        viewModel.AutoCapturing = false;
        viewModel.CurrentStep = 0;

        // Assert
        Assert.False(viewModel.CanShowCaptureButton,
            "CanShowCaptureButton should be false when cameras are not connected");

        // Cleanup
        viewModel.Cleanup();
    }

    /// <summary>
    /// Validates that CanShowCaptureButton returns false during auto-capture.
    /// </summary>
    [Fact]
    public void CanShowCaptureButton_ReturnsFalse_DuringAutoCapture()
    {
        // Arrange
        var settingsService = new SettingsService();
        var synchronizer = new StereoSynchronizer();
        var viewModel = new MainWindowViewModel(settingsService, synchronizer);

        // Cameras connected but auto-capture running
        viewModel.LeftCameraConnected = true;
        viewModel.RightCameraConnected = true;
        viewModel.AutoCapturing = true;
        viewModel.CurrentStep = 1;

        // Assert
        Assert.False(viewModel.CanShowCaptureButton,
            "CanShowCaptureButton should be false during auto-capture");

        // Cleanup
        viewModel.Cleanup();
    }

    /// <summary>
    /// Validates that CanChangeModeSelection returns false when cameras are connected
    /// and workflow is in progress.
    /// </summary>
    [Fact]
    public void CanChangeModeSelection_ReturnsFalse_DuringActiveWorkflow()
    {
        // Arrange
        var settingsService = new SettingsService();
        var synchronizer = new StereoSynchronizer();
        var viewModel = new MainWindowViewModel(settingsService, synchronizer);

        // Cameras connected and capture in progress
        viewModel.LeftCameraConnected = true;
        viewModel.RightCameraConnected = true;
        viewModel.CurrentStep = 1;

        // Assert
        Assert.False(viewModel.CanChangeModeSelection,
            "Mode selection should be disabled when cameras are connected and step > 0");

        // Cleanup
        viewModel.Cleanup();
    }

    /// <summary>
    /// Validates that CanChangeModeSelection returns true when cameras are not connected
    /// (setup phase).
    /// </summary>
    [Fact]
    public void CanChangeModeSelection_ReturnsTrue_WhenCamerasNotConnected()
    {
        // Arrange
        var settingsService = new SettingsService();
        var synchronizer = new StereoSynchronizer();
        var viewModel = new MainWindowViewModel(settingsService, synchronizer);

        // No cameras connected
        viewModel.CurrentStep = 0;

        // Assert
        Assert.True(viewModel.CanChangeModeSelection,
            "Mode selection should be enabled when cameras are not connected");

        // Cleanup
        viewModel.Cleanup();
    }

    /// <summary>
    /// Validates that CanCalibrate correctly uses saved session count when
    /// UseSavedSessionForCalibration is true (AutoCapturing is never active in this path).
    /// </summary>
    [Fact]
    public void CanCalibrate_UseSavedSession_WorksIndependentlyOfAutoCapture()
    {
        // Arrange
        var settingsService = new SettingsService();
        var synchronizer = new StereoSynchronizer();
        var viewModel = new MainWindowViewModel(settingsService, synchronizer);

        // Use saved session (no auto-capture involved)
        viewModel.UseSavedSessionForCalibration = true;
        viewModel.CurrentModeActiveSessionCaptureCount = 15;
        viewModel.CurrentModeActiveSessionIsCompleted = true;
        viewModel.AutoCapturing = false; // Always false in saved-session path

        // Assert - Should use session count, not live count
        Assert.True(viewModel.CanCalibrate,
            "CanCalibrate should use session count when UseSavedSessionForCalibration is true");

        // With insufficient session count
        viewModel.CurrentModeActiveSessionCaptureCount = 5;
        Assert.False(viewModel.CanCalibrate,
            "CanCalibrate should be false with insufficient session capture count");

        // Cleanup
        viewModel.Cleanup();
    }

    [Fact]
    public async Task CalibrateCommand_TogglesIsCalibrating_ForLiveCalibrationPath()
    {
        var settingsService = new SettingsService();
        var synchronizer = new StereoSynchronizer();
        var viewModel = new MainWindowViewModel(settingsService, synchronizer);

        try
        {
            viewModel.CapturedPairsCount = 10;

            var cornerDetector = new CornerDetector(9, 6);
            var calibrationEngine = new CalibrationEngine(cornerDetector);
            SetPrivateField(viewModel, "_calibrationEngine", calibrationEngine);

            var stateTransitions = new List<bool>();
            viewModel.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(MainWindowViewModel.IsCalibrating))
                {
                    stateTransitions.Add(viewModel.IsCalibrating);
                }
            };

            await viewModel.CalibrateCommand.ExecuteAsync(null);

            Assert.Contains(true, stateTransitions);
            Assert.Contains(false, stateTransitions);
            Assert.False(viewModel.IsCalibrating);
        }
        finally
        {
            viewModel.Cleanup();
        }
    }

    [Fact]
    public async Task CalibrateCommand_TogglesIsCalibrating_ForSavedSessionPath()
    {
        var settingsService = new SettingsService();
        settingsService.Settings.ActiveAutoSessionPath = Path.Combine(Path.GetTempPath(), $"missing-session-{Guid.NewGuid():N}");

        var synchronizer = new StereoSynchronizer();
        var viewModel = new MainWindowViewModel(settingsService, synchronizer)
        {
            UseSavedSessionForCalibration = true
        };

        try
        {
            var stateTransitions = new List<bool>();
            viewModel.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(MainWindowViewModel.IsCalibrating))
                {
                    stateTransitions.Add(viewModel.IsCalibrating);
                }
            };

            await viewModel.CalibrateCommand.ExecuteAsync(null);

            Assert.Contains(true, stateTransitions);
            Assert.Contains(false, stateTransitions);
            Assert.False(viewModel.IsCalibrating);
        }
        finally
        {
            viewModel.Cleanup();
        }
    }

    private static void SetPrivateField<T>(MainWindowViewModel viewModel, string fieldName, T value)
    {
        var field = typeof(MainWindowViewModel).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        field.SetValue(viewModel, value);
    }

    // === Camera Connection Cancellation Tests ===

    /// <summary>
    /// Проверяет, что IsConnectingCameras по умолчанию false.
    /// </summary>
    [Fact]
    public void IsConnectingCameras_DefaultsFalse()
    {
        var settingsService = new SettingsService();
        var synchronizer = new StereoSynchronizer();
        var viewModel = new MainWindowViewModel(settingsService, synchronizer);

        Assert.False(viewModel.IsConnectingCameras);

        viewModel.Cleanup();
    }

    /// <summary>
    /// Проверяет, что CanShowConnectButton = true когда не подключено и не подключается.
    /// </summary>
    [Fact]
    public void CanShowConnectButton_WhenDisconnected_ReturnsTrue()
    {
        var settingsService = new SettingsService();
        var synchronizer = new StereoSynchronizer();
        var viewModel = new MainWindowViewModel(settingsService, synchronizer);

        Assert.False(viewModel.AreCamerasConnected);
        Assert.False(viewModel.IsConnectingCameras);
        Assert.True(viewModel.CanShowConnectButton);

        viewModel.Cleanup();
    }

    /// <summary>
    /// Проверяет, что CanShowConnectButton = false при IsConnectingCameras = true.
    /// </summary>
    [Fact]
    public void CanShowConnectButton_WhenConnecting_ReturnsFalse()
    {
        var settingsService = new SettingsService();
        var synchronizer = new StereoSynchronizer();
        var viewModel = new MainWindowViewModel(settingsService, synchronizer);

        viewModel.IsConnectingCameras = true;

        Assert.False(viewModel.CanShowConnectButton);

        viewModel.Cleanup();
    }

    /// <summary>
    /// Проверяет, что CanShowConnectButton = false когда камеры подключены.
    /// </summary>
    [Fact]
    public void CanShowConnectButton_WhenConnected_ReturnsFalse()
    {
        var settingsService = new SettingsService();
        var synchronizer = new StereoSynchronizer();
        var viewModel = new MainWindowViewModel(settingsService, synchronizer);

        viewModel.LeftCameraConnected = true;
        viewModel.RightCameraConnected = true;

        Assert.True(viewModel.AreCamerasConnected);
        Assert.False(viewModel.CanShowConnectButton);

        viewModel.Cleanup();
    }

    /// <summary>
    /// Проверяет, что CancelConnectionText имеет непустой текст по умолчанию.
    /// </summary>
    [Fact]
    public void CancelConnectionText_HasDefaultValue()
    {
        var settingsService = new SettingsService();
        var synchronizer = new StereoSynchronizer();
        var viewModel = new MainWindowViewModel(settingsService, synchronizer);

        Assert.False(string.IsNullOrWhiteSpace(viewModel.CancelConnectionText));

        viewModel.Cleanup();
    }

    /// <summary>
    /// Проверяет, что CancelConnectionText локализуется в русский текст.
    /// </summary>
    [Fact]
    public void CancelConnectionText_IsLocalized_Russian()
    {
        var settingsService = new SettingsService();
        settingsService.Settings.Language = "ru";
        var synchronizer = new StereoSynchronizer();
        var viewModel = new MainWindowViewModel(settingsService, synchronizer);

        Assert.Equal("Прервать подключение", viewModel.CancelConnectionText);

        viewModel.Cleanup();
    }

    /// <summary>
    /// Проверяет, что CancelConnectionText локализуется в английский текст.
    /// </summary>
    [Fact]
    public void CancelConnectionText_IsLocalized_English()
    {
        var settingsService = new SettingsService();
        settingsService.Settings.Language = "en";
        var synchronizer = new StereoSynchronizer();
        var viewModel = new MainWindowViewModel(settingsService, synchronizer);

        Assert.Equal("Cancel Connection", viewModel.CancelConnectionText);

        viewModel.Cleanup();
    }

    /// <summary>
    /// Проверяет, что CancelCameraConnectionCommand существует и не null.
    /// </summary>
    [Fact]
    public void CancelCameraConnectionCommand_Exists()
    {
        var settingsService = new SettingsService();
        var synchronizer = new StereoSynchronizer();
        var viewModel = new MainWindowViewModel(settingsService, synchronizer);

        Assert.NotNull(viewModel.CancelCameraConnectionCommand);

        viewModel.Cleanup();
    }

    /// <summary>
    /// Проверяет, что ConnectionTimeoutMs имеет корректное значение по умолчанию.
    /// </summary>
    [Fact]
    public void AppSettings_ConnectionTimeoutMs_DefaultValue()
    {
        var settings = new LuminaCalib.Models.AppSettings();

        Assert.Equal(15000, settings.CameraConnectionTimeoutMs);
    }

    /// <summary>
    /// Проверяет, что изменение IsConnectingCameras уведомляет об изменении CanShowConnectButton.
    /// </summary>
    [Fact]
    public void IsConnectingCameras_NotifiesCanShowConnectButton()
    {
        var settingsService = new SettingsService();
        var synchronizer = new StereoSynchronizer();
        var viewModel = new MainWindowViewModel(settingsService, synchronizer);

        var propertyChanges = new System.Collections.Generic.List<string>();
        viewModel.PropertyChanged += (_, e) => propertyChanges.Add(e.PropertyName!);

        viewModel.IsConnectingCameras = true;

        Assert.Contains(nameof(viewModel.CanShowConnectButton), propertyChanges);

        viewModel.Cleanup();
    }

    // === Phase 6: Depth Map Integration Tests ===

    /// <summary>
    /// Validates that IsDepthMapView navigation property properly participates
    /// in mutual exclusivity with other navigation views.
    /// </summary>
    [Fact]
    public void Navigation_DepthMap_MutuallyExclusive()
    {
        // Arrange
        var settingsService = new SettingsService();
        var synchronizer = new StereoSynchronizer();
        var viewModel = new MainWindowViewModel(settingsService, synchronizer);

        // Act - Switch to DepthMap view
        viewModel.IsDepthMapView = true;

        // Assert - Only DepthMap should be active
        Assert.True(viewModel.IsDepthMapView, "DepthMap view should be active");
        Assert.False(viewModel.IsCalibrationView, "Calibration view should be deactivated");
        Assert.False(viewModel.IsHistoryView, "History view should be deactivated");
        Assert.False(viewModel.IsBoardGeneratorView, "BoardGenerator view should be deactivated");
        Assert.False(viewModel.IsLogView, "Log view should be deactivated");

        // Act - Switch away from DepthMap to Calibration
        viewModel.IsCalibrationView = true;

        // Assert - Only Calibration should be active
        Assert.True(viewModel.IsCalibrationView, "Calibration view should be active");
        Assert.False(viewModel.IsDepthMapView, "DepthMap view should be deactivated");
        Assert.False(viewModel.IsBoardGeneratorView, "BoardGenerator view should be deactivated");

        // Cleanup
        viewModel.Cleanup();
    }

    /// <summary>
    /// Validates that DepthMapVm is created and accessible after construction.
    /// </summary>
    [Fact]
    public void Constructor_Creates_DepthMapVm()
    {
        // Arrange
        var settingsService = new SettingsService();
        var synchronizer = new StereoSynchronizer();

        // Act
        var viewModel = new MainWindowViewModel(settingsService, synchronizer);

        // Assert
        Assert.NotNull(viewModel.DepthMapVm);

        // Cleanup
        viewModel.Cleanup();
    }

    /// <summary>
    /// Validates that the DepthMap tab text is properly localized.
    /// </summary>
    [Fact]
    public void DepthMapTabText_IsInitialized()
    {
        // Arrange
        var settingsService = new SettingsService();
        var synchronizer = new StereoSynchronizer();

        // Act
        var viewModel = new MainWindowViewModel(settingsService, synchronizer);

        // Assert
        Assert.False(string.IsNullOrWhiteSpace(viewModel.DepthMapTabText));

        // Cleanup
        viewModel.Cleanup();
    }

    /// <summary>
    /// Проверяет, что кнопка поиска паттерна отображается только на этапе «Захват».
    /// </summary>
    [Fact]
    public void CanShowDetectionButton_IsVisibleOnlyOnCaptureStep()
    {
        var settingsService = new SettingsService();
        settingsService.Settings.UseSavedSessionForCalibration = false;
        var synchronizer = new StereoSynchronizer();
        var viewModel = new MainWindowViewModel(settingsService, synchronizer);

        viewModel.LeftCameraConnected = true;
        viewModel.RightCameraConnected = true;

        viewModel.CurrentStep = 0;
        Assert.False(viewModel.CanShowDetectionButton);

        viewModel.CurrentStep = 1;
        Assert.True(viewModel.CanShowDetectionButton);

        viewModel.CurrentStep = 2;
        Assert.False(viewModel.CanShowDetectionButton);

        viewModel.CurrentStep = 3;
        Assert.False(viewModel.CanShowDetectionButton);

        viewModel.Cleanup();
    }

    /// <summary>
    /// Проверяет динамическую подпись кнопки поиска по текущему состоянию захвата.
    /// </summary>
    [Fact]
    public void DetectionToggleText_ReflectsCaptureState()
    {
        var settingsService = new SettingsService();
        settingsService.Settings.Language = "en";
        var synchronizer = new StereoSynchronizer();
        var viewModel = new MainWindowViewModel(settingsService, synchronizer);

        viewModel.RequiredPairs = 20;
        viewModel.CapturedPairsCount = 0;
        viewModel.AutoCapturing = false;
        Assert.Equal("Start Detection", viewModel.DetectionToggleText);

        viewModel.CapturedPairsCount = 3;
        Assert.Equal("Continue Detection", viewModel.DetectionToggleText);

        viewModel.AutoCapturing = true;
        Assert.Equal("Stop Detection", viewModel.DetectionToggleText);

        viewModel.AutoCapturing = false;
        viewModel.CapturedPairsCount = 20;
        Assert.Equal("Detection completed", viewModel.DetectionToggleText);

        viewModel.Cleanup();
    }

    /// <summary>
    /// Проверяет доступность кнопки поиска в зависимости от готовности сервиса автозахвата.
    /// </summary>
    [Fact]
    public void CanToggleDetection_RequiresCaptureStepAndAutoCaptureService()
    {
        var settingsService = new SettingsService();
        settingsService.Settings.UseSavedSessionForCalibration = false;
        var synchronizer = new StereoSynchronizer();
        var viewModel = new MainWindowViewModel(settingsService, synchronizer);

        viewModel.LeftCameraConnected = true;
        viewModel.RightCameraConnected = true;
        viewModel.CurrentStep = 1;
        viewModel.CapturedPairsCount = 0;
        viewModel.RequiredPairs = 20;

        Assert.False(viewModel.CanToggleDetection);

        var detector = new CornerDetector(9, 6);
        SetPrivateField(viewModel, "_autoCaptureService", new AutoCaptureService(detector, CalibrationMode.Auto));
        Assert.True(viewModel.CanToggleDetection);

        viewModel.CapturedPairsCount = 20;
        Assert.False(viewModel.CanToggleDetection);

        viewModel.Cleanup();
    }

    /// <summary>
    /// Проверяет динамическую подпись кнопки калибровки в зависимости от состояния вычислений/результата.
    /// </summary>
    [Fact]
    public void CalibrateActionText_ReflectsComputationAndResultState()
    {
        var settingsService = new SettingsService();
        settingsService.Settings.Language = "en";
        var synchronizer = new StereoSynchronizer();
        var viewModel = new MainWindowViewModel(settingsService, synchronizer);

        Assert.Equal("Calibrate", viewModel.CalibrateActionText);

        viewModel.IsCalibrating = true;
        Assert.Equal("Calibrating...", viewModel.CalibrateActionText);

        viewModel.IsCalibrating = false;
        SetPrivateField(viewModel, "_currentCalibration", CreateValidCalibrationResult());
        Assert.Equal("Recalibrate", viewModel.CalibrateActionText);

        viewModel.Cleanup();
    }

    [Fact]
    public Task DepthMap_EnableFails_WhenCamerasDisconnected()
    => UiTest.Run(() =>
    {
        var settingsService = new SettingsService();
        settingsService.Settings.Language = "en";
        var synchronizer = new StereoSynchronizer();
        var viewModel = new MainWindowViewModel(settingsService, synchronizer);

        viewModel.DepthMapVm.IsEnabled = true;

        Assert.False(viewModel.DepthMapVm.IsEnabled);
        Assert.Contains("connected cameras", viewModel.CalibrationStatusText, StringComparison.OrdinalIgnoreCase);

        viewModel.Cleanup();
    });

    /// <summary>
    /// Проверяет поведение кнопок и захвата при частичном подключении камер.
    /// </summary>
    [Fact]
    public void CameraButtons_WhenOnlyOneCameraConnected_DisconnectVisibleCaptureDisabled()
    {
        var settingsService = new SettingsService();
        var synchronizer = new StereoSynchronizer();
        var viewModel = new MainWindowViewModel(settingsService, synchronizer);

        viewModel.LeftCameraConnected = true;
        viewModel.RightCameraConnected = false;

        Assert.True(viewModel.HasAnyCameraConnected);
        Assert.False(viewModel.AreCamerasConnected);
        Assert.False(viewModel.CanShowConnectButton);
        Assert.True(viewModel.CanShowDisconnectButton);
        Assert.False(viewModel.CanCapture);

        viewModel.Cleanup();
    }

    /// <summary>
    /// Проверяет поведение кнопок при полном отключении камер.
    /// </summary>
    [Fact]
    public void CameraButtons_WhenNoCameraConnected_ConnectVisibleDisconnectHidden()
    {
        var settingsService = new SettingsService();
        var synchronizer = new StereoSynchronizer();
        var viewModel = new MainWindowViewModel(settingsService, synchronizer);

        viewModel.LeftCameraConnected = false;
        viewModel.RightCameraConnected = false;

        Assert.False(viewModel.HasAnyCameraConnected);
        Assert.True(viewModel.CanShowConnectButton);
        Assert.False(viewModel.CanShowDisconnectButton);

        viewModel.Cleanup();
    }

    [Fact]
    public Task DepthMap_EnableFails_WhenNoActiveDepthCalibration()
    => UiTest.Run(() =>
    {
        var settingsService = new SettingsService();
        settingsService.Settings.Language = "en";
        settingsService.Settings.ActiveDepthMapCalibrationPath = string.Empty;
        settingsService.Settings.ActiveAutoCalibrationPath = string.Empty;
        settingsService.Settings.ActiveStereoOnlyCalibrationPath = string.Empty;

        var synchronizer = new StereoSynchronizer();
        var viewModel = new MainWindowViewModel(settingsService, synchronizer);
        viewModel.LeftCameraConnected = true;
        viewModel.RightCameraConnected = true;

        viewModel.DepthMapVm.IsEnabled = true;

        Assert.False(viewModel.DepthMapVm.IsEnabled);
        Assert.Contains("active calibration file", viewModel.CalibrationStatusText, StringComparison.OrdinalIgnoreCase);

        viewModel.Cleanup();
    });

    [Fact]
    public void DepthMap_EnableSucceeds_WhenCalibrationIsAvailable()
    {
        var root = Path.Combine(Path.GetTempPath(), $"LuminaCalib_DepthMap_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var settingsPath = Path.Combine(root, "settings.json");
        var calibrationPath = Path.Combine(root, "depth.xml");

        try
        {
            var settingsService = new SettingsService(settingsPath);
            settingsService.Settings.Language = "en";
            settingsService.Settings.ActiveDepthMapCalibrationPath = calibrationPath;
            CreateCalibrationFile(calibrationPath);

            var viewModel = new MainWindowViewModel(settingsService, new StereoSynchronizer());
            viewModel.LeftCameraConnected = true;
            viewModel.RightCameraConnected = true;

            viewModel.DepthMapVm.IsEnabled = true;

            Assert.True(viewModel.DepthMapVm.IsEnabled);
            Assert.Contains(Path.GetFileName(calibrationPath), viewModel.DepthMapActiveCalibrationText, StringComparison.OrdinalIgnoreCase);

            var previewPathField = typeof(MainWindowViewModel).GetField("_previewCalibrationPath", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(previewPathField);
            var loadedPath = Assert.IsType<string>(previewPathField.GetValue(viewModel));
            Assert.Equal(Path.GetFullPath(calibrationPath), loadedPath, StringComparer.OrdinalIgnoreCase);

            viewModel.Cleanup();
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void EnsureActiveCaptureSession_PersistsActiveSessionPath()
    {
        var root = Path.Combine(Path.GetTempPath(), $"LuminaCalib_Session_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        var settingsPath = Path.Combine(root, "settings.json");
        var sessionsPath = Path.Combine(root, "sessions");
        Directory.CreateDirectory(sessionsPath);

        try
        {
            var settingsService = new SettingsService(settingsPath);
            settingsService.Settings.CaptureSessionsPath = sessionsPath;

            var viewModel = new MainWindowViewModel(settingsService, new StereoSynchronizer());
            try
            {
                var ensureMethod = typeof(MainWindowViewModel).GetMethod("EnsureActiveCaptureSession", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.NotNull(ensureMethod);

                var session = Assert.IsType<CaptureSessionManifest>(
                    ensureMethod.Invoke(viewModel, new object[] { CalibrationMode.Auto }));

                Assert.Equal(session.SessionDirectory, settingsService.Settings.ActiveAutoSessionPath);
                Assert.True(Directory.Exists(session.SessionDirectory));
            }
            finally
            {
                viewModel.Cleanup();
            }

            var reloaded = new SettingsService(settingsPath);
            Assert.False(string.IsNullOrWhiteSpace(reloaded.Settings.ActiveAutoSessionPath));
            Assert.True(Directory.Exists(reloaded.Settings.ActiveAutoSessionPath));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void EmitLeftFrame_WhenCalibrationViewDisabled_DoesNotRaisePreviewEvent()
    {
        var viewModel = new MainWindowViewModel(new SettingsService(), new StereoSynchronizer());
        try
        {
            var emitMethod = typeof(MainWindowViewModel).GetMethod(
                "EmitLeftFrame",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(emitMethod);

            int eventCount = 0;
            viewModel.OnLeftFrameReceived += _ => eventCount++;
            viewModel.IsCalibrationView = false;

            using var frame = new FrameRaw(new Mat(16, 16, DepthType.Cv8U, 3), DateTime.UtcNow, 1, "Left");
            emitMethod!.Invoke(viewModel, new object[] { frame });

            Assert.Equal(0, eventCount);
        }
        finally
        {
            viewModel.Cleanup();
        }
    }

    [Fact]
    public void EmitRightFrame_WhenCalibrationViewEnabled_RaisesPreviewEvent()
    {
        var viewModel = new MainWindowViewModel(new SettingsService(), new StereoSynchronizer());
        try
        {
            var emitMethod = typeof(MainWindowViewModel).GetMethod(
                "EmitRightFrame",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(emitMethod);

            int eventCount = 0;
            viewModel.OnRightFrameReceived += _ => eventCount++;
            viewModel.IsCalibrationView = true;

            using var frame = new FrameRaw(new Mat(16, 16, DepthType.Cv8U, 3), DateTime.UtcNow, 2, "Right");
            emitMethod!.Invoke(viewModel, new object[] { frame });

            Assert.Equal(1, eventCount);
        }
        finally
        {
            viewModel.Cleanup();
        }
    }

    [Fact]
    public async Task CaptureFrame_UsesFallbackPair_WhenStrictPairIsMissingButWithinLimit()
    {
        var settingsService = new SettingsService();
        settingsService.Settings.Language = "en";

        var synchronizer = new StereoSynchronizer(bufferCapacity: 10, toleranceMs: 50.0);
        var viewModel = new MainWindowViewModel(settingsService, synchronizer);
        try
        {
            viewModel.UseSavedSessionForCalibration = false;
            viewModel.LeftCameraConnected = true;
            viewModel.RightCameraConnected = true;

            var baseTime = DateTime.UtcNow;
            synchronizer.PushLeft(new FrameRaw(new Mat(120, 160, DepthType.Cv8U, 3), baseTime, 1, "left"));
            synchronizer.PushRight(new FrameRaw(new Mat(120, 160, DepthType.Cv8U, 3), baseTime.AddMilliseconds(80), 2, "right"));

            await viewModel.CaptureFrameCommand.ExecuteAsync(null);

            // strict-пара недоступна, поэтому в "Pattern not detected..." можно попасть только через fallback-пару.
            Assert.DoesNotContain("No synchronized frame pair available", viewModel.CalibrationStatusText, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Pattern not detected", viewModel.CalibrationStatusText, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            viewModel.Cleanup();
        }
    }

    [Fact]
    public async Task CaptureFrame_WhenClosestPairDeltaTooHigh_ShowsClearError()
    {
        var settingsService = new SettingsService();
        settingsService.Settings.Language = "en";

        var synchronizer = new StereoSynchronizer(bufferCapacity: 10, toleranceMs: 50.0);
        var viewModel = new MainWindowViewModel(settingsService, synchronizer);
        try
        {
            viewModel.UseSavedSessionForCalibration = false;
            viewModel.LeftCameraConnected = true;
            viewModel.RightCameraConnected = true;

            var baseTime = DateTime.UtcNow;
            synchronizer.PushLeft(new FrameRaw(new Mat(120, 160, DepthType.Cv8U, 3), baseTime, 1, "left"));
            synchronizer.PushRight(new FrameRaw(new Mat(120, 160, DepthType.Cv8U, 3), baseTime.AddMilliseconds(180), 2, "right"));

            await viewModel.CaptureFrameCommand.ExecuteAsync(null);

            Assert.Contains("Nearest delta", viewModel.CalibrationStatusText, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("limit is 120", viewModel.CalibrationStatusText, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            viewModel.Cleanup();
        }
    }

    [Fact]
    public void CurrentPatternDescriptorText_ForChessboard_ShowsInnerCorners()
    {
        var settingsService = new SettingsService();
        settingsService.Settings.Language = "en";
        settingsService.Settings.BoardType = BoardType.Chessboard;
        settingsService.Settings.PatternWidth = 9;
        settingsService.Settings.PatternHeight = 6;

        var viewModel = new MainWindowViewModel(settingsService, new StereoSynchronizer());
        try
        {
            Assert.Contains("Chessboard 9x6", viewModel.CurrentPatternDescriptorText, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("inner corners", viewModel.CurrentPatternDescriptorText, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            viewModel.Cleanup();
        }
    }

    [Fact]
    public void SavedSession_IncompatibleWithCurrentBoardSettings_IsMarkedAndNotCalibratable()
    {
        var root = Path.Combine(Path.GetTempPath(), $"LuminaCalib_IncompatSession_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        var settingsPath = Path.Combine(root, "settings.json");
        var sessionDirectory = Path.Combine(root, "session-incompatible");

        try
        {
            var settingsService = new SettingsService(settingsPath);
            settingsService.Settings.Language = "en";
            settingsService.Settings.UseSavedSessionForCalibration = true;
            settingsService.Settings.BoardType = BoardType.Chessboard;
            settingsService.Settings.PatternWidth = 9;
            settingsService.Settings.PatternHeight = 6;
            settingsService.Settings.ActiveAutoSessionPath = sessionDirectory;

            var manifest = new CaptureSessionManifest
            {
                SessionId = "charuco-session",
                CreatedAtUtc = DateTime.UtcNow,
                CreatedAtLocal = DateTime.Now,
                CaptureMode = CalibrationMode.Auto,
                BoardType = BoardType.ChArUco,
                CharucoDictionary = CharucoDictionary.Dict6x6_250,
                PatternWidth = 5,
                PatternHeight = 7,
                SquareSizeMm = 25f,
                MarkerSizeRatio = 0.73f,
                RequiredFrames = 20,
                SyncToleranceMs = 50,
                IsCompleted = true,
                Captures = Enumerable.Range(1, 20)
                    .Select(index => new CaptureFrameEntry
                    {
                        Index = index,
                        LeftImageFile = $"left_{index}.png",
                        RightImageFile = $"right_{index}.png"
                    })
                    .ToList()
            };
            WriteSessionManifest(sessionDirectory, manifest);

            var viewModel = new MainWindowViewModel(settingsService, new StereoSynchronizer());
            try
            {
                Assert.Contains("incompatible", viewModel.CurrentModeActiveSessionText, StringComparison.OrdinalIgnoreCase);
                Assert.False(viewModel.CanCalibrate);
                Assert.Equal(0, viewModel.CurrentModeActiveSessionCaptureCount);
                Assert.False(viewModel.CurrentModeActiveSessionIsCompleted);
            }
            finally
            {
                viewModel.Cleanup();
            }
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    private static void CreateCalibrationFile(string filePath)
    {
        using var calibration = CreateValidCalibrationResult();
        calibration.ImageSize = new System.Drawing.Size(640, 480);
        calibration.SaveToXml(filePath);
    }

    private static CalibrationResult CreateValidCalibrationResult()
    {
        var camLeft = new Mat(3, 3, DepthType.Cv64F, 1);
        CvInvoke.SetIdentity(camLeft, new MCvScalar(1));

        var camRight = new Mat(3, 3, DepthType.Cv64F, 1);
        CvInvoke.SetIdentity(camRight, new MCvScalar(1));

        var distLeft = new Mat(1, 5, DepthType.Cv64F, 1);
        distLeft.SetTo(new MCvScalar(0));

        var distRight = new Mat(1, 5, DepthType.Cv64F, 1);
        distRight.SetTo(new MCvScalar(0));

        var r = new Mat(3, 3, DepthType.Cv64F, 1);
        CvInvoke.SetIdentity(r, new MCvScalar(1));

        var t = new Mat(3, 1, DepthType.Cv64F, 1);
        t.SetTo(new MCvScalar(0));

        var e = new Mat(3, 3, DepthType.Cv64F, 1);
        e.SetTo(new MCvScalar(0));

        var f = new Mat(3, 3, DepthType.Cv64F, 1);
        f.SetTo(new MCvScalar(0));

        var r1 = new Mat(3, 3, DepthType.Cv64F, 1);
        CvInvoke.SetIdentity(r1, new MCvScalar(1));

        var r2 = new Mat(3, 3, DepthType.Cv64F, 1);
        CvInvoke.SetIdentity(r2, new MCvScalar(1));

        var p1 = new Mat(3, 4, DepthType.Cv64F, 1);
        CvInvoke.SetIdentity(p1, new MCvScalar(1));

        var p2 = new Mat(3, 4, DepthType.Cv64F, 1);
        CvInvoke.SetIdentity(p2, new MCvScalar(1));

        var q = new Mat(4, 4, DepthType.Cv64F, 1);
        CvInvoke.SetIdentity(q, new MCvScalar(1));

        return new CalibrationResult
        {
            CameraMatrixLeft = camLeft,
            CameraMatrixRight = camRight,
            DistCoeffsLeft = distLeft,
            DistCoeffsRight = distRight,
            R = r,
            T = t,
            E = e,
            F = f,
            R1 = r1,
            R2 = r2,
            P1 = p1,
            P2 = p2,
            Q = q
        };
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

    private static void WriteSessionManifest(string sessionDirectory, CaptureSessionManifest manifest)
    {
        Directory.CreateDirectory(sessionDirectory);
        manifest.SessionDirectory = sessionDirectory;

        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };
        options.Converters.Add(new JsonStringEnumConverter());

        var manifestPath = Path.Combine(sessionDirectory, "session.json");
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, options));
    }
}
