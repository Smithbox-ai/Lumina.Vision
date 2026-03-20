using System;
using System.Threading;
using System.Threading.Tasks;
using Emgu.CV;
using Emgu.CV.CvEnum;
using LuminaCalib.Models;
using LuminaCalib.Services;
using Xunit;

namespace LuminaCalib.Tests;

public sealed class StereoSynchronizerTests
{
    private static FrameRaw MakeFrame(DateTime ts, long id = 0, string sourceId = "test")
    {
        return new FrameRaw(new Mat(10, 10, DepthType.Cv8U, 3), ts, id, sourceId);
    }

    [Fact]
    public void ConcurrentPushAndMatch_DoesNotThrow()
    {
        // Test for race condition - concurrent pushes should not cause disposal conflicts
        using var sync = new StereoSynchronizer(bufferCapacity: 10, toleranceMs: 50.0);
        var exceptions = new System.Collections.Concurrent.ConcurrentBag<Exception>();
        
        var baseTime = DateTime.UtcNow;
        var cts = new CancellationTokenSource();
        
        var leftTask = Task.Run(() =>
        {
            try
            {
                for (int i = 0; i < 1000; i++)
                {
                    // Don't use 'using' - let RingBuffer own and dispose the frames
                    var frame = MakeFrame(baseTime.AddMilliseconds(i), i, "left");
                    sync.PushLeft(frame);
                    Thread.Sleep(1); // Small delay to increase contention
                }
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        });

        var rightTask = Task.Run(() =>
        {
            try
            {
                for (int i = 0; i < 1000; i++)
                {
                    // Don't use 'using' - let RingBuffer own and dispose the frames
                    var frame = MakeFrame(baseTime.AddMilliseconds(i + 5), i, "right");
                    sync.PushRight(frame);
                    Thread.Sleep(1); // Small delay to increase contention
                }
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        });

        Task.WaitAll(leftTask, rightTask);
        
        Assert.Empty(exceptions);
    }

    [Fact]
    public void FramesWithinTolerance_EmitPair()
    {
        using var sync = new StereoSynchronizer(bufferCapacity: 10, toleranceMs: 50.0);
        StereoFramePair? emittedPair = null;
        sync.OnStereoFrameReady += pair => emittedPair = pair;

        var baseTime = DateTime.UtcNow;
        
        var leftFrame = MakeFrame(baseTime, 1, "left");
        var rightFrame = MakeFrame(baseTime.AddMilliseconds(10), 2, "right");

        sync.PushLeft(leftFrame);
        sync.PushRight(rightFrame);

        Assert.NotNull(emittedPair);
        
        using (emittedPair)
        {
            Assert.Equal(1, emittedPair.Left.FrameId);
            Assert.Equal(2, emittedPair.Right.FrameId);
            
            var timeDelta = Math.Abs((emittedPair.Left.Timestamp - emittedPair.Right.Timestamp).TotalMilliseconds);
            Assert.True(timeDelta <= 50.0);
        }
    }

    [Fact]
    public void FramesBeyondTolerance_NoPair()
    {
        using var sync = new StereoSynchronizer(bufferCapacity: 10, toleranceMs: 50.0);
        StereoFramePair? emittedPair = null;
        sync.OnStereoFrameReady += pair => emittedPair = pair;

        var baseTime = DateTime.UtcNow;
        
        var leftFrame = MakeFrame(baseTime, 1, "left");
        var rightFrame = MakeFrame(baseTime.AddMilliseconds(100), 2, "right");

        sync.PushLeft(leftFrame);
        sync.PushRight(rightFrame);

        Assert.Null(emittedPair);
    }

    [Fact]
    public void TryGetClosestPair_ReturnsPair_WhenDeltaExceedsTolerance()
    {
        using var sync = new StereoSynchronizer(bufferCapacity: 10, toleranceMs: 20.0);

        var baseTime = DateTime.UtcNow;
        sync.PushLeft(MakeFrame(baseTime, 1, "left"));
        sync.PushRight(MakeFrame(baseTime.AddMilliseconds(85), 2, "right"));

        var found = sync.TryGetClosestPair(out var pair, out var deltaMs);

        Assert.True(found);
        Assert.NotNull(pair);
        Assert.True(deltaMs > 20.0);

        using (pair)
        {
            Assert.Equal(1, pair.Left.FrameId);
            Assert.Equal(2, pair.Right.FrameId);
        }
    }

    [Fact]
    public void TryGetLatestPair_RemainsStrict_WhenOnlyClosestPairExists()
    {
        using var sync = new StereoSynchronizer(bufferCapacity: 10, toleranceMs: 20.0);

        var baseTime = DateTime.UtcNow;
        sync.PushLeft(MakeFrame(baseTime, 1, "left"));
        sync.PushRight(MakeFrame(baseTime.AddMilliseconds(85), 2, "right"));

        var strictPair = sync.TryGetLatestPair();
        var hasClosest = sync.TryGetClosestPair(out var closestPair, out var deltaMs);

        Assert.Null(strictPair);
        Assert.True(hasClosest);
        Assert.NotNull(closestPair);
        Assert.True(deltaMs > 20.0);

        closestPair?.Dispose();
    }

    [Fact]
    public void BidirectionalMatching_FindsBestPair()
    {
        // Test that bidirectional matching picks the pair with smallest delta
        // Timeline: Left@T=0, Right@T=10ms, Left@T=25ms
        // Direction 1: leftLatest@T=25 → closest right@T=10, delta=15ms
        // Direction 2: rightLatest@T=10 → closest left@T=0, delta=10ms
        // Best pair: (Left@T=0, Right@T=10) with delta=10ms (smallest)
        
        using var sync = new StereoSynchronizer(bufferCapacity: 10, toleranceMs: 50.0);
        StereoFramePair? emittedPair = null;
        sync.OnStereoFrameReady += pair => emittedPair = pair;

        var baseTime = DateTime.UtcNow;
        
        var left1 = MakeFrame(baseTime, 1, "left");
        sync.PushLeft(left1);
        
        var right1 = MakeFrame(baseTime.AddMilliseconds(10), 2, "right");
        sync.PushRight(right1);
        
        // At this point we might get a pair (left@0, right@10)
        // Clear the event for the next push
        emittedPair?.Dispose();
        emittedPair = null;
        
        var left2 = MakeFrame(baseTime.AddMilliseconds(25), 3, "left");
        sync.PushLeft(left2);

        // TryGetLatestPair should return the best match overall
        var latestPair = sync.TryGetLatestPair();
        
        Assert.NotNull(latestPair);
        using (latestPair)
        {
            // Bidirectional matching picks smallest delta
            // (left@0, right@10) delta=10ms beats (left@25, right@10) delta=15ms
            Assert.Equal(1, latestPair.Left.FrameId);
            Assert.Equal(2, latestPair.Right.FrameId);
            
            var timeDelta = Math.Abs((latestPair.Left.Timestamp - latestPair.Right.Timestamp).TotalMilliseconds);
            Assert.True(timeDelta <= 50.0);
            Assert.InRange(timeDelta, 9, 11); // Should be ~10ms
        }
    }

    [Fact]
    public void AsymmetricTimestamps_StillMatch()
    {
        // Test that when right arrives first, then left, they still match
        using var sync = new StereoSynchronizer(bufferCapacity: 10, toleranceMs: 50.0);
        StereoFramePair? emittedPair = null;
        sync.OnStereoFrameReady += pair =>
        {
            emittedPair?.Dispose(); // Dispose previous if any
            emittedPair = pair;
        };

        var baseTime = DateTime.UtcNow;
        
        // Push right first
        var rightFrame = MakeFrame(baseTime, 1, "right");
        sync.PushRight(rightFrame);
        
        Assert.Null(emittedPair); // No pair yet
        
        // Push left slightly later
        var leftFrame = MakeFrame(baseTime.AddMilliseconds(20), 2, "left");
        sync.PushLeft(leftFrame);

        Assert.NotNull(emittedPair);
        
        using (emittedPair)
        {
            Assert.Equal(2, emittedPair.Left.FrameId);
            Assert.Equal(1, emittedPair.Right.FrameId);
            
            var timeDelta = Math.Abs((emittedPair.Left.Timestamp - emittedPair.Right.Timestamp).TotalMilliseconds);
            Assert.True(timeDelta <= 50.0);
        }
    }

    [Fact]
    public void TryGetLatestPair_ReturnsBestBidirectionalMatch()
    {
        using var sync = new StereoSynchronizer(bufferCapacity: 10, toleranceMs: 50.0);

        var baseTime = DateTime.UtcNow;
        
        // Push several frames
        var left1 = MakeFrame(baseTime, 1, "left");
        var right1 = MakeFrame(baseTime.AddMilliseconds(5), 2, "right");
        var left2 = MakeFrame(baseTime.AddMilliseconds(30), 3, "left");
        var right2 = MakeFrame(baseTime.AddMilliseconds(35), 4, "right");
        
        sync.PushLeft(left1);
        sync.PushRight(right1);
        sync.PushLeft(left2);
        sync.PushRight(right2);

        var pair = sync.TryGetLatestPair();
        
        Assert.NotNull(pair);
        
        using (pair)
        {
            // With bidirectional matching, should get the best match from latest frames
            // Latest left = T=30ms (id=3), Latest right = T=35ms (id=4)
            // Could be (left@30, right@35) with delta=5ms or other combinations
            // But we want the best overall match considering both directions
            
            var timeDelta = Math.Abs((pair.Left.Timestamp - pair.Right.Timestamp).TotalMilliseconds);
            Assert.True(timeDelta <= 50.0);
            
            // Should involve the latest frames
            Assert.True(pair.Left.FrameId >= 3 || pair.Right.FrameId >= 4);
        }
    }

    [Fact]
    public void Clear_RemovesAllFrames()
    {
        using var sync = new StereoSynchronizer(bufferCapacity: 10, toleranceMs: 50.0);

        var baseTime = DateTime.UtcNow;
        
        var left = MakeFrame(baseTime, 1, "left");
        var right = MakeFrame(baseTime, 2, "right");
        
        sync.PushLeft(left);
        sync.PushRight(right);
        
        Assert.True(sync.LeftBufferCount > 0 || sync.RightBufferCount > 0);
        
        sync.Clear();
        
        Assert.Equal(0, sync.LeftBufferCount);
        Assert.Equal(0, sync.RightBufferCount);
        
        var pair = sync.TryGetLatestPair();
        Assert.Null(pair);
    }

    [Fact]
    public void ToleranceMs_CanBeUpdated()
    {
        using var sync = new StereoSynchronizer(bufferCapacity: 10, toleranceMs: 50.0);
        
        Assert.Equal(50.0, sync.ToleranceMs);
        
        sync.ToleranceMs = 100.0;
        Assert.Equal(100.0, sync.ToleranceMs);
        
        // Invalid value should default to 20.0
        sync.ToleranceMs = -10.0;
        Assert.Equal(20.0, sync.ToleranceMs);
    }
}
