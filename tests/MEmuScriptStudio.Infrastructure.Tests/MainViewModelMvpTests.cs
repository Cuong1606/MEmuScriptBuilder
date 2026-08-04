using MEmuScriptStudio.App.Services;
using MEmuScriptStudio.App.ViewModels;
using MEmuScriptStudio.App;
using MEmuScriptStudio.App.Converters;
using MEmuScriptStudio.App.Views;
using MEmuScriptStudio.Core.Execution;
using MEmuScriptStudio.Core.MEmu;
using MEmuScriptStudio.Core.Models;
using MEmuScriptStudio.Core.Scripts;
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

        Assert.AreEqual(
            ShutdownMode.OnMainWindowClose,
            Application.Current!.ShutdownMode,
            "The application must declare close-driven MainWindow shutdown explicitly.");

        var viewModel = new ApplicationPickerViewModel(
            new MutableApplicationService([]), @"C:\MEmu\memuc.exe", 0);
        var window = new ApplicationPickerWindow(viewModel);

        Assert.IsNotNull(window.FindName("ManualDisplayNameTextBox"));
        Assert.IsNotNull(window.FindName("SaveApplicationNameButton"));
        var applicationsGrid = (DataGrid)window.FindName("ApplicationsGrid");
        Assert.IsFalse(ScrollViewer.GetIsDeferredScrollingEnabled(applicationsGrid));
        Assert.IsTrue(VirtualizingPanel.GetIsVirtualizing(applicationsGrid));
        Assert.AreEqual(VirtualizationMode.Recycling, VirtualizingPanel.GetVirtualizationMode(applicationsGrid));
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
        var initializationOverlay = (Border)window.FindName("InitializationOverlay");

        Assert.AreEqual(BindingMode.OneWay, BindingOperations.GetBinding(memucPath, TextBlock.TextProperty)!.Mode);
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
    public void MainWindow_OutsideClickClearsStepSelection()
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
            stepsGrid.SelectedItems.Clear();
            stepsGrid.SelectedItems.Add(viewModel.Steps[0]);
            stepsGrid.SelectedItems.Add(viewModel.Steps[2]);

            Assert.IsTrue(window.HandleWindowPreviewMouseDown((DependencyObject)window.FindName("MainStatusBar")));
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

            Assert.IsTrue(window.HandleWindowPreviewMouseDown(propertiesPanel));
            Assert.IsTrue(window.HandleWindowPreviewMouseDown(editorInput));
            Assert.IsTrue(window.HandleWindowPreviewMouseDown(actionBar));
            Assert.IsTrue(window.HandleWindowPreviewMouseDown(copyButton));
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
            window.HandleWindowPreviewKeyDownAsync(deleteKey, ModifierKeys.None, scriptsList)
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
    public void ControlCenter_HasNoHistoryRouteAndKeepsLatestResultWithRunTargetVirtualization()
    {
        if (Application.Current is null)
        {
            var application = new MEmuScriptStudio.App.App();
            application.InitializeComponent();
        }

        var runPanel = new RunControlPanel();
        var window = new ControlCenterWindow(CreateViewModel(new RecordingScriptStore(), new ImmediateEngine()));
        var tabHeaders = FindLogicalDescendants<TabItem>(window).Select(item => item.Header?.ToString()).ToList();
        CollectionAssert.AreEqual(new[] { "Đang hoạt động" }, tabHeaders);
        CollectionAssert.DoesNotContain(tabHeaders, "Lịch sử");
        CollectionAssert.DoesNotContain(tabHeaders, "Trang và thứ tự");
        Assert.IsNull(window.FindName("LayoutPanel"));
        Assert.IsNull(window.FindName("HistoryPanel"));
        Assert.IsNull(typeof(MainViewModel).Assembly.GetType("MEmuScriptStudio.App.Views.WindowLayoutPanel"));
        Assert.IsNull(typeof(MainViewModel).Assembly.GetType("MEmuScriptStudio.App.Views.ExecutionHistoryPanel"));
        Assert.IsNotNull(runPanel.FindName("LatestRunResultCard"));
        var issueGrid = (DataGrid)runPanel.FindName("LatestRunIssuesGrid");
        Assert.AreEqual("LatestRunResult.IssueInstances",
            BindingOperations.GetBinding(issueGrid, ItemsControl.ItemsSourceProperty)!.Path.Path);
        Assert.AreEqual("LatestRunResult.HasIssues",
            BindingOperations.GetBinding(issueGrid, UIElement.VisibilityProperty)!.Path.Path);
        var noIssuesText = (TextBlock)runPanel.FindName("LatestRunNoIssuesText");
        Assert.AreEqual("LatestRunResult.HasNoIssues",
            BindingOperations.GetBinding(noIssuesText, UIElement.VisibilityProperty)!.Path.Path);
        Assert.IsTrue(VirtualizingPanel.GetIsVirtualizing(issueGrid));
        Assert.AreEqual(VirtualizationMode.Recycling, VirtualizingPanel.GetVirtualizationMode(issueGrid));
        var issueMessageColumn = issueGrid.Columns.OfType<DataGridTemplateColumn>()
            .Single(column => Equals(column.Header, "Thông báo"));
        var issueMessage = (TextBlock)issueMessageColumn.CellTemplate.LoadContent();
        Assert.AreEqual("ErrorMessage", BindingOperations.GetBinding(issueMessage, TextBlock.TextProperty)!.Path.Path);
        Assert.AreEqual("ErrorMessage", BindingOperations.GetBinding(issueMessage, FrameworkElement.ToolTipProperty)!.Path.Path);

        var runTargets = (DataGrid)runPanel.FindName("RunTargetsGrid");
        Assert.IsTrue(VirtualizingPanel.GetIsVirtualizing(runTargets));
        Assert.AreEqual(VirtualizationMode.Recycling, VirtualizingPanel.GetVirtualizationMode(runTargets));
        Assert.IsTrue(ScrollViewer.GetCanContentScroll(runTargets));
        Assert.IsTrue(runTargets.EnableRowVirtualization);
        Assert.IsTrue(runTargets.EnableColumnVirtualization);
        Assert.AreEqual(nameof(MainViewModel.FilteredRunTargets),
            BindingOperations.GetBinding(runTargets, ItemsControl.ItemsSourceProperty)!.Path.Path);

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
        viewModel.SelectAllFilteredRunTargetsCommand.Execute(null);
        Assert.AreEqual(37, viewModel.SelectedRunTargetCount);
        Assert.IsTrue(viewModel.RunTargets.Where(item => !item.IsRunning).All(item => item.IsSelected));
        Assert.IsTrue(viewModel.RunTargets.Where(item => item.IsRunning).All(item => !item.IsSelected));
        Assert.AreEqual("Đã chọn 37 / Tổng 75", viewModel.RunTargetSelectionSummary);

        viewModel.ScriptAssignmentMode = ScriptAssignmentMode.PerInstance;
        viewModel.ControlCenterSelectedScript = viewModel.Scripts.Single(item => item.Id == second.Id);
        viewModel.AssignScriptToSelectedCommand.ExecuteAsync().GetAwaiter().GetResult();

        Assert.IsTrue(viewModel.RunTargets.Where(item => !item.IsRunning).All(item => item.AssignedScriptId == second.Id));
        Assert.IsTrue(viewModel.RunTargets.Where(item => item.IsRunning).All(item => item.AssignedScriptId is null));
        Assert.AreEqual(37, viewModel.SelectedRunTargetCount);

        viewModel.SelectedScript = viewModel.Scripts.Single(item => item.Id == first.Id);
        Assert.AreEqual(second.Id, viewModel.ControlCenterSelectedScript!.Id,
            "MainWindow script selection must not change the Control Center script selection.");
        Assert.IsTrue(viewModel.AssignCurrentScriptToAllCommand.CanExecute(null),
            "Assign-all must remain independent from run selection.");
        viewModel.AssignCurrentScriptToAllCommand.ExecuteAsync().GetAwaiter().GetResult();
        Assert.IsTrue(viewModel.RunTargets.All(item => item.AssignedScriptId == second.Id));
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
                Guid.NewGuid(), targets[index], script.Model, (_, _) => { }));
        var panel = new RunControlPanel { DataContext = viewModel };
        var activeGrid = (DataGrid)panel.FindName("ActiveInstancesGrid");

        Assert.AreEqual(500, viewModel.RunTargets.Count);
        Assert.AreEqual(500, viewModel.FilteredRunTargetCount);
        Assert.AreEqual(200, viewModel.ActiveInstanceRuns.Count);
        Assert.AreEqual(nameof(MainViewModel.ActiveInstanceRuns),
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
        await viewModel.DeleteStepCommand.ExecuteAsync();

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
            new MemuInstance(2, "Stopped", false, null)
        };
        var instances = new FixedInstanceService(targets);
        var engine = new ReportingMultiEngine();
        var viewModel = CreateViewModel(new RecordingScriptStore(), engine, instanceService: instances);
        await viewModel.InitializeAsync(CancellationToken.None);
        await viewModel.RefreshCommand.ExecuteAsync();
        foreach (var target in viewModel.RunTargets) target.IsSelected = true;

        await viewModel.RunCommand.ExecuteAsync();
        await WaitUntilAsync(() => viewModel.LatestRunResult?.GroupName == "Nhóm 01");

        CollectionAssert.AreEqual(new[] { 1 }, engine.Requests.Select(request => request.InstanceIndex).ToArray());
        Assert.AreEqual(2, viewModel.LatestRunResult!.TotalInstanceCount);
        Assert.AreEqual(1, viewModel.LatestRunResult.SucceededCount);
        Assert.AreEqual(1, viewModel.LatestRunResult.FailedCount);
        Assert.AreEqual(2, viewModel.LatestRunResult.IssueInstances.Single().Index);

        engine.Requests.Clear();
        viewModel.StopAllOnInvalidTarget = true;
        foreach (var target in viewModel.RunTargets) target.IsSelected = true;
        await viewModel.RunCommand.ExecuteAsync();
        await WaitUntilAsync(() => viewModel.LatestRunResult?.GroupName == "Nhóm 02");

        Assert.AreEqual(0, engine.Requests.Count);
        Assert.AreEqual(0, viewModel.LatestRunResult!.SucceededCount);
        Assert.AreEqual(1, viewModel.LatestRunResult.FailedCount);
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
            var instance = (ComboBox)window.FindName("InstanceComboBox");
            var browse = (Button)window.FindName("BrowseMemucButton");
            var refresh = (Button)window.FindName("RefreshInstancesButton");
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
            Assert.AreEqual(nameof(MainViewModel.MemucPath),
                BindingOperations.GetBinding(pathText, TextBlock.TextProperty)!.Path.Path);
            Assert.AreEqual(nameof(MainViewModel.MemucPath),
                BindingOperations.GetBinding(path, FrameworkElement.ToolTipProperty)!.Path.Path);
            Assert.AreEqual(34d, instance.Height);
            Assert.AreEqual(new Thickness(10, 5, 10, 5), instance.Padding);
            Assert.AreEqual(VerticalAlignment.Center, instance.VerticalAlignment);
            Assert.AreEqual(VerticalAlignment.Center, instance.VerticalContentAlignment);
            Assert.AreEqual(HorizontalAlignment.Stretch, instance.HorizontalContentAlignment);
            Assert.IsTrue(string.IsNullOrEmpty(instance.DisplayMemberPath));

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
            var latest = (DataGrid)runPanel.FindName("LatestRunIssuesGrid");

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

            foreach (var grid in new[] { steps, targets, latest, active })
            {
                Assert.IsTrue(grid.EnableRowVirtualization);
                Assert.IsTrue(grid.EnableColumnVirtualization);
                Assert.IsTrue(grid.Columns.All(column => !column.Width.IsAbsolute),
                    $"{grid.Name} columns must use Auto/* sizing instead of fixed pixel widths.");
            }

            Assert.IsTrue(active.Columns.All(column => column.MinWidth >= 50),
                "Active columns need readable minima and horizontal scrolling instead of collapsing to a few characters.");
            Assert.IsTrue(latest.Columns.All(column => column.MinWidth >= 56),
                "Latest-result columns need readable minima and horizontal scrolling instead of collapsing to a few characters.");

            var rootColumns = (Grid)runPanel.FindName("RunControlColumns");
            Assert.AreEqual(GridUnitType.Star, rootColumns.ColumnDefinitions[0].Width.GridUnitType);
            Assert.AreEqual(GridUnitType.Star, rootColumns.ColumnDefinitions[2].Width.GridUnitType);
            var reservedWidth = rootColumns.ColumnDefinitions.Sum(column =>
                column.MinWidth + (column.Width.IsAbsolute ? column.Width.Value : 0)) + 80;
            Assert.IsTrue(controlCenter.MinWidth >= reservedWidth,
                "The Control Center minimum width must include its content minima plus window/tab/panel chrome.");
            Assert.AreEqual(720d, controlCenter.Height);
            Assert.AreEqual(720d, controlCenter.MinHeight);
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
            Assert.AreEqual(nameof(ScriptItemViewModel.Name),
                BindingOperations.GetBinding(comboText, TextBlock.TextProperty)!.Path.Path);
            Assert.AreEqual(nameof(ScriptItemViewModel.Name),
                BindingOperations.GetBinding(comboText, FrameworkElement.ToolTipProperty)!.Path.Path);

            var stepsTitle = (TextBlock)mainWindow.FindName("StepsHeaderTitle");
            Assert.AreSame(Application.Current!.FindResource("SectionTitleStyle"), stepsTitle.Style);
            Assert.AreEqual(36d, steps.RowHeight);
            var overlay = (Border)mainWindow.FindName("InitializationOverlay");
            Assert.AreSame(Application.Current.FindResource("CanvasBrush"), overlay.Background,
                "Startup overlay must use the semantic canvas token instead of a hard-coded light color.");
            var emptyActive = (TextBlock)runPanel.FindName("ActiveInstancesEmptyState");
            Assert.IsTrue(emptyActive.Style.Triggers.OfType<DataTrigger>().Any(trigger =>
                trigger.Binding is Binding binding && binding.Path.Path == "ActiveInstanceRuns.Count"));

            var latestTitle = FindLogicalDescendants<TextBlock>(runPanel)
                .Single(text => Equals(text.Text, "Kết quả lần chạy gần nhất"));
            Assert.AreEqual(TextTrimming.CharacterEllipsis, latestTitle.TextTrimming);
            Assert.AreEqual("Kết quả lần chạy gần nhất", latestTitle.ToolTip);

            var assignSelected = (Button)runPanel.FindName("AssignScriptToSelectedButton");
            var assignAll = (Button)runPanel.FindName("AssignSelectedScriptToAllButton");
            Assert.AreSame(LogicalTreeHelper.GetParent(assignSelected), LogicalTreeHelper.GetParent(assignAll));
            Assert.IsInstanceOfType<WrapPanel>(LogicalTreeHelper.GetParent(assignSelected));
            Assert.AreEqual(34d, assignSelected.Height);
            Assert.AreEqual(34d, assignAll.Height);

            var runSelected = (Button)runPanel.FindName("RunSelectedButton");
            var runAll = (Button)runPanel.FindName("RunAllRemainingButton");
            var stopAll = FindLogicalDescendants<Button>(runPanel).Single(button => Equals(button.Content, "Dừng tất cả"));
            var runActions = LogicalTreeHelper.GetParent(runSelected);
            Assert.AreSame(runActions, LogicalTreeHelper.GetParent(runAll));
            Assert.IsInstanceOfType<WrapPanel>(runActions);
            Assert.AreEqual(nameof(MainViewModel.StopCommand),
                BindingOperations.GetBinding(stopAll, Button.CommandProperty)!.Path.Path);
            Assert.AreEqual(3, Grid.GetRow((UIElement)runActions),
                "Run actions must occupy a separate row so they cannot squeeze the spacing controls into a narrow column.");

            viewModel.ScriptAssignmentMode = ScriptAssignmentMode.PerInstance;
            controlCenter.Width = controlCenter.MinWidth;
            controlCenter.Height = controlCenter.MinHeight;
            controlCenter.Show();
            controlCenter.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            controlCenter.UpdateLayout();
            Assert.IsTrue(targets.ActualHeight >= 70,
                "At minimum window size, the per-instance target grid must retain its header and at least one complete data row viewport.");
            var spacingOptions = (WrapPanel)runPanel.FindName("LaunchSpacingOptions");
            Assert.IsTrue(spacingOptions.ActualHeight <= 40,
                "Spacing options must remain on one compact row at the minimum supported width.");
        }
        finally
        {
            controlCenter.Close();
            mainWindow.Close();
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
    public void ControlCenterEntryAndLatestResult_UseTheIntendedXamlContracts()
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
            var latestCard = (Border)runPanel.FindName("LatestRunResultCard");
            var latestGrid = (DataGrid)runPanel.FindName("LatestRunIssuesGrid");
            var clearButton = FindLogicalDescendants<Button>(runPanel).Single(button => Equals(button.Content, "Xóa kết quả"));

            Assert.AreEqual(1, controlCenterButtons.Count, "MainWindow must expose a single Control Center entry point.");
            Assert.AreSame(window.FindName("OpenControlCenterButton"), controlCenterButtons.Single());
            Assert.AreEqual(0, FindLogicalDescendants<Button>(statusBar).Count(), "The bottom status bar is data-only.");
            Assert.IsNotNull(latestCard);
            Assert.AreEqual("LatestRunResult.IssueInstances",
                BindingOperations.GetBinding(latestGrid, ItemsControl.ItemsSourceProperty)!.Path.Path);
            Assert.AreEqual(nameof(MainViewModel.ClearLatestRunResultCommand),
                BindingOperations.GetBinding(clearButton, Button.CommandProperty)!.Path.Path);
            Assert.IsTrue(FindLogicalDescendants<TextBlock>(runPanel)
                .Any(text => Equals(text.Text, "Chưa có nhóm chạy nào hoàn tất trong phiên này.")));
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

        Assert.AreEqual("Gán kịch bản đang chọn cho tất cả", assignAll.Content);
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
        Assert.IsTrue(activeGrid.Columns.All(column => !column.Width.IsAbsolute),
            "Active columns must size from content or available space instead of fixed pixel widths.");
        CollectionAssert.AreEqual(
            new[] { "Chọn", "Index", "Tên instance", "Kịch bản", "Bước hiện tại", "Trạng thái", "Dừng" },
            activeGrid.Columns.Select(column => column.Header?.ToString()).ToArray());
        Assert.AreEqual(0, FindLogicalDescendants<Expander>(panel).Count(),
            "The Active surface must not contain launch-group expanders.");
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
        Assert.IsTrue(viewModel.StopSelectedActiveInstancesCommand.CanExecute(null));
        viewModel.StopSelectedActiveInstancesCommand.Execute(null);
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
                (_, _) => { }))
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
                    Runtime: new InstanceRunItemViewModel(groupId, target, script, (_, _) => { }),
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
    public async Task LatestRunResult_OnlyKeepsFailedOrCancelledSummariesWithoutFullLog()
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
        Assert.IsTrue(latest.HasIssues);
        Assert.IsFalse(latest.HasNoIssues);
        CollectionAssert.AreEquivalent(new[] { 2, 3 }, latest.IssueInstances.Select(item => item.Index).ToArray());
        Assert.IsTrue(latest.IssueInstances.All(item => item.Index != 1));
        Assert.IsTrue(latest.IssueInstances.All(item => item.ErrorMessage.Length <= 240));
        Assert.IsTrue(latest.IssueInstances.All(item => item.LastStep == "Step 1"));
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
        await viewModel.DeleteStepCommand.ExecuteAsync();

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
        StringAssert.Contains(viewModel.LatestRunResult!.RunDescription, "Kịch bản riêng theo giả lập");
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
    public async Task RunSelected_PreflightExcludesStoppedTargetAndSnapshotsOnlyValidSelection()
    {
        var engine = new ReportingMultiEngine();
        var instances = new[]
        {
            new MemuInstance(1, "Running", true, 101, 1001),
            new MemuInstance(2, "Stopped", false, 0, 0)
        };
        var viewModel = CreateViewModel(
            new RecordingScriptStore(), engine, instanceService: new FixedInstanceService(instances));
        await viewModel.InitializeAsync(CancellationToken.None);
        await viewModel.RefreshCommand.ExecuteAsync();
        foreach (var target in viewModel.RunTargets) target.IsSelected = true;

        await viewModel.RunCommand.ExecuteAsync();
        await WaitUntilAsync(() => viewModel.LatestRunResult is not null);

        CollectionAssert.AreEqual(new[] { 1 }, engine.Requests.Select(request => request.InstanceIndex).ToArray());
        Assert.AreEqual(2, viewModel.LatestRunResult!.TotalInstanceCount);
        Assert.AreEqual(1, viewModel.LatestRunResult.SucceededCount);
        Assert.AreEqual(1, viewModel.LatestRunResult.FailedCount);
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

        Assert.AreEqual(0, layoutMembers.Length, $"Layout members remain: {string.Join(", ", layoutMembers)}");
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
        ISettingsStore? settingsStore = null)
    {
        var instances = instanceService ?? new EmptyInstanceService();
        var scheduler = new MultiInstanceExecutionScheduler(instances, engine, new ImmediateLaunchDelay(), new MinimumLaunchRandom());
        return new MainViewModel(
            instances, new ValidPathDiscovery(), settingsStore ?? new MemorySettingsStore(), fileDialog ?? new SelectedFileDialog(),
            store, scheduler, new ScriptStepCommandBuilder(new MemuCommandBuilder()), confirmation ?? new AlwaysConfirm(),
            picker ?? new NoopApplicationPicker(), capture ?? new NoopInputCapture(), tapOverlay ?? new NoopTapOverlay(), overlay ?? new NoopSwipeOverlay(),
            transfer, importConflict);
    }

    private sealed class TestPresentationSource : PresentationSource
    {
        public override Visual RootVisual { get; set; } = null!;
        public override bool IsDisposed => false;
        protected override CompositionTarget GetCompositionTargetCore() => null!;
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
                    ScriptAssignmentMode = run.ScriptAssignmentMode,
                    CommonScriptId = run.CommonScriptId
                }
            };
            foreach (var pair in run.ScriptAssignments) clone.MultiInstanceRun.ScriptAssignments[pair.Key] = pair.Value;
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
