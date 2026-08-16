using System.Collections.Frozen;
using System.Collections.ObjectModel;
using System.IO;
using System.Runtime.CompilerServices;
using MEmuScriptStudio.App.Services;
using MEmuScriptStudio.Core.Android;
using MEmuScriptStudio.Core.Execution;
using MEmuScriptStudio.Core.Formatting;
using MEmuScriptStudio.Core.MEmu;
using MEmuScriptStudio.Core.Models;
using MEmuScriptStudio.Core.Scripts;
using LaunchSpacingModeValue = MEmuScriptStudio.Core.Models.LaunchSpacingMode;
using ScriptAssignmentModeValue = MEmuScriptStudio.Core.Models.ScriptAssignmentMode;

namespace MEmuScriptStudio.App.ViewModels;

public enum RegularStepEditorMode
{
    None,
    Create,
    Edit
}

public sealed partial class MainViewModel : ObservableObject
{
    public event Action<IReadOnlyList<StepItemViewModel>>? StepSelectionRestoreRequested;
    public event Action<StepItemViewModel>? StepFocusRequested;

    private readonly ISettingsStore settingsStore;
    private readonly IFileDialogService fileDialogService;
    private readonly ScriptStepCommandBuilder stepCommandBuilder;
    private readonly IConfirmationService confirmationService;
    private ApplicationSettings applicationSettings = new();
    private readonly RangeObservableCollection<InstanceTargetItemViewModel> runTargets = [];
    private Guid? configuredCommonScriptId;
    private string statusMessage = "Đang khởi tạo...";
    private ScriptItemViewModel? selectedScript;
    private ScriptItemViewModel? commonRunScript;
    private StepItemViewModel? selectedStep;
    private string commandPreview = "Chọn một bước để xem preview.";

