using MEmuScriptStudio.App.Services;

namespace MEmuScriptStudio.Infrastructure.Tests;

[TestClass]
public sealed class ControlCenterWindowManagerTests
{
    [TestMethod]
    public void Open_ReusesVisibleWindow_AndRecreatesOnlyAfterClose()
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

        manager.Open(shared);
        created[0].IsMinimized = true;
        manager.Open(shared);

        Assert.AreEqual(1, created.Count);
        Assert.AreEqual(1, created[0].ShowCount);
        Assert.AreEqual(1, created[0].ActivateCount);
        Assert.IsFalse(created[0].IsMinimized);
        Assert.AreSame(shared, contexts[0]);

        created[0].Close();
        manager.Open(shared);
        Assert.AreEqual(2, created.Count);
        Assert.AreSame(shared, contexts[1]);
    }

    private sealed class FakeHost : IControlCenterWindowHost
    {
        public bool IsMinimized { get; set; }
        public int ShowCount { get; private set; }
        public int ActivateCount { get; private set; }
        public event EventHandler? Closed;
        public void Show() => ShowCount++;
        public void Activate() => ActivateCount++;
        public void Close() => Closed?.Invoke(this, EventArgs.Empty);
    }
}
