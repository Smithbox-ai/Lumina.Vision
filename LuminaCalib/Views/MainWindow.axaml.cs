using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Transformation;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using LuminaCalib.Controls;
using LuminaCalib.Models;
using LuminaCalib.Services;
using LuminaCalib.ViewModels;
using System.ComponentModel;
using System.Drawing;

namespace LuminaCalib.Views
{
    /// <summary>
    /// Главное окно приложения LuminaCalib.
    /// </summary>
    /// <remarks>
    /// Отвечает за отображение видеопотоков левой и правой камер, обработку событий
    /// ViewModel и потокобезопасную очередь кадров для предпросмотра.
    /// Фреймы поступают из фоновых потоков камер, клонируются, очередь ограничена
    /// одним ожидающим кадром (latest-frame стратегия), рендеринг выполняется в UI-потоке.
    /// </remarks>
    public partial class MainWindow : Window
    {
        /// <summary>Ссылка на предыдущую ViewModel для отписки от событий.</summary>
        private MainWindowViewModel? _previousViewModel;

        /// <summary>Блокировка для ожидающего левого кадра.</summary>
        private readonly object _leftPreviewLock = new();
        /// <summary>Блокировка для ожидающего правого кадра.</summary>
        private readonly object _rightPreviewLock = new();

        /// <summary>Ожидающий левый кадр для рендеринга.</summary>
        private FrameRaw? _pendingLeftFrame;
        /// <summary>Ожидающий правый кадр для рендеринга.</summary>
        private FrameRaw? _pendingRightFrame;

        /// <summary>Флаг: запланирован ли рендеринг левого кадра (0/1).</summary>
        private int _leftPreviewRenderScheduled;
        /// <summary>Флаг: запланирован ли рендеринг правого кадра (0/1).</summary>
        private int _rightPreviewRenderScheduled;

        /// <summary>
        /// Инициализирует новый экземпляр <see cref="MainWindow"/>.
        /// </summary>
        public MainWindow()
        {
            InitializeComponent();

            // Подписываемся на события SettingsViewModel
            DataContextChanged += OnDataContextChanged;


        }



        /// <summary>
        /// Обрабатывает смену DataContext — переподписывается на события новой ViewModel.
        /// </summary>
        private void OnDataContextChanged(object? sender, EventArgs e)
        {
            // Отписываемся от предыдущей ViewModel для предотвращения утечек памяти
            if (_previousViewModel != null)
            {
                _previousViewModel.SettingsVm.RequestFolderPicker -= OnRequestFolderPicker;
                _previousViewModel.SettingsVm.RequestCaptureSessionsFolderPicker -= OnRequestCaptureSessionsFolderPicker;
                _previousViewModel.SettingsVm.RequestNavigateToBoardGenerator -= OnRequestNavigateToBoardGenerator;
                _previousViewModel.SettingsVm.RequestClose -= OnRequestCloseSettings;
                _previousViewModel.OnLeftFrameReceived -= OnLeftFrameReceived;
                _previousViewModel.OnRightFrameReceived -= OnRightFrameReceived;
                _previousViewModel.OnDetectionOverlayUpdate -= OnDetectionOverlayUpdate;
                _previousViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            }

            if (DataContext is MainWindowViewModel vm)
            {
                // Подписываемся на события SettingsVm
                vm.SettingsVm.RequestFolderPicker += OnRequestFolderPicker;
                vm.SettingsVm.RequestCaptureSessionsFolderPicker += OnRequestCaptureSessionsFolderPicker;
                vm.SettingsVm.RequestNavigateToBoardGenerator += OnRequestNavigateToBoardGenerator;
                vm.SettingsVm.RequestClose += OnRequestCloseSettings;
                vm.RequestOpenAbout += OnRequestOpenAbout;
                vm.OnLeftFrameReceived += OnLeftFrameReceived;
                vm.OnRightFrameReceived += OnRightFrameReceived;
                vm.OnDetectionOverlayUpdate += OnDetectionOverlayUpdate;
                vm.PropertyChanged += OnViewModelPropertyChanged;

                // Wire confirmation dialogs for delete actions
                vm.CalibrationManagerVm.RequestConfirmation = (title, message, confirmText, cancelText) =>
                    ConfirmationDialog.ShowAsync(this, title, message, confirmText, cancelText);

                _previousViewModel = vm;
                UpdateVideoConnectionStatus();
            }
            else
            {
                _previousViewModel = null;
            }

            DisposePendingPreviewFrames();
        }

