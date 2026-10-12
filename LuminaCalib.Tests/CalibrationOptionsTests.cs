using System.Reflection;
using System.Drawing;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using Emgu.CV.Util;
using LuminaCalib.Calibration;
using LuminaCalib.Models;
using LuminaCalib.Services;
using LuminaCalib.ViewModels;
using LuminaCalib.Views;
using Xunit;

namespace LuminaCalib.Tests;

public class CalibrationOptionsTests
{
    [Fact]
    public void CalibrateFullStereo_RationalModel_PreservesExtendedDistortion()
    {
        using var engine = CreateSyntheticStereoEngine(rationalDistortion: true);
        engine.IntrinsicCalibrationFlags = CalibType.RationalModel;
        using var result = engine.CalibrateFullStereo();
        Assert.True(result.DistCoeffsLeft.Total.ToInt64() >= 8);
        Assert.True(result.DistCoeffsRight.Total.ToInt64() >= 8);
        Assert.InRange(result.ReprojectionError, 0, 0.02);
    }

    [Theory]
    [InlineData(8)]
    [InlineData(12)]
    [InlineData(14)]
    public void CalibrateStereoWithIntrinsics_ExtendedDistortion_PreservesInputCoefficients(int count)
    {
        using var engine = CreateSyntheticStereoEngine();
        using var camera = CreateMatrix(3, 3, [600, 0, 320, 0, 600, 240, 0, 0, 1]);
        var coefficients = new double[count];
        coefficients[0] = -0.25;
        coefficients[1] = 0.04;
        coefficients[5] = 0.02;
        if (count >= 12)
        {
            coefficients[8] = 0.001;
            coefficients[9] = -0.002;
        }
        if (count == 14)
            coefficients[12] = 0.001;
        using var distortionLeft = CreateMatrix(1, count, coefficients);
        using var distortionRight = distortionLeft.Clone();
        using var result = engine.CalibrateStereoWithIntrinsics(camera, distortionLeft, camera, distortionRight);
        var actual = new double[count];
        Assert.True(result.DistCoeffsLeft.Total.ToInt64() >= count);
        Marshal.Copy(result.DistCoeffsLeft.DataPointer, actual, 0, count);
        Assert.Equal(coefficients, actual);
    }

    [Fact]
    public Task Constructor_InvalidPersistedAlpha_AllowsCorrectionInSettings()
    => UiTest.Run(() =>
    {
        var settingsPath = Path.Combine(Path.GetTempPath(), $"LuminaCalib_Options_{Guid.NewGuid():N}.json");
        File.WriteAllText(settingsPath, "{\"rectificationAlpha\":-0.5}");
        var service = new SettingsService(settingsPath);
        using var synchronizer = new StereoSynchronizer();
        MainWindowViewModel? viewModel = null;
        try
        {
            viewModel = new MainWindowViewModel(service, synchronizer);
            Assert.NotNull(viewModel.SettingsVm.RectificationAlphaError);
            service.Settings.RectificationAlpha = 0;
            Assert.Null(viewModel.SettingsVm.RectificationAlphaError);
        }
        finally
        {
            viewModel?.Cleanup();
            File.Delete(settingsPath);
        }
    });

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    [InlineData(false, -1)]
    [InlineData(true, -1)]
    public void StereoCalibration_SelectedAlpha_ReachesRectification(bool fullStereo, double alpha)
    {
        using var engine = CreateSyntheticStereoEngine();
        engine.RectificationAlpha = alpha;
        using var camera = CreateMatrix(3, 3, [600, 0, 320, 0, 600, 240, 0, 0, 1]);
        using var distortion = CreateMatrix(1, 5, [-0.25, 0.04, 0, 0, 0]);
        using var result = fullStereo
            ? engine.CalibrateFullStereo()
            : engine.CalibrateStereoWithIntrinsics(camera, distortion, camera, distortion);
        using var r1 = new Mat();
        using var r2 = new Mat();
        using var p1 = new Mat();
        using var p2 = new Mat();
        using var q = new Mat();
        Rectangle roi1 = default;
        Rectangle roi2 = default;

        CvInvoke.StereoRectify(result.CameraMatrixLeft, result.DistCoeffsLeft,
            result.CameraMatrixRight, result.DistCoeffsRight, result.ImageSize,
            result.R, result.T, r1, r2, p1, p2, q, (StereoRectifyType)0x400,
            alpha, result.ImageSize, ref roi1, ref roi2);

        Assert.InRange(CvInvoke.Norm(result.P1, p1), 0, 1e-6);
        Assert.InRange(CvInvoke.Norm(result.P2, p2), 0, 1e-6);
        Assert.InRange(CvInvoke.Norm(result.Q, q), 0, 1e-6);
    }

