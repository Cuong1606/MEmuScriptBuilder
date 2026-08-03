using System.Collections.ObjectModel;
using MEmuScriptStudio.Core.MEmu;
using MEmuScriptStudio.Core.Models;
using ScriptAssignmentModeValue = MEmuScriptStudio.Core.Models.ScriptAssignmentMode;

namespace MEmuScriptStudio.App.ViewModels;

public sealed partial class MainViewModel
{
    private readonly IMemuWindowLayoutService? windowLayoutService;
    private ScriptAssignmentModeValue scriptAssignmentMode = ScriptAssignmentModeValue.OneScriptForAll;
    private ScriptItemViewModel? bulkAssignmentScript;
    private EmulatorSortMode layoutSortMode = EmulatorSortMode.Index;
    private LayoutItemsPerPageMode layoutItemsPerPageMode = LayoutItemsPerPageMode.AutoFit;
    private int customItemsPerPage = 4;
    private LayoutColumnMode layoutColumnMode = LayoutColumnMode.Auto;
    private int customColumns = 2;
    private EmulatorWindowSizeMode emulatorWindowSizeMode = EmulatorWindowSizeMode.Auto;
    private int customWindowWidth = 480;
    private int customWindowHeight = 800;
    private int windowGap = 8;
    private DisplayWorkArea? selectedDisplay;
    private int currentLayoutPage;
    private int layoutPageCount;
    private int effectiveItemsPerPage;
    private int effectiveRows;
    private int layoutMovePosition = 1;
    private bool isArrangingWindows;
    private readonly List<int> customLayoutOrder = [];
    private readonly List<SavedWindowPlacement> originalWindowPlacements = [];

    public ObservableCollection<DisplayWorkArea> Displays { get; } = [];

    public AsyncCommand AssignScriptToSelectedCommand { get; private set; } = null!;
    public AsyncCommand AssignCurrentScriptToAllCommand { get; private set; } = null!;
    public AsyncCommand MoveLayoutUpCommand { get; private set; } = null!;
    public AsyncCommand MoveLayoutDownCommand { get; private set; } = null!;
    public AsyncCommand MoveLayoutToPositionCommand { get; private set; } = null!;
    public AsyncCommand ArrangeGridCommand { get; private set; } = null!;
    public AsyncCommand PreviousLayoutPageCommand { get; private set; } = null!;
    public AsyncCommand NextLayoutPageCommand { get; private set; } = null!;
    public AsyncCommand FocusEmulatorCommand { get; private set; } = null!;
    public AsyncCommand ReturnToGridCommand { get; private set; } = null!;
    public AsyncCommand RestoreOriginalLayoutCommand { get; private set; } = null!;

    public ScriptAssignmentModeValue ScriptAssignmentMode
    {
        get => scriptAssignmentMode;
        set
        {
            if (!CanChangeSelection || !SetProperty(ref scriptAssignmentMode, value)) return;
            OnPropertyChanged(nameof(IsOneScriptForAll));
            OnPropertyChanged(nameof(IsPerInstanceScript));
            UpdateRunConfigurationState();
            RaiseWorkspaceCommandStates();
            PersistAssignmentMode();
        }
    }

    public bool IsOneScriptForAll
    {
        get => ScriptAssignmentMode == ScriptAssignmentModeValue.OneScriptForAll;
        set { if (value) ScriptAssignmentMode = ScriptAssignmentModeValue.OneScriptForAll; }
    }

    public bool IsPerInstanceScript
    {
        get => ScriptAssignmentMode == ScriptAssignmentModeValue.PerInstance;
        set { if (value) ScriptAssignmentMode = ScriptAssignmentModeValue.PerInstance; }
    }

    public ScriptItemViewModel? BulkAssignmentScript
    {
        get => bulkAssignmentScript;
        set
        {
            if (!SetProperty(ref bulkAssignmentScript, value)) return;
            RaiseWorkspaceCommandStates();
        }
    }

    public int SelectedLayoutTargetCount => RunTargets.Count(item => item.IsLayoutSelected);
    public bool CanChangeWindowLayout => windowLayoutService is not null && !IsBusy && !IsCapturing && !isArrangingWindows;

