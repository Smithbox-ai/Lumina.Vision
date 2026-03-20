using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Emgu.CV;
using Emgu.CV.CvEnum;
using LuminaCalib.Models;
using PixelFormat = Avalonia.Platform.PixelFormat;

namespace LuminaCalib.Controls;

/// <summary>
/// Высокопроизводительный элемент отображения видеопотока через WriteableBitmap.
/// </summary>
/// <remarks>
/// Поддерживает наложение обнаруженных углов шахматной доски и индикаторов статуса.
/// Кадры принимаются через <see cref="UpdateFrame(Mat)"/> и копируются в WriteableBitmap
/// с преобразованием BGR/Gray → BGRA.
/// </remarks>
public partial class VideoView : UserControl
{
    /// <summary>Внутренний WriteableBitmap для отрисовки кадров.</summary>
    private WriteableBitmap? _bitmap;
    /// <summary>Блокировка для потокобезопасного доступа к <see cref="_bitmap"/>.</summary>
    private readonly object _bitmapLock = new();
    
    // Данные наложения
    /// <summary>Координаты обнаруженных углов.</summary>
    private PointF[]? _corners;
    /// <summary>Признак успешного обнаружения шаблона.</summary>
    private bool _cornersFound;
    /// <summary>Тип калибровочной доски (влияет на стиль отрисовки оверлея).</summary>
    private BoardType _boardType = BoardType.Chessboard;
    /// <summary>Текст статусного сообщения.</summary>
    private string? _statusText;
    /// <summary>Текущий тип статуса видеопотока.</summary>
    private StatusType _status = StatusType.NoSignal;

    /// <summary>Кэшированная ссылка на Image для видеокадра.</summary>
    private Avalonia.Controls.Image? _image;
    /// <summary>Кэшированная ссылка на Canvas для наложения углов.</summary>
    private Canvas? _canvas;
    /// <summary>Кэшированная ссылка на Border статусной панели.</summary>
    private Border? _statusPanel;
    /// <summary>Кэшированная ссылка на TextBlock статусного текста.</summary>
    private TextBlock? _statusTextBlock;
    /// <summary>Последний размер компоновки для ограничения лишних перерисовок оверлея.</summary>
    private Avalonia.Size _lastArrangeSize;

    // Cached brushes for status panel backgrounds
    private static readonly SolidColorBrush StatusNoSignalBrush = new(Avalonia.Media.Color.FromArgb(200, 40, 40, 40));
    private static readonly SolidColorBrush StatusConnectingBrush = new(Avalonia.Media.Color.FromArgb(200, 0, 100, 150));
    private static readonly SolidColorBrush StatusErrorBrush = new(Avalonia.Media.Color.FromArgb(200, 150, 50, 50));
    private static readonly SolidColorBrush StatusHoldStillBrush = new(Avalonia.Media.Color.FromArgb(200, 150, 120, 0));
    private static readonly SolidColorBrush StatusCapturedBrush = new(Avalonia.Media.Color.FromArgb(200, 0, 150, 80));
    private static readonly SolidColorBrush CornerFoundBrush = new(Avalonia.Media.Color.FromRgb(0, 200, 83));
    private static readonly SolidColorBrush CornerNotFoundBrush = new(Avalonia.Media.Color.FromRgb(255, 82, 82));

    /// <summary>Свойство Avalonia: отображение наложения углов.</summary>
    public static readonly StyledProperty<bool> ShowOverlayProperty =
        AvaloniaProperty.Register<VideoView, bool>(nameof(ShowOverlay), true);

    /// <summary>Свойство Avalonia: отображение статусной панели.</summary>
    public static readonly StyledProperty<bool> ShowStatusProperty =
        AvaloniaProperty.Register<VideoView, bool>(nameof(ShowStatus), true);

    /// <summary>Свойство Avalonia: текст «Нет сигнала».</summary>
    public static readonly StyledProperty<string> NoSignalTextProperty =
        AvaloniaProperty.Register<VideoView, string>(nameof(NoSignalText), "No Signal");

    /// <summary>Свойство Avalonia: текст «Подключение...».</summary>
    public static readonly StyledProperty<string> ConnectingTextProperty =
        AvaloniaProperty.Register<VideoView, string>(nameof(ConnectingText), "Connecting...");

    /// <summary>Свойство Avalonia: текст «Ошибка».</summary>
    public static readonly StyledProperty<string> ErrorTextProperty =
        AvaloniaProperty.Register<VideoView, string>(nameof(ErrorText), "Error");

    /// <summary>Свойство Avalonia: текст «Не двигайтесь...».</summary>
    public static readonly StyledProperty<string> HoldStillTextProperty =
        AvaloniaProperty.Register<VideoView, string>(nameof(HoldStillText), "Hold Still...");

