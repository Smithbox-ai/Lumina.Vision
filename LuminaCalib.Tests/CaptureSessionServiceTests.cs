using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using LuminaCalib.Models;
using LuminaCalib.Services;
using Xunit;

namespace LuminaCalib.Tests;

public sealed class CaptureSessionServiceTests
{
    [Fact]
    public async Task SessionRoundTrip_SavesFramesAndManifest()
    {
        var root = Path.Combine(Path.GetTempPath(), $"LuminaCalib_Sessions_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var service = new CaptureSessionService(root);
            var settings = new AppSettings
            {
                BoardType = BoardType.Chessboard,
                PatternWidth = 9,
                PatternHeight = 6,
                SquareSize = 25,
                MarkerSizeRatio = 0.73f
            };

            var session = service.StartSession(settings, CalibrationMode.Auto);

            using var left = new Mat(32, 48, DepthType.Cv8U, 3);
            using var right = new Mat(32, 48, DepthType.Cv8U, 3);
            left.SetTo(new MCvScalar(10, 20, 30));
            right.SetTo(new MCvScalar(30, 20, 10));

            using var pair = new StereoFramePair(
                new FrameRaw(left.Clone(), DateTime.UtcNow, 101, "Left"),
                new FrameRaw(right.Clone(), DateTime.UtcNow.AddMilliseconds(5), 202, "Right"));

            await service.AppendCaptureAsync(session, pair, leftPatternFound: true, rightPatternFound: true);

            Assert.Single(session.Captures);
            Assert.True(File.Exists(session.ManifestPath));

            var capture = session.Captures[0];
            Assert.True(File.Exists(Path.Combine(session.FramesDirectory, capture.LeftImageFile)));
            Assert.True(File.Exists(Path.Combine(session.FramesDirectory, capture.RightImageFile)));

            var loaded = await service.LoadSessionAsync(session.SessionDirectory);
            Assert.Equal(session.SessionId, loaded.SessionId);
            Assert.Single(loaded.Captures);
            Assert.Equal(CalibrationMode.Auto, loaded.CaptureMode);
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch
            {
                // Best-effort cleanup for temp artifacts.
            }
        }
    }

    [Fact]
    public void ResolveRootPath_RelativePath_UsesAppBaseDirectory()
    {
        var resolved = CaptureSessionService.ResolveRootPath("./CaptureSessions");
        var expectedPrefix = Path.GetFullPath(AppContext.BaseDirectory);

        Assert.StartsWith(expectedPrefix, resolved, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void IsCompleted_DefaultsFalse_InNewSession()
    {
        var root = Path.Combine(Path.GetTempPath(), $"LuminaCalib_Sessions_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var service = new CaptureSessionService(root);
            var settings = new AppSettings
            {
                BoardType = BoardType.Chessboard,
                PatternWidth = 9,
                PatternHeight = 6,
                SquareSize = 25,
                MarkerSizeRatio = 0.73f
            };

            var session = service.StartSession(settings, CalibrationMode.Auto);

            Assert.False(session.IsCompleted);
            Assert.Null(session.CompletedAtUtc);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task CompleteSessionAsync_SetsIsCompletedAndTimestamp()
    {
        var root = Path.Combine(Path.GetTempPath(), $"LuminaCalib_Sessions_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var service = new CaptureSessionService(root);
            var settings = new AppSettings
            {
                BoardType = BoardType.Chessboard,
                PatternWidth = 9,
                PatternHeight = 6,
                SquareSize = 25,
                MarkerSizeRatio = 0.73f
            };

            var session = service.StartSession(settings, CalibrationMode.Auto);

            var beforeComplete = DateTime.UtcNow;
            await service.CompleteSessionAsync(session);

            Assert.True(session.IsCompleted);
            Assert.NotNull(session.CompletedAtUtc);
            Assert.True(session.CompletedAtUtc >= beforeComplete);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task IsCompleted_SurvivesJsonRoundTrip()
    {
        var root = Path.Combine(Path.GetTempPath(), $"LuminaCalib_Sessions_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var service = new CaptureSessionService(root);
            var settings = new AppSettings
            {
                BoardType = BoardType.Chessboard,
                PatternWidth = 9,
                PatternHeight = 6,
                SquareSize = 25,
                MarkerSizeRatio = 0.73f
            };

            var session = service.StartSession(settings, CalibrationMode.Auto);
            await service.CompleteSessionAsync(session);

            var loaded = await service.LoadSessionAsync(session.SessionDirectory);

            Assert.True(loaded.IsCompleted);
            Assert.NotNull(loaded.CompletedAtUtc);
            Assert.Equal(session.CompletedAtUtc!.Value, loaded.CompletedAtUtc!.Value, TimeSpan.FromSeconds(1));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }
}
