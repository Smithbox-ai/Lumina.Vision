using System.Drawing;
using System.Runtime.InteropServices;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using LuminaCalib.Calibration;
using LuminaCalib.Models;
using static Emgu.CV.StereoSGBM;
using Xunit;

namespace LuminaCalib.Tests;

public sealed class DepthMapProcessorTests
{
    /// <summary>
    /// Создаёт синтетическую стереопару со смещённым прямоугольником.
    /// </summary>
    private static (Mat left, Mat right) CreateSyntheticStereoPair()
    {
        var left = new Mat(240, 320, DepthType.Cv8U, 1);
        var right = new Mat(240, 320, DepthType.Cv8U, 1);
        left.SetTo(new MCvScalar(0));
        right.SetTo(new MCvScalar(0));

        CvInvoke.Rectangle(left, new Rectangle(80, 60, 100, 80), new MCvScalar(200), -1);
        CvInvoke.Rectangle(right, new Rectangle(70, 60, 100, 80), new MCvScalar(200), -1);

        return (left, right);
    }

    [Fact]
    public void ComputeDisparity_WithDefaultSettings_ReturnsNonEmptyMat()
    {
        var (left, right) = CreateSyntheticStereoPair();
        using (left)
        using (right)
        {
            var settings = new DepthMapSettings();
            using var disparity = DepthMapProcessor.ComputeDisparity(left, right, settings);

            Assert.False(disparity.IsEmpty);
            Assert.Equal(DepthType.Cv16S, disparity.Depth);
        }
    }

    [Fact]
    public void ComputeDisparity_WithWlsFilter_ReturnsNonEmptyMat()
    {
        var (left, right) = CreateSyntheticStereoPair();
        using (left)
        using (right)
        {
            var settings = new DepthMapSettings { UseWlsFilter = true };
            using var disparity = DepthMapProcessor.ComputeDisparity(left, right, settings);

            Assert.False(disparity.IsEmpty);
            Assert.Equal(DepthType.Cv16S, disparity.Depth);
        }
    }

    [Fact]
    public void ComputeDisparity_WithoutWlsFilter_ReturnsNonEmptyMat()
    {
        var (left, right) = CreateSyntheticStereoPair();
        using (left)
        using (right)
        {
            var settings = new DepthMapSettings { UseWlsFilter = false };
            using var disparity = DepthMapProcessor.ComputeDisparity(left, right, settings);

            Assert.False(disparity.IsEmpty);
            Assert.Equal(DepthType.Cv16S, disparity.Depth);
        }
    }

    [Fact]
    public void ComputeDisparity_WithMorphClosing_ReturnsNonEmptyMat()
    {
        var (left, right) = CreateSyntheticStereoPair();
        using (left)
        using (right)
        {
            var settings = new DepthMapSettings
            {
                UseWlsFilter = false,
                UseMorphologicalClosing = true,
                MorphKernelSize = 5
            };
            using var disparity = DepthMapProcessor.ComputeDisparity(left, right, settings);

            Assert.False(disparity.IsEmpty);
            Assert.Equal(DepthType.Cv16S, disparity.Depth);
        }
    }

    [Fact]
    public void ColorizeDisparity_ReturnsCorrectFormat()
    {
        // Создаём синтетическую карту диспаратности (CV_16S)
        using var disparity = new Mat(240, 320, DepthType.Cv16S, 1);
        disparity.SetTo(new MCvScalar(0));
        // Заполняем область ненулевыми значениями (имитация реальной диспаратности × 16)
        CvInvoke.Rectangle(disparity, new Rectangle(80, 60, 100, 80), new MCvScalar(64 * 16), -1);

        using var colorized = DepthMapProcessor.ColorizeDisparity(disparity, DepthColormap.Turbo);

        Assert.False(colorized.IsEmpty);
        Assert.Equal(DepthType.Cv8U, colorized.Depth);
        Assert.Equal(3, colorized.NumberOfChannels);
    }

    [Fact]
    public void ReprojectTo3D_ReturnsCorrectFormat()
    {
        // Синтетическая диспаратность
        using var disparity = new Mat(240, 320, DepthType.Cv16S, 1);
        disparity.SetTo(new MCvScalar(32 * 16));

        // Простая матрица Q (диагональная)
        var qData = new double[4, 4]
        {
            { 1, 0, 0, 0 },
            { 0, 1, 0, 0 },
            { 0, 0, 1, 0 },
            { 0, 0, 0, 1 }
        };
        using var Q = new Emgu.CV.Matrix<double>(qData);
        using var qMat = Q.Mat;

        using var points = DepthMapProcessor.ReprojectTo3D(disparity, qMat);

        Assert.False(points.IsEmpty);
        Assert.Equal(DepthType.Cv32F, points.Depth);
        Assert.Equal(3, points.NumberOfChannels);
    }

