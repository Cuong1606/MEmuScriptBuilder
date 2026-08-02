using System.Collections.ObjectModel;
using MEmuScriptStudio.App.Services;
using MEmuScriptStudio.Core.Execution;
using MEmuScriptStudio.Core.MEmu;
using MEmuScriptStudio.Core.Models;
using MEmuScriptStudio.Core.Scripts;

namespace MEmuScriptStudio.App.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly IMemuInstanceService instanceService;
    private readonly IMemucPathDiscovery pathDiscovery;
    private readonly ISettingsStore settingsStore;
    private readonly IFileDialogService fileDialogService;
    private readonly IScriptStore scriptStore;
    private readonly IScriptExecutionEngine executionEngine;
    private readonly ScriptStepCommandBuilder stepCommandBuilder;
    private readonly IConfirmationService confirmationService;
    private CancellationTokenSource? executionCancellation;
    private Guid? activeRunId;
    private string memucPath = string.Empty;
    private string statusMessage = "Đang đọc cấu hình…";
    private bool isBusy;
    private bool isExecuting;
    private ScriptItemViewModel? selectedScript;
    private StepItemViewModel? selectedStep;
    private MemuInstance? selectedInstance;
    private string scriptName = string.Empty;
    private string commandPreview = "Chọn một bước để xem preview.";
    private ScriptStepKind editorKind = ScriptStepKind.AndroidShell;
    private string editorName = "Bước mới";
    private bool editorIsEnabled = true;
    private bool editorContinueOnError;
    private int editorTimeoutSeconds = 30;
    private string editorCommand = string.Empty;
    private string editorPackageName = string.Empty;
    private string editorActivityName = string.Empty;
    private int editorDelayMilliseconds = 1000;
    private int editorX;
    private int editorY;
    private int editorX2;
    private int editorY2;
    private int editorSwipeDuration = 300;
    private string editorText = string.Empty;
    private AndroidKeyEvent editorKey = AndroidKeyEvent.Home;

    public MainViewModel(
        IMemuInstanceService instanceService,
        IMemucPathDiscovery pathDiscovery,
        ISettingsStore settingsStore,
        IFileDialogService fileDialogService,
        IScriptStore scriptStore,
        IScriptExecutionEngine executionEngine,
        ScriptStepCommandBuilder stepCommandBuilder,
        IConfirmationService confirmationService)
    {
        this.instanceService = instanceService;
        this.pathDiscovery = pathDiscovery;
        this.settingsStore = settingsStore;
        this.fileDialogService = fileDialogService;
        this.scriptStore = scriptStore;
        this.executionEngine = executionEngine;
        this.stepCommandBuilder = stepCommandBuilder;
        this.confirmationService = confirmationService;

        BrowseCommand = new AsyncCommand(BrowseAsync, () => !IsBusy && !IsExecuting, ReportUnexpectedError);
        RefreshCommand = new AsyncCommand(RefreshAsync, () => !IsBusy && !IsExecuting && IsPathValid, ReportUnexpectedError);
        CreateScriptCommand = new AsyncCommand(CreateScriptAsync, () => !IsExecuting, ReportUnexpectedError);
        RenameScriptCommand = new AsyncCommand(RenameScriptAsync, () => SelectedScript is not null && !IsExecuting, ReportUnexpectedError);
        DuplicateScriptCommand = new AsyncCommand(DuplicateScriptAsync, () => SelectedScript is not null && !IsExecuting, ReportUnexpectedError);
        DeleteScriptCommand = new AsyncCommand(DeleteScriptAsync, () => SelectedScript is not null && !IsExecuting, ReportUnexpectedError);
        NewStepCommand = new RelayCommand(PrepareNewStep, () => SelectedScript is not null && !IsExecuting);
        SaveStepCommand = new AsyncCommand(SaveStepAsync, () => SelectedScript is not null && !IsExecuting, ReportUnexpectedError);
        DuplicateStepCommand = new AsyncCommand(DuplicateStepAsync, () => SelectedStep is not null && !IsExecuting, ReportUnexpectedError);
        DeleteStepCommand = new AsyncCommand(DeleteStepAsync, () => SelectedStep is not null && !IsExecuting, ReportUnexpectedError);
        MoveStepUpCommand = new AsyncCommand(() => MoveStepAsync(-1), () => CanMoveStep(-1), ReportUnexpectedError);
        MoveStepDownCommand = new AsyncCommand(() => MoveStepAsync(1), () => CanMoveStep(1), ReportUnexpectedError);
        RunCommand = new AsyncCommand(RunAsync, CanRun, ReportUnexpectedError);
        StopCommand = new RelayCommand(Stop, () => IsExecuting);
    }

    public ObservableCollection<MemuInstance> Instances { get; } = [];
    public ObservableCollection<ScriptItemViewModel> Scripts { get; } = [];
    public ObservableCollection<StepItemViewModel> Steps { get; } = [];
    public ObservableCollection<string> ExecutionLog { get; } = [];
    public IReadOnlyList<ScriptStepKind> StepKinds { get; } = Enum.GetValues<ScriptStepKind>();
    public IReadOnlyList<AndroidKeyEvent> KeyEvents { get; } = Enum.GetValues<AndroidKeyEvent>();

    public AsyncCommand BrowseCommand { get; }
    public AsyncCommand RefreshCommand { get; }
    public AsyncCommand CreateScriptCommand { get; }
    public AsyncCommand RenameScriptCommand { get; }
    public AsyncCommand DuplicateScriptCommand { get; }
    public AsyncCommand DeleteScriptCommand { get; }
    public RelayCommand NewStepCommand { get; }
    public AsyncCommand SaveStepCommand { get; }
    public AsyncCommand DuplicateStepCommand { get; }
    public AsyncCommand DeleteStepCommand { get; }
    public AsyncCommand MoveStepUpCommand { get; }
    public AsyncCommand MoveStepDownCommand { get; }
    public AsyncCommand RunCommand { get; }
    public RelayCommand StopCommand { get; }

    public string MemucPath { get => memucPath; private set { if (SetProperty(ref memucPath, value)) { OnPropertyChanged(nameof(IsPathValid)); UpdatePreview(); RaiseCommandStates(); } } }
    public string StatusMessage { get => statusMessage; private set => SetProperty(ref statusMessage, value); }
    public bool IsPathValid => pathDiscovery.IsValidMemucPath(MemucPath);
    public bool IsBusy { get => isBusy; private set { if (SetProperty(ref isBusy, value)) RaiseCommandStates(); } }
    public bool IsExecuting
    {
        get => isExecuting;
        private set
        {
            if (!SetProperty(ref isExecuting, value)) return;
            OnPropertyChanged(nameof(CanChangeSelection));
            RaiseCommandStates();
        }
    }
    public bool CanChangeSelection => !IsExecuting;

    public ScriptItemViewModel? SelectedScript
    {
        get => selectedScript;
        set
        {
            if (IsExecuting && value != selectedScript) return;
            if (!SetProperty(ref selectedScript, value)) return;
            ScriptName = value?.Name ?? string.Empty;
            Steps.Clear();
            if (value is not null)
            {
                foreach (var step in value.Model.Steps) Steps.Add(new StepItemViewModel(step));
            }
            SelectedStep = Steps.FirstOrDefault();
            RaiseCommandStates();
        }
    }

    public StepItemViewModel? SelectedStep
    {
        get => selectedStep;
        set
        {
            if (!SetProperty(ref selectedStep, value)) return;
            if (value is null) ResetEditor(); else LoadEditor(value.Model);
            UpdatePreview();
            RaiseCommandStates();
        }
    }

    public MemuInstance? SelectedInstance
    {
        get => selectedInstance;
        set
        {
            if (IsExecuting && value != selectedInstance) return;
            if (SetProperty(ref selectedInstance, value)) { UpdatePreview(); RaiseCommandStates(); }
        }
    }

    public string ScriptName { get => scriptName; set => SetProperty(ref scriptName, value); }
    public string CommandPreview { get => commandPreview; private set => SetProperty(ref commandPreview, value); }
    public ScriptStepKind EditorKind { get => editorKind; set => SetProperty(ref editorKind, value); }
    public string EditorName { get => editorName; set => SetProperty(ref editorName, value); }
    public bool EditorIsEnabled { get => editorIsEnabled; set => SetProperty(ref editorIsEnabled, value); }
    public bool EditorContinueOnError { get => editorContinueOnError; set => SetProperty(ref editorContinueOnError, value); }
    public int EditorTimeoutSeconds { get => editorTimeoutSeconds; set => SetProperty(ref editorTimeoutSeconds, value); }
    public string EditorCommand { get => editorCommand; set => SetProperty(ref editorCommand, value); }
    public string EditorPackageName { get => editorPackageName; set => SetProperty(ref editorPackageName, value); }
    public string EditorActivityName { get => editorActivityName; set => SetProperty(ref editorActivityName, value); }
    public int EditorDelayMilliseconds { get => editorDelayMilliseconds; set => SetProperty(ref editorDelayMilliseconds, value); }
    public int EditorX { get => editorX; set => SetProperty(ref editorX, value); }
    public int EditorY { get => editorY; set => SetProperty(ref editorY, value); }
    public int EditorX2 { get => editorX2; set => SetProperty(ref editorX2, value); }
    public int EditorY2 { get => editorY2; set => SetProperty(ref editorY2, value); }
    public int EditorSwipeDuration { get => editorSwipeDuration; set => SetProperty(ref editorSwipeDuration, value); }
    public string EditorText { get => editorText; set => SetProperty(ref editorText, value); }
    public AndroidKeyEvent EditorKey { get => editorKey; set => SetProperty(ref editorKey, value); }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await InitializeMemuAsync(cancellationToken);
        try
        {
            var loaded = await scriptStore.LoadAsync(cancellationToken);
            if (loaded.Count == 0)
            {
                var template = ScriptTemplateFactory.CreateRestartChrome();
                var templateItem = new ScriptItemViewModel(template);
                Scripts.Add(templateItem);
                SelectedScript = templateItem;
                try { await scriptStore.SaveAsync([template], cancellationToken); }
                catch (Exception exception) { StatusMessage = $"{StatusMessage} Template đã được tạo trong phiên này nhưng không thể lưu ({exception.Message})."; }
            }
            else
            {
                foreach (var script in loaded) Scripts.Add(new ScriptItemViewModel(script));
            }
            SelectedScript ??= Scripts.FirstOrDefault();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            StatusMessage = $"{StatusMessage} Không thể đọc kịch bản đã lưu ({exception.Message}).";
        }
    }

    private async Task InitializeMemuAsync(CancellationToken cancellationToken)
    {
        ApplicationSettings settings;
        string? warning = null;
        try { settings = await settingsStore.LoadAsync(cancellationToken); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) { settings = new ApplicationSettings(); warning = $"Không thể đọc cấu hình đã lưu ({exception.Message})."; }

        MemucPath = pathDiscovery.IsValidMemucPath(settings.MemucPath) ? settings.MemucPath! : pathDiscovery.FindMemucPath() ?? string.Empty;
        var discovery = IsPathValid ? "Đã tìm thấy memuc.exe." : "Chưa tìm thấy memuc.exe. Hãy chọn file thủ công.";
        StatusMessage = warning is null ? discovery : $"{warning} {discovery}";
        if (IsPathValid && !string.Equals(settings.MemucPath, MemucPath, StringComparison.OrdinalIgnoreCase))
        {
            try { await settingsStore.SaveAsync(new ApplicationSettings { MemucPath = MemucPath }, cancellationToken); }
            catch (Exception exception) { StatusMessage = $"{StatusMessage} Không thể lưu đường dẫn ({exception.Message})."; }
        }
    }

    private async Task BrowseAsync()
    {
        var selectedPath = fileDialogService.SelectMemucPath(MemucPath);
        if (selectedPath is null) return;
        if (!pathDiscovery.IsValidMemucPath(selectedPath)) { StatusMessage = "File đã chọn không phải memuc.exe hợp lệ."; return; }
        MemucPath = selectedPath;
        Instances.Clear();
        try { await settingsStore.SaveAsync(new ApplicationSettings { MemucPath = selectedPath }, CancellationToken.None); StatusMessage = "Đã lưu đường dẫn memuc.exe."; }
        catch (Exception exception) { StatusMessage = $"Có thể dùng đường dẫn trong phiên này nhưng không thể lưu ({exception.Message})."; }
    }

    private async Task RefreshAsync()
    {
        IsBusy = true;
        StatusMessage = "Đang đọc danh sách máy ảo…";
        try
        {
            var selectedIndex = SelectedInstance?.Index;
            var instances = await instanceService.GetInstancesAsync(MemucPath, CancellationToken.None);
            Instances.Clear();
            foreach (var instance in instances) Instances.Add(instance);
            SelectedInstance = Instances.FirstOrDefault(item => item.Index == selectedIndex) ?? Instances.FirstOrDefault();
            StatusMessage = instances.Count == 0 ? "Không tìm thấy máy ảo nào." : $"Đã tải {instances.Count} máy ảo.";
        }
        catch (Exception exception) { StatusMessage = $"Không thể đọc danh sách máy ảo: {exception.Message}"; }
        finally { IsBusy = false; }
    }

    private async Task CreateScriptAsync()
    {
        var script = new ScriptDefinition { Name = $"Kịch bản {Scripts.Count + 1}" };
        var item = new ScriptItemViewModel(script);
        Scripts.Add(item);
        SelectedScript = item;
        await SaveScriptsAsync();
    }

    private async Task RenameScriptAsync()
    {
        if (SelectedScript is null) return;
        ArgumentException.ThrowIfNullOrWhiteSpace(ScriptName);
        SelectedScript.Model.Name = ScriptName.Trim();
        TouchSelectedScript();
        await SaveScriptsAsync();
    }

    private async Task DuplicateScriptAsync()
    {
        if (SelectedScript is null) return;
        var clone = new ScriptItemViewModel(ScriptCloner.Clone(SelectedScript.Model));
        Scripts.Add(clone);
        SelectedScript = clone;
        await SaveScriptsAsync();
    }

    private async Task DeleteScriptAsync()
    {
        if (SelectedScript is null || !confirmationService.Confirm($"Xóa kịch bản '{SelectedScript.Name}'?", "Xác nhận xóa")) return;
        var index = Scripts.IndexOf(SelectedScript);
        Scripts.Remove(SelectedScript);
        SelectedScript = Scripts.Count == 0 ? null : Scripts[Math.Min(index, Scripts.Count - 1)];
        await SaveScriptsAsync();
    }

    private void PrepareNewStep() { SelectedStep = null; ResetEditor(); }

    private async Task SaveStepAsync()
    {
        if (SelectedScript is null) return;
        var step = CreateStep(SelectedStep?.Id);
        if (SelectedStep is null)
        {
            var item = new StepItemViewModel(step);
            Steps.Add(item);
            SelectedStep = item;
        }
        else
        {
            var index = Steps.IndexOf(SelectedStep);
            SelectedStep.ReplaceModel(step);
            Steps[index] = SelectedStep;
        }
        SyncStepsToModel();
        TouchSelectedScript();
        UpdatePreview();
        await SaveScriptsAsync();
    }

    private async Task DuplicateStepAsync()
    {
        if (SelectedStep is null) return;
        var index = Steps.IndexOf(SelectedStep) + 1;
        var clone = new StepItemViewModel(ScriptCloner.CloneStep(SelectedStep.Model));
        Steps.Insert(index, clone);
        SelectedStep = clone;
        await PersistStepMutationAsync();
    }

    private async Task DeleteStepAsync()
    {
        if (SelectedStep is null || !confirmationService.Confirm($"Xóa bước '{SelectedStep.Name}'?", "Xác nhận xóa")) return;
        var index = Steps.IndexOf(SelectedStep);
        Steps.Remove(SelectedStep);
        SelectedStep = Steps.Count == 0 ? null : Steps[Math.Min(index, Steps.Count - 1)];
        await PersistStepMutationAsync();
    }

    private async Task MoveStepAsync(int offset)
    {
        if (SelectedStep is null) return;
        var oldIndex = Steps.IndexOf(SelectedStep);
        var newIndex = oldIndex + offset;
        Steps.Move(oldIndex, newIndex);
        await PersistStepMutationAsync();
        RaiseCommandStates();
    }

    private bool CanMoveStep(int offset)
    {
        if (SelectedStep is null || IsExecuting) return false;
        var index = Steps.IndexOf(SelectedStep) + offset;
        return index >= 0 && index < Steps.Count;
    }

    private async Task PersistStepMutationAsync()
    {
        SyncStepsToModel();
        TouchSelectedScript();
        await SaveScriptsAsync();
    }

    private async Task RunAsync()
    {
        if (SelectedScript is null || SelectedInstance is null || !IsPathValid) return;
        SyncStepsToModel();
        var rawStepCount = SelectedScript.Model.Steps.Count(step => step.IsEnabled && step is AndroidShellStep);
        if (rawStepCount > 0 && !confirmationService.Confirm(
                $"Kịch bản có {rawStepCount} lệnh Android shell thô. Chạy trên instance '{SelectedInstance.Name}' (index {SelectedInstance.Index})? Chỉ tiếp tục nếu bạn tin cậy các lệnh này.",
                "Cảnh báo lệnh shell thô"))
        {
            StatusMessage = "Đã hủy chạy vì lệnh shell thô chưa được xác nhận.";
            return;
        }
        foreach (var step in Steps) step.SetExecution(StepExecutionStatus.NotRun, null);
        ExecutionLog.Clear();
        executionCancellation = new CancellationTokenSource();
        var runId = Guid.NewGuid();
        activeRunId = runId;
        IsExecuting = true;
        StatusMessage = $"Đang chạy '{SelectedScript.Name}' trên {SelectedInstance.Name}…";
        var progress = new SynchronousContextProgress<StepExecutionUpdate>(update =>
        {
            if (activeRunId == runId) ApplyExecutionUpdate(update);
        });
        try
        {
            var result = await executionEngine.ExecuteAsync(new ExecutionRequest
            {
                Script = SelectedScript.Model,
                MemucPath = MemucPath,
                InstanceIndex = SelectedInstance.Index
            }, progress, executionCancellation.Token);
            StatusMessage = result.WasCancelled ? "Kịch bản đã được dừng." : "Kịch bản đã chạy xong.";
        }
        finally
        {
            activeRunId = null;
            executionCancellation.Dispose();
            executionCancellation = null;
            IsExecuting = false;
        }
    }

    private void Stop() { executionCancellation?.Cancel(); StatusMessage = "Đang dừng kịch bản…"; }

    private void ApplyExecutionUpdate(StepExecutionUpdate update)
    {
        var step = Steps.FirstOrDefault(item => item.Id == update.StepId);
        step?.SetExecution(update.Status, update.Result);
        if (update.Result is null) return;
        var result = update.Result;
        ExecutionLog.Add($"[{step?.Name ?? update.StepId.ToString()}] {step?.StatusText} | {result.CommandPreview}");
        if (result.ExitCode is not null) ExecutionLog.Add($"Exit code: {result.ExitCode}");
        if (!string.IsNullOrWhiteSpace(result.StandardOutput)) ExecutionLog.Add($"stdout: {result.StandardOutput.Trim()}");
        if (!string.IsNullOrWhiteSpace(result.StandardError)) ExecutionLog.Add($"stderr: {result.StandardError.Trim()}");
    }

    private bool CanRun() => !IsExecuting && SelectedScript is not null && SelectedInstance is not null && IsPathValid && Steps.Count > 0;

    private async Task SaveScriptsAsync() => await scriptStore.SaveAsync(Scripts.Select(item => item.Model).ToList(), CancellationToken.None);
    private void SyncStepsToModel() { if (SelectedScript is not null) { SelectedScript.Model.Steps.Clear(); SelectedScript.Model.Steps.AddRange(Steps.Select(item => item.Model)); } }
    private void TouchSelectedScript() { if (SelectedScript is null) return; SelectedScript.Model.UpdatedAt = DateTimeOffset.UtcNow; SelectedScript.Refresh(); }

    private ScriptStep CreateStep(Guid? id)
    {
        var name = string.IsNullOrWhiteSpace(EditorName) ? EditorKind.ToString() : EditorName.Trim();
        ScriptStep step = EditorKind switch
        {
            ScriptStepKind.AndroidShell => new AndroidShellStep { Id = id ?? Guid.NewGuid(), Name = name, Command = EditorCommand },
            ScriptStepKind.ForceStop => new ForceStopStep { Id = id ?? Guid.NewGuid(), Name = name, PackageName = EditorPackageName },
            ScriptStepKind.OpenApp => new OpenAppStep { Id = id ?? Guid.NewGuid(), Name = name, PackageName = EditorPackageName, ActivityName = EditorActivityName },
            ScriptStepKind.Delay => new DelayStep { Id = id ?? Guid.NewGuid(), Name = name, DurationMilliseconds = EditorDelayMilliseconds },
            ScriptStepKind.Tap => new TapStep { Id = id ?? Guid.NewGuid(), Name = name, X = EditorX, Y = EditorY },
            ScriptStepKind.Swipe => new SwipeStep { Id = id ?? Guid.NewGuid(), Name = name, X1 = EditorX, Y1 = EditorY, X2 = EditorX2, Y2 = EditorY2, DurationMilliseconds = EditorSwipeDuration },
            ScriptStepKind.InputText => new InputTextStep { Id = id ?? Guid.NewGuid(), Name = name, Text = EditorText },
            ScriptStepKind.KeyEvent => new KeyEventStep { Id = id ?? Guid.NewGuid(), Name = name, Key = EditorKey },
            ScriptStepKind.Note => new NoteStep { Id = id ?? Guid.NewGuid(), Name = name, Text = EditorText },
            _ => throw new ArgumentOutOfRangeException()
        };
        step.IsEnabled = EditorIsEnabled;
        step.ContinueOnError = EditorContinueOnError;
        step.TimeoutSeconds = EditorTimeoutSeconds;
        return step;
    }

    private void LoadEditor(ScriptStep step)
    {
        ResetEditor();
        EditorKind = step.Kind; EditorName = step.Name; EditorIsEnabled = step.IsEnabled;
        EditorContinueOnError = step.ContinueOnError; EditorTimeoutSeconds = step.TimeoutSeconds;
        switch (step)
        {
            case AndroidShellStep value: EditorCommand = value.Command; break;
            case ForceStopStep value: EditorPackageName = value.PackageName; break;
            case OpenAppStep value: EditorPackageName = value.PackageName; EditorActivityName = value.ActivityName; break;
            case DelayStep value: EditorDelayMilliseconds = value.DurationMilliseconds; break;
            case TapStep value: EditorX = value.X; EditorY = value.Y; break;
            case SwipeStep value: EditorX = value.X1; EditorY = value.Y1; EditorX2 = value.X2; EditorY2 = value.Y2; EditorSwipeDuration = value.DurationMilliseconds; break;
            case InputTextStep value: EditorText = value.Text; break;
            case KeyEventStep value: EditorKey = value.Key; break;
            case NoteStep value: EditorText = value.Text; break;
        }
    }

    private void ResetEditor()
    {
        EditorKind = ScriptStepKind.AndroidShell; EditorName = "Bước mới"; EditorIsEnabled = true;
        EditorContinueOnError = false; EditorTimeoutSeconds = 30; EditorCommand = string.Empty;
        EditorPackageName = string.Empty; EditorActivityName = string.Empty; EditorDelayMilliseconds = 1000;
        EditorX = 0; EditorY = 0; EditorX2 = 0; EditorY2 = 0; EditorSwipeDuration = 300;
        EditorText = string.Empty; EditorKey = AndroidKeyEvent.Home;
    }

    private void UpdatePreview()
    {
        CommandPreview = SelectedStep is null
            ? "Chọn một bước để xem preview."
            : stepCommandBuilder.BuildPreview(SelectedStep.Model, IsPathValid ? MemucPath : null, SelectedInstance?.Index);
    }

    private void RaiseCommandStates()
    {
        BrowseCommand?.RaiseCanExecuteChanged(); RefreshCommand?.RaiseCanExecuteChanged();
        CreateScriptCommand?.RaiseCanExecuteChanged(); RenameScriptCommand?.RaiseCanExecuteChanged();
        DuplicateScriptCommand?.RaiseCanExecuteChanged(); DeleteScriptCommand?.RaiseCanExecuteChanged();
        NewStepCommand?.RaiseCanExecuteChanged(); SaveStepCommand?.RaiseCanExecuteChanged();
        DuplicateStepCommand?.RaiseCanExecuteChanged(); DeleteStepCommand?.RaiseCanExecuteChanged();
        MoveStepUpCommand?.RaiseCanExecuteChanged(); MoveStepDownCommand?.RaiseCanExecuteChanged();
        RunCommand?.RaiseCanExecuteChanged(); StopCommand?.RaiseCanExecuteChanged();
    }

    public void ReportUnexpectedError(Exception exception) =>
        StatusMessage = $"Thao tác không hoàn tất ({exception.Message}). Hãy kiểm tra dữ liệu hoặc quyền truy cập.";

    private sealed class SynchronousContextProgress<T>(Action<T> handler) : IProgress<T>
    {
        private readonly SynchronizationContext? context = SynchronizationContext.Current;

        public void Report(T value)
        {
            if (context is null || ReferenceEquals(context, SynchronizationContext.Current)) handler(value);
            else context.Send(state => handler((T)state!), value);
        }
    }
}