    /// <summary>Свойство Avalonia: текст «Захвачено!».</summary>
    public static readonly StyledProperty<string> CapturedTextProperty =
        AvaloniaProperty.Register<VideoView, string>(nameof(CapturedText), "Captured!");

    /// <summary>
    /// Статический конструктор — регистрирует обработчики изменения статусных текстовых свойств.
    /// </summary>
    static VideoView()
    {
        NoSignalTextProperty.Changed.AddClassHandler<VideoView>((s, _) => s.RefreshStatusText());
        ConnectingTextProperty.Changed.AddClassHandler<VideoView>((s, _) => s.RefreshStatusText());
        ErrorTextProperty.Changed.AddClassHandler<VideoView>((s, _) => s.RefreshStatusText());
        HoldStillTextProperty.Changed.AddClassHandler<VideoView>((s, _) => s.RefreshStatusText());
        CapturedTextProperty.Changed.AddClassHandler<VideoView>((s, _) => s.RefreshStatusText());
    }

    /// <summary>Отображать ли наложение углов.</summary>
    public bool ShowOverlay
    {
        get => GetValue(ShowOverlayProperty);
        set => SetValue(ShowOverlayProperty, value);
    }

    /// <summary>Отображать ли статусную панель.</summary>
    public bool ShowStatus
    {
        get => GetValue(ShowStatusProperty);
        set => SetValue(ShowStatusProperty, value);
    }

    /// <summary>Текст статуса «Нет сигнала».</summary>
    public string NoSignalText
    {
        get => GetValue(NoSignalTextProperty);
        set => SetValue(NoSignalTextProperty, value);
    }

    /// <summary>Текст статуса «Подключение...».</summary>
    public string ConnectingText
    {
        get => GetValue(ConnectingTextProperty);
        set => SetValue(ConnectingTextProperty, value);
    }

    /// <summary>Текст статуса «Ошибка».</summary>
    public string ErrorText
    {
        get => GetValue(ErrorTextProperty);
        set => SetValue(ErrorTextProperty, value);
    }

    /// <summary>Текст статуса «Не двигайтесь...».</summary>
    public string HoldStillText
    {
        get => GetValue(HoldStillTextProperty);
        set => SetValue(HoldStillTextProperty, value);
    }

    /// <summary>Текст статуса «Захвачено!».</summary>
    public string CapturedText
    {
        get => GetValue(CapturedTextProperty);
        set => SetValue(CapturedTextProperty, value);
    }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="VideoView"/>.
    /// </summary>
    public VideoView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Строит визуальное дерево элемента программно: Image, Canvas, статусная панель.
    /// </summary>
    private void InitializeComponent()
    {
        // Создаём визуальное дерево
        var grid = new Grid();
        
        // Изображение для видеокадра
        var image = new Avalonia.Controls.Image
        {
            Name = "VideoImage",
            Stretch = Stretch.Uniform,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch
        };
        grid.Children.Add(image);

        // Холст для наложений (углы, линии)
        var canvas = new Canvas
        {
            Name = "OverlayCanvas",
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch,
            IsHitTestVisible = false
        };
        grid.Children.Add(canvas);

        // Статусная панель
        var statusBorder = new Border
        {
            Name = "StatusPanel",
            Background = new SolidColorBrush(Avalonia.Media.Color.FromArgb(180, 0, 0, 0)),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 4),
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            IsVisible = false,
            Child = new TextBlock
            {
                Name = "StatusText",
                Foreground = Avalonia.Media.Brushes.White,
                FontSize = 14
            }
        };
        grid.Children.Add(statusBorder);

        Content = grid;

