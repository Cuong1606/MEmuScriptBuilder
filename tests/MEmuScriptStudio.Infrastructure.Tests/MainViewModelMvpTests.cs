using MEmuScriptStudio.App.Services;
using MEmuScriptStudio.App.ViewModels;
using MEmuScriptStudio.App;
using MEmuScriptStudio.App.Converters;
using MEmuScriptStudio.Core.Execution;
using MEmuScriptStudio.Core.MEmu;
using MEmuScriptStudio.Core.Models;
using MEmuScriptStudio.Core.Scripts;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows;
using System.Windows.Input;

namespace MEmuScriptStudio.Infrastructure.Tests;

[TestClass]
public sealed class MainViewModelMvpTests
{
    [DataTestMethod]
    [DataRow(ScriptStepKind.AndroidShell, "Lệnh Android shell")]
    [DataRow(ScriptStepKind.ForceStop, "Buộc dừng ứng dụng")]
    [DataRow(ScriptStepKind.OpenApp, "Mở ứng dụng")]
    [DataRow(ScriptStepKind.Delay, "Chờ")]
    [DataRow(ScriptStepKind.Tap, "Chạm")]
    [DataRow(ScriptStepKind.Swipe, "Vuốt")]
    [DataRow(ScriptStepKind.InputText, "Nhập văn bản")]
    [DataRow(ScriptStepKind.KeyEvent, "Phím Android")]
    [DataRow(ScriptStepKind.Note, "Ghi chú")]
    public void ScriptStepKindDisplayConverter_ReturnsVietnameseLabel(ScriptStepKind kind, string expected)
    {
        var converter = new ScriptStepKindDisplayConverter();
        Assert.AreEqual(expected, converter.Convert(kind, typeof(string), null!, System.Globalization.CultureInfo.InvariantCulture));
    }

    [TestMethod]
    public void KeyEvents_AreOrderedAndDisplayedInVietnamese()
    {
        var viewModel = CreateViewModel(new RecordingScriptStore(), new ImmediateEngine());
        var converter = new AndroidKeyEventDisplayConverter();
        var expectedValues = new[]
        {
            AndroidKeyEvent.Home,
            AndroidKeyEvent.Back,
            AndroidKeyEvent.RecentApps,
            AndroidKeyEvent.Menu,
            AndroidKeyEvent.VolumeUp,
            AndroidKeyEvent.VolumeDown
        };
        var expectedLabels = new[]
        {
            "Trang chủ",
            "Quay lại",
            "Ứng dụng gần đây",
            "Menu (phím cũ)",
            "Tăng âm lượng",
            "Giảm âm lượng"
        };

        CollectionAssert.AreEqual(expectedValues, viewModel.KeyEvents.ToArray());
        CollectionAssert.AreEqual(
            expectedLabels,
            viewModel.KeyEvents.Select(key => (string)converter.Convert(
                key, typeof(string), null!, System.Globalization.CultureInfo.InvariantCulture)).ToArray());
    }

    [TestMethod]
    public async Task ApplicationPicker_SearchesAndRefreshesApplications()
    {
        var service = new MutableApplicationService(
        [
            new MemuApplicationInfo("com.android.chrome", ".Main"),
            new MemuApplicationInfo("com.example.notes", ".Notes", "Ghi chú")
        ]);
        var viewModel = new ApplicationPickerViewModel(service, @"C:\MEmu\memuc.exe", 3);

        await viewModel.RefreshAsync(CancellationToken.None);
        StringAssert.Contains(viewModel.StatusMessage, "1 ứng dụng chưa xác định được tên");
        Assert.AreEqual("Chưa xác định", viewModel.Applications[0].DisplayName);
        viewModel.SearchText = "chrome";
        Assert.AreEqual(1, viewModel.Applications.Count);
        Assert.AreEqual("com.android.chrome", viewModel.SelectedApplication!.PackageName);

        viewModel.SearchText = "Ghi chú";
        Assert.AreEqual("com.example.notes", viewModel.Applications.Single().PackageName);

        service.Applications = [new MemuApplicationInfo("com.example.game", ".Game", "Trò chơi")];
        viewModel.SearchText = string.Empty;
        await viewModel.RefreshAsync(CancellationToken.None);
        Assert.AreEqual("com.example.game", viewModel.Applications.Single().PackageName);
        Assert.AreEqual("Đã tải 1 ứng dụng.", viewModel.StatusMessage);

        service.Applications = [];
        await viewModel.RefreshAsync(CancellationToken.None);
        Assert.AreEqual(0, viewModel.Applications.Count);
        Assert.AreEqual("Không tìm thấy ứng dụng có launcher Activity.", viewModel.StatusMessage);
    }

