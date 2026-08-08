using System.Text.Json;
using MEmuScriptStudio.Core.Models;
using MEmuScriptStudio.Infrastructure.Persistence;

namespace MEmuScriptStudio.Infrastructure.Tests;

[TestClass]
public sealed class JsonSettingsStoreTests
{
    [TestMethod]
    public async Task AndroidDeviceAliases_RoundTripByExactSerialAndRemainIsolated()
    {
        var directory = CreateTestDirectory();
        try
        {
            var path = Path.Combine(directory, "settings.json");
            var settings = new ApplicationSettings();
            settings.AndroidDeviceAliases["SERIAL-A"] = "Redmi chính";
            settings.AndroidDeviceAliases["SERIAL-B"] = "Máy phụ";

            await new JsonSettingsStore(path).SaveAsync(settings, CancellationToken.None);
            var loaded = await new JsonSettingsStore(path).LoadAsync(CancellationToken.None);

            Assert.AreEqual("Redmi chính", loaded.AndroidDeviceAliases["SERIAL-A"]);
            Assert.AreEqual("Máy phụ", loaded.AndroidDeviceAliases["SERIAL-B"]);
            Assert.AreEqual(2, loaded.AndroidDeviceAliases.Count);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [TestMethod]
    public async Task SchemaEightSettings_UpgradeWithEmptyAliasMapAndPreserveExistingFields()
    {
        var directory = CreateTestDirectory();
        try
        {
            var path = Path.Combine(directory, "settings.json");
            await File.WriteAllTextAsync(path, """
                {
                  "SchemaVersion": 8,
                  "MemucPath": "C:\\MEmu\\memuc.exe",
                  "AdbPath": "C:\\MEmu\\adb.exe",
                  "ApplicationDisplayNames": { "com.example": "Example" },
                  "MultiInstanceRun": { "TargetScriptAssignments": {} }
                }
                """);

            var loaded = await new JsonSettingsStore(path).LoadAsync(CancellationToken.None);

            Assert.AreEqual(ApplicationSettings.CurrentSchemaVersion, loaded.SchemaVersion);
            Assert.AreEqual(@"C:\MEmu\adb.exe", loaded.AdbPath);
            Assert.AreEqual("Example", loaded.ApplicationDisplayNames["com.example"]);
            Assert.AreEqual(0, loaded.AndroidDeviceAliases.Count);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

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
                AdbPath = @"C:\Android\platform-tools\adb.exe",
                MultiInstanceRun = new MultiInstanceRunSettings
                {
                    LaunchSpacingMode = LaunchSpacingMode.Random,
                    FixedSpacingMilliseconds = 250,
                    RandomMinimumSpacingMilliseconds = 500,
                    RandomMaximumSpacingMilliseconds = 1500,
                    StopAllOnInvalidTarget = true,
                    ScriptAssignmentMode = ScriptAssignmentMode.PerInstance,
                    CommonScriptId = Guid.Parse("22222222-2222-2222-2222-222222222222")
                },
                ControlCenterLayout = new ControlCenterLayoutSettings
                {
                    WindowWidth = 1260,
                    WindowHeight = 690,
                    IsMaximized = true,
                    SetupPanelRatio = 0.64,
                    RecentListRatio = 0.42
                }
            };
            settings.MultiInstanceRun.ScriptAssignments[4] = Guid.Parse("11111111-1111-1111-1111-111111111111");
            settings.MultiInstanceRun.TargetScriptAssignments["android-adb:SERIAL-1"] =
                Guid.Parse("33333333-3333-3333-3333-333333333333");
            settings.ApplicationDisplayNames["com.example.app"] = "Ứng dụng mẫu";
            await store.SaveAsync(settings, CancellationToken.None);

            var loaded = await store.LoadAsync(CancellationToken.None);

            Assert.AreEqual(@"C:\MEmu\memuc.exe", loaded.MemucPath);
            Assert.AreEqual(@"C:\Android\platform-tools\adb.exe", loaded.AdbPath);
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
            Assert.AreEqual(
                settings.MultiInstanceRun.TargetScriptAssignments["android-adb:SERIAL-1"],
                loaded.MultiInstanceRun.TargetScriptAssignments["android-adb:SERIAL-1"]);
            Assert.AreEqual(1260d, loaded.ControlCenterLayout.WindowWidth);
            Assert.AreEqual(690d, loaded.ControlCenterLayout.WindowHeight);
            Assert.IsTrue(loaded.ControlCenterLayout.IsMaximized);
            Assert.AreEqual(0.64d, loaded.ControlCenterLayout.SetupPanelRatio);
            Assert.AreEqual(0.42d, loaded.ControlCenterLayout.RecentListRatio);
            Assert.IsNull(loaded.ControlCenterLayout.SetupPanelWidth);
            var json = await File.ReadAllTextAsync(Path.Combine(directory, "settings.json"));
            StringAssert.Contains(json, "\"SetupPanelRatio\"");
            Assert.IsFalse(json.Contains("\"SetupPanelWidth\"", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void ControlCenterLayout_NormalizeClampsFiniteOutOfRangeValues()
    {
        var outOfRange = new ControlCenterLayoutSettings
        {
            WindowWidth = 980,
            WindowHeight = 9000,
            IsMaximized = true,
            SetupPanelRatio = -1,
            RecentListRatio = 2
        };

        var normalized = ControlCenterLayoutSettings.Normalize(outOfRange, 950, 680);

        Assert.AreEqual(950d, normalized.WindowWidth);
        Assert.AreEqual(680d, normalized.WindowHeight);
        Assert.IsTrue(normalized.IsMaximized);
        Assert.AreEqual(0d, normalized.SetupPanelRatio);
        Assert.AreEqual(1d, normalized.RecentListRatio);

        var upperClamped = ControlCenterLayoutSettings.Normalize(new ControlCenterLayoutSettings
        {
            WindowWidth = 950,
            WindowHeight = 680,
            SetupPanelRatio = 980
        }, 950, 680);
        Assert.AreEqual(1d, upperClamped.SetupPanelRatio);
        Assert.AreEqual(
            1d - (ControlCenterLayoutSettings.MinimumRuntimePanelWidth / 900d),
            ControlCenterLayoutSettings.ResolveSetupPanelRatio(upperClamped, 900));
    }

    [TestMethod]
    public void ControlCenterLayout_NormalizeFallsBackOnlyForNonFiniteValues()
    {
        var invalid = new ControlCenterLayoutSettings
        {
            WindowWidth = double.NaN,
            WindowHeight = double.NegativeInfinity,
            SetupPanelRatio = double.PositiveInfinity,
            RecentListRatio = double.NaN
        };

        var normalized = ControlCenterLayoutSettings.Normalize(invalid, 1280, 720);

        Assert.AreEqual(ControlCenterLayoutSettings.DefaultWindowWidth, normalized.WindowWidth);
        Assert.AreEqual(ControlCenterLayoutSettings.DefaultWindowHeight, normalized.WindowHeight);
        Assert.AreEqual(ControlCenterLayoutSettings.DefaultSetupPanelRatio, normalized.SetupPanelRatio);
        Assert.AreEqual(ControlCenterLayoutSettings.DefaultRecentListRatio, normalized.RecentListRatio);
    }

    [TestMethod]
    public void ControlCenterLayout_RatioRoundTripsAcrossNormalAndMaximizedUsableWidths()
    {
        var layout = ControlCenterLayoutSettings.Normalize(new ControlCenterLayoutSettings
        {
            SetupPanelRatio = 0.65
        });

        var normalRatio = ControlCenterLayoutSettings.ResolveSetupPanelRatio(layout, 980);
        var maximizedRatio = ControlCenterLayoutSettings.ResolveSetupPanelRatio(layout, 1680);

        Assert.AreEqual(0.65, normalRatio, 0.001);
        Assert.AreEqual(0.65, maximizedRatio, 0.001);
        Assert.IsTrue(normalRatio * 980 >= ControlCenterLayoutSettings.MinimumSetupPanelWidth);
        Assert.IsTrue((1d - normalRatio) * 980 >= ControlCenterLayoutSettings.MinimumRuntimePanelWidth);
        Assert.IsTrue(maximizedRatio * 1680 >= ControlCenterLayoutSettings.MinimumSetupPanelWidth);
        Assert.IsTrue((1d - maximizedRatio) * 1680 >= ControlCenterLayoutSettings.MinimumRuntimePanelWidth);
    }

    [TestMethod]
    public async Task SchemaSixPixelLayout_LoadsAsLegacyInputForActualWidthMigration()
    {
        var directory = CreateTestDirectory();
        try
        {
            var path = Path.Combine(directory, "settings.json");
            await File.WriteAllTextAsync(path, """
                {
                  "SchemaVersion": 6,
                  "ControlCenterLayout": {
                    "WindowWidth": 1180,
                    "WindowHeight": 680,
                    "SetupPanelWidth": 710
                  }
                }
                """);

            var loaded = await new JsonSettingsStore(path).LoadAsync(CancellationToken.None);

            Assert.AreEqual(ApplicationSettings.CurrentSchemaVersion, loaded.SchemaVersion);
            Assert.IsNull(loaded.ControlCenterLayout.SetupPanelRatio);
            Assert.AreEqual(710d, loaded.ControlCenterLayout.SetupPanelWidth);
            Assert.AreEqual(710d / 1100d,
                ControlCenterLayoutSettings.ResolveSetupPanelRatio(loaded.ControlCenterLayout, 1100), 0.001);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task SchemaSixNullControlCenterLayout_IsRepairedAndCanBeUpdated()
    {
        var directory = CreateTestDirectory();
        try
        {
            var path = Path.Combine(directory, "settings.json");
            await File.WriteAllTextAsync(path, $$"""
                {
                  "SchemaVersion": {{ApplicationSettings.CurrentSchemaVersion}},
                  "ControlCenterLayout": null
                }
                """);
            var store = new JsonSettingsStore(path);

            var loaded = await store.LoadAsync(CancellationToken.None);
            Assert.IsNotNull(loaded.ControlCenterLayout);

            await store.UpdateAsync(settings =>
            {
                settings.ControlCenterLayout ??= new ControlCenterLayoutSettings();
                settings.ControlCenterLayout.SetupPanelWidth = 720;
            }, CancellationToken.None);

            var reopened = await store.LoadAsync(CancellationToken.None);
            Assert.AreEqual(720d, reopened.ControlCenterLayout.SetupPanelWidth);
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
    public async Task CorruptSettings_AreBackedUpThenCanBeSavedAndReloaded()
    {
        var directory = CreateTestDirectory();
        try
        {
            var path = Path.Combine(directory, "settings.json");
            const string corrupt = "{not-json-with-sentinel";
            await File.WriteAllTextAsync(path, corrupt);
            var store = new JsonSettingsStore(path);

            var recovered = await store.LoadAsync(CancellationToken.None);
            Assert.IsNull(recovered.MemucPath);
            Assert.IsNotNull(store.RecoveryNotice);
            var backup = Directory.GetFiles(directory, "settings.json.corrupt-*.bak").Single();
            Assert.AreEqual(corrupt, await File.ReadAllTextAsync(backup));
            Assert.AreEqual(corrupt, await File.ReadAllTextAsync(path));

            recovered.MemucPath = @"C:\Recovered\memuc.exe";
            await store.SaveAsync(recovered, CancellationToken.None);
            var reopened = await new JsonSettingsStore(path).LoadAsync(CancellationToken.None);
            Assert.AreEqual(@"C:\Recovered\memuc.exe", reopened.MemucPath);
            Assert.AreEqual(corrupt, await File.ReadAllTextAsync(backup));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task FutureSettingsSchema_IsRejectedAndCannotOverwriteUnknownData()
    {
        var directory = CreateTestDirectory();
        try
        {
            var path = Path.Combine(directory, "settings.json");
            var original = $$"""
                {
                  "SchemaVersion": {{ApplicationSettings.CurrentSchemaVersion + 1}},
                  "FutureSentinel": "keep-settings",
                  "MemucPath": "C:\\Future\\memuc.exe"
                }
                """;
            await File.WriteAllTextAsync(path, original);
            var store = new JsonSettingsStore(path);

            var loadError = await Assert.ThrowsExceptionAsync<InvalidDataException>(
                () => store.LoadAsync(CancellationToken.None));
            StringAssert.Contains(loadError.Message, "mới hơn");
            var directWriter = new JsonSettingsStore(path);
            await Assert.ThrowsExceptionAsync<InvalidDataException>(() =>
                directWriter.SaveAsync(new ApplicationSettings { MemucPath = @"C:\Direct\memuc.exe" }, CancellationToken.None));
            await Assert.ThrowsExceptionAsync<InvalidDataException>(() =>
                store.SaveAsync(new ApplicationSettings { MemucPath = @"C:\New\memuc.exe" }, CancellationToken.None));

            Assert.AreEqual(original, await File.ReadAllTextAsync(path));
            StringAssert.Contains(await File.ReadAllTextAsync(path), "keep-settings");
            Assert.AreEqual(0, Directory.GetFiles(directory, "settings.json.corrupt-*.bak").Length,
                "Future data is unsupported, not corrupt, and must not be quarantined as corruption.");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [TestMethod]
    public async Task DirectSave_SemanticallyCorruptCurrentSettingsAreBackedUpBeforeReplacement()
    {
        var directory = CreateTestDirectory();
        try
        {
            var path = Path.Combine(directory, "settings.json");
            var corrupt = $$"""
                {
                  "SchemaVersion": {{ApplicationSettings.CurrentSchemaVersion}},
                  "ApplicationDisplayNames": {},
                  "MultiInstanceRun": null,
                  "SemanticSentinel": "keep-in-backup"
                }
                """;
            await File.WriteAllTextAsync(path, corrupt);
            var store = new JsonSettingsStore(path);

            await store.SaveAsync(
                new ApplicationSettings { MemucPath = @"C:\Recovered\memuc.exe" },
                CancellationToken.None);

            var backup = Directory.GetFiles(directory, "settings.json.corrupt-*.bak").Single();
            Assert.AreEqual(corrupt, await File.ReadAllTextAsync(backup));
            StringAssert.Contains(await File.ReadAllTextAsync(backup), "keep-in-backup");
            var reopened = await new JsonSettingsStore(path).LoadAsync(CancellationToken.None);
            Assert.AreEqual(@"C:\Recovered\memuc.exe", reopened.MemucPath);
        }
        finally { Directory.Delete(directory, recursive: true); }
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
