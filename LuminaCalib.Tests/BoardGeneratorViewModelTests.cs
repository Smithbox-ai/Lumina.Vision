using LuminaCalib.Models;
using LuminaCalib.Services;
using LuminaCalib.ViewModels;
using Xunit;

namespace LuminaCalib.Tests;

/// <summary>
/// Unit tests for BoardGeneratorViewModel to validate board generation,
/// DPI selection, and ChArUco-specific UI visibility.
/// </summary>
public class BoardGeneratorViewModelTests : IDisposable
{
    private readonly BoardGeneratorViewModel _viewModel;

    public BoardGeneratorViewModelTests()
    {
        _viewModel = new BoardGeneratorViewModel();
    }

    public void Dispose()
    {
        _viewModel.Dispose();
    }

    /// <summary>
    /// Validates that AvailableDpis property is exposed with standard DPI values.
    /// </summary>
    [Fact]
    public void AvailableDpis_ContainsStandardValues()
    {
        // Assert
        Assert.NotNull(_viewModel.AvailableDpis);
        Assert.Contains(72, _viewModel.AvailableDpis);
        Assert.Contains(96, _viewModel.AvailableDpis);
        Assert.Contains(150, _viewModel.AvailableDpis);
        Assert.Contains(300, _viewModel.AvailableDpis);
        Assert.Contains(600, _viewModel.AvailableDpis);
    }

    /// <summary>
    /// Validates that Dpi property can be set to values from AvailableDpis.
    /// </summary>
    [Fact]
    public void Dpi_CanBeSetToAvailableValue()
    {
        // Act
        _viewModel.Dpi = 150;

        // Assert
        Assert.Equal(150, _viewModel.Dpi);
    }

    /// <summary>
    /// Validates that IsCharucoBoard is true when BoardType is ChArUco.
    /// </summary>
    [Fact]
    public void IsCharucoBoard_TrueForChArUco()
    {
        // Act
        _viewModel.BoardType = BoardType.ChArUco;

        // Assert
        Assert.True(_viewModel.IsCharucoBoard);
    }

    /// <summary>
    /// Validates that IsCharucoBoard is false when BoardType is Chessboard.
    /// </summary>
    [Fact]
    public void IsCharucoBoard_FalseForChessboard()
    {
        // Act
        _viewModel.BoardType = BoardType.Chessboard;

        // Assert
        Assert.False(_viewModel.IsCharucoBoard);
    }

