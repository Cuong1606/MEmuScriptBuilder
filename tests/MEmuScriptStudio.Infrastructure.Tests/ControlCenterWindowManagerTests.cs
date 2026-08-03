using MEmuScriptStudio.App.Services;

namespace MEmuScriptStudio.Infrastructure.Tests;

[TestClass]
public sealed class ControlCenterWindowManagerTests
{
    [TestMethod]
    public void TryOpen_FirstCall_CreatesAndShowsWindowWithSharedState()
    {
        var created = new List<FakeHost>();
        var contexts = new List<object?>();
        var manager = new ControlCenterWindowManager(context =>
        {
            contexts.Add(context);
            var host = new FakeHost();
            created.Add(host);
            return host;
        });
        var shared = new object();

        var opened = manager.TryOpen(shared, exception => Assert.Fail(exception.ToString()));

        Assert.IsTrue(opened);
        Assert.AreEqual(1, created.Count);
        Assert.AreEqual(1, created[0].ShowCount);
        Assert.AreSame(shared, contexts[0]);
    }

    [TestMethod]
    public void TryOpen_SecondCall_ActivatesExistingWindowWithoutCreatingAnother()
    {
        var created = new List<FakeHost>();
        var contexts = new List<object?>();
        var manager = CreateManager(created, contexts);
        var shared = new object();

        manager.TryOpen(shared, exception => Assert.Fail(exception.ToString()));
        created[0].IsMinimized = true;
        var opened = manager.TryOpen(shared, exception => Assert.Fail(exception.ToString()));

        Assert.IsTrue(opened);
        Assert.AreEqual(1, created.Count);
        Assert.AreEqual(1, created[0].ShowCount);
        Assert.AreEqual(1, created[0].ActivateCount);
        Assert.IsFalse(created[0].IsMinimized);
        Assert.AreEqual(1, contexts.Count);
    }

    [TestMethod]
    public void TryOpen_AfterClose_CreatesFreshWindowUsingSameState()
    {
        var created = new List<FakeHost>();
        var contexts = new List<object?>();
        var manager = CreateManager(created, contexts);
        var shared = new object();

        manager.TryOpen(shared, exception => Assert.Fail(exception.ToString()));
        created[0].Close();
        var opened = manager.TryOpen(shared, exception => Assert.Fail(exception.ToString()));

        Assert.IsTrue(opened);
        Assert.AreEqual(2, created.Count);
        Assert.AreNotSame(created[0], created[1]);
        Assert.AreSame(shared, contexts[0]);
        Assert.AreSame(shared, contexts[1]);
    }

    [TestMethod]
    public void TryOpen_WhenWindowInitializationFails_ContainsErrorAndAllowsRetry()
    {
        var attempts = 0;
        var errors = new List<Exception>();
        var shared = new object();
        var successfulHost = new FakeHost();
        var manager = new ControlCenterWindowManager(context =>
        {
            Assert.AreSame(shared, context);
            if (attempts++ == 0) throw new InvalidOperationException("XAML binding failed");
            return successfulHost;
        });

        var firstOpened = manager.TryOpen(shared, errors.Add);
        var secondOpened = manager.TryOpen(shared, errors.Add);

        Assert.IsFalse(firstOpened, "The open command must contain the fake initialization failure instead of terminating MainWindow.");
        Assert.IsTrue(secondOpened, "A failed construction must not leave a closed or invalid host cached.");
        Assert.AreEqual(1, errors.Count);
        Assert.AreEqual("XAML binding failed", errors[0].Message);
        Assert.AreEqual(1, successfulHost.ShowCount);
    }

    [TestMethod]
    public void TryOpen_WhenShowFails_DetachesInvalidWindowAndAllowsFreshWindow()
    {
        var first = new FakeHost { ShowException = new InvalidOperationException("Show failed") };
        var second = new FakeHost();
        var queue = new Queue<FakeHost>([first, second]);
        var errors = new List<Exception>();
        var manager = new ControlCenterWindowManager(_ => queue.Dequeue());

        Assert.IsFalse(manager.TryOpen(new object(), errors.Add));
        Assert.IsTrue(manager.TryOpen(new object(), errors.Add));

        Assert.AreEqual(1, errors.Count);
        Assert.AreEqual(1, first.ShowCount);
        Assert.AreEqual(1, second.ShowCount);
    }

    [TestMethod]
    public void TryOpen_WhenShowFailsAfterWindowIsAlive_KeepsTrackingSameWindow()
    {
        var created = new List<FakeHost>();
        var errors = new List<Exception>();
        var host = new FakeHost
        {
            ShowException = new InvalidOperationException("Render failed after HWND"),
            BecomeAliveBeforeShowFailure = true
        };
        var manager = new ControlCenterWindowManager(_ =>
        {
            created.Add(host);
            return host;
        });

        Assert.IsFalse(manager.TryOpen(new object(), errors.Add));
        host.ShowException = null;
        Assert.IsTrue(manager.TryOpen(new object(), errors.Add));

        Assert.AreEqual(1, created.Count, "A live window must never be abandoned and replaced by a duplicate.");
        Assert.AreEqual(1, host.ShowCount);
        Assert.AreEqual(1, host.ActivateCount);
    }

    [TestMethod]
    public void TryOpen_WhenActivateFailsOnLiveWindow_DoesNotCreateDuplicate()
    {
        var created = new List<FakeHost>();
        var errors = new List<Exception>();
        var host = new FakeHost();
        var manager = new ControlCenterWindowManager(_ =>
        {
            created.Add(host);
            return host;
        });

        Assert.IsTrue(manager.TryOpen(new object(), errors.Add));
        host.ActivateException = new InvalidOperationException("Activate failed");
        Assert.IsFalse(manager.TryOpen(new object(), errors.Add));
        Assert.IsFalse(manager.TryOpen(new object(), errors.Add));

        Assert.AreEqual(1, created.Count);
        Assert.AreEqual(2, host.ActivateCount);
        Assert.AreEqual(2, errors.Count);
    }

    private static ControlCenterWindowManager CreateManager(
        ICollection<FakeHost> created,
        ICollection<object?> contexts) =>
        new(context =>
        {
            contexts.Add(context);
            var host = new FakeHost();
            created.Add(host);
            return host;
        });

    private sealed class FakeHost : IControlCenterWindowHost
    {
        public bool IsAlive { get; private set; }
        public bool IsMinimized { get; set; }
        public int ShowCount { get; private set; }
        public int ActivateCount { get; private set; }
        public Exception? ShowException { get; set; }
        public Exception? ActivateException { get; set; }
        public bool BecomeAliveBeforeShowFailure { get; init; }
        public event EventHandler? Closed;
        public void Show()
        {
            ShowCount++;
            if (ShowException is null || BecomeAliveBeforeShowFailure) IsAlive = true;
            if (ShowException is not null) throw ShowException;
        }
        public void Activate()
        {
            ActivateCount++;
            if (ActivateException is not null) throw ActivateException;
        }
        public void Close()
        {
            IsAlive = false;
            Closed?.Invoke(this, EventArgs.Empty);
        }
    }
}
