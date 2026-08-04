using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Interop;
using MEmuScriptStudio.App.Services;
using MEmuScriptStudio.App.ViewModels;

namespace MEmuScriptStudio.App;

public partial class MainWindow : Window, IStartupWindow
{
    private Point dragStart;
    private StepItemViewModel? draggedStep;
    private InsertionAdorner? insertionAdorner;
    private int pendingInsertionIndex;
    private bool restoringStepSelection;
    private bool loadedLogged;
    private bool contentRenderedLogged;
    private readonly ControlCenterWindowManager controlCenterWindowManager;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        controlCenterWindowManager = new ControlCenterWindowManager(context => new ControlCenterWindow(context) { Owner = this });
        viewModel.StepSelectionRestoreRequested += RestoreStepSelection;
        Loaded += OnMainWindowLoaded;
    }

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        if (contentRenderedLogged) return;
        contentRenderedLogged = true;
        ApplicationLifecycleLogger.Write($"MainWindow ContentRendered HWND={new WindowInteropHelper(this).Handle}");
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        ApplicationLifecycleLogger.Write($"MainWindow Closing Cancel={e.Cancel}");
    }

    protected override void OnClosed(EventArgs e)
    {
        ApplicationLifecycleLogger.Write("MainWindow Closed");
        Loaded -= OnMainWindowLoaded;
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
        controlCenterWindowManager.TryOpen(DataContext, ReportControlCenterOpenError);
    }

    private void ReportControlCenterOpenError(Exception exception)
    {
        var logPath = ApplicationErrorReporter.Report(exception, "OpenControlCenter");
        if (DataContext is MainViewModel viewModel) viewModel.ReportUnexpectedError(exception);
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
        if (e.Key != Key.S || modifiers != ModifierKeys.Control ||
            DataContext is not MainViewModel viewModel || !viewModel.SaveStepCommand.CanExecute(null)) return;

        if (focusedElement is TextBox textBox)
            textBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();

        e.Handled = true;
        await viewModel.SaveStepCommand.ExecuteAsync();
    }

    private void StepsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!restoringStepSelection && DataContext is MainViewModel viewModel)
            viewModel.SynchronizeSelectedSteps(StepsGrid.SelectedItems.Cast<StepItemViewModel>());
    }

    private void StepsGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var source = e.OriginalSource as DependencyObject;
        var row = FindAncestor<DataGridRow>(source);
        if (row is null &&
            FindAncestor<DataGridColumnHeader>(source) is null &&
            FindAncestor<ScrollBar>(source) is null)
        {
            draggedStep = null;
            if (DataContext is MainViewModel viewModel) viewModel.TryClearStepSelection();
            e.Handled = true;
            return;
        }

        dragStart = e.GetPosition(StepsGrid);
        draggedStep = row?.Item as StepItemViewModel;
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

    private async void StepsGrid_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel) return;
        var source = Keyboard.FocusedElement as DependencyObject;
        var isTextInput = FindAncestor<TextBoxBase>(source) is not null || FindAncestor<ComboBox>(source) is not null;
        var shortcut = StepGridShortcutPolicy.Resolve(
            StepsGrid.IsKeyboardFocusWithin,
            isTextInput,
            viewModel.CopyStepsCommand.CanExecute(null),
            viewModel.PasteStepsCommand.CanExecute(null),
            viewModel.UndoStepListCommand.CanExecute(null),
            e.Key,
            Keyboard.Modifiers);
        if (shortcut == StepGridShortcut.None) return;

        e.Handled = true;
        try
        {
            switch (shortcut)
            {
                case StepGridShortcut.Copy:
                    viewModel.CopyStepsCommand.Execute(null);
                    break;
                case StepGridShortcut.Paste:
                    await viewModel.PasteStepsCommand.ExecuteAsync();
                    break;
                case StepGridShortcut.Delete:
                    await viewModel.DeleteSelectedStepFromShortcutAsync();
                    break;
                case StepGridShortcut.Undo:
                    await viewModel.UndoStepListCommand.ExecuteAsync();
                    break;
                case StepGridShortcut.ClearSelection:
                    viewModel.TryClearStepSelection();
                    break;
            }
        }
        catch (Exception exception)
        {
            viewModel.ReportUnexpectedError(exception);
        }
    }

    private void ShowInsertionAdorner(DataGridRow? row, bool insertBefore)
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

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match) return match;
            current = current is Visual or Visual3D ? VisualTreeHelper.GetParent(current) : LogicalTreeHelper.GetParent(current);
        }
        return null;
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
