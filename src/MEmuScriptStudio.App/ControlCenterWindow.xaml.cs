using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using MEmuScriptStudio.App.ViewModels;

namespace MEmuScriptStudio.App;

public partial class ControlCenterWindow : Window, Services.IControlCenterWindowHost
{
    private Point dragStart;
    private InstanceTargetItemViewModel? draggedTarget;
    private int insertionIndex;
    private LayoutInsertionAdorner? insertionAdorner;

    public ControlCenterWindow(object? dataContext)
    {
        InitializeComponent();
        DataContext = dataContext;
    }

    bool Services.IControlCenterWindowHost.IsMinimized
    {
        get => WindowState == WindowState.Minimized;
        set { if (!value && WindowState == WindowState.Minimized) WindowState = WindowState.Normal; }
    }

    void Services.IControlCenterWindowHost.Activate() => Activate();

    private void LayoutTargetsList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!HasAncestorTag(e.OriginalSource as DependencyObject, "LayoutDragHandle")) { draggedTarget = null; return; }
        dragStart = e.GetPosition(LayoutTargetsList);
        draggedTarget = FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject)?.Content as InstanceTargetItemViewModel;
    }

    private void LayoutTargetsList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || draggedTarget is null || DataContext is not MainViewModel vm || !vm.CanMoveLayoutTarget(draggedTarget)) return;
        var position = e.GetPosition(LayoutTargetsList);
        if (Math.Abs(position.X - dragStart.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(position.Y - dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        try { _ = DragDrop.DoDragDrop(LayoutTargetsList, draggedTarget, DragDropEffects.Move); }
        finally { draggedTarget = null; }
    }

    private void LayoutTargetsList_DragOver(object sender, DragEventArgs e)
    {
        if (DataContext is not MainViewModel vm || e.Data.GetData(typeof(InstanceTargetItemViewModel)) is not InstanceTargetItemViewModel item || !vm.CanMoveLayoutTarget(item)) { e.Effects = DragDropEffects.None; e.Handled = true; return; }
        var row = FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject);
        insertionIndex = row is null ? LayoutTargetsList.Items.Count : LayoutTargetsList.ItemContainerGenerator.IndexFromContainer(row) + (e.GetPosition(row).Y < row.ActualHeight / 2 ? 0 : 1);
        ClearAdorner();
        if (row is not null) { insertionAdorner = new LayoutInsertionAdorner(row, insertionIndex <= LayoutTargetsList.ItemContainerGenerator.IndexFromContainer(row)); AdornerLayer.GetAdornerLayer(row)?.Add(insertionAdorner); }
        e.Effects = DragDropEffects.Move; e.Handled = true;
    }

    private async void LayoutTargetsList_Drop(object sender, DragEventArgs e)
    {
        try { if (DataContext is MainViewModel vm && e.Data.GetData(typeof(InstanceTargetItemViewModel)) is InstanceTargetItemViewModel item) await vm.MoveLayoutTargetToAsync(item, insertionIndex); }
        catch (Exception exception) when (DataContext is MainViewModel vm) { vm.ReportUnexpectedError(exception); }
        finally { ClearAdorner(); e.Handled = true; }
    }

    private void LayoutTargetsList_DragLeave(object sender, DragEventArgs e) => ClearAdorner();
    private void ClearAdorner() { if (insertionAdorner is null) return; AdornerLayer.GetAdornerLayer(insertionAdorner.AdornedElement)?.Remove(insertionAdorner); insertionAdorner = null; }
    private static bool HasAncestorTag(DependencyObject? value, string tag) { while (value is not null) { if (value is FrameworkElement element && Equals(element.Tag, tag)) return true; value = value is Visual or Visual3D ? VisualTreeHelper.GetParent(value) : LogicalTreeHelper.GetParent(value); } return false; }
    private static T? FindAncestor<T>(DependencyObject? value) where T : DependencyObject { while (value is not null) { if (value is T match) return match; value = value is Visual or Visual3D ? VisualTreeHelper.GetParent(value) : LogicalTreeHelper.GetParent(value); } return null; }

    private sealed class LayoutInsertionAdorner(FrameworkElement adornedElement, bool before) : Adorner(adornedElement)
    {
        protected override void OnRender(DrawingContext dc) { var y = before ? 0 : AdornedElement.RenderSize.Height; var pen = new Pen(Brushes.DodgerBlue, 3); dc.DrawLine(pen, new Point(0, y), new Point(AdornedElement.RenderSize.Width, y)); }
    }
}