    public EmulatorSortMode LayoutSortMode
    {
        get => layoutSortMode;
        set
        {
            if (RunTargets.Count > 0 && !CanChangeWindowLayout) return;
            if (value == EmulatorSortMode.Custom && layoutSortMode != EmulatorSortMode.Custom)
            {
                customLayoutOrder.Clear();
                customLayoutOrder.AddRange(RunTargets.Select(item => item.Index));
            }
            if (!SetProperty(ref layoutSortMode, value)) return;
            OnPropertyChanged(nameof(IsSortByIndex));
            OnPropertyChanged(nameof(IsSortByName));
            OnPropertyChanged(nameof(IsSortCustom));
            ApplyTargetSort();
            RaiseWorkspaceCommandStates();
        }
    }

    public bool IsSortByIndex { get => LayoutSortMode == EmulatorSortMode.Index; set { if (value) LayoutSortMode = EmulatorSortMode.Index; } }
    public bool IsSortByName { get => LayoutSortMode == EmulatorSortMode.Name; set { if (value) LayoutSortMode = EmulatorSortMode.Name; } }
    public bool IsSortCustom { get => LayoutSortMode == EmulatorSortMode.Custom; set { if (value) LayoutSortMode = EmulatorSortMode.Custom; } }

    public LayoutItemsPerPageMode LayoutItemsPerPageMode
    {
        get => layoutItemsPerPageMode;
        set
        {
            if (!SetProperty(ref layoutItemsPerPageMode, value)) return;
            OnPropertyChanged(nameof(IsAutoItemsPerPage));
            OnPropertyChanged(nameof(IsCustomItemsPerPage));
            OnPropertyChanged(nameof(IsAllItemsPerPage));
            UpdateLayoutConfigurationState();
        }
    }

    public bool IsAutoItemsPerPage { get => LayoutItemsPerPageMode == LayoutItemsPerPageMode.AutoFit; set { if (value) LayoutItemsPerPageMode = LayoutItemsPerPageMode.AutoFit; } }
    public bool IsCustomItemsPerPage { get => LayoutItemsPerPageMode == LayoutItemsPerPageMode.Custom; set { if (value) LayoutItemsPerPageMode = LayoutItemsPerPageMode.Custom; } }
    public bool IsAllItemsPerPage { get => LayoutItemsPerPageMode == LayoutItemsPerPageMode.All; set { if (value) LayoutItemsPerPageMode = LayoutItemsPerPageMode.All; } }
    public int CustomItemsPerPage { get => customItemsPerPage; set { if (SetProperty(ref customItemsPerPage, value)) UpdateLayoutConfigurationState(); } }

    public LayoutColumnMode LayoutColumnMode
    {
        get => layoutColumnMode;
        set
        {
            if (!SetProperty(ref layoutColumnMode, value)) return;
            OnPropertyChanged(nameof(IsAutoColumns));
            OnPropertyChanged(nameof(IsCustomColumns));
            UpdateLayoutConfigurationState();
        }
    }

    public bool IsAutoColumns { get => LayoutColumnMode == LayoutColumnMode.Auto; set { if (value) LayoutColumnMode = LayoutColumnMode.Auto; } }
    public bool IsCustomColumns { get => LayoutColumnMode == LayoutColumnMode.Custom; set { if (value) LayoutColumnMode = LayoutColumnMode.Custom; } }
    public int CustomColumns { get => customColumns; set { if (SetProperty(ref customColumns, value)) UpdateLayoutConfigurationState(); } }

    public EmulatorWindowSizeMode EmulatorWindowSizeMode
    {
        get => emulatorWindowSizeMode;
        set
        {
            if (!SetProperty(ref emulatorWindowSizeMode, value)) return;
            OnPropertyChanged(nameof(IsMoveOnly));
            OnPropertyChanged(nameof(IsAutomaticWindowSize));
            OnPropertyChanged(nameof(IsCustomWindowSize));
            UpdateLayoutConfigurationState();
        }
    }