    [STATestMethod]
    public void MainWindow_ReadOnlyTextBindings_AreExplicitlyOneWay()
    {
        if (Application.Current is null)
        {
            var application = new MEmuScriptStudio.App.App();
            application.InitializeComponent();
        }
        var window = new MainWindow(CreateViewModel(new RecordingScriptStore(), new ImmediateEngine()));
        var memucPath = (TextBox)window.FindName("MemucPathTextBox");
        var commandPreview = (TextBox)window.FindName("CommandPreviewTextBox");
        var stepsGrid = (DataGrid)window.FindName("StepsGrid");

        Assert.AreEqual(BindingMode.OneWay, BindingOperations.GetBinding(memucPath, TextBox.TextProperty)!.Mode);
        Assert.AreEqual(BindingMode.OneWay, BindingOperations.GetBinding(commandPreview, TextBox.TextProperty)!.Mode);
        Assert.IsFalse(stepsGrid.CanUserSortColumns, "Visual row indexes must stay aligned with persisted execution order during drag/drop.");
        foreach (var column in stepsGrid.Columns.OfType<DataGridTextColumn>())
            Assert.AreEqual(BindingMode.OneWay, ((Binding)column.Binding).Mode);

        var enabledColumn = (DataGridTemplateColumn)stepsGrid.Columns[2];
        var enabledCheckBox = (CheckBox)enabledColumn.CellTemplate.LoadContent();
        Assert.AreEqual(BindingMode.TwoWay, BindingOperations.GetBinding(enabledCheckBox, CheckBox.IsCheckedProperty)!.Mode);
    }

    [TestMethod]
    public async Task StepEnabledCheckbox_UpdatesModelAndAutosavesImmediately()
    {
        var store = new RecordingScriptStore([CreateThreeStepScript()]);
        var viewModel = CreateViewModel(store, new ImmediateEngine());
        await viewModel.InitializeAsync(CancellationToken.None);

        viewModel.Steps[0].IsEnabled = false;

        Assert.IsFalse(viewModel.SelectedScript!.Model.Steps[0].IsEnabled);
        Assert.AreEqual(1, store.SaveCount);
        Assert.IsFalse(viewModel.EditorIsEnabled);
    }

    [TestMethod]
    public async Task StepEnabledCheckbox_SynchronizesEditorBeforeSlowAutosaveCompletes()
    {
        var store = new BlockingSaveScriptStore([CreateThreeStepScript()]);
        var viewModel = CreateViewModel(store, new ImmediateEngine());
        await viewModel.InitializeAsync(CancellationToken.None);

        viewModel.Steps[0].IsEnabled = false;
        await store.SaveStarted.Task;

        Assert.IsFalse(viewModel.EditorIsEnabled);
        store.ReleaseSave.TrySetResult();
        await store.SaveCompleted.Task;
    }

    [TestMethod]
    public async Task MoveStepTo_ReordersAndAutosaves()
    {
        var store = new RecordingScriptStore([CreateThreeStepScript()]);
        var viewModel = CreateViewModel(store, new ImmediateEngine());
        await viewModel.InitializeAsync(CancellationToken.None);
        var first = viewModel.Steps[0];

        await viewModel.MoveStepToAsync(first, 3);

        CollectionAssert.AreEqual(new[] { "B", "C", "A" }, viewModel.Steps.Select(step => step.Name).ToArray());
        CollectionAssert.AreEqual(new[] { "B", "C", "A" }, store.LastSaved.Single().Steps.Select(step => step.Name).ToArray());
        Assert.AreEqual(1, store.SaveCount);
    }

    [TestMethod]
    public async Task CopyPaste_InsertsAfterSelectionWithNewIdAndAutosaves()
    {
        var store = new RecordingScriptStore([CreateThreeStepScript()]);
        var viewModel = CreateViewModel(store, new ImmediateEngine());
        await viewModel.InitializeAsync(CancellationToken.None);
        var source = viewModel.Steps[0];

        viewModel.CopySelectedStep();
        await viewModel.PasteCopiedStepAsync();

        Assert.AreEqual(4, viewModel.Steps.Count);
        Assert.AreEqual("A", viewModel.Steps[1].Name);
        Assert.AreNotEqual(source.Id, viewModel.Steps[1].Id);
        Assert.AreEqual(1, store.SaveCount);
    }

