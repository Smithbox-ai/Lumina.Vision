using System.Globalization;
using Emgu.CV;

namespace LuminaCalib.Services;

/// <summary>Saves a full-resolution stereo pair, publishing the folder only after both PNGs are written.</summary>
public static class ScreenshotService
{
    /// <summary>Writes two PNGs to a unique capture folder and removes staging files on failure.</summary>
    public static string Save(Mat left, Mat right, string rootPath, DateTime timestamp, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        if (left.IsEmpty || right.IsEmpty || left.Size != right.Size)
            throw new ArgumentException("Screenshot images must be non-empty and have matching dimensions.");

        cancellationToken.ThrowIfCancellationRequested();
        var root = Path.GetFullPath(rootPath);
        Directory.CreateDirectory(root);
        var name = timestamp.ToLocalTime().ToString("yyyy-MM-dd_HH-mm-ss-fff", CultureInfo.InvariantCulture) + "_" + Guid.NewGuid().ToString("N")[..8];
        var destination = Path.Combine(root, name);
        var staging = Path.Combine(root, "." + name + ".pending");
        Directory.CreateDirectory(staging);
        var leftPath = Path.Combine(staging, "left_rectified.png");
        var rightPath = Path.Combine(staging, "right_rectified.png");
        try
        {
            // Managed file I/O preserves Unicode folder names on all supported platforms.
            File.WriteAllBytes(leftPath, CvInvoke.Imencode(".png", left));
            cancellationToken.ThrowIfCancellationRequested();
            File.WriteAllBytes(rightPath, CvInvoke.Imencode(".png", right));
            cancellationToken.ThrowIfCancellationRequested();
            Directory.Move(staging, destination);
            return destination;
        }
        finally
        {
            if (Directory.Exists(staging))
            {
                File.Delete(leftPath);
                File.Delete(rightPath);
                Directory.Delete(staging);
            }
        }
    }
}
