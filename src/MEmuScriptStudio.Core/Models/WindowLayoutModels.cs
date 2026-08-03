namespace MEmuScriptStudio.Core.Models;

public enum EmulatorSortMode
{
    Index,
    Name,
    Custom
}

public enum LayoutItemsPerPageMode
{
    AutoFit,
    Custom,
    All
}

public enum LayoutColumnMode
{
    Auto,
    Custom
}

public enum EmulatorWindowSizeMode
{
    MoveOnly,
    Auto,
    Custom
}

public sealed class EmulatorWindowLayoutSettings
{
    public EmulatorSortMode SortMode { get; set; } = EmulatorSortMode.Index;
    public List<int> CustomOrder { get; init; } = [];
    public LayoutItemsPerPageMode ItemsPerPageMode { get; set; } = LayoutItemsPerPageMode.AutoFit;
    public int CustomItemsPerPage { get; set; } = 4;
    public LayoutColumnMode ColumnMode { get; set; } = LayoutColumnMode.Auto;
    public int CustomColumns { get; set; } = 2;
    public EmulatorWindowSizeMode SizeMode { get; set; } = EmulatorWindowSizeMode.Auto;
    public int CustomWidth { get; set; } = 480;
    public int CustomHeight { get; set; } = 800;
    public int Gap { get; set; } = 8;
    public string? DisplayDeviceName { get; set; }
    public int CurrentPage { get; set; }
    public List<SavedWindowPlacement> OriginalPlacements { get; init; } = [];
}

public sealed class SavedWindowPlacement
{
    public int InstanceIndex { get; set; }
    public int Left { get; set; }
    public int Top { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }

    public ScreenRectangle ToRectangle() => new(Left, Top, Width, Height);
}

public sealed record DisplayWorkArea(string DeviceName, ScreenRectangle WorkArea, bool IsPrimary)
{
    public string DisplayName => $"{DeviceName} — {WorkArea.Width}×{WorkArea.Height}" + (IsPrimary ? " (chính)" : string.Empty);
}

public sealed record WindowLayoutTarget(
    int InstanceIndex,
    string InstanceName,
    long WindowHandle,
    ScreenRectangle CurrentBounds,
    int? ProcessId = null);

public sealed record PlannedWindowPlacement(
    int InstanceIndex,
    long WindowHandle,
    int PageIndex,
    int Row,
    int Column,
    ScreenRectangle Bounds);

public sealed class WindowGridPlan
{
    public int PageIndex { get; init; }
    public int PageCount { get; init; }
    public int ItemsPerPage { get; init; }
    public int Columns { get; init; }
    public int Rows { get; init; }
    public IReadOnlyList<PlannedWindowPlacement> Placements { get; init; } = [];
}

public sealed class WindowLayoutApplyResult
{
    public required WindowGridPlan Plan { get; init; }
    public IReadOnlyList<SavedWindowPlacement> CapturedOriginalPlacements { get; init; } = [];
    public bool ResizeWasRejected { get; init; }
    public string? Warning { get; init; }
}