    public bool IsMoveOnly { get => EmulatorWindowSizeMode == EmulatorWindowSizeMode.MoveOnly; set { if (value) EmulatorWindowSizeMode = EmulatorWindowSizeMode.MoveOnly; } }
    public bool IsAutomaticWindowSize { get => EmulatorWindowSizeMode == EmulatorWindowSizeMode.Auto; set { if (value) EmulatorWindowSizeMode = EmulatorWindowSizeMode.Auto; } }
    public bool IsCustomWindowSize { get => EmulatorWindowSizeMode == EmulatorWindowSizeMode.Custom; set { if (value) EmulatorWindowSizeMode = EmulatorWindowSizeMode.Custom; } }
    public int CustomWindowWidth { get => customWindowWidth; set { if (SetProperty(ref customWindowWidth, value)) UpdateLayoutConfigurationState(); } }
    public int CustomWindowHeight { get => customWindowHeight; set { if (SetProperty(ref customWindowHeight, value)) UpdateLayoutConfigurationState(); } }
    public int WindowGap { get => windowGap; set { if (SetProperty(ref windowGap, value)) UpdateLayoutConfigurationState(); } }

    public DisplayWorkArea? SelectedDisplay { get => selectedDisplay; set { if (SetProperty(ref selectedDisplay, value)) UpdateLayoutConfigurationState(); } }
    public int CurrentLayoutPage { get => currentLayoutPage; private set { if (SetProperty(ref currentLayoutPage, value)) { OnPropertyChanged(nameof(CurrentLayoutPageDisplay)); RaiseWorkspaceCommandStates(); } } }
    public int CurrentLayoutPageDisplay => LayoutPageCount == 0 ? 0 : CurrentLayoutPage + 1;
    public int LayoutPageCount { get => layoutPageCount; private set { if (SetProperty(ref layoutPageCount, value)) { OnPropertyChanged(nameof(CurrentLayoutPageDisplay)); RaiseWorkspaceCommandStates(); } } }
    public int EffectiveItemsPerPage { get => effectiveItemsPerPage; private set => SetProperty(ref effectiveItemsPerPage, value); }
    public int EffectiveRows { get => effectiveRows; private set => SetProperty(ref effectiveRows, value); }
    public int LayoutMovePosition { get => layoutMovePosition; set => SetProperty(ref layoutMovePosition, value); }
    public string? LayoutConfigurationError => ValidateLayoutConfiguration();

    private void InitializeWorkspaceCommands()
    {
        AssignScriptToSelectedCommand = new AsyncCommand(AssignScriptToSelectedAsync,
            () => IsPerInstanceScript && BulkAssignmentScript is not null && SelectedLayoutTargetCount > 0 && CanChangeSelection,
            ReportUnexpectedError);
        AssignCurrentScriptToAllCommand = new AsyncCommand(AssignCurrentScriptToAllAsync,
            () => IsPerInstanceScript && SelectedScript is not null && RunTargets.Count > 0 && CanChangeSelection,
            ReportUnexpectedError);
        MoveLayoutUpCommand = new AsyncCommand(() => MoveSelectedLayoutTargetsAsync(-1), () => CanMoveLayoutTargets(-1), ReportUnexpectedError);
        MoveLayoutDownCommand = new AsyncCommand(() => MoveSelectedLayoutTargetsAsync(1), () => CanMoveLayoutTargets(1), ReportUnexpectedError);
        MoveLayoutToPositionCommand = new AsyncCommand(MoveSelectedLayoutTargetsToPositionAsync,
            () => SelectedLayoutTargetCount > 0 && CanChangeWindowLayout && LayoutMovePosition > 0,
            ReportUnexpectedError);
        ArrangeGridCommand = new AsyncCommand(() => ArrangeGridAsync(CurrentLayoutPage),
            () => CanChangeWindowLayout && BuildWindowTargets().Count > 0 && ValidateLayoutConfiguration() is null,
            ReportUnexpectedError);
        PreviousLayoutPageCommand = new AsyncCommand(() => ArrangeGridAsync(CurrentLayoutPage - 1),
            () => CanChangeWindowLayout && CurrentLayoutPage > 0,
            ReportUnexpectedError);
        NextLayoutPageCommand = new AsyncCommand(() => ArrangeGridAsync(CurrentLayoutPage + 1),
            () => CanChangeWindowLayout && CurrentLayoutPage + 1 < LayoutPageCount,
            ReportUnexpectedError);
        FocusEmulatorCommand = new AsyncCommand(FocusSelectedEmulatorAsync,
            () => CanChangeWindowLayout && SelectedLayoutTargetCount == 1,
            ReportUnexpectedError);
        ReturnToGridCommand = new AsyncCommand(() => ArrangeGridAsync(CurrentLayoutPage),
            () => CanChangeWindowLayout && LayoutPageCount > 0,
            ReportUnexpectedError);
        RestoreOriginalLayoutCommand = new AsyncCommand(RestoreOriginalLayoutAsync,
            () => CanChangeWindowLayout && originalWindowPlacements.Count > 0,
            ReportUnexpectedError);
    }

