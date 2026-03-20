using System.Drawing;
using System.Diagnostics;
using Emgu.CV;
using LuminaCalib.Calibration;
using LuminaCalib.Models;
using LuminaCalib.Services;
using Xunit;

namespace LuminaCalib.Tests;

public class AutoCaptureServiceTests : IDisposable
{
    private readonly List<StereoFramePair> _createdPairs = new();

    public void Dispose()
    {
        foreach (var pair in _createdPairs)
        {
            pair.Dispose();
        }
        _createdPairs.Clear();
    }

    private static CornerDetector MakeDummyDetector() =>
        new(9, 6, BoardType.Chessboard);

    private StereoFramePair MakeTestPair()
    {
        var left = new FrameRaw(new Mat(10, 10, Emgu.CV.CvEnum.DepthType.Cv8U, 3), DateTime.UtcNow, 1, "Left");
        var right = new FrameRaw(new Mat(10, 10, Emgu.CV.CvEnum.DepthType.Cv8U, 3), DateTime.UtcNow, 2, "Right");
        var pair = new StereoFramePair(left, right);
        _createdPairs.Add(pair);
        return pair;
    }

    [Fact]
    public void ProcessFrame_WhenNotRunning_DoesNothing()
    {
        // Arrange
        var detector = MakeDummyDetector();
        var service = new AutoCaptureService(detector, CalibrationMode.Auto);
        var pair = MakeTestPair();
        bool detectionFired = false;
        bool captureFired = false;

        service.OnDetectionUpdate += (_, _, _, _) => detectionFired = true;
        service.OnValidPairCaptured += (_, _, _) => captureFired = true;

        // Act - ProcessFrame without calling Start()
        service.ProcessFrame(pair);

        // Assert
        Assert.False(detectionFired);
        Assert.False(captureFired);
        Assert.Equal(0, service.CapturedCount);
    }

    [Fact]
    public void ProcessFrame_PatternNotFound_EmitsDetectionUpdateWithFalse()
    {
        // Arrange
        var detector = MakeDummyDetector();
        var service = new AutoCaptureService(detector, CalibrationMode.Auto);
        var pair = MakeTestPair();

        bool detectionFired = false;
        bool leftFound = true, rightFound = true;
        PointF[]? leftCorners = null, rightCorners = null;
        using var detectionDone = new ManualResetEventSlim(false);

        service.OnDetectionUpdate += (lf, rf, lc, rc) =>
        {
            detectionFired = true;
            leftFound = lf;
            rightFound = rf;
            leftCorners = lc;
            rightCorners = rc;
            detectionDone.Set();
        };

        // Use blank images - pattern will not be found
        service.DetectOverride = _ => (false, null);
        service.Start();

        // Act
        service.ProcessFrame(pair);
        Assert.True(detectionDone.Wait(1500), "Detection update did not arrive in time");

        // Assert
        Assert.True(detectionFired);
        Assert.False(leftFound);
        Assert.False(rightFound);
        Assert.Null(leftCorners);
        Assert.Null(rightCorners);
    }

    [Fact]
    public void ProcessFrame_PatternFound_EmitsDetectionUpdateWithTrue()
    {
        // Arrange
        var detector = MakeDummyDetector();
        var service = new AutoCaptureService(detector, CalibrationMode.Auto);
        var pair = MakeTestPair();

        bool detectionFired = false;
        bool leftFound = false, rightFound = false;
        PointF[]? leftCorners = null, rightCorners = null;
        using var detectionDone = new ManualResetEventSlim(false);

        service.OnDetectionUpdate += (lf, rf, lc, rc) =>
        {
            detectionFired = true;
            leftFound = lf;
            rightFound = rf;
            leftCorners = lc;
            rightCorners = rc;
            detectionDone.Set();
        };

        var expectedCorners = new PointF[] { new(1, 1), new(2, 2) };
        service.DetectOverride = _ => (true, expectedCorners);
        service.Start();

        // Act
        service.ProcessFrame(pair);
        Assert.True(detectionDone.Wait(1500), "Detection update did not arrive in time");

        // Assert
        Assert.True(detectionFired);
        Assert.True(leftFound);
        Assert.True(rightFound);
        Assert.NotNull(leftCorners);
        Assert.NotNull(rightCorners);
        Assert.Equal(2, leftCorners!.Length);
    }

