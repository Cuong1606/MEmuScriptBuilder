using System.Collections.ObjectModel;
using MEmuScriptStudio.Core.Formatting;
using MEmuScriptStudio.Core.Models;

namespace MEmuScriptStudio.App.ViewModels;

public sealed class ScriptItemViewModel(ScriptDefinition model) : ObservableObject
{
    public ScriptDefinition Model { get; } = model;
    public Guid Id => Model.Id;
    public string Name => Model.Name;
    public ScriptKind Kind => Model.Kind;
    public string KindText => Kind == ScriptKind.Composite ? "Gộp" : "Thường";
    public string DisplayNameWithKind => $"{Name} · {KindText}";
    public string UpdatedAt => Model.UpdatedAt.LocalDateTime.ToString("g");
    public void Refresh() { OnPropertyChanged(nameof(Name)); OnPropertyChanged(nameof(UpdatedAt)); OnPropertyChanged(nameof(KindText)); OnPropertyChanged(nameof(DisplayNameWithKind)); }
}

public sealed class StepItemViewModel(ScriptStep model) : ObservableObject
{
    private ScriptStep model = model;
    private ScriptStepKind? draftKind;
    private int? draftDelayMilliseconds;

    public ScriptStep Model => model;
    public Guid Id => model.Id;
    public string Name => model.Name;
    public ScriptStepKind Kind => draftKind ?? model.Kind;
    public int? DurationMilliseconds => Kind == ScriptStepKind.Delay
        ? draftDelayMilliseconds ?? (model as DelayStep)?.DurationMilliseconds ?? 0
        : null;
    public string DisplayName => Kind == ScriptStepKind.Delay
        ? ScriptStepDisplayName.GetDelay(DurationMilliseconds ?? 0)
        : model.Name;
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
        draftKind = null;
        draftDelayMilliseconds = null;
        OnPropertyChanged(nameof(Model));
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Kind));
        OnPropertyChanged(nameof(DurationMilliseconds));
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(IsEnabled));
    }

    public void PreviewDraft(ScriptStepKind kind, int delayMilliseconds)
    {
        int? nextDuration = kind == ScriptStepKind.Delay ? Math.Max(0, delayMilliseconds) : null;
        if (draftKind == kind && draftDelayMilliseconds == nextDuration) return;
        draftKind = kind;
        draftDelayMilliseconds = nextDuration;
        OnPropertyChanged(nameof(Kind));
        OnPropertyChanged(nameof(DurationMilliseconds));
        OnPropertyChanged(nameof(DisplayName));
    }

    public void ClearDraftPreview()
    {
        if (draftKind is null && draftDelayMilliseconds is null) return;
        draftKind = null;
        draftDelayMilliseconds = null;
        OnPropertyChanged(nameof(Kind));
        OnPropertyChanged(nameof(DurationMilliseconds));
        OnPropertyChanged(nameof(DisplayName));
    }

    public void NotifyCanonicalNameChanged()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(DisplayName));
    }
}

public sealed class StepEnabledChangingEventArgs(bool value) : EventArgs
{
    public bool Value { get; } = value;
    public bool Cancel { get; set; }
}

public sealed class CompositeItemViewModel(CompositeScriptItem model, Func<Guid, string?> resolveScriptName) : ObservableObject
{
    private CompositeScriptItem model = model;
    private int? draftDelayMilliseconds;

    public event EventHandler<StepEnabledChangingEventArgs>? IsEnabledChanging;
    public event EventHandler? IsEnabledChanged;
    public CompositeScriptItem Model => model;
    public Guid Id => model.Id;
    public bool IsReference => model is ScriptReferenceItem;
    public bool IsDelay => model is CompositeDelayItem;
    public int? DurationMilliseconds => model is CompositeDelayItem
        ? draftDelayMilliseconds ?? ((CompositeDelayItem)model).DurationMilliseconds
        : null;
    public string KindText => IsReference ? "Kịch bản thường" : "Chờ";
    public string DisplayName => model switch
    {
        ScriptReferenceItem reference => resolveScriptName(reference.ScriptId) ?? $"Thiếu ScriptId {reference.ScriptId}",
        CompositeDelayItem => ScriptStepDisplayName.GetDelay(DurationMilliseconds ?? 0),
        _ => "—"
    };
    public string Description => model switch
    {
        ScriptReferenceItem reference => resolveScriptName(reference.ScriptId) ?? $"Thiếu ScriptId {reference.ScriptId}",
        CompositeDelayItem => DisplayName,
        _ => "—"
    };

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

