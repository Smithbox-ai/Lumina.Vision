using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Emgu.CV;
using LuminaCalib.Models;
using LuminaCalib.Services;

namespace LuminaCalib.ViewModels;

public partial class MainWindowViewModel
{
    private readonly object _screenshotLock = new();
    private TaskCompletionSource<StereoFramePair>? _pendingScreenshot;
    private bool _screenshotsDisposed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ScreenshotButtonText))]
    [NotifyPropertyChangedFor(nameof(ScreenshotHintText))]
    [NotifyCanExecuteChangedFor(nameof(SaveScreenshotCommand))]
    private bool _isSavingScreenshot;

    /// <summary>Whether the live rectified stereo snapshot action is available.</summary>
    public bool CanSaveScreenshot => !_screenshotsDisposed && IsCalibrationView && !IsSettingsOpen
        && !UseSavedSessionForCalibration && AreCamerasConnected && ShowRectified && !IsSavingScreenshot
        && !string.IsNullOrWhiteSpace(GetActiveCalibrationPath(SelectedCalibrationMode));

    /// <summary>Localized snapshot button label, including progress.</summary>
    public string ScreenshotButtonText => IsSavingScreenshot
        ? L("Saving…", "Сохранение…") : L("Stereo snapshot", "Снимок стереопары");
    /// <summary>Localized label for opening the screenshots directory.</summary>
    public string OpenScreenshotsFolderText => L("Open folder", "Открыть папку");
    /// <summary>Explains snapshot prerequisites, keyboard shortcut, or current progress.</summary>
    public string ScreenshotHintText => IsSavingScreenshot
        ? L("Waiting for a synchronized pair and saving PNGs…", "Ожидание синхронной пары и сохранение PNG…")
        : !AreCamerasConnected
            ? L("Connect both cameras to take a snapshot.", "Для снимка подключите обе камеры.")
            : !ShowRectified
                ? L("Enable rectified preview to take a snapshot.", "Для снимка включите ректификацию.")
                : string.IsNullOrWhiteSpace(GetActiveCalibrationPath(SelectedCalibrationMode))
                    ? L("Select an active calibration in Calibration Manager.", "Выберите активную калибровку в менеджере калибровок.")
                    : L("Ctrl+Shift+S · Two full-resolution PNGs, without overlays", "Ctrl+Shift+S · Два PNG в исходном разрешении, без разметки");

    private void RefreshScreenshotUi()
    {
        OnPropertyChanged(nameof(CanSaveScreenshot));
        OnPropertyChanged(nameof(ScreenshotButtonText));
        OnPropertyChanged(nameof(OpenScreenshotsFolderText));
        OnPropertyChanged(nameof(ScreenshotHintText));
        SaveScreenshotCommand.NotifyCanExecuteChanged();
    }

    private void OnScreenshotStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(LeftCameraConnected) or nameof(RightCameraConnected)
            or nameof(ShowRectified) or nameof(IsCalibrationView) or nameof(IsSettingsOpen)
            or nameof(UseSavedSessionForCalibration))
        {
            CancelScreenshotWait();
            RefreshScreenshotUi();
        }
    }

    private void CancelScreenshotWait()
    {
        lock (_screenshotLock)
        {
            _pendingScreenshot?.TrySetCanceled();
            _pendingScreenshot = null;
        }
    }

    private void SupplyScreenshotPair(StereoFramePair pair)
    {
        lock (_screenshotLock)
        {
            if (_pendingScreenshot is not { } request)
                return;
            _pendingScreenshot = null;
            try
            {
                var copy = pair.Clone();
                if (!request.TrySetResult(copy))
                    copy.Dispose();
            }
            catch (Exception ex)
            {
                request.TrySetException(ex);
            }
        }
    }

    [RelayCommand(CanExecute = nameof(CanSaveScreenshot))]
    private async Task SaveScreenshotAsync()
    {
        if (!CanSaveScreenshot)
            return;

        IsSavingScreenshot = true;
        var request = new TaskCompletionSource<StereoFramePair>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calibrationPath = GetActiveCalibrationPath(SelectedCalibrationMode);
        var root = _settingsService.Settings.ScreenshotsPath;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        try
        {
            lock (_screenshotLock)
                _pendingScreenshot = request;
            using var registration = timeout.Token.Register(CancelScreenshotWait);
            using var pair = await request.Task;
            var savedFolder = await Task.Run(() =>
            {
                using var left = new Mat();
                using var right = new Mat();
                lock (_rectificationLock)
                {
                    if (_screenshotsDisposed || !ShowRectified || !AreCamerasConnected
                        || calibrationPath != GetActiveCalibrationPath(SelectedCalibrationMode))
                        throw new OperationCanceledException();
                    if (!TryEnsureRectifierByPath(calibrationPath, out var error))
                        throw new InvalidOperationException(error);
                    if (_previewCalibration == null || !HasCompleteScreenshotCalibration(_previewCalibration))
                        throw new InvalidOperationException(L("The active calibration has missing or invalid stereo matrices.", "В активной калибровке отсутствуют или повреждены стереоматрицы."));
                    if (pair.Left.Image.Size != _previewCalibration.ImageSize
                        || pair.Right.Image.Size != _previewCalibration.ImageSize)
                        throw new InvalidOperationException(L("Frame dimensions do not match the active calibration.", "Размеры кадров не совпадают с активной калибровкой."));
                    _previewRectifier.Rectify(pair.Left.Image, pair.Right.Image, left, right);
                }
                return ScreenshotService.Save(left, right, root, pair.Timestamp);
            });
            if (!_screenshotsDisposed)
                NotificationService.Instance.ShowSuccess($"{L("Stereo snapshot saved", "Снимок стереопары сохранён")}: {savedFolder}");
        }
        catch (OperationCanceledException)
        {
            if (!_screenshotsDisposed)
                NotificationService.Instance.ShowWarning(timeout.IsCancellationRequested
                    ? L("No synchronized pair received. Check both camera streams.", "Синхронная пара не получена. Проверьте потоки обеих камер.")
                    : L("Snapshot cancelled: camera or preview settings changed.", "Снимок отменён: изменились камеры или настройки просмотра."));
        }
        catch (Exception ex)
        {
            ExceptionLogger.LogException(ex, "SaveScreenshot");
            if (!_screenshotsDisposed)
                NotificationService.Instance.ShowError($"{L("Could not save snapshot", "Не удалось сохранить снимок")}: {ex.Message}");
        }
        finally
        {
            CancelScreenshotWait();
            IsSavingScreenshot = false;
        }
    }

    private static bool HasCompleteScreenshotCalibration(CalibrationResult calibration) =>
        IsScreenshotMatrixValid(calibration.CameraMatrixLeft, 3, 3)
        && IsScreenshotMatrixValid(calibration.CameraMatrixRight, 3, 3)
        && IsScreenshotMatrixValid(calibration.R1, 3, 3)
        && IsScreenshotMatrixValid(calibration.R2, 3, 3)
        && IsScreenshotMatrixValid(calibration.P1, 3, 4)
        && IsScreenshotMatrixValid(calibration.P2, 3, 4)
        && IsScreenshotDistortionValid(calibration.DistCoeffsLeft)
        && IsScreenshotDistortionValid(calibration.DistCoeffsRight);

    private static bool IsScreenshotDistortionValid(Mat matrix) =>
        (matrix.Rows == 1 || matrix.Cols == 1)
        && matrix.Rows * matrix.Cols is 4 or 5 or 8 or 12 or 14
        && IsScreenshotMatrixValid(matrix, matrix.Rows, matrix.Cols);

    private static bool IsScreenshotMatrixValid(Mat matrix, int rows, int columns)
    {
        if (matrix.IsEmpty || matrix.Rows != rows || matrix.Cols != columns || matrix.NumberOfChannels != 1
            || matrix.Depth is not (Emgu.CV.CvEnum.DepthType.Cv32F or Emgu.CV.CvEnum.DepthType.Cv64F))
            return false;
        var position = new System.Drawing.Point();
        return CvInvoke.CheckRange(matrix, true, ref position, -double.MaxValue, double.MaxValue);
    }

    [RelayCommand]
    private void OpenScreenshotsFolder()
    {
        try
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(_settingsService.Settings.ScreenshotsPath);
            var path = Path.GetFullPath(_settingsService.Settings.ScreenshotsPath);
            Directory.CreateDirectory(path);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = path, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            NotificationService.Instance.ShowError($"{L("Could not open screenshots folder", "Не удалось открыть папку снимков")}: {ex.Message}");
        }
    }
}
