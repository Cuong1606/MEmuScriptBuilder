using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using MEmuScriptStudio.App.Services;
using MEmuScriptStudio.App.ViewModels;
using MEmuScriptStudio.Core.Models;

namespace MEmuScriptStudio.App;

public partial class ControlCenterWindow : Window, Services.IControlCenterWindowHost
{
    private bool isClosed;
    private WindowState lastNonMinimizedState = WindowState.Normal;
    private bool closePersistenceInProgress;
    private bool closePersistenceComplete;
    private bool savedLayoutApplyScheduled;

    internal TimeSpan LayoutPersistenceTimeout { get; set; } = TimeSpan.FromSeconds(2);
    internal bool HasAppliedSavedLayout { get; private set; }

    public ControlCenterWindow(object? dataContext)
    {
        InitializeComponent();
        DataContext = dataContext;
        Loaded += OnControlCenterLoaded;
    }

    bool Services.IControlCenterWindowHost.IsAlive =>
        !isClosed && (IsLoaded || IsVisible || new WindowInteropHelper(this).Handle != IntPtr.Zero);

    bool Services.IControlCenterWindowHost.IsMinimized
    {
        get => WindowState == WindowState.Minimized;
        set
        {
            if (!value && WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        }
    }

    void Services.IControlCenterWindowHost.Activate() => Activate();
    void Services.IControlCenterWindowHost.Close() => Close();

    protected override async void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!HasAppliedSavedLayout)
        {
            closePersistenceComplete = true;
            base.OnClosing(e);
            return;
        }
        if (!closePersistenceComplete && DataContext is MainViewModel viewModel)
        {
            e.Cancel = true;
            base.OnClosing(e);
            if (closePersistenceInProgress) return;
            closePersistenceInProgress = true;
            try
            {
                var bounds = WindowState == WindowState.Normal
                    ? new Rect(Left, Top, ActualWidth, ActualHeight)
                    : RestoreBounds;
                var panelLayout = RecentRunsPanel.CaptureLayout(
                    RunPanel.CaptureLayout(viewModel.ControlCenterLayout));
                var captured = new ControlCenterLayoutSettings
                {
                    WindowWidth = bounds.Width,
                    WindowHeight = bounds.Height,
                    IsMaximized = WindowState == WindowState.Maximized ||
                                  (WindowState == WindowState.Minimized && lastNonMinimizedState == WindowState.Maximized),
                    SetupPanelRatio = panelLayout.SetupPanelRatio,
                    RecentListRatio = panelLayout.RecentListRatio,
                    SetupPanelWidth = null
                };
                var normalized = ControlCenterLayoutSettings.Normalize(
                    captured,
                    SystemParameters.WorkArea.Width,
                    SystemParameters.WorkArea.Height);
                using var timeout = new CancellationTokenSource(LayoutPersistenceTimeout);
                var saved = await viewModel.PersistControlCenterLayoutAsync(normalized, timeout.Token)
                    .WaitAsync(LayoutPersistenceTimeout + TimeSpan.FromMilliseconds(250));
                if (!saved) ApplicationLifecycleLogger.Write("ControlCenter layout save did not complete successfully; closing continues");
            }
            catch (TimeoutException exception)
            {
                ApplicationLifecycleLogger.WriteException("ControlCenter layout save timed out; closing continues", exception);
            }
            catch (Exception exception)
            {
                ApplicationLifecycleLogger.WriteException("ControlCenter layout save failed; closing continues", exception);
            }
            finally
            {
                closePersistenceComplete = true;
                closePersistenceInProgress = false;
                if (!isClosed) _ = Dispatcher.BeginInvoke(Close, DispatcherPriority.Send);
            }
            return;
        }
        base.OnClosing(e);
    }

    protected override void OnStateChanged(EventArgs e)
    {
        if (WindowState != WindowState.Minimized) lastNonMinimizedState = WindowState;
        base.OnStateChanged(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        isClosed = true;
        Loaded -= OnControlCenterLoaded;
        base.OnClosed(e);
    }

    private void OnControlCenterLoaded(object sender, RoutedEventArgs e)
    {
        if (savedLayoutApplyScheduled) return;
        savedLayoutApplyScheduled = true;
        _ = Dispatcher.BeginInvoke(ApplySavedLayout, DispatcherPriority.Loaded);
    }

    private void ApplySavedLayout()
    {
        if (isClosed || !IsLoaded) return;
        if (DataContext is not MainViewModel viewModel) return;
        var layout = ControlCenterLayoutSettings.Normalize(
            viewModel.ControlCenterLayout,
            SystemParameters.WorkArea.Width,
            SystemParameters.WorkArea.Height);
        Width = layout.WindowWidth;
        Height = layout.WindowHeight;
        if (layout.IsMaximized)
        {
            lastNonMinimizedState = WindowState.Maximized;
            WindowState = WindowState.Maximized;
        }

        // Width/Height and WindowState only update ActualWidth/ActualHeight on the next
        // WPF layout pass. Restore splitter ratios afterwards so their minimum-size
        // clamp uses the resized (or maximized) panel dimensions.
        _ = Dispatcher.BeginInvoke(
            () => ApplySavedPanelLayout(layout),
            DispatcherPriority.Loaded);
    }

    private void ApplySavedPanelLayout(ControlCenterLayoutSettings layout)
    {
        if (isClosed || !IsLoaded) return;
        RunPanel.ApplyLayout(layout);
        RecentRunsPanel.ApplyLayout(layout);
        HasAppliedSavedLayout = true;
    }
}
