namespace MEmuScriptStudio.Core.Models;

public enum DeviceKind
{
    MEmu,
    AndroidAdb
}

public enum AndroidConnectionState
{
    Device,
    Unauthorized,
    Offline,
    Unknown
}

public enum AndroidTargetClassification
{
    ExternalAndroid,
    MEmuBackedAdb,
    Unknown
}

public interface IExecutionTarget
{
    DeviceKind Kind { get; }
    string TargetKey { get; }
    string Identifier { get; }
    string Name { get; }
    bool IsRunning { get; }

    // Compatibility surface for existing MEmu-only UI/tests. Android identity is
    // always TargetKey/Serial and never this sentinel value.
    int Index { get; }
}

public static class ExecutionTargetKeys
{
    public static string ForMemu(int index)
    {
        if (index < 0) throw new ArgumentOutOfRangeException(nameof(index));
        return $"memu:{index}";
    }

    public static string ForAndroidAdb(string serial)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serial);
        return $"android-adb:{serial.Trim()}";
    }
}

public sealed record MemuInstance(int Index, string Name, bool IsRunning, int? ProcessId, long? WindowHandle = null)
    : IExecutionTarget
{
    public DeviceKind Kind => DeviceKind.MEmu;
    public string TargetKey => ExecutionTargetKeys.ForMemu(Index);
    public string Identifier => Index.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

public sealed record AndroidAdbDevice(
    string Serial,
    string? Manufacturer,
    string? Model,
    string? AndroidVersion,
    int? AndroidSdk,
    int? ScreenWidth,
    int? ScreenHeight,
    int? DensityDpi,
    int? Orientation,
    AndroidConnectionState ConnectionState,
    string? Product = null,
    string? Device = null,
    string? Diagnostic = null,
    AndroidTargetClassification Classification = AndroidTargetClassification.Unknown,
    string? Alias = null) : IExecutionTarget
{
    public DeviceKind Kind => DeviceKind.AndroidAdb;
    public string TargetKey => ExecutionTargetKeys.ForAndroidAdb(Serial);
    public string Identifier => Serial;
    public string Name
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Alias)) return Alias.Trim();
            var identity = string.Join(' ', new[] { Manufacturer, Model }
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value!.Trim()));
            return identity.Length == 0 ? Serial : identity;
        }
    }
    public bool IsRunning => ConnectionState == AndroidConnectionState.Device;
    public int Index => -1;
    public string ResolutionText => ScreenWidth is int width && ScreenHeight is int height
        ? $"{width}x{height}"
        : "—";
}

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
