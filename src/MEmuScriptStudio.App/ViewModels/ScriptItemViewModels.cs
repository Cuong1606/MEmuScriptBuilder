using System.Collections.ObjectModel;
using MEmuScriptStudio.Core.Models;

namespace MEmuScriptStudio.App.ViewModels;

public sealed class ScriptItemViewModel(ScriptDefinition model) : ObservableObject
{
    public ScriptDefinition Model { get; } = model;
    public Guid Id => Model.Id;
    public string Name => Model.Name;
    public string UpdatedAt => Model.UpdatedAt.LocalDateTime.ToString("g");
    public void Refresh() { OnPropertyChanged(nameof(Name)); OnPropertyChanged(nameof(UpdatedAt)); }
}

public sealed class StepItemViewModel(ScriptStep model) : ObservableObject
{
    private ScriptStep model = model;

    public ScriptStep Model => model;
    public Guid Id => model.Id;
    public string Name => model.Name;
    public ScriptStepKind Kind => model.Kind;
    public event EventHandler<StepEnabledChangingEventArgs>? IsEnabledChanging;
    public event EventHandler? IsEnabledChanged;

    public bool IsEnabled
    {
        get => model.IsEnabled;
        set
        {
            if (model.IsEnabled == value) return;
            var args = new StepEnabledChangingEventArgs(value);
            IsEnabledChanging?.Invoke(this, args);
            if (args.Cancel) return;
            model.IsEnabled = value;
            OnPropertyChanged();
            IsEnabledChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void ReplaceModel(ScriptStep value)
    {
        model = value;
        OnPropertyChanged(nameof(Model));
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Kind));
        OnPropertyChanged(nameof(IsEnabled));
    }

}

public sealed class StepEnabledChangingEventArgs(bool value) : EventArgs
{
    public bool Value { get; } = value;
    public bool Cancel { get; set; }
}

public sealed class InstanceTargetItemViewModel(MemuInstance model) : ObservableObject
{
    private MemuInstance model = model;
    private bool isSelected;
    private bool isActive;
    private Guid? assignedScriptId;
    private string assignedScriptName = "Chưa gán";

    public event EventHandler? SelectionChanged;
    public event EventHandler? AssignmentChanged;

    public MemuInstance Model => model;
    public int Index => model.Index;
    public string Name => model.Name;
    public bool IsRunning => model.IsRunning;
    public bool IsActive => isActive;
    public bool CanSelectForRun => !isActive;
    public string AvailabilityText => model.IsRunning ? "Đang chạy" : "Đã tắt";
    public string AssignedScriptName => assignedScriptName;

