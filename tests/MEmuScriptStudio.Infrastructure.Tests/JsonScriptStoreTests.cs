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
                    new HoldStep { Name = "Hold", X = 3, Y = 4, DurationMilliseconds = 600 },
                    new SwipeStep { Name = "Swipe", X1 = 1, Y1 = 2, X2 = 3, Y2 = 4, DurationMilliseconds = 5 },
                    new InputTextStep { Name = "Input", Text = "hello", PressEnterAfterInput = true },
                    new AndroidClipboardPasteStep { Name = "Paste", PressEnterAfterPaste = true },
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
                    typeof(TapStep), typeof(HoldStep), typeof(SwipeStep), typeof(InputTextStep),
                    typeof(AndroidClipboardPasteStep), typeof(KeyEventStep), typeof(NoteStep)
                },
                loaded[0].Steps.Select(step => step.GetType()).ToArray());
            Assert.IsFalse(loaded[0].Steps[10].IsEnabled);
            Assert.IsTrue(loaded[0].Steps[10].ContinueOnError);
            Assert.AreEqual(9, loaded[0].Steps[10].TimeoutSeconds);
            Assert.AreEqual(AndroidKeyEvent.RecentApps, ((KeyEventStep)loaded[0].Steps[9]).Key);
            Assert.IsTrue(((InputTextStep)loaded[0].Steps[7]).PressEnterAfterInput);
            Assert.IsTrue(((AndroidClipboardPasteStep)loaded[0].Steps[8]).PressEnterAfterPaste);
            Assert.AreEqual(600, ((HoldStep)loaded[0].Steps[5]).DurationMilliseconds);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task LoadAsync_LegacyInputTextDefaultsEnterOptionToFalse()
    {
        var directory = CreateTestDirectory();
        try
        {
            var path = Path.Combine(directory, "scripts.json");
            await File.WriteAllTextAsync(path,
                """
                {
                  "SchemaVersion": 1,
                  "Scripts": [
                    {
                      "Name": "Legacy",
                      "Steps": [
                        { "$type": "inputText", "Name": "Input", "Text": "hello" }
                      ]
                    }
                  ]
                }
                """);
            using var store = new JsonScriptStore(path);

            var loaded = await store.LoadAsync(CancellationToken.None);

            Assert.IsFalse(((InputTextStep)loaded[0].Steps[0]).PressEnterAfterInput);
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
