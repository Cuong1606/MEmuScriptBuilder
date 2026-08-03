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
    bool TryProbeWindow(long windowHandle, int expectedProcessId, out WindowGeometrySnapshot geometry)
    {
        geometry = null!;
        if (!TryGetProcessId(windowHandle, out var processId) || processId != expectedProcessId ||
            !TryGetBounds(windowHandle, out var outer)) return false;
        geometry = new WindowGeometrySnapshot(
            windowHandle, processId, outer, null, outer, windowHandle, "TopLevelFallback", outer, []);
        return true;
    }
}

public sealed class WindowsMemuWindowLayoutService(
    IWindowPlatform platform,
    WindowGridPlanner planner) : IMemuWindowLayoutService
{
    private const int GeometryProbeAttempts = 10;
    private const int GeometryProbeDelayMilliseconds = 25;
    private readonly Dictionary<int, IReadOnlyDictionary<int, WindowGeometrySnapshot>> focusReturnGeometry = [];
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
        var diagnostics = new List<string>();
        var rejectedTargetCount = 0;
        foreach (var target in targets)
        {
            if (!IsExpectedWindow(target) || target.ProcessId is not int processId ||
                !platform.TryProbeWindow(target.WindowHandle, processId, out var geometry))
            {
                rejectedTargetCount++;
                continue;
            }
            liveTargets.Add(target with { CurrentBounds = geometry.RenderViewportBounds, Geometry = geometry });
            if (settings.EnableGeometryDiagnostics)
                diagnostics.Add(FormatGeometry(target.InstanceIndex, geometry));
        }
        var originals = liveTargets.Select(target => CreateSavedPlacement(target.InstanceIndex, target.Geometry!)).ToList();

        if (liveTargets.Count == 0)
        {
            return new WindowLayoutApplyResult
            {
                Plan = planner.CreatePlan([], display.WorkArea, settings, 0),
                CapturedOriginalPlacements = originals,
                GeometryDiagnostics = diagnostics,
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
                    .ToDictionary(placement => placement.InstanceIndex);
                (var allAccepted, var probeFailed) = ProbeResizeAll(liveTargets, desiredByIndex, cancellationToken);
                resizeRejected |= !allAccepted;
                applyFailed |= probeFailed;
                if (!allAccepted && allowPaginationFallback && candidateItems > 1)
                {
                    candidateItems--;
                    continue;
                }
            }
            plan = planner.CreatePlan(liveTargets, display.WorkArea, settings, pageIndex, candidateItems);
            (var accepted, var rejected, var failed) = ApplyAndVerify(
                plan,
                liveTargets,
                settings.SizeMode != EmulatorWindowSizeMode.MoveOnly,
                cancellationToken);
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
            warnings.Add($"Đã bỏ qua {rejectedTargetCount} cửa sổ vì handle/PID không còn hợp lệ hoặc không nhận diện được Android render viewport.");
        return new WindowLayoutApplyResult
        {
            Plan = plan,
            Applied = layoutApplied,
            CapturedOriginalPlacements = originals,
            ResizeWasRejected = resizeRejected,
            GeometryDiagnostics = diagnostics,
            Warning = warnings.Count == 0 ? null : string.Join(" ", warnings)
        };
    }

    public Task<string?> FocusAsync(
        WindowLayoutTarget target,
        DisplayWorkArea display,
        CancellationToken cancellationToken)
        => FocusAsync(target, [target], display, false, cancellationToken);

    public Task<string?> FocusAsync(
        WindowLayoutTarget target,
        IReadOnlyList<WindowLayoutTarget> pageTargets,
        DisplayWorkArea display,
        bool enableGeometryDiagnostics,
        CancellationToken cancellationToken)
        => Task.Run(() => FocusCore(target, pageTargets, display, enableGeometryDiagnostics, cancellationToken), cancellationToken);

    private string? FocusCore(
        WindowLayoutTarget target,
        IReadOnlyList<WindowLayoutTarget> pageTargets,
        DisplayWorkArea display,
        bool enableGeometryDiagnostics,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsExpectedWindow(target) || target.ProcessId is not int targetProcessId ||
            !platform.TryProbeWindow(target.WindowHandle, targetProcessId, out var current))
            return "Không thể tập trung vì cửa sổ không còn thuộc đúng process MEmu. Hãy làm mới danh sách giả lập.";

        var pageGeometry = new Dictionary<int, WindowGeometrySnapshot>();
        foreach (var pageTarget in pageTargets)
        {
            if (pageTarget.ProcessId is int processId &&
                IsExpectedWindow(pageTarget) &&
                platform.TryProbeWindow(pageTarget.WindowHandle, processId, out var geometry))
                pageGeometry[pageTarget.InstanceIndex] = geometry;
        }
        if (!pageGeometry.ContainsKey(target.InstanceIndex)) pageGeometry[target.InstanceIndex] = current;

        var fittedRender = FitInside(
            current.RenderViewportBounds.Width,
            current.RenderViewportBounds.Height,
            Math.Max(1, display.WorkArea.Width - current.ChromeWidth),
            Math.Max(1, display.WorkArea.Height - current.ChromeHeight));
        var expectedOuter = new ScreenRectangle(
            display.WorkArea.Left + (display.WorkArea.Width - fittedRender.Width - current.ChromeWidth) / 2,
            display.WorkArea.Top + (display.WorkArea.Height - fittedRender.Height - current.ChromeHeight) / 2,
            fittedRender.Width + current.ChromeWidth,
            fittedRender.Height + current.ChromeHeight);
        var expectedRender = new ScreenRectangle(
            expectedOuter.Left + Math.Max(0, current.RenderInsetLeft),
            expectedOuter.Top + Math.Max(0, current.RenderInsetTop),
            fittedRender.Width,
            fittedRender.Height);
        if (!platform.TrySetBounds(target.WindowHandle, expectedOuter, resize: true))
            return "Không thể phóng to cửa sổ MEmu đã chọn.";
        if (!TryProbeUntilStable(
                target.WindowHandle,
                targetProcessId,
                geometry => ApproximatelyEquals(geometry.OuterBounds, expectedOuter, includePosition: true) &&
                            ApproximatelyEquals(geometry.RenderViewportBounds, expectedRender, includePosition: true),
                cancellationToken,
                out _))
        {
            RestoreGeometryMap(pageGeometry);
            return "MEmu không nhận đầy đủ vị trí/kích thước tập trung. Nếu đang bật “Kích thước cố định”, hãy tắt tùy chọn đó.";
        }

        var parkingLeft = display.WorkArea.Right + 8;
        var parkingFailed = false;
        foreach (var pageTarget in pageTargets.Where(item => item.InstanceIndex != target.InstanceIndex))
        {
            if (!pageGeometry.TryGetValue(pageTarget.InstanceIndex, out var geometry)) continue;
            var parked = new ScreenRectangle(parkingLeft, display.WorkArea.Top, geometry.OuterBounds.Width, geometry.OuterBounds.Height);
            if (!platform.TrySetBounds(pageTarget.WindowHandle, parked, resize: false)) parkingFailed = true;
            parkingLeft += geometry.OuterBounds.Width + 8;
        }
        if (parkingFailed)
        {
            RestoreGeometryMap(pageGeometry);
            return "Không thể đưa toàn bộ cửa sổ khác ra ngoài vùng focus; đã phục hồi geometry của trang.";
        }
        lock (focusReturnGeometry)
        {
            if (!focusReturnGeometry.ContainsKey(target.InstanceIndex))
                focusReturnGeometry[target.InstanceIndex] = pageGeometry;
        }
        return null;
    }

    public Task<(bool Restored, string? Warning)> ReturnFromFocusAsync(
        WindowLayoutTarget target,
        CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyDictionary<int, WindowGeometrySnapshot>? expectedByIndex;
            lock (focusReturnGeometry)
            {
                if (!focusReturnGeometry.Remove(target.InstanceIndex, out expectedByIndex))
                    return (false, (string?)null);
            }
            var failed = 0;
            foreach (var pair in expectedByIndex)
            {
                var expected = pair.Value;
                if (!platform.TryGetProcessId(expected.TopLevelWindowHandle, out var currentProcessId) ||
                    currentProcessId != expected.ProcessId ||
                    !platform.TrySetBounds(expected.TopLevelWindowHandle, expected.OuterBounds, resize: true) ||
                    !TryProbeUntilStable(
                        expected.TopLevelWindowHandle,
                        expected.ProcessId,
                        geometry => ApproximatelyEquals(geometry.OuterBounds, expected.OuterBounds, includePosition: true) &&
                                    ApproximatelyEquals(geometry.ClientBounds, expected.ClientBounds, includePosition: true) &&
                                    ApproximatelyEquals(geometry.RenderViewportBounds, expected.RenderViewportBounds, includePosition: true),
                        cancellationToken,
                        out _))
                    failed++;
            }
            return failed == 0
                ? (true, (string?)null)
                : (true, $"Không thể trả chính xác {failed} cửa sổ về outer/client/render bounds trước focus. Kích thước cố định có thể đang cản resize.");
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
                target.ProcessId is not int processId ||
                !platform.TryProbeWindow(target.WindowHandle, processId, out var actual) ||
                !ApproximatelyEquals(actual.OuterBounds, expected, includePosition: true) ||
                placement.ClientBounds is ScreenRectangle expectedClient &&
                    !ApproximatelyEquals(actual.ClientBounds, expectedClient, includePosition: true) ||
                placement.RenderViewportBounds is ScreenRectangle expectedRender &&
                    !ApproximatelyEquals(actual.RenderViewportBounds, expectedRender, includePosition: true))
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

    private (bool Accepted, bool ResizeRejected, bool ApplyFailed) ApplyAndVerify(
        WindowGridPlan plan,
        IReadOnlyList<WindowLayoutTarget> targets,
        bool resize,
        CancellationToken cancellationToken)
    {
        var rejected = false;
        var failed = false;
        var actualBounds = new List<ScreenRectangle>();
        foreach (var placement in plan.Placements)
        {
            var target = targets.First(item => item.InstanceIndex == placement.InstanceIndex);
            if (!platform.TrySetBounds(placement.WindowHandle, placement.Bounds, resize))
            {
                failed = true;
                continue;
            }
            if (target.ProcessId is not int processId)
            {
                failed = true;
                continue;
            }
            var expectedRender = placement.RenderBounds ?? placement.Bounds;
            var settled = TryProbeUntilStable(
                placement.WindowHandle,
                processId,
                geometry => Math.Abs(geometry.OuterBounds.Left - placement.Bounds.Left) <= 2 &&
                            Math.Abs(geometry.OuterBounds.Top - placement.Bounds.Top) <= 2 &&
                            (!resize ||
                             (ApproximatelyEquals(geometry.RenderViewportBounds, expectedRender, includePosition: true) &&
                              Math.Abs(geometry.OuterBounds.Width - placement.Bounds.Width) <= 2 &&
                              Math.Abs(geometry.OuterBounds.Height - placement.Bounds.Height) <= 2)),
                cancellationToken,
                out var geometry);
            if (geometry is null)
            {
                failed = true;
                continue;
            }
            actualBounds.Add(geometry.OuterBounds);
            if (!settled)
            {
                if (Math.Abs(geometry.OuterBounds.Left - placement.Bounds.Left) > 2 ||
                    Math.Abs(geometry.OuterBounds.Top - placement.Bounds.Top) > 2)
                    failed = true;
                else
                    rejected = true;
            }
        }

        var overlaps = actualBounds.SelectMany((left, index) =>
                actualBounds.Skip(index + 1).Select(right => Intersects(left, right)))
            .Any(value => value);
        return (!failed && !rejected && !overlaps, rejected || overlaps, failed);
    }

    private (bool Accepted, bool ApplyFailed) ProbeResizeAll(
        IReadOnlyList<WindowLayoutTarget> targets,
        IReadOnlyDictionary<int, PlannedWindowPlacement> desiredByIndex,
        CancellationToken cancellationToken)
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
                    new ScreenRectangle(current.Left, current.Top, desired.Bounds.Width, desired.Bounds.Height),
                    resize: true) ||
                target.ProcessId is not int processId)
            {
                accepted = false;
                failed = true;
                continue;
            }
            var expectedRender = desired.RenderBounds ?? desired.Bounds;
            if (!TryProbeUntilStable(
                    target.WindowHandle,
                    processId,
                    geometry => Math.Abs(geometry.RenderViewportBounds.Width - expectedRender.Width) <= 2 &&
                                Math.Abs(geometry.RenderViewportBounds.Height - expectedRender.Height) <= 2,
                    cancellationToken,
                    out var actual))
            {
                accepted = false;
                if (actual is null) failed = true;
            }
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

    private void RestoreGeometryMap(IReadOnlyDictionary<int, WindowGeometrySnapshot> expectedByIndex)
    {
        foreach (var expected in expectedByIndex.Values)
        {
            if (platform.TryGetProcessId(expected.TopLevelWindowHandle, out var currentProcessId) &&
                currentProcessId == expected.ProcessId)
                _ = platform.TrySetBounds(expected.TopLevelWindowHandle, expected.OuterBounds, resize: true);
        }
    }

    private bool TryProbeUntilStable(
        long windowHandle,
        int expectedProcessId,
        Func<WindowGeometrySnapshot, bool> isExpected,
        CancellationToken cancellationToken,
        out WindowGeometrySnapshot? actual)
    {
        WindowGeometrySnapshot? previousMatch = null;
        actual = null;
        for (var attempt = 0; attempt < GeometryProbeAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (platform.TryProbeWindow(windowHandle, expectedProcessId, out var geometry))
            {
                actual = geometry;
                if (isExpected(geometry))
                {
                    if (previousMatch is not null && GeometryIsStable(previousMatch, geometry)) return true;
                    previousMatch = geometry;
                }
                else
                {
                    previousMatch = null;
                }
            }
            else
            {
                previousMatch = null;
            }

            if (attempt + 1 < GeometryProbeAttempts)
                Thread.Sleep(GeometryProbeDelayMilliseconds);
        }
        return false;
    }

    private static bool GeometryIsStable(WindowGeometrySnapshot left, WindowGeometrySnapshot right) =>
        left.TopLevelWindowHandle == right.TopLevelWindowHandle &&
        left.ProcessId == right.ProcessId &&
        left.RenderWindowHandle == right.RenderWindowHandle &&
        ApproximatelyEquals(left.OuterBounds, right.OuterBounds, includePosition: true) &&
        ApproximatelyEquals(left.ClientBounds, right.ClientBounds, includePosition: true) &&
        ApproximatelyEquals(left.RenderViewportBounds, right.RenderViewportBounds, includePosition: true);

    private static SavedWindowPlacement CreateSavedPlacement(int instanceIndex, WindowGeometrySnapshot geometry) => new()
    {
        InstanceIndex = instanceIndex,
        Left = geometry.OuterBounds.Left,
        Top = geometry.OuterBounds.Top,
        Width = geometry.OuterBounds.Width,
        Height = geometry.OuterBounds.Height,
        ClientBounds = geometry.ClientBounds,
        RenderViewportBounds = geometry.RenderViewportBounds,
        RenderWindowHandle = geometry.RenderWindowHandle
    };

    private static string FormatGeometry(int instanceIndex, WindowGeometrySnapshot geometry) =>
        $"#{instanceIndex} outer={FormatRectangle(geometry.OuterBounds)} client={FormatRectangle(geometry.ClientBounds)} render={FormatRectangle(geometry.RenderViewportBounds)} child=0x{geometry.RenderWindowHandle:X}/{geometry.RenderClassName}";

    private static string FormatRectangle(ScreenRectangle value) =>
        $"{value.Left},{value.Top},{value.Width}x{value.Height}";

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
    private const int DwmwaExtendedFrameBounds = 9;

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

    public bool TryProbeWindow(long windowHandle, int expectedProcessId, out WindowGeometrySnapshot geometry)
    {
        geometry = null!;
        if (!TryGetProcessId(windowHandle, out var processId) || processId != expectedProcessId ||
            !TryGetBounds(windowHandle, out var outer) ||
            !TryGetClientBounds(new IntPtr(windowHandle), out var client)) return false;

        ScreenRectangle? extendedFrame = null;
        if (DwmGetWindowAttribute(new IntPtr(windowHandle), DwmwaExtendedFrameBounds, out var extended, Marshal.SizeOf<NativeRectangle>()) == 0)
            extendedFrame = ToScreenRectangle(extended);

        var children = new List<WindowChildGeometry>();
        EnumChildWindows(new IntPtr(windowHandle), (child, _) =>
        {
            var visible = IsWindowVisible(child);
            if (!TryGetClientBounds(child, out var bounds) &&
                (!GetWindowRect(child, out var childRectangle) || (bounds = ToScreenRectangle(childRectangle)).Width <= 0))
                return true;
            children.Add(new WindowChildGeometry(
                child.ToInt64(),
                GetWindowClassName(child),
                visible,
                bounds));
            return true;
        }, IntPtr.Zero);

        var minimumArea = Math.Max(1L, (long)client.Width * client.Height / 4);
        var render = children
            .Where(item => item.IsVisible && item.Bounds.Width >= 100 && item.Bounds.Height >= 100)
            .Where(item => (long)item.Bounds.Width * item.Bounds.Height >= minimumArea)
            .OrderByDescending(item => RenderCandidateScore(item, client))
            .ThenByDescending(item => (long)item.Bounds.Width * item.Bounds.Height)
            .FirstOrDefault();
        geometry = render is null
            ? new WindowGeometrySnapshot(windowHandle, processId, outer, extendedFrame, client, windowHandle, "Unresolved", client, children)
            : new WindowGeometrySnapshot(windowHandle, processId, outer, extendedFrame, client, render.WindowHandle, render.ClassName, render.Bounds, children);
        return render is not null && geometry.RenderViewportBounds.Width > 0 && geometry.RenderViewportBounds.Height > 0;
    }

    private static int RenderCandidateScore(WindowChildGeometry candidate, ScreenRectangle client)
    {
        var className = candidate.ClassName;
        var score = className.Contains("render", StringComparison.OrdinalIgnoreCase) ||
                    className.Contains("android", StringComparison.OrdinalIgnoreCase) ? 200 : 0;
        if (className.Contains("Qt5QWindowIcon", StringComparison.OrdinalIgnoreCase)) score += 100;
        if (Math.Abs(candidate.Bounds.Bottom - client.Bottom) <= 8) score += 40;
        if (candidate.Bounds.Left >= client.Left && candidate.Bounds.Right <= client.Right) score += 20;
        return score;
    }

    private static bool TryGetClientBounds(IntPtr windowHandle, out ScreenRectangle bounds)
    {
        bounds = default;
        if (!GetClientRect(windowHandle, out var rectangle)) return false;
        var origin = new NativePoint { X = rectangle.Left, Y = rectangle.Top };
        if (!ClientToScreen(windowHandle, ref origin)) return false;
        bounds = new ScreenRectangle(origin.X, origin.Y, rectangle.Right - rectangle.Left, rectangle.Bottom - rectangle.Top);
        return bounds.Width > 0 && bounds.Height > 0;
    }

    private static ScreenRectangle ToScreenRectangle(NativeRectangle value) =>
        new(value.Left, value.Top, value.Right - value.Left, value.Bottom - value.Top);

    private static string GetWindowClassName(IntPtr windowHandle)
    {
        var buffer = new System.Text.StringBuilder(256);
        return GetClassName(windowHandle, buffer, buffer.Capacity) > 0 ? buffer.ToString() : string.Empty;
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
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(IntPtr windowHandle, out NativeRectangle rectangle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ClientToScreen(IntPtr windowHandle, ref NativePoint point);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumChildWindows(IntPtr parent, ChildWindowEnumeration callback, IntPtr data);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr windowHandle);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr windowHandle, System.Text.StringBuilder className, int maximumCount);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr windowHandle, int attribute, out NativeRectangle value, int size);

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
    private delegate bool ChildWindowEnumeration(IntPtr window, IntPtr data);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRectangle
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
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
