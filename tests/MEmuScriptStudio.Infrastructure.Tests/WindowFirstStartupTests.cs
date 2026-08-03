using MEmuScriptStudio.App.Services;

namespace MEmuScriptStudio.Infrastructure.Tests;

[TestClass]
public sealed class WindowFirstStartupTests
{
    [TestMethod]
    public async Task ShowAndInitializeAsync_ShowsExactlyOnceBeforeInitializationCompletes()
    {
        var calls = new List<string>();
        var initializationStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseInitialization = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var window = new FakeStartupWindow(() => calls.Add("show"));

        var startup = WindowFirstStartup.ShowAndInitializeAsync(
            window,
            async () =>
            {
                calls.Add("initialize");
                initializationStarted.SetResult();
                await releaseInitialization.Task;
            },
            _ => Assert.Fail("Initialization should not fail."));

        Assert.AreEqual(1, window.ShowCount);
        Assert.IsFalse(initializationStarted.Task.IsCompleted, "Initialization must wait until the first content render.");
        window.ReportContentRendered();
        await initializationStarted.Task;
        CollectionAssert.AreEqual(new[] { "show", "initialize" }, calls);
        Assert.IsFalse(startup.IsCompleted);

        releaseInitialization.SetResult();
        await startup;

        Assert.AreEqual(1, window.ShowCount);
    }

    [TestMethod]
    public async Task ShowAndInitializeAsync_ReportsInitializationFailureWithoutShowingAgain()
    {
        var window = new FakeStartupWindow();
        Exception? reported = null;
        var expected = new InvalidOperationException("startup failed");

        var startup = WindowFirstStartup.ShowAndInitializeAsync(
            window,
            () => Task.FromException(expected),
            exception => reported = exception);
        window.ReportContentRendered();
        await startup;

        Assert.AreEqual(1, window.ShowCount);
        Assert.AreSame(expected, reported);
    }

    [TestMethod]
    public void ConfigureMainWindow_AssignsTheSingleWindowAndPreservesCloseShutdownBehavior()
    {
        var host = new FakeStartupHost();
        var window = new FakeStartupWindow();

        WindowFirstStartup.ConfigureMainWindow(host, window);

        Assert.AreSame(window, host.MainWindow);
        Assert.AreEqual(System.Windows.ShutdownMode.OnMainWindowClose, host.ShutdownMode);
    }

    private sealed class FakeStartupWindow(Action? onShow = null) : IStartupWindow
    {
        public event EventHandler? ContentRendered;
        public int ShowCount { get; private set; }

        public void Show()
        {
            ShowCount++;
            onShow?.Invoke();
        }

        public void ReportContentRendered() => ContentRendered?.Invoke(this, EventArgs.Empty);
    }

    private sealed class FakeStartupHost : IStartupHost
    {
        public IStartupWindow? MainWindow { get; set; }
        public System.Windows.ShutdownMode ShutdownMode { get; set; } = System.Windows.ShutdownMode.OnExplicitShutdown;
    }
}
