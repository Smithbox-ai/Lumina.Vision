using System.Linq;
using LuminaCalib.Calibration;
using LuminaCalib.Models;
using System.Drawing;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using Emgu.CV.Util;
using Xunit;

namespace LuminaCalib.Tests;

public sealed class CalibrationEngineTests
{
    [Fact]
    public void ComputePerViewReprojectionErrors_ReturnsOneRmsValuePerView()
    {
        using var objectView1 = new VectorOfPoint3D32F(
        [
            new MCvPoint3D32f(0f, 0f, 1f),
            new MCvPoint3D32f(1f, 0f, 1f),
            new MCvPoint3D32f(0f, 1f, 1f),
            new MCvPoint3D32f(1f, 1f, 1f)
        ]);
        using var objectView2 = new VectorOfPoint3D32F(
        [
            new MCvPoint3D32f(0f, 0f, 1f),
            new MCvPoint3D32f(2f, 0f, 1f),
            new MCvPoint3D32f(0f, 2f, 1f),
            new MCvPoint3D32f(2f, 2f, 1f)
        ]);

        using var imageView1 = new VectorOfPointF(
        [
            new PointF(0f, 0f),
            new PointF(1f, 0f),
            new PointF(0f, 1f),
            new PointF(1f, 1f)
        ]);
        using var imageView2 = new VectorOfPointF(
        [
            new PointF(0f, 0f),
            new PointF(2f, 0f),
            new PointF(0f, 2f),
            new PointF(2f, 2f)
        ]);

        using var objectPoints = new VectorOfVectorOfPoint3D32F([objectView1, objectView2]);
        using var imagePoints = new VectorOfVectorOfPointF([imageView1, imageView2]);

        using var cameraMatrix = new Mat(3, 3, DepthType.Cv64F, 1);
        CvInvoke.SetIdentity(cameraMatrix, new MCvScalar(1.0));
        using var distCoeffs = new Mat(1, 5, DepthType.Cv64F, 1);
        distCoeffs.SetTo(new MCvScalar(0));

        using var rvecs = new VectorOfMat([CreateZeroVector3x1(), CreateZeroVector3x1()]);
        using var tvecs = new VectorOfMat([CreateZeroVector3x1(), CreateZeroVector3x1()]);

        var perView = CalibrationEngine.ComputePerViewReprojectionErrors(
            objectPoints,
            imagePoints,
            cameraMatrix,
            distCoeffs,
            rvecs,
            tvecs);

        Assert.Equal(2, perView.Length);
        Assert.All(perView, value => Assert.InRange(value, 0.0, 1e-9));
    }

    [Fact]
    public void SelectInlierPairIndices_ExcludesPairIfEitherSideIsOutlier()
    {
        var leftErrors = new[] { 0.30, 0.31, 0.33, 0.32, 0.34, 4.20 };
        var rightErrors = new[] { 0.31, 0.32, 0.30, 0.35, 5.10, 0.33 };

        var inliers = CalibrationEngine.SelectInlierPairIndices(leftErrors, rightErrors);

        Assert.Equal([0, 1, 2, 3], inliers);
    }

    [Fact]
    public void SelectPairsForStereoCalibration_WhenTooFewInliers_FallsBackToAllPairs()
    {
        var leftErrors = new[] { 0.30, 0.31, 0.29, 0.32, 0.28, 0.33, 0.30, 0.29, 4.2, 4.4 };
        var rightErrors = new[] { 0.31, 0.30, 0.32, 0.29, 0.33, 0.28, 0.31, 0.30, 4.0, 4.5 };

        var selection = CalibrationEngine.SelectPairsForStereoCalibration(leftErrors, rightErrors);

        Assert.Equal(10, selection.InlierIndices.Length);
        Assert.Equal(0, selection.FilteredOutPairCount);
        Assert.Equal([0, 1, 2, 3, 4, 5, 6, 7, 8, 9], selection.InlierIndices);
    }

    [Fact]
    public void StereoRectifyFlags_IncludeZeroDisparityFlag()
    {
        Assert.True((((int)CalibrationEngine.StereoRectifyFlags) & 0x400) != 0);
    }

    [Fact]
    public void StereoRectifyFlags_MatchExpectedCombinedValue()
    {
        Assert.Equal((StereoRectifyType)(((int)StereoRectifyType.Default) | 0x400), CalibrationEngine.StereoRectifyFlags);
    }

