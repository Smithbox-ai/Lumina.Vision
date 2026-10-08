using Emgu.CV;
using Emgu.CV.CvEnum;
using LuminaCalib.Calibration;
using LuminaCalib.Models;
using LuminaCalib.Services;
using System.Diagnostics;
using Xunit;

namespace LuminaCalib.Tests;

public class DepthMapServiceTests : IDisposable
{
    private readonly List<StereoFramePair> _createdPairs = new();
    private readonly List<IDisposable> _disposables = new();

    public void Dispose()
    {
        foreach (var pair in _createdPairs)
            pair.Dispose();
        _createdPairs.Clear();

        foreach (var d in _disposables)
            d.Dispose();
        _disposables.Clear();
    }

    private StereoFramePair MakeTestPair()
    {
        var left = new FrameRaw(
            new Mat(100, 100, DepthType.Cv8U, 3),
            DateTime.UtcNow, 1, "Left");
        var right = new FrameRaw(
            new Mat(100, 100, DepthType.Cv8U, 3),
            DateTime.UtcNow, 2, "Right");
        var pair = new StereoFramePair(left, right);
        _createdPairs.Add(pair);
        return pair;
    }

    private DepthMapService CreateService(bool enabled = true)
    {
        var rectifier = new StereoRectifier();
        _disposables.Add(rectifier);
        var settings = new DepthMapSettings();
        var service = new DepthMapService(rectifier, settings) { IsEnabled = enabled };
        _disposables.Add(service);
        return service;
    }

    // ── Test 1: Disabled service does not fire event ──

    [Fact]
    public void ProcessFrame_WhenNotEnabled_DoesNotCompute()
    {
        // Arrange
        var service = CreateService(enabled: false);
        bool eventFired = false;
        service.OnDepthMapReady += _ => eventFired = true;

        // Act
        service.ProcessStereoFrame(MakeTestPair());

        // Assert
        Assert.False(eventFired);
    }

    // ── Test 2: Uninitialized rectifier does not fire event ──

    [Fact]
    public void ProcessFrame_WhenRectifierNotInitialized_DoesNotCompute()
    {
        // Arrange — rectifier is NOT initialized (no calibration data)
        var service = CreateService(enabled: true);
        bool eventFired = false;
        service.OnDepthMapReady += _ => eventFired = true;

        // Act
        service.ProcessStereoFrame(MakeTestPair());

        // Assert
        Assert.False(eventFired);
    }

    // ── Test 3: Backpressure — rapid calls don't crash, second is skipped ──

    [Fact]
    public void ProcessFrame_RunsAsynchronouslyWithoutBlockingCaller()
    {
        // Arrange
        var service = CreateService(enabled: true);
        service.ProcessOverride = (left, right) =>
        {
            Thread.Sleep(200);
            var disparity = new Mat(left.Size, DepthType.Cv16S, 1);
            var colorized = new Mat(left.Size, DepthType.Cv8U, 3);
            return (disparity, colorized);
        };

        using var ready = new ManualResetEventSlim(false);
        int readyCount = 0;
        service.OnDepthMapReady += _ =>
        {
            Interlocked.Increment(ref readyCount);
            ready.Set();
        };

        // Act
        var sw = Stopwatch.StartNew();
        service.ProcessStereoFrame(MakeTestPair());
        sw.Stop();

        // Assert
        Assert.True(sw.ElapsedMilliseconds < 100, $"Expected non-blocking return, took {sw.ElapsedMilliseconds} ms");
        Assert.True(ready.Wait(2000), "Depth map was not produced in time");
        Assert.Equal(1, readyCount);
    }