    [Fact]
    public void ComputeDisparity_InvalidNumDisparities_ThrowsArgumentException()
    {
        var (left, right) = CreateSyntheticStereoPair();
        using (left)
        using (right)
        {
            var settings = new DepthMapSettings { NumDisparities = 13 };

            Assert.Throws<ArgumentException>(() =>
                DepthMapProcessor.ComputeDisparity(left, right, settings));
        }
    }

    [Fact]
    public void ComputeDisparity_InvalidBlockSize_ThrowsArgumentException()
    {
        var (left, right) = CreateSyntheticStereoPair();
        using (left)
        using (right)
        {
            var settings = new DepthMapSettings { BlockSize = 4 };

            Assert.Throws<ArgumentException>(() =>
                DepthMapProcessor.ComputeDisparity(left, right, settings));
        }
    }

    [Fact]
    public void IsCudaAvailable_DoesNotThrow()
    {
        // Просто проверяем, что свойство не бросает исключений
        _ = DepthMapProcessor.IsCudaAvailable;
    }

    [Fact]
    public void ComputeDisparityCuda_WhenNoCuda_ReturnsEmptyMat()
    {
        // На машинах без CUDA должен вернуть пустой Mat (graceful fallback)
        var (left, right) = CreateSyntheticStereoPair();
        using (left)
        using (right)
        {
            var settings = new DepthMapSettings { NumDisparities = 128 };
            using var result = DepthMapProcessor.ComputeDisparityCuda(left, right, settings);

            // Either empty (no CUDA) or valid disparity (CUDA available)
            if (!DepthMapProcessor.IsCudaAvailable)
                Assert.True(result.IsEmpty);
            else
                Assert.Equal(DepthType.Cv16S, result.Depth);
        }
    }

    [Fact]
    public void ComputeDisparity_WithDownscale_ReturnsOriginalSize()
    {
        var (left, right) = CreateSyntheticStereoPair();
        using (left)
        using (right)
        {
            var settings = new DepthMapSettings
            {
                DisplayScale = 0.5,
                UseWlsFilter = false
            };
            using var disparity = DepthMapProcessor.ComputeDisparity(left, right, settings);

            Assert.Equal(left.Height, disparity.Height);
            Assert.Equal(left.Width, disparity.Width);
        }
    }

    [Fact]
    public void ComputeDisparity_BlockSize13_Sgbm3Way_ThrowsArgumentException()
    {
        var (left, right) = CreateSyntheticStereoPair();
        using (left)
        using (right)
        {
            var settings = new DepthMapSettings { BlockSize = 13, SgbmMode = SgbmMode.Sgbm3Way };

            Assert.Throws<ArgumentException>(() =>
                DepthMapProcessor.ComputeDisparity(left, right, settings));
        }
    }

    [Fact]
    public void ComputeDisparity_BlockSize13_HH_DoesNotThrow()
    {
        var (left, right) = CreateSyntheticStereoPair();
        using (left)
        using (right)
        {
            var settings = new DepthMapSettings { BlockSize = 13, SgbmMode = SgbmMode.HH };

            // HH mode supports larger BlockSize — validation should pass
            using var disparity = DepthMapProcessor.ComputeDisparity(left, right, settings);
            Assert.False(disparity.IsEmpty);
        }
    }

    [Fact]
    public void ReprojectTo3D_WithKnownDisparity_ProducesCorrectZ()
    {
        // Stereo system: baseline=60mm, focal_length=500px, cx=160, cy=120
        // Q matrix from StereoRectify with CALIB_ZERO_DISPARITY (cx == cx'):
        //   [ 1   0    0       -cx       ]
        //   [ 0   1    0       -cy       ]
        //   [ 0   0    0        f        ]
        //   [ 0   0  -1/Tx   (cx-cx')/Tx ]
        // Tx = -baseline = -60, so Q[3,2] = -1/(-60) = 1/60
        // With CALIB_ZERO_DISPARITY: cx == cx', so Q[3,3] = 0
        double f = 500.0;
        double cx = 160.0;
        double cy = 120.0;
        double baseline = 60.0;
        double tx = -baseline;

        var qData = new double[4, 4]
        {
            { 1, 0, 0, -cx },
            { 0, 1, 0, -cy },
            { 0, 0, 0,  f  },
            { 0, 0, -1.0 / tx, 0 }
        };
        using var Q = new Emgu.CV.Matrix<double>(qData);
        using var qMat = Q.Mat;

        // Use CV_32F disparity to avoid ×16 ambiguity and handleMissingValues issues.
        // ReprojectImageTo3D uses the raw float value directly.
        int disparityPx = 32;
        using var disparity = new Mat(10, 10, DepthType.Cv32F, 1);
        // Set background to -1 (invalid) so handleMissingValues treats only those as outliers
        disparity.SetTo(new MCvScalar(-1.0));
        // Set a 3×3 region around (5,5) to our test disparity
        CvInvoke.Rectangle(disparity, new Rectangle(4, 4, 3, 3), new MCvScalar(disparityPx), -1);

        using var points = DepthMapProcessor.ReprojectTo3D(disparity, qMat);

        Assert.False(points.IsEmpty);
        Assert.Equal(DepthType.Cv32F, points.Depth);
        Assert.Equal(3, points.NumberOfChannels);

        // Read back Z from pixel (5, 5)
        int row = 5, col = 5;
        int pixelIndex = (row * points.Cols + col) * 3; // 3 channels: X, Y, Z
        var data = new float[points.Rows * points.Cols * 3];
        Marshal.Copy(points.DataPointer, data, 0, data.Length);
        float z = data[pixelIndex + 2];

        // Expected Z = f * baseline / disparity = 500 * 60 / 32 = 937.5
        double expectedZ = f * baseline / disparityPx;
        Assert.InRange(z, expectedZ - 1.0, expectedZ + 1.0);
    }

