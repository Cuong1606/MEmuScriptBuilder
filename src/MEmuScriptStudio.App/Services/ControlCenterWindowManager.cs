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
    private IControlCenterWindowHost? current;

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
        finally
        {
            if (ReferenceEquals(current, window)) DetachCurrent();
        }
    }

    private void Open(object? sharedDataContext)
    {
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
        try { return current.IsAlive; }
        catch { return true; }
    }
}