    public bool IsSelected
    {
        get => isSelected;
        set
        {
            if (value && !CanSelectForRun) return;
            if (!SetProperty(ref isSelected, value)) return;
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void SetActive(bool value)
    {
        if (!SetProperty(ref isActive, value, nameof(IsActive))) return;
        OnPropertyChanged(nameof(CanSelectForRun));
        if (value) IsSelected = false;
    }

    public Guid? AssignedScriptId
    {
        get => assignedScriptId;
        set
        {
            if (!SetProperty(ref assignedScriptId, value)) return;
            AssignmentChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void SetAssignedScript(Guid? scriptId, string? scriptName)
    {
        assignedScriptId = scriptId;
        assignedScriptName = string.IsNullOrWhiteSpace(scriptName) ? "Chưa gán" : scriptName;
        OnPropertyChanged(nameof(AssignedScriptId));
        OnPropertyChanged(nameof(AssignedScriptName));
    }

    public void ReplaceModel(MemuInstance value)
    {
        model = value;
        OnPropertyChanged(nameof(Model));
        OnPropertyChanged(nameof(Index));
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(IsRunning));
        OnPropertyChanged(nameof(AvailabilityText));
    }
}

public sealed class InstanceStepExecutionItemViewModel(ScriptStep step)
{
    public Guid Id => step.Id;
    public string Name => step.Name;
    public StepExecutionStatus Status { get; private set; } = StepExecutionStatus.NotRun;
    public void SetExecution(StepExecutionStatus value) => Status = value;
}

public sealed class InstanceRunItemViewModel : ObservableObject
{
    public event EventHandler? StateChanged;
    public event EventHandler? SelectionChanged;

    private readonly RelayCommand stopCommand;
    private InstanceExecutionStatus status = InstanceExecutionStatus.Queued;
    private string currentStep = "—";
    private string? message;
    private bool isSelected;

    public InstanceRunItemViewModel(Guid launchGroupId, MemuInstance target, ScriptDefinition script, Action<Guid, int> stop)
    {
        LaunchGroupId = launchGroupId;
        Target = target;
        ScriptId = script.Id;
        ScriptName = script.Name;
        foreach (var step in script.Steps) Steps.Add(new InstanceStepExecutionItemViewModel(step));
        stopCommand = new RelayCommand(() => stop(LaunchGroupId, Index), () => CanStop);
    }

    public Guid LaunchGroupId { get; }
    public MemuInstance Target { get; }
    public int Index => Target.Index;
    public string Name => Target.Name;
    public Guid ScriptId { get; }
    public string ScriptName { get; }
    public InstanceExecutionStatus Status => status;
    public string CurrentStep => currentStep;
    public string? Message => message;
    public bool IsSelected
    {
        get => isSelected;
        set
        {
            if (!SetProperty(ref isSelected, value)) return;
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }
    public bool CanStop => status is InstanceExecutionStatus.Queued or InstanceExecutionStatus.WaitingForLaunch or InstanceExecutionStatus.Running;
    public RelayCommand StopCommand => stopCommand;
    public ObservableCollection<InstanceStepExecutionItemViewModel> Steps { get; } = [];

    public string StatusText => status switch
    {
        InstanceExecutionStatus.Queued => "Chờ khởi chạy",
        InstanceExecutionStatus.WaitingForLaunch => "Chờ khởi chạy",
        InstanceExecutionStatus.Running => "Đang chạy",
        InstanceExecutionStatus.Succeeded => "Thành công",
        InstanceExecutionStatus.Failed => "Thất bại",
        InstanceExecutionStatus.Cancelled => "Đã hủy",
        InstanceExecutionStatus.Unavailable => "Không khả dụng / Bỏ qua",
        _ => status.ToString()
    };
    public void Apply(InstanceExecutionUpdate update)
    {
        if (update.LaunchGroupId != LaunchGroupId) return;
        if (update.ScriptId is Guid updateScriptId && updateScriptId != ScriptId) return;
        var groupSummaryChanged = SetStatus(update.Status, update.Message);
        if (update.StepUpdate is not null)
        {
            var step = Steps.FirstOrDefault(item => item.Id == update.StepUpdate.StepId);
            step?.SetExecution(update.StepUpdate.Status);
            if (update.StepUpdate.Status == StepExecutionStatus.Running)
                CurrentStepValue = step?.Name ?? update.StepUpdate.StepId.ToString();
        }
        if (groupSummaryChanged) StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private bool SetStatus(InstanceExecutionStatus value, string? statusMessage)
    {
        var statusChanged = status != value;
        status = value;
        message = statusMessage;
        if (value is InstanceExecutionStatus.Succeeded or InstanceExecutionStatus.Failed or InstanceExecutionStatus.Cancelled or InstanceExecutionStatus.Unavailable)
            CurrentStepValue = "—";
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(Message));
        OnPropertyChanged(nameof(CanStop));
        stopCommand.RaiseCanExecuteChanged();
        return statusChanged;
    }

    private string CurrentStepValue
    {
        set
        {
            if (!SetProperty(ref currentStep, value, nameof(CurrentStep))) return;
        }
    }
}

public sealed class LaunchGroupItemViewModel : ObservableObject
{
    private readonly Dictionary<InstanceRunItemViewModel, SummaryBucket> summaryBuckets = [];
    private bool isExpanded;
    private int runningCount;
    private int waitingCount;
    private int succeededCount;
    private int failedCount;
    private int cancelledCount;

    public LaunchGroupItemViewModel(
        int sequenceNumber,
        Guid launchGroupId,
        DateTimeOffset startedAt,
        string runDescription,
        IEnumerable<InstanceRunItemViewModel> instances)
    {
        SequenceNumber = sequenceNumber;
        LaunchGroupId = launchGroupId;
        StartedAt = startedAt;
        RunDescription = runDescription;
        foreach (var instance in instances)
        {
            Instances.Add(instance);
            instance.StateChanged += OnInstanceStateChanged;
        }
        HasInstanceStateSubscriptions = true;
        Refresh();
    }

    public int SequenceNumber { get; }
    public string DisplayName => $"Nhóm {SequenceNumber:00}";
    public string ShortId => LaunchGroupId.ToString("N")[..8];
    public Guid LaunchGroupId { get; }
    public DateTimeOffset StartedAt { get; }
    public string RunDescription { get; }
    public ObservableCollection<InstanceRunItemViewModel> Instances { get; } = [];
    public bool IsExpanded
    {
        get => isExpanded;
        set => SetProperty(ref isExpanded, value);
    }
    public int RunningCount => runningCount;
    public int WaitingCount => waitingCount;
    public int SucceededCount => succeededCount;
    public int FailedCount => failedCount;
    public int CancelledCount => cancelledCount;

    internal bool HasInstanceStateSubscriptions { get; private set; }

    public void Detach()
    {
        if (!HasInstanceStateSubscriptions) return;
        foreach (var instance in Instances) instance.StateChanged -= OnInstanceStateChanged;
        summaryBuckets.Clear();
        HasInstanceStateSubscriptions = false;
    }

    public void Refresh()
    {
        summaryBuckets.Clear();
        var nextRunning = 0;
        var nextWaiting = 0;
        var nextSucceeded = 0;
        var nextFailed = 0;
        var nextCancelled = 0;
        foreach (var instance in Instances)
        {
            var bucket = GetSummaryBucket(instance.Status);
            summaryBuckets[instance] = bucket;
            switch (bucket)
            {
                case SummaryBucket.Waiting:
                    nextWaiting++;
                    break;
                case SummaryBucket.Running:
                    nextRunning++;
                    break;
                case SummaryBucket.Succeeded:
                    nextSucceeded++;
                    break;
                case SummaryBucket.Failed:
                    nextFailed++;
                    break;
                case SummaryBucket.Cancelled:
                    nextCancelled++;
                    break;
            }
        }

        SetProperty(ref runningCount, nextRunning, nameof(RunningCount));
        SetProperty(ref waitingCount, nextWaiting, nameof(WaitingCount));
        SetProperty(ref succeededCount, nextSucceeded, nameof(SucceededCount));
        SetProperty(ref failedCount, nextFailed, nameof(FailedCount));
        SetProperty(ref cancelledCount, nextCancelled, nameof(CancelledCount));
    }

    private void OnInstanceStateChanged(object? sender, EventArgs args)
    {
        if (sender is not InstanceRunItemViewModel instance ||
            !summaryBuckets.TryGetValue(instance, out var previousBucket)) return;

        var currentBucket = GetSummaryBucket(instance.Status);
        if (currentBucket == previousBucket) return;
        summaryBuckets[instance] = currentBucket;
        AdjustBucket(previousBucket, -1);
        AdjustBucket(currentBucket, 1);
    }

    private void AdjustBucket(SummaryBucket bucket, int delta)
    {
        switch (bucket)
        {
            case SummaryBucket.Waiting:
                SetProperty(ref waitingCount, waitingCount + delta, nameof(WaitingCount));
                break;
            case SummaryBucket.Running:
                SetProperty(ref runningCount, runningCount + delta, nameof(RunningCount));
                break;
            case SummaryBucket.Succeeded:
                SetProperty(ref succeededCount, succeededCount + delta, nameof(SucceededCount));
                break;
            case SummaryBucket.Failed:
                SetProperty(ref failedCount, failedCount + delta, nameof(FailedCount));
                break;
            case SummaryBucket.Cancelled:
                SetProperty(ref cancelledCount, cancelledCount + delta, nameof(CancelledCount));
                break;
        }
    }

    private static SummaryBucket GetSummaryBucket(InstanceExecutionStatus status) => status switch
    {
        InstanceExecutionStatus.Queued or InstanceExecutionStatus.WaitingForLaunch => SummaryBucket.Waiting,
        InstanceExecutionStatus.Running => SummaryBucket.Running,
        InstanceExecutionStatus.Succeeded => SummaryBucket.Succeeded,
        InstanceExecutionStatus.Cancelled => SummaryBucket.Cancelled,
        _ => SummaryBucket.Failed
    };

    private enum SummaryBucket
    {
        Waiting,
        Running,
        Succeeded,
        Failed,
        Cancelled
    }
}

public sealed record LatestRunResultViewModel(
    Guid LaunchGroupId,
    string GroupName,
    string RunDescription,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    int TotalInstanceCount,
    int SucceededCount,
    int FailedCount,
    int CancelledCount,
    IReadOnlyList<LatestRunIssueViewModel> IssueInstances)
{
    public TimeSpan Duration => EndedAt >= StartedAt ? EndedAt - StartedAt : TimeSpan.Zero;
    public string DurationText => Duration.TotalHours >= 1
        ? $"{(int)Duration.TotalHours:00}:{Duration.Minutes:00}:{Duration.Seconds:00}"
        : $"{Duration.Minutes:00}:{Duration.Seconds:00}";
    public bool HasIssues => IssueInstances.Count > 0;
    public bool HasNoIssues => !HasIssues;
}

public sealed record LatestRunIssueViewModel(
    int Index,
    string InstanceName,
    string ScriptName,
    string LastStep,
    InstanceExecutionStatus Status,
    string ErrorMessage)
{
    public string StatusText => Status switch
    {
        InstanceExecutionStatus.Failed => "Thất bại",
        InstanceExecutionStatus.Cancelled => "Đã hủy",
        _ => Status.ToString()
    };
}
