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
    private bool preserveWindowAspectRatio = true;
    private int windowGap = 8;
    private DisplayWorkArea? selectedDisplay;
    private int currentLayoutPage;
    private int layoutPageCount;
    private int effectiveItemsPerPage;
    private int effectiveRows;
    private int layoutMovePosition = 1;
    private bool enableGeometryDiagnostics;
    private string geometryDiagnosticSummary = string.Empty;
    private bool isArrangingWindows;
    private int? focusedInstanceIndex;
    private bool isCurrentPageOrderMode = true;
    private string layoutSearchText = string.Empty;
    private LayoutPageItemViewModel? selectedLayoutPage;
    private LayoutPageItemViewModel? destinationLayoutPage;
    private LayoutPageFilterOption? selectedLayoutPageFilter;
    private readonly List<int> customLayoutOrder = [];
    private readonly List<SavedWindowPlacement> originalWindowPlacements = [];

    public ObservableCollection<DisplayWorkArea> Displays { get; } = [];
    public ObservableCollection<InstanceTargetItemViewModel> VisibleLayoutTargets { get; } = [];
    public ObservableCollection<LayoutPageItemViewModel> LayoutPages { get; } = [];
    public ObservableCollection<LayoutPageFilterOption> LayoutPageFilters { get; } = [];

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
    public AsyncCommand MoveLayoutToPageCommand { get; private set; } = null!;
    public AsyncCommand MoveLayoutToPageStartCommand { get; private set; } = null!;
    public AsyncCommand MoveLayoutToPageEndCommand { get; private set; } = null!;
    public AsyncCommand SortCurrentPageByNameCommand { get; private set; } = null!;
    public AsyncCommand SortCurrentPageByIndexCommand { get; private set; } = null!;

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
    public bool CanChangeWindowLayout => CanUseApplication && windowLayoutService is not null && !IsBusy && !IsCapturing && !isArrangingWindows;

    public EmulatorSortMode LayoutSortMode
    {
        get => layoutSortMode;
        set
        {
            if (RunTargets.Count > 0 && !CanEditWindowGrid) return;
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
    public bool PreserveWindowAspectRatio { get => preserveWindowAspectRatio; set { if (SetProperty(ref preserveWindowAspectRatio, value)) UpdateLayoutConfigurationState(); } }
    public int WindowGap { get => windowGap; set { if (SetProperty(ref windowGap, value)) UpdateLayoutConfigurationState(); } }

    public DisplayWorkArea? SelectedDisplay { get => selectedDisplay; set { if (SetProperty(ref selectedDisplay, value)) UpdateLayoutConfigurationState(); } }
    public int CurrentLayoutPage
    {
        get => currentLayoutPage;
        private set
        {
            if (!SetProperty(ref currentLayoutPage, value)) return;
            selectedLayoutPage = LayoutPages.FirstOrDefault(item => item.PageIndex == value);
            OnPropertyChanged(nameof(SelectedLayoutPage));
            OnPropertyChanged(nameof(CurrentLayoutPageDisplay));
            RefreshVisibleLayoutTargets();
            RaiseWorkspaceCommandStates();
        }
    }
    public int CurrentLayoutPageDisplay => LayoutPageCount == 0 ? 0 : CurrentLayoutPage + 1;
    public int LayoutPageCount { get => layoutPageCount; private set { if (SetProperty(ref layoutPageCount, value)) { OnPropertyChanged(nameof(CurrentLayoutPageDisplay)); RaiseWorkspaceCommandStates(); } } }
    public int EffectiveItemsPerPage { get => effectiveItemsPerPage; private set => SetProperty(ref effectiveItemsPerPage, value); }
    public int EffectiveRows { get => effectiveRows; private set => SetProperty(ref effectiveRows, value); }
    public int LayoutMovePosition { get => layoutMovePosition; set => SetProperty(ref layoutMovePosition, value); }
    public string? LayoutConfigurationError => ValidateLayoutConfiguration();
    public bool EnableGeometryDiagnostics { get => enableGeometryDiagnostics; set => SetProperty(ref enableGeometryDiagnostics, value); }
    public string GeometryDiagnosticSummary { get => geometryDiagnosticSummary; private set => SetProperty(ref geometryDiagnosticSummary, value); }
    public bool IsCurrentPageOrderMode
    {
        get => isCurrentPageOrderMode;
        set
        {
            if (!SetProperty(ref isCurrentPageOrderMode, value)) return;
            OnPropertyChanged(nameof(IsAllInstancesOrderMode));
            RefreshLayoutManagementView();
        }
    }
    public bool IsAllInstancesOrderMode
    {
        get => !IsCurrentPageOrderMode;
        set { if (value) IsCurrentPageOrderMode = false; }
    }
    public string LayoutSearchText
    {
        get => layoutSearchText;
        set { if (SetProperty(ref layoutSearchText, value)) RefreshLayoutManagementView(); }
    }
    public LayoutPageItemViewModel? SelectedLayoutPage
    {
        get => selectedLayoutPage;
        set
        {
            if (!SetProperty(ref selectedLayoutPage, value) || value is null) return;
            CurrentLayoutPage = value.PageIndex;
            RefreshLayoutManagementView();
        }
    }
    public LayoutPageItemViewModel? DestinationLayoutPage
    {
        get => destinationLayoutPage;
        set { if (SetProperty(ref destinationLayoutPage, value)) RaiseWorkspaceCommandStates(); }
    }
    public LayoutPageFilterOption? SelectedLayoutPageFilter
    {
        get => selectedLayoutPageFilter;
        set { if (SetProperty(ref selectedLayoutPageFilter, value)) RefreshVisibleLayoutTargets(); }
    }

    private void InitializeWorkspaceCommands()
    {
        AssignScriptToSelectedCommand = new AsyncCommand(AssignScriptToSelectedAsync,
            () => IsPerInstanceScript && BulkAssignmentScript is not null && RunTargets.Any(item => item.IsSelected || item.IsLayoutSelected) && CanChangeSelection,
            ReportUnexpectedError);
        AssignCurrentScriptToAllCommand = new AsyncCommand(AssignCurrentScriptToAllAsync,
            () => IsPerInstanceScript && SelectedScript is not null && RunTargets.Count > 0 && CanChangeSelection,
            ReportUnexpectedError);
        MoveLayoutUpCommand = new AsyncCommand(() => MoveSelectedLayoutTargetsAsync(-1), () => CanMoveLayoutTargets(-1), ReportUnexpectedError);
        MoveLayoutDownCommand = new AsyncCommand(() => MoveSelectedLayoutTargetsAsync(1), () => CanMoveLayoutTargets(1), ReportUnexpectedError);
        MoveLayoutToPositionCommand = new AsyncCommand(MoveSelectedLayoutTargetsToPositionAsync,
            () => SelectedTargetsOnActiveLayoutPage().Count > 0 && CanEditWindowGrid && LayoutMovePosition > 0,
            ReportUnexpectedError);
        ArrangeGridCommand = new AsyncCommand(() => ArrangeGridAsync(CurrentLayoutPage),
            () => CanEditWindowGrid && BuildWindowTargets().Count > 0 && ValidateLayoutConfiguration() is null,
            ReportUnexpectedError);
        PreviousLayoutPageCommand = new AsyncCommand(() => ArrangeGridAsync(CurrentLayoutPage - 1),
            () => CanEditWindowGrid && CurrentLayoutPage > 0,
            ReportUnexpectedError);
        NextLayoutPageCommand = new AsyncCommand(() => ArrangeGridAsync(CurrentLayoutPage + 1),
            () => CanEditWindowGrid && CurrentLayoutPage + 1 < LayoutPageCount,
            ReportUnexpectedError);
        FocusEmulatorCommand = new AsyncCommand(FocusSelectedEmulatorAsync,
            () => CanEditWindowGrid && SelectedLayoutTargetCount == 1,
            ReportUnexpectedError);
        ReturnToGridCommand = new AsyncCommand(ReturnToGridAsync,
            () => CanChangeWindowLayout && (focusedInstanceIndex is not null || LayoutPageCount > 0),
            ReportUnexpectedError);
        RestoreOriginalLayoutCommand = new AsyncCommand(RestoreOriginalLayoutAsync,
            () => CanEditWindowGrid && originalWindowPlacements.Count > 0,
            ReportUnexpectedError);
        MoveLayoutToPageCommand = new AsyncCommand(MoveSelectedLayoutTargetsToPageAsync,
            () => CanEditWindowGrid && SelectedLayoutTargetCount > 0 && DestinationLayoutPage is not null,
            ReportUnexpectedError);
        MoveLayoutToPageStartCommand = new AsyncCommand(() => MoveSelectedWithinCurrentPageAsync(toEnd: false),
            () => CanEditWindowGrid && SelectedTargetsOnActiveLayoutPage().Count > 0,
            ReportUnexpectedError);
        MoveLayoutToPageEndCommand = new AsyncCommand(() => MoveSelectedWithinCurrentPageAsync(toEnd: true),
            () => CanEditWindowGrid && SelectedTargetsOnActiveLayoutPage().Count > 0,
            ReportUnexpectedError);
        SortCurrentPageByNameCommand = new AsyncCommand(() => SortCurrentPageAsync(byName: true),
            () => CanEditWindowGrid && VisibleLayoutTargets.Count > 1,
            ReportUnexpectedError);
        SortCurrentPageByIndexCommand = new AsyncCommand(() => SortCurrentPageAsync(byName: false),
            () => CanEditWindowGrid && VisibleLayoutTargets.Count > 1,
            ReportUnexpectedError);
    }

    private bool CanEditWindowGrid => CanChangeWindowLayout && focusedInstanceIndex is null;

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
        catch (Exception exception)
        {
            LogInitializationIssue(exception);
            StatusMessage = $"{StatusMessage} Không thể đọc danh sách màn hình ({exception.Message}).";
        }
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
        preserveWindowAspectRatio = settings.PreserveAspectRatio;
        windowGap = settings.Gap;
        currentLayoutPage = Math.Max(0, settings.CurrentPage);
        enableGeometryDiagnostics = settings.EnableGeometryDiagnostics;
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
        var itemsPerPage = GetManagementItemsPerPage();
        var layoutIndex = 0;
        for (var index = 0; index < RunTargets.Count; index++)
        {
            RunTargets[index].LayoutPosition = index + 1;
            if (IsLayoutEligible(RunTargets[index]))
            {
                RunTargets[index].LayoutPageNumber = layoutIndex / itemsPerPage + 1;
                RunTargets[index].PositionInLayoutPage = layoutIndex % itemsPerPage + 1;
                layoutIndex++;
            }
            else
            {
                RunTargets[index].LayoutPageNumber = 0;
                RunTargets[index].PositionInLayoutPage = 0;
            }
        }
        RefreshLayoutManagementView();
        OnPropertyChanged(nameof(SelectedLayoutTargetCount));
        RaiseWorkspaceCommandStates();
    }

    private int GetManagementItemsPerPage()
    {
        if (LayoutItemsPerPageMode == LayoutItemsPerPageMode.All)
            return Math.Max(1, RunTargets.Count(IsLayoutEligible));
        if (LayoutItemsPerPageMode == LayoutItemsPerPageMode.Custom)
            return Math.Max(1, CustomItemsPerPage);
        return EffectiveItemsPerPage > 0 ? EffectiveItemsPerPage : 12;
    }

    private void RefreshLayoutManagementView()
    {
        var pageSize = GetManagementItemsPerPage();
        var eligibleCount = RunTargets.Count(IsLayoutEligible);
        var pageCount = eligibleCount == 0 ? 0 : (eligibleCount + pageSize - 1) / pageSize;
        if (pageCount == 0) currentLayoutPage = 0;
        else currentLayoutPage = Math.Clamp(currentLayoutPage, 0, pageCount - 1);
        layoutPageCount = pageCount;
        OnPropertyChanged(nameof(CurrentLayoutPage));
        OnPropertyChanged(nameof(CurrentLayoutPageDisplay));
        OnPropertyChanged(nameof(LayoutPageCount));

        var selectedPageIndex = selectedLayoutPage?.PageIndex ?? currentLayoutPage;
        var destinationPageIndex = destinationLayoutPage?.PageIndex ?? currentLayoutPage;
        var filterPageIndex = selectedLayoutPageFilter?.PageIndex;
        LayoutPages.Clear();
        for (var page = 0; page < pageCount; page++)
            LayoutPages.Add(new LayoutPageItemViewModel(page, Math.Min(pageSize, eligibleCount - page * pageSize)));
        selectedLayoutPage = LayoutPages.FirstOrDefault(item => item.PageIndex == selectedPageIndex)
            ?? LayoutPages.FirstOrDefault(item => item.PageIndex == currentLayoutPage);
        destinationLayoutPage = LayoutPages.FirstOrDefault(item => item.PageIndex == destinationPageIndex)
            ?? selectedLayoutPage;
        OnPropertyChanged(nameof(SelectedLayoutPage));
        OnPropertyChanged(nameof(DestinationLayoutPage));

        LayoutPageFilters.Clear();
        LayoutPageFilters.Add(new LayoutPageFilterOption(null, "Tất cả trang"));
        foreach (var page in LayoutPages)
            LayoutPageFilters.Add(new LayoutPageFilterOption(page.PageIndex, page.DisplayName));
        selectedLayoutPageFilter = LayoutPageFilters.FirstOrDefault(item => item.PageIndex == filterPageIndex)
            ?? LayoutPageFilters[0];
        OnPropertyChanged(nameof(SelectedLayoutPageFilter));
        RefreshVisibleLayoutTargets();
    }

    private void RefreshVisibleLayoutTargets()
    {
        IEnumerable<InstanceTargetItemViewModel> visible = RunTargets;
        if (IsCurrentPageOrderMode)
        {
            visible = visible.Where(item => item.LayoutPageNumber == CurrentLayoutPage + 1);
        }
        else
        {
            if (SelectedLayoutPageFilter?.PageIndex is int pageIndex)
                visible = visible.Where(item => item.LayoutPageNumber == pageIndex + 1);
            if (!string.IsNullOrWhiteSpace(LayoutSearchText))
            {
                var query = LayoutSearchText.Trim();
                visible = visible.Where(item => item.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                                                item.Index.ToString().Contains(query, StringComparison.Ordinal));
            }
        }
        VisibleLayoutTargets.Clear();
        foreach (var item in visible) VisibleLayoutTargets.Add(item);
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
        var selected = RunTargets.Where(item => item.IsSelected || item.IsLayoutSelected).ToList();
        foreach (var target in selected)
            target.SetAssignedScript(BulkAssignmentScript.Id, BulkAssignmentScript.Name);
        await PersistAssignmentsAsync();
        foreach (var target in selected) { target.IsSelected = false; target.IsLayoutSelected = false; }
        UpdateRunConfigurationState();
        StatusMessage = $"Đã gán '{BulkAssignmentScript.Name}' cho {selected.Count} giả lập.";
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
            settings.MultiInstanceRun.CommonScriptId = CommonRunScript?.Id;
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
            return CommonRunScript is null || Scripts.All(script => script.Id != CommonRunScript.Id)
                ? "Hãy chọn một kịch bản dùng chung hợp lệ."
                : CommonRunScript.Model.Steps.Count == 0
                    ? "Kịch bản dùng chung chưa có bước nào."
                    : null;
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
                ? CommonRunScript
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
        if (!CanEditWindowGrid) return false;
        var activePage = ActiveLayoutManagementPageIndex();
        var selected = SelectedTargetsOnActiveLayoutPage();
        if (selected.Count == 0) return false;
        var selectedSet = selected.ToHashSet();
        var blockIndex = RunTargets.TakeWhile(item => !ReferenceEquals(item, selected[0])).Count(item => !selectedSet.Contains(item));
        var target = blockIndex + offset;
        var pageStart = activePage * GetManagementItemsPerPage();
        var pageEnd = Math.Min(pageStart + GetManagementItemsPerPage(), RunTargets.Count) - selected.Count;
        return target >= pageStart && target <= pageEnd;
    }

    private async Task MoveSelectedLayoutTargetsAsync(int offset)
    {
        var selected = SelectedTargetsOnActiveLayoutPage();
        if (selected.Count == 0) return;
        var selectedSet = selected.ToHashSet();
        var remaining = RunTargets.Where(item => !selectedSet.Contains(item)).ToList();
        var blockIndex = RunTargets.TakeWhile(item => !ReferenceEquals(item, selected[0])).Count(item => !selectedSet.Contains(item));
        await MoveLayoutGroupAsync(selected, remaining, blockIndex + offset);
    }

    private Task MoveSelectedLayoutTargetsToPositionAsync()
    {
        var activePage = ActiveLayoutManagementPageIndex();
        var selected = SelectedTargetsOnActiveLayoutPage();
        var selectedSet = selected.ToHashSet();
        var remaining = RunTargets.Where(item => !selectedSet.Contains(item)).ToList();
        var positionInPage = Math.Clamp(LayoutMovePosition - 1, 0, Math.Max(0, GetManagementItemsPerPage() - selected.Count));
        return MoveLayoutGroupAsync(selected, remaining, FindPageInsertionIndex(remaining, activePage, positionInPage));
    }

    public bool CanMoveLayoutTarget(InstanceTargetItemViewModel item) =>
        CanEditWindowGrid && RunTargets.Contains(item);

    public async Task MoveLayoutTargetToAsync(InstanceTargetItemViewModel item, int insertionIndex)
    {
        if (!CanMoveLayoutTarget(item)) return;
        var group = item.IsLayoutSelected
            ? RunTargets.Where(target => target.IsLayoutSelected).ToList()
            : [item];
        var groupSet = group.ToHashSet();
        var original = RunTargets.ToList();
        var visibleInsertion = Math.Clamp(insertionIndex, 0, VisibleLayoutTargets.Count);
        var normalized = visibleInsertion < VisibleLayoutTargets.Count
            ? original.IndexOf(VisibleLayoutTargets[visibleInsertion])
            : VisibleLayoutTargets.Count > 0
                ? original.IndexOf(VisibleLayoutTargets[^1]) + 1
                : original.Count;
        var adjusted = normalized - original.Take(normalized).Count(groupSet.Contains);
        await MoveLayoutGroupAsync(group, original.Where(target => !groupSet.Contains(target)).ToList(), adjusted);
    }

    public async Task MoveLayoutTargetToPageAsync(InstanceTargetItemViewModel item, int pageIndex)
    {
        if (!CanMoveLayoutTarget(item)) return;
        var group = item.IsLayoutSelected
            ? RunTargets.Where(target => target.IsLayoutSelected).ToList()
            : [item];
        await MoveLayoutGroupToPageAsync(group, pageIndex);
    }

    private Task MoveSelectedLayoutTargetsToPageAsync()
    {
        var selected = RunTargets.Where(item => item.IsLayoutSelected).ToList();
        return DestinationLayoutPage is null
            ? Task.CompletedTask
            : MoveLayoutGroupToPageAsync(selected, DestinationLayoutPage.PageIndex);
    }

    private async Task MoveLayoutGroupToPageAsync(IReadOnlyList<InstanceTargetItemViewModel> group, int pageIndex)
    {
        if (group.Count == 0) return;
        var groupSet = group.ToHashSet();
        var remaining = RunTargets.Where(item => !groupSet.Contains(item)).ToList();
        var pageSize = GetManagementItemsPerPage();
        var destination = Math.Clamp((pageIndex + 1) * pageSize - Math.Min(group.Count, pageSize), 0, remaining.Count);
        await MoveLayoutGroupAsync(group, remaining, destination);
        CurrentLayoutPage = Math.Clamp(pageIndex, 0, Math.Max(0, LayoutPageCount - 1));
        StatusMessage = $"Đã chuyển {group.Count} giả lập sang trang {CurrentLayoutPageDisplay}, giữ nguyên thứ tự tương đối.";
    }

    private async Task MoveSelectedWithinCurrentPageAsync(bool toEnd)
    {
        var activePage = ActiveLayoutManagementPageIndex();
        var group = SelectedTargetsOnActiveLayoutPage();
        if (group.Count == 0) return;
        var groupSet = group.ToHashSet();
        var remaining = RunTargets.Where(item => !groupSet.Contains(item)).ToList();
        var pageItems = remaining.Where(item => item.LayoutPageNumber == activePage + 1).ToList();
        var destination = toEnd
            ? pageItems.Count == 0 ? remaining.Count : remaining.IndexOf(pageItems[^1]) + 1
            : pageItems.Count == 0 ? remaining.Count : remaining.IndexOf(pageItems[0]);
        await MoveLayoutGroupAsync(group, remaining, destination);
    }

    private async Task SortCurrentPageAsync(bool byName)
    {
        var activePage = ActiveLayoutManagementPageIndex();
        var page = RunTargets.Where(item => item.LayoutPageNumber == activePage + 1).ToList();
        if (page.Count < 2) return;
        var sorted = byName
            ? page.OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(item => item.Index).ToList()
            : page.OrderBy(item => item.Index).ToList();
        var ordered = RunTargets.ToList();
        var sortedIndex = 0;
        for (var index = 0; index < ordered.Count; index++)
            if (ordered[index].LayoutPageNumber == activePage + 1)
                ordered[index] = sorted[sortedIndex++];
        await MoveLayoutGroupAsync(ordered, [], 0);
        StatusMessage = $"Đã sắp xếp trang {activePage + 1} theo {(byName ? "tên" : "index")}.";
    }

    private int ActiveLayoutManagementPageIndex() =>
        !IsCurrentPageOrderMode && SelectedLayoutPageFilter?.PageIndex is int filteredPage
            ? filteredPage
            : CurrentLayoutPage;

    private List<InstanceTargetItemViewModel> SelectedTargetsOnActiveLayoutPage()
    {
        var pageNumber = ActiveLayoutManagementPageIndex() + 1;
        return RunTargets.Where(item => item.IsLayoutSelected && item.LayoutPageNumber == pageNumber).ToList();
    }

    private static bool IsLayoutEligible(InstanceTargetItemViewModel item) =>
        item.IsRunning && item.Model.WindowHandle is > 0;

    private static int FindPageInsertionIndex(
        IReadOnlyList<InstanceTargetItemViewModel> remaining,
        int pageIndex,
        int positionInPage)
    {
        var pageItems = remaining.Where(item => item.LayoutPageNumber == pageIndex + 1).ToList();
        if (pageItems.Count == 0) return remaining.Count;
        if (positionInPage >= pageItems.Count) return IndexOfTarget(remaining, pageItems[^1]) + 1;
        return IndexOfTarget(remaining, pageItems[Math.Max(0, positionInPage)]);
    }

    private static int IndexOfTarget(
        IReadOnlyList<InstanceTargetItemViewModel> items,
        InstanceTargetItemViewModel target)
    {
        for (var index = 0; index < items.Count; index++)
            if (ReferenceEquals(items[index], target)) return index;
        return items.Count;
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
        foreach (var target in group) target.IsLayoutSelected = false;
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
            if (result.Applied)
            {
                CurrentLayoutPage = result.Plan.PageIndex;
                LayoutPageCount = result.Plan.PageCount;
                EffectiveItemsPerPage = result.Plan.ItemsPerPage;
                EffectiveRows = result.Plan.Rows;
                UpdateLayoutPositions();
            }
            GeometryDiagnosticSummary = snapshot.EnableGeometryDiagnostics
                ? string.Join(" | ", result.GeometryDiagnostics)
                : string.Empty;
            var capturedIndices = originalWindowPlacements.Select(item => item.InstanceIndex).ToHashSet();
            originalWindowPlacements.AddRange(result.CapturedOriginalPlacements
                .Where(item => capturedIndices.Add(item.InstanceIndex))
                .Select(ClonePlacement));
            await PersistWindowLayoutSettingsAsync();
            StatusMessage = !result.Applied
                ? $"Không thể xếp lưới theo cấu hình hiện tại. {result.Warning}"
                : result.Warning is null
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
        UpdateLayoutPositions();
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
        var pageSize = Math.Max(1, EffectiveItemsPerPage > 0 ? EffectiveItemsPerPage : GetManagementItemsPerPage());
        var pageTargets = BuildWindowTargets().Skip(CurrentLayoutPage * pageSize).Take(pageSize).ToList();
        var warning = await windowLayoutService.FocusAsync(
            target,
            pageTargets,
            SelectedDisplay,
            EnableGeometryDiagnostics,
            CancellationToken.None);
        if (warning is not null)
        {
            StatusMessage = warning;
            return;
        }
        focusedInstanceIndex = selected.Index;
        RaiseWorkspaceCommandStates();
        StatusMessage = $"Đang tập trung giả lập #{selected.Index} {selected.Name}. Dùng “Trở lại lưới” để về đúng trang và ô.";
    }

    private async Task ReturnToGridAsync()
    {
        if (windowLayoutService is not null && focusedInstanceIndex is int index)
        {
            var target = BuildWindowTargets().FirstOrDefault(item => item.InstanceIndex == index);
            if (target is not null)
            {
                var restored = await windowLayoutService.ReturnFromFocusAsync(target, CancellationToken.None);
                focusedInstanceIndex = null;
                RaiseWorkspaceCommandStates();
                if (restored.Restored)
                {
                    StatusMessage = restored.Warning ?? $"Đã trở lại đúng trang {CurrentLayoutPageDisplay} và ô trước khi tập trung.";
                    return;
                }
            }
            else
            {
                focusedInstanceIndex = null;
                RaiseWorkspaceCommandStates();
            }
        }
        await ArrangeGridAsync(CurrentLayoutPage);
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
            PreserveAspectRatio = PreserveWindowAspectRatio,
            Gap = WindowGap,
            DisplayDeviceName = SelectedDisplay?.DeviceName,
            CurrentPage = CurrentLayoutPage,
            EnableGeometryDiagnostics = EnableGeometryDiagnostics
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
        target.PreserveAspectRatio = source.PreserveAspectRatio;
        target.Gap = source.Gap;
        target.DisplayDeviceName = source.DisplayDeviceName;
        target.CurrentPage = source.CurrentPage;
        target.EnableGeometryDiagnostics = source.EnableGeometryDiagnostics;
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
        Height = source.Height,
        ClientBounds = source.ClientBounds,
        RenderViewportBounds = source.RenderViewportBounds,
        RenderWindowHandle = source.RenderWindowHandle
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
        MoveLayoutToPageCommand?.RaiseCanExecuteChanged();
        MoveLayoutToPageStartCommand?.RaiseCanExecuteChanged();
        MoveLayoutToPageEndCommand?.RaiseCanExecuteChanged();
        SortCurrentPageByNameCommand?.RaiseCanExecuteChanged();
        SortCurrentPageByIndexCommand?.RaiseCanExecuteChanged();
    }
}
