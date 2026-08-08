using MEmuScriptStudio.Core.Android;
using MEmuScriptStudio.Infrastructure.Persistence;

namespace MEmuScriptStudio.Infrastructure.Tests;

[TestClass]
public sealed class JsonAndroidApplicationLibraryTransferServiceTests
{
    [TestMethod]
    public async Task ExportAndImport_RoundTripsProviderComponentsAndStableOrder()
    {
        var directory = CreateTestDirectory();
        try
        {
            var path = Path.Combine(directory, "library.androidappnames");
            var service = new JsonAndroidApplicationLibraryTransferService();
            AndroidApplicationLibraryEntry[] entries =
            [
                new("com.example.z", ".Home", "Zed"),
                new("com.example.a", "com.example.a.Main", "Alpha")
            ];

            await service.ExportAsync(path, entries, CancellationToken.None);
            var json = await File.ReadAllTextAsync(path);
            var imported = await service.ImportAsync(path, CancellationToken.None);

            StringAssert.Contains(json, "MEmuScriptStudio.AndroidApplicationLibrary");
            StringAssert.Contains(json, "\"Provider\": \"AndroidAdb\"");
            Assert.IsTrue(json.IndexOf("com.example.a", StringComparison.Ordinal) <
                json.IndexOf("com.example.z", StringComparison.Ordinal));
            CollectionAssert.AreEqual(entries.OrderBy(entry => entry.PackageName).ToList(), imported.ToList());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [DataTestMethod]
    [DataRow("Wrong", 1, "AndroidAdb")]
    [DataRow("MEmuScriptStudio.AndroidApplicationLibrary", 99, "AndroidAdb")]
    [DataRow("MEmuScriptStudio.AndroidApplicationLibrary", 1, "MEmu")]
    public async Task Import_RejectsWrongFormatVersionOrProvider(
        string format,
        int schemaVersion,
        string provider)
    {
        var directory = CreateTestDirectory();
        try
        {
            var path = Path.Combine(directory, "invalid.androidappnames");
            await File.WriteAllTextAsync(path, $$"""
                {
                  "SchemaVersion": {{schemaVersion}},
                  "Format": "{{format}}",
                  "Provider": "{{provider}}",
                  "Applications": [
                    { "PackageName": "com.example.app", "ActivityName": ".Main", "FriendlyName": "Example" }
                  ]
                }
                """);

            await Assert.ThrowsExceptionAsync<InvalidDataException>(() =>
                new JsonAndroidApplicationLibraryTransferService().ImportAsync(path, CancellationToken.None));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task Import_RejectsDuplicatePackageBeforeMutation()
    {
        var directory = CreateTestDirectory();
        try
        {
            var path = Path.Combine(directory, "duplicate.androidappnames");
            await File.WriteAllTextAsync(path, """
                {
                  "SchemaVersion": 1,
                  "Format": "MEmuScriptStudio.AndroidApplicationLibrary",
                  "Provider": "AndroidAdb",
                  "Applications": [
                    { "PackageName": "com.example.app", "ActivityName": ".One", "FriendlyName": "One" },
                    { "PackageName": "com.example.app", "ActivityName": ".Two", "FriendlyName": "Two" }
                  ]
                }
                """);

            await Assert.ThrowsExceptionAsync<InvalidDataException>(() =>
                new JsonAndroidApplicationLibraryTransferService().ImportAsync(path, CancellationToken.None));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [DataTestMethod]
    [DataRow("{ \"Applications\": [{ \"PackageName\": \"com.example.app\", \"ActivityName\": \".Main\", \"FriendlyName\": \"Example\" }] }")]
    [DataRow("{ \"SchemaVersion\": 1, \"Provider\": \"AndroidAdb\", \"Applications\": [{ \"PackageName\": \"com.example.app\", \"ActivityName\": \".Main\", \"FriendlyName\": \"Example\" }] }")]
    [DataRow("{ \"SchemaVersion\": 1, \"Format\": \"MEmuScriptStudio.AndroidApplicationLibrary\", \"Applications\": [{ \"PackageName\": \"com.example.app\", \"ActivityName\": \".Main\", \"FriendlyName\": \"Example\" }] }")]
    public async Task Import_RejectsMissingRequiredHeaders(string json)
    {
        var directory = CreateTestDirectory();
        try
        {
            var path = Path.Combine(directory, "missing-header.androidappnames");
            await File.WriteAllTextAsync(path, json);

            await Assert.ThrowsExceptionAsync<InvalidDataException>(() =>
                new JsonAndroidApplicationLibraryTransferService().ImportAsync(path, CancellationToken.None));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task Import_RejectsEntryWithoutActivityField()
    {
        var directory = CreateTestDirectory();
        try
        {
            var path = Path.Combine(directory, "missing-activity.androidappnames");
            await File.WriteAllTextAsync(path, """
                {
                  "SchemaVersion": 1,
                  "Format": "MEmuScriptStudio.AndroidApplicationLibrary",
                  "Provider": "AndroidAdb",
                  "Applications": [
                    { "PackageName": "com.example.app", "FriendlyName": "Example" }
                  ]
                }
                """);

            await Assert.ThrowsExceptionAsync<InvalidDataException>(() =>
                new JsonAndroidApplicationLibraryTransferService().ImportAsync(path, CancellationToken.None));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string CreateTestDirectory()
    {
        var directory = Path.Combine(
            AppContext.BaseDirectory,
            "AndroidApplicationLibraryTransferTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}
