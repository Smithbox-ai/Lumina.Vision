using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using LuminaCalib.Services;
using LuminaCalib.ViewModels;

namespace LuminaCalib.Views;

/// <summary>
/// Представление карты глубины: отображение изображения, настройки SGBM/WLS
/// и строка статуса с метриками производительности.
/// </summary>
/// <remarks>
/// Код-бихайнд подписывается на события ViewModel для открытия файловых диалогов.
/// </remarks>
public partial class DepthMapView : UserControl
{
    /// <summary>Ссылка на ViewModel для управления подпиской на события.</summary>
    private DepthMapViewModel? _vm;

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="DepthMapView"/>.
    /// </summary>
    public DepthMapView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_vm != null)
        {
            _vm.RequestSaveSnapshot -= OnRequestSaveSnapshot;
            _vm.RequestExportPointCloud -= OnRequestExportPointCloud;
            _vm.DepthFramePresented -= OnDepthFramePresented;
        }

        _vm = DataContext as DepthMapViewModel;

        if (_vm != null)
        {
            _vm.RequestSaveSnapshot += OnRequestSaveSnapshot;
            _vm.RequestExportPointCloud += OnRequestExportPointCloud;
            _vm.DepthFramePresented += OnDepthFramePresented;
        }
    }

    /// <summary>
    /// Инвалидирует визуализацию Image с depth-картой.
    /// Нужен для стабильного обновления при переиспользовании одного WriteableBitmap.
    /// </summary>
    private void OnDepthFramePresented()
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            DepthMapImageControl.InvalidateVisual();
            return;
        }

        Dispatcher.UIThread.Post(
            () => DepthMapImageControl.InvalidateVisual(),
            DispatcherPriority.Background);
    }

    private async void OnRequestSaveSnapshot()
    {
        try
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null || _vm == null) return;

            var folder = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Save Depth Map Snapshot",
                AllowMultiple = false
            });

            if (folder.Count > 0)
            {
                var path = folder[0].Path.LocalPath;
                _vm.SaveSnapshotToPath(path);
            }
        }
        catch (Exception ex)
        {
            ExceptionLogger.LogException(ex, "DepthMapView.SaveSnapshot");
        }
    }

    private async void OnRequestExportPointCloud()
    {
        try
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null || _vm == null) return;

            var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export Point Cloud",
                SuggestedFileName = $"pointcloud_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.ply",
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("PLY Point Cloud") { Patterns = new[] { "*.ply" } }
                }
            });

            if (file != null)
            {
                _vm.ExportPointCloudToPath(file.Path.LocalPath);
            }
        }
        catch (Exception ex)
        {
            ExceptionLogger.LogException(ex, "DepthMapView.ExportPointCloud");
        }
    }
}
