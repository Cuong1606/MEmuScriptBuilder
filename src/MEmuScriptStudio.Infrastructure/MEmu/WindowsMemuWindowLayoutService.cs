using System.Runtime.InteropServices;
using MEmuScriptStudio.Core.MEmu;
using MEmuScriptStudio.Core.Models;

namespace MEmuScriptStudio.Infrastructure.MEmu;

public interface IWindowPlatform
{
    IReadOnlyList<DisplayWorkArea> GetDisplays();
    bool TryGetProcessId(long windowHandle, out int processId);
    bool TryGetBounds(long windowHandle, out ScreenRectangle bounds);
    bool TrySetBounds(long windowHandle, ScreenRectangle bounds, bool resize);
}

public sealed class WindowsMemuWindowLayoutService(
    IWindowPlatform platform,
    WindowGridPlanner planner) : IMemuWindowLayoutService
{
    private readonly Dictionary<int, ScreenRectangle> focusReturnBounds = [];
    public Task<IReadOnlyList<DisplayWorkArea>> GetDisplaysAsync(CancellationToken cancellationToken)
        => Task.Run<IReadOnlyList<DisplayWorkArea>>(() => platform.GetDisplays(), cancellationToken);

    public Task<WindowLayoutApplyResult> ArrangeAsync(
        IReadOnlyList<WindowLayoutTarget> targets,
        EmulatorWindowLayoutSettings settings,
        int pageIndex,
        CancellationToken cancellationToken)
        => Task.Run(() => ArrangeCore(targets, settings, pageIndex, cancellationToken), cancellationToken);

    private WindowLayoutApplyResult ArrangeCore(
        IReadOnlyList<WindowLayoutTarget> targets,
        EmulatorWindowLayoutSettings settings,
        int pageIndex,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(settings);
        var displays = platform.GetDisplays();
        if (displays.Count == 0) throw new InvalidOperationException("Windows không trả về màn hình khả dụng.");
        var display = displays.FirstOrDefault(item => string.Equals(
                item.DeviceName,
                settings.DisplayDeviceName,
                StringComparison.OrdinalIgnoreCase))
            ?? displays.FirstOrDefault(item => item.IsPrimary)
            ?? displays[0];

        var liveTargets = new List<WindowLayoutTarget>();
        var rejectedTargetCount = 0;
        foreach (var target in targets)
        {
            if (!IsExpectedWindow(target) || !platform.TryGetBounds(target.WindowHandle, out var bounds))
            {
                rejectedTargetCount++;
                continue;
            }
            liveTargets.Add(target with { CurrentBounds = bounds });
        }
        var originals = liveTargets.Select(target => new SavedWindowPlacement
        {
            InstanceIndex = target.InstanceIndex,
            Left = target.CurrentBounds.Left,
            Top = target.CurrentBounds.Top,
            Width = target.CurrentBounds.Width,
            Height = target.CurrentBounds.Height
        }).ToList();

        if (liveTargets.Count == 0)
        {
            return new WindowLayoutApplyResult
            {
                Plan = planner.CreatePlan([], display.WorkArea, settings, 0),
                CapturedOriginalPlacements = originals,
                Warning = "Không tìm thấy cửa sổ MEmu đang chạy để xếp lưới."
            };
        }

        var requestedItems = settings.ItemsPerPageMode == LayoutItemsPerPageMode.Custom
            ? Math.Min(Math.Max(1, settings.CustomItemsPerPage), liveTargets.Count)
            : liveTargets.Count;
        var candidateItems = requestedItems;
        var allowPaginationFallback = settings.ItemsPerPageMode != LayoutItemsPerPageMode.All;
        WindowGridPlan plan;
        var resizeRejected = false;
        var applyFailed = false;
        var singlePageRollbackFailed = false;
        var layoutApplied = true;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (settings.SizeMode != EmulatorWindowSizeMode.MoveOnly)
            {
                var probePlan = planner.CreatePlan(liveTargets, display.WorkArea, settings, pageIndex: 0, candidateItems);
                var desiredByIndex = Enumerable.Range(0, probePlan.PageCount)
                    .SelectMany(probePage => planner.CreatePlan(liveTargets, display.WorkArea, settings, probePage, candidateItems).Placements)
                    .ToDictionary(placement => placement.InstanceIndex, placement => placement.Bounds);
                (var allAccepted, var probeFailed) = ProbeResizeAll(liveTargets, desiredByIndex);
                resizeRejected |= !allAccepted;
                applyFailed |= probeFailed;
                if (!allAccepted && allowPaginationFallback && candidateItems > 1)
                {
                    candidateItems--;
                    continue;
                }
            }
            plan = planner.CreatePlan(liveTargets, display.WorkArea, settings, pageIndex, candidateItems);
            (var accepted, var rejected, var failed) = ApplyAndVerify(plan, settings.SizeMode != EmulatorWindowSizeMode.MoveOnly);
            resizeRejected |= rejected;
            applyFailed |= failed;
            if (accepted || !allowPaginationFallback || candidateItems == 1)
            {
                if (!accepted && !allowPaginationFallback)
                {
                    layoutApplied = false;
                    singlePageRollbackFailed = !RestoreAttemptedLayout(
                        liveTargets,
                        originals,
                        resize: settings.SizeMode != EmulatorWindowSizeMode.MoveOnly,
                        cancellationToken);
                    if (singlePageRollbackFailed)
                        _ = ParkOtherPages(liveTargets, new WindowGridPlan(), displays, settings.Gap);
                }
                break;
            }
            candidateItems--;
        }

        var parkingFailed = !ParkOtherPages(liveTargets, plan, displays, settings.Gap);
        var warnings = new List<string>();
        if (resizeRejected)
            warnings.Add("Một hoặc nhiều cửa sổ không nhận kích thước yêu cầu; đã giảm số cửa sổ trong trang khi có thể. Nếu MEmu bật “Kích thước cố định”, hãy tắt tùy chọn đó để cho phép resize.");
        if (resizeRejected && settings.ItemsPerPageMode == LayoutItemsPerPageMode.All)
            warnings.Add("Không thể xếp tất cả cửa sổ trên một trang mà không chồng lấn. Hãy chọn “Tự động phân trang” hoặc “Số lượng tùy chỉnh”.");
        if (singlePageRollbackFailed)
            warnings.Add("Không thể phục hồi đầy đủ bounds trước lần thử xếp một trang.");
        if (applyFailed || parkingFailed)
            warnings.Add("Một hoặc nhiều cửa sổ không thể di chuyển hoặc đổi kích thước.");
        if (rejectedTargetCount > 0)
            warnings.Add($"Đã bỏ qua {rejectedTargetCount} cửa sổ vì handle không còn thuộc đúng process MEmu.");
        return new WindowLayoutApplyResult
        {
            Plan = plan,
            Applied = layoutApplied,
            CapturedOriginalPlacements = originals,
            ResizeWasRejected = resizeRejected,
            Warning = warnings.Count == 0 ? null : string.Join(" ", warnings)
        };
    }

    public Task<string?> FocusAsync(
        WindowLayoutTarget target,
        DisplayWorkArea display,
        CancellationToken cancellationToken)
        => Task.Run(() => FocusCore(target, display, cancellationToken), cancellationToken);

    private string? FocusCore(
        WindowLayoutTarget target,
        DisplayWorkArea display,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsExpectedWindow(target))
            return "Không thể tập trung vì cửa sổ không còn thuộc đúng process MEmu. Hãy làm mới danh sách giả lập.";
        if (!platform.TryGetBounds(target.WindowHandle, out var current))
            return "Không thể đọc kích thước cửa sổ MEmu đã chọn.";
        lock (focusReturnBounds)
        {
            if (!focusReturnBounds.ContainsKey(target.InstanceIndex))
                focusReturnBounds[target.InstanceIndex] = current;
        }
        var fitted = FitInside(current.Width, current.Height, display.WorkArea.Width, display.WorkArea.Height);
        var expected = new ScreenRectangle(
            display.WorkArea.Left + (display.WorkArea.Width - fitted.Width) / 2,
            display.WorkArea.Top + (display.WorkArea.Height - fitted.Height) / 2,
            fitted.Width,
            fitted.Height);
        if (!platform.TrySetBounds(target.WindowHandle, expected, resize: true))
            return "Không thể phóng to cửa sổ MEmu đã chọn.";
        if (!platform.TryGetBounds(target.WindowHandle, out var actual) ||
            !ApproximatelyEquals(actual, expected, includePosition: true))
            return "MEmu không nhận đầy đủ vị trí/kích thước tập trung. Nếu đang bật “Kích thước cố định”, hãy tắt tùy chọn đó.";
        return null;
    }

    public Task<(bool Restored, string? Warning)> ReturnFromFocusAsync(
        WindowLayoutTarget target,
        CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            ScreenRectangle expected;
            lock (focusReturnBounds)
            {
                if (!focusReturnBounds.Remove(target.InstanceIndex, out expected))
                    return (false, (string?)null);
            }
            if (!IsExpectedWindow(target) ||
                !platform.TrySetBounds(target.WindowHandle, expected, resize: true) ||
                !platform.TryGetBounds(target.WindowHandle, out var actual) ||
                !ApproximatelyEquals(actual, expected, includePosition: true))
                return (true, "Không thể trả cửa sổ về chính xác ô trước khi tập trung. Nếu MEmu bật “Kích thước cố định”, hãy tắt tùy chọn đó.");
            return (true, (string?)null);
        }, cancellationToken);

    public Task<string?> RestoreOriginalAsync(
        IReadOnlyList<WindowLayoutTarget> targets,
        IReadOnlyList<SavedWindowPlacement> placements,
        CancellationToken cancellationToken)
        => Task.Run(() => RestoreOriginalCore(targets, placements, cancellationToken), cancellationToken);

    private string? RestoreOriginalCore(
        IReadOnlyList<WindowLayoutTarget> targets,
        IReadOnlyList<SavedWindowPlacement> placements,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var placementByIndex = placements.GroupBy(item => item.InstanceIndex).ToDictionary(group => group.Key, group => group.Last());
        var failed = 0;
        foreach (var target in targets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!placementByIndex.TryGetValue(target.InstanceIndex, out var placement)) continue;
            var expected = placement.ToRectangle();
            if (!IsExpectedWindow(target) ||
                !platform.TrySetBounds(target.WindowHandle, expected, resize: true) ||
                !platform.TryGetBounds(target.WindowHandle, out var actual) ||
                !ApproximatelyEquals(actual, expected, includePosition: true))
                failed++;
        }
        return failed == 0
            ? null
            : $"Không thể khôi phục đầy đủ {failed} cửa sổ. Hãy làm mới danh sách và tắt “Kích thước cố định” nếu cần resize.";
    }

    private bool RestoreAttemptedLayout(
        IReadOnlyList<WindowLayoutTarget> targets,
        IReadOnlyList<SavedWindowPlacement> placements,
        bool resize,
        CancellationToken cancellationToken)
    {
        var placementByIndex = placements.ToDictionary(item => item.InstanceIndex);
        var succeeded = true;
        foreach (var target in targets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!placementByIndex.TryGetValue(target.InstanceIndex, out var placement)) continue;
            var expected = placement.ToRectangle();
            if (!IsExpectedWindow(target) ||
                !platform.TrySetBounds(target.WindowHandle, expected, resize) ||
                !platform.TryGetBounds(target.WindowHandle, out var actual) ||
                !ApproximatelyEquals(actual, expected, includePosition: true))
                succeeded = false;
        }
        return succeeded;
    }

    private (bool Accepted, bool ResizeRejected, bool ApplyFailed) ApplyAndVerify(WindowGridPlan plan, bool resize)
    {
        var rejected = false;
        var failed = false;
        var actualBounds = new List<ScreenRectangle>();
        foreach (var placement in plan.Placements)
        {
            if (!platform.TrySetBounds(placement.WindowHandle, placement.Bounds, resize))
            {
                failed = true;
                continue;
            }
            if (!platform.TryGetBounds(placement.WindowHandle, out var actual))
            {
                failed = true;
                continue;
            }
            actualBounds.Add(actual);
            if (Math.Abs(actual.Left - placement.Bounds.Left) > 2 ||
                Math.Abs(actual.Top - placement.Bounds.Top) > 2)
                failed = true;
            if (resize &&
                (Math.Abs(actual.Width - placement.Bounds.Width) > 2 ||
                 Math.Abs(actual.Height - placement.Bounds.Height) > 2))
                rejected = true;
        }

        var overlaps = actualBounds.SelectMany((left, index) =>
                actualBounds.Skip(index + 1).Select(right => Intersects(left, right)))
            .Any(value => value);
        return (!failed && !rejected && !overlaps, rejected || overlaps, failed);
    }

    private (bool Accepted, bool ApplyFailed) ProbeResizeAll(
        IReadOnlyList<WindowLayoutTarget> targets,
        IReadOnlyDictionary<int, ScreenRectangle> desiredByIndex)
    {
        var accepted = true;
        var failed = false;
        foreach (var target in targets)
        {
            if (!desiredByIndex.TryGetValue(target.InstanceIndex, out var desired))
            {
                accepted = false;
                failed = true;
                continue;
            }
            if (!platform.TryGetBounds(target.WindowHandle, out var current) ||
                !platform.TrySetBounds(
                    target.WindowHandle,
                    new ScreenRectangle(current.Left, current.Top, desired.Width, desired.Height),
                    resize: true) ||
                !platform.TryGetBounds(target.WindowHandle, out var actual))
            {
                accepted = false;
                failed = true;
                continue;
            }
            if (Math.Abs(actual.Width - desired.Width) > 2 || Math.Abs(actual.Height - desired.Height) > 2)
                accepted = false;
        }
        return (accepted, failed);
    }

    private bool ParkOtherPages(
        IReadOnlyList<WindowLayoutTarget> targets,
        WindowGridPlan plan,
        IReadOnlyList<DisplayWorkArea> displays,
        int gap)
    {
        var visible = plan.Placements.Select(item => item.InstanceIndex).ToHashSet();
        var parkingLeft = displays.Max(item => item.WorkArea.Right) + Math.Max(1, gap);
        var parkingTop = displays.Min(item => item.WorkArea.Top);
        var succeeded = true;
        foreach (var target in targets.Where(item => !visible.Contains(item.InstanceIndex)))
        {
            if (!platform.TryGetBounds(target.WindowHandle, out var current))
            {
                succeeded = false;
                continue;
            }
            var expected = new ScreenRectangle(parkingLeft, parkingTop, current.Width, current.Height);
            if (!platform.TrySetBounds(target.WindowHandle, expected, resize: false) ||
                !platform.TryGetBounds(target.WindowHandle, out var actual) ||
                Math.Abs(actual.Left - expected.Left) > 2 || Math.Abs(actual.Top - expected.Top) > 2)
            {
                succeeded = false;
                actual = current;
            }
            parkingLeft = checked(parkingLeft + Math.Max(1, actual.Width) + Math.Max(1, gap));
        }
        return succeeded;
    }

    private bool IsExpectedWindow(WindowLayoutTarget target) =>
        target.WindowHandle > 0 &&
        target.ProcessId is > 0 &&
        platform.TryGetProcessId(target.WindowHandle, out var actualProcessId) &&
        actualProcessId == target.ProcessId.Value;

    private static bool ApproximatelyEquals(
        ScreenRectangle actual,
        ScreenRectangle expected,
        bool includePosition) =>
        (!includePosition ||
         (Math.Abs(actual.Left - expected.Left) <= 2 && Math.Abs(actual.Top - expected.Top) <= 2)) &&
        Math.Abs(actual.Width - expected.Width) <= 2 &&
        Math.Abs(actual.Height - expected.Height) <= 2;

    private static bool Intersects(ScreenRectangle left, ScreenRectangle right) =>
        left.Left < right.Right && left.Right > right.Left && left.Top < right.Bottom && left.Bottom > right.Top;

    private static (int Width, int Height) FitInside(int sourceWidth, int sourceHeight, int maximumWidth, int maximumHeight)
    {
        var scale = Math.Min((double)maximumWidth / Math.Max(1, sourceWidth), (double)maximumHeight / Math.Max(1, sourceHeight));
        return (
            Math.Max(1, Math.Min(maximumWidth, (int)Math.Floor(sourceWidth * scale))),
            Math.Max(1, Math.Min(maximumHeight, (int)Math.Floor(sourceHeight * scale))));
    }
}