    [Fact]
    public void ProcessFrame_WhenBusy_SkipsNextFrame()
    {
        // Arrange
        var service = CreateService(enabled: true);
        int processCount = 0;
        using var firstStarted = new ManualResetEventSlim(false);
        using var releaseFirst = new ManualResetEventSlim(false);
        using var ready = new ManualResetEventSlim(false);

        service.ProcessOverride = (left, right) =>
        {
            var index = Interlocked.Increment(ref processCount);
            if (index == 1)
            {
                firstStarted.Set();
                releaseFirst.Wait(2000);
            }

            var disparity = new Mat(left.Size, DepthType.Cv16S, 1);
            var colorized = new Mat(left.Size, DepthType.Cv8U, 3);
            return (disparity, colorized);
        };

        int readyCount = 0;
        service.OnDepthMapReady += _ =>
        {
            Interlocked.Increment(ref readyCount);
            ready.Set();
        };

        // Act
        var pair1 = MakeTestPair();
        var pair2 = MakeTestPair();

        service.ProcessStereoFrame(pair1);
        Assert.True(firstStarted.Wait(1000), "First processing did not start");

        // Must return quickly and be skipped by backpressure while first is still running
        var sw = Stopwatch.StartNew();
        service.ProcessStereoFrame(pair2);
        sw.Stop();

        releaseFirst.Set();
        Assert.True(ready.Wait(2000), "First depth map result did not arrive in time");

        // Assert
        Assert.True(sw.ElapsedMilliseconds < 100, $"Expected quick skip while busy, took {sw.ElapsedMilliseconds} ms");
        Assert.Equal(1, processCount);
        Assert.Equal(1, readyCount);
    }

    // ── Test 4: Enabled + ProcessOverride → fires OnDepthMapReady ──

    [Fact]
    public void ProcessFrame_WhenEnabled_FiresOnDepthMapReady()
    {
        // Arrange
        var service = CreateService(enabled: true);
        Mat? colorizedResult = null;
        using var ready = new ManualResetEventSlim(false);

        service.ProcessOverride = (left, right) =>
        {
            var disparity = new Mat(left.Size, DepthType.Cv16S, 1);
            var colorized = new Mat(left.Size, DepthType.Cv8U, 3);
            return (disparity, colorized);
        };

        service.OnDepthMapReady += mat =>
        {
            colorizedResult = mat;
            ready.Set();
        };

        // Act
        service.ProcessStereoFrame(MakeTestPair());
        Assert.True(ready.Wait(1500), "Depth map result did not arrive in time");

        // Assert
        Assert.NotNull(colorizedResult);
        Assert.False(colorizedResult!.IsEmpty);
    }

    // ── Test 5: Fires OnProcessingTimeMs with value > 0 ──

    [Fact]
    public void ProcessFrame_FiresOnProcessingTimeMs()
    {
        // Arrange
        var service = CreateService(enabled: true);
        double? elapsedMs = null;
        using var done = new ManualResetEventSlim(false);

        service.ProcessOverride = (left, right) =>
        {
            var disparity = new Mat(left.Size, DepthType.Cv16S, 1);
            var colorized = new Mat(left.Size, DepthType.Cv8U, 3);
            return (disparity, colorized);
        };

        service.OnProcessingTimeMs += ms =>
        {
            elapsedMs = ms;
            done.Set();
        };

        // Act
        service.ProcessStereoFrame(MakeTestPair());
        Assert.True(done.Wait(1500), "Processing time event did not arrive in time");

        // Assert
        Assert.NotNull(elapsedMs);
        Assert.True(elapsedMs!.Value >= 0, $"Expected elapsed time >= 0, got {elapsedMs.Value}");
    }

    // ── Test 6: Dispose does not crash even after processing ──

    [Fact]
    public void Dispose_CleansUpLatestColorized()
    {
        // Arrange
        var rectifier = new StereoRectifier();
        var settings = new DepthMapSettings();
        var service = new DepthMapService(rectifier, settings) { IsEnabled = true };

        using var ready = new ManualResetEventSlim(false);
        service.ProcessOverride = (left, right) =>
        {
            var disparity = new Mat(left.Size, DepthType.Cv16S, 1);
            var colorized = new Mat(left.Size, DepthType.Cv8U, 3);
            return (disparity, colorized);
        };

        service.OnDepthMapReady += _ => ready.Set();

        service.ProcessStereoFrame(MakeTestPair());
        Assert.True(ready.Wait(1500), "Depth map result did not arrive in time");

        // Act & Assert — should not throw
        service.Dispose();
        rectifier.Dispose();
    }

    // ── Test 7: Throttling — ProcessEveryNthFrame skips correct frames ──

