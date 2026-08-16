using System.Collections.ObjectModel;
using MEmuScriptStudio.Core.Models;
using MEmuScriptStudio.Core.Scripts;
using ScriptAssignmentModeValue = MEmuScriptStudio.Core.Models.ScriptAssignmentMode;

namespace MEmuScriptStudio.App.ViewModels;

public enum RunTargetAvailabilityFilter
{
    All,
    Running,
    Stopped
}

public enum RunTargetSortMode
{
    Index,
    Name
}

public sealed record RunTargetAvailabilityFilterOption(RunTargetAvailabilityFilter Value, string Label);
public sealed record RunTargetSortOption(RunTargetSortMode Value, string Label);

public sealed partial class MainViewModel
{
    private ScriptAssignmentModeValue scriptAssignmentMode = ScriptAssignmentModeValue.OneScriptForAll;
    private ScriptItemViewModel? controlCenterSelectedScript;
    private string runTargetSearchText = string.Empty;
    private RunTargetAvailabilityFilter runTargetAvailabilityFilter;
    private RunTargetSortMode runTargetSortMode;
    private bool isBatchUpdatingRunTargetSelection;

    public AsyncCommand AssignScriptToSelectedCommand { get; private set; } = null!;
    public AsyncCommand AssignCurrentScriptToAllCommand { get; private set; } = null!;
    public RelayCommand SelectAllFilteredRunTargetsCommand { get; private set; } = null!;
    public RelayCommand ClearRunTargetSelectionCommand { get; private set; } = null!;
    public ObservableCollection<InstanceTargetItemViewModel> FilteredRunTargets { get; } = [];

    public IReadOnlyList<RunTargetAvailabilityFilterOption> RunTargetAvailabilityFilters { get; } =
    [
        new(RunTargetAvailabilityFilter.All, "Tất cả"),
        new(RunTargetAvailabilityFilter.Running, "Khả dụng"),
        new(RunTargetAvailabilityFilter.Stopped, "Không khả dụng")
    ];

    public IReadOnlyList<RunTargetSortOption> RunTargetSortOptions { get; } =
    [
        new(RunTargetSortMode.Index, "Thiết bị"),
        new(RunTargetSortMode.Name, "Tên")
    ];

    public string RunTargetSearchText
    {
        get => runTargetSearchText;
        set
        {
            if (!SetProperty(ref runTargetSearchText, value)) return;
            RebuildRunTargetProjection(clearHiddenSelection: true);
        }
    }

    public RunTargetAvailabilityFilter SelectedRunTargetAvailabilityFilter
    {
        get => runTargetAvailabilityFilter;
        set
        {
            if (!SetProperty(ref runTargetAvailabilityFilter, value)) return;
            RebuildRunTargetProjection(clearHiddenSelection: true);
        }
    }

    public RunTargetSortMode SelectedRunTargetSortMode
    {
        get => runTargetSortMode;
        set
        {
            if (!SetProperty(ref runTargetSortMode, value)) return;
            RebuildRunTargetProjection(clearHiddenSelection: false);
        }
    }

    public int FilteredRunTargetCount => FilteredRunTargets.Count;
    public string RunTargetSelectionSummary => $"Đã chọn {SelectedRunTargetCount} / Tổng {RunTargets.Count}";
    public int RemainingRunTargetCount => ResolveAllRemainingTargetCandidates().Count;
    public string RunAllRemainingLabel => $"Chạy {RemainingRunTargetCount} thiết bị chưa chạy";

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

    public ScriptItemViewModel? ControlCenterSelectedScript
    {
        get => controlCenterSelectedScript;
        set
        {
            if (!SetProperty(ref controlCenterSelectedScript, value)) return;
            OnPropertyChanged(nameof(BulkAssignmentScript));
            RaiseWorkspaceCommandStates();
        }
    }

    public ScriptItemViewModel? BulkAssignmentScript
    {
        get => ControlCenterSelectedScript;
        set => ControlCenterSelectedScript = value;
    }

    private void InitializeWorkspaceCommands()
    {
        RebuildRunTargetProjection(clearHiddenSelection: false);
        AssignScriptToSelectedCommand = new AsyncCommand(AssignScriptToSelectedAsync,
            () => IsPerInstanceScript && ControlCenterSelectedScript is not null &&
                  FilteredRunTargets.Any(item => item.IsSelected) && CanChangeSelection,
            ReportUnexpectedError);
        AssignCurrentScriptToAllCommand = new AsyncCommand(AssignCurrentScriptToAllAsync,
            () => IsPerInstanceScript && ControlCenterSelectedScript is not null && RunTargets.Count > 0 && CanChangeSelection,
            ReportUnexpectedError);
        SelectAllFilteredRunTargetsCommand = new RelayCommand(SelectAllFilteredRunTargets,
            () => CanChangeSelection && FilteredRunTargets.Any(item => item.CanSelectForRun));
        ClearRunTargetSelectionCommand = new RelayCommand(ClearRunTargetSelection,
            () => CanChangeSelection && RunTargets.Any(item => item.IsSelected));
    }