    private async Task InitializeWindowWorkspaceAsync(CancellationToken cancellationToken)
    {
        if (windowLayoutService is null) return;
        try
        {
            var displays = await windowLayoutService.GetDisplaysAsync(cancellationToken);
            Displays.Clear();
            foreach (var display in displays) Displays.Add(display);
            SelectedDisplay = Displays.FirstOrDefault(item => string.Equals(item.DeviceName, applicationSettings.WindowLayout.DisplayDeviceName, StringComparison.OrdinalIgnoreCase))
                ?? Displays.FirstOrDefault(item => item.IsPrimary)
                ?? Displays.FirstOrDefault();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) { StatusMessage = $"{StatusMessage} Không thể đọc danh sách màn hình ({exception.Message})."; }
        RaiseWorkspaceCommandStates();
    }

    private void ApplyWindowLayoutSettings(EmulatorWindowLayoutSettings settings)
    {
        layoutSortMode = settings.SortMode;
        layoutItemsPerPageMode = settings.ItemsPerPageMode;
        customItemsPerPage = settings.CustomItemsPerPage;
        layoutColumnMode = settings.ColumnMode;
        customColumns = settings.CustomColumns;
        emulatorWindowSizeMode = settings.SizeMode;
        customWindowWidth = settings.CustomWidth;
        customWindowHeight = settings.CustomHeight;
        windowGap = settings.Gap;
        currentLayoutPage = Math.Max(0, settings.CurrentPage);
        customLayoutOrder.Clear();
        customLayoutOrder.AddRange(settings.CustomOrder.Distinct());
        originalWindowPlacements.Clear();
        originalWindowPlacements.AddRange(settings.OriginalPlacements.Select(ClonePlacement));
    }

    private IReadOnlyList<MemuInstance> OrderInstancesForLayout(IReadOnlyList<MemuInstance> instances)
    {
        return layoutSortMode switch
        {
            EmulatorSortMode.Name => instances.OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(item => item.Index).ToList(),
            EmulatorSortMode.Custom => instances.OrderBy(item => CustomOrderPosition(item.Index)).ThenBy(item => item.Index).ToList(),
            _ => instances.OrderBy(item => item.Index).ToList()
        };
    }

    private int CustomOrderPosition(int index)
    {
        var position = customLayoutOrder.IndexOf(index);
        return position < 0 ? int.MaxValue : position;
    }

    private void ApplyTargetSort()
    {
        if (RunTargets.Count == 0) return;
        var ordered = LayoutSortMode switch
        {
            EmulatorSortMode.Name => RunTargets.OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(item => item.Index).ToList(),
            EmulatorSortMode.Custom => RunTargets.OrderBy(item => CustomOrderPosition(item.Index)).ThenBy(item => item.Index).ToList(),
            _ => RunTargets.OrderBy(item => item.Index).ToList()
        };
        ReorderRunTargets(ordered);
    }

    private void ReorderRunTargets(IReadOnlyList<InstanceTargetItemViewModel> ordered)
    {
        for (var targetIndex = 0; targetIndex < ordered.Count; targetIndex++)
        {
            var currentIndex = RunTargets.IndexOf(ordered[targetIndex]);
            if (currentIndex != targetIndex) RunTargets.Move(currentIndex, targetIndex);
        }
        UpdateLayoutPositions();
        UpdateRunConfigurationState();
    }