    public MainViewModel(
        IMemuInstanceService instanceService,
        IMemucPathDiscovery pathDiscovery,
        ISettingsStore settingsStore,
        IFileDialogService fileDialogService,
        IScriptStore scriptStore,
        IMultiInstanceExecutionScheduler executionScheduler,
        ScriptStepCommandBuilder stepCommandBuilder,
        IConfirmationService confirmationService,
        IApplicationPickerService applicationPickerService,
        IMemuInputCaptureService inputCaptureService,
        ITapCaptureOverlayService tapCaptureOverlayService,
        ISwipeCaptureOverlayService swipeCaptureOverlayService,
        IScriptTransferService? scriptTransferService = null,
        IScriptImportConflictService? scriptImportConflictService = null,
        IStartupIssueLogger? startupIssueLogger = null,
        IAndroidAdbDeviceService? androidDeviceService = null,
        IAdbPathDiscovery? adbPathDiscovery = null,
        AdbCommandBuilder? adbCommandBuilder = null,
        IAndroidCoordinateCaptureDialogService? androidCoordinateCaptureDialogService = null,
        IAndroidApplicationPickerService? androidApplicationPickerService = null,
        IAndroidDeviceAliasDialogService? androidDeviceAliasDialogService = null)
    {
        this.instanceService = instanceService;
        this.pathDiscovery = pathDiscovery;
        this.settingsStore = settingsStore;
        this.fileDialogService = fileDialogService;
        this.scriptStore = scriptStore;
        this.executionScheduler = executionScheduler;
        this.stepCommandBuilder = stepCommandBuilder;
        this.confirmationService = confirmationService;
        this.applicationPickerService = applicationPickerService;
        this.androidApplicationPickerService = androidApplicationPickerService;
        this.androidDeviceAliasDialogService = androidDeviceAliasDialogService;
        this.inputCaptureService = inputCaptureService;
        this.tapCaptureOverlayService = tapCaptureOverlayService;
        this.swipeCaptureOverlayService = swipeCaptureOverlayService;
        this.scriptTransferService = scriptTransferService;
        this.scriptImportConflictService = scriptImportConflictService;
        this.startupIssueLogger = startupIssueLogger;
        this.androidDeviceService = androidDeviceService;
        this.adbPathDiscovery = adbPathDiscovery;
        this.adbCommandBuilder = adbCommandBuilder;
        this.androidCoordinateCaptureDialogService = androidCoordinateCaptureDialogService;

        BrowseCommand = new AsyncCommand(BrowseAsync, () => !IsBusy && !IsExecuting && !IsCapturing, ReportUnexpectedError);
        BrowseAdbCommand = new AsyncCommand(BrowseAdbAsync, () => !IsBusy && !IsExecuting && !IsCapturing, ReportUnexpectedError);
        RefreshCommand = new AsyncCommand(RefreshAsync, () => CanDiscoverTargets && !IsBusy && !IsCapturing, ReportUnexpectedError);
        EditAndroidDeviceAliasCommand = new AsyncCommand(EditAndroidDeviceAliasAsync, CanEditAndroidDeviceAlias, ReportUnexpectedError);
        CreateScriptCommand = new AsyncCommand(CreateScriptAsync,
            () => !IsCapturing && !IsScriptPersistenceBlocked, ReportUnexpectedError);
        RenameScriptCommand = new AsyncCommand(RenameScriptAsync,
            () => SelectedScript is not null && !IsCapturing && CanRenameScript,
            ReportUnexpectedError);
        DuplicateScriptCommand = new AsyncCommand(DuplicateScriptAsync,
            () => HasSelectedScriptsForMutation() && !IsCapturing && !IsScriptPersistenceBlocked, ReportUnexpectedError);
        DeleteScriptCommand = new AsyncCommand(DeleteScriptAsync,
            () => HasSelectedScriptsForMutation() && !IsCapturing && !IsScriptPersistenceBlocked, ReportUnexpectedError);
        NewStepCommand = new AsyncCommand(PrepareNewStepAsync, () => SelectedScript is not null && CanMutateSteps);
        AddStepCommand = new AsyncCommand(AddStepAsync, CanAddStep, ReportUnexpectedError);
        SaveStepCommand = new AsyncCommand(SaveStepAsync, CanSaveStep, ReportUnexpectedError);
        TestStepCommand = new AsyncCommand(TestCurrentStepAsync, CanTestCurrentStep, ReportUnexpectedError);
        CancelStepCreateCommand = new RelayCommand(CancelStepCreate,
            () => StepEditorMode == RegularStepEditorMode.Create && CanChangeSelection && !IsEditorPersistenceBusy);
        CancelScriptRenameCommand = new RelayCommand(CancelScriptRename, () => IsScriptNameDirty);
        DuplicateStepCommand = new AsyncCommand(DuplicateStepAsync, () => SelectedStepCount > 0 && CanMutateSteps, ReportUnexpectedError);
        DeleteStepCommand = new AsyncCommand(DeleteStepAsync, () => SelectedStepCount > 0 && CanMutateSteps, ReportUnexpectedError);
        MoveStepUpCommand = new AsyncCommand(() => MoveStepAsync(-1), () => CanMoveStep(-1), ReportUnexpectedError);
        MoveStepDownCommand = new AsyncCommand(() => MoveStepAsync(1), () => CanMoveStep(1), ReportUnexpectedError);
        UndoStepListCommand = new AsyncCommand(RestoreStepHistoryAsync, CanUndoStepList, ReportUnexpectedError);
        CopyStepsCommand = new RelayCommand(CopySelectedSteps, () => SelectedStepCount > 0 && CanChangeSelection);
        PasteStepsCommand = new AsyncCommand(PasteCopiedStepsAsync,
            () => SelectedScript is not null && HasCopiedSteps && CanMutateSteps, ReportUnexpectedError);
        RunCommand = new AsyncCommand(RunAsync, CanRun, ReportUnexpectedError);
        RunAllRemainingCommand = new AsyncCommand(RunAllRemainingAsync, CanRunAllRemaining, ReportUnexpectedError);
        StopCommand = new RelayCommand(Stop, () => IsExecuting);
        StopSelectedActiveInstancesCommand = new RelayCommand(StopSelectedActiveInstances,
            () => ActiveInstanceRuns.Any(item => item.IsSelected && item.CanStop));
        StopGroupCommand = new RelayCommand<Guid>(StopGroup, groupId => executionSessions.ContainsKey(groupId));
        ClearLatestRunResultCommand = new RelayCommand(ClearLatestRunResult, () => LatestRunResult is not null);
        SelectApplicationCommand = new AsyncCommand(SelectApplicationAsync, CanSelectApplication, ReportUnexpectedError);
        CaptureTapCommand = new AsyncCommand(CaptureTapAsync, () => CanCapture(ScriptStepKind.Tap), ReportUnexpectedError);
        CaptureHoldCommand = new AsyncCommand(CaptureHoldAsync, () => CanCapture(ScriptStepKind.Hold), ReportUnexpectedError);
        CaptureSwipeCommand = new AsyncCommand(CaptureSwipeAsync, () => CanCapture(ScriptStepKind.Swipe), ReportUnexpectedError);
        ExportSelectedScriptCommand = new AsyncCommand(ExportSelectedScriptAsync,
            () => scriptTransferService is not null && SelectedScript is not null && CanChangeSelection, ReportUnexpectedError);
        ExportAllScriptsCommand = new AsyncCommand(ExportAllScriptsAsync,
            () => scriptTransferService is not null && Scripts.Count > 0 && CanChangeSelection, ReportUnexpectedError);
        ImportScriptsCommand = new AsyncCommand(ImportScriptsAsync,
            () => scriptTransferService is not null && scriptImportConflictService is not null &&
                  CanChangeSelection && !IsScriptPersistenceBlocked, ReportUnexpectedError);
        InitializeCompositeWorkspace();
        InitializeWorkspaceCommands();
        InitializeControlCenterOperations();
    }

