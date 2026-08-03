using MEmuScriptStudio.Infrastructure.Persistence;

namespace MEmuScriptStudio.Infrastructure.Tests;

[TestClass]
public sealed class JsonApplicationNameTransferServiceTests
{
    [TestMethod]
    public async Task ExportAndImport_RoundTripsSchemaAndSortedNames()
    {
        var directory = CreateTestDirectory();
        try
        {
            var path = Path.Combine(directory, "library.memuappnames");
            var service = new JsonApplicationNameTransferService();
            var names = new Dictionary<string, string>
            {
                ["com.example.z"] = "Tên Z",
                ["com.example.a"] = "Tên A"
            };

            await service.ExportAsync(path, names, CancellationToken.None);
            var json = await File.ReadAllTextAsync(path);
            var imported = await service.ImportAsync(path, CancellationToken.None);

            StringAssert.Contains(json, "MEmuScriptStudio.ApplicationNames");
            Assert.IsTrue(json.IndexOf("com.example.a", StringComparison.Ordinal) <
                json.IndexOf("com.example.z", StringComparison.Ordinal));
            CollectionAssert.AreEquivalent(names.ToList(), imported.ToList());
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [DataTestMethod]
    [DataRow("{ \"schemaVersion\": 99, \"format\": \"MEmuScriptStudio.ApplicationNames\", \"names\": [] }")]
    [DataRow("{ \"schemaVersion\": 1, \"format\": \"Wrong\", \"names\": [] }")]
    [DataRow("{ \"schemaVersion\": 1, \"format\": \"MEmuScriptStudio.ApplicationNames\", \"names\": [] }")]
    [DataRow("{ \"schemaVersion\": 1, \"format\": \"MEmuScriptStudio.ApplicationNames\", \"names\": null }")]
    public async Task Import_RejectsUnsupportedOrEmptyDocument(string json)
    {
        var directory = CreateTestDirectory();
        try
        {
            var path = Path.Combine(directory, "invalid.memuappnames");
            await File.WriteAllTextAsync(path, json);
            await Assert.ThrowsExceptionAsync<InvalidDataException>(() =>
                new JsonApplicationNameTransferService().ImportAsync(path, CancellationToken.None));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [TestMethod]
    public async Task Import_RejectsDuplicatePackageBeforeReturningNames()
    {
        var directory = CreateTestDirectory();
        try
        {
            var path = Path.Combine(directory, "duplicate.memuappnames");
            await File.WriteAllTextAsync(path,
                """
                {
                  "schemaVersion": 1,
                  "format": "MEmuScriptStudio.ApplicationNames",
                  "names": [
                    { "packageName": "com.example.app", "displayName": "Một" },
                    { "packageName": "com.example.app", "displayName": "Hai" }
                  ]
                }
                """);

            await Assert.ThrowsExceptionAsync<InvalidDataException>(() =>
                new JsonApplicationNameTransferService().ImportAsync(path, CancellationToken.None));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [TestMethod]
    public async Task Transfer_RequiresMemuAppNamesExtension()
    {
        var service = new JsonApplicationNameTransferService();
        await Assert.ThrowsExceptionAsync<ArgumentException>(() =>
            service.ExportAsync("library.json", new Dictionary<string, string> { ["com.example"] = "Example" }, CancellationToken.None));
        await Assert.ThrowsExceptionAsync<ArgumentException>(() =>
            service.ImportAsync("library.json", CancellationToken.None));
    }

    private static string CreateTestDirectory()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "ApplicationNameTransferTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}