    public void ReplaceModel(CompositeScriptItem value)
    {
        model = value;
        draftDelayMilliseconds = null;
        OnPropertyChanged(nameof(Model));
        OnPropertyChanged(nameof(IsReference));
        OnPropertyChanged(nameof(IsDelay));
        OnPropertyChanged(nameof(DurationMilliseconds));
        OnPropertyChanged(nameof(KindText));
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(Description));
        OnPropertyChanged(nameof(IsEnabled));
    }

    public void PreviewDelayDuration(int durationMilliseconds)
    {
        if (model is not CompositeDelayItem) return;
        var normalized = Math.Max(0, durationMilliseconds);
        if (draftDelayMilliseconds == normalized) return;
        draftDelayMilliseconds = normalized;
        OnPropertyChanged(nameof(DurationMilliseconds));
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(Description));
    }

    public void ClearDraftPreview()
    {
        if (draftDelayMilliseconds is null) return;
        draftDelayMilliseconds = null;
        OnPropertyChanged(nameof(DurationMilliseconds));
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(Description));
    }
}

public sealed class InstanceTargetItemViewModel(IExecutionTarget model) : ObservableObject
{
    private IExecutionTarget model = model;
    private bool isSelected;
    private bool isActive;
    private Guid? assignedScriptId;
    private string assignedScriptName = "Chưa gán";
    private ScriptKind? assignedScriptKind;

    public event EventHandler? SelectionChanged;
    public event EventHandler? AssignmentChanged;

    public IExecutionTarget Model => model;
    public string TargetKey => model.TargetKey;
    public DeviceKind DeviceKind => model.Kind;
    public string DeviceKindText => model.Kind == DeviceKind.MEmu ? "MEmu" : "Android / ADB";
    public string Identifier => model.Identifier;
    public int Index => model.Index;
    public string Name => model.Name;
    public bool IsRunning => model.IsRunning;
    public bool IsActive => isActive;
    public bool CanSelectForRun => model.IsRunning && !isActive;
    public string AvailabilityText => model switch
    {
        AndroidAdbDevice { ConnectionState: AndroidConnectionState.Unauthorized } => "Chưa authorize",
        AndroidAdbDevice { ConnectionState: AndroidConnectionState.Offline } => "Offline",
        AndroidAdbDevice { ConnectionState: AndroidConnectionState.Unknown } => "Không khả dụng",
        AndroidAdbDevice => "Đã kết nối",
        _ => model.IsRunning ? "Đang chạy" : "Đã tắt"
    };
    public string DeviceDetails => model switch
    {
        AndroidAdbDevice android => string.Join(" · ", new[]
        {
            string.IsNullOrWhiteSpace(android.Manufacturer) && string.IsNullOrWhiteSpace(android.Model)
                ? null
                : string.Join(' ', new[] { android.Manufacturer, android.Model }
                    .Where(value => !string.IsNullOrWhiteSpace(value))),
            $"Serial {android.Serial}",
            string.IsNullOrWhiteSpace(android.AndroidVersion) ? null : $"Android {android.AndroidVersion}",
            android.AndroidSdk is int sdk ? $"SDK {sdk}" : null,
            android.ScreenWidth is int width && android.ScreenHeight is int height ? $"{width}x{height}" : null,
            android.DensityDpi is int dpi ? $"{dpi} DPI" : null,
            string.IsNullOrWhiteSpace(android.Diagnostic) ? null : $"Cảnh báo: {android.Diagnostic}"
        }.Where(value => value is not null)),
        _ => string.Empty
    };
    public string AssignedScriptName => assignedScriptName;
    public string AssignedScriptDisplay => assignedScriptKind is null
        ? assignedScriptName
        : $"{assignedScriptName} · {(assignedScriptKind == ScriptKind.Composite ? "Gộp" : "Thường")}";

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

    public void SetAssignedScript(Guid? scriptId, string? scriptName, ScriptKind? scriptKind = null)
    {
        assignedScriptId = scriptId;
        assignedScriptName = string.IsNullOrWhiteSpace(scriptName) ? "Chưa gán" : scriptName;
        assignedScriptKind = scriptKind;
        OnPropertyChanged(nameof(AssignedScriptId));
        OnPropertyChanged(nameof(AssignedScriptName));
        OnPropertyChanged(nameof(AssignedScriptDisplay));
    }

