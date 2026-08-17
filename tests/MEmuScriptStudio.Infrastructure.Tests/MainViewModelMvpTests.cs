using MEmuScriptStudio.App.Services;
using MEmuScriptStudio.App.ViewModels;
using MEmuScriptStudio.App;
using MEmuScriptStudio.App.Converters;
using MEmuScriptStudio.App.Controls;
using MEmuScriptStudio.App.Behaviors;
using MEmuScriptStudio.App.Views;
using MEmuScriptStudio.Core.Android;
using MEmuScriptStudio.Core.Execution;
using MEmuScriptStudio.Core.Formatting;
using MEmuScriptStudio.Core.MEmu;
using MEmuScriptStudio.Core.Models;
using MEmuScriptStudio.Core.Scripts;
using MEmuScriptStudio.Infrastructure.Persistence;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows;
using System.Windows.Input;
using System.Windows.Documents;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.Windows.Media;

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

        var applicationXaml = File.ReadAllText(System.IO.Path.Combine(
            FindRepositoryRoot(), "src", "MEmuScriptStudio.App", "App.xaml"));
        StringAssert.Contains(
            applicationXaml,
            "ShutdownMode=\"OnMainWindowClose\"",
            "The application must declare close-driven MainWindow shutdown explicitly in App.xaml.");

        var viewModel = new ApplicationPickerViewModel(
            new MutableApplicationService([]), @"C:\MEmu\memuc.exe", 0);
        var window = new ApplicationPickerWindow(viewModel);

        Assert.IsTrue(BackgroundFocusBehavior.GetIsEnabled(window));
        Assert.IsNotNull(window.FindName("ManualDisplayNameTextBox"));
        Assert.IsNotNull(window.FindName("SaveApplicationNameButton"));
        var applicationsGrid = (DataGrid)window.FindName("ApplicationsGrid");
        Assert.IsFalse(ScrollViewer.GetIsDeferredScrollingEnabled(applicationsGrid));
        Assert.IsTrue(VirtualizingPanel.GetIsVirtualizing(applicationsGrid));
        Assert.AreEqual(VirtualizationMode.Recycling, VirtualizingPanel.GetVirtualizationMode(applicationsGrid));
    }

    [STATestMethod]
    public void AndroidApplicationPickerWindow_ExposesForegroundAndNameLibraryParityActions()
    {
        if (Application.Current is null)
        {
            var application = new MEmuScriptStudio.App.App();
            application.InitializeComponent();
        }

        var viewModel = new AndroidApplicationPickerViewModel(
            new FixedAndroidApplicationService([]), @"C:\Tools\adb.exe", "SERIAL-A");
        var window = new ApplicationPickerWindow(viewModel);

        Assert.IsTrue(viewModel.ShowForegroundApplication);
        Assert.IsTrue(viewModel.ShowNameLibrary);
        Assert.IsNotNull(window.FindName("ForegroundApplicationButton"));
        Assert.IsNotNull(window.FindName("SaveApplicationNameButton"));
        Assert.IsNotNull(window.FindName("DeleteApplicationNameButton"));
        Assert.IsNotNull(window.FindName("ImportApplicationNamesButton"));
        Assert.IsNotNull(window.FindName("ExportApplicationNamesButton"));
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
        var memucPath = (TextBlock)window.FindName("MemucPathTextBlock");
        var commandPreview = (TextBox)window.FindName("CommandPreviewTextBox");
        var stepsGrid = (DataGrid)window.FindName("StepsGrid");
        var pressEnter = (CheckBox)window.FindName("PressEnterAfterInputCheckBox");
        var workspace = (Grid)window.FindName("WorkspaceRoot");
        var instance = (ComboBox)window.FindName("InstanceComboBox");

        Assert.IsTrue(BackgroundFocusBehavior.GetIsEnabled(window));
        Assert.AreEqual(BindingMode.OneWay, BindingOperations.GetBinding(memucPath, TextBlock.TextProperty)!.Mode);
        Assert.AreEqual(BindingMode.OneWay, BindingOperations.GetBinding(commandPreview, TextBox.TextProperty)!.Mode);
        Assert.IsNull(BindingOperations.GetBinding(workspace, UIElement.IsEnabledProperty));
        Assert.AreEqual(nameof(MainViewModel.CanSelectEditorTarget),
            BindingOperations.GetBinding(instance, UIElement.IsEnabledProperty)!.Path.Path);
        Assert.AreEqual(nameof(MainViewModel.EditorTargets),
            BindingOperations.GetBinding(instance, ItemsControl.ItemsSourceProperty)!.Path.Path);
        Assert.AreEqual(nameof(MainViewModel.SelectedEditorTarget),
            BindingOperations.GetBinding(instance, Selector.SelectedItemProperty)!.Path.Path);
        Assert.IsNull(window.FindName("InitializationOverlay"));
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

    [TestMethod]
    public void AndroidCoordinateCaptureWindow_DeclaresUniformImageRefreshAndSwipeVisuals()
    {
        var xaml = File.ReadAllText(System.IO.Path.Combine(
            FindRepositoryRoot(), "src", "MEmuScriptStudio.App", "AndroidCoordinateCaptureWindow.xaml"));

        StringAssert.Contains(xaml, "Stretch=\"Uniform\"");
        StringAssert.Contains(xaml, "x:Name=\"RefreshButton\"");
        StringAssert.Contains(xaml, "MouseLeftButtonDown=\"Screenshot_MouseLeftButtonDown\"");
        StringAssert.Contains(xaml, "MouseMove=\"Screenshot_MouseMove\"");
        StringAssert.Contains(xaml, "x:Name=\"SwipeLine\"");
        StringAssert.Contains(xaml, "x:Name=\"StartMarker\"");
        StringAssert.Contains(xaml, "x:Name=\"EndMarker\"");
    }

    [STATestMethod]
    public void DurationInputs_BindToExistingMillisecondPropertiesAndLeaveHoldSwipeUnchanged()
    {
        if (Application.Current is null)
        {
            var application = new MEmuScriptStudio.App.App();
            application.InitializeComponent();
        }

        var window = new MainWindow(CreateViewModel(new RecordingScriptStore(), new ImmediateEngine()));
        var runPanel = new RunControlPanel();
        try
        {
            AssertDurationBinding(
                (DurationInputControl)window.FindName("EditorDelayInput"),
                nameof(MainViewModel.EditorDelayMilliseconds),
                nameof(MainViewModel.IsEditorDelayInputValid),
                nameof(MainViewModel.EditorDelayInputRefreshToken));
            AssertDurationBinding(
                (DurationInputControl)window.FindName("CompositeDelayInput"),
                nameof(MainViewModel.CompositeDelayMilliseconds),
                nameof(MainViewModel.IsCompositeDelayInputValid),
                nameof(MainViewModel.CompositeDelayInputRefreshToken));
            AssertDurationBinding(
                (DurationInputControl)runPanel.FindName("FixedSpacingInput"),
                nameof(MainViewModel.FixedSpacingMilliseconds),
                nameof(MainViewModel.IsFixedSpacingInputValid));
            AssertDurationBinding(
                (DurationInputControl)runPanel.FindName("RandomMinimumSpacingInput"),
                nameof(MainViewModel.RandomMinimumSpacingMilliseconds),
                nameof(MainViewModel.IsRandomMinimumSpacingInputValid));
            AssertDurationBinding(
                (DurationInputControl)runPanel.FindName("RandomMaximumSpacingInput"),
                nameof(MainViewModel.RandomMaximumSpacingMilliseconds),
                nameof(MainViewModel.IsRandomMaximumSpacingInputValid));

            var textBindings = FindLogicalDescendants<TextBox>(window)
                .Select(textBox => BindingOperations.GetBinding(textBox, TextBox.TextProperty)?.Path.Path)
                .Where(path => path is not null)
                .ToList();
            CollectionAssert.Contains(textBindings, nameof(MainViewModel.EditorHoldDuration));
            CollectionAssert.Contains(textBindings, nameof(MainViewModel.EditorSwipeDuration));
        }
        finally
        {
            window.Close();
        }
    }

    [STATestMethod]
    public void InvalidDurationDrafts_BlockSaveAndRunWithoutUsingPreviousMilliseconds()
    {
        if (Application.Current is null)
        {
            var application = new MEmuScriptStudio.App.App();
            application.InitializeComponent();
        }

        var engine = new ImmediateEngine();
        var regular = ScriptTemplateFactory.CreateRestartChrome();
        var composite = new ScriptDefinition
        {
            Name = "Composite delay",
            Kind = ScriptKind.Composite,
            CompositeItems = [new CompositeDelayItem { DurationMilliseconds = 1_000 }]
        };
        var store = new RecordingScriptStore([regular, composite]);
        var viewModel = CreateViewModel(
            store,
            engine,
            instanceService: new FixedInstanceService([new MemuInstance(1, "One", true, 101)]));
        viewModel.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();
        viewModel.RefreshCommand.ExecuteAsync().GetAwaiter().GetResult();
        viewModel.RunTargets.Single().IsSelected = true;
        var window = new MainWindow(viewModel);
        var runPanel = new RunControlPanel { DataContext = viewModel };
        try
        {
            viewModel.SelectedStep = viewModel.Steps.Single(step => step.Kind == ScriptStepKind.Delay);
            DrainDataBindings();
            var editorDelay = (DurationInputControl)window.FindName("EditorDelayInput");
            var originalDelay = ((DelayStep)viewModel.SelectedStep.Model).DurationMilliseconds;
            var saveCountBeforeInvalidDraft = store.SaveCount;
            ((TextBox)editorDelay.FindName("MinutesTextBox")).Text = "60";
            DrainDataBindings();
            Assert.IsFalse(viewModel.SaveStepCommand.CanExecute(null));
            viewModel.SaveStepCommand.ExecuteAsync().GetAwaiter().GetResult();
            Assert.AreEqual(originalDelay, ((DelayStep)viewModel.SelectedStep.Model).DurationMilliseconds);
            Assert.AreEqual(saveCountBeforeInvalidDraft, store.SaveCount);

            Assert.IsFalse(viewModel.RunCommand.CanExecute(null),
                "An invalid editor Delay draft must not run the previously persisted duration.");
            ((TextBox)editorDelay.FindName("MinutesTextBox")).Text = "0";
            DrainDataBindings();
            Assert.IsTrue(viewModel.RunCommand.CanExecute(null),
                $"Run should recover after correcting Delay input. Error: {viewModel.RunConfigurationError}; " +
                $"dirty={viewModel.IsEditorDirty}; delayValid={viewModel.IsEditorDelayInputValid}; bindingErrors={viewModel.HasEditorBindingErrors}");

            viewModel.HasEditorBindingErrors = true;
            Assert.IsFalse(viewModel.RunCommand.CanExecute(null));
            Assert.IsFalse(viewModel.RunAllRemainingCommand.CanExecute(null));
            viewModel.HasEditorBindingErrors = false;
            Assert.IsTrue(viewModel.RunCommand.CanExecute(null));

            viewModel.NavigateToScriptAsync(viewModel.Scripts.Single(script => script.Id == composite.Id))
                .GetAwaiter().GetResult();
            DrainDataBindings();
            viewModel.IsCompositeDelayInputValid = false;
            Assert.IsFalse(viewModel.RunCommand.CanExecute(null));
            Assert.IsFalse(viewModel.RunAllRemainingCommand.CanExecute(null));
            viewModel.IsCompositeDelayInputValid = true;
            Assert.IsTrue(viewModel.RunCommand.CanExecute(null));
            viewModel.NavigateToScriptAsync(viewModel.Scripts.Single(script => script.Id == regular.Id))
                .GetAwaiter().GetResult();
            DrainDataBindings();

            var fixedInput = (DurationInputControl)runPanel.FindName("FixedSpacingInput");
            ((TextBox)fixedInput.FindName("HoursTextBox")).Text = "999";
            DrainDataBindings();
            Assert.IsFalse(viewModel.IsFixedSpacingInputValid);
            Assert.IsFalse(viewModel.RunCommand.CanExecute(null));
            viewModel.RunCommand.ExecuteAsync().GetAwaiter().GetResult();
            Assert.IsNull(engine.LastRequest);

            ((TextBox)fixedInput.FindName("HoursTextBox")).Text = "0";
            viewModel.IsRandomSpacing = true;
            var randomMinimum = (DurationInputControl)runPanel.FindName("RandomMinimumSpacingInput");
            var randomMaximum = (DurationInputControl)runPanel.FindName("RandomMaximumSpacingInput");
            ((TextBox)randomMinimum.FindName("SecondsTextBox")).Text = "2";
            ((TextBox)randomMaximum.FindName("SecondsTextBox")).Text = "1";
            DrainDataBindings();
            Assert.IsFalse(viewModel.RunCommand.CanExecute(null), "Random minimum greater than maximum must remain blocked.");
            StringAssert.Contains(viewModel.RunConfigurationError, "tối thiểu");

            ((TextBox)randomMaximum.FindName("SecondsTextBox")).Text = "3";
            DrainDataBindings();
            Assert.IsTrue(viewModel.RunCommand.CanExecute(null));
            ((TextBox)randomMaximum.FindName("MinutesTextBox")).Text = "60";
            DrainDataBindings();
            Assert.IsFalse(viewModel.RunCommand.CanExecute(null));
            Assert.IsNull(engine.LastRequest);
        }
        finally
        {
            window.Close();
        }
    }

    [STATestMethod]
    public void DurationInputs_KeepInvalidDraftUntilCorrectedThenRefreshEqualValueSelection()
    {
        if (Application.Current is null)
        {
            var application = new MEmuScriptStudio.App.App();
            application.InitializeComponent();
        }

        var regular = new ScriptDefinition
        {
            Name = "Regular",
            Steps =
            [
                new DelayStep { Name = "First", DurationMilliseconds = 1000 },
                new DelayStep { Name = "Second", DurationMilliseconds = 1000 }
            ]
        };
        var composite = new ScriptDefinition
        {
            Name = "Composite",
            Kind = ScriptKind.Composite,
            CompositeItems =
            [
                new CompositeDelayItem { DurationMilliseconds = 1000 },
                new CompositeDelayItem { DurationMilliseconds = 1000 }
            ]
        };
        var viewModel = CreateViewModel(
            new RecordingScriptStore([regular, composite]),
            new ImmediateEngine(),
            new ConfigurableConfirmation(false));
        viewModel.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();
        var window = new MainWindow(viewModel);
        try
        {
            var editorInput = (DurationInputControl)window.FindName("EditorDelayInput");
            viewModel.SelectedStep = viewModel.Steps[0];
            DrainDataBindings();
            ((TextBox)editorInput.FindName("MinutesTextBox")).Text = "60";
            DrainDataBindings();
            Assert.IsFalse(editorInput.IsInputValid);

            viewModel.NavigateToStepAsync(viewModel.Steps[1]).GetAwaiter().GetResult();
            DrainDataBindings();
            Assert.AreSame(viewModel.Steps[0], viewModel.SelectedStep);
            AssertDurationParts(editorInput, "0", "60", "1", "0");
            Assert.IsFalse(editorInput.IsInputValid);

            ((TextBox)editorInput.FindName("MinutesTextBox")).Text = "0";
            viewModel.NavigateToStepAsync(viewModel.Steps[1]).GetAwaiter().GetResult();
            DrainDataBindings();
            AssertDurationParts(editorInput, "0", "0", "1", "0");
            Assert.IsTrue(editorInput.IsInputValid);
            Assert.IsFalse(viewModel.SaveStepCommand.CanExecute(null));

            viewModel.NavigateToScriptAsync(viewModel.Scripts.Single(script => script.Id == composite.Id))
                .GetAwaiter().GetResult();
            DrainDataBindings();
            var compositeInput = (DurationInputControl)window.FindName("CompositeDelayInput");
            viewModel.SelectedCompositeItem = viewModel.CompositeItems[0];
            DrainDataBindings();
            ((TextBox)compositeInput.FindName("MinutesTextBox")).Text = "60";
            DrainDataBindings();
            Assert.IsFalse(compositeInput.IsInputValid);

            viewModel.NavigateToCompositeItemAsync(viewModel.CompositeItems[1]).GetAwaiter().GetResult();
            DrainDataBindings();
            Assert.AreSame(viewModel.CompositeItems[0], viewModel.SelectedCompositeItem);
            AssertDurationParts(compositeInput, "0", "60", "1", "0");
            Assert.IsFalse(compositeInput.IsInputValid);

            ((TextBox)compositeInput.FindName("MinutesTextBox")).Text = "0";
            viewModel.NavigateToCompositeItemAsync(viewModel.CompositeItems[1]).GetAwaiter().GetResult();
            DrainDataBindings();
            AssertDurationParts(compositeInput, "0", "0", "1", "0");
            Assert.IsTrue(compositeInput.IsInputValid);
            Assert.IsFalse(viewModel.SaveCompositeItemCommand.CanExecute(null));
            Assert.IsTrue(viewModel.AddCompositeDelayCommand.CanExecute(null));
        }
        finally
        {
            window.Close();
        }
    }

    [STATestMethod]
    public async Task MainWindow_OnlyStepsGridEmptySpaceClearsStepSelection()
    {
        if (Application.Current is null)
        {
            var application = new MEmuScriptStudio.App.App();
            application.InitializeComponent();
        }

        var viewModel = CreateViewModel(new RecordingScriptStore([CreateThreeStepScript()]), new ImmediateEngine());
        viewModel.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();
        var window = new MainWindow(viewModel);
        try
        {
            var stepsGrid = (DataGrid)window.FindName("StepsGrid");
            viewModel.SynchronizeSelectedSteps([viewModel.Steps[0], viewModel.Steps[2]]);
            DrainDataBindings();
            var selectedCountBeforeOutsideClick = viewModel.SelectedStepCount;
            Assert.IsTrue(selectedCountBeforeOutsideClick > 0);

            Assert.IsFalse(await window.TryClearStepSelectionFromEmptyClickAsync(
                (DependencyObject)window.FindName("MainStatusBar")));
            Assert.AreEqual(selectedCountBeforeOutsideClick, viewModel.SelectedStepCount);

            Assert.IsTrue(await window.TryClearStepSelectionFromEmptyClickAsync(stepsGrid));
            Assert.AreEqual(0, stepsGrid.SelectedItems.Count);
            Assert.AreEqual(0, viewModel.SelectedStepCount);
            Assert.IsNull(viewModel.SelectedStep);
        }
        finally
        {
            window.Close();
        }
    }

    [STATestMethod]
    public void MainWindow_PropertiesAndStepActionBarPreserveSelectionForCommands()
    {
        if (Application.Current is null)
        {
            var application = new MEmuScriptStudio.App.App();
            application.InitializeComponent();
        }

        var source = CreateThreeStepScript();
        var viewModel = CreateViewModel(new RecordingScriptStore([source]), new ImmediateEngine());
        viewModel.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();
        var window = new MainWindow(viewModel);
        try
        {
            var stepsGrid = (DataGrid)window.FindName("StepsGrid");
            var propertiesPanel = (Border)window.FindName("StepPropertiesPanel");
            var editorInput = (TextBox)window.FindName("EditorNameTextBox");
            var actionBar = (WrapPanel)window.FindName("StepActionBar");
            var copyButton = (Button)window.FindName("CopyStepsButton");
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            stepsGrid.SelectedItems.Clear();
            stepsGrid.SelectedItems.Add(viewModel.Steps[0]);
            stepsGrid.SelectedItems.Add(viewModel.Steps[2]);
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.DataBind);

            Assert.IsFalse(MainWindow.IsStepsGridEmptySpaceSource(editorInput));
            Assert.IsFalse(MainWindow.IsStepsGridEmptySpaceSource(copyButton));
            Assert.IsTrue(BackgroundFocusBehavior.GetIsEnabled(window));
            Assert.AreEqual(2, viewModel.SelectedStepCount);
            Assert.IsTrue(copyButton.Command.CanExecute(copyButton.CommandParameter));

            copyButton.Command.Execute(copyButton.CommandParameter);

            StringAssert.Contains(viewModel.StepClipboardSummary, "Clipboard: 2");
            StringAssert.Contains(viewModel.StepClipboardSummary, source.Name);
            Assert.AreEqual(2, stepsGrid.SelectedItems.Count);
        }
        finally
        {
            window.Close();
        }
    }

    [STATestMethod]
    public void LightTheme_UsesSubtleButtonFocusAndNoCellFocusFrame()
    {
        if (Application.Current is null)
        {
            var application = new MEmuScriptStudio.App.App();
            application.InitializeComponent();
        }

        var focusBrush = (SolidColorBrush)Application.Current!.FindResource("FocusBrush");
        var focusStyle = (Style)Application.Current.FindResource("SharedFocusVisualStyle");
        var focusBorderStyle = (Style)Application.Current.FindResource("SharedFocusBorderStyle");
        var buttonStyle = (Style)Application.Current.FindResource("SecondaryButtonStyle");
        var cellStyle = (Style)Application.Current.FindResource(typeof(DataGridCell));

        Assert.AreEqual(Color.FromRgb(0x7D, 0x9F, 0xB2), focusBrush.Color);
        Assert.AreEqual(new Thickness(1), focusBorderStyle.Setters.Cast<Setter>()
            .Single(setter => setter.Property == Border.BorderThicknessProperty).Value);
        Assert.AreSame(focusBrush, focusBorderStyle.Setters.Cast<Setter>()
            .Single(setter => setter.Property == Border.BorderBrushProperty).Value);
        Assert.AreSame(focusStyle, buttonStyle.Setters.Cast<Setter>()
            .Single(setter => setter.Property == Control.FocusVisualStyleProperty).Value);
        Assert.IsNull(cellStyle.Setters.Cast<Setter>()
            .Single(setter => setter.Property == Control.FocusVisualStyleProperty).Value,
            "DataGridCell must rely on full-row selection instead of drawing its own focus frame.");
        Assert.AreEqual(new Thickness(0), cellStyle.Setters.Cast<Setter>()
            .Single(setter => setter.Property == Control.BorderThicknessProperty).Value);
    }

    [STATestMethod]
    public void StepsGrid_DoubleClickRowTogglesOnceButCheckboxDoesNotApplyRowToggle()
    {
        if (Application.Current is null)
        {
            var application = new MEmuScriptStudio.App.App();
            application.InitializeComponent();
        }

        var store = new RecordingScriptStore([CreateThreeStepScript()]);
        var viewModel = CreateViewModel(store, new ImmediateEngine());
        viewModel.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();
        var window = new MainWindow(viewModel);
        try
        {
            var grid = (DataGrid)window.FindName("StepsGrid");
            var step = viewModel.Steps[1];
            var row = new DataGridRow { DataContext = step };
            var changeCount = 0;
            step.IsEnabledChanged += (_, _) => changeCount++;
            grid.SelectedItems.Clear();
            grid.SelectedItems.Add(viewModel.Steps[0]);
            grid.SelectedItems.Add(viewModel.Steps[2]);
            var selectedBefore = grid.SelectedItems.Cast<StepItemViewModel>().ToArray();

            Assert.IsTrue(window.TryToggleStepFromDoubleClick(row));
            Assert.IsFalse(step.IsEnabled);
            Assert.AreEqual(1, changeCount);
            Assert.AreEqual(1, store.SaveCount);
            Assert.AreSame(step, grid.CurrentItem);
            CollectionAssert.AreEqual(selectedBefore, grid.SelectedItems.Cast<StepItemViewModel>().ToArray());

            Assert.IsFalse(window.TryToggleStepFromDoubleClick(new CheckBox { DataContext = step }));
            Assert.IsFalse(step.IsEnabled);
            Assert.AreEqual(1, changeCount);
            Assert.AreEqual(1, store.SaveCount);
        }
        finally
        {
            window.Close();
        }
    }

    [STATestMethod]
    public async Task CompositeGrid_EmptyClickPreservesCommandRegionsTogglesOnceAndUsesInsertionMarkerHooks()
    {
        if (Application.Current is null)
        {
            var application = new MEmuScriptStudio.App.App();
            application.InitializeComponent();
        }
        var child = new ScriptDefinition { Name = "Child", Steps = [new NoteStep { Name = "N" }] };
        var composite = new ScriptDefinition
        {
            Name = "Composite",
            Kind = ScriptKind.Composite,
            CompositeItems = [new ScriptReferenceItem { ScriptId = child.Id }]
        };
        var viewModel = CreateViewModel(new RecordingScriptStore([child, composite]), new ImmediateEngine());
        viewModel.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();
        viewModel.SelectedScript = viewModel.Scripts.Single(script => script.Id == composite.Id);
        var window = new MainWindow(viewModel);
        try
        {
            var grid = (DataGrid)window.FindName("CompositeItemsGrid");
            var actionBar = (Grid)window.FindName("CompositeActionBar");
            var properties = (Border)window.FindName("StepPropertiesPanel");
            var item = viewModel.CompositeItems.Single();
            viewModel.SynchronizeSelectedCompositeItems([item]);

            Assert.IsTrue(BackgroundFocusBehavior.GetIsEnabled(window));
            Assert.AreEqual(1, viewModel.SelectedCompositeItemCount);
            Assert.IsFalse(await window.TryClearStepSelectionFromEmptyClickAsync(grid));
            Assert.AreEqual(1, viewModel.SelectedCompositeItemCount);

            var row = new DataGridRow { Item = item, DataContext = item };
            Assert.IsTrue(window.TryToggleCompositeItemFromDoubleClick(row));
            Assert.IsFalse(item.IsEnabled);
            Assert.IsFalse(window.TryToggleCompositeItemFromDoubleClick(new CheckBox { DataContext = item }));
            Assert.IsFalse(item.IsEnabled);
            Assert.IsFalse(MainWindow.ShouldSuppressCompositeCheckboxClick(1));
            Assert.IsTrue(MainWindow.ShouldSuppressCompositeCheckboxClick(2));

            var xaml = File.ReadAllText(System.IO.Path.Combine(FindRepositoryRoot(), "src", "MEmuScriptStudio.App", "MainWindow.xaml"));
            StringAssert.Contains(xaml, "CompositeItemsGrid_DragLeave");
            StringAssert.Contains(xaml, "CompositeItemsGrid_MouseDoubleClick");
            var code = File.ReadAllText(System.IO.Path.Combine(FindRepositoryRoot(), "src", "MEmuScriptStudio.App", "MainWindow.xaml.cs"));
            StringAssert.Contains(code, "ShowInsertionAdorner(row, insertBefore)");
            StringAssert.Contains(code, "ClearInsertionAdorner()");

            Assert.AreEqual(140d, grid.Columns[0].Width.Value);
            Assert.IsTrue(grid.Columns[1].Width.IsStar);
            Assert.AreEqual(56d, grid.Columns[2].Width.Value);
            Assert.AreEqual(36d, grid.RowHeight);
            var description = (TextBlock)((DataGridTemplateColumn)grid.Columns[1]).CellTemplate.LoadContent();
            Assert.AreEqual(new Thickness(11, 0, 11, 0), description.Padding);
            Assert.AreEqual(TextTrimming.CharacterEllipsis, description.TextTrimming);
            Assert.AreEqual(nameof(CompositeItemViewModel.Description),
                BindingOperations.GetBinding(description, FrameworkElement.ToolTipProperty)!.Path.Path);
            Assert.AreEqual(4, actionBar.ColumnDefinitions.Count);
            Assert.AreEqual(2, actionBar.RowDefinitions.Count);
            Assert.IsTrue(actionBar.ColumnDefinitions.All(column => column.Width.IsStar));
        }
        finally { window.Close(); }
    }

    [STATestMethod]
    public void ApplicationPickerRowUsesStableThreeColumnLayout()
    {
        if (Application.Current is null)
        {
            var application = new MEmuScriptStudio.App.App();
            application.InitializeComponent();
        }
        var viewModel = CreateViewModel(new RecordingScriptStore([CreateThreeStepScript()]), new ImmediateEngine());
        viewModel.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();
        var window = new MainWindow(viewModel);
        try
        {
            var row = (Grid)window.FindName("ApplicationPickerRow");
            var textBox = (TextBox)window.FindName("PackageNameTextBox");
            var button = (Button)window.FindName("SelectApplicationButton");
            var displayName = (Border)window.FindName("ApplicationDisplayNameField");
            Assert.AreEqual(3, row.ColumnDefinitions.Count);
            Assert.IsTrue(row.ColumnDefinitions[0].Width.IsStar);
            Assert.AreEqual(8d, row.ColumnDefinitions[1].Width.Value);
            Assert.IsTrue(row.ColumnDefinitions[2].Width.IsAuto);
            Assert.AreEqual(34d, textBox.Height);
            Assert.AreEqual(34d, button.Height);
            Assert.AreEqual(116d, button.MinWidth);
            Assert.AreEqual(VerticalAlignment.Center, textBox.VerticalAlignment);
            Assert.AreEqual(VerticalAlignment.Center, button.VerticalAlignment);
            Assert.IsNotNull(displayName.Child as TextBlock);
            Assert.IsNull(window.FindName("ApplicationDisplayNameTextBox"));
        }
        finally { window.Close(); }
    }

    [STATestMethod]
    public void ScriptLibraryUsesReadableRowsAndThreeStarSizedEditorPanes()
    {
        if (Application.Current is null)
        {
            var application = new MEmuScriptStudio.App.App();
            application.InitializeComponent();
        }
        var viewModel = CreateViewModel(new RecordingScriptStore([CreateThreeStepScript()]), new ImmediateEngine());
        viewModel.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();
        var window = new MainWindow(viewModel)
        {
            Left = -32000,
            Top = -32000,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.None
        };
        window.Show();
        try
        {
            var list = (ListBox)window.FindName("ScriptsList");
            var search = (TextBox)window.FindName("ScriptLibrarySearchTextBox");
            var sort = (ComboBox)window.FindName("ScriptLibrarySortComboBox");
            var more = (Button)window.FindName("ScriptLibraryMoreButton");
            var morePopup = (Popup)window.FindName("ScriptLibraryMorePopup");
            var deviceSettings = (Button)window.FindName("DeviceSettingsButton");
            var deviceSettingsPopup = (Popup)window.FindName("DeviceSettingsPopup");
            var deviceSettingsPopupContent = (Border)window.FindName("DeviceSettingsPopupContent");
            var morePopupContent = (Border)window.FindName("ScriptLibraryMorePopupContent");
            var nameHeader = (TextBlock)window.FindName("ScriptNameColumnHeader");
            var kindHeader = (TextBlock)window.FindName("ScriptKindColumnHeader");
            var updatedHeader = (TextBlock)window.FindName("ScriptUpdatedColumnHeader");
            var nameLabel = (TextBlock)window.FindName("ScriptNameLabel");
            var testStep = (Button)window.FindName("TestStepButton");
            var row = (Grid)list.ItemTemplate.LoadContent();
            var texts = row.Children.OfType<TextBlock>().ToList();
            var layout = (Grid)window.FindName("EditorLayoutGrid");
            var splitter = (GridSplitter)window.FindName("LibraryEditorSplitter");
            var propertiesSplitter = (GridSplitter)window.FindName("StepsPropertiesSplitter");

            Assert.IsTrue(row.ColumnDefinitions[0].Width.IsStar);
            Assert.AreEqual(64d, row.ColumnDefinitions[1].Width.Value);
            Assert.AreEqual(128d, row.ColumnDefinitions[2].Width.Value);
            Assert.AreEqual(36d, row.Height);
            Assert.AreEqual(new Thickness(11, 0, 11, 0), texts[0].Margin);
            Assert.AreEqual(TextTrimming.CharacterEllipsis, texts[0].TextTrimming);
            Assert.AreEqual(nameof(ScriptItemViewModel.Name),
                BindingOperations.GetBinding(texts[0], FrameworkElement.ToolTipProperty)!.Path.Path);
            Assert.AreEqual(TextAlignment.Center, texts[1].TextAlignment);
            Assert.AreEqual(TextAlignment.Right, texts[2].TextAlignment);
            Assert.AreEqual(nameof(MainViewModel.ScriptLibrarySearchText),
                BindingOperations.GetBinding(search, TextBox.TextProperty)!.Path.Path);
            Assert.AreEqual(nameof(MainViewModel.SelectedScriptLibrarySortMode),
                BindingOperations.GetBinding(sort, Selector.SelectedValueProperty)!.Path.Path);
            Assert.AreEqual("Tên", nameHeader.Text);
            Assert.AreEqual("Loại", kindHeader.Text);
            Assert.AreEqual("Cập nhật", updatedHeader.Text);
            Assert.AreEqual("Tên kịch bản", nameLabel.Text);
            Assert.AreSame(more, morePopup.PlacementTarget);
            Assert.AreEqual("…", more.Content);
            Assert.IsNull(BindingOperations.GetBinding(list, Selector.SelectedItemProperty),
                "SelectedItems synchronization must be the only script-list selection source.");
            Assert.AreEqual(nameof(MainViewModel.TestStepCommand),
                BindingOperations.GetBinding(testStep, Button.CommandProperty)!.Path.Path);
            var stepActions = (DockPanel)LogicalTreeHelper.GetParent(testStep);
            Assert.IsFalse(stepActions.LastChildFill);
            Assert.AreEqual(Dock.Right, DockPanel.GetDock(testStep));
            more.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.IsTrue(morePopup.IsOpen);
            deviceSettings.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.IsFalse(morePopup.IsOpen);
            Assert.IsTrue(deviceSettingsPopup.IsOpen);
            deviceSettings.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.IsFalse(deviceSettingsPopup.IsOpen);

            more.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.IsTrue(morePopup.IsOpen);
            more.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.IsFalse(morePopup.IsOpen);
            var moreSuppression = typeof(MainWindow).GetField(
                "suppressScriptLibraryPopupReopen",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            moreSuppression.SetValue(window, true);
            more.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0)
            {
                RoutedEvent = Mouse.LostMouseCaptureEvent
            });
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            Assert.IsFalse((bool)moreSuppression.GetValue(window)!);
            more.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var keyboardClose = CreatePreviewKeyEvent(morePopupContent, Key.Escape);
            morePopupContent.RaiseEvent(keyboardClose);
            Assert.IsTrue(keyboardClose.Handled);
            Assert.IsFalse(morePopup.IsOpen);

            deviceSettings.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.IsTrue(deviceSettingsPopup.IsOpen);
            var deviceClose = CreatePreviewKeyEvent(deviceSettingsPopupContent, Key.Escape);
            deviceSettingsPopupContent.RaiseEvent(deviceClose);
            Assert.IsTrue(deviceClose.Handled);
            Assert.IsFalse(deviceSettingsPopup.IsOpen);
            Assert.AreEqual(36d, (double)((Setter)list.ItemContainerStyle.Setters
                .Single(setter => setter is Setter value && value.Property == FrameworkElement.HeightProperty)).Value);
            Assert.IsTrue(layout.ColumnDefinitions[0].Width.IsStar);
            Assert.AreEqual(5d, layout.ColumnDefinitions[0].Width.Value);
            Assert.AreEqual(240d, layout.ColumnDefinitions[0].MinWidth);
            Assert.AreEqual(double.PositiveInfinity, layout.ColumnDefinitions[0].MaxWidth);
            Assert.IsTrue(layout.ColumnDefinitions[2].Width.IsStar);
            Assert.AreEqual(8d, layout.ColumnDefinitions[2].Width.Value);
            Assert.AreEqual(340d, layout.ColumnDefinitions[2].MinWidth);
            Assert.IsTrue(layout.ColumnDefinitions[4].Width.IsStar);
            Assert.AreEqual(7d, layout.ColumnDefinitions[4].Width.Value);
            Assert.AreEqual(320d, layout.ColumnDefinitions[4].MinWidth);
            Assert.AreEqual(double.PositiveInfinity, layout.ColumnDefinitions[4].MaxWidth);
            Assert.AreEqual(1, Grid.GetColumn(splitter));
            Assert.AreEqual(GridResizeDirection.Columns, splitter.ResizeDirection);
            Assert.AreEqual(GridResizeBehavior.PreviousAndNext, splitter.ResizeBehavior);
            Assert.IsFalse(splitter.ShowsPreview);
            Assert.AreEqual(Cursors.SizeWE, splitter.Cursor);
            Assert.AreEqual(3, Grid.GetColumn(propertiesSplitter));
            Assert.AreEqual(GridResizeDirection.Columns, propertiesSplitter.ResizeDirection);
            Assert.AreEqual(GridResizeBehavior.PreviousAndNext, propertiesSplitter.ResizeBehavior);
            Assert.IsFalse(propertiesSplitter.ShowsPreview);
            Assert.AreEqual(Cursors.SizeWE, propertiesSplitter.Cursor);
        }
        finally { window.Close(); }
    }

    [TestMethod]
    public async Task ScriptLibrarySearchAndSortPreserveDefaultOrderAndSelection()
    {
        var bravo = new ScriptDefinition { Name = "Bravo", Steps = [new NoteStep { Name = "B" }] };
        var alpha = new ScriptDefinition { Name = "Alpha", Steps = [new NoteStep { Name = "A" }] };
        var charlie = new ScriptDefinition { Name = "Charlie", Steps = [new NoteStep { Name = "C" }] };
        var viewModel = CreateViewModel(
            new RecordingScriptStore([bravo, alpha, charlie]),
            new ImmediateEngine());
        await viewModel.InitializeAsync(CancellationToken.None);
        var selected = viewModel.SelectedScript;

        CollectionAssert.AreEqual(
            new[] { "Bravo", "Alpha", "Charlie" },
            viewModel.ScriptLibraryView.Cast<ScriptItemViewModel>().Select(item => item.Name).ToArray());

        viewModel.ScriptLibrarySearchText = "alp";
        CollectionAssert.AreEqual(
            new[] { "Alpha" },
            viewModel.ScriptLibraryView.Cast<ScriptItemViewModel>().Select(item => item.Name).ToArray());
        Assert.AreSame(selected, viewModel.SelectedScript);

        viewModel.ScriptLibrarySearchText = string.Empty;
        viewModel.SelectedScriptLibrarySortMode = ScriptLibrarySortMode.NameAscending;
        CollectionAssert.AreEqual(
            new[] { "Alpha", "Bravo", "Charlie" },
            viewModel.ScriptLibraryView.Cast<ScriptItemViewModel>().Select(item => item.Name).ToArray());
        Assert.AreSame(selected, viewModel.SelectedScript);

        viewModel.ScriptName = "Zulu";
        await viewModel.RenameScriptCommand.ExecuteAsync();
        CollectionAssert.AreEqual(
            new[] { "Alpha", "Charlie", "Zulu" },
            viewModel.ScriptLibraryView.Cast<ScriptItemViewModel>().Select(item => item.Name).ToArray());

        viewModel.SelectedScriptLibrarySortMode = ScriptLibrarySortMode.NameDescending;
        CollectionAssert.AreEqual(
            new[] { "Zulu", "Charlie", "Alpha" },
            viewModel.ScriptLibraryView.Cast<ScriptItemViewModel>().Select(item => item.Name).ToArray());

        viewModel.SelectedScriptLibrarySortMode = ScriptLibrarySortMode.Default;
        CollectionAssert.AreEqual(
            new[] { "Zulu", "Alpha", "Charlie" },
            viewModel.ScriptLibraryView.Cast<ScriptItemViewModel>().Select(item => item.Name).ToArray());
    }

    [TestMethod]
    public async Task ScriptLibraryDragReorderOnlyUsesDefaultUnfilteredProjection()
    {
        var alpha = new ScriptDefinition { Name = "Alpha" };
        var bravo = new ScriptDefinition { Name = "Bravo" };
        var charlie = new ScriptDefinition { Name = "Charlie" };
        var viewModel = CreateViewModel(new RecordingScriptStore([alpha, bravo, charlie]), new ImmediateEngine());
        await viewModel.InitializeAsync(CancellationToken.None);
        var first = viewModel.Scripts[0];
        var third = viewModel.Scripts[2];
        viewModel.SynchronizeSelectedScripts([first, third], first);

        Assert.IsTrue(viewModel.CanReorderScriptLibrary);
        Assert.IsTrue(viewModel.CanDragScript(first));
        await viewModel.MoveScriptsToAsync(first, 3);
        CollectionAssert.AreEqual(new[] { "Bravo", "Alpha", "Charlie" },
            viewModel.Scripts.Select(item => item.Name).ToArray());

        viewModel.SelectedScriptLibrarySortMode = ScriptLibrarySortMode.NameAscending;
        Assert.IsFalse(viewModel.CanReorderScriptLibrary);
        Assert.IsFalse(viewModel.CanDragScript(first));
        viewModel.SelectedScriptLibrarySortMode = ScriptLibrarySortMode.Default;
        viewModel.ScriptLibrarySearchText = "a";
        Assert.IsFalse(viewModel.CanReorderScriptLibrary);
        viewModel.ScriptLibrarySearchText = string.Empty;
        viewModel.ScriptLibraryFilter = ScriptLibraryFilter.Regular;
        Assert.IsFalse(viewModel.CanReorderScriptLibrary);
    }

    [STATestMethod]
    public void ScriptLibraryShortcutsOperateOnMultiSelectionAndRespectTextInputFocus()
    {
        if (Application.Current is null)
        {
            var application = new MEmuScriptStudio.App.App();
            application.InitializeComponent();
        }
        var store = new RecordingScriptStore([
            new ScriptDefinition { Name = "Alpha" },
            new ScriptDefinition { Name = "Bravo" },
            new ScriptDefinition { Name = "Charlie" }]);
        var confirmation = new ConfigurableConfirmation(true);
        var viewModel = CreateViewModel(store, new ImmediateEngine(), confirmation);
        viewModel.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();
        var window = new MainWindow(viewModel)
        {
            Left = -32000,
            Top = -32000,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.None
        };
        window.Show();
        try
        {
            var list = (ListBox)window.FindName("ScriptsList");
            var name = (TextBox)window.FindName("ScriptNameTextBox");
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            Assert.AreEqual(SelectionMode.Extended, list.SelectionMode);
            Assert.IsNull(BindingOperations.GetBinding(list, Selector.SelectedItemProperty));
            list.Focus();
            Keyboard.Focus(list);

            var selectAll = CreatePreviewKeyEvent(window, Key.A);
            window.HandleWindowPreviewKeyDownAsync(selectAll, ModifierKeys.Control, list).GetAwaiter().GetResult();
            Assert.IsTrue(selectAll.Handled);
            Assert.AreEqual(3, list.SelectedItems.Count);
            Assert.AreEqual(3, viewModel.SelectedScriptCount);
            Assert.AreEqual("Đã chọn: 3", viewModel.ScriptLibrarySelectionSummary);
            window.UpdateLayout();
            foreach (var item in viewModel.ScriptLibraryView.Cast<ScriptItemViewModel>())
            {
                list.ScrollIntoView(item);
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                window.UpdateLayout();
                var realizedContainer = (ListBoxItem?)list.ItemContainerGenerator.ContainerFromItem(item);
                if (realizedContainer is not null) Assert.IsTrue(realizedContainer.IsSelected);
            }

            var selectedForPointer = viewModel.Scripts[1];
            list.ScrollIntoView(selectedForPointer);
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            var selectedContainer = (ListBoxItem?)list.ItemContainerGenerator.ContainerFromItem(selectedForPointer) ??
                                    new ListBoxItem { DataContext = selectedForPointer };
            var pointerDown = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
            {
                RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent,
                Source = selectedContainer
            };
            list.RaiseEvent(pointerDown);
            Assert.IsTrue(pointerDown.Handled, "A possible group drag must retain the multi-selection until the drag threshold or mouse-up.");
            Assert.AreEqual(3, list.SelectedItems.Count);
            Assert.AreEqual(3, viewModel.SelectedScriptCount);
            var pointerUp = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
            {
                RoutedEvent = UIElement.PreviewMouseLeftButtonUpEvent,
                Source = selectedContainer
            };
            list.RaiseEvent(pointerUp);
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
            Assert.IsTrue(pointerUp.Handled);
            Assert.AreEqual(1, list.SelectedItems.Count);
            Assert.AreSame(selectedForPointer, list.SelectedItems[0]);
            Assert.AreEqual(1, viewModel.SelectedScriptCount);

            var ordinaryDown = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
            {
                RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent,
                Source = selectedContainer
            };
            list.RaiseEvent(ordinaryDown);
            Assert.IsFalse(ordinaryDown.Handled);
            var pendingDrag = typeof(MainWindow).GetField(
                "draggedScript",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            var ordinaryUp = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
            {
                RoutedEvent = UIElement.PreviewMouseLeftButtonUpEvent,
                Source = selectedContainer
            };
            list.RaiseEvent(ordinaryUp);
            Assert.IsNull(pendingDrag.GetValue(window), "An ordinary click must not leave a stale drag candidate.");
            pendingDrag.SetValue(window, selectedForPointer);
            list.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0)
            {
                RoutedEvent = Mouse.LostMouseCaptureEvent
            });
            Assert.IsNull(pendingDrag.GetValue(window), "Lost capture must clear a pending drag even before mouse-up.");

            var selectAllAgain = CreatePreviewKeyEvent(window, Key.A);
            window.HandleWindowPreviewKeyDownAsync(selectAllAgain, ModifierKeys.Control, list).GetAwaiter().GetResult();
            var escape = CreatePreviewKeyEvent(window, Key.Escape);
            window.HandleWindowPreviewKeyDownAsync(escape, ModifierKeys.None, list).GetAwaiter().GetResult();
            Assert.IsTrue(escape.Handled);
            Assert.AreEqual(1, list.SelectedItems.Count);
            Assert.AreEqual(1, viewModel.SelectedScriptCount);

            window.HandleWindowPreviewKeyDownAsync(selectAllAgain, ModifierKeys.Control, list).GetAwaiter().GetResult();

            var rename = CreatePreviewKeyEvent(window, Key.F2);
            window.HandleWindowPreviewKeyDownAsync(rename, ModifierKeys.None, list).GetAwaiter().GetResult();
            Assert.IsTrue(rename.Handled);
            Assert.AreEqual(name.Text.Length, name.SelectionLength);
            list.Focus();
            Keyboard.Focus(list);

            var duplicate = CreatePreviewKeyEvent(window, Key.D);
            window.HandleWindowPreviewKeyDownAsync(duplicate, ModifierKeys.Control, list).GetAwaiter().GetResult();
            Assert.IsTrue(duplicate.Handled);
            Assert.AreEqual(6, viewModel.Scripts.Count);
            Assert.AreEqual(3, viewModel.SelectedScriptCount);
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Input);

            name.Focus();
            Keyboard.Focus(name);
            var textDelete = CreatePreviewKeyEvent(window, Key.Delete);
            window.HandleWindowPreviewKeyDownAsync(textDelete, ModifierKeys.None, name).GetAwaiter().GetResult();
            Assert.IsFalse(textDelete.Handled);
            Assert.AreEqual(6, viewModel.Scripts.Count);

            list.Focus();
            Keyboard.Focus(list);
            var delete = CreatePreviewKeyEvent(window, Key.Delete);
            window.HandleWindowPreviewKeyDownAsync(delete, ModifierKeys.None, list).GetAwaiter().GetResult();
            Assert.IsTrue(delete.Handled);
            Assert.AreEqual(3, viewModel.Scripts.Count);
            Assert.AreEqual(1, confirmation.CallCount);

            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Input);
            Assert.AreEqual(1, viewModel.SelectedScriptCount);
            Assert.AreEqual(1, list.SelectedItems.Count);

            var blankClick = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
            {
                RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent
            };
            list.RaiseEvent(blankClick);
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
            Assert.IsTrue(blankClick.Handled);
            Assert.AreEqual(0, list.SelectedItems.Count);
            Assert.AreEqual(0, viewModel.SelectedScriptCount);
        }
        finally { window.Close(); }
    }

    [STATestMethod]
    public void ScriptLibraryFilteringKeepsLogicalAndVisualSelectionConsistent()
    {
        if (Application.Current is null)
        {
            var application = new MEmuScriptStudio.App.App();
            application.InitializeComponent();
        }

        var bravo = new ScriptDefinition { Name = "Bravo", Steps = [new NoteStep { Name = "B" }] };
        var alpha = new ScriptDefinition { Name = "Alpha", Steps = [new NoteStep { Name = "A" }] };
        var alpine = new ScriptDefinition { Name = "Alpine", Steps = [new NoteStep { Name = "A2" }] };
        var confirmation = new ConfigurableConfirmation(false);
        var viewModel = CreateViewModel(
            new RecordingScriptStore([bravo, alpha, alpine]),
            new ImmediateEngine(),
            confirmation);
        viewModel.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();
        var selected = viewModel.SelectedScript;
        var originalEditorName = viewModel.EditorName;
        var window = new MainWindow(viewModel);
        try
        {
            var list = (ListBox)window.FindName("ScriptsList");
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            var alphaItem = viewModel.Scripts.Single(item => item.Name == "Alpha");
            var alpineItem = viewModel.Scripts.Single(item => item.Name == "Alpine");
            list.SelectedItems.Add(alphaItem);
            Assert.AreEqual(2, viewModel.SelectedScriptCount);

            viewModel.EditorName = "B draft";
            Assert.IsTrue(viewModel.HasRegularEditorDraft);
            viewModel.ScriptLibrarySearchText = "alp";
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);

            Assert.AreEqual(string.Empty, viewModel.ScriptLibrarySearchText,
                "Rejecting dirty-draft navigation must reveal the prior selection instead of keeping hidden logical items.");
            Assert.AreEqual(2, list.SelectedItems.Count);
            Assert.AreEqual(2, viewModel.SelectedScriptCount);
            Assert.IsTrue(list.SelectedItems.Contains(alphaItem));
            Assert.AreSame(selected, viewModel.SelectedScript);
            Assert.IsTrue(viewModel.HasRegularEditorDraft);
            Assert.AreEqual(1, confirmation.CallCount);

            viewModel.EditorName = originalEditorName;
            viewModel.ScriptLibrarySearchText = "alp";
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
            CollectionAssert.AreEquivalent(new[] { alphaItem }, list.SelectedItems.Cast<ScriptItemViewModel>().ToArray());
            CollectionAssert.AreEquivalent(new[] { alphaItem }, viewModel.SelectedScripts.ToArray());
            Assert.AreSame(alphaItem, viewModel.SelectedScript);

            window.ApplyScriptListSelectionAsync([alphaItem, alpineItem], ModifierKeys.Control).GetAwaiter().GetResult();
            Assert.AreEqual(2, list.SelectedItems.Count);
            Assert.AreEqual(2, viewModel.SelectedScriptCount);
            viewModel.ScriptLibrarySearchText = "alpi";
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
            CollectionAssert.AreEquivalent(new[] { alpineItem }, list.SelectedItems.Cast<ScriptItemViewModel>().ToArray());
            CollectionAssert.AreEquivalent(new[] { alpineItem }, viewModel.SelectedScripts.ToArray());
            Assert.AreSame(alpineItem, viewModel.SelectedScript);
        }
        finally
        {
            viewModel.EditorName = originalEditorName;
            window.Close();
        }
    }

    [STATestMethod]
    public void MainWindow_ThreePaneSplittersKeepAdjacentMinimaAcrossNarrowAndWideResize()
    {
        if (Application.Current is null)
        {
            var application = new MEmuScriptStudio.App.App();
            application.InitializeComponent();
        }

        var viewModel = CreateViewModel(new RecordingScriptStore([CreateThreeStepScript()]), new ImmediateEngine());
        viewModel.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();
        var window = new MainWindow(viewModel);
        var layout = (Grid)window.FindName("EditorLayoutGrid");
        var workspace = (Grid)window.FindName("WorkspaceRoot");
        workspace.Children.Remove(layout);
        layout.DataContext = viewModel;
        using var host = new System.Windows.Interop.HwndSource(new System.Windows.Interop.HwndSourceParameters("MainWindowEditorSplitterTest")
        {
            Width = 1800,
            Height = 620,
            PositionX = -32000,
            PositionY = -32000,
            WindowStyle = unchecked((int)0x80000000)
        });
        try
        {
            host.RootVisual = layout;
            ArrangeEditorLayout(1068d);
            var librarySplitter = (GridSplitter)window.FindName("LibraryEditorSplitter");
            var propertiesSplitter = (GridSplitter)window.FindName("StepsPropertiesSplitter");

            for (var index = 0; index < 300; index++)
            {
                librarySplitter.RaiseEvent(new KeyEventArgs(
                    Keyboard.PrimaryDevice,
                    new TestPresentationSource { RootVisual = layout },
                    Environment.TickCount,
                    Key.Right) { RoutedEvent = Keyboard.KeyDownEvent });
                propertiesSplitter.RaiseEvent(new KeyEventArgs(
                    Keyboard.PrimaryDevice,
                    new TestPresentationSource { RootVisual = layout },
                    Environment.TickCount,
                    Key.Right) { RoutedEvent = Keyboard.KeyDownEvent });
            }
            layout.UpdateLayout();

            Assert.IsTrue(layout.ColumnDefinitions[0].ActualWidth >= 240d);
            Assert.IsTrue(layout.ColumnDefinitions[2].ActualWidth >= 340d);
            Assert.IsTrue(layout.ColumnDefinitions[4].ActualWidth >= 320d);
            Assert.IsTrue(layout.ColumnDefinitions[4].ActualWidth > 0d);

            window.ResetEditorPaneLayout();
            ArrangeEditorLayout(1248d);
            var propertiesBeforeBidirectionalDrag = layout.ColumnDefinitions[4].ActualWidth;
            for (var index = 0; index < 10; index++)
            {
                propertiesSplitter.RaiseEvent(new KeyEventArgs(
                    Keyboard.PrimaryDevice,
                    new TestPresentationSource { RootVisual = layout },
                    Environment.TickCount,
                    Key.Left) { RoutedEvent = Keyboard.KeyDownEvent });
            }
            layout.UpdateLayout();
            var propertiesAfterLeft = layout.ColumnDefinitions[4].ActualWidth;
            Assert.IsTrue(propertiesAfterLeft > propertiesBeforeBidirectionalDrag);
            for (var index = 0; index < 20; index++)
            {
                propertiesSplitter.RaiseEvent(new KeyEventArgs(
                    Keyboard.PrimaryDevice,
                    new TestPresentationSource { RootVisual = layout },
                    Environment.TickCount,
                    Key.Right) { RoutedEvent = Keyboard.KeyDownEvent });
            }
            layout.UpdateLayout();
            Assert.IsTrue(layout.ColumnDefinitions[4].ActualWidth < propertiesAfterLeft);
            Assert.IsTrue(layout.ColumnDefinitions[2].ActualWidth >= 340d);
            Assert.IsTrue(layout.ColumnDefinitions[4].ActualWidth >= 320d);

            window.ResetEditorPaneLayout();
            ArrangeEditorLayout(1768d);

            Assert.IsTrue(layout.ColumnDefinitions[0].ActualWidth > 240d);
            Assert.IsTrue(layout.ColumnDefinitions[2].ActualWidth > 340d);
            Assert.IsTrue(layout.ColumnDefinitions[4].ActualWidth > 320d);
            Assert.AreEqual(
                layout.ActualWidth,
                layout.ColumnDefinitions.Sum(column => column.ActualWidth),
                1d,
                "The three panes and splitters should consume the available wide-window width.");
            Assert.IsTrue(layout.ColumnDefinitions[0].Width.IsStar);
            Assert.IsTrue(layout.ColumnDefinitions[2].Width.IsStar);
            Assert.IsTrue(layout.ColumnDefinitions[4].Width.IsStar);

            void ArrangeEditorLayout(double width)
            {
                layout.Measure(new Size(width, 620d));
                layout.Arrange(new Rect(0d, 0d, width, 620d));
                layout.UpdateLayout();
            }
        }
        finally
        {
            window.Close();
        }
    }

    [STATestMethod]
    public void MainWindow_StepShortcutsUseButtonCommandsAcrossScriptsWithoutGridFocus()
    {
        if (Application.Current is null)
        {
            var application = new MEmuScriptStudio.App.App();
            application.InitializeComponent();
        }

        var source = CreateThreeStepScript();
        var target = new ScriptDefinition { Name = "Target" };
        var store = new RecordingScriptStore([source, target]);
        var viewModel = CreateViewModel(store, new ImmediateEngine());
        viewModel.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();
        var pasteCanExecuteChangedCount = 0;
        viewModel.PasteStepsCommand.CanExecuteChanged += (_, _) => pasteCanExecuteChangedCount++;
        var window = new MainWindow(viewModel);
        try
        {
            var scriptsList = (ListBox)window.FindName("ScriptsList");
            var stepsGrid = (DataGrid)window.FindName("StepsGrid");
            var stepsHeader = (Grid)window.FindName("StepsHeaderGrid");
            var deleteButton = (Button)window.FindName("DeleteStepsButton");
            var copyButton = (Button)window.FindName("CopyStepsButton");
            var pasteButton = (Button)window.FindName("PasteStepsButton");
            var undoButton = (Button)window.FindName("UndoStepsButton");
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.DataBind);

            Assert.AreSame(viewModel.DeleteStepCommand, deleteButton.Command);
            Assert.AreSame(viewModel.CopyStepsCommand, copyButton.Command);
            Assert.AreSame(viewModel.PasteStepsCommand, pasteButton.Command);
            Assert.AreSame(viewModel.UndoStepListCommand, undoButton.Command);
            Assert.IsFalse(viewModel.PasteStepsCommand.CanExecute(null));

            stepsGrid.SelectedItems.Clear();
            stepsGrid.SelectedItems.Add(viewModel.Steps[0]);
            stepsGrid.SelectedItems.Add(viewModel.Steps[2]);
            Assert.IsTrue(viewModel.CopyStepsCommand.CanExecute(null));
            Assert.IsTrue(copyButton.IsEnabled);
            var copyKey = CreatePreviewKeyEvent(window, Key.C);
            window.HandleWindowPreviewKeyDownAsync(copyKey, ModifierKeys.Control, scriptsList)
                .GetAwaiter().GetResult();

            Assert.IsTrue(copyKey.Handled);
            Assert.AreEqual($"Clipboard: 2 bước từ “{source.Name}”", viewModel.StepClipboardSummary);
            Assert.IsTrue(viewModel.PasteStepsCommand.CanExecute(null));
            Assert.IsTrue(pasteCanExecuteChangedCount > 0);
            var canExecuteChangedAfterCopy = pasteCanExecuteChangedCount;
            scriptsList.SelectedItem = viewModel.Scripts.Single(item => item.Id == target.Id);
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.DataBind);

            Assert.AreEqual(target.Id, viewModel.SelectedScript!.Id);
            Assert.IsTrue(pasteCanExecuteChangedCount > canExecuteChangedAfterCopy);
            Assert.IsFalse(stepsGrid.IsKeyboardFocusWithin);
            Assert.IsTrue(viewModel.PasteStepsCommand.CanExecute(null));
            Assert.IsTrue(pasteButton.IsEnabled);
            var pasteKey = CreatePreviewKeyEvent(window, Key.V);
            window.HandleWindowPreviewKeyDownAsync(pasteKey, ModifierKeys.Control, scriptsList)
                .GetAwaiter().GetResult();

            Assert.IsTrue(pasteKey.Handled);
            CollectionAssert.AreEqual(new[] { "A", "C" }, target.Steps.Select(step => step.Name).ToArray());
            Assert.IsTrue(target.Steps.All(step => source.Steps.All(sourceStep => step.Id != sourceStep.Id)));
            Assert.IsTrue(target.Steps.All(step => source.Steps.All(sourceStep => !ReferenceEquals(step, sourceStep))));
            Assert.IsTrue(viewModel.UndoStepListCommand.CanExecute(null));
            var undoKey = CreatePreviewKeyEvent(window, Key.Z);
            window.HandleWindowPreviewKeyDownAsync(undoKey, ModifierKeys.Control, scriptsList)
                .GetAwaiter().GetResult();
            Assert.IsTrue(undoKey.Handled);
            Assert.AreEqual(0, target.Steps.Count);
            Assert.AreEqual(3, source.Steps.Count);
            Assert.IsFalse(viewModel.UndoStepListCommand.CanExecute(null));

            scriptsList.SelectedItem = viewModel.Scripts.Single(item => item.Id == source.Id);
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            stepsGrid.SelectedItems.Clear();
            stepsGrid.SelectedItems.Add(viewModel.Steps[0]);
            stepsGrid.SelectedItems.Add(viewModel.Steps[2]);
            var deleteKey = CreatePreviewKeyEvent(window, Key.Delete);
            window.HandleWindowPreviewKeyDownAsync(deleteKey, ModifierKeys.None, stepsHeader)
                .GetAwaiter().GetResult();

            Assert.IsTrue(deleteKey.Handled);
            CollectionAssert.AreEqual(new[] { "B" }, source.Steps.Select(step => step.Name).ToArray());
        }
        finally
        {
            window.Close();
        }
    }

    [STATestMethod]
    public void MainWindow_TextInputShortcutsDoNotInvokeStepCommands()
    {
        if (Application.Current is null)
        {
            var application = new MEmuScriptStudio.App.App();
            application.InitializeComponent();
        }

        var source = CreateThreeStepScript();
        var target = new ScriptDefinition
        {
            Name = "Target",
            Steps = [new NoteStep { Name = "Target step", Text = "Target" }]
        };
        var viewModel = CreateViewModel(new RecordingScriptStore([source, target]), new ImmediateEngine());
        viewModel.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();
        viewModel.CopyStepsCommand.Execute(null);
        viewModel.SelectedScript = viewModel.Scripts.Single(item => item.Id == target.Id);
        viewModel.DuplicateStepCommand.ExecuteAsync().GetAwaiter().GetResult();
        var window = new MainWindow(viewModel);
        try
        {
            var textBox = (TextBox)window.FindName("EditorNameTextBox");
            var stepsGrid = (DataGrid)window.FindName("StepsGrid");
            FocusManager.SetFocusedElement(window, textBox);

            Assert.AreSame(textBox, FocusManager.GetFocusedElement(window));
            Assert.IsFalse(stepsGrid.IsKeyboardFocusWithin);
            Assert.IsTrue(viewModel.PasteStepsCommand.CanExecute(null));
            var expectedClipboard = viewModel.StepClipboardSummary;
            var expectedIds = target.Steps.Select(step => step.Id).ToArray();
            foreach (var (key, modifiers) in new[]
                     {
                         (Key.C, ModifierKeys.Control),
                         (Key.V, ModifierKeys.Control),
                         (Key.Z, ModifierKeys.Control),
                         (Key.Delete, ModifierKeys.None)
                     })
            {
                var keyEvent = CreatePreviewKeyEvent(window, key);
                window.HandleWindowPreviewKeyDownAsync(keyEvent, modifiers, textBox)
                    .GetAwaiter().GetResult();
                Assert.IsFalse(keyEvent.Handled, $"{key} must remain available to the focused TextBox.");
            }

            var editableCombo = new ComboBox { IsEditable = true };
            foreach (var (key, modifiers) in new[]
                     {
                         (Key.C, ModifierKeys.Control),
                         (Key.V, ModifierKeys.Control),
                         (Key.Z, ModifierKeys.Control),
                         (Key.Delete, ModifierKeys.None)
                     })
            {
                var keyEvent = CreatePreviewKeyEvent(window, key);
                window.HandleWindowPreviewKeyDownAsync(keyEvent, modifiers, editableCombo)
                    .GetAwaiter().GetResult();
                Assert.IsFalse(keyEvent.Handled, $"{key} must remain available to the editable ComboBox.");
            }

            CollectionAssert.AreEqual(expectedIds, target.Steps.Select(step => step.Id).ToArray());
            Assert.AreEqual(expectedClipboard, viewModel.StepClipboardSummary);
            Assert.IsTrue(viewModel.UndoStepListCommand.CanExecute(null));
            Assert.IsTrue(viewModel.PasteStepsCommand.CanExecute(null));
        }
        finally
        {
            window.Close();
        }
    }

    [STATestMethod]
    public void MainWindow_ContextualCtrlSFlushesAndRoutesToTheExistingCommandWhileExecutionIsActive()
    {
        if (Application.Current is null)
        {
            var application = new MEmuScriptStudio.App.App();
            application.InitializeComponent();
        }

        var regular = CreateThreeStepScript();
        var composite = new ScriptDefinition
        {
            Name = "Composite",
            Kind = ScriptKind.Composite,
            CompositeItems = [new CompositeDelayItem { DurationMilliseconds = 1000 }]
        };
        var store = new RecordingScriptStore([regular, composite]);
        var engine = new PerInstanceBlockingEngine([1]);
        var viewModel = CreateViewModel(
            store,
            engine,
            instanceService: new FixedInstanceService([new MemuInstance(1, "One", true, 101)]));
        viewModel.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();
        viewModel.RefreshCommand.ExecuteAsync().GetAwaiter().GetResult();
        viewModel.RunTargets.Single().IsSelected = true;
        var previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(
            new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        viewModel.RunCommand.ExecuteAsync().GetAwaiter().GetResult();
        engine.WaitForStartsAsync(1).GetAwaiter().GetResult();

        var window = new MainWindow(viewModel);
        try
        {
            Assert.IsTrue(viewModel.IsExecuting);
            var scriptName = (TextBox)window.FindName("ScriptNameTextBox");
            var renameButton = (Button)window.FindName("RenameScriptButton");
            var editorName = (TextBox)window.FindName("EditorNameTextBox");
            var saveStepButton = (Button)window.FindName("SaveStepButton");
            var compositeDelay = (DurationInputControl)window.FindName("CompositeDelayInput");
            var compositeMinutes = (TextBox)compositeDelay.FindName("MinutesTextBox");
            var compositeSeconds = (TextBox)compositeDelay.FindName("SecondsTextBox");
            var compositeMilliseconds = (TextBox)compositeDelay.FindName("MillisecondsTextBox");
            var saveCompositeButton = (Button)window.FindName("SaveCompositeItemButton");
            var scriptsList = (ListBox)window.FindName("ScriptsList");
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.DataBind);

            Assert.AreSame(viewModel.RenameScriptCommand, renameButton.Command);
            Assert.AreSame(viewModel.SaveStepCommand, saveStepButton.Command);
            Assert.AreSame(viewModel.SaveCompositeItemCommand, saveCompositeButton.Command);

            BindingOperations.SetBinding(scriptName, TextBox.TextProperty, new Binding(nameof(MainViewModel.ScriptName))
            {
                Source = viewModel,
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.Explicit
            });
            viewModel.SelectedScript = viewModel.Scripts.Single(script => script.Id == regular.Id);
            scriptName.GetBindingExpression(TextBox.TextProperty)!.UpdateTarget();
            scriptName.Text = "Regular qua Ctrl+S";
            var saveCountBeforeRegularRename = store.SaveCount;
            var regularRename = CreatePreviewKeyEvent(window, Key.S);
            window.HandleWindowPreviewKeyDownAsync(regularRename, ModifierKeys.Control, scriptName)
                .GetAwaiter().GetResult();
            Assert.IsTrue(regularRename.Handled);
            Assert.AreEqual("Regular qua Ctrl+S", regular.Name);
            Assert.AreEqual(saveCountBeforeRegularRename + 1, store.SaveCount);

            viewModel.SelectedScript = viewModel.Scripts.Single(script => script.Id == composite.Id);
            scriptName.GetBindingExpression(TextBox.TextProperty)!.UpdateTarget();
            scriptName.Text = "Composite qua Ctrl+S";
            var saveCountBeforeCompositeRename = store.SaveCount;
            var compositeRename = CreatePreviewKeyEvent(window, Key.S);
            window.HandleWindowPreviewKeyDownAsync(compositeRename, ModifierKeys.Control, scriptName)
                .GetAwaiter().GetResult();
            Assert.IsTrue(compositeRename.Handled);
            Assert.AreEqual("Composite qua Ctrl+S", composite.Name);
            Assert.AreEqual(saveCountBeforeCompositeRename + 1, store.SaveCount);

            viewModel.SelectedScript = viewModel.Scripts.Single(script => script.Id == regular.Id);
            BindingOperations.SetBinding(editorName, TextBox.TextProperty, new Binding(nameof(MainViewModel.EditorName))
            {
                Source = viewModel,
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.Explicit
            });
            editorName.GetBindingExpression(TextBox.TextProperty)!.UpdateTarget();
            editorName.Text = "Bước qua Ctrl+S";
            var saveCountBeforeStepSave = store.SaveCount;
            var stepSave = CreatePreviewKeyEvent(window, Key.S);
            window.HandleWindowPreviewKeyDownAsync(stepSave, ModifierKeys.Control, editorName)
                .GetAwaiter().GetResult();
            Assert.IsTrue(stepSave.Handled);
            Assert.AreEqual("Bước qua Ctrl+S", regular.Steps[0].Name);
            Assert.AreEqual(saveCountBeforeStepSave + 1, store.SaveCount);

            viewModel.SelectedScript = viewModel.Scripts.Single(script => script.Id == composite.Id);
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            compositeMinutes.Text = "60";
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            Assert.IsFalse(viewModel.SaveCompositeItemCommand.CanExecute(null));
            var invalidCompositeSave = CreatePreviewKeyEvent(window, Key.S);
            var saveCountBeforeInvalidCompositeSave = store.SaveCount;
            window.HandleWindowPreviewKeyDownAsync(invalidCompositeSave, ModifierKeys.Control, compositeMinutes)
                .GetAwaiter().GetResult();
            Assert.IsTrue(invalidCompositeSave.Handled);
            Assert.AreEqual(saveCountBeforeInvalidCompositeSave, store.SaveCount);
            Assert.AreEqual(1000, ((CompositeDelayItem)composite.CompositeItems[0]).DurationMilliseconds);

            compositeMinutes.Text = "0";
            compositeSeconds.Text = "2";
            compositeMilliseconds.Text = "345";
            var saveCountBeforeCompositeSave = store.SaveCount;
            var compositeSave = CreatePreviewKeyEvent(window, Key.S);
            window.HandleWindowPreviewKeyDownAsync(compositeSave, ModifierKeys.Control, compositeMilliseconds)
                .GetAwaiter().GetResult();
            Assert.IsTrue(compositeSave.Handled);
            Assert.AreEqual(2345, ((CompositeDelayItem)composite.CompositeItems[0]).DurationMilliseconds);
            Assert.AreEqual(saveCountBeforeCompositeSave + 1, store.SaveCount);

            var unrelatedTextBox = new TextBox();
            var nativeTextSave = CreatePreviewKeyEvent(window, Key.S);
            var saveCountBeforeNativeTextSave = store.SaveCount;
            window.HandleWindowPreviewKeyDownAsync(nativeTextSave, ModifierKeys.Control, unrelatedTextBox)
                .GetAwaiter().GetResult();
            Assert.IsFalse(nativeTextSave.Handled);
            Assert.AreEqual(saveCountBeforeNativeTextSave, store.SaveCount);

            viewModel.SelectedScript = viewModel.Scripts.Single(script => script.Id == regular.Id);
            var persistedStepName = regular.Steps[0].Name;
            var persistedScriptName = regular.Name;
            var saveCount = store.SaveCount;
            viewModel.ScriptName = "Không được đổi tên";
            viewModel.EditorName = "Không được lưu bước";
            var elsewhere = CreatePreviewKeyEvent(window, Key.S);
            window.HandleWindowPreviewKeyDownAsync(elsewhere, ModifierKeys.Control, scriptsList)
                .GetAwaiter().GetResult();
            Assert.IsFalse(elsewhere.Handled);
            Assert.AreEqual(saveCount, store.SaveCount);
            Assert.AreEqual(persistedScriptName, regular.Name);
            Assert.AreEqual(persistedStepName, regular.Steps[0].Name);
        }
        finally
        {
            engine.Complete(1);
            PumpDispatcherUntil(() => !viewModel.IsExecuting, TimeSpan.FromSeconds(2));
            window.Close();
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }
    }

    [STATestMethod]
    public void ControlCenterWindow_ShowsWithSharedState_AndUsesFreshVisualTrees()
    {
        if (Application.Current is null)
        {
            var application = new MEmuScriptStudio.App.App();
            application.InitializeComponent();
        }

        var viewModel = CreateViewModel(new RecordingScriptStore(), new ImmediateEngine());
        var first = new ControlCenterWindow(viewModel);
        var second = new ControlCenterWindow(viewModel);
        try
        {
            first.Show();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.DataBind);

            var firstRunPanel = (RunControlPanel)first.FindName("RunPanel");
            var secondRunPanel = (RunControlPanel)second.FindName("RunPanel");

            Assert.IsTrue(first.IsVisible, "InitializeComponent and the first render must complete without terminating the application.");
            Assert.AreSame(viewModel, first.DataContext);
            Assert.AreSame(viewModel, firstRunPanel.DataContext);
            Assert.AreSame(viewModel, second.DataContext);
            Assert.AreNotSame(firstRunPanel, secondRunPanel, "Each window must own a fresh run-panel visual instance.");
            Assert.AreSame(first, Window.GetWindow(firstRunPanel));
            Assert.AreSame(second, Window.GetWindow(secondRunPanel));
            Assert.IsNull(first.FindName("LayoutPanel"));
            Assert.IsNull(second.FindName("LayoutPanel"));
        }
        finally
        {
            first.Close();
            second.Close();
        }
    }

    [STATestMethod]
    public void ControlCenterWindow_RestoresOnlyAfterLoadedAndRepeatedRealOpenCloseCompletes()
    {
        if (Application.Current is null)
        {
            var application = new MEmuScriptStudio.App.App();
            application.InitializeComponent();
        }

        var settingsStore = new RecordingRunSettingsStore(new ApplicationSettings
        {
            MemucPath = @"C:\MEmu\memuc.exe",
            ControlCenterLayout = new ControlCenterLayoutSettings
            {
                WindowWidth = 1040,
                WindowHeight = 620,
                SetupPanelRatio = 0.69,
                RecentListRatio = 0.43
            }
        });
        var viewModel = CreateViewModel(
            new RecordingScriptStore(),
            new ImmediateEngine(),
            settingsStore: settingsStore);
        viewModel.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();
        var endedAt = DateTimeOffset.UtcNow;
        var recent = new LatestRunResultViewModel(
            Guid.NewGuid(), "Stress", "Stress open/close", endedAt.AddSeconds(-1), endedAt,
            1, 1, 0, 0, 0,
            [new RecentRunInstanceSnapshotViewModel(1, "VM 1", "Script", "Step", InstanceExecutionStatus.Succeeded, "Hoàn tất.")]);
        viewModel.RecentRuns.Add(recent);
        viewModel.SelectedRecentRunResult = recent;

        for (var iteration = 0; iteration < 8; iteration++)
        {
            var closed = false;
            var window = new ControlCenterWindow(viewModel)
            {
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -32000,
                Top = -32000,
                ShowActivated = false,
                ShowInTaskbar = false
            };
            Assert.IsFalse(window.HasAppliedSavedLayout,
                "Saved splitter state must not be applied in the constructor before ActualWidth is usable.");
            window.Closed += (_, _) => closed = true;

            window.Show();
            PumpDispatcherUntil(() => window.HasAppliedSavedLayout, TimeSpan.FromSeconds(2));
            var recentPanel = (RecentRunsPanel)window.FindName("RecentRunsPanel");
            var recentListRow = (RowDefinition)recentPanel.FindName("RecentListRowDefinition");
            var recentDetailRow = (RowDefinition)recentPanel.FindName("RecentDetailRowDefinition");
            Assert.AreEqual(GridUnitType.Star, recentListRow.Height.GridUnitType);
            Assert.AreEqual(GridUnitType.Star, recentDetailRow.Height.GridUnitType);
            Assert.AreEqual(0.43, recentListRow.Height.Value, 0.001,
                "The inactive Recent tab must still receive its saved Star ratio after Control Center Loaded.");
            FindLogicalDescendants<TabControl>(window).Single().SelectedIndex = 1;
            DrainDataBindings();
            var runPanel = (RunControlPanel)window.FindName("RunPanel");
            var columns = (Grid)runPanel.FindName("RunControlColumns");
            Assert.IsTrue(columns.ActualWidth > 0);

            window.Close();
            PumpDispatcherUntil(() => closed, TimeSpan.FromSeconds(2));
            Assert.IsFalse(window.IsLoaded);
        }
        Assert.IsNotNull(settingsStore.LastSaved?.ControlCenterLayout.SetupPanelRatio);
        Assert.IsNotNull(settingsStore.LastSaved?.ControlCenterLayout.RecentListRatio);
        Assert.AreEqual(0.43, settingsStore.LastSaved!.ControlCenterLayout.RecentListRatio!.Value, 0.03);
        Assert.IsNull(settingsStore.LastSaved?.ControlCenterLayout.SetupPanelWidth);
    }

    [STATestMethod]
    public void ControlCenterWindow_RestoresExtremeRatioAfterResizedLayoutPass()
    {
        if (Application.Current is null)
        {
            var application = new MEmuScriptStudio.App.App();
            application.InitializeComponent();
        }

        var settingsStore = new RecordingRunSettingsStore(new ApplicationSettings
        {
            MemucPath = @"C:\MEmu\memuc.exe",
            ControlCenterLayout = new ControlCenterLayoutSettings
            {
                WindowWidth = 1600,
                WindowHeight = 700,
                SetupPanelRatio = 0.78,
                RecentListRatio = 0.43
            }
        });
        var viewModel = CreateViewModel(
            new RecordingScriptStore(),
            new ImmediateEngine(),
            settingsStore: settingsStore);
        viewModel.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();
        var closed = false;
        var window = new ControlCenterWindow(viewModel)
        {
            Width = 800,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -32000,
            Top = -32000,
            ShowActivated = false,
            ShowInTaskbar = false
        };
        window.Closed += (_, _) => closed = true;

        try
        {
            window.Show();
            PumpDispatcherUntil(() => window.HasAppliedSavedLayout, TimeSpan.FromSeconds(2));
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);

            var runPanel = (RunControlPanel)window.FindName("RunPanel");
            var setupColumn = (ColumnDefinition)runPanel.FindName("RunSetupColumnDefinition");
            var runtimeColumn = (ColumnDefinition)runPanel.FindName("RunRuntimeColumnDefinition");
            Assert.AreEqual(GridUnitType.Star, setupColumn.Width.GridUnitType);
            Assert.AreEqual(GridUnitType.Star, runtimeColumn.Width.GridUnitType);
            Assert.AreEqual(0.78, setupColumn.Width.Value, 0.001,
                "The saved ratio must be clamped against the restored wide layout, not the initial narrow ActualWidth.");
            Assert.IsTrue(setupColumn.ActualWidth >= setupColumn.MinWidth);
            Assert.IsTrue(runtimeColumn.ActualWidth >= runtimeColumn.MinWidth);
        }
        finally
        {
            window.Close();
            PumpDispatcherUntil(() => closed, TimeSpan.FromSeconds(2));
        }

        Assert.AreEqual(0.78, settingsStore.LastSaved!.ControlCenterLayout.SetupPanelRatio!.Value, 0.01,
            "Closing after restore must preserve the extreme-but-feasible ratio.");
    }

    [STATestMethod]
    public void ControlCenterWindow_SaveFailureAndTimeoutNeverBlockClosed()
    {
        if (Application.Current is null)
        {
            var application = new MEmuScriptStudio.App.App();
            application.InitializeComponent();
        }

        ISettingsStore[] stores = [new ThrowingUpdateSettingsStore(), new NeverCompletingSettingsStore()];
        foreach (var store in stores)
        {
            var viewModel = CreateViewModel(
                new RecordingScriptStore(),
                new ImmediateEngine(),
                settingsStore: store);
            var closed = false;
            var window = new ControlCenterWindow(viewModel)
            {
                LayoutPersistenceTimeout = TimeSpan.FromMilliseconds(35),
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -32000,
                Top = -32000,
                ShowActivated = false,
                ShowInTaskbar = false
            };
            window.Closed += (_, _) => closed = true;
            window.Show();
            PumpDispatcherUntil(() => window.HasAppliedSavedLayout, TimeSpan.FromSeconds(2));

            window.Close();

            PumpDispatcherUntil(() => closed, TimeSpan.FromSeconds(1));
            Assert.IsFalse(window.IsLoaded);
        }
    }

    [STATestMethod]
    public void ControlCenterWindow_CloseFromLoadedBeforeDeferredRestoreDoesNotOverwriteSavedLayout()
    {
        if (Application.Current is null)
        {
            var application = new MEmuScriptStudio.App.App();
            application.InitializeComponent();
        }

        var settingsStore = new RecordingRunSettingsStore(new ApplicationSettings
        {
            MemucPath = @"C:\MEmu\memuc.exe",
            ControlCenterLayout = new ControlCenterLayoutSettings
            {
                WindowWidth = 1010,
                WindowHeight = 610,
                SetupPanelRatio = 0.68
            }
        });
        var viewModel = CreateViewModel(
            new RecordingScriptStore(),
            new ImmediateEngine(),
            settingsStore: settingsStore);
        viewModel.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();
        var closed = false;
        var layoutWasAppliedInsideLoaded = true;
        var window = new ControlCenterWindow(viewModel)
        {
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -32000,
            Top = -32000,
            ShowActivated = false,
            ShowInTaskbar = false
        };
        window.Loaded += (_, _) =>
        {
            layoutWasAppliedInsideLoaded = window.HasAppliedSavedLayout;
            window.Close();
        };
        window.Closed += (_, _) => closed = true;

        window.Show();
        PumpDispatcherUntil(() => closed, TimeSpan.FromSeconds(2));

        Assert.IsFalse(layoutWasAppliedInsideLoaded);
        Assert.AreEqual(0, settingsStore.SaveCount,
            "Closing before deferred restore must preserve the existing ratio instead of saving XAML defaults.");
        Assert.AreEqual(0.68, viewModel.ControlCenterLayout.SetupPanelRatio);
    }

    [STATestMethod]
    public void RunControlPanel_NativeKeyboardResizeKeepsMinimaAndStarRatioAcrossReopen()
    {
        if (Application.Current is null)
        {
            var application = new MEmuScriptStudio.App.App();
            application.InitializeComponent();
        }

        using var firstHost = new System.Windows.Interop.HwndSource(new System.Windows.Interop.HwndSourceParameters("SplitterClampTest")
        {
            Width = 1000,
            Height = 620,
            PositionX = -32000,
            PositionY = -32000,
            WindowStyle = unchecked((int)0x80000000)
        });
        var first = new RunControlPanel();
        firstHost.RootVisual = first;
        first.Measure(new Size(1000, 620));
        first.Arrange(new Rect(0, 0, 1000, 620));
        first.UpdateLayout();
        first.ApplyLayout(new ControlCenterLayoutSettings { SetupPanelRatio = 0.60 });
        first.UpdateLayout();

        var firstGrid = (Grid)first.FindName("RunControlColumns");
        var splitter = (GridSplitter)first.FindName("RunSetupRuntimeSplitter");
        Assert.AreEqual(GridResizeDirection.Columns, splitter.ResizeDirection);
        Assert.AreEqual(GridResizeBehavior.PreviousAndNext, splitter.ResizeBehavior);
        Assert.IsFalse(splitter.ShowsPreview);
        Assert.AreEqual(Cursors.SizeWE, splitter.Cursor);
        var setupBeforeKeyboardResize = firstGrid.ColumnDefinitions[0].ActualWidth;
        for (var index = 0; index < 200; index++)
        {
            splitter.RaiseEvent(new KeyEventArgs(
                Keyboard.PrimaryDevice,
                new TestPresentationSource { RootVisual = first },
                Environment.TickCount,
                Key.Right)
            {
                RoutedEvent = Keyboard.KeyDownEvent
            });
        }
        first.UpdateLayout();

        Assert.IsTrue(firstGrid.ColumnDefinitions[0].ActualWidth > setupBeforeKeyboardResize,
            "Native GridSplitter keyboard handling must resize the adjacent columns.");
        Assert.IsTrue(firstGrid.ColumnDefinitions[0].ActualWidth >= ControlCenterLayoutSettings.MinimumSetupPanelWidth);
        Assert.IsTrue(firstGrid.ColumnDefinitions[2].ActualWidth >= ControlCenterLayoutSettings.MinimumRuntimePanelWidth);
        Assert.AreEqual(Visibility.Visible, splitter.Visibility);
        Assert.IsTrue(splitter.ActualWidth > 0);
        Assert.AreEqual(GridUnitType.Star, firstGrid.ColumnDefinitions[0].Width.GridUnitType);
        Assert.AreEqual(GridUnitType.Star, firstGrid.ColumnDefinitions[2].Width.GridUnitType);
        var captured = first.CaptureLayout(new ControlCenterLayoutSettings());
        Assert.IsNotNull(captured.SetupPanelRatio);
        Assert.AreNotEqual(ControlCenterLayoutSettings.DefaultSetupPanelRatio, captured.SetupPanelRatio!.Value, 0.01,
            "An extreme but valid split must not be replaced by the default before persistence.");

        using var secondHost = new System.Windows.Interop.HwndSource(new System.Windows.Interop.HwndSourceParameters("SplitterRestoreTest")
        {
            Width = 1320,
            Height = 620,
            PositionX = -32000,
            PositionY = -32000,
            WindowStyle = unchecked((int)0x80000000)
        });
        var second = new RunControlPanel();
        secondHost.RootVisual = second;
        second.Measure(new Size(1320, 620));
        second.Arrange(new Rect(0, 0, 1320, 620));
        second.UpdateLayout();
        second.ApplyLayout(captured);
        second.UpdateLayout();
        var reopened = second.CaptureLayout(new ControlCenterLayoutSettings());

        Assert.AreEqual(captured.SetupPanelRatio!.Value, reopened.SetupPanelRatio!.Value, 0.01);
        var secondGrid = (Grid)second.FindName("RunControlColumns");
        Assert.AreEqual(GridUnitType.Star, secondGrid.ColumnDefinitions[0].Width.GridUnitType);
        Assert.AreEqual(GridUnitType.Star, secondGrid.ColumnDefinitions[2].Width.GridUnitType);
        Assert.IsTrue(secondGrid.ColumnDefinitions[0].ActualWidth >= ControlCenterLayoutSettings.MinimumSetupPanelWidth);
        Assert.IsTrue(secondGrid.ColumnDefinitions[2].ActualWidth >= ControlCenterLayoutSettings.MinimumRuntimePanelWidth);
    }

    [STATestMethod]
    public void RecentRunsPanel_NativeKeyboardResizeCapturesAndRestoresStarRowRatio()
    {
        if (Application.Current is null)
        {
            var application = new MEmuScriptStudio.App.App();
            application.InitializeComponent();
        }

        var viewModel = CreateViewModel(new RecordingScriptStore(), new ImmediateEngine());
        var endedAt = DateTimeOffset.UtcNow;
        var result = new LatestRunResultViewModel(
            Guid.NewGuid(), "Recent", "Native row splitter", endedAt.AddSeconds(-1), endedAt,
            1, 1, 0, 0, 0,
            [new RecentRunInstanceSnapshotViewModel(1, "VM", "Script", "Step", InstanceExecutionStatus.Succeeded, "Done")]);
        viewModel.RecentRuns.Add(result);
        viewModel.SelectedRecentRunResult = result;

        using var firstHost = new System.Windows.Interop.HwndSource(new System.Windows.Interop.HwndSourceParameters("RecentSplitterTest")
        {
            Width = 960,
            Height = 650,
            PositionX = -32000,
            PositionY = -32000,
            WindowStyle = unchecked((int)0x80000000)
        });
        var first = new RecentRunsPanel { DataContext = viewModel };
        firstHost.RootVisual = first;
        first.Measure(new Size(960, 650));
        first.Arrange(new Rect(0, 0, 960, 650));
        first.UpdateLayout();
        first.ApplyLayout(new ControlCenterLayoutSettings { RecentListRatio = 0.40 });
        first.UpdateLayout();

        var splitter = (GridSplitter)first.FindName("RecentListDetailSplitter");
        var listRow = (RowDefinition)first.FindName("RecentListRowDefinition");
        var detailRow = (RowDefinition)first.FindName("RecentDetailRowDefinition");
        Assert.AreEqual(GridResizeDirection.Rows, splitter.ResizeDirection);
        Assert.AreEqual(GridResizeBehavior.PreviousAndNext, splitter.ResizeBehavior);
        Assert.IsFalse(splitter.ShowsPreview);
        Assert.AreEqual(Cursors.SizeNS, splitter.Cursor);
        var listHeightBefore = listRow.ActualHeight;
        for (var index = 0; index < 100; index++)
        {
            splitter.RaiseEvent(new KeyEventArgs(
                Keyboard.PrimaryDevice,
                new TestPresentationSource { RootVisual = first },
                Environment.TickCount,
                Key.Down)
            {
                RoutedEvent = Keyboard.KeyDownEvent
            });
        }
        first.UpdateLayout();

        Assert.IsTrue(listRow.ActualHeight > listHeightBefore);
        Assert.IsTrue(listRow.ActualHeight >= ControlCenterLayoutSettings.MinimumRecentListHeight);
        Assert.IsTrue(detailRow.ActualHeight >= ControlCenterLayoutSettings.MinimumRecentDetailHeight);
        Assert.AreEqual(GridUnitType.Star, listRow.Height.GridUnitType);
        Assert.AreEqual(GridUnitType.Star, detailRow.Height.GridUnitType);
        var captured = first.CaptureLayout(new ControlCenterLayoutSettings { SetupPanelRatio = 0.61 });

        using var secondHost = new System.Windows.Interop.HwndSource(new System.Windows.Interop.HwndSourceParameters("RecentSplitterRestoreTest")
        {
            Width = 960,
            Height = 760,
            PositionX = -32000,
            PositionY = -32000,
            WindowStyle = unchecked((int)0x80000000)
        });
        var second = new RecentRunsPanel { DataContext = viewModel };
        secondHost.RootVisual = second;
        second.Measure(new Size(960, 760));
        second.Arrange(new Rect(0, 0, 960, 760));
        second.UpdateLayout();
        second.ApplyLayout(captured);
        second.UpdateLayout();
        var reopened = second.CaptureLayout(new ControlCenterLayoutSettings());
        var reopenedListRow = (RowDefinition)second.FindName("RecentListRowDefinition");
        var reopenedDetailRow = (RowDefinition)second.FindName("RecentDetailRowDefinition");

        Assert.AreEqual(captured.RecentListRatio!.Value, reopened.RecentListRatio!.Value, 0.01);
        Assert.AreEqual(GridUnitType.Star, reopenedListRow.Height.GridUnitType);
        Assert.AreEqual(GridUnitType.Star, reopenedDetailRow.Height.GridUnitType);
    }

    [STATestMethod]
    public void ControlCenter_UsesSeparateActiveAndRecentTabsWithLiveSafeSplitter()
    {
        if (Application.Current is null)
        {
            var application = new MEmuScriptStudio.App.App();
            application.InitializeComponent();
        }

        var viewModel = CreateViewModel(new RecordingScriptStore(), new ImmediateEngine());
        var window = new ControlCenterWindow(viewModel);
        var runPanel = (RunControlPanel)window.FindName("RunPanel");
        var recentPanel = (RecentRunsPanel)window.FindName("RecentRunsPanel");
        Assert.IsTrue(BackgroundFocusBehavior.GetIsEnabled(window));
        var tabHeaders = FindLogicalDescendants<TabItem>(window).Select(item => item.Header?.ToString()).ToList();
        CollectionAssert.AreEqual(new[] { "Đang hoạt động", "Kết quả gần đây" }, tabHeaders);
        CollectionAssert.DoesNotContain(tabHeaders, "Lịch sử");
        CollectionAssert.DoesNotContain(tabHeaders, "Trang và thứ tự");
        Assert.IsNull(window.FindName("LayoutPanel"));
        Assert.IsNull(window.FindName("HistoryPanel"));
        Assert.IsNull(typeof(MainViewModel).Assembly.GetType("MEmuScriptStudio.App.Views.WindowLayoutPanel"));
        Assert.IsNull(typeof(MainViewModel).Assembly.GetType("MEmuScriptStudio.App.Views.ExecutionHistoryPanel"));
        Assert.IsNull(runPanel.FindName("RecentRunsCard"));
        Assert.IsNull(runPanel.FindName("RecentActiveSplitter"));
        var recentRunsGrid = (DataGrid)recentPanel.FindName("RecentRunsGrid");
        Assert.AreEqual(nameof(MainViewModel.RecentRuns),
            BindingOperations.GetBinding(recentRunsGrid, ItemsControl.ItemsSourceProperty)!.Path.Path);
        var issueGrid = (DataGrid)recentPanel.FindName("RecentRunInstancesGrid");
        Assert.AreEqual("Instances",
            BindingOperations.GetBinding(issueGrid, ItemsControl.ItemsSourceProperty)!.Path.Path);
        Assert.AreEqual("HasInstances",
            BindingOperations.GetBinding(issueGrid, UIElement.VisibilityProperty)!.Path.Path);
        var noIssuesText = (TextBlock)recentPanel.FindName("RecentRunNoInstancesText");
        Assert.AreEqual("HasNoInstances",
            BindingOperations.GetBinding(noIssuesText, UIElement.VisibilityProperty)!.Path.Path);
        Assert.IsTrue(VirtualizingPanel.GetIsVirtualizing(issueGrid));
        Assert.AreEqual(VirtualizationMode.Recycling, VirtualizingPanel.GetVirtualizationMode(issueGrid));
        var issueMessageColumn = issueGrid.Columns.OfType<DataGridTemplateColumn>()
            .Single(column => Equals(column.Header, "Thông báo"));
        var issueMessage = (TextBlock)issueMessageColumn.CellTemplate.LoadContent();
        Assert.AreEqual("ShortMessage", BindingOperations.GetBinding(issueMessage, TextBlock.TextProperty)!.Path.Path);
        Assert.AreEqual("ShortMessage", BindingOperations.GetBinding(issueMessage, FrameworkElement.ToolTipProperty)!.Path.Path);

        var runTargets = (DataGrid)runPanel.FindName("RunTargetsGrid");
        Assert.IsTrue(VirtualizingPanel.GetIsVirtualizing(runTargets));
        Assert.AreEqual(VirtualizationMode.Recycling, VirtualizingPanel.GetVirtualizationMode(runTargets));
        Assert.IsTrue(ScrollViewer.GetCanContentScroll(runTargets));
        Assert.IsTrue(runTargets.EnableRowVirtualization);
        Assert.IsTrue(runTargets.EnableColumnVirtualization);
        Assert.AreEqual(nameof(MainViewModel.FilteredRunTargets),
            BindingOperations.GetBinding(runTargets, ItemsControl.ItemsSourceProperty)!.Path.Path);
        Assert.AreEqual(ScrollBarVisibility.Auto, ScrollViewer.GetHorizontalScrollBarVisibility(runTargets));

        var horizontalSplitter = (GridSplitter)runPanel.FindName("RunSetupRuntimeSplitter");
        Assert.AreEqual(GridResizeDirection.Columns, horizontalSplitter.ResizeDirection);
        Assert.AreEqual(Cursors.SizeWE, horizontalSplitter.Cursor);
        Assert.IsFalse(horizontalSplitter.ShowsPreview);
        var columns = (Grid)runPanel.FindName("RunControlColumns");
        Assert.AreEqual(ControlCenterLayoutSettings.MinimumSetupPanelWidth, columns.ColumnDefinitions[0].MinWidth);
        Assert.AreEqual(ControlCenterLayoutSettings.MinimumRuntimePanelWidth, columns.ColumnDefinitions[2].MinWidth);
        Assert.AreEqual(GridUnitType.Star, columns.ColumnDefinitions[0].Width.GridUnitType);
        Assert.AreEqual(GridUnitType.Star, columns.ColumnDefinitions[2].Width.GridUnitType);
        var recentSplitter = (GridSplitter)recentPanel.FindName("RecentListDetailSplitter");
        Assert.AreEqual(GridResizeDirection.Rows, recentSplitter.ResizeDirection);
        Assert.AreEqual(GridResizeBehavior.PreviousAndNext, recentSplitter.ResizeBehavior);
        Assert.IsFalse(recentSplitter.ShowsPreview);
        Assert.AreEqual(Cursors.SizeNS, recentSplitter.Cursor);
        Assert.AreEqual(
            ControlCenterLayoutSettings.MinimumRecentListHeight,
            ((RowDefinition)recentPanel.FindName("RecentListRowDefinition")).MinHeight);
        Assert.AreEqual(
            ControlCenterLayoutSettings.MinimumRecentDetailHeight,
            ((RowDefinition)recentPanel.FindName("RecentDetailRowDefinition")).MinHeight);
        var statusMessage = (TextBlock)runPanel.FindName("ControlCenterStatusMessage");
        Assert.AreEqual(nameof(MainViewModel.StatusMessage),
            BindingOperations.GetBinding(statusMessage, TextBlock.TextProperty)!.Path.Path);

        var assignmentColumn = runTargets.Columns.OfType<DataGridTemplateColumn>()
            .Single(column => Equals(column.Header, "Kịch bản được gán"));
        var displayContent = (DependencyObject)assignmentColumn.CellTemplate.LoadContent();
        var editingContent = (DependencyObject)assignmentColumn.CellEditingTemplate.LoadContent();
        Assert.IsInstanceOfType<TextBlock>(displayContent,
            "Assignment cells must render lightweight text until the user starts editing.");
        Assert.IsInstanceOfType<ComboBox>(editingContent,
            "The script ComboBox must be created only by the editing template.");
        window.Close();
    }

    [STATestMethod]
    public void RecentRunsPanel_DataGridSelectionUpdatesViewModelAndShowsEveryTargetSnapshot()
    {
        if (Application.Current is null)
        {
            var application = new MEmuScriptStudio.App.App();
            application.InitializeComponent();
        }

        var viewModel = CreateViewModel(new RecordingScriptStore(), new ImmediateEngine());
        var panel = new RecentRunsPanel { DataContext = viewModel };
        using var host = new System.Windows.Interop.HwndSource(new System.Windows.Interop.HwndSourceParameters("RecentRunsVisibilityTest")
        {
            Width = 900,
            Height = 600,
            PositionX = -32000,
            PositionY = -32000,
            WindowStyle = unchecked((int)0x80000000)
        });
        host.RootVisual = panel;
        panel.Measure(new Size(900, 600));
        panel.Arrange(new Rect(0, 0, 900, 600));
        panel.UpdateLayout();
        var emptyState = (TextBlock)panel.FindName("RecentRunsEmptyState");
        var recentGrid = (DataGrid)panel.FindName("RecentRunsGrid");
        var recentCard = (Border)panel.FindName("RecentRunsCard");
        var detail = (Border)panel.FindName("RecentRunDetailPanel");
        var endedAt = new DateTimeOffset(new DateTime(2026, 8, 7, 19, 42, 0, DateTimeKind.Local));
        var result = new LatestRunResultViewModel(
            Guid.NewGuid(), "Nhóm 01", "Một kịch bản cho tất cả · Script A",
            endedAt.AddMinutes(-2), endedAt, 4, 1, 1, 1, 1,
            [
                new RecentRunInstanceSnapshotViewModel(1, "VM 1", "Script A", "Bước 1", InstanceExecutionStatus.Succeeded, "Hoàn tất."),
                new RecentRunInstanceSnapshotViewModel(2, "VM 2", "Script A", "Bước 2", InstanceExecutionStatus.Failed, "Lỗi mẫu"),
                new RecentRunInstanceSnapshotViewModel(3, "VM 3", "Script B", "Bước 3", InstanceExecutionStatus.Unavailable, "Không khả dụng"),
                new RecentRunInstanceSnapshotViewModel(4, "VM 4", "Script B", "Bước 4", InstanceExecutionStatus.Cancelled, "Đã hủy")
            ]);

        Assert.AreEqual(Visibility.Visible, emptyState.Visibility);
        Assert.AreEqual(Visibility.Collapsed, recentGrid.Visibility);
        Assert.AreEqual(Visibility.Collapsed, detail.Visibility);

        viewModel.RecentRuns.Add(result);
        DrainDataBindings();
        Assert.IsNull(viewModel.SelectedRecentRunResult);
        Assert.AreEqual(Visibility.Collapsed, detail.Visibility,
            "The detail row must not reserve an empty visible card before a row is selected.");
        Assert.AreEqual(3, Grid.GetRowSpan(recentCard),
            "Without a selection the recent list must consume the detail area instead of leaving blank space.");

        recentGrid.SelectedItem = result;
        DrainDataBindings();

        Assert.AreEqual(Visibility.Collapsed, emptyState.Visibility);
        Assert.AreEqual(Visibility.Visible, recentGrid.Visibility);
        Assert.AreEqual(Visibility.Visible, detail.Visibility);
        Assert.AreEqual(1, Grid.GetRowSpan(recentCard));
        Assert.AreSame(result, viewModel.SelectedRecentRunResult,
            "Selection must travel through DataGrid.SelectedItem, not a direct ViewModel assignment.");
        Assert.AreSame(result, ((Grid)detail.Child).DataContext);
        var instancesGrid = (DataGrid)panel.FindName("RecentRunInstancesGrid");
        PumpDispatcherUntil(() => instancesGrid.Items.Count == 4, TimeSpan.FromSeconds(2));
        Assert.AreEqual(4, instancesGrid.Items.Count);
        CollectionAssert.AreEquivalent(
            new[]
            {
                InstanceExecutionStatus.Succeeded,
                InstanceExecutionStatus.Failed,
                InstanceExecutionStatus.Unavailable,
                InstanceExecutionStatus.Cancelled
            },
            instancesGrid.Items.Cast<RecentRunInstanceSnapshotViewModel>().Select(item => item.Status).ToArray());
        Assert.AreEqual("07/08/2026 19:42", result.EndedAtText);
        Assert.IsFalse(result.EndedAtText.Contains("AM", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(result.EndedAtText.Contains("PM", StringComparison.OrdinalIgnoreCase));
        var descriptionColumn = recentGrid.Columns.OfType<DataGridTemplateColumn>()
            .Single(column => Equals(column.Header, "Kịch bản / lần chạy"));
        var descriptionText = (TextBlock)descriptionColumn.CellTemplate.LoadContent();
        Assert.AreEqual(nameof(LatestRunResultViewModel.RunDescription),
            BindingOperations.GetBinding(descriptionText, TextBlock.TextProperty)!.Path.Path);
        foreach (var column in recentGrid.Columns.OfType<DataGridTextColumn>())
            Assert.AreEqual(BindingMode.OneWay, ((Binding)column.Binding).Mode);
        foreach (var run in FindLogicalDescendants<Run>(detail)
                     .Where(run => BindingOperations.GetBinding(run, Run.TextProperty) is not null))
            Assert.AreEqual(BindingMode.OneWay, BindingOperations.GetBinding(run, Run.TextProperty)!.Mode);

        recentGrid.SelectedItem = null;
        viewModel.RecentRuns.Clear();
        DrainDataBindings();
        Assert.AreEqual(Visibility.Visible, emptyState.Visibility);
        Assert.AreEqual(Visibility.Collapsed, recentGrid.Visibility);
        Assert.AreEqual(Visibility.Collapsed, detail.Visibility);
    }

    [STATestMethod]
    public void RunTargetProjection_SearchesFiltersSortsAndBulkAssignsAcrossLargeCollections()
    {
        var first = new ScriptDefinition { Name = "Script A", Steps = { new NoteStep { Name = "A" } } };
        var second = new ScriptDefinition { Name = "Script B", Steps = { new NoteStep { Name = "B" } } };
        var instances = Enumerable.Range(0, 75)
            .Reverse()
            .Select(index => new MemuInstance(index, $"VM {74 - index:D3}", index % 2 == 0, 100 + index))
            .ToArray();
        var viewModel = CreateViewModel(
            new RecordingScriptStore([first, second]),
            new ImmediateEngine(),
            instanceService: new FixedInstanceService(instances));

        viewModel.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();
        viewModel.RefreshCommand.ExecuteAsync().GetAwaiter().GetResult();

        Assert.AreEqual(75, viewModel.RunTargets.Count, "The target collection must not impose a product limit.");
        Assert.AreEqual(75, viewModel.FilteredRunTargetCount);
        CollectionAssert.AreEqual(
            Enumerable.Range(0, 75).ToArray(),
            viewModel.FilteredRunTargets.Cast<InstanceTargetItemViewModel>().Select(item => item.Index).ToArray());

        viewModel.SelectedRunTargetSortMode = RunTargetSortMode.Name;
        CollectionAssert.AreEqual(
            Enumerable.Range(0, 75).Reverse().ToArray(),
            viewModel.FilteredRunTargets.Cast<InstanceTargetItemViewModel>().Select(item => item.Index).ToArray());

        viewModel.RunTargetSearchText = "vm 010";
        viewModel.SelectedRunTargetAvailabilityFilter = RunTargetAvailabilityFilter.Running;
        CollectionAssert.AreEqual(
            new[] { 64 },
            viewModel.FilteredRunTargets.Cast<InstanceTargetItemViewModel>().Select(item => item.Index).ToArray());

        viewModel.RunTargetSearchText = "VM 011";
        Assert.AreEqual(0, viewModel.FilteredRunTargetCount);
        viewModel.SelectedRunTargetAvailabilityFilter = RunTargetAvailabilityFilter.Stopped;
        CollectionAssert.AreEqual(
            new[] { 63 },
            viewModel.FilteredRunTargets.Cast<InstanceTargetItemViewModel>().Select(item => item.Index).ToArray());

        viewModel.RunTargetSearchText = string.Empty;
        viewModel.SelectedRunTargetAvailabilityFilter = RunTargetAvailabilityFilter.Running;
        viewModel.SelectAllFilteredRunTargetsCommand.Execute(null);
        Assert.AreEqual(38, viewModel.SelectedRunTargetCount);
        Assert.IsTrue(viewModel.RunTargets.Where(item => item.IsRunning).All(item => item.IsSelected));
        Assert.IsTrue(viewModel.RunTargets.Where(item => !item.IsRunning).All(item => !item.IsSelected));
        Assert.AreEqual("Đã chọn 38 / Tổng 75", viewModel.RunTargetSelectionSummary);

        viewModel.ScriptAssignmentMode = ScriptAssignmentMode.PerInstance;
        viewModel.ControlCenterSelectedScript = viewModel.Scripts.Single(item => item.Id == second.Id);
        viewModel.AssignScriptToSelectedCommand.ExecuteAsync().GetAwaiter().GetResult();

        Assert.IsTrue(viewModel.RunTargets.Where(item => item.IsRunning).All(item => item.AssignedScriptId == second.Id));
        Assert.IsTrue(viewModel.RunTargets.Where(item => !item.IsRunning).All(item => item.AssignedScriptId is null));
        Assert.AreEqual(38, viewModel.SelectedRunTargetCount);

        viewModel.SelectedScript = viewModel.Scripts.Single(item => item.Id == first.Id);
        Assert.AreEqual(second.Id, viewModel.ControlCenterSelectedScript!.Id,
            "MainWindow script selection must not change the Control Center script selection.");
        Assert.IsTrue(viewModel.AssignCurrentScriptToAllCommand.CanExecute(null),
            "Assign-all must remain independent from run selection.");
        viewModel.AssignCurrentScriptToAllCommand.ExecuteAsync().GetAwaiter().GetResult();
        Assert.IsTrue(viewModel.RunTargets.All(item => item.AssignedScriptId == second.Id));
    }

    [TestMethod]
    public async Task SelectAllFiltered_IsDisabledForStoppedOnlyAndSelectsOnlyRunnableFromMixedTargets()
    {
        var instances = new MutableInstanceService(
        [
            new MemuInstance(1, "Stopped 1", false, 0),
            new MemuInstance(2, "Stopped 2", false, 0)
        ]);
        var viewModel = CreateViewModel(
            new RecordingScriptStore(),
            new ImmediateEngine(),
            instanceService: instances);
        await viewModel.InitializeAsync(CancellationToken.None);
        await viewModel.RefreshCommand.ExecuteAsync();

        Assert.AreEqual(2, viewModel.FilteredRunTargetCount);
        Assert.IsFalse(viewModel.SelectAllFilteredRunTargetsCommand.CanExecute(null));

        instances.Instances =
        [
            new MemuInstance(1, "Running", true, 101),
            new MemuInstance(2, "Stopped", false, 0)
        ];
        await viewModel.RefreshCommand.ExecuteAsync();

        Assert.IsTrue(viewModel.SelectAllFilteredRunTargetsCommand.CanExecute(null));
        viewModel.SelectAllFilteredRunTargetsCommand.Execute(null);
        CollectionAssert.AreEqual(
            new[] { 1 },
            viewModel.RunTargets.Where(item => item.IsSelected).Select(item => item.Index).ToArray());
    }

    [TestMethod]
    public void ActiveEmptyState_DistinguishesNoActiveInstancesFromFilterNoMatch()
    {
        var viewModel = CreateViewModel(new RecordingScriptStore(), new ImmediateEngine());
        Assert.IsTrue(viewModel.HasNoActiveInstances);
        Assert.IsFalse(viewModel.HasActiveInstancesButNoFilteredMatches);

        var script = new ScriptDefinition { Name = "Active", Steps = [new NoteStep { Name = "Step" }] };
        viewModel.ActiveInstanceRuns.Add(new InstanceRunItemViewModel(
            Guid.NewGuid(),
            new MemuInstance(7, "VM 7", true, 107),
            script,
            (_, _) => true));

        Assert.IsTrue(viewModel.HasActiveInstances);
        Assert.IsTrue(viewModel.HasFilteredActiveInstances);
        Assert.IsFalse(viewModel.HasActiveInstancesButNoFilteredMatches);

        viewModel.ActiveInstanceSearchText = "does-not-match";

        Assert.IsTrue(viewModel.HasActiveInstances);
        Assert.IsTrue(viewModel.HasNoFilteredActiveInstances);
        Assert.IsTrue(viewModel.HasActiveInstancesButNoFilteredMatches);
    }

    [TestMethod]
    public async Task RunTargetSelection_OneOfSixUpdatesImmediatelyAndFilteringClearsHiddenSelection()
    {
        var instances = Enumerable.Range(0, 6)
            .Select(index => new MemuInstance(index, $"VM {index}", true, 100 + index))
            .ToArray();
        var viewModel = CreateViewModel(
            new RecordingScriptStore(),
            new ImmediateEngine(),
            instanceService: new FixedInstanceService(instances));
        await viewModel.InitializeAsync(CancellationToken.None);
        await viewModel.RefreshCommand.ExecuteAsync();
        var projection = viewModel.FilteredRunTargets;
        var collectionChanges = 0;
        projection.CollectionChanged += (_, _) => collectionChanges++;

        viewModel.RunTargets[2].IsSelected = true;

        Assert.AreEqual(1, viewModel.SelectedRunTargetCount);
        Assert.AreEqual("Đã chọn 1 / Tổng 6", viewModel.RunTargetSelectionSummary);
        Assert.AreSame(projection, viewModel.FilteredRunTargets);
        Assert.AreEqual(0, collectionChanges, "A checkbox change must not rebuild or refresh the target projection.");

        viewModel.RunTargets[2].IsSelected = false;
        Assert.AreEqual(0, viewModel.SelectedRunTargetCount);
        viewModel.RunTargetSearchText = "VM 1";
        viewModel.SelectAllFilteredRunTargetsCommand.Execute(null);
        Assert.IsTrue(viewModel.RunTargets.Single(item => item.Index == 1).IsSelected);
        Assert.AreEqual(1, viewModel.SelectedRunTargetCount);

        viewModel.RunTargetSearchText = "VM 3";
        Assert.AreEqual(0, viewModel.SelectedRunTargetCount,
            "A target hidden by a filter change must not remain selected for bulk assignment or running.");
        viewModel.SelectAllFilteredRunTargetsCommand.Execute(null);
        viewModel.ClearRunTargetSelectionCommand.Execute(null);
        Assert.IsTrue(viewModel.RunTargets.All(item => !item.IsSelected));
    }

    [STATestMethod]
    public void RunTargetRowAndCheckboxShareIsSelectedWithoutCellSelection()
    {
        if (Application.Current is null)
        {
            var application = new MEmuScriptStudio.App.App();
            application.InitializeComponent();
        }

        var panel = new RunControlPanel();
        var grid = (DataGrid)panel.FindName("RunTargetsGrid");
        var selectionColumn = grid.Columns.OfType<DataGridTemplateColumn>()
            .Single(column => Equals(column.Header, "Chọn"));
        var checkBox = (CheckBox)selectionColumn.CellTemplate.LoadContent();
        var target = new InstanceTargetItemViewModel(new MemuInstance(1, "VM", true, 101));

        RunControlPanel.ToggleRunTargetSelection(target);
        Assert.IsTrue(target.IsSelected);
        target.IsSelected = false;
        Assert.IsFalse(target.IsSelected);
        Assert.AreEqual(nameof(InstanceTargetItemViewModel.IsSelected),
            BindingOperations.GetBinding(checkBox, ToggleButton.IsCheckedProperty)!.Path.Path);
        Assert.AreEqual(BindingMode.TwoWay,
            BindingOperations.GetBinding(checkBox, ToggleButton.IsCheckedProperty)!.Mode);
        Assert.AreEqual(DataGridSelectionUnit.FullRow, grid.SelectionUnit);
        Assert.AreNotEqual(DataGridSelectionUnit.Cell, grid.SelectionUnit);
    }

    [STATestMethod]
    public void RunControlCollections_HandleFiveHundredTargetsAndManyFlatActiveRows()
    {
        if (Application.Current is null)
        {
            var application = new MEmuScriptStudio.App.App();
            application.InitializeComponent();
        }
        var targets = Enumerable.Range(0, 500)
            .Select(index => new MemuInstance(index, $"VM {index:D4}", true, 1000 + index))
            .ToArray();
        var viewModel = CreateViewModel(
            new RecordingScriptStore(), new ImmediateEngine(), instanceService: new FixedInstanceService(targets));
        viewModel.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();
        viewModel.RefreshCommand.ExecuteAsync().GetAwaiter().GetResult();
        var script = viewModel.Scripts.Single();
        for (var index = 0; index < 200; index++)
            viewModel.ActiveInstanceRuns.Add(new InstanceRunItemViewModel(
                Guid.NewGuid(), targets[index], script.Model, (_, _) => true));
        var panel = new RunControlPanel { DataContext = viewModel };
        var activeGrid = (DataGrid)panel.FindName("ActiveInstancesGrid");

        Assert.AreEqual(500, viewModel.RunTargets.Count);
        Assert.AreEqual(500, viewModel.FilteredRunTargetCount);
        Assert.AreEqual(200, viewModel.ActiveInstanceRuns.Count);
        Assert.AreEqual(nameof(MainViewModel.FilteredActiveInstanceRuns),
            BindingOperations.GetBinding(activeGrid, ItemsControl.ItemsSourceProperty)!.Path.Path);
        Assert.IsTrue(VirtualizingPanel.GetIsVirtualizing(activeGrid));
        Assert.AreEqual(VirtualizationMode.Recycling, VirtualizingPanel.GetVirtualizationMode(activeGrid));
        Assert.AreEqual(ScrollBarVisibility.Auto, ScrollViewer.GetVerticalScrollBarVisibility(activeGrid));
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

        viewModel.CopyStepsCommand.Execute(null);
        await viewModel.PasteStepsCommand.ExecuteAsync();

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

        viewModel.CopyStepsCommand.Execute(null);
        Assert.IsTrue(viewModel.PasteStepsCommand.CanExecute(null));
        viewModel.SelectedScript = viewModel.Scripts.Single(script => script.Name == "Target");
        Assert.IsTrue(viewModel.PasteStepsCommand.CanExecute(null));
        await viewModel.PasteStepsCommand.ExecuteAsync();
        var firstPasteIds = viewModel.Steps.Select(step => step.Id).ToArray();
        await viewModel.PasteStepsCommand.ExecuteAsync();

        CollectionAssert.AreEqual(new[] { "A", "C", "A", "C" }, viewModel.Steps.Select(step => step.Name).ToArray());
        Assert.IsTrue(firstPasteIds.All(id => !originalIds.Contains(id)));
        Assert.IsTrue(viewModel.Steps.Skip(2).All(step => !firstPasteIds.Contains(step.Id)));
        Assert.AreEqual(2, store.SaveCount);
        Assert.AreEqual("Đã dán 2 bước.", viewModel.StatusMessage);
    }

    [TestMethod]
    public async Task CrossScriptPasteIsOwnedAndUndoneOnlyByTheDestinationScript()
    {
        var source = CreateThreeStepScript();
        var target = new ScriptDefinition { Name = "Target" };
        var viewModel = CreateViewModel(new RecordingScriptStore([source, target]), new ImmediateEngine());
        await viewModel.InitializeAsync(CancellationToken.None);
        viewModel.SynchronizeSelectedSteps([viewModel.Steps[0], viewModel.Steps[1]]);

        viewModel.CopyStepsCommand.Execute(null);
        Assert.IsFalse(viewModel.IsEditorDirty);
        Assert.AreEqual(3, source.Steps.Count);
        viewModel.SelectedScript = viewModel.Scripts.Single(item => item.Id == target.Id);
        await viewModel.PasteStepsCommand.ExecuteAsync();
        Assert.AreEqual(2, target.Steps.Count);
        Assert.IsTrue(target.Steps.All(step => source.Steps.All(sourceStep => !ReferenceEquals(step, sourceStep))));

        await viewModel.UndoStepListCommand.ExecuteAsync();
        Assert.AreEqual(0, target.Steps.Count);
        Assert.AreEqual(3, source.Steps.Count);
        Assert.IsFalse(viewModel.IsEditorDirty);
        Assert.IsTrue(viewModel.HasCopiedSteps, "Clipboard cấp ứng dụng phải còn để có thể dán nhiều lần.");
    }

    [TestMethod]
    public async Task DeleteStepCommand_DeletesAllSelectedStepsWithOneConfirmationAndAutosave()
    {
        var store = new RecordingScriptStore([CreateThreeStepScript()]);
        var confirmation = new ConfigurableConfirmation(true);
        var viewModel = CreateViewModel(store, new ImmediateEngine(), confirmation);
        await viewModel.InitializeAsync(CancellationToken.None);
        viewModel.SynchronizeSelectedSteps([viewModel.Steps[0], viewModel.Steps[2]]);

        await viewModel.DeleteStepCommand.ExecuteAsync();

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
    public void StepGridShortcutPolicy_RoutesOutsideGridAndDoesNotCaptureTextInput()
    {
        Assert.AreEqual(StepGridShortcut.Copy, StepGridShortcutPolicy.Resolve(false, false, true, false, false, false, Key.C, ModifierKeys.Control));
        Assert.AreEqual(StepGridShortcut.Paste, StepGridShortcutPolicy.Resolve(false, false, false, true, false, false, Key.V, ModifierKeys.Control));
        Assert.AreEqual(StepGridShortcut.Undo, StepGridShortcutPolicy.Resolve(false, false, false, false, true, false, Key.Z, ModifierKeys.Control));
        Assert.AreEqual(StepGridShortcut.Delete, StepGridShortcutPolicy.Resolve(false, false, false, false, false, true, Key.Delete, ModifierKeys.None));
        Assert.AreEqual(StepGridShortcut.None, StepGridShortcutPolicy.Resolve(false, true, true, true, true, true, Key.C, ModifierKeys.Control));
        Assert.AreEqual(StepGridShortcut.None, StepGridShortcutPolicy.Resolve(true, true, true, true, true, true, Key.V, ModifierKeys.Control));
        Assert.AreEqual(StepGridShortcut.None, StepGridShortcutPolicy.Resolve(true, true, true, true, true, true, Key.Z, ModifierKeys.Control));
        Assert.AreEqual(StepGridShortcut.None, StepGridShortcutPolicy.Resolve(true, true, true, true, true, true, Key.Delete, ModifierKeys.None));
        Assert.AreEqual(StepGridShortcut.None, StepGridShortcutPolicy.Resolve(true, false, false, false, false, false, Key.Delete, ModifierKeys.None));
        Assert.AreEqual(StepGridShortcut.None, StepGridShortcutPolicy.Resolve(true, false, true, false, true, true, Key.Y, ModifierKeys.Control));
        Assert.AreEqual(StepGridShortcut.None, StepGridShortcutPolicy.Resolve(true, false, true, false, true, true, Key.Z, ModifierKeys.Control | ModifierKeys.Shift));
        Assert.AreEqual(StepGridShortcut.None, StepGridShortcutPolicy.Resolve(false, false, true, false, false, false, Key.Escape, ModifierKeys.None));
        Assert.AreEqual(StepGridShortcut.ClearSelection, StepGridShortcutPolicy.Resolve(true, false, true, false, false, false, Key.Escape, ModifierKeys.None));
        Assert.IsFalse(StepGridShortcutPolicy.ShouldPreserveSelectionForDrag(2, true, false, ModifierKeys.Control));
        Assert.IsTrue(StepGridShortcutPolicy.ShouldPreserveSelectionForDrag(2, true, false, ModifierKeys.None));
        Assert.IsFalse(StepGridShortcutPolicy.ShouldPreserveSelectionForDrag(2, true, true, ModifierKeys.None));
    }

    [TestMethod]
    public async Task CtrlV_InTextInput_DoesNotPasteStepsOrBlockNativeClipboardRoute()
    {
        var source = CreateThreeStepScript();
        var target = new ScriptDefinition { Name = "Target" };
        var viewModel = CreateViewModel(new RecordingScriptStore([source, target]), new ImmediateEngine());
        await viewModel.InitializeAsync(CancellationToken.None);
        viewModel.CopyStepsCommand.Execute(null);
        viewModel.SelectedScript = viewModel.Scripts.Single(item => item.Id == target.Id);
        Assert.IsTrue(viewModel.PasteStepsCommand.CanExecute(null));

        var shortcut = StepGridShortcutPolicy.Resolve(
            isGridFocusWithin: true,
            isTextInput: true,
            canCopy: false,
            canPaste: viewModel.PasteStepsCommand.CanExecute(null),
            canUndo: false,
            canDelete: false,
            Key.V,
            ModifierKeys.Control);
        if (shortcut == StepGridShortcut.Paste)
            await viewModel.PasteStepsCommand.ExecuteAsync();

        Assert.AreEqual(StepGridShortcut.None, shortcut);
        Assert.AreEqual(0, target.Steps.Count);
        Assert.IsTrue(viewModel.PasteStepsCommand.CanExecute(null));
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
    public async Task ClearSelection_WithDirtyDraftKeepsSelectionWithoutPrompt()
    {
        var confirmation = new ConfigurableConfirmation(false);
        var viewModel = CreateViewModel(
            new RecordingScriptStore([CreateThreeStepScript()]), new ImmediateEngine(), confirmation);
        await viewModel.InitializeAsync(CancellationToken.None);
        var original = viewModel.SelectedStep;
        viewModel.EditorName = "Bản nháp chưa lưu";

        var cleared = viewModel.TryClearStepSelection();

        Assert.IsFalse(cleared);
        Assert.AreEqual(0, confirmation.CallCount);
        Assert.AreSame(original, viewModel.SelectedStep);
        Assert.AreEqual(1, viewModel.SelectedStepCount);
        Assert.IsTrue(viewModel.IsEditorDirty);
    }

    [TestMethod]
    public async Task ClearSelection_WithoutDraftClearsAllSelectedStepsWithoutPrompt()
    {
        var confirmation = new ConfigurableConfirmation(true);
        var viewModel = CreateViewModel(
            new RecordingScriptStore([CreateThreeStepScript()]), new ImmediateEngine(), confirmation);
        await viewModel.InitializeAsync(CancellationToken.None);
        viewModel.SynchronizeSelectedSteps([viewModel.Steps[0], viewModel.Steps[2]]);
        var cleared = viewModel.TryClearStepSelection();

        Assert.IsTrue(cleared);
        Assert.AreEqual(0, confirmation.CallCount);
        Assert.IsNull(viewModel.SelectedStep);
        Assert.AreEqual(0, viewModel.SelectedStepCount);
        Assert.IsFalse(viewModel.HasRegularEditorDraft);
    }

    [TestMethod]
    public async Task BulkPaste_UndoUsesOneEntryAndRestoresIdsOrderAndSelection()
    {
        var store = new RecordingScriptStore([CreateThreeStepScript()]);
        var viewModel = CreateViewModel(store, new ImmediateEngine());
        await viewModel.InitializeAsync(CancellationToken.None);
        var originalIds = viewModel.Steps.Select(step => step.Id).ToArray();
        viewModel.SynchronizeSelectedSteps([viewModel.Steps[0], viewModel.Steps[2]]);
        viewModel.CopyStepsCommand.Execute(null);

        await viewModel.PasteStepsCommand.ExecuteAsync();
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
    public async Task DirectToggleReorderAndDeleteRemainAvailableWhileSnapshotKeepsRunning()
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
        await viewModel.DeleteStepCommand.ExecuteAsync();

        Assert.IsFalse(first.IsEnabled);
        CollectionAssert.AreEqual(new[] { "C" }, viewModel.Steps.Select(step => step.Name).ToArray());
        Assert.AreEqual(3, store.SaveCount);
        CollectionAssert.AreEqual(new[] { "A", "B", "C" },
            engine.LastRequest!.Script.Steps.Select(step => step.Name).ToArray());
        Assert.IsTrue(engine.LastRequest.Script.Steps[0].IsEnabled);
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
        Assert.AreEqual(kind != ScriptStepKind.Delay, viewModel.ShowStepName);
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
        ScriptItemViewModel? focusedScript = null;
        viewModel.ScriptSelectionRestoreRequested += (items, focus) =>
        {
            if (focus) focusedScript = items.LastOrDefault();
        };

        await viewModel.CreateScriptCommand.ExecuteAsync();
        Assert.AreSame(viewModel.SelectedScript, focusedScript);
        viewModel.ScriptName = "Automation";
        await viewModel.RenameScriptCommand.ExecuteAsync();
        var sourceId = viewModel.SelectedScript!.Id;
        await viewModel.DuplicateScriptCommand.ExecuteAsync();
        var cloneId = viewModel.SelectedScript!.Id;
        Assert.AreSame(viewModel.SelectedScript, focusedScript);
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
        StepItemViewModel? focusedStep = null;
        viewModel.StepFocusRequested += item => focusedStep = item;

        await viewModel.NewStepCommand.ExecuteAsync();
        viewModel.EditorKind = ScriptStepKind.Tap;
        viewModel.EditorName = "Tap login";
        viewModel.EditorX = 100;
        viewModel.EditorY = 200;
        viewModel.EditorIsEnabled = false;
        viewModel.EditorContinueOnError = true;
        await viewModel.AddStepCommand.ExecuteAsync();
        var originalId = viewModel.SelectedStep!.Id;
        Assert.AreSame(viewModel.SelectedStep, focusedStep);
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

        await viewModel.NewStepCommand.ExecuteAsync();
        Assert.IsFalse(viewModel.HasRegularEditorDraft);
        Assert.IsFalse(viewModel.IsEditorDirty);
        Assert.AreEqual("Đã lưu", viewModel.EditorSaveState);
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

        await viewModel.NavigateToStepAsync(viewModel.Steps[1]);

        Assert.AreSame(originalStep, viewModel.SelectedStep);
        Assert.AreEqual("Bản nháp chưa lưu", viewModel.EditorName);
        Assert.IsTrue(viewModel.IsEditorDirty);
        Assert.AreEqual(1, confirmation.CallCount);
        Assert.AreEqual("Thay đổi chưa lưu", confirmation.LastTitle);
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

        await viewModel.NavigateToScriptAsync(viewModel.Scripts[1]);

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
        viewModel.CopyStepsCommand.Execute(null);
        viewModel.EditorName = "Bản nháp chưa lưu";

        await viewModel.PasteStepsCommand.ExecuteAsync();

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
    public async Task ScriptImport_CopyingCompositeBundleRemapsEachScriptExactlyOnce()
    {
        var existingChild = new ScriptDefinition { Name = "Existing child" };
        var existingComposite = new ScriptDefinition
        {
            Name = "Existing composite",
            Kind = ScriptKind.Composite,
            CompositeItems = [new ScriptReferenceItem { ScriptId = existingChild.Id }]
        };
        var incomingChild = new ScriptDefinition { Id = existingChild.Id, Name = "Incoming child" };
        var incomingComposite = new ScriptDefinition
        {
            Id = existingComposite.Id,
            Name = "Incoming composite",
            Kind = ScriptKind.Composite,
            CompositeItems = [new ScriptReferenceItem { ScriptId = incomingChild.Id }]
        };
        var store = new RecordingScriptStore([existingChild, existingComposite]);
        var transfer = new RecordingScriptTransferService([incomingComposite, incomingChild]);
        var dialogs = new RecordingFileDialog(@"C:\Temp\bundle.memuscript", exportPath: null);
        var viewModel = CreateViewModel(store, new ImmediateEngine(), fileDialog: dialogs, transfer: transfer,
            importConflict: new FixedImportConflict(ScriptImportConflictResolution.CreateCopy));
        await viewModel.InitializeAsync(CancellationToken.None);

        await viewModel.ImportScriptsCommand.ExecuteAsync();

        Assert.AreEqual(4, viewModel.Scripts.Count);
        var copiedComposite = viewModel.Scripts.Single(script =>
            script.Kind == ScriptKind.Composite && script.Id != existingComposite.Id).Model;
        var copiedChild = viewModel.Scripts.Single(script =>
            script.Kind == ScriptKind.Regular && script.Id != existingChild.Id).Model;
        Assert.AreEqual(copiedChild.Id,
            copiedComposite.CompositeItems.OfType<ScriptReferenceItem>().Single().ScriptId);
        Assert.AreNotEqual(incomingComposite.CompositeItems[0].Id, copiedComposite.CompositeItems[0].Id);
        Assert.AreEqual(1, store.SaveCount);
    }

    [TestMethod]
    public async Task ScriptImport_RejectsProspectiveLibraryBeforeAnyPartialMutation()
    {
        var existingChild = new ScriptDefinition { Name = "Existing child" };
        var existingRoot = new ScriptDefinition
        {
            Name = "Existing root",
            Kind = ScriptKind.Composite,
            CompositeItems = [new ScriptReferenceItem { ScriptId = existingChild.Id }]
        };
        var importedDependency = new ScriptDefinition { Name = "Imported dependency" };
        var importedReplacement = new ScriptDefinition
        {
            Id = existingChild.Id,
            Name = "Wrong-type replacement",
            Kind = ScriptKind.Composite,
            CompositeItems = [new ScriptReferenceItem { ScriptId = importedDependency.Id }]
        };
        var store = new RecordingScriptStore([existingChild, existingRoot]);
        var transfer = new RecordingScriptTransferService([importedReplacement, importedDependency]);
        var dialogs = new RecordingFileDialog(@"C:\Temp\invalid-merge.memuscript", exportPath: null);
        var viewModel = CreateViewModel(store, new ImmediateEngine(), fileDialog: dialogs, transfer: transfer,
            importConflict: new FixedImportConflict(ScriptImportConflictResolution.Overwrite));
        await viewModel.InitializeAsync(CancellationToken.None);

        await viewModel.ImportScriptsCommand.ExecuteAsync();

        Assert.AreEqual(2, viewModel.Scripts.Count);
        Assert.AreEqual(ScriptKind.Regular,
            viewModel.Scripts.Single(script => script.Id == existingChild.Id).Kind);
        Assert.AreEqual(0, store.SaveCount);
        StringAssert.Contains(viewModel.StatusMessage, "tham chiếu");
    }

    [TestMethod]
    public async Task CompositeDeleteGuardAndInternalClipboardUndoPreserveReferences()
    {
        var child = new ScriptDefinition { Name = "Protected child" };
        var originalReference = new ScriptReferenceItem { ScriptId = child.Id };
        var composite = new ScriptDefinition
        {
            Name = "Using composite",
            Kind = ScriptKind.Composite,
            CompositeItems = [originalReference]
        };
        var store = new RecordingScriptStore([child, composite]);
        var viewModel = CreateViewModel(store, new ImmediateEngine(), new ConfigurableConfirmation(true));
        await viewModel.InitializeAsync(CancellationToken.None);

        await viewModel.DeleteScriptCommand.ExecuteAsync();
        Assert.AreEqual(2, viewModel.Scripts.Count);
        StringAssert.Contains(viewModel.StatusMessage, composite.Name);

        viewModel.SelectedScript = viewModel.Scripts.Single(script => script.Id == composite.Id);
        viewModel.SynchronizeSelectedCompositeItems([viewModel.CompositeItems.Single()]);
        viewModel.CopyCompositeItemsCommand.Execute(null);
        await viewModel.PasteCompositeItemsCommand.ExecuteAsync();
        Assert.AreEqual(2, viewModel.CompositeItems.Count);
        Assert.AreNotEqual(viewModel.CompositeItems[0].Id, viewModel.CompositeItems[1].Id);

        await viewModel.UndoCompositeItemsCommand.ExecuteAsync();
        Assert.AreEqual(1, viewModel.CompositeItems.Count);
        Assert.AreEqual(originalReference.Id, viewModel.CompositeItems[0].Id);
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

    [TestMethod]
    public async Task ScriptImport_OverwriteRefreshesCommonScriptReferenceAndNextRunSnapshot()
    {
        var original = CreateThreeStepScript();
        var incoming = new ScriptDefinition
        {
            Id = original.Id,
            Name = "Incoming common",
            Steps = [new NoteStep { Name = "Imported step", Text = "Imported" }]
        };
        var engine = new ImmediateEngine();
        var viewModel = CreateViewModel(
            new RecordingScriptStore([original]),
            engine,
            fileDialog: new RecordingFileDialog(@"C:\Temp\input.memuscript", exportPath: null),
            transfer: new RecordingScriptTransferService([incoming]),
            importConflict: new FixedImportConflict(ScriptImportConflictResolution.Overwrite),
            instanceService: new FixedInstanceService([new MemuInstance(4, "VM 4", true, 104)]));
        await viewModel.InitializeAsync(CancellationToken.None);
        await viewModel.RefreshCommand.ExecuteAsync();

        await viewModel.ImportScriptsCommand.ExecuteAsync();
        viewModel.RunTargets.Single().IsSelected = true;
        await viewModel.RunCommand.ExecuteAsync();

        Assert.AreSame(viewModel.Scripts.Single(), viewModel.CommonRunScript);
        Assert.AreEqual("Incoming common", engine.LastRequest!.Script.Name);
        Assert.AreEqual("Imported step", engine.LastRequest.Script.Steps.Single().Name);
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

    [STATestMethod]
    public void RunControlPanel_CommonScriptDropdownBindsToApplicationSelection()
    {
        if (Application.Current is null)
        {
            var application = new MEmuScriptStudio.App.App();
            application.InitializeComponent();
        }
        var panel = new RunControlPanel();
        var combo = (ComboBox)panel.FindName("CommonScriptComboBox");
        Assert.AreEqual(nameof(MainViewModel.Scripts), BindingOperations.GetBinding(combo, ItemsControl.ItemsSourceProperty)!.Path.Path);
        Assert.AreEqual(nameof(MainViewModel.CommonRunScript), BindingOperations.GetBinding(combo, Selector.SelectedItemProperty)!.Path.Path);
        Assert.AreEqual(BindingMode.TwoWay, BindingOperations.GetBinding(combo, Selector.SelectedItemProperty)!.Mode);
    }

    [TestMethod]
    public async Task CommonScriptSelectionDefaultsToEditorAndRunUsesItsSnapshot()
    {
        var editor = new ScriptDefinition { Name = "Editor", Steps = { new NoteStep { Name = "Editor step", Text = "A" } } };
        var common = new ScriptDefinition { Name = "Common", Steps = { new NoteStep { Name = "Common step", Text = "B" } } };
        var engine = new BlockingEngine();
        var viewModel = CreateViewModel(
            new RecordingScriptStore([editor, common]),
            engine,
            instanceService: new FixedInstanceService([new MemuInstance(7, "VM 7", true, 107)]));
        await viewModel.InitializeAsync(CancellationToken.None);
        await viewModel.RefreshCommand.ExecuteAsync();

        Assert.AreSame(viewModel.SelectedScript, viewModel.CommonRunScript);
        viewModel.CommonRunScript = viewModel.Scripts.Single(item => item.Id == common.Id);
        viewModel.RunTargets.Single().IsSelected = true;
        await viewModel.RunCommand.ExecuteAsync();
        await engine.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        common.Name = "Mutated after launch";
        common.Steps[0].Name = "Mutated step";

        Assert.AreEqual(common.Id, engine.LastRequest!.Script.Id);
        Assert.AreEqual("Common", engine.LastRequest.Script.Name);
        Assert.AreEqual("Common step", engine.LastRequest.Script.Steps.Single().Name);
        viewModel.StopCommand.Execute(null);
        await WaitUntilAsync(() => !viewModel.IsExecuting);
    }

    [TestMethod]
    public async Task CommonScriptModeDisablesRunAndExplainsWhenNoValidScriptHasSteps()
    {
        var empty = new ScriptDefinition { Name = "Empty" };
        var viewModel = CreateViewModel(
            new RecordingScriptStore([empty]),
            new ImmediateEngine(),
            instanceService: new FixedInstanceService([new MemuInstance(1, "VM", true, 101)]));
        await viewModel.InitializeAsync(CancellationToken.None);
        await viewModel.RefreshCommand.ExecuteAsync();
        viewModel.RunTargets.Single().IsSelected = true;

        Assert.IsFalse(viewModel.RunCommand.CanExecute(null));
        Assert.IsFalse(viewModel.RunAllRemainingCommand.CanExecute(null));
        StringAssert.Contains(viewModel.RunConfigurationError, "chưa có bước");
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
        await WaitUntilAsync(() => viewModel.LatestRunResult is not null);

        CollectionAssert.AreEquivalent(new[] { 2, 5 }, engine.Requests.Select(request => request.InstanceIndex).ToArray());
        var latest = viewModel.LatestRunResult!;
        Assert.AreEqual(2, latest.TotalInstanceCount);
        Assert.AreEqual(1, latest.SucceededCount);
        Assert.AreEqual(1, latest.FailedCount);
        Assert.AreEqual(0, latest.CancelledCount);
        Assert.AreEqual(1, latest.IssueInstances.Count);
        Assert.AreEqual(2, latest.IssueInstances.Single().Index);
        Assert.AreEqual(InstanceExecutionStatus.Failed, latest.IssueInstances.Single().Status);
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
            new MemuInstance(2, "Stops after selection", true, 102)
        };
        var instances = new MutableInstanceService(targets);
        var engine = new ReportingMultiEngine();
        var viewModel = CreateViewModel(new RecordingScriptStore(), engine, instanceService: instances);
        await viewModel.InitializeAsync(CancellationToken.None);
        await viewModel.RefreshCommand.ExecuteAsync();
        foreach (var target in viewModel.RunTargets) target.IsSelected = true;
        instances.Instances =
        [
            targets[0],
            new MemuInstance(2, "Stops after selection", false, null)
        ];

        await viewModel.RunCommand.ExecuteAsync();
        await WaitUntilAsync(() => viewModel.LatestRunResult?.GroupName == "Nhóm 01");

        CollectionAssert.AreEqual(new[] { 1 }, engine.Requests.Select(request => request.InstanceIndex).ToArray());
        Assert.AreEqual(2, viewModel.LatestRunResult!.TotalInstanceCount);
        Assert.AreEqual(1, viewModel.LatestRunResult.SucceededCount);
        Assert.AreEqual(0, viewModel.LatestRunResult.FailedCount);
        Assert.AreEqual(1, viewModel.LatestRunResult.UnavailableCount);
        Assert.AreEqual(InstanceExecutionStatus.Unavailable, viewModel.LatestRunResult.IssueInstances.Single().Status);
        Assert.AreEqual(2, viewModel.LatestRunResult.IssueInstances.Single().Index);

        engine.Requests.Clear();
        viewModel.StopAllOnInvalidTarget = true;
        foreach (var target in viewModel.RunTargets) target.IsSelected = true;
        await viewModel.RunCommand.ExecuteAsync();
        await WaitUntilAsync(() => viewModel.LatestRunResult?.GroupName == "Nhóm 02");

        Assert.AreEqual(0, engine.Requests.Count);
        Assert.AreEqual(0, viewModel.LatestRunResult!.SucceededCount);
        Assert.AreEqual(0, viewModel.LatestRunResult.FailedCount);
        Assert.AreEqual(1, viewModel.LatestRunResult.UnavailableCount);
        Assert.AreEqual(1, viewModel.LatestRunResult.CancelledCount);
        CollectionAssert.AreEquivalent(new[] { 1, 2 }, viewModel.LatestRunResult.IssueInstances.Select(item => item.Index).ToArray());
        StringAssert.Contains(viewModel.StatusMessage, "dừng toàn bộ tại preflight");
    }

    [TestMethod]
    public async Task CompletedRunLeavesActiveStateWhileSettingsUpdateIsPendingAndKeepsSnapshot()
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

        Assert.IsFalse(viewModel.IsExecuting);
        Assert.AreEqual(0, viewModel.InstanceRuns.Count);
        Assert.IsNotNull(viewModel.LatestRunResult);
        Assert.IsTrue(viewModel.CanChangeSelection);
        Assert.IsTrue(viewModel.BrowseCommand.CanExecute(null));
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
    public void MainWindow_DoesNotContainDuplicateRunStateOrExecutionLog()
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
            Assert.IsNull(window.FindName("RunTargetsList"));
            Assert.IsNull(window.FindName("InstanceRunsGrid"));
            Assert.IsNull(window.FindName("ExecutionLogList"));
            Assert.IsNull(typeof(MainViewModel).GetProperty("SelectedInstanceRun"));
            Assert.IsNull(typeof(MainViewModel).GetProperty("ExecutionLog"));
            Assert.IsNull(typeof(MainViewModel).GetProperty("FailedInstanceCount"));
            Assert.IsNull(typeof(MainViewModel).GetProperty("CanChangeRunTargets"));
            Assert.IsNull(typeof(StepItemViewModel).GetProperty("StatusText"));
            Assert.IsNull(typeof(StepItemViewModel).GetProperty("Result"));
            Assert.IsNotNull(window.FindName("CopyStepsButton"));
            Assert.IsNotNull(window.FindName("PasteStepsButton"));

            var stepsGrid = (DataGrid)window.FindName("StepsGrid");
            CollectionAssert.AreEqual(new[] { "Tên", "Loại", "Bật" },
                stepsGrid.Columns.Select(column => column.Header?.ToString()).ToArray());
            Assert.IsTrue(stepsGrid.Columns.All(column => !column.Width.IsAbsolute),
                "Editor columns must use Auto/* sizing so translated text and DPI scaling do not clip fixed columns.");

            var saveStateBindings = FindLogicalDescendants<TextBlock>(window)
                .Count(text => BindingOperations.GetBinding(text, TextBlock.TextProperty)?.Path.Path == nameof(MainViewModel.EditorSaveState));
            Assert.AreEqual(1, saveStateBindings, "Editor save state must appear only in the bottom summary bar.");
        }
        finally
        {
            window.Close();
        }
    }

    [STATestMethod]
    public void MainWindow_EditorHeaderAndToolbarUseSeparatedAlignedContracts()
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
            var header = (Grid)window.FindName("StepsHeaderGrid");
            var title = (TextBlock)window.FindName("StepsHeaderTitle");
            var clipboard = (TextBlock)window.FindName("StepClipboardStatus");
            var path = (Border)window.FindName("MemucPathField");
            var pathText = (TextBlock)window.FindName("MemucPathTextBlock");
            var memucStatus = (TextBlock)window.FindName("MemucStatusText");
            var adbPath = (Border)window.FindName("AdbPathField");
            var adbPathText = (TextBlock)window.FindName("AdbPathTextBlock");
            var adbStatus = (TextBlock)window.FindName("AdbStatusText");
            var instance = (ComboBox)window.FindName("InstanceComboBox");
            var browse = (Button)window.FindName("BrowseMemucButton");
            var refresh = (Button)window.FindName("RefreshInstancesButton");
            var checkConnection = (Button)window.FindName("CheckConnectionButton");
            var usageGuide = (Button)window.FindName("UsageGuideButton");
            var settings = (Button)window.FindName("DeviceSettingsButton");
            var settingsPopup = (Popup)window.FindName("DeviceSettingsPopup");
            var controlCenter = (Button)window.FindName("OpenControlCenterButton");
            var statusBar = (Border)window.FindName("MainStatusBar");

            Assert.AreSame(header, LogicalTreeHelper.GetParent(title));
            Assert.AreSame(header, LogicalTreeHelper.GetParent(clipboard));
            Assert.AreEqual(0, Grid.GetColumn(title));
            Assert.AreEqual(2, Grid.GetColumn(clipboard));
            Assert.AreEqual(nameof(MainViewModel.StepClipboardSummary),
                BindingOperations.GetBinding(clipboard, TextBlock.TextProperty)!.Path.Path);
            Assert.AreEqual(TextTrimming.CharacterEllipsis, clipboard.TextTrimming);
            Assert.AreEqual(nameof(MainViewModel.StepClipboardSummary),
                BindingOperations.GetBinding(clipboard, FrameworkElement.ToolTipProperty)!.Path.Path);
            Assert.AreEqual("Clipboard: trống", viewModel.StepClipboardSummary);

            Assert.AreEqual(34d, path.Height);
            Assert.AreEqual(new Thickness(10, 6, 10, 6), path.Padding);
            Assert.AreEqual(VerticalAlignment.Center, path.VerticalAlignment);
            Assert.AreEqual(TextTrimming.CharacterEllipsis, pathText.TextTrimming);
            Assert.AreEqual(nameof(MainViewModel.MemucPathDisplay),
                BindingOperations.GetBinding(pathText, TextBlock.TextProperty)!.Path.Path);
            Assert.AreEqual(nameof(MainViewModel.MemucPath),
                BindingOperations.GetBinding(path, FrameworkElement.ToolTipProperty)!.Path.Path);
            Assert.AreEqual(nameof(MainViewModel.MemucConnectionStatus),
                BindingOperations.GetBinding(memucStatus, TextBlock.TextProperty)!.Path.Path);
            Assert.AreEqual(nameof(MainViewModel.AdbPathDisplay),
                BindingOperations.GetBinding(adbPathText, TextBlock.TextProperty)!.Path.Path);
            Assert.AreEqual(nameof(MainViewModel.AdbPath),
                BindingOperations.GetBinding(adbPath, FrameworkElement.ToolTipProperty)!.Path.Path);
            Assert.AreEqual(nameof(MainViewModel.AdbConnectionStatus),
                BindingOperations.GetBinding(adbStatus, TextBlock.TextProperty)!.Path.Path);
            Assert.AreEqual("Kiểm tra kết nối", checkConnection.Content);
            Assert.AreEqual(nameof(MainViewModel.RefreshCommand),
                BindingOperations.GetBinding(checkConnection, Button.CommandProperty)!.Path.Path);
            Assert.AreEqual("Hướng dẫn", usageGuide.Content);
            Assert.AreEqual(34d, instance.Height);
            Assert.AreEqual(new Thickness(10, 5, 10, 5), instance.Padding);
            Assert.AreEqual(VerticalAlignment.Center, instance.VerticalAlignment);
            Assert.AreEqual(VerticalAlignment.Center, instance.VerticalContentAlignment);
            Assert.AreEqual(HorizontalAlignment.Stretch, instance.HorizontalContentAlignment);
            Assert.IsTrue(string.IsNullOrEmpty(instance.DisplayMemberPath));
            Assert.AreEqual("Kết nối / Cài đặt thiết bị", settings.Content);
            Assert.AreSame(settings, settingsPopup.PlacementTarget);

            var instanceTemplate = (Grid)instance.ItemTemplate.LoadContent();
            var instanceTexts = FindLogicalDescendants<TextBlock>(instanceTemplate).ToList();
            Assert.AreEqual(2, instanceTexts.Count);
            Assert.AreEqual(0, Grid.GetColumn(instanceTexts[0]));
            Assert.AreEqual(2, Grid.GetColumn(instanceTexts[1]));
            Assert.AreEqual(TextTrimming.CharacterEllipsis, instanceTexts[0].TextTrimming);

            foreach (var button in new[] { browse, refresh, controlCenter })
            {
                Assert.AreEqual(34d, button.Height);
                Assert.AreEqual(new Thickness(12, 6, 12, 6), button.Padding);
                Assert.AreEqual(new Thickness(0), button.Margin);
                Assert.AreEqual(VerticalAlignment.Center, button.VerticalAlignment);
            }

            var statusLabels = string.Join(" ",
                FindLogicalDescendants<Run>(statusBar).Select(run => run.Text));
            Assert.AreEqual(1, FindLogicalDescendants<TextBlock>(statusBar).Count(text =>
                BindingOperations.GetBinding(text, TextBlock.TextProperty)?.Path.Path == nameof(MainViewModel.MemucConnectionStatus)));
            Assert.AreEqual(1, FindLogicalDescendants<TextBlock>(statusBar).Count(text =>
                BindingOperations.GetBinding(text, TextBlock.TextProperty)?.Path.Path == nameof(MainViewModel.AdbConnectionStatus)));
            Assert.IsFalse(statusLabels.Contains("Đang chạy", StringComparison.Ordinal));
            Assert.IsFalse(statusLabels.Contains("Chờ khởi chạy", StringComparison.Ordinal));
            Assert.IsFalse(statusLabels.Contains("Thất bại gần nhất", StringComparison.Ordinal));
            Assert.IsFalse(FindLogicalDescendants<TextBlock>(window)
                .Any(text => text.Text?.Contains("Ctrl+C/Ctrl+V hoạt động", StringComparison.Ordinal) == true));
        }
        finally
        {
            window.Close();
        }
    }

    [STATestMethod]
    public void Phase15b_LargeListsAndResponsiveColumnsKeepVirtualizationAndReadableSizing()
    {
        if (Application.Current is null)
        {
            var application = new MEmuScriptStudio.App.App();
            application.InitializeComponent();
        }

        var viewModel = CreateViewModel(new RecordingScriptStore(), new ImmediateEngine());
        var mainWindow = new MainWindow(viewModel);
        var controlCenter = new ControlCenterWindow(viewModel);
        var runPanel = (RunControlPanel)controlCenter.FindName("RunPanel");
        try
        {
            var scripts = (ListBox)mainWindow.FindName("ScriptsList");
            var steps = (DataGrid)mainWindow.FindName("StepsGrid");
            var targets = (DataGrid)runPanel.FindName("RunTargetsGrid");
            var active = (DataGrid)runPanel.FindName("ActiveInstancesGrid");
            var recentPanel = (RecentRunsPanel)controlCenter.FindName("RecentRunsPanel");
            var latest = (DataGrid)recentPanel.FindName("RecentRunInstancesGrid");

            foreach (var list in new ItemsControl[] { scripts, steps, targets, active, latest })
            {
                Assert.IsTrue(VirtualizingPanel.GetIsVirtualizing(list), $"{list.GetType().Name} must keep virtualization enabled.");
                Assert.AreEqual(VirtualizationMode.Recycling, VirtualizingPanel.GetVirtualizationMode(list));
                Assert.IsTrue(ScrollViewer.GetCanContentScroll(list));
                Assert.IsFalse(ScrollViewer.GetIsDeferredScrollingEnabled(list),
                    $"{list.GetType().Name} must update content while the scrollbar thumb is dragged.");
                Assert.IsFalse(HasLogicalAncestor<ScrollViewer>(list),
                    $"{list.GetType().Name} must not be wrapped by an authored outer ScrollViewer.");
            }

            foreach (var grid in new[] { steps, targets, latest })
            {
                Assert.IsTrue(grid.EnableRowVirtualization);
                Assert.IsTrue(grid.EnableColumnVirtualization);
                Assert.IsTrue(grid.Columns.All(column => !column.Width.IsAbsolute),
                    $"{grid.Name} columns must use Auto/* sizing instead of fixed pixel widths.");
            }

            Assert.IsTrue(active.EnableRowVirtualization);
            Assert.IsTrue(active.EnableColumnVirtualization);
            Assert.IsTrue(active.Columns.All(column => !column.Width.IsAbsolute),
                "Active columns must adapt with Auto/Star sizing instead of fixed pixel widths.");
            Assert.AreEqual(5, active.Columns.Count(column => column.Width.IsAuto));
            Assert.AreEqual(4, active.Columns.Count(column => column.Width.IsStar));

            Assert.IsTrue(active.Columns.All(column => column.MinWidth >= 50),
                "Active columns need readable minima and horizontal scrolling instead of collapsing to a few characters.");
            Assert.IsTrue(latest.Columns.All(column => column.MinWidth >= 56),
                "Latest-result columns need readable minima and horizontal scrolling instead of collapsing to a few characters.");

            var rootColumns = (Grid)runPanel.FindName("RunControlColumns");
            Assert.AreEqual(GridUnitType.Star, rootColumns.ColumnDefinitions[0].Width.GridUnitType,
                "The constructor must keep proportional XAML columns until Loaded provides usable ActualWidth.");
            Assert.AreEqual(GridUnitType.Star, rootColumns.ColumnDefinitions[2].Width.GridUnitType);
            var reservedWidth = rootColumns.ColumnDefinitions.Sum(column => column.MinWidth) + 80;
            Assert.IsTrue(controlCenter.MinWidth >= reservedWidth,
                "The Control Center minimum width must include its content minima plus window/tab/panel chrome.");
            Assert.AreEqual(680d, controlCenter.Height);
            Assert.AreEqual(420d, controlCenter.MinHeight);
            var setupColumn = (Grid)runPanel.FindName("RunSetupColumn");
            Assert.AreEqual(GridUnitType.Star, setupColumn.RowDefinitions[2].Height.GridUnitType);
            Assert.AreEqual(0d, setupColumn.RowDefinitions[2].MinHeight,
                "The virtualized target list must absorb short-window pressure instead of forcing the bottom controls outside the panel.");

            var assignmentColumn = targets.Columns.OfType<DataGridTemplateColumn>()
                .Single(column => Equals(column.Header, "Kịch bản được gán"));
            Assert.IsInstanceOfType<TextBlock>(assignmentColumn.CellTemplate.LoadContent());
            Assert.IsInstanceOfType<ComboBox>(assignmentColumn.CellEditingTemplate.LoadContent());

            var commonScript = (ComboBox)runPanel.FindName("CommonScriptComboBox");
            Assert.AreEqual(34d, commonScript.Height);
            var comboText = (TextBlock)commonScript.ItemTemplate.LoadContent();
            Assert.AreEqual(TextTrimming.CharacterEllipsis, comboText.TextTrimming);
            Assert.AreEqual(nameof(ScriptItemViewModel.DisplayNameWithKind),
                BindingOperations.GetBinding(comboText, TextBlock.TextProperty)!.Path.Path);
            Assert.AreEqual(nameof(ScriptItemViewModel.DisplayNameWithKind),
                BindingOperations.GetBinding(comboText, FrameworkElement.ToolTipProperty)!.Path.Path);

            var stepsTitle = (TextBlock)mainWindow.FindName("StepsHeaderTitle");
            Assert.AreSame(Application.Current!.FindResource("SectionTitleStyle"), stepsTitle.Style);
            Assert.AreEqual(36d, steps.RowHeight);
            Assert.IsNull(mainWindow.FindName("InitializationOverlay"),
                "MainWindow must expose the workspace immediately instead of covering it with a startup screen.");
            var emptyActive = (TextBlock)runPanel.FindName("ActiveInstancesEmptyState");
            Assert.IsTrue(emptyActive.Style.Triggers.OfType<DataTrigger>().Any(trigger =>
                trigger.Binding is Binding binding && binding.Path.Path == nameof(MainViewModel.HasNoActiveInstances)));
            Assert.AreEqual("Chưa có thiết bị đang hoạt động.", emptyActive.Text);
            var filteredEmptyActive = (TextBlock)runPanel.FindName("ActiveInstancesFilterEmptyState");
            Assert.IsTrue(filteredEmptyActive.Style.Triggers.OfType<DataTrigger>().Any(trigger =>
                trigger.Binding is Binding binding &&
                binding.Path.Path == nameof(MainViewModel.HasActiveInstancesButNoFilteredMatches)));
            Assert.AreEqual("Không có thiết bị phù hợp tìm kiếm hoặc bộ lọc.", filteredEmptyActive.Text);

            var latestTitle = FindLogicalDescendants<TextBlock>(recentPanel)
                .Single(text => Equals(text.Text, "Kết quả gần đây"));
            Assert.AreEqual(TextTrimming.CharacterEllipsis, latestTitle.TextTrimming);
            Assert.AreEqual("Tối đa 20 lần chạy hoàn tất trong phiên hiện tại", latestTitle.ToolTip);

            var assignSelected = (Button)runPanel.FindName("AssignScriptToSelectedButton");
            var assignAll = (Button)runPanel.FindName("AssignSelectedScriptToAllButton");
            Assert.AreSame(LogicalTreeHelper.GetParent(assignSelected), LogicalTreeHelper.GetParent(assignAll));
            Assert.IsInstanceOfType<WrapPanel>(LogicalTreeHelper.GetParent(assignSelected));
            Assert.AreEqual(34d, assignSelected.Height);
            Assert.AreEqual(34d, assignAll.Height);
            Assert.AreEqual("Gán cho thiết bị đã chọn", assignSelected.Content);
            Assert.AreEqual("Gán cho tất cả thiết bị", assignAll.Content);

            var runSelected = (Button)runPanel.FindName("RunSelectedButton");
            var runAll = (Button)runPanel.FindName("RunAllRemainingButton");
            var stopAll = FindLogicalDescendants<Button>(runPanel).Single(button => Equals(button.Content, "Dừng tất cả"));
            var runActions = LogicalTreeHelper.GetParent(runSelected);
            Assert.AreSame(runActions, LogicalTreeHelper.GetParent(runAll));
            Assert.IsInstanceOfType<WrapPanel>(runActions);
            Assert.AreEqual(nameof(MainViewModel.RunAllRemainingLabel),
                BindingOperations.GetBinding(runAll, ContentControl.ContentProperty)!.Path.Path);
            Assert.AreEqual(nameof(MainViewModel.StopCommand),
                BindingOperations.GetBinding(stopAll, Button.CommandProperty)!.Path.Path);
            Assert.AreEqual(2, Grid.GetRow((UIElement)runActions),
                "Run actions must occupy a separate row so they cannot squeeze the spacing controls into a narrow column.");

            viewModel.ScriptAssignmentMode = ScriptAssignmentMode.PerInstance;
            controlCenter.Width = controlCenter.MinWidth;
            controlCenter.Height = controlCenter.MinHeight;
            controlCenter.Show();
            controlCenter.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            controlCenter.UpdateLayout();
            Assert.IsTrue(targets.ActualHeight >= 70,
                $"At minimum window size, the per-instance target grid must retain its header and at least one complete data row viewport (target {targets.ActualHeight}, window {controlCenter.ActualHeight}, panel {runPanel.ActualHeight}, rows {string.Join(",", setupColumn.RowDefinitions.Select(row => row.ActualHeight))}).");
            var spacingOptions = (Grid)runPanel.FindName("LaunchSpacingOptions");
            Assert.IsTrue(spacingOptions.ActualHeight <= 80,
                "Duration spacing inputs must keep their compact fixed/random rows at the minimum supported width.");
            viewModel.IsRandomSpacing = true;
            controlCenter.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            controlCenter.UpdateLayout();
            Assert.IsTrue(targets.ActualHeight >= 70,
                "Random spacing must still leave the target grid header and one complete row at minimum window size.");
            Assert.IsTrue(spacingOptions.ActualHeight <= 80,
                "Random spacing must remain two compact From/To rows without wrapping.");
        }
        finally
        {
            controlCenter.Close();
            mainWindow.Close();
        }
    }

    [STATestMethod]
    public void ActiveInstancesGrid_ResponsiveScrollbarHandlesEmptyFilteredOneAndManyRows()
    {
        if (Application.Current is null)
        {
            var application = new MEmuScriptStudio.App.App();
            application.InitializeComponent();
        }

        var viewModel = CreateViewModel(new RecordingScriptStore(), new ImmediateEngine());
        var script = new ScriptDefinition
        {
            Name = "Responsive layout",
            Steps = [new NoteStep { Name = "Current step" }]
        };
        var firstItem = new InstanceRunItemViewModel(
            Guid.NewGuid(), new MemuInstance(1, "First instance", true, 101), script, (_, _) => true);
        var window = new ControlCenterWindow(viewModel) { Height = 560 };

        try
        {
            window.Show();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            window.UpdateLayout();
            var panel = (RunControlPanel)window.FindName("RunPanel");
            var activeGrid = (DataGrid)panel.FindName("ActiveInstancesGrid");

            AssertActiveGridResponsiveState(window, panel, activeGrid, isWide: true, expectedItemCount: 0);
            AssertActiveGridResponsiveState(window, panel, activeGrid, isWide: false, expectedItemCount: 0);

            viewModel.ActiveInstanceRuns.Add(firstItem);
            viewModel.ActiveInstanceSearchText = "no matching active instance";
            DrainDataBindings();
            Assert.AreEqual(1, viewModel.ActiveInstanceRuns.Count);
            Assert.AreEqual(0, viewModel.FilteredActiveInstanceRuns.Count);
            Assert.IsTrue(viewModel.HasActiveInstancesButNoFilteredMatches);
            AssertActiveGridResponsiveState(window, panel, activeGrid, isWide: true, expectedItemCount: 0);
            AssertActiveGridResponsiveState(window, panel, activeGrid, isWide: false, expectedItemCount: 0);

            viewModel.ActiveInstanceSearchText = string.Empty;
            DrainDataBindings();
            AssertActiveGridResponsiveState(window, panel, activeGrid, isWide: true, expectedItemCount: 1);
            AssertActiveGridResponsiveState(window, panel, activeGrid, isWide: false, expectedItemCount: 1);

            for (var index = 2; index <= 200; index++)
            {
                viewModel.ActiveInstanceRuns.Add(new InstanceRunItemViewModel(
                    Guid.NewGuid(), new MemuInstance(index, $"Instance {index:D3}", true, 100 + index), script, (_, _) => true));
            }
            DrainDataBindings();
            AssertActiveGridResponsiveState(window, panel, activeGrid, isWide: true, expectedItemCount: 200);
            AssertActiveGridResponsiveState(window, panel, activeGrid, isWide: false, expectedItemCount: 200);
            AssertActiveGridResponsiveState(window, panel, activeGrid, isWide: true, expectedItemCount: 200);
        }
        finally
        {
            window.Close();
        }
    }

    [TestMethod]
    public void UsageGuideStartInfo_UsesNotepadWithOneLiteralPathArgument()
    {
        var startInfo = MainWindow.CreateUsageGuideStartInfo(
            @"C:\Portable App\HUONG-DAN-SU-DUNG.md",
            @"C:\Windows\System32");

        Assert.AreEqual(@"C:\Windows\System32\notepad.exe", startInfo.FileName);
        Assert.IsFalse(startInfo.UseShellExecute);
        CollectionAssert.AreEqual(
            new[] { @"C:\Portable App\HUONG-DAN-SU-DUNG.md" },
            startInfo.ArgumentList.ToArray());
    }

    [STATestMethod]
    public void ActiveInstancesGrid_NarrowResizePreservesIdentitySelectionVirtualizationAndReachesLastColumn()
    {
        if (Application.Current is null)
        {
            var application = new MEmuScriptStudio.App.App();
            application.InitializeComponent();
        }

        var viewModel = CreateViewModel(new RecordingScriptStore(), new ImmediateEngine());
        var script = new ScriptDefinition { Name = "Identity", Steps = [new NoteStep { Name = "Current" }] };
        var items = Enumerable.Range(1, 200)
            .Select(index => new InstanceRunItemViewModel(
                Guid.NewGuid(), new MemuInstance(index, $"Instance {index:D3}", true, 100 + index), script, (_, _) => true))
            .ToArray();
        foreach (var item in items)
            viewModel.ActiveInstanceRuns.Add(item);
        var selectedItem = items[119];
        var window = new ControlCenterWindow(viewModel) { Height = 560 };

        try
        {
            window.Show();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            window.UpdateLayout();
            var panel = (RunControlPanel)window.FindName("RunPanel");
            var activeGrid = (DataGrid)panel.FindName("ActiveInstancesGrid");
            AssertActiveGridResponsiveState(window, panel, activeGrid, isWide: true, expectedItemCount: items.Length);
            activeGrid.SelectedItem = selectedItem;
            selectedItem.IsSelected = true;

            AssertActiveGridResponsiveState(window, panel, activeGrid, isWide: false, expectedItemCount: items.Length);
            var scrollViewer = FindVisualDescendants<ScrollViewer>(activeGrid)
                .First(viewer => viewer.Name == "DG_ScrollViewer");
            Assert.AreSame(selectedItem, activeGrid.SelectedItem);
            Assert.AreSame(selectedItem, activeGrid.Items[119]);
            Assert.IsTrue(selectedItem.IsSelected);
            Assert.IsTrue(activeGrid.Items.Cast<object>().SequenceEqual(items));
            Assert.IsTrue(VirtualizingPanel.GetIsVirtualizing(activeGrid));
            Assert.AreEqual(VirtualizationMode.Recycling, VirtualizingPanel.GetVirtualizationMode(activeGrid));
            Assert.IsTrue(ScrollViewer.GetCanContentScroll(activeGrid));
            Assert.IsFalse(HasLogicalAncestor<ScrollViewer>(activeGrid));
            var realizedRows = FindVisualDescendants<DataGridRow>(activeGrid).ToList();
            Assert.IsTrue(realizedRows.Count > 0);
            Assert.IsTrue(realizedRows.Count < items.Length,
                "Row virtualization must not realize the full active-instance collection.");

            var lastColumn = activeGrid.Columns[^1];
            activeGrid.ScrollIntoView(selectedItem, lastColumn);
            DrainDataBindings();
            window.UpdateLayout();
            scrollViewer.ScrollToRightEnd();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            window.UpdateLayout();
            var row = (DataGridRow)activeGrid.ItemContainerGenerator.ContainerFromItem(selectedItem);
            var lastCell = FindVisualDescendants<DataGridCell>(row)
                .SingleOrDefault(candidate => ReferenceEquals(candidate.Column, lastColumn));
            Assert.IsNotNull(lastCell, "The Stop column must be materialized after native DataGrid scrolling.");
            AssertElementWithinHorizontalViewport(activeGrid, scrollViewer, lastCell,
                "The Stop cell must be inside the internal DataGrid viewport after scrolling right.");
            var stopButton = FindVisualDescendants<Button>(lastCell).Single();
            Assert.IsTrue(stopButton.IsHitTestVisible);
            AssertElementWithinHorizontalViewport(activeGrid, scrollViewer, stopButton,
                "The Stop button must be reachable and hit-testable in a narrow viewport.");

            Assert.IsTrue(scrollViewer.HorizontalOffset > 0);
            Assert.AreEqual(scrollViewer.ScrollableWidth, scrollViewer.HorizontalOffset, 0.5,
                "The internal DataGrid scroller must reach the message and stop columns at the right edge.");

            AssertActiveGridResponsiveState(window, panel, activeGrid, isWide: true, expectedItemCount: items.Length);
            Assert.AreSame(selectedItem, activeGrid.SelectedItem);
            Assert.AreSame(selectedItem, activeGrid.Items[119]);
            Assert.IsTrue(selectedItem.IsSelected);
        }
        finally
        {
            window.Close();
        }
    }

    [STATestMethod]
    public void StepsGrid_UsesDedicatedLeftAndCenteredColumnStyles()
    {
        if (Application.Current is null)
        {
            var application = new MEmuScriptStudio.App.App();
            application.InitializeComponent();
        }
        var window = new MainWindow(CreateViewModel(new RecordingScriptStore(), new ImmediateEngine()));
        try
        {
            var grid = (DataGrid)window.FindName("StepsGrid");
            var nameColumn = (DataGridTemplateColumn)grid.Columns[0];
            var kindColumn = (DataGridTextColumn)grid.Columns[1];
            var enabledColumn = (DataGridTemplateColumn)grid.Columns[2];
            var nameText = (TextBlock)nameColumn.CellTemplate.LoadContent();
            var enabledCheckBox = (CheckBox)enabledColumn.CellTemplate.LoadContent();

            foreach (var headerStyle in new[] { nameColumn.HeaderStyle, kindColumn.HeaderStyle })
            {
                Assert.AreEqual(HorizontalAlignment.Left,
                    headerStyle!.Setters.Cast<Setter>().Single(setter => setter.Property == Control.HorizontalContentAlignmentProperty).Value);
                Assert.AreEqual(VerticalAlignment.Center,
                    headerStyle!.Setters.Cast<Setter>().Single(setter => setter.Property == Control.VerticalContentAlignmentProperty).Value);
                Assert.AreEqual(new Thickness(11, 5, 11, 5),
                    headerStyle!.Setters.Cast<Setter>().Single(setter => setter.Property == Control.PaddingProperty).Value);
            }

            Assert.AreEqual(new Thickness(11, 0, 11, 0), nameText.Padding);
            Assert.AreEqual(HorizontalAlignment.Stretch, nameText.HorizontalAlignment);
            Assert.AreEqual(VerticalAlignment.Center, nameText.VerticalAlignment);
            Assert.AreEqual(TextAlignment.Left, nameText.TextAlignment);
            Assert.AreSame(nameText.Style, kindColumn.ElementStyle);
            Assert.AreSame(nameColumn.CellStyle, kindColumn.CellStyle);

            Assert.AreEqual(HorizontalAlignment.Center,
                enabledColumn.HeaderStyle!.Setters.Cast<Setter>().Single(setter => setter.Property == Control.HorizontalContentAlignmentProperty).Value);
            Assert.AreEqual(VerticalAlignment.Center,
                enabledColumn.HeaderStyle!.Setters.Cast<Setter>().Single(setter => setter.Property == Control.VerticalContentAlignmentProperty).Value);
            Assert.AreEqual(HorizontalAlignment.Center,
                enabledColumn.CellStyle!.Setters.Cast<Setter>().Single(setter => setter.Property == Control.HorizontalContentAlignmentProperty).Value);
            Assert.AreEqual(VerticalAlignment.Center,
                enabledColumn.CellStyle!.Setters.Cast<Setter>().Single(setter => setter.Property == Control.VerticalContentAlignmentProperty).Value);
            Assert.AreEqual(HorizontalAlignment.Center, enabledCheckBox.HorizontalAlignment);
            Assert.AreEqual(VerticalAlignment.Center, enabledCheckBox.VerticalAlignment);
            Assert.AreEqual(DataGridSelectionUnit.FullRow, grid.SelectionUnit);
            Assert.AreSame(Application.Current!.FindResource("PanelBrush"), grid.AlternatingRowBackground,
                "Alternating rows must use the same white panel brush rather than zebra striping.");
        }
        finally
        {
            window.Close();
        }
    }

    [STATestMethod]
    public void ControlCenterEntryAndRecentRuns_UseTheIntendedXamlContracts()
    {
        if (Application.Current is null)
        {
            var application = new MEmuScriptStudio.App.App();
            application.InitializeComponent();
        }

        var viewModel = CreateViewModel(new RecordingScriptStore(), new ImmediateEngine());
        var window = new MainWindow(viewModel);
        var runPanel = new RunControlPanel();
        try
        {
            var controlCenterButtons = FindLogicalDescendants<Button>(window)
                .Where(button => Equals(button.Content, "Mở Trung tâm điều khiển"))
                .ToList();
            var statusBar = (Border)window.FindName("MainStatusBar");
            var recentPanel = new RecentRunsPanel { DataContext = viewModel };
            var latestCard = (Border)recentPanel.FindName("RecentRunsCard");
            var latestGrid = (DataGrid)recentPanel.FindName("RecentRunInstancesGrid");
            var clearButton = FindLogicalDescendants<Button>(recentPanel).Single(button => Equals(button.Content, "Xóa lịch sử"));

            Assert.AreEqual(1, controlCenterButtons.Count, "MainWindow must expose a single Control Center entry point.");
            Assert.AreSame(window.FindName("OpenControlCenterButton"), controlCenterButtons.Single());
            Assert.AreEqual(0, FindLogicalDescendants<Button>(statusBar).Count(), "The bottom status bar is data-only.");
            Assert.IsNotNull(latestCard);
            Assert.AreEqual("Instances",
                BindingOperations.GetBinding(latestGrid, ItemsControl.ItemsSourceProperty)!.Path.Path);
            Assert.AreEqual(nameof(MainViewModel.ClearLatestRunResultCommand),
                BindingOperations.GetBinding(clearButton, Button.CommandProperty)!.Path.Path);
            Assert.IsTrue(FindLogicalDescendants<TextBlock>(recentPanel)
                .Any(text => Equals(text.Text, "Chưa có lần chạy nào hoàn tất trong phiên này.")));
            Assert.IsFalse(FindLogicalDescendants<ItemsControl>(runPanel).Any(control =>
                BindingOperations.GetBinding(control, ItemsControl.ItemsSourceProperty)?.Path.Path == "ExecutionLog"),
                "Run Control must not keep a full-log panel permanently in its visual tree.");
            Assert.AreEqual(1, FindLogicalDescendants<Button>(runPanel)
                .Count(button => Equals(button.Content, "Dừng tất cả")),
                "Run Control must expose one non-duplicated stop-all action.");
        }
        finally
        {
            window.Close();
        }
    }

    [STATestMethod]
    public void RunControlPanel_UsesFlatActiveGridAndControlCenterScriptSelection()
    {
        if (Application.Current is null)
        {
            var application = new MEmuScriptStudio.App.App();
            application.InitializeComponent();
        }

        var panel = new RunControlPanel();
        var assignSelected = (Button)panel.FindName("AssignScriptToSelectedButton");
        var assignAll = (Button)panel.FindName("AssignSelectedScriptToAllButton");
        var runSelected = (Button)panel.FindName("RunSelectedButton");
        var runAll = (Button)panel.FindName("RunAllRemainingButton");
        var activeGrid = (DataGrid)panel.FindName("ActiveInstancesGrid");
        var instanceStopColumn = activeGrid.Columns.OfType<DataGridTemplateColumn>()
            .Single(column => Equals(column.Header, "Dừng"));
        var instanceStop = (Button)instanceStopColumn.CellTemplate.LoadContent();
        var controlCenterScript = (ComboBox)panel.FindName("ControlCenterScriptComboBox");

        Assert.AreEqual("Gán cho thiết bị đã chọn", assignSelected.Content);
        Assert.AreEqual("Gán cho tất cả thiết bị", assignAll.Content);
        Assert.AreSame(Application.Current!.FindResource("ToolbarButtonStyle"), assignSelected.Style);
        Assert.AreSame(Application.Current.FindResource("ToolbarButtonStyle"), assignAll.Style);
        Assert.AreSame(Application.Current.FindResource("PrimaryButtonStyle"), runSelected.Style);
        Assert.AreSame(Application.Current.FindResource("SecondaryButtonStyle"), runAll.Style);
        Assert.AreEqual(nameof(MainViewModel.ControlCenterSelectedScript),
            BindingOperations.GetBinding(controlCenterScript, Selector.SelectedItemProperty)!.Path.Path);
        Assert.IsTrue(VirtualizingPanel.GetIsVirtualizing(activeGrid));
        Assert.AreEqual(VirtualizationMode.Recycling, VirtualizingPanel.GetVirtualizationMode(activeGrid));
        Assert.IsTrue(ScrollViewer.GetCanContentScroll(activeGrid));
        Assert.IsTrue(activeGrid.EnableRowVirtualization);
        Assert.IsTrue(activeGrid.EnableColumnVirtualization);
        CollectionAssert.AreEqual(
            new[] { DataGridLengthUnitType.Auto, DataGridLengthUnitType.Auto, DataGridLengthUnitType.Auto, DataGridLengthUnitType.Star,
                DataGridLengthUnitType.Star, DataGridLengthUnitType.Star, DataGridLengthUnitType.Auto,
                DataGridLengthUnitType.Star, DataGridLengthUnitType.Auto },
            activeGrid.Columns.Select(column => column.Width.UnitType).ToArray());
        Assert.AreEqual(2, activeGrid.Columns[7].Width.Value,
            "The message column receives the largest share of flexible runtime space.");
        CollectionAssert.AreEqual(
            new[] { "Chọn", "Nguồn", "ID / Serial", "Tên thiết bị", "Kịch bản", "Bước hiện tại", "Trạng thái", "Thông báo", "Dừng" },
            activeGrid.Columns.Select(column => column.Header?.ToString()).ToArray());
        CollectionAssert.AreEquivalent(
            new[] { "Thiết lập chạy", "Khoảng cách khởi chạy trong nhóm" },
            FindLogicalDescendants<Expander>(panel).Select(expander => expander.Header?.ToString()).ToArray(),
            "Only compact run-configuration sections may use expanders; active instances remain one flat table.");
        Assert.IsNull(panel.FindName("ActiveLaunchGroupsList"));
        Assert.IsNull(panel.TryFindResource("ActiveGroupDetailsTemplate"));
        Assert.AreEqual(nameof(InstanceRunItemViewModel.StopCommand),
            BindingOperations.GetBinding(instanceStop, Button.CommandProperty)!.Path.Path);
        var stopSelected = (Button)panel.FindName("StopSelectedActiveButton");
        Assert.AreEqual(nameof(MainViewModel.StopSelectedActiveInstancesCommand),
            BindingOperations.GetBinding(stopSelected, Button.CommandProperty)!.Path.Path);
        var stopAllButtons = FindLogicalDescendants<Button>(panel)
            .Where(button => Equals(button.Content, "Dừng tất cả"))
            .ToList();
        Assert.AreEqual(1, stopAllButtons.Count, "The stop-all command must not be duplicated in the same Control Center surface.");
        Assert.AreEqual(nameof(MainViewModel.StopCommand),
            BindingOperations.GetBinding(stopAllButtons.Single(), Button.CommandProperty)?.Path.Path);
    }

    [STATestMethod]
    public void WpfDesignSystem_ProvidesAllRequiredNamedStylesAndReadablePrimaryText()
    {
        if (Application.Current is null)
        {
            var application = new MEmuScriptStudio.App.App();
            application.InitializeComponent();
        }
        var required = new[]
        {
            "PrimaryButtonStyle", "SecondaryButtonStyle", "DangerButtonStyle", "ToolbarButtonStyle",
            "DataGridStyle", "TabStyle", "StatusBadgeStyle", "GroupCardStyle"
        };
        var currentApplication = Application.Current!;
        foreach (var key in required) Assert.IsInstanceOfType<Style>(currentApplication.FindResource(key));
        Assert.IsNull(currentApplication.TryFindResource("ConsoleBrush"));
        Assert.IsNull(typeof(MainViewModel).Assembly.GetType("MEmuScriptStudio.App.Converters.RunningStateConverter"));

        Assert.IsFalse(currentApplication.Resources.MergedDictionaries
            .Any(dictionary => dictionary.Source?.OriginalString.Contains("Dark", StringComparison.OrdinalIgnoreCase) == true));
        var dataGridStyle = (Style)currentApplication.FindResource("DataGridStyle");
        Assert.AreEqual(0,
            dataGridStyle.Setters.Cast<Setter>().Single(setter => setter.Property == ItemsControl.AlternationCountProperty).Value);
        var scrollBarStyle = (Style)currentApplication.FindResource(typeof(ScrollBar));
        Assert.IsFalse(scrollBarStyle.Setters.Cast<Setter>().Any(setter => setter.Property == Control.TemplateProperty),
            "Scrollbars must keep the native template, including RepeatButton arrow controls.");
        Assert.IsTrue(scrollBarStyle.Triggers.OfType<Trigger>().Any(trigger =>
            trigger.Property == ScrollBar.OrientationProperty && Equals(trigger.Value, Orientation.Vertical)));

        var primary = (Style)currentApplication.FindResource("PrimaryButtonStyle");
        var foreground = (SolidColorBrush)primary.Setters.Cast<Setter>().Single(item => item.Property == Control.ForegroundProperty).Value;
        Assert.AreEqual(Color.FromRgb(0x08, 0x35, 0x4A), foreground.Color);
        Assert.AreNotEqual(Colors.White, foreground.Color);
    }

    [STATestMethod]
    public void AsyncCommandFailure_IsContainedAndDoesNotChangeApplicationShutdownMode()
    {
        if (Application.Current is null)
        {
            var application = new MEmuScriptStudio.App.App();
            application.InitializeComponent();
        }
        var applicationInstance = Application.Current!;
        Exception? reported = null;
        var command = new AsyncCommand(
            () => Task.FromException(new InvalidOperationException("expected command failure")),
            onError: exception => reported = exception);

        command.ExecuteAsync().GetAwaiter().GetResult();

        Assert.IsInstanceOfType<InvalidOperationException>(reported);
        Assert.AreSame(applicationInstance, Application.Current);
        Assert.IsFalse(applicationInstance.Dispatcher.HasShutdownStarted);
        Assert.IsTrue(command.CanExecute(null));
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
        var stopping = viewModel.ActiveInstanceRuns.Single();
        var observedStopRequested = false;
        stopping.PropertyChanged += (_, _) => observedStopRequested |=
            stopping.IsStopRequested && !stopping.CanStop && stopping.StatusText == "Đang dừng…";
        viewModel.StopCommand.Execute(null);
        Assert.IsTrue(observedStopRequested);
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
        var stopping = viewModel.InstanceRuns.Single(item => item.Index == 1);
        var observedStopRequested = false;
        stopping.PropertyChanged += (_, _) => observedStopRequested |=
            stopping.IsStopRequested && !stopping.CanStop && stopping.StatusText == "Đang dừng…";
        stopping.StopCommand.Execute(null);
        Assert.IsTrue(observedStopRequested);
        stopping.StopCommand.Execute(null);
        await engine.WaitForCancellationAsync(1);
        engine.Complete(2);
        await runTask.WaitAsync(TimeSpan.FromSeconds(2));
        await WaitUntilAsync(() => !viewModel.IsExecuting);

        Assert.AreEqual(1, viewModel.LatestRunResult!.SucceededCount);
        Assert.AreEqual(1, viewModel.LatestRunResult.CancelledCount);
        Assert.AreEqual(0, viewModel.LatestRunResult.FailedCount);
        Assert.AreEqual(1, viewModel.LatestRunResult.IssueInstances.Single().Index);
        Assert.AreEqual(InstanceExecutionStatus.Cancelled, viewModel.LatestRunResult.IssueInstances.Single().Status);
        CollectionAssert.AreEqual(new[] { 1 }, engine.CancelledIndices.Order().ToArray());
    }

    [TestMethod]
    public async Task StopSelectedActiveInstances_CancelsOnlySelectedRows()
    {
        var targets = Enumerable.Range(1, 3)
            .Select(index => new MemuInstance(index, $"VM {index}", true, 100 + index))
            .ToArray();
        var engine = new PerInstanceBlockingEngine([1, 2, 3]);
        var viewModel = CreateViewModel(
            new RecordingScriptStore(), engine, instanceService: new FixedInstanceService(targets));
        await viewModel.InitializeAsync(CancellationToken.None);
        await viewModel.RefreshCommand.ExecuteAsync();
        foreach (var target in viewModel.RunTargets) target.IsSelected = true;
        var runTask = viewModel.RunCommand.ExecuteAsync();
        await engine.WaitForStartsAsync(3);

        viewModel.ActiveInstanceRuns.Single(item => item.Index == 1).IsSelected = true;
        viewModel.ActiveInstanceRuns.Single(item => item.Index == 3).IsSelected = true;
        var stopTransitions = viewModel.ActiveInstanceRuns
            .Where(item => item.Index is 1 or 3)
            .ToDictionary(item => item.Index, _ => false);
        foreach (var item in viewModel.ActiveInstanceRuns.Where(item => item.Index is 1 or 3))
        {
            item.PropertyChanged += (_, _) => stopTransitions[item.Index] |=
                item.IsStopRequested && !item.CanStop && item.StatusText == "Đang dừng…";
        }
        Assert.IsTrue(viewModel.StopSelectedActiveInstancesCommand.CanExecute(null));
        viewModel.StopSelectedActiveInstancesCommand.Execute(null);
        Assert.IsTrue(stopTransitions.Values.All(value => value));
        Assert.IsFalse(viewModel.StopSelectedActiveInstancesCommand.CanExecute(null));
        await engine.WaitForCancellationAsync(2);
        engine.Complete(2);
        await runTask.WaitAsync(TimeSpan.FromSeconds(2));
        await WaitUntilAsync(() => !viewModel.IsExecuting);

        CollectionAssert.AreEquivalent(new[] { 1, 3 }, engine.CancelledIndices.ToArray());
        Assert.AreEqual(1, viewModel.LatestRunResult!.SucceededCount);
        Assert.AreEqual(2, viewModel.LatestRunResult.CancelledCount);
        Assert.AreEqual(0, viewModel.ActiveInstanceRuns.Count);
    }

    [TestMethod]
    public void InstanceRunItem_StopRequestedSurvivesLateRunningUpdateAndTerminalUpdateReplacesIt()
    {
        var script = new ScriptDefinition { Name = "Script", Steps = [new NoteStep { Name = "Step" }] };
        var groupId = Guid.NewGuid();
        var item = new InstanceRunItemViewModel(
            groupId, new MemuInstance(8, "Eight", true, 108), script, (_, _) => true);

        Assert.IsTrue(item.RequestStop());
        Assert.IsFalse(item.RequestStop());
        item.Apply(new InstanceExecutionUpdate(
            groupId, item.Index, item.Name, InstanceExecutionStatus.Running, ScriptId: script.Id));

        Assert.IsTrue(item.IsStopRequested);
        Assert.AreEqual("Đang dừng…", item.StatusText);
        Assert.IsFalse(item.CanStop);

        item.Apply(new InstanceExecutionUpdate(
            groupId, item.Index, item.Name, InstanceExecutionStatus.Cancelled,
            Message: "Đã dừng theo yêu cầu.", ScriptId: script.Id));

        Assert.IsFalse(item.IsStopRequested);
        Assert.AreEqual("Đã hủy", item.StatusText);
        Assert.AreEqual("Đã dừng theo yêu cầu.", item.MessageText);
    }

    [TestMethod]
    public async Task StopKeepsInstanceReservedUntilCancellationCleanupFinishesThenAllowsRerun()
    {
        var target = new MemuInstance(6, "Six", true, 106);
        var engine = new CancellationCleanupEngine();
        var viewModel = CreateViewModel(
            new RecordingScriptStore(), engine, instanceService: new FixedInstanceService([target]));
        await viewModel.InitializeAsync(CancellationToken.None);
        await viewModel.RefreshCommand.ExecuteAsync();
        viewModel.RunTargets.Single().IsSelected = true;
        await viewModel.RunCommand.ExecuteAsync();
        await engine.FirstStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        viewModel.ActiveInstanceRuns.Single().StopCommand.Execute(null);
        await engine.CancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var reserved = viewModel.RunTargets.Single();
        Assert.IsTrue(reserved.IsActive);
        Assert.IsFalse(reserved.CanSelectForRun);
        Assert.IsTrue(viewModel.IsExecuting);
        reserved.IsSelected = true;
        Assert.IsFalse(reserved.IsSelected);
        await viewModel.RunCommand.ExecuteAsync();
        Assert.AreEqual(1, engine.InvocationCount,
            "A rerun must not be admitted while the cancelled execution is still cleaning up.");

        engine.ReleaseCleanup.TrySetResult();
        await WaitUntilAsync(() => !viewModel.IsExecuting);
        Assert.IsFalse(reserved.IsActive);
        Assert.IsTrue(reserved.CanSelectForRun);

        reserved.IsSelected = true;
        await viewModel.RunCommand.ExecuteAsync();
        await WaitUntilAsync(() => !viewModel.IsExecuting);
        Assert.AreEqual(2, engine.InvocationCount);
    }

    [TestMethod]
    public async Task StopAllKeepsEveryInstanceReservedUntilCancellationCleanupFinishes()
    {
        var targets = new[]
        {
            new MemuInstance(11, "Eleven", true, 111),
            new MemuInstance(12, "Twelve", true, 112)
        };
        var engine = new StopAllCleanupEngine();
        var viewModel = CreateViewModel(
            new RecordingScriptStore(), engine, instanceService: new FixedInstanceService(targets));
        await viewModel.InitializeAsync(CancellationToken.None);
        await viewModel.RefreshCommand.ExecuteAsync();
        foreach (var target in viewModel.RunTargets) target.IsSelected = true;
        await viewModel.RunCommand.ExecuteAsync();
        await engine.WaitForStartsAsync(2);

        viewModel.StopCommand.Execute(null);
        await engine.WaitForCancellationsAsync(2);
        await Task.Delay(650);

        Assert.IsTrue(viewModel.IsExecuting);
        Assert.AreEqual(2, viewModel.ActiveInstanceRuns.Count);
        Assert.IsTrue(viewModel.ActiveInstanceRuns.All(item => item.IsStopRequested && !item.CanStop));
        Assert.IsTrue(viewModel.RunTargets.All(item => item.IsActive && !item.CanSelectForRun));
        await viewModel.RunCommand.ExecuteAsync();
        Assert.AreEqual(2, engine.InvocationCount,
            "Stop-all must not release either reservation while cancelled executions are still cleaning up.");

        engine.ReleaseCleanup.TrySetResult();
        await WaitUntilAsync(() => !viewModel.IsExecuting);

        Assert.AreEqual(0, viewModel.ActiveInstanceRuns.Count);
        Assert.IsTrue(viewModel.RunTargets.All(item => !item.IsActive && item.CanSelectForRun));
    }

    [TestMethod]
    public async Task MainWindowClose_CloseWhileExecutionActive_UsesStopAllAndApprovesCloseAfterTerminal()
    {
        var engine = new BlockingEngine();
        var viewModel = await CreateRunningViewModelAsync(engine);
        var coordinator = new MainWindowCloseCoordinator();

        Assert.IsTrue(coordinator.RequiresDeferral(viewModel, hasControlCenter: false));
        Assert.IsTrue(await coordinator.TryResolveAsync(viewModel, () => Task.CompletedTask)
            .WaitAsync(TimeSpan.FromSeconds(2)));

        Assert.IsTrue(engine.WasCancelled);
        Assert.IsFalse(viewModel.IsExecuting);
        Assert.AreEqual(0, viewModel.ActiveLaunchGroupCount);
        Assert.IsTrue(coordinator.IsCloseApproved);
    }

    [TestMethod]
    public async Task MainWindowClose_CloseWhileSafeStopCleanupWaits_KeepsReservationAndClosePending()
    {
        var engine = new CancellationCleanupEngine();
        var viewModel = await CreateRunningViewModelAsync(engine);
        var coordinator = new MainWindowCloseCoordinator();
        var closeTask = coordinator.TryResolveAsync(viewModel, () => Task.CompletedTask);

        try
        {
            await engine.CancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(2));

            Assert.IsFalse(closeTask.IsCompleted);
            Assert.IsFalse(coordinator.IsCloseApproved);
            Assert.IsTrue(viewModel.IsExecuting);
            Assert.IsTrue(viewModel.RunTargets.Single().IsActive);
            Assert.IsFalse(viewModel.RunTargets.Single().CanSelectForRun);
            Assert.IsTrue(viewModel.ActiveInstanceRuns.Single().IsStopRequested);
            StringAssert.Contains(viewModel.StatusMessage, "Đang dừng");
        }
        finally
        {
            engine.ReleaseCleanup.TrySetResult();
        }

        Assert.IsTrue(await closeTask.WaitAsync(TimeSpan.FromSeconds(2)));
    }

    [TestMethod]
    public async Task MainWindowClose_RepeatedCloseDuringSafeStop_IsIdempotent()
    {
        var engine = new CancellationCleanupEngine();
        var viewModel = await CreateRunningViewModelAsync(engine);
        var coordinator = new MainWindowCloseCoordinator();
        var controlCenterCloseCount = 0;
        var controlCenterCloseStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseControlCenterClose = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstClose = coordinator.TryResolveAsync(viewModel, async () =>
        {
            controlCenterCloseCount++;
            controlCenterCloseStarted.TrySetResult();
            await releaseControlCenterClose.Task;
        });

        try
        {
            await engine.CancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(2));
            engine.ReleaseCleanup.TrySetResult();
            await controlCenterCloseStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.IsFalse(viewModel.IsExecuting);
            Assert.IsTrue(coordinator.RequiresDeferral(viewModel, hasControlCenter: false));
            var repeatedClose = await coordinator.TryResolveAsync(viewModel, () =>
            {
                controlCenterCloseCount++;
                return Task.CompletedTask;
            });

            Assert.IsFalse(repeatedClose);
            Assert.AreEqual(1, controlCenterCloseCount);
            Assert.AreEqual(1, engine.InvocationCount);
            Assert.IsFalse(firstClose.IsCompleted);
        }
        finally
        {
            engine.ReleaseCleanup.TrySetResult();
            releaseControlCenterClose.TrySetResult();
        }

        Assert.IsTrue(await firstClose.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.AreEqual(1, controlCenterCloseCount);
    }

    [TestMethod]
    public async Task MainWindowClose_CloseWhileIdle_DoesNotDeferExistingCloseBehavior()
    {
        var viewModel = CreateViewModel(new RecordingScriptStore(), new ImmediateEngine());
        await viewModel.InitializeAsync(CancellationToken.None);
        var coordinator = new MainWindowCloseCoordinator();

        Assert.IsFalse(coordinator.RequiresDeferral(viewModel, hasControlCenter: false));
        Assert.IsFalse(coordinator.IsResolutionInProgress);
        Assert.IsFalse(coordinator.IsCloseApproved);
    }

    [TestMethod]
    public async Task MainWindowClose_CloseApprovalOccursOnlyAfterSessionTerminalAndReservationRelease()
    {
        var engine = new CancellationCleanupEngine();
        var viewModel = await CreateRunningViewModelAsync(engine);
        var coordinator = new MainWindowCloseCoordinator();
        bool? terminalAtClose = null;
        var closeTask = coordinator.TryResolveAsync(viewModel, () =>
        {
            terminalAtClose =
            !viewModel.IsExecuting &&
            viewModel.ActiveLaunchGroupCount == 0 &&
            viewModel.ActiveInstanceRuns.Count == 0 &&
            viewModel.RunTargets.All(item => !item.IsActive && item.CanSelectForRun);
            return Task.CompletedTask;
        });

        await engine.CancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.IsNull(terminalAtClose);
        Assert.IsFalse(closeTask.IsCompleted);
        Assert.IsFalse(viewModel.RunCommand.CanExecute(null),
            "Safe shutdown must reject new execution admission while cleanup is pending.");

        engine.ReleaseCleanup.TrySetResult();
        Assert.IsTrue(await closeTask.WaitAsync(TimeSpan.FromSeconds(2)));

        Assert.AreEqual(true, terminalAtClose);
        Assert.IsFalse(viewModel.RunCommand.CanExecute(null),
            "Safe shutdown must keep new execution admission closed through the final window close.");
    }

    [TestMethod]
    public async Task MainWindowClose_DraftCreatedDuringCleanup_IsResolvedBeforeCloseApproval()
    {
        var decisions = new DraftDecisionConfirmation(EditorDraftDecision.Cancel);
        var engine = new CancellationCleanupEngine();
        var viewModel = await CreateRunningViewModelAsync(engine, decisions);
        var coordinator = new MainWindowCloseCoordinator();
        var closeTask = coordinator.TryResolveAsync(viewModel, () => Task.CompletedTask);

        await engine.CancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var originalEditorName = viewModel.EditorName;
        viewModel.EditorName = "Draft created while stopping";
        Assert.IsTrue(viewModel.HasPendingNavigationDraft);

        engine.ReleaseCleanup.TrySetResult();
        Assert.IsFalse(await closeTask.WaitAsync(TimeSpan.FromSeconds(2)));

        Assert.IsFalse(coordinator.IsCloseApproved);
        Assert.AreEqual(1, decisions.Calls.Count);
        Assert.IsTrue(viewModel.HasPendingNavigationDraft);
        viewModel.EditorName = originalEditorName;
        Assert.IsFalse(viewModel.HasPendingNavigationDraft);
        viewModel.RunTargets.Single().IsSelected = true;
        Assert.IsTrue(viewModel.RunCommand.CanExecute(null),
            "Cancelling the final draft decision must reopen admission after safe-stop cleanup is terminal.");
    }

    [TestMethod]
    public async Task ActiveExecutionAllowsRegularEditorMutationWhileAdmittedSnapshotStaysFrozen()
    {
        var source = CreateThreeStepScript();
        var store = new RecordingScriptStore([source]);
        var engine = new PerInstanceBlockingEngine([1]);
        var viewModel = CreateViewModel(
            store,
            engine,
            instanceService: new FixedInstanceService([new MemuInstance(1, "One", true, 101)]));
        await viewModel.InitializeAsync(CancellationToken.None);
        await viewModel.RefreshCommand.ExecuteAsync();
        viewModel.RunTargets.Single().IsSelected = true;
        await viewModel.RunCommand.ExecuteAsync();
        await engine.WaitForStartsAsync(1);

        Assert.IsTrue(viewModel.IsExecuting);
        Assert.IsFalse(viewModel.SaveStepCommand.CanExecute(null));
        Assert.IsTrue(viewModel.CreateScriptCommand.CanExecute(null));
        viewModel.EditorName = "Edited while active";
        Assert.IsTrue(viewModel.SaveStepCommand.CanExecute(null));
        await viewModel.SaveStepCommand.ExecuteAsync();

        Assert.AreEqual("Edited while active", viewModel.Steps[0].Name);
        Assert.AreEqual("A", engine.Requests[1].Script.Steps[0].Name);
        Assert.AreNotSame(viewModel.SelectedScript!.Model, engine.Requests[1].Script);
        engine.Complete(1);
        await WaitUntilAsync(() => !viewModel.IsExecuting);
    }

    [TestMethod]
    public async Task ActiveExecutionAllowsCompositeMutationWhileAdmittedGraphStaysFrozen()
    {
        var child = new ScriptDefinition { Name = "Child", Steps = [new NoteStep { Name = "N" }] };
        var composite = new ScriptDefinition
        {
            Name = "Composite",
            Kind = ScriptKind.Composite,
            CompositeItems = [new ScriptReferenceItem { ScriptId = child.Id }]
        };
        var engine = new PerInstanceBlockingEngine([1]);
        var viewModel = CreateViewModel(
            new RecordingScriptStore([child, composite]),
            engine,
            instanceService: new FixedInstanceService([new MemuInstance(1, "One", true, 101)]));
        await viewModel.InitializeAsync(CancellationToken.None);
        await viewModel.RefreshCommand.ExecuteAsync();
        viewModel.CommonRunScript = viewModel.Scripts.Single(script => script.Id == composite.Id);
        viewModel.SelectedScript = viewModel.CommonRunScript;
        viewModel.RunTargets.Single().IsSelected = true;
        await viewModel.RunCommand.ExecuteAsync();
        await engine.WaitForStartsAsync(1);

        Assert.IsTrue(viewModel.IsExecuting);
        Assert.IsTrue(viewModel.AddCompositeDelayCommand.CanExecute(null));
        await viewModel.AddCompositeDelayCommand.ExecuteAsync();

        Assert.AreEqual(2, viewModel.CompositeItems.Count);
        Assert.AreEqual(1, engine.Requests[1].Script.CompositeItems.Count);
        Assert.AreEqual(1, engine.Requests[1].ScriptLibrary[composite.Id].CompositeItems.Count);
        engine.Complete(1);
        await WaitUntilAsync(() => !viewModel.IsExecuting);
    }

    [TestMethod]
    public async Task OneLaunchBuildsIndependentExecutionGraphsFromOneFrozenLibrarySnapshot()
    {
        var child = new ScriptDefinition { Name = "Child", Steps = [new NoteStep { Name = "Original child" }] };
        var composite = new ScriptDefinition
        {
            Name = "Composite",
            Kind = ScriptKind.Composite,
            CompositeItems = [new ScriptReferenceItem { ScriptId = child.Id }]
        };
        var engine = new PerInstanceBlockingEngine([1, 2]);
        var viewModel = CreateViewModel(
            new RecordingScriptStore([child, composite]),
            engine,
            instanceService: new FixedInstanceService(
            [
                new MemuInstance(1, "One", true, 101),
                new MemuInstance(2, "Two", true, 102)
            ]));
        await viewModel.InitializeAsync(CancellationToken.None);
        await viewModel.RefreshCommand.ExecuteAsync();
        viewModel.CommonRunScript = viewModel.Scripts.Single(script => script.Id == composite.Id);
        foreach (var target in viewModel.RunTargets) target.IsSelected = true;

        await viewModel.RunCommand.ExecuteAsync();
        await engine.WaitForStartsAsync(2);

        var firstRequest = engine.Requests[1];
        var secondRequest = engine.Requests[2];
        Assert.AreNotSame(firstRequest, secondRequest);
        Assert.AreNotSame(firstRequest.ScriptLibrary, secondRequest.ScriptLibrary,
            "Mutable execution graphs must not be shared across instances.");
        Assert.AreNotSame(firstRequest.Script, secondRequest.Script);
        Assert.AreSame(firstRequest.Script, firstRequest.ScriptLibrary[composite.Id]);
        Assert.AreSame(secondRequest.Script, secondRequest.ScriptLibrary[composite.Id]);
        Assert.AreNotSame(composite, firstRequest.Script);
        Assert.AreNotSame(child, firstRequest.ScriptLibrary[child.Id]);
        Assert.AreNotSame(firstRequest.ScriptLibrary[child.Id], secondRequest.ScriptLibrary[child.Id]);

        viewModel.Scripts.Single(script => script.Id == child.Id).Model.Steps[0].Name = "Edited after launch";
        Assert.AreEqual("Original child", firstRequest.ScriptLibrary[child.Id].Steps[0].Name);
        Assert.AreEqual("Original child", secondRequest.ScriptLibrary[child.Id].Steps[0].Name);

        engine.Complete(1);
        engine.Complete(2);
        await WaitUntilAsync(() => viewModel.LatestRunResult is not null);

        Assert.AreEqual(2, viewModel.LatestRunResult!.Instances.Count);
        Assert.AreNotSame(viewModel.LatestRunResult.Instances[0], viewModel.LatestRunResult.Instances[1]);
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
        Assert.IsTrue(viewModel.ActiveLaunchGroups.All(group => !group.IsExpanded),
            "Every active group card must start collapsed.");
        var expandedGroup = viewModel.ActiveLaunchGroups[0];
        var otherGroup = viewModel.ActiveLaunchGroups[1];
        expandedGroup.IsExpanded = true;
        Assert.IsTrue(expandedGroup.IsExpanded);
        Assert.IsFalse(otherGroup.IsExpanded, "Expanding one active group must not expand another group.");

        viewModel.RunTargets.Single(item => item.Index == 1).IsSelected = true;
        Assert.IsFalse(viewModel.RunTargets.Single(item => item.Index == 1).IsSelected,
            "An active instance must not be selectable for another run.");
        await viewModel.RunCommand.ExecuteAsync();
        Assert.AreEqual(2, viewModel.InstanceRuns.Count);

        var groupA = viewModel.InstanceRuns.Single(item => item.Index == 1).LaunchGroupId;
        viewModel.StopGroupCommand.Execute(groupA);
        await engine.WaitForCancellationAsync(1);
        await WaitUntilAsync(() => viewModel.ActiveLaunchGroupCount == 1);
        Assert.IsTrue(viewModel.IsExecuting);
        viewModel.StopCommand.Execute(null);
        await engine.WaitForCancellationAsync(2);
        await WaitUntilAsync(() => !viewModel.IsExecuting);
    }

    [TestMethod]
    public async Task StopGroupCommand_CancelsOnlyItsExactGroupWhenTwoGroupsHaveMultipleActiveInstances()
    {
        var targets = Enumerable.Range(1, 4)
            .Select(index => new MemuInstance(index, $"VM {index}", true, 100 + index))
            .ToArray();
        var engine = new PerInstanceBlockingEngine([1, 2, 3, 4]);
        var viewModel = CreateViewModel(
            new RecordingScriptStore(), engine, instanceService: new FixedInstanceService(targets));
        await viewModel.InitializeAsync(CancellationToken.None);
        await viewModel.RefreshCommand.ExecuteAsync();

        foreach (var target in viewModel.RunTargets.Where(item => item.Index is 1 or 2)) target.IsSelected = true;
        await viewModel.RunCommand.ExecuteAsync();
        await engine.WaitForStartsAsync(2);
        var groupAItem = viewModel.ActiveLaunchGroups.Single();
        var groupA = groupAItem.LaunchGroupId;

        foreach (var target in viewModel.RunTargets.Where(item => item.Index is 3 or 4)) target.IsSelected = true;
        await viewModel.RunCommand.ExecuteAsync();
        await engine.WaitForStartsAsync(4);
        var groupB = viewModel.ActiveLaunchGroups.Single(item => item.LaunchGroupId != groupA).LaunchGroupId;

        viewModel.StopGroupCommand.Execute(groupA);
        await engine.WaitForCancellationAsync(2);
        await WaitUntilAsync(() => viewModel.ActiveLaunchGroups.All(item => item.LaunchGroupId != groupA));

        CollectionAssert.AreEquivalent(new[] { 1, 2 }, engine.CancelledIndices.ToArray());
        Assert.AreEqual(groupA, viewModel.LatestRunResult!.LaunchGroupId);
        Assert.IsFalse(groupAItem.HasInstanceStateSubscriptions);
        Assert.IsTrue(viewModel.ActiveLaunchGroups.Any(item => item.LaunchGroupId == groupB));
        Assert.IsTrue(viewModel.InstanceRuns.Where(item => item.LaunchGroupId == groupB)
            .All(item => item.Status is InstanceExecutionStatus.Running or InstanceExecutionStatus.Queued));
        engine.Complete(3);
        engine.Complete(4);
        await WaitUntilAsync(() => !viewModel.IsExecuting);
        CollectionAssert.AreEquivalent(new[] { 1, 2 }, engine.CancelledIndices.ToArray());
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
        Assert.AreEqual(0, rerun.InstanceRuns.Count);
        Assert.AreEqual("Nhóm 03", rerun.LatestRunResult!.GroupName);
    }

    [TestMethod]
    public async Task RunAllRemainingLabelUsesTheCommandCandidateSetAcrossLifecycleAndRefresh()
    {
        var instances = new MutableInstanceService(
        [
            new MemuInstance(1, "One", true, 101),
            new MemuInstance(2, "Two", true, 102),
            new MemuInstance(3, "Three", true, 103)
        ]);
        var engine = new PerInstanceBlockingEngine([1, 2, 3]);
        var viewModel = CreateViewModel(new RecordingScriptStore(), engine, instanceService: instances);
        await viewModel.InitializeAsync(CancellationToken.None);
        await viewModel.RefreshCommand.ExecuteAsync();

        Assert.AreEqual(3, viewModel.RemainingRunTargetCount);
        Assert.AreEqual("Chạy 3 thiết bị chưa chạy", viewModel.RunAllRemainingLabel);

        viewModel.RunTargets.Single(item => item.Index == 1).IsSelected = true;
        await viewModel.RunCommand.ExecuteAsync();
        await engine.WaitForStartsAsync(1);
        Assert.AreEqual(2, viewModel.RemainingRunTargetCount);
        Assert.AreEqual("Chạy 2 thiết bị chưa chạy", viewModel.RunAllRemainingLabel);

        await viewModel.RunAllRemainingCommand.ExecuteAsync();
        await engine.WaitForStartsAsync(3);
        Assert.AreEqual(0, viewModel.RemainingRunTargetCount);
        Assert.AreEqual("Chạy 0 thiết bị chưa chạy", viewModel.RunAllRemainingLabel);

        engine.Complete(1);
        engine.Complete(2);
        engine.Complete(3);
        await WaitUntilAsync(() => !viewModel.IsExecuting);
        Assert.AreEqual(3, viewModel.RemainingRunTargetCount,
            "A completed universe starts a new remaining-run session with the same command semantics.");

        instances.Instances = [new MemuInstance(2, "Two", true, 102)];
        await viewModel.RefreshCommand.ExecuteAsync();
        Assert.AreEqual(1, viewModel.RemainingRunTargetCount);
        Assert.AreEqual("Chạy 1 thiết bị chưa chạy", viewModel.RunAllRemainingLabel);
    }

    [TestMethod]
    public async Task RunAllRemainingDefersTargetsDiscoveredAfterTheCurrentSessionStarted()
    {
        var instances = new MutableInstanceService(
        [
            new MemuInstance(1, "One", true, 101),
            new MemuInstance(2, "Two", true, 102)
        ]);
        var engine = new PerInstanceBlockingEngine([1, 2, 3]);
        var viewModel = CreateViewModel(new RecordingScriptStore(), engine, instanceService: instances);
        await viewModel.InitializeAsync(CancellationToken.None);
        await viewModel.RefreshCommand.ExecuteAsync();

        viewModel.RunTargets.Single(item => item.Index == 1).IsSelected = true;
        await viewModel.RunCommand.ExecuteAsync();
        await engine.WaitForStartsAsync(1);

        instances.Instances =
        [
            new MemuInstance(1, "One", true, 101),
            new MemuInstance(2, "Two", true, 102),
            new MemuInstance(3, "Three", true, 103)
        ];
        await viewModel.RefreshCommand.ExecuteAsync();
        Assert.AreEqual(1, viewModel.RemainingRunTargetCount);
        Assert.AreEqual("Chạy 1 thiết bị chưa chạy", viewModel.RunAllRemainingLabel);

        await viewModel.RunAllRemainingCommand.ExecuteAsync();
        await engine.WaitForStartsAsync(2);
        CollectionAssert.AreEquivalent(new[] { 1, 2 }, engine.StartedIndices.ToArray());
        Assert.IsFalse(engine.StartedIndices.Contains(3));
        Assert.AreEqual(0, viewModel.RemainingRunTargetCount);

        engine.Complete(1);
        engine.Complete(2);
        await WaitUntilAsync(() => !viewModel.IsExecuting);
        Assert.AreEqual(3, viewModel.RemainingRunTargetCount,
            "A newly discovered target joins only the next remaining-run session.");
    }

    [TestMethod]
    public async Task TestStepRunsEditAndCreateDraftsThroughReservedTransientLifecycleWithoutPersistence()
    {
        var persistedStep = new TapStep { Name = "Persisted tap", X = 10, Y = 20 };
        var script = new ScriptDefinition { Name = "Script", Steps = [persistedStep] };
        var store = new RecordingScriptStore([script]);
        var engine = new PerInstanceBlockingEngine([7]);
        var viewModel = CreateViewModel(
            store,
            engine,
            instanceService: new FixedInstanceService([new MemuInstance(7, "Seven", true, 107)]));
        await viewModel.InitializeAsync(CancellationToken.None);
        await viewModel.RefreshCommand.ExecuteAsync();
        var saveCountBeforeTest = store.SaveCount;
        var assignedScriptBeforeTest = viewModel.RunTargets.Single().AssignedScriptId;

        viewModel.EditorName = "Draft tap";
        viewModel.EditorX = 111;
        viewModel.EditorY = 222;
        viewModel.HasEditorBindingErrors = true;
        Assert.IsFalse(viewModel.TestStepCommand.CanExecute(null));
        viewModel.HasEditorBindingErrors = false;
        viewModel.EditorIsEnabled = false;
        Assert.IsFalse(viewModel.TestStepCommand.CanExecute(null));
        viewModel.EditorIsEnabled = true;
        Assert.IsTrue(viewModel.TestStepCommand.CanExecute(null));
        await viewModel.TestStepCommand.ExecuteAsync();
        await engine.WaitForStartsAsync(1);

        var editRequest = engine.Requests[7];
        var editDraft = (TapStep)editRequest.Script.Steps.Single();
        Assert.AreEqual("Draft tap", editDraft.Name);
        Assert.AreEqual(111, editDraft.X);
        Assert.AreEqual(222, editDraft.Y);
        Assert.AreEqual("Persisted tap", persistedStep.Name);
        Assert.AreEqual(10, persistedStep.X);
        Assert.AreEqual(saveCountBeforeTest, store.SaveCount);
        Assert.AreEqual(assignedScriptBeforeTest, viewModel.RunTargets.Single().AssignedScriptId);
        Assert.IsTrue(viewModel.RunTargets.Single().IsActive);
        Assert.IsFalse(viewModel.TestStepCommand.CanExecute(null));
        StringAssert.Contains(viewModel.StatusMessage, "Đang chạy thử bước");
        Assert.AreEqual("Đang chạy…", viewModel.TestStepFeedback);

        engine.Complete(7);
        await WaitUntilAsync(() => !viewModel.IsExecuting);
        StringAssert.Contains(viewModel.StatusMessage, "thành công");
        Assert.AreEqual("Thành công", viewModel.TestStepFeedback);
        Assert.AreEqual(1, viewModel.RecentRuns.Count);

        viewModel.EditorName = persistedStep.Name;
        viewModel.EditorX = persistedStep.X;
        viewModel.EditorY = persistedStep.Y;
        await viewModel.NewStepCommand.ExecuteAsync();
        viewModel.EditorKind = ScriptStepKind.Tap;
        viewModel.EditorName = "Create draft";
        viewModel.EditorX = 333;
        viewModel.EditorY = 444;
        Assert.IsTrue(viewModel.TestStepCommand.CanExecute(null));

        await viewModel.TestStepCommand.ExecuteAsync();
        await engine.WaitForStartsAsync(2);
        await WaitUntilAsync(() => !viewModel.IsExecuting);
        var createDraft = (TapStep)engine.Requests[7].Script.Steps.Single();
        Assert.AreEqual("Create draft", createDraft.Name);
        Assert.AreEqual(333, createDraft.X);
        Assert.AreEqual(444, createDraft.Y);
        Assert.AreEqual(1, script.Steps.Count);
        Assert.AreEqual(saveCountBeforeTest, store.SaveCount);
        Assert.AreEqual(assignedScriptBeforeTest, viewModel.RunTargets.Single().AssignedScriptId);
    }

    [TestMethod]
    public async Task TestStepFeedbackTracksLatestOverlappingTestRun()
    {
        var script = new ScriptDefinition { Name = "Tap", Steps = [new TapStep { Name = "Tap", X = 1, Y = 2 }] };
        var engine = new PerInstanceBlockingEngine([7, 8]);
        var viewModel = CreateViewModel(
            new RecordingScriptStore([script]),
            engine,
            instanceService: new FixedInstanceService([
                new MemuInstance(7, "Seven", true, 107),
                new MemuInstance(8, "Eight", true, 108)]));
        await viewModel.InitializeAsync(CancellationToken.None);
        await viewModel.RefreshCommand.ExecuteAsync();

        viewModel.SelectedEditorTarget = viewModel.EditorTargets.Single(item => item.Identifier == "7");
        await viewModel.TestStepCommand.ExecuteAsync();
        await engine.WaitForStartsAsync(1);
        viewModel.SelectedEditorTarget = viewModel.EditorTargets.Single(item => item.Identifier == "8");
        await viewModel.TestStepCommand.ExecuteAsync();
        await engine.WaitForStartsAsync(2);
        Assert.AreEqual("Đang chạy…", viewModel.TestStepFeedback);

        engine.Complete(7);
        await WaitUntilAsync(() => viewModel.ActiveLaunchGroupCount == 1);
        Assert.AreEqual("Đang chạy…", viewModel.TestStepFeedback,
            "Completion from the older test run must not overwrite the latest run feedback.");

        engine.Complete(8);
        await WaitUntilAsync(() => !viewModel.IsExecuting);
        Assert.AreEqual("Thành công", viewModel.TestStepFeedback);
    }

    [TestMethod]
    public async Task TestStepUsesExactSelectedAndroidTargetAndSerialScopedPreview()
    {
        var script = new ScriptDefinition
        {
            Name = "Tap",
            Steps = [new TapStep { Name = "Tap", X = 1, Y = 2 }]
        };
        var devices = new MutableAndroidDeviceService(
        [
            new AndroidAdbDevice("SERIAL-A", "Maker", "A", "13", 33, 720, 1280, 320, 0, AndroidConnectionState.Device),
            new AndroidAdbDevice("SERIAL-B", "Maker", "B", "13", 33, 720, 1280, 320, 0, AndroidConnectionState.Device)
        ]);
        var engine = new ImmediateEngine();
        var store = new RecordingScriptStore([script]);
        var viewModel = CreateViewModel(
            store,
            engine,
            androidDeviceService: devices,
            androidStateProbe: devices,
            adbPathDiscovery: new ValidAdbPathDiscovery());
        await viewModel.InitializeAsync(CancellationToken.None);
        await viewModel.RefreshCommand.ExecuteAsync();
        viewModel.SelectedEditorTarget = viewModel.EditorTargets.Single(item => item.Identifier == "SERIAL-B");
        viewModel.EditorX = 55;
        viewModel.EditorY = 66;
        viewModel.RunTargets.Single(item => item.Identifier == "SERIAL-A").IsSelected = true;
        var saveCountBeforeTest = store.SaveCount;

        StringAssert.Contains(viewModel.CommandPreview, "-s SERIAL-B");
        Assert.IsFalse(viewModel.CommandPreview.Contains("SERIAL-A", StringComparison.Ordinal));
        await viewModel.TestStepCommand.ExecuteAsync();
        await WaitUntilAsync(() => !viewModel.IsExecuting && engine.LastRequest is not null);

        Assert.AreEqual("android-adb:SERIAL-B", engine.LastRequest!.Target.TargetKey);
        Assert.AreEqual("SERIAL-B", engine.LastRequest.Target.Identifier);
        Assert.AreEqual(@"C:\MEmu\adb.exe", engine.LastRequest.AdbPath);
        var draft = (TapStep)engine.LastRequest.Script.Steps.Single();
        Assert.AreEqual(55, draft.X);
        Assert.AreEqual(66, draft.Y);
        Assert.IsTrue(viewModel.RunTargets.Single(item => item.Identifier == "SERIAL-A").IsSelected,
            "The editor test target must not use or mutate Control Center selection.");
        Assert.AreEqual(saveCountBeforeTest, store.SaveCount);
    }

    [TestMethod]
    public async Task TestStepReportsFailedExecutionWithoutSavingDraft()
    {
        var script = new ScriptDefinition { Name = "Tap", Steps = [new TapStep { Name = "Tap", X = 1, Y = 2 }] };
        var store = new RecordingScriptStore([script]);
        var viewModel = CreateViewModel(
            store,
            new ReportingMultiEngine(failedIndex: 9),
            instanceService: new FixedInstanceService([new MemuInstance(9, "Nine", true, 109)]));
        await viewModel.InitializeAsync(CancellationToken.None);
        await viewModel.RefreshCommand.ExecuteAsync();
        var saveCountBeforeTest = store.SaveCount;
        viewModel.EditorName = "Failing draft";

        await viewModel.TestStepCommand.ExecuteAsync();
        await WaitUntilAsync(() => !viewModel.IsExecuting);

        StringAssert.Contains(viewModel.StatusMessage, "gặp lỗi");
        Assert.AreEqual("Lỗi", viewModel.TestStepFeedback);
        Assert.AreEqual(saveCountBeforeTest, store.SaveCount);
        Assert.AreEqual("Tap", script.Steps.Single().Name);
    }

    [TestMethod]
    public async Task TestStepRawShellRequiresConfirmationBeforeSchedulerAdmission()
    {
        var raw = new AndroidShellStep { Name = "Raw", Command = "echo ok" };
        var script = new ScriptDefinition { Name = "Raw script", Steps = [raw] };
        var store = new RecordingScriptStore([script]);
        var engine = new ImmediateEngine();
        var confirmation = new ConfigurableConfirmation(false);
        var viewModel = CreateViewModel(
            store,
            engine,
            confirmation,
            instanceService: new FixedInstanceService([new MemuInstance(4, "Four", true, 104)]));
        await viewModel.InitializeAsync(CancellationToken.None);
        await viewModel.RefreshCommand.ExecuteAsync();
        var saveCountBeforeTest = store.SaveCount;

        Assert.IsTrue(viewModel.TestStepCommand.CanExecute(null));
        await viewModel.TestStepCommand.ExecuteAsync();

        Assert.IsNull(engine.LastRequest);
        Assert.AreEqual(1, confirmation.CallCount);
        StringAssert.Contains(viewModel.StatusMessage, "chưa được xác nhận");
        Assert.AreEqual(saveCountBeforeTest, store.SaveCount);
        Assert.AreEqual(raw, script.Steps.Single());
    }

    [TestMethod]
    public async Task TestStepRechecksReservationAfterRawShellConfirmation()
    {
        var raw = new AndroidShellStep { Name = "Raw", Command = "echo ok" };
        var script = new ScriptDefinition { Name = "Raw script", Steps = [raw] };
        var engine = new PerInstanceBlockingEngine([4]);
        MainViewModel? viewModel = null;
        var confirmation = new ConfigurableConfirmation(
            true,
            () => viewModel!.RunCommand.ExecuteAsync().GetAwaiter().GetResult());
        viewModel = CreateViewModel(
            new RecordingScriptStore([script]),
            engine,
            confirmation,
            instanceService: new FixedInstanceService([new MemuInstance(4, "Four", true, 104)]));
        await viewModel.InitializeAsync(CancellationToken.None);
        await viewModel.RefreshCommand.ExecuteAsync();
        viewModel.RunTargets.Single().IsSelected = true;

        await viewModel.TestStepCommand.ExecuteAsync();
        await engine.WaitForStartsAsync(1);

        Assert.AreEqual(1, engine.StartedIndices.Count);
        Assert.AreEqual("Raw script", engine.Requests[4].Script.Name,
            "Only the Control Center launch admitted during confirmation may own the target.");
        Assert.AreEqual(1, viewModel.ActiveLaunchGroups.Count);
        StringAssert.Contains(viewModel.StatusMessage, "thiết bị đã được dùng");

        engine.Complete(4);
        await WaitUntilAsync(() => !viewModel.IsExecuting);
    }

    [TestMethod]
    public async Task TestStepStopCancelsTransientRunReleasesReservationAndDoesNotSaveDraft()
    {
        var persistedStep = new TapStep { Name = "Persisted", X = 1, Y = 2 };
        var script = new ScriptDefinition { Name = "Tap", Steps = [persistedStep] };
        var store = new RecordingScriptStore([script]);
        var engine = new PerInstanceBlockingEngine([5]);
        var viewModel = CreateViewModel(
            store,
            engine,
            instanceService: new FixedInstanceService([new MemuInstance(5, "Five", true, 105)]));
        await viewModel.InitializeAsync(CancellationToken.None);
        await viewModel.RefreshCommand.ExecuteAsync();
        var saveCountBeforeTest = store.SaveCount;
        viewModel.EditorName = "Unsaved draft";
        viewModel.EditorX = 50;

        await viewModel.TestStepCommand.ExecuteAsync();
        await engine.WaitForStartsAsync(1);
        viewModel.StopCommand.Execute(null);
        await engine.WaitForCancellationAsync(1);
        await WaitUntilAsync(() => viewModel.StatusMessage.Contains("Đã hủy chạy thử bước", StringComparison.Ordinal));

        StringAssert.Contains(viewModel.StatusMessage, "Đã hủy chạy thử bước");
        Assert.IsTrue(viewModel.TestStepCommand.CanExecute(null));
        Assert.IsFalse(viewModel.RunTargets.Single().IsActive);
        Assert.AreEqual(saveCountBeforeTest, store.SaveCount);
        Assert.AreEqual("Persisted", persistedStep.Name);
        Assert.AreEqual(1, persistedStep.X);
    }

    [TestMethod]
    public async Task LatestRunResult_KeepsOnlyTheMostRecentlyCompletedGroup()
    {
        var viewModel = CreateViewModel(
            new RecordingScriptStore(),
            new ImmediateEngine(),
            instanceService: new FixedInstanceService([new MemuInstance(1, "One", true, 101)]));
        await viewModel.InitializeAsync(CancellationToken.None);
        await viewModel.RefreshCommand.ExecuteAsync();

        viewModel.RunTargets.Single().IsSelected = true;
        await viewModel.RunCommand.ExecuteAsync();
        await WaitUntilAsync(() => viewModel.LatestRunResult?.GroupName == "Nhóm 01");
        var first = viewModel.LatestRunResult;

        viewModel.RunTargets.Single().IsSelected = true;
        await viewModel.RunCommand.ExecuteAsync();
        await WaitUntilAsync(() => viewModel.LatestRunResult?.GroupName == "Nhóm 02");

        Assert.AreNotSame(first, viewModel.LatestRunResult);
        Assert.AreNotEqual(first!.LaunchGroupId, viewModel.LatestRunResult!.LaunchGroupId);
        Assert.AreEqual(0, viewModel.ActiveLaunchGroups.Count);
        Assert.AreEqual(0, viewModel.InstanceRuns.Count);
        Assert.IsFalse(viewModel.LatestRunResult.HasIssues);
        Assert.IsTrue(viewModel.LatestRunResult.HasNoIssues);
    }

    [TestMethod]
    public void LaunchGroupSummary_UpdatesLargeGroupsIncrementallyAndDetaches()
    {
        var groupId = Guid.NewGuid();
        var script = new ScriptDefinition { Name = "Large", Steps = { new NoteStep { Name = "Step" } } };
        var instances = Enumerable.Range(0, 500)
            .Select(index => new InstanceRunItemViewModel(
                groupId,
                new MemuInstance(index, $"VM {index}", true, 1000 + index),
                script,
                (_, _) => true))
            .ToArray();
        var group = new LaunchGroupItemViewModel(1, groupId, DateTimeOffset.UtcNow, "Large", instances);

        Assert.AreEqual(500, group.WaitingCount);
        foreach (var instance in instances)
            instance.Apply(new InstanceExecutionUpdate(
                groupId,
                instance.Index,
                instance.Name,
                instance.Index % 2 == 0 ? InstanceExecutionStatus.Succeeded : InstanceExecutionStatus.Failed,
                ScriptId: instance.ScriptId,
                ScriptName: instance.ScriptName));

        Assert.AreEqual(0, group.WaitingCount);
        Assert.AreEqual(250, group.SucceededCount);
        Assert.AreEqual(250, group.FailedCount);
        group.Detach();
        Assert.IsFalse(group.HasInstanceStateSubscriptions);
    }

    [TestMethod]
    public async Task LargePerInstanceGroup_BoundsDescriptionAndReleasesRuntimeState()
    {
        const int targetCount = 150;
        var scripts = Enumerable.Range(0, targetCount)
            .Select(index => new ScriptDefinition
            {
                Name = $"Script {index:D3} {new string('x', 80)}",
                Steps = { new NoteStep { Name = "Step" } }
            })
            .ToArray();
        var targets = Enumerable.Range(0, targetCount)
            .Select(index => new MemuInstance(index, $"VM {index:D3}", true, 1000 + index))
            .ToArray();
        var viewModel = CreateViewModel(
            new RecordingScriptStore(scripts),
            new ImmediateEngine(),
            instanceService: new FixedInstanceService(targets));
        await viewModel.InitializeAsync(CancellationToken.None);
        await viewModel.RefreshCommand.ExecuteAsync();
        viewModel.ScriptAssignmentMode = ScriptAssignmentMode.PerInstance;
        foreach (var target in viewModel.RunTargets)
        {
            var script = scripts[target.Index];
            target.SetAssignedScript(script.Id, script.Name);
            target.IsSelected = true;
        }

        await viewModel.RunCommand.ExecuteAsync();
        await WaitUntilAsync(() => viewModel.LatestRunResult is not null);

        Assert.IsTrue(viewModel.LatestRunResult!.RunDescription.Length <= 240);
        StringAssert.Contains(viewModel.LatestRunResult.RunDescription, "kịch bản khác");
        Assert.AreEqual(0, viewModel.InstanceRuns.Count);
        Assert.AreEqual(0, viewModel.RunningInstanceCount);
        Assert.AreEqual(0, viewModel.WaitingInstanceCount);
    }

    [TestMethod]
    public void LatestRunResult_LargeMostlyFailedGroupUsesIndexMappedSnapshotAndHandlesMissingRuntime()
    {
        const int runtimeCount = 1200;
        var groupId = Guid.NewGuid();
        var startedAt = DateTimeOffset.UtcNow;
        var entries = Enumerable.Range(0, runtimeCount)
            .Select(index =>
            {
                var target = new MemuInstance(index, $"VM {index:D4}", true, 1000 + index);
                var script = new ScriptDefinition
                {
                    Name = $"Script {index:D4}"
                };
                foreach (var stepIndex in Enumerable.Range(0, 40))
                    script.Steps.Add(new NoteStep { Name = $"Step {index:D4}-{stepIndex:D3}" });
                return (
                    Runtime: new InstanceRunItemViewModel(groupId, target, script, (_, _) => true),
                    Target: target,
                    Script: script);
            })
            .ToArray();
        var group = new LaunchGroupItemViewModel(
            1,
            groupId,
            startedAt,
            "Large latest snapshot",
            entries.Select(entry => entry.Runtime));
        var results = entries.AsEnumerable()
            .Reverse()
            .Select(entry =>
            {
                var status = entry.Target.Index % 5 == 0
                    ? InstanceExecutionStatus.Succeeded
                    : entry.Target.Index % 2 == 0
                        ? InstanceExecutionStatus.Cancelled
                        : InstanceExecutionStatus.Failed;
                var stepStatus = status == InstanceExecutionStatus.Succeeded
                    ? StepExecutionStatus.Succeeded
                    : status == InstanceExecutionStatus.Cancelled
                        ? StepExecutionStatus.Cancelled
                        : StepExecutionStatus.Failed;
                return new InstanceExecutionResult
                {
                    LaunchGroupId = groupId,
                    Target = entry.Target,
                    Status = status,
                    Execution = new ExecutionResult
                    {
                        StartedAt = startedAt,
                        EndedAt = startedAt.AddSeconds(1),
                        WasCancelled = status == InstanceExecutionStatus.Cancelled,
                        Steps = entry.Script.Steps
                            .Select((step, stepIndex) => new StepExecutionResult
                            {
                                StepId = step.Id,
                                Status = stepIndex == entry.Script.Steps.Count - 1
                                    ? stepStatus
                                    : StepExecutionStatus.Succeeded,
                                StartedAt = startedAt,
                                EndedAt = startedAt.AddSeconds(1),
                                StandardError = stepIndex != entry.Script.Steps.Count - 1 ||
                                                status == InstanceExecutionStatus.Succeeded
                                    ? string.Empty
                                    : new string('e', 500)
                            })
                            .ToArray()
                    }
                };
            })
            .ToList();
        results.Add(new InstanceExecutionResult
        {
            LaunchGroupId = groupId,
            Target = new MemuInstance(runtimeCount + 100, "Missing runtime", true, 9999),
            Status = InstanceExecutionStatus.Failed,
            Execution = new ExecutionResult
            {
                StartedAt = startedAt,
                EndedAt = startedAt.AddSeconds(1),
                Steps =
                [
                    new StepExecutionResult
                    {
                        StepId = Guid.NewGuid(),
                        Status = StepExecutionStatus.Failed,
                        StartedAt = startedAt,
                        EndedAt = startedAt.AddSeconds(1),
                        StandardError = new string('m', 500)
                    }
                ]
            }
        });
        var completedResult = new MultiInstanceExecutionResult
        {
            LaunchGroupId = groupId,
            StartedAt = startedAt,
            EndedAt = startedAt.AddSeconds(1),
            Instances = results
        };
        var createLatestRunResult = typeof(MainViewModel).GetMethod(
            "CreateLatestRunResult",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);

        var latest = (LatestRunResultViewModel)createLatestRunResult!.Invoke(
            null,
            [group, completedResult, completedResult.EndedAt])!;

        Assert.AreEqual(runtimeCount + 1, latest.TotalInstanceCount);
        Assert.AreEqual(240, latest.SucceededCount);
        Assert.AreEqual(481, latest.FailedCount);
        Assert.AreEqual(480, latest.CancelledCount);
        Assert.AreEqual(runtimeCount + 1, latest.Instances.Count);
        Assert.IsTrue(latest.Instances.Any(snapshot =>
            snapshot.Index == 0 && snapshot.Status == InstanceExecutionStatus.Succeeded));
        Assert.AreEqual(961, latest.IssueInstances.Count);
        Assert.IsFalse(latest.IssueInstances.Any(issue => issue.Index % 5 == 0 && issue.Index < runtimeCount));
        var mapped = latest.IssueInstances.Single(issue => issue.Index == 1199);
        Assert.AreEqual("Script 1199", mapped.ScriptName);
        Assert.AreEqual("Step 1199-039", mapped.LastStep);
        Assert.AreEqual(240, mapped.ErrorMessage.Length);
        var missing = latest.IssueInstances.Single(issue => issue.Index == runtimeCount + 100);
        Assert.AreEqual("\u2014", missing.ScriptName);
        Assert.AreEqual("\u2014", missing.LastStep);
        Assert.AreEqual(240, missing.ErrorMessage.Length);
    }

    [TestMethod]
    public async Task LatestRunResult_KeepsBoundedSnapshotForEveryTargetWithoutLiveExecutionState()
    {
        var targets = new[]
        {
            new MemuInstance(1, "Succeeded", true, 101),
            new MemuInstance(2, "Failed", true, 102),
            new MemuInstance(3, "Cancelled", true, 103)
        };
        var viewModel = CreateViewModel(
            new RecordingScriptStore([new ScriptDefinition { Name = "Latest", Steps = { new NoteStep { Name = "Step 1" } } }]),
            new LatestResultEngine(),
            instanceService: new FixedInstanceService(targets));
        await viewModel.InitializeAsync(CancellationToken.None);
        await viewModel.RefreshCommand.ExecuteAsync();
        foreach (var target in viewModel.RunTargets) target.IsSelected = true;

        await viewModel.RunCommand.ExecuteAsync();
        await WaitUntilAsync(() => viewModel.LatestRunResult is not null);

        var latest = viewModel.LatestRunResult!;
        Assert.AreEqual(3, latest.TotalInstanceCount);
        Assert.AreEqual(1, latest.SucceededCount);
        Assert.AreEqual(1, latest.FailedCount);
        Assert.AreEqual(1, latest.CancelledCount);
        Assert.IsTrue(latest.HasInstances);
        Assert.IsFalse(latest.HasNoInstances);
        CollectionAssert.AreEquivalent(new[] { 1, 2, 3 }, latest.Instances.Select(item => item.Index).ToArray());
        CollectionAssert.AreEquivalent(
            new[] { InstanceExecutionStatus.Succeeded, InstanceExecutionStatus.Failed, InstanceExecutionStatus.Cancelled },
            latest.Instances.Select(item => item.Status).ToArray());
        Assert.IsTrue(latest.Instances.All(item => item.ShortMessage.Length <= 240));
        Assert.IsTrue(latest.Instances.All(item => item.LastStep == "Step 1"));
        Assert.IsTrue(latest.Duration >= TimeSpan.Zero);
        Assert.IsFalse(string.IsNullOrWhiteSpace(latest.DurationText));
        Assert.IsFalse(typeof(LatestRunResultViewModel).GetProperties().Any(property =>
            property.Name.Contains("Log", StringComparison.OrdinalIgnoreCase) ||
            property.Name.Contains("StandardOutput", StringComparison.OrdinalIgnoreCase) ||
            property.Name.Contains("StandardError", StringComparison.OrdinalIgnoreCase) ||
            property.PropertyType == typeof(ExecutionResult)));
    }

    [TestMethod]
    public async Task LatestRunResult_ClearCommandReturnsStateToEmptyWithoutPersistingIt()
    {
        var scripts = new RecordingScriptStore();
        var settings = new RecordingRunSettingsStore(new ApplicationSettings { MemucPath = @"C:\MEmu\memuc.exe" });
        var viewModel = CreateViewModel(
            scripts,
            new ImmediateEngine(),
            instanceService: new FixedInstanceService([new MemuInstance(1, "One", true, 101)]),
            settingsStore: settings);
        await viewModel.InitializeAsync(CancellationToken.None);
        await viewModel.RefreshCommand.ExecuteAsync();
        viewModel.RunTargets.Single().IsSelected = true;
        await viewModel.RunCommand.ExecuteAsync();
        await WaitUntilAsync(() => viewModel.LatestRunResult is not null);

        Assert.IsTrue(viewModel.HasLatestRunResult);
        Assert.IsTrue(viewModel.ClearLatestRunResultCommand.CanExecute(null));
        viewModel.ClearLatestRunResultCommand.Execute(null);

        Assert.IsNull(viewModel.LatestRunResult);
        Assert.IsFalse(viewModel.HasLatestRunResult);
        Assert.IsTrue(viewModel.HasNoLatestRunResult);
        Assert.IsFalse(viewModel.ClearLatestRunResultCommand.CanExecute(null));

        var reopened = CreateViewModel(
            scripts,
            new ImmediateEngine(),
            instanceService: new FixedInstanceService([new MemuInstance(1, "One", true, 101)]),
            settingsStore: settings);
        await reopened.InitializeAsync(CancellationToken.None);
        Assert.IsNull(reopened.LatestRunResult);
        Assert.AreEqual(0, reopened.RecentRuns.Count);
    }

    [TestMethod]
    public async Task RecentRuns_AreNewestFirstAndBoundedToTwentySnapshots()
    {
        var viewModel = CreateViewModel(
            new RecordingScriptStore(),
            new ImmediateEngine(),
            instanceService: new FixedInstanceService([new MemuInstance(1, "One", true, 101)]));
        await viewModel.InitializeAsync(CancellationToken.None);
        await viewModel.RefreshCommand.ExecuteAsync();

        for (var run = 1; run <= 21; run++)
        {
            viewModel.RunTargets.Single().IsSelected = true;
            await viewModel.RunCommand.ExecuteAsync();
            var expectedGroup = $"Nhóm {run:00}";
            await WaitUntilAsync(() => viewModel.LatestRunResult?.GroupName == expectedGroup);
        }

        Assert.AreEqual(20, viewModel.RecentRuns.Count);
        Assert.AreEqual("Nhóm 21", viewModel.RecentRuns[0].GroupName);
        Assert.AreEqual("Nhóm 02", viewModel.RecentRuns[^1].GroupName);
        Assert.AreSame(viewModel.RecentRuns[0], viewModel.LatestRunResult);
        Assert.AreSame(viewModel.RecentRuns[0], viewModel.SelectedRecentRunResult);
        Assert.IsFalse(viewModel.RecentRuns.GetType().GetGenericArguments()[0].GetProperties().Any(property =>
            typeof(Task).IsAssignableFrom(property.PropertyType) ||
            property.PropertyType == typeof(ExecutionResult) ||
            property.Name.Contains("Execution", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public async Task ActiveInstances_SearchAndStatusFilterCombineAcrossIndexNameAndScript()
    {
        var viewModel = CreateViewModel(new RecordingScriptStore(), new ImmediateEngine());
        await viewModel.InitializeAsync(CancellationToken.None);
        var queued = CreateActiveItem(12, "Alpha", "Script A", InstanceExecutionStatus.Queued);
        var running = CreateActiveItem(23, "Beta", "Script B", InstanceExecutionStatus.Running);
        var failed = CreateActiveItem(34, "Gamma", "Other", InstanceExecutionStatus.Failed);
        var unavailable = CreateActiveItem(45, "Delta", "Script B", InstanceExecutionStatus.Unavailable);
        var cancelled = CreateActiveItem(56, "Epsilon", "Other", InstanceExecutionStatus.Cancelled);
        foreach (var item in new[] { queued, running, failed, unavailable, cancelled })
            viewModel.ActiveInstanceRuns.Add(item);

        viewModel.ActiveInstanceSearchText = "23";
        CollectionAssert.AreEqual(new[] { 23 }, viewModel.FilteredActiveInstanceRuns.Select(item => item.Index).ToArray());
        viewModel.ActiveInstanceSearchText = "beta";
        CollectionAssert.AreEqual(new[] { 23 }, viewModel.FilteredActiveInstanceRuns.Select(item => item.Index).ToArray());
        viewModel.ActiveInstanceSearchText = "script b";
        CollectionAssert.AreEquivalent(new[] { 23, 45 }, viewModel.FilteredActiveInstanceRuns.Select(item => item.Index).ToArray());

        viewModel.SelectedActiveInstanceFilter = ActiveInstanceFilter.Running;
        CollectionAssert.AreEqual(new[] { 23 }, viewModel.FilteredActiveInstanceRuns.Select(item => item.Index).ToArray());
        viewModel.ActiveInstanceSearchText = "delta";
        Assert.AreEqual(0, viewModel.FilteredActiveInstanceCount);
        viewModel.ActiveInstanceSearchText = string.Empty;
        viewModel.SelectedActiveInstanceFilter = ActiveInstanceFilter.Waiting;
        CollectionAssert.AreEqual(new[] { 12 }, viewModel.FilteredActiveInstanceRuns.Select(item => item.Index).ToArray());
        viewModel.SelectedActiveInstanceFilter = ActiveInstanceFilter.Problem;
        CollectionAssert.AreEquivalent(new[] { 34, 45 }, viewModel.FilteredActiveInstanceRuns.Select(item => item.Index).ToArray());
        Assert.IsFalse(viewModel.FilteredActiveInstanceRuns.Contains(cancelled));
    }

    [TestMethod]
    public void ActiveInstance_TerminalStatusesKeepLastMeaningfulStepAndBoundedMessage()
    {
        var step = new NoteStep { Name = "Bước có ý nghĩa" };
        var script = new ScriptDefinition { Name = "Script", Steps = { step } };
        var groupId = Guid.NewGuid();
        var item = new InstanceRunItemViewModel(groupId, new MemuInstance(7, "VM", true, 107), script, (_, _) => true);
        Assert.AreEqual("Đang chờ", item.StatusText);
        item.Apply(new InstanceExecutionUpdate(groupId, 7, "VM", InstanceExecutionStatus.Running,
            new StepExecutionUpdate(step.Id, StepExecutionStatus.Running), ScriptId: script.Id));
        Assert.AreEqual("Đang chạy", item.StatusText);
        item.Apply(new InstanceExecutionUpdate(groupId, 7, "VM", InstanceExecutionStatus.Failed,
            Message: new string('x', 500), ScriptId: script.Id));

        Assert.AreEqual("Bước có ý nghĩa", item.CurrentStep);
        Assert.AreEqual("Lỗi", item.StatusText);
        Assert.AreEqual(240, item.MessageText.Length);
        item.Apply(new InstanceExecutionUpdate(groupId, 7, "VM", InstanceExecutionStatus.Unavailable,
            Message: "Không còn khả dụng", ScriptId: script.Id));
        Assert.AreEqual("Bước có ý nghĩa", item.CurrentStep);
        Assert.AreEqual("Không khả dụng", item.StatusText);
        item.Apply(new InstanceExecutionUpdate(groupId, 7, "VM", InstanceExecutionStatus.Cancelled,
            Message: "Đã hủy", ScriptId: script.Id));
        Assert.AreEqual("Bước có ý nghĩa", item.CurrentStep);
        Assert.AreEqual("Đã hủy", item.StatusText);
        item.Apply(new InstanceExecutionUpdate(groupId, 7, "VM", InstanceExecutionStatus.Succeeded,
            ScriptId: script.Id));
        Assert.AreEqual("Thành công", item.StatusText);
    }

    [TestMethod]
    public async Task SelectProblemInstances_SelectsOnlyExistingFailedAndUnavailableTargets()
    {
        var instances = new FixedInstanceService(
        [
            new MemuInstance(1, "Failed", true, 101),
            new MemuInstance(2, "Unavailable", false, 0),
            new MemuInstance(3, "Cancelled", true, 103)
        ]);
        var viewModel = CreateViewModel(new RecordingScriptStore(), new ImmediateEngine(), instanceService: instances);
        await viewModel.InitializeAsync(CancellationToken.None);
        await viewModel.RefreshCommand.ExecuteAsync();
        viewModel.RunTargetSearchText = "Cancelled";
        viewModel.RunTargets.Single(item => item.Index == 3).IsSelected = true;
        viewModel.SelectedRecentRunResult = new LatestRunResultViewModel(
            Guid.NewGuid(), "Nhóm 01", "Test", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
            4, 0, 2, 1, 1,
            [
                new RecentRunIssueViewModel(1, "Failed", "Script", "Step", InstanceExecutionStatus.Failed, "failed"),
                new RecentRunIssueViewModel(2, "Unavailable", "Script", "Step", InstanceExecutionStatus.Unavailable, "missing"),
                new RecentRunIssueViewModel(3, "Cancelled", "Script", "Step", InstanceExecutionStatus.Cancelled, "cancelled"),
                new RecentRunIssueViewModel(4, "Gone", "Script", "Step", InstanceExecutionStatus.Failed, "gone")
            ]);

        Assert.IsTrue(viewModel.SelectProblemInstancesCommand.CanExecute(null));
        viewModel.SelectProblemInstancesCommand.Execute(null);

        CollectionAssert.AreEqual(new[] { 1 }, viewModel.RunTargets.Where(item => item.IsSelected).Select(item => item.Index).ToArray());
        Assert.AreEqual("Cancelled", viewModel.RunTargetSearchText);
        StringAssert.Contains(viewModel.StatusMessage, "Đã chọn 1");
        StringAssert.Contains(viewModel.StatusMessage, "2 thiết bị hiện không thể chạy");
    }

    [TestMethod]
    public async Task StoppedTarget_CannotBeSelectedAndRefreshImmediatelyUnselectsIt()
    {
        var instances = new MutableInstanceService([new MemuInstance(7, "Seven", true, 107)]);
        var viewModel = CreateViewModel(
            new RecordingScriptStore(),
            new ImmediateEngine(),
            instanceService: instances);
        await viewModel.InitializeAsync(CancellationToken.None);
        await viewModel.RefreshCommand.ExecuteAsync();
        var target = viewModel.RunTargets.Single();
        target.IsSelected = true;
        Assert.IsTrue(target.IsSelected);
        Assert.AreEqual(1, viewModel.SelectedRunTargetCount);

        instances.Instances = [new MemuInstance(7, "Seven", false, 0)];
        await viewModel.RefreshCommand.ExecuteAsync();

        Assert.AreSame(target, viewModel.RunTargets.Single());
        Assert.IsFalse(target.IsRunning);
        Assert.IsFalse(target.CanSelectForRun);
        Assert.IsFalse(target.IsSelected);
        Assert.AreEqual(0, viewModel.SelectedRunTargetCount);
        Assert.AreEqual("Đã chọn 0 / Tổng 1", viewModel.RunTargetSelectionSummary);
        Assert.IsFalse(viewModel.RunCommand.CanExecute(null));

        target.IsSelected = true;
        Assert.IsFalse(target.IsSelected);
    }

    [TestMethod]
    public async Task RefreshWhileRunning_PreservesReservationAndActiveTarget()
    {
        var instances = new MutableInstanceService([new MemuInstance(9, "Nine", true, 109)]);
        var engine = new BlockingEngine();
        var viewModel = CreateViewModel(new RecordingScriptStore(), engine, instanceService: instances);
        await viewModel.InitializeAsync(CancellationToken.None);
        await viewModel.RefreshCommand.ExecuteAsync();
        viewModel.RunTargets.Single().IsSelected = true;
        var runTask = viewModel.RunCommand.ExecuteAsync();
        await engine.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        instances.Instances = [];
        Assert.IsTrue(viewModel.RefreshCommand.CanExecute(null));
        await viewModel.RefreshCommand.ExecuteAsync();

        var preserved = viewModel.RunTargets.Single(item => item.Index == 9);
        Assert.IsTrue(preserved.IsActive);
        Assert.IsFalse(preserved.CanSelectForRun);
        Assert.AreEqual(1, viewModel.ActiveInstanceRuns.Count);
        Assert.IsFalse(viewModel.RunCommand.CanExecute(null));
        viewModel.StopCommand.Execute(null);
        await runTask.WaitAsync(TimeSpan.FromSeconds(2));
        await WaitUntilAsync(() => !viewModel.IsExecuting);
        Assert.AreEqual(0, viewModel.RunTargets.Count,
            "A target missing during refresh must leave the target list as soon as its reservation ends.");
    }

    [DataTestMethod]
    [DataRow(3)]
    [DataRow(60)]
    [DataRow(125)]
    public void ProgressPump_BurstUsesOneDrainAndKeepsEveryInstanceTerminal(int instanceCount)
    {
        var context = new QueuedSynchronizationContext();
        var applied = new List<InstanceExecutionUpdate>();
        var pump = new InstanceExecutionProgressPump(context, applied.Add);
        var groupId = Guid.NewGuid();

        for (var index = 0; index < instanceCount; index++)
        {
            for (var updateIndex = 0; updateIndex < 40; updateIndex++)
            {
                pump.Report(new InstanceExecutionUpdate(
                    groupId,
                    index,
                    $"VM {index}",
                    InstanceExecutionStatus.Running,
                    new StepExecutionUpdate(Guid.NewGuid(), StepExecutionStatus.Running)));
            }

            var terminalStatus = (index % 4) switch
            {
                0 => InstanceExecutionStatus.Succeeded,
                1 => InstanceExecutionStatus.Failed,
                2 => InstanceExecutionStatus.Unavailable,
                _ => InstanceExecutionStatus.Cancelled
            };
            pump.Report(new InstanceExecutionUpdate(groupId, index, $"VM {index}", terminalStatus));
            pump.Report(new InstanceExecutionUpdate(groupId, index, $"VM {index}", InstanceExecutionStatus.Running));
        }

        Assert.AreEqual(1, context.PostCount);
        Assert.AreEqual(1, pump.PostedDrainCount);
        Assert.AreEqual(0, applied.Count);
        context.DrainAll();

        Assert.IsTrue(applied.Count <= instanceCount * 3,
            "A burst should retain bounded lifecycle/latest/terminal work per instance.");
        for (var index = 0; index < instanceCount; index++)
        {
            var expected = (index % 4) switch
            {
                0 => InstanceExecutionStatus.Succeeded,
                1 => InstanceExecutionStatus.Failed,
                2 => InstanceExecutionStatus.Unavailable,
                _ => InstanceExecutionStatus.Cancelled
            };
            Assert.AreEqual(expected, applied.Last(update => update.InstanceIndex == index).Status);
        }
    }

    [TestMethod]
    public void ProgressPump_PreservesImportantPerInstanceOrderingBeforeTerminalBarrier()
    {
        var context = new QueuedSynchronizationContext();
        var applied = new List<InstanceExecutionUpdate>();
        var pump = new InstanceExecutionProgressPump(context, applied.Add);
        var groupId = Guid.NewGuid();
        var failedStepId = Guid.NewGuid();

        pump.Report(new InstanceExecutionUpdate(groupId, 7, "VM", InstanceExecutionStatus.WaitingForLaunch));
        pump.Report(new InstanceExecutionUpdate(groupId, 7, "VM", InstanceExecutionStatus.Running));
        pump.Report(new InstanceExecutionUpdate(
            groupId,
            7,
            "VM",
            InstanceExecutionStatus.Running,
            new StepExecutionUpdate(failedStepId, StepExecutionStatus.Failed)));
        pump.Report(new InstanceExecutionUpdate(groupId, 7, "VM", InstanceExecutionStatus.Cancelled));
        pump.Report(new InstanceExecutionUpdate(groupId, 7, "VM", InstanceExecutionStatus.Running));

        pump.DrainPending();

        CollectionAssert.AreEqual(
            new[]
            {
                InstanceExecutionStatus.WaitingForLaunch,
                InstanceExecutionStatus.Running,
                InstanceExecutionStatus.Running,
                InstanceExecutionStatus.Cancelled
            },
            applied.Select(update => update.Status).ToArray());
        Assert.AreEqual(failedStepId, applied[2].StepUpdate!.StepId);
        Assert.AreEqual(InstanceExecutionStatus.Cancelled, applied[^1].Status);
        context.DrainAll();
        Assert.AreEqual(4, applied.Count);
    }

    [TestMethod]
    public void ProgressPump_UsesAndroidTargetKeyWhenIndexesShareMinusOneSentinel()
    {
        var context = new QueuedSynchronizationContext();
        var applied = new List<InstanceExecutionUpdate>();
        var pump = new InstanceExecutionProgressPump(context, applied.Add);
        var groupId = Guid.NewGuid();

        pump.Report(new InstanceExecutionUpdate(groupId, -1, "Phone A", InstanceExecutionStatus.Running)
        {
            TargetKey = "android-adb:SERIAL-A",
            DeviceKind = DeviceKind.AndroidAdb,
            TargetIdentifier = "SERIAL-A"
        });
        pump.Report(new InstanceExecutionUpdate(groupId, -1, "Phone A", InstanceExecutionStatus.Succeeded)
        {
            TargetKey = "android-adb:SERIAL-A",
            DeviceKind = DeviceKind.AndroidAdb,
            TargetIdentifier = "SERIAL-A"
        });
        pump.Report(new InstanceExecutionUpdate(groupId, -1, "Phone B", InstanceExecutionStatus.Running)
        {
            TargetKey = "android-adb:SERIAL-B",
            DeviceKind = DeviceKind.AndroidAdb,
            TargetIdentifier = "SERIAL-B"
        });
        pump.Report(new InstanceExecutionUpdate(groupId, -1, "Phone B", InstanceExecutionStatus.Failed)
        {
            TargetKey = "android-adb:SERIAL-B",
            DeviceKind = DeviceKind.AndroidAdb,
            TargetIdentifier = "SERIAL-B"
        });

        context.DrainAll();

        Assert.AreEqual(InstanceExecutionStatus.Succeeded,
            applied.Last(update => update.TargetKey == "android-adb:SERIAL-A").Status);
        Assert.IsTrue(applied.Any(update => update.TargetKey == "android-adb:SERIAL-B" &&
                                            update.Status == InstanceExecutionStatus.Running));
        Assert.AreEqual(InstanceExecutionStatus.Failed,
            applied.Last(update => update.TargetKey == "android-adb:SERIAL-B").Status);
    }

    [TestMethod]
    public void ProgressPump_FailedAndMessageBurstKeepsLatestImportantWorkBoundedBeforeTerminal()
    {
        var context = new QueuedSynchronizationContext();
        var applied = new List<InstanceExecutionUpdate>();
        var pump = new InstanceExecutionProgressPump(context, applied.Add);
        var groupId = Guid.NewGuid();
        var finalFailedStepId = Guid.NewGuid();

        pump.Report(new InstanceExecutionUpdate(groupId, 9, "VM", InstanceExecutionStatus.Running));
        for (var index = 0; index < 500; index++)
        {
            pump.Report(new InstanceExecutionUpdate(
                groupId,
                9,
                "VM",
                InstanceExecutionStatus.Running,
                new StepExecutionUpdate(Guid.NewGuid(), StepExecutionStatus.Failed)));
            pump.Report(new InstanceExecutionUpdate(
                groupId,
                9,
                "VM",
                InstanceExecutionStatus.Running,
                Message: $"important-{index}"));
        }
        pump.Report(new InstanceExecutionUpdate(
            groupId,
            9,
            "VM",
            InstanceExecutionStatus.Running,
            new StepExecutionUpdate(finalFailedStepId, StepExecutionStatus.Failed)));
        pump.Report(new InstanceExecutionUpdate(groupId, 9, "VM", InstanceExecutionStatus.Failed));

        Assert.AreEqual(1, context.PostCount);
        context.DrainAll();

        Assert.IsTrue(applied.Count <= 3);
        Assert.IsTrue(applied.Any(update => update.StepUpdate?.StepId == finalFailedStepId));
        Assert.AreEqual(InstanceExecutionStatus.Failed, applied[^1].Status);
    }

    [TestMethod]
    public void ActiveInstance_UnchangedProgressDoesNotNotifyOrRequeryAndTerminalRejectsStaleRunning()
    {
        var item = CreateActiveItem(7, "VM", "Script", InstanceExecutionStatus.Running);
        var changed = new List<string?>();
        var stopRequeryCount = 0;
        item.PropertyChanged += (_, args) => changed.Add(args.PropertyName);
        item.StopCommand.CanExecuteChanged += (_, _) => stopRequeryCount++;

        item.Apply(new InstanceExecutionUpdate(item.LaunchGroupId, item.Index, item.Name, InstanceExecutionStatus.Running));

        Assert.AreEqual(0, changed.Count);
        Assert.AreEqual(0, stopRequeryCount);

        item.Apply(new InstanceExecutionUpdate(item.LaunchGroupId, item.Index, item.Name, InstanceExecutionStatus.Failed));
        Assert.IsTrue(changed.Contains(nameof(InstanceRunItemViewModel.Status)));
        Assert.IsTrue(changed.Contains(nameof(InstanceRunItemViewModel.CanStop)));
        Assert.AreEqual(1, stopRequeryCount);
        changed.Clear();

        item.Apply(new InstanceExecutionUpdate(item.LaunchGroupId, item.Index, item.Name, InstanceExecutionStatus.Running));
        Assert.AreEqual(InstanceExecutionStatus.Failed, item.Status);
        Assert.AreEqual(0, changed.Count);
        Assert.AreEqual(1, stopRequeryCount);
    }

    [TestMethod]
    public void ActiveInstance_StatusFilterUpdatesMembershipIncrementallyAndPreservesRowIdentity()
    {
        var viewModel = CreateViewModel(new RecordingScriptStore(), new ImmediateEngine());
        var item = CreateActiveItem(11, "Alpha", "Script", InstanceExecutionStatus.Queued);
        item.IsSelected = true;
        viewModel.ActiveInstanceRuns.Add(item);
        viewModel.SelectedActiveInstanceFilter = ActiveInstanceFilter.Running;
        var actions = new List<System.Collections.Specialized.NotifyCollectionChangedAction>();
        viewModel.FilteredActiveInstanceRuns.CollectionChanged += (_, args) => actions.Add(args.Action);

        item.Apply(new InstanceExecutionUpdate(item.LaunchGroupId, item.Index, item.Name, InstanceExecutionStatus.Running));

        Assert.AreEqual(1, viewModel.FilteredActiveInstanceRuns.Count);
        Assert.AreSame(item, viewModel.FilteredActiveInstanceRuns.Single());
        Assert.IsTrue(item.IsSelected);
        CollectionAssert.AreEqual(
            new[] { System.Collections.Specialized.NotifyCollectionChangedAction.Add },
            actions.ToArray());

        item.Apply(new InstanceExecutionUpdate(item.LaunchGroupId, item.Index, item.Name, InstanceExecutionStatus.Running));
        Assert.AreEqual(1, actions.Count);

        item.Apply(new InstanceExecutionUpdate(item.LaunchGroupId, item.Index, item.Name, InstanceExecutionStatus.Failed));
        CollectionAssert.AreEqual(
            new[]
            {
                System.Collections.Specialized.NotifyCollectionChangedAction.Add,
                System.Collections.Specialized.NotifyCollectionChangedAction.Remove
            },
            actions.ToArray());
        Assert.IsFalse(actions.Contains(System.Collections.Specialized.NotifyCollectionChangedAction.Reset));
        Assert.IsTrue(item.IsSelected);
    }

    [TestMethod]
    public async Task IntermediateProgress_DoesNotRebuildProjectionOrRequeryUnrelatedCommands()
    {
        var engine = new BurstBlockingEngine();
        var viewModel = CreateViewModel(
            new RecordingScriptStore(),
            engine,
            instanceService: new FixedInstanceService([new MemuInstance(4, "VM 4", true, 104)]));
        await viewModel.InitializeAsync(CancellationToken.None);
        await viewModel.RefreshCommand.ExecuteAsync();
        viewModel.RunTargets.Single().IsSelected = true;
        await viewModel.RunCommand.ExecuteAsync();
        await engine.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var active = viewModel.ActiveInstanceRuns.Single();
        active.IsSelected = true;
        var unrelatedCommandRequeryCount = 0;
        var stopSelectedRequeryCount = 0;
        var projectionChangeCount = 0;
        viewModel.BrowseCommand.CanExecuteChanged += (_, _) => unrelatedCommandRequeryCount++;
        viewModel.StopSelectedActiveInstancesCommand.CanExecuteChanged += (_, _) => stopSelectedRequeryCount++;
        viewModel.FilteredActiveInstanceRuns.CollectionChanged += (_, _) => projectionChangeCount++;

        engine.ReportBurst(200);

        Assert.AreEqual(0, unrelatedCommandRequeryCount);
        Assert.AreEqual(0, stopSelectedRequeryCount);
        Assert.AreEqual(0, projectionChangeCount);
        Assert.AreSame(active, viewModel.FilteredActiveInstanceRuns.Single());
        Assert.IsTrue(active.IsSelected);

        engine.Complete();
        await WaitUntilAsync(() => !viewModel.IsExecuting);
    }

    private static InstanceRunItemViewModel CreateActiveItem(
        int index,
        string instanceName,
        string scriptName,
        InstanceExecutionStatus status)
    {
        var script = new ScriptDefinition { Name = scriptName, Steps = { new NoteStep { Name = "Step" } } };
        var groupId = Guid.NewGuid();
        var item = new InstanceRunItemViewModel(groupId, new MemuInstance(index, instanceName, true, 100 + index), script, (_, _) => true);
        if (status != InstanceExecutionStatus.Queued)
            item.Apply(new InstanceExecutionUpdate(groupId, index, instanceName, status, ScriptId: script.Id));
        return item;
    }

    [TestMethod]
    public void ExecutionHistoryStateTypesCommandsAndLimitAreRemoved()
    {
        Assert.IsNull(typeof(MainViewModel).GetProperty("ExecutionHistory"));
        Assert.IsNull(typeof(MainViewModel).GetProperty("SelectedHistoryGroup"));
        Assert.IsNull(typeof(MainViewModel).GetProperty("SelectedHistoryInstance"));
        Assert.IsNull(typeof(MainViewModel).GetProperty("HistoryExecutionLog"));
        Assert.IsNull(typeof(MainViewModel).GetProperty("DeleteSelectedHistoryCommand"));
        Assert.IsNull(typeof(MainViewModel).GetProperty("DeleteCompletedHistoryCommand"));
        Assert.IsNull(typeof(MainViewModel).GetProperty("ClearHistoryCommand"));
        Assert.IsNull(typeof(LaunchGroupItemViewModel).GetProperty("IsChecked"));
        Assert.IsNull(typeof(LaunchGroupItemViewModel).GetProperty("IsCompleted"));
        Assert.IsNull(typeof(LaunchGroupItemViewModel).GetProperty("EndedAt"));
        Assert.IsNull(typeof(LaunchGroupItemViewModel).GetProperty("TechnicalId"));
        Assert.IsNull(typeof(LaunchGroupItemViewModel).GetMethod("MarkCompleted"));
        Assert.IsNull(typeof(ApplicationSettings).GetProperty("ExecutionHistory"));
        Assert.IsNull(typeof(ApplicationSettings).GetProperty("LatestRunResult"));
        Assert.IsNull(typeof(MainViewModel).GetField("ExecutionHistoryLimit",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic));
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
        var latest = viewModel.LatestRunResult;
        engine.ReportLate(viewModel.SelectedScript!.Model.Steps[0].Id);

        Assert.AreSame(latest, viewModel.LatestRunResult);
        Assert.AreEqual(0, viewModel.ActiveLaunchGroups.Count);
        Assert.AreEqual(0, viewModel.InstanceRuns.Count);
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
        Assert.AreEqual("com.example.app", viewModel.EditorApplicationDisplayName);

        viewModel.EditorKind = ScriptStepKind.ForceStop;
        viewModel.EditorActivityName = "keep";
        await viewModel.SelectApplicationCommand.ExecuteAsync();
        Assert.AreEqual("com.example.app", viewModel.EditorPackageName);
        Assert.AreEqual("keep", viewModel.EditorActivityName);
        Assert.AreEqual(6, picker.LastInstanceIndex);
    }

    [TestMethod]
    public async Task AndroidSelectApplication_UsesSelectedSerialAndFillsProviderSpecificStepFields()
    {
        var android = new FixedAndroidDeviceService([AndroidDevice("SERIAL-A"), AndroidDevice("SERIAL-B")]);
        var picker = new RecordingAndroidApplicationPicker(
            new AndroidApplicationInfo("com.example.android", ".LauncherActivity"));
        var viewModel = CreateViewModel(
            new RecordingScriptStore(),
            new ImmediateEngine(),
            androidDeviceService: android,
            androidStateProbe: android,
            adbPathDiscovery: new ValidAdbPathDiscovery(),
            androidApplicationPickerService: picker);
        await viewModel.InitializeAsync(CancellationToken.None);
        await viewModel.RefreshCommand.ExecuteAsync();
        viewModel.SelectedEditorTarget = viewModel.EditorTargets.Single(target => target.Identifier == "SERIAL-B");

        viewModel.EditorKind = ScriptStepKind.OpenApp;
        await viewModel.SelectApplicationCommand.ExecuteAsync();
        Assert.AreEqual("com.example.android", viewModel.EditorPackageName);
        Assert.AreEqual(".LauncherActivity", viewModel.EditorActivityName);

        viewModel.EditorKind = ScriptStepKind.ForceStop;
        viewModel.EditorActivityName = ".StaleMemuActivity";
        await viewModel.SelectApplicationCommand.ExecuteAsync();
        Assert.AreEqual("com.example.android", viewModel.EditorPackageName);
        Assert.AreEqual(string.Empty, viewModel.EditorActivityName);
        Assert.IsTrue(picker.Calls.All(call => call.AdbPath == @"C:\MEmu\adb.exe" && call.Serial == "SERIAL-B"));
    }

    [TestMethod]
    public async Task AndroidSelectedApp_SavesFriendlyNameSeparatelyFromPackageAndActivity()
    {
        var step = new OpenAppStep
        {
            Name = "Mở ứng dụng",
            PackageName = "com.legacy.app",
            ActivityName = ".Legacy"
        };
        var script = new ScriptDefinition { Name = "Apps", Steps = [step] };
        var store = new RecordingScriptStore([script]);
        var android = new FixedAndroidDeviceService([AndroidDevice("SERIAL-A")]);
        var picker = new RecordingAndroidApplicationPicker(
            new AndroidApplicationInfo("com.example.android", ".LauncherActivity", "Tên từ Android"));
        var viewModel = CreateViewModel(
            store,
            new ImmediateEngine(),
            androidDeviceService: android,
            androidStateProbe: android,
            adbPathDiscovery: new ValidAdbPathDiscovery(),
            androidApplicationPickerService: picker);
        await viewModel.InitializeAsync(CancellationToken.None);
        await viewModel.RefreshCommand.ExecuteAsync();
        viewModel.SelectedEditorTarget = viewModel.EditorTargets.Single();
        viewModel.SelectedStep = viewModel.Steps.Single();

        await viewModel.SelectApplicationCommand.ExecuteAsync();
        Assert.AreEqual("Tên từ Android", viewModel.EditorApplicationDisplayName);
        viewModel.EditorApplicationDisplayName = "  Tên người dùng sửa  ";
        await viewModel.SaveStepCommand.ExecuteAsync();

        var saved = (OpenAppStep)store.LastSaved.Single().Steps.Single();
        Assert.AreEqual("Tên người dùng sửa", saved.ApplicationDisplayName);
        Assert.AreEqual("com.example.android", saved.PackageName);
        Assert.AreEqual(".LauncherActivity", saved.ActivityName);
    }

    [TestMethod]
    public async Task AndroidPickerFriendlyName_PersistsAndIsPassedBackWhenPickerReopens()
    {
        var step = new OpenAppStep
        {
            Name = "Mở ứng dụng",
            PackageName = "com.legacy.app",
            ActivityName = ".Legacy"
        };
        var store = new RecordingScriptStore([new ScriptDefinition { Name = "Apps", Steps = [step] }]);
        var android = new FixedAndroidDeviceService([AndroidDevice("SERIAL-A")]);
        var picker = new RecordingAndroidApplicationPicker(
            new AndroidApplicationInfo(
                "com.android.chrome",
                "com.google.android.apps.chrome.Main",
                "Chrome"));
        var viewModel = CreateViewModel(
            store,
            new ImmediateEngine(),
            androidDeviceService: android,
            androidStateProbe: android,
            adbPathDiscovery: new ValidAdbPathDiscovery(),
            androidApplicationPickerService: picker);
        await viewModel.InitializeAsync(CancellationToken.None);
        await viewModel.RefreshCommand.ExecuteAsync();
        viewModel.SelectedEditorTarget = viewModel.EditorTargets.Single();
        viewModel.SelectedStep = viewModel.Steps.Single();

        await viewModel.SelectApplicationCommand.ExecuteAsync();
        await viewModel.SaveStepCommand.ExecuteAsync();
        var saved = (OpenAppStep)store.LastSaved.Single().Steps.Single();
        Assert.AreEqual("Chrome", saved.ApplicationDisplayName);
        Assert.AreEqual("com.android.chrome", saved.PackageName);
        Assert.AreEqual("com.google.android.apps.chrome.Main", saved.ActivityName);

        await viewModel.SelectApplicationCommand.ExecuteAsync();

        var reopened = picker.Calls.Last().CurrentSelection;
        Assert.IsNotNull(reopened);
        Assert.AreEqual("Chrome", reopened.ApplicationLabel);
        Assert.AreEqual(saved.PackageName, reopened.PackageName);
        Assert.AreEqual(saved.ActivityName, reopened.ActivityName);
    }

    [TestMethod]
    public async Task AndroidPicker_DoesNotTreatLegacyPackageFallbackAsFriendlyNameOnReopen()
    {
        var step = new OpenAppStep
        {
            Name = "Mở ứng dụng",
            PackageName = "com.android.chrome",
            ActivityName = "com.google.android.apps.chrome.Main",
            ApplicationDisplayName = "com.android.chrome"
        };
        var android = new FixedAndroidDeviceService([AndroidDevice("SERIAL-A")]);
        var picker = new RecordingAndroidApplicationPicker(
            new AndroidApplicationInfo(
                "com.android.chrome",
                "com.google.android.apps.chrome.Main",
                "Chrome"));
        var viewModel = CreateViewModel(
            new RecordingScriptStore([new ScriptDefinition { Name = "Apps", Steps = [step] }]),
            new ImmediateEngine(),
            androidDeviceService: android,
            androidStateProbe: android,
            adbPathDiscovery: new ValidAdbPathDiscovery(),
            androidApplicationPickerService: picker);
        await viewModel.InitializeAsync(CancellationToken.None);
        await viewModel.RefreshCommand.ExecuteAsync();
        viewModel.SelectedEditorTarget = viewModel.EditorTargets.Single();
        viewModel.SelectedStep = viewModel.Steps.Single();

        await viewModel.SelectApplicationCommand.ExecuteAsync();

        Assert.IsNull(picker.Calls.Single().CurrentSelection!.ApplicationLabel);
        Assert.AreEqual("Chrome", viewModel.EditorApplicationDisplayName);
        Assert.AreEqual("com.android.chrome", viewModel.EditorPackageName);
        Assert.AreEqual("com.google.android.apps.chrome.Main", viewModel.EditorActivityName);
    }

    [TestMethod]
    public async Task AndroidSelectedApp_LeavesFriendlyNameBlankWhenLabelIsUnknown()
    {
        var android = new FixedAndroidDeviceService([AndroidDevice("SERIAL-A")]);
        var picker = new RecordingAndroidApplicationPicker(
            new AndroidApplicationInfo("com.example.fallback", ".Main"));
        var viewModel = CreateViewModel(
            new RecordingScriptStore(),
            new ImmediateEngine(),
            androidDeviceService: android,
            androidStateProbe: android,
            adbPathDiscovery: new ValidAdbPathDiscovery(),
            androidApplicationPickerService: picker);
        await viewModel.InitializeAsync(CancellationToken.None);
        await viewModel.RefreshCommand.ExecuteAsync();
        viewModel.EditorKind = ScriptStepKind.OpenApp;

        await viewModel.SelectApplicationCommand.ExecuteAsync();

        Assert.AreEqual(string.Empty, viewModel.EditorApplicationDisplayName);
        Assert.AreEqual("Không xác định", viewModel.EditorApplicationDisplayText);
        Assert.AreEqual("com.example.fallback", viewModel.EditorPackageName);
        Assert.AreEqual(".Main", viewModel.EditorActivityName);
    }

    [TestMethod]
    public async Task AndroidDeviceAlias_AppliesByExactSerialAcrossReconnectWithoutChangingTargetKey()
    {
        var settings = new ApplicationSettings { MemucPath = @"C:\MEmu\memuc.exe", AdbPath = @"C:\MEmu\adb.exe" };
        settings.AndroidDeviceAliases["SERIAL-A"] = "Redmi chính";
        var settingsStore = new MutableSettingsStore(settings);
        var devices = new MutableAndroidDeviceService([AndroidDevice("SERIAL-A"), AndroidDevice("SERIAL-B")]);
        var viewModel = CreateViewModel(
            new RecordingScriptStore(),
            new ImmediateEngine(),
            settingsStore: settingsStore,
            androidDeviceService: devices,
            androidStateProbe: devices,
            adbPathDiscovery: new ValidAdbPathDiscovery());
        await viewModel.InitializeAsync(CancellationToken.None);
        await viewModel.RefreshCommand.ExecuteAsync();

        Assert.AreEqual("Android · Redmi chính", viewModel.EditorTargets.Single(item => item.Identifier == "SERIAL-A").DisplayName);
        StringAssert.Contains(viewModel.EditorTargets.Single(item => item.Identifier == "SERIAL-B").DisplayName, "Redmi 9C");
        Assert.AreEqual("android-adb:SERIAL-A", viewModel.RunTargets.Single(item => item.Identifier == "SERIAL-A").TargetKey);

        devices.Devices = [AndroidDevice("SERIAL-B"), AndroidDevice("SERIAL-A")];
        await viewModel.RefreshCommand.ExecuteAsync();

        Assert.AreEqual("Android · Redmi chính", viewModel.EditorTargets.Single(item => item.Identifier == "SERIAL-A").DisplayName);
        Assert.AreEqual("android-adb:SERIAL-A", viewModel.EditorTargets.Single(item => item.Identifier == "SERIAL-A").TargetKey);
    }

    [TestMethod]
    public async Task AndroidDeviceAlias_RenameAndRemoveAffectOnlySelectedSerial()
    {
        var settings = new ApplicationSettings { MemucPath = @"C:\MEmu\memuc.exe", AdbPath = @"C:\MEmu\adb.exe" };
        settings.AndroidDeviceAliases["SERIAL-B"] = "Giữ nguyên";
        var settingsStore = new MutableSettingsStore(settings);
        var devices = new FixedAndroidDeviceService([AndroidDevice("SERIAL-A"), AndroidDevice("SERIAL-B")]);
        var dialog = new QueueAndroidDeviceAliasDialog(
            new AndroidDeviceAliasEditResult("Máy chính"),
            new AndroidDeviceAliasEditResult(null, RemoveAlias: true));
        var viewModel = CreateViewModel(
            new RecordingScriptStore(),
            new ImmediateEngine(),
            settingsStore: settingsStore,
            androidDeviceService: devices,
            androidStateProbe: devices,
            adbPathDiscovery: new ValidAdbPathDiscovery(),
            androidDeviceAliasDialogService: dialog);
        await viewModel.InitializeAsync(CancellationToken.None);
        await viewModel.RefreshCommand.ExecuteAsync();
        viewModel.SelectedEditorTarget = viewModel.EditorTargets.Single(item => item.Identifier == "SERIAL-A");

        await viewModel.EditAndroidDeviceAliasCommand.ExecuteAsync();
        Assert.AreEqual("Máy chính", settingsStore.Current.AndroidDeviceAliases["SERIAL-A"]);
        Assert.AreEqual("Giữ nguyên", settingsStore.Current.AndroidDeviceAliases["SERIAL-B"]);
        Assert.AreEqual("Android · Máy chính", viewModel.SelectedEditorTarget.DisplayName);

        await viewModel.EditAndroidDeviceAliasCommand.ExecuteAsync();
        Assert.IsFalse(settingsStore.Current.AndroidDeviceAliases.ContainsKey("SERIAL-A"));
        Assert.AreEqual("Giữ nguyên", settingsStore.Current.AndroidDeviceAliases["SERIAL-B"]);
        StringAssert.Contains(viewModel.SelectedEditorTarget.DisplayName, "SERIAL-A");
    }

    [TestMethod]
    public async Task TargetRefresh_ShowsOneMemuProviderRowAndOneFilteredExternalAndroidRow()
    {
        var android = new FixedAndroidDeviceService([AndroidDevice("PHONE")]);
        var viewModel = CreateViewModel(
            new RecordingScriptStore(),
            new ImmediateEngine(),
            instanceService: new FixedInstanceService([new MemuInstance(0, "MEmu 0", true, 100)]),
            androidDeviceService: android,
            androidStateProbe: android,
            adbPathDiscovery: new ValidAdbPathDiscovery());
        await viewModel.InitializeAsync(CancellationToken.None);

        await viewModel.RefreshCommand.ExecuteAsync();

        Assert.AreEqual(2, viewModel.EditorTargets.Count);
        Assert.AreEqual(1, viewModel.EditorTargets.Count(item => item.DeviceKind == DeviceKind.MEmu));
        Assert.AreEqual(1, viewModel.EditorTargets.Count(item => item.DeviceKind == DeviceKind.AndroidAdb));
        Assert.AreEqual(2, viewModel.RunTargets.Count);
    }

    [TestMethod]
    public async Task AndroidSelectApplication_DoesNotQueryAfterSelectedTargetDisconnects()
    {
        var android = new MutableAndroidDeviceService([AndroidDevice("SERIAL-A")]);
        var picker = new RecordingAndroidApplicationPicker(
            new AndroidApplicationInfo("com.example.android", ".LauncherActivity"));
        var viewModel = CreateViewModel(
            new RecordingScriptStore(),
            new ImmediateEngine(),
            androidDeviceService: android,
            androidStateProbe: android,
            adbPathDiscovery: new ValidAdbPathDiscovery(),
            androidApplicationPickerService: picker);
        await viewModel.InitializeAsync(CancellationToken.None);
        await viewModel.RefreshCommand.ExecuteAsync();
        viewModel.EditorKind = ScriptStepKind.OpenApp;

        android.Devices = [];
        await viewModel.RefreshCommand.ExecuteAsync();
        await viewModel.SelectApplicationCommand.ExecuteAsync();

        Assert.IsNull(viewModel.SelectedEditorTarget);
        Assert.IsFalse(viewModel.SelectApplicationCommand.CanExecute(null));
        Assert.AreEqual(0, picker.Calls.Count);
    }

    [TestMethod]
    public async Task AndroidShell_IsHiddenOnlyFromNewStepAuthoringAndLegacyStepRemainsEditable()
    {
        var legacy = new ScriptDefinition
        {
            Name = "Legacy shell",
            Steps = [new AndroidShellStep { Name = "Legacy", Command = "settings get system user_rotation" }]
        };
        var viewModel = CreateViewModel(new RecordingScriptStore([legacy]), new ImmediateEngine());
        await viewModel.InitializeAsync(CancellationToken.None);
        viewModel.SelectedStep = viewModel.Steps.Single();

        Assert.AreEqual(RegularStepEditorMode.Edit, viewModel.StepEditorMode);
        Assert.AreEqual(ScriptStepKind.AndroidShell, viewModel.EditorKind);
        Assert.IsTrue(viewModel.StepKinds.Contains(ScriptStepKind.AndroidShell));

        await viewModel.NewStepCommand.ExecuteAsync();

        Assert.AreEqual(RegularStepEditorMode.Create, viewModel.StepEditorMode);
        Assert.AreEqual(ScriptStepKind.ForceStop, viewModel.EditorKind);
        Assert.IsFalse(viewModel.StepKinds.Contains(ScriptStepKind.AndroidShell));
    }

    [TestMethod]
    public async Task AndroidApplicationPicker_FiltersCatalogWithoutPackageNameFallbackAndPersistsEditedName()
    {
        var service = new FixedAndroidApplicationService(
        [
            new AndroidApplicationInfo("com.alpha.app", ".Main", "Alpha"),
            new AndroidApplicationInfo("com.beta.app", ".Home")
        ]);
        var viewModel = new AndroidApplicationPickerViewModel(service, @"C:\Tools\adb.exe", "SERIAL-A");

        await viewModel.RefreshAsync(CancellationToken.None);
        Assert.AreEqual(2, viewModel.Applications.Count);
        Assert.AreEqual("Không xác định", viewModel.Applications.Single(item => item.PackageName == "com.beta.app").DisplayName);
        Assert.IsTrue(viewModel.ShowForegroundApplication);
        Assert.IsTrue(viewModel.ShowNameLibrary);

        viewModel.SearchText = "Alpha";
        Assert.AreEqual("com.alpha.app", viewModel.Applications.Single().PackageName);
        viewModel.ManualDisplayName = "  Chrome  ";
        var selection = viewModel.CreateSelection();
        Assert.AreEqual("Chrome", selection!.ApplicationLabel);
        Assert.AreEqual("com.alpha.app", selection.PackageName);
        Assert.AreEqual(".Main", selection.ActivityName);
        Assert.AreEqual((@"C:\Tools\adb.exe", "SERIAL-A"), service.LastRequest);
    }

    [TestMethod]
    public async Task AndroidApplicationPicker_ReopensCurrentStepWithFriendlyNameWithoutChangingComponent()
    {
        var service = new FixedAndroidApplicationService(
        [
            new AndroidApplicationInfo("com.android.chrome", "com.google.android.apps.chrome.Main"),
            new AndroidApplicationInfo("com.example.other", ".Main", "Other")
        ]);
        var current = new AndroidApplicationInfo(
            "com.android.chrome",
            "com.google.android.apps.chrome.Main",
            "Chrome");
        var viewModel = new AndroidApplicationPickerViewModel(
            service,
            @"C:\Tools\adb.exe",
            "SERIAL-A",
            current);

        await viewModel.RefreshAsync(CancellationToken.None);

        Assert.AreEqual("com.android.chrome", viewModel.SelectedApplication!.PackageName);
        Assert.AreEqual("Chrome", viewModel.Applications.Single(application =>
            application.PackageName == "com.android.chrome").DisplayName);
        Assert.AreEqual("Chrome", viewModel.ManualDisplayName);
        var selection = viewModel.CreateSelection();
        Assert.AreEqual("Chrome", selection!.ApplicationLabel);
        Assert.AreEqual(current.PackageName, selection.PackageName);
        Assert.AreEqual(current.ActivityName, selection.ActivityName);
    }

    [TestMethod]
    public async Task AndroidApplicationPicker_SavedAliasWinsAndReloadsFromSettings()
    {
        var service = new FixedAndroidApplicationService(
        [
            new AndroidApplicationInfo("com.android.chrome", ".Main", "Android Chrome")
        ]);
        var settings = new ApplicationSettings();
        settings.ApplicationDisplayNames["com.android.chrome"] = "Chrome";
        var store = new MutableSettingsStore(settings);
        var current = new AndroidApplicationInfo("com.android.chrome", ".Main", "Step-local Chrome");

        var first = new AndroidApplicationPickerViewModel(
            service, @"C:\Tools\adb.exe", "SERIAL-A", current, settings.ApplicationDisplayNames,
            settings: settings, settingsStore: store);
        await first.RefreshAsync(CancellationToken.None);

        Assert.AreEqual("Chrome", first.Applications.Single().DisplayName);

        var reloadedSettings = await store.LoadAsync(CancellationToken.None);
        var reopened = new AndroidApplicationPickerViewModel(
            service, @"C:\Tools\adb.exe", "SERIAL-A", savedAliases: reloadedSettings.ApplicationDisplayNames,
            settings: reloadedSettings, settingsStore: store);
        await reopened.RefreshAsync(CancellationToken.None);

        Assert.AreEqual("Chrome", reopened.Applications.Single().DisplayName);
    }

    [STATestMethod]
    public async Task AndroidApplicationPickerWindow_CtrlSUpdatesPersistenceRowAndSearchWithoutAdbReload()
    {
        if (Application.Current is null)
        {
            var application = new MEmuScriptStudio.App.App();
            application.InitializeComponent();
        }

        var service = new FixedAndroidApplicationService(
        [
            new AndroidApplicationInfo("com.example.browser", ".Main")
        ]);
        var settings = new ApplicationSettings();
        settings.ApplicationDisplayNames["com.example.browser"] = "Chrome";
        var store = new MutableSettingsStore(settings);
        var viewModel = new AndroidApplicationPickerViewModel(
            service, @"C:\Tools\adb.exe", "SERIAL-A", savedAliases: settings.ApplicationDisplayNames,
            settings: settings, settingsStore: store);
        await viewModel.RefreshAsync(CancellationToken.None);
        var window = new ApplicationPickerWindow(viewModel);
        viewModel.SearchText = "Chrome";
        viewModel.ManualDisplayName = "Firefox Test";

        var handled = await window.TrySaveNameShortcutAsync(Key.S, ModifierKeys.Control);

        Assert.IsTrue(handled);
        Assert.AreEqual("Firefox Test", store.Current.ApplicationDisplayNames["com.example.browser"]);
        Assert.AreEqual("Firefox Test", viewModel.Applications.Single(application =>
            application.PackageName == "com.example.browser").DisplayName);
        Assert.AreEqual("Firefox Test", viewModel.ManualDisplayName);
        Assert.AreEqual(string.Empty, viewModel.SearchText,
            "An old-name filter is cleared so the renamed row remains selected and visible.");
        Assert.AreEqual(1, service.RequestCount, "Saving a name must not repeat Android discovery.");
        viewModel.SearchText = "Firefox Test";
        Assert.AreEqual("com.example.browser", viewModel.Applications.Single().PackageName);
        StringAssert.Contains(viewModel.StatusMessage, "Đã lưu tên");
    }

    [TestMethod]
    public async Task AndroidApplicationPicker_AliasIsPackageScopedAcrossActivitiesAndSeparatePackages()
    {
        var service = new FixedAndroidApplicationService(
        [
            new AndroidApplicationInfo("com.example.same", ".One"),
            new AndroidApplicationInfo("com.example.same", ".Two"),
            new AndroidApplicationInfo("com.example.other", ".Main", "Other label")
        ]);
        var settings = new ApplicationSettings();
        settings.ApplicationDisplayNames["com.example.same"] = "Same alias";
        var store = new MutableSettingsStore(settings);
        var viewModel = new AndroidApplicationPickerViewModel(
            service, @"C:\Tools\adb.exe", "SERIAL-A", savedAliases: settings.ApplicationDisplayNames,
            settings: settings, settingsStore: store);

        await viewModel.RefreshAsync(CancellationToken.None);

        Assert.IsTrue(viewModel.Applications
            .Where(application => application.PackageName == "com.example.same")
            .All(application => application.DisplayName == "Same alias"));
        Assert.AreEqual("Other label", viewModel.Applications.Single(application =>
            application.PackageName == "com.example.other").DisplayName);
    }

    [TestMethod]
    public async Task AndroidApplicationPicker_BlankSaveRemovesAliasAndFallsBackToLabelOrUnknown()
    {
        var service = new FixedAndroidApplicationService(
        [
            new AndroidApplicationInfo("com.example.labeled", ".Main", "Android label"),
            new AndroidApplicationInfo("com.example.unknown", ".Main")
        ]);
        var settings = new ApplicationSettings();
        settings.ApplicationDisplayNames["com.example.labeled"] = "Custom labeled";
        settings.ApplicationDisplayNames["com.example.unknown"] = "Custom unknown";
        var store = new MutableSettingsStore(settings);
        var viewModel = new AndroidApplicationPickerViewModel(
            service, @"C:\Tools\adb.exe", "SERIAL-A", savedAliases: settings.ApplicationDisplayNames,
            settings: settings, settingsStore: store);
        await viewModel.RefreshAsync(CancellationToken.None);

        viewModel.SelectedApplication = viewModel.Applications.Single(application =>
            application.PackageName == "com.example.labeled");
        viewModel.ManualDisplayName = string.Empty;
        await viewModel.SaveNameAsync(CancellationToken.None);
        Assert.AreEqual("Android label", viewModel.SelectedApplication!.DisplayName);

        viewModel.SelectedApplication = viewModel.Applications.Single(application =>
            application.PackageName == "com.example.unknown");
        viewModel.ManualDisplayName = string.Empty;
        await viewModel.SaveNameAsync(CancellationToken.None);

        Assert.AreEqual("Không xác định", viewModel.SelectedApplication!.DisplayName);
        Assert.IsFalse(store.Current.ApplicationDisplayNames.ContainsKey("com.example.labeled"));
        Assert.IsFalse(store.Current.ApplicationDisplayNames.ContainsKey("com.example.unknown"));
        Assert.AreEqual(1, service.RequestCount);
    }

    [STATestMethod]
    public async Task AndroidApplicationPicker_ChoosePersistsUnsavedNameAndReturnsComponent()
    {
        if (Application.Current is null)
        {
            var application = new MEmuScriptStudio.App.App();
            application.InitializeComponent();
        }

        var service = new FixedAndroidApplicationService(
        [
            new AndroidApplicationInfo("com.example.choose", ".Launcher", "Chrome"),
            new AndroidApplicationInfo("com.example.other", ".Launcher", "Chrome Other")
        ]);
        var settings = new ApplicationSettings();
        var store = new MutableSettingsStore(settings);
        var viewModel = new AndroidApplicationPickerViewModel(
            service, @"C:\Tools\adb.exe", "SERIAL-A", savedAliases: settings.ApplicationDisplayNames,
            settings: settings, settingsStore: store);
        await viewModel.RefreshAsync(CancellationToken.None);
        var window = new ApplicationPickerWindow(viewModel);
        viewModel.SearchText = "Chrome";
        viewModel.SelectedApplication = viewModel.Applications.Single(application =>
            application.PackageName == "com.example.choose");
        viewModel.ManualDisplayName = "Chosen name";

        Assert.IsTrue(await window.PersistSelectionNameIfRequiredAsync());
        var selection = viewModel.CreateSelection();

        Assert.AreEqual("Chosen name", store.Current.ApplicationDisplayNames["com.example.choose"]);
        Assert.AreEqual("Chosen name", selection!.ApplicationLabel);
        Assert.AreEqual("com.example.choose", selection.PackageName);
        Assert.AreEqual(".Launcher", selection.ActivityName);
        Assert.AreEqual(string.Empty, viewModel.SearchText);
    }

    [TestMethod]
    public async Task AndroidApplicationPicker_SaveThenCancelKeepsAliasWithoutMutatingCurrentStep()
    {
        var step = new OpenAppStep
        {
            Name = "Open app",
            PackageName = "com.example.cancel",
            ActivityName = ".Main",
            ApplicationDisplayName = "Old step name"
        };
        var service = new FixedAndroidApplicationService(
        [
            new AndroidApplicationInfo(step.PackageName, step.ActivityName)
        ]);
        var settings = new ApplicationSettings();
        var store = new MutableSettingsStore(settings);
        var current = new AndroidApplicationInfo(step.PackageName, step.ActivityName, step.ApplicationDisplayName);
        var viewModel = new AndroidApplicationPickerViewModel(
            service, @"C:\Tools\adb.exe", "SERIAL-A", current, settings.ApplicationDisplayNames,
            settings, store);
        await viewModel.RefreshAsync(CancellationToken.None);
        viewModel.ManualDisplayName = "Saved alias only";

        await viewModel.SaveNameAsync(CancellationToken.None);

        Assert.AreEqual("Saved alias only", store.Current.ApplicationDisplayNames[step.PackageName]);
        Assert.AreEqual("Old step name", step.ApplicationDisplayName,
            "Cancel does not apply the picker selection to the current step.");
    }

    [TestMethod]
    public async Task AndroidApplicationPicker_FailedAliasSaveKeepsPersistedAndDisplayedState()
    {
        var service = new FixedAndroidApplicationService(
        [
            new AndroidApplicationInfo("com.example.failure", ".Main")
        ]);
        var settings = new ApplicationSettings();
        settings.ApplicationDisplayNames["com.example.failure"] = "Existing alias";
        var viewModel = new AndroidApplicationPickerViewModel(
            service, @"C:\Tools\adb.exe", "SERIAL-A", savedAliases: settings.ApplicationDisplayNames,
            settings: settings, settingsStore: new ThrowingUpdateSettingsStore());
        await viewModel.RefreshAsync(CancellationToken.None);
        viewModel.ManualDisplayName = "Unsaved alias";

        await Assert.ThrowsExceptionAsync<IOException>(() =>
            viewModel.SaveNameAsync(CancellationToken.None));

        Assert.AreEqual("Existing alias", settings.ApplicationDisplayNames["com.example.failure"]);
        Assert.AreEqual("Existing alias", viewModel.Applications.Single().DisplayName);
        Assert.AreEqual("Unsaved alias", viewModel.ManualDisplayName);
    }

    [TestMethod]
    public async Task AndroidApplicationPicker_ForegroundSelectsExactLauncherComponentAndSavedAlias()
    {
        var applications = new FixedAndroidApplicationService(
        [
            new AndroidApplicationInfo("com.example.app", ".Main", "Android label"),
            new AndroidApplicationInfo("com.example.other", ".Home", "Other")
        ]);
        var settings = new ApplicationSettings();
        settings.ApplicationDisplayNames["com.example.app"] = "Saved alias";
        var viewModel = new AndroidApplicationPickerViewModel(
            applications, @"C:\Tools\adb.exe", "SERIAL-B",
            savedAliases: settings.ApplicationDisplayNames,
            settings: settings,
            settingsStore: new MutableSettingsStore(settings),
            foregroundApplicationService: new FixedAndroidForegroundApplicationService(
                new AndroidApplicationInfo("com.example.app", ".Main")));
        await viewModel.RefreshAsync(CancellationToken.None);
        Assert.AreEqual("com.example.app", viewModel.SelectedApplication!.PackageName);
        viewModel.ManualDisplayName = "Unsaved stale text";

        await viewModel.UseForegroundApplicationAsync(CancellationToken.None);

        Assert.AreEqual("com.example.app", viewModel.SelectedApplication!.PackageName);
        Assert.AreEqual(".Main", viewModel.SelectedApplication.ActivityName);
        Assert.AreEqual("Saved alias", viewModel.ManualDisplayName);
        StringAssert.Contains(viewModel.StatusMessage, "com.example.app/.Main");
    }

    [TestMethod]
    public async Task AndroidApplicationPicker_ForegroundPreservesDifferentCurrentActivity()
    {
        var applications = new FixedAndroidApplicationService(
        [
            new AndroidApplicationInfo("com.android.chrome", ".Launcher", "Chrome")
        ]);
        var foreground = new FixedAndroidForegroundApplicationService(
            new AndroidApplicationInfo("com.android.chrome", ".IncognitoActivity"));
        var viewModel = new AndroidApplicationPickerViewModel(
            applications, @"C:\Tools\adb.exe", "SERIAL-A",
            foregroundApplicationService: foreground);
        await viewModel.RefreshAsync(CancellationToken.None);

        await viewModel.UseForegroundApplicationAsync(CancellationToken.None);

        Assert.AreEqual(2, viewModel.Applications.Count);
        Assert.AreEqual(".IncognitoActivity", viewModel.SelectedApplication!.ActivityName);
        Assert.AreEqual("Chrome", viewModel.SelectedApplication.DisplayName);
        Assert.AreEqual("SERIAL-A", foreground.Serial);
    }

    [TestMethod]
    public async Task AndroidApplicationPicker_ForegroundNonLauncherBecomesSelectableTemporaryCandidate()
    {
        var viewModel = new AndroidApplicationPickerViewModel(
            new FixedAndroidApplicationService(
                [new AndroidApplicationInfo("com.example.launcher", ".Main", "Launcher")]),
            @"C:\Tools\adb.exe",
            "SERIAL-A",
            foregroundApplicationService: new FixedAndroidForegroundApplicationService(
                new AndroidApplicationInfo("com.example.hidden", ".Internal")));
        await viewModel.RefreshAsync(CancellationToken.None);

        await viewModel.UseForegroundApplicationAsync(CancellationToken.None);
        viewModel.ManualDisplayName = "Hidden app";
        var selection = viewModel.CreateSelection();

        Assert.AreEqual("com.example.hidden", selection!.PackageName);
        Assert.AreEqual(".Internal", selection.ActivityName);
        Assert.AreEqual("Hidden app", selection.ApplicationLabel);
        Assert.AreEqual("Không xác định", viewModel.Applications.Single(application =>
            application.PackageName == "com.example.hidden").DisplayName);
    }

    [TestMethod]
    public async Task AndroidApplicationPicker_ForegroundFailureKeepsCurrentSelection()
    {
        var viewModel = new AndroidApplicationPickerViewModel(
            new FixedAndroidApplicationService(
            [
                new AndroidApplicationInfo("com.example.current", ".Main"),
                new AndroidApplicationInfo("com.example.other", ".Home")
            ]),
            @"C:\Tools\adb.exe",
            "SERIAL-A",
            foregroundApplicationService: new ThrowingAndroidForegroundApplicationService());
        await viewModel.RefreshAsync(CancellationToken.None);
        viewModel.SelectedApplication = viewModel.Applications.Single(application =>
            application.PackageName == "com.example.other");

        await Assert.ThrowsExceptionAsync<AndroidAdbDeviceUnavailableException>(() =>
            viewModel.UseForegroundApplicationAsync(CancellationToken.None));

        Assert.AreEqual("com.example.other", viewModel.SelectedApplication!.PackageName);
        Assert.AreEqual(".Home", viewModel.SelectedApplication.ActivityName);
    }

    [TestMethod]
    public async Task AndroidApplicationPicker_SaveAndDeleteNotifyOnlyPersistedFriendlyFallback()
    {
        var changes = new List<(string PackageName, string? FriendlyName)>();
        var draftFriendlyName = "Old alias";
        var settings = new ApplicationSettings();
        settings.ApplicationDisplayNames["com.example.app"] = "Old alias";
        var viewModel = new AndroidApplicationPickerViewModel(
            new FixedAndroidApplicationService(
                [new AndroidApplicationInfo("com.example.app", ".Main", "Android label")]),
            @"C:\Tools\adb.exe",
            "SERIAL-A",
            savedAliases: settings.ApplicationDisplayNames,
            settings: settings,
            settingsStore: new MutableSettingsStore(settings),
            aliasChanged: (packageName, friendlyName) =>
            {
                changes.Add((packageName, friendlyName));
                if (packageName == "com.example.app") draftFriendlyName = friendlyName ?? string.Empty;
            });
        await viewModel.RefreshAsync(CancellationToken.None);
        viewModel.ManualDisplayName = "New alias";

        await viewModel.SaveNameAsync(CancellationToken.None);
        await viewModel.DeleteSavedNameAsync(CancellationToken.None);

        CollectionAssert.AreEqual(
            new[]
            {
                ("com.example.app", (string?)"New alias"),
                ("com.example.app", (string?)"Android label")
            },
            changes);
        Assert.AreEqual("Android label", viewModel.SelectedApplication!.DisplayName);
        Assert.AreEqual("Android label", draftFriendlyName);
        Assert.IsFalse(viewModel.CanDeleteSavedName);
    }

    [TestMethod]
    public async Task AndroidApplicationPicker_BlankCtrlSWithoutAliasClearsStepOverlayToUnknown()
    {
        string? synchronizedName = "Step overlay";
        var current = new AndroidApplicationInfo("com.example.unknown", ".Main", "Step overlay");
        var settings = new ApplicationSettings();
        var viewModel = new AndroidApplicationPickerViewModel(
            new FixedAndroidApplicationService(
                [new AndroidApplicationInfo("com.example.unknown", ".Main")]),
            @"C:\Tools\adb.exe",
            "SERIAL-A",
            current,
            settings.ApplicationDisplayNames,
            settings,
            new MutableSettingsStore(settings),
            aliasChanged: (_, friendlyName) => synchronizedName = friendlyName);
        await viewModel.RefreshAsync(CancellationToken.None);
        viewModel.ManualDisplayName = string.Empty;

        await viewModel.SaveNameAsync(CancellationToken.None);

        Assert.AreEqual("Không xác định", viewModel.SelectedApplication!.DisplayName);
        Assert.AreEqual(string.Empty, viewModel.ManualDisplayName);
        Assert.IsNull(synchronizedName);
        Assert.AreEqual(0, settings.ApplicationDisplayNames.Count);
    }

    [TestMethod]
    public async Task AndroidApplicationPicker_ImportMergesDeterministicallyAndAddsSelectableComponent()
    {
        var settings = new ApplicationSettings();
        settings.ApplicationDisplayNames["com.example.a"] = "Old A";
        settings.ApplicationDisplayNames["com.example.b"] = "Old B";
        var conflicts = new QueueApplicationNameConflict(
            ApplicationNameImportConflictResolution.Skip,
            ApplicationNameImportConflictResolution.Overwrite);
        var transfer = new RecordingAndroidApplicationLibraryTransferService(
        [
            new AndroidApplicationLibraryEntry("com.example.b", ".ImportedB", "New B"),
            new AndroidApplicationLibraryEntry("com.example.c", ".ImportedC", "New C"),
            new AndroidApplicationLibraryEntry("com.example.a", ".ImportedA", "New A")
        ]);
        var viewModel = new AndroidApplicationPickerViewModel(
            new FixedAndroidApplicationService([]),
            @"C:\Tools\adb.exe",
            "SERIAL-A",
            savedAliases: settings.ApplicationDisplayNames,
            settings: settings,
            settingsStore: new MutableSettingsStore(settings),
            fileDialogService: new AndroidApplicationLibraryFileDialog(@"C:\Temp\in.androidappnames", null),
            applicationLibraryTransferService: transfer,
            importConflictService: conflicts);
        await viewModel.RefreshAsync(CancellationToken.None);

        await viewModel.ImportNamesAsync(CancellationToken.None);

        CollectionAssert.AreEqual(new[] { "com.example.a", "com.example.b" },
            conflicts.Calls.Select(call => call.PackageName).ToArray());
        Assert.AreEqual("Old A", settings.ApplicationDisplayNames["com.example.a"]);
        Assert.AreEqual("New B", settings.ApplicationDisplayNames["com.example.b"]);
        Assert.AreEqual("New C", settings.ApplicationDisplayNames["com.example.c"]);
        Assert.IsTrue(viewModel.Applications.Any(application =>
            application.PackageName == "com.example.c" && application.ActivityName == ".ImportedC"));
    }

    [TestMethod]
    public async Task AndroidApplicationPicker_ExportIncludesFriendlyPackageAndKnownActivity()
    {
        var settings = new ApplicationSettings();
        settings.ApplicationDisplayNames["com.example.app"] = "Example";
        var transfer = new RecordingAndroidApplicationLibraryTransferService([]);
        var viewModel = new AndroidApplicationPickerViewModel(
            new FixedAndroidApplicationService(
            [
                new AndroidApplicationInfo("com.example.app", ".Zed", "Android label"),
                new AndroidApplicationInfo("com.example.app", ".Alpha", "Android label")
            ]),
            @"C:\Tools\adb.exe",
            "SERIAL-A",
            savedAliases: settings.ApplicationDisplayNames,
            settings: settings,
            settingsStore: new MutableSettingsStore(settings),
            fileDialogService: new AndroidApplicationLibraryFileDialog(
                null, @"C:\Temp\out.androidappnames"),
            applicationLibraryTransferService: transfer,
            importConflictService: new QueueApplicationNameConflict());
        await viewModel.RefreshAsync(CancellationToken.None);
        viewModel.SelectedApplication = viewModel.Applications.Single(application =>
            application.ActivityName == ".Zed");

        await viewModel.ExportNamesAsync(CancellationToken.None);

        Assert.AreEqual(@"C:\Temp\out.androidappnames", transfer.ExportPath);
        Assert.AreEqual(
            new AndroidApplicationLibraryEntry("com.example.app", ".Alpha", "Example"),
            transfer.ExportedEntries!.Single());
    }

    [TestMethod]
    public async Task AndroidPickerAliasChange_SamePackageUpdatesDraftAcrossCancelWithoutChangingComponent()
    {
        var step = new OpenAppStep
        {
            Name = "Open",
            PackageName = "com.example.app",
            ActivityName = ".Main",
            ApplicationDisplayName = "Old name"
        };
        var android = new FixedAndroidDeviceService([AndroidDevice("SERIAL-A")]);
        var picker = new AliasChangingAndroidApplicationPicker(
            "com.example.app", "New name", result: null);
        var viewModel = CreateViewModel(
            new RecordingScriptStore([new ScriptDefinition { Name = "Apps", Steps = [step] }]),
            new ImmediateEngine(),
            androidDeviceService: android,
            androidStateProbe: android,
            adbPathDiscovery: new ValidAdbPathDiscovery(),
            androidApplicationPickerService: picker);
        await viewModel.InitializeAsync(CancellationToken.None);
        await viewModel.RefreshCommand.ExecuteAsync();
        viewModel.SelectedEditorTarget = viewModel.EditorTargets.Single();
        viewModel.SelectedStep = viewModel.Steps.Single();

        await viewModel.SelectApplicationCommand.ExecuteAsync();

        Assert.AreEqual("New name", viewModel.EditorApplicationDisplayName);
        Assert.AreEqual("com.example.app", viewModel.EditorPackageName);
        Assert.AreEqual(".Main", viewModel.EditorActivityName);
        Assert.AreEqual("Old name", step.ApplicationDisplayName,
            "Cancel/X must not commit the editor draft to the step model.");
    }

    [TestMethod]
    public async Task AndroidPickerAliasChange_DifferentPackageDoesNotChangeCurrentDraft()
    {
        var android = new FixedAndroidDeviceService([AndroidDevice("SERIAL-A")]);
        var picker = new AliasChangingAndroidApplicationPicker(
            "com.example.other", "Other name", result: null);
        var viewModel = CreateViewModel(
            new RecordingScriptStore(),
            new ImmediateEngine(),
            androidDeviceService: android,
            androidStateProbe: android,
            adbPathDiscovery: new ValidAdbPathDiscovery(),
            androidApplicationPickerService: picker);
        await viewModel.InitializeAsync(CancellationToken.None);
        await viewModel.RefreshCommand.ExecuteAsync();
        viewModel.EditorKind = ScriptStepKind.OpenApp;
        viewModel.EditorPackageName = "com.example.current";
        viewModel.EditorActivityName = ".Current";
        viewModel.EditorApplicationDisplayName = "Current name";

        await viewModel.SelectApplicationCommand.ExecuteAsync();

        Assert.AreEqual("Current name", viewModel.EditorApplicationDisplayName);
        Assert.AreEqual("com.example.current", viewModel.EditorPackageName);
        Assert.AreEqual(".Current", viewModel.EditorActivityName);
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
    public async Task AndroidCapture_FillsExistingTapHoldAndSwipeFieldsWithoutExecutingScript()
    {
        var device = AndroidDevice("SERIAL-A");
        var android = new FixedAndroidDeviceService([device]);
        var dialog = new RecordingAndroidCoordinateCaptureDialog(mode => mode switch
        {
            AndroidCoordinateCaptureMode.Tap => new AndroidCoordinateCaptureResult(new CapturedTap(120, 340)),
            AndroidCoordinateCaptureMode.Hold => new AndroidCoordinateCaptureResult(new CapturedTap(125, 345)),
            AndroidCoordinateCaptureMode.Swipe => new AndroidCoordinateCaptureResult(
                Swipe: new CapturedSwipe(10, 20, 300, 400)),
            _ => null
        });
        var engine = new ImmediateEngine();
        var viewModel = CreateViewModel(
            new RecordingScriptStore(),
            engine,
            androidDeviceService: android,
            androidStateProbe: android,
            adbPathDiscovery: new ValidAdbPathDiscovery(),
            androidCoordinateCaptureDialogService: dialog);
        await viewModel.InitializeAsync(CancellationToken.None);
        await viewModel.RefreshCommand.ExecuteAsync();

        viewModel.EditorKind = ScriptStepKind.Tap;
        await viewModel.CaptureTapCommand.ExecuteAsync();
        Assert.AreEqual(120, viewModel.EditorX);
        Assert.AreEqual(340, viewModel.EditorY);

        viewModel.EditorKind = ScriptStepKind.Hold;
        viewModel.EditorHoldDuration = 850;
        await viewModel.CaptureHoldCommand.ExecuteAsync();
        Assert.AreEqual(125, viewModel.EditorX);
        Assert.AreEqual(345, viewModel.EditorY);
        Assert.AreEqual(850, viewModel.EditorHoldDuration);

        viewModel.EditorKind = ScriptStepKind.Swipe;
        viewModel.EditorSwipeDuration = 650;
        await viewModel.CaptureSwipeCommand.ExecuteAsync();
        Assert.AreEqual(10, viewModel.EditorX);
        Assert.AreEqual(20, viewModel.EditorY);
        Assert.AreEqual(300, viewModel.EditorX2);
        Assert.AreEqual(400, viewModel.EditorY2);
        Assert.AreEqual(650, viewModel.EditorSwipeDuration);
        CollectionAssert.AreEqual(
            new[]
            {
                AndroidCoordinateCaptureMode.Tap,
                AndroidCoordinateCaptureMode.Hold,
                AndroidCoordinateCaptureMode.Swipe
            },
            dialog.Calls.Select(call => call.Mode).ToArray());
        Assert.IsTrue(dialog.Calls.All(call => call.Serial == "SERIAL-A"));
        Assert.IsNull(engine.LastRequest);
    }

    [TestMethod]
    public async Task AndroidCapture_UsesOnlySelectedEditorSerialAcrossMultipleDevices()
    {
        var android = new FixedAndroidDeviceService([AndroidDevice("SERIAL-A"), AndroidDevice("SERIAL-B")]);
        var dialog = new RecordingAndroidCoordinateCaptureDialog(_ =>
            new AndroidCoordinateCaptureResult(new CapturedTap(7, 8)));
        var viewModel = CreateViewModel(
            new RecordingScriptStore(),
            new ImmediateEngine(),
            androidDeviceService: android,
            androidStateProbe: android,
            adbPathDiscovery: new ValidAdbPathDiscovery(),
            androidCoordinateCaptureDialogService: dialog);
        await viewModel.InitializeAsync(CancellationToken.None);
        await viewModel.RefreshCommand.ExecuteAsync();
        viewModel.SelectedEditorTarget = viewModel.EditorTargets.Single(item => item.Identifier == "SERIAL-B");
        viewModel.EditorKind = ScriptStepKind.Tap;

        await viewModel.CaptureTapCommand.ExecuteAsync();

        Assert.AreEqual(1, dialog.Calls.Count);
        Assert.AreEqual("SERIAL-B", dialog.Calls[0].Serial);
        Assert.AreEqual(@"C:\MEmu\adb.exe", dialog.Calls[0].AdbPath);
    }

    [TestMethod]
    public async Task AndroidCapture_FailureLeavesFieldsUnchangedAndRestoresEditorState()
    {
        var android = new FixedAndroidDeviceService([AndroidDevice("SERIAL-A")]);
        var dialog = new RecordingAndroidCoordinateCaptureDialog(_ =>
            throw new InvalidOperationException("error: device offline"));
        var viewModel = CreateViewModel(
            new RecordingScriptStore(),
            new ImmediateEngine(),
            androidDeviceService: android,
            androidStateProbe: android,
            adbPathDiscovery: new ValidAdbPathDiscovery(),
            androidCoordinateCaptureDialogService: dialog);
        await viewModel.InitializeAsync(CancellationToken.None);
        await viewModel.RefreshCommand.ExecuteAsync();
        viewModel.EditorKind = ScriptStepKind.Tap;
        viewModel.EditorX = 41;
        viewModel.EditorY = 42;

        await viewModel.CaptureTapCommand.ExecuteAsync();

        Assert.AreEqual(41, viewModel.EditorX);
        Assert.AreEqual(42, viewModel.EditorY);
        Assert.IsFalse(viewModel.IsCapturing);
        Assert.IsTrue(viewModel.CanChangeSelection);
        StringAssert.Contains(viewModel.StatusMessage, "device offline");
    }

    [DataTestMethod]
    [DataRow(AndroidConnectionState.Unauthorized)]
    [DataRow(AndroidConnectionState.Offline)]
    public async Task AndroidCapture_IsUnavailableForUnauthorizedOrOfflineDevice(AndroidConnectionState state)
    {
        var device = AndroidDevice("SERIAL-A", state);
        var android = new FixedAndroidDeviceService([device]);
        var dialog = new RecordingAndroidCoordinateCaptureDialog(_ =>
            new AndroidCoordinateCaptureResult(new CapturedTap(1, 2)));
        var viewModel = CreateViewModel(
            new RecordingScriptStore(),
            new ImmediateEngine(),
            androidDeviceService: android,
            androidStateProbe: android,
            adbPathDiscovery: new ValidAdbPathDiscovery(),
            androidCoordinateCaptureDialogService: dialog);
        await viewModel.InitializeAsync(CancellationToken.None);
        await viewModel.RefreshCommand.ExecuteAsync();
        viewModel.EditorKind = ScriptStepKind.Tap;

        Assert.IsFalse(viewModel.CaptureTapCommand.CanExecute(null));
        await viewModel.CaptureTapCommand.ExecuteAsync();
        Assert.AreEqual(0, dialog.Calls.Count);
    }

    [TestMethod]
    public async Task EditorTargetRefresh_DoesNotSilentlyRetargetDisconnectedAndroidSelection()
    {
        var android = new MutableAndroidDeviceService([AndroidDevice("SERIAL-A")]);
        var dialog = new RecordingAndroidCoordinateCaptureDialog(_ =>
            new AndroidCoordinateCaptureResult(new CapturedTap(1, 2)));
        var viewModel = CreateViewModel(
            new RecordingScriptStore(),
            new ImmediateEngine(),
            androidDeviceService: android,
            androidStateProbe: android,
            adbPathDiscovery: new ValidAdbPathDiscovery(),
            androidCoordinateCaptureDialogService: dialog);
        await viewModel.InitializeAsync(CancellationToken.None);
        await viewModel.RefreshCommand.ExecuteAsync();
        Assert.AreEqual("SERIAL-A", viewModel.SelectedEditorTarget!.Identifier);

        android.Devices = [AndroidDevice("SERIAL-B")];
        await viewModel.RefreshCommand.ExecuteAsync();
        viewModel.EditorKind = ScriptStepKind.Tap;

        Assert.IsNull(viewModel.SelectedEditorTarget);
        Assert.IsFalse(viewModel.CaptureTapCommand.CanExecute(null));
        Assert.AreEqual(0, dialog.Calls.Count);
        StringAssert.Contains(viewModel.StatusMessage, "chọn lại");
    }

    [TestMethod]
    public async Task EditorTargetRefresh_RebindsSelectedMemuToFreshProcessMetadata()
    {
        var instances = new MutableInstanceService(
            [new MemuInstance(2, "Target", true, 100, 1000)]);
        var viewModel = CreateViewModel(
            new RecordingScriptStore(),
            new ImmediateEngine(),
            instanceService: instances);
        await viewModel.InitializeAsync(CancellationToken.None);
        await viewModel.RefreshCommand.ExecuteAsync();
        Assert.AreEqual(100, viewModel.SelectedInstance!.ProcessId);

        instances.Instances = [new MemuInstance(2, "Target", true, 200, 2000)];
        await viewModel.RefreshCommand.ExecuteAsync();

        Assert.AreEqual(200, viewModel.SelectedInstance!.ProcessId);
        Assert.AreEqual(2000L, viewModel.SelectedInstance.WindowHandle);
        Assert.AreSame(viewModel.SelectedEditorTarget!.Model, viewModel.SelectedInstance);
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
        await viewModel.DeleteStepCommand.ExecuteAsync();

        Assert.AreSame(originalStep, viewModel.SelectedStep);
        Assert.AreEqual(ScriptStepKind.Tap, viewModel.EditorKind);
        Assert.IsFalse(viewModel.CanChangeSelection);
        Assert.IsFalse(viewModel.CanSelectEditorTarget);
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

        Assert.IsFalse(viewModel.SaveStepCommand.CanExecute(null));
        await viewModel.SaveStepCommand.ExecuteAsync();

        Assert.AreSame(originalModel, viewModel.SelectedStep.Model);
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

    private static AndroidAdbDevice AndroidDevice(
        string serial,
        AndroidConnectionState state = AndroidConnectionState.Device) =>
        new(serial, "Xiaomi", "Redmi 9C", "10", 29, 720, 1600, 320, 0, state);

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(System.IO.Path.Combine(current.FullName, "MEmuScriptStudio.sln")))
            current = current.Parent;
        return current?.FullName ?? throw new DirectoryNotFoundException();
    }

    [TestMethod]
    public async Task BulkAssignmentKeepsTheAcceptedOperationSelection()
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
        viewModel.ControlCenterSelectedScript = viewModel.Scripts.Single(item => item.Id == second.Id);
        foreach (var target in viewModel.RunTargets) target.IsSelected = true;

        await viewModel.AssignScriptToSelectedCommand.ExecuteAsync();

        Assert.IsTrue(viewModel.RunTargets.All(item => item.AssignedScriptId == second.Id));
        Assert.IsTrue(viewModel.RunTargets.All(item => item.IsSelected));
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
        await WaitUntilAsync(() => viewModel.LatestRunResult is not null);

        var requests = engine.Requests.OrderBy(item => item.InstanceIndex).ToList();
        Assert.AreEqual(first.Id, requests[0].Script.Id);
        Assert.AreEqual(second.Id, requests[1].Script.Id);
        StringAssert.Contains(viewModel.LatestRunResult!.RunDescription, "Kịch bản riêng theo thiết bị");
        StringAssert.Contains(viewModel.LatestRunResult.RunDescription, "Script A");
        StringAssert.Contains(viewModel.LatestRunResult.RunDescription, "Script B");
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
    public async Task RunControlBulkAssignmentUsesOnlyRunSelection()
    {
        var first = new ScriptDefinition { Name = "Script A", Steps = { new NoteStep { Name = "A" } } };
        var second = new ScriptDefinition { Name = "Script B", Steps = { new NoteStep { Name = "B" } } };
        var instances = new[]
        {
            new MemuInstance(0, "VM 0", true, 100, 1000),
            new MemuInstance(1, "VM 1", true, 101, 1001)
        };
        var viewModel = CreateViewModel(new RecordingScriptStore([first, second]), new ImmediateEngine(),
            instanceService: new FixedInstanceService(instances));
        await viewModel.InitializeAsync(CancellationToken.None);
        await viewModel.RefreshCommand.ExecuteAsync();
        viewModel.ScriptAssignmentMode = ScriptAssignmentMode.PerInstance;
        viewModel.ControlCenterSelectedScript = viewModel.Scripts.Single(item => item.Id == second.Id);
        viewModel.RunTargets.Single(item => item.Index == 1).IsSelected = true;

        await viewModel.AssignScriptToSelectedCommand.ExecuteAsync();

        Assert.IsNull(viewModel.RunTargets.Single(item => item.Index == 0).AssignedScriptId);
        Assert.AreEqual(second.Id, viewModel.RunTargets.Single(item => item.Index == 1).AssignedScriptId);
        Assert.IsFalse(viewModel.RunTargets.Single(item => item.Index == 0).IsSelected);
        Assert.IsTrue(viewModel.RunTargets.Single(item => item.Index == 1).IsSelected);
        Assert.IsTrue(viewModel.AssignScriptToSelectedCommand.CanExecute(null));
    }

    [TestMethod]
    public async Task BulkAssignment_DeselectedTargetsAreNotAssignedAgain()
    {
        var first = new ScriptDefinition { Name = "Script A", Steps = { new NoteStep { Name = "A" } } };
        var second = new ScriptDefinition { Name = "Script B", Steps = { new NoteStep { Name = "B" } } };
        var instances = Enumerable.Range(0, 6)
            .Select(index => new MemuInstance(index, $"VM {index}", true, 100 + index))
            .ToArray();
        var viewModel = CreateViewModel(
            new RecordingScriptStore([first, second]), new ImmediateEngine(),
            instanceService: new FixedInstanceService(instances));
        await viewModel.InitializeAsync(CancellationToken.None);
        await viewModel.RefreshCommand.ExecuteAsync();
        viewModel.ScriptAssignmentMode = ScriptAssignmentMode.PerInstance;
        viewModel.ControlCenterSelectedScript = viewModel.Scripts.Single(item => item.Id == second.Id);
        foreach (var target in viewModel.RunTargets) target.IsSelected = true;
        viewModel.RunTargets.Single(item => item.Index == 1).IsSelected = false;
        viewModel.RunTargets.Single(item => item.Index == 4).IsSelected = false;

        await viewModel.AssignScriptToSelectedCommand.ExecuteAsync();

        Assert.IsTrue(viewModel.RunTargets.Where(item => item.Index is not (1 or 4))
            .All(item => item.AssignedScriptId == second.Id));
        Assert.IsTrue(viewModel.RunTargets.Where(item => item.Index is 1 or 4)
            .All(item => item.AssignedScriptId is null));
    }

    [TestMethod]
    public async Task RunSelected_PreflightHandlesTargetStoppedAfterSelectionAndSnapshotsUnavailable()
    {
        var engine = new ReportingMultiEngine();
        var instances = new[]
        {
            new MemuInstance(1, "Running", true, 101, 1001),
            new MemuInstance(2, "Stops after selection", true, 102, 1002)
        };
        var instanceService = new MutableInstanceService(instances);
        var viewModel = CreateViewModel(
            new RecordingScriptStore(), engine, instanceService: instanceService);
        await viewModel.InitializeAsync(CancellationToken.None);
        await viewModel.RefreshCommand.ExecuteAsync();
        foreach (var target in viewModel.RunTargets) target.IsSelected = true;
        instanceService.Instances =
        [
            instances[0],
            new MemuInstance(2, "Stops after selection", false, 0, 0)
        ];

        await viewModel.RunCommand.ExecuteAsync();
        await WaitUntilAsync(() => viewModel.LatestRunResult is not null);

        CollectionAssert.AreEqual(new[] { 1 }, engine.Requests.Select(request => request.InstanceIndex).ToArray());
        Assert.AreEqual(2, viewModel.LatestRunResult!.TotalInstanceCount);
        Assert.AreEqual(1, viewModel.LatestRunResult.SucceededCount);
        Assert.AreEqual(0, viewModel.LatestRunResult.FailedCount);
        Assert.AreEqual(1, viewModel.LatestRunResult.UnavailableCount);
        Assert.AreEqual(2, viewModel.LatestRunResult.IssueInstances.Single().Index);
        Assert.IsFalse(viewModel.RunTargets.Single(item => item.Index == 1).IsSelected);
        Assert.IsFalse(viewModel.RunTargets.Single(item => item.Index == 2).IsSelected);
    }

    [TestMethod]
    public async Task RefreshTargets_PreservesRunSelectionAndPersistedScriptAssignment()
    {
        var first = new ScriptDefinition { Name = "Script A", Steps = { new NoteStep { Name = "A" } } };
        var second = new ScriptDefinition { Name = "Script B", Steps = { new NoteStep { Name = "B" } } };
        var settings = new RecordingRunSettingsStore(new ApplicationSettings { MemucPath = @"C:\MEmu\memuc.exe" });
        var instances = new MutableInstanceService(
        [
            new MemuInstance(0, "VM 0", true, 100, 1000),
            new MemuInstance(1, "VM 1", true, 101, 1001)
        ]);
        var viewModel = CreateViewModel(
            new RecordingScriptStore([first, second]),
            new ImmediateEngine(),
            instanceService: instances,
            settingsStore: settings);
        await viewModel.InitializeAsync(CancellationToken.None);
        await viewModel.RefreshCommand.ExecuteAsync();
        viewModel.ScriptAssignmentMode = ScriptAssignmentMode.PerInstance;
        var target = viewModel.RunTargets.Single(item => item.Index == 1);
        target.IsSelected = true;
        target.AssignedScriptId = second.Id;
        await WaitUntilAsync(() => settings.LastSaved?.MultiInstanceRun.ScriptAssignments.GetValueOrDefault(1) == second.Id);

        instances.Instances =
        [
            new MemuInstance(0, "VM 0 refreshed", true, 200, 2000),
            new MemuInstance(1, "VM 1 refreshed", true, 201, 2001),
            new MemuInstance(2, "VM 2", true, 202, 2002)
        ];

        await viewModel.RefreshCommand.ExecuteAsync();

        Assert.IsTrue(viewModel.RunTargets.Single(item => item.Index == 1).IsSelected);
        Assert.AreEqual(second.Id, viewModel.RunTargets.Single(item => item.Index == 1).AssignedScriptId);
        Assert.IsFalse(viewModel.RunTargets.Single(item => item.Index == 0).IsSelected);
        Assert.IsNull(viewModel.RunTargets.Single(item => item.Index == 2).AssignedScriptId);
    }

    [TestMethod]
    public void AppSurface_HasNoPageOrderBindingsCommandsOrWindowLayoutDependency()
    {
        var layoutMembers = typeof(MainViewModel)
            .GetMembers()
            .Where(member => member.Name.Contains("Layout", StringComparison.Ordinal) ||
                             member.Name.Contains("Geometry", StringComparison.Ordinal) ||
                             member.Name.Contains("ArrangeGrid", StringComparison.Ordinal) ||
                             member.Name.Contains("ReturnToGrid", StringComparison.Ordinal))
            .Select(member => member.Name)
            .Distinct()
            .ToArray();

        Assert.IsTrue(layoutMembers.All(name => name.Contains("ControlCenterLayout", StringComparison.Ordinal)),
            $"Only Control Center UI layout members may remain: {string.Join(", ", layoutMembers)}");
        Assert.IsNull(typeof(InstanceTargetItemViewModel).GetProperty("IsLayoutSelected"));
        Assert.IsFalse(typeof(MainViewModel).GetConstructors()
            .SelectMany(constructor => constructor.GetParameters())
            .Any(parameter => parameter.ParameterType.FullName == "MEmuScriptStudio.Core.MEmu.IMemuWindowLayoutService"));
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        while (!condition() && DateTime.UtcNow < deadline) await Task.Delay(10);
        Assert.IsTrue(condition(), "Condition was not reached before timeout.");
    }

    private static KeyEventArgs CreatePreviewKeyEvent(Visual root, Key key) => new(
        Keyboard.PrimaryDevice,
        new TestPresentationSource { RootVisual = root },
        Environment.TickCount,
        key)
    {
        RoutedEvent = Keyboard.PreviewKeyDownEvent
    };

    private static void AssertDurationBinding(
        DurationInputControl control,
        string millisecondsProperty,
        string validityProperty,
        string? refreshTokenProperty = null)
    {
        var totalBinding = BindingOperations.GetBinding(control, DurationInputControl.TotalMillisecondsProperty);
        Assert.IsNotNull(totalBinding);
        Assert.AreEqual(millisecondsProperty, totalBinding.Path.Path);
        Assert.AreEqual(BindingMode.TwoWay, totalBinding.Mode);

        var validityBinding = BindingOperations.GetBinding(control, DurationInputControl.IsInputValidProperty);
        Assert.IsNotNull(validityBinding);
        Assert.AreEqual(validityProperty, validityBinding.Path.Path);
        Assert.AreEqual(BindingMode.OneWayToSource, validityBinding.Mode);

        var refreshBinding = BindingOperations.GetBinding(control, DurationInputControl.RefreshTokenProperty);
        if (refreshTokenProperty is null)
            Assert.IsNull(refreshBinding);
        else
        {
            Assert.IsNotNull(refreshBinding);
            Assert.AreEqual(refreshTokenProperty, refreshBinding.Path.Path);
            Assert.AreEqual(BindingMode.OneWay, refreshBinding.Mode);
        }
    }

    [TestMethod]
    public async Task ScriptImport_NormalizesLegacyDelayNameWhenImportedLibraryIsSaved()
    {
        var incoming = new ScriptDefinition
        {
            Name = "Imported Delay",
            Steps = [new DelayStep { Name = "Tên cũ trong JSON", DurationMilliseconds = 100_000 }]
        };
        var store = new RecordingScriptStore([CreateThreeStepScript()]);
        var viewModel = CreateViewModel(
            store,
            new ImmediateEngine(),
            fileDialog: new RecordingFileDialog(@"C:\Temp\delay.memuscript", exportPath: null),
            transfer: new RecordingScriptTransferService([incoming]),
            importConflict: new FixedImportConflict(ScriptImportConflictResolution.CreateCopy));
        await viewModel.InitializeAsync(CancellationToken.None);

        await viewModel.ImportScriptsCommand.ExecuteAsync();

        var imported = viewModel.Scripts.Single(script => script.Name == "Imported Delay").Model;
        Assert.AreEqual("Chờ", imported.Steps.Single().Name);
        Assert.AreEqual("Chờ · 1 phút 40 giây",
            new StepItemViewModel(imported.Steps.Single()).DisplayName);
        Assert.AreEqual("Chờ", store.LastSaved.Single(script => script.Name == "Imported Delay").Steps.Single().Name);
    }

    [TestMethod]
    public async Task DelayLegacyNameDisplaysFromDurationAndNormalizesOnlyWhenSaved()
    {
        var delay = new DelayStep { Name = "Tên tùy chỉnh cũ", DurationMilliseconds = 100_000 };
        var store = new RecordingScriptStore([new ScriptDefinition { Name = "Legacy", Steps = [delay] }]);
        var viewModel = CreateViewModel(store, new ImmediateEngine());

        await viewModel.InitializeAsync(CancellationToken.None);

        Assert.AreEqual("Tên tùy chỉnh cũ", delay.Name);
        Assert.AreEqual("Chờ · 1 phút 40 giây", viewModel.SelectedStep!.DisplayName);
        Assert.AreEqual("Chờ", viewModel.EditorName);
        Assert.IsFalse(viewModel.ShowStepName);

        viewModel.EditorDelayMilliseconds = 3_723_400;
        Assert.AreEqual("Chờ · 1 giờ 2 phút 3 giây 400 ms", viewModel.SelectedStep.DisplayName);
        Assert.IsTrue(viewModel.SaveStepCommand.CanExecute(null));
        await viewModel.SaveStepCommand.ExecuteAsync();

        Assert.AreEqual("Tên tùy chỉnh cũ", delay.Name,
            "Saving replaces the edited model and does not need to mutate a detached legacy object reference.");
        Assert.AreEqual("Chờ", viewModel.SelectedStep.Model.Name);
        Assert.AreEqual("Chờ", store.LastSaved.Single().Steps.Single().Name);
        Assert.AreEqual("Chờ · 1 giờ 2 phút 3 giây 400 ms", viewModel.SelectedStep.DisplayName);
    }

    [TestMethod]
    public async Task StepKindTransitionUsesCanonicalDelayAndDefaultNameWhenLeavingDelay()
    {
        var viewModel = CreateViewModel(
            new RecordingScriptStore([new ScriptDefinition
            {
                Name = "Kinds",
                Steps = [new NoteStep { Name = "Ghi chú riêng", Text = "Nội dung" }]
            }]),
            new ImmediateEngine());
        await viewModel.InitializeAsync(CancellationToken.None);

        viewModel.EditorKind = ScriptStepKind.Delay;
        Assert.AreEqual("Chờ", viewModel.EditorName);
        Assert.IsFalse(viewModel.ShowStepName);
        Assert.AreEqual("Chờ · 1 giây", viewModel.SelectedStep!.DisplayName);

        viewModel.EditorKind = ScriptStepKind.Tap;
        Assert.AreEqual("Chạm", viewModel.EditorName);
        Assert.IsTrue(viewModel.ShowStepName);
        Assert.AreEqual("Ghi chú riêng", viewModel.SelectedStep.DisplayName,
            "A non-Delay draft must keep the persisted non-Delay display until it is saved.");
    }

    [TestMethod]
    public async Task StepEditor_DefaultAndUserSelectedKindNamesDoNotOverwriteHydratedCustomName()
    {
        var custom = new NoteStep { Name = "Tên tùy chỉnh", Text = "Nội dung" };
        var viewModel = CreateViewModel(
            new RecordingScriptStore([new ScriptDefinition { Name = "Names", Steps = [custom] }]),
            new ImmediateEngine());
        await viewModel.InitializeAsync(CancellationToken.None);

        Assert.AreEqual("Tên tùy chỉnh", viewModel.EditorName,
            "Hydration must preserve the persisted custom name.");

        await viewModel.NewStepCommand.ExecuteAsync();
        Assert.AreEqual(ScriptStepDisplayName.GetDefaultName(ScriptStepKind.ForceStop), viewModel.EditorName);
        Assert.IsFalse(viewModel.StepKinds.Contains(ScriptStepKind.AndroidShell));

        viewModel.EditorKind = ScriptStepKind.InputText;
        Assert.AreEqual("Nhập văn bản", viewModel.EditorName);
        viewModel.EditorKind = ScriptStepKind.OpenApp;
        Assert.AreEqual("Mở ứng dụng", viewModel.EditorName);
    }

    [TestMethod]
    public async Task StepSave_CanonicalizesTrimAndFallbackAcrossModelEditorAndBaseline()
    {
        var step = new TapStep { Name = "Tên cũ", X = 10, Y = 20 };
        var store = new RecordingScriptStore([new ScriptDefinition { Name = "Canonical", Steps = [step] }]);
        var viewModel = CreateViewModel(store, new ImmediateEngine());
        await viewModel.InitializeAsync(CancellationToken.None);

        viewModel.EditorName = "   Chạm tùy chỉnh   ";
        await viewModel.SaveStepCommand.ExecuteAsync();

        Assert.AreEqual("Chạm tùy chỉnh", viewModel.SelectedStep!.Model.Name);
        Assert.AreEqual("Chạm tùy chỉnh", viewModel.EditorName);
        Assert.IsFalse(viewModel.IsEditorDirty);

        viewModel.EditorName = "   ";
        await viewModel.SaveStepCommand.ExecuteAsync();

        Assert.AreEqual("Chạm", viewModel.SelectedStep.Model.Name);
        Assert.AreEqual("Chạm", viewModel.EditorName);
        Assert.AreEqual("Chạm", store.LastSaved.Single().Steps.Single().Name);
        Assert.IsFalse(viewModel.IsEditorDirty);
    }

    [TestMethod]
    public async Task AddStep_DoesNotOverwriteNewCreateDraftEnteredWhileSaveIsPending()
    {
        var store = new BlockingSaveScriptStore([new ScriptDefinition { Name = "Add race" }]);
        var viewModel = CreateViewModel(store, new ImmediateEngine());
        await viewModel.InitializeAsync(CancellationToken.None);
        await viewModel.NewStepCommand.ExecuteAsync();
        viewModel.EditorKind = ScriptStepKind.InputText;
        viewModel.EditorText = "first snapshot";

        var addTask = viewModel.AddStepCommand.ExecuteAsync();
        await store.SaveStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.IsFalse(viewModel.CancelStepCreateCommand.CanExecute(null));

        viewModel.EditorText = "second draft";
        store.ReleaseSave.TrySetResult();
        await addTask;

        Assert.AreEqual(1, viewModel.Steps.Count);
        Assert.AreEqual("first snapshot", ((InputTextStep)viewModel.Steps.Single().Model).Text);
        Assert.AreEqual(RegularStepEditorMode.Create, viewModel.StepEditorMode);
        Assert.IsNull(viewModel.SelectedStep);
        Assert.AreEqual("second draft", viewModel.EditorText);
        Assert.IsTrue(viewModel.IsEditorDirty);
        Assert.IsTrue(viewModel.AddStepCommand.CanExecute(null));
        StringAssert.Contains(viewModel.StatusMessage, "còn thay đổi mới");
    }

    [TestMethod]
    public async Task FailedSaveAfterNameCanonicalizationRecomputesSemanticDirty()
    {
        var step = new TapStep { Name = "A", X = 10, Y = 20 };
        var store = new RecordingScriptStore([new ScriptDefinition { Name = "Failure", Steps = [step] }])
        {
            ThrowOnSave = true
        };
        var viewModel = CreateViewModel(store, new ImmediateEngine());
        await viewModel.InitializeAsync(CancellationToken.None);
        viewModel.EditorName = "  A  ";

        await viewModel.SaveStepCommand.ExecuteAsync();

        Assert.AreEqual("A", viewModel.SelectedStep!.Model.Name);
        Assert.AreEqual("A", viewModel.EditorName);
        Assert.IsFalse(viewModel.IsEditorDirty);
        Assert.IsFalse(viewModel.SaveStepCommand.CanExecute(null));
    }

    [STATestMethod]
    public async Task MainWindow_AddAndSaveButtonsTrackInputTextValidityImmediately()
    {
        if (Application.Current is null)
        {
            var application = new MEmuScriptStudio.App.App();
            application.InitializeComponent();
        }

        var viewModel = CreateViewModel(
            new RecordingScriptStore([new ScriptDefinition { Name = "Buttons" }]),
            new ImmediateEngine());
        await viewModel.InitializeAsync(CancellationToken.None);
        var window = new MainWindow(viewModel);
        try
        {
            await viewModel.NewStepCommand.ExecuteAsync();
            viewModel.EditorKind = ScriptStepKind.InputText;
            DrainDataBindings();

            var input = (TextBox)window.FindName("EditorInputTextBox");
            var add = (Button)window.FindName("AddStepButton");
            var save = (Button)window.FindName("SaveStepButton");

            Assert.IsFalse(add.IsEnabled);
            input.Text = "aaaaaaaa";
            DrainDataBindings();
            Assert.IsTrue(add.IsEnabled, "The real Add button must refresh when an already-dirty draft becomes valid.");

            input.Text = string.Empty;
            DrainDataBindings();
            Assert.IsFalse(add.IsEnabled, "The real Add button must refresh on valid-to-invalid transitions.");

            input.Text = "persisted";
            DrainDataBindings();
            await viewModel.AddStepCommand.ExecuteAsync();
            DrainDataBindings();

            input.Text = string.Empty;
            DrainDataBindings();
            Assert.IsFalse(save.IsEnabled, "The real Save button must disable for an invalid edit draft.");

            input.Text = "edited";
            DrainDataBindings();
            Assert.IsTrue(save.IsEnabled, "The real Save button must re-enable without requiring Ctrl+S.");
        }
        finally
        {
            viewModel.HasEditorBindingErrors = false;
            window.Close();
        }
    }

    [STATestMethod]
    public async Task CompositeSaveButtonTracksReferenceValidityWhileDirtyStateStaysTrue()
    {
        if (Application.Current is null)
        {
            var application = new MEmuScriptStudio.App.App();
            application.InitializeComponent();
        }

        var first = new ScriptDefinition { Name = "First", Steps = [new NoteStep { Name = "A", Text = "A" }] };
        var second = new ScriptDefinition { Name = "Second", Steps = [new NoteStep { Name = "B", Text = "B" }] };
        var composite = new ScriptDefinition
        {
            Name = "Composite",
            Kind = ScriptKind.Composite,
            CompositeItems = [new ScriptReferenceItem { ScriptId = first.Id }]
        };
        var viewModel = CreateViewModel(new RecordingScriptStore([first, second, composite]), new ImmediateEngine());
        await viewModel.InitializeAsync(CancellationToken.None);
        await viewModel.NavigateToScriptAsync(viewModel.Scripts.Single(script => script.Id == composite.Id));
        var window = new MainWindow(viewModel);
        try
        {
            DrainDataBindings();
            var save = (Button)window.FindName("SaveCompositeItemButton");

            viewModel.CompositeContinueOnFailure = true;
            DrainDataBindings();
            Assert.IsTrue(save.IsEnabled);

            viewModel.CompositeReferenceScript = null;
            DrainDataBindings();
            Assert.IsFalse(save.IsEnabled);

            viewModel.CompositeReferenceScript = viewModel.RegularScripts.Single(script => script.Id == second.Id);
            DrainDataBindings();
            Assert.IsTrue(save.IsEnabled);

            viewModel.HasEditorBindingErrors = true;
            DrainDataBindings();
            Assert.IsFalse(save.IsEnabled);

            viewModel.HasEditorBindingErrors = false;
            DrainDataBindings();
            Assert.IsTrue(save.IsEnabled);
        }
        finally { window.Close(); }
    }

    [TestMethod]
    public async Task CommandPreviewUsesCreateEditAndPendingDelayDraftsWithoutPersistedFallback()
    {
        var input = new InputTextStep { Name = "Input", Text = "persisted" };
        var delay = new DelayStep { Name = "Chờ", DurationMilliseconds = 1_000 };
        var viewModel = CreateViewModel(
            new RecordingScriptStore([new ScriptDefinition { Name = "Preview", Steps = [input, delay] }]),
            new ImmediateEngine(),
            instanceService: new FixedInstanceService([new MemuInstance(3, "Three", true, 103)]));
        await viewModel.InitializeAsync(CancellationToken.None);
        await viewModel.RefreshCommand.ExecuteAsync();

        viewModel.EditorText = "edit-draft";
        StringAssert.Contains(viewModel.CommandPreview, "edit-draft");
        Assert.AreEqual("persisted", input.Text);

        await viewModel.NewStepCommand.ExecuteAsync();
        viewModel.EditorKind = ScriptStepKind.InputText;
        viewModel.EditorText = "create-draft";
        StringAssert.Contains(viewModel.CommandPreview, "create-draft");

        viewModel.EditorText = string.Empty;
        StringAssert.Contains(viewModel.CommandPreview, "không hợp lệ");

        viewModel.EditorText = "valid";
        Assert.IsTrue(await viewModel.NavigateToStepAsync(viewModel.Steps.Single(step => step.Model is DelayStep)));
        viewModel.EditorDelayMilliseconds = 9_000;
        StringAssert.Contains(viewModel.CommandPreview, "9 giây");
        Assert.AreEqual(1_000, ((DelayStep)viewModel.SelectedStep!.Model).DurationMilliseconds);
    }

    [STATestMethod]
    public async Task CtrlSAndSharedRunStateRejectVisibleInvalidBindingThenRecover()
    {
        if (Application.Current is null)
        {
            var application = new MEmuScriptStudio.App.App();
            application.InitializeComponent();
        }

        var step = new TapStep { Name = "Tap", X = 1, Y = 2, TimeoutSeconds = 30 };
        var store = new RecordingScriptStore([new ScriptDefinition { Name = "Boundary", Steps = [step] }]);
        var viewModel = CreateViewModel(
            store,
            new ImmediateEngine(),
            instanceService: new FixedInstanceService([new MemuInstance(1, "One", true, 101)]));
        await viewModel.InitializeAsync(CancellationToken.None);
        await viewModel.RefreshCommand.ExecuteAsync();
        viewModel.RunTargets.Single().IsSelected = true;
        viewModel.EditorName = "Tap draft";

        var window = new MainWindow(viewModel);
        var timeout = (TextBox)window.FindName("EditorTimeoutTextBox");
        BindingOperations.SetBinding(timeout, TextBox.TextProperty, new Binding(nameof(MainViewModel.EditorTimeoutSeconds))
        {
            Source = viewModel,
            Mode = BindingMode.TwoWay,
            UpdateSourceTrigger = UpdateSourceTrigger.Explicit,
            ValidatesOnExceptions = true
        });
        try
        {
            DrainDataBindings();
            timeout.Text = "abc";
            var invalidSave = CreatePreviewKeyEvent(window, Key.S);
            window.HandleWindowPreviewKeyDownAsync(invalidSave, ModifierKeys.Control, timeout)
                .GetAwaiter().GetResult();

            Assert.AreEqual(0, store.SaveCount);
            Assert.AreEqual(30, viewModel.EditorTimeoutSeconds, "The last valid VM value must not be saved.");
            Assert.IsTrue(viewModel.HasEditorBindingErrors);
            Assert.IsFalse(viewModel.RunCommand.CanExecute(null),
                "Control Center shares this command and must see the visible MainWindow validation error.");

            timeout.Text = "30";
            var validSave = CreatePreviewKeyEvent(window, Key.S);
            window.HandleWindowPreviewKeyDownAsync(validSave, ModifierKeys.Control, timeout)
                .GetAwaiter().GetResult();
            DrainDataBindings();

            Assert.AreEqual(1, store.SaveCount);
            Assert.IsFalse(viewModel.HasEditorBindingErrors);
            Assert.AreEqual("Tap draft", viewModel.SelectedStep!.Model.Name);
            Assert.IsTrue(viewModel.RunCommand.CanExecute(null));
        }
        finally
        {
            viewModel.HasEditorBindingErrors = false;
            window.Close();
        }
    }

    [TestMethod]
    public async Task InvalidDraftBlocksKindTransitionUntilValidationIsCorrected()
    {
        var viewModel = CreateViewModel(
            new RecordingScriptStore([new ScriptDefinition
            {
                Name = "Invalid kind transition",
                Steps = [new DelayStep { Name = "Chờ", DurationMilliseconds = 1_000 }]
            }]),
            new ImmediateEngine());
        await viewModel.InitializeAsync(CancellationToken.None);

        viewModel.IsEditorDelayInputValid = false;
        viewModel.EditorKind = ScriptStepKind.Tap;
        Assert.AreEqual(ScriptStepKind.Delay, viewModel.EditorKind);

        viewModel.IsEditorDelayInputValid = true;
        viewModel.HasEditorBindingErrors = true;
        viewModel.EditorKind = ScriptStepKind.Tap;
        Assert.AreEqual(ScriptStepKind.Delay, viewModel.EditorKind);

        viewModel.HasEditorBindingErrors = false;
        viewModel.EditorKind = ScriptStepKind.Tap;
        Assert.AreEqual(ScriptStepKind.Tap, viewModel.EditorKind);
        Assert.AreEqual("Chạm", viewModel.EditorName);
    }

    [TestMethod]
    public async Task CreatingDelayPersistsOnlyCanonicalNameAndDuration()
    {
        var store = new RecordingScriptStore([new ScriptDefinition { Name = "Create" }]);
        var viewModel = CreateViewModel(store, new ImmediateEngine());
        await viewModel.InitializeAsync(CancellationToken.None);

        await viewModel.NewStepCommand.ExecuteAsync();
        viewModel.EditorKind = ScriptStepKind.Delay;
        viewModel.EditorDelayMilliseconds = 3_000;
        await viewModel.AddStepCommand.ExecuteAsync();

        var delay = (DelayStep)viewModel.SelectedStep!.Model;
        Assert.AreEqual("Chờ", delay.Name);
        Assert.AreEqual(3_000, delay.DurationMilliseconds);
        Assert.AreEqual("Chờ · 3 giây", viewModel.SelectedStep.DisplayName);
        Assert.AreEqual("Chờ", store.LastSaved.Single().Steps.Single().Name);
    }

    [TestMethod]
    public async Task DelayCopyPasteDuplicateUndoAndScriptDuplicateKeepCanonicalName()
    {
        var legacy = new DelayStep { Name = "Tên Delay cũ", DurationMilliseconds = 3_000 };
        var store = new RecordingScriptStore([new ScriptDefinition { Name = "Delay flow", Steps = [legacy] }]);
        var viewModel = CreateViewModel(store, new ImmediateEngine());
        await viewModel.InitializeAsync(CancellationToken.None);

        viewModel.CopyStepsCommand.Execute(null);
        await viewModel.PasteStepsCommand.ExecuteAsync();
        Assert.IsTrue(viewModel.Steps.All(step => step.Name == "Chờ"));
        Assert.IsTrue(viewModel.Steps.All(step => step.DisplayName == "Chờ · 3 giây"));

        await viewModel.DuplicateStepCommand.ExecuteAsync();
        Assert.IsTrue(viewModel.Steps.All(step => step.Name == "Chờ"));
        await viewModel.UndoStepListCommand.ExecuteAsync();
        Assert.AreEqual(2, viewModel.Steps.Count);
        Assert.IsTrue(viewModel.Steps.All(step => step.Name == "Chờ"));

        await viewModel.DuplicateScriptCommand.ExecuteAsync();
        Assert.IsTrue(viewModel.SelectedScript!.Model.Steps.All(step => step.Name == "Chờ"));
        Assert.IsTrue(store.LastSaved.SelectMany(script => script.Steps)
            .OfType<DelayStep>().All(step => step.Name == "Chờ"));
    }

    [TestMethod]
    public void CompositeDelayDisplayNameUpdatesFromTheSharedFormatter()
    {
        var item = new CompositeItemViewModel(
            new CompositeDelayItem { DurationMilliseconds = 100_000 },
            _ => null);
        var changed = new List<string?>();
        item.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

        Assert.AreEqual("Chờ · 1 phút 40 giây", item.DisplayName);
        Assert.AreEqual(item.DisplayName, item.Description);

        item.PreviewDelayDuration(3_723_400);

        Assert.AreEqual("Chờ · 1 giờ 2 phút 3 giây 400 ms", item.DisplayName);
        CollectionAssert.Contains(changed, nameof(CompositeItemViewModel.DisplayName));
        CollectionAssert.Contains(changed, nameof(CompositeItemViewModel.Description));
    }

    [TestMethod]
    public async Task InvalidCompositeDelayDraftBlocksScriptMutationsWithoutLeavingDetachedSelection()
    {
        var regular = new ScriptDefinition { Name = "Regular", Steps = [new NoteStep { Name = "N" }] };
        var composite = new ScriptDefinition
        {
            Name = "Composite",
            Kind = ScriptKind.Composite,
            CompositeItems = [new CompositeDelayItem { DurationMilliseconds = 1_000 }]
        };
        var store = new RecordingScriptStore([regular, composite]);
        var viewModel = CreateViewModel(store, new ImmediateEngine(), new ConfigurableConfirmation(false));
        await viewModel.InitializeAsync(CancellationToken.None);
        viewModel.SelectedScript = viewModel.Scripts.Single(script => script.Id == composite.Id);
        var selected = viewModel.SelectedScript;
        viewModel.IsCompositeDelayInputValid = false;

        await viewModel.CreateScriptCommand.ExecuteAsync();
        await viewModel.DuplicateScriptCommand.ExecuteAsync();
        await viewModel.DeleteScriptCommand.ExecuteAsync();

        Assert.AreEqual(2, viewModel.Scripts.Count);
        Assert.AreSame(selected, viewModel.SelectedScript);
        Assert.IsTrue(viewModel.Scripts.Contains(viewModel.SelectedScript));
        Assert.AreEqual(0, store.SaveCount);
        Assert.IsFalse(viewModel.IsCompositeDelayInputValid);
    }

    [STATestMethod]
    public void StepsGrid_EmptySpacePolicyExcludesRowsHeadersCheckboxesAndScrollbars()
    {
        Assert.IsTrue(MainWindow.IsStepsGridEmptySpaceSource(new DataGrid()));
        Assert.IsFalse(MainWindow.IsStepsGridEmptySpaceSource(new DataGridRow()));
        Assert.IsFalse(MainWindow.IsStepsGridEmptySpaceSource(new DataGridColumnHeader()));
        Assert.IsFalse(MainWindow.IsStepsGridEmptySpaceSource(new DataGridColumnHeadersPresenter()));
        Assert.IsFalse(MainWindow.IsStepsGridEmptySpaceSource(new CheckBox()));
        Assert.IsFalse(MainWindow.IsStepsGridEmptySpaceSource(new Button()));
        Assert.IsFalse(MainWindow.IsStepsGridEmptySpaceSource(new ComboBox()));
        Assert.IsFalse(MainWindow.IsStepsGridEmptySpaceSource(new TextBox()));
        Assert.IsFalse(MainWindow.IsStepsGridEmptySpaceSource(new ScrollBar()));
    }

    [STATestMethod]
    public async Task StepsGrid_EmptySpaceKeepsValidDirtyDraftWithoutSavingOrClearing()
    {
        if (Application.Current is null)
        {
            var application = new MEmuScriptStudio.App.App();
            application.InitializeComponent();
        }

        var store = new RecordingScriptStore([CreateThreeStepScript()]);
        var viewModel = CreateViewModel(store, new ImmediateEngine());
        await viewModel.InitializeAsync(CancellationToken.None);
        var window = new MainWindow(viewModel);
        try
        {
            viewModel.EditorName = "Đã commit khi bỏ chọn";

            Assert.IsFalse(await window.TryClearStepSelectionFromEmptyClickAsync(
                (DataGrid)window.FindName("StepsGrid")));
            Assert.IsNotNull(viewModel.SelectedStep);
            Assert.IsTrue(viewModel.IsEditorDirty);
            Assert.AreEqual(0, store.SaveCount);
            Assert.AreEqual("A", viewModel.SelectedStep!.Model.Name);
        }
        finally { window.Close(); }
    }

    [STATestMethod]
    public async Task StepsGrid_EmptySpaceKeepsInvalidDelayDraftAndSelection()
    {
        if (Application.Current is null)
        {
            var application = new MEmuScriptStudio.App.App();
            application.InitializeComponent();
        }

        var script = new ScriptDefinition
        {
            Name = "Delay",
            Steps = [new DelayStep { Name = "Tên cũ", DurationMilliseconds = 100_000 }]
        };
        var store = new RecordingScriptStore([script]);
        var viewModel = CreateViewModel(store, new ImmediateEngine());
        await viewModel.InitializeAsync(CancellationToken.None);
        var selected = viewModel.SelectedStep;
        var window = new MainWindow(viewModel);
        try
        {
            viewModel.EditorDelayMilliseconds = 101_000;
            viewModel.IsEditorDelayInputValid = false;

            Assert.IsFalse(await window.TryClearStepSelectionFromEmptyClickAsync(
                (DataGrid)window.FindName("StepsGrid")));
            Assert.AreSame(selected, viewModel.SelectedStep);
            Assert.IsTrue(viewModel.IsEditorDirty);
            Assert.AreEqual(101_000, viewModel.EditorDelayMilliseconds);
            Assert.AreEqual(100_000, ((DelayStep)script.Steps[0]).DurationMilliseconds);
            Assert.AreEqual(0, store.SaveCount);
            Assert.IsFalse(viewModel.SaveStepCommand.CanExecute(null));
        }
        finally { window.Close(); }
    }

    [TestMethod]
    public async Task RegularEditorState_SeparatesNoneCreateAndEditAndSaveNeverAdds()
    {
        var viewModel = CreateViewModel(
            new RecordingScriptStore([CreateThreeStepScript()]),
            new ImmediateEngine());
        await viewModel.InitializeAsync(CancellationToken.None);
        var originalCount = viewModel.Steps.Count;

        Assert.AreEqual(RegularStepEditorMode.Edit, viewModel.StepEditorMode);
        Assert.IsTrue(viewModel.TryClearStepSelectionFromBlank());
        Assert.AreEqual(RegularStepEditorMode.None, viewModel.StepEditorMode);
        Assert.IsNull(viewModel.SelectedStep);

        await viewModel.SaveStepCommand.ExecuteAsync();
        Assert.AreEqual(originalCount, viewModel.Steps.Count, "Save không bao giờ được tạo bước khi không có selection.");

        await viewModel.NewStepCommand.ExecuteAsync();
        Assert.AreEqual(RegularStepEditorMode.Create, viewModel.StepEditorMode);
        Assert.IsNull(viewModel.SelectedStep);
        Assert.IsFalse(viewModel.SaveStepCommand.CanExecute(null));
        viewModel.EditorKind = ScriptStepKind.Note;
        viewModel.EditorText = "Nội dung mới";
        Assert.IsTrue(viewModel.AddStepCommand.CanExecute(null));

        await viewModel.AddStepCommand.ExecuteAsync();
        Assert.AreEqual(originalCount + 1, viewModel.Steps.Count);
        Assert.AreEqual(RegularStepEditorMode.Edit, viewModel.StepEditorMode);
        Assert.IsNotNull(viewModel.SelectedStep);
    }

    [TestMethod]
    public async Task Navigation_SaveDecisionCommitsValidDraftBeforeSelectingTarget()
    {
        var store = new RecordingScriptStore([CreateThreeStepScript()]);
        var decisions = new DraftDecisionConfirmation(EditorDraftDecision.Save);
        var viewModel = CreateViewModel(store, new ImmediateEngine(), decisions);
        await viewModel.InitializeAsync(CancellationToken.None);
        var edited = viewModel.SelectedStep!;
        var target = viewModel.Steps[1];
        viewModel.EditorName = "A đã lưu";

        Assert.IsTrue(await viewModel.NavigateToStepAsync(target));

        Assert.AreEqual("A đã lưu", edited.Model.Name);
        Assert.AreSame(target, viewModel.SelectedStep);
        Assert.AreEqual(1, store.SaveCount);
        Assert.AreEqual(("Thuộc tính bước", true), decisions.Calls.Single());
    }

    [TestMethod]
    public async Task Navigation_InvalidDraftOffersOnlyDiscardOrCancelAndCancelRetainsError()
    {
        var decisions = new DraftDecisionConfirmation(EditorDraftDecision.Cancel);
        var viewModel = CreateViewModel(
            new RecordingScriptStore([CreateThreeStepScript()]),
            new ImmediateEngine(),
            decisions);
        await viewModel.InitializeAsync(CancellationToken.None);
        var original = viewModel.SelectedStep;
        viewModel.EditorName = "Draft";
        viewModel.HasEditorBindingErrors = true;

        Assert.IsFalse(await viewModel.NavigateToStepAsync(viewModel.Steps[1]));
        Assert.AreSame(original, viewModel.SelectedStep);
        Assert.IsTrue(viewModel.IsEditorDirty);
        Assert.IsTrue(viewModel.HasEditorBindingErrors);
        Assert.IsFalse(decisions.Calls.Single().CanSave);
    }

    [TestMethod]
    public async Task ScriptName_IsDirtyUntilRenameAndCancelRestoresPersistedValue()
    {
        var store = new RecordingScriptStore([CreateThreeStepScript()]);
        var viewModel = CreateViewModel(store, new ImmediateEngine());
        await viewModel.InitializeAsync(CancellationToken.None);
        var persisted = viewModel.SelectedScript!.Name;

        viewModel.ScriptName = "Tên nháp";
        Assert.IsTrue(viewModel.IsScriptNameDirty);
        Assert.AreEqual(0, store.SaveCount);
        viewModel.CancelScriptRenameCommand.Execute(null);
        Assert.AreEqual(persisted, viewModel.ScriptName);
        Assert.IsFalse(viewModel.IsScriptNameDirty);

        viewModel.ScriptName = "Tên đã đổi";
        Assert.IsTrue(viewModel.RenameScriptCommand.CanExecute(null));
        await viewModel.RenameScriptCommand.ExecuteAsync();
        Assert.AreEqual("Tên đã đổi", viewModel.SelectedScript.Name);
        Assert.IsFalse(viewModel.IsScriptNameDirty);
        Assert.AreEqual(1, store.SaveCount);
    }

    [TestMethod]
    public async Task RenameScript_DoesNotOverwriteNewDraftEnteredWhileSaveIsPending()
    {
        var store = new BlockingSaveScriptStore([CreateThreeStepScript()]);
        var viewModel = CreateViewModel(store, new ImmediateEngine());
        await viewModel.InitializeAsync(CancellationToken.None);

        viewModel.ScriptName = "First saved name";
        var renameTask = viewModel.RenameScriptCommand.ExecuteAsync();
        await store.SaveStarted.Task;
        viewModel.ScriptName = "Second draft name";
        store.ReleaseSave.TrySetResult();
        await renameTask;

        Assert.AreEqual("First saved name", viewModel.SelectedScript!.Name);
        Assert.AreEqual("Second draft name", viewModel.ScriptName);
        Assert.IsTrue(viewModel.IsScriptNameDirty);
    }

    [TestMethod]
    public async Task UnsavedRegularOrCompositeDraftBlocksRunWithExplicitReason()
    {
        var regular = CreateThreeStepScript();
        var child = new ScriptDefinition { Name = "Child", Steps = [new NoteStep { Name = "N" }] };
        var composite = new ScriptDefinition
        {
            Name = "Composite",
            Kind = ScriptKind.Composite,
            CompositeItems = [new ScriptReferenceItem { ScriptId = child.Id }]
        };
        var viewModel = CreateViewModel(
            new RecordingScriptStore([regular, child, composite]),
            new ImmediateEngine());
        await viewModel.InitializeAsync(CancellationToken.None);

        viewModel.EditorName = "Draft chưa lưu";
        StringAssert.Contains(viewModel.RunConfigurationError, "lưu hoặc hủy");
        Assert.IsFalse(viewModel.RunCommand.CanExecute(null));

        viewModel.EditorName = viewModel.SelectedStep!.Name;
        await viewModel.SaveStepCommand.ExecuteAsync();
        await viewModel.NavigateToScriptAsync(viewModel.Scripts.Single(item => item.Id == composite.Id));
        viewModel.CompositeContinueOnFailure = true;
        StringAssert.Contains(viewModel.RunConfigurationError, "lưu hoặc hủy");
        Assert.IsFalse(viewModel.RunCommand.CanExecute(null));
    }

    [TestMethod]
    public async Task ExistingDelay_RemainsDraftUntilExplicitSaveAndCreateNeverAutoCreates()
    {
        var script = new ScriptDefinition
        {
            Name = "Delay",
            Steps = [new DelayStep { Name = "Tên cũ", DurationMilliseconds = 1_000 }]
        };
        var store = new RecordingScriptStore([script]);
        var viewModel = CreateViewModel(store, new ImmediateEngine());
        await viewModel.InitializeAsync(CancellationToken.None);

        viewModel.EditorDelayMilliseconds = 3_000;
        Assert.IsTrue(viewModel.SaveStepCommand.CanExecute(null));
        Assert.AreEqual("Có thay đổi chưa lưu", viewModel.EditorSaveState);
        await Task.Delay(500);
        Assert.AreEqual(1_000, ((DelayStep)viewModel.SelectedStep!.Model).DurationMilliseconds);
        Assert.AreEqual(0, store.SaveCount);

        await viewModel.SaveStepCommand.ExecuteAsync();
        Assert.AreEqual(3_000, ((DelayStep)viewModel.SelectedStep!.Model).DurationMilliseconds);
        Assert.AreEqual("Chờ", viewModel.SelectedStep.Model.Name);
        Assert.AreEqual(1, store.SaveCount);
        Assert.AreEqual("Đã lưu", viewModel.EditorSaveState);

        await viewModel.NewStepCommand.ExecuteAsync();
        viewModel.EditorKind = ScriptStepKind.Delay;
        viewModel.EditorDelayMilliseconds = 5_000;
        await Task.Delay(500);
        Assert.AreEqual(1, viewModel.Steps.Count, "Nhập Delay mới không được tự tạo item.");
        Assert.AreEqual(1, store.SaveCount);
        Assert.IsTrue(viewModel.AddStepCommand.CanExecute(null));
    }

    [TestMethod]
    public async Task ExistingCompositeDelay_RemainsDraftUntilExplicitSave()
    {
        var child = new ScriptDefinition { Name = "Child", Steps = [new NoteStep { Name = "N" }] };
        var composite = new ScriptDefinition
        {
            Name = "Composite",
            Kind = ScriptKind.Composite,
            CompositeItems = [new CompositeDelayItem { DurationMilliseconds = 1_000 }]
        };
        var store = new RecordingScriptStore([child, composite]);
        var viewModel = CreateViewModel(store, new ImmediateEngine());
        await viewModel.InitializeAsync(CancellationToken.None);
        await viewModel.NavigateToScriptAsync(viewModel.Scripts.Single(item => item.Id == composite.Id));

        viewModel.CompositeDelayMilliseconds = 3_000;
        Assert.IsTrue(viewModel.SaveCompositeItemCommand.CanExecute(null));
        Assert.AreEqual("Có thay đổi chưa lưu", viewModel.EditorSaveState);
        await Task.Delay(500);
        Assert.AreEqual(1_000, ((CompositeDelayItem)viewModel.SelectedCompositeItem!.Model).DurationMilliseconds);
        Assert.AreEqual(0, store.SaveCount);

        await viewModel.SaveCompositeItemCommand.ExecuteAsync();
        Assert.AreEqual(3_000, ((CompositeDelayItem)viewModel.SelectedCompositeItem.Model).DurationMilliseconds);
        Assert.AreEqual(1, store.SaveCount);
        Assert.AreEqual("Đã lưu", viewModel.EditorSaveState);
    }

    [TestMethod]
    public async Task CompositeReferenceTracksDirtyAndBlankDoesNotDiscardIt()
    {
        var first = new ScriptDefinition { Name = "First", Steps = [new NoteStep { Name = "A" }] };
        var second = new ScriptDefinition { Name = "Second", Steps = [new NoteStep { Name = "B" }] };
        var composite = new ScriptDefinition
        {
            Name = "Composite",
            Kind = ScriptKind.Composite,
            CompositeItems = [new ScriptReferenceItem { ScriptId = first.Id }]
        };
        var store = new RecordingScriptStore([first, second, composite]);
        var viewModel = CreateViewModel(store, new ImmediateEngine());
        await viewModel.InitializeAsync(CancellationToken.None);
        await viewModel.NavigateToScriptAsync(viewModel.Scripts.Single(item => item.Id == composite.Id));

        viewModel.CompositeReferenceScript = viewModel.RegularScripts.Single(item => item.Id == second.Id);
        viewModel.CompositeContinueOnFailure = true;
        Assert.IsTrue(viewModel.IsCompositeEditorDirty);
        Assert.IsTrue(viewModel.SaveCompositeItemCommand.CanExecute(null));
        Assert.IsFalse(viewModel.TryClearCompositeSelectionFromBlank());

        await viewModel.SaveCompositeItemCommand.ExecuteAsync();
        var saved = (ScriptReferenceItem)viewModel.SelectedCompositeItem!.Model;
        Assert.AreEqual(second.Id, saved.ScriptId);
        Assert.IsTrue(saved.ContinueOnFailure);
        Assert.IsFalse(viewModel.IsCompositeEditorDirty);
    }

    [TestMethod]
    public async Task SaveCompositeReference_DoesNotClearDraftChangedWhileSaveIsPending()
    {
        var first = new ScriptDefinition { Name = "First", Steps = [new NoteStep { Name = "A" }] };
        var second = new ScriptDefinition { Name = "Second", Steps = [new NoteStep { Name = "B" }] };
        var composite = new ScriptDefinition
        {
            Name = "Composite",
            Kind = ScriptKind.Composite,
            CompositeItems = [new ScriptReferenceItem { ScriptId = first.Id }]
        };
        var store = new BlockingSaveScriptStore([first, second, composite]);
        var viewModel = CreateViewModel(store, new ImmediateEngine());
        await viewModel.InitializeAsync(CancellationToken.None);
        await viewModel.NavigateToScriptAsync(viewModel.Scripts.Single(item => item.Id == composite.Id));

        viewModel.CompositeReferenceScript = viewModel.RegularScripts.Single(item => item.Id == second.Id);
        viewModel.CompositeContinueOnFailure = true;
        var saveTask = viewModel.SaveCompositeItemCommand.ExecuteAsync();
        await store.SaveStarted.Task;
        viewModel.CompositeReferenceScript = viewModel.RegularScripts.Single(item => item.Id == first.Id);
        viewModel.CompositeContinueOnFailure = false;
        store.ReleaseSave.TrySetResult();
        await saveTask;

        var saved = (ScriptReferenceItem)viewModel.SelectedCompositeItem!.Model;
        Assert.AreEqual(second.Id, saved.ScriptId);
        Assert.IsTrue(saved.ContinueOnFailure);
        Assert.AreEqual(first.Id, viewModel.CompositeReferenceScript!.Id);
        Assert.IsFalse(viewModel.CompositeContinueOnFailure);
        Assert.IsTrue(viewModel.IsCompositeEditorDirty);
        Assert.IsFalse(viewModel.RunCommand.CanExecute(null));
    }

    [TestMethod]
    public async Task EnabledTogglePersistsAtomicallyWithoutCommittingUnrelatedDraft()
    {
        var store = new RecordingScriptStore([CreateThreeStepScript()]);
        var viewModel = CreateViewModel(store, new ImmediateEngine());
        await viewModel.InitializeAsync(CancellationToken.None);
        var selected = viewModel.SelectedStep!;
        viewModel.EditorName = "Tên nháp";

        selected.IsEnabled = false;
        await WaitUntilAsync(() => store.SaveCount == 1);

        Assert.IsFalse(selected.Model.IsEnabled);
        Assert.AreEqual("A", selected.Model.Name);
        Assert.AreEqual("Tên nháp", viewModel.EditorName);
        Assert.IsTrue(viewModel.IsEditorDirty);
        Assert.AreEqual("A", store.LastSaved.Single().Steps[0].Name);
    }

    [STATestMethod]
    public async Task RegularCompositeRegular_HydrationAndRegularItemsRefreshStayClean()
    {
        if (Application.Current is null)
        {
            var application = new MEmuScriptStudio.App.App();
            application.InitializeComponent();
        }

        var regular = CreateThreeStepScript();
        var composite = new ScriptDefinition
        {
            Name = "Composite",
            Kind = ScriptKind.Composite,
            CompositeItems = [new ScriptReferenceItem { ScriptId = regular.Id }]
        };
        var decisions = new DraftDecisionConfirmation();
        var viewModel = CreateViewModel(new RecordingScriptStore([regular, composite]), new ImmediateEngine(), decisions);
        await viewModel.InitializeAsync(CancellationToken.None);
        var window = new MainWindow(viewModel);
        try
        {
            DrainDataBindings();
            Assert.IsFalse(viewModel.HasAnyEditorDraft);

            Assert.IsTrue(await viewModel.NavigateToScriptAsync(
                viewModel.Scripts.Single(script => script.Id == composite.Id)));
            DrainDataBindings();
            Assert.IsFalse(viewModel.IsCompositeEditorDirty);
            Assert.AreEqual(regular.Id, viewModel.CompositeReferenceScript!.Id);

            typeof(MainViewModel).GetMethod("RefreshScriptCollections", System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic)!.Invoke(viewModel, null);
            DrainDataBindings();
            Assert.AreEqual(regular.Id, viewModel.CompositeReferenceScript!.Id);
            Assert.IsFalse(viewModel.IsCompositeEditorDirty,
                "ItemsSource refresh and ComboBox SelectedItem reconciliation must be hydration, not an edit.");

            Assert.IsTrue(await viewModel.NavigateToScriptAsync(
                viewModel.Scripts.Single(script => script.Id == regular.Id)));
            DrainDataBindings();
            Assert.IsFalse(viewModel.HasAnyEditorDraft);
            Assert.AreEqual(0, decisions.Calls.Count);
        }
        finally { window.Close(); }
    }

    [TestMethod]
    public async Task SemanticDirty_RevertAndNormalizedScriptNameReturnToClean()
    {
        var first = new ScriptDefinition { Name = "First", Steps = [new NoteStep { Name = "A", Text = "A" }] };
        var second = new ScriptDefinition { Name = "Second", Steps = [new NoteStep { Name = "B", Text = "B" }] };
        var composite = new ScriptDefinition
        {
            Name = "Composite",
            Kind = ScriptKind.Composite,
            CompositeItems = [new ScriptReferenceItem { ScriptId = first.Id }]
        };
        var viewModel = CreateViewModel(new RecordingScriptStore([first, second, composite]), new ImmediateEngine());
        await viewModel.InitializeAsync(CancellationToken.None);

        viewModel.EditorName = "Changed";
        Assert.IsTrue(viewModel.IsEditorDirty);
        viewModel.EditorName = "A";
        Assert.IsFalse(viewModel.IsEditorDirty);

        viewModel.ScriptName = "First ";
        Assert.IsFalse(viewModel.IsScriptNameDirty);
        Assert.IsFalse(viewModel.CanRenameScript);
        viewModel.ScriptName = "Changed name";
        Assert.IsTrue(viewModel.IsScriptNameDirty);
        viewModel.ScriptName = "First";
        Assert.IsFalse(viewModel.IsScriptNameDirty);

        Assert.IsTrue(await viewModel.NavigateToScriptAsync(
            viewModel.Scripts.Single(script => script.Id == composite.Id)));
        viewModel.CompositeReferenceScript = viewModel.RegularScripts.Single(script => script.Id == second.Id);
        Assert.IsTrue(viewModel.IsCompositeEditorDirty);
        viewModel.CompositeReferenceScript = viewModel.RegularScripts.Single(script => script.Id == first.Id);
        Assert.IsFalse(viewModel.IsCompositeEditorDirty);
    }

    [TestMethod]
    public async Task UntouchedCreate_DoesNotPromptOnNavigationOrClosePreparation()
    {
        var first = CreateThreeStepScript();
        var second = new ScriptDefinition { Name = "Second", Steps = [new NoteStep { Name = "N", Text = "N" }] };
        var decisions = new DraftDecisionConfirmation();
        var viewModel = CreateViewModel(new RecordingScriptStore([first, second]), new ImmediateEngine(), decisions);
        await viewModel.InitializeAsync(CancellationToken.None);

        await viewModel.NewStepCommand.ExecuteAsync();
        Assert.AreEqual(RegularStepEditorMode.Create, viewModel.StepEditorMode);
        Assert.IsFalse(viewModel.HasRegularEditorDraft);
        Assert.IsTrue(await viewModel.NavigateToScriptAsync(viewModel.Scripts.Single(script => script.Id == second.Id)));

        await viewModel.NewStepCommand.ExecuteAsync();
        Assert.IsTrue(await viewModel.TryPrepareForCloseAsync());
        Assert.AreEqual(0, decisions.Calls.Count);
    }

    [STATestMethod]
    public async Task CloseBoundary_CommitsFocusedTextAndDiscardClearsStaleBindingError()
    {
        if (Application.Current is null)
        {
            var application = new MEmuScriptStudio.App.App();
            application.InitializeComponent();
        }

        var decisions = new DraftDecisionConfirmation(EditorDraftDecision.Cancel, EditorDraftDecision.Discard);
        var viewModel = CreateViewModel(new RecordingScriptStore([CreateThreeStepScript()]), new ImmediateEngine(), decisions);
        await viewModel.InitializeAsync(CancellationToken.None);
        var window = new MainWindow(viewModel);
        var panel = (Grid)window.FindName("RegularStepPropertiesPanel");
        var focusedDraft = new TextBox();
        focusedDraft.SetBinding(TextBox.TextProperty, new Binding(nameof(MainViewModel.EditorName))
        {
            UpdateSourceTrigger = UpdateSourceTrigger.LostFocus
        });
        panel.Children.Add(focusedDraft);
        try
        {
            DrainDataBindings();
            focusedDraft.Text = "Focused close draft";
            window.CommitEditorBoundaryInput(focusedDraft);
            Assert.AreEqual("Focused close draft", viewModel.EditorName);
            Assert.IsFalse(await viewModel.TryPrepareForCloseAsync());
            Assert.AreEqual("Focused close draft", viewModel.EditorName);

            var invalidInput = new TextBox();
            invalidInput.SetBinding(TextBox.TextProperty, new Binding(nameof(MainViewModel.EditorTimeoutSeconds))
            {
                UpdateSourceTrigger = UpdateSourceTrigger.Explicit,
                ValidatesOnExceptions = true
            });
            panel.Children.Add(invalidInput);
            DrainDataBindings();
            invalidInput.Text = "invalid";
            invalidInput.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
            Assert.IsTrue(Validation.GetHasError(invalidInput));
            viewModel.HasEditorBindingErrors = true;

            Assert.IsTrue(await viewModel.NavigateToStepAsync(viewModel.Steps[1]),
                $"Navigation failed after discard; dirty={viewModel.IsEditorDirty}; bindingErrors={viewModel.HasEditorBindingErrors}; " +
                $"calls={decisions.Calls.Count}; selected={viewModel.SelectedStep?.Name}");
            DrainDataBindings();
            BackgroundFocusBehavior.RefreshInputBindingsAndValidation(panel);
            Assert.IsFalse(Validation.GetHasError(invalidInput));
            Assert.IsFalse(viewModel.HasEditorBindingErrors);
        }
        finally
        {
            panel.Children.Remove(focusedDraft);
            window.Close();
        }
    }

    [TestMethod]
    public async Task CopyRegularCompositeAndPendingDelay_UsesVisibleValidDraft()
    {
        var first = new ScriptDefinition
        {
            Name = "First",
            Steps =
            [
                new NoteStep { Name = "A", Text = "A" },
                new DelayStep { Name = "Chờ", DurationMilliseconds = 1_000 }
            ]
        };
        var second = new ScriptDefinition { Name = "Second", Steps = [new NoteStep { Name = "B", Text = "B" }] };
        var composite = new ScriptDefinition
        {
            Name = "Composite",
            Kind = ScriptKind.Composite,
            CompositeItems = [new ScriptReferenceItem { ScriptId = first.Id }]
        };
        var viewModel = CreateViewModel(new RecordingScriptStore([first, second, composite]), new ImmediateEngine());
        await viewModel.InitializeAsync(CancellationToken.None);

        viewModel.EditorName = "Visible draft";
        viewModel.CopyStepsCommand.Execute(null);
        viewModel.EditorName = "A";
        await viewModel.PasteStepsCommand.ExecuteAsync();
        Assert.AreEqual("Visible draft", viewModel.SelectedStep!.Model.Name);

        viewModel.SelectedStep = viewModel.Steps.Single(step => step.Model is DelayStep &&
            ((DelayStep)step.Model).DurationMilliseconds == 1_000);
        viewModel.EditorDelayMilliseconds = 9_000;
        viewModel.CopyStepsCommand.Execute(null);
        viewModel.EditorDelayMilliseconds = 1_000;
        await viewModel.PasteStepsCommand.ExecuteAsync();
        Assert.AreEqual(9_000, ((DelayStep)viewModel.SelectedStep!.Model).DurationMilliseconds);

        Assert.IsTrue(await viewModel.NavigateToScriptAsync(
            viewModel.Scripts.Single(script => script.Id == composite.Id)));
        viewModel.CompositeReferenceScript = viewModel.RegularScripts.Single(script => script.Id == second.Id);
        viewModel.CopyCompositeItemsCommand.Execute(null);
        viewModel.CompositeReferenceScript = viewModel.RegularScripts.Single(script => script.Id == first.Id);
        await viewModel.PasteCompositeItemsCommand.ExecuteAsync();
        Assert.AreEqual(second.Id, ((ScriptReferenceItem)viewModel.SelectedCompositeItem!.Model).ScriptId);
    }

    [TestMethod]
    public async Task InvalidCopyDoesNotFallbackToPersistedModel()
    {
        var viewModel = CreateViewModel(new RecordingScriptStore([CreateThreeStepScript()]), new ImmediateEngine());
        await viewModel.InitializeAsync(CancellationToken.None);
        viewModel.HasEditorBindingErrors = true;

        viewModel.CopyStepsCommand.Execute(null);

        Assert.IsFalse(viewModel.HasCopiedSteps);
        StringAssert.Contains(viewModel.StatusMessage, "không hợp lệ");
    }

    [TestMethod]
    public async Task ExportResolvesRegularCompositeAndScriptNameDraftsAndCancelExportsNothing()
    {
        var regular = new ScriptDefinition { Name = "Regular", Steps = [new NoteStep { Name = "A", Text = "A" }] };
        var composite = new ScriptDefinition
        {
            Name = "Composite",
            Kind = ScriptKind.Composite,
            CompositeItems = [new ScriptReferenceItem { ScriptId = regular.Id }]
        };
        var transfer = new RecordingScriptTransferService([]);
        var decisions = new DraftDecisionConfirmation(
            EditorDraftDecision.Save,
            EditorDraftDecision.Save,
            EditorDraftDecision.Save,
            EditorDraftDecision.Cancel);
        var viewModel = CreateViewModel(
            new RecordingScriptStore([regular, composite]),
            new ImmediateEngine(),
            decisions,
            fileDialog: new RecordingFileDialog(null, @"C:\Temp\export.memuscript"),
            transfer: transfer,
            importConflict: new FixedImportConflict(ScriptImportConflictResolution.Skip));
        await viewModel.InitializeAsync(CancellationToken.None);

        viewModel.EditorName = "Visible exported step";
        viewModel.ScriptName = "Renamed regular";
        await viewModel.ExportSelectedScriptCommand.ExecuteAsync();
        Assert.AreEqual("Renamed regular", transfer.Exports[0].Single().Name);
        Assert.AreEqual("Visible exported step", transfer.Exports[0].Single().Steps[0].Name);

        Assert.IsTrue(await viewModel.NavigateToScriptAsync(
            viewModel.Scripts.Single(script => script.Id == composite.Id)));
        viewModel.CompositeContinueOnFailure = true;
        await viewModel.ExportAllScriptsCommand.ExecuteAsync();
        var exportedComposite = transfer.Exports[1].Single(script => script.Id == composite.Id);
        Assert.IsTrue(((ScriptReferenceItem)exportedComposite.CompositeItems[0]).ContinueOnFailure);

        viewModel.CompositeContinueOnFailure = false;
        await viewModel.ExportAllScriptsCommand.ExecuteAsync();
        Assert.AreEqual(2, transfer.Exports.Count, "Cancel must not produce an export file.");
    }

    [TestMethod]
    public async Task FailedPersistenceKeepsDraftDirtyAndRollsBackPersistedModel()
    {
        var source = CreateThreeStepScript();
        var store = new RecordingScriptStore([source]) { ThrowOnSave = true };
        var viewModel = CreateViewModel(store, new ImmediateEngine());
        await viewModel.InitializeAsync(CancellationToken.None);
        viewModel.EditorName = "Unsaved after failure";

        await viewModel.SaveStepCommand.ExecuteAsync();

        Assert.IsTrue(viewModel.IsEditorDirty);
        Assert.AreEqual("Unsaved after failure", viewModel.EditorName);
        Assert.AreEqual("A", viewModel.SelectedStep!.Model.Name);
    }

    [TestMethod]
    public async Task RevertWhileSaveIsPending_WaitsBeforeNavigationAndReevaluatesAgainstSavedBaseline()
    {
        var first = CreateThreeStepScript();
        var second = new ScriptDefinition { Name = "Second", Steps = [new NoteStep { Name = "N", Text = "N" }] };
        var store = new BlockingSaveScriptStore([first, second]);
        var decisions = new DraftDecisionConfirmation(EditorDraftDecision.Cancel);
        var viewModel = CreateViewModel(store, new ImmediateEngine(), decisions);
        await viewModel.InitializeAsync(CancellationToken.None);
        var originalName = viewModel.EditorName;

        viewModel.EditorName = "Saved B";
        var saveTask = viewModel.SaveStepCommand.ExecuteAsync();
        await store.SaveStarted.Task;
        viewModel.EditorName = originalName;
        Assert.IsFalse(viewModel.IsEditorDirty);
        Assert.IsTrue(viewModel.IsEditorPersistenceBusy);
        Assert.IsTrue(viewModel.HasPendingNavigationDraft);

        var navigationTask = viewModel.NavigateToScriptAsync(
            viewModel.Scripts.Single(script => script.Id == second.Id));
        Assert.IsFalse(navigationTask.IsCompleted, "Navigation must await the in-flight persistence operation.");

        store.ReleaseSave.TrySetResult();
        await saveTask;
        Assert.IsFalse(await navigationTask, "After B persists, visible A is dirty against the new B baseline.");
        Assert.AreEqual("Saved B", viewModel.SelectedStep!.Model.Name);
        Assert.AreEqual(originalName, viewModel.EditorName);
        Assert.IsTrue(viewModel.IsEditorDirty);
        Assert.IsFalse(viewModel.IsEditorPersistenceBusy);
        Assert.AreEqual(("Thuộc tính bước", true), decisions.Calls.Single());
    }

    [TestMethod]
    public async Task CompositeSaveFailureAfterRevert_WaitsAndRollsBackCapturedOwnerBeforeNavigation()
    {
        var first = new ScriptDefinition { Name = "First", Steps = [new NoteStep { Name = "A", Text = "A" }] };
        var second = new ScriptDefinition { Name = "Second", Steps = [new NoteStep { Name = "B", Text = "B" }] };
        var composite = new ScriptDefinition
        {
            Name = "Composite",
            Kind = ScriptKind.Composite,
            CompositeItems = [new ScriptReferenceItem { ScriptId = first.Id }]
        };
        var secondUpdatedAt = second.UpdatedAt;
        var store = new BlockingFailingScriptStore([first, second, composite]);
        var viewModel = CreateViewModel(store, new ImmediateEngine());
        await viewModel.InitializeAsync(CancellationToken.None);
        Assert.IsTrue(await viewModel.NavigateToScriptAsync(
            viewModel.Scripts.Single(script => script.Id == composite.Id)));

        viewModel.CompositeReferenceScript = viewModel.RegularScripts.Single(script => script.Id == second.Id);
        var saveTask = viewModel.SaveCompositeItemCommand.ExecuteAsync();
        await store.SaveStarted.Task;
        viewModel.CompositeReferenceScript = viewModel.RegularScripts.Single(script => script.Id == first.Id);
        Assert.IsFalse(viewModel.IsCompositeEditorDirty);

        var navigationTask = viewModel.NavigateToScriptAsync(
            viewModel.Scripts.Single(script => script.Id == second.Id));
        Assert.IsFalse(navigationTask.IsCompleted);
        store.ReleaseSave.TrySetResult();
        await saveTask;

        Assert.IsTrue(await navigationTask);
        Assert.AreEqual(first.Id, ((ScriptReferenceItem)composite.CompositeItems.Single()).ScriptId);
        Assert.AreEqual(secondUpdatedAt, second.UpdatedAt,
            "Rollback must not mutate the script selected after the failed save.");
        Assert.IsFalse(viewModel.IsEditorPersistenceBusy);
    }

    [TestMethod]
    public async Task RegularListMutations_SaveFailureRollsBackModelSelectionHistoryAndTimestamp()
    {
        var script = CreateThreeStepScript();
        var originalUpdatedAt = script.UpdatedAt;
        var originalIds = script.Steps.Select(step => step.Id).ToArray();
        var store = new RecordingScriptStore([script]);
        var viewModel = CreateViewModel(store, new ImmediateEngine());
        await viewModel.InitializeAsync(CancellationToken.None);
        var originalSelectedId = viewModel.SelectedStep!.Id;

        viewModel.CopyStepsCommand.Execute(null);
        store.ThrowOnSave = true;
        await viewModel.PasteStepsCommand.ExecuteAsync();
        CollectionAssert.AreEqual(originalIds, viewModel.Steps.Select(step => step.Id).ToArray());
        Assert.AreEqual(originalSelectedId, viewModel.SelectedStep!.Id);
        Assert.IsFalse(viewModel.UndoStepListCommand.CanExecute(null));
        Assert.AreEqual(originalUpdatedAt, script.UpdatedAt);

        await viewModel.DuplicateStepCommand.ExecuteAsync();
        CollectionAssert.AreEqual(originalIds, viewModel.Steps.Select(step => step.Id).ToArray());
        Assert.AreEqual(originalSelectedId, viewModel.SelectedStep!.Id);
        Assert.IsFalse(viewModel.UndoStepListCommand.CanExecute(null));

        await viewModel.DeleteStepCommand.ExecuteAsync();
        CollectionAssert.AreEqual(originalIds, viewModel.Steps.Select(step => step.Id).ToArray());
        Assert.AreEqual(originalSelectedId, viewModel.SelectedStep!.Id);
        Assert.IsFalse(viewModel.UndoStepListCommand.CanExecute(null));

        await Assert.ThrowsExceptionAsync<IOException>(() =>
            viewModel.MoveStepToAsync(viewModel.Steps[0], viewModel.Steps.Count));
        CollectionAssert.AreEqual(originalIds, viewModel.Steps.Select(step => step.Id).ToArray());
        Assert.AreEqual(originalSelectedId, viewModel.SelectedStep!.Id);
        Assert.IsFalse(viewModel.UndoStepListCommand.CanExecute(null));
        Assert.AreEqual(originalUpdatedAt, script.UpdatedAt);

        var toggleAttempt = store.SaveAttemptCount + 1;
        viewModel.Steps[0].IsEnabled = false;
        await WaitUntilAsync(() => store.SaveAttemptCount >= toggleAttempt);
        Assert.IsTrue(viewModel.Steps[0].IsEnabled);
        Assert.AreEqual(originalSelectedId, viewModel.SelectedStep!.Id);
        Assert.IsFalse(viewModel.UndoStepListCommand.CanExecute(null));
        Assert.AreEqual(originalUpdatedAt, script.UpdatedAt);

        store.ThrowOnSave = false;
        await viewModel.DuplicateStepCommand.ExecuteAsync();
        var committedIds = viewModel.Steps.Select(step => step.Id).ToArray();
        var committedUpdatedAt = script.UpdatedAt;
        Assert.IsTrue(viewModel.UndoStepListCommand.CanExecute(null));

        store.ThrowOnSave = true;
        await viewModel.UndoStepListCommand.ExecuteAsync();
        CollectionAssert.AreEqual(committedIds, viewModel.Steps.Select(step => step.Id).ToArray());
        Assert.IsTrue(viewModel.UndoStepListCommand.CanExecute(null));
        Assert.AreEqual(committedUpdatedAt, script.UpdatedAt);
    }

    [TestMethod]
    public async Task CompositeListMutations_SaveFailureRollsBackModelSelectionHistoryAndTimestamp()
    {
        var child = new ScriptDefinition { Name = "Child" };
        var composite = new ScriptDefinition
        {
            Name = "Composite",
            Kind = ScriptKind.Composite,
            CompositeItems =
            [
                new ScriptReferenceItem { ScriptId = child.Id },
                new CompositeDelayItem { DurationMilliseconds = 500 }
            ]
        };
        var originalUpdatedAt = composite.UpdatedAt;
        var originalIds = composite.CompositeItems.Select(item => item.Id).ToArray();
        var store = new RecordingScriptStore([child, composite]);
        var viewModel = CreateViewModel(store, new ImmediateEngine());
        await viewModel.InitializeAsync(CancellationToken.None);
        Assert.IsTrue(await viewModel.NavigateToScriptAsync(
            viewModel.Scripts.Single(item => item.Id == composite.Id)));
        var originalSelectedId = viewModel.SelectedCompositeItem!.Id;
        viewModel.CopyCompositeItemsCommand.Execute(null);

        store.ThrowOnSave = true;
        await viewModel.PasteCompositeItemsCommand.ExecuteAsync();
        CollectionAssert.AreEqual(originalIds, viewModel.CompositeItems.Select(item => item.Id).ToArray());
        Assert.AreEqual(originalSelectedId, viewModel.SelectedCompositeItem!.Id);
        Assert.IsFalse(viewModel.UndoCompositeItemsCommand.CanExecute(null));
        Assert.AreEqual(originalUpdatedAt, composite.UpdatedAt);

        await viewModel.AddCompositeDelayCommand.ExecuteAsync();
        CollectionAssert.AreEqual(originalIds, viewModel.CompositeItems.Select(item => item.Id).ToArray());
        Assert.AreEqual(originalSelectedId, viewModel.SelectedCompositeItem!.Id);
        Assert.IsFalse(viewModel.UndoCompositeItemsCommand.CanExecute(null));

        await viewModel.DeleteCompositeItemsCommand.ExecuteAsync();
        CollectionAssert.AreEqual(originalIds, viewModel.CompositeItems.Select(item => item.Id).ToArray());
        Assert.AreEqual(originalSelectedId, viewModel.SelectedCompositeItem!.Id);
        Assert.IsFalse(viewModel.UndoCompositeItemsCommand.CanExecute(null));

        await viewModel.MoveCompositeItemDownCommand.ExecuteAsync();
        CollectionAssert.AreEqual(originalIds, viewModel.CompositeItems.Select(item => item.Id).ToArray());
        Assert.AreEqual(originalSelectedId, viewModel.SelectedCompositeItem!.Id);
        Assert.IsFalse(viewModel.UndoCompositeItemsCommand.CanExecute(null));
        Assert.AreEqual(originalUpdatedAt, composite.UpdatedAt);

        var toggleAttempt = store.SaveAttemptCount + 1;
        viewModel.CompositeItems[0].IsEnabled = false;
        await WaitUntilAsync(() => store.SaveAttemptCount >= toggleAttempt);
        Assert.IsTrue(viewModel.CompositeItems[0].IsEnabled);
        Assert.AreEqual(originalSelectedId, viewModel.SelectedCompositeItem!.Id);
        Assert.IsFalse(viewModel.UndoCompositeItemsCommand.CanExecute(null));
        Assert.AreEqual(originalUpdatedAt, composite.UpdatedAt);

        store.ThrowOnSave = false;
        await viewModel.PasteCompositeItemsCommand.ExecuteAsync();
        var committedIds = viewModel.CompositeItems.Select(item => item.Id).ToArray();
        var committedUpdatedAt = composite.UpdatedAt;
        Assert.IsTrue(viewModel.UndoCompositeItemsCommand.CanExecute(null));

        store.ThrowOnSave = true;
        await viewModel.UndoCompositeItemsCommand.ExecuteAsync();
        CollectionAssert.AreEqual(committedIds, viewModel.CompositeItems.Select(item => item.Id).ToArray());
        Assert.IsTrue(viewModel.UndoCompositeItemsCommand.CanExecute(null));
        Assert.AreEqual(committedUpdatedAt, composite.UpdatedAt);
    }

    [TestMethod]
    public async Task CompositeToggle_DelayedFailureBlocksSecondToggleAndRollsBackCapturedState()
    {
        var composite = new ScriptDefinition
        {
            Name = "Composite",
            Kind = ScriptKind.Composite,
            CompositeItems = [new CompositeDelayItem { DurationMilliseconds = 500 }]
        };
        var originalUpdatedAt = composite.UpdatedAt;
        var store = new BlockingFailingScriptStore([composite]);
        var viewModel = CreateViewModel(store, new ImmediateEngine());
        await viewModel.InitializeAsync(CancellationToken.None);
        var originalSelectedId = viewModel.SelectedCompositeItem!.Id;

        viewModel.CompositeItems[0].IsEnabled = false;
        await store.SaveStarted.Task;
        Assert.IsFalse(viewModel.CompositeItems[0].IsEnabled);

        viewModel.CompositeItems[0].IsEnabled = true;
        Assert.IsFalse(viewModel.CompositeItems[0].IsEnabled,
            "A second toggle must be rejected while the first persistence transaction is pending.");
        Assert.AreEqual(1, store.SaveAttemptCount);

        store.ReleaseSave.TrySetResult();
        await WaitUntilAsync(() => !viewModel.IsEditorPersistenceBusy && viewModel.CompositeItems[0].IsEnabled);

        Assert.AreEqual(1, store.SaveAttemptCount);
        Assert.IsTrue(viewModel.CompositeItems[0].IsEnabled);
        Assert.AreEqual(originalSelectedId, viewModel.SelectedCompositeItem!.Id);
        Assert.AreEqual(originalUpdatedAt, composite.UpdatedAt);
        Assert.IsFalse(viewModel.UndoCompositeItemsCommand.CanExecute(null));
    }

    [TestMethod]
    public async Task LibraryMutations_SaveFailureRollsBackCreateDuplicateDeleteImportAssignmentsAndHistory()
    {
        var first = CreateThreeStepScript();
        var second = new ScriptDefinition { Name = "Second", Steps = [new NoteStep { Name = "B", Text = "B" }] };
        var loadedSettings = new ApplicationSettings();
        loadedSettings.MultiInstanceRun.ScriptAssignmentMode = ScriptAssignmentMode.PerInstance;
        loadedSettings.MultiInstanceRun.ScriptAssignments[4] = first.Id;
        loadedSettings.MultiInstanceRun.CommonScriptId = first.Id;
        var settings = new RecordingRunSettingsStore(loadedSettings);
        var store = new RecordingScriptStore([first, second]);
        var incoming = new ScriptDefinition { Name = "Imported", Steps = [new NoteStep { Name = "I", Text = "I" }] };
        var viewModel = CreateViewModel(
            store,
            new ImmediateEngine(),
            new ConfigurableConfirmation(true),
            fileDialog: new RecordingFileDialog(@"C:\Temp\rollback.memuscript", exportPath: null),
            transfer: new RecordingScriptTransferService([incoming]),
            importConflict: new FixedImportConflict(ScriptImportConflictResolution.Skip),
            instanceService: new FixedInstanceService([new MemuInstance(4, "VM 4", true, 44)]),
            settingsStore: settings);
        await viewModel.InitializeAsync(CancellationToken.None);
        await viewModel.RefreshCommand.ExecuteAsync();
        await viewModel.DuplicateStepCommand.ExecuteAsync();
        Assert.IsTrue(viewModel.UndoStepListCommand.CanExecute(null));

        var originalIds = viewModel.Scripts.Select(script => script.Id).ToArray();
        var originalSelected = viewModel.SelectedScript;
        var originalCommon = viewModel.CommonRunScript;
        var originalControlSelection = viewModel.ControlCenterSelectedScript;
        var originalAssignedId = viewModel.RunTargets.Single().AssignedScriptId;
        store.ThrowOnSave = true;

        await viewModel.CreateScriptCommand.ExecuteAsync();
        CollectionAssert.AreEqual(originalIds, viewModel.Scripts.Select(script => script.Id).ToArray());
        Assert.AreSame(originalSelected, viewModel.SelectedScript);

        await viewModel.CreateCompositeScriptCommand.ExecuteAsync();
        CollectionAssert.AreEqual(originalIds, viewModel.Scripts.Select(script => script.Id).ToArray());
        Assert.AreSame(originalSelected, viewModel.SelectedScript);

        await viewModel.DuplicateScriptCommand.ExecuteAsync();
        CollectionAssert.AreEqual(originalIds, viewModel.Scripts.Select(script => script.Id).ToArray());
        Assert.AreSame(originalSelected, viewModel.SelectedScript);

        await viewModel.DeleteScriptCommand.ExecuteAsync();
        CollectionAssert.AreEqual(originalIds, viewModel.Scripts.Select(script => script.Id).ToArray());
        Assert.AreSame(originalSelected, viewModel.SelectedScript);
        Assert.AreSame(originalCommon, viewModel.CommonRunScript);
        Assert.AreSame(originalControlSelection, viewModel.ControlCenterSelectedScript);
        Assert.AreEqual(originalAssignedId, viewModel.RunTargets.Single().AssignedScriptId);
        Assert.IsTrue(viewModel.UndoStepListCommand.CanExecute(null));

        await viewModel.ImportScriptsCommand.ExecuteAsync();
        CollectionAssert.AreEqual(originalIds, viewModel.Scripts.Select(script => script.Id).ToArray());
        Assert.AreSame(originalSelected, viewModel.SelectedScript);
        Assert.AreEqual(originalAssignedId, viewModel.RunTargets.Single().AssignedScriptId);
        Assert.IsTrue(viewModel.UndoStepListCommand.CanExecute(null));
    }

    [STATestMethod]
    public async Task CorruptScriptLibrary_DeclinedRecoveryKeepsDestructiveMutationBlocked()
    {
        var directory = System.IO.Path.Combine(AppContext.BaseDirectory, "ViewModelRecoveryTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = System.IO.Path.Combine(directory, "scripts.json");
            const string corrupt = "{broken-script-data";
            await File.WriteAllTextAsync(path, corrupt);
            using var store = new JsonScriptStore(path);
            var declined = CreateViewModel(store, new ImmediateEngine(), new ConfigurableConfirmation(false));

            await declined.InitializeAsync(CancellationToken.None);

            Assert.IsTrue(declined.IsScriptPersistenceBlocked);
            Assert.IsFalse(declined.CreateScriptCommand.CanExecute(null));
            Assert.AreEqual(corrupt, await File.ReadAllTextAsync(path));
            Assert.AreEqual(corrupt, await File.ReadAllTextAsync(store.RecoveryBackupPath!));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static void AssertDurationParts(
        DurationInputControl control,
        string hours,
        string minutes,
        string seconds,
        string milliseconds)
    {
        Assert.AreEqual(hours, ((TextBox)control.FindName("HoursTextBox")).Text);
        Assert.AreEqual(minutes, ((TextBox)control.FindName("MinutesTextBox")).Text);
        Assert.AreEqual(seconds, ((TextBox)control.FindName("SecondsTextBox")).Text);
        Assert.AreEqual(milliseconds, ((TextBox)control.FindName("MillisecondsTextBox")).Text);
    }

    private static void DrainDataBindings() =>
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.DataBind);

    private static void PumpDispatcherUntil(Func<bool> condition, TimeSpan timeout)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(20)
        };
        var deadline = DateTime.UtcNow + timeout;
        timer.Tick += (_, _) =>
        {
            if (!condition() && DateTime.UtcNow < deadline) return;
            timer.Stop();
            frame.Continue = false;
        };
        timer.Start();
        Dispatcher.PushFrame(frame);
        if (!condition()) Assert.Fail("Timed out while pumping the dispatcher.");
    }

    private static async Task<MainViewModel> CreateRunningViewModelAsync(
        IScriptExecutionEngine engine,
        IConfirmationService? confirmation = null)
    {
        var viewModel = CreateViewModel(
            new RecordingScriptStore(),
            engine,
            confirmation,
            instanceService: new FixedInstanceService([new MemuInstance(2, "Target", true, 456)]));
        await viewModel.InitializeAsync(CancellationToken.None);
        await viewModel.RefreshCommand.ExecuteAsync();
        viewModel.RunTargets.Single().IsSelected = true;
        await viewModel.RunCommand.ExecuteAsync();

        switch (engine)
        {
            case BlockingEngine blocking:
                await blocking.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
                break;
            case CancellationCleanupEngine cleanup:
                await cleanup.FirstStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
                break;
        }

        return viewModel;
    }

    private static void AssertActiveGridResponsiveState(
        ControlCenterWindow window,
        RunControlPanel panel,
        DataGrid activeGrid,
        bool isWide,
        int expectedItemCount)
    {
        panel.ApplyLayout(new ControlCenterLayoutSettings { SetupPanelRatio = isWide ? 0 : 1 });
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        window.UpdateLayout();

        if (isWide)
        {
            var compactColumnHeadroom = activeGrid.Columns
                .Where(column => column.Width.IsAuto && double.IsFinite(column.MaxWidth))
                .Sum(column => Math.Max(0, column.MaxWidth - column.MinWidth));
            var usefulColumnsWidth = activeGrid.Columns.Sum(column => column.MinWidth) + compactColumnHeadroom;
            if (activeGrid.ActualWidth < usefulColumnsWidth)
            {
                window.Width += usefulColumnsWidth - activeGrid.ActualWidth + 32;
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                panel.ApplyLayout(new ControlCenterLayoutSettings { SetupPanelRatio = 0 });
                window.UpdateLayout();
            }
        }

        activeGrid.ApplyTemplate();
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        activeGrid.UpdateLayout();
        var scrollViewer = FindVisualDescendants<ScrollViewer>(activeGrid)
            .First(viewer => viewer.Name == "DG_ScrollViewer");
        var horizontalScrollBar = FindVisualDescendants<ScrollBar>(activeGrid)
            .First(scrollBar => scrollBar.Orientation == Orientation.Horizontal);

        Assert.AreEqual(expectedItemCount, activeGrid.Items.Count);
        Assert.IsFalse(activeGrid.CanUserAddRows);
        Assert.IsTrue(activeGrid.Items.Cast<object>().All(item => item is InstanceRunItemViewModel),
            "The grid must contain only source items, never a placeholder row used to manufacture scroll extent.");
        Assert.IsTrue(ScrollViewer.GetCanContentScroll(activeGrid));
        Assert.IsTrue(VirtualizingPanel.GetIsVirtualizing(activeGrid));
        Assert.AreEqual(VirtualizationMode.Recycling, VirtualizingPanel.GetVirtualizationMode(activeGrid));
        Assert.IsFalse(HasLogicalAncestor<ScrollViewer>(activeGrid));

        if (isWide)
        {
            Assert.AreEqual(Visibility.Collapsed, scrollViewer.ComputedHorizontalScrollBarVisibility);
            Assert.AreEqual(0, scrollViewer.ScrollableWidth, 0.5);
            Assert.AreEqual(Visibility.Collapsed, horizontalScrollBar.Visibility);
        }
        else
        {
            Assert.IsTrue(activeGrid.ActualWidth < activeGrid.Columns.Sum(column => column.ActualWidth));
            Assert.AreEqual(Visibility.Visible, scrollViewer.ComputedHorizontalScrollBarVisibility);
            Assert.IsTrue(scrollViewer.ScrollableWidth > 0);
            Assert.AreEqual(Visibility.Visible, horizontalScrollBar.Visibility);
            Assert.IsTrue(horizontalScrollBar.IsVisible);
            Assert.IsTrue(horizontalScrollBar.Maximum > 0);

            scrollViewer.ScrollToRightEnd();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            window.UpdateLayout();
            Assert.IsTrue(scrollViewer.HorizontalOffset > 0);
            Assert.AreEqual(scrollViewer.ScrollableWidth, scrollViewer.HorizontalOffset, 0.5);
            var stopHeader = FindVisualDescendants<DataGridColumnHeader>(activeGrid)
                .Single(header => Equals(header.Content, activeGrid.Columns[^1].Header));
            AssertElementWithinHorizontalViewport(activeGrid, scrollViewer, stopHeader,
                "The Stop header must be inside the internal viewport for empty, filtered-empty and populated grids.");
        }
    }

    private static void AssertElementWithinHorizontalViewport(
        DataGrid activeGrid,
        ScrollViewer scrollViewer,
        FrameworkElement element,
        string message)
    {
        var scrollContentPresenter = (ScrollContentPresenter)scrollViewer.Template.FindName(
            "PART_ScrollContentPresenter",
            scrollViewer);
        var viewportBounds = scrollContentPresenter.TransformToAncestor(scrollViewer)
            .TransformBounds(new Rect(scrollContentPresenter.RenderSize));
        var elementBounds = element.TransformToAncestor(scrollViewer)
            .TransformBounds(new Rect(element.RenderSize));
        var tolerance = (1 / VisualTreeHelper.GetDpi(activeGrid).DpiScaleX) + 0.01;

        Assert.IsTrue(
            elementBounds.Left >= viewportBounds.Left - tolerance &&
            elementBounds.Right <= viewportBounds.Right + tolerance &&
            elementBounds.Width > 0,
            $"{message} Element={elementBounds}; viewport={viewportBounds}; tolerance={tolerance}.");
    }

    private static IEnumerable<T> FindLogicalDescendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root))
        {
            if (child is not DependencyObject dependencyObject) continue;
            if (dependencyObject is T match) yield return match;
            foreach (var descendant in FindLogicalDescendants<T>(dependencyObject)) yield return descendant;
        }
    }

    private static IEnumerable<T> FindVisualDescendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match) yield return match;
            foreach (var descendant in FindVisualDescendants<T>(child)) yield return descendant;
        }
    }

    private static bool HasLogicalAncestor<T>(DependencyObject item)
        where T : DependencyObject
    {
        for (var parent = LogicalTreeHelper.GetParent(item); parent is not null; parent = LogicalTreeHelper.GetParent(parent))
        {
            if (parent is T) return true;
        }

        return false;
    }

    private static double ContrastRatio(Color first, Color second)
    {
        static double Luminance(Color color)
        {
            static double Linearize(byte channel)
            {
                var value = channel / 255d;
                return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
            }

            return (0.2126 * Linearize(color.R)) + (0.7152 * Linearize(color.G)) + (0.0722 * Linearize(color.B));
        }

        var lighter = Math.Max(Luminance(first), Luminance(second));
        var darker = Math.Min(Luminance(first), Luminance(second));
        return (lighter + 0.05) / (darker + 0.05);
    }

    [TestMethod]
    public async Task AndroidAdbTarget_IsDiscoveredRunBySerialAndRecordedInRecentRuns()
    {
        var script = new ScriptDefinition
        {
            Name = "Android tap",
            Steps = [new TapStep { Name = "Tap", X = 10, Y = 20 }]
        };
        var device = new AndroidAdbDevice(
            "SERIAL-USB", "Xiaomi", "M2006C3MG", "10", 29, 720, 1600, 320, 0, AndroidConnectionState.Device);
        var android = new FixedAndroidDeviceService([device]);
        var engine = new ImmediateEngine();
        var viewModel = CreateViewModel(
            new RecordingScriptStore([script]),
            engine,
            androidDeviceService: android,
            androidStateProbe: android,
            adbPathDiscovery: new ValidAdbPathDiscovery());
        await viewModel.InitializeAsync(CancellationToken.None);

        await viewModel.RefreshCommand.ExecuteAsync();
        var target = viewModel.RunTargets.Single();
        target.IsSelected = true;
        Assert.AreEqual(DeviceKind.AndroidAdb, target.DeviceKind);
        Assert.AreEqual("SERIAL-USB", target.Identifier);
        Assert.IsTrue(viewModel.RunCommand.CanExecute(null));

        await viewModel.RunCommand.ExecuteAsync();
        for (var attempt = 0; attempt < 100 && viewModel.IsExecuting; attempt++)
            await Task.Delay(10);

        Assert.IsNotNull(engine.LastRequest);
        Assert.AreEqual("SERIAL-USB", ((AndroidAdbDevice)engine.LastRequest.Target).Serial);
        Assert.AreEqual(@"C:\MEmu\adb.exe", engine.LastRequest.AdbPath);
        Assert.AreEqual(1, viewModel.RecentRuns.Count);
        Assert.AreEqual("SERIAL-USB", viewModel.RecentRuns[0].Instances.Single().Identifier);
        Assert.AreEqual(DeviceKind.AndroidAdb, viewModel.RecentRuns[0].Instances.Single().DeviceKind);
    }

    [TestMethod]
    public async Task AndroidAdbPreview_UsesSelectedSerialWithoutMemuc()
    {
        var script = new ScriptDefinition
        {
            Name = "Android preview",
            Steps = [new TapStep { Name = "Tap", X = 10, Y = 20 }]
        };
        var device = new AndroidAdbDevice(
            "SERIAL-USB", "Xiaomi", "M2006C3MG", "10", 29, 720, 1600, 320, 0, AndroidConnectionState.Device);
        var android = new FixedAndroidDeviceService([device]);
        var viewModel = CreateViewModel(
            new RecordingScriptStore([script]),
            new ImmediateEngine(),
            androidDeviceService: android,
            androidStateProbe: android,
            adbPathDiscovery: new ValidAdbPathDiscovery());
        await viewModel.InitializeAsync(CancellationToken.None);
        viewModel.SelectedStep = viewModel.Steps.Single();
        await viewModel.RefreshCommand.ExecuteAsync();

        Assert.AreEqual("SERIAL-USB", ((AndroidAdbDevice)viewModel.SelectedEditorTarget!.Model).Serial);
        StringAssert.Contains(viewModel.CommandPreview, "-s SERIAL-USB");
        StringAssert.Contains(viewModel.CommandPreview, "adb.exe");
        StringAssert.Contains(viewModel.CommandPreview, "-s SERIAL-USB");
        Assert.IsFalse(viewModel.CommandPreview.Contains("memuc.exe", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public async Task EditorPreview_IsIndependentFromControlCenterRunTargetSelection()
    {
        var script = new ScriptDefinition
        {
            Name = "Android preview",
            Steps = [new TapStep { Name = "Tap", X = 10, Y = 20 }]
        };
        var devices = new[]
        {
            new AndroidAdbDevice("SERIAL-A", "Xiaomi", "A", "10", 29, 720, 1600, 320, 0, AndroidConnectionState.Device),
            new AndroidAdbDevice("SERIAL-B", "Xiaomi", "B", "10", 29, 720, 1600, 320, 0, AndroidConnectionState.Device)
        };
        var android = new FixedAndroidDeviceService(devices);
        var viewModel = CreateViewModel(
            new RecordingScriptStore([script]),
            new ImmediateEngine(),
            androidDeviceService: android,
            androidStateProbe: android,
            adbPathDiscovery: new ValidAdbPathDiscovery());
        await viewModel.InitializeAsync(CancellationToken.None);
        await viewModel.RefreshCommand.ExecuteAsync();
        viewModel.SelectedStep = viewModel.Steps.Single();
        viewModel.SelectedEditorTarget = viewModel.EditorTargets.Single(target => target.Identifier == "SERIAL-A");
        StringAssert.Contains(viewModel.CommandPreview, "-s SERIAL-A");

        viewModel.SelectAllFilteredRunTargetsCommand.Execute(null);
        StringAssert.Contains(viewModel.CommandPreview, "-s SERIAL-A");

        viewModel.ClearRunTargetSelectionCommand.Execute(null);
        StringAssert.Contains(viewModel.CommandPreview, "-s SERIAL-A");

        viewModel.SelectedEditorTarget = viewModel.EditorTargets.Single(target => target.Identifier == "SERIAL-B");
        StringAssert.Contains(viewModel.CommandPreview, "-s SERIAL-B");
        Assert.IsFalse(viewModel.CommandPreview.Contains("SERIAL-A", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task AndroidAdbAssignment_SurvivesDisconnectAndAnotherTargetRun()
    {
        var script = new ScriptDefinition
        {
            Name = "Android tap",
            Steps = [new TapStep { Name = "Tap", X = 10, Y = 20 }]
        };
        var first = new AndroidAdbDevice(
            "SERIAL-A", "Xiaomi", "A", "10", 29, 720, 1600, 320, 0, AndroidConnectionState.Device);
        var second = new AndroidAdbDevice(
            "SERIAL-B", "Xiaomi", "B", "10", 29, 720, 1600, 320, 0, AndroidConnectionState.Device);
        var android = new MutableAndroidDeviceService([first]);
        var settings = new ApplicationSettings { MemucPath = @"C:\MEmu\memuc.exe" };
        settings.MultiInstanceRun.TargetScriptAssignments[first.TargetKey] = script.Id;
        var settingsStore = new MutableSettingsStore(settings);
        var viewModel = CreateViewModel(
            new RecordingScriptStore([script]),
            new ImmediateEngine(),
            settingsStore: settingsStore,
            androidDeviceService: android,
            androidStateProbe: android,
            adbPathDiscovery: new ValidAdbPathDiscovery());
        await viewModel.InitializeAsync(CancellationToken.None);
        await viewModel.RefreshCommand.ExecuteAsync();
        Assert.AreEqual(script.Id, viewModel.RunTargets.Single().AssignedScriptId);

        android.Devices = [second];
        await viewModel.RefreshCommand.ExecuteAsync();
        viewModel.RunTargets.Single().IsSelected = true;
        await viewModel.RunCommand.ExecuteAsync();
        await WaitUntilAsync(() => !viewModel.IsExecuting);

        Assert.AreEqual(script.Id, settingsStore.Current.MultiInstanceRun.TargetScriptAssignments[first.TargetKey]);
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
        IAndroidAdbDeviceService? androidDeviceService = null,
        IAndroidAdbStateProbe? androidStateProbe = null,
        IAdbPathDiscovery? adbPathDiscovery = null,
        IAndroidCoordinateCaptureDialogService? androidCoordinateCaptureDialogService = null,
        IAndroidApplicationPickerService? androidApplicationPickerService = null,
        IAndroidDeviceAliasDialogService? androidDeviceAliasDialogService = null)
    {
        var instances = instanceService ?? new EmptyInstanceService();
        var scheduler = new MultiInstanceExecutionScheduler(
            instances,
            engine,
            new ImmediateLaunchDelay(),
            new MinimumLaunchRandom(),
            new AlwaysPinnedHealthProbe(),
            androidTransportService: androidDeviceService as IAndroidAdbTransportService,
            androidStateProbe: androidStateProbe);
        return new MainViewModel(
            instances, new ValidPathDiscovery(), settingsStore ?? new MemorySettingsStore(), fileDialog ?? new SelectedFileDialog(),
            store, scheduler, new ScriptStepCommandBuilder(new MemuCommandBuilder()), confirmation ?? new AlwaysConfirm(),
            picker ?? new NoopApplicationPicker(), capture ?? new NoopInputCapture(), tapOverlay ?? new NoopTapOverlay(), overlay ?? new NoopSwipeOverlay(),
            transfer, importConflict, androidDeviceService: androidDeviceService,
            adbPathDiscovery: adbPathDiscovery, adbCommandBuilder: new AdbCommandBuilder(),
            androidCoordinateCaptureDialogService: androidCoordinateCaptureDialogService,
            androidApplicationPickerService: androidApplicationPickerService,
            androidDeviceAliasDialogService: androidDeviceAliasDialogService);
    }

    private sealed class TestPresentationSource : PresentationSource
    {
        public override Visual RootVisual { get; set; } = null!;
        public override bool IsDisposed => false;
        protected override CompositionTarget GetCompositionTargetCore() => null!;
    }

    private sealed class AlwaysPinnedHealthProbe : IMemuInstanceHealthProbe
    {
        public Task<MemuInstanceHealthResult> CheckAsync(
            MemuInstance instance,
            MemuInstanceCoreIdentity? expectedCoreIdentity,
            CancellationToken cancellationToken) =>
            Task.FromResult(MemuInstanceHealthResult.HealthyFor(
                expectedCoreIdentity?.ProcessId ?? 900 + instance.Index,
                expectedCoreIdentity?.CreationTimeUtcFileTime ?? 10_000 + instance.Index));
    }

    private sealed class QueuedSynchronizationContext : SynchronizationContext
    {
        private readonly Queue<(SendOrPostCallback Callback, object? State)> callbacks = [];
        public int PostCount { get; private set; }

        public override void Post(SendOrPostCallback d, object? state)
        {
            PostCount++;
            callbacks.Enqueue((d, state));
        }

        public void DrainAll()
        {
            while (callbacks.TryDequeue(out var callback)) callback.Callback(callback.State);
        }
    }

    private sealed class BurstBlockingEngine : IScriptExecutionEngine
    {
        private readonly TaskCompletionSource<ExecutionResult> completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private IProgress<StepExecutionUpdate>? progress;
        private Guid stepId;

        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<ExecutionResult> ExecuteAsync(
            ExecutionRequest request,
            IProgress<StepExecutionUpdate>? progress,
            CancellationToken cancellationToken)
        {
            this.progress = progress;
            stepId = request.Script.Steps[0].Id;
            Started.TrySetResult();
            return await completion.Task.WaitAsync(cancellationToken);
        }

        public void ReportBurst(int count)
        {
            for (var index = 0; index < count; index++)
                progress?.Report(new StepExecutionUpdate(stepId, StepExecutionStatus.Running));
        }

        public void Complete()
        {
            var now = DateTimeOffset.UtcNow;
            completion.TrySetResult(new ExecutionResult { StartedAt = now, EndedAt = now });
        }
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
        public bool ThrowOnSave { get; set; }
        public int SaveAttemptCount { get; private set; }
        public IReadOnlyList<ScriptDefinition> LastSaved { get; private set; } = [];
        public Task<IReadOnlyList<ScriptDefinition>> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(loaded);
        public Task SaveAsync(IReadOnlyCollection<ScriptDefinition> scripts, CancellationToken cancellationToken)
        {
            SaveAttemptCount++;
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

    private sealed class LatestResultEngine : IScriptExecutionEngine
    {
        public Task<ExecutionResult> ExecuteAsync(
            ExecutionRequest request,
            IProgress<StepExecutionUpdate>? progress,
            CancellationToken cancellationToken)
        {
            var now = DateTimeOffset.UtcNow;
            var step = request.Script.Steps[0];
            var status = request.InstanceIndex switch
            {
                2 => StepExecutionStatus.Failed,
                3 => StepExecutionStatus.Cancelled,
                _ => StepExecutionStatus.Succeeded
            };
            var result = new StepExecutionResult
            {
                StepId = step.Id,
                Status = status,
                StartedAt = now,
                EndedAt = now,
                CommandPreview = new string('c', 800),
                StandardOutput = new string('o', 800),
                StandardError = status == StepExecutionStatus.Succeeded ? string.Empty : new string('e', 800)
            };
            progress?.Report(new StepExecutionUpdate(step.Id, StepExecutionStatus.Running));
            progress?.Report(new StepExecutionUpdate(step.Id, status, result));
            return Task.FromResult(new ExecutionResult
            {
                StartedAt = now,
                EndedAt = now,
                WasCancelled = request.InstanceIndex == 3,
                Steps = [result]
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

    private sealed class CancellationCleanupEngine : IScriptExecutionEngine
    {
        private int invocationCount;
        public TaskCompletionSource FirstStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource CancellationObserved { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseCleanup { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int InvocationCount => Volatile.Read(ref invocationCount);

        public async Task<ExecutionResult> ExecuteAsync(
            ExecutionRequest request,
            IProgress<StepExecutionUpdate>? progress,
            CancellationToken cancellationToken)
        {
            var invocation = Interlocked.Increment(ref invocationCount);
            var now = DateTimeOffset.UtcNow;
            if (invocation > 1)
                return new ExecutionResult { StartedAt = now, EndedAt = now };

            FirstStarted.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                CancellationObserved.TrySetResult();
                await ReleaseCleanup.Task;
                return new ExecutionResult
                {
                    StartedAt = now,
                    EndedAt = DateTimeOffset.UtcNow,
                    WasCancelled = true
                };
            }

            throw new AssertFailedException("The first execution must be cancelled.");
        }
    }

    private sealed class StopAllCleanupEngine : IScriptExecutionEngine
    {
        private readonly SemaphoreSlim startedSignal = new(0);
        private readonly SemaphoreSlim cancellationSignal = new(0);
        private int invocationCount;
        private int startedCount;
        private int cancellationCount;

        public TaskCompletionSource ReleaseCleanup { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int InvocationCount => Volatile.Read(ref invocationCount);

        public async Task<ExecutionResult> ExecuteAsync(
            ExecutionRequest request,
            IProgress<StepExecutionUpdate>? progress,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref invocationCount);
            Interlocked.Increment(ref startedCount);
            startedSignal.Release();
            var startedAt = DateTimeOffset.UtcNow;
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                Interlocked.Increment(ref cancellationCount);
                cancellationSignal.Release();
                await ReleaseCleanup.Task;
                return new ExecutionResult
                {
                    StartedAt = startedAt,
                    EndedAt = DateTimeOffset.UtcNow,
                    WasCancelled = true
                };
            }

            throw new AssertFailedException("Every stop-all execution must be cancelled.");
        }

        public async Task WaitForStartsAsync(int count)
        {
            while (Volatile.Read(ref startedCount) < count)
                await startedSignal.WaitAsync(TimeSpan.FromSeconds(2));
        }

        public async Task WaitForCancellationsAsync(int count)
        {
            while (Volatile.Read(ref cancellationCount) < count)
                await cancellationSignal.WaitAsync(TimeSpan.FromSeconds(2));
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
        public System.Collections.Concurrent.ConcurrentDictionary<int, ExecutionRequest> Requests { get; } = [];

        public async Task<ExecutionResult> ExecuteAsync(
            ExecutionRequest request,
            IProgress<StepExecutionUpdate>? progress,
            CancellationToken cancellationToken)
        {
            Requests[request.InstanceIndex] = request;
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
    private sealed class ValidAdbPathDiscovery : IAdbPathDiscovery
    {
        public string FindAdbPath(string? memucPath = null) => @"C:\MEmu\adb.exe";
        public bool IsValidAdbPath(string? path) => !string.IsNullOrWhiteSpace(path);
    }
    private sealed class FixedAndroidDeviceService(IReadOnlyList<AndroidAdbDevice> devices)
        : IAndroidAdbDeviceService, IAndroidAdbTransportService, IAndroidAdbStateProbe
    {
        public Task<IReadOnlyList<AndroidAdbDevice>> GetDevicesAsync(string adbPath, CancellationToken cancellationToken) =>
            Task.FromResult(devices);

        public Task<IReadOnlyList<AdbDeviceListEntry>> GetTransportsAsync(string adbPath, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AdbDeviceListEntry>>(devices.Select(ToTransport).ToList());

        public Task<AndroidAdbStateResult> CheckStateAsync(string adbPath, string serial, CancellationToken cancellationToken) =>
            Task.FromResult(new AndroidAdbStateResult(
                devices.FirstOrDefault(device => device.Serial == serial)?.ConnectionState ?? AndroidConnectionState.Unknown));
    }
    private sealed class MutableAndroidDeviceService(IReadOnlyList<AndroidAdbDevice> devices)
        : IAndroidAdbDeviceService, IAndroidAdbTransportService, IAndroidAdbStateProbe
    {
        public IReadOnlyList<AndroidAdbDevice> Devices { get; set; } = devices;
        public Task<IReadOnlyList<AndroidAdbDevice>> GetDevicesAsync(string adbPath, CancellationToken cancellationToken) =>
            Task.FromResult(Devices);
        public Task<IReadOnlyList<AdbDeviceListEntry>> GetTransportsAsync(string adbPath, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AdbDeviceListEntry>>(Devices.Select(ToTransport).ToList());
        public Task<AndroidAdbStateResult> CheckStateAsync(string adbPath, string serial, CancellationToken cancellationToken) =>
            Task.FromResult(new AndroidAdbStateResult(
                Devices.FirstOrDefault(device => device.Serial == serial)?.ConnectionState ?? AndroidConnectionState.Unknown));
    }
    private static AdbDeviceListEntry ToTransport(AndroidAdbDevice device) =>
        new(device.Serial, device.ConnectionState, device.Product, device.Model, device.Device);
    private sealed class QueueAndroidDeviceAliasDialog(params AndroidDeviceAliasEditResult[] results)
        : IAndroidDeviceAliasDialogService
    {
        private readonly Queue<AndroidDeviceAliasEditResult> results = new(results);
        public List<(string Serial, string? CurrentAlias)> Calls { get; } = [];

        public AndroidDeviceAliasEditResult? Edit(string serial, string? currentAlias)
        {
            Calls.Add((serial, currentAlias));
            return results.Count == 0 ? null : results.Dequeue();
        }
    }
    private sealed class MutableSettingsStore(ApplicationSettings settings) : ISettingsStore
    {
        public ApplicationSettings Current { get; private set; } = settings;
        public Task<ApplicationSettings> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(Current);
        public Task SaveAsync(ApplicationSettings value, CancellationToken cancellationToken)
        {
            Current = value;
            return Task.CompletedTask;
        }
        public Task<ApplicationSettings> UpdateAsync(Action<ApplicationSettings> update, CancellationToken cancellationToken)
        {
            update(Current);
            return Task.FromResult(Current);
        }
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
    private sealed class ThrowingUpdateSettingsStore : ISettingsStore
    {
        public Task<ApplicationSettings> LoadAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new ApplicationSettings());
        public Task SaveAsync(ApplicationSettings settings, CancellationToken cancellationToken) =>
            Task.CompletedTask;
        public Task<ApplicationSettings> UpdateAsync(Action<ApplicationSettings> update, CancellationToken cancellationToken) =>
            Task.FromException<ApplicationSettings>(new IOException("Simulated settings save failure."));
    }
    private sealed class NeverCompletingSettingsStore : ISettingsStore
    {
        private readonly TaskCompletionSource<ApplicationSettings> never =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<ApplicationSettings> LoadAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new ApplicationSettings());
        public Task SaveAsync(ApplicationSettings settings, CancellationToken cancellationToken) =>
            Task.CompletedTask;
        public Task<ApplicationSettings> UpdateAsync(Action<ApplicationSettings> update, CancellationToken cancellationToken) =>
            never.Task;
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
                AdbPath = settings.AdbPath,
                ControlCenterLayout = new ControlCenterLayoutSettings
                {
                    WindowWidth = settings.ControlCenterLayout.WindowWidth,
                    WindowHeight = settings.ControlCenterLayout.WindowHeight,
                    IsMaximized = settings.ControlCenterLayout.IsMaximized,
                    SetupPanelRatio = settings.ControlCenterLayout.SetupPanelRatio,
                    RecentListRatio = settings.ControlCenterLayout.RecentListRatio,
                    SetupPanelWidth = settings.ControlCenterLayout.SetupPanelWidth
                },
                MultiInstanceRun = new MultiInstanceRunSettings
                {
                    LaunchSpacingMode = run.LaunchSpacingMode,
                    FixedSpacingMilliseconds = run.FixedSpacingMilliseconds,
                    RandomMinimumSpacingMilliseconds = run.RandomMinimumSpacingMilliseconds,
                    RandomMaximumSpacingMilliseconds = run.RandomMaximumSpacingMilliseconds,
                    StopAllOnInvalidTarget = run.StopAllOnInvalidTarget,
                    ScriptAssignmentMode = run.ScriptAssignmentMode,
                    CommonScriptId = run.CommonScriptId
                }
            };
            foreach (var pair in run.ScriptAssignments) clone.MultiInstanceRun.ScriptAssignments[pair.Key] = pair.Value;
            foreach (var pair in run.TargetScriptAssignments) clone.MultiInstanceRun.TargetScriptAssignments[pair.Key] = pair.Value;
            foreach (var pair in settings.ApplicationDisplayNames) clone.ApplicationDisplayNames[pair.Key] = pair.Value;
            foreach (var pair in settings.AndroidDeviceAliases) clone.AndroidDeviceAliases[pair.Key] = pair.Value;
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
    private sealed class AndroidApplicationLibraryFileDialog(string? importPath, string? exportPath)
        : IFileDialogService
    {
        public string? SelectMemucPath(string? currentPath) => null;
        public string? SelectScriptImportPath() => null;
        public string? SelectScriptExportPath(string suggestedFileName) => null;
        public string? SelectApplicationNameImportPath() => null;
        public string? SelectApplicationNameExportPath(string suggestedFileName) => null;
        public string? SelectAndroidApplicationLibraryImportPath() => importPath;
        public string? SelectAndroidApplicationLibraryExportPath(string suggestedFileName) => exportPath;
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
            foreach (var pair in settings.AndroidDeviceAliases)
                LastSaved.AndroidDeviceAliases[pair.Key] = pair.Value;
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
    private sealed class RecordingAndroidApplicationLibraryTransferService(
        IReadOnlyList<AndroidApplicationLibraryEntry> imported)
        : IAndroidApplicationLibraryTransferService
    {
        public string? ExportPath { get; private set; }
        public IReadOnlyCollection<AndroidApplicationLibraryEntry>? ExportedEntries { get; private set; }

        public Task ExportAsync(
            string path,
            IReadOnlyCollection<AndroidApplicationLibraryEntry> entries,
            CancellationToken cancellationToken)
        {
            ExportPath = path;
            ExportedEntries = entries.ToList();
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<AndroidApplicationLibraryEntry>> ImportAsync(
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
    private sealed class ConfigurableConfirmation(bool result, Action? onConfirm = null) : IConfirmationService
    {
        public int CallCount { get; private set; }
        public string? LastMessage { get; private set; }
        public string? LastTitle { get; private set; }
        public bool Confirm(string message, string title)
        {
            CallCount++;
            LastMessage = message;
            LastTitle = title;
            var callback = onConfirm;
            onConfirm = null;
            callback?.Invoke();
            return result;
        }
    }
    private sealed class QueueConfirmation(params bool[] results) : IConfirmationService
    {
        private readonly Queue<bool> results = new(results);
        public bool Confirm(string message, string title) => results.Dequeue();
    }

    private sealed class BlockingFailingScriptStore(IReadOnlyList<ScriptDefinition> loaded) : IScriptStore
    {
        public TaskCompletionSource SaveStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseSave { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int SaveAttemptCount { get; private set; }

        public Task<IReadOnlyList<ScriptDefinition>> LoadAsync(CancellationToken cancellationToken) =>
            Task.FromResult(loaded);

        public async Task SaveAsync(IReadOnlyCollection<ScriptDefinition> scripts, CancellationToken cancellationToken)
        {
            SaveAttemptCount++;
            SaveStarted.TrySetResult();
            await ReleaseSave.Task.WaitAsync(cancellationToken);
            throw new IOException("read-only after wait");
        }
    }
    private sealed class DraftDecisionConfirmation(params EditorDraftDecision[] decisions) : IConfirmationService
    {
        private readonly Queue<EditorDraftDecision> decisions = new(decisions);
        public List<(string Description, bool CanSave)> Calls { get; } = [];
        public bool Confirm(string message, string title) => true;
        public EditorDraftDecision DecideEditorDraft(string description, bool canSave)
        {
            Calls.Add((description, canSave));
            return decisions.Dequeue();
        }
    }
    private sealed class NoopApplicationPicker : IApplicationPickerService
    {
        public Task<MemuApplicationInfo?> SelectAsync(string memucPath, int instanceIndex, CancellationToken cancellationToken) =>
            Task.FromResult<MemuApplicationInfo?>(null);
    }
    private sealed class RecordingAndroidCoordinateCaptureDialog(
        Func<AndroidCoordinateCaptureMode, AndroidCoordinateCaptureResult?> resultFactory)
        : IAndroidCoordinateCaptureDialogService
    {
        public List<(string AdbPath, string Serial, AndroidCoordinateCaptureMode Mode)> Calls { get; } = [];

        public Task<AndroidCoordinateCaptureResult?> CaptureAsync(
            string adbPath,
            AndroidAdbDevice device,
            AndroidCoordinateCaptureMode mode,
            CancellationToken cancellationToken)
        {
            Calls.Add((adbPath, device.Serial, mode));
            return Task.FromResult(resultFactory(mode));
        }
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
    private sealed class RecordingAndroidApplicationPicker(AndroidApplicationInfo application)
        : IAndroidApplicationPickerService
    {
        public List<(string AdbPath, string Serial, AndroidApplicationInfo? CurrentSelection)> Calls { get; } = [];

        public Task<AndroidApplicationInfo?> SelectAsync(
            string adbPath,
            string serial,
            AndroidApplicationInfo? currentSelection,
            CancellationToken cancellationToken,
            Action<string, string?>? aliasChanged = null)
        {
            Calls.Add((adbPath, serial, currentSelection));
            return Task.FromResult<AndroidApplicationInfo?>(application);
        }
    }
    private sealed class AliasChangingAndroidApplicationPicker(
        string changedPackage,
        string? changedFriendlyName,
        AndroidApplicationInfo? result) : IAndroidApplicationPickerService
    {
        public Task<AndroidApplicationInfo?> SelectAsync(
            string adbPath,
            string serial,
            AndroidApplicationInfo? currentSelection,
            CancellationToken cancellationToken,
            Action<string, string?>? aliasChanged = null)
        {
            aliasChanged?.Invoke(changedPackage, changedFriendlyName);
            return Task.FromResult(result);
        }
    }
    private sealed class FixedAndroidApplicationService(IReadOnlyList<AndroidApplicationInfo> applications)
        : IAndroidApplicationService
    {
        public (string AdbPath, string Serial)? LastRequest { get; private set; }
        public int RequestCount { get; private set; }

        public Task<IReadOnlyList<AndroidApplicationInfo>> GetApplicationsAsync(
            string adbPath,
            string serial,
            CancellationToken cancellationToken)
        {
            LastRequest = (adbPath, serial);
            RequestCount++;
            return Task.FromResult(applications);
        }
    }
    private sealed class FixedAndroidForegroundApplicationService(AndroidApplicationInfo application)
        : IAndroidForegroundApplicationService
    {
        public string? Serial { get; private set; }
        public Task<AndroidApplicationInfo> GetForegroundApplicationAsync(
            string adbPath,
            string serial,
            CancellationToken cancellationToken)
        {
            Serial = serial;
            return Task.FromResult(application);
        }
    }
    private sealed class ThrowingAndroidForegroundApplicationService : IAndroidForegroundApplicationService
    {
        public Task<AndroidApplicationInfo> GetForegroundApplicationAsync(
            string adbPath,
            string serial,
            CancellationToken cancellationToken) =>
            Task.FromException<AndroidApplicationInfo>(
                new AndroidAdbDeviceUnavailableException($"{serial} offline"));
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
