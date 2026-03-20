using System.Globalization;
using System.Runtime.InteropServices;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using LuminaCalib.Calibration;
using LuminaCalib.Models;
using LuminaCalib.Services;
using LuminaCalib.ViewModels;
using Xunit;

namespace LuminaCalib.Tests;

/// <summary>Тесты Phase 8: snapshot-сохранение и PLY-экспорт.</summary>
public sealed class DepthMapExportTests : IDisposable
{
    private readonly List<IDisposable> _disposables = new();
    private readonly List<StereoFramePair> _createdPairs = new();

    public void Dispose()
    {
        foreach (var pair in _createdPairs) pair.Dispose();
        _createdPairs.Clear();
        foreach (var d in _disposables) d.Dispose();
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

    // ── Test 1: Service stores disparity + colorized, snapshot methods return clones ──

    [Fact]
    public void GetLatestSnapshots_AfterProcessing_ReturnClones()
    {
        // Arrange
        var service = CreateService(enabled: true);
        using var ready = new ManualResetEventSlim(false);
        service.ProcessOverride = (left, right) =>
        {
            var disparity = new Mat(left.Size, DepthType.Cv16S, 1);
            disparity.SetTo(new MCvScalar(500));
            var colorized = new Mat(left.Size, DepthType.Cv8U, 3);
            colorized.SetTo(new MCvScalar(128, 64, 32));
            return (disparity, colorized);
        };
        service.OnDepthMapReady += _ => ready.Set();

        // Act
        service.ProcessStereoFrame(MakeTestPair());
        Assert.True(ready.Wait(1500), "Depth map result did not arrive in time");

        // Assert
        using var colorSnap = service.GetLatestColorizedSnapshot();
        using var dispSnap = service.GetLatestDisparitySnapshot();

        Assert.NotNull(colorSnap);
        Assert.NotNull(dispSnap);
        Assert.False(colorSnap!.IsEmpty);
        Assert.False(dispSnap!.IsEmpty);
        Assert.Equal(100, dispSnap.Rows);
        Assert.Equal(100, dispSnap.Cols);
    }

    // ── Test 2: ExportPly writes valid PLY header and correct vertex count ──

    [Fact]
    public void ExportPly_ValidFormat_WritesCorrectHeader()
    {
        // Arrange: 2×3 image = 6 pixels, all valid z
        var points = new Mat(2, 3, DepthType.Cv32F, 3);
        _disposables.Add(points);
        var data = new float[]
        {
            1f, 2f, 5f,    // pixel (0,0)
            3f, 4f, 10f,   // pixel (0,1)
            5f, 6f, 15f,   // pixel (0,2)
            7f, 8f, 20f,   // pixel (1,0)
            9f, 10f, 25f,  // pixel (1,1)
            11f, 12f, 30f  // pixel (1,2)
        };
        Marshal.Copy(data, 0, points.DataPointer, data.Length);

        var color = new Mat(2, 3, DepthType.Cv8U, 3);
        _disposables.Add(color);
        color.SetTo(new MCvScalar(255, 128, 64)); // B=255, G=128, R=64

        var tempFile = Path.GetTempFileName();
        try
        {
            // Act
            DepthMapProcessor.ExportPly(tempFile, points, color);

            // Assert
            var lines = File.ReadAllLines(tempFile);
            Assert.Equal("ply", lines[0]);
            Assert.Equal("format ascii 1.0", lines[1]);
            Assert.Equal("element vertex 6", lines[2]);
            Assert.Contains("property float x", lines);
            Assert.Contains("property float y", lines);
            Assert.Contains("property float z", lines);
            Assert.Contains("property uchar red", lines);
            Assert.Contains("property uchar green", lines);
            Assert.Contains("property uchar blue", lines);
            Assert.Contains("end_header", lines);

            // 10 header lines + 6 vertex lines
            Assert.Equal(10 + 6, lines.Length);

            // Verify first vertex data contains expected x y z values
            var firstVertex = lines[10].Split(' ');
            Assert.Equal(6, firstVertex.Length); // x y z r g b
            Assert.True(float.TryParse(firstVertex[0], CultureInfo.InvariantCulture, out _));
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    // ── Test 3: ExportPly filters out invalid z values ──

    [Fact]
    public void ExportPly_FiltersInvalidPoints()
    {
        // Arrange: 1×4 image = 4 pixels, only 1 valid
        var points = new Mat(1, 4, DepthType.Cv32F, 3);
        _disposables.Add(points);
        var data = new float[]
        {
            1f, 2f, 5f,                        // valid (z=5)
            3f, 4f, -1f,                       // invalid (z<0)
            5f, 6f, float.PositiveInfinity,    // invalid (infinity)
            7f, 8f, 0f                         // invalid (z=0)
        };
        Marshal.Copy(data, 0, points.DataPointer, data.Length);

        var tempFile = Path.GetTempFileName();
        try
        {
            // Act
            DepthMapProcessor.ExportPly(tempFile, points, null);

            // Assert — only 1 valid vertex
            var lines = File.ReadAllLines(tempFile);
            Assert.Equal("element vertex 1", lines[2]);
            Assert.Equal(10 + 1, lines.Length);

            // Null colorImage → gray (128,128,128)
            var vertex = lines[10].Split(' ');
            Assert.Equal("128", vertex[3]);
            Assert.Equal("128", vertex[4]);
            Assert.Equal("128", vertex[5]);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    // ── Test 4: SaveSnapshotToPath creates PNG (TIFF requires full-res disparity) ──

    [Fact]
    public void SaveSnapshotToPath_CreatesPng()
    {
        // Arrange — process a frame BEFORE creating VM to avoid Dispatcher issues
        var service = CreateService(enabled: true);

        using var ready = new ManualResetEventSlim(false);
        service.ProcessOverride = (left, right) =>
        {
            var disparity = new Mat(left.Size, DepthType.Cv16S, 1);
            disparity.SetTo(new MCvScalar(500));
            var colorized = new Mat(left.Size, DepthType.Cv8U, 3);
            colorized.SetTo(new MCvScalar(128, 64, 32));
            return (disparity, colorized);
        };
        service.OnDepthMapReady += _ => ready.Set();
        service.ProcessStereoFrame(MakeTestPair());
        Assert.True(ready.Wait(3000), "Depth map result did not arrive in time");

        // Unsubscribe service events before creating VM to avoid RunOnUiThread
        service.OnDepthMapReady += _ => { };

        var settings = new DepthMapSettings();
        var vm = new DepthMapViewModel(service, settings, new SettingsService());
        _disposables.Add(vm);

        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        try
        {
            // Act
            var result = vm.SaveSnapshotToPath(tempDir);

            // Assert — PNG created from colorized snapshot
            Assert.NotNull(result);
            Assert.True(File.Exists(result));
            Assert.EndsWith(".png", result!);

            // TIFF is NOT created because ComputeFullResolutionDisparity() returns null
            // (ProcessOverride does not cache rectified images for full-res recompute)
            var tiffPath = Path.ChangeExtension(result, ".tiff");
            Assert.False(File.Exists(tiffPath));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    // ── Test: ExportPly filters sentinel Z=10000 when maxDepth=10000 ──

    [Fact]
    public void ExportPly_FiltersSentinelDepth_ExcludesZEquals10000()
    {
        // Arrange: 1×2 image, point 0 valid (z=5), point 1 sentinel (z=10000)
        var points = new Mat(1, 2, DepthType.Cv32F, 3);
        _disposables.Add(points);
        var data = new float[]
        {
            1f, 2f, 5f,        // valid point
            3f, 4f, 10000f     // sentinel invalid point (handleMissingValues=true)
        };
        Marshal.Copy(data, 0, points.DataPointer, data.Length);

        var tempFile = Path.GetTempFileName();
        try
        {
            // Act
            DepthMapProcessor.ExportPly(tempFile, points, null, maxDepth: 10000.0);

            // Assert — only 1 valid vertex; sentinel Z=10000 must be excluded
            var lines = File.ReadAllLines(tempFile);
            Assert.Equal("element vertex 1", lines[2]);
            Assert.Equal(10 + 1, lines.Length);

            // The single vertex must be the valid point (z=5), not the sentinel
            var vertex = lines[10].Split(' ');
            var z = float.Parse(vertex[2], CultureInfo.InvariantCulture);
            Assert.Equal(5f, z);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    // ── Test: ComputeFullResolutionDisparity returns null when no data ──

    [Fact]
    public void ComputeFullResolutionDisparity_WhenNoData_ReturnsNull()
    {
        // Arrange — service created, no frames processed → no rectified images
        var service = CreateService(enabled: true);

        // Act
        var result = service.ComputeFullResolutionDisparity();

        // Assert
        Assert.Null(result);
    }

    // ── Test: ExportPointCloudToPath returns false when Q matrix is missing ──

    [Fact]
    public void ExportPointCloudToPath_NoQMatrix_ReturnsFalse()
    {
        // Arrange — process a frame to get disparity, but no LoadCalibration → no Q matrix
        var service = CreateService(enabled: true);
        using var ready = new ManualResetEventSlim(false);
        service.ProcessOverride = (left, right) =>
        {
            var disparity = new Mat(left.Size, DepthType.Cv16S, 1);
            disparity.SetTo(new MCvScalar(500));
            var colorized = new Mat(left.Size, DepthType.Cv8U, 3);
            colorized.SetTo(new MCvScalar(128, 64, 32));
            return (disparity, colorized);
        };
        service.OnDepthMapReady += _ => ready.Set();
        service.ProcessStereoFrame(MakeTestPair());
        Assert.True(ready.Wait(1500), "Depth map result did not arrive in time");

        // Unsubscribe service events before creating VM to avoid RunOnUiThread
        service.OnDepthMapReady += _ => { };

        var settings = new DepthMapSettings();
        var vm = new DepthMapViewModel(service, settings, new SettingsService());
        _disposables.Add(vm);

        var tempFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".ply");
        try
        {
            // Act
            var result = vm.ExportPointCloudToPath(tempFile);

            // Assert — no Q matrix → export should fail
            Assert.False(result);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    // ── Test: ExportPointCloudToPath creates PLY file when Q matrix is available ──

    [Fact]
    public void ExportPointCloudToPath_WithValidData_CreatesFile()
    {
        // Arrange — use ProcessOverride + inject Q matrix and rectified images
        var service = CreateService(enabled: true);

        using var ready = new ManualResetEventSlim(false);
        service.ProcessOverride = (left, right) =>
        {
            var disparity = new Mat(left.Size, DepthType.Cv16S, 1);
            disparity.SetTo(new MCvScalar(500));
            var colorized = new Mat(left.Size, DepthType.Cv8U, 3);
            colorized.SetTo(new MCvScalar(128, 64, 32));
            return (disparity, colorized);
        };
        service.OnDepthMapReady += _ => ready.Set();
        service.ProcessStereoFrame(MakeTestPair());
        Assert.True(ready.Wait(3000), "Depth map result did not arrive in time");

        // Inject Q matrix and rectified images for ComputeFullResolutionDisparity
        using var q = new Mat(4, 4, DepthType.Cv64F, 1);
        CvInvoke.SetIdentity(q, new MCvScalar(1));
        service.InjectQMatrix(q);

        using var fakeRect = new Mat(100, 100, DepthType.Cv8U, 3);
        fakeRect.SetTo(new MCvScalar(128));
        service.InjectRectifiedImages(fakeRect, fakeRect);

        // Unsubscribe service events before creating VM to avoid RunOnUiThread
        service.OnDepthMapReady += _ => { };

        var settings = new DepthMapSettings();
        var vm = new DepthMapViewModel(service, settings, new SettingsService());
        _disposables.Add(vm);

        var tempFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".ply");
        try
        {
            // Act
            var result = vm.ExportPointCloudToPath(tempFile);

            // Assert — Q matrix available → export should succeed
            Assert.True(result);
            Assert.True(File.Exists(tempFile));

            var lines = File.ReadAllLines(tempFile);
            Assert.True(lines.Length >= 10, "PLY file should have at least a header");
            Assert.Equal("ply", lines[0]);
            Assert.Equal("format ascii 1.0", lines[1]);
            Assert.StartsWith("element vertex", lines[2]);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    // ── Test: SaveSnapshotToPath skips TIFF when only real-time snapshot is available ──

    [Fact]
    public void SaveSnapshotToPath_WhenNoFullResDisparity_SkipsTiff()
    {
        // Arrange — process a frame BEFORE creating VM to avoid Dispatcher issues
        var service = CreateService(enabled: true);
        using var ready = new ManualResetEventSlim(false);
        service.ProcessOverride = (left, right) =>
        {
            var disparity = new Mat(left.Size, DepthType.Cv16S, 1);
            disparity.SetTo(new MCvScalar(500));
            var colorized = new Mat(left.Size, DepthType.Cv8U, 3);
            colorized.SetTo(new MCvScalar(128, 64, 32));
            return (disparity, colorized);
        };
        service.OnDepthMapReady += _ => ready.Set();
        service.ProcessStereoFrame(MakeTestPair());
        Assert.True(ready.Wait(1500), "Depth map result did not arrive in time");

        service.OnDepthMapReady += _ => { };

        var settings = new DepthMapSettings();
        var vm = new DepthMapViewModel(service, settings, new SettingsService());
        _disposables.Add(vm);

        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        try
        {
            // Act
            var result = vm.SaveSnapshotToPath(tempDir);

            // Assert — PNG exists (colorized snapshot is available)
            Assert.NotNull(result);
            Assert.True(File.Exists(result));
            Assert.EndsWith(".png", result!);

            // TIFF should NOT exist because ComputeFullResolutionDisparity() returns null
            // and we no longer fallback to GetLatestDisparitySnapshot()
            var tiffPath = Path.ChangeExtension(result, ".tiff");
            Assert.False(File.Exists(tiffPath), "TIFF should NOT be created when only real-time snapshot is available");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    // ── Test: PLY export returns false when Q is present but no full-res disparity ──

    [Fact]
    public void ExportPointCloudToPath_QMatrixPresentButNoFullResDisparity_ReturnsFalse()
    {
        // Arrange — process a frame (populates real-time snapshot) but don't inject rectified images
        var service = CreateService(enabled: true);
        using var ready = new ManualResetEventSlim(false);
        service.ProcessOverride = (left, right) =>
        {
            var disparity = new Mat(left.Size, DepthType.Cv16S, 1);
            disparity.SetTo(new MCvScalar(500));
            var colorized = new Mat(left.Size, DepthType.Cv8U, 3);
            colorized.SetTo(new MCvScalar(128, 64, 32));
            return (disparity, colorized);
        };
        service.OnDepthMapReady += _ => ready.Set();
        service.ProcessStereoFrame(MakeTestPair());
        Assert.True(ready.Wait(3000), "Depth map result did not arrive in time");

        // Inject Q matrix (present) but do NOT inject rectified images
        // → ComputeFullResolutionDisparity returns null, proving no fallback to snapshot
        using var q = new Mat(4, 4, DepthType.Cv64F, 1);
        CvInvoke.SetIdentity(q, new MCvScalar(1));
        service.InjectQMatrix(q);

        service.OnDepthMapReady += _ => { };

        var settings = new DepthMapSettings();
        var vm = new DepthMapViewModel(service, settings, new SettingsService());
        _disposables.Add(vm);

        var tempFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".ply");
        try
        {
            // Act
            var result = vm.ExportPointCloudToPath(tempFile);

            // Assert — Q is available but full-res disparity is not → must return false
            Assert.False(result, "Export must not succeed without full-resolution disparity");
            Assert.False(File.Exists(tempFile), "PLY file must not be created");
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    // ── Test 5: ExportPly with null color writes gray vertices ──

    [Fact]
    public void ExportPly_NullColor_WritesGrayVertices()
    {
        // Arrange: 1×2 image = 2 pixels, both valid
        var points = new Mat(1, 2, DepthType.Cv32F, 3);
        _disposables.Add(points);
        var data = new float[]
        {
            1f, 2f, 5f,   // valid
            3f, 4f, 10f   // valid
        };
        Marshal.Copy(data, 0, points.DataPointer, data.Length);

        var tempFile = Path.GetTempFileName();
        try
        {
            // Act
            DepthMapProcessor.ExportPly(tempFile, points, null);

            // Assert — all vertices should have gray color
            var lines = File.ReadAllLines(tempFile);
            Assert.Equal("element vertex 2", lines[2]);

            for (int i = 10; i < lines.Length; i++)
            {
                var parts = lines[i].Split(' ');
                Assert.Equal("128", parts[3]);
                Assert.Equal("128", parts[4]);
                Assert.Equal("128", parts[5]);
            }
        }
        finally
        {
            File.Delete(tempFile);
        }
    }
}
