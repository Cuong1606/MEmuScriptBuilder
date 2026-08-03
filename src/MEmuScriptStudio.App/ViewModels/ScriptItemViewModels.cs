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
    private readonly RelayCommand stopCommand;
    private InstanceExecutionStatus status = InstanceExecutionStatus.Queued;
    private string currentStep = "—";
    private string? message;

    public InstanceRunItemViewModel(MemuInstance target, ScriptDefinition script, Action<int> stop)
    {
        Target = target;
        ScriptId = script.Id;
        ScriptName = script.Name;
        foreach (var step in script.Steps) Steps.Add(new InstanceStepExecutionItemViewModel(step));
        stopCommand = new RelayCommand(() => stop(Index), () => CanStop);
    }

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
        InstanceExecutionStatus.Queued => "Đang chờ",
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
        if (update.ScriptId is Guid updateScriptId && updateScriptId != ScriptId) return;
        SetStatus(update.Status, update.Message);
        if (update.StepUpdate is null) return;

        var step = Steps.FirstOrDefault(item => item.Id == update.StepUpdate.StepId);
        step?.SetExecution(update.StepUpdate.Status, update.StepUpdate.Result);
        if (update.StepUpdate.Status == StepExecutionStatus.Running)
            CurrentStepValue = step?.Name ?? update.StepUpdate.StepId.ToString();
        if (update.StepUpdate.Result is not null)
            AppendStepLog(step?.Name ?? update.StepUpdate.StepId.ToString(), step?.StatusText ?? update.StepUpdate.Status.ToString(), update.StepUpdate.Result);
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
