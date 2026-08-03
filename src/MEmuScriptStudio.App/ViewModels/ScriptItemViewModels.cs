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
    private StepExecutionStatus status = StepExecutionStatus.NotRun;
    private StepExecutionResult? result;

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
    public bool ContinueOnError => model.ContinueOnError;
    public string StatusText => status switch
    {
        StepExecutionStatus.NotRun => "Chưa chạy",
        StepExecutionStatus.Running => "Đang chạy",
        StepExecutionStatus.Succeeded => "Thành công",
        StepExecutionStatus.Failed => "Thất bại",
        StepExecutionStatus.Skipped => "Đã bỏ qua",
        StepExecutionStatus.Cancelled => "Đã hủy",
        _ => status.ToString()
    };
    public StepExecutionResult? Result => result;

    public void ReplaceModel(ScriptStep value)
    {
        model = value;
        OnPropertyChanged(nameof(Model));
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Kind));
        OnPropertyChanged(nameof(IsEnabled));
        OnPropertyChanged(nameof(ContinueOnError));
    }

    public void SetExecution(StepExecutionStatus value, StepExecutionResult? executionResult)
    {
        status = value;
        result = executionResult;
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(Result));
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
    private bool isLayoutSelected;
    private Guid? assignedScriptId;
    private string assignedScriptName = "Chưa gán";
    private int layoutPosition;
    private int layoutPageNumber;
    private int positionInLayoutPage;

    public event EventHandler? SelectionChanged;
    public event EventHandler? LayoutSelectionChanged;
    public event EventHandler? AssignmentChanged;

    public MemuInstance Model => model;
    public int Index => model.Index;
    public string Name => model.Name;
    public bool IsRunning => model.IsRunning;
    public string AvailabilityText => model.IsRunning ? "Đang chạy" : "Đã tắt";
    public string AssignedScriptName => assignedScriptName;
    public int LayoutPosition { get => layoutPosition; internal set => SetProperty(ref layoutPosition, value); }
    public int LayoutPageNumber
    {
        get => layoutPageNumber;
        internal set
        {
            if (!SetProperty(ref layoutPageNumber, value)) return;
            OnPropertyChanged(nameof(LayoutPageText));
        }
    }
    public int PositionInLayoutPage
    {
        get => positionInLayoutPage;
        internal set
        {
            if (!SetProperty(ref positionInLayoutPage, value)) return;
            OnPropertyChanged(nameof(LayoutSlotText));
        }
    }
    public string LayoutPageText => LayoutPageNumber > 0 ? $"Trang {LayoutPageNumber:00}" : "Chưa chạy";
    public string LayoutSlotText => PositionInLayoutPage > 0 ? $"Ô {PositionInLayoutPage:00}" : "—";

    public bool IsSelected
    {
        get => isSelected;
        set
        {
            if (!SetProperty(ref isSelected, value)) return;
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public bool IsLayoutSelected
    {
        get => isLayoutSelected;
        set
        {
            if (!SetProperty(ref isLayoutSelected, value)) return;
            LayoutSelectionChanged?.Invoke(this, EventArgs.Empty);
        }
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

public sealed class LayoutPageItemViewModel(int pageIndex, int count)
{
    public int PageIndex { get; } = pageIndex;
    public int PageNumber => PageIndex + 1;
    public int Count { get; } = count;
    public string DisplayName => $"Trang {PageNumber:00} · {Count} máy";
}

public sealed class LayoutPageFilterOption(int? pageIndex, string displayName)
{
    public int? PageIndex { get; } = pageIndex;
    public string DisplayName { get; } = displayName;
}

public sealed class InstanceStepExecutionItemViewModel(ScriptStep step) : ObservableObject
{
    private StepExecutionStatus status = StepExecutionStatus.NotRun;
    private StepExecutionResult? result;

    public Guid Id => step.Id;
    public string Name => step.Name;
    public StepExecutionStatus Status => status;
    public StepExecutionResult? Result => result;
    public string StatusText => status switch
    {
        StepExecutionStatus.NotRun => "Chưa chạy",
        StepExecutionStatus.Running => "Đang chạy",
        StepExecutionStatus.Succeeded => "Thành công",
        StepExecutionStatus.Failed => "Thất bại",
        StepExecutionStatus.Skipped => "Đã bỏ qua",
        StepExecutionStatus.Cancelled => "Đã hủy",
        _ => status.ToString()
    };
    public void SetExecution(StepExecutionStatus value, StepExecutionResult? executionResult)
    {
        status = value;
        result = executionResult;
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(Result));
    }
}

public sealed class InstanceRunItemViewModel : ObservableObject
{
    public event EventHandler? StateChanged;

    private readonly RelayCommand stopCommand;
    private InstanceExecutionStatus status = InstanceExecutionStatus.Queued;
    private string currentStep = "—";
    private string? message;

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
    public string LaunchGroupDisplay => LaunchGroupId.ToString("N")[..8];
    public MemuInstance Target { get; }
    public int Index => Target.Index;
    public string Name => Target.Name;
    public Guid ScriptId { get; }
    public string ScriptName { get; }
    public InstanceExecutionStatus Status => status;
    public string CurrentStep => currentStep;
    public string? Message => message;
    public bool CanStop => status is InstanceExecutionStatus.Queued or InstanceExecutionStatus.WaitingForLaunch or InstanceExecutionStatus.Running;
    public RelayCommand StopCommand => stopCommand;
    public ObservableCollection<InstanceStepExecutionItemViewModel> Steps { get; } = [];
    public ObservableCollection<string> Log { get; } = [];

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
    public string StatusGlyph => status switch
    {
        InstanceExecutionStatus.Queued => "○",
        InstanceExecutionStatus.WaitingForLaunch => "◷",
        InstanceExecutionStatus.Running => "▶",
        InstanceExecutionStatus.Succeeded => "✓",
        InstanceExecutionStatus.Failed => "!",
        InstanceExecutionStatus.Cancelled => "×",
        InstanceExecutionStatus.Unavailable => "—",
        _ => "•"
    };

    public void Apply(InstanceExecutionUpdate update)
    {
        if (update.LaunchGroupId != LaunchGroupId) return;
        if (update.ScriptId is Guid updateScriptId && updateScriptId != ScriptId) return;
        SetStatus(update.Status, update.Message);
        if (update.StepUpdate is null) return;

        var step = Steps.FirstOrDefault(item => item.Id == update.StepUpdate.StepId);
        step?.SetExecution(update.StepUpdate.Status, update.StepUpdate.Result);
        if (update.StepUpdate.Status == StepExecutionStatus.Running)
            CurrentStepValue = step?.Name ?? update.StepUpdate.StepId.ToString();
        if (update.StepUpdate.Result is not null)
            AppendStepLog(step?.Name ?? update.StepUpdate.StepId.ToString(), step?.StatusText ?? update.StepUpdate.Status.ToString(), update.StepUpdate.Result);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SetStatus(InstanceExecutionStatus value, string? statusMessage)
    {
        status = value;
        message = statusMessage;
        if (value is InstanceExecutionStatus.Succeeded or InstanceExecutionStatus.Failed or InstanceExecutionStatus.Cancelled or InstanceExecutionStatus.Unavailable)
            CurrentStepValue = "—";
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(StatusGlyph));
        OnPropertyChanged(nameof(Message));
        OnPropertyChanged(nameof(CanStop));
        stopCommand.RaiseCanExecuteChanged();
        if (!string.IsNullOrWhiteSpace(statusMessage)) Log.Add($"[Hệ thống] {statusMessage}");
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private string CurrentStepValue
    {
        set
        {
            if (!SetProperty(ref currentStep, value, nameof(CurrentStep))) return;
        }
    }

    private void AppendStepLog(string stepName, string statusText, StepExecutionResult result)
    {
        Log.Add($"[{stepName}] {statusText} | {result.CommandPreview}");
        if (result.ExitCode is not null) Log.Add($"Exit code: {result.ExitCode}");
        if (!string.IsNullOrWhiteSpace(result.StandardOutput)) Log.Add($"stdout: {result.StandardOutput.Trim()}");
        if (!string.IsNullOrWhiteSpace(result.StandardError)) Log.Add($"stderr: {result.StandardError.Trim()}");
    }
}

public sealed class LaunchGroupItemViewModel : ObservableObject
{
    private DateTimeOffset? endedAt;

    public LaunchGroupItemViewModel(
        int sequenceNumber,
        Guid launchGroupId,
        DateTimeOffset startedAt,
        IEnumerable<InstanceRunItemViewModel> instances)
    {
        SequenceNumber = sequenceNumber;
        LaunchGroupId = launchGroupId;
        StartedAt = startedAt;
        foreach (var instance in instances)
        {
            Instances.Add(instance);
            instance.StateChanged += OnInstanceStateChanged;
        }
    }

    public int SequenceNumber { get; }
    public string DisplayName => $"Nhóm {SequenceNumber:00}";
    public Guid LaunchGroupId { get; }
    public string TechnicalId => LaunchGroupId.ToString("D");
    public DateTimeOffset StartedAt { get; }
    public DateTimeOffset? EndedAt => endedAt;
    public bool IsCompleted => endedAt is not null;
    public ObservableCollection<InstanceRunItemViewModel> Instances { get; } = [];
    public int RunningCount => Instances.Count(item => item.Status == InstanceExecutionStatus.Running);
    public int WaitingCount => Instances.Count(item => item.Status is InstanceExecutionStatus.Queued or InstanceExecutionStatus.WaitingForLaunch);
    public int FailedCount => Instances.Count(item => item.Status == InstanceExecutionStatus.Failed);
    public int CancelledCount => Instances.Count(item => item.Status == InstanceExecutionStatus.Cancelled);
    public int SucceededCount => Instances.Count(item => item.Status == InstanceExecutionStatus.Succeeded);
    public bool CanStop => !IsCompleted && Instances.Any(item => item.CanStop);
    public string ScriptSummary => string.Join(", ", Instances.Select(item => item.ScriptName).Distinct(StringComparer.CurrentCultureIgnoreCase));
    public string StatusText => IsCompleted
        ? FailedCount > 0 ? $"Hoàn tất · {FailedCount} thất bại"
        : CancelledCount > 0 ? $"Hoàn tất · {CancelledCount} đã hủy"
        : "Hoàn tất"
        : RunningCount > 0 ? $"Đang chạy {RunningCount} · chờ {WaitingCount}"
        : $"Chờ khởi chạy {WaitingCount}";

    public void MarkCompleted(DateTimeOffset value)
    {
        if (endedAt is not null) return;
        endedAt = value;
        Refresh();
    }

    public void Detach()
    {
        foreach (var instance in Instances) instance.StateChanged -= OnInstanceStateChanged;
    }

    public void Refresh()
    {
        OnPropertyChanged(nameof(EndedAt));
        OnPropertyChanged(nameof(IsCompleted));
        OnPropertyChanged(nameof(RunningCount));
        OnPropertyChanged(nameof(WaitingCount));
        OnPropertyChanged(nameof(FailedCount));
        OnPropertyChanged(nameof(CancelledCount));
        OnPropertyChanged(nameof(SucceededCount));
        OnPropertyChanged(nameof(CanStop));
        OnPropertyChanged(nameof(ScriptSummary));
        OnPropertyChanged(nameof(StatusText));
    }

    private void OnInstanceStateChanged(object? sender, EventArgs args) => Refresh();
}
