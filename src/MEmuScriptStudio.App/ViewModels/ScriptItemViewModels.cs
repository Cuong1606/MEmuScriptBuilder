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
        StepExecutionStatus.NotRun => "Not started",
        StepExecutionStatus.Running => "Running",
        StepExecutionStatus.Succeeded => "Passed",
        StepExecutionStatus.Failed => "Failed",
        StepExecutionStatus.Skipped => "Skipped",
        StepExecutionStatus.Cancelled => "Cancelled",
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
