namespace MEmuScriptStudio.Core.Models;

public sealed record MemuInstance(int Index, string Name, bool IsRunning, int? ProcessId, long? WindowHandle = null);

public sealed record MemuApplicationInfo(string PackageName, string ActivityName, string? ApplicationLabel = null)
{
    public const string UnknownApplicationLabel = "Chưa xác định";

    public bool HasResolvedApplicationLabel => !string.IsNullOrWhiteSpace(ApplicationLabel);
    public string DisplayName => HasResolvedApplicationLabel ? ApplicationLabel!.Trim() : UnknownApplicationLabel;
}

public readonly record struct ScreenPoint(int X, int Y);
public readonly record struct ScreenRectangle(int Left, int Top, int Width, int Height)
{
    public int Right => Left + Width;
    public int Bottom => Top + Height;
    public bool Contains(ScreenPoint point) => point.X >= Left && point.X < Right && point.Y >= Top && point.Y < Bottom;
}

public sealed record CapturedTap(int X, int Y);
public sealed record CapturedSwipe(int X1, int Y1, int X2, int Y2);

public sealed record TapCaptureUpdate(
    ScreenRectangle Viewport,
    int GuestWidth,
    int GuestHeight,
    ScreenPoint? Point);

public sealed record SwipeCaptureUpdate(
    ScreenRectangle Viewport,
    int GuestWidth,
    int GuestHeight,
    ScreenPoint? StartPoint,
    ScreenPoint? EndPoint);