    private void UpdateLayoutPositions()
    {
        for (var index = 0; index < RunTargets.Count; index++) RunTargets[index].LayoutPosition = index + 1;
        OnPropertyChanged(nameof(SelectedLayoutTargetCount));
        RaiseWorkspaceCommandStates();
    }

    private void OnLayoutTargetSelectionChanged(object? sender, EventArgs args)
    {
        OnPropertyChanged(nameof(SelectedLayoutTargetCount));
        RaiseWorkspaceCommandStates();
    }

    private async void OnTargetAssignmentChanged(object? sender, EventArgs args)
    {
        if (sender is not InstanceTargetItemViewModel target) return;
        var script = Scripts.FirstOrDefault(item => item.Id == target.AssignedScriptId);
        target.SetAssignedScript(script?.Id, script?.Name);
        UpdateRunConfigurationState();
        try { await PersistAssignmentsAsync(); }
        catch (Exception exception) { ReportUnexpectedError(exception); }
    }

    private async Task AssignScriptToSelectedAsync()
    {
        if (BulkAssignmentScript is null) return;
        foreach (var target in RunTargets.Where(item => item.IsLayoutSelected))
            target.SetAssignedScript(BulkAssignmentScript.Id, BulkAssignmentScript.Name);
        await PersistAssignmentsAsync();
        UpdateRunConfigurationState();
        StatusMessage = $"Đã gán '{BulkAssignmentScript.Name}' cho {SelectedLayoutTargetCount} giả lập.";
    }

    private async Task AssignCurrentScriptToAllAsync()
    {
        if (SelectedScript is null) return;
        foreach (var target in RunTargets) target.SetAssignedScript(SelectedScript.Id, SelectedScript.Name);
        await PersistAssignmentsAsync();
        UpdateRunConfigurationState();
        StatusMessage = $"Đã gán kịch bản hiện tại '{SelectedScript.Name}' cho tất cả giả lập.";
    }

    private async Task PersistAssignmentsAsync()
    {
        var assignments = RunTargets
            .Where(item => item.AssignedScriptId is not null)
            .ToDictionary(item => item.Index, item => item.AssignedScriptId!.Value);
        await UpdateApplicationSettingsAsync(settings =>
        {
            settings.MultiInstanceRun.ScriptAssignmentMode = ScriptAssignmentMode;
            settings.MultiInstanceRun.ScriptAssignments.Clear();
            foreach (var pair in assignments) settings.MultiInstanceRun.ScriptAssignments[pair.Key] = pair.Value;
        }, CancellationToken.None);
    }

    private async void PersistAssignmentMode()
    {
        try { await PersistAssignmentsAsync(); }
        catch (Exception exception) { ReportUnexpectedError(exception); }
    }

    private void ClearAssignmentsForScript(Guid scriptId)
    {
        foreach (var target in RunTargets.Where(item => item.AssignedScriptId == scriptId))
            target.SetAssignedScript(null, null);
        UpdateRunConfigurationState();
    }

    private void RefreshAssignedScriptLabels()
    {
        foreach (var target in RunTargets)
        {
            var script = Scripts.FirstOrDefault(item => item.Id == target.AssignedScriptId);
            target.SetAssignedScript(script?.Id, script?.Name);
        }
        UpdateRunConfigurationState();
    }

    private string? ValidateScriptAssignments()
    {
        if (ScriptAssignmentMode == ScriptAssignmentModeValue.OneScriptForAll)
            return SelectedScript is null ? "Hãy chọn một kịch bản để chạy." : null;
        var requested = ResolveRequestedTargets();
        var missing = requested.Count(target =>
        {
            var item = RunTargets.FirstOrDefault(candidate => candidate.Index == target.Index);
            return item?.AssignedScriptId is not Guid id || Scripts.All(script => script.Id != id);
        });
        return missing == 0 ? null : $"Còn {missing} giả lập chưa được gán kịch bản hợp lệ.";
    }

