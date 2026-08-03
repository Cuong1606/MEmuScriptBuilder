using MEmuScriptStudio.App.Services;
using MEmuScriptStudio.App.ViewModels;
using MEmuScriptStudio.App;
using MEmuScriptStudio.App.Converters;
using MEmuScriptStudio.Core.Execution;
using MEmuScriptStudio.Core.MEmu;
using MEmuScriptStudio.Core.Models;
using MEmuScriptStudio.Core.Scripts;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows;
using System.Windows.Input;
using System.Windows.Shapes;
using System.Windows.Threading;

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
    [DataRow(ScriptStepKind.Hold, "Nhấn giữ")]
    [DataRow(ScriptStepKind.Swipe, "Vuốt")]
    [DataRow(ScriptStepKind.InputText, "Nhập văn bản")]
    [DataRow(ScriptStepKind.AndroidClipboardPaste, "Dán clipboard Android")]
    [DataRow(ScriptStepKind.KeyEvent, "Phím Android")]
    [DataRow(ScriptStepKind.Note, "Ghi chú — không thực thi")]
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

    [TestMethod]
    public async Task ApplicationPicker_UsesForegroundAndManualDisplayNameOverride()
    {
        var applications = new MutableApplicationService(
            [new MemuApplicationInfo("com.example.listed", ".Launcher", "Listed")]);
        var foreground = new FixedForegroundApplicationService(
            new MemuApplicationInfo("com.example.foreground", ".Current"));
        var overrides = new Dictionary<string, string> { ["com.example.foreground"] = "Tên đã nhớ" };
        var viewModel = new ApplicationPickerViewModel(
            applications, @"C:\MEmu\memuc.exe", 3, foreground, overrides);

        await viewModel.RefreshAsync(CancellationToken.None);
        await viewModel.UseForegroundApplicationAsync(CancellationToken.None);

        Assert.AreEqual("com.example.foreground", viewModel.SelectedApplication!.PackageName);
        Assert.AreEqual(".Current", viewModel.SelectedApplication.ActivityName);
        Assert.AreEqual("Tên đã nhớ", viewModel.SelectedApplication.DisplayName);
        Assert.AreEqual("Tên đã nhớ", viewModel.ManualDisplayName);

        viewModel.ManualDisplayName = "Tên thủ công mới";
        Assert.AreEqual("Tên thủ công mới", viewModel.CreateSelection()!.DisplayName);
        Assert.AreEqual(3, foreground.InstanceIndex);
    }

    [TestMethod]
    public async Task ApplicationPicker_ForegroundReplacesLauncherActivityForSamePackage()
    {
        var applications = new MutableApplicationService(
            [new MemuApplicationInfo("com.example.app", ".Launcher", "Example")]);
        var foreground = new FixedForegroundApplicationService(
            new MemuApplicationInfo("com.example.app", ".CurrentActivity"));
        var viewModel = new ApplicationPickerViewModel(
            applications, @"C:\MEmu\memuc.exe", 1, foreground);

        await viewModel.RefreshAsync(CancellationToken.None);
        await viewModel.UseForegroundApplicationAsync(CancellationToken.None);

        Assert.AreEqual(".CurrentActivity", viewModel.SelectedApplication!.ActivityName);
        Assert.AreEqual(".CurrentActivity", viewModel.CreateSelection()!.ActivityName);
    }

    [TestMethod]
    public async Task ApplicationNameLibrary_SaveAndDeleteAreExplicitAndRestoreNativeLabel()
    {
        var applications = new MutableApplicationService(
            [new MemuApplicationInfo("com.example.app", ".Launcher", "Tên Android")]);
        var settings = new ApplicationSettings
        {
            MemucPath = @"C:\MEmu\memuc.exe",
            MultiInstanceRun = new MultiInstanceRunSettings
            {
                LaunchSpacingMode = LaunchSpacingMode.Random,
                RandomMinimumSpacingMilliseconds = 25,
                RandomMaximumSpacingMilliseconds = 75
            }
        };
        settings.ApplicationDisplayNames["com.example.other"] = "Tên khác";
        var store = new RecordingApplicationSettingsStore(settings);
        var viewModel = CreateApplicationNameLibraryViewModel(applications, settings, store);
        await viewModel.RefreshAsync(CancellationToken.None);

        viewModel.ManualDisplayName = "  Tên đã lưu  ";
        Assert.AreEqual("Tên đã lưu", viewModel.CreateSelection()!.DisplayName,
            "A draft may be selected without being persisted implicitly.");
        Assert.AreEqual(0, store.SaveCount);

        await viewModel.SaveNameAsync(CancellationToken.None);

        Assert.AreEqual(1, store.SaveCount);
        Assert.AreEqual("Tên đã lưu", settings.ApplicationDisplayNames["com.example.app"]);
        Assert.AreEqual("Tên khác", settings.ApplicationDisplayNames["com.example.other"]);
        Assert.AreEqual(@"C:\MEmu\memuc.exe", store.LastSaved!.MemucPath);
        Assert.AreEqual(LaunchSpacingMode.Random, store.LastSaved.MultiInstanceRun.LaunchSpacingMode);
        Assert.AreEqual(25, store.LastSaved.MultiInstanceRun.RandomMinimumSpacingMilliseconds);
        Assert.AreEqual(75, store.LastSaved.MultiInstanceRun.RandomMaximumSpacingMilliseconds);
        Assert.AreEqual("Tên đã lưu", viewModel.SelectedApplication!.DisplayName);

        await viewModel.DeleteSavedNameAsync(CancellationToken.None);

        Assert.AreEqual(2, store.SaveCount);
        Assert.IsFalse(settings.ApplicationDisplayNames.ContainsKey("com.example.app"));
        Assert.AreEqual("Tên Android", viewModel.SelectedApplication!.DisplayName);
        Assert.AreEqual(string.Empty, viewModel.ManualDisplayName);
    }

    [TestMethod]
    public async Task ApplicationNameLibrary_ImportOverwriteSkipAndAddSavesOnce()
    {
        var applications = new MutableApplicationService(
            [new MemuApplicationInfo("com.example.a", ".A", "Android A")]);
        var settings = new ApplicationSettings();
        settings.ApplicationDisplayNames["com.example.a"] = "Cũ A";
        settings.ApplicationDisplayNames["com.example.b"] = "Giữ B";
        var store = new RecordingApplicationSettingsStore(settings);
        var transfer = new RecordingApplicationNameTransferService(new Dictionary<string, string>
        {
            ["com.example.a"] = "Mới A",
            ["com.example.b"] = "Mới B",
            ["com.example.c"] = "Mới C"
        });
        var conflicts = new QueueApplicationNameConflict(
            ApplicationNameImportConflictResolution.Overwrite,
            ApplicationNameImportConflictResolution.Skip);
        var dialogs = new ApplicationNameFileDialog(importPath: @"C:\Temp\names.memuappnames", exportPath: null);
        var viewModel = CreateApplicationNameLibraryViewModel(applications, settings, store, dialogs, transfer, conflicts);
        await viewModel.RefreshAsync(CancellationToken.None);

        await viewModel.ImportNamesAsync(CancellationToken.None);

        Assert.AreEqual(1, store.SaveCount);
        Assert.AreEqual("Mới A", settings.ApplicationDisplayNames["com.example.a"]);
        Assert.AreEqual("Giữ B", settings.ApplicationDisplayNames["com.example.b"]);
        Assert.AreEqual("Mới C", settings.ApplicationDisplayNames["com.example.c"]);
        Assert.AreEqual("Mới A", viewModel.SelectedApplication!.DisplayName);
        Assert.AreEqual(2, conflicts.Calls.Count);
        StringAssert.Contains(viewModel.StatusMessage, "Đã nhập 1 tên mới, ghi đè 1, bỏ qua 1");
    }

    [TestMethod]
    public async Task ApplicationNameLibrary_ImportCancelIsAtomicAndDoesNotSave()
    {
        var applications = new MutableApplicationService(
            [new MemuApplicationInfo("com.example.a", ".A", "Android A")]);
        var settings = new ApplicationSettings();
        settings.ApplicationDisplayNames["com.example.a"] = "Cũ A";
        settings.ApplicationDisplayNames["com.example.b"] = "Cũ B";
        var store = new RecordingApplicationSettingsStore(settings);
        var transfer = new RecordingApplicationNameTransferService(new Dictionary<string, string>
        {
            ["com.example.0new"] = "Tên mới",
            ["com.example.a"] = "Mới A",
            ["com.example.b"] = "Mới B"
        });
        var conflicts = new QueueApplicationNameConflict(
            ApplicationNameImportConflictResolution.Overwrite,
            ApplicationNameImportConflictResolution.Cancel);
        var dialogs = new ApplicationNameFileDialog(importPath: @"C:\Temp\names.memuappnames", exportPath: null);
        var viewModel = CreateApplicationNameLibraryViewModel(applications, settings, store, dialogs, transfer, conflicts);
        await viewModel.RefreshAsync(CancellationToken.None);

        await viewModel.ImportNamesAsync(CancellationToken.None);

        Assert.AreEqual(0, store.SaveCount);
        Assert.AreEqual(2, settings.ApplicationDisplayNames.Count);
        Assert.AreEqual("Cũ A", settings.ApplicationDisplayNames["com.example.a"]);
        Assert.AreEqual("Cũ B", settings.ApplicationDisplayNames["com.example.b"]);
        Assert.IsFalse(settings.ApplicationDisplayNames.ContainsKey("com.example.0new"));
        StringAssert.Contains(viewModel.StatusMessage, "không có thay đổi nào được lưu");
    }

    [TestMethod]
    public async Task ApplicationNameLibrary_ExportIncludesGlobalNamesOutsideCurrentInstance()
    {
        var applications = new MutableApplicationService(
            [new MemuApplicationInfo("com.example.current", ".Current", "Current")]);
        var settings = new ApplicationSettings();
        settings.ApplicationDisplayNames["com.example.current"] = "Tên hiện tại";
        settings.ApplicationDisplayNames["com.example.other-instance"] = "Tên giả lập khác";
        var store = new RecordingApplicationSettingsStore(settings);
        var transfer = new RecordingApplicationNameTransferService(new Dictionary<string, string>());
        var dialogs = new ApplicationNameFileDialog(importPath: null, exportPath: @"C:\Temp\names.memuappnames");
        var viewModel = CreateApplicationNameLibraryViewModel(
            applications, settings, store, dialogs, transfer, new QueueApplicationNameConflict());

        await viewModel.ExportNamesAsync(CancellationToken.None);

        Assert.AreEqual(@"C:\Temp\names.memuappnames", transfer.ExportPath);
        Assert.AreEqual(2, transfer.ExportedNames!.Count);
        Assert.AreEqual("Tên giả lập khác", transfer.ExportedNames["com.example.other-instance"]);
        Assert.AreEqual(0, store.SaveCount);
    }

    [TestMethod]
    public void ApplicationPickerShortcutPolicy_MapsOnlyExactCtrlS()
    {
        Assert.IsTrue(ApplicationPickerShortcutPolicy.IsSaveShortcut(Key.S, ModifierKeys.Control));
        Assert.IsFalse(ApplicationPickerShortcutPolicy.IsSaveShortcut(Key.S, ModifierKeys.Control | ModifierKeys.Shift));
        Assert.IsFalse(ApplicationPickerShortcutPolicy.IsSaveShortcut(Key.Z, ModifierKeys.Control));
    }

    [STATestMethod]
    public void ApplicationPickerWindow_WiresExplicitNameActionsAndCtrlS()
    {
        if (Application.Current is null)
        {
            var application = new MEmuScriptStudio.App.App();
            application.InitializeComponent();
        }

        Assert.AreEqual(
            ShutdownMode.OnExplicitShutdown,
            Application.Current!.ShutdownMode,
            "Async startup must not use implicit window shutdown before MainWindow exists.");

        var viewModel = new ApplicationPickerViewModel(
            new MutableApplicationService([]), @"C:\MEmu\memuc.exe", 0);
        var window = new ApplicationPickerWindow(viewModel);

        Assert.IsNotNull(window.FindName("ManualDisplayNameTextBox"));
        Assert.IsNotNull(window.FindName("SaveApplicationNameButton"));
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
        var pressEnter = (CheckBox)window.FindName("PressEnterAfterInputCheckBox");
        var workspace = (Grid)window.FindName("WorkspaceRoot");
        var initializationOverlay = (Border)window.FindName("InitializationOverlay");

        Assert.AreEqual(BindingMode.OneWay, BindingOperations.GetBinding(memucPath, TextBox.TextProperty)!.Mode);
        Assert.AreEqual(BindingMode.OneWay, BindingOperations.GetBinding(commandPreview, TextBox.TextProperty)!.Mode);
        Assert.AreEqual(nameof(MainViewModel.CanUseApplication), BindingOperations.GetBinding(workspace, UIElement.IsEnabledProperty)!.Path.Path);
        Assert.AreEqual(nameof(MainViewModel.IsStartupOverlayVisible), BindingOperations.GetBinding(initializationOverlay, UIElement.VisibilityProperty)!.Path.Path);
        Assert.IsFalse(stepsGrid.CanUserSortColumns, "Visual row indexes must stay aligned with persisted execution order during drag/drop.");
        Assert.AreEqual(DataGridSelectionMode.Extended, stepsGrid.SelectionMode);
        Assert.AreEqual(DataGridSelectionUnit.FullRow, stepsGrid.SelectionUnit);
        Assert.AreEqual(BindingMode.OneWay, BindingOperations.GetBinding(stepsGrid, DataGrid.SelectedItemProperty)!.Mode);
        Assert.AreEqual(
            nameof(MainViewModel.EditorPressEnterAfterInput),
            BindingOperations.GetBinding(pressEnter, CheckBox.IsCheckedProperty)!.Path.Path);
        foreach (var column in stepsGrid.Columns.OfType<DataGridTextColumn>())
            Assert.AreEqual(BindingMode.OneWay, ((Binding)column.Binding).Mode);

        var enabledColumn = (DataGridTemplateColumn)stepsGrid.Columns[2];
        var enabledCheckBox = (CheckBox)enabledColumn.CellTemplate.LoadContent();
        Assert.AreEqual(BindingMode.TwoWay, BindingOperations.GetBinding(enabledCheckBox, CheckBox.IsCheckedProperty)!.Mode);
    }

    [STATestMethod]
    public void StepsGrid_ExtendedSelection_SynchronizesBulkDeleteAndNextSelection()
    {
        if (Application.Current is null)
        {
            var application = new MEmuScriptStudio.App.App();
            application.InitializeComponent();
        }

        var store = new RecordingScriptStore([CreateThreeStepScript()]);
        var confirmation = new ConfigurableConfirmation(true);
        var viewModel = CreateViewModel(store, new ImmediateEngine(), confirmation);
        viewModel.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();
        var window = new MainWindow(viewModel);
        try
        {
            var stepsGrid = (DataGrid)window.FindName("StepsGrid");
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.DataBind);

            stepsGrid.SelectedItems.Clear();
            stepsGrid.SelectedItems.Add(viewModel.Steps[0]);
            stepsGrid.SelectedItems.Add(viewModel.Steps[2]);
            Assert.AreEqual(2, viewModel.SelectedStepCount);

            stepsGrid.SelectedItems.Remove(viewModel.Steps[0]);
            Assert.AreEqual(1, viewModel.SelectedStepCount);
            Assert.AreSame(viewModel.Steps[2], viewModel.SelectedStep);

            stepsGrid.SelectedItems.Add(viewModel.Steps[0]);
            viewModel.DeleteStepCommand.ExecuteAsync().GetAwaiter().GetResult();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.DataBind);

            CollectionAssert.AreEqual(new[] { "B" }, viewModel.Steps.Select(step => step.Name).ToArray());
            Assert.AreEqual(1, confirmation.CallCount);
            Assert.AreEqual("Xóa 2 bước đã chọn?", confirmation.LastMessage);
            Assert.AreEqual(1, store.SaveCount);
            Assert.AreEqual(1, stepsGrid.SelectedItems.Count);
            Assert.AreSame(viewModel.Steps[0], stepsGrid.SelectedItem);
            Assert.AreSame(viewModel.Steps[0], viewModel.SelectedStep);
        }
        finally
        {
            window.Close();
        }
    }

    [STATestMethod]
    public void SwipeOverlay_UsesCompactMarkersAndHighContrastDirectionLayers()
    {
        if (Application.Current is null)
        {
            var application = new MEmuScriptStudio.App.App();
            application.InitializeComponent();
        }

        var window = new SwipeCaptureOverlayWindow();
        try
        {
            var startMarker = (Ellipse)window.FindName("StartMarker");
            var endMarker = (Ellipse)window.FindName("EndMarker");
            var line = (Line)window.FindName("SwipeLine");
            var lineOutline = (Line)window.FindName("SwipeLineOutline");
            var arrow = (Polygon)window.FindName("ArrowHead");
            var arrowOutline = (Polygon)window.FindName("ArrowHeadOutline");
            var startLabelText = (TextBlock)window.FindName("StartLabelText");

            Assert.AreEqual(8d, startMarker.Width);
            Assert.AreEqual(8d, startMarker.Height);
            Assert.AreEqual(8d, endMarker.Width);
            Assert.AreEqual(8d, endMarker.Height);
            Assert.IsTrue(startMarker.StrokeThickness >= 2);
            Assert.IsTrue(endMarker.StrokeThickness >= 2);
            Assert.IsTrue(lineOutline.StrokeThickness > line.StrokeThickness);
            Assert.AreNotEqual(line.Stroke.ToString(), lineOutline.Stroke.ToString());
            Assert.AreNotEqual(arrow.Fill.ToString(), arrowOutline.Fill.ToString());
            Assert.AreEqual(11d, startLabelText.FontSize);
        }
        finally
        {
            window.Close();
        }
    }

    [STATestMethod]
    public void TapOverlay_ReusesCompactMarkerAndShowsConfirmationInstructions()
    {
        if (Application.Current is null)
        {
            var application = new MEmuScriptStudio.App.App();
            application.InitializeComponent();
        }

        var window = new SwipeCaptureOverlayWindow();
        try
        {
            window.ConfigureTapCapture();
            var instruction = (TextBlock)window.FindName("InstructionText");
            var marker = (Ellipse)window.FindName("StartMarker");
            var endMarker = (Ellipse)window.FindName("EndMarker");

            StringAssert.Contains(instruction.Text, "Enter");
            StringAssert.Contains(instruction.Text, "Esc");
            Assert.AreEqual(8d, marker.Width);
            Assert.AreEqual(Visibility.Collapsed, endMarker.Visibility);
        }
        finally
        {
            window.Close();
        }
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
        Assert.IsFalse(viewModel.IsEditorDirty);
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

        viewModel.CopySelectedSteps();
        await viewModel.PasteCopiedStepsAsync();

        Assert.AreEqual(4, viewModel.Steps.Count);
        Assert.AreEqual("A", viewModel.Steps[1].Name);
        Assert.AreNotEqual(source.Id, viewModel.Steps[1].Id);
        Assert.AreEqual(1, store.SaveCount);
    }

    [TestMethod]
    public async Task CopyPaste_MultipleStepsPreservesOrderAndWorksAcrossScriptsWithFreshIds()
    {
        var source = CreateThreeStepScript();
        var target = new ScriptDefinition { Name = "Target" };
        var store = new RecordingScriptStore([source, target]);
        var viewModel = CreateViewModel(store, new ImmediateEngine());
        await viewModel.InitializeAsync(CancellationToken.None);
        var originalIds = new[] { viewModel.Steps[0].Id, viewModel.Steps[2].Id };
        viewModel.SynchronizeSelectedSteps([viewModel.Steps[0], viewModel.Steps[2]]);

        viewModel.CopySelectedSteps();
        viewModel.SelectedScript = viewModel.Scripts.Single(script => script.Name == "Target");
        await viewModel.PasteCopiedStepsAsync();
        var firstPasteIds = viewModel.Steps.Select(step => step.Id).ToArray();
        await viewModel.PasteCopiedStepsAsync();

        CollectionAssert.AreEqual(new[] { "A", "C", "A", "C" }, viewModel.Steps.Select(step => step.Name).ToArray());
        Assert.IsTrue(firstPasteIds.All(id => !originalIds.Contains(id)));
        Assert.IsTrue(viewModel.Steps.Skip(2).All(step => !firstPasteIds.Contains(step.Id)));
        Assert.AreEqual(2, store.SaveCount);
        Assert.AreEqual("Đã dán 2 bước.", viewModel.StatusMessage);
    }

    [TestMethod]
    public async Task DeleteShortcut_DeletesAllSelectedStepsWithOneConfirmationAndAutosave()
    {
        var store = new RecordingScriptStore([CreateThreeStepScript()]);
        var confirmation = new ConfigurableConfirmation(true);
        var viewModel = CreateViewModel(store, new ImmediateEngine(), confirmation);
        await viewModel.InitializeAsync(CancellationToken.None);
        viewModel.SynchronizeSelectedSteps([viewModel.Steps[0], viewModel.Steps[2]]);

        await viewModel.DeleteSelectedStepFromShortcutAsync();

        CollectionAssert.AreEqual(new[] { "B" }, viewModel.Steps.Select(step => step.Name).ToArray());
        CollectionAssert.AreEqual(new[] { "B" }, store.LastSaved.Single().Steps.Select(step => step.Name).ToArray());
        Assert.AreEqual("Xóa 2 bước đã chọn?", confirmation.LastMessage);
        Assert.AreEqual(1, confirmation.CallCount);
        Assert.AreEqual(1, store.SaveCount);
        Assert.AreEqual("B", viewModel.SelectedStep!.Name);
        Assert.AreEqual(1, viewModel.SelectedStepCount);
        Assert.AreEqual("Đã xóa 2 bước.", viewModel.StatusMessage);

        await viewModel.UndoStepListCommand.ExecuteAsync();

        CollectionAssert.AreEqual(new[] { "A", "B", "C" }, viewModel.Steps.Select(step => step.Name).ToArray());
        CollectionAssert.AreEqual(new[] { "A", "C" }, viewModel.SelectedSteps.Select(step => step.Name).ToArray());
        Assert.IsFalse(viewModel.UndoStepListCommand.CanExecute(null));
        Assert.AreEqual(2, store.SaveCount);
    }

    [TestMethod]
    public async Task BulkDelete_DeclinedLeavesSelectionAndPersistenceUnchanged()
    {
        var store = new RecordingScriptStore([CreateThreeStepScript()]);
        var confirmation = new ConfigurableConfirmation(false);
        var viewModel = CreateViewModel(store, new ImmediateEngine(), confirmation);
        await viewModel.InitializeAsync(CancellationToken.None);
        viewModel.SynchronizeSelectedSteps([viewModel.Steps[0], viewModel.Steps[1]]);

        await viewModel.DeleteStepCommand.ExecuteAsync();

        CollectionAssert.AreEqual(new[] { "A", "B", "C" }, viewModel.Steps.Select(step => step.Name).ToArray());
        Assert.AreEqual(2, viewModel.SelectedStepCount);
        Assert.AreEqual(0, store.SaveCount);
        Assert.AreEqual(1, confirmation.CallCount);
    }

    [TestMethod]
    public void StepGridShortcutPolicy_DoesNotCaptureTextInputOrFocusOutsideGrid()
    {
        Assert.AreEqual(StepGridShortcut.None, StepGridShortcutPolicy.Resolve(false, false, true, true, true, Key.C, ModifierKeys.Control));
        Assert.AreEqual(StepGridShortcut.None, StepGridShortcutPolicy.Resolve(true, true, true, true, true, Key.Z, ModifierKeys.Control));
        Assert.AreEqual(StepGridShortcut.None, StepGridShortcutPolicy.Resolve(true, true, true, true, true, Key.Y, ModifierKeys.Control));
        Assert.AreEqual(StepGridShortcut.None, StepGridShortcutPolicy.Resolve(true, false, false, false, false, Key.Delete, ModifierKeys.None));
        Assert.AreEqual(StepGridShortcut.Copy, StepGridShortcutPolicy.Resolve(true, false, true, false, false, Key.C, ModifierKeys.Control));
        Assert.AreEqual(StepGridShortcut.Paste, StepGridShortcutPolicy.Resolve(true, false, false, true, false, Key.V, ModifierKeys.Control));
        Assert.AreEqual(StepGridShortcut.Delete, StepGridShortcutPolicy.Resolve(true, false, true, false, false, Key.Delete, ModifierKeys.None));
        Assert.AreEqual(StepGridShortcut.Undo, StepGridShortcutPolicy.Resolve(true, false, true, false, true, Key.Z, ModifierKeys.Control));
        Assert.AreEqual(StepGridShortcut.None, StepGridShortcutPolicy.Resolve(true, false, true, false, true, Key.Y, ModifierKeys.Control));
        Assert.AreEqual(StepGridShortcut.None, StepGridShortcutPolicy.Resolve(true, false, true, false, true, Key.Z, ModifierKeys.Control | ModifierKeys.Shift));
        Assert.AreEqual(StepGridShortcut.ClearSelection, StepGridShortcutPolicy.Resolve(true, false, true, false, false, Key.Escape, ModifierKeys.None));
        Assert.IsFalse(StepGridShortcutPolicy.ShouldPreserveSelectionForDrag(2, true, false, ModifierKeys.Control));
        Assert.IsTrue(StepGridShortcutPolicy.ShouldPreserveSelectionForDrag(2, true, false, ModifierKeys.None));
        Assert.IsFalse(StepGridShortcutPolicy.ShouldPreserveSelectionForDrag(2, true, true, ModifierKeys.None));
    }

    [TestMethod]
    public async Task StepEnabledCheckbox_AutosaveDoesNotCreateDraftOrNavigationWarning()
    {
        var confirmation = new ConfigurableConfirmation(false);
        var store = new RecordingScriptStore([CreateThreeStepScript()]);
        var viewModel = CreateViewModel(store, new ImmediateEngine(), confirmation);
        await viewModel.InitializeAsync(CancellationToken.None);

        viewModel.Steps[0].IsEnabled = false;
        viewModel.SynchronizeSelectedSteps([viewModel.Steps[1]]);

        Assert.AreEqual(1, store.SaveCount);
        Assert.IsFalse(store.LastSaved.Single().Steps[0].IsEnabled);
        Assert.IsFalse(viewModel.IsEditorDirty);
        Assert.AreSame(viewModel.Steps[1], viewModel.SelectedStep);
        Assert.AreEqual(0, confirmation.CallCount);
    }

    [TestMethod]
    public async Task ClearSelection_WithDirtyDraftWarnsOnceAndDeclineRestoresSelection()
    {
        var confirmation = new ConfigurableConfirmation(false);
        var viewModel = CreateViewModel(
            new RecordingScriptStore([CreateThreeStepScript()]), new ImmediateEngine(), confirmation);
        await viewModel.InitializeAsync(CancellationToken.None);
        var original = viewModel.SelectedStep;
        viewModel.EditorName = "Bản nháp chưa lưu";

        var cleared = viewModel.TryClearStepSelection();

        Assert.IsFalse(cleared);
        Assert.AreEqual(1, confirmation.CallCount);
        Assert.AreSame(original, viewModel.SelectedStep);
        Assert.AreEqual(1, viewModel.SelectedStepCount);
        Assert.IsTrue(viewModel.IsEditorDirty);
    }

    [TestMethod]
    public async Task ClearSelection_AcceptedClearsAllSelectedStepsWithOneWarning()
    {
        var confirmation = new ConfigurableConfirmation(true);
        var viewModel = CreateViewModel(
            new RecordingScriptStore([CreateThreeStepScript()]), new ImmediateEngine(), confirmation);
        await viewModel.InitializeAsync(CancellationToken.None);
        viewModel.SynchronizeSelectedSteps([viewModel.Steps[0], viewModel.Steps[2]]);
        viewModel.EditorName = "Bản nháp chưa lưu";

        var cleared = viewModel.TryClearStepSelection();

        Assert.IsTrue(cleared);
        Assert.AreEqual(1, confirmation.CallCount);
        Assert.IsNull(viewModel.SelectedStep);
        Assert.AreEqual(0, viewModel.SelectedStepCount);
        Assert.IsFalse(viewModel.IsEditorDirty);
    }

    [TestMethod]
    public async Task BulkPaste_UndoUsesOneEntryAndRestoresIdsOrderAndSelection()
    {
        var store = new RecordingScriptStore([CreateThreeStepScript()]);
        var viewModel = CreateViewModel(store, new ImmediateEngine());
        await viewModel.InitializeAsync(CancellationToken.None);
        var originalIds = viewModel.Steps.Select(step => step.Id).ToArray();
        viewModel.SynchronizeSelectedSteps([viewModel.Steps[0], viewModel.Steps[2]]);
        viewModel.CopySelectedSteps();

        await viewModel.PasteCopiedStepsAsync();
        Assert.AreEqual(5, viewModel.Steps.Count);
        Assert.AreEqual(2, viewModel.SelectedStepCount);
        Assert.IsTrue(viewModel.UndoStepListCommand.CanExecute(null));
        await viewModel.UndoStepListCommand.ExecuteAsync();

        CollectionAssert.AreEqual(originalIds, viewModel.Steps.Select(step => step.Id).ToArray());
        CollectionAssert.AreEqual(new[] { originalIds[0], originalIds[2] }, viewModel.SelectedSteps.Select(step => step.Id).ToArray());
        Assert.IsFalse(viewModel.UndoStepListCommand.CanExecute(null));
        Assert.AreEqual(2, store.SaveCount);
    }

    [TestMethod]
    public async Task DuplicateMultipleSteps_IsOneHistoryEntry()
    {
        var store = new RecordingScriptStore([CreateThreeStepScript()]);
        var viewModel = CreateViewModel(store, new ImmediateEngine());
        await viewModel.InitializeAsync(CancellationToken.None);
        viewModel.SynchronizeSelectedSteps([viewModel.Steps[0], viewModel.Steps[2]]);

        await viewModel.DuplicateStepCommand.ExecuteAsync();

        CollectionAssert.AreEqual(new[] { "A", "B", "C", "A", "C" }, viewModel.Steps.Select(step => step.Name).ToArray());
        Assert.AreEqual(2, viewModel.SelectedStepCount);
        await viewModel.UndoStepListCommand.ExecuteAsync();

        CollectionAssert.AreEqual(new[] { "A", "B", "C" }, viewModel.Steps.Select(step => step.Name).ToArray());
        Assert.IsFalse(viewModel.UndoStepListCommand.CanExecute(null));
        Assert.AreEqual(2, store.SaveCount);
    }

    [TestMethod]
    public async Task StepHistory_IsLimitedToFiftyEntriesPerScript()
    {
        var viewModel = CreateViewModel(
            new RecordingScriptStore([CreateThreeStepScript()]), new ImmediateEngine());
        await viewModel.InitializeAsync(CancellationToken.None);

        for (var index = 0; index < 51; index++)
        {
            viewModel.EditorName = $"Tên {index}";
            await viewModel.SaveStepCommand.ExecuteAsync();
        }

        for (var index = 0; index < 50; index++)
        {
            Assert.IsTrue(viewModel.UndoStepListCommand.CanExecute(null), $"Undo entry {index + 1} should exist.");
            await viewModel.UndoStepListCommand.ExecuteAsync();
        }

        Assert.AreEqual("Tên 0", viewModel.Steps[0].Name);
        Assert.IsFalse(viewModel.UndoStepListCommand.CanExecute(null));
    }

    [TestMethod]
    public async Task NewMutationAfterUndoContinuesHistoryAndHistoriesAreIndependentPerScript()
    {
        var first = CreateThreeStepScript();
        var second = new ScriptDefinition
        {
            Name = "Second",
            Steps = [new NoteStep { Name = "Second A", Text = "A" }]
        };
        var viewModel = CreateViewModel(new RecordingScriptStore([first, second]), new ImmediateEngine());
        await viewModel.InitializeAsync(CancellationToken.None);

        await viewModel.DuplicateStepCommand.ExecuteAsync();
        await viewModel.UndoStepListCommand.ExecuteAsync();
        Assert.IsFalse(viewModel.UndoStepListCommand.CanExecute(null));

        viewModel.Steps[0].IsEnabled = false;
        Assert.IsTrue(viewModel.UndoStepListCommand.CanExecute(null));
        await viewModel.UndoStepListCommand.ExecuteAsync();
        Assert.IsTrue(viewModel.Steps[0].IsEnabled);
        Assert.IsFalse(viewModel.UndoStepListCommand.CanExecute(null));

        await viewModel.DuplicateStepCommand.ExecuteAsync();
        Assert.IsTrue(viewModel.UndoStepListCommand.CanExecute(null));

        viewModel.SelectedScript = viewModel.Scripts.Single(script => script.Name == "Second");
        Assert.IsFalse(viewModel.UndoStepListCommand.CanExecute(null));
        await viewModel.DuplicateStepCommand.ExecuteAsync();
        Assert.IsTrue(viewModel.UndoStepListCommand.CanExecute(null));

        viewModel.SelectedScript = viewModel.Scripts.Single(script => script.Name == first.Name);
        Assert.IsTrue(viewModel.UndoStepListCommand.CanExecute(null));
    }

    [TestMethod]
    public async Task Undo_WithDirtyDraftWarnsOnceAndDeclineKeepsHistoryAndPersistence()
    {
        var confirmation = new ConfigurableConfirmation(false);
        var store = new RecordingScriptStore([CreateThreeStepScript()]);
        var viewModel = CreateViewModel(store, new ImmediateEngine(), confirmation);
        await viewModel.InitializeAsync(CancellationToken.None);
        await viewModel.DuplicateStepCommand.ExecuteAsync();
        viewModel.EditorName = "Bản nháp chưa lưu";

        await viewModel.UndoStepListCommand.ExecuteAsync();

        Assert.AreEqual(1, confirmation.CallCount);
        Assert.AreEqual(4, viewModel.Steps.Count);
        Assert.AreEqual(1, store.SaveCount);
        Assert.IsTrue(viewModel.UndoStepListCommand.CanExecute(null));
        Assert.IsTrue(viewModel.IsEditorDirty);
    }

    [TestMethod]
    public async Task DirectToggleAndReorder_AreBlockedWhileScriptIsRunning()
    {
        var store = new RecordingScriptStore([CreateThreeStepScript()]);
        var engine = new BlockingEngine();
        var instances = new FixedInstanceService([new MemuInstance(0, "Target", true, 123, 456)]);
        var viewModel = CreateViewModel(store, engine, instanceService: instances);
        await viewModel.InitializeAsync(CancellationToken.None);
        await viewModel.RefreshCommand.ExecuteAsync();
        viewModel.RunTargets[0].IsSelected = true;
        var first = viewModel.Steps[0];
        viewModel.SynchronizeSelectedSteps([viewModel.Steps[0], viewModel.Steps[1]]);
        var runTask = viewModel.RunCommand.ExecuteAsync();
        await engine.Started.Task;

        first.IsEnabled = false;
        await viewModel.MoveStepToAsync(first, 3);
        await viewModel.DeleteSelectedStepFromShortcutAsync();

        Assert.IsTrue(first.IsEnabled);
        CollectionAssert.AreEqual(new[] { "A", "B", "C" }, viewModel.Steps.Select(step => step.Name).ToArray());
        Assert.AreEqual(0, store.SaveCount);
        viewModel.StopCommand.Execute(null);
        await runTask;
    }

    [TestMethod]
    public async Task DragReorder_MovesSelectedStepsAsBlockAndRestoresSelection()
    {
        var script = new ScriptDefinition
        {
            Name = "Five",
            Steps =
            [
                new NoteStep { Name = "A" },
                new NoteStep { Name = "B" },
                new NoteStep { Name = "C" },
                new NoteStep { Name = "D" },
                new NoteStep { Name = "E" }
            ]
        };
        var store = new RecordingScriptStore([script]);
        var viewModel = CreateViewModel(store, new ImmediateEngine());
        await viewModel.InitializeAsync(CancellationToken.None);
        var selected = new[] { viewModel.Steps[1], viewModel.Steps[3] };
        IReadOnlyList<StepItemViewModel>? restored = null;
        viewModel.StepSelectionRestoreRequested += items => restored = items;
        viewModel.SynchronizeSelectedSteps(selected);

        await viewModel.MoveStepToAsync(selected[0], 5);

        CollectionAssert.AreEqual(new[] { "A", "C", "E", "B", "D" }, viewModel.Steps.Select(step => step.Name).ToArray());
        CollectionAssert.AreEqual(new[] { "B", "D" }, viewModel.SelectedSteps.Select(step => step.Name).ToArray());
        CollectionAssert.AreEqual(new[] { "B", "D" }, restored!.Select(step => step.Name).ToArray());
        Assert.IsTrue(viewModel.CanDragStep(selected[0]));
        Assert.AreEqual(1, store.SaveCount);

        await viewModel.UndoStepListCommand.ExecuteAsync();

        CollectionAssert.AreEqual(new[] { "A", "B", "C", "D", "E" }, viewModel.Steps.Select(step => step.Name).ToArray());
        CollectionAssert.AreEqual(new[] { "B", "D" }, viewModel.SelectedSteps.Select(step => step.Name).ToArray());
        Assert.IsFalse(viewModel.UndoStepListCommand.CanExecute(null));
        Assert.AreEqual(2, store.SaveCount);
    }

    [TestMethod]
    public async Task MoveButtons_ApplyToNonContiguousSelectionAsOneBlock()
    {
        var script = new ScriptDefinition
        {
            Name = "Five",
            Steps =
            [
                new NoteStep { Name = "A" },
                new NoteStep { Name = "B" },
                new NoteStep { Name = "C" },
                new NoteStep { Name = "D" },
                new NoteStep { Name = "E" }
            ]
        };
        var store = new RecordingScriptStore([script]);
        var viewModel = CreateViewModel(store, new ImmediateEngine());
        await viewModel.InitializeAsync(CancellationToken.None);
        viewModel.SynchronizeSelectedSteps([viewModel.Steps[1], viewModel.Steps[3]]);

        await viewModel.MoveStepUpCommand.ExecuteAsync();
        CollectionAssert.AreEqual(new[] { "B", "D", "A", "C", "E" }, viewModel.Steps.Select(step => step.Name).ToArray());
        await viewModel.MoveStepDownCommand.ExecuteAsync();

        CollectionAssert.AreEqual(new[] { "A", "B", "D", "C", "E" }, viewModel.Steps.Select(step => step.Name).ToArray());
        CollectionAssert.AreEqual(new[] { "B", "D" }, viewModel.SelectedSteps.Select(step => step.Name).ToArray());
        Assert.AreEqual(2, store.SaveCount);
    }

    [DataTestMethod]
    [DataRow(ScriptStepKind.AndroidShell, false, false, false, false, false, false, false, false, false, true, false)]
    [DataRow(ScriptStepKind.ForceStop, true, false, false, false, false, false, false, false, false, false, false)]
    [DataRow(ScriptStepKind.OpenApp, true, true, false, false, false, false, false, false, false, false, false)]
    [DataRow(ScriptStepKind.Delay, false, false, true, false, false, false, false, false, false, false, false)]
    [DataRow(ScriptStepKind.Tap, false, false, false, true, false, false, false, false, false, false, false)]
    [DataRow(ScriptStepKind.Hold, false, false, false, false, true, false, false, false, false, false, false)]
    [DataRow(ScriptStepKind.Swipe, false, false, false, false, false, true, false, false, false, false, false)]
    [DataRow(ScriptStepKind.InputText, false, false, false, false, false, false, true, false, false, false, false)]
    [DataRow(ScriptStepKind.AndroidClipboardPaste, false, false, false, false, false, false, false, true, false, false, false)]
    [DataRow(ScriptStepKind.KeyEvent, false, false, false, false, false, false, false, false, true, false, false)]
    [DataRow(ScriptStepKind.Note, false, false, false, false, false, false, false, false, false, false, true)]
    public void EditorKind_ShowsOnlyRelevantParameterGroup(
        ScriptStepKind kind,
        bool package,
        bool activity,
        bool delay,
        bool tap,
        bool hold,
        bool swipe,
        bool inputText,
        bool androidClipboardPaste,
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
        Assert.AreEqual(hold, viewModel.ShowHold);
        Assert.AreEqual(swipe, viewModel.ShowSwipe);
        Assert.AreEqual(inputText, viewModel.ShowInputText);
        Assert.AreEqual(androidClipboardPaste, viewModel.ShowAndroidClipboardPaste);
        Assert.AreEqual(keyEvent, viewModel.ShowKeyEvent);
        Assert.AreEqual(androidShell, viewModel.ShowAndroidShell);
        Assert.AreEqual(note, viewModel.ShowNote);
        Assert.AreEqual(kind is not ScriptStepKind.Delay and not ScriptStepKind.Note, viewModel.ShowContinueOnError);
        Assert.AreEqual(kind is not ScriptStepKind.Delay and not ScriptStepKind.Note, viewModel.ShowTimeout);
    }

    [TestMethod]
    public async Task InputTextEnterOption_LoadsAndSavesWithTheStep()
    {
        var input = new InputTextStep
        {
            Name = "Submit",
            Text = "hello",
            PressEnterAfterInput = true
        };
        var store = new RecordingScriptStore([new ScriptDefinition { Name = "Input", Steps = [input] }]);
        var viewModel = CreateViewModel(store, new ImmediateEngine());

        await viewModel.InitializeAsync(CancellationToken.None);

        Assert.IsTrue(viewModel.EditorPressEnterAfterInput);
        viewModel.EditorPressEnterAfterInput = false;
        await viewModel.SaveStepCommand.ExecuteAsync();

        Assert.IsFalse(((InputTextStep)viewModel.SelectedStep!.Model).PressEnterAfterInput);
        Assert.AreEqual(1, store.SaveCount);
    }

    [TestMethod]
    public async Task AndroidClipboardPasteEnterOption_LoadsAndSavesWithTheStep()
    {
        var paste = new AndroidClipboardPasteStep { Name = "Paste", PressEnterAfterPaste = true };
        var store = new RecordingScriptStore([new ScriptDefinition { Name = "Paste", Steps = [paste] }]);
        var viewModel = CreateViewModel(store, new ImmediateEngine());

        await viewModel.InitializeAsync(CancellationToken.None);

        Assert.IsTrue(viewModel.EditorPressEnterAfterPaste);
        viewModel.EditorPressEnterAfterPaste = false;
        await viewModel.SaveStepCommand.ExecuteAsync();

        Assert.IsFalse(((AndroidClipboardPasteStep)viewModel.SelectedStep!.Model).PressEnterAfterPaste);
        Assert.AreEqual(1, store.SaveCount);
    }

    [TestMethod]
    public async Task HoldStep_LoadsAndSavesCoordinatesAndDuration()
    {
        var hold = new HoldStep { Name = "Hold", X = 10, Y = 20, DurationMilliseconds = 700 };
        var store = new RecordingScriptStore([new ScriptDefinition { Name = "Hold", Steps = [hold] }]);
        var viewModel = CreateViewModel(store, new ImmediateEngine());

        await viewModel.InitializeAsync(CancellationToken.None);

        Assert.AreEqual(ScriptStepKind.Hold, viewModel.EditorKind);
        Assert.AreEqual(10, viewModel.EditorX);
        Assert.AreEqual(20, viewModel.EditorY);
        Assert.AreEqual(700, viewModel.EditorHoldDuration);
        viewModel.EditorHoldDuration = 900;
        await viewModel.SaveStepCommand.ExecuteAsync();

        Assert.AreEqual(900, ((HoldStep)viewModel.SelectedStep!.Model).DurationMilliseconds);
        Assert.AreEqual(1, store.SaveCount);
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
    public async Task StepEditor_TracksUnsavedAndSavedState()
    {
        var viewModel = CreateViewModel(new RecordingScriptStore(), new ImmediateEngine());
        await viewModel.InitializeAsync(CancellationToken.None);

        Assert.IsFalse(viewModel.IsEditorDirty);
        Assert.AreEqual("Đã lưu", viewModel.EditorSaveState);

        viewModel.EditorName = "Tên đang sửa";
        Assert.IsTrue(viewModel.IsEditorDirty);
        Assert.AreEqual("Có thay đổi chưa lưu", viewModel.EditorSaveState);

        await viewModel.SaveStepCommand.ExecuteAsync();
        Assert.IsFalse(viewModel.IsEditorDirty);
        Assert.AreEqual("Đã lưu", viewModel.EditorSaveState);
        Assert.AreEqual("Đã lưu bước.", viewModel.StatusMessage);

        viewModel.NewStepCommand.Execute(null);
        Assert.IsTrue(viewModel.IsEditorDirty);
        Assert.AreEqual("Có thay đổi chưa lưu", viewModel.EditorSaveState);
    }

    [TestMethod]
    public async Task StepEditor_ChangesDuringSaveRemainDirty()
    {
        var store = new BlockingSaveScriptStore([CreateThreeStepScript()]);
        var viewModel = CreateViewModel(store, new ImmediateEngine());
        await viewModel.InitializeAsync(CancellationToken.None);
        viewModel.EditorName = "Giá trị đang lưu";

        var saveTask = viewModel.SaveStepCommand.ExecuteAsync();
        await store.SaveStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        viewModel.EditorName = "Giá trị sửa sau";
        store.ReleaseSave.TrySetResult();
        await saveTask;

        Assert.AreEqual("Giá trị đang lưu", viewModel.SelectedStep!.Name);
        Assert.AreEqual("Giá trị sửa sau", viewModel.EditorName);
        Assert.IsTrue(viewModel.IsEditorDirty);
        Assert.AreEqual("Đã lưu bước; còn thay đổi chưa lưu.", viewModel.StatusMessage);
    }

    [TestMethod]
    public async Task StepEditor_RejectingNavigationPreservesUnsavedDraft()
    {
        var confirmation = new ConfigurableConfirmation(false);
        var viewModel = CreateViewModel(
            new RecordingScriptStore([CreateThreeStepScript()]), new ImmediateEngine(), confirmation);
        await viewModel.InitializeAsync(CancellationToken.None);
        var originalStep = viewModel.SelectedStep;
        viewModel.EditorName = "Bản nháp chưa lưu";

        viewModel.SelectedStep = viewModel.Steps[1];

        Assert.AreSame(originalStep, viewModel.SelectedStep);
        Assert.AreEqual("Bản nháp chưa lưu", viewModel.EditorName);
        Assert.IsTrue(viewModel.IsEditorDirty);
        Assert.AreEqual(1, confirmation.CallCount);
        Assert.AreEqual("Bỏ thay đổi chưa lưu", confirmation.LastTitle);
    }

    [TestMethod]
    public async Task StepEditor_RejectingScriptNavigationPreservesUnsavedDraft()
    {
        var first = CreateThreeStepScript();
        var second = new ScriptDefinition { Name = "Other", Steps = [new NoteStep { Name = "Other", Text = "Other" }] };
        var confirmation = new ConfigurableConfirmation(false);
        var viewModel = CreateViewModel(new RecordingScriptStore([first, second]), new ImmediateEngine(), confirmation);
        await viewModel.InitializeAsync(CancellationToken.None);
        var originalScript = viewModel.SelectedScript;
        viewModel.EditorName = "Bản nháp chưa lưu";

        viewModel.SelectedScript = viewModel.Scripts[1];

        Assert.AreSame(originalScript, viewModel.SelectedScript);
        Assert.AreEqual("Bản nháp chưa lưu", viewModel.EditorName);
        Assert.IsTrue(viewModel.IsEditorDirty);
    }

    [TestMethod]
    public async Task StepEditor_RejectingDraftDiscardPreventsStepDeletion()
    {
        var store = new RecordingScriptStore([CreateThreeStepScript()]);
        var confirmation = new QueueConfirmation(true, false);
        var viewModel = CreateViewModel(store, new ImmediateEngine(), confirmation);
        await viewModel.InitializeAsync(CancellationToken.None);
        var originalStep = viewModel.SelectedStep;
        viewModel.EditorName = "Bản nháp chưa lưu";

        await viewModel.DeleteStepCommand.ExecuteAsync();

        Assert.AreEqual(3, viewModel.Steps.Count);
        Assert.AreSame(originalStep, viewModel.SelectedStep);
        Assert.AreEqual("Bản nháp chưa lưu", viewModel.EditorName);
        Assert.IsTrue(viewModel.IsEditorDirty);
        Assert.AreEqual(0, store.SaveCount);
    }

    [TestMethod]
    public async Task StepEditor_RejectingDraftDiscardPreventsPasteMutation()
    {
        var store = new RecordingScriptStore([CreateThreeStepScript()]);
        var confirmation = new ConfigurableConfirmation(false);
        var viewModel = CreateViewModel(store, new ImmediateEngine(), confirmation);
        await viewModel.InitializeAsync(CancellationToken.None);
        viewModel.CopySelectedSteps();
        viewModel.EditorName = "Bản nháp chưa lưu";

        await viewModel.PasteCopiedStepsAsync();

        Assert.AreEqual(3, viewModel.Steps.Count);
        Assert.AreEqual("Bản nháp chưa lưu", viewModel.EditorName);
        Assert.IsTrue(viewModel.IsEditorDirty);
        Assert.AreEqual(0, store.SaveCount);
    }

    [TestMethod]
    public async Task ScriptExportCommands_ExportSelectedOrWholeLibrary()
    {
        var store = new RecordingScriptStore([CreateThreeStepScript()]);
        var transfer = new RecordingScriptTransferService([]);
        var dialogs = new RecordingFileDialog(importPath: null, exportPath: @"C:\Temp\scripts.memuscript");
        var viewModel = CreateViewModel(store, new ImmediateEngine(), fileDialog: dialogs, transfer: transfer,
            importConflict: new FixedImportConflict(ScriptImportConflictResolution.Skip));
        await viewModel.InitializeAsync(CancellationToken.None);

        await viewModel.ExportSelectedScriptCommand.ExecuteAsync();
        await viewModel.ExportAllScriptsCommand.ExecuteAsync();

        Assert.AreEqual(2, transfer.Exports.Count);
        Assert.AreEqual(1, transfer.Exports[0].Count);
        Assert.AreEqual(viewModel.Scripts.Count, transfer.Exports[1].Count);
        Assert.AreEqual(@"C:\Temp\scripts.memuscript", transfer.LastExportPath);
    }

    [DataTestMethod]
    [DataRow(ScriptImportConflictResolution.CreateCopy)]
    [DataRow(ScriptImportConflictResolution.Overwrite)]
    [DataRow(ScriptImportConflictResolution.Skip)]
    public async Task ScriptImport_AppliesSelectedConflictResolution(ScriptImportConflictResolution resolution)
    {
        var original = CreateThreeStepScript();
        var incoming = new ScriptDefinition
        {
            Id = original.Id,
            Name = "Incoming",
            Steps = [new NoteStep { Name = "Imported note", Text = "Imported" }]
        };
        var store = new RecordingScriptStore([original]);
        var transfer = new RecordingScriptTransferService([incoming]);
        var dialogs = new RecordingFileDialog(@"C:\Temp\input.memuscript", exportPath: null);
        var viewModel = CreateViewModel(store, new ImmediateEngine(), fileDialog: dialogs, transfer: transfer,
            importConflict: new FixedImportConflict(resolution));
        await viewModel.InitializeAsync(CancellationToken.None);

        await viewModel.ImportScriptsCommand.ExecuteAsync();

        if (resolution == ScriptImportConflictResolution.CreateCopy)
        {
            Assert.AreEqual(2, viewModel.Scripts.Count);
            var copy = viewModel.Scripts.Single(script => script.Id != original.Id).Model;
            Assert.AreNotEqual(incoming.Id, copy.Id);
            Assert.AreNotEqual(incoming.Steps[0].Id, copy.Steps[0].Id);
            Assert.AreEqual(1, store.SaveCount);
        }
        else if (resolution == ScriptImportConflictResolution.Overwrite)
        {
            Assert.AreEqual(1, viewModel.Scripts.Count);
            Assert.AreEqual("Incoming", viewModel.Scripts[0].Name);
            Assert.AreEqual(incoming.Steps[0].Id, viewModel.Scripts[0].Model.Steps[0].Id);
            Assert.AreEqual(1, store.SaveCount);
        }
        else
        {
            Assert.AreEqual(1, viewModel.Scripts.Count);
            Assert.AreEqual("Steps", viewModel.Scripts[0].Name);
            Assert.AreEqual(0, store.SaveCount);
        }
    }

    [TestMethod]
    public async Task ScriptImport_AllSkippedPreservesUnsavedEditorDraft()
    {
        var original = CreateThreeStepScript();
        var incoming = new ScriptDefinition
        {
            Id = original.Id,
            Name = "Skipped",
            Steps = [new NoteStep { Name = "Skipped", Text = "Skipped" }]
        };
        var store = new RecordingScriptStore([original]);
        var transfer = new RecordingScriptTransferService([incoming]);
        var dialogs = new RecordingFileDialog(@"C:\Temp\input.memuscript", exportPath: null);
        var viewModel = CreateViewModel(store, new ImmediateEngine(), fileDialog: dialogs, transfer: transfer,
            importConflict: new FixedImportConflict(ScriptImportConflictResolution.Skip));
        await viewModel.InitializeAsync(CancellationToken.None);
        viewModel.EditorName = "Bản nháp chưa lưu";

        await viewModel.ImportScriptsCommand.ExecuteAsync();

        Assert.AreEqual(1, viewModel.Scripts.Count);
        Assert.AreEqual("Bản nháp chưa lưu", viewModel.EditorName);
        Assert.IsTrue(viewModel.IsEditorDirty);
        Assert.AreEqual(0, store.SaveCount);
        Assert.AreEqual("Đã nhập 0 kịch bản; bỏ qua 1.", viewModel.StatusMessage);
    }

    [STATestMethod]
    public void StepEditor_TextBindingCanBeFlushedBeforeCtrlS()
    {
        if (Application.Current is null)
        {
            var application = new MEmuScriptStudio.App.App();
            application.InitializeComponent();
        }

        var viewModel = CreateViewModel(new RecordingScriptStore(), new ImmediateEngine());
        viewModel.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();
        var window = new MainWindow(viewModel);
        var editorName = (TextBox)window.FindName("EditorNameTextBox");
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
        viewModel.SelectedStep = viewModel.Steps[0];
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
        editorName.Text = "Giá trị chưa mất focus";

        editorName.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
        Assert.AreEqual("Giá trị chưa mất focus", viewModel.EditorName);
        viewModel.SaveStepCommand.ExecuteAsync().GetAwaiter().GetResult();

        Assert.AreEqual("Giá trị chưa mất focus", viewModel.SelectedStep!.Name);
        Assert.IsFalse(viewModel.IsEditorDirty);
    }

    [TestMethod]
    public async Task RunCommand_UsesExactlySelectedInstance()
    {
        var engine = new ImmediateEngine();
        var instances = new FixedInstanceService([new MemuInstance(8, "Selected", true, 123)]);
        var viewModel = CreateViewModel(new RecordingScriptStore(), engine, instanceService: instances);
        await viewModel.InitializeAsync(CancellationToken.None);
        await viewModel.RefreshCommand.ExecuteAsync();
        viewModel.RunTargets[0].IsSelected = true;

        await viewModel.RunCommand.ExecuteAsync();

        Assert.IsNotNull(engine.LastRequest);
        Assert.AreEqual(8, engine.LastRequest.InstanceIndex);
        Assert.AreEqual(viewModel.SelectedScript!.Id, engine.LastRequest.Script.Id);
    }

    [TestMethod]
    public async Task MultiInstanceRun_AllScopePersistsConfigurationAndKeepsPerInstanceResultsSeparate()
    {
        var targets = new[]
        {
            new MemuInstance(2, "Two", true, 102),
            new MemuInstance(5, "Five", true, 105)
        };
        var persistedSettings = new ApplicationSettings { MemucPath = @"C:\MEmu\memuc.exe" };
        var settings = new RecordingRunSettingsStore(persistedSettings);
        var engine = new ReportingMultiEngine(failedIndex: 2);
        var viewModel = CreateViewModel(
            new RecordingScriptStore([CreateThreeStepScript()]),
            engine,
            instanceService: new FixedInstanceService(targets),
            settingsStore: settings);
        await viewModel.InitializeAsync(CancellationToken.None);
        await viewModel.RefreshCommand.ExecuteAsync();
        persistedSettings.ApplicationDisplayNames["com.example.added-later"] = "Tên mới";
        foreach (var target in viewModel.RunTargets) target.IsSelected = true;
        viewModel.IsRandomSpacing = true;
        viewModel.RandomMinimumSpacingMilliseconds = 4;
        viewModel.RandomMaximumSpacingMilliseconds = 9;
        viewModel.StopAllOnInvalidTarget = true;

        await viewModel.RunCommand.ExecuteAsync();

        CollectionAssert.AreEquivalent(new[] { 2, 5 }, engine.Requests.Select(request => request.InstanceIndex).ToArray());
        Assert.AreEqual(2, viewModel.InstanceRuns.Count);
        Assert.AreEqual(InstanceExecutionStatus.Failed, viewModel.InstanceRuns.Single(item => item.Index == 2).Status);
        Assert.AreEqual(InstanceExecutionStatus.Succeeded, viewModel.InstanceRuns.Single(item => item.Index == 5).Status);
        StringAssert.Contains(string.Join("\n", viewModel.InstanceRuns.Single(item => item.Index == 2).Log), "instance-2");
        StringAssert.Contains(string.Join("\n", viewModel.InstanceRuns.Single(item => item.Index == 5).Log), "instance-5");
        Assert.AreEqual(1, settings.SaveCount);
        var saved = settings.LastSaved!;
        Assert.AreEqual(LaunchSpacingMode.Random, saved.MultiInstanceRun.LaunchSpacingMode);
        Assert.AreEqual(4, saved.MultiInstanceRun.RandomMinimumSpacingMilliseconds);
        Assert.AreEqual(9, saved.MultiInstanceRun.RandomMaximumSpacingMilliseconds);
        Assert.IsTrue(saved.MultiInstanceRun.StopAllOnInvalidTarget);
        Assert.AreEqual("Tên mới", saved.ApplicationDisplayNames["com.example.added-later"]);
    }

    [TestMethod]
    public async Task MultiInstanceRun_PreflightSkipsUnavailableByDefaultAndCanAbortAll()
    {
        var targets = new[]
        {
            new MemuInstance(1, "Running", true, 101),
            new MemuInstance(2, "Stopped", false, null)
        };
        var instances = new FixedInstanceService(targets);
        var engine = new ReportingMultiEngine();
        var viewModel = CreateViewModel(new RecordingScriptStore(), engine, instanceService: instances);
        await viewModel.InitializeAsync(CancellationToken.None);
        await viewModel.RefreshCommand.ExecuteAsync();
        foreach (var target in viewModel.RunTargets) target.IsSelected = true;

        await viewModel.RunCommand.ExecuteAsync();

        CollectionAssert.AreEqual(new[] { 1 }, engine.Requests.Select(request => request.InstanceIndex).ToArray());
        Assert.AreEqual(InstanceExecutionStatus.Unavailable, viewModel.InstanceRuns.Single(item => item.Index == 2).Status);

        engine.Requests.Clear();
        viewModel.StopAllOnInvalidTarget = true;
        foreach (var target in viewModel.RunTargets) target.IsSelected = true;
        await viewModel.RunCommand.ExecuteAsync();

        Assert.AreEqual(0, engine.Requests.Count);
        Assert.AreEqual(InstanceExecutionStatus.Cancelled, viewModel.InstanceRuns.Last(item => item.Index == 1).Status);
        Assert.AreEqual(InstanceExecutionStatus.Unavailable, viewModel.InstanceRuns.Last(item => item.Index == 2).Status);
        StringAssert.Contains(viewModel.StatusMessage, "dừng toàn bộ tại preflight");
    }

    [TestMethod]
    public async Task RunCommand_LocksUiAndUsesSnapshotWhileSettingsUpdateIsPending()
    {
        var target = new MemuInstance(6, "Target", true, 106);
        var settings = new BlockingUpdateSettingsStore(new ApplicationSettings { MemucPath = @"C:\MEmu\memuc.exe" });
        var engine = new ImmediateEngine();
        var store = new RecordingScriptStore([CreateThreeStepScript()]);
        var viewModel = CreateViewModel(
            store,
            engine,
            instanceService: new FixedInstanceService([target]),
            settingsStore: settings);
        await viewModel.InitializeAsync(CancellationToken.None);
        await viewModel.RefreshCommand.ExecuteAsync();
        viewModel.RunTargets[0].IsSelected = true;
        var originalScript = viewModel.SelectedScript;
        var otherScript = new ScriptItemViewModel(new ScriptDefinition { Name = "Other" });
        viewModel.Scripts.Add(otherScript);

        var runTask = viewModel.RunCommand.ExecuteAsync();
        await settings.UpdateStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.IsTrue(viewModel.IsExecuting);
        Assert.IsTrue(viewModel.CanChangeSelection);
        Assert.IsFalse(viewModel.BrowseCommand.CanExecute(null));
        viewModel.SelectedScript = otherScript;
        Assert.AreSame(otherScript, viewModel.SelectedScript);

        settings.ReleaseUpdate.TrySetResult();
        await runTask.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.AreEqual(6, engine.LastRequest!.InstanceIndex);
        Assert.AreEqual(originalScript!.Id, engine.LastRequest.Script.Id);
        Assert.AreEqual(originalScript!.Id, engine.LastRequest.Script.Id,
            "The launch group must keep the script snapshot accepted at launch time.");
    }

    [TestMethod]
    public async Task InitializeAsync_RestoresLastMultiInstanceRunConfiguration()
    {
        var loaded = new ApplicationSettings
        {
            MemucPath = @"C:\MEmu\memuc.exe",
            MultiInstanceRun = new MultiInstanceRunSettings
            {
                LaunchSpacingMode = LaunchSpacingMode.Random,
                FixedSpacingMilliseconds = 50,
                RandomMinimumSpacingMilliseconds = 100,
                RandomMaximumSpacingMilliseconds = 300,
                StopAllOnInvalidTarget = true
            }
        };
        var viewModel = CreateViewModel(
            new RecordingScriptStore(),
            new ImmediateEngine(),
            settingsStore: new RecordingRunSettingsStore(loaded));

        await viewModel.InitializeAsync(CancellationToken.None);

        Assert.IsTrue(viewModel.IsRandomSpacing);
        Assert.AreEqual(50, viewModel.FixedSpacingMilliseconds);
        Assert.AreEqual(100, viewModel.RandomMinimumSpacingMilliseconds);
        Assert.AreEqual(300, viewModel.RandomMaximumSpacingMilliseconds);
        Assert.IsTrue(viewModel.StopAllOnInvalidTarget);
    }

    [STATestMethod]
    public void MainWindow_MultiInstanceControlsExposeBindingsAndPerInstanceStatusGrid()
    {
        if (Application.Current is null)
        {
            var application = new MEmuScriptStudio.App.App();
            application.InitializeComponent();
        }

        var viewModel = CreateViewModel(new RecordingScriptStore(), new ImmediateEngine());
        var window = new MainWindow(viewModel);
        try
        {
            var targets = (ListBox)window.FindName("RunTargetsList");
            var fixedSpacing = (TextBox)window.FindName("FixedSpacingTextBox");
            var randomMinimum = (TextBox)window.FindName("RandomMinimumSpacingTextBox");
            var randomMaximum = (TextBox)window.FindName("RandomMaximumSpacingTextBox");
            var stopInvalid = (CheckBox)window.FindName("StopAllOnInvalidTargetCheckBox");
            var runs = (DataGrid)window.FindName("InstanceRunsGrid");

            Assert.AreEqual(nameof(MainViewModel.RunTargets), BindingOperations.GetBinding(targets, ItemsControl.ItemsSourceProperty)!.Path.Path);
            Assert.AreEqual(nameof(MainViewModel.FixedSpacingMilliseconds), BindingOperations.GetBinding(fixedSpacing, TextBox.TextProperty)!.Path.Path);
            Assert.AreEqual(nameof(MainViewModel.RandomMinimumSpacingMilliseconds), BindingOperations.GetBinding(randomMinimum, TextBox.TextProperty)!.Path.Path);
            Assert.AreEqual(nameof(MainViewModel.RandomMaximumSpacingMilliseconds), BindingOperations.GetBinding(randomMaximum, TextBox.TextProperty)!.Path.Path);
            Assert.AreEqual(nameof(MainViewModel.StopAllOnInvalidTarget), BindingOperations.GetBinding(stopInvalid, ToggleButton.IsCheckedProperty)!.Path.Path);
            Assert.AreEqual(nameof(MainViewModel.InstanceRuns), BindingOperations.GetBinding(runs, ItemsControl.ItemsSourceProperty)!.Path.Path);
            Assert.AreEqual(nameof(MainViewModel.SelectedInstanceRun), BindingOperations.GetBinding(runs, Selector.SelectedItemProperty)!.Path.Path);
            Assert.AreEqual(6, runs.Columns.Count);
        }
        finally
        {
            window.Close();
        }
    }

    [TestMethod]
    public async Task StopCommand_CancelsRunningExecution()
    {
        var engine = new BlockingEngine();
        var instances = new FixedInstanceService([new MemuInstance(2, "Target", true, 456)]);
        var viewModel = CreateViewModel(new RecordingScriptStore(), engine, instanceService: instances);
        await viewModel.InitializeAsync(CancellationToken.None);
        await viewModel.RefreshCommand.ExecuteAsync();
        viewModel.RunTargets[0].IsSelected = true;

        var runTask = viewModel.RunCommand.ExecuteAsync();
        await engine.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        viewModel.StopCommand.Execute(null);
        await runTask.WaitAsync(TimeSpan.FromSeconds(2));
        await WaitUntilAsync(() => !viewModel.IsExecuting);

        Assert.IsTrue(engine.WasCancelled);
        Assert.IsFalse(viewModel.IsExecuting);
    }

    [TestMethod]
    public async Task StopOneInstance_DoesNotCancelOtherRunningInstance()
    {
        var targets = new[]
        {
            new MemuInstance(1, "One", true, 101),
            new MemuInstance(2, "Two", true, 102)
        };
        var engine = new PerInstanceBlockingEngine([1, 2]);
        var viewModel = CreateViewModel(
            new RecordingScriptStore(),
            engine,
            instanceService: new FixedInstanceService(targets));
        await viewModel.InitializeAsync(CancellationToken.None);
        await viewModel.RefreshCommand.ExecuteAsync();
        foreach (var item in viewModel.RunTargets) item.IsSelected = true;

        var runTask = viewModel.RunCommand.ExecuteAsync();
        await engine.WaitForStartsAsync(2);
        viewModel.InstanceRuns.Single(item => item.Index == 1).StopCommand.Execute(null);
        await engine.WaitForCancellationAsync(1);
        engine.Complete(2);
        await runTask.WaitAsync(TimeSpan.FromSeconds(2));
        await WaitUntilAsync(() => !viewModel.IsExecuting);

        Assert.AreEqual(InstanceExecutionStatus.Cancelled, viewModel.InstanceRuns.Single(item => item.Index == 1).Status);
        Assert.AreEqual(InstanceExecutionStatus.Succeeded, viewModel.InstanceRuns.Single(item => item.Index == 2).Status);
        CollectionAssert.AreEqual(new[] { 1 }, engine.CancelledIndices.Order().ToArray());
    }

    [TestMethod]
    public async Task DynamicLaunchGroups_StartImmediatelyAndRejectAnAlreadyActiveInstance()
    {
        var targets = new[]
        {
            new MemuInstance(1, "One", true, 101),
            new MemuInstance(2, "Two", true, 102)
        };
        var engine = new PerInstanceBlockingEngine([1, 2]);
        var viewModel = CreateViewModel(new RecordingScriptStore(), engine, instanceService: new FixedInstanceService(targets));
        await viewModel.InitializeAsync(CancellationToken.None);
        await viewModel.RefreshCommand.ExecuteAsync();

        viewModel.RunTargets.Single(item => item.Index == 1).IsSelected = true;
        await viewModel.RunCommand.ExecuteAsync();
        await engine.WaitForStartsAsync(1);
        viewModel.RunTargets.Single(item => item.Index == 2).IsSelected = true;
        await viewModel.RunCommand.ExecuteAsync();
        await engine.WaitForStartsAsync(2);

        Assert.AreEqual(2, viewModel.ActiveLaunchGroupCount);
        Assert.AreEqual(2, viewModel.RunningInstanceCount);
        Assert.IsTrue(viewModel.RunTargets.All(item => !item.IsSelected));
        Assert.AreEqual(2, viewModel.InstanceRuns.Select(item => item.LaunchGroupId).Distinct().Count());

        viewModel.RunTargets.Single(item => item.Index == 1).IsSelected = true;
        await viewModel.RunCommand.ExecuteAsync();
        Assert.AreEqual(2, viewModel.InstanceRuns.Count);
        StringAssert.Contains(viewModel.StatusMessage, "bỏ qua 1 giả lập");

        viewModel.SelectedInstanceRun = viewModel.InstanceRuns.Single(item => item.Index == 1);
        viewModel.StopSelectedGroupCommand.Execute(null);
        await engine.WaitForCancellationAsync(1);
        await WaitUntilAsync(() => viewModel.ActiveLaunchGroupCount == 1);
        Assert.IsTrue(viewModel.IsExecuting);
        viewModel.StopCommand.Execute(null);
        await engine.WaitForCancellationAsync(2);
        await WaitUntilAsync(() => !viewModel.IsExecuting);
    }

    [TestMethod]
    public async Task RunAllRemainingCreatesANewGroupAndCompletedInstanceCanRunAgain()
    {
        var targets = new[]
        {
            new MemuInstance(1, "One", true, 101),
            new MemuInstance(2, "Two", true, 102)
        };
        var engine = new PerInstanceBlockingEngine([1, 2]);
        var viewModel = CreateViewModel(new RecordingScriptStore(), engine, instanceService: new FixedInstanceService(targets));
        await viewModel.InitializeAsync(CancellationToken.None);
        await viewModel.RefreshCommand.ExecuteAsync();
        viewModel.RunTargets.Single(item => item.Index == 1).IsSelected = true;
        await viewModel.RunCommand.ExecuteAsync();
        await engine.WaitForStartsAsync(1);

        await viewModel.RunAllRemainingCommand.ExecuteAsync();
        await engine.WaitForStartsAsync(2);
        Assert.AreEqual(2, viewModel.ActiveLaunchGroupCount);
        Assert.AreEqual(2, viewModel.InstanceRuns.Count);

        viewModel.StopCommand.Execute(null);
        await engine.WaitForCancellationAsync(1);
        await engine.WaitForCancellationAsync(2);
        await WaitUntilAsync(() => !viewModel.IsExecuting);

        var immediate = new ReportingMultiEngine();
        var rerun = CreateViewModel(new RecordingScriptStore(), immediate, instanceService: new FixedInstanceService([targets[0]]));
        await rerun.InitializeAsync(CancellationToken.None);
        await rerun.RefreshCommand.ExecuteAsync();
        rerun.RunTargets.Single().IsSelected = true;
        await rerun.RunCommand.ExecuteAsync();
        await WaitUntilAsync(() => !rerun.IsExecuting);
        rerun.RunTargets.Single().IsSelected = true;
        await rerun.RunCommand.ExecuteAsync();
        await WaitUntilAsync(() => !rerun.IsExecuting);
        await rerun.RunAllRemainingCommand.ExecuteAsync();
        await WaitUntilAsync(() => !rerun.IsExecuting);
        Assert.AreEqual(3, rerun.InstanceRuns.Count);
        Assert.AreEqual(3, rerun.InstanceRuns.Select(item => item.LaunchGroupId).Distinct().Count());
    }

    [TestMethod]
    public async Task RunAllRemainingStartsANewSessionWhenAnUnadmittedTargetDisappearsOnRefresh()
    {
        var instances = new MutableInstanceService(
        [
            new MemuInstance(1, "One", true, 101),
            new MemuInstance(2, "Two", true, 102)
        ]);
        var engine = new ReportingMultiEngine();
        var viewModel = CreateViewModel(new RecordingScriptStore(), engine, instanceService: instances);
        await viewModel.InitializeAsync(CancellationToken.None);
        await viewModel.RefreshCommand.ExecuteAsync();
        viewModel.RunTargets.Single(item => item.Index == 1).IsSelected = true;
        await viewModel.RunCommand.ExecuteAsync();
        await WaitUntilAsync(() => !viewModel.IsExecuting);

        instances.Instances = [new MemuInstance(1, "One", true, 101)];
        await viewModel.RefreshCommand.ExecuteAsync();

        Assert.IsTrue(viewModel.RunAllRemainingCommand.CanExecute(null));
        await viewModel.RunAllRemainingCommand.ExecuteAsync();
        await WaitUntilAsync(() => !viewModel.IsExecuting);
        CollectionAssert.AreEqual(new[] { 1, 1 }, engine.Requests.Select(item => item.InstanceIndex).ToArray());
    }

    [TestMethod]
    public async Task RunCommand_RawShellDeclined_DoesNotInvokeEngine()
    {
        var engine = new ImmediateEngine();
        var rawScript = new ScriptDefinition { Name = "Raw", Steps = { new AndroidShellStep { Name = "Raw", Command = "echo ok" } } };
        var store = new RecordingScriptStore([rawScript]);
        var instances = new FixedInstanceService([new MemuInstance(3, "Target", true, 1)]);
        var viewModel = CreateViewModel(store, engine, new ConfigurableConfirmation(false), instanceService: instances);
        await viewModel.InitializeAsync(CancellationToken.None);
        await viewModel.RefreshCommand.ExecuteAsync();
        viewModel.RunTargets[0].IsSelected = true;

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
    public async Task SelectionCanChangeWhileExecutionIsRunningWithoutChangingTheSnapshot()
    {
        var engine = new BlockingEngine();
        var target = new MemuInstance(2, "Target", true, 456);
        var otherTarget = new MemuInstance(4, "Other", true, 789);
        var instances = new FixedInstanceService([target, otherTarget]);
        var viewModel = CreateViewModel(new RecordingScriptStore(), engine, instanceService: instances);
        await viewModel.InitializeAsync(CancellationToken.None);
        await viewModel.RefreshCommand.ExecuteAsync();
        var executingScript = viewModel.SelectedScript;
        var otherScript = new ScriptItemViewModel(new ScriptDefinition { Name = "Other" });
        viewModel.Scripts.Add(otherScript);
        viewModel.SelectedInstance = target;
        viewModel.RunTargets[0].IsSelected = true;

        var runTask = viewModel.RunCommand.ExecuteAsync();
        await engine.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        viewModel.SelectedScript = otherScript;
        viewModel.SelectedInstance = otherTarget;

        Assert.AreSame(otherScript, viewModel.SelectedScript);
        Assert.AreSame(otherTarget, viewModel.SelectedInstance);
        Assert.IsTrue(viewModel.CanChangeSelection);
        Assert.AreEqual(executingScript!.Model.Id, engine.LastRequest!.Script.Id);
        viewModel.StopCommand.Execute(null);
        await runTask.WaitAsync(TimeSpan.FromSeconds(2));
        await WaitUntilAsync(() => !viewModel.IsExecuting);
    }

    [TestMethod]
    public async Task LateProgressFromCompletedRun_IsIgnored()
    {
        var engine = new LateReportingEngine();
        var instances = new FixedInstanceService([new MemuInstance(2, "Target", true, 456)]);
        var viewModel = CreateViewModel(new RecordingScriptStore(), engine, instanceService: instances);
        await viewModel.InitializeAsync(CancellationToken.None);
        await viewModel.RefreshCommand.ExecuteAsync();
        viewModel.RunTargets[0].IsSelected = true;

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
        var tapOverlay = new RecordingTapOverlay();
        var overlay = new RecordingSwipeOverlay();
        var capture = new FixedInputCapture(
            new CapturedTap(120, 340),
            new CapturedSwipe(10, 20, 300, 400));
        var engine = new ImmediateEngine();
        var viewModel = CreateViewModel(new RecordingScriptStore(), engine, capture: capture, tapOverlay: tapOverlay, overlay: overlay);
        await viewModel.InitializeAsync(CancellationToken.None);
        viewModel.Instances.Add(new MemuInstance(2, "Target", true, 456, 998877));
        viewModel.SelectedInstance = viewModel.Instances[0];

        viewModel.EditorKind = ScriptStepKind.Tap;
        await viewModel.CaptureTapCommand.ExecuteAsync();
        Assert.AreEqual(120, viewModel.EditorX);
        Assert.AreEqual(340, viewModel.EditorY);
        Assert.AreEqual(1, tapOverlay.Updates.Count);
        Assert.IsTrue(tapOverlay.WasDisposed);

        viewModel.EditorKind = ScriptStepKind.Hold;
        viewModel.EditorHoldDuration = 850;
        await viewModel.CaptureHoldCommand.ExecuteAsync();
        Assert.AreEqual(120, viewModel.EditorX);
        Assert.AreEqual(340, viewModel.EditorY);
        Assert.AreEqual(850, viewModel.EditorHoldDuration, "Capture must preserve the manually entered hold duration.");
        Assert.AreEqual(2, tapOverlay.Updates.Count);

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
        var store = new RecordingScriptStore([CreateThreeStepScript()]);
        var viewModel = CreateViewModel(store, new ImmediateEngine(), capture: capture);
        await viewModel.InitializeAsync(CancellationToken.None);
        viewModel.Instances.Add(new MemuInstance(2, "Target", true, 456, 998877));
        viewModel.SelectedInstance = viewModel.Instances[0];
        viewModel.EditorKind = ScriptStepKind.Tap;
        var originalStep = viewModel.SelectedStep;
        viewModel.SynchronizeSelectedSteps([viewModel.Steps[0], viewModel.Steps[1]]);

        var captureTask = viewModel.CaptureTapCommand.ExecuteAsync();
        await capture.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        viewModel.SelectedStep = viewModel.Steps[1];
        viewModel.EditorKind = ScriptStepKind.Swipe;
        await viewModel.DeleteSelectedStepFromShortcutAsync();

        Assert.AreSame(originalStep, viewModel.SelectedStep);
        Assert.AreEqual(ScriptStepKind.Tap, viewModel.EditorKind);
        Assert.IsFalse(viewModel.CanChangeSelection);
        Assert.AreEqual(3, viewModel.Steps.Count);
        Assert.AreEqual(0, store.SaveCount);
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

    [TestMethod]
    public async Task BulkAssignmentClearsOnlyTheAcceptedOperationSelection()
    {
        var first = new ScriptDefinition { Name = "Script A", Steps = { new NoteStep { Name = "A" } } };
        var second = new ScriptDefinition { Name = "Script B", Steps = { new NoteStep { Name = "B" } } };
        var viewModel = CreateViewModel(
            new RecordingScriptStore([first, second]),
            new ImmediateEngine(),
            instanceService: new FixedInstanceService([new MemuInstance(0, "VM 0", true, 100), new MemuInstance(1, "VM 1", true, 101)]));
        await viewModel.InitializeAsync(CancellationToken.None);
        await viewModel.RefreshCommand.ExecuteAsync();
        viewModel.ScriptAssignmentMode = ScriptAssignmentMode.PerInstance;
        viewModel.BulkAssignmentScript = viewModel.Scripts.Single(item => item.Id == second.Id);
        foreach (var target in viewModel.RunTargets) target.IsSelected = true;

        await viewModel.AssignScriptToSelectedCommand.ExecuteAsync();

        Assert.IsTrue(viewModel.RunTargets.All(item => item.AssignedScriptId == second.Id));
        Assert.IsTrue(viewModel.RunTargets.All(item => !item.IsSelected && !item.IsLayoutSelected));
    }

    [TestMethod]
    public async Task PerInstanceAssignments_RunTheCorrectSnapshottedScriptForEveryTarget()
    {
        var first = new ScriptDefinition { Name = "Script A", Steps = { new NoteStep { Name = "A" } } };
        var second = new ScriptDefinition { Name = "Script B", Steps = { new NoteStep { Name = "B" } } };
        var engine = new ReportingMultiEngine();
        var instances = new FixedInstanceService(
        [
            new MemuInstance(0, "VM 0", true, 100, 1000),
            new MemuInstance(1, "VM 1", true, 101, 1001)
        ]);
        var viewModel = CreateViewModel(new RecordingScriptStore([first, second]), engine, instanceService: instances);
        await viewModel.InitializeAsync(CancellationToken.None);
        await viewModel.RefreshCommand.ExecuteAsync();
        foreach (var target in viewModel.RunTargets) target.IsSelected = true;
        viewModel.ScriptAssignmentMode = ScriptAssignmentMode.PerInstance;
        viewModel.RunTargets.Single(item => item.Index == 0).AssignedScriptId = first.Id;
        viewModel.RunTargets.Single(item => item.Index == 1).AssignedScriptId = second.Id;

        await viewModel.RunCommand.ExecuteAsync();

        var requests = engine.Requests.OrderBy(item => item.InstanceIndex).ToList();
        Assert.AreEqual(first.Id, requests[0].Script.Id);
        Assert.AreEqual(second.Id, requests[1].Script.Id);
        CollectionAssert.AreEqual(new[] { "Script A", "Script B" },
            viewModel.InstanceRuns.OrderBy(item => item.Index).Select(item => item.ScriptName).ToArray());
    }

    [TestMethod]
    public async Task PerInstanceAssignments_CanRunWhenTheEditorScriptIsEmptyButAssignedScriptsHaveSteps()
    {
        var empty = new ScriptDefinition { Name = "Empty editor script" };
        var assigned = new ScriptDefinition { Name = "Assigned", Steps = { new NoteStep { Name = "Run" } } };
        var engine = new ReportingMultiEngine();
        var instances = new FixedInstanceService([new MemuInstance(0, "VM 0", true, 100, 1000)]);
        var viewModel = CreateViewModel(new RecordingScriptStore([empty, assigned]), engine, instanceService: instances);
        await viewModel.InitializeAsync(CancellationToken.None);
        await viewModel.RefreshCommand.ExecuteAsync();
        viewModel.SelectedScript = viewModel.Scripts.Single(item => item.Id == empty.Id);
        viewModel.RunTargets.Single().IsSelected = true;
        viewModel.ScriptAssignmentMode = ScriptAssignmentMode.PerInstance;
        viewModel.RunTargets.Single().AssignedScriptId = assigned.Id;

        await viewModel.RunCommand.ExecuteAsync();

        Assert.AreEqual(1, engine.Requests.Count);
        Assert.AreEqual(assigned.Id, engine.Requests.Single().Script.Id);
    }

    [TestMethod]
    public async Task LayoutWorkspace_CapturesOriginalPlacementForInstancesDiscoveredLater()
    {
        var settings = new RecordingRunSettingsStore(new ApplicationSettings { MemucPath = @"C:\MEmu\memuc.exe" });
        var windows = new RecordingWindowLayoutService();
        var instances = new MutableInstanceService([new MemuInstance(0, "VM 0", true, 100, 1000)]);
        var viewModel = CreateViewModel(
            new RecordingScriptStore(),
            new ImmediateEngine(),
            instanceService: instances,
            settingsStore: settings,
            windowLayoutService: windows);
        await viewModel.InitializeAsync(CancellationToken.None);
        await viewModel.RefreshCommand.ExecuteAsync();
        await viewModel.ArrangeGridCommand.ExecuteAsync();
        instances.Instances =
        [
            new MemuInstance(0, "VM 0", true, 100, 1000),
            new MemuInstance(1, "VM 1", true, 101, 1001)
        ];

        await viewModel.RefreshCommand.ExecuteAsync();
        await viewModel.ArrangeGridCommand.ExecuteAsync();

        CollectionAssert.AreEquivalent(new[] { 0, 1 },
            settings.LastSaved!.WindowLayout.OriginalPlacements.Select(item => item.InstanceIndex).ToArray());
    }

    [TestMethod]
    public async Task LayoutWorkspace_MovesASelectedGroupAndPersistsTheEffectiveGrid()
    {
        var settings = new RecordingRunSettingsStore(new ApplicationSettings { MemucPath = @"C:\MEmu\memuc.exe" });
        var windows = new RecordingWindowLayoutService();
        var instances = new FixedInstanceService(
        [
            new MemuInstance(2, "Zulu", true, 102, 1002),
            new MemuInstance(0, "Alpha", true, 100, 1000),
            new MemuInstance(1, "Beta", true, 101, 1001)
        ]);
        var viewModel = CreateViewModel(
            new RecordingScriptStore(),
            new ImmediateEngine(),
            instanceService: instances,
            settingsStore: settings,
            windowLayoutService: windows);
        await viewModel.InitializeAsync(CancellationToken.None);
        await viewModel.RefreshCommand.ExecuteAsync();
        foreach (var target in viewModel.RunTargets.Take(2)) target.IsLayoutSelected = true;
        viewModel.LayoutMovePosition = 2;

        await viewModel.MoveLayoutToPositionCommand.ExecuteAsync();
        Assert.IsTrue(viewModel.RunTargets.All(item => !item.IsLayoutSelected));
        await viewModel.ArrangeGridCommand.ExecuteAsync();
        foreach (var target in viewModel.RunTargets) target.IsLayoutSelected = target.Index == 2;
        await viewModel.FocusEmulatorCommand.ExecuteAsync();

        CollectionAssert.AreEqual(new[] { 2, 0, 1 }, viewModel.RunTargets.Select(item => item.Index).ToArray());
        CollectionAssert.AreEqual(new[] { 2, 0, 1 }, windows.LastArrangedTargets.Select(item => item.InstanceIndex).ToArray());
        Assert.AreEqual(2, viewModel.EffectiveItemsPerPage);
        Assert.AreEqual(2, viewModel.LayoutPageCount);
        Assert.AreEqual("DISPLAY2", settings.LastSaved!.WindowLayout.DisplayDeviceName);
        CollectionAssert.AreEqual(new[] { 2, 0, 1 }, settings.LastSaved.WindowLayout.CustomOrder.ToArray());
        Assert.AreEqual(3, settings.LastSaved.WindowLayout.OriginalPlacements.Count);
        Assert.AreEqual(2, windows.LastFocusedIndex);
        Assert.AreEqual(2, viewModel.SelectedInstance!.Index);
        Assert.IsFalse(viewModel.FocusEmulatorCommand.CanExecute(null));
        Assert.IsTrue(viewModel.ReturnToGridCommand.CanExecute(null));
    }

    [TestMethod]
    public async Task LayoutWorkspace_DraggingAnUntickedRowMovesOnlyThatRow()
    {
        var settings = new RecordingRunSettingsStore(new ApplicationSettings { MemucPath = @"C:\MEmu\memuc.exe" });
        var viewModel = CreateViewModel(
            new RecordingScriptStore(),
            new ImmediateEngine(),
            instanceService: new FixedInstanceService(
            [
                new MemuInstance(0, "Zero", true, 100, 1000),
                new MemuInstance(1, "One", true, 101, 1001),
                new MemuInstance(2, "Two", true, 102, 1002)
            ]),
            settingsStore: settings,
            windowLayoutService: new RecordingWindowLayoutService());
        await viewModel.InitializeAsync(CancellationToken.None);
        await viewModel.RefreshCommand.ExecuteAsync();

        await viewModel.MoveLayoutTargetToAsync(viewModel.RunTargets.Single(item => item.Index == 2), 0);

        CollectionAssert.AreEqual(new[] { 2, 0, 1 }, viewModel.RunTargets.Select(item => item.Index).ToArray());
        Assert.IsTrue(viewModel.RunTargets.All(item => !item.IsLayoutSelected));
        CollectionAssert.AreEqual(new[] { 2, 0, 1 }, settings.LastSaved!.WindowLayout.CustomOrder.ToArray());
    }

    [TestMethod]
    public async Task LayoutWorkspace_DoesNotReportSuccessOrAdoptRejectedSinglePagePlan()
    {
        var windows = new RecordingWindowLayoutService { ApplySucceeded = false, Warning = "Hãy chọn Tự động phân trang." };
        var viewModel = CreateViewModel(
            new RecordingScriptStore(),
            new ImmediateEngine(),
            instanceService: new FixedInstanceService([new MemuInstance(0, "Zero", true, 100, 1000)]),
            windowLayoutService: windows);
        await viewModel.InitializeAsync(CancellationToken.None);
        await viewModel.RefreshCommand.ExecuteAsync();

        await viewModel.ArrangeGridCommand.ExecuteAsync();

        Assert.AreEqual(0, viewModel.LayoutPageCount);
        StringAssert.StartsWith(viewModel.StatusMessage, "Không thể xếp lưới");
        Assert.IsFalse(viewModel.StatusMessage.Contains("Đã xếp", StringComparison.Ordinal));
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        while (!condition() && DateTime.UtcNow < deadline) await Task.Delay(10);
        Assert.IsTrue(condition(), "Condition was not reached before timeout.");
    }

    private static MainViewModel CreateViewModel(
        IScriptStore store,
        IScriptExecutionEngine engine,
        IConfirmationService? confirmation = null,
        IApplicationPickerService? picker = null,
        IMemuInputCaptureService? capture = null,
        ITapCaptureOverlayService? tapOverlay = null,
        ISwipeCaptureOverlayService? overlay = null,
        IFileDialogService? fileDialog = null,
        IScriptTransferService? transfer = null,
        IScriptImportConflictService? importConflict = null,
        IMemuInstanceService? instanceService = null,
        ISettingsStore? settingsStore = null,
        IMemuWindowLayoutService? windowLayoutService = null)
    {
        var instances = instanceService ?? new EmptyInstanceService();
        var scheduler = new MultiInstanceExecutionScheduler(instances, engine, new ImmediateLaunchDelay(), new MinimumLaunchRandom());
        return new MainViewModel(
            instances, new ValidPathDiscovery(), settingsStore ?? new MemorySettingsStore(), fileDialog ?? new SelectedFileDialog(),
            store, scheduler, new ScriptStepCommandBuilder(new MemuCommandBuilder()), confirmation ?? new AlwaysConfirm(),
            picker ?? new NoopApplicationPicker(), capture ?? new NoopInputCapture(), tapOverlay ?? new NoopTapOverlay(), overlay ?? new NoopSwipeOverlay(),
            transfer, importConflict, windowLayoutService);
    }

    private static ApplicationPickerViewModel CreateApplicationNameLibraryViewModel(
        IMemuApplicationService applications,
        ApplicationSettings settings,
        ISettingsStore settingsStore,
        IFileDialogService? fileDialog = null,
        IApplicationNameTransferService? transfer = null,
        IApplicationNameImportConflictService? conflict = null) => new(
            applications,
            @"C:\MEmu\memuc.exe",
            0,
            displayNameOverrides: settings.ApplicationDisplayNames,
            settings: settings,
            settingsStore: settingsStore,
            fileDialogService: fileDialog ?? new SelectedFileDialog(),
            applicationNameTransferService: transfer ?? new RecordingApplicationNameTransferService(new Dictionary<string, string>()),
            importConflictService: conflict ?? new QueueApplicationNameConflict());

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

    private sealed class ReportingMultiEngine(int? failedIndex = null) : IScriptExecutionEngine
    {
        public System.Collections.Concurrent.ConcurrentBag<ExecutionRequest> Requests { get; } = [];

        public Task<ExecutionResult> ExecuteAsync(
            ExecutionRequest request,
            IProgress<StepExecutionUpdate>? progress,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            var now = DateTimeOffset.UtcNow;
            var status = request.InstanceIndex == failedIndex ? StepExecutionStatus.Failed : StepExecutionStatus.Succeeded;
            var step = request.Script.Steps[0];
            var stepResult = new StepExecutionResult
            {
                StepId = step.Id,
                Status = status,
                StartedAt = now,
                EndedAt = now,
                CommandPreview = $"instance-{request.InstanceIndex}",
                StandardOutput = $"instance-{request.InstanceIndex}"
            };
            progress?.Report(new StepExecutionUpdate(step.Id, StepExecutionStatus.Running));
            progress?.Report(new StepExecutionUpdate(step.Id, status, stepResult));
            return Task.FromResult(new ExecutionResult
            {
                StartedAt = now,
                EndedAt = now,
                Steps = [stepResult]
            });
        }
    }

    private sealed class BlockingEngine : IScriptExecutionEngine
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool WasCancelled { get; private set; }
        public ExecutionRequest? LastRequest { get; private set; }
        public async Task<ExecutionResult> ExecuteAsync(ExecutionRequest request, IProgress<StepExecutionUpdate>? progress, CancellationToken cancellationToken)
        {
            LastRequest = request;
            Started.TrySetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
            catch (OperationCanceledException) { WasCancelled = true; }
            return new ExecutionResult { StartedAt = DateTimeOffset.UtcNow, EndedAt = DateTimeOffset.UtcNow, WasCancelled = WasCancelled };
        }
    }

    private sealed class PerInstanceBlockingEngine(IEnumerable<int> instanceIndices) : IScriptExecutionEngine
    {
        private readonly System.Collections.Concurrent.ConcurrentDictionary<int, TaskCompletionSource<ExecutionResult>> completions =
            new(instanceIndices.ToDictionary(
                index => index,
                _ => new TaskCompletionSource<ExecutionResult>(TaskCreationOptions.RunContinuationsAsynchronously)));
        private readonly SemaphoreSlim startedSignal = new(0);
        private readonly SemaphoreSlim cancelledSignal = new(0);
        public System.Collections.Concurrent.ConcurrentBag<int> StartedIndices { get; } = [];
        public System.Collections.Concurrent.ConcurrentBag<int> CancelledIndices { get; } = [];

        public async Task<ExecutionResult> ExecuteAsync(
            ExecutionRequest request,
            IProgress<StepExecutionUpdate>? progress,
            CancellationToken cancellationToken)
        {
            StartedIndices.Add(request.InstanceIndex);
            startedSignal.Release();
            try
            {
                return await completions[request.InstanceIndex].Task.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                CancelledIndices.Add(request.InstanceIndex);
                cancelledSignal.Release();
                var now = DateTimeOffset.UtcNow;
                return new ExecutionResult { StartedAt = now, EndedAt = now, WasCancelled = true };
            }
        }

        public void Complete(int instanceIndex)
        {
            var now = DateTimeOffset.UtcNow;
            completions[instanceIndex].TrySetResult(new ExecutionResult { StartedAt = now, EndedAt = now });
        }

        public async Task WaitForStartsAsync(int count)
        {
            while (StartedIndices.Count < count)
                await startedSignal.WaitAsync(TimeSpan.FromSeconds(2));
        }

        public async Task WaitForCancellationAsync(int count)
        {
            while (CancelledIndices.Count < count)
                await cancelledSignal.WaitAsync(TimeSpan.FromSeconds(2));
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
    private sealed class FixedInstanceService(IReadOnlyList<MemuInstance> instances) : IMemuInstanceService
    {
        public Task<IReadOnlyList<MemuInstance>> GetInstancesAsync(string memucPath, CancellationToken cancellationToken) => Task.FromResult(instances);
    }
    private sealed class MutableInstanceService(IReadOnlyList<MemuInstance> instances) : IMemuInstanceService
    {
        public IReadOnlyList<MemuInstance> Instances { get; set; } = instances;
        public Task<IReadOnlyList<MemuInstance>> GetInstancesAsync(string memucPath, CancellationToken cancellationToken) => Task.FromResult(Instances);
    }
    private sealed class ImmediateLaunchDelay : ILaunchDelayProvider
    {
        public Task DelayAsync(TimeSpan duration, CancellationToken cancellationToken) => Task.CompletedTask;
    }
    private sealed class MinimumLaunchRandom : ILaunchSpacingRandom
    {
        public int NextInclusive(int minimumMilliseconds, int maximumMilliseconds) => minimumMilliseconds;
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
        public async Task<ApplicationSettings> UpdateAsync(Action<ApplicationSettings> update, CancellationToken cancellationToken)
        {
            var settings = await LoadAsync(cancellationToken);
            update(settings);
            return settings;
        }
    }
    private sealed class RecordingRunSettingsStore(ApplicationSettings loaded) : ISettingsStore
    {
        public int SaveCount { get; private set; }
        public ApplicationSettings? LastSaved { get; private set; }
        public Task<ApplicationSettings> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(loaded);
        public Task SaveAsync(ApplicationSettings settings, CancellationToken cancellationToken)
        {
            SaveCount++;
            LastSaved = CloneSettings(settings);
            return Task.CompletedTask;
        }
        public async Task<ApplicationSettings> UpdateAsync(Action<ApplicationSettings> update, CancellationToken cancellationToken)
        {
            var settings = await LoadAsync(cancellationToken);
            update(settings);
            await SaveAsync(settings, cancellationToken);
            return settings;
        }

        private static ApplicationSettings CloneSettings(ApplicationSettings settings)
        {
            var run = settings.MultiInstanceRun;
            var clone = new ApplicationSettings
            {
                MemucPath = settings.MemucPath,
                MultiInstanceRun = new MultiInstanceRunSettings
                {
                    LaunchSpacingMode = run.LaunchSpacingMode,
                    FixedSpacingMilliseconds = run.FixedSpacingMilliseconds,
                    RandomMinimumSpacingMilliseconds = run.RandomMinimumSpacingMilliseconds,
                    RandomMaximumSpacingMilliseconds = run.RandomMaximumSpacingMilliseconds,
                    StopAllOnInvalidTarget = run.StopAllOnInvalidTarget,
                    ScriptAssignmentMode = run.ScriptAssignmentMode
                },
                WindowLayout = new EmulatorWindowLayoutSettings()
            };
            foreach (var pair in run.ScriptAssignments) clone.MultiInstanceRun.ScriptAssignments[pair.Key] = pair.Value;
            clone.WindowLayout.SortMode = settings.WindowLayout.SortMode;
            clone.WindowLayout.ItemsPerPageMode = settings.WindowLayout.ItemsPerPageMode;
            clone.WindowLayout.CustomItemsPerPage = settings.WindowLayout.CustomItemsPerPage;
            clone.WindowLayout.ColumnMode = settings.WindowLayout.ColumnMode;
            clone.WindowLayout.CustomColumns = settings.WindowLayout.CustomColumns;
            clone.WindowLayout.SizeMode = settings.WindowLayout.SizeMode;
            clone.WindowLayout.CustomWidth = settings.WindowLayout.CustomWidth;
            clone.WindowLayout.CustomHeight = settings.WindowLayout.CustomHeight;
            clone.WindowLayout.PreserveAspectRatio = settings.WindowLayout.PreserveAspectRatio;
            clone.WindowLayout.Gap = settings.WindowLayout.Gap;
            clone.WindowLayout.DisplayDeviceName = settings.WindowLayout.DisplayDeviceName;
            clone.WindowLayout.CurrentPage = settings.WindowLayout.CurrentPage;
            clone.WindowLayout.CustomOrder.AddRange(settings.WindowLayout.CustomOrder);
            clone.WindowLayout.OriginalPlacements.AddRange(settings.WindowLayout.OriginalPlacements.Select(item => new SavedWindowPlacement
            {
                InstanceIndex = item.InstanceIndex,
                Left = item.Left,
                Top = item.Top,
                Width = item.Width,
                Height = item.Height
            }));
            foreach (var pair in settings.ApplicationDisplayNames) clone.ApplicationDisplayNames[pair.Key] = pair.Value;
            return clone;
        }
    }
    private sealed class BlockingUpdateSettingsStore(ApplicationSettings loaded) : ISettingsStore
    {
        public TaskCompletionSource UpdateStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseUpdate { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ApplicationSettings? LastSaved { get; private set; }

        public Task<ApplicationSettings> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(loaded);
        public Task SaveAsync(ApplicationSettings settings, CancellationToken cancellationToken)
        {
            LastSaved = settings;
            return Task.CompletedTask;
        }

        public async Task<ApplicationSettings> UpdateAsync(Action<ApplicationSettings> update, CancellationToken cancellationToken)
        {
            UpdateStarted.TrySetResult();
            await ReleaseUpdate.Task.WaitAsync(cancellationToken);
            update(loaded);
            await SaveAsync(loaded, cancellationToken);
            return loaded;
        }
    }
    private sealed class RecordingWindowLayoutService : IMemuWindowLayoutService
    {
        public IReadOnlyList<WindowLayoutTarget> LastArrangedTargets { get; private set; } = [];
        public int? LastFocusedIndex { get; private set; }
        public bool ApplySucceeded { get; init; } = true;
        public string? Warning { get; init; }

        public Task<IReadOnlyList<DisplayWorkArea>> GetDisplaysAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<DisplayWorkArea>>(
            [
                new DisplayWorkArea("DISPLAY2", new ScreenRectangle(800, 0, 1200, 900), true)
            ]);

        public Task<WindowLayoutApplyResult> ArrangeAsync(
            IReadOnlyList<WindowLayoutTarget> targets,
            EmulatorWindowLayoutSettings settings,
            int pageIndex,
            CancellationToken cancellationToken)
        {
            LastArrangedTargets = targets.ToList();
            return Task.FromResult(new WindowLayoutApplyResult
            {
                Applied = ApplySucceeded,
                Warning = Warning,
                Plan = new WindowGridPlan
                {
                    PageIndex = 0,
                    PageCount = 2,
                    ItemsPerPage = 2,
                    Columns = 2,
                    Rows = 1,
                    Placements = targets.Take(2).Select((target, index) => new PlannedWindowPlacement(
                        target.InstanceIndex,
                        target.WindowHandle,
                        0,
                        0,
                        index,
                        new ScreenRectangle(800 + index * 600, 0, 592, 900))).ToList()
                },
                CapturedOriginalPlacements = targets.Select(target => new SavedWindowPlacement
                {
                    InstanceIndex = target.InstanceIndex,
                    Left = 10,
                    Top = 10,
                    Width = 320,
                    Height = 480
                }).ToList()
            });
        }

        public Task<string?> FocusAsync(WindowLayoutTarget target, DisplayWorkArea display, CancellationToken cancellationToken)
        {
            LastFocusedIndex = target.InstanceIndex;
            return Task.FromResult<string?>(null);
        }

        public Task<string?> RestoreOriginalAsync(
            IReadOnlyList<WindowLayoutTarget> targets,
            IReadOnlyList<SavedWindowPlacement> placements,
            CancellationToken cancellationToken) => Task.FromResult<string?>(null);
    }

    private sealed class SelectedFileDialog : IFileDialogService
    {
        public string? SelectMemucPath(string? currentPath) => null;
        public string? SelectScriptImportPath() => null;
        public string? SelectScriptExportPath(string suggestedFileName) => null;
        public string? SelectApplicationNameImportPath() => null;
        public string? SelectApplicationNameExportPath(string suggestedFileName) => null;
    }
    private sealed class RecordingFileDialog(string? importPath, string? exportPath) : IFileDialogService
    {
        public string? SelectMemucPath(string? currentPath) => null;
        public string? SelectScriptImportPath() => importPath;
        public string? SelectScriptExportPath(string suggestedFileName) => exportPath;
        public string? SelectApplicationNameImportPath() => null;
        public string? SelectApplicationNameExportPath(string suggestedFileName) => null;
    }
    private sealed class ApplicationNameFileDialog(string? importPath, string? exportPath) : IFileDialogService
    {
        public string? SelectMemucPath(string? currentPath) => null;
        public string? SelectScriptImportPath() => null;
        public string? SelectScriptExportPath(string suggestedFileName) => null;
        public string? SelectApplicationNameImportPath() => importPath;
        public string? SelectApplicationNameExportPath(string suggestedFileName) => exportPath;
    }
    private sealed class RecordingApplicationSettingsStore(ApplicationSettings initial) : ISettingsStore
    {
        public int SaveCount { get; private set; }
        public ApplicationSettings? LastSaved { get; private set; }
        public Task<ApplicationSettings> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(initial);
        public Task SaveAsync(ApplicationSettings settings, CancellationToken cancellationToken)
        {
            SaveCount++;
            LastSaved = new ApplicationSettings
            {
                MemucPath = settings.MemucPath,
                MultiInstanceRun = new MultiInstanceRunSettings
                {
                    LaunchSpacingMode = settings.MultiInstanceRun.LaunchSpacingMode,
                    FixedSpacingMilliseconds = settings.MultiInstanceRun.FixedSpacingMilliseconds,
                    RandomMinimumSpacingMilliseconds = settings.MultiInstanceRun.RandomMinimumSpacingMilliseconds,
                    RandomMaximumSpacingMilliseconds = settings.MultiInstanceRun.RandomMaximumSpacingMilliseconds,
                    StopAllOnInvalidTarget = settings.MultiInstanceRun.StopAllOnInvalidTarget
                }
            };
            foreach (var pair in settings.ApplicationDisplayNames)
                LastSaved.ApplicationDisplayNames[pair.Key] = pair.Value;
            return Task.CompletedTask;
        }
        public async Task<ApplicationSettings> UpdateAsync(Action<ApplicationSettings> update, CancellationToken cancellationToken)
        {
            var settings = await LoadAsync(cancellationToken);
            update(settings);
            await SaveAsync(settings, cancellationToken);
            return settings;
        }
    }
    private sealed class RecordingApplicationNameTransferService(
        IReadOnlyDictionary<string, string> imported) : IApplicationNameTransferService
    {
        public string? ExportPath { get; private set; }
        public IReadOnlyDictionary<string, string>? ExportedNames { get; private set; }
        public Task ExportAsync(
            string path,
            IReadOnlyDictionary<string, string> applicationNames,
            CancellationToken cancellationToken)
        {
            ExportPath = path;
            ExportedNames = new Dictionary<string, string>(applicationNames, StringComparer.Ordinal);
            return Task.CompletedTask;
        }
        public Task<IReadOnlyDictionary<string, string>> ImportAsync(
            string path,
            CancellationToken cancellationToken) => Task.FromResult(imported);
    }
    private sealed class QueueApplicationNameConflict(
        params ApplicationNameImportConflictResolution[] resolutions) : IApplicationNameImportConflictService
    {
        private readonly Queue<ApplicationNameImportConflictResolution> resolutions = new(resolutions);
        public List<(string PackageName, string CurrentName, string ImportedName)> Calls { get; } = [];
        public ApplicationNameImportConflictResolution Resolve(
            string packageName,
            string currentDisplayName,
            string importedDisplayName)
        {
            Calls.Add((packageName, currentDisplayName, importedDisplayName));
            return resolutions.Dequeue();
        }
    }
    private sealed class RecordingScriptTransferService(IReadOnlyList<ScriptDefinition> imported) : IScriptTransferService
    {
        public List<IReadOnlyCollection<ScriptDefinition>> Exports { get; } = [];
        public string? LastExportPath { get; private set; }
        public Task ExportAsync(string path, IReadOnlyCollection<ScriptDefinition> scripts, CancellationToken cancellationToken)
        {
            LastExportPath = path;
            Exports.Add(scripts.ToList());
            return Task.CompletedTask;
        }
        public Task<IReadOnlyList<ScriptDefinition>> ImportAsync(string path, CancellationToken cancellationToken) =>
            Task.FromResult(imported);
    }
    private sealed class FixedImportConflict(ScriptImportConflictResolution resolution) : IScriptImportConflictService
    {
        public ScriptImportConflictResolution Resolve(ScriptDefinition importedScript) => resolution;
    }
    private sealed class AlwaysConfirm : IConfirmationService { public bool Confirm(string message, string title) => true; }
    private sealed class ConfigurableConfirmation(bool result) : IConfirmationService
    {
        public int CallCount { get; private set; }
        public string? LastMessage { get; private set; }
        public string? LastTitle { get; private set; }
        public bool Confirm(string message, string title)
        {
            CallCount++;
            LastMessage = message;
            LastTitle = title;
            return result;
        }
    }
    private sealed class QueueConfirmation(params bool[] results) : IConfirmationService
    {
        private readonly Queue<bool> results = new(results);
        public bool Confirm(string message, string title) => results.Dequeue();
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
    private sealed class FixedForegroundApplicationService(MemuApplicationInfo application) : IMemuForegroundApplicationService
    {
        public int InstanceIndex { get; private set; } = -1;
        public Task<MemuApplicationInfo> GetForegroundApplicationAsync(string memucPath, int instanceIndex, CancellationToken cancellationToken)
        {
            InstanceIndex = instanceIndex;
            return Task.FromResult(application);
        }
    }
    private sealed class NoopInputCapture : IMemuInputCaptureService
    {
        public Task<CapturedTap> CaptureTapAsync(string memucPath, MemuInstance instance, IProgress<TapCaptureUpdate>? progress, CancellationToken cancellationToken) =>
            Task.FromResult(new CapturedTap(0, 0));
        public Task<CapturedSwipe> CaptureSwipeAsync(string memucPath, MemuInstance instance, IProgress<SwipeCaptureUpdate>? progress, CancellationToken cancellationToken) =>
            Task.FromResult(new CapturedSwipe(0, 0, 0, 0));
    }
    private sealed class FixedInputCapture(CapturedTap tap, CapturedSwipe swipe) : IMemuInputCaptureService
    {
        public Task<CapturedTap> CaptureTapAsync(string memucPath, MemuInstance instance, IProgress<TapCaptureUpdate>? progress, CancellationToken cancellationToken)
        {
            progress?.Report(new TapCaptureUpdate(new ScreenRectangle(100, 200, 540, 960), 1080, 1920, new ScreenPoint(tap.X, tap.Y)));
            return Task.FromResult(tap);
        }
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
        public Task<CapturedTap> CaptureTapAsync(string memucPath, MemuInstance instance, IProgress<TapCaptureUpdate>? progress, CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            return TapResult.Task;
        }
        public Task<CapturedSwipe> CaptureSwipeAsync(string memucPath, MemuInstance instance, IProgress<SwipeCaptureUpdate>? progress, CancellationToken cancellationToken) =>
            Task.FromResult(new CapturedSwipe(0, 0, 0, 0));
    }

    private sealed class NoopTapOverlay : ITapCaptureOverlayService
    {
        public ITapCaptureOverlaySession Show() => new Session();
        private sealed class Session : ITapCaptureOverlaySession
        {
            public void Report(TapCaptureUpdate value) { }
            public void Dispose() { }
        }
    }

    private sealed class RecordingTapOverlay : ITapCaptureOverlayService, ITapCaptureOverlaySession
    {
        public List<TapCaptureUpdate> Updates { get; } = [];
        public bool WasDisposed { get; private set; }
        public ITapCaptureOverlaySession Show() => this;
        public void Report(TapCaptureUpdate value) => Updates.Add(value);
        public void Dispose() => WasDisposed = true;
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