    [Fact]
    public void ProcessFrame_StablePattern_CapturesAfterThreshold()
    {
        // Arrange
        var detector = MakeDummyDetector();
        var service = new AutoCaptureService(detector, CalibrationMode.Auto)
        {
            StabilityThresholdMs = 0,
            MinCaptureIntervalMs = 0
        };
        var pair = MakeTestPair();

        StereoFramePair? capturedPair = null;
        PointF[]? capturedLeftCorners = null;
        PointF[]? capturedRightCorners = null;
        using var captureDone = new ManualResetEventSlim(false);

        service.OnValidPairCaptured += (p, lc, rc) =>
        {
            capturedPair = p;
            capturedLeftCorners = lc;
            capturedRightCorners = rc;
            captureDone.Set();
        };

        var expectedCorners = new PointF[] { new(3, 3), new(4, 4) };
        service.DetectOverride = _ => (true, expectedCorners);
        service.Start();

        // Act
        service.ProcessFrame(pair);
        Assert.True(captureDone.Wait(1500), "Capture event did not arrive in time");

        // Assert
        Assert.NotNull(capturedPair);
        Assert.NotNull(capturedLeftCorners);
        Assert.NotNull(capturedRightCorners);
        Assert.Equal(1, service.CapturedCount);

        // Cleanup captured pair
        capturedPair?.Dispose();
    }

    [Fact]
    public void ProcessFrame_UnstablePattern_DoesNotCapture()
    {
        // Arrange
        var detector = MakeDummyDetector();
        var service = new AutoCaptureService(detector, CalibrationMode.Auto)
        {
            StabilityThresholdMs = 100,
            MinCaptureIntervalMs = 0
        };

        StereoFramePair? capturedPair = null;
        service.OnValidPairCaptured += (p, _, _) => capturedPair = p;

        var expectedCorners = new PointF[] { new(1, 1) };
        bool returnFound = true;
        service.DetectOverride = _ => (returnFound, returnFound ? expectedCorners : null);
        service.Start();

        // Act - alternate found/not-found rapidly
        for (int i = 0; i < 10; i++)
        {
            returnFound = i % 2 == 0;
            var pair = MakeTestPair();
            service.ProcessFrame(pair);
            Thread.Sleep(10);
        }

        // Assert - no capture because pattern was never stable
        Assert.Null(capturedPair);
        Assert.Equal(0, service.CapturedCount);
    }

    [Fact]
    public void ProcessFrame_RespectsMinInterval()
    {
        // Arrange
        var detector = MakeDummyDetector();
        var service = new AutoCaptureService(detector, CalibrationMode.Auto)
        {
            StabilityThresholdMs = 0,
            MinCaptureIntervalMs = 500
        };

        int captureCount = 0;
        service.OnValidPairCaptured += (p, _, _) =>
        {
            captureCount++;
            p.Dispose();
        };

        var expectedCorners = new PointF[] { new(1, 1) };
        service.DetectOverride = _ => (true, expectedCorners);
        service.Start();

        // Act - process two frames quickly
        service.ProcessFrame(MakeTestPair());
        Thread.Sleep(50); // Less than MinCaptureIntervalMs
        service.ProcessFrame(MakeTestPair());

        // Assert - only first capture should succeed
        Assert.Equal(1, captureCount);
        Assert.Equal(1, service.CapturedCount);
    }

    [Fact]
    public void ProcessFrame_StopsAtMaxCaptures()
    {
        // Arrange
        var detector = MakeDummyDetector();
        var service = new AutoCaptureService(detector, CalibrationMode.Auto)
        {
            StabilityThresholdMs = 0,
            MinCaptureIntervalMs = 0,
            MaxCaptures = 1
        };

        int captureCount = 0;
        service.OnValidPairCaptured += (p, _, _) =>
        {
            captureCount++;
            p.Dispose();
        };

        var expectedCorners = new PointF[] { new(1, 1) };
        service.DetectOverride = _ => (true, expectedCorners);
        service.Start();

        // Act - attempt two captures
        service.ProcessFrame(MakeTestPair());
        Thread.Sleep(10);
        service.ProcessFrame(MakeTestPair());

        // Assert - only 1 capture (max)
        Assert.Equal(1, captureCount);
        Assert.Equal(1, service.CapturedCount);
    }