    private Dictionary<int, ScriptDefinition>? ResolveAssignedScripts(IReadOnlyList<MemuInstance> targets)
    {
        var resolved = new Dictionary<int, ScriptDefinition>();
        foreach (var target in targets)
        {
            var script = ScriptAssignmentMode == ScriptAssignmentModeValue.OneScriptForAll
                ? SelectedScript
                : Scripts.FirstOrDefault(item => item.Id == RunTargets.FirstOrDefault(row => row.Index == target.Index)?.AssignedScriptId);
            if (script is null) return null;
            resolved[target.Index] = script.Model;
        }
        return resolved;
    }

    private bool AssignedScriptsHaveSteps()
    {
        var targets = ResolveRequestedTargets();
        var assignedScripts = ResolveAssignedScripts(targets);
        return assignedScripts is not null &&
            assignedScripts.Count > 0 &&
            assignedScripts.Values.All(script => script.Steps.Count > 0);
    }

    private bool CanMoveLayoutTargets(int offset)
    {
        if (!CanChangeWindowLayout) return false;
        var selected = RunTargets.Where(item => item.IsLayoutSelected).ToList();
        if (selected.Count == 0) return false;
        var selectedSet = selected.ToHashSet();
        var blockIndex = RunTargets.TakeWhile(item => !ReferenceEquals(item, selected[0])).Count(item => !selectedSet.Contains(item));
        var target = blockIndex + offset;
        return target >= 0 && target <= RunTargets.Count - selected.Count;
    }

    private async Task MoveSelectedLayoutTargetsAsync(int offset)
    {
        var selected = RunTargets.Where(item => item.IsLayoutSelected).ToList();
        if (selected.Count == 0) return;
        var selectedSet = selected.ToHashSet();
        var remaining = RunTargets.Where(item => !selectedSet.Contains(item)).ToList();
        var blockIndex = RunTargets.TakeWhile(item => !ReferenceEquals(item, selected[0])).Count(item => !selectedSet.Contains(item));
        await MoveLayoutGroupAsync(selected, remaining, blockIndex + offset);
    }

    private Task MoveSelectedLayoutTargetsToPositionAsync()
    {
        var selected = RunTargets.Where(item => item.IsLayoutSelected).ToList();
        var selectedSet = selected.ToHashSet();
        var remaining = RunTargets.Where(item => !selectedSet.Contains(item)).ToList();
        return MoveLayoutGroupAsync(selected, remaining, Math.Clamp(LayoutMovePosition - 1, 0, remaining.Count));
    }

    public bool CanMoveLayoutTarget(InstanceTargetItemViewModel item) =>
        CanChangeWindowLayout && item.IsLayoutSelected;

    public async Task MoveLayoutTargetToAsync(InstanceTargetItemViewModel item, int insertionIndex)
    {
        if (!CanMoveLayoutTarget(item)) return;
        var group = RunTargets.Where(target => target.IsLayoutSelected).ToList();
        var groupSet = group.ToHashSet();
        var original = RunTargets.ToList();
        var normalized = Math.Clamp(insertionIndex, 0, original.Count);
        var adjusted = normalized - original.Take(normalized).Count(groupSet.Contains);
        await MoveLayoutGroupAsync(group, original.Where(target => !groupSet.Contains(target)).ToList(), adjusted);
    }

    private async Task MoveLayoutGroupAsync(
        IReadOnlyList<InstanceTargetItemViewModel> group,
        IReadOnlyList<InstanceTargetItemViewModel> remaining,
        int insertionIndex)
    {
        if (group.Count == 0) return;
        var ordered = remaining.ToList();
        ordered.InsertRange(Math.Clamp(insertionIndex, 0, remaining.Count), group);
        layoutSortMode = EmulatorSortMode.Custom;
        OnPropertyChanged(nameof(LayoutSortMode));
        OnPropertyChanged(nameof(IsSortByIndex));
        OnPropertyChanged(nameof(IsSortByName));
        OnPropertyChanged(nameof(IsSortCustom));
        customLayoutOrder.Clear();
        customLayoutOrder.AddRange(ordered.Select(item => item.Index));
        ReorderRunTargets(ordered);
        await PersistWindowLayoutSettingsAsync();
        StatusMessage = $"Đã đổi thứ tự {group.Count} giả lập.";
    }