        /// <summary>
        /// Открывает диалог выбора папки калибровочных данных.
        /// </summary>
        private async void OnRequestFolderPicker()
        {
            try
            {
                if (DataContext is not MainWindowViewModel vm) return;

                var folder = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
                {
                    Title = IsEnglish(vm)
                        ? "Select Calibration Data Folder"
                        : "Выберите папку калибровок",
                    AllowMultiple = false
                });

                if (folder.Count > 0)
                {
                    var path = folder[0].Path.LocalPath;
                    vm.SettingsVm.Settings.CalibrationDataPath = path;
                }
            }
            catch (Exception ex)
            {
                ExceptionLogger.LogException(ex, "MainWindow.FolderPicker");
            }
        }

        /// <summary>
        /// Открывает диалог выбора папки сессий захвата.
        /// </summary>
        private async void OnRequestCaptureSessionsFolderPicker()
        {
            try
            {
                if (DataContext is not MainWindowViewModel vm) return;

                var folder = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
                {
                    Title = IsEnglish(vm)
                        ? "Select Capture Sessions Folder"
                        : "Выберите папку сессий снимков",
                    AllowMultiple = false
                });

                if (folder.Count > 0)
                {
                    vm.SettingsVm.Settings.CaptureSessionsPath = folder[0].Path.LocalPath;
                }
            }
            catch (Exception ex)
            {
                ExceptionLogger.LogException(ex, "MainWindow.CaptureSessionsFolderPicker");
            }
        }

        /// <summary>
        /// Обработчик получения левого кадра от ViewModel.
        /// </summary>
        private void OnLeftFrameReceived(FrameRaw frame)
        {
            QueueLeftFrame(frame);
        }

        /// <summary>
        /// Обработчик получения правого кадра от ViewModel.
        /// </summary>
        private void OnRightFrameReceived(FrameRaw frame)
        {
            QueueRightFrame(frame);
        }

        /// <summary>
        /// Помещает клон левого кадра в очередь и планирует рендеринг в UI-потоке.
        /// </summary>
        /// <param name="frame">Исходный кадр (клонируется внутри).</param>
        private void QueueLeftFrame(FrameRaw frame)
        {
            var frameCopy = frame.Clone();
            FrameRaw? staleFrame;
            lock (_leftPreviewLock)
            {
                staleFrame = _pendingLeftFrame;
                _pendingLeftFrame = frameCopy;
            }

            staleFrame?.Dispose();

            if (Interlocked.Exchange(ref _leftPreviewRenderScheduled, 1) == 0)
            {
                Dispatcher.UIThread.Post(RenderPendingLeftPreviewFrame, DispatcherPriority.Background);
            }
        }

        /// <summary>
        /// Помещает клон правого кадра в очередь и планирует рендеринг в UI-потоке.
        /// </summary>
        /// <param name="frame">Исходный кадр (клонируется внутри).</param>
        private void QueueRightFrame(FrameRaw frame)
        {
            var frameCopy = frame.Clone();
            FrameRaw? staleFrame;
            lock (_rightPreviewLock)
            {
                staleFrame = _pendingRightFrame;
                _pendingRightFrame = frameCopy;
            }

            staleFrame?.Dispose();

            if (Interlocked.Exchange(ref _rightPreviewRenderScheduled, 1) == 0)
            {
                Dispatcher.UIThread.Post(RenderPendingRightPreviewFrame, DispatcherPriority.Background);
            }
        }

        /// <summary>
        /// Извлекает ожидающий левый кадр и отправляет его на рендеринг.
        /// </summary>
        private void RenderPendingLeftPreviewFrame()
        {
            try
            {
                if (TryTakePendingLeftFrame(out var frame))
                {
                    RenderPreviewFrame(frame, isLeft: true);
                }
            }
            finally
            {
                Interlocked.Exchange(ref _leftPreviewRenderScheduled, 0);
            }

            if (HasPendingLeftFrame() && Interlocked.Exchange(ref _leftPreviewRenderScheduled, 1) == 0)
            {
                Dispatcher.UIThread.Post(RenderPendingLeftPreviewFrame, DispatcherPriority.Background);
            }
        }

        /// <summary>
        /// Извлекает ожидающий правый кадр и отправляет его на рендеринг.
        /// </summary>
        private void RenderPendingRightPreviewFrame()
        {
            try
            {
                if (TryTakePendingRightFrame(out var frame))
                {
                    RenderPreviewFrame(frame, isLeft: false);
                }
            }
            finally
            {
                Interlocked.Exchange(ref _rightPreviewRenderScheduled, 0);
            }

            if (HasPendingRightFrame() && Interlocked.Exchange(ref _rightPreviewRenderScheduled, 1) == 0)
            {
                Dispatcher.UIThread.Post(RenderPendingRightPreviewFrame, DispatcherPriority.Background);
            }
        }

        /// <summary>
        /// Потокобезопасно извлекает ожидающий левый кадр.
        /// </summary>
        /// <param name="frame">Извлечённый кадр (владелец отвечает за Dispose).</param>
        /// <returns><c>true</c>, если кадр был доступен.</returns>
        private bool TryTakePendingLeftFrame(out FrameRaw frame)
        {
            lock (_leftPreviewLock)
            {
                if (_pendingLeftFrame == null)
                {
                    frame = null!;
                    return false;
                }

                frame = _pendingLeftFrame;
                _pendingLeftFrame = null;
                return true;
            }
        }

        /// <summary>
        /// Потокобезопасно извлекает ожидающий правый кадр.
        /// </summary>
        /// <param name="frame">Извлечённый кадр (владелец отвечает за Dispose).</param>
        /// <returns><c>true</c>, если кадр был доступен.</returns>
        private bool TryTakePendingRightFrame(out FrameRaw frame)
        {
            lock (_rightPreviewLock)
            {
                if (_pendingRightFrame == null)
                {
                    frame = null!;
                    return false;
                }

                frame = _pendingRightFrame;
                _pendingRightFrame = null;
                return true;
            }
        }

        /// <summary>Проверяет, есть ли ожидающий левый кадр.</summary>
        private bool HasPendingLeftFrame()
        {
            lock (_leftPreviewLock)
            {
                return _pendingLeftFrame != null;
            }
        }

        /// <summary>Проверяет, есть ли ожидающий правый кадр.</summary>
        private bool HasPendingRightFrame()
        {
            lock (_rightPreviewLock)
            {
                return _pendingRightFrame != null;
            }
        }

        /// <summary>
        /// Отрисовывает кадр предпросмотра в соответствующем VideoView.
        /// </summary>
        /// <param name="frame">Сырой кадр для рендеринга.</param>
        /// <param name="isLeft"><c>true</c> для левой камеры, <c>false</c> для правой.</param>
        private void RenderPreviewFrame(FrameRaw frame, bool isLeft)
        {
            var vm = _previousViewModel;
            if (vm == null || !vm.IsCalibrationView)
            {
                frame.Dispose();
                return;
            }

            try
            {
                if (!vm.TryCreatePreviewFrame(frame, isLeft, out var preview, out var ownsPreview))
                {
                    frame.Dispose();
                    return;
                }

                try
                {
                    if (isLeft)
                    {
                        LeftVideoView.UpdateFrame(preview);
                    }
                    else
                    {
                        RightVideoView.UpdateFrame(preview);
                    }
                }
                catch (Exception ex)
                {
                    ExceptionLogger.LogException(ex, "MainWindow.RenderPreviewFrame");
                }
                finally
                {
                    if (ownsPreview)
                    {
                        preview.Dispose();
                    }

                    frame.Dispose();
                }
            }
            catch (Exception ex)
            {
                ExceptionLogger.LogException(ex, "MainWindow.RenderPreviewFrameOuter");
                frame.Dispose();
            }
        }

        /// <summary>
        /// Переходит к представлению генератора калибровочных шаблонов.
        /// </summary>
        private void OnRequestNavigateToBoardGenerator()
        {
            if (DataContext is MainWindowViewModel vm)
            {
                vm.IsSettingsOpen = false;
                vm.IsBoardGeneratorView = true;
            }
        }

        /// <summary>
        /// Закрывает панель настроек.
        /// </summary>
        private void OnRequestCloseSettings()
        {
            if (DataContext is MainWindowViewModel vm)
            {
                vm.IsSettingsOpen = false;
            }
        }

        /// <summary>
        /// Открывает окно «О программе» как модальный диалог.
        /// </summary>
        private async void OnRequestOpenAbout(object? sender, EventArgs e)
        {
            if (DataContext is MainWindowViewModel vm)
            {
                var aboutWindow = new AboutWindow(vm.SettingsVm.Settings);
                await aboutWindow.ShowDialog(this);
            }
        }

        /// <summary>
        /// Закрывает настройки при клике на подложку.
        /// </summary>
        private void OnBackdropPressed(object sender, PointerPressedEventArgs e)
        {
            // Закрываем настройки при клике на подложку
            if (DataContext is MainWindowViewModel vm)
            {
                vm.IsSettingsOpen = false;
            }
            e.Handled = true;
        }

        /// <summary>
        /// Определяет, выбран ли английский язык интерфейса.
        /// </summary>
        private static bool IsEnglish(MainWindowViewModel vm)
        {
            return string.Equals(
                vm.SettingsVm.Settings.Language,
                "en",
                StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Обрабатывает обновление наложения детекции углов на обоих VideoView.
        /// </summary>
        /// <param name="leftFound">Обнаружен ли шаблон на левой камере.</param>
        /// <param name="rightFound">Обнаружен ли шаблон на правой камере.</param>
        /// <param name="leftCorners">Углы левой камеры.</param>
        /// <param name="rightCorners">Углы правой камеры.</param>
        /// <param name="boardType">Тип калибровочной доски.</param>
        private void OnDetectionOverlayUpdate(bool leftFound, bool rightFound, PointF[]? leftCorners, PointF[]? rightCorners, BoardType boardType)
        {
            Dispatcher.UIThread.Post(() =>
            {
                LeftVideoView.DrawCorners(leftCorners, leftFound, boardType);
                RightVideoView.DrawCorners(rightCorners, rightFound, boardType);
            });
        }

        /// <summary>
        /// Реагирует на изменения свойств ViewModel (статус подключения, FPS).
        /// </summary>
        private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainWindowViewModel.LeftCameraConnected)
                || e.PropertyName == nameof(MainWindowViewModel.RightCameraConnected)
                || e.PropertyName == nameof(MainWindowViewModel.LeftCameraFps)
                || e.PropertyName == nameof(MainWindowViewModel.RightCameraFps)
                || e.PropertyName == nameof(MainWindowViewModel.IsConnectingCameras))
            {
                UpdateVideoConnectionStatus();
            }
            else if (e.PropertyName == nameof(MainWindowViewModel.IsSettingsOpen))
            {
                AnimateSettingsOverlay();
            }
        }

        /// <summary>
        /// Анимирует открытие/закрытие оверлея настроек (fade + slide-up).
        /// </summary>
        private async void AnimateSettingsOverlay()
        {
            var vm = _previousViewModel;
            if (vm == null) return;

            if (vm.IsSettingsOpen)
            {
                // Show overlay, then animate in
                SettingsOverlay.IsVisible = true;
                // Allow layout pass before animating
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
                SettingsBackdrop.Opacity = 1;
                SettingsCard.Opacity = 1;
                SettingsCard.RenderTransform = TransformOperations.Parse("translate(0px, 0px)");
            }
            else
            {
                // Animate out, then hide overlay
                SettingsBackdrop.Opacity = 0;
                SettingsCard.Opacity = 0;
                SettingsCard.RenderTransform = TransformOperations.Parse("translate(0px, 20px)");
                await Task.Delay(250);
                SettingsOverlay.IsVisible = false;
            }
        }

        /// <summary>
        /// Обновляет статус подключения обоих VideoView на основе данных ViewModel.
        /// </summary>
        private void UpdateVideoConnectionStatus()
        {
            var vm = _previousViewModel;
            if (vm == null)
            {
                return;
            }

            Dispatcher.UIThread.Post(() =>
            {
                if (vm.LeftCameraConnected)
                {
                    if (vm.LeftCameraFps > 0.01)
                    {
                        LeftVideoView.SetStatus(StatusType.Connected);
                    }
                    else
                    {
                        LeftVideoView.SetStatus(StatusType.Connecting, vm.VideoConnectingText);
                    }
                }
                else if (vm.IsConnectingCameras)
                {
                    LeftVideoView.SetStatus(StatusType.Connecting, vm.VideoConnectingText);
                }
                else
                {
                    LeftVideoView.Clear();
                }

                if (vm.RightCameraConnected)
                {
                    if (vm.RightCameraFps > 0.01)
                    {
                        RightVideoView.SetStatus(StatusType.Connected);
                    }
                    else
                    {
                        RightVideoView.SetStatus(StatusType.Connecting, vm.VideoConnectingText);
                    }
                }
                else if (vm.IsConnectingCameras)
                {
                    RightVideoView.SetStatus(StatusType.Connecting, vm.VideoConnectingText);
                }
                else
                {
                    RightVideoView.Clear();
                }
            });
        }

        /// <summary>
        /// Освобождает ресурсы при закрытии окна: отписывается от событий, очищает ViewModel.
        /// </summary>
        protected override void OnClosed(EventArgs e)
        {
            DataContextChanged -= OnDataContextChanged;

            if (_previousViewModel != null)
            {
                _previousViewModel.SettingsVm.RequestFolderPicker -= OnRequestFolderPicker;
                _previousViewModel.SettingsVm.RequestCaptureSessionsFolderPicker -= OnRequestCaptureSessionsFolderPicker;
                _previousViewModel.SettingsVm.RequestNavigateToBoardGenerator -= OnRequestNavigateToBoardGenerator;
                _previousViewModel.SettingsVm.RequestClose -= OnRequestCloseSettings;
                _previousViewModel.RequestOpenAbout -= OnRequestOpenAbout;
                _previousViewModel.OnLeftFrameReceived -= OnLeftFrameReceived;
                _previousViewModel.OnRightFrameReceived -= OnRightFrameReceived;
                _previousViewModel.OnDetectionOverlayUpdate -= OnDetectionOverlayUpdate;
                _previousViewModel.PropertyChanged -= OnViewModelPropertyChanged;
                _previousViewModel.Cleanup();
                _previousViewModel = null;
            }

            DisposePendingPreviewFrames();

            base.OnClosed(e);
        }

        /// <summary>
        /// Освобождает ожидающие кадры предпросмотра обеих камер.
        /// </summary>
        private void DisposePendingPreviewFrames()
        {
            FrameRaw? pendingLeft;
            lock (_leftPreviewLock)
            {
                pendingLeft = _pendingLeftFrame;
                _pendingLeftFrame = null;
            }

            FrameRaw? pendingRight;
            lock (_rightPreviewLock)
            {
                pendingRight = _pendingRightFrame;
                _pendingRightFrame = null;
            }

            pendingLeft?.Dispose();
            pendingRight?.Dispose();
        }
    }
}
