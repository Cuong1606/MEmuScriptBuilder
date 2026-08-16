using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Interop;
using MEmuScriptStudio.App.Behaviors;
using MEmuScriptStudio.App.Services;
using MEmuScriptStudio.App.ViewModels;

namespace MEmuScriptStudio.App;

public partial class MainWindow : Window, IStartupWindow
{
    private Point dragStart;
    private StepItemViewModel? draggedStep;
    private CompositeItemViewModel? draggedCompositeItem;
    private ScriptItemViewModel? draggedScript;
    private InsertionAdorner? insertionAdorner;
    private int pendingInsertionIndex;
    private bool restoringStepSelection;
    private bool restoringCompositeSelection;
    private bool restoringScriptSelection;
    private bool scriptSelectionDeferredForDrag;
    private bool suppressDeviceSettingsPopupReopen;
    private bool suppressScriptLibraryPopupReopen;
    private readonly MainWindowCloseCoordinator closeCoordinator = new();
    private bool loadedLogged;
    private bool contentRenderedLogged;
    private readonly ControlCenterWindowManager controlCenterWindowManager;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        controlCenterWindowManager = new ControlCenterWindowManager(context => new ControlCenterWindow(context));
        viewModel.StepSelectionRestoreRequested += RestoreStepSelection;
        viewModel.StepFocusRequested += FocusStep;
        viewModel.CompositeSelectionRestoreRequested += RestoreCompositeSelection;
        viewModel.ScriptSelectionRestoreRequested += RestoreScriptSelection;
        viewModel.PropertyChanged += ViewModel_EditorStateChanged;
        Loaded += OnMainWindowLoaded;
        _ = Dispatcher.BeginInvoke(
            () => RestoreScriptSelection(viewModel.SelectedScripts, focus: false),
            System.Windows.Threading.DispatcherPriority.DataBind);
    }

    internal void ResetEditorPaneLayout()
    {
        LibraryPaneColumn.Width = new GridLength(5, GridUnitType.Star);
        StepsPaneColumn.Width = new GridLength(8, GridUnitType.Star);
        PropertiesPaneColumn.Width = new GridLength(7, GridUnitType.Star);
    }

    private void EditorSplitter_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        ResetEditorPaneLayout();
        e.Handled = true;
    }

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        if (contentRenderedLogged) return;
        contentRenderedLogged = true;
        ApplicationLifecycleLogger.Write($"MainWindow ContentRendered HWND={new WindowInteropHelper(this).Handle}");
    }

    protected override async void OnClosing(CancelEventArgs e)
    {
        CommitEditorBoundaryInput(Keyboard.FocusedElement as DependencyObject);
        if (DataContext is MainViewModel viewModel &&
            closeCoordinator.RequiresDeferral(viewModel, controlCenterWindowManager.HasCurrent))
        {
            e.Cancel = true;
            base.OnClosing(e);
            ApplicationLifecycleLogger.Write($"MainWindow Closing Cancel={e.Cancel}");
            try
            {
                var approved = await closeCoordinator.TryResolveAsync(viewModel, async () =>
                {
                    var controlCenterClosed = await controlCenterWindowManager.CloseCurrentAsync();
                    if (!controlCenterClosed)
                        ApplicationLifecycleLogger.Write("ControlCenter did not confirm Closed before the shutdown deadline");
                });
                if (approved) _ = Dispatcher.BeginInvoke(Close);
            }
            catch (Exception exception) { viewModel.ReportUnexpectedError(exception); }
            return;
        }
        base.OnClosing(e);
        ApplicationLifecycleLogger.Write($"MainWindow Closing Cancel={e.Cancel}");
    }

    protected override void OnClosed(EventArgs e)
    {
        ApplicationLifecycleLogger.Write("MainWindow Closed");
        Loaded -= OnMainWindowLoaded;
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.StepSelectionRestoreRequested -= RestoreStepSelection;
            viewModel.StepFocusRequested -= FocusStep;
            viewModel.CompositeSelectionRestoreRequested -= RestoreCompositeSelection;
            viewModel.ScriptSelectionRestoreRequested -= RestoreScriptSelection;
            viewModel.PropertyChanged -= ViewModel_EditorStateChanged;
        }
        controlCenterWindowManager.CloseCurrent();
        base.OnClosed(e);
    }

    private void OnMainWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (loadedLogged) return;
        loadedLogged = true;
        ApplicationLifecycleLogger.Write($"MainWindow Loaded HWND={new WindowInteropHelper(this).Handle}");
    }

    private void OpenControlCenter_Click(object sender, RoutedEventArgs e)
    {
        if (closeCoordinator.IsResolutionInProgress || closeCoordinator.IsCloseApproved || !IsLoaded) return;
        controlCenterWindowManager.TryOpen(DataContext, ReportControlCenterOpenError);
    }

    private void DeviceSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        ScriptLibraryMorePopup.IsOpen = false;
        if (suppressDeviceSettingsPopupReopen)
        {
            suppressDeviceSettingsPopupReopen = false;
            return;
        }
        TogglePopup(DeviceSettingsPopup);
    }

    private void ScriptLibraryMoreButton_Click(object sender, RoutedEventArgs e)
    {
        DeviceSettingsPopup.IsOpen = false;
        if (suppressScriptLibraryPopupReopen)
        {
            suppressScriptLibraryPopupReopen = false;
            return;
        }
        TogglePopup(ScriptLibraryMorePopup);
    }

    private void DeviceSettingsPopup_Closed(object? sender, EventArgs e)
    {
        if (Mouse.LeftButton == MouseButtonState.Pressed && DeviceSettingsButton.IsMouseOver)
            suppressDeviceSettingsPopupReopen = true;
    }

    private void ScriptLibraryMorePopup_Closed(object? sender, EventArgs e)
    {
        if (Mouse.LeftButton == MouseButtonState.Pressed && ScriptLibraryMoreButton.IsMouseOver)
            suppressScriptLibraryPopupReopen = true;
    }

    private void PopupToggleButton_LostMouseCapture(object sender, MouseEventArgs e)
    {
        var deviceSettingsButtonLostCapture = ReferenceEquals(sender, DeviceSettingsButton);
        _ = Dispatcher.BeginInvoke(() =>
        {
            if (deviceSettingsButtonLostCapture) suppressDeviceSettingsPopupReopen = false;
            else suppressScriptLibraryPopupReopen = false;
        }, System.Windows.Threading.DispatcherPriority.ContextIdle);
    }

    private void TogglePopup(Popup popup)
    {
        popup.IsOpen = !popup.IsOpen;
        if (popup.IsOpen)
            Dispatcher.BeginInvoke(() => popup.Child?.MoveFocus(new TraversalRequest(FocusNavigationDirection.First)));
    }

    private void CloseDeviceSettingsPopup_Click(object sender, RoutedEventArgs e) =>
        DeviceSettingsPopup.IsOpen = false;

    private void CloseScriptLibraryMorePopup_Click(object sender, RoutedEventArgs e) =>
        ScriptLibraryMorePopup.IsOpen = false;

    private void DeviceSettingsPopupContent_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        DeviceSettingsPopup.IsOpen = false;
        e.Handled = true;
        _ = Dispatcher.BeginInvoke(DeviceSettingsButton.Focus);
    }

    private void ScriptLibraryMorePopupContent_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        ScriptLibraryMorePopup.IsOpen = false;
        e.Handled = true;
        _ = Dispatcher.BeginInvoke(ScriptLibraryMoreButton.Focus);
    }

    private void TestStepButton_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) =>
        CommitEditorBoundaryInput(Keyboard.FocusedElement as DependencyObject);

    private void ReportControlCenterOpenError(Exception exception)
    {
        var logPath = ApplicationErrorReporter.Report(exception, "OpenControlCenter");
        if (DataContext is MainViewModel viewModel) viewModel.ReportUnexpectedError(exception);
        if (closeCoordinator.IsResolutionInProgress || closeCoordinator.IsCloseApproved || !IsLoaded || !IsVisible) return;
        var logHint = string.IsNullOrWhiteSpace(logPath) ? string.Empty : $"\n\nChi tiết đã được ghi tại:\n{logPath}";
        try
        {
            MessageBox.Show(
                this,
                $"Không thể mở Trung tâm điều khiển. Cửa sổ chính vẫn hoạt động.\n\n{exception.Message}{logHint}",
                "Lỗi Trung tâm điều khiển",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch (Exception dialogException)
        {
            ApplicationErrorReporter.Report(
                new AggregateException("Không thể hiển thị thông báo lỗi Trung tâm điều khiển.", exception, dialogException),
                "OpenControlCenterErrorDialog");
        }
    }

    private async void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        await HandleWindowPreviewKeyDownAsync(
            e,
            Keyboard.Modifiers,
            Keyboard.FocusedElement as DependencyObject);
    }

    internal async Task HandleWindowPreviewKeyDownAsync(
        KeyEventArgs e,
        ModifierKeys modifiers,
        DependencyObject? focusedElement)
    {
        if (e.Key == Key.Escape && modifiers == ModifierKeys.None &&
            (DeviceSettingsPopup.IsOpen || ScriptLibraryMorePopup.IsOpen))
        {
            DeviceSettingsPopup.IsOpen = false;
            ScriptLibraryMorePopup.IsOpen = false;
            e.Handled = true;
            return;
        }
        if (DataContext is not MainViewModel viewModel) return;

        if (HasAncestor(focusedElement, ScriptsList) && FindAncestor<TextBoxBase>(focusedElement) is null)
        {
            try
            {
                if (e.Key == Key.Escape && modifiers == ModifierKeys.None &&
                    viewModel.HasMultipleSelectedScripts)
                {
                    e.Handled = true;
                    await ApplyScriptSelectionAsync(viewModel.SelectedScript is null
                        ? []
                        : [viewModel.SelectedScript]);
                    return;
                }
                if (e.Key == Key.A && modifiers == ModifierKeys.Control)
                {
                    e.Handled = true;
                    await ApplyScriptSelectionAsync(ScriptsList.Items.Cast<ScriptItemViewModel>().ToList());
                    return;
                }
                if (e.Key == Key.D && modifiers == ModifierKeys.Control &&
                    viewModel.DuplicateScriptCommand.CanExecute(null))
                {
                    e.Handled = true;
                    await viewModel.DuplicateScriptCommand.ExecuteAsync();
                    return;
                }
                if (e.Key == Key.Delete && modifiers == ModifierKeys.None &&
                    viewModel.DeleteScriptCommand.CanExecute(null))
                {
                    e.Handled = true;
                    await viewModel.DeleteScriptCommand.ExecuteAsync();
                    return;
                }
                if (e.Key == Key.F2 && modifiers == ModifierKeys.None && viewModel.SelectedScript is not null)
                {
                    e.Handled = true;
                    ScriptNameTextBox.Focus();
                    ScriptNameTextBox.SelectAll();
                    return;
                }
            }
            catch (Exception exception)
            {
                viewModel.ReportUnexpectedError(exception);
                return;
            }
        }

        if (modifiers == ModifierKeys.None && HasAncestor(focusedElement, ScriptNameTextBox))
        {
            if (e.Key == Key.Escape && viewModel.CancelScriptRenameCommand.CanExecute(null))
            {
                e.Handled = true;
                viewModel.CancelScriptRenameCommand.Execute(null);
                return;
            }
            if (e.Key == Key.Enter)
            {
                CommitEditorBoundaryInput(focusedElement);
                if (viewModel.RenameScriptCommand.CanExecute(null))
                {
                    e.Handled = true;
                    await viewModel.RenameScriptCommand.ExecuteAsync();
                }
                return;
            }
        }

        if (e.Key == Key.Escape && modifiers == ModifierKeys.None &&
            viewModel.CancelStepCreateCommand.CanExecute(null))
        {
            e.Handled = true;
            viewModel.CancelStepCreateCommand.Execute(null);
            return;
        }

        if (e.Key == Key.S && modifiers == ModifierKeys.Control)
        {
            if (HasAncestor(focusedElement, ScriptNameTextBox))
            {
                CommitEditorBoundaryInput(focusedElement);
                if (viewModel.RenameScriptCommand.CanExecute(null))
                {
                    e.Handled = true;
                    await viewModel.RenameScriptCommand.ExecuteAsync();
                }
                return;
            }
            if (HasAncestor(focusedElement, RegularStepPropertiesPanel))
            {
                CommitEditorBoundaryInput(focusedElement);
                if (viewModel.IsStepEditorCreate)
                {
                    e.Handled = true;
                    if (viewModel.AddStepCommand.CanExecute(null))
                        await viewModel.AddStepCommand.ExecuteAsync();
                }
                else if (viewModel.IsStepEditorEdit)
                {
                    e.Handled = true;
                    if (viewModel.SaveStepCommand.CanExecute(null))
                        await viewModel.SaveStepCommand.ExecuteAsync();
                }
                return;
            }
            if (HasAncestor(focusedElement, CompositePropertiesPanel))
            {
                CommitEditorBoundaryInput(focusedElement);
                if (viewModel.SelectedCompositeItem is not null)
                {
                    e.Handled = true;
                    if (viewModel.SaveCompositeItemCommand.CanExecute(null))
                        await viewModel.SaveCompositeItemCommand.ExecuteAsync();
                }
                return;
            }
        }

        var composite = viewModel.IsCompositeScriptSelected;
        var focusedComboBox = FindAncestor<ComboBox>(focusedElement);
        var isTextInput = FindAncestor<TextBoxBase>(focusedElement) is not null ||
                          FindAncestor<PasswordBox>(focusedElement) is not null ||
                          focusedComboBox is { IsEditable: true } ||
                          (composite && focusedComboBox is not null);
        var shortcut = StepGridShortcutPolicy.Resolve(
            composite ? CompositeItemsGrid.IsKeyboardFocusWithin : StepsGrid.IsKeyboardFocusWithin,
            isTextInput,
            composite ? viewModel.CopyCompositeItemsCommand.CanExecute(null) : viewModel.CopyStepsCommand.CanExecute(null),
            composite ? viewModel.PasteCompositeItemsCommand.CanExecute(null) : viewModel.PasteStepsCommand.CanExecute(null),
            composite ? viewModel.UndoCompositeItemsCommand.CanExecute(null) : viewModel.UndoStepListCommand.CanExecute(null),
            composite ? viewModel.DeleteCompositeItemsCommand.CanExecute(null) : viewModel.DeleteStepCommand.CanExecute(null),
            e.Key,
            modifiers);
        if (shortcut == StepGridShortcut.None) return;

        e.Handled = true;
        try
        {
            switch (shortcut)
            {
                case StepGridShortcut.Copy:
                    if (composite) viewModel.CopyCompositeItemsCommand.Execute(null);
                    else viewModel.CopyStepsCommand.Execute(null);
                    break;
                case StepGridShortcut.Paste:
                    if (composite) await viewModel.PasteCompositeItemsCommand.ExecuteAsync();
                    else await viewModel.PasteStepsCommand.ExecuteAsync();
                    break;
                case StepGridShortcut.Delete:
                    if (composite) await viewModel.DeleteCompositeItemsCommand.ExecuteAsync();
                    else await viewModel.DeleteStepCommand.ExecuteAsync();
                    break;
                case StepGridShortcut.Undo:
                    if (composite) await viewModel.UndoCompositeItemsCommand.ExecuteAsync();
                    else await viewModel.UndoStepListCommand.ExecuteAsync();
                    break;
                case StepGridShortcut.ClearSelection:
                    if (composite) viewModel.TryClearCompositeSelection();
                    else viewModel.TryClearStepSelection();
                    break;
            }
        }
        catch (Exception exception)
        {
            viewModel.ReportUnexpectedError(exception);
        }
    }

    private async void StepsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (restoringStepSelection || DataContext is not MainViewModel viewModel) return;
        CommitEditorBoundaryInput(Keyboard.FocusedElement as DependencyObject);
        var requested = StepsGrid.SelectedItems.Cast<StepItemViewModel>().ToList();
        var target = viewModel.SelectedStep is not null && requested.Contains(viewModel.SelectedStep)
            ? viewModel.SelectedStep
            : requested.FirstOrDefault();
        if (!ReferenceEquals(target, viewModel.SelectedStep) &&
            (viewModel.HasRegularEditorDraft || viewModel.IsEditorPersistenceBusy))
        {
            RestoreStepSelection(viewModel.SelectedSteps.ToList());
            try
            {
                if (await viewModel.NavigateToStepAsync(target))
                {
                    RestoreStepSelection(requested);
                    viewModel.SynchronizeSelectedSteps(requested);
                }
            }
            catch (Exception exception) { viewModel.ReportUnexpectedError(exception); }
            return;
        }
        viewModel.SynchronizeSelectedSteps(requested);
    }

    internal bool CommitEditorBoundaryInput(DependencyObject? focusedElement)
    {
        var isValid = BackgroundFocusBehavior.CommitFocusedInputAndRefresh(this, focusedElement);
        if (DataContext is MainViewModel viewModel) viewModel.HasEditorBindingErrors = !isValid;
        return isValid;
    }

    private void ViewModel_EditorStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.ScriptLibrarySearchText) or
            nameof(MainViewModel.ScriptLibraryFilter))
        {
            _ = Dispatcher.BeginInvoke(
                ReconcileScriptSelectionWithLibraryViewAsync,
                System.Windows.Threading.DispatcherPriority.DataBind);
        }

        if (e.PropertyName is not (nameof(MainViewModel.SelectedScript) or
            nameof(MainViewModel.SelectedStep) or
            nameof(MainViewModel.SelectedCompositeItem) or
            nameof(MainViewModel.StepEditorMode) or
            nameof(MainViewModel.EditorDelayInputRefreshToken) or
            nameof(MainViewModel.CompositeDelayInputRefreshToken))) return;

        Dispatcher.BeginInvoke(
            () => BackgroundFocusBehavior.RefreshInputBindingsAndValidation(this),
            System.Windows.Threading.DispatcherPriority.DataBind);
    }

    private async void ScriptsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (restoringScriptSelection || DataContext is not MainViewModel viewModel) return;
        var requested = ScriptsList.SelectedItems.Cast<ScriptItemViewModel>().ToList();
        CommitEditorBoundaryInput(Keyboard.FocusedElement as DependencyObject);
        try { await ApplyScriptListSelectionAsync(requested, Keyboard.Modifiers); }
        catch (Exception exception) { viewModel.ReportUnexpectedError(exception); }
    }

    internal async Task ApplyScriptListSelectionAsync(
        IReadOnlyList<ScriptItemViewModel> requested,
        ModifierKeys modifiers)
    {
        await ApplyScriptSelectionAsync(requested);
    }

    private async void ReconcileScriptSelectionWithLibraryViewAsync()
    {
        if (DataContext is not MainViewModel viewModel) return;
        try
        {
            var visibleSelection = viewModel.SelectedScripts
                .Where(viewModel.ScriptLibraryView.Contains)
                .ToList();
            await ApplyScriptSelectionAsync(visibleSelection);
        }
        catch (Exception exception) { viewModel.ReportUnexpectedError(exception); }
    }

    private async Task ApplyScriptSelectionAsync(IReadOnlyList<ScriptItemViewModel> requested)
    {
        if (DataContext is not MainViewModel viewModel) return;
        var previous = viewModel.SelectedScripts.ToList();
        var target = viewModel.SelectedScript is not null && requested.Contains(viewModel.SelectedScript)
            ? viewModel.SelectedScript
            : requested.FirstOrDefault();
        if (!ReferenceEquals(target, viewModel.SelectedScript))
        {
            RestoreScriptSelection(previous, focus: false);
            if (!await viewModel.NavigateToScriptAsync(target))
            {
                viewModel.EnsureCurrentScriptSelectionVisible();
                RestoreScriptSelection(previous, focus: false);
                return;
            }
        }
        viewModel.SynchronizeSelectedScripts(requested, target);
        RestoreScriptSelection(requested, focus: false);
    }

    private async void ScriptsList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var source = e.OriginalSource as DependencyObject;
        var container = FindAncestor<ListBoxItem>(source);
        draggedScript = container?.DataContext as ScriptItemViewModel;
        scriptSelectionDeferredForDrag = false;
        if (draggedScript is null)
        {
            if (!IsScriptListBlankSource(source)) return;
            e.Handled = true;
            CommitEditorBoundaryInput(Keyboard.FocusedElement as DependencyObject);
            try { await ApplyScriptSelectionAsync([]); }
            catch (Exception exception) when (DataContext is MainViewModel viewModel)
            {
                viewModel.ReportUnexpectedError(exception);
            }
            return;
        }
        dragStart = e.GetPosition(ScriptsList);
        scriptSelectionDeferredForDrag = StepGridShortcutPolicy.ShouldPreserveSelectionForDrag(
            ScriptsList.SelectedItems.Count,
            ScriptsList.SelectedItems.Contains(draggedScript),
            clickedInteractiveControl: false,
            Keyboard.Modifiers);
        if (scriptSelectionDeferredForDrag)
            e.Handled = true;
    }

    private async void ScriptsList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!scriptSelectionDeferredForDrag || draggedScript is null)
        {
            ResetPendingScriptDrag();
            return;
        }
        var clicked = draggedScript;
        ResetPendingScriptDrag();
        e.Handled = true;
        try { await ApplyScriptSelectionAsync([clicked]); }
        catch (Exception exception) when (DataContext is MainViewModel viewModel)
        {
            viewModel.ReportUnexpectedError(exception);
        }
    }

    private void ScriptsList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || draggedScript is null ||
            DataContext is not MainViewModel viewModel || !viewModel.CanDragScript(draggedScript)) return;
        var position = e.GetPosition(ScriptsList);
        if (Math.Abs(position.X - dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(position.Y - dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        scriptSelectionDeferredForDrag = false;
        try { _ = DragDrop.DoDragDrop(ScriptsList, draggedScript, DragDropEffects.Move); }
        finally
        {
            ResetPendingScriptDrag();
            ClearInsertionAdorner();
        }
    }

    private void ScriptsList_LostMouseCapture(object sender, MouseEventArgs e)
    {
        ResetPendingScriptDrag();
    }

    private void ResetPendingScriptDrag()
    {
        draggedScript = null;
        scriptSelectionDeferredForDrag = false;
    }

    private void ScriptsList_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(ScriptItemViewModel)) is not ScriptItemViewModel item ||
            DataContext is not MainViewModel viewModel || !viewModel.CanDragScript(item))
        {
            e.Effects = DragDropEffects.None;
            ClearInsertionAdorner();
            e.Handled = true;
            return;
        }
        var container = FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject);
        if (container is null)
        {
            container = ScriptsList.ItemContainerGenerator.ContainerFromIndex(ScriptsList.Items.Count - 1) as ListBoxItem;
            pendingInsertionIndex = ScriptsList.Items.Count;
            ShowInsertionAdorner(container, insertBefore: false);
        }
        else
        {
            var insertBefore = e.GetPosition(container).Y < container.ActualHeight / 2;
            pendingInsertionIndex = ScriptsList.ItemContainerGenerator.IndexFromContainer(container) + (insertBefore ? 0 : 1);
            ShowInsertionAdorner(container, insertBefore);
        }
        e.Effects = DragDropEffects.Move;
        e.Handled = true;
    }

    private void ScriptsList_DragLeave(object sender, DragEventArgs e) => ClearInsertionAdorner();

    private async void ScriptsList_Drop(object sender, DragEventArgs e)
    {
        try
        {
            if (DataContext is MainViewModel viewModel &&
                e.Data.GetData(typeof(ScriptItemViewModel)) is ScriptItemViewModel item &&
                viewModel.CanDragScript(item))
            {
                await viewModel.MoveScriptsToAsync(item, pendingInsertionIndex);
                e.Effects = DragDropEffects.Move;
            }
        }
        catch (Exception exception) when (DataContext is MainViewModel viewModel)
        {
            viewModel.ReportUnexpectedError(exception);
        }
        finally
        {
            ClearInsertionAdorner();
            e.Handled = true;
        }
    }

    private async void CompositeItemsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (restoringCompositeSelection || DataContext is not MainViewModel viewModel) return;
        CommitEditorBoundaryInput(Keyboard.FocusedElement as DependencyObject);
        var requested = CompositeItemsGrid.SelectedItems.Cast<CompositeItemViewModel>().ToList();
        var target = viewModel.SelectedCompositeItem is not null && requested.Contains(viewModel.SelectedCompositeItem)
            ? viewModel.SelectedCompositeItem
            : requested.FirstOrDefault();
        if (!ReferenceEquals(target, viewModel.SelectedCompositeItem) &&
            (viewModel.HasCompositeEditorDraft || viewModel.IsEditorPersistenceBusy))
        {
            RestoreCompositeSelection(viewModel.SelectedCompositeItems.ToList());
            try
            {
                if (await viewModel.NavigateToCompositeItemAsync(target))
                {
                    RestoreCompositeSelection(requested);
                    viewModel.SynchronizeSelectedCompositeItems(requested);
                }
            }
            catch (Exception exception) { viewModel.ReportUnexpectedError(exception); }
            return;
        }
        viewModel.SynchronizeSelectedCompositeItems(requested);
    }

    private async void CompositeItemsGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var source = e.OriginalSource as DependencyObject;
        var row = FindAncestor<DataGridRow>(source);
        if (row is null)
        {
            draggedCompositeItem = null;
            if (!IsGridEmptySpaceSource(source)) return;
            e.Handled = true;
            if (Keyboard.FocusedElement is DependencyObject focusedElement)
                BackgroundFocusBehavior.CommitInputBinding(focusedElement);
            if (DataContext is MainViewModel blankViewModel)
                blankViewModel.TryClearCompositeSelectionFromBlank();
            return;
        }
        draggedCompositeItem = row?.Item as CompositeItemViewModel;
        if (DataContext is MainViewModel navigationViewModel && draggedCompositeItem is not null &&
            !ReferenceEquals(draggedCompositeItem, navigationViewModel.SelectedCompositeItem) &&
            navigationViewModel.HasCompositeEditorDraft && !IsInteractiveGridSource(source))
        {
            e.Handled = true;
            try { await navigationViewModel.NavigateToCompositeItemAsync(draggedCompositeItem); }
            catch (Exception exception) { navigationViewModel.ReportUnexpectedError(exception); }
            return;
        }
        dragStart = e.GetPosition(CompositeItemsGrid);
        var interactive = FindAncestor<ButtonBase>(source) is not null ||
                          FindAncestor<TextBoxBase>(source) is not null ||
                          FindAncestor<ComboBox>(source) is not null;
        if (draggedCompositeItem is not null && StepGridShortcutPolicy.ShouldPreserveSelectionForDrag(
                CompositeItemsGrid.SelectedItems.Count,
                CompositeItemsGrid.SelectedItems.Contains(draggedCompositeItem),
                interactive,
                Keyboard.Modifiers))
            e.Handled = true;
    }

    private void CompositeItemsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left && TryToggleCompositeItemFromDoubleClick(e.OriginalSource as DependencyObject))
            e.Handled = true;
    }

    private void CompositeEnabledCheckBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (ShouldSuppressCompositeCheckboxClick(e.ClickCount)) e.Handled = true;
    }

    internal static bool ShouldSuppressCompositeCheckboxClick(int clickCount) => clickCount > 1;

    internal bool TryToggleCompositeItemFromDoubleClick(DependencyObject? source)
    {
        if (IsInteractiveGridSource(source)) return false;
        var row = FindAncestor<DataGridRow>(source);
        var item = row?.Item as CompositeItemViewModel ?? row?.DataContext as CompositeItemViewModel;
        if (item is null) return false;
        CompositeItemsGrid.CurrentItem = item;
        item.IsEnabled = !item.IsEnabled;
        return true;
    }

    private void CompositeItemsGrid_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || draggedCompositeItem is null ||
            DataContext is not MainViewModel viewModel || !viewModel.CanDragCompositeItem(draggedCompositeItem)) return;
        var position = e.GetPosition(CompositeItemsGrid);
        if (Math.Abs(position.X - dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(position.Y - dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        try { _ = DragDrop.DoDragDrop(CompositeItemsGrid, draggedCompositeItem, DragDropEffects.Move); }
        finally
        {
            draggedCompositeItem = null;
            ClearInsertionAdorner();
        }
    }

    private void CompositeItemsGrid_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(CompositeItemViewModel)) is not CompositeItemViewModel item ||
            DataContext is not MainViewModel viewModel || !viewModel.CanDragCompositeItem(item))
        {
            e.Effects = DragDropEffects.None;
            ClearInsertionAdorner();
            e.Handled = true;
            return;
        }
        var row = FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject);
        if (row is null)
        {
            row = CompositeItemsGrid.ItemContainerGenerator.ContainerFromIndex(CompositeItemsGrid.Items.Count - 1) as DataGridRow;
            pendingInsertionIndex = CompositeItemsGrid.Items.Count;
            ShowInsertionAdorner(row, insertBefore: false);
        }
        else
        {
            var insertBefore = e.GetPosition(row).Y < row.ActualHeight / 2;
            pendingInsertionIndex = row.GetIndex() + (insertBefore ? 0 : 1);
            ShowInsertionAdorner(row, insertBefore);
        }
        e.Effects = DragDropEffects.Move;
        e.Handled = true;
    }

    private void CompositeItemsGrid_DragLeave(object sender, DragEventArgs e) => ClearInsertionAdorner();

    private async void CompositeItemsGrid_Drop(object sender, DragEventArgs e)
    {
        try
        {
            if (DataContext is MainViewModel viewModel &&
                e.Data.GetData(typeof(CompositeItemViewModel)) is CompositeItemViewModel item)
                await viewModel.MoveCompositeItemToAsync(item, pendingInsertionIndex);
        }
        catch (Exception exception) when (DataContext is MainViewModel viewModel)
        {
            viewModel.ReportUnexpectedError(exception);
        }
        finally
        {
            ClearInsertionAdorner();
            e.Handled = true;
        }
    }

    private async void StepsGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var source = e.OriginalSource as DependencyObject;
        var row = FindAncestor<DataGridRow>(source);
        if (row is null)
        {
            draggedStep = null;
            if (!IsStepsGridEmptySpaceSource(source)) return;
            e.Handled = true;
            TryClearStepSelectionFromEmptyClick(source);
            return;
        }

        dragStart = e.GetPosition(StepsGrid);
        draggedStep = row?.Item as StepItemViewModel;
        if (DataContext is MainViewModel navigationViewModel && draggedStep is not null &&
            !ReferenceEquals(draggedStep, navigationViewModel.SelectedStep) &&
            navigationViewModel.HasRegularEditorDraft && !IsInteractiveGridSource(source))
        {
            e.Handled = true;
            try { await navigationViewModel.NavigateToStepAsync(draggedStep); }
            catch (Exception exception) { navigationViewModel.ReportUnexpectedError(exception); }
            return;
        }
        var clickedInteractiveControl = FindAncestor<ButtonBase>(source) is not null ||
                                        FindAncestor<TextBoxBase>(source) is not null ||
                                        FindAncestor<ComboBox>(source) is not null;
        if (draggedStep is not null && StepGridShortcutPolicy.ShouldPreserveSelectionForDrag(
                StepsGrid.SelectedItems.Count,
                StepsGrid.SelectedItems.Contains(draggedStep),
                clickedInteractiveControl,
                Keyboard.Modifiers))
            e.Handled = true;
    }

    internal bool TryClearStepSelectionFromEmptyClick(DependencyObject? source)
    {
        if (!HasAncestor(source, StepsGrid) || !IsStepsGridEmptySpaceSource(source) ||
            DataContext is not MainViewModel viewModel ||
            !viewModel.IsRegularScriptSelected)
            return false;

        if (Keyboard.FocusedElement is DependencyObject focusedElement)
            BackgroundFocusBehavior.CommitInputBinding(focusedElement);
        return viewModel.TryClearStepSelectionFromBlank();
    }

    internal Task<bool> TryClearStepSelectionFromEmptyClickAsync(DependencyObject? source) =>
        Task.FromResult(TryClearStepSelectionFromEmptyClick(source));

    internal static bool IsStepsGridEmptySpaceSource(DependencyObject? source) => IsGridEmptySpaceSource(source);

    internal static bool IsScriptListBlankSource(DependencyObject? source) =>
        source is not null &&
        FindAncestor<ListBoxItem>(source) is null &&
        FindAncestor<ScrollBar>(source) is null &&
        FindAncestor<ButtonBase>(source) is null &&
        FindAncestor<TextBoxBase>(source) is null &&
        FindAncestor<ComboBox>(source) is null;

    internal static bool IsGridEmptySpaceSource(DependencyObject? source) =>
        source is not null &&
        FindAncestor<DataGridRow>(source) is null &&
        FindAncestor<DataGridColumnHeader>(source) is null &&
        FindAncestor<DataGridColumnHeadersPresenter>(source) is null &&
        FindAncestor<ScrollBar>(source) is null &&
        FindAncestor<ButtonBase>(source) is null &&
        FindAncestor<TextBoxBase>(source) is null &&
        FindAncestor<ComboBox>(source) is null;

    private void StepsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left && TryToggleStepFromDoubleClick(e.OriginalSource as DependencyObject))
            e.Handled = true;
    }

    internal bool TryToggleStepFromDoubleClick(DependencyObject? source)
    {
        if (IsInteractiveGridSource(source)) return false;

        var row = FindAncestor<DataGridRow>(source);
        var step = row?.Item as StepItemViewModel ?? row?.DataContext as StepItemViewModel;
        if (step is null) return false;

        StepsGrid.CurrentItem = step;
        step.IsEnabled = !step.IsEnabled;
        return true;
    }

    private static bool IsInteractiveGridSource(DependencyObject? source) =>
        FindAncestor<ScrollBar>(source) is not null ||
        FindAncestor<ButtonBase>(source) is not null ||
        FindAncestor<TextBoxBase>(source) is not null ||
        FindAncestor<ComboBox>(source) is not null;

    private void StepsGrid_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || draggedStep is null ||
            DataContext is not MainViewModel viewModel || !CanDragStep(viewModel, draggedStep)) return;
        var position = e.GetPosition(StepsGrid);
        if (Math.Abs(position.X - dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(position.Y - dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        try
        {
            _ = DragDrop.DoDragDrop(StepsGrid, draggedStep, DragDropEffects.Move);
        }
        finally
        {
            draggedStep = null;
            ClearInsertionAdorner();
        }
    }

    private void StepsGrid_DragOver(object sender, DragEventArgs e)
    {
        var item = e.Data.GetData(typeof(StepItemViewModel)) as StepItemViewModel;
        if (DataContext is not MainViewModel viewModel || item is null || !CanDragStep(viewModel, item))
        {
            e.Effects = DragDropEffects.None;
            ClearInsertionAdorner();
            e.Handled = true;
            return;
        }

        var row = FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject);
        if (row is null)
        {
            row = StepsGrid.ItemContainerGenerator.ContainerFromIndex(StepsGrid.Items.Count - 1) as DataGridRow;
            pendingInsertionIndex = StepsGrid.Items.Count;
            ShowInsertionAdorner(row, insertBefore: false);
        }
        else
        {
            var insertBefore = e.GetPosition(row).Y < row.ActualHeight / 2;
            pendingInsertionIndex = row.GetIndex() + (insertBefore ? 0 : 1);
            ShowInsertionAdorner(row, insertBefore);
        }

        e.Effects = DragDropEffects.Move;
        e.Handled = true;
    }

    private void StepsGrid_DragLeave(object sender, DragEventArgs e) => ClearInsertionAdorner();

    private async void StepsGrid_Drop(object sender, DragEventArgs e)
    {
        try
        {
            if (DataContext is MainViewModel viewModel &&
                e.Data.GetData(typeof(StepItemViewModel)) is StepItemViewModel item &&
                CanDragStep(viewModel, item))
            {
                await viewModel.MoveStepToAsync(item, pendingInsertionIndex);
                e.Effects = DragDropEffects.Move;
            }
        }
        catch (Exception exception) when (DataContext is MainViewModel viewModel)
        {
            viewModel.ReportUnexpectedError(exception);
        }
        finally
        {
            ClearInsertionAdorner();
            e.Handled = true;
        }
    }

    private void ShowInsertionAdorner(FrameworkElement? row, bool insertBefore)
    {
        ClearInsertionAdorner();
        if (row is null) return;
        var layer = AdornerLayer.GetAdornerLayer(row);
        if (layer is null) return;
        insertionAdorner = new InsertionAdorner(row, insertBefore);
        layer.Add(insertionAdorner);
    }

    private void ClearInsertionAdorner()
    {
        if (insertionAdorner is null) return;
        AdornerLayer.GetAdornerLayer(insertionAdorner.AdornedElement)?.Remove(insertionAdorner);
        insertionAdorner = null;
    }

    private bool CanDragStep(MainViewModel viewModel, StepItemViewModel item) =>
        StepsGrid.SelectedItems.Contains(item) &&
        viewModel.CanDragStep(item);

    private void RestoreStepSelection(IReadOnlyList<StepItemViewModel> items)
    {
        restoringStepSelection = true;
        try
        {
            StepsGrid.SelectedItems.Clear();
            foreach (var item in items.Where(StepsGrid.Items.Contains))
                StepsGrid.SelectedItems.Add(item);
        }
        finally { restoringStepSelection = false; }
    }

    private void FocusStep(StepItemViewModel item) => FocusSelectedItem(StepsGrid, item);

    private void RestoreCompositeSelection(IReadOnlyList<CompositeItemViewModel> items)
    {
        restoringCompositeSelection = true;
        try
        {
            CompositeItemsGrid.SelectedItems.Clear();
            foreach (var item in items.Where(CompositeItemsGrid.Items.Contains))
                CompositeItemsGrid.SelectedItems.Add(item);
        }
        finally { restoringCompositeSelection = false; }
    }

    private void RestoreScriptSelection(IReadOnlyList<ScriptItemViewModel> items, bool focus)
    {
        restoringScriptSelection = true;
        try
        {
            ScriptsList.SelectedItems.Clear();
            foreach (var item in items.Where(ScriptsList.Items.Contains))
                ScriptsList.SelectedItems.Add(item);
        }
        finally { restoringScriptSelection = false; }
        if (focus)
            FocusSelectedItem(ScriptsList, DataContext is MainViewModel viewModel ? viewModel.SelectedScript : items.LastOrDefault());
    }

    private void FocusSelectedItem(ItemsControl control, object? item)
    {
        if (item is null || !control.Items.Contains(item)) return;
        if (control is ListBox listBox) listBox.ScrollIntoView(item);
        else if (control is DataGrid dataGrid) dataGrid.ScrollIntoView(item);
        _ = Dispatcher.BeginInvoke(() =>
        {
            if (control.ItemContainerGenerator.ContainerFromItem(item) is UIElement container)
                container.Focus();
            else control.Focus();
        }, System.Windows.Threading.DispatcherPriority.Input);
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match) return match;
            current = current is Visual or Visual3D ? VisualTreeHelper.GetParent(current) : LogicalTreeHelper.GetParent(current);
        }
        return null;
    }

    private static bool HasAncestor(DependencyObject? current, DependencyObject expected)
    {
        while (current is not null)
        {
            if (ReferenceEquals(current, expected)) return true;
            var visualParent = current is Visual or Visual3D ? VisualTreeHelper.GetParent(current) : null;
            current = visualParent ?? LogicalTreeHelper.GetParent(current);
        }
        return false;
    }

    private sealed class InsertionAdorner(FrameworkElement adornedElement, bool insertBefore) : Adorner(adornedElement)
    {
        protected override void OnRender(DrawingContext drawingContext)
        {
            var y = insertBefore ? 0 : AdornedElement.RenderSize.Height;
            var brush = (Brush?)FindResource("AccentBrush") ?? Brushes.DodgerBlue;
            var pen = new Pen(brush, 3);
            drawingContext.DrawLine(pen, new Point(0, y), new Point(AdornedElement.RenderSize.Width, y));
            drawingContext.DrawGeometry(brush, null, CreateTriangle(0, y, pointsRight: true));
            drawingContext.DrawGeometry(brush, null, CreateTriangle(AdornedElement.RenderSize.Width, y, pointsRight: false));
        }

        private static StreamGeometry CreateTriangle(double x, double y, bool pointsRight)
        {
            var direction = pointsRight ? 1 : -1;
            var geometry = new StreamGeometry();
            using var context = geometry.Open();
            context.BeginFigure(new Point(x, y), true, true);
            context.LineTo(new Point(x + direction * 9, y - 5), true, false);
            context.LineTo(new Point(x + direction * 9, y + 5), true, false);
            return geometry;
        }
    }
}
