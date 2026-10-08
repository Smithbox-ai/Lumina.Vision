using System.Runtime.InteropServices;
using Emgu.CV;
using Emgu.CV.Aruco;
using Emgu.CV.Util;

namespace LuminaCalib.Calibration;

/// <summary>Bridges the missing managed DetectBoard method in EmguCV 5.0.0.6584.</summary>
internal static class EmguCharucoDetection
{
    // Signature from Emgu.CV.Extern/objdetect/aruco_c.h at package commit
    // 4f8dc1bf4bb562e396dedf9badaa1856deb68c29. Remove when Emgu exposes DetectBoard.
    [DllImport(CvInvoke.ExternLibrary, CallingConvention = CvInvoke.CvCallingConvention,
        EntryPoint = "cveCharucoDetectorDetectBoard")]
    private static extern void DetectBoardNative(IntPtr detector, IntPtr image,
        IntPtr corners, IntPtr ids, IntPtr markerCorners, IntPtr markerIds);

    internal static void DetectBoard(CharucoDetector detector, Mat image,
        VectorOfPointF corners, VectorOfInt ids)
    {
        using var markerCorners = new VectorOfVectorOfPointF();
        using var markerIds = new VectorOfInt();
        using var input = image.GetInputArray();
        using var outputCorners = corners.GetOutputArray();
        using var outputIds = ids.GetOutputArray();
        using var inputOutputCorners = markerCorners.GetInputOutputArray();
        using var inputOutputIds = markerIds.GetInputOutputArray();
        DetectBoardNative(detector.Ptr, input.Ptr, outputCorners.Ptr, outputIds.Ptr,
            inputOutputCorners.Ptr, inputOutputIds.Ptr);
        CvInvoke.CheckError();
        GC.KeepAlive(detector);
    }
}
