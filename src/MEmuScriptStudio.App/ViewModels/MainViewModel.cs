using System.Collections.ObjectModel;
using System.IO;
using System.Runtime.CompilerServices;
using MEmuScriptStudio.App.Services;
using MEmuScriptStudio.Core.Execution;
using MEmuScriptStudio.Core.MEmu;
using MEmuScriptStudio.Core.Models;
using MEmuScriptStudio.Core.Scripts;

namespace MEmuScriptStudio.App.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private const int StepHistoryLimit = 50;

    public event Action<IReadOnlyList<StepItemViewModel>>? StepSelectionRestoreRequested;

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
    private readonly IScriptTransferService? scriptTransferService;
    private readonly IScriptImportConflictService? scriptImportConflictService;
    private readonly List<StepItemViewModel> selectedSteps = [];
    private readonly Dictionary<Guid, StepHistory> stepHistories = [];
    private readonly SemaphoreSlim scriptSaveGate = new(1, 1);
    private IReadOnlyList<ScriptStep> copiedSteps = [];
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
    private int editorHoldDuration = 500;
    private int editorX2;
    private int editorY2;
    private int editorSwipeDuration = 300;
    private string editorText = string.Empty;
    private bool editorPressEnterAfterInput;
    private bool editorPressEnterAfterPaste;
    private AndroidKeyEvent editorKey = AndroidKeyEvent.Home;
    private bool synchronizingSelectedSteps;
    private bool suppressEditorDirty;
    private bool isApplyingStepHistory;
    private bool isStepMutationBusy;
    private StepListSnapshot? pendingToggleSnapshot;
    private bool isEditorDirty;
    private long editorVersion;

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
        ISwipeCaptureOverlayService swipeCaptureOverlayService,
        IScriptTransferService? scriptTransferService = null,
        IScriptImportConflictService? scriptImportConflictService = null)
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
        this.scriptTransferService = scriptTransferService;
        this.scriptImportConflictService = scriptImportConflictService;

        BrowseCommand = new AsyncCommand(BrowseAsync, () => !IsBusy && !IsExecuting && !IsCapturing, ReportUnexpectedError);
        RefreshCommand = new AsyncCommand(RefreshAsync, () => !IsBusy && !IsExecuting && !IsCapturing && IsPathValid, ReportUnexpectedError);
        CreateScriptCommand = new AsyncCommand(CreateScriptAsync, () => !IsExecuting && !IsCapturing, ReportUnexpectedError);
        RenameScriptCommand = new AsyncCommand(RenameScriptAsync, () => SelectedScript is not null && !IsExecuting && !IsCapturing, ReportUnexpectedError);
        DuplicateScriptCommand = new AsyncCommand(DuplicateScriptAsync, () => SelectedScript is not null && !IsExecuting && !IsCapturing, ReportUnexpectedError);
        DeleteScriptCommand = new AsyncCommand(DeleteScriptAsync, () => SelectedScript is not null && !IsExecuting && !IsCapturing, ReportUnexpectedError);
        NewStepCommand = new RelayCommand(PrepareNewStep, () => SelectedScript is not null && CanMutateSteps);
        SaveStepCommand = new AsyncCommand(SaveStepAsync, () => SelectedScript is not null && CanMutateSteps, ReportUnexpectedError);
        DuplicateStepCommand = new AsyncCommand(DuplicateStepAsync, () => SelectedStepCount > 0 && CanMutateSteps, ReportUnexpectedError);
        DeleteStepCommand = new AsyncCommand(DeleteStepAsync, () => SelectedStepCount > 0 && CanMutateSteps, ReportUnexpectedError);
        MoveStepUpCommand = new AsyncCommand(() => MoveStepAsync(-1), () => CanMoveStep(-1), ReportUnexpectedError);
        MoveStepDownCommand = new AsyncCommand(() => MoveStepAsync(1), () => CanMoveStep(1), ReportUnexpectedError);
        UndoStepListCommand = new AsyncCommand(RestoreStepHistoryAsync, CanUndoStepList, ReportUnexpectedError);
        RunCommand = new AsyncCommand(RunAsync, CanRun, ReportUnexpectedError);
        StopCommand = new RelayCommand(Stop, () => IsExecuting);
        SelectApplicationCommand = new AsyncCommand(SelectApplicationAsync, CanSelectApplication, ReportUnexpectedError);
        CaptureTapCommand = new AsyncCommand(CaptureTapAsync, () => CanCapture(ScriptStepKind.Tap), ReportUnexpectedError);
        CaptureHoldCommand = new AsyncCommand(CaptureHoldAsync, () => CanCapture(ScriptStepKind.Hold), ReportUnexpectedError);
        CaptureSwipeCommand = new AsyncCommand(CaptureSwipeAsync, () => CanCapture(ScriptStepKind.Swipe), ReportUnexpectedError);
        ExportSelectedScriptCommand = new AsyncCommand(ExportSelectedScriptAsync,
            () => scriptTransferService is not null && SelectedScript is not null && CanChangeSelection, ReportUnexpectedError);
        ExportAllScriptsCommand = new AsyncCommand(ExportAllScriptsAsync,
            () => scriptTransferService is not null && Scripts.Count > 0 && CanChangeSelection, ReportUnexpectedError);
        ImportScriptsCommand = new AsyncCommand(ImportScriptsAsync,
            () => scriptTransferService is not null && scriptImportConflictService is not null && CanChangeSelection, ReportUnexpectedError);
    }

    public ObservableCollection<MemuInstance> Instances { get; } = [];
    public ObservableCollection<ScriptItemViewModel> Scripts { get; } = [];
    public ObservableCollection<StepItemViewModel> Steps { get; } = [];
    public IReadOnlyList<StepItemViewModel> SelectedSteps => selectedSteps;
    public int SelectedStepCount => selectedSteps.Count;
    public bool HasCopiedSteps => copiedSteps.Count > 0;
    public bool IsEditorDirty => isEditorDirty;
    public string EditorSaveState => IsEditorDirty ? "Có thay đổi chưa lưu" : "Đã lưu";
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
    public AsyncCommand UndoStepListCommand { get; }
    public AsyncCommand RunCommand { get; }
    public RelayCommand StopCommand { get; }
    public AsyncCommand SelectApplicationCommand { get; }
    public AsyncCommand CaptureTapCommand { get; }
    public AsyncCommand CaptureHoldCommand { get; }
    public AsyncCommand CaptureSwipeCommand { get; }
    public AsyncCommand ExportSelectedScriptCommand { get; }
    public AsyncCommand ExportAllScriptsCommand { get; }
    public AsyncCommand ImportScriptsCommand { get; }

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
    private bool CanMutateSteps => CanChangeSelection && !isStepMutationBusy;

    public ScriptItemViewModel? SelectedScript
    {
        get => selectedScript;
        set
        {
            if (!CanChangeSelection && value != selectedScript) return;
            if (value != selectedScript && !ConfirmDiscardEditorChanges(nameof(SelectedScript))) return;
            if (!SetProperty(ref selectedScript, value)) return;
            DiscardEditorChanges();
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
            if (value != selectedStep && !ConfirmDiscardEditorChanges(nameof(SelectedStep))) return;
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
            if (!SetEditorProperty(ref editorKind, value)) return;
            OnPropertyChanged(nameof(ShowContinueOnError));
            OnPropertyChanged(nameof(ShowTimeout));
            OnPropertyChanged(nameof(ShowPackageName));
            OnPropertyChanged(nameof(ShowActivityName));
            OnPropertyChanged(nameof(ShowDelay));
            OnPropertyChanged(nameof(ShowTap));
            OnPropertyChanged(nameof(ShowHold));
            OnPropertyChanged(nameof(ShowSwipe));
            OnPropertyChanged(nameof(ShowInputText));
            OnPropertyChanged(nameof(ShowAndroidClipboardPaste));
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
    public bool ShowHold => EditorKind == ScriptStepKind.Hold;
    public bool ShowSwipe => EditorKind == ScriptStepKind.Swipe;
    public bool ShowInputText => EditorKind == ScriptStepKind.InputText;
    public bool ShowAndroidClipboardPaste => EditorKind == ScriptStepKind.AndroidClipboardPaste;
    public bool ShowKeyEvent => EditorKind == ScriptStepKind.KeyEvent;
    public bool ShowAndroidShell => EditorKind == ScriptStepKind.AndroidShell;
    public bool ShowNote => EditorKind == ScriptStepKind.Note;
    public string EditorName { get => editorName; set => SetEditorProperty(ref editorName, value); }
    public bool EditorIsEnabled { get => editorIsEnabled; set => SetEditorProperty(ref editorIsEnabled, value); }
    public bool EditorContinueOnError { get => editorContinueOnError; set => SetEditorProperty(ref editorContinueOnError, value); }
    public int EditorTimeoutSeconds { get => editorTimeoutSeconds; set => SetEditorProperty(ref editorTimeoutSeconds, value); }
    public string EditorCommand { get => editorCommand; set => SetEditorProperty(ref editorCommand, value); }
    public string EditorPackageName { get => editorPackageName; set => SetEditorProperty(ref editorPackageName, value); }
    public string EditorActivityName { get => editorActivityName; set => SetEditorProperty(ref editorActivityName, value); }
    public int EditorDelayMilliseconds { get => editorDelayMilliseconds; set => SetEditorProperty(ref editorDelayMilliseconds, value); }
    public int EditorX { get => editorX; set => SetEditorProperty(ref editorX, value); }
    public int EditorY { get => editorY; set => SetEditorProperty(ref editorY, value); }
    public int EditorHoldDuration { get => editorHoldDuration; set => SetEditorProperty(ref editorHoldDuration, value); }
    public int EditorX2 { get => editorX2; set => SetEditorProperty(ref editorX2, value); }
    public int EditorY2 { get => editorY2; set => SetEditorProperty(ref editorY2, value); }
    public int EditorSwipeDuration { get => editorSwipeDuration; set => SetEditorProperty(ref editorSwipeDuration, value); }
    public string EditorText { get => editorText; set => SetEditorProperty(ref editorText, value); }
    public bool EditorPressEnterAfterInput { get => editorPressEnterAfterInput; set => SetEditorProperty(ref editorPressEnterAfterInput, value); }
    public bool EditorPressEnterAfterPaste { get => editorPressEnterAfterPaste; set => SetEditorProperty(ref editorPressEnterAfterPaste, value); }
    public AndroidKeyEvent EditorKey { get => editorKey; set => SetEditorProperty(ref editorKey, value); }

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
            try
            {
                settings.MemucPath = MemucPath;
                await settingsStore.SaveAsync(settings, cancellationToken);
            }
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
        try
        {
            var settings = await settingsStore.LoadAsync(CancellationToken.None);
            settings.MemucPath = selectedPath;
            await settingsStore.SaveAsync(settings, CancellationToken.None);
            StatusMessage = "Đã lưu đường dẫn memuc.exe.";
        }
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
        if (!TryDiscardEditorChangesForMutation()) return;
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
        if (!TryDiscardEditorChangesForMutation()) return;
        var clone = new ScriptItemViewModel(ScriptCloner.Clone(SelectedScript.Model));
        Scripts.Add(clone);
        SelectedScript = clone;
        await SaveScriptsAsync();
    }

    private async Task DeleteScriptAsync()
    {
        if (SelectedScript is null || !confirmationService.Confirm($"Xóa kịch bản '{SelectedScript.Name}'?", "Xác nhận xóa")) return;
        if (!TryDiscardEditorChangesForMutation()) return;
        var deletedScriptId = SelectedScript.Id;
        var index = Scripts.IndexOf(SelectedScript);
        Scripts.Remove(SelectedScript);
        stepHistories.Remove(deletedScriptId);
        SelectedScript = Scripts.Count == 0 ? null : Scripts[Math.Min(index, Scripts.Count - 1)];
        await SaveScriptsAsync();
    }

    private async Task ExportSelectedScriptAsync()
    {
        if (scriptTransferService is null || SelectedScript is null) return;
        var path = fileDialogService.SelectScriptExportPath(ToSafeFileName(SelectedScript.Name));
        if (path is null) return;
        await scriptTransferService.ExportAsync(path, [SelectedScript.Model], CancellationToken.None);
        StatusMessage = $"Đã xuất kịch bản '{SelectedScript.Name}'.";
    }

    private async Task ExportAllScriptsAsync()
    {
        if (scriptTransferService is null || Scripts.Count == 0) return;
        var path = fileDialogService.SelectScriptExportPath("thu-vien-kich-ban");
        if (path is null) return;
        await scriptTransferService.ExportAsync(path, Scripts.Select(item => item.Model).ToList(), CancellationToken.None);
        StatusMessage = $"Đã xuất {Scripts.Count} kịch bản.";
    }

    private async Task ImportScriptsAsync()
    {
        if (scriptTransferService is null || scriptImportConflictService is null) return;
        var path = fileDialogService.SelectScriptImportPath();
        if (path is null) return;
        var imported = await scriptTransferService.ImportAsync(path, CancellationToken.None);
        foreach (var script in imported)
        foreach (var step in script.Steps)
            stepCommandBuilder.Validate(step);

        var plan = imported.Select(script =>
        {
            var existing = Scripts.FirstOrDefault(item => item.Id == script.Id);
            var resolution = existing is null
                ? (ScriptImportConflictResolution?)null
                : scriptImportConflictService.Resolve(script);
            return (Script: script, Existing: existing, Resolution: resolution);
        }).ToList();
        var skippedCount = plan.Count(item => item.Resolution == ScriptImportConflictResolution.Skip);
        if (plan.All(item => item.Resolution == ScriptImportConflictResolution.Skip))
        {
            StatusMessage = $"Đã nhập 0 kịch bản; bỏ qua {skippedCount}.";
            return;
        }
        if (!TryDiscardEditorChangesForMutation()) return;

        var importedCount = 0;
        ScriptItemViewModel? lastImported = null;
        foreach (var item in plan)
        {
            if (item.Existing is null)
            {
                lastImported = new ScriptItemViewModel(item.Script);
                Scripts.Add(lastImported);
                importedCount++;
                continue;
            }

            switch (item.Resolution)
            {
                case ScriptImportConflictResolution.CreateCopy:
                    lastImported = new ScriptItemViewModel(ScriptCloner.Clone(item.Script));
                    Scripts.Add(lastImported);
                    importedCount++;
                    break;
                case ScriptImportConflictResolution.Overwrite:
                    var index = Scripts.IndexOf(item.Existing);
                    stepHistories.Remove(item.Existing.Id);
                    lastImported = new ScriptItemViewModel(item.Script);
                    Scripts[index] = lastImported;
                    importedCount++;
                    break;
            }
        }

        if (importedCount > 0)
        {
            SelectedScript = lastImported;
            await SaveScriptsAsync();
        }
        StatusMessage = $"Đã nhập {importedCount} kịch bản; bỏ qua {skippedCount}.";
        RaiseCommandStates();
    }

    private static string ToSafeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var sanitized = new string(value.Trim().Select(character => invalid.Contains(character) ? '-' : character).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "kich-ban" : sanitized;
    }

    private void PrepareNewStep()
    {
        if (!TryDiscardEditorChangesForMutation()) return;
        SelectedStep = null;
        ResetEditor();
        SetEditorDirty(true);
    }

    private async Task SaveStepAsync()
    {
        if (SelectedScript is null || !TryBeginStepMutation()) return;
        try
        {
            var step = CreateStep(SelectedStep?.Id);
            stepCommandBuilder.Validate(step);
            var before = CaptureStepListSnapshot();
            if (SelectedStep is null)
            {
                var item = CreateStepItem(step);
                Steps.Add(item);
                DiscardEditorChanges();
                SetStepSelection([item], item);
            }
            else
            {
                SelectedStep.ReplaceModel(step);
            }
            PushUndoSnapshot(before);
            SyncStepsToModel();
            TouchSelectedScript();
            UpdatePreview();
            SetEditorDirty(true);
            var savedEditorVersion = editorVersion;
            await SaveScriptsAsync();
            if (editorVersion == savedEditorVersion) SetEditorDirty(false);
            StatusMessage = IsEditorDirty ? "Đã lưu bước; còn thay đổi chưa lưu." : "Đã lưu bước.";
        }
        finally { EndStepMutation(); }
    }

    private async Task DuplicateStepAsync()
    {
        var source = GetSelectedStepsForMutation();
        if (source.Count == 0 || !TryBeginStepMutation()) return;
        try
        {
            if (!TryDiscardEditorChangesForMutation()) return;
            var before = CaptureStepListSnapshot();
            var insertionIndex = source.Select(Steps.IndexOf).Max() + 1;
            var clones = source.Select(step => CreateStepItem(ScriptCloner.CloneStep(step.Model))).ToList();
            for (var index = 0; index < clones.Count; index++)
                Steps.Insert(insertionIndex + index, clones[index]);
            SetStepSelection(clones, clones[^1]);
            PushUndoSnapshot(before);
            await PersistStepMutationCoreAsync();
            StatusMessage = $"Đã nhân bản {clones.Count} bước.";
        }
        finally { EndStepMutation(); }
    }

    private async Task DeleteStepAsync()
    {
        var stepsToDelete = GetSelectedStepsForMutation();
        if (stepsToDelete.Count == 0 ||
            !confirmationService.Confirm($"Xóa {stepsToDelete.Count} bước đã chọn?", "Xác nhận xóa")) return;
        if (!TryBeginStepMutation()) return;
        try
        {
            if (!TryDiscardEditorChangesForMutation()) return;
            var indexes = stepsToDelete.Select(Steps.IndexOf).Where(index => index >= 0).OrderBy(index => index).ToList();
            if (indexes.Count == 0) return;
            var before = CaptureStepListSnapshot();
            var nextSelectionIndex = indexes[0];
            for (var index = indexes.Count - 1; index >= 0; index--)
                Steps.RemoveAt(indexes[index]);

            var next = Steps.Count == 0 ? null : Steps[Math.Min(nextSelectionIndex, Steps.Count - 1)];
            SetStepSelection(next is null ? [] : [next], next);
            PushUndoSnapshot(before);
            await PersistStepMutationCoreAsync();
            StatusMessage = $"Đã xóa {indexes.Count} bước.";
        }
        finally { EndStepMutation(); }
    }

    private async Task MoveStepAsync(int offset)
    {
        var group = GetSelectedStepsForMutation();
        if (group.Count == 0) return;
        var groupSet = group.ToHashSet();
        var remaining = Steps.Where(item => !groupSet.Contains(item)).ToList();
        var currentBlockIndex = Steps.TakeWhile(item => !ReferenceEquals(item, group[0])).Count(item => !groupSet.Contains(item));
        await MoveSelectedBlockAsync(group, remaining, currentBlockIndex + offset);
    }

    private bool CanMoveStep(int offset)
    {
        if (!CanMutateSteps) return false;
        var group = GetSelectedStepsForMutation();
        if (group.Count == 0) return false;
        var groupSet = group.ToHashSet();
        var currentBlockIndex = Steps.TakeWhile(item => !ReferenceEquals(item, group[0])).Count(item => !groupSet.Contains(item));
        var target = currentBlockIndex + offset;
        return target >= 0 && target <= Steps.Count - group.Count;
    }

    public async Task MoveStepToAsync(StepItemViewModel item, int insertionIndex)
    {
        if (!CanDragStep(item)) return;
        var group = GetSelectedStepsForMutation();
        var groupSet = group.ToHashSet();
        var original = Steps.ToList();
        var normalized = Math.Clamp(insertionIndex, 0, original.Count);
        var adjustedInsertionIndex = normalized - original.Take(normalized).Count(groupSet.Contains);
        var remaining = original.Where(step => !groupSet.Contains(step)).ToList();
        await MoveSelectedBlockAsync(group, remaining, adjustedInsertionIndex);
    }

    private async Task MoveSelectedBlockAsync(
        IReadOnlyList<StepItemViewModel> group,
        IReadOnlyList<StepItemViewModel> remaining,
        int insertionIndex)
    {
        var normalized = Math.Clamp(insertionIndex, 0, remaining.Count);
        var desired = remaining.ToList();
        desired.InsertRange(normalized, group);
        if (Steps.SequenceEqual(desired) || !TryBeginStepMutation()) return;
        try
        {
            if (!TryDiscardEditorChangesForMutation()) return;
            var before = CaptureStepListSnapshot();
            synchronizingSelectedSteps = true;
            try
            {
                for (var targetIndex = 0; targetIndex < desired.Count; targetIndex++)
                {
                    var currentIndex = Steps.IndexOf(desired[targetIndex]);
                    if (currentIndex != targetIndex) Steps.Move(currentIndex, targetIndex);
                }
            }
            finally { synchronizingSelectedSteps = false; }
            var primary = SelectedStep is not null && group.Contains(SelectedStep) ? SelectedStep : group[0];
            SetStepSelection(group, primary);
            PushUndoSnapshot(before);
            await PersistStepMutationCoreAsync();
            StatusMessage = $"Đã di chuyển {group.Count} bước.";
        }
        finally { EndStepMutation(); }
    }

    public void CopySelectedSteps()
    {
        if (!CanChangeSelection) return;
        var source = GetSelectedStepsForMutation();
        if (source.Count == 0) return;
        copiedSteps = source.Select(item => ScriptCloner.CloneStep(item.Model)).ToList();
        OnPropertyChanged(nameof(HasCopiedSteps));
        StatusMessage = $"Đã sao chép {copiedSteps.Count} bước.";
    }

    public async Task PasteCopiedStepsAsync()
    {
        if (SelectedScript is null || copiedSteps.Count == 0 || !TryBeginStepMutation()) return;
        try
        {
            if (!TryDiscardEditorChangesForMutation()) return;
            var before = CaptureStepListSnapshot();
            var selectedIndexes = GetSelectedStepsForMutation().Select(Steps.IndexOf).Where(index => index >= 0).ToList();
            var insertionIndex = selectedIndexes.Count == 0 ? Steps.Count : selectedIndexes.Max() + 1;
            var pasted = copiedSteps.Select(step => CreateStepItem(ScriptCloner.CloneStep(step))).ToList();
            for (var index = 0; index < pasted.Count; index++)
                Steps.Insert(insertionIndex + index, pasted[index]);
            SetStepSelection(pasted, pasted[^1]);
            PushUndoSnapshot(before);
            await PersistStepMutationCoreAsync();
            StatusMessage = $"Đã dán {pasted.Count} bước.";
        }
        finally { EndStepMutation(); }
    }

    public Task DeleteSelectedStepFromShortcutAsync() =>
        CanMutateSteps ? DeleteStepAsync() : Task.CompletedTask;

    public bool CanDragStep(StepItemViewModel item) =>
        CanMutateSteps && GetSelectedStepsForMutation().Contains(item);

    public void SynchronizeSelectedSteps(IEnumerable<StepItemViewModel> selection)
    {
        ArgumentNullException.ThrowIfNull(selection);
        if (!CanChangeSelection || synchronizingSelectedSteps) return;

        var normalized = selection
            .Where(Steps.Contains)
            .Distinct()
            .OrderBy(Steps.IndexOf)
            .ToList();
        var primary = SelectedStep is not null && normalized.Contains(SelectedStep)
            ? SelectedStep
            : normalized.FirstOrDefault();
        if (ReferenceEquals(primary, SelectedStep))
        {
            ReplaceSelectedSteps(normalized);
            return;
        }

        var previousSelection = GetSelectedStepsForMutation();
        synchronizingSelectedSteps = true;
        try { SelectedStep = primary; }
        finally { synchronizingSelectedSteps = false; }
        if (!ReferenceEquals(SelectedStep, primary))
        {
            RestoreStepSelection(previousSelection);
            return;
        }
        ReplaceSelectedSteps(normalized);
    }

    public bool TryClearStepSelection()
    {
        if (!CanChangeSelection) return false;
        if (SelectedStep is null && selectedSteps.Count == 0) return true;
        if (!ConfirmDiscardEditorChanges())
        {
            RestoreStepSelection(GetSelectedStepsForMutation());
            return false;
        }

        DiscardEditorChanges();
        SetStepSelection([], null);
        return true;
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

    private void SetStepSelection(IReadOnlyList<StepItemViewModel> selection, StepItemViewModel? primary)
    {
        synchronizingSelectedSteps = true;
        try
        {
            SelectedStep = primary;
            ReplaceSelectedSteps(selection);
            StepSelectionRestoreRequested?.Invoke(selection);
        }
        finally { synchronizingSelectedSteps = false; }
    }

    private void RestoreStepSelection(IReadOnlyList<StepItemViewModel> selection)
    {
        synchronizingSelectedSteps = true;
        try { StepSelectionRestoreRequested?.Invoke(selection); }
        finally { synchronizingSelectedSteps = false; }
    }

    private StepItemViewModel CreateStepItem(ScriptStep step)
    {
        var item = new StepItemViewModel(step);
        item.IsEnabledChanging += OnStepEnabledChanging;
        item.IsEnabledChanged += OnStepEnabledChanged;
        return item;
    }

    private void OnStepEnabledChanging(object? sender, StepEnabledChangingEventArgs args)
    {
        if (isApplyingStepHistory) return;
        if (!CanMutateSteps || SelectedScript is null)
        {
            args.Cancel = true;
            return;
        }

        pendingToggleSnapshot = CaptureStepListSnapshot();
        SetStepMutationBusy(true);
    }

    private async void OnStepEnabledChanged(object? sender, EventArgs e)
    {
        try
        {
            if (sender is StepItemViewModel item && ReferenceEquals(item, SelectedStep))
            {
                suppressEditorDirty = true;
                try { EditorIsEnabled = item.IsEnabled; }
                finally { suppressEditorDirty = false; }
                UpdatePreview();
            }
            if (pendingToggleSnapshot is not null) PushUndoSnapshot(pendingToggleSnapshot);
            await PersistStepMutationCoreAsync();
        }
        catch (Exception exception)
        {
            ReportUnexpectedError(exception);
        }
        finally
        {
            pendingToggleSnapshot = null;
            EndStepMutation();
        }
    }

    private async Task PersistStepMutationCoreAsync()
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

    private async Task CaptureHoldAsync()
    {
        if (SelectedInstance is null) return;
        var target = SelectedInstance;
        IsCapturing = true;
        StatusMessage = "Nhấp để chọn tọa độ Nhấn giữ, có thể nhấp lại để điều chỉnh. Nhấn Enter để xác nhận hoặc Esc để hủy.";
        try
        {
            using var overlay = tapCaptureOverlayService.Show();
            var tap = await inputCaptureService.CaptureTapAsync(MemucPath, target, overlay, CancellationToken.None);
            EditorX = tap.X;
            EditorY = tap.Y;
            StatusMessage = $"Đã chọn tọa độ nhấn giữ: X={tap.X}, Y={tap.Y}.";
        }
        catch (OperationCanceledException) { StatusMessage = "Đã hủy chọn tọa độ nhấn giữ."; }
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

    private async Task SaveScriptsAsync()
    {
        await scriptSaveGate.WaitAsync(CancellationToken.None);
        try
        {
            var snapshot = Scripts.Select(item => SnapshotScript(item.Model)).ToList();
            await scriptStore.SaveAsync(snapshot, CancellationToken.None);
        }
        finally { scriptSaveGate.Release(); }
    }

    private void SyncStepsToModel() { if (SelectedScript is not null) { SelectedScript.Model.Steps.Clear(); SelectedScript.Model.Steps.AddRange(Steps.Select(item => item.Model)); } }
    private void TouchSelectedScript() { if (SelectedScript is null) return; SelectedScript.Model.UpdatedAt = DateTimeOffset.UtcNow; SelectedScript.Refresh(); }

    private bool TryBeginStepMutation()
    {
        if (!CanMutateSteps) return false;
        SetStepMutationBusy(true);
        return true;
    }

    private void EndStepMutation() => SetStepMutationBusy(false);

    private void SetStepMutationBusy(bool value)
    {
        if (isStepMutationBusy == value) return;
        isStepMutationBusy = value;
        RaiseCommandStates();
    }

    private bool CanUndoStepList() =>
        CanMutateSteps && SelectedScript is not null &&
        stepHistories.TryGetValue(SelectedScript.Id, out var history) && history.Undo.Count > 0;

    private async Task RestoreStepHistoryAsync()
    {
        if (SelectedScript is null || !TryBeginStepMutation()) return;
        try
        {
            if (!TryDiscardEditorChangesForMutation()) return;
            var history = GetStepHistory(SelectedScript.Id);
            if (history.Undo.Count == 0) return;

            var target = history.Undo.Last!.Value;
            history.Undo.RemoveLast();
            ApplyStepListSnapshot(target);
            await PersistStepMutationCoreAsync();
            StatusMessage = "Đã hoàn tác thao tác danh sách bước.";
        }
        finally { EndStepMutation(); }
    }

    private StepListSnapshot CaptureStepListSnapshot() => new(
        Steps.Select(item => ScriptCloner.CloneStepPreservingId(item.Model)).ToList(),
        selectedSteps.Where(Steps.Contains).Select(item => item.Id).ToList(),
        SelectedStep is not null && Steps.Contains(SelectedStep) ? SelectedStep.Id : null);

    private void ApplyStepListSnapshot(StepListSnapshot snapshot)
    {
        isApplyingStepHistory = true;
        try
        {
            Steps.Clear();
            foreach (var step in snapshot.Steps.Select(ScriptCloner.CloneStepPreservingId))
                Steps.Add(CreateStepItem(step));

            var selectedIds = snapshot.SelectedStepIds.ToHashSet();
            var selection = Steps.Where(item => selectedIds.Contains(item.Id)).ToList();
            var primary = snapshot.PrimaryStepId is Guid primaryId
                ? selection.FirstOrDefault(item => item.Id == primaryId)
                : null;
            primary ??= selection.FirstOrDefault();
            SetStepSelection(selection, primary);
        }
        finally { isApplyingStepHistory = false; }
    }

    private void PushUndoSnapshot(StepListSnapshot snapshot)
    {
        if (SelectedScript is null) return;
        var history = GetStepHistory(SelectedScript.Id);
        AddHistorySnapshot(history.Undo, snapshot);
        RaiseCommandStates();
    }

    private StepHistory GetStepHistory(Guid scriptId)
    {
        if (!stepHistories.TryGetValue(scriptId, out var history))
        {
            history = new StepHistory();
            stepHistories[scriptId] = history;
        }
        return history;
    }

    private static void AddHistorySnapshot(LinkedList<StepListSnapshot> history, StepListSnapshot snapshot)
    {
        history.AddLast(snapshot);
        while (history.Count > StepHistoryLimit) history.RemoveFirst();
    }

    private static ScriptDefinition SnapshotScript(ScriptDefinition source) => new()
    {
        SchemaVersion = source.SchemaVersion,
        Id = source.Id,
        Name = source.Name,
        DefaultInstanceIndex = source.DefaultInstanceIndex,
        UpdatedAt = source.UpdatedAt,
        Variables = source.Variables.Select(variable => new ScriptVariable
        {
            Name = variable.Name,
            Value = variable.Value,
            IsSecret = variable.IsSecret
        }).ToList(),
        Steps = source.Steps.Select(ScriptCloner.CloneStepPreservingId).ToList()
    };

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
            ScriptStepKind.Hold => new HoldStep
            {
                Id = id ?? Guid.NewGuid(),
                Name = name,
                X = EditorX,
                Y = EditorY,
                DurationMilliseconds = EditorHoldDuration
            },
            ScriptStepKind.Swipe => new SwipeStep { Id = id ?? Guid.NewGuid(), Name = name, X1 = EditorX, Y1 = EditorY, X2 = EditorX2, Y2 = EditorY2, DurationMilliseconds = EditorSwipeDuration },
            ScriptStepKind.InputText => new InputTextStep
            {
                Id = id ?? Guid.NewGuid(),
                Name = name,
                Text = EditorText,
                PressEnterAfterInput = EditorPressEnterAfterInput
            },
            ScriptStepKind.AndroidClipboardPaste => new AndroidClipboardPasteStep
            {
                Id = id ?? Guid.NewGuid(),
                Name = name,
                PressEnterAfterPaste = EditorPressEnterAfterPaste
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
        suppressEditorDirty = true;
        try
        {
            ResetEditorValues();
            EditorKind = step.Kind; EditorName = step.Name; EditorIsEnabled = step.IsEnabled;
            EditorContinueOnError = step.ContinueOnError; EditorTimeoutSeconds = step.TimeoutSeconds;
            switch (step)
            {
                case AndroidShellStep value: EditorCommand = value.Command; break;
                case ForceStopStep value: EditorPackageName = value.PackageName; break;
                case OpenAppStep value: EditorPackageName = value.PackageName; EditorActivityName = value.ActivityName; break;
                case DelayStep value: EditorDelayMilliseconds = value.DurationMilliseconds; break;
                case TapStep value: EditorX = value.X; EditorY = value.Y; break;
                case HoldStep value: EditorX = value.X; EditorY = value.Y; EditorHoldDuration = value.DurationMilliseconds; break;
                case SwipeStep value: EditorX = value.X1; EditorY = value.Y1; EditorX2 = value.X2; EditorY2 = value.Y2; EditorSwipeDuration = value.DurationMilliseconds; break;
                case InputTextStep value:
                    EditorText = value.Text;
                    EditorPressEnterAfterInput = value.PressEnterAfterInput;
                    break;
                case AndroidClipboardPasteStep value: EditorPressEnterAfterPaste = value.PressEnterAfterPaste; break;
                case KeyEventStep value: EditorKey = value.Key; break;
                case NoteStep value: EditorText = value.Text; break;
            }
        }
        finally { suppressEditorDirty = false; }
        DiscardEditorChanges();
    }

    private void ResetEditor()
    {
        suppressEditorDirty = true;
        try { ResetEditorValues(); }
        finally { suppressEditorDirty = false; }
        DiscardEditorChanges();
    }

    private void ResetEditorValues()
    {
        EditorKind = ScriptStepKind.AndroidShell; EditorName = "Bước mới"; EditorIsEnabled = true;
        EditorContinueOnError = false; EditorTimeoutSeconds = 30; EditorCommand = string.Empty;
        EditorPackageName = string.Empty; EditorActivityName = string.Empty; EditorDelayMilliseconds = 1000;
        EditorX = 0; EditorY = 0; EditorHoldDuration = 500; EditorX2 = 0; EditorY2 = 0; EditorSwipeDuration = 300;
        EditorText = string.Empty; EditorPressEnterAfterInput = false; EditorPressEnterAfterPaste = false; EditorKey = AndroidKeyEvent.Home;
    }

    private bool SetEditorProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (!SetProperty(ref field, value, propertyName)) return false;
        if (!suppressEditorDirty)
        {
            editorVersion++;
            SetEditorDirty(true);
        }
        return true;
    }

    private bool ConfirmDiscardEditorChanges(string? propertyName = null)
    {
        if (!IsEditorDirty) return true;
        if (confirmationService.Confirm(
                "Thuộc tính bước có thay đổi chưa lưu. Bạn có muốn bỏ các thay đổi này?",
                "Bỏ thay đổi chưa lưu")) return true;
        if (propertyName is not null) OnPropertyChanged(propertyName);
        RestoreStepSelection(GetSelectedStepsForMutation());
        return false;
    }

    private bool TryDiscardEditorChangesForMutation()
    {
        if (!IsEditorDirty) return true;
        if (!ConfirmDiscardEditorChanges()) return false;
        DiscardEditorChanges();
        return true;
    }

    private void DiscardEditorChanges()
    {
        editorVersion++;
        SetEditorDirty(false);
    }

    private void SetEditorDirty(bool value)
    {
        if (!SetProperty(ref isEditorDirty, value, nameof(IsEditorDirty))) return;
        OnPropertyChanged(nameof(EditorSaveState));
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
        UndoStepListCommand?.RaiseCanExecuteChanged();
        RunCommand?.RaiseCanExecuteChanged(); StopCommand?.RaiseCanExecuteChanged();
        SelectApplicationCommand?.RaiseCanExecuteChanged();
        CaptureTapCommand?.RaiseCanExecuteChanged(); CaptureHoldCommand?.RaiseCanExecuteChanged(); CaptureSwipeCommand?.RaiseCanExecuteChanged();
        ExportSelectedScriptCommand?.RaiseCanExecuteChanged(); ExportAllScriptsCommand?.RaiseCanExecuteChanged(); ImportScriptsCommand?.RaiseCanExecuteChanged();
    }

    public void ReportUnexpectedError(Exception exception) =>
        StatusMessage = $"Thao tác không hoàn tất ({exception.Message}). Hãy kiểm tra dữ liệu hoặc quyền truy cập.";

    private sealed class StepHistory
    {
        public LinkedList<StepListSnapshot> Undo { get; } = [];
    }

    private sealed record StepListSnapshot(
        IReadOnlyList<ScriptStep> Steps,
        IReadOnlyList<Guid> SelectedStepIds,
        Guid? PrimaryStepId);

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