    [TestMethod]
    public async Task DeleteShortcut_UsesConfirmationAndAutosaves()
    {
        var store = new RecordingScriptStore([CreateThreeStepScript()]);
        var viewModel = CreateViewModel(store, new ImmediateEngine(), new ConfigurableConfirmation(true));
        await viewModel.InitializeAsync(CancellationToken.None);

        await viewModel.DeleteSelectedStepFromShortcutAsync();

        CollectionAssert.AreEqual(new[] { "B", "C" }, viewModel.Steps.Select(step => step.Name).ToArray());
        Assert.AreEqual(1, store.SaveCount);
    }

    [TestMethod]
    public void StepGridShortcutPolicy_DoesNotCaptureTextInputOrFocusOutsideGrid()
    {
        Assert.AreEqual(StepGridShortcut.None, StepGridShortcutPolicy.Resolve(false, false, true, Key.C, ModifierKeys.Control));
        Assert.AreEqual(StepGridShortcut.None, StepGridShortcutPolicy.Resolve(true, true, true, Key.V, ModifierKeys.Control));
        Assert.AreEqual(StepGridShortcut.None, StepGridShortcutPolicy.Resolve(true, false, false, Key.Delete, ModifierKeys.None));
        Assert.AreEqual(StepGridShortcut.Copy, StepGridShortcutPolicy.Resolve(true, false, true, Key.C, ModifierKeys.Control));
        Assert.AreEqual(StepGridShortcut.Paste, StepGridShortcutPolicy.Resolve(true, false, true, Key.V, ModifierKeys.Control));
        Assert.AreEqual(StepGridShortcut.Delete, StepGridShortcutPolicy.Resolve(true, false, true, Key.Delete, ModifierKeys.None));
    }

    [TestMethod]
    public async Task DirectToggleAndReorder_AreBlockedWhileScriptIsRunning()
    {
        var store = new RecordingScriptStore([CreateThreeStepScript()]);
        var engine = new BlockingEngine();
        var viewModel = CreateViewModel(store, engine);
        await viewModel.InitializeAsync(CancellationToken.None);
        viewModel.Instances.Add(new MemuInstance(0, "Target", true, 123, 456));
        viewModel.SelectedInstance = viewModel.Instances[0];
        var first = viewModel.Steps[0];
        var runTask = viewModel.RunCommand.ExecuteAsync();
        await engine.Started.Task;

        first.IsEnabled = false;
        await viewModel.MoveStepToAsync(first, 3);

        Assert.IsTrue(first.IsEnabled);
        CollectionAssert.AreEqual(new[] { "A", "B", "C" }, viewModel.Steps.Select(step => step.Name).ToArray());
        Assert.AreEqual(0, store.SaveCount);
        viewModel.StopCommand.Execute(null);
        await runTask;
    }

    [DataTestMethod]
    [DataRow(ScriptStepKind.AndroidShell, false, false, false, false, false, false, false, true, false)]
    [DataRow(ScriptStepKind.ForceStop, true, false, false, false, false, false, false, false, false)]
    [DataRow(ScriptStepKind.OpenApp, true, true, false, false, false, false, false, false, false)]
    [DataRow(ScriptStepKind.Delay, false, false, true, false, false, false, false, false, false)]
    [DataRow(ScriptStepKind.Tap, false, false, false, true, false, false, false, false, false)]
    [DataRow(ScriptStepKind.Swipe, false, false, false, false, true, false, false, false, false)]
    [DataRow(ScriptStepKind.InputText, false, false, false, false, false, true, false, false, false)]
    [DataRow(ScriptStepKind.KeyEvent, false, false, false, false, false, false, true, false, false)]
    [DataRow(ScriptStepKind.Note, false, false, false, false, false, false, false, false, true)]
    public void EditorKind_ShowsOnlyRelevantParameterGroup(
        ScriptStepKind kind,
        bool package,
        bool activity,
        bool delay,
        bool tap,
        bool swipe,
        bool inputText,
        bool keyEvent,
        bool androidShell,
        bool note)
    {
        var viewModel = CreateViewModel(new RecordingScriptStore(), new ImmediateEngine());

        viewModel.EditorKind = kind;

        Assert.AreEqual(package, viewModel.ShowPackageName);
        Assert.AreEqual(activity, viewModel.ShowActivityName);
        Assert.AreEqual(delay, viewModel.ShowDelay);
        Assert.AreEqual(tap, viewModel.ShowTap);
        Assert.AreEqual(swipe, viewModel.ShowSwipe);
        Assert.AreEqual(inputText, viewModel.ShowInputText);
        Assert.AreEqual(keyEvent, viewModel.ShowKeyEvent);
        Assert.AreEqual(androidShell, viewModel.ShowAndroidShell);
        Assert.AreEqual(note, viewModel.ShowNote);
        Assert.AreEqual(kind is not ScriptStepKind.Delay and not ScriptStepKind.Note, viewModel.ShowContinueOnError);
        Assert.AreEqual(kind is not ScriptStepKind.Delay and not ScriptStepKind.Note, viewModel.ShowTimeout);
    }

