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

    private sealed class FakeWindowPlatform(int minimumWidth = 0, int minimumHeight = 0, bool fixedSize = false) : IWindowPlatform
    {
        public Dictionary<long, ScreenRectangle> Bounds { get; } = [];
        public Dictionary<long, int> ProcessIds { get; } = [];
        public HashSet<long> FailedHandles { get; } = [];
        public List<(long Handle, ScreenRectangle Bounds, bool Resize)> SetCalls { get; } = [];

        public IReadOnlyList<DisplayWorkArea> GetDisplays() =>
        [
            new("DISPLAY1", new ScreenRectangle(0, 0, 800, 600), true),
            new("DISPLAY2", new ScreenRectangle(800, 0, 1000, 800), false)
        ];

        public bool TryGetProcessId(long windowHandle, out int processId) => ProcessIds.TryGetValue(windowHandle, out processId);

        public bool TryGetBounds(long windowHandle, out ScreenRectangle bounds) => Bounds.TryGetValue(windowHandle, out bounds);

        public bool TrySetBounds(long windowHandle, ScreenRectangle bounds, bool resize)
        {
            SetCalls.Add((windowHandle, bounds, resize));
            if (FailedHandles.Contains(windowHandle) || !Bounds.TryGetValue(windowHandle, out var current)) return false;
            Bounds[windowHandle] = new ScreenRectangle(
                bounds.Left,
                bounds.Top,
                resize && !fixedSize ? Math.Max(bounds.Width, minimumWidth) : current.Width,
                resize && !fixedSize ? Math.Max(bounds.Height, minimumHeight) : current.Height);
            return true;
        }
    }
}