    public void ReplaceModel(IExecutionTarget value)
    {
        model = value;
        OnPropertyChanged(nameof(Model));
        OnPropertyChanged(nameof(TargetKey));
        OnPropertyChanged(nameof(DeviceKind));
        OnPropertyChanged(nameof(DeviceKindText));
        OnPropertyChanged(nameof(Identifier));
        OnPropertyChanged(nameof(Index));
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(IsRunning));
        OnPropertyChanged(nameof(AvailabilityText));
        OnPropertyChanged(nameof(DeviceDetails));
        OnPropertyChanged(nameof(CanSelectForRun));
        if (!CanSelectForRun) IsSelected = false;
    }
}

public sealed class InstanceStepExecutionItemViewModel(ScriptStep step)
{
    public Guid Id => step.Id;
    public string Name => ScriptStepDisplayName.Get(step);
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
    private bool isStopRequested;

    public InstanceRunItemViewModel(Guid launchGroupId, IExecutionTarget target, ScriptDefinition script, Func<Guid, string, bool> stop)
    {
        LaunchGroupId = launchGroupId;
        Target = target;
        ScriptId = script.Id;
        ScriptName = script.Name;
        foreach (var step in script.Steps) Steps.Add(new InstanceStepExecutionItemViewModel(step));
        stopCommand = new RelayCommand(
            () =>
            {
                if (CanStop) stop(LaunchGroupId, TargetKey);
            },
            () => CanStop);
    }

