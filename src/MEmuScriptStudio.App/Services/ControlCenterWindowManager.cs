namespace MEmuScriptStudio.App.Services;

public interface IControlCenterWindowHost
{
    bool IsAlive { get; }
    bool IsMinimized { get; set; }
    event EventHandler? Closed;
    void Show();
    void Activate();
    void Close();
}

public sealed class ControlCenterWindowManager(Func<object?, IControlCenterWindowHost> createWindow)
{
    private static readonly TimeSpan DefaultCloseTimeout = TimeSpan.FromSeconds(3);
    private IControlCenterWindowHost? current;

    public bool HasCurrent => current is not null;

    public bool TryOpen(object? sharedDataContext, Action<Exception> reportError)
    {
        ArgumentNullException.ThrowIfNull(reportError);
        try
        {
            Open(sharedDataContext);
            return true;
        }
        catch (Exception exception)
        {
            reportError(exception);
            return false;
        }
    }

    public void CloseCurrent()
    {
        var window = current;
        if (window is null) return;
        try { window.Close(); }
        catch (Exception exception)
        {
            ApplicationLifecycleLogger.WriteException("ControlCenter Close failed; shutdown continues", exception);
        }
        if (ReferenceEquals(current, window) && !IsCurrentAlive()) DetachCurrent();
    }

    public async Task<bool> CloseCurrentAsync(TimeSpan? timeout = null)
    {
        var window = current;
        if (window is null) return true;
        if (!IsAlive(window))
        {
            if (ReferenceEquals(current, window)) DetachCurrent();
            return true;
        }

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnWindowClosed(object? sender, EventArgs args) => completion.TrySetResult();
        window.Closed += OnWindowClosed;
        try
        {
            try { window.Close(); }
            catch (Exception exception)
            {
                ApplicationLifecycleLogger.WriteException("ControlCenter Close failed; shutdown continues", exception);
                return false;
            }
            if (!IsAlive(window)) completion.TrySetResult();
            try
            {
                await completion.Task.WaitAsync(timeout ?? DefaultCloseTimeout);
                return true;
            }
            catch (TimeoutException exception)
            {
                ApplicationLifecycleLogger.WriteException("ControlCenter close timed out; MainWindow shutdown continues", exception);
                return false;
            }
        }
        finally
        {
            window.Closed -= OnWindowClosed;
            if (ReferenceEquals(current, window) && !IsAlive(window)) DetachCurrent();
        }
    }

    private void Open(object? sharedDataContext)
    {
        if (current is not null && !IsCurrentAlive()) DetachCurrent();
        if (current is not null)
        {
            try
            {
                current.IsMinimized = false;
                current.Activate();
            }
            catch
            {
                if (!IsCurrentAlive()) DetachCurrent();
                throw;
            }
            return;
        }

        var candidate = createWindow(sharedDataContext);
        current = candidate;
        candidate.Closed += OnClosed;
        try
        {
            candidate.Show();
        }
        catch
        {
            if (ReferenceEquals(current, candidate))
            {
                if (!IsCurrentAlive()) DetachCurrent();
            }
            else
            {
                candidate.Closed -= OnClosed;
            }
            throw;
        }
    }

    private void OnClosed(object? sender, EventArgs args)
    {
        if (!ReferenceEquals(current, sender)) return;
        DetachCurrent();
    }

    private void DetachCurrent()
    {
        if (current is not null) current.Closed -= OnClosed;
        current = null;
    }

    private bool IsCurrentAlive()
    {
        if (current is null) return false;
        return IsAlive(current);
    }

    private static bool IsAlive(IControlCenterWindowHost window)
    {
        try { return window.IsAlive; }
        catch { return true; }
    }
}