    [Fact]
    public void ComputeDisparity_WithDownscale_DoesNotRescaleValues()
    {
        // Create a larger, textured stereo pair so SGBM produces valid results at both scales
        using var left = new Mat(480, 640, DepthType.Cv8U, 1);
        using var right = new Mat(480, 640, DepthType.Cv8U, 1);
        left.SetTo(new MCvScalar(0));
        right.SetTo(new MCvScalar(0));

        // Add a large textured region with clear horizontal shift (20px)
        // Use multiple brightness levels to give SGBM enough texture to match
        for (int i = 0; i < 5; i++)
        {
            int y = 100 + i * 40;
            int brightness = 100 + i * 30;
            CvInvoke.Rectangle(left, new Rectangle(80, y, 400, 30), new MCvScalar(brightness), -1);
            CvInvoke.Rectangle(right, new Rectangle(60, y, 400, 30), new MCvScalar(brightness), -1);
        }

        // Full resolution disparity
        var settingsFull = new DepthMapSettings
        {
            DisplayScale = 1.0,
            UseWlsFilter = false,
            UseMorphologicalClosing = false
        };
        using var disparityFull = DepthMapProcessor.ComputeDisparity(left, right, settingsFull);

        // Find max positive value in full-res disparity
        var fullData = new short[disparityFull.Rows * disparityFull.Cols];
        Marshal.Copy(disparityFull.DataPointer, fullData, 0, fullData.Length);
        short fullMax = 0;
        foreach (var v in fullData)
            if (v > fullMax) fullMax = v;

        // Downscaled disparity (0.5)
        var settingsHalf = new DepthMapSettings
        {
            DisplayScale = 0.5,
            UseWlsFilter = false,
            UseMorphologicalClosing = false
        };
        using var disparityHalf = DepthMapProcessor.ComputeDisparity(left, right, settingsHalf);

        // Find max positive value in downscaled disparity
        var halfData = new short[disparityHalf.Rows * disparityHalf.Cols];
        Marshal.Copy(disparityHalf.DataPointer, halfData, 0, halfData.Length);
        short halfMax = 0;
        foreach (var v in halfData)
            if (v > halfMax) halfMax = v;

        // SGBM at half resolution produces ~half the disparity values.
        // After the fix (no ConvertTo rescaling), halfMax should be noticeably
        // smaller than fullMax (~0.5×). Before the fix, ConvertTo multiplied
        // by 1/0.5=2.0, bringing halfMax back to ~fullMax level.
        // Assert that halfMax is genuinely smaller, confirming no artificial inflation.
        Assert.True(fullMax > 0, "Full-res disparity should have positive values");
        Assert.True(halfMax > 0, "Half-res disparity should have positive values");
        Assert.True(halfMax < (short)(fullMax * 0.75),
            $"Downscaled disparity max ({halfMax}) should be less than 0.75× full-res max ({fullMax}), " +
            $"indicating no artificial rescaling. Ratio: {(double)halfMax / fullMax:F2}");
    }

    [Theory]
    [InlineData(SgbmMode.Sgbm, Mode.SGBM)]
    [InlineData(SgbmMode.HH, Mode.HH)]
    [InlineData(SgbmMode.Sgbm3Way, (Mode)2)]
    [InlineData(SgbmMode.HH4, (Mode)3)]
    public void MapSgbmMode_AllModes_ReturnExpectedValues(SgbmMode input, Mode expected)
    {
        var result = DepthMapProcessor.MapSgbmMode(input);
        Assert.Equal(expected, result);
    }
}
