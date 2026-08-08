using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using MEmuScriptStudio.Core.Models;

namespace MEmuScriptStudio.App.ViewModels;

public enum ActiveInstanceFilter
{
    All,
    Waiting,
    Running,
    Problem
}

public sealed record ActiveInstanceFilterOption(ActiveInstanceFilter Value, string Label);

public sealed partial class MainViewModel
{
    internal const int RecentRunLimit = 20;

    private string activeInstanceSearchText = string.Empty;
    private ActiveInstanceFilter selectedActiveInstanceFilter;
    private LatestRunResultViewModel? selectedRecentRunResult;
    private ControlCenterLayoutSettings controlCenterLayout = new();
    private readonly HashSet<InstanceRunItemViewModel> observedActiveInstanceRuns = [];
    private readonly HashSet<InstanceRunItemViewModel> filteredActiveInstanceRunSet = [];

    public ObservableCollection<InstanceRunItemViewModel> FilteredActiveInstanceRuns { get; } = [];
    public ObservableCollection<LatestRunResultViewModel> RecentRuns { get; } = [];

    public IReadOnlyList<ActiveInstanceFilterOption> ActiveInstanceFilters { get; } =
    [
        new(ActiveInstanceFilter.All, "Tất cả"),
        new(ActiveInstanceFilter.Waiting, "Đang chờ"),
        new(ActiveInstanceFilter.Running, "Đang chạy"),
        new(ActiveInstanceFilter.Problem, "Có vấn đề")
    ];

    public string ActiveInstanceSearchText
    {
        get => activeInstanceSearchText;
        set
        {
            if (!SetProperty(ref activeInstanceSearchText, value)) return;
            RebuildActiveInstanceProjection();
        }
    }

    public ActiveInstanceFilter SelectedActiveInstanceFilter
    {
        get => selectedActiveInstanceFilter;
        set
        {
            if (!SetProperty(ref selectedActiveInstanceFilter, value)) return;
            RebuildActiveInstanceProjection();
        }
    }

    public int FilteredActiveInstanceCount => FilteredActiveInstanceRuns.Count;
    public bool HasActiveInstances => ActiveInstanceRuns.Count > 0;
    public bool HasNoActiveInstances => !HasActiveInstances;
    public bool HasFilteredActiveInstances => FilteredActiveInstanceCount > 0;
    public bool HasNoFilteredActiveInstances => !HasFilteredActiveInstances;
    public bool HasActiveInstancesButNoFilteredMatches => HasActiveInstances && HasNoFilteredActiveInstances;

    public LatestRunResultViewModel? SelectedRecentRunResult
    {
        get => selectedRecentRunResult;
        set
        {
            if (!SetProperty(ref selectedRecentRunResult, value)) return;
            OnPropertyChanged(nameof(HasSelectedRecentRunResult));
            SelectProblemInstancesCommand.RaiseCanExecuteChanged();
        }
    }

    public bool HasSelectedRecentRunResult => SelectedRecentRunResult is not null;
    public bool HasRecentRuns => RecentRuns.Count > 0;
    public bool HasNoRecentRuns => !HasRecentRuns;
    public ControlCenterLayoutSettings ControlCenterLayout
    {
        get => controlCenterLayout;
        private set => SetProperty(ref controlCenterLayout, value);
    }

    public RelayCommand SelectProblemInstancesCommand { get; private set; } = null!;

    private void InitializeControlCenterOperations()
    {
        SelectProblemInstancesCommand = new RelayCommand(
            SelectProblemInstances,
            () => SelectedRecentRunResult?.HasSelectableProblems == true);
        ActiveInstanceRuns.CollectionChanged += OnActiveInstanceRunsChanged;
        RecentRuns.CollectionChanged += OnRecentRunsChanged;
        RebuildActiveInstanceProjection();
    }

