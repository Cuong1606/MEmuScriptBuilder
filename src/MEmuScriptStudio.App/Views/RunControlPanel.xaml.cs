using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using MEmuScriptStudio.App.ViewModels;

namespace MEmuScriptStudio.App.Views;

public partial class RunControlPanel : UserControl
{
    public RunControlPanel() => InitializeComponent();

    private void RunTargetsGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var source = e.OriginalSource as DependencyObject;
        var cell = FindAncestor<DataGridCell>(source);
        var row = FindAncestor<DataGridRow>(source);
        if (row?.Item is not InstanceTargetItemViewModel target) return;
        if (FindAncestor<CheckBox>(source) is not null)
        {
            ToggleRunTargetSelection(target);
            e.Handled = true;
            return;
        }
        if (FindAncestor<Button>(source) is not null || FindAncestor<ComboBox>(source) is not null) return;

        if (cell?.Column == AssignmentColumn)
        {
            cell.Focus();
            RunTargetsGrid.CurrentCell = new DataGridCellInfo(target, AssignmentColumn);
            RunTargetsGrid.BeginEdit();
            e.Handled = true;
            return;
        }

        if (!target.CanSelectForRun) return;
        ToggleRunTargetSelection(target);
        e.Handled = true;
    }

    private void ActiveInstancesGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var source = e.OriginalSource as DependencyObject;
        var row = FindAncestor<DataGridRow>(source);
        if (row?.Item is not InstanceRunItemViewModel item || !item.CanStop) return;
        if (FindAncestor<Button>(source) is not null && FindAncestor<CheckBox>(source) is null) return;
        item.IsSelected = !item.IsSelected;
        e.Handled = true;
    }

    internal static void ToggleRunTargetSelection(InstanceTargetItemViewModel target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (target.CanSelectForRun) target.IsSelected = !target.IsSelected;
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match) return match;
            current = current is Visual or Visual3D
                ? VisualTreeHelper.GetParent(current)
                : LogicalTreeHelper.GetParent(current);
        }
        return null;
    }
}

public sealed class BindingProxy : Freezable
{
    public static readonly DependencyProperty DataProperty = DependencyProperty.Register(
        nameof(Data),
        typeof(object),
        typeof(BindingProxy));

    public object? Data
    {
        get => GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    protected override Freezable CreateInstanceCore() => new BindingProxy();
}