    [TestMethod]
    public async Task ScriptCommands_CreateRenameDuplicateDeleteAndAutosave()
    {
        var store = new RecordingScriptStore();
        var viewModel = CreateViewModel(store, new ImmediateEngine());
        await viewModel.InitializeAsync(CancellationToken.None);
        var initialSaveCount = store.SaveCount;

        await viewModel.CreateScriptCommand.ExecuteAsync();
        viewModel.ScriptName = "Automation";
        await viewModel.RenameScriptCommand.ExecuteAsync();
        var sourceId = viewModel.SelectedScript!.Id;
        await viewModel.DuplicateScriptCommand.ExecuteAsync();
        var cloneId = viewModel.SelectedScript!.Id;
        await viewModel.DeleteScriptCommand.ExecuteAsync();

        Assert.AreNotEqual(sourceId, cloneId);
        Assert.IsTrue(store.SaveCount >= initialSaveCount + 4);
        Assert.IsTrue(store.LastSaved.Any(script => script.Name == "Automation"));
        Assert.AreEqual(2, viewModel.Scripts.Count);
    }

    [TestMethod]
    public async Task StepCommands_AddEditDuplicateMoveDeleteAndAutosave()
    {
        var store = new RecordingScriptStore();
        var viewModel = CreateViewModel(store, new ImmediateEngine());
        await viewModel.InitializeAsync(CancellationToken.None);
        var originalCount = viewModel.Steps.Count;

        viewModel.NewStepCommand.Execute(null);
        viewModel.EditorKind = ScriptStepKind.Tap;
        viewModel.EditorName = "Tap login";
        viewModel.EditorX = 100;
        viewModel.EditorY = 200;
        viewModel.EditorIsEnabled = false;
        viewModel.EditorContinueOnError = true;
        await viewModel.SaveStepCommand.ExecuteAsync();
        var originalId = viewModel.SelectedStep!.Id;
        await viewModel.DuplicateStepCommand.ExecuteAsync();
        var cloneId = viewModel.SelectedStep!.Id;
        await viewModel.MoveStepUpCommand.ExecuteAsync();
        viewModel.EditorName = "Tap edited";
        await viewModel.SaveStepCommand.ExecuteAsync();
        await viewModel.DeleteStepCommand.ExecuteAsync();

        Assert.AreNotEqual(originalId, cloneId);
        Assert.AreEqual(originalCount + 1, viewModel.Steps.Count);
        var original = viewModel.Steps.Single(item => item.Id == originalId).Model;
        Assert.IsFalse(original.IsEnabled);
        Assert.IsTrue(original.ContinueOnError);
        Assert.IsTrue(store.SaveCount >= 5);
    }

    [TestMethod]
    public async Task RunCommand_UsesExactlySelectedInstance()
    {
        var engine = new ImmediateEngine();
        var viewModel = CreateViewModel(new RecordingScriptStore(), engine);
        await viewModel.InitializeAsync(CancellationToken.None);
        viewModel.Instances.Add(new MemuInstance(8, "Selected", true, 123));
        viewModel.SelectedInstance = viewModel.Instances[0];

        await viewModel.RunCommand.ExecuteAsync();

        Assert.IsNotNull(engine.LastRequest);
        Assert.AreEqual(8, engine.LastRequest.InstanceIndex);
        Assert.AreEqual(viewModel.SelectedScript!.Id, engine.LastRequest.Script.Id);
    }

    [TestMethod]
    public async Task StopCommand_CancelsRunningExecution()
    {
        var engine = new BlockingEngine();
        var viewModel = CreateViewModel(new RecordingScriptStore(), engine);
        await viewModel.InitializeAsync(CancellationToken.None);
        viewModel.Instances.Add(new MemuInstance(2, "Target", true, 456));
        viewModel.SelectedInstance = viewModel.Instances[0];

        var runTask = viewModel.RunCommand.ExecuteAsync();
        await engine.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        viewModel.StopCommand.Execute(null);
        await runTask.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.IsTrue(engine.WasCancelled);
        Assert.IsFalse(viewModel.IsExecuting);
    }