    public ObservableCollection<MemuInstance> Instances { get; } = [];
    public ObservableCollection<EditorTargetItemViewModel> EditorTargets { get; } = [];
    public ObservableCollection<InstanceTargetItemViewModel> RunTargets => runTargets;
    public ObservableCollection<InstanceRunItemViewModel> ActiveInstanceRuns => activeInstanceRuns;
    public IReadOnlyList<InstanceRunItemViewModel> InstanceRuns => ActiveInstanceRuns;
    public ObservableCollection<LaunchGroupItemViewModel> ActiveLaunchGroups { get; } = [];
    public ObservableCollection<ScriptItemViewModel> Scripts { get; } = [];
    public ObservableCollection<StepItemViewModel> Steps { get; } = [];
    public IReadOnlyList<StepItemViewModel> SelectedSteps => selectedSteps;
    public int SelectedStepCount => selectedSteps.Count;
    public bool HasCopiedSteps => copiedSteps.Count > 0;
    public int CopiedStepCount => copiedSteps.Count;
    public string? CopiedFromScriptName => copiedFromScriptName;
    public string StepClipboardSummary => HasCopiedSteps
        ? $"Clipboard: {CopiedStepCount} bước từ “{CopiedFromScriptName ?? "Kịch bản không tên"}”"
        : "Clipboard: trống";
    public bool IsEditorDirty => isEditorDirty;
    public bool HasAnyEditorDraft => HasRegularEditorDraft || HasCompositeEditorDraft ||
                                     IsScriptNameDirty || IsEditorPersistenceBusy;
    public string EditorSaveState => HasAnyEditorDraft ? "Có thay đổi chưa lưu" : "Đã lưu";
    public RegularStepEditorMode StepEditorMode
    {
        get => stepEditorMode;
        private set
        {
            if (!SetProperty(ref stepEditorMode, value)) return;
            OnPropertyChanged(nameof(IsStepEditorNone));
            OnPropertyChanged(nameof(IsStepEditorCreate));
            OnPropertyChanged(nameof(IsStepEditorEdit));
            OnPropertyChanged(nameof(ShowRegularStepEditor));
            OnPropertyChanged(nameof(ShowRegularEmptyState));
            OnPropertyChanged(nameof(ShowRegularSaveButton));
            OnPropertyChanged(nameof(ShowRegularAddButtons));
            OnPropertyChanged(nameof(StepKinds));
            OnPropertyChanged(nameof(HasAnyEditorDraft));
            OnPropertyChanged(nameof(EditorSaveState));
            OnPropertyChanged(nameof(RunConfigurationError));
            UpdatePreview();
            RaiseCommandStates();
        }
    }
    public bool IsStepEditorNone => StepEditorMode == RegularStepEditorMode.None;
    public bool IsStepEditorCreate => StepEditorMode == RegularStepEditorMode.Create;
    public bool IsStepEditorEdit => StepEditorMode == RegularStepEditorMode.Edit;
    public bool ShowRegularStepEditor => !IsStepEditorNone;
    public bool ShowRegularEmptyState => IsStepEditorNone;
    public bool ShowRegularSaveButton => IsStepEditorEdit;
    public bool ShowRegularAddButtons => IsStepEditorCreate;
    public IReadOnlyList<ScriptStepKind> StepKinds =>
        IsStepEditorEdit && EditorKind == ScriptStepKind.AndroidShell
            ? AllStepKinds
            : AuthorableStepKinds;
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
    public AsyncCommand BrowseAdbCommand { get; }
    public AsyncCommand RefreshCommand { get; }
    public AsyncCommand EditAndroidDeviceAliasCommand { get; }
    public AsyncCommand CreateScriptCommand { get; }
    public AsyncCommand RenameScriptCommand { get; }
    public AsyncCommand DuplicateScriptCommand { get; }
    public AsyncCommand DeleteScriptCommand { get; }
    public AsyncCommand NewStepCommand { get; }
    public AsyncCommand AddStepCommand { get; }
    public AsyncCommand SaveStepCommand { get; }
    public AsyncCommand TestStepCommand { get; }
    public RelayCommand CancelStepCreateCommand { get; }
    public RelayCommand CancelScriptRenameCommand { get; }
    public AsyncCommand DuplicateStepCommand { get; }
    public AsyncCommand DeleteStepCommand { get; }
    public AsyncCommand MoveStepUpCommand { get; }
    public AsyncCommand MoveStepDownCommand { get; }
    public AsyncCommand UndoStepListCommand { get; }
    public RelayCommand CopyStepsCommand { get; }
    public AsyncCommand PasteStepsCommand { get; }
    public AsyncCommand RunCommand { get; }
    public AsyncCommand RunAllRemainingCommand { get; }
    public RelayCommand StopCommand { get; }
    public RelayCommand StopSelectedActiveInstancesCommand { get; }
    public RelayCommand<Guid> StopGroupCommand { get; }
    public RelayCommand ClearLatestRunResultCommand { get; }
    public AsyncCommand SelectApplicationCommand { get; }
    public AsyncCommand CaptureTapCommand { get; }
    public AsyncCommand CaptureHoldCommand { get; }
    public AsyncCommand CaptureSwipeCommand { get; }
    public AsyncCommand ExportSelectedScriptCommand { get; }
    public AsyncCommand ExportAllScriptsCommand { get; }
    public AsyncCommand ImportScriptsCommand { get; }

