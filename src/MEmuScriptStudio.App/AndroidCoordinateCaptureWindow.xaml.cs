using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using MEmuScriptStudio.App.Services;
using MEmuScriptStudio.Core.Android;
using MEmuScriptStudio.Core.Models;

namespace MEmuScriptStudio.App;

public partial class AndroidCoordinateCaptureWindow : Window
{
    private readonly IAndroidScreenshotCaptureService screenshotCaptureService;
    private readonly string adbPath;
    private readonly AndroidAdbDevice device;
    private readonly AndroidCoordinateCaptureMode mode;
    private CancellationTokenSource? captureCancellation;
    private BitmapSource? screenshot;
    private ScreenPoint? startPoint;
    private ScreenPoint? endPoint;
    private bool isDragging;

    public AndroidCoordinateCaptureWindow(
        IAndroidScreenshotCaptureService screenshotCaptureService,
        string adbPath,
        AndroidAdbDevice device,
        AndroidCoordinateCaptureMode mode)
    {
        InitializeComponent();
        this.screenshotCaptureService = screenshotCaptureService;
        this.adbPath = adbPath;
        this.device = device;
        this.mode = mode;
        DeviceText.Text = $"Android · {device.Name} · {device.Serial}";
        InstructionText.Text = mode switch
        {
            AndroidCoordinateCaptureMode.Swipe => "Giữ chuột trái tại điểm đầu, kéo theo hướng vuốt rồi thả tại điểm cuối.",
            AndroidCoordinateCaptureMode.Hold => "Nhấp một điểm trên ảnh. Thời gian giữ trong editor sẽ không bị thay đổi.",
            _ => "Nhấp một điểm trên ảnh để lấy tọa độ chạm."
        };
    }

    public AndroidCoordinateCaptureResult? CaptureResult { get; private set; }

    private async void Window_Loaded(object sender, RoutedEventArgs e) => await RefreshScreenshotAsync();

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshScreenshotAsync();

    internal async Task RefreshScreenshotAsync()
    {
        var cancellation = new CancellationTokenSource();
        var previousCancellation = captureCancellation;
        captureCancellation = cancellation;
        previousCancellation?.Cancel();
        previousCancellation?.Dispose();
        RefreshButton.IsEnabled = false;
        UseButton.IsEnabled = false;
        StatusText.Text = "Đang chụp màn hình Android…";
        screenshot = null;
        ScreenshotImage.Source = null;
        ScreenshotImage.IsEnabled = false;
        ClearSelection();
        try
        {
            var data = await screenshotCaptureService
                .CaptureAsync(adbPath, device.Serial, cancellation.Token);
            if (!ReferenceEquals(captureCancellation, cancellation)) return;
            screenshot = DecodePng(data.PngBytes);
            ScreenshotImage.Source = screenshot;
            StatusText.Text = $"Ảnh hiện tại: {screenshot.PixelWidth}×{screenshot.PixelHeight} px.";
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception exception)
        {
            if (!ReferenceEquals(captureCancellation, cancellation)) return;
            screenshot = null;
            ScreenshotImage.Source = null;
            StatusText.Text = CompactError(exception.Message);
        }
        finally
        {
            if (ReferenceEquals(captureCancellation, cancellation))
            {
                RefreshButton.IsEnabled = true;
                ScreenshotImage.IsEnabled = screenshot is not null;
            }
        }
    }

