using MEmuScriptStudio.Core.MEmu;
using MEmuScriptStudio.Core.Models;

namespace MEmuScriptStudio.Core.Tests;

[TestClass]
public sealed class WindowGridPlannerTests
{
    private readonly WindowGridPlanner planner = new();

    [TestMethod]
    public void CustomPageSizeAndColumnsCalculateRowsWithoutHardLimit()
    {
        var targets = Targets(37);
        var settings = new EmulatorWindowLayoutSettings
        {
            ItemsPerPageMode = LayoutItemsPerPageMode.Custom,
            CustomItemsPerPage = 17,
            ColumnMode = LayoutColumnMode.Custom,
            CustomColumns = 7,
            SizeMode = EmulatorWindowSizeMode.Auto,
            Gap = 6
        };

        var plan = planner.CreatePlan(targets, new ScreenRectangle(0, 0, 3440, 1400), settings, pageIndex: 1);

        Assert.AreEqual(17, plan.ItemsPerPage);
        Assert.AreEqual(3, plan.PageCount);
        Assert.AreEqual(7, plan.Columns);
        Assert.AreEqual(3, plan.Rows);
        Assert.AreEqual(17, plan.Placements.Count);
        Assert.IsFalse(HasOverlap(plan.Placements));
    }

    [TestMethod]
    public void CustomWindowSizeAutomaticallySplitsWhenRequestedPageCannotFitWorkArea()
    {
        var settings = new EmulatorWindowLayoutSettings
        {
            ItemsPerPageMode = LayoutItemsPerPageMode.AutoFit,
            ColumnMode = LayoutColumnMode.Auto,
            SizeMode = EmulatorWindowSizeMode.Custom,
            CustomWidth = 500,
            CustomHeight = 400,
            Gap = 10
        };

        var plan = planner.CreatePlan(Targets(8), new ScreenRectangle(0, 0, 1200, 900), settings, 0);

        Assert.AreEqual(4, plan.ItemsPerPage);
        Assert.AreEqual(2, plan.PageCount);
        Assert.AreEqual(2, plan.Rows);
        Assert.IsFalse(HasOverlap(plan.Placements));
    }

    [TestMethod]
    public void MoveOnlyPreservesEveryWindowSizeAndStillUsesWorkAreaPaging()
    {
        var targets = Enumerable.Range(0, 5)
            .Select(index => new WindowLayoutTarget(index, $"VM {index}", index + 1, new ScreenRectangle(20, 20, 600, 500)))
            .ToList();
        var settings = new EmulatorWindowLayoutSettings
        {
            ItemsPerPageMode = LayoutItemsPerPageMode.AutoFit,
            SizeMode = EmulatorWindowSizeMode.MoveOnly,
            Gap = 8
        };

        var plan = planner.CreatePlan(targets, new ScreenRectangle(100, 40, 1300, 1040), settings, 0);

        Assert.AreEqual(4, plan.ItemsPerPage);
        Assert.AreEqual(2, plan.PageCount);
        Assert.IsTrue(plan.Placements.All(item => item.Bounds.Width == 600 && item.Bounds.Height == 500));
        Assert.IsTrue(plan.Placements.All(item => item.Bounds.Left >= 100 && item.Bounds.Top >= 40));
        Assert.IsFalse(HasOverlap(plan.Placements));
    }

    [TestMethod]
    public void AutoAndCustomPreserveAspectRatioAndCenterInsideCells()
    {
        var targets = new[] { new WindowLayoutTarget(0, "Portrait", 1, new ScreenRectangle(0, 0, 320, 568)) };
        var auto = planner.CreatePlan(targets, new ScreenRectangle(0, 0, 800, 420), new EmulatorWindowLayoutSettings
        {
            SizeMode = EmulatorWindowSizeMode.Auto,
            ItemsPerPageMode = LayoutItemsPerPageMode.All
        }, 0).Placements.Single().Bounds;
        Assert.AreEqual(237, auto.Width, 1);
        Assert.AreEqual(420, auto.Height);
        Assert.AreEqual((800 - auto.Width) / 2, auto.Left);

        var custom = planner.CreatePlan(targets, new ScreenRectangle(0, 0, 800, 600), new EmulatorWindowLayoutSettings
        {
            SizeMode = EmulatorWindowSizeMode.Custom,
            CustomWidth = 480,
            CustomHeight = 420,
            PreserveAspectRatio = true,
            ItemsPerPageMode = LayoutItemsPerPageMode.All
        }, 0).Placements.Single().Bounds;
        Assert.AreEqual(237, custom.Width, 1);
        Assert.AreEqual(420, custom.Height);
    }

    [TestMethod]
    public void SinglePageModeNeverSilentlyCreatesAdditionalPages()
    {
        var plan = planner.CreatePlan(Targets(8), new ScreenRectangle(0, 0, 1200, 900), new EmulatorWindowLayoutSettings
        {
            ItemsPerPageMode = LayoutItemsPerPageMode.All,
            SizeMode = EmulatorWindowSizeMode.Custom,
            CustomWidth = 500,
            CustomHeight = 400
        }, 0);
        Assert.AreEqual(8, plan.ItemsPerPage);
        Assert.AreEqual(1, plan.PageCount);
    }

    [TestMethod]
    public void RenderViewportDrivesAspectRatioAndOuterBoundsIncludeChrome()
    {
        var outer = new ScreenRectangle(10, 20, 340, 560);
        var render = new ScreenRectangle(20, 80, 320, 480);
        var geometry = new WindowGeometrySnapshot(1, 100, outer, outer,
            new ScreenRectangle(14, 48, 332, 528), 2, "Qt5QWindowIcon", render, []);
        var target = new WindowLayoutTarget(0, "Portrait", 1, render, 100, geometry);

        var placement = planner.CreatePlan([target], new ScreenRectangle(0, 0, 800, 600),
            new EmulatorWindowLayoutSettings
            {
                SizeMode = EmulatorWindowSizeMode.Auto,
                ItemsPerPageMode = LayoutItemsPerPageMode.All
            }, 0).Placements.Single();

        Assert.IsNotNull(placement.RenderBounds);
        Assert.AreEqual(placement.RenderBounds!.Value.Width + geometry.ChromeWidth, placement.Bounds.Width);
        Assert.AreEqual(placement.RenderBounds.Value.Height + geometry.ChromeHeight, placement.Bounds.Height);
        Assert.AreEqual((double)render.Width / render.Height,
            (double)placement.RenderBounds.Value.Width / placement.RenderBounds.Value.Height, 0.01);
        Assert.AreEqual((800 - placement.Bounds.Width) / 2, placement.Bounds.Left);
    }

    private static List<WindowLayoutTarget> Targets(int count) => Enumerable.Range(0, count)
        .Select(index => new WindowLayoutTarget(index, $"VM {index}", index + 1, new ScreenRectangle(0, 0, 320, 480)))
        .ToList();

    private static bool HasOverlap(IReadOnlyList<PlannedWindowPlacement> placements) =>
        placements.SelectMany((left, index) => placements.Skip(index + 1).Select(right =>
                left.Bounds.Left < right.Bounds.Right && left.Bounds.Right > right.Bounds.Left &&
                left.Bounds.Top < right.Bounds.Bottom && left.Bounds.Bottom > right.Bounds.Top))
            .Any(value => value);
}
