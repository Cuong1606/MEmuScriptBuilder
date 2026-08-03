using MEmuScriptStudio.Core.MEmu;
using MEmuScriptStudio.Core.Models;
using MEmuScriptStudio.Infrastructure.MEmu;

namespace MEmuScriptStudio.Infrastructure.Tests;

[TestClass]
public sealed class WindowsMemuWindowLayoutServiceTests
{
    [TestMethod]
    public async Task AutoFitReadsActualFixedSizeAndAddsPagesUntilWindowsNoLongerOverlap()
    {
        var platform = new FakeWindowPlatform(minimumWidth: 600, minimumHeight: 500);
        var targets = AddTargets(platform, 4);
        var service = new WindowsMemuWindowLayoutService(platform, new WindowGridPlanner());
        var settings = new EmulatorWindowLayoutSettings
        {
            ItemsPerPageMode = LayoutItemsPerPageMode.AutoFit,
            ColumnMode = LayoutColumnMode.Auto,
            SizeMode = EmulatorWindowSizeMode.Auto,
            Gap = 8,
            DisplayDeviceName = "DISPLAY2"
        };

        var result = await service.ArrangeAsync(targets, settings, 0, CancellationToken.None);

        Assert.IsTrue(result.ResizeWasRejected);
        Assert.AreEqual(1, result.Plan.ItemsPerPage);
        Assert.AreEqual(4, result.Plan.PageCount);
        Assert.AreEqual(4, result.CapturedOriginalPlacements.Count);
        StringAssert.Contains(result.Warning, "Kích thước cố định");
        Assert.IsTrue(platform.Bounds.Values.Skip(1).Select(item => item.Left).Distinct().Count() == 3,
            "Các trang không hiển thị phải được đỗ ở vị trí riêng, không xếp chồng.");
    }

    [TestMethod]
    public async Task MoveOnlyNeverRequestsResize()
    {
        var platform = new FakeWindowPlatform();
        var targets = AddTargets(platform, 3);
        var service = new WindowsMemuWindowLayoutService(platform, new WindowGridPlanner());
        var settings = new EmulatorWindowLayoutSettings
        {
            ItemsPerPageMode = LayoutItemsPerPageMode.All,
            SizeMode = EmulatorWindowSizeMode.MoveOnly
        };

        await service.ArrangeAsync(targets, settings, 0, CancellationToken.None);

        Assert.IsTrue(platform.SetCalls.All(call => !call.Resize));
    }

    [TestMethod]
    public async Task SinglePageNeverReportsPaginationFallbackAsSuccess()
    {
        var platform = new FakeWindowPlatform(minimumWidth: 600, minimumHeight: 500);
        var targets = AddTargets(platform, 4);
        var service = new WindowsMemuWindowLayoutService(platform, new WindowGridPlanner());
        var result = await service.ArrangeAsync(targets, new EmulatorWindowLayoutSettings
        {
            ItemsPerPageMode = LayoutItemsPerPageMode.All,
            SizeMode = EmulatorWindowSizeMode.Auto
        }, 0, CancellationToken.None);

        Assert.AreEqual(4, result.Plan.ItemsPerPage);
        Assert.AreEqual(1, result.Plan.PageCount);
        Assert.IsFalse(result.Applied);
        Assert.IsTrue(result.ResizeWasRejected);
        StringAssert.Contains(result.Warning, "Tự động phân trang");
        Assert.IsFalse(platform.Bounds.Values.SelectMany((left, index) =>
            platform.Bounds.Values.Skip(index + 1).Select(right => Intersects(left, right))).Any(value => value));
    }

    [TestMethod]
    public async Task HiddenPagesUseActualWidthsAndDoNotOverlap()
    {
        var platform = new FakeWindowPlatform(minimumWidth: 600, minimumHeight: 500);
        var targets = AddTargets(platform, 4);
        var service = new WindowsMemuWindowLayoutService(platform, new WindowGridPlanner());
        var settings = new EmulatorWindowLayoutSettings
        {
            ItemsPerPageMode = LayoutItemsPerPageMode.Custom,
            CustomItemsPerPage = 1,
            SizeMode = EmulatorWindowSizeMode.Auto,
            Gap = 8
        };

        var result = await service.ArrangeAsync(targets, settings, 0, CancellationToken.None);

        var visibleHandle = result.Plan.Placements.Single().WindowHandle;
        var parked = platform.Bounds.Where(item => item.Key != visibleHandle).Select(item => item.Value).ToList();
        Assert.IsFalse(parked.SelectMany((left, index) => parked.Skip(index + 1).Select(right => Intersects(left, right))).Any(value => value));
    }

