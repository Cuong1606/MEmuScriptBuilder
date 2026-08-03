using MEmuScriptStudio.Core.Models;

namespace MEmuScriptStudio.Core.MEmu;

public interface IMemuWindowLayoutService
{
    Task<IReadOnlyList<DisplayWorkArea>> GetDisplaysAsync(CancellationToken cancellationToken);

    Task<WindowLayoutApplyResult> ArrangeAsync(
        IReadOnlyList<WindowLayoutTarget> targets,
        EmulatorWindowLayoutSettings settings,
        int pageIndex,
        CancellationToken cancellationToken);

    Task<string?> FocusAsync(
        WindowLayoutTarget target,
        DisplayWorkArea display,
        CancellationToken cancellationToken);

    Task<string?> FocusAsync(
        WindowLayoutTarget target,
        IReadOnlyList<WindowLayoutTarget> pageTargets,
        DisplayWorkArea display,
        bool enableGeometryDiagnostics,
        CancellationToken cancellationToken) =>
        FocusAsync(target, display, cancellationToken);

    Task<string?> RestoreOriginalAsync(
        IReadOnlyList<WindowLayoutTarget> targets,
        IReadOnlyList<SavedWindowPlacement> placements,
        CancellationToken cancellationToken);

    Task<(bool Restored, string? Warning)> ReturnFromFocusAsync(
        WindowLayoutTarget target,
        CancellationToken cancellationToken) =>
        Task.FromResult<(bool, string?)>((false, null));
}

public sealed class WindowGridPlanner
{
    public WindowGridPlan CreatePlan(
        IReadOnlyList<WindowLayoutTarget> targets,
        ScreenRectangle workArea,
        EmulatorWindowLayoutSettings settings,
        int pageIndex,
        int? itemsPerPageOverride = null)
    {
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(settings);
        if (workArea.Width <= 0 || workArea.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(workArea), "Vùng làm việc của màn hình không hợp lệ.");
        if (settings.Gap < 0) throw new ArgumentOutOfRangeException(nameof(settings.Gap));

        if (targets.Count == 0)
            return new WindowGridPlan { PageIndex = 0, PageCount = 0, ItemsPerPage = 0, Columns = 0, Rows = 0 };

        var requested = itemsPerPageOverride ?? (settings.ItemsPerPageMode switch
        {
            LayoutItemsPerPageMode.Custom => settings.CustomItemsPerPage,
            _ => targets.Count
        });
        if (requested <= 0) throw new ArgumentOutOfRangeException(nameof(settings.CustomItemsPerPage));

        var itemsPerPage = Math.Min(requested, targets.Count);
        if (settings.ItemsPerPageMode != LayoutItemsPerPageMode.All &&
            (settings.SizeMode is EmulatorWindowSizeMode.Custom or EmulatorWindowSizeMode.MoveOnly))
        {
            var width = settings.SizeMode == EmulatorWindowSizeMode.Custom
                ? settings.CustomWidth + targets.Max(target => target.Geometry?.ChromeWidth ?? 0)
                : Math.Max(1, targets.Max(target => target.Geometry?.OuterBounds.Width ?? target.CurrentBounds.Width));
            var height = settings.SizeMode == EmulatorWindowSizeMode.Custom
                ? settings.CustomHeight + targets.Max(target => target.Geometry?.ChromeHeight ?? 0)
                : Math.Max(1, targets.Max(target => target.Geometry?.OuterBounds.Height ?? target.CurrentBounds.Height));
            if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(settings.CustomWidth));
            var fitColumns = Math.Max(1, (workArea.Width + settings.Gap) / checked(width + settings.Gap));
            var fitRows = Math.Max(1, (workArea.Height + settings.Gap) / checked(height + settings.Gap));
            var requestedColumns = settings.ColumnMode == LayoutColumnMode.Custom
                ? Math.Max(1, settings.CustomColumns)
                : fitColumns;
            itemsPerPage = Math.Min(itemsPerPage, checked(Math.Min(fitColumns, requestedColumns) * fitRows));
        }

        var pageCount = (targets.Count + itemsPerPage - 1) / itemsPerPage;
        var normalizedPage = Math.Clamp(pageIndex, 0, pageCount - 1);
        var pageTargets = targets.Skip(normalizedPage * itemsPerPage).Take(itemsPerPage).ToList();
        var columns = ResolveColumns(pageTargets, workArea, settings);
        var rows = (pageTargets.Count + columns - 1) / columns;
        var cellWidth = Math.Max(1, (workArea.Width - settings.Gap * Math.Max(0, columns - 1)) / columns);
        var cellHeight = Math.Max(1, (workArea.Height - settings.Gap * Math.Max(0, rows - 1)) / rows);
        var placements = new List<PlannedWindowPlacement>(pageTargets.Count);