    /// <summary>
    /// Validates that PropertyChanged fires for IsCharucoBoard when BoardType changes.
    /// This ensures UI elements bound to IsCharucoBoard update correctly.
    /// </summary>
    [Fact]
    public void IsCharucoBoard_NotifiesWhenBoardTypeChanges()
    {
        // Arrange
        var propertyChangedEvents = new List<string>();
        _viewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName != null)
            {
                propertyChangedEvents.Add(e.PropertyName);
            }
        };

        // Act
        _viewModel.BoardType = BoardType.Chessboard;

        // Assert
        Assert.Contains(nameof(BoardGeneratorViewModel.BoardType), propertyChangedEvents);
        Assert.Contains(nameof(BoardGeneratorViewModel.IsCharucoBoard), propertyChangedEvents);
    }

    /// <summary>
    /// Validates that rapid sequential GeneratePreview calls don't overlap.
    /// Only the last call should complete, preventing resource waste.
    /// </summary>
    [Fact]
    public async Task GeneratePreview_PreventsConcurrentExecution()
    {
        // Arrange
        var generationCount = 0;
        
        // Subscribe to preview updates to track generations
        _viewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(BoardGeneratorViewModel.PreviewImage) && 
                _viewModel.PreviewImage != null)
            {
                generationCount++;
            }
        };

        // Act - Fire multiple generate commands rapidly
        var task1 = _viewModel.GeneratePreviewCommand.ExecuteAsync(null);
        var task2 = _viewModel.GeneratePreviewCommand.ExecuteAsync(null);
        var task3 = _viewModel.GeneratePreviewCommand.ExecuteAsync(null);

        await Task.WhenAll(task1, task2, task3);
        
        // Allow a brief moment for any pending updates
        await Task.Delay(100);

        // Assert - Should have only completed final generation (or at most 2 if one started before cancellation)
        // This verifies we're not wasting resources on overlapping generations
        Assert.True(generationCount <= 2, 
            $"Expected at most 2 generations (one in-flight + final), but got {generationCount}");
    }

    [Fact]
    public void ApplyToCalibration_SyncsSettingsToAppSettings()
    {
        // Arrange
        var settingsPath = Path.Combine(Path.GetTempPath(), $"LuminaCalib_BoardSync_{Guid.NewGuid():N}.json");
        var settingsService = new SettingsService(settingsPath);

        try
        {
            using var vm = new BoardGeneratorViewModel(settingsService);
            vm.SquaresX = 11;
            vm.SquaresY = 8;
            vm.SquareLength = 35f;

            // Act
            vm.ApplyToCalibrationCommand.Execute(null);

            // Assert
            var appSettings = settingsService.Settings;
            Assert.Equal(11, appSettings.PatternWidth);
            Assert.Equal(8, appSettings.PatternHeight);
            Assert.Equal(35f, appSettings.SquareSize);
        }
        finally
        {
            try
            {
                if (File.Exists(settingsPath))
                    File.Delete(settingsPath);
            }
            catch { /* best-effort cleanup */ }
        }
    }

    [Fact]
    public void ApplyToCalibration_Chessboard_ConvertsSquaresToInnerCorners()
    {
        var settingsPath = Path.Combine(Path.GetTempPath(), $"LuminaCalib_BoardSyncChess_{Guid.NewGuid():N}.json");
        var settingsService = new SettingsService(settingsPath);

        try
        {
            using var vm = new BoardGeneratorViewModel(settingsService);
            vm.BoardType = BoardType.Chessboard;
            vm.SquaresX = 10;
            vm.SquaresY = 7;
            vm.SquareLength = 30f;

            vm.ApplyToCalibrationCommand.Execute(null);

            var appSettings = settingsService.Settings;
            Assert.Equal(BoardType.Chessboard, appSettings.BoardType);
            Assert.Equal(9, appSettings.PatternWidth);
            Assert.Equal(6, appSettings.PatternHeight);
            Assert.Equal(30f, appSettings.SquareSize);
        }
        finally
        {
            try
            {
                if (File.Exists(settingsPath))
                    File.Delete(settingsPath);
            }
            catch { /* best-effort cleanup */ }
        }
    }

    [Fact]
    public void Localization_UpdatesWhenLanguageChanges()
    {
        var settingsPath = Path.Combine(Path.GetTempPath(), $"LuminaCalib_Board_{Guid.NewGuid():N}.json");
        var settingsService = new SettingsService(settingsPath);

        try
        {
            using var vm = new BoardGeneratorViewModel(settingsService);

            settingsService.Settings.Language = "ru";
            Assert.NotEqual("Export", vm.ExportHeaderText);

            settingsService.Settings.Language = "en";
            Assert.Equal("Export", vm.ExportHeaderText);
        }
        finally
        {
            try
            {
                if (File.Exists(settingsPath))
                {
                    File.Delete(settingsPath);
                }
            }
            catch
            {
                // best-effort cleanup
            }
        }
    }
}

/// <summary>
/// Tests for MainWindowViewModel integration with BoardGeneratorViewModel.
/// </summary>
public class MainWindowBoardGeneratorIntegrationTests
{
    /// <summary>
    /// Validates that MainWindowViewModel has BoardGeneratorVm property initialized.
    /// </summary>
    [Fact]
    public void MainWindow_HasBoardGeneratorViewModel()
    {
        // Arrange
        var settingsService = new SettingsService();
        var synchronizer = new StereoSynchronizer();

        // Act
        var viewModel = new MainWindowViewModel(settingsService, synchronizer);

        // Assert
        Assert.NotNull(viewModel.BoardGeneratorVm);
        
        // Cleanup
        viewModel.Cleanup();
    }

    /// <summary>
    /// Validates that BoardGenerator tab navigation shows BoardGeneratorView.
    /// This is an integration test that verifies the entire tab switching flow.
    /// </summary>
    [Fact]
    public void MainWindow_BoardGeneratorTab_ShowsBoardGeneratorView()
    {
        // Arrange
        var settingsService = new SettingsService();
        var synchronizer = new StereoSynchronizer();
        var viewModel = new MainWindowViewModel(settingsService, synchronizer);

        // Act - Switch to Board Generator tab
        viewModel.IsBoardGeneratorView = true;

        // Assert - Board Generator view should be active
        Assert.True(viewModel.IsBoardGeneratorView);
        Assert.False(viewModel.IsCalibrationView);
        Assert.False(viewModel.IsHistoryView);
        
        // BoardGeneratorVm should be accessible for binding
        Assert.NotNull(viewModel.BoardGeneratorVm);
        
        // Cleanup
        viewModel.Cleanup();
    }
}
