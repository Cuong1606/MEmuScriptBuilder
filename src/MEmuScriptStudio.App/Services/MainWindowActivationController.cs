using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace MEmuScriptStudio.App.Services;

internal interface IActivationDispatcher
{
    bool IsShuttingDown { get; }
    void Post(Action action);
}

internal interface IMainWindowActivationTarget
{
    bool IsVisible { get; }
    bool IsMinimized { get; }
    void Show();
    void Restore();
    void BringToFront();
}

internal sealed class WpfActivationDispatcher(Dispatcher dispatcher) : IActivationDispatcher
{
    public bool IsShuttingDown => dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished;
    public void Post(Action action) => dispatcher.BeginInvoke(action, DispatcherPriority.Normal);
}

internal sealed class WpfMainWindowActivationTarget(Window window) : IMainWindowActivationTarget
{
    public bool IsVisible => window.IsVisible;
    public bool IsMinimized => window.WindowState == WindowState.Minimized;
    public void Show() => window.Show();
    public void Restore() => window.WindowState = WindowState.Normal;

    public void BringToFront()
    {
        window.Activate();
        window.Focus();
        var handle = new WindowInteropHelper(window).Handle;
        if (handle != IntPtr.Zero) SetForegroundWindow(handle);
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr windowHandle);
}

internal sealed class MainWindowActivationController(
    IActivationDispatcher dispatcher,
    Func<IMainWindowActivationTarget?> targetProvider,
    Action<Exception>? reportError = null)
{
    private readonly Action<Exception> reportError = reportError ?? (_ => { });
    private int pendingActivation;
    private int windowReady;

    public void RequestActivation()
    {
        Interlocked.Exchange(ref pendingActivation, 1);
        ScheduleProcessing();
    }

    public void MarkWindowReady()
    {
        Interlocked.Exchange(ref windowReady, 1);
        ScheduleProcessing();
    }

    private void ScheduleProcessing()
    {
        try
        {
            if (!dispatcher.IsShuttingDown) dispatcher.Post(ProcessPendingActivation);
        }
        catch (Exception exception)
        {
            ReportSafely(exception);
        }
    }

    private void ProcessPendingActivation()
    {
        if (Volatile.Read(ref windowReady) == 0 || Volatile.Read(ref pendingActivation) == 0) return;
        IMainWindowActivationTarget? target;
        try
        {
            target = targetProvider();
        }
        catch (Exception exception)
        {
            ReportSafely(exception);
            return;
        }
        if (target is null || Interlocked.Exchange(ref pendingActivation, 0) == 0) return;
        try
        {
            if (!target.IsVisible) target.Show();
            if (target.IsMinimized) target.Restore();
            target.BringToFront();
        }
        catch (Exception exception)
        {
            ReportSafely(exception);
        }
    }

    private void ReportSafely(Exception exception)
    {
        try { reportError(exception); }
        catch (Exception) { }
    }
}