    private bool MatchesRunTargetFilter(object item)
    {
        if (item is not InstanceTargetItemViewModel target) return false;
        if (SelectedRunTargetAvailabilityFilter == RunTargetAvailabilityFilter.Running && !target.IsRunning) return false;
        if (SelectedRunTargetAvailabilityFilter == RunTargetAvailabilityFilter.Stopped && target.IsRunning) return false;

        var search = RunTargetSearchText.Trim();
        return search.Length == 0 ||
               target.Name.Contains(search, StringComparison.CurrentCultureIgnoreCase) ||
               target.Identifier.Contains(search, StringComparison.OrdinalIgnoreCase) ||
               target.DeviceKindText.Contains(search, StringComparison.CurrentCultureIgnoreCase);
    }

    private void RebuildRunTargetProjection(bool clearHiddenSelection)
    {
        var projected = RunTargets.Where(MatchesRunTargetFilter);
        projected = SelectedRunTargetSortMode == RunTargetSortMode.Name
            ? projected.OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(item => item.Identifier)
            : projected.OrderBy(item => item.DeviceKind).ThenBy(item => item.Index < 0 ? int.MaxValue : item.Index)
                .ThenBy(item => item.Identifier, StringComparer.OrdinalIgnoreCase);
        var visibleTargets = projected.ToList();

        if (clearHiddenSelection)
        {
            var visible = visibleTargets.ToHashSet();
            UpdateRunTargetSelectionBatch(() =>
            {
                foreach (var target in RunTargets.Where(item => item.IsSelected && !visible.Contains(item)))
                    target.IsSelected = false;
            });
        }

        FilteredRunTargets.Clear();
        foreach (var target in visibleTargets) FilteredRunTargets.Add(target);
        NotifyRunTargetViewStateChanged();
        NotifyRemainingRunTargetStateChanged();
    }

    private void NotifyRunTargetViewStateChanged()
    {
        OnPropertyChanged(nameof(FilteredRunTargetCount));
        OnPropertyChanged(nameof(RunTargetSelectionSummary));
        SelectAllFilteredRunTargetsCommand?.RaiseCanExecuteChanged();
        ClearRunTargetSelectionCommand?.RaiseCanExecuteChanged();
    }

    private void SelectAllFilteredRunTargets()
    {
        var visibleTargets = FilteredRunTargets.Where(item => item.CanSelectForRun).ToList();
        UpdateRunTargetSelectionBatch(() =>
        {
            foreach (var target in visibleTargets) target.IsSelected = true;
        });
    }

    private void ClearRunTargetSelection()
    {
        UpdateRunTargetSelectionBatch(() =>
        {
            foreach (var target in RunTargets) target.IsSelected = false;
        });
    }

    private void UpdateRunTargetSelectionBatch(Action update)
    {
        isBatchUpdatingRunTargetSelection = true;
        try { update(); }
        finally { isBatchUpdatingRunTargetSelection = false; }
        UpdateRunConfigurationState();
        UpdatePreview();
    }

    private void HandleRunTargetSelectionChanged()
    {
        if (isBatchUpdatingRunTargetSelection) return;
        UpdateRunConfigurationState();
        UpdatePreview();
    }

    private async void OnTargetAssignmentChanged(object? sender, EventArgs args)
    {
        if (sender is not InstanceTargetItemViewModel target) return;
        var script = Scripts.FirstOrDefault(item => item.Id == target.AssignedScriptId);
        target.SetAssignedScript(script?.Id, script?.Name, script?.Model.Kind);
        UpdateRunConfigurationState();
        try { await PersistAssignmentsAsync(); }
        catch (Exception exception) { ReportUnexpectedError(exception); }
    }

    private async Task AssignScriptToSelectedAsync()
    {
        if (ControlCenterSelectedScript is null) return;
        var selected = FilteredRunTargets.Where(item => item.IsSelected).ToList();
        foreach (var target in selected)
            target.SetAssignedScript(ControlCenterSelectedScript.Id, ControlCenterSelectedScript.Name, ControlCenterSelectedScript.Model.Kind);
        await PersistAssignmentsAsync();
        UpdateRunConfigurationState();
        StatusMessage = $"Đã gán '{ControlCenterSelectedScript.Name}' cho {selected.Count} thiết bị.";
    }

    private async Task AssignCurrentScriptToAllAsync()
    {
        if (ControlCenterSelectedScript is null) return;
        foreach (var target in RunTargets)
            target.SetAssignedScript(ControlCenterSelectedScript.Id, ControlCenterSelectedScript.Name, ControlCenterSelectedScript.Model.Kind);
        await PersistAssignmentsAsync();
        UpdateRunConfigurationState();
        StatusMessage = $"Đã gán kịch bản đang chọn '{ControlCenterSelectedScript.Name}' cho tất cả thiết bị.";
    }