    [TestMethod]
    public async Task FocusDetectsAWindowThatCannotGrow()
    {
        var platform = new FakeWindowPlatform(fixedSize: true);
        var target = AddTargets(platform, 1).Single();
        var service = new WindowsMemuWindowLayoutService(platform, new WindowGridPlanner());

        var warning = await service.FocusAsync(target, platform.GetDisplays()[0], CancellationToken.None);

        Assert.IsNotNull(warning);
    }

    [TestMethod]
    public async Task FocusPreservesAspectRatioAndReturnsToExactPreviousBounds()
    {
        var platform = new FakeWindowPlatform();
        var target = AddTargets(platform, 1).Single();
        var original = platform.Bounds[target.WindowHandle];
        var service = new WindowsMemuWindowLayoutService(platform, new WindowGridPlanner());

        var warning = await service.FocusAsync(target, platform.GetDisplays()[0], CancellationToken.None);

        Assert.IsNull(warning);
        Assert.AreEqual(new ScreenRectangle(200, 0, 400, 600), platform.Bounds[target.WindowHandle]);
        var repeatedWarning = await service.FocusAsync(target, platform.GetDisplays()[0], CancellationToken.None);
        Assert.IsNull(repeatedWarning);
        var restored = await service.ReturnFromFocusAsync(target, CancellationToken.None);
        Assert.IsTrue(restored.Restored);
        Assert.IsNull(restored.Warning);
        Assert.AreEqual(original, platform.Bounds[target.WindowHandle]);
    }

    [TestMethod]
    public async Task ArrangeRejectsARecycledWindowHandle()
    {
        var platform = new FakeWindowPlatform();
        var targets = AddTargets(platform, 2);
        platform.ProcessIds[targets[1].WindowHandle] = 9999;
        var service = new WindowsMemuWindowLayoutService(platform, new WindowGridPlanner());

        var result = await service.ArrangeAsync(targets, new EmulatorWindowLayoutSettings(), 0, CancellationToken.None);

        Assert.AreEqual(1, result.Plan.Placements.Count);
        Assert.IsNotNull(result.Warning);
    }

    [TestMethod]
    public async Task ArrangeReportsParkingFailure()
    {
        var platform = new FakeWindowPlatform();
        var targets = AddTargets(platform, 2);
        platform.FailedHandles.Add(targets[1].WindowHandle);
        var service = new WindowsMemuWindowLayoutService(platform, new WindowGridPlanner());
        var settings = new EmulatorWindowLayoutSettings
        {
            ItemsPerPageMode = LayoutItemsPerPageMode.Custom,
            CustomItemsPerPage = 1,
            SizeMode = EmulatorWindowSizeMode.MoveOnly
        };

        var result = await service.ArrangeAsync(targets, settings, 0, CancellationToken.None);

        Assert.IsNotNull(result.Warning);
    }

    [TestMethod]
    public async Task RestoreReportsAWindowThatDoesNotAcceptItsSavedSize()
    {
        var platform = new FakeWindowPlatform(fixedSize: true);
        var target = AddTargets(platform, 1).Single();
        var service = new WindowsMemuWindowLayoutService(platform, new WindowGridPlanner());
        var placements = new[]
        {
            new SavedWindowPlacement { InstanceIndex = 0, Left = 50, Top = 60, Width = 800, Height = 600 }
        };

        var warning = await service.RestoreOriginalAsync([target], placements, CancellationToken.None);

        Assert.IsNotNull(warning);
    }

    [TestMethod]
    public async Task ArrangeValidatesOuterClientAndRenderViewportInsteadOfOuterOnly()
    {
        var platform = new FakeWindowPlatform(renderChromeWidth: 20, renderChromeHeight: 80);
        var target = AddTargets(platform, 1).Single();
        platform.Bounds[target.WindowHandle] = new ScreenRectangle(10, 20, 340, 560);
        var service = new WindowsMemuWindowLayoutService(platform, new WindowGridPlanner());

        var result = await service.ArrangeAsync([target], new EmulatorWindowLayoutSettings
        {
            ItemsPerPageMode = LayoutItemsPerPageMode.All,
            SizeMode = EmulatorWindowSizeMode.Auto,
            EnableGeometryDiagnostics = true
        }, 0, CancellationToken.None);

        Assert.IsTrue(result.Applied);
        Assert.IsFalse(result.ResizeWasRejected);
        Assert.IsNotNull(result.Plan.Placements.Single().RenderBounds);
        Assert.AreEqual(1, result.GeometryDiagnostics.Count);
        StringAssert.Contains(result.GeometryDiagnostics[0], "outer=");
        StringAssert.Contains(result.GeometryDiagnostics[0], "client=");
        StringAssert.Contains(result.GeometryDiagnostics[0], "render=");
    }

