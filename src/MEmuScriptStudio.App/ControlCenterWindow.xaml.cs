using System.Windows;
using System.Windows.Interop;

namespace MEmuScriptStudio.App;

public partial class ControlCenterWindow : Window, Services.IControlCenterWindowHost
{
    private bool isClosed;

    public ControlCenterWindow(object? dataContext)
    {
        InitializeComponent();
        DataContext = dataContext;
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

    protected override void OnClosed(EventArgs e)
    {
        isClosed = true;
        base.OnClosed(e);
    }
}
