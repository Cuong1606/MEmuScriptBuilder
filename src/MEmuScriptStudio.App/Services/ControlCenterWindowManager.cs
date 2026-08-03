namespace MEmuScriptStudio.App.Services;

public interface IControlCenterWindowHost
{
    bool IsMinimized { get; set; }
    event EventHandler? Closed;
    void Show();
    void Activate();
}

public sealed class ControlCenterWindowManager(Func<object?, IControlCenterWindowHost> createWindow)
{
    private IControlCenterWindowHost? current;

    public void Open(object? sharedDataContext)
    {
        if (current is not null)
        {
            current.IsMinimized = false;
            current.Activate();
            return;
        }
        current = createWindow(sharedDataContext);
        current.Closed += OnClosed;
        current.Show();
    }

    private void OnClosed(object? sender, EventArgs args)
    {
        if (current is not null) current.Closed -= OnClosed;
        current = null;
    }
}