    [TestMethod]
    public async Task ArrangeRejectsFakeSuccessWhenOuterChangesButRenderViewportDoesNot()
    {
        var platform = new FakeWindowPlatform(renderViewportFixed: true);
        var targets = AddTargets(platform, 2);
        var service = new WindowsMemuWindowLayoutService(platform, new WindowGridPlanner());

        var result = await service.ArrangeAsync(targets, new EmulatorWindowLayoutSettings
        {
            ItemsPerPageMode = LayoutItemsPerPageMode.All,
            SizeMode = EmulatorWindowSizeMode.Auto
        }, 0, CancellationToken.None);

        Assert.IsFalse(result.Applied);
        Assert.IsTrue(result.ResizeWasRejected);
        StringAssert.Contains(result.Warning, "Kích thước cố định");
    }

    [TestMethod]
    public async Task FocusParksOtherPageWindowsAndRestoresFullPageGeometry()
    {
        var platform = new FakeWindowPlatform(renderChromeWidth: 20, renderChromeHeight: 60);
        var targets = AddTargets(platform, 2);
        var originals = platform.Bounds.ToDictionary(pair => pair.Key, pair => pair.Value);
        var service = new WindowsMemuWindowLayoutService(platform, new WindowGridPlanner());

        var warning = await service.FocusAsync(targets[0], targets, platform.GetDisplays()[0], false, CancellationToken.None);

        Assert.IsNull(warning);
        Assert.IsTrue(platform.Bounds[targets[1].WindowHandle].Left >= 800);
        var restored = await service.ReturnFromFocusAsync(targets[0], CancellationToken.None);
        Assert.IsTrue(restored.Restored);
        Assert.IsNull(restored.Warning);
        CollectionAssert.AreEquivalent(originals.Values.ToArray(), platform.Bounds.Values.ToArray());
    }

    [TestMethod]
    public async Task ReturnFromFocusRejectsRecycledHandleBeforeMovingIt()
    {
        var platform = new FakeWindowPlatform();
        var target = AddTargets(platform, 1).Single();
        var service = new WindowsMemuWindowLayoutService(platform, new WindowGridPlanner());
        Assert.IsNull(await service.FocusAsync(target, platform.GetDisplays()[0], CancellationToken.None));
        platform.SetCalls.Clear();
        platform.ProcessIds[target.WindowHandle] = 9999;

        var restored = await service.ReturnFromFocusAsync(target, CancellationToken.None);

        Assert.IsTrue(restored.Restored);
        Assert.IsNotNull(restored.Warning);
        Assert.AreEqual(0, platform.SetCalls.Count, "Không được gọi SetWindowPos lên HWND đã thuộc process khác.");
    }

    [TestMethod]
    public async Task FocusWaitsForDelayedRenderViewportToSettle()
    {
        var platform = new FakeWindowPlatform(delayedRenderProbeCount: 2);
        var target = AddTargets(platform, 1).Single();
        var service = new WindowsMemuWindowLayoutService(platform, new WindowGridPlanner());

        var warning = await service.FocusAsync(target, platform.GetDisplays()[0], CancellationToken.None);

        Assert.IsNull(warning);
        Assert.IsTrue(platform.ProbeCounts[target.WindowHandle] >= 4);
    }

    private static List<WindowLayoutTarget> AddTargets(FakeWindowPlatform platform, int count)
    {
        var targets = new List<WindowLayoutTarget>();
        for (var index = 0; index < count; index++)
        {
            var bounds = new ScreenRectangle(10 + index * 20, 20 + index * 20, 320, 480);
            platform.Bounds[index + 1] = bounds;
            platform.ProcessIds[index + 1] = index + 100;
            targets.Add(new WindowLayoutTarget(index, $"VM {index}", index + 1, bounds, index + 100));
        }
        return targets;
    }