    [TestMethod]
    public async Task RunCommand_RawShellDeclined_DoesNotInvokeEngine()
    {
        var engine = new ImmediateEngine();
        var rawScript = new ScriptDefinition { Name = "Raw", Steps = { new AndroidShellStep { Name = "Raw", Command = "echo ok" } } };
        var store = new RecordingScriptStore([rawScript]);
        var viewModel = CreateViewModel(store, engine, new ConfigurableConfirmation(false));
        await viewModel.InitializeAsync(CancellationToken.None);
        viewModel.Instances.Add(new MemuInstance(3, "Target", true, 1));
        viewModel.SelectedInstance = viewModel.Instances[0];

        await viewModel.RunCommand.ExecuteAsync();

        Assert.IsNull(engine.LastRequest);
        StringAssert.Contains(viewModel.StatusMessage, "chưa được xác nhận");
    }

    [TestMethod]
    public async Task InitializeAsync_TemplateSaveFails_TemplateRemainsSelectedAndUsable()
    {
        var store = new RecordingScriptStore { ThrowOnSave = true };
        var viewModel = CreateViewModel(store, new ImmediateEngine());

        await viewModel.InitializeAsync(CancellationToken.None);

        Assert.IsNotNull(viewModel.SelectedScript);
        Assert.AreEqual("Khởi động lại Chrome", viewModel.SelectedScript.Name);
        Assert.AreEqual(3, viewModel.Steps.Count);
        StringAssert.Contains(viewModel.StatusMessage, "không thể lưu");
    }