    public Guid LaunchGroupId { get; }
    public IExecutionTarget Target { get; }
    public string TargetKey => Target.TargetKey;
    public string Identifier => Target.Identifier;
    public string DeviceKindText => Target.Kind == DeviceKind.MEmu ? "MEmu" : "Android / ADB";
    public int Index => Target.Index;
    public string Name => Target.Name;
    public Guid ScriptId { get; }
    public string ScriptName { get; }
    public InstanceExecutionStatus Status => status;
    public string CurrentStep => currentStep;
    public string? Message => message;
    public string MessageText => string.IsNullOrWhiteSpace(message) ? "—" : message;
    public bool IsStopRequested => isStopRequested;
    public bool IsSelected
    {
        get => isSelected;
        set
        {
            if (!SetProperty(ref isSelected, value)) return;
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }
    public bool CanStop => !isStopRequested &&
        status is InstanceExecutionStatus.Queued or InstanceExecutionStatus.WaitingForLaunch or InstanceExecutionStatus.Running;
    public RelayCommand StopCommand => stopCommand;
    public ObservableCollection<InstanceStepExecutionItemViewModel> Steps { get; } = [];

    public string StatusText => isStopRequested ? "Đang dừng…" : status switch
    {
        InstanceExecutionStatus.Queued => "Đang chờ",
        InstanceExecutionStatus.WaitingForLaunch => "Đang chờ",
        InstanceExecutionStatus.Running => "Đang chạy",
        InstanceExecutionStatus.Succeeded => "Thành công",
        InstanceExecutionStatus.Failed => "Lỗi",
        InstanceExecutionStatus.Cancelled => "Đã hủy",
        InstanceExecutionStatus.Unavailable => "Không khả dụng",
        _ => status.ToString()
    };
    public void Apply(InstanceExecutionUpdate update) => ApplyCore(update);

    internal InstanceRunUpdateChanges ApplyAndGetChanges(InstanceExecutionUpdate update) => ApplyCore(update);

    private InstanceRunUpdateChanges ApplyCore(InstanceExecutionUpdate update)
    {
        if (update.LaunchGroupId != LaunchGroupId) return default;
        if (update.TargetKey.Length > 0 && !string.Equals(update.TargetKey, TargetKey, StringComparison.Ordinal)) return default;
        if (update.ScriptId is Guid updateScriptId && updateScriptId != ScriptId) return default;
        if (IsTerminal(status) && !IsTerminal(update.Status)) return default;
        var changes = SetStatus(update.Status, ResolveOperationalMessage(update));
        if (update.StepUpdate is not null)
        {
            var step = Steps.FirstOrDefault(item => item.Id == update.StepUpdate.StepId);
            step?.SetExecution(update.StepUpdate.Status);
            if (update.StepUpdate.Status != StepExecutionStatus.NotRun)
                CurrentStepValue = update.StepUpdate.CompositeContext is { } context
                    ? context.FullDisplayName
                    : step?.Name ?? update.StepUpdate.StepId.ToString();
        }
        if (changes.StatusChanged) StateChanged?.Invoke(this, EventArgs.Empty);
        return changes;
    }

    public bool RequestStop()
    {
        if (!CanStop) return false;
        var previousStatusText = StatusText;
        var previousMessage = message;
        var previousCanStop = CanStop;
        isStopRequested = true;
        message = "Đang dừng theo yêu cầu…";
        if (isSelected) IsSelected = false;
        OnPropertyChanged(nameof(IsStopRequested));
        if (!string.Equals(previousStatusText, StatusText, StringComparison.Ordinal))
            OnPropertyChanged(nameof(StatusText));
        if (!string.Equals(previousMessage, message, StringComparison.Ordinal))
        {
            OnPropertyChanged(nameof(Message));
            OnPropertyChanged(nameof(MessageText));
        }
        if (previousCanStop != CanStop)
        {
            OnPropertyChanged(nameof(CanStop));
            stopCommand.RaiseCanExecuteChanged();
        }
        return true;
    }

    private InstanceRunUpdateChanges SetStatus(InstanceExecutionStatus value, string? statusMessage)
    {
        var previousStatusText = StatusText;
        var previousMessage = message;
        var previousStopRequested = isStopRequested;
        var previousCanStop = CanStop;
        var statusChanged = status != value;
        status = value;
        var compactMessage = CompactMessage(statusMessage);
        if (IsTerminal(value))
        {
            isStopRequested = false;
            message = compactMessage;
        }
        else if (compactMessage is not null && !isStopRequested)
            message = compactMessage;
        var stopRequestedChanged = previousStopRequested != isStopRequested;
        var messageChanged = !string.Equals(previousMessage, message, StringComparison.Ordinal);
        var canStopChanged = previousCanStop != CanStop;

        if (statusChanged) OnPropertyChanged(nameof(Status));
        if (stopRequestedChanged) OnPropertyChanged(nameof(IsStopRequested));
        if (!string.Equals(previousStatusText, StatusText, StringComparison.Ordinal))
            OnPropertyChanged(nameof(StatusText));
        if (messageChanged)
        {
            OnPropertyChanged(nameof(Message));
            OnPropertyChanged(nameof(MessageText));
        }
        if (canStopChanged)
        {
            OnPropertyChanged(nameof(CanStop));
            stopCommand.RaiseCanExecuteChanged();
        }
        return new InstanceRunUpdateChanges(statusChanged, canStopChanged);
    }

    private static bool IsTerminal(InstanceExecutionStatus value) =>
        value is InstanceExecutionStatus.Succeeded or InstanceExecutionStatus.Failed or
            InstanceExecutionStatus.Cancelled or InstanceExecutionStatus.Unavailable;

    private static string? ResolveOperationalMessage(InstanceExecutionUpdate update)
    {
        if (!string.IsNullOrWhiteSpace(update.Message)) return update.Message;
        var directResult = update.StepUpdate?.Result;
        if (directResult is not null)
        {
            if (!string.IsNullOrWhiteSpace(directResult.StandardError)) return directResult.StandardError;
            if (directResult.Status == StepExecutionStatus.Failed && directResult.ExitCode is int exitCode)
                return $"Bước trả về exit code {exitCode}.";
        }
        var problem = update.Result?.Steps.LastOrDefault(step =>
            step.Status is StepExecutionStatus.Failed or StepExecutionStatus.Cancelled);
        if (!string.IsNullOrWhiteSpace(problem?.StandardError)) return problem.StandardError;
        return problem?.ExitCode is int problemExitCode ? $"Bước trả về exit code {problemExitCode}." : null;
    }

    private static string? CompactMessage(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = string.Join(" ", value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return normalized.Length <= 240 ? normalized : $"{normalized[..239]}…";
    }

    private string CurrentStepValue
    {
        set
        {
            if (!SetProperty(ref currentStep, value, nameof(CurrentStep))) return;
        }
    }
}

public sealed class EditorTargetItemViewModel(IExecutionTarget model) : ObservableObject
{
    private IExecutionTarget model = model;

    public IExecutionTarget Model => model;
    public string TargetKey => model.TargetKey;
    public string Identifier => model.Identifier;
    public DeviceKind DeviceKind => model.Kind;
    public bool IsAvailable => model.IsRunning;
    public string DisplayName => model switch
    {
        MemuInstance memu => $"MEmu · {memu.Index} · {memu.Name}",
        AndroidAdbDevice { Alias: not null } android when !string.IsNullOrWhiteSpace(android.Alias) =>
            $"Android · {android.Alias.Trim()}",
        AndroidAdbDevice android => $"Android · {android.Name} · {android.Serial}",
        _ => $"{model.Kind} · {model.Identifier}"
    };
    public string Details => model switch
    {
        AndroidAdbDevice android => string.Join(" · ", new[]
        {
            string.Join(' ', new[] { android.Manufacturer, android.Model }
                .Where(value => !string.IsNullOrWhiteSpace(value))),
            $"Serial {android.Serial}"
        }.Where(value => !string.IsNullOrWhiteSpace(value))),
        _ => DisplayName
    };
    public string AvailabilityText => model switch
    {
        AndroidAdbDevice { ConnectionState: AndroidConnectionState.Unauthorized } => "Chưa authorize",
        AndroidAdbDevice { ConnectionState: AndroidConnectionState.Offline } => "Offline",
        AndroidAdbDevice { ConnectionState: AndroidConnectionState.Unknown } => "Không khả dụng",
        AndroidAdbDevice => "Đã kết nối",
        _ => model.IsRunning ? "Đang chạy" : "Đã tắt"
    };

    public void ReplaceModel(IExecutionTarget value)
    {
        model = value;
        OnPropertyChanged(nameof(Model));
        OnPropertyChanged(nameof(TargetKey));
        OnPropertyChanged(nameof(Identifier));
        OnPropertyChanged(nameof(DeviceKind));
        OnPropertyChanged(nameof(IsAvailable));
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(Details));
        OnPropertyChanged(nameof(AvailabilityText));
    }
}

internal readonly record struct InstanceRunUpdateChanges(bool StatusChanged, bool CanStopChanged);

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
    int UnavailableCount,
    int CancelledCount,
    IReadOnlyList<RecentRunInstanceSnapshotViewModel> Instances)
{
    public TimeSpan Duration => EndedAt >= StartedAt ? EndedAt - StartedAt : TimeSpan.Zero;
    public string EndedAtText => EndedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm", System.Globalization.CultureInfo.GetCultureInfo("vi-VN"));
    public string DurationText => Duration.TotalHours >= 1
        ? $"{(int)Duration.TotalHours:00}:{Duration.Minutes:00}:{Duration.Seconds:00}"
        : $"{Duration.Minutes:00}:{Duration.Seconds:00}";
    public bool HasInstances => Instances.Count > 0;
    public bool HasNoInstances => !HasInstances;
    public IReadOnlyList<RecentRunInstanceSnapshotViewModel> IssueInstances => Instances
        .Where(item => item.Status is InstanceExecutionStatus.Failed or InstanceExecutionStatus.Unavailable or InstanceExecutionStatus.Cancelled)
        .ToArray();
    public bool HasIssues => IssueInstances.Count > 0;
    public bool HasNoIssues => !HasIssues;
    public bool HasSelectableProblems => Instances.Any(item =>
        item.Status is InstanceExecutionStatus.Failed or InstanceExecutionStatus.Unavailable);
}

