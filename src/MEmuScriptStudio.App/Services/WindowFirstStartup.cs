using System.Windows;

namespace MEmuScriptStudio.App.Services;

public interface IStartupWindow
{
    event EventHandler? ContentRendered;
    void Show();
}

public interface IStartupHost
{
    IStartupWindow? MainWindow { get; set; }
    ShutdownMode ShutdownMode { get; set; }
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
        window.ContentRendered += renderedHandler;
        try
        {
            window.Show();
            await contentRendered.Task;
            await initializeAsync();
        }
        catch (Exception exception)
        {
            reportInitializationError(exception);
        }
        finally
        {
            window.ContentRendered -= renderedHandler;
        }
    }

    public static void ConfigureMainWindow(IStartupHost application, IStartupWindow mainWindow)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(mainWindow);
        application.MainWindow = mainWindow;
        application.ShutdownMode = ShutdownMode.OnMainWindowClose;
    }
}