    [Fact]
    public void OnStageChanged_IsAvailable()
    {
        var cornerDetector = new CornerDetector(9, 6, BoardType.Chessboard, CharucoDictionary.Dict6x6_250, 25f, 25f * 0.73f);
        using var engine = new CalibrationEngine(cornerDetector);

        var stages = new List<string>();
        engine.OnStageChanged += stage => stages.Add(stage);

        // Verify subscription succeeded without throwing — basic smoke test
        Assert.NotNull(engine);
    }

    [Fact]
    public void CalibrateFullStereo_WithInsufficientData_ThrowsInvalidOperation()
    {
        var cornerDetector = new CornerDetector(9, 6, BoardType.Chessboard, CharucoDictionary.Dict6x6_250, 25f, 25f * 0.73f);
        using var engine = new CalibrationEngine(cornerDetector);

        var ex = Assert.Throws<InvalidOperationException>(() => engine.CalibrateFullStereo());
        Assert.Contains("At least 10", ex.Message);
    }

    [Fact]
    public void CalibrateSingleCamera_WithInsufficientData_ThrowsInvalidOperation()
    {
        var cornerDetector = new CornerDetector(9, 6, BoardType.Chessboard, CharucoDictionary.Dict6x6_250, 25f, 25f * 0.73f);
        using var engine = new CalibrationEngine(cornerDetector);

        var ex = Assert.Throws<InvalidOperationException>(() => engine.CalibrateSingleCamera(true));
        Assert.Contains("At least 10", ex.Message);
    }

    /// <summary>
    /// Chessboard with pattern 9×6 (inner corners) should produce 9*6 = 54 object points.
    /// </summary>
    [Fact]
    public void GenerateObjectPoints_Chessboard_Returns_InnerCornerCount()
    {
        var detector = new CornerDetector(9, 6, BoardType.Chessboard, CharucoDictionary.Dict6x6_250, 25f, 25f * 0.73f);
        var pts = detector.GenerateObjectPoints();

        // 9×6 inner corners
        Assert.Equal(54, pts.Length);
    }

    /// <summary>
    /// ChArUco with pattern 9×6 (cells) should produce (9-1)*(6-1) = 40 object points.
    /// </summary>
    [Fact]
    public void GenerateObjectPoints_ChArUco_Returns_InnerCornerCount()
    {
        var detector = new CornerDetector(9, 6, BoardType.ChArUco, CharucoDictionary.Dict6x6_250, 25f, 18f);
        var pts = detector.GenerateObjectPoints();

        // ChArUco 9×6 → (9-1)*(6-1) = 40 charuco corners
        Assert.Equal(40, pts.Length);
    }

    /// <summary>
    /// Verifies board-type-specific semantics:
    /// Chessboard uses inner corners (W*H), ChArUco uses cells ((W-1)*(H-1)).
    /// </summary>
    [Theory]
    [InlineData(9, 6)]
    [InlineData(5, 7)]
    [InlineData(11, 8)]
    public void GenerateObjectPoints_UsesBoardTypeSpecificSemantics(int w, int h)
    {
        var chess = new CornerDetector(w, h, BoardType.Chessboard, CharucoDictionary.Dict6x6_250, 30f, 22f);
        var charuco = new CornerDetector(w, h, BoardType.ChArUco, CharucoDictionary.Dict6x6_250, 30f, 22f);

        var chessPts = chess.GenerateObjectPoints();
        var charucoPts = charuco.GenerateObjectPoints();

        Assert.Equal(w * h, chessPts.Length);
        Assert.Equal((w - 1) * (h - 1), charucoPts.Length);
    }

    [Fact]
    public void IntrinsicCalibrationFlags_DefaultsToCalibTypeDefault()
    {
        var cornerDetector = new CornerDetector(9, 6, BoardType.Chessboard, CharucoDictionary.Dict6x6_250, 25f, 25f * 0.73f);
        using var engine = new CalibrationEngine(cornerDetector);

        Assert.Equal(CalibType.Default, engine.IntrinsicCalibrationFlags);
    }

    [Fact]
    public void IntrinsicCalibrationFlags_CanBeSetToRationalModel()
    {
        var cornerDetector = new CornerDetector(9, 6, BoardType.Chessboard, CharucoDictionary.Dict6x6_250, 25f, 25f * 0.73f);
        using var engine = new CalibrationEngine(cornerDetector);

        engine.IntrinsicCalibrationFlags = CalibType.RationalModel;

        Assert.Equal(CalibType.RationalModel, engine.IntrinsicCalibrationFlags);
    }