        for (var index = 0; index < pageTargets.Count; index++)
        {
            var target = pageTargets[index];
            var row = index / columns;
            var column = index % columns;
            var maximumWidth = settings.SizeMode == EmulatorWindowSizeMode.Custom
                ? Math.Min(settings.CustomWidth, Math.Max(1, cellWidth - (target.Geometry?.ChromeWidth ?? 0)))
                : Math.Max(1, cellWidth - (target.Geometry?.ChromeWidth ?? 0));
            var maximumHeight = settings.SizeMode == EmulatorWindowSizeMode.Custom
                ? Math.Min(settings.CustomHeight, Math.Max(1, cellHeight - (target.Geometry?.ChromeHeight ?? 0)))
                : Math.Max(1, cellHeight - (target.Geometry?.ChromeHeight ?? 0));
            var preserveRatio = settings.SizeMode == EmulatorWindowSizeMode.Auto ||
                                (settings.SizeMode == EmulatorWindowSizeMode.Custom && settings.PreserveAspectRatio);
            (int Width, int Height) fitted = settings.SizeMode == EmulatorWindowSizeMode.MoveOnly
                ? (target.Geometry?.RenderViewportBounds.Width ?? target.CurrentBounds.Width,
                   target.Geometry?.RenderViewportBounds.Height ?? target.CurrentBounds.Height)
                : preserveRatio
                    ? FitInside(target.CurrentBounds.Width, target.CurrentBounds.Height, maximumWidth, maximumHeight)
                    : (maximumWidth, maximumHeight);
            var width = settings.SizeMode == EmulatorWindowSizeMode.MoveOnly
                ? target.Geometry?.OuterBounds.Width ?? target.CurrentBounds.Width
                : fitted.Width + (target.Geometry?.ChromeWidth ?? 0);
            var height = settings.SizeMode == EmulatorWindowSizeMode.MoveOnly
                ? target.Geometry?.OuterBounds.Height ?? target.CurrentBounds.Height
                : fitted.Height + (target.Geometry?.ChromeHeight ?? 0);
            var left = workArea.Left + column * (cellWidth + settings.Gap) + Math.Max(0, (cellWidth - width) / 2);
            var top = workArea.Top + row * (cellHeight + settings.Gap) + Math.Max(0, (cellHeight - height) / 2);
            var renderBounds = new ScreenRectangle(
                left + Math.Max(0, target.Geometry?.RenderInsetLeft ?? 0),
                top + Math.Max(0, target.Geometry?.RenderInsetTop ?? 0),
                fitted.Width,
                fitted.Height);
            placements.Add(new PlannedWindowPlacement(
                target.InstanceIndex,
                target.WindowHandle,
                normalizedPage,
                row,
                column,
                new ScreenRectangle(left, top, width, height),
                renderBounds));
        }

        return new WindowGridPlan
        {
            PageIndex = normalizedPage,
            PageCount = pageCount,
            ItemsPerPage = itemsPerPage,
            Columns = columns,
            Rows = rows,
            Placements = placements
        };
    }

    internal static (int Width, int Height) FitInside(
        int sourceWidth,
        int sourceHeight,
        int maximumWidth,
        int maximumHeight)
    {
        sourceWidth = Math.Max(1, sourceWidth);
        sourceHeight = Math.Max(1, sourceHeight);
        maximumWidth = Math.Max(1, maximumWidth);
        maximumHeight = Math.Max(1, maximumHeight);
        var scale = Math.Min((double)maximumWidth / sourceWidth, (double)maximumHeight / sourceHeight);
        return (
            Math.Max(1, Math.Min(maximumWidth, (int)Math.Floor(sourceWidth * scale))),
            Math.Max(1, Math.Min(maximumHeight, (int)Math.Floor(sourceHeight * scale))));
    }

    private static int ResolveAutomaticColumns(int itemCount, ScreenRectangle workArea)
    {
        if (itemCount <= 1) return Math.Max(1, itemCount);
        var aspect = (double)workArea.Width / workArea.Height;
        return Math.Clamp((int)Math.Ceiling(Math.Sqrt(itemCount * aspect)), 1, itemCount);
    }

    private static int ResolveColumns(
        IReadOnlyList<WindowLayoutTarget> pageTargets,
        ScreenRectangle workArea,
        EmulatorWindowLayoutSettings settings)
    {
        if (settings.SizeMode == EmulatorWindowSizeMode.Auto)
            return settings.ColumnMode == LayoutColumnMode.Custom
                ? Math.Min(Math.Max(1, settings.CustomColumns), Math.Max(1, pageTargets.Count))
                : ResolveAutomaticColumns(pageTargets.Count, workArea);

        var width = settings.SizeMode == EmulatorWindowSizeMode.Custom
            ? settings.CustomWidth + pageTargets.Max(target => target.Geometry?.ChromeWidth ?? 0)
            : Math.Max(1, pageTargets.Max(target => target.Geometry?.OuterBounds.Width ?? target.CurrentBounds.Width));
        var fittingColumns = Math.Max(1, (workArea.Width + settings.Gap) / checked(width + settings.Gap));
        var desiredColumns = settings.ColumnMode == LayoutColumnMode.Custom
            ? Math.Max(1, settings.CustomColumns)
            : fittingColumns;
        return Math.Min(Math.Min(fittingColumns, desiredColumns), Math.Max(1, pageTargets.Count));
    }
}