    private void Screenshot_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!TryMap(e.GetPosition(ScreenshotImage), out var point))
        {
            StatusText.Text = "Hãy chọn bên trong vùng ảnh thực tế.";
            return;
        }

        startPoint = point;
        endPoint = mode == AndroidCoordinateCaptureMode.Swipe ? point : null;
        isDragging = mode == AndroidCoordinateCaptureMode.Swipe;
        if (isDragging) ScreenshotImage.CaptureMouse();
        UpdateSelectionVisual();
        UpdateSelectionStatus();
        e.Handled = true;
    }

    private void Screenshot_MouseMove(object sender, MouseEventArgs e)
    {
        if (!isDragging || e.LeftButton != MouseButtonState.Pressed) return;
        if (TryMap(e.GetPosition(ScreenshotImage), out var point))
        {
            endPoint = point;
            UpdateSelectionVisual();
            UpdateSelectionStatus();
        }
        e.Handled = true;
    }

    private void Screenshot_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!isDragging) return;
        isDragging = false;
        ScreenshotImage.ReleaseMouseCapture();
        if (TryMap(e.GetPosition(ScreenshotImage), out var point)) endPoint = point;
        else endPoint = null;
        UpdateSelectionVisual();
        UpdateSelectionStatus();
        e.Handled = true;
    }

    private void Screenshot_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateSelectionVisual();

    private void Use_Click(object sender, RoutedEventArgs e)
    {
        if (startPoint is not ScreenPoint start) return;
        CaptureResult = mode == AndroidCoordinateCaptureMode.Swipe
            ? endPoint is ScreenPoint end
                ? new AndroidCoordinateCaptureResult(Swipe: new CapturedSwipe(start.X, start.Y, end.X, end.Y))
                : null
            : new AndroidCoordinateCaptureResult(Tap: new CapturedTap(start.X, start.Y));
        if (CaptureResult is not null) DialogResult = true;
    }

    private bool TryMap(Point point, out ScreenPoint nativePoint)
    {
        if (screenshot is null || ScreenshotImage.ActualWidth <= 0 || ScreenshotImage.ActualHeight <= 0)
        {
            nativePoint = default;
            return false;
        }
        return UniformImageCoordinateMapper.TryToNative(
            new DisplayPoint(point.X, point.Y),
            ScreenshotImage.ActualWidth,
            ScreenshotImage.ActualHeight,
            screenshot.PixelWidth,
            screenshot.PixelHeight,
            out nativePoint);
    }

    private void UpdateSelectionStatus()
    {
        UseButton.IsEnabled = startPoint is not null &&
            (mode != AndroidCoordinateCaptureMode.Swipe || endPoint is not null);
        StatusText.Text = (startPoint, endPoint, mode) switch
        {
            ({ } start, { } end, AndroidCoordinateCaptureMode.Swipe) =>
                $"Vuốt: ({start.X}, {start.Y}) → ({end.X}, {end.Y}).",
            ({ } start, _, _) => $"Điểm đã chọn: X={start.X}, Y={start.Y}.",
            _ => "Hãy chọn bên trong vùng ảnh thực tế."
        };
    }

    private void UpdateSelectionVisual()
    {
        if (screenshot is null || startPoint is not ScreenPoint start ||
            ScreenshotImage.ActualWidth <= 0 || ScreenshotImage.ActualHeight <= 0)
        {
            StartMarker.Visibility = Visibility.Collapsed;
            EndMarker.Visibility = Visibility.Collapsed;
            SwipeLine.Visibility = Visibility.Collapsed;
            return;
        }

        var startDisplay = ToDisplay(start);
        PositionMarker(StartMarker, startDisplay);
        StartMarker.Visibility = Visibility.Visible;
        if (mode == AndroidCoordinateCaptureMode.Swipe && endPoint is ScreenPoint end)
        {
            var endDisplay = ToDisplay(end);
            PositionMarker(EndMarker, endDisplay);
            EndMarker.Visibility = Visibility.Visible;
            SwipeLine.X1 = startDisplay.X;
            SwipeLine.Y1 = startDisplay.Y;
            SwipeLine.X2 = endDisplay.X;
            SwipeLine.Y2 = endDisplay.Y;
            SwipeLine.Visibility = Visibility.Visible;
        }
        else
        {
            EndMarker.Visibility = Visibility.Collapsed;
            SwipeLine.Visibility = Visibility.Collapsed;
        }
    }

    private DisplayPoint ToDisplay(ScreenPoint point) => UniformImageCoordinateMapper.ToDisplay(
        point,
        ScreenshotImage.ActualWidth,
        ScreenshotImage.ActualHeight,
        screenshot!.PixelWidth,
        screenshot.PixelHeight);

    private static void PositionMarker(FrameworkElement marker, DisplayPoint point)
    {
        System.Windows.Controls.Canvas.SetLeft(marker, point.X - marker.Width / 2d);
        System.Windows.Controls.Canvas.SetTop(marker, point.Y - marker.Height / 2d);
    }

    private void ClearSelection()
    {
        startPoint = null;
        endPoint = null;
        isDragging = false;
        ScreenshotImage.ReleaseMouseCapture();
        UpdateSelectionVisual();
    }

    internal static BitmapSource DecodePng(byte[] bytes)
    {
        var expectedDimensions = AndroidPngHeaderValidator.ValidateAndReadDimensions(bytes);
        using var stream = new MemoryStream(bytes, writable: false);
        var decoder = new PngBitmapDecoder(
            stream,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);
        var frame = decoder.Frames.FirstOrDefault()
            ?? throw new InvalidDataException("Ảnh PNG không có frame hợp lệ.");
        if (frame.PixelWidth <= 0 || frame.PixelHeight <= 0)
            throw new InvalidDataException("Kích thước ảnh PNG không hợp lệ.");
        if (frame.PixelWidth != expectedDimensions.Width || frame.PixelHeight != expectedDimensions.Height)
            throw new InvalidDataException("Kích thước ảnh PNG decode không khớp IHDR.");
        frame.Freeze();
        return frame;
    }

    private static string CompactError(string message)
    {
        var normalized = string.Join(' ', message.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (normalized.Length == 0) normalized = "Không thể chụp màn hình Android.";
        return normalized.Length <= 240 ? normalized : $"{normalized[..239]}…";
    }

    protected override void OnClosed(EventArgs e)
    {
        var cancellation = captureCancellation;
        captureCancellation = null;
        cancellation?.Cancel();
        cancellation?.Dispose();
        base.OnClosed(e);
    }
}