    private List<WindowLayoutTarget> BuildWindowTargets() => RunTargets
        .Where(item => item.IsRunning && item.Model.WindowHandle is > 0)
        .Select(item => new WindowLayoutTarget(
            item.Index,
            item.Name,
            item.Model.WindowHandle!.Value,
            new ScreenRectangle(0, 0, 1, 1),
            item.Model.ProcessId))
        .ToList();

    private async Task ArrangeGridAsync(int pageIndex)
    {
        if (windowLayoutService is null || SelectedDisplay is null) return;
        var targets = BuildWindowTargets();
        if (targets.Count == 0) { StatusMessage = "Không có cửa sổ MEmu đang chạy để xếp lưới."; return; }
        isArrangingWindows = true;
        OnPropertyChanged(nameof(CanChangeWindowLayout));
        RaiseWorkspaceCommandStates();
        try
        {
            var snapshot = CreateWindowLayoutSettingsSnapshot();
            snapshot.DisplayDeviceName = SelectedDisplay.DeviceName;
            var result = await windowLayoutService.ArrangeAsync(targets, snapshot, pageIndex, CancellationToken.None);
            CurrentLayoutPage = result.Plan.PageIndex;
            LayoutPageCount = result.Plan.PageCount;
            EffectiveItemsPerPage = result.Plan.ItemsPerPage;
            EffectiveRows = result.Plan.Rows;
            var capturedIndices = originalWindowPlacements.Select(item => item.InstanceIndex).ToHashSet();
            originalWindowPlacements.AddRange(result.CapturedOriginalPlacements
                .Where(item => capturedIndices.Add(item.InstanceIndex))
                .Select(ClonePlacement));
            await PersistWindowLayoutSettingsAsync();
            StatusMessage = result.Warning is null
                ? $"Đã xếp lưới trang {CurrentLayoutPageDisplay}/{LayoutPageCount}: {result.Plan.Placements.Count} cửa sổ, {result.Plan.Columns} cột × {result.Plan.Rows} hàng."
                : $"Đã xếp lưới với cảnh báo. {result.Warning}";
        }
        finally
        {
            isArrangingWindows = false;
            OnPropertyChanged(nameof(CanChangeWindowLayout));
            RaiseWorkspaceCommandStates();
        }
    }

    private string? ValidateLayoutConfiguration()
    {
        if (SelectedDisplay is null) return "Không có màn hình khả dụng.";
        if (LayoutItemsPerPageMode == LayoutItemsPerPageMode.Custom && CustomItemsPerPage <= 0)
            return "Số cửa sổ mỗi trang phải lớn hơn 0.";
        if (LayoutColumnMode == LayoutColumnMode.Custom && CustomColumns <= 0)
            return "Số cột phải lớn hơn 0.";
        if (EmulatorWindowSizeMode == EmulatorWindowSizeMode.Custom && (CustomWindowWidth <= 0 || CustomWindowHeight <= 0))
            return "Chiều rộng và chiều cao cửa sổ phải lớn hơn 0.";
        if (WindowGap < 0) return "Khoảng cách giữa cửa sổ không được âm.";
        return null;
    }

    private void UpdateLayoutConfigurationState()
    {
        OnPropertyChanged(nameof(LayoutConfigurationError));
        RaiseWorkspaceCommandStates();
    }

    private async Task FocusSelectedEmulatorAsync()
    {
        if (windowLayoutService is null || SelectedDisplay is null) return;
        var selected = RunTargets.SingleOrDefault(item => item.IsLayoutSelected && item.IsRunning && item.Model.WindowHandle is > 0);
        if (selected is null) { StatusMessage = "Hãy chọn đúng một giả lập đang chạy để tập trung."; return; }
        if (LayoutPageCount == 0) await ArrangeGridAsync(0);
        var targetIndex = BuildWindowTargets().FindIndex(item => item.InstanceIndex == selected.Index);
        if (targetIndex < 0) return;
        var targetPage = EffectiveItemsPerPage <= 0 ? 0 : targetIndex / EffectiveItemsPerPage;
        if (targetPage != CurrentLayoutPage) await ArrangeGridAsync(targetPage);
        var target = BuildWindowTargets().Single(item => item.InstanceIndex == selected.Index);
        SelectedInstance = Instances.FirstOrDefault(item => item.Index == selected.Index);
        var warning = await windowLayoutService.FocusAsync(target, SelectedDisplay, CancellationToken.None);
        StatusMessage = warning ?? $"Đang tập trung giả lập #{selected.Index} {selected.Name}. Dùng “Trở lại lưới” để về đúng trang và ô.";
    }

