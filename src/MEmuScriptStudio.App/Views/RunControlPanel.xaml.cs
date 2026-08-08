using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using MEmuScriptStudio.App.ViewModels;
using MEmuScriptStudio.Core.Models;

namespace MEmuScriptStudio.App.Views;

public partial class RunControlPanel : UserControl
{
    private bool isCompactHeight;

    public RunControlPanel()
    {
        InitializeComponent();
        SizeChanged += (_, _) => UpdateCompactHeight();
    }

    internal void ApplyLayout(ControlCenterLayoutSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var panelWidth = RunSetupColumnDefinition.ActualWidth + RunRuntimeColumnDefinition.ActualWidth;
        var ratio = ControlCenterLayoutSettings.ResolveSetupPanelRatio(settings, panelWidth);
        SetStarRatio(ratio);
    }

    private void UpdateCompactHeight()
    {
        var shouldUseCompactHeight = ActualHeight > 0 && ActualHeight < 500;
        if (shouldUseCompactHeight != isCompactHeight)
        {
            isCompactHeight = shouldUseCompactHeight;
            RunSetupExpander.IsExpanded = !isCompactHeight;
            LaunchSpacingExpander.IsExpanded = !isCompactHeight;
            var cardPadding = isCompactHeight ? new Thickness(12, 4, 12, 4) : new Thickness(12);
            RunSetupCard.Padding = cardPadding;
            RunTargetsCard.Padding = cardPadding;
            LaunchSpacingCard.Padding = cardPadding;
        }
    }

    private void SetStarRatio(double ratio)
    {
        var normalized = ControlCenterLayoutSettings.NormalizeSetupPanelRatio(ratio);
        RunSetupColumnDefinition.Width = new GridLength(normalized, GridUnitType.Star);
        RunRuntimeColumnDefinition.Width = new GridLength(1d - normalized, GridUnitType.Star);
    }

    internal ControlCenterLayoutSettings CaptureLayout(ControlCenterLayoutSettings current)
    {
        ArgumentNullException.ThrowIfNull(current);
        return new ControlCenterLayoutSettings
        {
            WindowWidth = current.WindowWidth,
            WindowHeight = current.WindowHeight,
            IsMaximized = current.IsMaximized,
            SetupPanelRatio = ControlCenterLayoutSettings.CaptureSplitRatio(
                RunSetupColumnDefinition.ActualWidth,
                RunRuntimeColumnDefinition.ActualWidth,
                current.SetupPanelRatio ?? ControlCenterLayoutSettings.DefaultSetupPanelRatio),
            RecentListRatio = current.RecentListRatio,
            SetupPanelWidth = null
        };
    }

    private void RunSetupRuntimeSplitter_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        SetStarRatio(ControlCenterLayoutSettings.DefaultSetupPanelRatio);
        e.Handled = true;
    }

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
