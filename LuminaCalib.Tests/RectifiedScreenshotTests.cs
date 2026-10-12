using System.Text.Json;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using LuminaCalib.Calibration;
using LuminaCalib.Views;
using LuminaCalib.Models;
using LuminaCalib.Services;
using LuminaCalib.ViewModels;
using Xunit;

namespace LuminaCalib.Tests;

public sealed class RectifiedScreenshotTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"LuminaCalib-Screenshots-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_root) && Path.GetFullPath(_root).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase))
            Directory.Delete(_root, recursive: true);
    }

    private SettingsService CreateSettings() => new(Path.Combine(_root, "settings.json"));

    [Fact]
    public Task ScreenshotCommand_WithoutCameras_IsDisabled() => UiTest.Run(() =>
    {
        var settings = CreateSettings();
        var vm = new MainWindowViewModel(settings, new StereoSynchronizer());
        try
        {
            Assert.False(vm.SaveScreenshotCommand.CanExecute(null));
        }
        finally
        {
            vm.Cleanup();
        }
    });

    [Fact]
    public void ScreenshotDirectory_Default_IsInUserPicturesFolder()
    {
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(new AppSettings()));
        Assert.True(json.RootElement.TryGetProperty("ScreenshotsPath", out var path));
        Assert.Equal(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "LuminaCalib"), path.GetString());
    }

    [Fact]
    public Task ScreenshotCommand_PreviewAndCameraChanges_UpdateAvailability() => UiTest.Run(() =>
    {
        var settings = CreateSettings();
        settings.Settings.ActiveAutoCalibrationPath = Path.Combine(_root, "calibration.xml");
        var vm = new MainWindowViewModel(settings, new StereoSynchronizer());
        try
        {
            var updates = 0;
            vm.SaveScreenshotCommand.CanExecuteChanged += (_, _) => updates++;
            vm.LeftCameraConnected = true;
            vm.RightCameraConnected = true;
            Assert.False(vm.SaveScreenshotCommand.CanExecute(null));
            vm.ShowRectified = true;
            Assert.True(vm.SaveScreenshotCommand.CanExecute(null));
            vm.IsSettingsOpen = true;
            Assert.False(vm.SaveScreenshotCommand.CanExecute(null));
            vm.IsSettingsOpen = false;
            vm.IsHistoryView = true;
            Assert.False(vm.SaveScreenshotCommand.CanExecute(null));
            Assert.True(updates > 0);
        }
        finally { vm.Cleanup(); }
    });

    [Fact]
    public Task SaveScreenshot_SynchronizedPair_SavesRectifiedPixelsAtOriginalResolution() => UiTest.Run(async () =>
    {
        var settings = CreateSettings();
        settings.Settings.ScreenshotsPath = Path.Combine(_root, "снимки");
        settings.Settings.ActiveAutoCalibrationPath = Path.Combine(_root, "calibration.xml");
        Directory.CreateDirectory(_root);
        using var calibration = CreateCalibration();
        calibration.SaveToXml(settings.Settings.ActiveAutoCalibrationPath);
        var synchronizer = new StereoSynchronizer();
        var vm = new MainWindowViewModel(settings, synchronizer);
        try
        {
            vm.LeftCameraConnected = vm.RightCameraConnected = true;
            vm.ShowRectified = true;
            var save = vm.SaveScreenshotCommand.ExecuteAsync(null);
            Assert.True(vm.IsSavingScreenshot);
            Assert.False(vm.SaveScreenshotCommand.CanExecute(null));
            using var originalLeft = CreateImage(55);
            using var originalRight = CreateImage(155);
            var now = DateTime.UtcNow;
            synchronizer.PushLeft(new FrameRaw(originalLeft.Clone(), now, 10));
            synchronizer.PushRight(new FrameRaw(originalRight.Clone(), now.AddSeconds(1), 11));
            Assert.False(save.IsCompleted);
            synchronizer.PushRight(new FrameRaw(originalRight.Clone(), now, 12));
            await save;
            Assert.False(vm.IsSavingScreenshot);
            Assert.Equal(0, vm.CapturedPairsCount);
            var folder = Assert.Single(Directory.GetDirectories(settings.Settings.ScreenshotsPath));
            Assert.Equal(2, Directory.GetFiles(folder).Length);
            using var savedLeft = new Mat();
            using var savedRight = new Mat();
            CvInvoke.Imdecode(File.ReadAllBytes(Path.Combine(folder, "left_rectified.png")), ImreadModes.Unchanged, savedLeft);
            CvInvoke.Imdecode(File.ReadAllBytes(Path.Combine(folder, "right_rectified.png")), ImreadModes.Unchanged, savedRight);
            using var expectedLeft = new Mat();
            using var expectedRight = new Mat();
            using var rectifier = new StereoRectifier();
            rectifier.Initialize(calibration);
            rectifier.Rectify(originalLeft, originalRight, expectedLeft, expectedRight);
            Assert.Equal(originalLeft.Size, savedLeft.Size);
            Assert.Equal(0, CvInvoke.Norm(savedLeft, expectedLeft));
            Assert.Equal(0, CvInvoke.Norm(savedRight, expectedRight));
            Assert.True(CvInvoke.Norm(savedLeft, originalLeft) > 0);
        }
        finally { vm.Cleanup(); }
    });

    [Theory]
    [InlineData("missing-file")]
    [InlineData("wrong-size")]
    [InlineData("R1")]
    [InlineData("R2")]
    [InlineData("P1")]
    [InlineData("P2")]
    [InlineData("DistCoeffsLeft")]
    [InlineData("DistCoeffsRight")]
    [InlineData("non-finite")]
    public Task SaveScreenshot_InvalidCalibration_DoesNotSaveRawImages(string invalidData) => UiTest.Run(async () =>
    {
        var settings = CreateSettings();
        settings.Settings.ScreenshotsPath = Path.Combine(_root, "screenshots");
        settings.Settings.ActiveAutoCalibrationPath = Path.Combine(_root, "calibration.xml");
        Directory.CreateDirectory(_root);
        if (invalidData != "missing-file")
        {
            using var calibration = CreateCalibration(invalidData);
            if (invalidData == "wrong-size")
                calibration.ImageSize = new System.Drawing.Size(32, 24);
            calibration.SaveToXml(settings.Settings.ActiveAutoCalibrationPath);
        }
        var synchronizer = new StereoSynchronizer();
        var vm = new MainWindowViewModel(settings, synchronizer);
        try
        {
            vm.LeftCameraConnected = vm.RightCameraConnected = true;
            vm.ShowRectified = true;
            var save = vm.SaveScreenshotCommand.ExecuteAsync(null);
            var now = DateTime.UtcNow;
            synchronizer.PushLeft(new FrameRaw(CreateImage(55), now, 1));
            synchronizer.PushRight(new FrameRaw(CreateImage(155), now, 2));
            await save;
            Assert.False(Directory.Exists(settings.Settings.ScreenshotsPath));
            Assert.False(vm.IsSavingScreenshot);
        }
        finally { vm.Cleanup(); }
    });

    [Fact]
    public Task SaveScreenshot_DisconnectedWhileWaiting_CancelsWithoutFiles() => UiTest.Run(async () =>
    {
        var settings = CreateSettings();
        settings.Settings.ActiveAutoCalibrationPath = "unused.xml";
        settings.Settings.ScreenshotsPath = Path.Combine(_root, "screenshots");
        var vm = new MainWindowViewModel(settings, new StereoSynchronizer());
        try
        {
            vm.LeftCameraConnected = vm.RightCameraConnected = true;
            vm.ShowRectified = true;
            var save = vm.SaveScreenshotCommand.ExecuteAsync(null);
            vm.RightCameraConnected = false;
            await save;
            Assert.False(vm.IsSavingScreenshot);
            Assert.False(Directory.Exists(settings.Settings.ScreenshotsPath));
        }
        finally { vm.Cleanup(); }
    });

    [Fact]
    public void Save_RepeatedTimestamp_PreservesBothPairs()
    {
        using var left = CreateImage(55);
        using var right = CreateImage(155);
        var time = DateTime.UtcNow;
        var first = ScreenshotService.Save(left, right, _root, time, TestContext.Current.CancellationToken);
        var second = ScreenshotService.Save(left, right, _root, time, TestContext.Current.CancellationToken);
        Assert.NotEqual(first, second);
        Assert.Equal(2, Directory.GetDirectories(_root).Length);
        Assert.Equal(4, Directory.GetFiles(_root, "*.png", SearchOption.AllDirectories).Length);
    }

    [Fact]
    public void Save_Cancelled_DoesNotCreateFolder()
    {
        using var left = CreateImage(55);
        using var right = CreateImage(155);
        Assert.Throws<OperationCanceledException>(() => ScreenshotService.Save(left, right, _root, DateTime.UtcNow, new CancellationToken(true)));
        Assert.False(Directory.Exists(_root));
    }

    [Fact]
    public void Save_DestinationIsFile_FailsWithoutPublishingPair()
    {
        Directory.CreateDirectory(_root);
        var destination = Path.Combine(_root, "existing.txt");
        File.WriteAllText(destination, "preserve");
        using var left = CreateImage(55);
        using var right = CreateImage(155);
        Assert.Throws<IOException>(() => ScreenshotService.Save(left, right, destination, DateTime.UtcNow, TestContext.Current.CancellationToken));
        Assert.Equal("preserve", File.ReadAllText(destination));
        Assert.Empty(Directory.GetDirectories(_root));
    }

    [Fact]
    public Task SaveScreenshot_NoSynchronizedFrames_TimesOutAndRestoresCommand() => UiTest.Run(async () =>
    {
        var settings = CreateSettings();
        settings.Settings.ActiveAutoCalibrationPath = "unused.xml";
        settings.Settings.ScreenshotsPath = Path.Combine(_root, "screenshots");
        var vm = new MainWindowViewModel(settings, new StereoSynchronizer());
        try
        {
            vm.LeftCameraConnected = vm.RightCameraConnected = true;
            vm.ShowRectified = true;
            await vm.SaveScreenshotCommand.ExecuteAsync(null);
            Assert.False(vm.IsSavingScreenshot);
            Assert.True(vm.SaveScreenshotCommand.CanExecute(null));
            Assert.False(Directory.Exists(settings.Settings.ScreenshotsPath));
        }
        finally { vm.Cleanup(); }
    });

    [Fact]
    public Task Settings_ScreenshotDirectory_IsPersistedAndBrowseIsRequested() => UiTest.Run(() =>
    {
        Directory.CreateDirectory(_root);
        var settings = CreateSettings();
        using var vm = new SettingsViewModel(settings);
        settings.Settings.ScreenshotsPath = Path.Combine(_root, "custom");
        var requested = false;
        vm.RequestScreenshotsFolderPicker += () => requested = true;
        vm.BrowseScreenshotsPathCommand.Execute(null);
        Assert.True(requested);
        settings.Save();
        Assert.Equal(settings.Settings.ScreenshotsPath, CreateSettings().Settings.ScreenshotsPath);
        settings.Settings.Language = "en";
        Assert.Equal("Screenshots folder:", vm.ScreenshotsPathLabelText);
    });

    [Theory]
    [InlineData("ru")]
    [InlineData("en")]
    public Task MainWindow_ScreenshotActions_AreBoundAndFitMinimumWidth(string language) => UiTest.Run(() =>
    {
        var settings = CreateSettings();
        settings.Settings.Language = language;
        var vm = new MainWindowViewModel(settings, new StereoSynchronizer());
        var window = new MainWindow { DataContext = vm, Width = 1024, Height = 700 };
        try
        {
            window.Show();
            window.UpdateLayout();
            var button = window.FindControl<Button>("SaveScreenshotButton")!;
            var folder = window.FindControl<Button>("OpenScreenshotsFolderButton")!;
            Assert.Same(vm.SaveScreenshotCommand, button.Command);
            Assert.Same(vm.OpenScreenshotsFolderCommand, folder.Command);
            Assert.False(button.IsEffectivelyEnabled);
            Assert.True(button.Bounds.Width > 0);
            Assert.True(folder.Bounds.Right <= ((Control)folder.Parent!).Bounds.Width);
            Assert.Contains(window.KeyBindings, binding => binding.Command == vm.SaveScreenshotCommand);
        }
        finally { window.Close(); vm.Cleanup(); }
    });

    private static Mat CreateImage(double color)
    {
        var image = new Mat(12, 16, DepthType.Cv8U, 3);
        image.SetTo(new MCvScalar(color, color + 20, color + 30));
        return image;
    }

    private static Mat Matrix(int rows, int columns, params double[] values)
    {
        var matrix = new Mat(rows, columns, DepthType.Cv64F, 1);
        Marshal.Copy(values, 0, matrix.DataPointer, values.Length);
        return matrix;
    }

    private static CalibrationResult CreateCalibration(string? emptyMatrix = null) => new()
    {
        ImageSize = new System.Drawing.Size(16, 12),
        CameraMatrixLeft = Matrix(3, 3, 1, 0, 0, 0, 1, 0, 0, 0, 1),
        CameraMatrixRight = Matrix(3, 3, 1, 0, 0, 0, 1, 0, 0, 0, 1),
        DistCoeffsLeft = emptyMatrix == "DistCoeffsLeft" ? new Mat() : Matrix(1, 5, 0, 0, 0, 0, 0),
        DistCoeffsRight = emptyMatrix == "DistCoeffsRight" ? new Mat() : Matrix(1, 5, 0, 0, 0, 0, 0),
        R = Matrix(3, 3, 1, 0, 0, 0, 1, 0, 0, 0, 1),
        T = Matrix(3, 1, 1, 0, 0),
        E = Matrix(3, 3, 0, 0, 0, 0, 0, 0, 0, 0, 0),
        F = Matrix(3, 3, 0, 0, 0, 0, 0, 0, 0, 0, 0),
        R1 = emptyMatrix == "R1" ? new Mat() : Matrix(3, 3, emptyMatrix == "non-finite" ? double.NaN : 1, 0, 0, 0, 1, 0, 0, 0, 1),
        R2 = emptyMatrix == "R2" ? new Mat() : Matrix(3, 3, 1, 0, 0, 0, 1, 0, 0, 0, 1),
        P1 = emptyMatrix == "P1" ? new Mat() : Matrix(3, 4, 1, 0, 2, 0, 0, 1, 0, 0, 0, 0, 1, 0),
        P2 = emptyMatrix == "P2" ? new Mat() : Matrix(3, 4, 1, 0, 2, 0, 0, 1, 0, 0, 0, 0, 1, 0),
        Q = Matrix(4, 4, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1)
    };
}
