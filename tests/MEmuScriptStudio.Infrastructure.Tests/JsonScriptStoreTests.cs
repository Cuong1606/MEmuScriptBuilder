using MEmuScriptStudio.Core.Models;
using MEmuScriptStudio.Core.Scripts;
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
            Assert.AreEqual("Delay", loaded[0].Steps[3].Name,
                "Persistence must continue loading legacy custom Delay names without a schema migration.");
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
    public async Task LegacyAndroidShell_LoadSaveRoundTripPreservesCommandAndCommonFields()
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
                      "Name": "Legacy Android shell",
                      "Steps": [
                        {
                          "$type": "androidShell",
                          "Name": "Read rotation",
                          "Command": "settings get system user_rotation",
                          "IsEnabled": false,
                          "ContinueOnError": true,
                          "TimeoutSeconds": 17
                        }
                      ]
                    }
                  ]
                }
                """);
            using var store = new JsonScriptStore(path);

            var loaded = await store.LoadAsync(CancellationToken.None);
            await store.SaveAsync(loaded, CancellationToken.None);
            var roundTripped = (AndroidShellStep)(await store.LoadAsync(CancellationToken.None))[0].Steps[0];

            Assert.AreEqual("Read rotation", roundTripped.Name);
            Assert.AreEqual("settings get system user_rotation", roundTripped.Command);
            Assert.IsFalse(roundTripped.IsEnabled);
            Assert.IsTrue(roundTripped.ContinueOnError);
            Assert.AreEqual(17, roundTripped.TimeoutSeconds);
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

    [TestMethod]
    public async Task FutureDocumentSchema_IsRejectedAndCannotOverwriteUnknownData()
    {
        var directory = CreateTestDirectory();
        try
        {
            var path = Path.Combine(directory, "scripts.json");
            const string original = """
                {
                  "SchemaVersion": 2,
                  "FutureSentinel": "keep-root",
                  "Scripts": []
                }
                """;
            await File.WriteAllTextAsync(path, original);
            using var store = new JsonScriptStore(path);

            var loadError = await Assert.ThrowsExceptionAsync<InvalidDataException>(
                () => store.LoadAsync(CancellationToken.None));
            StringAssert.Contains(loadError.Message, "mới hơn");

            using var directWriter = new JsonScriptStore(path);
            await Assert.ThrowsExceptionAsync<InvalidDataException>(() =>
                directWriter.SaveAsync([new ScriptDefinition { Name = "Không được ghi trực tiếp" }], CancellationToken.None));
            await Assert.ThrowsExceptionAsync<InvalidDataException>(() =>
                store.SaveAsync([new ScriptDefinition { Name = "Không được ghi" }], CancellationToken.None));
            Assert.AreEqual(original, await File.ReadAllTextAsync(path));
            StringAssert.Contains(await File.ReadAllTextAsync(path), "keep-root");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [TestMethod]
    public async Task FutureScriptSchema_IsRejectedAndCannotOverwriteUnknownData()
    {
        var directory = CreateTestDirectory();
        try
        {
            var path = Path.Combine(directory, "scripts.json");
            const string original = """
                {
                  "SchemaVersion": 1,
                  "Scripts": [
                    {
                      "SchemaVersion": 2,
                      "Name": "Future",
                      "FutureSentinel": "keep-script",
                      "Steps": []
                    }
                  ]
                }
                """;
            await File.WriteAllTextAsync(path, original);
            using var store = new JsonScriptStore(path);

            var loadError = await Assert.ThrowsExceptionAsync<InvalidDataException>(
                () => store.LoadAsync(CancellationToken.None));
            StringAssert.Contains(loadError.Message, "Một kịch bản dùng schema 2");
            await Assert.ThrowsExceptionAsync<InvalidDataException>(() =>
                store.SaveAsync([new ScriptDefinition { Name = "Không được ghi" }], CancellationToken.None));

            Assert.AreEqual(original, await File.ReadAllTextAsync(path));
            StringAssert.Contains(await File.ReadAllTextAsync(path), "keep-script");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [TestMethod]
    public async Task CorruptLibrary_IsBackedUpAndWriteBlockedUntilExplicitRecovery()
    {
        var directory = CreateTestDirectory();
        try
        {
            var path = Path.Combine(directory, "scripts.json");
            const string corrupt = "{not-json-with-sentinel";
            await File.WriteAllTextAsync(path, corrupt);
            using var store = new JsonScriptStore(path);

            var loadError = await Assert.ThrowsExceptionAsync<ScriptDataRecoveryRequiredException>(
                () => store.LoadAsync(CancellationToken.None));
            Assert.IsTrue(store.IsWriteBlocked);
            Assert.IsTrue(store.IsRecoveryRequired);
            Assert.AreEqual(loadError.BackupPath, store.RecoveryBackupPath);
            Assert.AreEqual(corrupt, await File.ReadAllTextAsync(loadError.BackupPath));

            await Assert.ThrowsExceptionAsync<ScriptDataRecoveryRequiredException>(() =>
                store.SaveAsync([new ScriptDefinition { Name = "Không được ghi" }], CancellationToken.None));
            Assert.AreEqual(corrupt, await File.ReadAllTextAsync(path));

            await store.RecoverAsync(CancellationToken.None);
            Assert.IsFalse(store.IsWriteBlocked);
            var replacement = new ScriptDefinition { Name = "Sau phục hồi" };
            await store.SaveAsync([replacement], CancellationToken.None);
            var reopened = await store.LoadAsync(CancellationToken.None);

            Assert.AreEqual("Sau phục hồi", reopened.Single().Name);
            Assert.AreEqual(corrupt, await File.ReadAllTextAsync(loadError.BackupPath));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [TestMethod]
    public async Task DirectSave_SemanticallyCorruptCurrentSchemaIsBackedUpAndBlocked()
    {
        var directory = CreateTestDirectory();
        try
        {
            var path = Path.Combine(directory, "scripts.json");
            const string corrupt = """
                {
                  "SchemaVersion": 1,
                  "Scripts": [
                    {
                      "SchemaVersion": 1,
                      "Id": "11111111-1111-1111-1111-111111111111",
                      "Name": "Unsupported step",
                      "Steps": [ { "$type": "futureStep", "Name": "Keep me" } ]
                    }
                  ]
                }
                """;
            await File.WriteAllTextAsync(path, corrupt);
            using var store = new JsonScriptStore(path);

            var error = await Assert.ThrowsExceptionAsync<ScriptDataRecoveryRequiredException>(() =>
                store.SaveAsync([new ScriptDefinition { Name = "Replacement" }], CancellationToken.None));

            Assert.AreEqual(corrupt, await File.ReadAllTextAsync(path));
            Assert.AreEqual(corrupt, await File.ReadAllTextAsync(error.BackupPath));
            Assert.IsTrue(store.IsWriteBlocked);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [TestMethod]
    public async Task NonObjectScriptEntry_UsesCorruptRecoveryPath()
    {
        var directory = CreateTestDirectory();
        try
        {
            var path = Path.Combine(directory, "scripts.json");
            const string corrupt = """
                { "SchemaVersion": 1, "Scripts": [ null ] }
                """;
            await File.WriteAllTextAsync(path, corrupt);
            using var store = new JsonScriptStore(path);

            var error = await Assert.ThrowsExceptionAsync<ScriptDataRecoveryRequiredException>(
                () => store.LoadAsync(CancellationToken.None));

            Assert.AreEqual(corrupt, await File.ReadAllTextAsync(path));
            Assert.AreEqual(corrupt, await File.ReadAllTextAsync(error.BackupPath));
            Assert.IsTrue(store.IsRecoveryRequired);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static string CreateTestDirectory()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "ScriptStoreTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}
