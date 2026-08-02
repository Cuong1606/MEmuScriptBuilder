using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using MEmuScriptStudio.Core.Models;

namespace MEmuScriptStudio.App;

public partial class SwipeCaptureOverlayWindow : Window
{
    private const int GwlExStyle = -20;
    private const int WsExTransparent = 0x00000020;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowWindow = 0x0040;
    private nint handle;

    public SwipeCaptureOverlayWindow()
    {
        InitializeComponent();
        SourceInitialized += OnSourceInitialized;
    }

    public void UpdateCapture(SwipeCaptureUpdate update)
    {
        if (handle == nint.Zero || update.Viewport.Width <= 0 || update.Viewport.Height <= 0) return;

        SetWindowPos(
            handle,
            new nint(-1),
            update.Viewport.Left,
            update.Viewport.Top,
            update.Viewport.Width,
            update.Viewport.Height,
            SwpNoActivate | SwpShowWindow);

        var transform = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        Point ToLocal(ScreenPoint point) => transform.Transform(new Point(
            (double)point.X * update.Viewport.Width / update.GuestWidth,
            (double)point.Y * update.Viewport.Height / update.GuestHeight));

        var start = update.StartPoint is { } startPoint ? ToLocal(startPoint) : (Point?)null;
        var end = update.EndPoint is { } endPoint ? ToLocal(endPoint) : (Point?)null;
        DrawMarker(start, update.StartPoint, StartMarker, StartLabel, StartLabelText, "Bắt đầu");
        DrawMarker(end, update.EndPoint, EndMarker, EndLabel, EndLabelText, "Kết thúc");
        DrawArrow(start, end);
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        handle = new WindowInteropHelper(this).Handle;
        var style = GetWindowLongPtr(handle, GwlExStyle).ToInt64();
        _ = SetWindowLongPtr(handle, GwlExStyle, new nint(style | WsExTransparent | WsExNoActivate | WsExToolWindow));
    }

    private static void DrawMarker(
        Point? local,
        ScreenPoint? guest,
        FrameworkElement marker,
        FrameworkElement label,
        System.Windows.Controls.TextBlock labelText,
        string prefix)
    {
        var visibility = local.HasValue ? Visibility.Visible : Visibility.Collapsed;
        marker.Visibility = visibility;
        label.Visibility = visibility;
        if (local is not { } point || guest is not { } guestPoint) return;

        System.Windows.Controls.Canvas.SetLeft(marker, point.X - marker.Width / 2);
        System.Windows.Controls.Canvas.SetTop(marker, point.Y - marker.Height / 2);
        System.Windows.Controls.Canvas.SetLeft(label, point.X + 14);
        System.Windows.Controls.Canvas.SetTop(label, Math.Max(48, point.Y - 34));
        labelText.Text = $"{prefix}: {guestPoint.X}, {guestPoint.Y}";
    }

    private void DrawArrow(Point? start, Point? end)
    {
        var visible = start.HasValue && end.HasValue;
        SwipeLine.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        ArrowHead.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        if (!visible) return;

        var from = start!.Value;
        var to = end!.Value;
        SwipeLine.X1 = from.X;
        SwipeLine.Y1 = from.Y;
        SwipeLine.X2 = to.X;
        SwipeLine.Y2 = to.Y;

        var angle = Math.Atan2(to.Y - from.Y, to.X - from.X);
        const double size = 14;
        var left = new Point(to.X - size * Math.Cos(angle - Math.PI / 6), to.Y - size * Math.Sin(angle - Math.PI / 6));
        var right = new Point(to.X - size * Math.Cos(angle + Math.PI / 6), to.Y - size * Math.Sin(angle + Math.PI / 6));
        ArrowHead.Points = new PointCollection([to, left, right]);
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern nint GetWindowLongPtr(nint window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern nint SetWindowLongPtr(nint window, int index, nint value);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(nint window, nint insertAfter, int x, int y, int width, int height, uint flags);
}
