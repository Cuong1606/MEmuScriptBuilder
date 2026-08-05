using MEmuScriptStudio.App.Services;

namespace MEmuScriptStudio.Infrastructure.Tests;

[TestClass]
public sealed class SingleInstanceCoordinatorTests
{
    [TestMethod]
    public void PrimaryAcquiresOwnershipAndDisposeAllowsNextPrimary()
    {
        var names = CreateNames();
        var first = new SingleInstanceCoordinator(names);
        var firstResult = first.Start(() => { });

        Assert.IsTrue(firstResult.IsPrimary);
        Assert.IsTrue(firstResult.ShouldContinueStartup);

        first.Dispose();
        using var received = new ManualResetEventSlim();
        using var next = new SingleInstanceCoordinator(names);
        Assert.IsTrue(next.Start(received.Set).IsPrimary);
        using var secondary = new SingleInstanceCoordinator(names);
        Assert.IsTrue(secondary.Start(() => { }).ActivationSent);
        Assert.IsTrue(received.Wait(TimeSpan.FromSeconds(2)));
    }

    [TestMethod]
    public void ProductionNamesAreStableAndScopedToCurrentUserSession()
    {
        var first = SingleInstanceNames.ForCurrentUserSession();
        var second = SingleInstanceNames.ForCurrentUserSession();

        Assert.AreEqual(first, second);
        StringAssert.StartsWith(first.MutexName, @"Local\MEmuScriptStudio.SingleInstance.");
        StringAssert.StartsWith(first.PipeName, "MEmuScriptStudio.Activation.");
        Assert.IsFalse(first.PipeName.Contains('\\'));
    }

    [TestMethod]
    public void SecondaryDoesNotContinueStartupAndListenerReceivesActivation()
    {
        var names = CreateNames();
        using var received = new ManualResetEventSlim();
        using var primary = new SingleInstanceCoordinator(names);
        Assert.IsTrue(primary.Start(received.Set).IsPrimary);

        using var secondary = new SingleInstanceCoordinator(names);
        var result = secondary.Start(() => Assert.Fail("Secondary must not start a listener."));

        Assert.IsFalse(result.IsPrimary);
        Assert.IsFalse(result.ShouldContinueStartup);
        Assert.IsTrue(result.ActivationSent);
        Assert.IsTrue(received.Wait(TimeSpan.FromSeconds(2)));
    }

    [TestMethod]
    public void ActivationHandlerFailureIsReportedAndListenerContinues()
    {
        var names = CreateNames();
        using var errorReported = new ManualResetEventSlim();
        using var secondActivation = new ManualResetEventSlim();
        var activationCount = 0;
        using var primary = new SingleInstanceCoordinator(names, _ => errorReported.Set());
        primary.Start(() =>
        {
            if (Interlocked.Increment(ref activationCount) == 1)
                throw new InvalidOperationException("activation failed");
            secondActivation.Set();
        });

        using (var firstSecondary = new SingleInstanceCoordinator(names))
            Assert.IsTrue(firstSecondary.Start(() => { }).ActivationSent);
        Assert.IsTrue(errorReported.Wait(TimeSpan.FromSeconds(2)));

        using (var secondSecondary = new SingleInstanceCoordinator(names))
            Assert.IsTrue(secondSecondary.Start(() => { }).ActivationSent);
        Assert.IsTrue(secondActivation.Wait(TimeSpan.FromSeconds(2)));
        Assert.AreEqual(2, activationCount);
    }

    [TestMethod]
    public void MissingPipeDoesNotThrowOrAllowSecondaryStartup()
    {
        var names = CreateNames();
        using var mutex = new Mutex(true, names.MutexName, out var createdNew);
        Assert.IsTrue(createdNew);
        var errors = 0;
        using var secondary = new SingleInstanceCoordinator(
            names, _ => Interlocked.Increment(ref errors), connectTimeoutMilliseconds: 50);

        var result = secondary.Start(() => { });

        Assert.IsFalse(result.ShouldContinueStartup);
        Assert.IsFalse(result.ActivationSent);
        Assert.AreEqual(1, errors);
        mutex.ReleaseMutex();
    }