    public string MemucPath { get => memucPath; private set { if (SetProperty(ref memucPath, value)) { OnPropertyChanged(nameof(IsPathValid)); OnPropertyChanged(nameof(CanUseMemuControls)); OnPropertyChanged(nameof(CanDiscoverTargets)); OnPropertyChanged(nameof(CanSelectEditorTarget)); OnPropertyChanged(nameof(MemucConnectionStatus)); UpdatePreview(); RaiseCommandStates(); } } }
    public string AdbPath
    {
        get => adbPath;
        private set
        {
            if (!SetProperty(ref adbPath, value)) return;
            OnPropertyChanged(nameof(IsAdbPathValid));
            OnPropertyChanged(nameof(CanDiscoverTargets));
            OnPropertyChanged(nameof(CanSelectEditorTarget));
            OnPropertyChanged(nameof(AdbConnectionStatus));
            UpdatePreview();
            RaiseCommandStates();
        }
    }
    public string StatusMessage { get => statusMessage; private set => SetProperty(ref statusMessage, value); }
    public bool IsInitializing
    {
        get => isInitializing;
        private set
        {
            if (!SetProperty(ref isInitializing, value)) return;
            OnPropertyChanged(nameof(CanUseMemuControls));
            OnPropertyChanged(nameof(CanDiscoverTargets));
            OnPropertyChanged(nameof(CanSelectEditorTarget));
            OnPropertyChanged(nameof(CanChangeSelection));
            RaiseCommandStates();
        }
    }
    public string? InitializationErrorMessage
    {
        get => initializationErrorMessage;
        private set
        {
            if (!SetProperty(ref initializationErrorMessage, value)) return;
            OnPropertyChanged(nameof(HasInitializationError));
            OnPropertyChanged(nameof(CanUseMemuControls));
            OnPropertyChanged(nameof(CanDiscoverTargets));
            OnPropertyChanged(nameof(CanSelectEditorTarget));
            OnPropertyChanged(nameof(CanChangeSelection));
            RaiseCommandStates();
        }
    }
    public bool HasInitializationError => !string.IsNullOrWhiteSpace(InitializationErrorMessage);
    public bool CanUseMemuControls => !IsInitializing && !HasInitializationError && IsPathValid;
    public bool IsPathValid => pathDiscovery.IsValidMemucPath(MemucPath);
    public bool IsAdbPathValid => adbPathDiscovery?.IsValidAdbPath(AdbPath) == true;
    public bool CanDiscoverTargets => !IsInitializing && !HasInitializationError && (IsPathValid || IsAdbPathValid);
    public bool CanSelectEditorTarget => CanDiscoverTargets && CanChangeSelection && !IsBusy;
    public string MemucConnectionStatus => IsPathValid ? "MEMUC sẵn sàng" : "Chưa cấu hình MEMUC";
    public string AdbConnectionStatus => IsAdbPathValid ? "ADB sẵn sàng" : "Chưa cấu hình ADB";
    public bool ShowAndroidDeviceAliasAction => SelectedEditorTarget?.Model is AndroidAdbDevice;
    public bool IsBusy
    {
        get => isBusy;
        private set
        {
            if (!SetProperty(ref isBusy, value)) return;
            OnPropertyChanged(nameof(CanSelectEditorTarget));
            RaiseCommandStates();
        }
    }
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
            OnPropertyChanged(nameof(CanSelectEditorTarget));
            RaiseCommandStates();
        }
    }
    public bool CanChangeSelection => !IsCapturing;
    public bool IsScriptPersistenceBlocked => isScriptPersistenceBlocked;
    private bool CanMutateSteps => CanChangeSelection && !IsEditorPersistenceBusy && !isStepMutationBusy &&
        !IsScriptPersistenceBlocked && SelectedScript?.Model.Kind == ScriptKind.Regular;

    public ScriptItemViewModel? SelectedScript
    {
        get => selectedScript;
        set
        {
            if (!CanChangeSelection && !IsInitializing && value != selectedScript) return;
            if (!IsInitializing && value != selectedScript && HasAnyEditorDraft)
            {
                OnPropertyChanged(nameof(SelectedScript));
                return;
            }
            if (!SetProperty(ref selectedScript, value)) return;
            if (!synchronizingSelectedScripts)
                ReplaceSelectedScripts(value is null ? [] : [value]);
            DiscardEditorChanges();
            SetScriptNameFromModel(value?.Name ?? string.Empty);
            Steps.Clear();
            if (value is not null)
            {
                foreach (var step in value.Model.Steps) Steps.Add(CreateStepItem(step));
            }
            var firstStep = Steps.FirstOrDefault();
            SetStepSelection(firstStep is null ? [] : [firstStep], firstStep);
            LoadCompositeWorkspace();
            OnPropertyChanged(nameof(IsRegularScriptSelected));
            OnPropertyChanged(nameof(IsCompositeScriptSelected));
            RaiseCommandStates();
        }
    }

    public ScriptItemViewModel? CommonRunScript
    {
        get => commonRunScript;
        set
        {
            if (!SetProperty(ref commonRunScript, value)) return;
            configuredCommonScriptId = value?.Id;
            UpdateRunConfigurationState();
            if (!IsInitializing) PersistCommonRunScriptSelection();
        }
    }

    public StepItemViewModel? SelectedStep
    {
        get => selectedStep;
        set
        {
            if (!CanChangeSelection && !IsInitializing && value != selectedStep) return;
            if (!IsInitializing && value != selectedStep && (HasRegularEditorDraft || IsEditorPersistenceBusy)) return;
            var previous = selectedStep;
            if (!SetProperty(ref selectedStep, value)) return;
            previous?.ClearDraftPreview();
            if (!synchronizingSelectedSteps)
                ReplaceSelectedSteps(value is null ? [] : [value]);
            if (value is null)
            {
                ResetEditor();
                StepEditorMode = RegularStepEditorMode.None;
            }
            else
            {
                LoadEditor(value.Model);
                StepEditorMode = RegularStepEditorMode.Edit;
            }
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
            if (!SetProperty(ref selectedInstance, value)) return;
            if (value is not null)
            {
                var editorTarget = EditorTargets.FirstOrDefault(item => item.TargetKey == value.TargetKey);
                if (editorTarget is null)
                {
                    editorTarget = new EditorTargetItemViewModel(value);
                    EditorTargets.Add(editorTarget);
                }
                SetSelectedEditorTarget(editorTarget);
            }
            else if (SelectedEditorTarget?.DeviceKind == DeviceKind.MEmu)
                SetSelectedEditorTarget(null);
            UpdatePreview();
            RaiseCommandStates();
        }
    }

    public EditorTargetItemViewModel? SelectedEditorTarget
    {
        get => selectedEditorTarget;
        set
        {
            if (!CanChangeSelection && value != selectedEditorTarget) return;
            SetSelectedEditorTarget(value);
        }
    }

    private void SetSelectedEditorTarget(EditorTargetItemViewModel? value)
    {
        SetProperty(ref selectedEditorTarget, value, nameof(SelectedEditorTarget));
        OnPropertyChanged(nameof(ShowAndroidDeviceAliasAction));
        var memu = value?.Model as MemuInstance;
        if (!ReferenceEquals(selectedInstance, memu))
        {
            selectedInstance = memu;
            OnPropertyChanged(nameof(SelectedInstance));
        }
        UpdatePreview();
        RaiseCommandStates();
    }

    public LatestRunResultViewModel? LatestRunResult
    {
        get => latestRunResult;
        private set
        {
            if (!SetProperty(ref latestRunResult, value)) return;
            OnPropertyChanged(nameof(HasLatestRunResult));
            OnPropertyChanged(nameof(HasNoLatestRunResult));
            ClearLatestRunResultCommand.RaiseCanExecuteChanged();
        }
    }

    public bool HasLatestRunResult => LatestRunResult is not null;
    public bool HasNoLatestRunResult => LatestRunResult is null;

    public LaunchSpacingModeValue LaunchSpacingMode
    {
        get => launchSpacingMode;
        set
        {
            if (!SetProperty(ref launchSpacingMode, value)) return;
            OnPropertyChanged(nameof(IsFixedSpacing));
            OnPropertyChanged(nameof(IsRandomSpacing));
            UpdateRunConfigurationState();
        }
    }

    public bool IsFixedSpacing
    {
        get => LaunchSpacingMode == LaunchSpacingModeValue.Fixed;
        set { if (value) LaunchSpacingMode = LaunchSpacingModeValue.Fixed; }
    }

    public bool IsRandomSpacing
    {
        get => LaunchSpacingMode == LaunchSpacingModeValue.Random;
        set { if (value) LaunchSpacingMode = LaunchSpacingModeValue.Random; }
    }

    public int FixedSpacingMilliseconds
    {
        get => fixedSpacingMilliseconds;
        set { if (SetProperty(ref fixedSpacingMilliseconds, value)) UpdateRunConfigurationState(); }
    }

    public int RandomMinimumSpacingMilliseconds
    {
        get => randomMinimumSpacingMilliseconds;
        set { if (SetProperty(ref randomMinimumSpacingMilliseconds, value)) UpdateRunConfigurationState(); }
    }

    public int RandomMaximumSpacingMilliseconds
    {
        get => randomMaximumSpacingMilliseconds;
        set { if (SetProperty(ref randomMaximumSpacingMilliseconds, value)) UpdateRunConfigurationState(); }
    }

    public bool IsFixedSpacingInputValid
    {
        get => isFixedSpacingInputValid;
        set { if (SetProperty(ref isFixedSpacingInputValid, value)) UpdateRunConfigurationState(); }
    }

    public bool IsRandomMinimumSpacingInputValid
    {
        get => isRandomMinimumSpacingInputValid;
        set { if (SetProperty(ref isRandomMinimumSpacingInputValid, value)) UpdateRunConfigurationState(); }
    }

    public bool IsRandomMaximumSpacingInputValid
    {
        get => isRandomMaximumSpacingInputValid;
        set { if (SetProperty(ref isRandomMaximumSpacingInputValid, value)) UpdateRunConfigurationState(); }
    }

    public bool StopAllOnInvalidTarget
    {
        get => stopAllOnInvalidTarget;
        set => SetProperty(ref stopAllOnInvalidTarget, value);
    }

    public int SelectedRunTargetCount => RunTargets.Count(item => item.IsSelected && item.CanSelectForRun);
    public int RunningInstanceCount => runningInstanceCount;
    public int WaitingInstanceCount => waitingInstanceCount;
    public int ActiveLaunchGroupCount => executionSessions.Count;
    public string? RunConfigurationError => ValidateRunConfiguration();

    public string ScriptName
    {
        get => scriptName;
        set
        {
            if (!SetProperty(ref scriptName, value)) return;
            OnPropertyChanged(nameof(IsScriptNameDirty));
            OnPropertyChanged(nameof(IsScriptNameValid));
            OnPropertyChanged(nameof(CanRenameScript));
            OnPropertyChanged(nameof(HasAnyEditorDraft));
            OnPropertyChanged(nameof(EditorSaveState));
            RaiseCommandStates();
        }
    }
    public bool IsScriptNameDirty => !suppressScriptNameDirty && SelectedScript is not null &&
        !string.Equals(NormalizeScriptName(ScriptName), NormalizeScriptName(scriptNameBaseline), StringComparison.Ordinal);
    public bool IsScriptNameValid => !string.IsNullOrWhiteSpace(ScriptName);
    public bool CanRenameScript => IsScriptNameValid && SelectedScript is not null &&
                                   IsScriptNameDirty && !IsEditorPersistenceBusy && !IsScriptPersistenceBlocked;
    public string CommandPreview { get => commandPreview; private set => SetProperty(ref commandPreview, value); }
    public ScriptStepKind EditorKind
    {
        get => editorKind;
        set
        {
            if (!CanChangeSelection && !IsInitializing && !suppressEditorDirty && value != editorKind) return;
            if (!suppressEditorDirty && value != editorKind && HasInvalidRegularEditorDraft)
            {
                OnPropertyChanged(nameof(EditorKind));
                return;
            }
            var previousKind = editorKind;
            if (!SetEditorProperty(ref editorKind, value)) return;
            if (!suppressEditorDirty && value != previousKind)
                EditorName = ScriptStepDisplayName.GetDefaultName(value);
            UpdateSelectedStepDraftPreview();
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
            OnPropertyChanged(nameof(ShowStepName));
            OnPropertyChanged(nameof(ShowRegularSaveButton));
            OnPropertyChanged(nameof(StepKinds));
            RaiseCommandStates();
        }
    }
    public bool ShowStepName => EditorKind != ScriptStepKind.Delay;
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
    public string EditorApplicationDisplayName
    {
        get => editorApplicationDisplayName;
        set
        {
            if (SetEditorProperty(ref editorApplicationDisplayName, value))
                OnPropertyChanged(nameof(EditorApplicationDisplayText));
        }
    }
    public string EditorApplicationDisplayText => string.IsNullOrWhiteSpace(EditorApplicationDisplayName)
        ? "Không xác định"
        : EditorApplicationDisplayName.Trim();
    public string EditorPackageName { get => editorPackageName; set => SetEditorProperty(ref editorPackageName, value); }
    public string EditorActivityName { get => editorActivityName; set => SetEditorProperty(ref editorActivityName, value); }
    public int EditorDelayMilliseconds
    {
        get => editorDelayMilliseconds;
        set
        {
            if (!SetEditorProperty(ref editorDelayMilliseconds, value)) return;
            UpdateSelectedStepDraftPreview();
        }
    }
    public bool IsEditorDelayInputValid
    {
        get => isEditorDelayInputValid;
        set
        {
            if (!SetProperty(ref isEditorDelayInputValid, value)) return;
            if (!suppressEditorDirty && StepEditorMode != RegularStepEditorMode.None)
            {
                editorVersion++;
                RefreshRegularEditorDirty();
            }
            OnPropertyChanged(nameof(HasRegularEditorDraft));
            OnPropertyChanged(nameof(HasAnyEditorDraft));
            OnPropertyChanged(nameof(EditorSaveState));
            UpdatePreview();
            RaiseCommandStates();
        }
    }
    public long EditorDelayInputRefreshToken => editorDelayInputRefreshToken;
    public bool HasEditorBindingErrors
    {
        get => hasEditorBindingErrors;
        set
        {
            if (!SetProperty(ref hasEditorBindingErrors, value)) return;
            OnPropertyChanged(nameof(HasRegularEditorDraft));
            OnPropertyChanged(nameof(HasCompositeEditorDraft));
            OnPropertyChanged(nameof(HasAnyEditorDraft));
            OnPropertyChanged(nameof(EditorSaveState));
            OnPropertyChanged(nameof(RunConfigurationError));
            UpdatePreview();
            RaiseCommandStates();
        }
    }
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

    private void UpdatePreview()
    {
        if (StepEditorMode == RegularStepEditorMode.None)
        {
            CommandPreview = "Chọn một bước để xem preview.";
            return;
        }

        if (HasInvalidRegularEditorDraft)
        {
            CommandPreview = "Không thể xem trước: dữ liệu bước đang không hợp lệ.";
            return;
        }

        try
        {
            var draft = CreateStep(SelectedStep?.Id);
            if (SelectedStep is not null && StepEditorMode == RegularStepEditorMode.Edit)
                draft.IsEnabled = SelectedStep.IsEnabled;
            stepCommandBuilder.Validate(draft);
            var previewTarget = SelectedEditorTarget?.Model;
            CommandPreview = previewTarget switch
            {
                AndroidAdbDevice android => (adbCommandBuilder ??
                    throw new InvalidOperationException("ADB command builder chưa được cấu hình."))
                    .BuildPreview(draft, IsAdbPathValid ? AdbPath : null, android.Serial),
                MemuInstance memu => stepCommandBuilder.BuildPreview(
                    draft, IsPathValid ? MemucPath : null, memu.Index),
                _ => stepCommandBuilder.BuildPreview(
                    draft, IsPathValid ? MemucPath : null, SelectedInstance?.Index)
            };
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or NotSupportedException)
        {
            CommandPreview = "Không thể xem trước: dữ liệu bước đang không hợp lệ.";
        }
    }

    private void RaiseCommandStates()
    {
        BrowseCommand?.RaiseCanExecuteChanged(); BrowseAdbCommand?.RaiseCanExecuteChanged(); RefreshCommand?.RaiseCanExecuteChanged(); EditAndroidDeviceAliasCommand?.RaiseCanExecuteChanged();
        CreateScriptCommand?.RaiseCanExecuteChanged(); RenameScriptCommand?.RaiseCanExecuteChanged();
        DuplicateScriptCommand?.RaiseCanExecuteChanged(); DeleteScriptCommand?.RaiseCanExecuteChanged();
        NewStepCommand?.RaiseCanExecuteChanged(); AddStepCommand?.RaiseCanExecuteChanged(); SaveStepCommand?.RaiseCanExecuteChanged();
        CancelStepCreateCommand?.RaiseCanExecuteChanged(); CancelScriptRenameCommand?.RaiseCanExecuteChanged();
        DuplicateStepCommand?.RaiseCanExecuteChanged(); DeleteStepCommand?.RaiseCanExecuteChanged();
        MoveStepUpCommand?.RaiseCanExecuteChanged(); MoveStepDownCommand?.RaiseCanExecuteChanged();
        UndoStepListCommand?.RaiseCanExecuteChanged();
        CopyStepsCommand?.RaiseCanExecuteChanged(); PasteStepsCommand?.RaiseCanExecuteChanged();
        RunCommand?.RaiseCanExecuteChanged(); RunAllRemainingCommand?.RaiseCanExecuteChanged(); TestStepCommand?.RaiseCanExecuteChanged(); StopCommand?.RaiseCanExecuteChanged(); StopSelectedActiveInstancesCommand?.RaiseCanExecuteChanged(); StopGroupCommand?.RaiseCanExecuteChanged();
        SelectApplicationCommand?.RaiseCanExecuteChanged();
        CaptureTapCommand?.RaiseCanExecuteChanged(); CaptureHoldCommand?.RaiseCanExecuteChanged(); CaptureSwipeCommand?.RaiseCanExecuteChanged();
        ExportSelectedScriptCommand?.RaiseCanExecuteChanged(); ExportAllScriptsCommand?.RaiseCanExecuteChanged(); ImportScriptsCommand?.RaiseCanExecuteChanged();
        RaiseCompositeCommandStates();
        RaiseWorkspaceCommandStates();
    }

}