    private async Task RestoreOriginalLayoutAsync()
    {
        if (windowLayoutService is null || originalWindowPlacements.Count == 0) return;
        var warning = await windowLayoutService.RestoreOriginalAsync(
            BuildWindowTargets(),
            originalWindowPlacements,
            CancellationToken.None);
        StatusMessage = warning ?? "Đã khôi phục vị trí và kích thước cửa sổ trước lần xếp lưới đầu tiên.";
    }

    private EmulatorWindowLayoutSettings CreateWindowLayoutSettingsSnapshot()
    {
        var settings = new EmulatorWindowLayoutSettings
        {
            SortMode = LayoutSortMode,
            ItemsPerPageMode = LayoutItemsPerPageMode,
            CustomItemsPerPage = CustomItemsPerPage,
            ColumnMode = LayoutColumnMode,
            CustomColumns = CustomColumns,
            SizeMode = EmulatorWindowSizeMode,
            CustomWidth = CustomWindowWidth,
            CustomHeight = CustomWindowHeight,
            Gap = WindowGap,
            DisplayDeviceName = SelectedDisplay?.DeviceName,
            CurrentPage = CurrentLayoutPage
        };
        settings.CustomOrder.AddRange(RunTargets.Select(item => item.Index));
        settings.OriginalPlacements.AddRange(originalWindowPlacements.Select(ClonePlacement));
        return settings;
    }

    private async Task PersistWindowLayoutSettingsAsync()
    {
        var snapshot = CreateWindowLayoutSettingsSnapshot();
        await UpdateApplicationSettingsAsync(settings => CopyWindowLayoutSettings(snapshot, settings.WindowLayout), CancellationToken.None);
    }

    private static void CopyWindowLayoutSettings(EmulatorWindowLayoutSettings source, EmulatorWindowLayoutSettings target)
    {
        target.SortMode = source.SortMode;
        target.ItemsPerPageMode = source.ItemsPerPageMode;
        target.CustomItemsPerPage = source.CustomItemsPerPage;
        target.ColumnMode = source.ColumnMode;
        target.CustomColumns = source.CustomColumns;
        target.SizeMode = source.SizeMode;
        target.CustomWidth = source.CustomWidth;
        target.CustomHeight = source.CustomHeight;
        target.Gap = source.Gap;
        target.DisplayDeviceName = source.DisplayDeviceName;
        target.CurrentPage = source.CurrentPage;
        target.CustomOrder.Clear();
        target.CustomOrder.AddRange(source.CustomOrder);
        target.OriginalPlacements.Clear();
        target.OriginalPlacements.AddRange(source.OriginalPlacements.Select(ClonePlacement));
    }

    private static SavedWindowPlacement ClonePlacement(SavedWindowPlacement source) => new()
    {
        InstanceIndex = source.InstanceIndex,
        Left = source.Left,
        Top = source.Top,
        Width = source.Width,
        Height = source.Height
    };

    private void RaiseWorkspaceCommandStates()
    {
        AssignScriptToSelectedCommand?.RaiseCanExecuteChanged();
        AssignCurrentScriptToAllCommand?.RaiseCanExecuteChanged();
        MoveLayoutUpCommand?.RaiseCanExecuteChanged();
        MoveLayoutDownCommand?.RaiseCanExecuteChanged();
        MoveLayoutToPositionCommand?.RaiseCanExecuteChanged();
        ArrangeGridCommand?.RaiseCanExecuteChanged();
        PreviousLayoutPageCommand?.RaiseCanExecuteChanged();
        NextLayoutPageCommand?.RaiseCanExecuteChanged();
        FocusEmulatorCommand?.RaiseCanExecuteChanged();
        ReturnToGridCommand?.RaiseCanExecuteChanged();
        RestoreOriginalLayoutCommand?.RaiseCanExecuteChanged();
    }
}