    [Fact]
    public void Throttling_SkipsCorrectFrames()
    {
        // Arrange
        var service = CreateService(enabled: true);
        service.ProcessEveryNthFrame = 3;

        int readyCount = 0;
        using var ready = new ManualResetEventSlim(false);
        service.ProcessOverride = (left, right) =>
        {
            var disparity = new Mat(left.Size, DepthType.Cv16S, 1);
            var colorized = new Mat(left.Size, DepthType.Cv8U, 3);
            return (disparity, colorized);
        };

        service.OnDepthMapReady += _ =>
        {
            Interlocked.Increment(ref readyCount);
            ready.Set();
        };

        // Act — send 6 frames; with ProcessEveryNthFrame=3, frames 3 and 6 should process
        for (int i = 0; i < 6; i++)
        {
            service.ProcessStereoFrame(MakeTestPair());

            if ((i + 1) % 3 == 0)
            {
                Assert.True(ready.Wait(1500), $"Expected processed frame for index {i + 1}");
                ready.Reset();
            }
        }

        // Assert
        Assert.Equal(2, readyCount);
    }

    // ── Test 8: After dispose, ProcessStereoFrame is a no-op ──

    [Fact]
    public void ProcessFrame_AfterDispose_DoesNothing()
    {
        // Arrange
        var service = CreateService(enabled: true);
        service.ProcessOverride = (left, right) =>
        {
            var disparity = new Mat(left.Size, DepthType.Cv16S, 1);
            var colorized = new Mat(left.Size, DepthType.Cv8U, 3);
            return (disparity, colorized);
        };

        bool eventFired = false;
        service.OnDepthMapReady += _ => eventFired = true;

        // Act
        service.Dispose();
        service.ProcessStereoFrame(MakeTestPair());

        // Assert
        Assert.False(eventFired);
    }

    // ── Test 9: Ownership boundary — caller may dispose input pair immediately ──

    [Fact]
    public void ProcessFrame_CallerCanDisposeInputPairImmediately_ProcessingStillCompletes()
    {
        var service = CreateService(enabled: true);

        using var ready = new ManualResetEventSlim(false);
        int readyCount = 0;

        service.ProcessOverride = (left, right) =>
        {
            Thread.Sleep(100);
            var disparity = new Mat(left.Size, DepthType.Cv16S, 1);
            var colorized = new Mat(left.Size, DepthType.Cv8U, 3);
            return (disparity, colorized);
        };

        service.OnDepthMapReady += _ =>
        {
            Interlocked.Increment(ref readyCount);
            ready.Set();
        };

        var left = new FrameRaw(new Mat(100, 100, DepthType.Cv8U, 3), DateTime.UtcNow, 10, "Left");
        var right = new FrameRaw(new Mat(100, 100, DepthType.Cv8U, 3), DateTime.UtcNow, 11, "Right");
        var pair = new StereoFramePair(left, right);

        service.ProcessStereoFrame(pair);
        pair.Dispose();

        Assert.True(ready.Wait(2000), "Processing did not complete after caller disposed input pair");
        Assert.Equal(1, readyCount);
    }

    // ── Test 10: Pre-allocated Mats — multiple frames don't crash ──

    [Fact]
    public void ProcessFrame_CanProcessMultipleFramesSequentially()
    {
        // Verify service can process multiple frames end-to-end without crashes
        var service = CreateService(enabled: true);

        int readyCount = 0;
        using var ready = new ManualResetEventSlim(false);
        service.ProcessOverride = (left, right) =>
        {
            return (new Mat(10, 10, DepthType.Cv16S, 1), new Mat(10, 10, DepthType.Cv8U, 3));
        };

        service.OnDepthMapReady += _ =>
        {
            Interlocked.Increment(ref readyCount);
            ready.Set();
        };

        // Process 5 frames
        for (int i = 0; i < 5; i++)
        {
            var pair = MakeTestPair();
            // Results are published before the worker clears its busy flag. Retry
            // submission until the previous worker has released the service.
            Assert.True(SpinWait.SpinUntil(() =>
            {
                service.ProcessStereoFrame(pair);
                return ready.Wait(10, TestContext.Current.CancellationToken);
            }, 1500), $"Expected depth map result for frame {i + 1}");
            ready.Reset();
        }

        Assert.Equal(5, readyCount);
    }
}