    private async Task PersistAssignmentsAsync()
    {
        var visibleTargetKeys = RunTargets.Select(item => item.TargetKey).ToHashSet(StringComparer.Ordinal);
        var visibleMemuIndexes = RunTargets.Where(item => item.DeviceKind == DeviceKind.MEmu)
            .Select(item => item.Index).ToHashSet();
        var validScriptIds = Scripts.Select(item => item.Id).ToHashSet();
        var assignments = RunTargets
            .Where(item => item.AssignedScriptId is not null)
            .ToDictionary(item => item.TargetKey, item => item.AssignedScriptId!.Value, StringComparer.Ordinal);
        await UpdateApplicationSettingsAsync(settings =>
        {
            settings.MultiInstanceRun.ScriptAssignmentMode = ScriptAssignmentMode;
            settings.MultiInstanceRun.CommonScriptId = CommonRunScript?.Id;
            foreach (var key in settings.MultiInstanceRun.TargetScriptAssignments
                         .Where(pair => visibleTargetKeys.Contains(pair.Key) || !validScriptIds.Contains(pair.Value))
                         .Select(pair => pair.Key)
                         .ToList())
                settings.MultiInstanceRun.TargetScriptAssignments.Remove(key);
            foreach (var pair in assignments) settings.MultiInstanceRun.TargetScriptAssignments[pair.Key] = pair.Value;
            foreach (var index in settings.MultiInstanceRun.ScriptAssignments
                         .Where(pair => visibleMemuIndexes.Contains(pair.Key) || !validScriptIds.Contains(pair.Value))
                         .Select(pair => pair.Key)
                         .ToList())
                settings.MultiInstanceRun.ScriptAssignments.Remove(index);
            foreach (var target in RunTargets.Where(item => item.DeviceKind == DeviceKind.MEmu && item.AssignedScriptId is not null))
                settings.MultiInstanceRun.ScriptAssignments[target.Index] = target.AssignedScriptId!.Value;
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
            target.SetAssignedScript(script?.Id, script?.Name, script?.Model.Kind);
        }
        UpdateRunConfigurationState();
    }

    private string? ValidateScriptAssignments(IReadOnlyList<IExecutionTarget>? requestedTargets = null)
    {
        try { ScriptLibraryValidator.Validate(Scripts.Select(script => script.Model).ToList()); }
        catch (Exception exception) { return exception.Message; }
        if (ScriptAssignmentMode == ScriptAssignmentModeValue.OneScriptForAll)
            return CommonRunScript is null || Scripts.All(script => script.Id != CommonRunScript.Id)
                ? "Hãy chọn một kịch bản dùng chung hợp lệ."
                : !ScriptHasContent(CommonRunScript.Model)
                    ? "Kịch bản dùng chung chưa có bước hoặc mục gộp nào."
                    : null;
        var requested = requestedTargets ?? ResolveRequestedTargets();
        var missing = requested.Count(target =>
        {
            var item = RunTargets.FirstOrDefault(candidate => candidate.TargetKey == target.TargetKey);
            return item?.AssignedScriptId is not Guid id || Scripts.All(script => script.Id != id);
        });
        if (missing != 0) return $"Còn {missing} thiết bị chưa được gán kịch bản hợp lệ.";
        var empty = requested.Count(target =>
        {
            var id = RunTargets.First(row => row.TargetKey == target.TargetKey).AssignedScriptId!.Value;
            return !ScriptHasContent(Scripts.First(script => script.Id == id).Model);
        });
        return empty == 0 ? null : $"Còn {empty} thiết bị được gán kịch bản rỗng.";
    }

    private Dictionary<string, ScriptDefinition>? ResolveAssignedScripts(IReadOnlyList<IExecutionTarget> targets)
    {
        var resolved = new Dictionary<string, ScriptDefinition>(StringComparer.Ordinal);
        foreach (var target in targets)
        {
            var script = ScriptAssignmentMode == ScriptAssignmentModeValue.OneScriptForAll
                ? CommonRunScript
                : Scripts.FirstOrDefault(item => item.Id == RunTargets.FirstOrDefault(row => row.TargetKey == target.TargetKey)?.AssignedScriptId);
            if (script is null) return null;
            resolved[target.TargetKey] = script.Model;
        }
        return resolved;
    }

    private bool AssignedScriptsHaveSteps()
    {
        var targets = ResolveRequestedTargets();
        var assignedScripts = ResolveAssignedScripts(targets);
        return assignedScripts is not null &&
            assignedScripts.Count > 0 &&
            assignedScripts.Values.All(ScriptHasContent);
    }

    private static bool ScriptHasContent(ScriptDefinition script) => script.Kind == ScriptKind.Regular
        ? script.Steps.Count > 0
        : script.CompositeItems.Count > 0;

    private void RaiseWorkspaceCommandStates()
    {
        AssignScriptToSelectedCommand?.RaiseCanExecuteChanged();
        AssignCurrentScriptToAllCommand?.RaiseCanExecuteChanged();
        SelectAllFilteredRunTargetsCommand?.RaiseCanExecuteChanged();
        ClearRunTargetSelectionCommand?.RaiseCanExecuteChanged();
    }

    private void NotifyRemainingRunTargetStateChanged()
    {
        OnPropertyChanged(nameof(RemainingRunTargetCount));
        OnPropertyChanged(nameof(RunAllRemainingLabel));
    }
}
