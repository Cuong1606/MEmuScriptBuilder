using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using MEmuScriptStudio.App.ViewModels;

namespace MEmuScriptStudio.App;

public partial class MainWindow : Window
{
    private Point dragStart;
    private StepItemViewModel? draggedStep;
    private InsertionAdorner? insertionAdorner;
    private int pendingInsertionIndex;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void StepsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
            viewModel.SynchronizeSelectedSteps(StepsGrid.SelectedItems.Cast<StepItemViewModel>());
    }

    private void StepsGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        dragStart = e.GetPosition(StepsGrid);
        draggedStep = FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject)?.Item as StepItemViewModel;
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
            e.Key,
            Keyboard.Modifiers);
        if (shortcut == StepGridShortcut.None) return;

        e.Handled = true;
        try
        {
            switch (shortcut)
            {
                case StepGridShortcut.Copy:
                    viewModel.CopySelectedStep();
                    break;
                case StepGridShortcut.Paste:
                    await viewModel.PasteCopiedStepAsync();
                    break;
                case StepGridShortcut.Delete:
                    await viewModel.DeleteSelectedStepFromShortcutAsync();
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
        StepsGrid.SelectedItems.Count == 1 &&
        ReferenceEquals(StepsGrid.SelectedItems[0], item) &&
        viewModel.CanDragStep(item);

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match) return match;
            current = current is Visual or Visual3D ? VisualTreeHelper.GetParent(current) : LogicalTreeHelper.GetParent(current);
        }
        return null;
    }

    private sealed class InsertionAdorner(DataGridRow adornedElement, bool insertBefore) : Adorner(adornedElement)
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
