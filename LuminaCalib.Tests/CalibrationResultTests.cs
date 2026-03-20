using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using LuminaCalib.Models;
using Xunit;

namespace LuminaCalib.Tests;

public sealed class CalibrationResultTests
{
    [Fact]
    public void SaveLoad_WithPerViewErrorsAndFilteredCount_RoundTrips()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"LuminaCalib_Phase2_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var xmlPath = Path.Combine(dir, "calib.xml");

        try
        {
            using var original = CreateValidCalibrationResult();
            original.PerViewErrors = [0.11, 0.22, 0.33];
            original.FilteredOutPairCount = 2;

            original.SaveToXml(xmlPath);

            using var loaded = CalibrationResult.LoadFromXml(xmlPath);

            Assert.Equal(2, loaded.FilteredOutPairCount);
            Assert.Equal([0.11, 0.22, 0.33], loaded.PerViewErrors, new DoubleToleranceComparer(1e-9));
        }
        finally
        {
            TryDeleteDirectory(dir);
        }
    }

    [Fact]
    public void SaveLoad_WithIntrinsicErrors_RoundTrips()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"LuminaCalib_RT_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var xmlPath = Path.Combine(dir, "calib.xml");

        try
        {
            using var original = CreateValidCalibrationResult();
            original.LeftIntrinsicError = 0.234;
            original.RightIntrinsicError = 0.567;
            original.ReprojectionError = 0.42;
            original.ImageSize = new System.Drawing.Size(1920, 1080);
            original.ImagePairCount = 15;

            original.SaveToXml(xmlPath);

            using var loaded = CalibrationResult.LoadFromXml(xmlPath);

            Assert.Equal(original.LeftIntrinsicError, loaded.LeftIntrinsicError, 0.001);
            Assert.Equal(original.RightIntrinsicError, loaded.RightIntrinsicError, 0.001);
            Assert.Equal(original.ReprojectionError, loaded.ReprojectionError, 0.001);
            Assert.Equal(original.ImageSize, loaded.ImageSize);
            Assert.Equal(original.ImagePairCount, loaded.ImagePairCount);
        }
        finally
        {
            TryDeleteDirectory(dir);
        }
    }

    [Fact]
    public void LoadLegacyFile_WithoutIntrinsicErrors_DefaultsToZero()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"LuminaCalib_Legacy_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var xmlPath = Path.Combine(dir, "calib.xml");

        try
        {
            using var original = CreateValidCalibrationResult();
            original.LeftIntrinsicError = 1.5;
            original.RightIntrinsicError = 2.5;
            original.ReprojectionError = 0.8;
            original.ImagePairCount = 20;
            original.ImageSize = new System.Drawing.Size(1280, 720);

            original.SaveToXml(xmlPath);

            // Rewrite the sidecar text file without the intrinsic error lines
            var txtPath = Path.ChangeExtension(xmlPath, ".txt");
            var lines = File.ReadAllLines(txtPath)
                .Where(l => !l.StartsWith("LeftIntrinsicError=") && !l.StartsWith("RightIntrinsicError="))
                .ToArray();
            File.WriteAllLines(txtPath, lines);

            using var loaded = CalibrationResult.LoadFromXml(xmlPath);

            Assert.Equal(0.0, loaded.LeftIntrinsicError);
            Assert.Equal(0.0, loaded.RightIntrinsicError);
            // Other metadata should still load correctly
            Assert.Equal(0.8, loaded.ReprojectionError, 0.001);
            Assert.Equal(20, loaded.ImagePairCount);
        }
        finally
        {
            TryDeleteDirectory(dir);
        }
    }

    [Theory]
    [InlineData(0.0, CalibrationQuality.Excellent)]
    [InlineData(0.3, CalibrationQuality.Excellent)]
    [InlineData(0.49, CalibrationQuality.Excellent)]
    [InlineData(0.5, CalibrationQuality.Good)]
    [InlineData(0.75, CalibrationQuality.Good)]
    [InlineData(0.99, CalibrationQuality.Good)]
    [InlineData(1.0, CalibrationQuality.Poor)]
    [InlineData(2.5, CalibrationQuality.Poor)]
    public void Quality_ReturnsCorrectRating(double error, CalibrationQuality expected)
    {
        using var result = CreateValidCalibrationResult();
        result.ReprojectionError = error;

        Assert.Equal(expected, result.Quality);
    }

    [Fact]
    public void SaveLoad_WithEpipolarYError_RoundTrips()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"LuminaCalib_Phase4_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var xmlPath = Path.Combine(dir, "calib.xml");

        try
        {
            using var original = CreateValidCalibrationResult();
            original.EpipolarYError = 0.42;

            original.SaveToXml(xmlPath);

            using var loaded = CalibrationResult.LoadFromXml(xmlPath);

            Assert.Equal(0.42, loaded.EpipolarYError, 0.001);
        }
        finally
        {
            TryDeleteDirectory(dir);
        }
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
        r1.SetTo(new MCvScalar(0));

        var r2 = new Mat(3, 3, DepthType.Cv64F, 1);
        r2.SetTo(new MCvScalar(0));

        var p1 = new Mat(3, 4, DepthType.Cv64F, 1);
        p1.SetTo(new MCvScalar(0));

        var p2 = new Mat(3, 4, DepthType.Cv64F, 1);
        p2.SetTo(new MCvScalar(0));

        var q = new Mat(4, 4, DepthType.Cv64F, 1);
        q.SetTo(new MCvScalar(0));

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
            Q = q,
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

    private sealed class DoubleToleranceComparer(double tolerance) : IEqualityComparer<double>
    {
        public bool Equals(double x, double y)
        {
            return Math.Abs(x - y) <= tolerance;
        }

        public int GetHashCode(double obj)
        {
            return 0;
        }
    }
}