    private static CalibrationEngine CreateSyntheticStereoEngine(bool rationalDistortion = false)
    {
        var engine = new CalibrationEngine(new CornerDetector(9, 6));
        var objects = GetEngineField<List<VectorOfPoint3D32F>>(engine, "_stereoObjectPoints");
        var left = GetEngineField<List<VectorOfPointF>>(engine, "_leftImagePoints");
        var right = GetEngineField<List<VectorOfPointF>>(engine, "_rightImagePoints");
        typeof(CalibrationEngine).GetField("_imageSize", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(engine, new Size(640, 480));

        for (int shot = 0; shot < 16; shot++)
        {
            var points = new List<MCvPoint3D32f>();
            var leftPoints = new List<PointF>();
            var rightPoints = new List<PointF>();
            double yaw = (shot % 4 - 1.5) * 0.15;
            double pitch = (shot / 4 - 1.5) * 0.12;
            for (int row = 0; row < 6; row++)
            {
                for (int column = 0; column < 9; column++)
                {
                    float x = column * 20;
                    float y = row * 20;
                    points.Add(new MCvPoint3D32f(x, y, 0));
                    double px = Math.Cos(yaw) * x + Math.Sin(yaw) * Math.Sin(pitch) * y - 80;
                    double py = Math.Cos(pitch) * y - 50;
                    double pz = -Math.Sin(yaw) * x + Math.Cos(yaw) * Math.Sin(pitch) * y + 450 + shot * 12;
                    leftPoints.Add(Project(px, py, pz, rationalDistortion));
                    rightPoints.Add(Project(px - 80, py, pz, rationalDistortion));
                }
            }
            objects.Add(new VectorOfPoint3D32F(points.ToArray()));
            left.Add(new VectorOfPointF(leftPoints.ToArray()));
            right.Add(new VectorOfPointF(rightPoints.ToArray()));
        }
        return engine;
    }

    private static PointF Project(double x, double y, double z, bool rationalDistortion)
    {
        x /= z;
        y /= z;
        double radiusSquared = x * x + y * y;
        double scale = 1 - 0.25 * radiusSquared + 0.04 * radiusSquared * radiusSquared;
        if (rationalDistortion)
            scale /= 1 + 0.08 * radiusSquared + 0.02 * radiusSquared * radiusSquared;
        return new PointF((float)(600 * x * scale + 320), (float)(600 * y * scale + 240));
    }

    private static T GetEngineField<T>(CalibrationEngine engine, string name)
        => (T)typeof(CalibrationEngine).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(engine)!;

    private static Mat CreateMatrix(int rows, int columns, double[] values)
    {
        var matrix = new Mat(rows, columns, DepthType.Cv64F, 1);
        Marshal.Copy(values, 0, matrix.DataPointer, values.Length);
        return matrix;
    }

    [Theory]
    [InlineData(true, CalibType.Default, CalibType.RationalModel)]
    [InlineData(false, CalibType.RationalModel, CalibType.Default)]
    public Task RunCalibrationForMode_CurrentSettings_OverridesEngineModel(
        bool useRationalModel, CalibType initialFlags, CalibType expectedFlags)
    => UiTest.Run(() =>
    {
        var settingsPath = Path.Combine(Path.GetTempPath(), $"LuminaCalib_Options_{Guid.NewGuid():N}.json");
        var settingsService = new SettingsService(settingsPath);
        settingsService.Settings.UseRationalModel = useRationalModel;
        settingsService.Settings.RectificationAlpha = 0.75;
        using var synchronizer = new StereoSynchronizer();
        var viewModel = new MainWindowViewModel(settingsService, synchronizer);
        using var engine = new CalibrationEngine(new CornerDetector(9, 6))
        {
            IntrinsicCalibrationFlags = initialFlags
        };

        try
        {
            // Live captures and saved sessions both dispatch through this method.
            var runMethod = typeof(MainWindowViewModel).GetMethod(
                "RunCalibrationForMode", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var exception = Assert.Throws<TargetInvocationException>(
                () => runMethod.Invoke(viewModel, [CalibrationMode.Auto, engine]));
            Assert.IsType<InvalidOperationException>(exception.InnerException);
            Assert.Equal(expectedFlags, engine.IntrinsicCalibrationFlags);
            Assert.Equal(0.75, engine.RectificationAlpha);
        }
        finally
        {
            viewModel.Cleanup();
            File.Delete(settingsPath);
        }
    });

    [Theory]
    [InlineData(-0.5)]
    [InlineData(-2)]
    [InlineData(1.1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public Task RectificationAlpha_InvalidValue_BlocksSaveAndCalibration(double alpha)
    => UiTest.Run(() =>
    {
        var service = new SettingsService(Path.Combine(Path.GetTempPath(), $"LuminaCalib_Options_{Guid.NewGuid():N}.json"));
        using var viewModel = new SettingsViewModel(service);
        service.Settings.LeftCameraUrl = "rtsp://192.168.1.100/stream";
        service.Settings.RightCameraUrl = "rtsp://192.168.1.101/stream";
        Assert.True(viewModel.CanSave);

        service.Settings.RectificationAlpha = alpha;
        Assert.False(viewModel.CanSave);
        Assert.NotNull(viewModel.RectificationAlphaError);
        using var engine = new CalibrationEngine(new CornerDetector(9, 6));
        Assert.Throws<ArgumentOutOfRangeException>(() => engine.RectificationAlpha = alpha);
    });

    [Fact]
    public Task SettingsView_OptionsEditedAndApplied_PersistForNextCalibration()
    => UiTest.Run(async () =>
    {
        var settingsPath = Path.Combine(Path.GetTempPath(), $"LuminaCalib_Options_{Guid.NewGuid():N}.json");
        var service = new SettingsService(settingsPath);
        using var viewModel = new SettingsViewModel(service) { SelectedTabIndex = 2 };
        var view = new SettingsView { DataContext = viewModel };
        var window = new Window { Content = view, Width = 800, Height = 1000 };
        window.Show();
        try
        {
            var rational = view.FindControl<CheckBox>("UseRationalModelCheckBox")!;
            var alpha = view.FindControl<NumericUpDown>("RectificationAlphaInput")!;
            rational.IsChecked = true;
            alpha.Value = 1;
            Assert.True(service.Settings.UseRationalModel);
            Assert.Equal(1, service.Settings.RectificationAlpha);

            await viewModel.ApplyCommand.ExecuteAsync(null);
            var reloaded = new SettingsService(settingsPath).Settings;
            Assert.True(reloaded.UseRationalModel);
            Assert.Equal(1, reloaded.RectificationAlpha);

            viewModel.ResetCommand.Execute(null);
            Assert.False(service.Settings.UseRationalModel);
            Assert.Equal(0, service.Settings.RectificationAlpha);
            Assert.False(rational.IsChecked);
            Assert.Equal(0m, alpha.Value);
        }
        finally
        {
            window.Close();
            File.Delete(settingsPath);
        }
    });

    [Fact]
    public void Load_LegacySettings_UsesCropAndKeepsSelectedDistortionModel()
    {
        var settingsPath = Path.Combine(Path.GetTempPath(), $"LuminaCalib_Options_{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(settingsPath, "{\"useRationalModel\":true}");
            var settings = new SettingsService(settingsPath).Settings;
            Assert.True(settings.UseRationalModel);
            Assert.Equal(0, settings.RectificationAlpha);
        }
        finally
        {
            File.Delete(settingsPath);
        }
    }
}
