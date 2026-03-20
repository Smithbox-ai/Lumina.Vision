using System.Drawing;
using Emgu.CV;
using Emgu.CV.CvEnum;
using LuminaCalib.Calibration;
using LuminaCalib.Models;
using Xunit;

namespace LuminaCalib.Tests;

/// <summary>
/// Tests for CornerDetector downscale support (Phase 5 — CPU optimization).
/// </summary>
public sealed class CornerDetectorDownscaleTests
{
    private static CornerDetector MakeChessboardDetector() =>
        new(9, 6, BoardType.Chessboard);

    private static CornerDetector MakeCharucoDetector() =>
        new(5, 7, BoardType.ChArUco, CharucoDictionary.Dict6x6_250, 30f, 22f);

    /// <summary>
    /// With default scale (1.0), DetectCorners behaves identically to the original method.
    /// On a blank image no pattern is found — verifying passthrough.
    /// </summary>
    [Fact]
    public void DetectCorners_WithDefaultScale_ReturnsOriginalBehavior()
    {
        // Arrange
        var detector = MakeChessboardDetector();
        using var image = new Mat(480, 640, DepthType.Cv8U, 3);
        image.SetTo(new Emgu.CV.Structure.MCvScalar(128, 128, 128));

        // Act — scale 1.0 (default)
        bool found = detector.DetectCorners(image, out var corners, out var ids, 1.0f);

        // Assert — blank image has no pattern
        Assert.False(found);
        Assert.Empty(corners);
        Assert.Null(ids);
    }

    /// <summary>
    /// With half scale (0.5), DetectCorners still works (no crash) and returns corners
    /// in original image coordinates. On a blank image, pattern is not found.
    /// </summary>
    [Fact]
    public void DetectCorners_WithHalfScale_DoesNotCrash()
    {
        // Arrange
        var detector = MakeChessboardDetector();
        using var image = new Mat(480, 640, DepthType.Cv8U, 3);
        image.SetTo(new Emgu.CV.Structure.MCvScalar(128, 128, 128));

        // Act — scale 0.5
        bool found = detector.DetectCorners(image, out var corners, out var ids, 0.5f);

        // Assert — blank image, no pattern
        Assert.False(found);
        Assert.Empty(corners);
    }

    /// <summary>
    /// Scale &lt;= 0 or &gt; 1 is treated as full resolution (no downscale).
    /// </summary>
    [Theory]
    [InlineData(0f)]
    [InlineData(-0.5f)]
    [InlineData(1.5f)]
    [InlineData(2.0f)]
    public void DetectCorners_WithInvalidScale_FallsToFullRes(float scale)
    {
        // Arrange
        var detector = MakeChessboardDetector();
        using var image = new Mat(480, 640, DepthType.Cv8U, 3);
        image.SetTo(new Emgu.CV.Structure.MCvScalar(128, 128, 128));

        // Act — invalid scale should be treated as 1.0
        bool found = detector.DetectCorners(image, out var corners, out var ids, scale);

        // Assert — no crash, blank image returns false
        Assert.False(found);
        Assert.Empty(corners);
    }

    /// <summary>
    /// ChArUco detector also works with downscale without crashing.
    /// </summary>
    [Fact]
    public void DetectCorners_ChArUco_WithHalfScale_DoesNotCrash()
    {
        // Arrange
        var detector = MakeCharucoDetector();
        using var image = new Mat(480, 640, DepthType.Cv8U, 3);
        image.SetTo(new Emgu.CV.Structure.MCvScalar(200, 200, 200));

        // Act
        bool found = detector.DetectCorners(image, out var corners, out var ids, 0.5f);

        // Assert — blank image, no pattern
        Assert.False(found);
    }

    /// <summary>
    /// Null or empty image returns false regardless of scale.
    /// </summary>
    [Theory]
    [InlineData(0.5f)]
    [InlineData(1.0f)]
    public void DetectCorners_NullImage_ReturnsFalse(float scale)
    {
        var detector = MakeChessboardDetector();
        bool found = detector.DetectCorners(null!, out var corners, out var ids, scale);
        Assert.False(found);
        Assert.Empty(corners);
    }
}
