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
    public async Task ShowAndInitializeAsync_WhenShowThrows_PropagatesWithoutTreatingItAsInitializationFailure()
    {
        var expected = new InvalidOperationException("show failed");
        var window = new FakeStartupWindow(onShow: () => throw expected);
        var initializeCalled = false;
        var reportCalled = false;

        var actual = await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
            WindowFirstStartup.ShowAndInitializeAsync(
                window,
                () =>
                {
                    initializeCalled = true;
                    return Task.CompletedTask;
                },
                _ => reportCalled = true));

        Assert.AreSame(expected, actual);
        Assert.IsFalse(initializeCalled);
        Assert.IsFalse(reportCalled);
        Assert.AreEqual(0, window.ContentRenderedSubscriberCount);
        Assert.AreEqual(0, window.ClosedSubscriberCount);
    }

    [TestMethod]
    public async Task ShowAndInitializeAsync_WhenWindowClosesBeforeFirstRender_PropagatesAndDoesNotInitialize()
    {
        var window = new FakeStartupWindow();
        var initializeCalled = false;
        var reportCalled = false;

        var startup = WindowFirstStartup.ShowAndInitializeAsync(
            window,
            () =>
            {
                initializeCalled = true;
                return Task.CompletedTask;
            },
            _ => reportCalled = true);
        window.ReportClosed();

        var exception = await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => startup);

        StringAssert.Contains(exception.Message, "closed before its first ContentRendered");
        Assert.IsFalse(initializeCalled);
        Assert.IsFalse(reportCalled);
        Assert.AreEqual(0, window.ContentRenderedSubscriberCount);
        Assert.AreEqual(0, window.ClosedSubscriberCount);
    }

    [TestMethod]
    public void ConfigureMainWindow_AssignsTheSingleWindowAndClosingItShutsDownTheApplication()
    {
        var host = new FakeStartupHost();
        var window = new FakeStartupWindow();

        WindowFirstStartup.ConfigureMainWindow(host, window);

        Assert.AreSame(window, host.MainWindow);
        Assert.AreEqual(System.Windows.ShutdownMode.OnMainWindowClose, host.ShutdownMode);

        window.ReportClosed();
        window.ReportClosed();

        Assert.AreEqual(1, host.ShutdownCount);
        Assert.AreEqual(0, host.ExitCode);
    }

    private sealed class FakeStartupWindow(Action? onShow = null) : IStartupWindow
    {
        private EventHandler? contentRendered;
        private EventHandler? closed;

        public event EventHandler? ContentRendered
        {
            add => contentRendered += value;
            remove => contentRendered -= value;
        }

        public event EventHandler? Closed
        {
            add => closed += value;
            remove => closed -= value;
        }

        public int ShowCount { get; private set; }
        public int ContentRenderedSubscriberCount => contentRendered?.GetInvocationList().Length ?? 0;
        public int ClosedSubscriberCount => closed?.GetInvocationList().Length ?? 0;

        public void Show()
        {
            ShowCount++;
            onShow?.Invoke();
        }

        public void ReportContentRendered() => contentRendered?.Invoke(this, EventArgs.Empty);
        public void ReportClosed() => closed?.Invoke(this, EventArgs.Empty);
    }

    private sealed class FakeStartupHost : IStartupHost
    {
        public IStartupWindow? MainWindow { get; set; }
        public System.Windows.ShutdownMode ShutdownMode { get; set; } = System.Windows.ShutdownMode.OnExplicitShutdown;
        public bool IsShutdownStarted { get; private set; }
        public int ShutdownCount { get; private set; }
        public int? ExitCode { get; private set; }

        public void Shutdown(int exitCode)
        {
            IsShutdownStarted = true;
            ShutdownCount++;
            ExitCode = exitCode;
        }
    }

}
