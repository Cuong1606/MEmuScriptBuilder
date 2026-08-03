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

    Task<string?> RestoreOriginalAsync(
        IReadOnlyList<WindowLayoutTarget> targets,
        IReadOnlyList<SavedWindowPlacement> placements,
        CancellationToken cancellationToken);
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
        if (settings.SizeMode is EmulatorWindowSizeMode.Custom or EmulatorWindowSizeMode.MoveOnly)
        {
            var width = settings.SizeMode == EmulatorWindowSizeMode.Custom
                ? settings.CustomWidth
                : Math.Max(1, targets.Max(target => target.CurrentBounds.Width));
            var height = settings.SizeMode == EmulatorWindowSizeMode.Custom
                ? settings.CustomHeight
                : Math.Max(1, targets.Max(target => target.CurrentBounds.Height));
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
            var width = settings.SizeMode switch
            {
                EmulatorWindowSizeMode.Custom => Math.Min(settings.CustomWidth, cellWidth),
                EmulatorWindowSizeMode.MoveOnly => target.CurrentBounds.Width,
                _ => cellWidth
            };
            var height = settings.SizeMode switch
            {
                EmulatorWindowSizeMode.Custom => Math.Min(settings.CustomHeight, cellHeight),
                EmulatorWindowSizeMode.MoveOnly => target.CurrentBounds.Height,
                _ => cellHeight
            };
            var left = workArea.Left + column * (cellWidth + settings.Gap) + Math.Max(0, (cellWidth - width) / 2);
            var top = workArea.Top + row * (cellHeight + settings.Gap) + Math.Max(0, (cellHeight - height) / 2);
            placements.Add(new PlannedWindowPlacement(
                target.InstanceIndex,
                target.WindowHandle,
                normalizedPage,
                row,
                column,
                new ScreenRectangle(left, top, width, height)));
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
            ? settings.CustomWidth
            : Math.Max(1, pageTargets.Max(target => target.CurrentBounds.Width));
        var fittingColumns = Math.Max(1, (workArea.Width + settings.Gap) / checked(width + settings.Gap));
        var desiredColumns = settings.ColumnMode == LayoutColumnMode.Custom
            ? Math.Max(1, settings.CustomColumns)
            : fittingColumns;
        return Math.Min(Math.Min(fittingColumns, desiredColumns), Math.Max(1, pageTargets.Count));
    }
}