    private void OnRecentRunsChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        OnPropertyChanged(nameof(HasRecentRuns));
        OnPropertyChanged(nameof(HasNoRecentRuns));
    }

    private void OnActiveInstanceRunsChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        var previousActiveCount = args.Action switch
        {
            NotifyCollectionChangedAction.Add => ActiveInstanceRuns.Count - (args.NewItems?.Count ?? 0),
            NotifyCollectionChangedAction.Remove => ActiveInstanceRuns.Count + (args.OldItems?.Count ?? 0),
            NotifyCollectionChangedAction.Reset => observedActiveInstanceRuns.Count,
            _ => ActiveInstanceRuns.Count
        };
        var previousFilteredCount = FilteredActiveInstanceRuns.Count;

        if (args.Action is NotifyCollectionChangedAction.Remove or NotifyCollectionChangedAction.Replace)
        {
            foreach (var item in args.OldItems?.Cast<InstanceRunItemViewModel>() ?? [])
            {
                item.PropertyChanged -= OnActiveInstancePropertyChanged;
                observedActiveInstanceRuns.Remove(item);
                if (filteredActiveInstanceRunSet.Remove(item)) FilteredActiveInstanceRuns.Remove(item);
            }
        }

        if (args.Action is NotifyCollectionChangedAction.Add or NotifyCollectionChangedAction.Replace)
        {
            foreach (var item in args.NewItems?.Cast<InstanceRunItemViewModel>() ?? [])
            {
                if (observedActiveInstanceRuns.Add(item)) item.PropertyChanged += OnActiveInstancePropertyChanged;
                if (MatchesActiveInstanceFilter(item)) InsertActiveInstanceProjectionItem(item);
            }
        }

        if (args.Action == NotifyCollectionChangedAction.Move)
        {
            foreach (var item in args.NewItems?.Cast<InstanceRunItemViewModel>() ?? [])
            {
                if (!filteredActiveInstanceRunSet.Contains(item)) continue;
                FilteredActiveInstanceRuns.Remove(item);
                InsertActiveInstanceProjectionItem(item, alreadyTracked: true);
            }
        }

        if (args.Action == NotifyCollectionChangedAction.Reset)
        {
            foreach (var item in observedActiveInstanceRuns)
                item.PropertyChanged -= OnActiveInstancePropertyChanged;
            observedActiveInstanceRuns.Clear();
            foreach (var item in ActiveInstanceRuns)
            {
                item.PropertyChanged += OnActiveInstancePropertyChanged;
                observedActiveInstanceRuns.Add(item);
            }
            RebuildActiveInstanceProjection(notifyDerivedState: false);
        }

        NotifyActiveInstanceProjectionStateChanges(previousActiveCount, previousFilteredCount);
    }

    private void OnActiveInstancePropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (sender is not InstanceRunItemViewModel item ||
            args.PropertyName is not (nameof(InstanceRunItemViewModel.Status) or
                nameof(InstanceRunItemViewModel.Name) or nameof(InstanceRunItemViewModel.ScriptName))) return;

        var previousFilteredCount = FilteredActiveInstanceRuns.Count;
        var shouldBeVisible = MatchesActiveInstanceFilter(item);
        var isVisible = filteredActiveInstanceRunSet.Contains(item);
        if (shouldBeVisible == isVisible) return;
        if (shouldBeVisible) InsertActiveInstanceProjectionItem(item);
        else
        {
            filteredActiveInstanceRunSet.Remove(item);
            FilteredActiveInstanceRuns.Remove(item);
        }
        NotifyActiveInstanceProjectionStateChanges(ActiveInstanceRuns.Count, previousFilteredCount);
    }

    private bool MatchesActiveInstanceFilter(InstanceRunItemViewModel item)
    {
        var matchesStatus = SelectedActiveInstanceFilter switch
        {
            ActiveInstanceFilter.Waiting => item.Status is InstanceExecutionStatus.Queued or InstanceExecutionStatus.WaitingForLaunch,
            ActiveInstanceFilter.Running => item.Status == InstanceExecutionStatus.Running,
            ActiveInstanceFilter.Problem => item.Status is InstanceExecutionStatus.Failed or InstanceExecutionStatus.Unavailable,
            _ => true
        };
        if (!matchesStatus) return false;

        var search = ActiveInstanceSearchText.Trim();
        return search.Length == 0 ||
               item.Identifier.Contains(search, StringComparison.OrdinalIgnoreCase) ||
               item.DeviceKindText.Contains(search, StringComparison.CurrentCultureIgnoreCase) ||
               item.Name.Contains(search, StringComparison.CurrentCultureIgnoreCase) ||
               item.ScriptName.Contains(search, StringComparison.CurrentCultureIgnoreCase);
    }

    private void RebuildActiveInstanceProjection(bool notifyDerivedState = true)
    {
        var previousActiveCount = ActiveInstanceRuns.Count;
        var previousFilteredCount = FilteredActiveInstanceRuns.Count;
        var visible = ActiveInstanceRuns.Where(MatchesActiveInstanceFilter).ToList();
        FilteredActiveInstanceRuns.Clear();
        filteredActiveInstanceRunSet.Clear();
        foreach (var item in visible)
        {
            FilteredActiveInstanceRuns.Add(item);
            filteredActiveInstanceRunSet.Add(item);
        }
        if (notifyDerivedState)
            NotifyActiveInstanceProjectionStateChanges(previousActiveCount, previousFilteredCount);
    }

    private void InsertActiveInstanceProjectionItem(InstanceRunItemViewModel item, bool alreadyTracked = false)
    {
        if (!alreadyTracked && !filteredActiveInstanceRunSet.Add(item)) return;
        var sourceIndex = ActiveInstanceRuns.IndexOf(item);
        var projectedIndex = 0;
        for (var index = 0; index < sourceIndex; index++)
        {
            if (filteredActiveInstanceRunSet.Contains(ActiveInstanceRuns[index])) projectedIndex++;
        }
        FilteredActiveInstanceRuns.Insert(projectedIndex, item);
    }

    private void NotifyActiveInstanceProjectionStateChanges(int previousActiveCount, int previousFilteredCount)
    {
        var currentActiveCount = ActiveInstanceRuns.Count;
        var currentFilteredCount = FilteredActiveInstanceRuns.Count;
        if (previousFilteredCount != currentFilteredCount)
            OnPropertyChanged(nameof(FilteredActiveInstanceCount));
        if ((previousActiveCount > 0) != (currentActiveCount > 0))
        {
            OnPropertyChanged(nameof(HasActiveInstances));
            OnPropertyChanged(nameof(HasNoActiveInstances));
        }
        if ((previousFilteredCount > 0) != (currentFilteredCount > 0))
        {
            OnPropertyChanged(nameof(HasFilteredActiveInstances));
            OnPropertyChanged(nameof(HasNoFilteredActiveInstances));
        }
        if ((previousActiveCount > 0 && previousFilteredCount == 0) !=
            (currentActiveCount > 0 && currentFilteredCount == 0))
            OnPropertyChanged(nameof(HasActiveInstancesButNoFilteredMatches));
    }

    private void AddRecentRun(LatestRunResultViewModel result)
    {
        RecentRuns.Insert(0, result);
        while (RecentRuns.Count > RecentRunLimit) RecentRuns.RemoveAt(RecentRuns.Count - 1);
        LatestRunResult = RecentRuns[0];
        SelectedRecentRunResult = result;
    }

    private void SelectProblemInstances()
    {
        if (SelectedRecentRunResult is null) return;
        var problemTargetKeys = SelectedRecentRunResult.Instances
            .Where(item => item.Status is InstanceExecutionStatus.Failed or InstanceExecutionStatus.Unavailable)
            .Select(item => item.EffectiveTargetKey)
            .Distinct()
            .ToHashSet(StringComparer.Ordinal);
        var selectedCount = 0;
        var skippedCount = 0;
        UpdateRunTargetSelectionBatch(() =>
        {
            foreach (var target in RunTargets)
            {
                var isProblemTarget = problemTargetKeys.Contains(target.TargetKey);
                target.IsSelected = isProblemTarget && target.CanSelectForRun;
                if (target.IsSelected) selectedCount++;
                else if (isProblemTarget) skippedCount++;
            }
        });
        skippedCount += problemTargetKeys.Count(key => RunTargets.All(target => target.TargetKey != key));
        StatusMessage = skippedCount == 0
            ? $"Đã chọn {selectedCount} target có vấn đề."
            : $"Đã chọn {selectedCount} target có vấn đề; {skippedCount} target hiện không thể chạy.";
    }

    public async Task<bool> PersistControlCenterLayoutAsync(
        ControlCenterLayoutSettings layout,
        CancellationToken cancellationToken = default)
    {
        var normalized = ControlCenterLayoutSettings.Normalize(layout);
        try
        {
            applicationSettings = await settingsStore.UpdateAsync(settings =>
            {
                var persistedLayout = settings.ControlCenterLayout ??= new ControlCenterLayoutSettings();
                persistedLayout.WindowWidth = normalized.WindowWidth;
                persistedLayout.WindowHeight = normalized.WindowHeight;
                persistedLayout.IsMaximized = normalized.IsMaximized;
                persistedLayout.SetupPanelRatio = normalized.SetupPanelRatio ?? ControlCenterLayoutSettings.DefaultSetupPanelRatio;
                persistedLayout.RecentListRatio = normalized.RecentListRatio ?? ControlCenterLayoutSettings.DefaultRecentListRatio;
                persistedLayout.SetupPanelWidth = null;
            }, cancellationToken);
            ControlCenterLayout = ControlCenterLayoutSettings.Normalize(applicationSettings.ControlCenterLayout);
            return true;
        }
        catch (Exception exception)
        {
            Services.ApplicationLifecycleLogger.WriteException("ControlCenter layout persistence failed", exception);
            StatusMessage = $"Không thể lưu bố cục Control Center ({exception.Message}).";
            return false;
        }
    }
}