    [TestMethod]
    public void ActivationRequestedDuringStartupRunsOnceWhenWindowBecomesReady()
    {
        var dispatcher = new ImmediateDispatcher();
        var target = new FakeActivationTarget { IsVisible = false, IsMinimized = true };
        var controller = new MainWindowActivationController(dispatcher, () => target);

        controller.RequestActivation();

        Assert.AreEqual(0, target.BringToFrontCount);
        controller.MarkWindowReady();
        Assert.AreEqual(1, target.ShowCount);
        Assert.AreEqual(1, target.RestoreCount);
        Assert.AreEqual(1, target.BringToFrontCount);
        CollectionAssert.AreEqual(new[] { "Show", "Restore", "BringToFront" }, target.Calls.ToArray());
    }

    [TestMethod]
    public void ActivationExceptionIsContained()
    {
        var errors = 0;
        var target = new FakeActivationTarget { IsVisible = true, ThrowOnBringToFront = true };
        var controller = new MainWindowActivationController(
            new ImmediateDispatcher(), () => target, _ => Interlocked.Increment(ref errors));
        controller.MarkWindowReady();

        controller.RequestActivation();

        Assert.AreEqual(1, errors);
    }

    [TestMethod]
    public void AppChecksSingleInstanceBeforeConstructingApplicationServices()
    {
        var source = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "MEmuScriptStudio.App", "App.xaml.cs"));
        var ownershipCheck = source.IndexOf("singleInstanceCoordinator.Start", StringComparison.Ordinal);
        var secondaryExit = source.IndexOf("!singleInstanceResult.ShouldContinueStartup", StringComparison.Ordinal);
        var serviceConstruction = source.IndexOf("new ServiceCollection", StringComparison.Ordinal);

        Assert.IsTrue(ownershipCheck >= 0);
        Assert.IsTrue(secondaryExit > ownershipCheck);
        Assert.IsTrue(serviceConstruction > secondaryExit);
    }

    [TestMethod]
    public void WindowActivationDoesNotUseTopmost()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(), "src", "MEmuScriptStudio.App", "Services", "MainWindowActivationController.cs"));
        Assert.IsFalse(source.Contains("Topmost", StringComparison.Ordinal));
        var coordinatorSource = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(), "src", "MEmuScriptStudio.App", "Services", "SingleInstanceCoordinator.cs"));
        Assert.IsFalse(coordinatorSource.Contains("GetProcessesByName", StringComparison.Ordinal));
    }

    private static SingleInstanceNames CreateNames()
    {
        var id = Guid.NewGuid().ToString("N");
        return new SingleInstanceNames($@"Local\MEmuScriptStudio.Tests.{id}", $"MEmuScriptStudio.Tests.{id}");
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "MEmuScriptStudio.sln")))
            current = current.Parent;
        return current?.FullName ?? throw new DirectoryNotFoundException();
    }

    private sealed class ImmediateDispatcher : IActivationDispatcher
    {
        public bool IsShuttingDown { get; set; }
        public void Post(Action action) => action();
    }

    private sealed class FakeActivationTarget : IMainWindowActivationTarget
    {
        public bool IsVisible { get; set; }
        public bool IsMinimized { get; set; }
        public bool ThrowOnBringToFront { get; set; }
        public int ShowCount { get; private set; }
        public int RestoreCount { get; private set; }
        public int BringToFrontCount { get; private set; }
        public List<string> Calls { get; } = [];

        public void Show()
        {
            ShowCount++;
            Calls.Add("Show");
            IsVisible = true;
        }

        public void Restore()
        {
            RestoreCount++;
            Calls.Add("Restore");
            IsMinimized = false;
        }

        public void BringToFront()
        {
            BringToFrontCount++;
            Calls.Add("BringToFront");
            if (ThrowOnBringToFront) throw new InvalidOperationException("activate failed");
        }
    }
}