    [Fact]
    public void ProcessFrame_MaxCapturesZero_DoesNotEnforceInternalLimit()
    {
        // Arrange
        var detector = MakeDummyDetector();
        var service = new AutoCaptureService(detector, CalibrationMode.Auto)
        {
            StabilityThresholdMs = 0,
            MinCaptureIntervalMs = 0,
            MaxCaptures = 0
        };

        int captureCount = 0;
        using var firstCapture = new ManualResetEventSlim(false);
        using var secondCapture = new ManualResetEventSlim(false);
        service.OnValidPairCaptured += (p, _, _) =>
        {
            var current = Interlocked.Increment(ref captureCount);
            if (current == 1) firstCapture.Set();
            if (current == 2) secondCapture.Set();
            p.Dispose();
        };

        var expectedCorners = new PointF[] { new(1, 1) };
        service.DetectOverride = _ => (true, expectedCorners);
        service.Start();

        // Act
        service.ProcessFrame(MakeTestPair());
        Assert.True(firstCapture.Wait(1500), "First capture did not arrive in time");

        service.ProcessFrame(MakeTestPair());
        Assert.True(secondCapture.Wait(1500), "Second capture did not arrive in time");

        // Assert
        Assert.Equal(2, captureCount);
        Assert.Equal(2, service.CapturedCount);
    }

    [Fact]
    public void ProcessFrame_MinDetectionInterval_ThrottlesDetectionAttempts()
    {
        // Arrange
        var detector = MakeDummyDetector();
        var service = new AutoCaptureService(detector, CalibrationMode.Auto)
        {
            MinDetectionIntervalMs = 500
        };

        int detectCalls = 0;
        int updates = 0;
        using var firstUpdate = new ManualResetEventSlim(false);
        using var secondUpdate = new ManualResetEventSlim(false);
        service.DetectOverride = _ =>
        {
            Interlocked.Increment(ref detectCalls);
            return (false, null);
        };
        service.OnDetectionUpdate += (_, _, _, _) =>
        {
            var current = Interlocked.Increment(ref updates);
            if (current == 1) firstUpdate.Set();
            if (current == 2) secondUpdate.Set();
        };
        service.Start();

        // Act
        service.ProcessFrame(MakeTestPair());
        Assert.True(firstUpdate.Wait(1500), "First detection update did not arrive in time");

        service.ProcessFrame(MakeTestPair()); // should be skipped by detection throttle
        Thread.Sleep(100);

        // Assert intermediate state
        Assert.Equal(1, Volatile.Read(ref updates));
        Assert.Equal(2, Volatile.Read(ref detectCalls)); // left + right for first processed frame

        // Act
        Thread.Sleep(450); // >= 500 ms since first accepted attempt
        service.ProcessFrame(MakeTestPair());
        Assert.True(secondUpdate.Wait(1500), "Second detection update did not arrive in time");

        // Assert final state
        Assert.Equal(2, Volatile.Read(ref updates));
        Assert.Equal(4, Volatile.Read(ref detectCalls)); // left + right for each processed frame
    }

    [Fact]
    public void Reset_ClearsCounts()
    {
        // Arrange
        var detector = MakeDummyDetector();
        var service = new AutoCaptureService(detector, CalibrationMode.Auto)
        {
            StabilityThresholdMs = 0,
            MinCaptureIntervalMs = 0
        };

        using var captureDone = new ManualResetEventSlim(false);
        service.OnValidPairCaptured += (p, _, _) => p.Dispose();
        service.OnValidPairCaptured += (_, _, _) => captureDone.Set();

        var expectedCorners = new PointF[] { new(1, 1) };
        service.DetectOverride = _ => (true, expectedCorners);
        service.Start();

        service.ProcessFrame(MakeTestPair());
        Assert.True(captureDone.Wait(1500), "Initial capture did not arrive in time");
        Assert.Equal(1, service.CapturedCount);

        // Act
        service.Reset();

        // Assert
        Assert.Equal(0, service.CapturedCount);
        Assert.False(service.PatternDetectedLeft);
        Assert.False(service.PatternDetectedRight);
    }

    [Fact]
    public void Stop_PreventsCapture()
    {
        // Arrange
        var detector = MakeDummyDetector();
        var service = new AutoCaptureService(detector, CalibrationMode.Auto)
        {
            StabilityThresholdMs = 0,
            MinCaptureIntervalMs = 0
        };

        int captureCount = 0;
        service.OnValidPairCaptured += (p, _, _) =>
        {
            captureCount++;
            p.Dispose();
        };

        var expectedCorners = new PointF[] { new(1, 1) };
        service.DetectOverride = _ => (true, expectedCorners);
        service.Start();

        // Act
        service.Stop();
        service.ProcessFrame(MakeTestPair());

        // Assert
        Assert.Equal(0, captureCount);
        Assert.Equal(0, service.CapturedCount);
    }

