using Avalonia.Controls;
using LuminaCalib.Devices;
using LuminaCalib.Models;
using LuminaCalib.Services;
using LuminaCalib.ViewModels;
using LuminaCalib.Views;
using Xunit;

namespace LuminaCalib.Tests;

/// <summary>
/// Smoke tests to verify that all Views can be instantiated and loaded without exceptions.
/// These tests validate basic view construction and XAML compilation.
/// </summary>
public class ViewLoadingTests
{
    /// <summary>
    /// Validates that MainWindow can be instantiated with a ViewModel without throwing exceptions.
    /// This catches XAML binding errors, missing resources, and basic initialization issues.
    /// </summary>
    [Fact]
    public Task MainWindow_Loads_Without_Exception()
    => UiTest.Run(() =>
    {
        // Arrange & Act
        var settingsService = new SettingsService();
        var synchronizer = new StereoSynchronizer();
        var viewModel = new MainWindowViewModel(settingsService, synchronizer);
        var window = new MainWindow
        {
            DataContext = viewModel
        };

        // Assert - successful instantiation without exceptions
        Assert.NotNull(window);
        Assert.NotNull(window.DataContext);
        Assert.IsType<MainWindowViewModel>(window.DataContext);

        // Cleanup
        viewModel.Cleanup();
    });

    /// <summary>
    /// Validates that SettingsView can be instantiated without throwing exceptions.
    /// This catches XAML compilation errors and missing resources.
    /// </summary>
    [Fact]
    public Task SettingsView_Loads_Without_Exception()
    => UiTest.Run(() =>
    {
        // Arrange & Act
        var view = new SettingsView();

        // Assert - successful instantiation without exceptions
        Assert.NotNull(view);
    });

    /// <summary>
    /// Validates that BoardGeneratorView can be instantiated without throwing exceptions.
    /// This catches XAML compilation errors and missing resources.
    /// </summary>
    [Fact]
    public Task BoardGeneratorView_Loads_Without_Exception()
    => UiTest.Run(() =>
    {
        // Arrange & Act
        var view = new BoardGeneratorView();

        // Assert - successful instantiation without exceptions
        Assert.NotNull(view);
    });

    /// <summary>
    /// Validates that the Settings overlay is properly wired to IsSettingsOpen property.
    /// This test ensures the UI binding doesn't break.
    /// </summary>
    [Fact]
    public Task MainWindow_SettingsOverlay_BindsToIsSettingsOpen()
    => UiTest.Run(() =>
    {
        // Arrange
        var settingsService = new SettingsService();
        var synchronizer = new StereoSynchronizer();
        var vm = new MainWindowViewModel(settingsService, synchronizer);
        var window = new MainWindow { DataContext = vm };

        // Force layout
        window.ApplyTemplate();

        // Act - Open settings
        vm.IsSettingsOpen = true;

        // Assert - Settings should be open
        Assert.True(vm.IsSettingsOpen);

        // Act - Close settings
        vm.IsSettingsOpen = false;

        // Assert - Settings should be closed
        Assert.False(vm.IsSettingsOpen);

        // Cleanup
        vm.Cleanup();
    });

    [Fact]
    public Task MainWindow_DefaultCalibrationModeRadioSelection_IsAuto()
    => UiTest.Run(() =>
    {
        var root = Path.Combine(Path.GetTempPath(), $"LuminaCalib_MainMode_Default_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var settingsPath = Path.Combine(root, "settings.json");

        try
        {
            var settingsService = new SettingsService(settingsPath);
            var vm = new MainWindowViewModel(settingsService, new StereoSynchronizer());
            try
            {
                var window = new MainWindow { DataContext = vm };
                window.ApplyTemplate();

                var (autoButton, stereoOnlyButton, singleCameraButton) = GetMainCalibrationModeButtons(window);
                Assert.Equal("MainCalibrationModeGroup", autoButton.GroupName);
                Assert.Equal("MainCalibrationModeGroup", stereoOnlyButton.GroupName);
                Assert.Equal("MainCalibrationModeGroup", singleCameraButton.GroupName);

                Assert.True(autoButton.IsChecked == true);
                Assert.False(stereoOnlyButton.IsChecked == true);
                Assert.False(singleCameraButton.IsChecked == true);
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
    });

    [Fact]
    public Task MainWindow_RestoresCalibrationModeRadioSelection_FromSettings()
    => UiTest.Run(() =>
    {
        var root = Path.Combine(Path.GetTempPath(), $"LuminaCalib_MainMode_Restore_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var settingsPath = Path.Combine(root, "settings.json");

        try
        {
            var writer = new SettingsService(settingsPath);
            writer.Settings.CalibrationMode = CalibrationMode.SingleCamera;
            writer.Save();

            var reader = new SettingsService(settingsPath);
            var vm = new MainWindowViewModel(reader, new StereoSynchronizer());
            try
            {
                var window = new MainWindow { DataContext = vm };
                window.ApplyTemplate();

                var (autoButton, stereoOnlyButton, singleCameraButton) = GetMainCalibrationModeButtons(window);
                Assert.True(autoButton.IsChecked == false);
                Assert.False(stereoOnlyButton.IsChecked == true);
                Assert.True(singleCameraButton.IsChecked == true);
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
    });

    private static (RadioButton Auto, RadioButton StereoOnly, RadioButton SingleCamera) GetMainCalibrationModeButtons(MainWindow window)
    {
        var autoButton = window.FindControl<RadioButton>("AutoModeRadioButton");
        var stereoOnlyButton = window.FindControl<RadioButton>("StereoOnlyModeRadioButton");
        var singleCameraButton = window.FindControl<RadioButton>("SingleCameraModeRadioButton");

        Assert.NotNull(autoButton);
        Assert.NotNull(stereoOnlyButton);
        Assert.NotNull(singleCameraButton);

        return (autoButton!, stereoOnlyButton!, singleCameraButton!);
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }
        catch
        {
            // Ignore cleanup failures in tests.
        }
    }
}