    private static bool Intersects(ScreenRectangle left, ScreenRectangle right) =>
        left.Left < right.Right && left.Right > right.Left && left.Top < right.Bottom && left.Bottom > right.Top;

    private sealed class FakeWindowPlatform(
        int minimumWidth = 0,
        int minimumHeight = 0,
        bool fixedSize = false,
        int renderChromeWidth = 0,
        int renderChromeHeight = 0,
        bool renderViewportFixed = false,
        int delayedRenderProbeCount = 0) : IWindowPlatform
    {
        public Dictionary<long, ScreenRectangle> Bounds { get; } = [];
        public Dictionary<long, int> ProcessIds { get; } = [];
        public HashSet<long> FailedHandles { get; } = [];
        public List<(long Handle, ScreenRectangle Bounds, bool Resize)> SetCalls { get; } = [];
        public Dictionary<long, int> ProbeCounts { get; } = [];
        private readonly Dictionary<long, ScreenRectangle> initialBounds = [];
        private readonly Dictionary<long, ScreenRectangle> settledRenderSources = [];
        private readonly Dictionary<long, ScreenRectangle> delayedRenderSources = [];
        private readonly Dictionary<long, int> delayedProbesRemaining = [];

        public IReadOnlyList<DisplayWorkArea> GetDisplays() =>
        [
            new("DISPLAY1", new ScreenRectangle(0, 0, 800, 600), true),
            new("DISPLAY2", new ScreenRectangle(800, 0, 1000, 800), false)
        ];

        public bool TryGetProcessId(long windowHandle, out int processId) => ProcessIds.TryGetValue(windowHandle, out processId);

        public bool TryGetBounds(long windowHandle, out ScreenRectangle bounds) => Bounds.TryGetValue(windowHandle, out bounds);

        public bool TryProbeWindow(long windowHandle, int expectedProcessId, out WindowGeometrySnapshot geometry)
        {
            geometry = null!;
            ProbeCounts[windowHandle] = ProbeCounts.GetValueOrDefault(windowHandle) + 1;
            if (!Bounds.TryGetValue(windowHandle, out var outer) ||
                !ProcessIds.TryGetValue(windowHandle, out var processId) || processId != expectedProcessId) return false;
            if (!initialBounds.ContainsKey(windowHandle)) initialBounds[windowHandle] = outer;
            if (!settledRenderSources.ContainsKey(windowHandle)) settledRenderSources[windowHandle] = outer;
            ScreenRectangle renderSource;
            if (delayedProbesRemaining.GetValueOrDefault(windowHandle) > 0)
            {
                renderSource = delayedRenderSources[windowHandle];
                delayedProbesRemaining[windowHandle]--;
            }
            else
            {
                renderSource = renderViewportFixed ? initialBounds[windowHandle] : outer;
                settledRenderSources[windowHandle] = renderSource;
            }
            var insetLeft = renderChromeWidth / 2;
            var insetTop = renderChromeHeight * 3 / 4;
            var render = new ScreenRectangle(
                outer.Left + insetLeft,
                outer.Top + insetTop,
                Math.Max(1, renderSource.Width - renderChromeWidth),
                Math.Max(1, renderSource.Height - renderChromeHeight));
            var client = new ScreenRectangle(outer.Left + 4, outer.Top + 28, Math.Max(1, outer.Width - 8), Math.Max(1, outer.Height - 32));
            geometry = new WindowGeometrySnapshot(windowHandle, processId, outer, outer, client,
                windowHandle + 1000, "Qt5QWindowIcon", render, []);
            return true;
        }

        public bool TrySetBounds(long windowHandle, ScreenRectangle bounds, bool resize)
        {
            SetCalls.Add((windowHandle, bounds, resize));
            if (FailedHandles.Contains(windowHandle) || !Bounds.TryGetValue(windowHandle, out var current)) return false;
            if (resize && delayedRenderProbeCount > 0)
            {
                delayedRenderSources[windowHandle] = settledRenderSources.GetValueOrDefault(windowHandle, current);
                delayedProbesRemaining[windowHandle] = delayedRenderProbeCount;
            }
            Bounds[windowHandle] = new ScreenRectangle(
                bounds.Left,
                bounds.Top,
                resize && !fixedSize ? Math.Max(bounds.Width, minimumWidth) : current.Width,
                resize && !fixedSize ? Math.Max(bounds.Height, minimumHeight) : current.Height);
            return true;
        }
    }
}
