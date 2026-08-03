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
    private readonly IApplicationPickerService applicationPickerService;
    private readonly IMemuInputCaptureService inputCaptureService;
    private readonly ITapCaptureOverlayService tapCaptureOverlayService;
    private readonly ISwipeCaptureOverlayService swipeCaptureOverlayService;
    private readonly List<StepItemViewModel> selectedSteps = [];
    private ScriptStep? copiedStep;
    private CancellationTokenSource? executionCancellation;
    private Guid? activeRunId;
    private string memucPath = string.Empty;
    private string statusMessage = "Đang đọc cấu hình…";
    private bool isBusy;
    private bool isExecuting;
    private bool isCapturing;
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
    private bool editorPressEnterAfterInput;
    private AndroidKeyEvent editorKey = AndroidKeyEvent.Home;
    private bool synchronizingSelectedSteps;

    public MainViewModel(
        IMemuInstanceService instanceService,
        IMemucPathDiscovery pathDiscovery,
        ISettingsStore settingsStore,
        IFileDialogService fileDialogService,
        IScriptStore scriptStore,
        IScriptExecutionEngine executionEngine,
        ScriptStepCommandBuilder stepCommandBuilder,
        IConfirmationService confirmationService,
        IApplicationPickerService applicationPickerService,
        IMemuInputCaptureService inputCaptureService,
        ITapCaptureOverlayService tapCaptureOverlayService,
        ISwipeCaptureOverlayService swipeCaptureOverlayService)
    {
        this.instanceService = instanceService;
        this.pathDiscovery = pathDiscovery;
        this.settingsStore = settingsStore;
        this.fileDialogService = fileDialogService;
        this.scriptStore = scriptStore;
        this.executionEngine = executionEngine;
        this.stepCommandBuilder = stepCommandBuilder;
        this.confirmationService = confirmationService;
        this.applicationPickerService = applicationPickerService;
        this.inputCaptureService = inputCaptureService;
        this.tapCaptureOverlayService = tapCaptureOverlayService;
        this.swipeCaptureOverlayService = swipeCaptureOverlayService;

        BrowseCommand = new AsyncCommand(BrowseAsync, () => !IsBusy && !IsExecuting && !IsCapturing, ReportUnexpectedError);
        RefreshCommand = new AsyncCommand(RefreshAsync, () => !IsBusy && !IsExecuting && !IsCapturing && IsPathValid, ReportUnexpectedError);
        CreateScriptCommand = new AsyncCommand(CreateScriptAsync, () => !IsExecuting && !IsCapturing, ReportUnexpectedError);
        RenameScriptCommand = new AsyncCommand(RenameScriptAsync, () => SelectedScript is not null && !IsExecuting && !IsCapturing, ReportUnexpectedError);
        DuplicateScriptCommand = new AsyncCommand(DuplicateScriptAsync, () => SelectedScript is not null && !IsExecuting && !IsCapturing, ReportUnexpectedError);
        DeleteScriptCommand = new AsyncCommand(DeleteScriptAsync, () => SelectedScript is not null && !IsExecuting && !IsCapturing, ReportUnexpectedError);
        NewStepCommand = new RelayCommand(PrepareNewStep, () => SelectedScript is not null && !IsExecuting && !IsCapturing);
        SaveStepCommand = new AsyncCommand(SaveStepAsync, () => SelectedScript is not null && !IsExecuting && !IsCapturing, ReportUnexpectedError);
        DuplicateStepCommand = new AsyncCommand(DuplicateStepAsync, () => SelectedStep is not null && !IsExecuting && !IsCapturing, ReportUnexpectedError);
        DeleteStepCommand = new AsyncCommand(DeleteStepAsync, () => SelectedStepCount > 0 && !IsExecuting && !IsCapturing, ReportUnexpectedError);
        MoveStepUpCommand = new AsyncCommand(() => MoveStepAsync(-1), () => CanMoveStep(-1), ReportUnexpectedError);
        MoveStepDownCommand = new AsyncCommand(() => MoveStepAsync(1), () => CanMoveStep(1), ReportUnexpectedError);
        RunCommand = new AsyncCommand(RunAsync, CanRun, ReportUnexpectedError);
        StopCommand = new RelayCommand(Stop, () => IsExecuting);
        SelectApplicationCommand = new AsyncCommand(SelectApplicationAsync, CanSelectApplication, ReportUnexpectedError);
        CaptureTapCommand = new AsyncCommand(CaptureTapAsync, () => CanCapture(ScriptStepKind.Tap), ReportUnexpectedError);
        CaptureSwipeCommand = new AsyncCommand(CaptureSwipeAsync, () => CanCapture(ScriptStepKind.Swipe), ReportUnexpectedError);
    }

    public ObservableCollection<MemuInstance> Instances { get; } = [];
    public ObservableCollection<ScriptItemViewModel> Scripts { get; } = [];
    public ObservableCollection<StepItemViewModel> Steps { get; } = [];
    public IReadOnlyList<StepItemViewModel> SelectedSteps => selectedSteps;
    public int SelectedStepCount => selectedSteps.Count;
    public ObservableCollection<string> ExecutionLog { get; } = [];
    public IReadOnlyList<ScriptStepKind> StepKinds { get; } = Enum.GetValues<ScriptStepKind>();
    public IReadOnlyList<AndroidKeyEvent> KeyEvents { get; } =
    [
        AndroidKeyEvent.Home,
        AndroidKeyEvent.Back,
        AndroidKeyEvent.RecentApps,
        AndroidKeyEvent.Menu,
        AndroidKeyEvent.VolumeUp,
        AndroidKeyEvent.VolumeDown
    ];

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
    public AsyncCommand SelectApplicationCommand { get; }
    public AsyncCommand CaptureTapCommand { get; }
    public AsyncCommand CaptureSwipeCommand { get; }

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
    public bool IsCapturing
    {
        get => isCapturing;
        private set
        {
            if (!SetProperty(ref isCapturing, value)) return;
            OnPropertyChanged(nameof(CanChangeSelection));
            RaiseCommandStates();
        }
    }
    public bool CanChangeSelection => !IsExecuting && !IsCapturing;

    public ScriptItemViewModel? SelectedScript
    {
        get => selectedScript;
        set
        {
            if (!CanChangeSelection && value != selectedScript) return;
            if (!SetProperty(ref selectedScript, value)) return;
            ScriptName = value?.Name ?? string.Empty;
            Steps.Clear();
            if (value is not null)
            {
                foreach (var step in value.Model.Steps) Steps.Add(CreateStepItem(step));
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
            if (!CanChangeSelection && value != selectedStep) return;
            if (!SetProperty(ref selectedStep, value)) return;
            if (!synchronizingSelectedSteps)
                ReplaceSelectedSteps(value is null ? [] : [value]);
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
            if (!CanChangeSelection && value != selectedInstance) return;
            if (SetProperty(ref selectedInstance, value)) { UpdatePreview(); RaiseCommandStates(); }
        }
    }

    public string ScriptName { get => scriptName; set => SetProperty(ref scriptName, value); }
    public string CommandPreview { get => commandPreview; private set => SetProperty(ref commandPreview, value); }
    public ScriptStepKind EditorKind
    {
        get => editorKind;
        set
        {
            if (!CanChangeSelection && value != editorKind) return;
            if (!SetProperty(ref editorKind, value)) return;
            OnPropertyChanged(nameof(ShowContinueOnError));
            OnPropertyChanged(nameof(ShowTimeout));
            OnPropertyChanged(nameof(ShowPackageName));
            OnPropertyChanged(nameof(ShowActivityName));
            OnPropertyChanged(nameof(ShowDelay));
            OnPropertyChanged(nameof(ShowTap));
            OnPropertyChanged(nameof(ShowSwipe));
            OnPropertyChanged(nameof(ShowInputText));
            OnPropertyChanged(nameof(ShowKeyEvent));
            OnPropertyChanged(nameof(ShowAndroidShell));
            OnPropertyChanged(nameof(ShowNote));
            RaiseCommandStates();
        }
    }
    public bool ShowContinueOnError => EditorKind is not ScriptStepKind.Delay and not ScriptStepKind.Note;
    public bool ShowTimeout => EditorKind is not ScriptStepKind.Delay and not ScriptStepKind.Note;
    public bool ShowPackageName => EditorKind is ScriptStepKind.ForceStop or ScriptStepKind.OpenApp;
    public bool ShowActivityName => EditorKind == ScriptStepKind.OpenApp;
    public bool ShowDelay => EditorKind == ScriptStepKind.Delay;
    public bool ShowTap => EditorKind == ScriptStepKind.Tap;
    public bool ShowSwipe => EditorKind == ScriptStepKind.Swipe;
    public bool ShowInputText => EditorKind == ScriptStepKind.InputText;
    public bool ShowKeyEvent => EditorKind == ScriptStepKind.KeyEvent;
    public bool ShowAndroidShell => EditorKind == ScriptStepKind.AndroidShell;
    public bool ShowNote => EditorKind == ScriptStepKind.Note;
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
    public bool EditorPressEnterAfterInput { get => editorPressEnterAfterInput; set => SetProperty(ref editorPressEnterAfterInput, value); }
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
        stepCommandBuilder.Validate(step);
        if (SelectedStep is null)
        {
            var item = CreateStepItem(step);
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
        var clone = CreateStepItem(ScriptCloner.CloneStep(SelectedStep.Model));
        Steps.Insert(index, clone);
        SelectedStep = clone;
        await PersistStepMutationAsync();
    }

    private async Task DeleteStepAsync()
    {
        var stepsToDelete = GetSelectedStepsForMutation();
        if (stepsToDelete.Count == 0 ||
            !confirmationService.Confirm($"Xóa {stepsToDelete.Count} bước đã chọn?", "Xác nhận xóa")) return;

        var indexes = stepsToDelete.Select(Steps.IndexOf).Where(index => index >= 0).OrderBy(index => index).ToList();
        if (indexes.Count == 0) return;
        var nextSelectionIndex = indexes[0];
        for (var index = indexes.Count - 1; index >= 0; index--)
            Steps.RemoveAt(indexes[index]);

        SelectedStep = Steps.Count == 0 ? null : Steps[Math.Min(nextSelectionIndex, Steps.Count - 1)];
        await PersistStepMutationAsync();
        StatusMessage = $"Đã xóa {indexes.Count} bước.";
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
        if (SelectedStep is null || IsExecuting || IsCapturing) return false;
        var index = Steps.IndexOf(SelectedStep) + offset;
        return index >= 0 && index < Steps.Count;
    }

    public async Task MoveStepToAsync(StepItemViewModel item, int insertionIndex)
    {
        if (!CanDragStep(item)) return;
        var oldIndex = Steps.IndexOf(item);
        if (oldIndex < 0) return;

        var normalized = Math.Clamp(insertionIndex, 0, Steps.Count);
        if (normalized > oldIndex) normalized--;
        if (normalized == oldIndex) return;

        Steps.Move(oldIndex, normalized);
        SelectedStep = item;
        await PersistStepMutationAsync();
        RaiseCommandStates();
    }

    public void CopySelectedStep()
    {
        if (!CanChangeSelection || SelectedStep is null) return;
        copiedStep = ScriptCloner.CloneStep(SelectedStep.Model);
        StatusMessage = $"Đã sao chép bước '{SelectedStep.Name}'.";
    }

    public async Task PasteCopiedStepAsync()
    {
        if (!CanChangeSelection || SelectedStep is null || copiedStep is null) return;
        var pasted = CreateStepItem(ScriptCloner.CloneStep(copiedStep));
        Steps.Insert(Steps.IndexOf(SelectedStep) + 1, pasted);
        SelectedStep = pasted;
        await PersistStepMutationAsync();
        StatusMessage = $"Đã dán bước '{pasted.Name}'.";
    }

    public Task DeleteSelectedStepFromShortcutAsync() =>
        CanChangeSelection ? DeleteStepAsync() : Task.CompletedTask;

    public bool CanDragStep(StepItemViewModel item) =>
        CanChangeSelection && selectedSteps.Count == 1 && ReferenceEquals(selectedSteps[0], item);

    public void SynchronizeSelectedSteps(IEnumerable<StepItemViewModel> selection)
    {
        ArgumentNullException.ThrowIfNull(selection);
        if (!CanChangeSelection) return;

        var normalized = selection
            .Where(Steps.Contains)
            .Distinct()
            .OrderBy(Steps.IndexOf)
            .ToList();
        ReplaceSelectedSteps(normalized);

        var primary = SelectedStep is not null && normalized.Contains(SelectedStep)
            ? SelectedStep
            : normalized.FirstOrDefault();
        if (ReferenceEquals(primary, SelectedStep)) return;

        synchronizingSelectedSteps = true;
        try { SelectedStep = primary; }
        finally { synchronizingSelectedSteps = false; }
    }

    private IReadOnlyList<StepItemViewModel> GetSelectedStepsForMutation()
    {
        var valid = selectedSteps.Where(Steps.Contains).Distinct().OrderBy(Steps.IndexOf).ToList();
        if (valid.Count == 0 && SelectedStep is not null && Steps.Contains(SelectedStep)) valid.Add(SelectedStep);
        return valid;
    }

    private void ReplaceSelectedSteps(IReadOnlyCollection<StepItemViewModel> selection)
    {
        if (selectedSteps.SequenceEqual(selection)) return;
        selectedSteps.Clear();
        selectedSteps.AddRange(selection);
        OnPropertyChanged(nameof(SelectedSteps));
        OnPropertyChanged(nameof(SelectedStepCount));
        RaiseCommandStates();
    }

    private StepItemViewModel CreateStepItem(ScriptStep step)
    {
        var item = new StepItemViewModel(step);
        item.IsEnabledChanging += (_, args) => args.Cancel = !CanChangeSelection;
        item.IsEnabledChanged += OnStepEnabledChanged;
        return item;
    }

    private async void OnStepEnabledChanged(object? sender, EventArgs e)
    {
        try
        {
            if (sender is StepItemViewModel item && ReferenceEquals(item, SelectedStep))
            {
                EditorIsEnabled = item.IsEnabled;
                UpdatePreview();
            }
            await PersistStepMutationAsync();
        }
        catch (Exception exception)
        {
            ReportUnexpectedError(exception);
        }
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

    private async Task SelectApplicationAsync()
    {
        if (SelectedInstance is null) return;
        var target = SelectedInstance;
        var targetKind = EditorKind;
        IsCapturing = true;
        StatusMessage = "Đang tải danh sách ứng dụng…";
        try
        {
            var selected = await applicationPickerService.SelectAsync(MemucPath, target.Index, CancellationToken.None);
            if (selected is null) return;
            EditorPackageName = selected.PackageName;
            if (targetKind == ScriptStepKind.OpenApp) EditorActivityName = selected.ActivityName;
            StatusMessage = $"Đã chọn ứng dụng {selected.PackageName}.";
        }
        finally { IsCapturing = false; }
    }

    private bool CanSelectApplication() =>
        !IsExecuting && !IsCapturing && IsPathValid && SelectedInstance is { IsRunning: true } &&
        EditorKind is ScriptStepKind.ForceStop or ScriptStepKind.OpenApp;

    private bool CanCapture(ScriptStepKind kind) =>
        !IsExecuting && !IsCapturing && IsPathValid && EditorKind == kind &&
        SelectedInstance is { IsRunning: true, ProcessId: > 0, WindowHandle: > 0 };

    private async Task CaptureTapAsync()
    {
        if (SelectedInstance is null) return;
        var target = SelectedInstance;
        IsCapturing = true;
        StatusMessage = "Nhấp để chọn tọa độ Chạm, có thể nhấp lại để điều chỉnh. Nhấn Enter để xác nhận hoặc Esc để hủy.";
        try
        {
            using var overlay = tapCaptureOverlayService.Show();
            var tap = await inputCaptureService.CaptureTapAsync(MemucPath, target, overlay, CancellationToken.None);
            EditorX = tap.X;
            EditorY = tap.Y;
            StatusMessage = $"Đã lấy tọa độ chạm: X={tap.X}, Y={tap.Y}.";
        }
        catch (OperationCanceledException) { StatusMessage = "Đã hủy lấy tọa độ."; }
        finally { IsCapturing = false; }
    }

    private async Task CaptureSwipeAsync()
    {
        if (SelectedInstance is null) return;
        var target = SelectedInstance;
        IsCapturing = true;
        StatusMessage = "Chuột trái chọn điểm đầu, chuột phải chọn điểm cuối. Nhấn Enter để xác nhận hoặc Esc để hủy.";
        try
        {
            using var overlay = swipeCaptureOverlayService.Show();
            var swipe = await inputCaptureService.CaptureSwipeAsync(MemucPath, target, overlay, CancellationToken.None);
            EditorX = swipe.X1;
            EditorY = swipe.Y1;
            EditorX2 = swipe.X2;
            EditorY2 = swipe.Y2;
            StatusMessage = $"Đã chọn đường vuốt từ ({swipe.X1}, {swipe.Y1}) đến ({swipe.X2}, {swipe.Y2}).";
        }
        catch (OperationCanceledException) { StatusMessage = "Đã hủy chọn đường vuốt."; }
        finally { IsCapturing = false; }
    }

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

    private bool CanRun() => !IsExecuting && !IsCapturing && SelectedScript is not null && SelectedInstance is not null && IsPathValid && Steps.Count > 0;

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
            ScriptStepKind.InputText => new InputTextStep
            {
                Id = id ?? Guid.NewGuid(),
                Name = name,
                Text = EditorText,
                PressEnterAfterInput = EditorPressEnterAfterInput
            },
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
            case InputTextStep value:
                EditorText = value.Text;
                EditorPressEnterAfterInput = value.PressEnterAfterInput;
                break;
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
        EditorText = string.Empty; EditorPressEnterAfterInput = false; EditorKey = AndroidKeyEvent.Home;
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
        SelectApplicationCommand?.RaiseCanExecuteChanged();
        CaptureTapCommand?.RaiseCanExecuteChanged(); CaptureSwipeCommand?.RaiseCanExecuteChanged();
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
