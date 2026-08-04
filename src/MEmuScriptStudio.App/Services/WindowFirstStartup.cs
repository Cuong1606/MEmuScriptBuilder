using System.Windows;

namespace MEmuScriptStudio.App.Services;

public interface IStartupWindow
{
    event EventHandler? ContentRendered;
    event EventHandler? Closed;
    void Show();
}

public interface IStartupHost
{
    IStartupWindow? MainWindow { get; set; }
    ShutdownMode ShutdownMode { get; set; }
    bool IsShutdownStarted { get; }
    void Shutdown(int exitCode);
}

public sealed class WpfStartupHost(Application application) : IStartupHost
{
    public IStartupWindow? MainWindow
    {
        get => application.MainWindow as IStartupWindow;
        set => application.MainWindow = value as Window
            ?? throw new ArgumentException("The startup window must be a WPF Window.", nameof(value));
    }

    public ShutdownMode ShutdownMode
    {
        get => application.ShutdownMode;
        set => application.ShutdownMode = value;
    }

    public bool IsShutdownStarted =>
        application.Dispatcher.HasShutdownStarted || application.Dispatcher.HasShutdownFinished;

    public void Shutdown(int exitCode) => application.Shutdown(exitCode);
}

public static class WindowFirstStartup
{
    public static async Task ShowAndInitializeAsync(
        IStartupWindow window,
        Func<Task> initializeAsync,
        Action<Exception> reportInitializationError)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(initializeAsync);
        ArgumentNullException.ThrowIfNull(reportInitializationError);

        var contentRendered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler renderedHandler = (_, _) => contentRendered.TrySetResult();
        EventHandler closedHandler = (_, _) => contentRendered.TrySetException(
            new InvalidOperationException("MainWindow closed before its first ContentRendered event."));
        window.ContentRendered += renderedHandler;
        window.Closed += closedHandler;
        try
        {
            ApplicationLifecycleLogger.Write("MainWindow Show requested");
            window.Show();
            ApplicationLifecycleLogger.Write("MainWindow Show returned");
            await contentRendered.Task;
        }
        finally
        {
            window.ContentRendered -= renderedHandler;
            window.Closed -= closedHandler;
        }

        try
        {
            await initializeAsync();
        }
        catch (Exception exception)
        {
            reportInitializationError(exception);
        }
    }

    public static void ConfigureMainWindow(IStartupHost application, IStartupWindow mainWindow)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(mainWindow);
        application.MainWindow = mainWindow;
        application.ShutdownMode = ShutdownMode.OnMainWindowClose;
        mainWindow.Closed += (_, _) =>
        {
            if (!application.IsShutdownStarted) application.Shutdown(0);
        };
    }
}