    [TestMethod]
    public async Task SelectionCannotChangeWhileExecutionIsRunning()
    {
        var engine = new BlockingEngine();
        var viewModel = CreateViewModel(new RecordingScriptStore(), engine);
        await viewModel.InitializeAsync(CancellationToken.None);
        var executingScript = viewModel.SelectedScript;
        var otherScript = new ScriptItemViewModel(new ScriptDefinition { Name = "Other" });
        viewModel.Scripts.Add(otherScript);
        var target = new MemuInstance(2, "Target", true, 456);
        var otherTarget = new MemuInstance(4, "Other", true, 789);
        viewModel.Instances.Add(target);
        viewModel.Instances.Add(otherTarget);
        viewModel.SelectedInstance = target;

        var runTask = viewModel.RunCommand.ExecuteAsync();
        await engine.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        viewModel.SelectedScript = otherScript;
        viewModel.SelectedInstance = otherTarget;

        Assert.AreSame(executingScript, viewModel.SelectedScript);
        Assert.AreSame(target, viewModel.SelectedInstance);
        Assert.IsFalse(viewModel.CanChangeSelection);
        viewModel.StopCommand.Execute(null);
        await runTask.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [TestMethod]
    public async Task LateProgressFromCompletedRun_IsIgnored()
    {
        var engine = new LateReportingEngine();
        var viewModel = CreateViewModel(new RecordingScriptStore(), engine);
        await viewModel.InitializeAsync(CancellationToken.None);
        viewModel.Instances.Add(new MemuInstance(2, "Target", true, 456));
        viewModel.SelectedInstance = viewModel.Instances[0];

        await viewModel.RunCommand.ExecuteAsync();
        engine.ReportLate(viewModel.SelectedScript!.Model.Steps[0].Id);

        Assert.AreEqual(0, viewModel.ExecutionLog.Count);
    }

    [TestMethod]
    public async Task SelectApplication_FillsPackageAndOnlyOpenAppFillsActivity()
    {
        var picker = new FixedApplicationPicker(new MemuApplicationInfo("com.example.app", ".Launcher"));
        var viewModel = CreateViewModel(new RecordingScriptStore(), new ImmediateEngine(), picker: picker);
        await viewModel.InitializeAsync(CancellationToken.None);
        viewModel.Instances.Add(new MemuInstance(6, "Target", true, 1, 123));
        viewModel.SelectedInstance = viewModel.Instances[0];

        viewModel.EditorKind = ScriptStepKind.OpenApp;
        await viewModel.SelectApplicationCommand.ExecuteAsync();
        Assert.AreEqual("com.example.app", viewModel.EditorPackageName);
        Assert.AreEqual(".Launcher", viewModel.EditorActivityName);

        viewModel.EditorKind = ScriptStepKind.ForceStop;
        viewModel.EditorActivityName = "keep";
        await viewModel.SelectApplicationCommand.ExecuteAsync();
        Assert.AreEqual("com.example.app", viewModel.EditorPackageName);
        Assert.AreEqual("keep", viewModel.EditorActivityName);
        Assert.AreEqual(6, picker.LastInstanceIndex);
    }

    [TestMethod]
    public async Task CaptureCommands_FillTapAndSwipeFieldsWithoutExecutingScript()
    {
        var overlay = new RecordingSwipeOverlay();
        var capture = new FixedInputCapture(
            new CapturedTap(120, 340),
            new CapturedSwipe(10, 20, 300, 400));
        var engine = new ImmediateEngine();
        var viewModel = CreateViewModel(new RecordingScriptStore(), engine, capture: capture, overlay: overlay);
        await viewModel.InitializeAsync(CancellationToken.None);
        viewModel.Instances.Add(new MemuInstance(2, "Target", true, 456, 998877));
        viewModel.SelectedInstance = viewModel.Instances[0];

        viewModel.EditorKind = ScriptStepKind.Tap;
        await viewModel.CaptureTapCommand.ExecuteAsync();
        Assert.AreEqual(120, viewModel.EditorX);
        Assert.AreEqual(340, viewModel.EditorY);

        viewModel.EditorKind = ScriptStepKind.Swipe;
        viewModel.EditorSwipeDuration = 650;
        await viewModel.CaptureSwipeCommand.ExecuteAsync();
        Assert.AreEqual(10, viewModel.EditorX);
        Assert.AreEqual(20, viewModel.EditorY);
        Assert.AreEqual(300, viewModel.EditorX2);
        Assert.AreEqual(400, viewModel.EditorY2);
        Assert.AreEqual(650, viewModel.EditorSwipeDuration, "Capture must preserve the manually entered duration.");
        Assert.AreEqual(1, overlay.Updates.Count);
        Assert.IsTrue(overlay.WasDisposed);
        Assert.IsNull(engine.LastRequest);
    }

    [TestMethod]
    public async Task Capture_LocksEditorContextUntilResultIsApplied()
    {
        var capture = new BlockingInputCapture();
        var viewModel = CreateViewModel(new RecordingScriptStore(), new ImmediateEngine(), capture: capture);
        await viewModel.InitializeAsync(CancellationToken.None);
        viewModel.Instances.Add(new MemuInstance(2, "Target", true, 456, 998877));
        viewModel.SelectedInstance = viewModel.Instances[0];
        viewModel.EditorKind = ScriptStepKind.Tap;
        var originalStep = viewModel.SelectedStep;

        var captureTask = viewModel.CaptureTapCommand.ExecuteAsync();
        await capture.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        viewModel.SelectedStep = viewModel.Steps[1];
        viewModel.EditorKind = ScriptStepKind.Swipe;

        Assert.AreSame(originalStep, viewModel.SelectedStep);
        Assert.AreEqual(ScriptStepKind.Tap, viewModel.EditorKind);
        Assert.IsFalse(viewModel.CanChangeSelection);
        capture.TapResult.TrySetResult(new CapturedTap(11, 22));
        await captureTask;
        Assert.AreEqual(11, viewModel.EditorX);
        Assert.AreEqual(22, viewModel.EditorY);
    }

    [TestMethod]
    public async Task SaveStep_InvalidResolvedActivityDoesNotMutateSelectedStep()
    {
        var viewModel = CreateViewModel(new RecordingScriptStore(), new ImmediateEngine());
        await viewModel.InitializeAsync(CancellationToken.None);
        var originalModel = viewModel.SelectedStep!.Model;
        viewModel.EditorKind = ScriptStepKind.OpenApp;
        viewModel.EditorPackageName = "com.example.app";
        viewModel.EditorActivityName = ".Main$Alias";

        await viewModel.SaveStepCommand.ExecuteAsync();

        Assert.AreSame(originalModel, viewModel.SelectedStep.Model);
        StringAssert.Contains(viewModel.StatusMessage, "không hoàn tất");
    }

    private static ScriptDefinition CreateThreeStepScript() => new()
    {
        Name = "Steps",
        Steps =
        [
            new NoteStep { Name = "A", Text = "A" },
            new NoteStep { Name = "B", Text = "B" },
            new NoteStep { Name = "C", Text = "C" }
        ]
    };

    private static MainViewModel CreateViewModel(
        IScriptStore store,
        IScriptExecutionEngine engine,
        IConfirmationService? confirmation = null,
        IApplicationPickerService? picker = null,
        IMemuInputCaptureService? capture = null,
        ISwipeCaptureOverlayService? overlay = null) => new(
        new EmptyInstanceService(), new ValidPathDiscovery(), new MemorySettingsStore(), new SelectedFileDialog(),
        store, engine, new ScriptStepCommandBuilder(new MemuCommandBuilder()), confirmation ?? new AlwaysConfirm(),
        picker ?? new NoopApplicationPicker(), capture ?? new NoopInputCapture(), overlay ?? new NoopSwipeOverlay());

    private sealed class RecordingScriptStore : IScriptStore
    {
        private readonly IReadOnlyList<ScriptDefinition> loaded;
        public RecordingScriptStore(IReadOnlyList<ScriptDefinition>? loaded = null) => this.loaded = loaded ?? [];
        public int SaveCount { get; private set; }
        public bool ThrowOnSave { get; init; }
        public IReadOnlyList<ScriptDefinition> LastSaved { get; private set; } = [];
        public Task<IReadOnlyList<ScriptDefinition>> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(loaded);
        public Task SaveAsync(IReadOnlyCollection<ScriptDefinition> scripts, CancellationToken cancellationToken)
        {
            if (ThrowOnSave) throw new IOException("read-only");
            SaveCount++;
            LastSaved = scripts.ToList();
            return Task.CompletedTask;
        }
    }

    private sealed class BlockingSaveScriptStore(IReadOnlyList<ScriptDefinition> loaded) : IScriptStore
    {
        public TaskCompletionSource SaveStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseSave { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource SaveCompleted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<IReadOnlyList<ScriptDefinition>> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(loaded);

        public async Task SaveAsync(IReadOnlyCollection<ScriptDefinition> scripts, CancellationToken cancellationToken)
        {
            SaveStarted.TrySetResult();
            await ReleaseSave.Task.WaitAsync(cancellationToken);
            SaveCompleted.TrySetResult();
        }
    }

    private sealed class ImmediateEngine : IScriptExecutionEngine
    {
        public ExecutionRequest? LastRequest { get; private set; }
        public Task<ExecutionResult> ExecuteAsync(ExecutionRequest request, IProgress<StepExecutionUpdate>? progress, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(new ExecutionResult { StartedAt = DateTimeOffset.UtcNow, EndedAt = DateTimeOffset.UtcNow });
        }
    }

    private sealed class BlockingEngine : IScriptExecutionEngine
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool WasCancelled { get; private set; }
        public async Task<ExecutionResult> ExecuteAsync(ExecutionRequest request, IProgress<StepExecutionUpdate>? progress, CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
            catch (OperationCanceledException) { WasCancelled = true; }
            return new ExecutionResult { StartedAt = DateTimeOffset.UtcNow, EndedAt = DateTimeOffset.UtcNow, WasCancelled = WasCancelled };
        }
    }

    private sealed class LateReportingEngine : IScriptExecutionEngine
    {
        private IProgress<StepExecutionUpdate>? progress;
        public Task<ExecutionResult> ExecuteAsync(ExecutionRequest request, IProgress<StepExecutionUpdate>? progress, CancellationToken cancellationToken)
        {
            this.progress = progress;
            return Task.FromResult(new ExecutionResult { StartedAt = DateTimeOffset.UtcNow, EndedAt = DateTimeOffset.UtcNow });
        }

        public void ReportLate(Guid stepId)
        {
            var now = DateTimeOffset.UtcNow;
            progress?.Report(new StepExecutionUpdate(stepId, StepExecutionStatus.Succeeded, new StepExecutionResult
            {
                StepId = stepId,
                Status = StepExecutionStatus.Succeeded,
                StartedAt = now,
                EndedAt = now,
                CommandPreview = "late"
            }));
        }
    }

    private sealed class EmptyInstanceService : IMemuInstanceService
    {
        public Task<IReadOnlyList<MemuInstance>> GetInstancesAsync(string memucPath, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<MemuInstance>>([]);
    }
    private sealed class ValidPathDiscovery : IMemucPathDiscovery
    {
        public string FindMemucPath() => @"C:\MEmu\memuc.exe";
        public bool IsValidMemucPath(string? path) => !string.IsNullOrWhiteSpace(path);
    }
    private sealed class MemorySettingsStore : ISettingsStore
    {
        public Task<ApplicationSettings> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(new ApplicationSettings { MemucPath = @"C:\MEmu\memuc.exe" });
        public Task SaveAsync(ApplicationSettings settings, CancellationToken cancellationToken) => Task.CompletedTask;
    }
    private sealed class SelectedFileDialog : IFileDialogService { public string? SelectMemucPath(string? currentPath) => null; }
    private sealed class AlwaysConfirm : IConfirmationService { public bool Confirm(string message, string title) => true; }
    private sealed class ConfigurableConfirmation(bool result) : IConfirmationService
    {
        public bool Confirm(string message, string title) => result;
    }
    private sealed class NoopApplicationPicker : IApplicationPickerService
    {
        public Task<MemuApplicationInfo?> SelectAsync(string memucPath, int instanceIndex, CancellationToken cancellationToken) =>
            Task.FromResult<MemuApplicationInfo?>(null);
    }
    private sealed class FixedApplicationPicker(MemuApplicationInfo application) : IApplicationPickerService
    {
        public int? LastInstanceIndex { get; private set; }
        public Task<MemuApplicationInfo?> SelectAsync(string memucPath, int instanceIndex, CancellationToken cancellationToken)
        {
            LastInstanceIndex = instanceIndex;
            return Task.FromResult<MemuApplicationInfo?>(application);
        }
    }
    private sealed class MutableApplicationService(IReadOnlyList<MemuApplicationInfo> applications) : IMemuApplicationService
    {
        public IReadOnlyList<MemuApplicationInfo> Applications { get; set; } = applications;
        public Task<IReadOnlyList<MemuApplicationInfo>> GetApplicationsAsync(string memucPath, int instanceIndex, CancellationToken cancellationToken) =>
            Task.FromResult(Applications);
    }
    private sealed class NoopInputCapture : IMemuInputCaptureService
    {
        public Task<CapturedTap> CaptureTapAsync(string memucPath, MemuInstance instance, CancellationToken cancellationToken) =>
            Task.FromResult(new CapturedTap(0, 0));
        public Task<CapturedSwipe> CaptureSwipeAsync(string memucPath, MemuInstance instance, IProgress<SwipeCaptureUpdate>? progress, CancellationToken cancellationToken) =>
            Task.FromResult(new CapturedSwipe(0, 0, 0, 0));
    }
    private sealed class FixedInputCapture(CapturedTap tap, CapturedSwipe swipe) : IMemuInputCaptureService
    {
        public Task<CapturedTap> CaptureTapAsync(string memucPath, MemuInstance instance, CancellationToken cancellationToken) => Task.FromResult(tap);
        public Task<CapturedSwipe> CaptureSwipeAsync(string memucPath, MemuInstance instance, IProgress<SwipeCaptureUpdate>? progress, CancellationToken cancellationToken)
        {
            progress?.Report(new SwipeCaptureUpdate(new ScreenRectangle(100, 200, 540, 960), 1080, 1920, new ScreenPoint(swipe.X1, swipe.Y1), new ScreenPoint(swipe.X2, swipe.Y2)));
            return Task.FromResult(swipe);
        }
    }
    private sealed class BlockingInputCapture : IMemuInputCaptureService
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<CapturedTap> TapResult { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<CapturedTap> CaptureTapAsync(string memucPath, MemuInstance instance, CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            return TapResult.Task;
        }
        public Task<CapturedSwipe> CaptureSwipeAsync(string memucPath, MemuInstance instance, IProgress<SwipeCaptureUpdate>? progress, CancellationToken cancellationToken) =>
            Task.FromResult(new CapturedSwipe(0, 0, 0, 0));
    }

    private sealed class NoopSwipeOverlay : ISwipeCaptureOverlayService
    {
        public ISwipeCaptureOverlaySession Show() => new Session();
        private sealed class Session : ISwipeCaptureOverlaySession
        {
            public void Report(SwipeCaptureUpdate value) { }
            public void Dispose() { }
        }
    }

    private sealed class RecordingSwipeOverlay : ISwipeCaptureOverlayService, ISwipeCaptureOverlaySession
    {
        public List<SwipeCaptureUpdate> Updates { get; } = [];
        public bool WasDisposed { get; private set; }
        public ISwipeCaptureOverlaySession Show() => this;
        public void Report(SwipeCaptureUpdate value) => Updates.Add(value);
        public void Dispose() => WasDisposed = true;
    }
}
