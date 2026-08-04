using System.Text.Json;
using MEmuScriptStudio.Core.Models;
using MEmuScriptStudio.Infrastructure.Persistence;

namespace MEmuScriptStudio.Infrastructure.Tests;

[TestClass]
public sealed class JsonSettingsStoreTests
{
    [TestMethod]
    public async Task SaveAndLoadAsync_RoundTripsSettings()
    {
        var directory = CreateTestDirectory();
        try
        {
            var store = new JsonSettingsStore(Path.Combine(directory, "settings.json"));
            var settings = new ApplicationSettings
            {
                MemucPath = @"C:\MEmu\memuc.exe",
                MultiInstanceRun = new MultiInstanceRunSettings
                {
                    LaunchSpacingMode = LaunchSpacingMode.Random,
                    FixedSpacingMilliseconds = 250,
                    RandomMinimumSpacingMilliseconds = 500,
                    RandomMaximumSpacingMilliseconds = 1500,
                    StopAllOnInvalidTarget = true,
                    ScriptAssignmentMode = ScriptAssignmentMode.PerInstance,
                    CommonScriptId = Guid.Parse("22222222-2222-2222-2222-222222222222")
                }
            };
            settings.MultiInstanceRun.ScriptAssignments[4] = Guid.Parse("11111111-1111-1111-1111-111111111111");
            settings.ApplicationDisplayNames["com.example.app"] = "Ứng dụng mẫu";
            await store.SaveAsync(settings, CancellationToken.None);

            var loaded = await store.LoadAsync(CancellationToken.None);

            Assert.AreEqual(@"C:\MEmu\memuc.exe", loaded.MemucPath);
            Assert.AreEqual(ApplicationSettings.CurrentSchemaVersion, loaded.SchemaVersion);
            Assert.AreEqual("Ứng dụng mẫu", loaded.ApplicationDisplayNames["com.example.app"]);
            Assert.AreEqual(LaunchSpacingMode.Random, loaded.MultiInstanceRun.LaunchSpacingMode);
            Assert.AreEqual(250, loaded.MultiInstanceRun.FixedSpacingMilliseconds);
            Assert.AreEqual(500, loaded.MultiInstanceRun.RandomMinimumSpacingMilliseconds);
            Assert.AreEqual(1500, loaded.MultiInstanceRun.RandomMaximumSpacingMilliseconds);
            Assert.IsTrue(loaded.MultiInstanceRun.StopAllOnInvalidTarget);
            Assert.AreEqual(ScriptAssignmentMode.PerInstance, loaded.MultiInstanceRun.ScriptAssignmentMode);
            Assert.AreEqual(settings.MultiInstanceRun.CommonScriptId, loaded.MultiInstanceRun.CommonScriptId);
            Assert.AreEqual(settings.MultiInstanceRun.ScriptAssignments[4], loaded.MultiInstanceRun.ScriptAssignments[4]);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task LegacyWindowLayout_LoadsSafelyAndIsOmittedOnNextSave()
    {
        var directory = CreateTestDirectory();
        try
        {
            var path = Path.Combine(directory, "settings.json");
            await File.WriteAllTextAsync(path, """
                {
                  "SchemaVersion": 5,
                  "MemucPath": "C:\\MEmu\\memuc.exe",
                  "ApplicationDisplayNames": { "com.example.app": "Example" },
                  "MultiInstanceRun": {
                    "LaunchSpacingMode": 0,
                    "FixedSpacingMilliseconds": 450,
                    "ScriptAssignments": {}
                  },
                  "WindowLayout": {
                    "SortMode": 2,
                    "CustomOrder": [4, 2, 9],
                    "OriginalPlacements": [
                      { "InstanceIndex": 4, "Left": 10, "Top": 20, "Width": 300, "Height": 500 }
                    ]
                  }
                }
                """);
            var store = new JsonSettingsStore(path);

            var loaded = await store.LoadAsync(CancellationToken.None);

            Assert.AreEqual(@"C:\MEmu\memuc.exe", loaded.MemucPath);
            Assert.AreEqual("Example", loaded.ApplicationDisplayNames["com.example.app"]);
            Assert.AreEqual(450, loaded.MultiInstanceRun.FixedSpacingMilliseconds);

            await store.SaveAsync(loaded, CancellationToken.None);
            using var saved = JsonDocument.Parse(await File.ReadAllTextAsync(path));
            Assert.IsFalse(saved.RootElement.TryGetProperty("WindowLayout", out _));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void ProductionAssemblies_DoNotExposeLegacyWindowLayoutTypes()
    {
        var coreAssembly = typeof(ApplicationSettings).Assembly;
        var infrastructureAssembly = typeof(JsonSettingsStore).Assembly;

        string[] coreTypeNames =
        [
            "MEmuScriptStudio.Core.MEmu.WindowGridPlanner",
            "MEmuScriptStudio.Core.MEmu.IMemuWindowLayoutService",
            "MEmuScriptStudio.Core.Models.EmulatorWindowLayoutSettings",
            "MEmuScriptStudio.Core.Models.WindowGridPlan",
            "MEmuScriptStudio.Core.Models.WindowGeometrySnapshot"
        ];
        string[] infrastructureTypeNames =
        [
            "MEmuScriptStudio.Infrastructure.MEmu.IWindowPlatform",
            "MEmuScriptStudio.Infrastructure.MEmu.WindowsMemuWindowLayoutService",
            "MEmuScriptStudio.Infrastructure.MEmu.WindowsWindowPlatform"
        ];

        foreach (var typeName in coreTypeNames)
            Assert.IsNull(coreAssembly.GetType(typeName), $"Legacy Core type remains: {typeName}");
        foreach (var typeName in infrastructureTypeNames)
            Assert.IsNull(infrastructureAssembly.GetType(typeName), $"Legacy Infrastructure type remains: {typeName}");
        Assert.IsNull(typeof(ApplicationSettings).GetProperty("WindowLayout"));
    }

    [TestMethod]
    public async Task LoadAsync_SchemaVersionOneAddsDefaultMultiInstanceSettings()
    {
        var directory = CreateTestDirectory();
        try
        {
            var path = Path.Combine(directory, "settings.json");
            await File.WriteAllTextAsync(path, """
                {
                  "SchemaVersion": 1,
                  "MemucPath": "C:\\MEmu\\memuc.exe",
                  "ApplicationDisplayNames": { "com.example.app": "Example" }
                }
                """);
            var store = new JsonSettingsStore(path);

            var loaded = await store.LoadAsync(CancellationToken.None);

            Assert.AreEqual(ApplicationSettings.CurrentSchemaVersion, loaded.SchemaVersion);
            Assert.AreEqual(@"C:\MEmu\memuc.exe", loaded.MemucPath);
            Assert.AreEqual("Example", loaded.ApplicationDisplayNames["com.example.app"]);
            Assert.AreEqual(LaunchSpacingMode.Fixed, loaded.MultiInstanceRun.LaunchSpacingMode);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task UpdateAsync_ConcurrentWritersPreserveIndependentSettingsFields()
    {
        var directory = CreateTestDirectory();
        try
        {
            var store = new JsonSettingsStore(Path.Combine(directory, "settings.json"));
            await store.SaveAsync(new ApplicationSettings { MemucPath = @"C:\MEmu\memuc.exe" }, CancellationToken.None);

            await Task.WhenAll(
                store.UpdateAsync(settings =>
                {
                    settings.MultiInstanceRun.FixedSpacingMilliseconds = 300;
                }, CancellationToken.None),
                store.UpdateAsync(settings =>
                {
                    settings.ApplicationDisplayNames["com.example.concurrent"] = "Concurrent";
                }, CancellationToken.None));

            var loaded = await store.LoadAsync(CancellationToken.None);
            Assert.AreEqual(300, loaded.MultiInstanceRun.FixedSpacingMilliseconds);
            Assert.AreEqual("Concurrent", loaded.ApplicationDisplayNames["com.example.concurrent"]);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task LegacyConcurrencyFields_LoadButAreOmittedOnNextSave()
    {
        var directory = CreateTestDirectory();
        try
        {
            var path = Path.Combine(directory, "settings.json");
            await File.WriteAllTextAsync(path, """
                {
                  "SchemaVersion": 3,
                  "MultiInstanceRun": {
                    "TargetScope": 1,
                    "MaximumConcurrencyMode": 1,
                    "MaximumConcurrency": 7,
                    "LaunchSpacingMode": 0,
                    "FixedSpacingMilliseconds": 450,
                    "ScriptAssignments": {}
                  }
                }
                """);
            var store = new JsonSettingsStore(path);
            var loaded = await store.LoadAsync(CancellationToken.None);
            Assert.AreEqual(450, loaded.MultiInstanceRun.FixedSpacingMilliseconds);

            await store.SaveAsync(loaded, CancellationToken.None);
            var saved = await File.ReadAllTextAsync(path);
            Assert.IsFalse(saved.Contains("MaximumConcurrency", StringComparison.Ordinal));
            Assert.IsFalse(saved.Contains("TargetScope", StringComparison.Ordinal));
            Assert.IsTrue(saved.Contains("FixedSpacingMilliseconds", StringComparison.Ordinal));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [TestMethod]
    public async Task LoadAsync_CorruptJsonReportsJsonException()
    {
        var directory = CreateTestDirectory();
        try
        {
            var path = Path.Combine(directory, "settings.json");
            await File.WriteAllTextAsync(path, "{not-json");
            var store = new JsonSettingsStore(path);

            await Assert.ThrowsExceptionAsync<JsonException>(() => store.LoadAsync(CancellationToken.None));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task SaveAsync_FailureDoesNotOverwriteExistingFile()
    {
        var directory = CreateTestDirectory();
        try
        {
            var targetDirectory = Path.Combine(directory, "settings.json");
            Directory.CreateDirectory(targetDirectory);
            var markerPath = Path.Combine(targetDirectory, "keep.txt");
            await File.WriteAllTextAsync(markerPath, "keep");
            var store = new JsonSettingsStore(targetDirectory);

            Exception? capturedException = null;
            try
            {
                await store.SaveAsync(
                    new ApplicationSettings { MemucPath = @"C:\MEmu\memuc.exe" },
                    CancellationToken.None);
            }
            catch (Exception exception)
            {
                capturedException = exception;
            }

            Assert.IsTrue(capturedException is IOException or UnauthorizedAccessException);
            Assert.AreEqual("keep", await File.ReadAllTextAsync(markerPath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string CreateTestDirectory()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "SettingsTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}