public record RecentRunInstanceSnapshotViewModel(
    int Index,
    string InstanceName,
    string ScriptName,
    string LastStep,
    InstanceExecutionStatus Status,
    string ShortMessage,
    string? TargetKey = null,
    DeviceKind DeviceKind = DeviceKind.MEmu,
    string? TargetIdentifier = null)
{
    public string EffectiveTargetKey => string.IsNullOrWhiteSpace(TargetKey)
        ? ExecutionTargetKeys.ForMemu(Index)
        : TargetKey;
    public string Identifier => string.IsNullOrWhiteSpace(TargetIdentifier)
        ? Index.ToString(System.Globalization.CultureInfo.InvariantCulture)
        : TargetIdentifier;
    public string DeviceKindText => DeviceKind == DeviceKind.MEmu ? "MEmu" : "Android / ADB";
    public string ErrorMessage => ShortMessage;
    public string StatusText => Status switch
    {
        InstanceExecutionStatus.Succeeded => "Thành công",
        InstanceExecutionStatus.Failed => "Lỗi",
        InstanceExecutionStatus.Unavailable => "Không khả dụng",
        InstanceExecutionStatus.Cancelled => "Đã hủy",
        _ => Status.ToString()
    };
}

public sealed record RecentRunIssueViewModel(
    int Index,
    string InstanceName,
    string ScriptName,
    string LastStep,
    InstanceExecutionStatus Status,
    string ShortMessage)
    : RecentRunInstanceSnapshotViewModel(Index, InstanceName, ScriptName, LastStep, Status, ShortMessage);