        _image = image;
        _canvas = canvas;
        _statusPanel = statusBorder;
        _statusTextBlock = statusBorder.Child as TextBlock;
    }

    /// <summary>
    /// Обновляет видеокадр из EmguCV Mat.
    /// Вызывайте из UI-потока или через Dispatcher.
    /// </summary>
    /// <param name="frame">Mat-кадр из камеры (BGR, Gray или BGRA).</param>
    public void UpdateFrame(Mat frame)
    {
        if (frame == null || frame.IsEmpty) return;

        if (Dispatcher.UIThread.CheckAccess())
        {
            UpdateFrameInternal(frame);
            return;
        }

        var frameCopy = frame.Clone();
        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                UpdateFrameInternal(frameCopy);
            }
            finally
            {
                frameCopy.Dispose();
            }
        });
    }

    /// <summary>
    /// Внутренняя обработка кадра: конвертация Mat → BGRA → WriteableBitmap.
    /// </summary>
    /// <param name="frame">Mat-кадр для отрисовки.</param>
    private void UpdateFrameInternal(Mat frame)
    {
        if (_image == null) return;

        int width = frame.Width;
        int height = frame.Height;

        lock (_bitmapLock)
        {
            // Создаём или пересоздаём битмап при изменении размера
            if (_bitmap == null || _bitmap.PixelSize.Width != width || _bitmap.PixelSize.Height != height)
            {
                _bitmap = new WriteableBitmap(
                    new PixelSize(width, height),
                    new Vector(96, 96),
                    PixelFormats.Bgra8888,
                    AlphaFormat.Premul);
            }

            using var frameBuffer = _bitmap.Lock();
            
            // Конвертируем Mat в BGRA при необходимости
            using var bgraMat = new Mat();
            if (frame.NumberOfChannels == 3)
            {
                CvInvoke.CvtColor(frame, bgraMat, ColorConversion.Bgr2Bgra);
            }
            else if (frame.NumberOfChannels == 1)
            {
                CvInvoke.CvtColor(frame, bgraMat, ColorConversion.Gray2Bgra);
            }
            else
            {
                frame.CopyTo(bgraMat);
            }

            // Копируем пиксельные данные в битмап построчно
            var srcPtr = bgraMat.DataPointer;
            var dstPtr = frameBuffer.Address;
            var rowBytes = width * 4;

            for (int y = 0; y < height; y++)
            {
                unsafe
                {
                    Buffer.MemoryCopy(
                        (void*)(srcPtr + y * bgraMat.Step),
                        (void*)(dstPtr + y * frameBuffer.RowBytes),
                        rowBytes,
                        rowBytes);
                }
            }
        }

        _image.Source = _bitmap;
        _image.InvalidateVisual();
        if (_status != StatusType.Connected || _statusText != null)
        {
            _status = StatusType.Connected;
            _statusText = null;
            UpdateStatus(StatusType.Connected);
        }
        DrawOverlay();
    }

    /// <summary>
    /// Отрисовывает обнаруженные углы шаблона на наложении.
    /// </summary>
    /// <param name="corners">Координаты углов в пикселях кадра, или <c>null</c>.</param>
    /// <param name="isFound"><c>true</c>, если шаблон полностью обнаружен.</param>
    /// <param name="boardType">Тип калибровочной доски (влияет на стиль отрисовки).</param>
    public void DrawCorners(PointF[]? corners, bool isFound, BoardType boardType = BoardType.Chessboard)
    {
        _corners = corners;
        _cornersFound = isFound;
        _boardType = boardType;
        
        Dispatcher.UIThread.Post(DrawOverlay);
    }

    /// <summary>
    /// Устанавливает статусное сообщение, отображаемое поверх видео.
    /// </summary>
    /// <param name="status">Тип статуса.</param>
    /// <param name="text">Пользовательский текст (или <c>null</c> для текста по умолчанию).</param>
    public void SetStatus(StatusType status, string? text = null)
    {
        _status = status;
        _statusText = text;
        
        Dispatcher.UIThread.Post(() => UpdateStatus(status));
    }

    /// <summary>
    /// Обновляет статусный текст при изменении локализованных свойств.
    /// </summary>
    private void RefreshStatusText()
    {
        if (!ShowStatus)
        {
            return;
        }

        Dispatcher.UIThread.Post(() => UpdateStatus(_status));
    }

    /// <summary>
    /// Обновляет видимость, текст и фон статусной панели в зависимости от типа статуса.
    /// </summary>
    /// <param name="status">Текущий тип статуса.</param>
    private void UpdateStatus(StatusType status)
    {
        if (_statusPanel == null || _statusTextBlock == null) return;

        _statusPanel.IsVisible = status != StatusType.Connected || !string.IsNullOrEmpty(_statusText);

        switch (status)
        {
            case StatusType.NoSignal:
                _statusTextBlock.Text = _statusText ?? NoSignalText;
                _statusPanel.Background = StatusNoSignalBrush;
                break;
            case StatusType.Connecting:
                _statusTextBlock.Text = _statusText ?? ConnectingText;
                _statusPanel.Background = StatusConnectingBrush;
                break;
            case StatusType.Connected:
                _statusTextBlock.Text = _statusText ?? "";
                _statusPanel.IsVisible = !string.IsNullOrEmpty(_statusText);
                break;
            case StatusType.Error:
                _statusTextBlock.Text = _statusText ?? ErrorText;
                _statusPanel.Background = StatusErrorBrush;
                break;
            case StatusType.HoldStill:
                _statusTextBlock.Text = _statusText ?? HoldStillText;
                _statusPanel.Background = StatusHoldStillBrush;
                break;
            case StatusType.Captured:
                _statusTextBlock.Text = _statusText ?? CapturedText;
                _statusPanel.Background = StatusCapturedBrush;
                break;
        }
    }

    /// <summary>
    /// Отрисовывает наложение с углами и соединительными линиями на холсте.
    /// </summary>
    private void DrawOverlay()
    {
        if (_canvas == null || _image == null)
        {
            return;
        }

        // Всегда очищаем сначала, чтобы устаревшие углы не отображались при потере детекции
        _canvas.Children.Clear();

        if (!ShowOverlay || _corners == null || _corners.Length == 0 || _bitmap == null)
        {
            return;
        }

        // Вычисляем масштаб между изображением и фактическим размером на экране
        var imageWidth = _image.Bounds.Width;
        var imageHeight = _image.Bounds.Height;
        if (imageWidth <= 0 || imageHeight <= 0)
        {
            return;
        }

        var imageOrigin = _image.TranslatePoint(new Avalonia.Point(0, 0), _canvas) ?? default;

        var scaleX = imageWidth / _bitmap.PixelSize.Width;
        var scaleY = imageHeight / _bitmap.PixelSize.Height;
        var scale = Math.Min(scaleX, scaleY);
        
        var offsetX = imageOrigin.X + (imageWidth - _bitmap.PixelSize.Width * scale) / 2;
        var offsetY = imageOrigin.Y + (imageHeight - _bitmap.PixelSize.Height * scale) / 2;

        var cornerColor = _cornersFound ? CornerFoundBrush : CornerNotFoundBrush;

        foreach (var corner in _corners)
        {
            var ellipse = new Avalonia.Controls.Shapes.Ellipse
            {
                Width = 8,
                Height = 8,
                Fill = cornerColor,
                Stroke = Avalonia.Media.Brushes.White,
                StrokeThickness = 1
            };

            Canvas.SetLeft(ellipse, corner.X * scale + offsetX - 4);
            Canvas.SetTop(ellipse, corner.Y * scale + offsetY - 4);
            
            _canvas.Children.Add(ellipse);
        }

        // Соединительные линии между углами при обнаруженном шаблоне
        // Для ChArUco линии не рисуются — углы могут быть разреженными и неупорядоченными
        if (_cornersFound && _corners.Length > 1 && _boardType != BoardType.ChArUco)
        {
            for (int i = 0; i < _corners.Length - 1; i++)
            {
                var line = new Avalonia.Controls.Shapes.Line
                {
                    StartPoint = new Avalonia.Point(
                        _corners[i].X * scale + offsetX,
                        _corners[i].Y * scale + offsetY),
                    EndPoint = new Avalonia.Point(
                        _corners[i + 1].X * scale + offsetX,
                        _corners[i + 1].Y * scale + offsetY),
                    Stroke = cornerColor,
                    StrokeThickness = 2,
                    Opacity = 0.7
                };
                
                _canvas.Children.Add(line);
            }
        }
    }

    /// <summary>
    /// Очищает видеокадр — переводит элемент в состояние «Нет сигнала».
    /// </summary>
    public void Clear()
    {
        _corners = null;
        _cornersFound = false;
        _status = StatusType.NoSignal;
        _statusText = null;
        
        Dispatcher.UIThread.Post(() =>
        {
            if (_image != null) _image.Source = null;
            if (_canvas != null) _canvas.Children.Clear();
            
            UpdateStatus(StatusType.NoSignal);
        });
    }

    /// <summary>
    /// Перерисовывает оверлей при изменении размера/layout, чтобы геометрия оставалась актуальной.
    /// </summary>
    protected override Avalonia.Size ArrangeOverride(Avalonia.Size finalSize)
    {
        var arranged = base.ArrangeOverride(finalSize);

        if (!_lastArrangeSize.Equals(arranged))
        {
            _lastArrangeSize = arranged;
            DrawOverlay();
        }

        return arranged;
    }

    /// <summary>
    /// Освобождает ресурсы WriteableBitmap при отключении от визуального дерева.
    /// </summary>
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);

        lock (_bitmapLock)
        {
            _bitmap?.Dispose();
            _bitmap = null;
        }
    }
}

/// <summary>
/// Типы статуса видеопотока.
/// </summary>
public enum StatusType
{
    /// <summary>Нет сигнала — камера не подключена.</summary>
    NoSignal,
    /// <summary>Подключение в процессе.</summary>
    Connecting,
    /// <summary>Камера подключена, кадры поступают.</summary>
    Connected,
    /// <summary>Ошибка подключения или захвата.</summary>
    Error,
    /// <summary>Ожидание неподвижности доски перед захватом.</summary>
    HoldStill,
    /// <summary>Кадр успешно захвачен.</summary>
    Captured
}