    [Fact]
    public void ComputePerViewEpipolarYErrors_ReturnsOneValuePerView()
    {
        int w = 100, h = 100;
        using var mapX = CreateIdentityMapX(w, h);
        using var mapY = CreateIdentityMapY(w, h);

        PointF[][] leftPts = [
            [new(10, 20), new(30, 40)],
            [new(50, 60), new(70, 80)]
        ];
        PointF[][] rightPts = [
            [new(15, 22), new(35, 42)],  // Y offset = 2
            [new(55, 60), new(75, 80)]   // Y offset = 0
        ];

        var errors = CalibrationValidator.ComputePerViewEpipolarYErrors(
            leftPts, rightPts, mapX, mapY, mapX, mapY);

        Assert.Equal(2, errors.Length);
        Assert.InRange(errors[0], 1.99, 2.01);  // view 0: avg |Δy| = 2
        Assert.InRange(errors[1], 0.0, 0.01);   // view 1: avg |Δy| = 0
    }

    [Fact]
    public void IterativeConstants_HaveExpectedValues()
    {
        // Verify the iterative recalibration constants are reasonable
        // Access via reflection since they're private
        var engineType = typeof(CalibrationEngine);
        var maxIterField = engineType.GetField("MaxCalibrationIterations",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var minPairsField = engineType.GetField("MinPairsFloor",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var thresholdField = engineType.GetField("IterativeYErrorThreshold",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        Assert.NotNull(maxIterField);
        Assert.NotNull(minPairsField);
        Assert.NotNull(thresholdField);

        Assert.Equal(3, (int)maxIterField!.GetValue(null)!);
        Assert.Equal(12, (int)minPairsField!.GetValue(null)!);
        Assert.Equal(1.0, (double)thresholdField!.GetValue(null)!);
    }

    [Fact]
    public void ComputeEpipolarYError_IdentityMaps_ReturnsZero()
    {
        int w = 100, h = 100;
        using var mapX = CreateIdentityMapX(w, h);
        using var mapY = CreateIdentityMapY(w, h);

        PointF[][] leftPts = [
            [new(10, 20), new(30, 40)],
            [new(50, 60), new(70, 80)]
        ];
        PointF[][] rightPts = [
            [new(15, 20), new(35, 40)],  // same Y, different X
            [new(55, 60), new(75, 80)]
        ];

        double yError = CalibrationValidator.ComputeEpipolarYError(
            leftPts, rightPts, mapX, mapY, mapX, mapY);

        Assert.InRange(yError, 0.0, 1e-6);
    }

    [Fact]
    public void ComputeEpipolarYError_WithKnownYOffset_ReturnsCorrectValue()
    {
        int w = 100, h = 100;
        using var mapX = CreateIdentityMapX(w, h);
        using var mapY = CreateIdentityMapY(w, h);

        // All points have Y-offset of 2.0 between left and right
        PointF[][] leftPts = [
            [new(10, 20), new(30, 40)]
        ];
        PointF[][] rightPts = [
            [new(15, 22), new(35, 42)]  // Y offset = +2
        ];

        double yError = CalibrationValidator.ComputeEpipolarYError(
            leftPts, rightPts, mapX, mapY, mapX, mapY);

        Assert.InRange(yError, 1.99, 2.01);
    }

    [Fact]
    public void RemapPoint_WithIdentityMaps_ReturnsOriginalPoint()
    {
        int w = 100, h = 100;
        using var mapX = CreateIdentityMapX(w, h);
        using var mapY = CreateIdentityMapY(w, h);

        var result = CalibrationValidator.RemapPoint(new PointF(25.5f, 30.7f), mapX, mapY);

        Assert.InRange(result.X, 25.4, 25.6);
        Assert.InRange(result.Y, 30.6, 30.8);
    }

    [Fact]
    public void Validate_PopulatesEpipolarYErrorAndFilteredOutPairCount()
    {
        using var result = CreateMinimalCalibrationResult();
        result.EpipolarYError = 0.45;
        result.FilteredOutPairCount = 3;
        result.ReprojectionError = 0.5;
        result.ImagePairCount = 15;

        var validation = CalibrationValidator.Validate(result);

        Assert.Equal(0.45, validation.EpipolarYError, 0.001);
        Assert.Equal(3, validation.FilteredOutPairCount);
    }

    [Fact]
    public void GetRecommendations_CriticalYError_ReturnsWarning()
    {
        var validation = new CalibrationValidation
        {
            EpipolarYError = 3.0,
            FilteredOutPairCount = 0,
            ReprojectionError = 0.8,
            ImagePairCount = 20,
            HasValidRectification = true,
            FocalLengthLeftX = 500
        };

        var recommendations = CalibrationValidator.GetRecommendations(validation).ToList();

        Assert.Contains(recommendations, r => r.Contains("Critical") && r.Contains("Y-axis"));
    }

    [Fact]
    public void GetRecommendations_ModerateYError_ReturnsModerateWarning()
    {
        var validation = new CalibrationValidation
        {
            EpipolarYError = 1.0,
            FilteredOutPairCount = 0,
            ReprojectionError = 0.4,
            ImagePairCount = 20,
            HasValidRectification = true,
            FocalLengthLeftX = 500
        };

        var recommendations = CalibrationValidator.GetRecommendations(validation).ToList();

        Assert.Contains(recommendations, r => r.Contains("Moderate") && r.Contains("Y-axis"));
    }

    [Fact]
    public void GetRecommendations_ExcellentQuality_ReturnsPositiveMessage()
    {
        var validation = new CalibrationValidation
        {
            EpipolarYError = 0.2,
            FilteredOutPairCount = 0,
            ReprojectionError = 0.3,
            ImagePairCount = 20,
            HasValidRectification = true,
            FocalLengthLeftX = 500
        };

        var recommendations = CalibrationValidator.GetRecommendations(validation).ToList();

        Assert.Contains(recommendations, r => r.Contains("Excellent"));
    }

    [Fact]
    public void GetRecommendations_FilteredPairs_ReportsCount()
    {
        var validation = new CalibrationValidation
        {
            EpipolarYError = 0.4,
            FilteredOutPairCount = 5,
            ReprojectionError = 0.4,
            ImagePairCount = 20,
            HasValidRectification = true,
            FocalLengthLeftX = 500
        };

        var recommendations = CalibrationValidator.GetRecommendations(validation).ToList();

        Assert.Contains(recommendations, r => r.Contains("5") && r.Contains("outlier"));
    }

    private static CalibrationResult CreateMinimalCalibrationResult()
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
        t.SetTo(new MCvScalar(100));
        var e = new Mat(3, 3, DepthType.Cv64F, 1);
        e.SetTo(new MCvScalar(0));
        var f = new Mat(3, 3, DepthType.Cv64F, 1);
        f.SetTo(new MCvScalar(0));
        var r1 = new Mat(3, 3, DepthType.Cv64F, 1);
        CvInvoke.SetIdentity(r1, new MCvScalar(1));
        var r2 = new Mat(3, 3, DepthType.Cv64F, 1);
        CvInvoke.SetIdentity(r2, new MCvScalar(1));
        var p1 = new Mat(3, 4, DepthType.Cv64F, 1);
        p1.SetTo(new MCvScalar(1));
        var p2 = new Mat(3, 4, DepthType.Cv64F, 1);
        p2.SetTo(new MCvScalar(1));
        var q = new Mat(4, 4, DepthType.Cv64F, 1);
        q.SetTo(new MCvScalar(1));

        return new CalibrationResult
        {
            CameraMatrixLeft = camLeft, CameraMatrixRight = camRight,
            DistCoeffsLeft = distLeft, DistCoeffsRight = distRight,
            R = r, T = t, E = e, F = f,
            R1 = r1, R2 = r2, P1 = p1, P2 = p2, Q = q
        };
    }

    private static Mat CreateIdentityMapX(int width, int height)
    {
        var map = new Mat(height, width, DepthType.Cv32F, 1);
        using var img = map.ToImage<Gray, float>();
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                img[y, x] = new Gray(x);
        img.Mat.CopyTo(map);
        return map;
    }

    private static Mat CreateIdentityMapY(int width, int height)
    {
        var map = new Mat(height, width, DepthType.Cv32F, 1);
        using var img = map.ToImage<Gray, float>();
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                img[y, x] = new Gray(y);
        img.Mat.CopyTo(map);
        return map;
    }

    private static Mat CreateZeroVector3x1()
    {
        var vec = new Mat(3, 1, DepthType.Cv64F, 1);
        vec.SetTo(new MCvScalar(0));
        return vec;
    }
}
