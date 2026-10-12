using System.Drawing;
using Emgu.CV;
using Emgu.CV.Aruco;
using Emgu.CV.Util;
using LuminaCalib.Calibration;
using LuminaCalib.Models;
using LuminaCalib.Services;
using Xunit;

namespace LuminaCalib.Tests;

public sealed class CharucoBoardGeneratorTests
{
    [Theory]
    [InlineData(0.5f)]
    [InlineData(0.51f)]
    [InlineData(0.5105f)]
    [InlineData(0.73345f)]
    [InlineData(0.9f)]
    public void GenerateCharucoBoard_MarkerSliderValue_GeneratesBoard(float ratio)
    {
        using var generator = new CharucoBoardGenerator();
        var settings = new BoardGeneratorSettings
        {
            SquaresX = 9,
            SquaresY = 6,
            SquareLength = 30f,
            MarkerLengthRatio = ratio,
            Dictionary = CharucoDictionary.Dict6x6_250
        };

        using var image = generator.GenerateCharucoBoard(settings);

        Assert.Equal(3286, image.Width);
        Assert.Equal(2224, image.Height);
    }

    [Theory]
    [InlineData(9, 6, 0.51f)]
    [InlineData(5, 7, 0.51f)]
    [InlineData(9, 6, 0.73345f)]
    [InlineData(5, 7, 0.73345f)]
    public void GenerateCharucoBoard_ValidSettings_DetectionObjectsRecognizeAllCorners(
        int squaresX, int squaresY, float ratio)
    {
        using var generator = new CharucoBoardGenerator();
        using var image = generator.GenerateCharucoBoard(new BoardGeneratorSettings
        {
            SquaresX = squaresX,
            SquaresY = squaresY,
            SquareLength = 30f,
            MarkerLengthRatio = ratio,
            Dictionary = CharucoDictionary.Dict6x6_250
        });
        var objects = generator.GetDetectionObjects();
        Assert.NotNull(objects);
        using var parameters = new CharucoParameters(2, false, true);
        using var detector = new CharucoDetector(objects.Value.Board, parameters,
            DetectorParameters.GetDefault(), new RefineParameters(10f, 3f, true));
        using var corners = new VectorOfPointF();
        using var ids = new VectorOfInt();

        EmguCharucoDetection.DetectBoard(detector, image, corners, ids);

        int expectedCount = (squaresX - 1) * (squaresY - 1);
        Assert.Equal(expectedCount, corners.Size);
        Assert.Equal(Enumerable.Range(0, expectedCount), ids.ToArray().Order());
        var points = corners.ToArray();
        var cornerIds = ids.ToArray();
        for (int i = 0; i < points.Length; i++)
        {
            int expectedX = 50 + (cornerIds[i] % (squaresX - 1) + 1) * 354;
            int expectedY = 50 + (cornerIds[i] / (squaresX - 1) + 1) * 354;
            Assert.InRange(points[i].X, expectedX - 1, expectedX + 1);
            Assert.InRange(points[i].Y, expectedY - 1, expectedY + 1);
        }
    }

    [Fact]
    public void GenerateChessboard_ValidSettings_PreservesWhiteMargins()
    {
        using var generator = new CharucoBoardGenerator();
        using var image = generator.GenerateChessboard(new BoardGeneratorSettings
        {
            SquaresX = 3, SquaresY = 3, SquareLength = 5f, Dpi = 72, Margin = 200
        });

        AssertWhiteRegion(image, new Rectangle(0, 242, 442, 200));
        AssertWhiteRegion(image, new Rectangle(242, 0, 200, 442));
    }

    [Theory]
    [InlineData(9, 6, 30f, 300, 50, 3286, 2224)]
    [InlineData(5, 7, 30f, 100, 20, 630, 866)]
    [InlineData(3, 3, 5f, 72, 200, 442, 442)]
    [InlineData(9, 11, 5f, 96, 10, 182, 218)]
    [InlineData(9, 6, 30f, 300, 0, 3186, 2124)]
    public void GenerateCharucoBoard_ValidSettings_PreservesCellSizeAndAddsWhiteMargins(
        int squaresX, int squaresY, float squareLength, int dpi, int margin,
        int expectedWidth, int expectedHeight)
    {
        var settings = new BoardGeneratorSettings
        {
            SquaresX = squaresX,
            SquaresY = squaresY,
            SquareLength = squareLength,
            MarkerLengthRatio = 0.73f,
            Dictionary = CharucoDictionary.Dict6x6_250,
            Dpi = dpi,
            Margin = margin
        };
        using var generator = new CharucoBoardGenerator();

        using var image = generator.GenerateCharucoBoard(settings);

        Assert.Equal(expectedWidth, image.Width);
        Assert.Equal(expectedHeight, image.Height);
        Assert.Equal(1, image.NumberOfChannels);
        Assert.Equal(0, System.Runtime.InteropServices.Marshal.ReadByte(
            image.DataPointer, margin * image.Step + margin));
        if (margin > 0)
        {
            AssertWhiteRegion(image, new Rectangle(0, 0, image.Width, margin));
            AssertWhiteRegion(image, new Rectangle(0, image.Height - margin, image.Width, margin));
            AssertWhiteRegion(image, new Rectangle(0, 0, margin, image.Height));
            AssertWhiteRegion(image, new Rectangle(image.Width - margin, 0, margin, image.Height));
        }
    }

    private static void AssertWhiteRegion(Mat image, Rectangle region)
    {
        using var border = new Mat(image, region);
        Assert.Equal(region.Width * region.Height, CvInvoke.CountNonZero(border));
    }
}
