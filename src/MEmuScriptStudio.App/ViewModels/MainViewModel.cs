using System.Collections.Frozen;
using System.Collections.ObjectModel;
using System.IO;
using System.Runtime.CompilerServices;
using MEmuScriptStudio.App.Services;
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
    private const int StepHistoryLimit = 50;
    private const int LatestRunMessageLimit = 240;
    private const int LatestRunDescriptionLimit = 240;
    private const int RunDescriptionScriptNameLimit = 48;
    private const int RunDescriptionVisibleScriptLimit = 3;

    public event Action<IReadOnlyList<StepItemViewModel>>? StepSelectionRestoreRequested;

    private readonly IMemuInstanceService instanceService;
    private readonly IMemucPathDiscovery pathDiscovery;
    private readonly ISettingsStore settingsStore;
    private readonly IFileDialogService fileDialogService;
    private readonly IScriptStore scriptStore;
    private readonly IMultiInstanceExecutionScheduler executionScheduler;
    private readonly ScriptStepCommandBuilder stepCommandBuilder;
    private readonly IConfirmationService confirmationService;
    private readonly IApplicationPickerService applicationPickerService;
    private readonly IMemuInputCaptureService inputCaptureService;
    private readonly ITapCaptureOverlayService tapCaptureOverlayService;
    private readonly ISwipeCaptureOverlayService swipeCaptureOverlayService;
    private readonly IScriptTransferService? scriptTransferService;
    private readonly IScriptImportConflictService? scriptImportConflictService;
    private readonly IStartupIssueLogger? startupIssueLogger;
    private readonly List<StepItemViewModel> selectedSteps = [];
    private readonly Dictionary<Guid, StepHistory> stepHistories = [];
    private readonly SemaphoreSlim scriptSaveGate = new(1, 1);
    private IReadOnlyList<ScriptStep> copiedSteps = [];
    private string? copiedFromScriptName;
    private ApplicationSettings applicationSettings = new();
    private readonly Dictionary<Guid, MultiInstanceExecutionSession> executionSessions = [];
    private readonly Dictionary<int, Guid> activeInstanceGroups = [];
    private readonly Dictionary<(Guid LaunchGroupId, int InstanceIndex), InstanceRunItemViewModel> instanceRunsByKey = [];
    private TaskCompletionSource? executionTerminalCompletion;
    private readonly HashSet<int> dynamicSessionUniverse = [];
    private readonly HashSet<int> dynamicSessionAdmitted = [];
    private Guid? configuredCommonScriptId;
    private int launchGroupSequence;
    private int runningInstanceCount;
    private int waitingInstanceCount;
    private string memucPath = string.Empty;
    private string statusMessage = "Đang khởi tạo...";
    private bool isInitializing = true;
    private string? initializationErrorMessage;
    private bool isBusy;
    private bool isExecuting;
    private bool isSafeShutdownRequested;
    private bool isCapturing;
    private ScriptItemViewModel? selectedScript;
    private ScriptItemViewModel? commonRunScript;
    private StepItemViewModel? selectedStep;
    private MemuInstance? selectedInstance;
    private LatestRunResultViewModel? latestRunResult;
    private LaunchSpacingModeValue launchSpacingMode = LaunchSpacingModeValue.Fixed;
    private int fixedSpacingMilliseconds;
    private int randomMinimumSpacingMilliseconds;
    private int randomMaximumSpacingMilliseconds;
    private bool isFixedSpacingInputValid = true;
    private bool isRandomMinimumSpacingInputValid = true;
    private bool isRandomMaximumSpacingInputValid = true;
    private bool stopAllOnInvalidTarget;
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
    private bool isEditorDelayInputValid = true;
    private bool hasEditorBindingErrors;
    private long editorDelayInputRefreshToken;
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
    private StepMutationTransaction? pendingToggleTransaction;
    private bool isScriptPersistenceBlocked;
    private bool isEditorDirty;
    private long editorVersion;
    private RegularStepEditorMode stepEditorMode;
    private CancellationTokenSource? regularDelayAutosaveCancellation;
    private Task regularDelayAutosaveTask = Task.CompletedTask;
    private bool suppressScriptNameDirty;
    private readonly SynchronizationContext? editorSynchronizationContext = SynchronizationContext.Current;

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
        IStartupIssueLogger? startupIssueLogger = null)
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
        this.inputCaptureService = inputCaptureService;
        this.tapCaptureOverlayService = tapCaptureOverlayService;
        this.swipeCaptureOverlayService = swipeCaptureOverlayService;
        this.scriptTransferService = scriptTransferService;
        this.scriptImportConflictService = scriptImportConflictService;
        this.startupIssueLogger = startupIssueLogger;

        BrowseCommand = new AsyncCommand(BrowseAsync, () => !IsBusy && !IsExecuting && !IsCapturing, ReportUnexpectedError);
        RefreshCommand = new AsyncCommand(RefreshAsync, () => CanUseMemuControls && !IsBusy && !IsCapturing && IsPathValid, ReportUnexpectedError);
        CreateScriptCommand = new AsyncCommand(CreateScriptAsync,
            () => !IsCapturing && !IsScriptPersistenceBlocked, ReportUnexpectedError);
        RenameScriptCommand = new AsyncCommand(RenameScriptAsync,
            () => SelectedScript is not null && !IsCapturing && CanRenameScript,
            ReportUnexpectedError);
        DuplicateScriptCommand = new AsyncCommand(DuplicateScriptAsync,
            () => SelectedScript is not null && !IsCapturing && !IsScriptPersistenceBlocked, ReportUnexpectedError);
        DeleteScriptCommand = new AsyncCommand(DeleteScriptAsync,
            () => SelectedScript is not null && !IsCapturing && !IsScriptPersistenceBlocked, ReportUnexpectedError);
        NewStepCommand = new AsyncCommand(PrepareNewStepAsync, () => SelectedScript is not null && CanMutateSteps);
        AddStepCommand = new AsyncCommand(AddStepAsync, CanAddStep, ReportUnexpectedError);
        SaveStepCommand = new AsyncCommand(SaveStepAsync, CanSaveStep, ReportUnexpectedError);
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
    public ObservableCollection<InstanceTargetItemViewModel> RunTargets { get; } = [];
    public ObservableCollection<InstanceRunItemViewModel> ActiveInstanceRuns { get; } = [];
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
    public bool ShowRegularSaveButton => IsStepEditorEdit &&
        !(SelectedStep?.Model is DelayStep && EditorKind == ScriptStepKind.Delay);
    public bool ShowRegularAddButtons => IsStepEditorCreate;
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
    public AsyncCommand NewStepCommand { get; }
    public AsyncCommand AddStepCommand { get; }
    public AsyncCommand SaveStepCommand { get; }
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

    public string MemucPath { get => memucPath; private set { if (SetProperty(ref memucPath, value)) { OnPropertyChanged(nameof(IsPathValid)); OnPropertyChanged(nameof(CanUseMemuControls)); OnPropertyChanged(nameof(MemucConnectionStatus)); UpdatePreview(); RaiseCommandStates(); } } }
    public string StatusMessage { get => statusMessage; private set => SetProperty(ref statusMessage, value); }
    public bool IsInitializing
    {
        get => isInitializing;
        private set
        {
            if (!SetProperty(ref isInitializing, value)) return;
            OnPropertyChanged(nameof(CanUseMemuControls));
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
            OnPropertyChanged(nameof(CanChangeSelection));
            RaiseCommandStates();
        }
    }
    public bool HasInitializationError => !string.IsNullOrWhiteSpace(InitializationErrorMessage);
    public bool CanUseMemuControls => !IsInitializing && !HasInitializationError && IsPathValid;
    public bool IsPathValid => pathDiscovery.IsValidMemucPath(MemucPath);
    public string MemucConnectionStatus => IsPathValid ? "MEMUC sẵn sàng" : "Chưa cấu hình MEMUC";
    public bool IsBusy
    {
        get => isBusy;
        private set
        {
            if (!SetProperty(ref isBusy, value)) return;
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
            if (SetProperty(ref selectedInstance, value)) { UpdatePreview(); RaiseCommandStates(); }
        }
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
    public string EditorPackageName { get => editorPackageName; set => SetEditorProperty(ref editorPackageName, value); }
    public string EditorActivityName { get => editorActivityName; set => SetEditorProperty(ref editorActivityName, value); }
    public int EditorDelayMilliseconds
    {
        get => editorDelayMilliseconds;
        set
        {
            if (!SetEditorProperty(ref editorDelayMilliseconds, value)) return;
            UpdateSelectedStepDraftPreview();
            ScheduleRegularDelayAutosave();
        }
    }
    public bool IsEditorDelayInputValid
    {
        get => isEditorDelayInputValid;
        set
        {
            if (!SetProperty(ref isEditorDelayInputValid, value)) return;
            if (!value) CancelRegularDelayAutosave();
            if (!suppressEditorDirty && StepEditorMode != RegularStepEditorMode.None)
            {
                editorVersion++;
                RefreshRegularEditorDirty();
            }
            if (value) ScheduleRegularDelayAutosave();
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

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        IsInitializing = true;
        InitializationErrorMessage = null;
        StatusMessage = "Đang khởi tạo...";
        try
        {
            await InitializeMemuAsync(cancellationToken);
            try
            {
                await scriptSaveGate.WaitAsync(cancellationToken);
                try
                {
                    IReadOnlyList<ScriptDefinition> loaded;
                    try { loaded = await scriptStore.LoadAsync(cancellationToken); }
                    catch (ScriptDataRecoveryRequiredException exception)
                    {
                        LogInitializationIssue(exception);
                        SetScriptPersistenceBlocked(true);
                        var recover = confirmationService.Confirm(
                            $"Dữ liệu kịch bản bị lỗi đã được sao lưu tại:\n{exception.BackupPath}\n\n" +
                            "Khôi phục thư viện về trạng thái an toàn trống? Dữ liệu lỗi trong bản sao lưu sẽ được giữ nguyên.",
                            "Phục hồi dữ liệu kịch bản");
                        if (!recover)
                        {
                            StatusMessage = $"{StatusMessage} Thư viện bị khóa để bảo vệ dữ liệu lỗi tại '{exception.BackupPath}'. Khởi động lại và xác nhận phục hồi để tiếp tục chỉnh sửa.";
                            return;
                        }

                        await scriptStore.RecoverAsync(cancellationToken);
                        SetScriptPersistenceBlocked(false);
                        loaded = [];
                        StatusMessage = $"{StatusMessage} Đã phục hồi thư viện; dữ liệu lỗi vẫn được giữ tại '{exception.BackupPath}'.";
                    }
                    if (loaded.Count == 0)
                    {
                        var template = ScriptTemplateFactory.CreateRestartChrome();
                        var templateItem = new ScriptItemViewModel(template);
                        Scripts.Add(templateItem);
                        SelectedScript = templateItem;
                        try { await scriptStore.SaveAsync([template], cancellationToken); }
                        catch (Exception exception)
                        {
                            LogInitializationIssue(exception);
                            StatusMessage = $"{StatusMessage} Template đã được tạo trong phiên này nhưng không thể lưu ({exception.Message}).";
                        }
                    }
                    else
                    {
                        foreach (var script in loaded) Scripts.Add(new ScriptItemViewModel(script));
                    }
                    SelectedScript ??= Scripts.FirstOrDefault();
                    RefreshScriptCollections();
                    CommonRunScript = Scripts.FirstOrDefault(item => item.Id == configuredCommonScriptId) ?? SelectedScript;
                    ControlCenterSelectedScript ??= CommonRunScript ?? SelectedScript;
                }
                finally { scriptSaveGate.Release(); }
                if (StatusMessage == "Đã tìm thấy memuc.exe.") StatusMessage = "Sẵn sàng.";
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception exception)
            {
                LogInitializationIssue(exception);
                SetScriptPersistenceBlocked(scriptStore.IsWriteBlocked);
                StatusMessage = $"{StatusMessage} Không thể đọc kịch bản đã lưu ({exception.Message}).";
            }
        }
        finally
        {
            IsInitializing = false;
        }
    }

    private async Task InitializeMemuAsync(CancellationToken cancellationToken)
    {
        ApplicationSettings settings;
        string? warning = null;
        try
        {
            settings = await settingsStore.LoadAsync(cancellationToken);
            warning = settingsStore.RecoveryNotice;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            LogInitializationIssue(exception);
            settings = new ApplicationSettings();
            warning = $"Không thể đọc cấu hình đã lưu ({exception.Message}).";
        }

        applicationSettings = settings;
        ControlCenterLayout = ControlCenterLayoutSettings.Normalize(settings.ControlCenterLayout);
        ApplyRunSettings(settings.MultiInstanceRun);
        MemucPath = pathDiscovery.IsValidMemucPath(settings.MemucPath) ? settings.MemucPath! : pathDiscovery.FindMemucPath() ?? string.Empty;
        var discovery = IsPathValid ? "Đã tìm thấy memuc.exe." : "Chưa tìm thấy memuc.exe. Hãy chọn file thủ công.";
        StatusMessage = warning is null ? discovery : $"{warning} {discovery}";
        if (IsPathValid && !string.Equals(settings.MemucPath, MemucPath, StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                await UpdateApplicationSettingsAsync(
                    current => current.MemucPath = MemucPath,
                    cancellationToken);
            }
            catch (Exception exception)
            {
                LogInitializationIssue(exception);
                StatusMessage = $"{StatusMessage} Không thể lưu đường dẫn ({exception.Message}).";
            }
        }
    }

    private async Task BrowseAsync()
    {
        var selectedPath = fileDialogService.SelectMemucPath(MemucPath);
        if (selectedPath is null) return;
        if (!pathDiscovery.IsValidMemucPath(selectedPath)) { StatusMessage = "File đã chọn không phải memuc.exe hợp lệ."; return; }
        MemucPath = selectedPath;
        InitializationErrorMessage = null;
        Instances.Clear();
        SynchronizeRunTargets([], new HashSet<int>());
        try
        {
            await UpdateApplicationSettingsAsync(
                settings => settings.MemucPath = selectedPath,
                CancellationToken.None);
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
            var selectedTargets = RunTargets.Where(item => item.IsSelected).Select(item => item.Index).ToHashSet();
            var instances = await instanceService.GetInstancesAsync(MemucPath, CancellationToken.None);
            Instances.Clear();
            foreach (var instance in instances) Instances.Add(instance);
            SynchronizeRunTargets(instances, selectedTargets);
            SelectedInstance = Instances.FirstOrDefault(item => item.Index == selectedIndex) ?? Instances.FirstOrDefault();
            StatusMessage = instances.Count == 0 ? "Không tìm thấy máy ảo nào." : $"Đã tải {instances.Count} máy ảo.";
        }
        catch (Exception exception) { StatusMessage = $"Không thể đọc danh sách máy ảo: {exception.Message}"; }
        finally { IsBusy = false; }
    }

    private void ApplyRunSettings(MultiInstanceRunSettings settings)
    {
        launchSpacingMode = settings.LaunchSpacingMode;
        fixedSpacingMilliseconds = settings.FixedSpacingMilliseconds;
        randomMinimumSpacingMilliseconds = settings.RandomMinimumSpacingMilliseconds;
        randomMaximumSpacingMilliseconds = settings.RandomMaximumSpacingMilliseconds;
        stopAllOnInvalidTarget = settings.StopAllOnInvalidTarget;
        scriptAssignmentMode = settings.ScriptAssignmentMode;
        configuredCommonScriptId = settings.CommonScriptId;
        OnPropertyChanged(nameof(LaunchSpacingMode));
        OnPropertyChanged(nameof(IsFixedSpacing));
        OnPropertyChanged(nameof(IsRandomSpacing));
        OnPropertyChanged(nameof(FixedSpacingMilliseconds));
        OnPropertyChanged(nameof(RandomMinimumSpacingMilliseconds));
        OnPropertyChanged(nameof(RandomMaximumSpacingMilliseconds));
        OnPropertyChanged(nameof(StopAllOnInvalidTarget));
        OnPropertyChanged(nameof(ScriptAssignmentMode));
        OnPropertyChanged(nameof(IsOneScriptForAll));
        OnPropertyChanged(nameof(IsPerInstanceScript));
        UpdateRunConfigurationState();
    }

    private void SynchronizeRunTargets(IReadOnlyList<MemuInstance> instances, IReadOnlySet<int> selectedIndices)
    {
        var targetsByIndex = RunTargets.ToDictionary(item => item.Index);
        var refreshedIndices = instances.Select(item => item.Index).ToHashSet();

        foreach (var removed in RunTargets
                     .Where(item => !refreshedIndices.Contains(item.Index) && !activeInstanceGroups.ContainsKey(item.Index))
                     .ToList())
        {
            removed.SelectionChanged -= OnRunTargetSelectionChanged;
            removed.AssignmentChanged -= OnTargetAssignmentChanged;
            RunTargets.Remove(removed);
        }

        foreach (var instance in instances)
        {
            if (targetsByIndex.TryGetValue(instance.Index, out var existing))
            {
                existing.ReplaceModel(instance);
                existing.SetActive(activeInstanceGroups.ContainsKey(instance.Index));
                var existingScript = Scripts.FirstOrDefault(item => item.Id == existing.AssignedScriptId);
                existing.SetAssignedScript(existingScript?.Id, existingScript?.Name, existingScript?.Model.Kind);
                continue;
            }

            var target = new InstanceTargetItemViewModel(instance) { IsSelected = selectedIndices.Contains(instance.Index) };
            target.SetActive(activeInstanceGroups.ContainsKey(instance.Index));
            var assignedId = applicationSettings.MultiInstanceRun.ScriptAssignments.GetValueOrDefault(instance.Index);
            var assignedScript = Scripts.FirstOrDefault(item => item.Id == assignedId);
            target.SetAssignedScript(assignedScript?.Id, assignedScript?.Name, assignedScript?.Model.Kind);
            target.SelectionChanged += OnRunTargetSelectionChanged;
            target.AssignmentChanged += OnTargetAssignmentChanged;
            RunTargets.Add(target);
        }
        var currentIndices = RunTargets.Select(item => item.Index).ToHashSet();
        dynamicSessionUniverse.IntersectWith(currentIndices);
        dynamicSessionAdmitted.IntersectWith(currentIndices);
        RebuildRunTargetProjection(clearHiddenSelection: false);
        UpdateRunConfigurationState();
    }

    private void OnRunTargetSelectionChanged(object? sender, EventArgs args) => HandleRunTargetSelectionChanged();

    private IReadOnlyList<MemuInstance> ResolveSelectedTargetCandidates() =>
        FilteredRunTargets.Where(item => item.IsSelected && item.CanSelectForRun).Select(item => item.Model).ToList();

    private IReadOnlyList<MemuInstance> ResolveRequestedTargets() =>
        FilteredRunTargets
            .Where(item => item.IsSelected && item.IsRunning && !activeInstanceGroups.ContainsKey(item.Index))
            .Select(item => item.Model)
            .ToList();

    private string? ValidateRunConfiguration(IReadOnlyList<MemuInstance>? requestedTargets = null)
    {
        if (HasBlockingExecutionDraft)
            return "Hãy lưu hoặc hủy thay đổi trong editor trước khi chạy.";
        if (LaunchSpacingMode == LaunchSpacingModeValue.Fixed)
        {
            if (!IsFixedSpacingInputValid) return "Khoảng cách cố định đang có giá trị không hợp lệ.";
            if (FixedSpacingMilliseconds < 0) return "Khoảng cách cố định không được âm.";
        }
        if (LaunchSpacingMode == LaunchSpacingModeValue.Random)
        {
            if (!IsRandomMinimumSpacingInputValid || !IsRandomMaximumSpacingInputValid)
                return "Khoảng cách ngẫu nhiên đang có giá trị không hợp lệ.";
            if (RandomMinimumSpacingMilliseconds < 0 || RandomMaximumSpacingMilliseconds < 0)
                return "Khoảng cách ngẫu nhiên không được âm.";
            if (RandomMinimumSpacingMilliseconds > RandomMaximumSpacingMilliseconds)
                return "Khoảng cách ngẫu nhiên tối thiểu không được lớn hơn tối đa.";
        }
        return ValidateScriptAssignments(requestedTargets);
    }

    private void UpdateRunConfigurationState()
    {
        OnPropertyChanged(nameof(SelectedRunTargetCount));
        OnPropertyChanged(nameof(RunTargetSelectionSummary));
        OnPropertyChanged(nameof(RunConfigurationError));
        NotifyRunTargetViewStateChanged();
        RaiseCommandStates();
    }

    private async Task<string?> PersistRunSettingsAsync(
        string memucPath,
        MultiInstanceRunSettings snapshot)
    {
        try
        {
            await UpdateApplicationSettingsAsync(settings =>
            {
                settings.MemucPath = memucPath;
                var runSettings = settings.MultiInstanceRun;
                runSettings.LaunchSpacingMode = snapshot.LaunchSpacingMode;
                runSettings.FixedSpacingMilliseconds = snapshot.FixedSpacingMilliseconds;
                runSettings.RandomMinimumSpacingMilliseconds = snapshot.RandomMinimumSpacingMilliseconds;
                runSettings.RandomMaximumSpacingMilliseconds = snapshot.RandomMaximumSpacingMilliseconds;
                runSettings.StopAllOnInvalidTarget = snapshot.StopAllOnInvalidTarget;
                runSettings.ScriptAssignmentMode = snapshot.ScriptAssignmentMode;
                runSettings.CommonScriptId = snapshot.CommonScriptId;
                runSettings.ScriptAssignments.Clear();
                foreach (var pair in snapshot.ScriptAssignments) runSettings.ScriptAssignments[pair.Key] = pair.Value;
            }, CancellationToken.None);
            return null;
        }
        catch (Exception exception) { return $"Không thể lưu cấu hình chạy ({exception.Message})."; }
    }

    private async Task UpdateApplicationSettingsAsync(
        Action<ApplicationSettings> update,
        CancellationToken cancellationToken)
    {
        applicationSettings = await settingsStore.UpdateAsync(update, cancellationToken);
    }

    private static string BuildCompletionMessage(MultiInstanceExecutionResult result)
    {
        var succeeded = result.Instances.Count(item => item.Status == InstanceExecutionStatus.Succeeded);
        var failed = result.Instances.Count(item => item.Status == InstanceExecutionStatus.Failed);
        var unavailable = result.Instances.Count(item => item.Status == InstanceExecutionStatus.Unavailable);
        var cancelled = result.Instances.Count(item => item.Status == InstanceExecutionStatus.Cancelled);
        var prefix = result.WasStoppedByInvalidTargetPolicy
            ? "Đã dừng toàn bộ tại preflight."
            : result.WasCancelled
                ? "Đã dừng phiên chạy."
                : "Đã hoàn tất phiên chạy.";
        return $"{prefix} Thành công: {succeeded}; thất bại: {failed}; không khả dụng/bỏ qua: {unavailable}; đã hủy: {cancelled}.";
    }

    private async Task CreateScriptAsync()
    {
        if (!await ResolvePendingEditorChangesAsync()) return;
        var transaction = CaptureLibraryMutationTransaction();
        var script = new ScriptDefinition { Name = $"Kịch bản {Scripts.Count + 1}" };
        var item = new ScriptItemViewModel(script);
        Scripts.Add(item);
        SelectedScript = item;
        await SaveScriptsWithRollbackAsync(transaction);
        RefreshScriptCollections();
    }

    private async Task RenameScriptAsync()
    {
        if (SelectedScript is null) return;
        ArgumentException.ThrowIfNullOrWhiteSpace(ScriptName);
        var target = SelectedScript;
        var draftAtSaveStart = ScriptName;
        var savedName = ScriptName.Trim();
        var previousName = target.Model.Name;
        var previousUpdatedAt = target.Model.UpdatedAt;
        using var persistence = BeginEditorPersistence();
        target.Model.Name = savedName;
        TouchSelectedScript();
        try { await SaveScriptsAsync(); }
        catch
        {
            target.Model.Name = previousName;
            target.Model.UpdatedAt = previousUpdatedAt;
            target.Refresh();
            throw;
        }
        if (ReferenceEquals(target, SelectedScript))
        {
            scriptNameBaseline = savedName;
            if (string.Equals(ScriptName, draftAtSaveStart, StringComparison.Ordinal))
                SetScriptNameFromModel(savedName);
            else
            {
                OnPropertyChanged(nameof(IsScriptNameDirty));
                OnPropertyChanged(nameof(CanRenameScript));
                OnPropertyChanged(nameof(HasAnyEditorDraft));
                OnPropertyChanged(nameof(EditorSaveState));
                RaiseCommandStates();
            }
        }
        RefreshAssignedScriptLabels();
        await PersistAssignmentsAsync();
    }

    private async Task DuplicateScriptAsync()
    {
        if (SelectedScript is null) return;
        if (!await ResolvePendingEditorChangesAsync()) return;
        var transaction = CaptureLibraryMutationTransaction();
        var clone = new ScriptItemViewModel(ScriptCloner.Clone(SelectedScript.Model));
        Scripts.Add(clone);
        RefreshScriptCollections();
        SelectedScript = clone;
        await SaveScriptsWithRollbackAsync(transaction);
    }

    private async Task DeleteScriptAsync()
    {
        if (SelectedScript is null) return;
        var usedBy = Scripts.Where(candidate => candidate.Model.Kind == ScriptKind.Composite &&
            candidate.Model.CompositeItems.OfType<ScriptReferenceItem>().Any(reference => reference.ScriptId == SelectedScript.Id))
            .Select(candidate => candidate.Name).ToList();
        if (usedBy.Count > 0)
        {
            StatusMessage = $"Không thể xóa '{SelectedScript.Name}' vì đang được dùng bởi: {string.Join(", ", usedBy)}.";
            return;
        }
        if (!confirmationService.Confirm($"Xóa kịch bản '{SelectedScript.Name}'?", "Xác nhận xóa")) return;
        if (!await ResolvePendingEditorChangesAsync()) return;
        var transaction = CaptureLibraryMutationTransaction();
        var deletedScriptId = SelectedScript.Id;
        var index = Scripts.IndexOf(SelectedScript);
        Scripts.Remove(SelectedScript);
        ClearAssignmentsForScript(deletedScriptId);
        stepHistories.Remove(deletedScriptId);
        compositeHistories.Remove(deletedScriptId);
        SelectedScript = Scripts.Count == 0 ? null : Scripts[Math.Min(index, Scripts.Count - 1)];
        if (CommonRunScript?.Id == deletedScriptId)
        {
            commonRunScript = SelectedScript;
            configuredCommonScriptId = SelectedScript?.Id;
            OnPropertyChanged(nameof(CommonRunScript));
            UpdateRunConfigurationState();
        }
        if (ControlCenterSelectedScript?.Id == deletedScriptId) ControlCenterSelectedScript = Scripts.FirstOrDefault();
        await SaveScriptsWithRollbackAsync(transaction);
        await PersistAssignmentsAsync();
        RefreshScriptCollections();
    }

    private async Task ExportSelectedScriptAsync()
    {
        if (scriptTransferService is null || SelectedScript is null) return;
        if (!await ResolvePendingEditorChangesAsync()) return;
        var path = fileDialogService.SelectScriptExportPath(ToSafeFileName(SelectedScript.Name));
        if (path is null) return;
        var closure = ScriptLibraryValidator.BuildExportClosure(
            [SelectedScript.Model], Scripts.Select(item => item.Model).ToList());
        await scriptTransferService.ExportAsync(path, closure, CancellationToken.None);
        StatusMessage = $"Đã xuất kịch bản '{SelectedScript.Name}'.";
    }

    private async Task ExportAllScriptsAsync()
    {
        if (scriptTransferService is null || Scripts.Count == 0) return;
        if (!await ResolvePendingEditorChangesAsync()) return;
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
        ScriptLibraryValidator.Validate(imported);
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
        var importCopies = plan
            .Where(item => item.Resolution == ScriptImportConflictResolution.CreateCopy)
            .ToDictionary(item => item.Script.Id, item => ScriptCloner.Clone(item.Script));
        var destinationIds = plan.ToDictionary(
            item => item.Script.Id,
            item => importCopies.TryGetValue(item.Script.Id, out var copy) ? copy.Id : item.Script.Id);
        foreach (var copy in importCopies.Values.Where(script => script.Kind == ScriptKind.Composite))
        foreach (var reference in copy.CompositeItems.OfType<ScriptReferenceItem>())
            reference.ScriptId = destinationIds[reference.ScriptId];

        var prospectiveLibrary = Scripts.Select(item => item.Model).ToList();
        foreach (var item in plan)
        {
            if (item.Existing is null)
            {
                prospectiveLibrary.Add(item.Script);
                continue;
            }
            if (item.Resolution == ScriptImportConflictResolution.CreateCopy)
                prospectiveLibrary.Add(importCopies[item.Script.Id]);
            else if (item.Resolution == ScriptImportConflictResolution.Overwrite)
                prospectiveLibrary[prospectiveLibrary.FindIndex(script => script.Id == item.Script.Id)] = item.Script;
        }
        ScriptLibraryValidator.Validate(prospectiveLibrary);
        foreach (var script in prospectiveLibrary)
        foreach (var step in script.Steps)
            stepCommandBuilder.Validate(step);
        if (!await ResolvePendingEditorChangesAsync()) return;
        var transaction = CaptureLibraryMutationTransaction();

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
                    lastImported = new ScriptItemViewModel(importCopies[item.Script.Id]);
                    Scripts.Add(lastImported);
                    importedCount++;
                    break;
                case ScriptImportConflictResolution.Overwrite:
                    var index = Scripts.IndexOf(item.Existing);
                    stepHistories.Remove(item.Existing.Id);
                    compositeHistories.Remove(item.Existing.Id);
                    lastImported = new ScriptItemViewModel(item.Script);
                    Scripts[index] = lastImported;
                    if (CommonRunScript?.Id == item.Existing.Id)
                    {
                        commonRunScript = lastImported;
                        configuredCommonScriptId = lastImported.Id;
                        OnPropertyChanged(nameof(CommonRunScript));
                        UpdateRunConfigurationState();
                    }
                    if (BulkAssignmentScript?.Id == item.Existing.Id)
                        BulkAssignmentScript = lastImported;
                    if (ControlCenterSelectedScript?.Id == item.Existing.Id)
                        ControlCenterSelectedScript = lastImported;
                    importedCount++;
                    break;
            }
        }

        if (importedCount > 0)
        {
            SelectedScript = lastImported;
            RefreshScriptCollections();
            await SaveScriptsWithRollbackAsync(transaction);
            RefreshAssignedScriptLabels();
            await PersistAssignmentsAsync();
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

    private async Task PrepareNewStepAsync()
    {
        if (!await ResolvePendingEditorChangesAsync()) return;
        SetStepSelection([], null);
        ResetEditor();
        StepEditorMode = RegularStepEditorMode.Create;
    }

    private void CancelStepCreate()
    {
        if (StepEditorMode != RegularStepEditorMode.Create) return;
        DiscardEditorChanges();
        ResetEditor();
        StepEditorMode = RegularStepEditorMode.None;
    }

    private async Task AddStepAsync()
    {
        if (SelectedScript is null || StepEditorMode != RegularStepEditorMode.Create || !TryBeginStepMutation()) return;
        try
        {
            var owner = SelectedScript;
            var step = CreateStep(null);
            stepCommandBuilder.Validate(step);
            ApplyCanonicalEditorName(step.Name);
            var savedEditorVersion = editorVersion;
            var savedDraft = CaptureRegularEditorDraft();
            var before = CaptureStepListSnapshot();
            var previousUpdatedAt = owner.Model.UpdatedAt;
            var item = CreateStepItem(step);
            Steps.Add(item);
            PushUndoSnapshot(before);
            SyncStepsToModel();
            TouchSelectedScript();
            using (BeginEditorPersistence())
            {
                try { await SaveScriptsAsync(); }
                catch
                {
                    Steps.Remove(item);
                    SyncStepsToModel(owner);
                    owner.Model.UpdatedAt = previousUpdatedAt;
                    owner.Refresh();
                    var history = GetStepHistory(owner.Id);
                    if (history.Undo.Count > 0) history.Undo.RemoveLast();
                    RefreshRegularEditorDirty();
                    throw;
                }
            }
            if (StepEditorMode == RegularStepEditorMode.Create && editorVersion == savedEditorVersion)
            {
                DiscardEditorChanges();
                StepEditorMode = RegularStepEditorMode.None;
                SetStepSelection([item], item);
                StatusMessage = "Đã thêm bước.";
            }
            else if (StepEditorMode == RegularStepEditorMode.Create)
            {
                regularEditorBaseline = savedDraft;
                RefreshRegularEditorDirty();
                StatusMessage = IsEditorDirty
                    ? "Đã thêm bước; còn thay đổi mới trong trình tạo."
                    : "Đã thêm bước.";
            }
            else StatusMessage = "Đã thêm bước.";
        }
        finally { EndStepMutation(); }
    }

    private async Task SaveStepAsync()
    {
        if (SelectedScript is null || SelectedStep is null || StepEditorMode != RegularStepEditorMode.Edit ||
            SelectedStep.Model is DelayStep && EditorKind == ScriptStepKind.Delay || !TryBeginStepMutation()) return;
        try
        {
            var target = SelectedStep;
            var owner = SelectedScript;
            using var persistence = BeginEditorPersistence();
            var previousModel = ScriptCloner.CloneStepPreservingId(target.Model);
            var previousUpdatedAt = owner.Model.UpdatedAt;
            var step = CreateStep(SelectedStep.Id);
            step.IsEnabled = SelectedStep.IsEnabled;
            stepCommandBuilder.Validate(step);
            ApplyCanonicalEditorName(step.Name);
            var savedEditorVersion = editorVersion;
            var savedDraft = CaptureRegularEditorDraft();
            var before = CaptureStepListSnapshot();
            SelectedStep.ReplaceModel(step);
            OnPropertyChanged(nameof(ShowRegularSaveButton));
            PushUndoSnapshot(before);
            SyncStepsToModel();
            TouchSelectedScript();
            UpdatePreview();
            try { await SaveScriptsAsync(); }
            catch
            {
                target.ReplaceModel(previousModel);
                SyncStepsToModel(owner);
                owner.Model.UpdatedAt = previousUpdatedAt;
                owner.Refresh();
                var history = GetStepHistory(owner.Id);
                if (history.Undo.Count > 0) history.Undo.RemoveLast();
                UpdatePreview();
                RefreshRegularEditorDirty();
                throw;
            }
            if (ReferenceEquals(target, SelectedStep))
            {
                regularEditorBaseline = savedDraft;
                if (editorVersion == savedEditorVersion) SetEditorDirty(false);
                else RefreshRegularEditorDirty();
            }
            StatusMessage = IsEditorDirty ? "Đã lưu bước; còn thay đổi chưa lưu." : "Đã lưu bước.";
        }
        finally { EndStepMutation(); }
    }

    private async Task DuplicateStepAsync()
    {
        var source = GetSelectedStepsForMutation();
        if (source.Count == 0 || !await ResolveRegularEditorChangesAsync() || !TryBeginStepMutation()) return;
        try
        {
            var transaction = CaptureStepMutationTransaction();
            var before = transaction.Snapshot;
            var insertionIndex = source.Select(Steps.IndexOf).Max() + 1;
            var clones = source.Select(step => CreateStepItem(ScriptCloner.CloneStep(step.Model))).ToList();
            for (var index = 0; index < clones.Count; index++)
                Steps.Insert(insertionIndex + index, clones[index]);
            SetStepSelection(clones, clones[^1]);
            PushUndoSnapshot(before);
            await PersistStepMutationCoreAsync(transaction);
            StatusMessage = $"Đã nhân bản {clones.Count} bước.";
        }
        finally { EndStepMutation(); }
    }

    private async Task DeleteStepAsync()
    {
        var stepsToDelete = GetSelectedStepsForMutation();
        if (stepsToDelete.Count == 0 ||
            !confirmationService.Confirm($"Xóa {stepsToDelete.Count} bước đã chọn?", "Xác nhận xóa")) return;
        if (!await ResolveRegularEditorChangesAsync() || !TryBeginStepMutation()) return;
        try
        {
            var indexes = stepsToDelete.Select(Steps.IndexOf).Where(index => index >= 0).OrderBy(index => index).ToList();
            if (indexes.Count == 0) return;
            var transaction = CaptureStepMutationTransaction();
            var before = transaction.Snapshot;
            var nextSelectionIndex = indexes[0];
            for (var index = indexes.Count - 1; index >= 0; index--)
                Steps.RemoveAt(indexes[index]);

            var next = Steps.Count == 0 ? null : Steps[Math.Min(nextSelectionIndex, Steps.Count - 1)];
            SetStepSelection(next is null ? [] : [next], next);
            PushUndoSnapshot(before);
            await PersistStepMutationCoreAsync(transaction);
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
        if (Steps.SequenceEqual(desired) || !await ResolveRegularEditorChangesAsync() || !TryBeginStepMutation()) return;
        try
        {
            var transaction = CaptureStepMutationTransaction();
            var before = transaction.Snapshot;
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
            await PersistStepMutationCoreAsync(transaction);
            StatusMessage = $"Đã di chuyển {group.Count} bước.";
        }
        finally { EndStepMutation(); }
    }

    private void CopySelectedSteps()
    {
        if (!CanChangeSelection) return;
        var source = GetSelectedStepsForMutation();
        if (source.Count == 0) return;
        var visibleCopies = new List<ScriptStep>(source.Count);
        foreach (var item in source)
        {
            if (ReferenceEquals(item, SelectedStep) && StepEditorMode == RegularStepEditorMode.Edit &&
                (IsEditorDirty || HasInvalidRegularEditorDraft || IsEditorPersistenceBusy))
            {
                if (HasInvalidRegularEditorDraft || !IsRegularEditorDraftSemanticallyValid())
                {
                    StatusMessage = "Không thể sao chép vì dữ liệu đang hiển thị không hợp lệ.";
                    return;
                }
                var visibleDraft = CreateStep(item.Id);
                visibleDraft.IsEnabled = item.IsEnabled;
                visibleCopies.Add(visibleDraft);
            }
            else visibleCopies.Add(item.Model);
        }
        copiedSteps = visibleCopies.Select(ScriptCloner.CloneStep).ToList();
        copiedFromScriptName = SelectedScript?.Name;
        OnPropertyChanged(nameof(HasCopiedSteps));
        OnPropertyChanged(nameof(CopiedStepCount));
        OnPropertyChanged(nameof(CopiedFromScriptName));
        OnPropertyChanged(nameof(StepClipboardSummary));
        PasteStepsCommand.RaiseCanExecuteChanged();
        StatusMessage = $"Đã sao chép {copiedSteps.Count} bước.";
    }

    private async Task PasteCopiedStepsAsync()
    {
        if (SelectedScript is null || copiedSteps.Count == 0 ||
            !await ResolveRegularEditorChangesAsync() || !TryBeginStepMutation()) return;
        try
        {
            var transaction = CaptureStepMutationTransaction();
            var before = transaction.Snapshot;
            var selectedIndexes = GetSelectedStepsForMutation().Select(Steps.IndexOf).Where(index => index >= 0).ToList();
            var insertionIndex = selectedIndexes.Count == 0 ? Steps.Count : selectedIndexes.Max() + 1;
            var pasted = copiedSteps.Select(step => CreateStepItem(ScriptCloner.CloneStep(step))).ToList();
            for (var index = 0; index < pasted.Count; index++)
                Steps.Insert(insertionIndex + index, pasted[index]);
            SetStepSelection(pasted, pasted[^1]);
            PushUndoSnapshot(before);
            await PersistStepMutationCoreAsync(transaction);
            StatusMessage = $"Đã dán {pasted.Count} bước.";
        }
        finally { EndStepMutation(); }
    }

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
        return TryClearStepSelectionFromBlank();
    }

    public Task<bool> CommitAndClearStepSelectionAsync() =>
        Task.FromResult(TryClearStepSelectionFromBlank());

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

        pendingToggleTransaction = CaptureStepMutationTransaction();
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
            if (pendingToggleTransaction is null) return;
            PushUndoSnapshot(pendingToggleTransaction.Snapshot);
            await PersistStepMutationCoreAsync(pendingToggleTransaction);
        }
        catch (Exception exception)
        {
            ReportUnexpectedError(exception);
        }
        finally
        {
            pendingToggleTransaction = null;
            EndStepMutation();
        }
    }

    private async Task PersistStepMutationCoreAsync(StepMutationTransaction transaction)
    {
        var persistence = BeginEditorPersistence();
        try
        {
            SyncStepsToModel(transaction.Owner);
            TouchScript(transaction.Owner);
            await SaveScriptsAsync();
        }
        catch
        {
            persistence.Dispose();
            RestoreStepMutationTransaction(transaction);
            throw;
        }
        finally { persistence.Dispose(); }
    }

    private async Task RunAsync()
    {
        await StartLaunchGroupAsync(ResolveSelectedTargetCandidates());
    }

    private async Task RunAllRemainingAsync()
    {
        EnsureDynamicSession();
        var requestedTargets = RunTargets
            .Where(item => item.IsRunning && dynamicSessionUniverse.Contains(item.Index) &&
                           !dynamicSessionAdmitted.Contains(item.Index) && !activeInstanceGroups.ContainsKey(item.Index))
            .Select(item => item.Model)
            .ToList();
        if (requestedTargets.Count == 0)
        {
            StatusMessage = "Không còn giả lập nào trong phiên hiện tại để chạy.";
            return;
        }
        await StartLaunchGroupAsync(requestedTargets);
    }

    private async Task StartLaunchGroupAsync(IReadOnlyList<MemuInstance> requestedTargets)
    {
        if (isSafeShutdownRequested || !IsPathValid) return;
        if (!await FlushPendingDelayAutosavesAsync())
        {
            StatusMessage = "Thời gian chờ đang không hợp lệ. Hãy sửa giá trị trước khi chạy.";
            return;
        }
        if (isSafeShutdownRequested) return;
        EnsureDynamicSession();
        var skippedActive = requestedTargets.Where(target => activeInstanceGroups.ContainsKey(target.Index)).ToList();
        requestedTargets = requestedTargets
            .Where(target => !activeInstanceGroups.ContainsKey(target.Index))
            .ToList();
        var configurationError = ValidateRunConfiguration(requestedTargets);
        if (requestedTargets.Count == 0 || configurationError is not null)
        {
            StatusMessage = configurationError ?? (skippedActive.Count > 0
                ? $"Đã bỏ qua {skippedActive.Count} giả lập đang hoạt động."
                : "Hãy chọn ít nhất một giả lập để chạy.");
            return;
        }

        if (SelectedScript is not null) SyncStepsToModel();
        var assignedScripts = ResolveAssignedScripts(requestedTargets);
        if (assignedScripts is null)
        {
            StatusMessage = ValidateScriptAssignments() ?? "Hãy gán kịch bản cho mọi giả lập sẽ chạy.";
            return;
        }
        var libraryModels = Scripts.Select(item => item.Model).ToList();
        ScriptLibraryValidator.Validate(libraryModels);
        var libraryById = libraryModels.ToDictionary(script => script.Id);
        var rawStepCount = assignedScripts.Values.Sum(script => CountRawShellSteps(script, libraryById));
        if (rawStepCount > 0 && !confirmationService.Confirm(
                $"Các kịch bản đã gán có tổng cộng {rawStepCount} lệnh Android shell thô trên {requestedTargets.Count} lượt chạy. Chỉ tiếp tục nếu bạn tin cậy các lệnh này.",
                "Cảnh báo lệnh shell thô"))
        {
            StatusMessage = "Đã hủy chạy vì lệnh shell thô chưa được xác nhận.";
            return;
        }

        var scriptLibrarySnapshot = ExecutionScriptLibrarySnapshot.Create(libraryModels);
        var scriptSnapshots = assignedScripts.ToFrozenDictionary(
            pair => pair.Key,
            pair => scriptLibrarySnapshot.CreateScriptCopy(pair.Value.Id));
        var defaultScriptSnapshot = scriptSnapshots[requestedTargets[0].Index];
        var memucPathSnapshot = MemucPath;
        var runSettingsSnapshot = new MultiInstanceRunSettings
        {
            LaunchSpacingMode = LaunchSpacingMode,
            FixedSpacingMilliseconds = FixedSpacingMilliseconds,
            RandomMinimumSpacingMilliseconds = RandomMinimumSpacingMilliseconds,
            RandomMaximumSpacingMilliseconds = RandomMaximumSpacingMilliseconds,
            StopAllOnInvalidTarget = StopAllOnInvalidTarget,
            ScriptAssignmentMode = ScriptAssignmentMode,
            CommonScriptId = CommonRunScript?.Id
        };
        foreach (var target in RunTargets.Where(item => item.AssignedScriptId is not null))
            runSettingsSnapshot.ScriptAssignments[target.Index] = target.AssignedScriptId!.Value;
        var executionRequest = new MultiInstanceExecutionRequest
        {
            LaunchGroupId = Guid.NewGuid(),
            Script = defaultScriptSnapshot,
            ScriptsByInstance = scriptSnapshots,
            ScriptLibrarySnapshot = scriptLibrarySnapshot,
            MemucPath = memucPathSnapshot,
            Targets = requestedTargets,
            LaunchSpacingMode = runSettingsSnapshot.LaunchSpacingMode,
            FixedSpacing = TimeSpan.FromMilliseconds(runSettingsSnapshot.FixedSpacingMilliseconds),
            RandomMinimumSpacing = TimeSpan.FromMilliseconds(runSettingsSnapshot.RandomMinimumSpacingMilliseconds),
            RandomMaximumSpacing = TimeSpan.FromMilliseconds(runSettingsSnapshot.RandomMaximumSpacingMilliseconds),
            StopAllOnInvalidTarget = runSettingsSnapshot.StopAllOnInvalidTarget
        };
        var groupId = executionRequest.LaunchGroupId;
        var runItems = new List<InstanceRunItemViewModel>();
        foreach (var target in requestedTargets)
        {
            activeInstanceGroups[target.Index] = groupId;
            dynamicSessionAdmitted.Add(target.Index);
            var item = new InstanceRunItemViewModel(groupId, target, scriptSnapshots[target.Index], StopInstance);
            item.SelectionChanged += OnActiveInstanceSelectionChanged;
            runItems.Add(item);
            ActiveInstanceRuns.Add(item);
            instanceRunsByKey[(groupId, target.Index)] = item;
            AdjustActiveStatusCount(item.Status, 1);
            var row = RunTargets.FirstOrDefault(candidate => candidate.Index == target.Index);
            if (row is not null) row.SetActive(true);
        }
        var runDescription = BuildRunDescription(runSettingsSnapshot.ScriptAssignmentMode, defaultScriptSnapshot, scriptSnapshots);
        var group = new LaunchGroupItemViewModel(
            ++launchGroupSequence,
            groupId,
            DateTimeOffset.UtcNow,
            runDescription,
            runItems);
        ActiveLaunchGroups.Add(group);
        SetExecutionAggregateState();
        StatusMessage = ScriptAssignmentMode == ScriptAssignmentModeValue.OneScriptForAll
            ? $"Đang chạy '{defaultScriptSnapshot.Name}' trên {requestedTargets.Count} giả lập…"
            : $"Đang chạy kịch bản đã gán trên {requestedTargets.Count} giả lập…";
        if (skippedActive.Count > 0)
            StatusMessage += $" Đã bỏ qua {skippedActive.Count} giả lập đang hoạt động.";
        var progress = new InstanceExecutionProgressPump(
            editorSynchronizationContext,
            ApplyExecutionUpdate);
        try
        {
            var session = executionScheduler.Start(executionRequest, progress);
            executionSessions[groupId] = session;
            SetExecutionAggregateState();
            var settingsTask = PersistRunSettingsAsync(memucPathSnapshot, runSettingsSnapshot);
            _ = ObserveLaunchGroupAsync(groupId, session, settingsTask, progress);
        }
        catch
        {
            foreach (var target in requestedTargets)
            {
                if (activeInstanceGroups.GetValueOrDefault(target.Index) == groupId)
                    activeInstanceGroups.Remove(target.Index);
                RunTargets.FirstOrDefault(item => item.Index == target.Index)?.SetActive(false);
            }
            foreach (var item in runItems)
            {
                var previousStatus = item.Status;
                item.Apply(new InstanceExecutionUpdate(groupId, item.Index, item.Name, InstanceExecutionStatus.Failed,
                    Message: "Không thể khởi tạo nhóm chạy.", ScriptId: item.ScriptId, ScriptName: item.ScriptName));
                UpdateActiveStatusCount(previousStatus, item.Status);
            }
            CompleteLaunchGroup(groupId, null, DateTimeOffset.UtcNow);
            SetExecutionAggregateState();
            throw;
        }
        await Task.CompletedTask;
    }

    private async Task ObserveLaunchGroupAsync(
        Guid groupId,
        MultiInstanceExecutionSession session,
        Task<string?> settingsTask,
        InstanceExecutionProgressPump progress)
    {
        MultiInstanceExecutionResult? completedResult = null;
        Exception? completionError = null;
        try
        {
            completedResult = await session.Completion;
        }
        catch (Exception exception)
        {
            completionError = exception;
        }
        finally
        {
            progress.DrainPending();
            foreach (var index in activeInstanceGroups
                         .Where(pair => pair.Value == groupId).Select(pair => pair.Key).ToList())
                activeInstanceGroups.Remove(index);
            executionSessions.Remove(groupId);
            session.Dispose();
            CompleteLaunchGroup(groupId, completedResult, completedResult?.EndedAt ?? DateTimeOffset.UtcNow);
            SetExecutionAggregateState();
        }

        if (completionError is not null)
        {
            completedResult = null;
            session = null!;
            StatusMessage = $"Nhóm chạy gặp lỗi: {completionError.Message}";
            return;
        }

        var completionMessage = BuildCompletionMessage(completedResult!);
        completedResult = null;
        session = null!;
        var settingsWarning = await settingsTask;
        StatusMessage = settingsWarning is null
            ? completionMessage
            : $"{completionMessage} {settingsWarning}";
    }

    private void CompleteLaunchGroup(
        Guid groupId,
        MultiInstanceExecutionResult? completedResult,
        DateTimeOffset fallbackEndedAt)
    {
        var group = ActiveLaunchGroups.FirstOrDefault(item => item.LaunchGroupId == groupId);
        if (group is null) return;

        var latestRunResult = CreateLatestRunResult(group, completedResult, fallbackEndedAt);
        group.Detach();
        ActiveLaunchGroups.Remove(group);
        foreach (var instance in group.Instances)
        {
            instance.SelectionChanged -= OnActiveInstanceSelectionChanged;
            AdjustActiveStatusCount(instance.Status, -1);
            instanceRunsByKey.Remove((groupId, instance.Index));
            ActiveInstanceRuns.Remove(instance);
            var target = RunTargets.FirstOrDefault(item => item.Index == instance.Index);
            target?.SetActive(false);
            if (target is not null && Instances.All(item => item.Index != instance.Index))
            {
                target.SelectionChanged -= OnRunTargetSelectionChanged;
                target.AssignmentChanged -= OnTargetAssignmentChanged;
                RunTargets.Remove(target);
                dynamicSessionUniverse.Remove(instance.Index);
                dynamicSessionAdmitted.Remove(instance.Index);
            }
        }

        RebuildRunTargetProjection(clearHiddenSelection: false);
        UpdateRunConfigurationState();
        AddRecentRun(latestRunResult);
        StopSelectedActiveInstancesCommand.RaiseCanExecuteChanged();
    }

    private static LatestRunResultViewModel CreateLatestRunResult(
        LaunchGroupItemViewModel group,
        MultiInstanceExecutionResult? completedResult,
        DateTimeOffset fallbackEndedAt)
    {
        var instanceSnapshots = new List<RecentRunInstanceSnapshotViewModel>();
        var runtimesByInstanceIndex = new Dictionary<int, InstanceRunItemViewModel>(group.Instances.Count);
        var runtimeStepNamesByKey = new Dictionary<(int InstanceIndex, Guid StepId), string>();
        var lastRuntimeStepNamesByInstanceIndex = new Dictionary<int, string>();
        foreach (var runtime in group.Instances)
        {
            if (!runtimesByInstanceIndex.TryAdd(runtime.Index, runtime)) continue;
            string? lastRuntimeStepName = null;
            foreach (var step in runtime.Steps)
            {
                runtimeStepNamesByKey.TryAdd((runtime.Index, step.Id), step.Name);
                if (step.Status != StepExecutionStatus.NotRun) lastRuntimeStepName = step.Name;
            }
            if (lastRuntimeStepName is not null)
                lastRuntimeStepNamesByInstanceIndex.TryAdd(runtime.Index, lastRuntimeStepName);
        }

        IReadOnlyList<InstanceExecutionStatus> statuses;
        if (completedResult is not null)
        {
            var normalizedStatuses = new InstanceExecutionStatus[completedResult.Instances.Count];
            var executionSnapshots = new LatestExecutionSnapshot[completedResult.Instances.Count];
            for (var index = 0; index < completedResult.Instances.Count; index++)
            {
                var result = completedResult.Instances[index];
                normalizedStatuses[index] = NormalizeLatestStatus(result.Status);
                executionSnapshots[index] = CreateLatestExecutionSnapshot(result.Execution);
            }
            statuses = normalizedStatuses;

            for (var index = 0; index < completedResult.Instances.Count; index++)
            {
                var result = completedResult.Instances[index];
                var latestStatus = normalizedStatuses[index];
                runtimesByInstanceIndex.TryGetValue(result.Target.Index, out var runtime);
                instanceSnapshots.Add(new RecentRunInstanceSnapshotViewModel(
                    result.Target.Index,
                    CompactText(result.Target.Name, 160),
                    CompactText(result.ScriptName ?? runtime?.ScriptName ?? "—", 160),
                    ResolveLastStepName(
                        result.Target.Index,
                        executionSnapshots[index],
                        runtimeStepNamesByKey,
                        lastRuntimeStepNamesByInstanceIndex),
                    latestStatus,
                    BuildShortRunMessage(latestStatus, result.Message ?? runtime?.Message, executionSnapshots[index])));
            }
        }
        else
        {
            statuses = group.Instances.Select(instance => NormalizeLatestStatus(instance.Status)).ToArray();
            foreach (var instance in group.Instances)
            {
                var latestStatus = NormalizeLatestStatus(instance.Status);
                instanceSnapshots.Add(new RecentRunInstanceSnapshotViewModel(
                    instance.Index,
                    CompactText(instance.Name, 160),
                    CompactText(instance.ScriptName, 160),
                    lastRuntimeStepNamesByInstanceIndex.GetValueOrDefault(instance.Index, "—"),
                    latestStatus,
                    BuildShortRunMessage(latestStatus, instance.Message, default)));
            }
        }

        return new LatestRunResultViewModel(
            group.LaunchGroupId,
            group.DisplayName,
            group.RunDescription,
            completedResult?.StartedAt ?? group.StartedAt,
            completedResult?.EndedAt ?? fallbackEndedAt,
            statuses.Count,
            statuses.Count(status => status == InstanceExecutionStatus.Succeeded),
            statuses.Count(status => status == InstanceExecutionStatus.Failed),
            statuses.Count(status => status == InstanceExecutionStatus.Unavailable),
            statuses.Count(status => status == InstanceExecutionStatus.Cancelled),
            instanceSnapshots.AsReadOnly());
    }

    private static InstanceExecutionStatus NormalizeLatestStatus(InstanceExecutionStatus status) => status switch
    {
        InstanceExecutionStatus.Succeeded => InstanceExecutionStatus.Succeeded,
        InstanceExecutionStatus.Failed => InstanceExecutionStatus.Failed,
        InstanceExecutionStatus.Unavailable => InstanceExecutionStatus.Unavailable,
        InstanceExecutionStatus.Cancelled => InstanceExecutionStatus.Cancelled,
        _ => InstanceExecutionStatus.Failed
    };

    private static LatestExecutionSnapshot CreateLatestExecutionSnapshot(ExecutionResult? execution)
    {
        if (execution is null) return default;

        Guid? lastExecutedStepId = null;
        string? lastExecutedCompositePath = null;
        Guid? lastProblemStepId = null;
        string? lastProblemCompositePath = null;
        string? lastProblemStandardError = null;
        int? lastProblemExitCode = null;
        foreach (var step in execution.Steps)
        {
            if (step.Status != StepExecutionStatus.NotRun)
            {
                lastExecutedStepId = step.StepId;
                lastExecutedCompositePath = step.CompositeContext?.FullDisplayName;
            }
            if (step.Status is not (StepExecutionStatus.Failed or StepExecutionStatus.Cancelled)) continue;
            lastProblemStepId = step.StepId;
            lastProblemCompositePath = step.CompositeContext?.FullDisplayName;
            lastProblemStandardError = step.StandardError;
            lastProblemExitCode = step.ExitCode;
        }

        return new LatestExecutionSnapshot(
            lastExecutedStepId,
            lastExecutedCompositePath,
            lastProblemStepId,
            lastProblemCompositePath,
            lastProblemStandardError,
            lastProblemExitCode);
    }

    private static string ResolveLastStepName(
        int instanceIndex,
        LatestExecutionSnapshot execution,
        IReadOnlyDictionary<(int InstanceIndex, Guid StepId), string> runtimeStepNamesByKey,
        IReadOnlyDictionary<int, string> lastRuntimeStepNamesByInstanceIndex)
    {
        if (!string.IsNullOrWhiteSpace(execution.LastProblemCompositePath))
            return CompactText(execution.LastProblemCompositePath, 160);
        if (execution.LastProblemStepId is Guid problemStepId)
            return runtimeStepNamesByKey.GetValueOrDefault((instanceIndex, problemStepId), "—");
        if (!string.IsNullOrWhiteSpace(execution.LastExecutedCompositePath))
            return CompactText(execution.LastExecutedCompositePath, 160);
        if (execution.LastExecutedStepId is Guid stepId)
            return runtimeStepNamesByKey.GetValueOrDefault((instanceIndex, stepId), "—");
        return lastRuntimeStepNamesByInstanceIndex.GetValueOrDefault(instanceIndex, "—");
    }

    private static string BuildShortRunMessage(
        InstanceExecutionStatus status,
        string? message,
        LatestExecutionSnapshot execution)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            if (!string.IsNullOrWhiteSpace(execution.LastProblemStandardError))
                message = execution.LastProblemStandardError;
            else if (execution.LastProblemExitCode is int exitCode)
                message = $"Bước cuối trả về exit code {exitCode}.";
        }

        message ??= status switch
        {
            InstanceExecutionStatus.Succeeded => "Hoàn tất.",
            InstanceExecutionStatus.Cancelled => "Đã hủy theo yêu cầu.",
            InstanceExecutionStatus.Unavailable => "Giả lập không khả dụng.",
            _ => "Kịch bản không hoàn tất."
        };
        var normalized = string.Join(" ", message.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return normalized.Length <= LatestRunMessageLimit
            ? normalized
            : $"{normalized[..(LatestRunMessageLimit - 1)]}…";
    }

    private readonly record struct LatestExecutionSnapshot(
        Guid? LastExecutedStepId,
        string? LastExecutedCompositePath,
        Guid? LastProblemStepId,
        string? LastProblemCompositePath,
        string? LastProblemStandardError,
        int? LastProblemExitCode);

    private static string BuildRunDescription(
        ScriptAssignmentModeValue assignmentMode,
        ScriptDefinition defaultScript,
        IReadOnlyDictionary<int, ScriptDefinition> scriptsByInstance)
    {
        if (assignmentMode == ScriptAssignmentModeValue.OneScriptForAll)
            return CompactRunDescription($"Một kịch bản cho tất cả · {defaultScript.Name}");

        var distinctNames = scriptsByInstance.Values
            .Select(script => script.Name)
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        var visibleNames = distinctNames
            .Take(RunDescriptionVisibleScriptLimit)
            .Select(name => CompactText(name, RunDescriptionScriptNameLimit));
        var description = $"Kịch bản riêng theo giả lập · {string.Join(", ", visibleNames)}";
        var remainingCount = distinctNames.Count - RunDescriptionVisibleScriptLimit;
        if (remainingCount > 0) description += $" · +{remainingCount} kịch bản khác";
        return CompactRunDescription(description);
    }

    private static string CompactRunDescription(string value) => CompactText(value, LatestRunDescriptionLimit);

    private static string CompactText(string value, int limit)
    {
        var normalized = string.Join(" ", value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (normalized.Length == 0) return "—";
        return normalized.Length <= limit ? normalized : $"{normalized[..(limit - 1)]}…";
    }

    private void ClearLatestRunResult()
    {
        if (RecentRuns.Count == 0 ||
            !confirmationService.Confirm("Xóa toàn bộ kết quả gần đây?", "Xác nhận xóa kết quả")) return;
        RecentRuns.Clear();
        LatestRunResult = null;
        SelectedRecentRunResult = null;
        OnPropertyChanged(nameof(HasRecentRuns));
        OnPropertyChanged(nameof(HasNoRecentRuns));
        StatusMessage = "Đã xóa kết quả gần đây.";
    }

    private async void PersistCommonRunScriptSelection()
    {
        try
        {
            var scriptId = CommonRunScript?.Id;
            await UpdateApplicationSettingsAsync(
                settings => settings.MultiInstanceRun.CommonScriptId = scriptId,
                CancellationToken.None);
        }
        catch (Exception exception)
        {
            StatusMessage = $"Đã đổi kịch bản dùng chung trong phiên nhưng không thể lưu ({exception.Message}).";
        }
    }

    private void EnsureDynamicSession()
    {
        if (dynamicSessionUniverse.Count > 0 &&
            (executionSessions.Count > 0 || dynamicSessionAdmitted.Count < dynamicSessionUniverse.Count)) return;
        dynamicSessionUniverse.Clear();
        dynamicSessionAdmitted.Clear();
        foreach (var target in RunTargets.Where(item => item.IsRunning)) dynamicSessionUniverse.Add(target.Index);
    }

    private void Stop()
    {
        foreach (var pair in executionSessions.ToList())
        {
            pair.Value.StopAll(index =>
            {
                if (instanceRunsByKey.TryGetValue((pair.Key, index), out var item)) item.RequestStop();
            });
        }
        StatusMessage = "Đang dừng tất cả nhóm chạy…";
        StopSelectedActiveInstancesCommand.RaiseCanExecuteChanged();
    }

    public async Task StopAllForSafeShutdownAsync()
    {
        if (!isSafeShutdownRequested)
        {
            isSafeShutdownRequested = true;
            RaiseCommandStates();
        }

        if (!IsExecuting) return;

        Stop();
        var terminalCompletion = executionTerminalCompletion ??
            throw new InvalidOperationException("Active execution is missing its terminal completion signal.");
        await terminalCompletion.Task;
    }

    internal void ResumeAfterCancelledSafeShutdown()
    {
        if (!isSafeShutdownRequested) return;
        isSafeShutdownRequested = false;
        RaiseCommandStates();
    }

    private void StopSelectedActiveInstances()
    {
        var selected = ActiveInstanceRuns.Where(item => item.IsSelected && item.CanStop).ToList();
        var acceptedCount = 0;
        foreach (var item in selected)
        {
            if (!executionSessions.TryGetValue(item.LaunchGroupId, out var session) ||
                !session.StopInstance(item.Index, () => item.RequestStop())) continue;
            acceptedCount++;
        }
        StatusMessage = $"Đang dừng {acceptedCount} giả lập đã chọn…";
    }

    private void OnActiveInstanceSelectionChanged(object? sender, EventArgs args) =>
        StopSelectedActiveInstancesCommand.RaiseCanExecuteChanged();

    private void StopGroup(Guid groupId)
    {
        if (!executionSessions.TryGetValue(groupId, out var session)) return;
        session.StopAll(index =>
        {
            if (instanceRunsByKey.TryGetValue((groupId, index), out var item)) item.RequestStop();
        });
        var groupName = ActiveLaunchGroups.FirstOrDefault(item => item.LaunchGroupId == groupId)?.DisplayName ?? "nhóm đã chọn";
        StatusMessage = $"Đang dừng {groupName}…";
    }

    private bool StopInstance(Guid groupId, int instanceIndex)
    {
        if (!executionSessions.TryGetValue(groupId, out var session) ||
            !session.StopInstance(instanceIndex, () =>
            {
                if (instanceRunsByKey.TryGetValue((groupId, instanceIndex), out var item)) item.RequestStop();
            }))
            return false;
        StatusMessage = $"Đang dừng giả lập index {instanceIndex}…";
        StopSelectedActiveInstancesCommand.RaiseCanExecuteChanged();
        return true;
    }

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
        CanUseMemuControls && !IsCapturing && IsPathValid && SelectedInstance is { IsRunning: true } &&
        EditorKind is ScriptStepKind.ForceStop or ScriptStepKind.OpenApp;

    private bool CanCapture(ScriptStepKind kind) =>
        CanUseMemuControls && !IsCapturing && IsPathValid && EditorKind == kind &&
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

    private void ApplyExecutionUpdate(InstanceExecutionUpdate update)
    {
        if (!executionSessions.ContainsKey(update.LaunchGroupId) &&
            !activeInstanceGroups.Values.Contains(update.LaunchGroupId)) return;
        if (!instanceRunsByKey.TryGetValue((update.LaunchGroupId, update.InstanceIndex), out var instance)) return;
        var previousStatus = instance.Status;
        var changes = instance.ApplyAndGetChanges(update);
        if (changes.StatusChanged)
        {
            var previousRunningCount = runningInstanceCount;
            var previousWaitingCount = waitingInstanceCount;
            UpdateActiveStatusCount(previousStatus, instance.Status);
            if (previousRunningCount != runningInstanceCount)
                OnPropertyChanged(nameof(RunningInstanceCount));
            if (previousWaitingCount != waitingInstanceCount)
                OnPropertyChanged(nameof(WaitingInstanceCount));
        }
        if (changes.CanStopChanged)
            StopSelectedActiveInstancesCommand.RaiseCanExecuteChanged();
    }

    private void UpdateActiveStatusCount(InstanceExecutionStatus previousStatus, InstanceExecutionStatus currentStatus)
    {
        if (previousStatus == currentStatus) return;
        AdjustActiveStatusCount(previousStatus, -1);
        AdjustActiveStatusCount(currentStatus, 1);
    }

    private void AdjustActiveStatusCount(InstanceExecutionStatus status, int delta)
    {
        if (status == InstanceExecutionStatus.Running)
            runningInstanceCount += delta;
        else if (status is InstanceExecutionStatus.Queued or InstanceExecutionStatus.WaitingForLaunch)
            waitingInstanceCount += delta;
    }

    private void SetExecutionAggregateState()
    {
        var hasActiveExecution = executionSessions.Count > 0 || activeInstanceGroups.Count > 0;
        TaskCompletionSource? completed = null;
        if (hasActiveExecution)
        {
            executionTerminalCompletion ??=
                new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        else
        {
            completed = executionTerminalCompletion;
            executionTerminalCompletion = null;
        }

        IsExecuting = hasActiveExecution;
        OnPropertyChanged(nameof(RunningInstanceCount));
        OnPropertyChanged(nameof(WaitingInstanceCount));
        OnPropertyChanged(nameof(ActiveLaunchGroupCount));
        StopGroupCommand.RaiseCanExecuteChanged();
        RaiseCommandStates();
        completed?.TrySetResult();
    }

    private bool CanRun() => !isSafeShutdownRequested && CanUseMemuControls && !IsCapturing && IsPathValid && AreCurrentEditorInputsValid &&
        ResolveRequestedTargets().Count > 0 &&
        ValidateRunConfiguration() is null && AssignedScriptsHaveSteps();

    private bool CanAddStep() => SelectedScript is not null && CanMutateSteps &&
        StepEditorMode == RegularStepEditorMode.Create && IsRegularEditorDraftSemanticallyValid();

    private bool CanSaveStep() => SelectedScript is not null && SelectedStep is not null && CanMutateSteps &&
        StepEditorMode == RegularStepEditorMode.Edit &&
        !(SelectedStep.Model is DelayStep && EditorKind == ScriptStepKind.Delay) &&
        IsEditorDirty && IsRegularEditorDraftSemanticallyValid();

    private bool IsRegularEditorDraftSemanticallyValid()
    {
        if (HasInvalidRegularEditorDraft) return false;
        try
        {
            stepCommandBuilder.Validate(CreateStep(SelectedStep?.Id));
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }

    private bool CanRunAllRemaining()
    {
        if (isSafeShutdownRequested || !CanUseMemuControls || IsCapturing || !IsPathValid || !AreCurrentEditorInputsValid ||
            ValidateRunConfiguration() is not null)
            return false;
        var startNewSession = executionSessions.Count == 0 &&
                              (dynamicSessionUniverse.Count == 0 || dynamicSessionAdmitted.Count >= dynamicSessionUniverse.Count);
        var remaining = RunTargets.Where(item => item.IsRunning && !activeInstanceGroups.ContainsKey(item.Index) &&
            (startNewSession || !dynamicSessionAdmitted.Contains(item.Index))).Select(item => item.Model).ToList();
        var scripts = ResolveAssignedScripts(remaining);
        return remaining.Count > 0 && scripts is not null && scripts.Values.All(ScriptHasContent);
    }

    private async Task SaveScriptsAsync()
    {
        if (IsScriptPersistenceBlocked || scriptStore.IsWriteBlocked)
            throw new InvalidOperationException("Thư viện kịch bản đang bị khóa để bảo vệ dữ liệu gốc.");
        await scriptSaveGate.WaitAsync(CancellationToken.None);
        try
        {
            var snapshot = Scripts.Select(item => SnapshotScript(item.Model)).ToList();
            ScriptStepDisplayName.NormalizeDelayNames(snapshot);
            await scriptStore.SaveAsync(snapshot, CancellationToken.None);
            ScriptStepDisplayName.NormalizeDelayNames(Scripts.Select(item => item.Model));
            foreach (var item in Steps) item.NotifyCanonicalNameChanged();
        }
        finally { scriptSaveGate.Release(); }
    }

    private async Task SaveScriptsWithRollbackAsync(LibraryMutationTransaction transaction)
    {
        var persistence = BeginEditorPersistence();
        try { await SaveScriptsAsync(); }
        catch
        {
            persistence.Dispose();
            RestoreLibraryMutationTransaction(transaction);
            throw;
        }
        finally { persistence.Dispose(); }
    }

    private LibraryMutationTransaction CaptureLibraryMutationTransaction() => new(
        Scripts.ToList(),
        SelectedScript,
        CommonRunScript,
        ControlCenterSelectedScript,
        configuredCommonScriptId,
        GetSelectedStepsForMutation().Select(item => item.Id).ToList(),
        SelectedStep is not null && Steps.Contains(SelectedStep) ? SelectedStep.Id : null,
        GetSelectedCompositeItems().Select(item => item.Id).ToList(),
        SelectedCompositeItem is not null && CompositeItems.Contains(SelectedCompositeItem)
            ? SelectedCompositeItem.Id
            : null,
        RunTargets.ToDictionary(item => item.Index, item => item.AssignedScriptId),
        stepHistories.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<StepListSnapshot>)pair.Value.Undo.ToList()),
        compositeHistories.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<CompositeListSnapshot>)pair.Value.ToList()));

    private void RestoreLibraryMutationTransaction(LibraryMutationTransaction transaction)
    {
        Scripts.Clear();
        foreach (var item in transaction.Scripts) Scripts.Add(item);

        stepHistories.Clear();
        foreach (var pair in transaction.StepHistories)
        {
            var history = new StepHistory();
            foreach (var snapshot in pair.Value) history.Undo.AddLast(snapshot);
            stepHistories[pair.Key] = history;
        }

        compositeHistories.Clear();
        foreach (var pair in transaction.CompositeHistories)
            compositeHistories[pair.Key] = new LinkedList<CompositeListSnapshot>(pair.Value);

        SelectedScript = transaction.SelectedScript;
        if (SelectedScript?.Model.Kind == ScriptKind.Regular)
        {
            var ids = transaction.SelectedStepIds.ToHashSet();
            var selection = Steps.Where(item => ids.Contains(item.Id)).ToList();
            var primary = transaction.PrimaryStepId is Guid primaryId
                ? selection.FirstOrDefault(item => item.Id == primaryId)
                : selection.FirstOrDefault();
            SetStepSelection(selection, primary);
        }
        else if (SelectedScript?.Model.Kind == ScriptKind.Composite)
        {
            var ids = transaction.SelectedCompositeItemIds.ToHashSet();
            var selection = CompositeItems.Where(item => ids.Contains(item.Id)).ToList();
            var primary = transaction.PrimaryCompositeItemId is Guid primaryId
                ? selection.FirstOrDefault(item => item.Id == primaryId)
                : selection.FirstOrDefault();
            SetCompositeSelection(selection, primary);
        }

        configuredCommonScriptId = transaction.ConfiguredCommonScriptId;
        commonRunScript = transaction.CommonRunScript;
        OnPropertyChanged(nameof(CommonRunScript));
        controlCenterSelectedScript = transaction.ControlCenterSelectedScript;
        OnPropertyChanged(nameof(ControlCenterSelectedScript));
        OnPropertyChanged(nameof(BulkAssignmentScript));

        foreach (var target in RunTargets)
        {
            transaction.Assignments.TryGetValue(target.Index, out var scriptId);
            var script = scriptId is Guid id ? Scripts.FirstOrDefault(item => item.Id == id) : null;
            target.SetAssignedScript(script?.Id, script?.Name, script?.Model.Kind);
        }

        RefreshScriptCollections();
        UpdateRunConfigurationState();
        RaiseCommandStates();
    }

    private void SyncStepsToModel() { if (SelectedScript is not null) SyncStepsToModel(SelectedScript); }
    private void SyncStepsToModel(ScriptItemViewModel owner) { if (owner.Model.Kind == ScriptKind.Regular) { owner.Model.Steps.Clear(); owner.Model.Steps.AddRange(Steps.Select(item => item.Model)); } }
    private void TouchSelectedScript() { if (SelectedScript is null) return; SelectedScript.Model.UpdatedAt = DateTimeOffset.UtcNow; SelectedScript.Refresh(); }
    private static void TouchScript(ScriptItemViewModel owner) { owner.Model.UpdatedAt = DateTimeOffset.UtcNow; owner.Refresh(); }

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
        if (SelectedScript is null || !await ResolveRegularEditorChangesAsync() || !TryBeginStepMutation()) return;
        try
        {
            var transaction = CaptureStepMutationTransaction();
            var history = GetStepHistory(SelectedScript.Id);
            if (history.Undo.Count == 0) return;

            var target = history.Undo.Last!.Value;
            history.Undo.RemoveLast();
            ApplyStepListSnapshot(target);
            await PersistStepMutationCoreAsync(transaction);
            StatusMessage = "Đã hoàn tác thao tác danh sách bước.";
        }
        finally { EndStepMutation(); }
    }

    private StepListSnapshot CaptureStepListSnapshot() => new(
        Steps.Select(item => ScriptCloner.CloneStepPreservingId(item.Model)).ToList(),
        selectedSteps.Where(Steps.Contains).Select(item => item.Id).ToList(),
        SelectedStep is not null && Steps.Contains(SelectedStep) ? SelectedStep.Id : null);

    private StepMutationTransaction CaptureStepMutationTransaction()
    {
        var owner = SelectedScript ?? throw new InvalidOperationException("Chưa chọn kịch bản để thay đổi.");
        var hadHistory = stepHistories.TryGetValue(owner.Id, out var history);
        return new StepMutationTransaction(
            owner,
            CaptureStepListSnapshot(),
            owner.Model.UpdatedAt,
            hadHistory,
            history?.Undo.ToList() ?? []);
    }

    private void RestoreStepMutationTransaction(StepMutationTransaction transaction)
    {
        ApplyStepListSnapshot(transaction.Snapshot);
        SyncStepsToModel(transaction.Owner);
        transaction.Owner.Model.UpdatedAt = transaction.UpdatedAt;
        transaction.Owner.Refresh();

        if (!transaction.HadHistory)
        {
            stepHistories.Remove(transaction.Owner.Id);
        }
        else
        {
            var history = GetStepHistory(transaction.Owner.Id);
            history.Undo.Clear();
            foreach (var snapshot in transaction.History) history.Undo.AddLast(snapshot);
        }
        RaiseCommandStates();
    }

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
        Kind = source.Kind,
        DefaultInstanceIndex = source.DefaultInstanceIndex,
        UpdatedAt = source.UpdatedAt,
        Variables = source.Variables.Select(variable => new ScriptVariable
        {
            Name = variable.Name,
            Value = variable.Value,
            IsSecret = variable.IsSecret
        }).ToList(),
        Steps = source.Steps.Select(ScriptCloner.CloneStepPreservingId).ToList(),
        CompositeItems = source.CompositeItems.Select(ScriptCloner.CloneCompositeItemPreservingId).ToList()
    };

    private ScriptStep CreateStep(Guid? id)
    {
        var name = CanonicalizeStepName(EditorKind, EditorName);
        ScriptStep step = EditorKind switch
        {
            ScriptStepKind.AndroidShell => new AndroidShellStep { Id = id ?? Guid.NewGuid(), Name = name, Command = EditorCommand },
            ScriptStepKind.ForceStop => new ForceStopStep { Id = id ?? Guid.NewGuid(), Name = name, PackageName = EditorPackageName },
            ScriptStepKind.OpenApp => new OpenAppStep { Id = id ?? Guid.NewGuid(), Name = name, PackageName = EditorPackageName, ActivityName = EditorActivityName },
            ScriptStepKind.Delay => new DelayStep { Id = id ?? Guid.NewGuid(), Name = ScriptStepDisplayName.DelayCanonicalName, DurationMilliseconds = EditorDelayMilliseconds },
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
            ScriptStepKind.CloseChromeTabs => new CloseChromeTabsStep { Id = id ?? Guid.NewGuid(), Name = name },
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
            EditorKind = step.Kind;
            EditorName = step is DelayStep ? ScriptStepDisplayName.DelayCanonicalName : step.Name;
            EditorIsEnabled = step.IsEnabled;
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
        RefreshEditorDelayInput();
        UpdateSelectedStepDraftPreview();
        AcceptRegularEditorBaseline();
    }

    private void ResetEditor()
    {
        suppressEditorDirty = true;
        try { ResetEditorValues(); }
        finally { suppressEditorDirty = false; }
        RefreshEditorDelayInput();
        AcceptRegularEditorBaseline();
    }

    private void SetScriptPersistenceBlocked(bool value)
    {
        if (!SetProperty(ref isScriptPersistenceBlocked, value, nameof(IsScriptPersistenceBlocked))) return;
        RaiseCommandStates();
    }

    private void RefreshEditorDelayInput()
    {
        HasEditorBindingErrors = false;
        IsEditorDelayInputValid = true;
        editorDelayInputRefreshToken = unchecked(editorDelayInputRefreshToken + 1);
        OnPropertyChanged(nameof(EditorDelayInputRefreshToken));
    }

    private void ResetEditorValues()
    {
        EditorKind = ScriptStepKind.AndroidShell;
        EditorName = ScriptStepDisplayName.GetDefaultName(EditorKind);
        EditorIsEnabled = true;
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
            RefreshRegularEditorDirty();
            NotifyRegularEditorDraftContentChanged();
        }
        return true;
    }

    private static string CanonicalizeStepName(ScriptStepKind kind, string value) =>
        kind == ScriptStepKind.Delay
            ? ScriptStepDisplayName.DelayCanonicalName
            : string.IsNullOrWhiteSpace(value)
                ? ScriptStepDisplayName.GetDefaultName(kind)
                : value.Trim();

    private void ApplyCanonicalEditorName(string canonicalName)
    {
        if (string.Equals(EditorName, canonicalName, StringComparison.Ordinal)) return;
        suppressEditorDirty = true;
        try { EditorName = canonicalName; }
        finally { suppressEditorDirty = false; }
        NotifyRegularEditorDraftContentChanged();
    }

    private void NotifyRegularEditorDraftContentChanged()
    {
        UpdateSelectedStepDraftPreview();
        UpdatePreview();
        RaiseCommandStates();
    }

    private void UpdateSelectedStepDraftPreview() =>
        SelectedStep?.PreviewDraft(EditorKind, EditorDelayMilliseconds);

    private void DiscardEditorChanges()
    {
        editorVersion++;
        AcceptRegularEditorBaseline();
    }

    private bool HasInvalidRegularEditorDraft => HasEditorBindingErrors ||
        (EditorKind == ScriptStepKind.Delay && !IsEditorDelayInputValid);

    private bool AreCurrentEditorInputsValid => !HasEditorBindingErrors &&
        (!IsRegularScriptSelected || EditorKind != ScriptStepKind.Delay || IsEditorDelayInputValid) &&
        (!IsCompositeScriptSelected || SelectedCompositeItem?.Model is not CompositeDelayItem ||
         IsCompositeDelayInputValid);

    private void SetEditorDirty(bool value)
    {
        if (!SetProperty(ref isEditorDirty, value, nameof(IsEditorDirty))) return;
        OnPropertyChanged(nameof(HasRegularEditorDraft));
        OnPropertyChanged(nameof(EditorSaveState));
        OnPropertyChanged(nameof(HasAnyEditorDraft));
        OnPropertyChanged(nameof(RunConfigurationError));
        RaiseCommandStates();
    }

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
            CommandPreview = stepCommandBuilder.BuildPreview(
                draft,
                IsPathValid ? MemucPath : null,
                SelectedInstance?.Index);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or NotSupportedException)
        {
            CommandPreview = "Không thể xem trước: dữ liệu bước đang không hợp lệ.";
        }
    }

    private void RaiseCommandStates()
    {
        BrowseCommand?.RaiseCanExecuteChanged(); RefreshCommand?.RaiseCanExecuteChanged();
        CreateScriptCommand?.RaiseCanExecuteChanged(); RenameScriptCommand?.RaiseCanExecuteChanged();
        DuplicateScriptCommand?.RaiseCanExecuteChanged(); DeleteScriptCommand?.RaiseCanExecuteChanged();
        NewStepCommand?.RaiseCanExecuteChanged(); AddStepCommand?.RaiseCanExecuteChanged(); SaveStepCommand?.RaiseCanExecuteChanged();
        CancelStepCreateCommand?.RaiseCanExecuteChanged(); CancelScriptRenameCommand?.RaiseCanExecuteChanged();
        DuplicateStepCommand?.RaiseCanExecuteChanged(); DeleteStepCommand?.RaiseCanExecuteChanged();
        MoveStepUpCommand?.RaiseCanExecuteChanged(); MoveStepDownCommand?.RaiseCanExecuteChanged();
        UndoStepListCommand?.RaiseCanExecuteChanged();
        CopyStepsCommand?.RaiseCanExecuteChanged(); PasteStepsCommand?.RaiseCanExecuteChanged();
        RunCommand?.RaiseCanExecuteChanged(); RunAllRemainingCommand?.RaiseCanExecuteChanged(); StopCommand?.RaiseCanExecuteChanged(); StopSelectedActiveInstancesCommand?.RaiseCanExecuteChanged(); StopGroupCommand?.RaiseCanExecuteChanged();
        SelectApplicationCommand?.RaiseCanExecuteChanged();
        CaptureTapCommand?.RaiseCanExecuteChanged(); CaptureHoldCommand?.RaiseCanExecuteChanged(); CaptureSwipeCommand?.RaiseCanExecuteChanged();
        ExportSelectedScriptCommand?.RaiseCanExecuteChanged(); ExportAllScriptsCommand?.RaiseCanExecuteChanged(); ImportScriptsCommand?.RaiseCanExecuteChanged();
        RaiseCompositeCommandStates();
        RaiseWorkspaceCommandStates();
    }

    private static int CountRawShellSteps(
        ScriptDefinition script,
        IReadOnlyDictionary<Guid, ScriptDefinition> library) => script.Kind == ScriptKind.Regular
        ? script.Steps.Count(step => step.IsEnabled && step is AndroidShellStep)
        : script.CompositeItems.OfType<ScriptReferenceItem>()
            .Where(reference => reference.IsEnabled && library.ContainsKey(reference.ScriptId))
            .Sum(reference => CountRawShellSteps(library[reference.ScriptId], library));

    public void ReportUnexpectedError(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var logPath = ApplicationErrorReporter.Report(exception, "CommandFailure");
        var logHint = string.IsNullOrWhiteSpace(logPath) ? string.Empty : $" Chi tiết: {logPath}";
        StatusMessage = $"Thao tác không hoàn tất ({exception.Message}). Hãy kiểm tra dữ liệu hoặc quyền truy cập.{logHint}";
    }

    public void ReportInitializationError(Exception exception, string? logPath)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var logHint = string.IsNullOrWhiteSpace(logPath) ? string.Empty : $" Chi tiết: {logPath}";
        InitializationErrorMessage = $"Không thể khởi tạo đầy đủ ({exception.Message}). Giao diện vẫn dùng được; hãy chọn lại memuc.exe sau khi kiểm tra cấu hình và quyền truy cập.{logHint}";
        StatusMessage = InitializationErrorMessage;
        IsInitializing = false;
    }

    private void LogInitializationIssue(Exception exception)
    {
        try { startupIssueLogger?.Report(exception); }
        catch { }
    }

    private sealed class StepHistory
    {
        public LinkedList<StepListSnapshot> Undo { get; } = [];
    }

    private sealed record StepListSnapshot(
        IReadOnlyList<ScriptStep> Steps,
        IReadOnlyList<Guid> SelectedStepIds,
        Guid? PrimaryStepId);

    private sealed record StepMutationTransaction(
        ScriptItemViewModel Owner,
        StepListSnapshot Snapshot,
        DateTimeOffset UpdatedAt,
        bool HadHistory,
        IReadOnlyList<StepListSnapshot> History);

    private sealed record LibraryMutationTransaction(
        IReadOnlyList<ScriptItemViewModel> Scripts,
        ScriptItemViewModel? SelectedScript,
        ScriptItemViewModel? CommonRunScript,
        ScriptItemViewModel? ControlCenterSelectedScript,
        Guid? ConfiguredCommonScriptId,
        IReadOnlyList<Guid> SelectedStepIds,
        Guid? PrimaryStepId,
        IReadOnlyList<Guid> SelectedCompositeItemIds,
        Guid? PrimaryCompositeItemId,
        IReadOnlyDictionary<int, Guid?> Assignments,
        IReadOnlyDictionary<Guid, IReadOnlyList<StepListSnapshot>> StepHistories,
        IReadOnlyDictionary<Guid, IReadOnlyList<CompositeListSnapshot>> CompositeHistories);

}