public sealed class WindowsWindowPlatform : IWindowPlatform
{
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoSize = 0x0001;

    public IReadOnlyList<DisplayWorkArea> GetDisplays()
    {
        var displays = new List<DisplayWorkArea>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (monitor, _, _, _) =>
        {
            var info = new MonitorInfoEx { Size = Marshal.SizeOf<MonitorInfoEx>() };
            if (!GetMonitorInfo(monitor, ref info)) return true;
            displays.Add(new DisplayWorkArea(
                info.DeviceName.ToString(),
                new ScreenRectangle(
                    info.WorkArea.Left,
                    info.WorkArea.Top,
                    info.WorkArea.Right - info.WorkArea.Left,
                    info.WorkArea.Bottom - info.WorkArea.Top),
                (info.Flags & 1) != 0));
            return true;
        }, IntPtr.Zero);
        return displays;
    }

    public bool TryGetBounds(long windowHandle, out ScreenRectangle bounds)
    {
        if (windowHandle <= 0 || !GetWindowRect(new IntPtr(windowHandle), out var rectangle))
        {
            bounds = default;
            return false;
        }
        bounds = new ScreenRectangle(
            rectangle.Left,
            rectangle.Top,
            rectangle.Right - rectangle.Left,
            rectangle.Bottom - rectangle.Top);
        return bounds.Width > 0 && bounds.Height > 0;
    }

    public bool TryGetProcessId(long windowHandle, out int processId)
    {
        processId = 0;
        if (windowHandle <= 0) return false;
        _ = GetWindowThreadProcessId(new IntPtr(windowHandle), out var nativeProcessId);
        if (nativeProcessId == 0 || nativeProcessId > int.MaxValue) return false;
        processId = (int)nativeProcessId;
        return true;
    }

    public bool TrySetBounds(long windowHandle, ScreenRectangle bounds, bool resize)
    {
        if (windowHandle <= 0) return false;
        var flags = SwpNoActivate | SwpNoZOrder | (resize ? 0 : SwpNoSize);
        return SetWindowPos(
            new IntPtr(windowHandle),
            IntPtr.Zero,
            bounds.Left,
            bounds.Top,
            Math.Max(1, bounds.Width),
            Math.Max(1, bounds.Height),
            flags);
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayMonitors(
        IntPtr deviceContext,
        IntPtr clipRectangle,
        MonitorEnumeration callback,
        IntPtr data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfoEx monitorInfo);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr windowHandle, out NativeRectangle rectangle);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr windowHandle, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr windowHandle,
        IntPtr insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    private delegate bool MonitorEnumeration(IntPtr monitor, IntPtr deviceContext, IntPtr rectangle, IntPtr data);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRectangle
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfoEx
    {
        public int Size;
        public NativeRectangle Monitor;
        public NativeRectangle WorkArea;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;
    }
}
