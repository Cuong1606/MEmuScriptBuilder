using System.Windows;
using System.Windows.Controls;
using MEmuScriptStudio.Core.Models;

namespace MEmuScriptStudio.App.Views;

public partial class RecentRunsPanel : UserControl
{
    public RecentRunsPanel()
    {
        InitializeComponent();
    }

    internal void ApplyLayout(ControlCenterLayoutSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var panelHeight = RecentListRowDefinition.ActualHeight + RecentDetailRowDefinition.ActualHeight;
        SetStarRatio(ControlCenterLayoutSettings.ResolveRecentListRatio(settings, panelHeight));
    }

    internal ControlCenterLayoutSettings CaptureLayout(ControlCenterLayoutSettings current)
    {
        ArgumentNullException.ThrowIfNull(current);
        return new ControlCenterLayoutSettings
        {
            WindowWidth = current.WindowWidth,
            WindowHeight = current.WindowHeight,
            IsMaximized = current.IsMaximized,
            SetupPanelRatio = current.SetupPanelRatio,
            RecentListRatio = ControlCenterLayoutSettings.CaptureSplitRatio(
                RecentListRowDefinition.ActualHeight,
                RecentDetailRowDefinition.ActualHeight,
                current.RecentListRatio ?? ControlCenterLayoutSettings.DefaultRecentListRatio),
            SetupPanelWidth = null
        };
    }

    private void SetStarRatio(double ratio)
    {
        var normalized = ControlCenterLayoutSettings.NormalizeRecentListRatio(ratio);
        RecentListRowDefinition.Height = new GridLength(normalized, GridUnitType.Star);
        RecentDetailRowDefinition.Height = new GridLength(1d - normalized, GridUnitType.Star);
    }
}
