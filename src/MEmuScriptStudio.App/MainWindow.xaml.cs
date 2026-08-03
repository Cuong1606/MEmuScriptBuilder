using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
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
    private InstanceTargetItemViewModel? draggedLayoutTarget;
    private int pendingLayoutInsertionIndex;
    private InsertionAdorner? layoutInsertionAdorner;
    private readonly ControlCenterWindowManager controlCenterWindowManager;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        controlCenterWindowManager = new ControlCenterWindowManager(context => new ControlCenterWindow(context) { Owner = this });
        viewModel.StepSelectionRestoreRequested += RestoreStepSelection;
    }

    private void OpenControlCenter_Click(object sender, RoutedEventArgs e)
    {
        controlCenterWindowManager.Open(DataContext);
    }

    private async void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.S || Keyboard.Modifiers != ModifierKeys.Control ||
            DataContext is not MainViewModel viewModel || !viewModel.SaveStepCommand.CanExecute(null)) return;

        if (Keyboard.FocusedElement is TextBox textBox)
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
            viewModel.CanChangeSelection && viewModel.SelectedStepCount > 0,
            viewModel.CanChangeSelection && viewModel.SelectedScript is not null && viewModel.HasCopiedSteps,
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
                    viewModel.CopySelectedSteps();
                    break;
                case StepGridShortcut.Paste:
                    await viewModel.PasteCopiedStepsAsync();
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

    private void LayoutTargetsList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!HasAncestorTag(e.OriginalSource as DependencyObject, "LayoutDragHandle"))
        {
            draggedLayoutTarget = null;
            return;
        }
        dragStart = e.GetPosition(LayoutTargetsList);
        draggedLayoutTarget = FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject)?.Content as InstanceTargetItemViewModel;
    }

    private void LayoutTargetsList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || draggedLayoutTarget is null ||
            DataContext is not MainViewModel viewModel || !viewModel.CanMoveLayoutTarget(draggedLayoutTarget)) return;
        var position = e.GetPosition(LayoutTargetsList);
        if (Math.Abs(position.X - dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(position.Y - dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        try { _ = DragDrop.DoDragDrop(LayoutTargetsList, draggedLayoutTarget, DragDropEffects.Move); }
        finally { draggedLayoutTarget = null; }
    }

    private void LayoutTargetsList_DragOver(object sender, DragEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel ||
            e.Data.GetData(typeof(InstanceTargetItemViewModel)) is not InstanceTargetItemViewModel item ||
            !viewModel.CanMoveLayoutTarget(item))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }
        var row = FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject);
        pendingLayoutInsertionIndex = row is null
            ? LayoutTargetsList.Items.Count
            : LayoutTargetsList.ItemContainerGenerator.IndexFromContainer(row) +
              (e.GetPosition(row).Y < row.ActualHeight / 2 ? 0 : 1);
        ClearLayoutInsertionAdorner();
        if (row is not null)
        {
            layoutInsertionAdorner = new InsertionAdorner(row, pendingLayoutInsertionIndex <= LayoutTargetsList.ItemContainerGenerator.IndexFromContainer(row));
            AdornerLayer.GetAdornerLayer(row)?.Add(layoutInsertionAdorner);
        }
        e.Effects = DragDropEffects.Move;
        e.Handled = true;
    }

    private async void LayoutTargetsList_Drop(object sender, DragEventArgs e)
    {
        try
        {
            if (DataContext is MainViewModel viewModel &&
                e.Data.GetData(typeof(InstanceTargetItemViewModel)) is InstanceTargetItemViewModel item)
                await viewModel.MoveLayoutTargetToAsync(item, pendingLayoutInsertionIndex);
        }
        catch (Exception exception) when (DataContext is MainViewModel viewModel)
        {
            viewModel.ReportUnexpectedError(exception);
        }
        finally { ClearLayoutInsertionAdorner(); e.Handled = true; }
    }

    private void LayoutTargetsList_DragLeave(object sender, DragEventArgs e) => ClearLayoutInsertionAdorner();

    private void ClearLayoutInsertionAdorner()
    {
        if (layoutInsertionAdorner is null) return;
        AdornerLayer.GetAdornerLayer(layoutInsertionAdorner.AdornedElement)?.Remove(layoutInsertionAdorner);
        layoutInsertionAdorner = null;
    }

    private static bool HasAncestorTag(DependencyObject? current, string tag)
    {
        while (current is not null)
        {
            if (current is FrameworkElement element && Equals(element.Tag, tag)) return true;
            current = current is Visual or Visual3D ? VisualTreeHelper.GetParent(current) : LogicalTreeHelper.GetParent(current);
        }
        return false;
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