    [Fact]
    public void Constructor_ThrowsOnNullDetector()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new AutoCaptureService(null!, CalibrationMode.Auto));
    }

    [Fact]
    public void ProcessFrame_RunsAsynchronouslyWithoutBlocking()
    {
        // Arrange
        var detector = MakeDummyDetector();
        var service = new AutoCaptureService(detector, CalibrationMode.Auto)
        {
            StabilityThresholdMs = 0,
            MinCaptureIntervalMs = 0
        };

        using var detectionDone = new ManualResetEventSlim(false);
        service.DetectOverride = _ =>
        {
            Thread.Sleep(150);
            return (true, new[] { new PointF(1, 1) });
        };
        service.OnDetectionUpdate += (_, _, _, _) => detectionDone.Set();
        service.Start();

        var pair = MakeTestPair();

        // Act
        var sw = Stopwatch.StartNew();
        service.ProcessFrame(pair);
        sw.Stop();

        // Assert: call returns quickly while detection continues in background
        Assert.True(sw.ElapsedMilliseconds < 75, $"ProcessFrame took {sw.ElapsedMilliseconds} ms");
        Assert.True(detectionDone.Wait(1500), "Background processing did not complete in time");
    }

    [Fact]
    public void ProcessFrame_BackpressureSkipsFrameWhenBusy()
    {
        // Arrange
        var detector = MakeDummyDetector();
        var service = new AutoCaptureService(detector, CalibrationMode.Auto)
        {
            StabilityThresholdMs = 0,
            MinCaptureIntervalMs = 0
        };

        using var enteredGate = new ManualResetEventSlim(false);
        using var releaseGate = new ManualResetEventSlim(false);
        using var detectionDone = new ManualResetEventSlim(false);

        int detectCalls = 0;
        service.DetectOverride = _ =>
        {
            int callNumber = Interlocked.Increment(ref detectCalls);
            if (callNumber == 1)
            {
                enteredGate.Set();
                releaseGate.Wait(1500);
            }
            return (true, new[] { new PointF(1, 1) });
        };
        service.OnDetectionUpdate += (_, _, _, _) => detectionDone.Set();
        service.Start();

        // Act
        service.ProcessFrame(MakeTestPair());
        Assert.True(enteredGate.Wait(1000), "First frame did not enter processing in time");

        service.ProcessFrame(MakeTestPair()); // should be skipped while busy
        releaseGate.Set();

        // Assert
        Assert.True(detectionDone.Wait(1500), "First frame did not complete processing in time");
        Assert.Equal(2, Volatile.Read(ref detectCalls)); // exactly one frame processed (left + right)
    }

    [Fact]
    public void ProcessFrame_DetectOverrideThrows_ReportsNoPatternAndKeepsProcessing()
    {
        // Arrange
        var detector = MakeDummyDetector();
        var service = new AutoCaptureService(detector, CalibrationMode.Auto);

        int detectCalls = 0;
        int updates = 0;
        bool firstLeft = true, firstRight = true;
        bool secondLeft = false, secondRight = false;
        using var firstUpdate = new ManualResetEventSlim(false);
        using var secondUpdate = new ManualResetEventSlim(false);

        service.DetectOverride = _ =>
        {
            var call = Interlocked.Increment(ref detectCalls);
            if (call <= 2)
            {
                throw new InvalidOperationException("Synthetic detector failure.");
            }

            return (true, new[] { new PointF(1, 1) });
        };

        service.OnDetectionUpdate += (lf, rf, _, _) =>
        {
            var updateNumber = Interlocked.Increment(ref updates);
            if (updateNumber == 1)
            {
                firstLeft = lf;
                firstRight = rf;
                firstUpdate.Set();
                return;
            }

            if (updateNumber == 2)
            {
                secondLeft = lf;
                secondRight = rf;
                secondUpdate.Set();
            }
        };

        service.Start();

        // Act
        service.ProcessFrame(MakeTestPair());
        Assert.True(firstUpdate.Wait(1500), "First detection update did not arrive in time.");

        service.ProcessFrame(MakeTestPair());
        Assert.True(secondUpdate.Wait(1500), "Second detection update did not arrive in time.");

        // Assert
        Assert.False(firstLeft);
        Assert.False(firstRight);
        Assert.True(secondLeft);
        Assert.True(secondRight);
    }
}
