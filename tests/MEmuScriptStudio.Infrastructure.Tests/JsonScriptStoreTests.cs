using MEmuScriptStudio.Core.Models;
using MEmuScriptStudio.Infrastructure.Persistence;

namespace MEmuScriptStudio.Infrastructure.Tests;

[TestClass]
public sealed class JsonScriptStoreTests
{
    [TestMethod]
    public async Task SaveAndLoadAsync_RoundTripsEveryMvpStepType()
    {
        var directory = CreateTestDirectory();
        try
        {
            using var store = new JsonScriptStore(Path.Combine(directory, "scripts.json"));
            var script = new ScriptDefinition
            {
                Name = "All steps",
                Steps =
                [
                    new AndroidShellStep { Name = "Shell", Command = "echo ok" },
                    new ForceStopStep { Name = "Stop", PackageName = "app" },
                    new OpenAppStep { Name = "Open", PackageName = "app", ActivityName = ".Main" },
                    new DelayStep { Name = "Delay", DurationMilliseconds = 50 },
                    new TapStep { Name = "Tap", X = 1, Y = 2 },
                    new SwipeStep { Name = "Swipe", X1 = 1, Y1 = 2, X2 = 3, Y2 = 4, DurationMilliseconds = 5 },
                    new InputTextStep { Name = "Input", Text = "hello" },
                    new KeyEventStep { Name = "Recent apps", Key = AndroidKeyEvent.RecentApps },
                    new NoteStep { Name = "Note", Text = "skip", IsEnabled = false, ContinueOnError = true, TimeoutSeconds = 9 }
                ]
            };

            await store.SaveAsync([script], CancellationToken.None);
            var loaded = await store.LoadAsync(CancellationToken.None);

            Assert.AreEqual(1, loaded.Count);
            CollectionAssert.AreEqual(
                new[]
                {
                    typeof(AndroidShellStep), typeof(ForceStopStep), typeof(OpenAppStep), typeof(DelayStep),
                    typeof(TapStep), typeof(SwipeStep), typeof(InputTextStep), typeof(KeyEventStep), typeof(NoteStep)
                },
                loaded[0].Steps.Select(step => step.GetType()).ToArray());
            Assert.IsFalse(loaded[0].Steps[8].IsEnabled);
            Assert.IsTrue(loaded[0].Steps[8].ContinueOnError);
            Assert.AreEqual(9, loaded[0].Steps[8].TimeoutSeconds);
            Assert.AreEqual(AndroidKeyEvent.RecentApps, ((KeyEventStep)loaded[0].Steps[7]).Key);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task LoadAsync_MissingFileReturnsEmptyCollection()
    {
        var directory = CreateTestDirectory();
        try
        {
            using var store = new JsonScriptStore(Path.Combine(directory, "missing.json"));
            Assert.AreEqual(0, (await store.LoadAsync(CancellationToken.None)).Count);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string CreateTestDirectory()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "ScriptStoreTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}
