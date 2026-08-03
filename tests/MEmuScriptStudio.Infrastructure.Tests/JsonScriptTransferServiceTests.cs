using System.Text.Json;
using MEmuScriptStudio.Core.Models;
using MEmuScriptStudio.Infrastructure.Persistence;

namespace MEmuScriptStudio.Infrastructure.Tests;

[TestClass]
public sealed class JsonScriptTransferServiceTests
{
    [TestMethod]
    public async Task ExportAndImport_RoundTripsIdsAndScrubsSecretValues()
    {
        var directory = CreateTestDirectory();
        try
        {
            var path = Path.Combine(directory, "library.memuscript");
            var script = new ScriptDefinition
            {
                Name = "Transfer",
                Variables =
                [
                    new ScriptVariable { Name = "public", Value = "visible", IsSecret = false },
                    new ScriptVariable { Name = "password", Value = "must-not-leak", IsSecret = true }
                ],
                Steps = [new AndroidClipboardPasteStep { Name = "Paste", PressEnterAfterPaste = true }]
            };
            var service = new JsonScriptTransferService();

            await service.ExportAsync(path, [script], CancellationToken.None);
            var rawJson = await File.ReadAllTextAsync(path);
            var imported = await service.ImportAsync(path, CancellationToken.None);

            StringAssert.Contains(rawJson, "MEmuScriptStudio.ScriptTransfer");
            StringAssert.Contains(rawJson, "visible");
            Assert.IsFalse(rawJson.Contains("must-not-leak", StringComparison.Ordinal));
            Assert.AreEqual(script.Id, imported.Single().Id);
            Assert.AreEqual(script.Steps[0].Id, imported.Single().Steps[0].Id);
            Assert.IsNull(imported.Single().Variables.Single(variable => variable.IsSecret).Value);
            Assert.AreEqual("must-not-leak", script.Variables.Single(variable => variable.IsSecret).Value,
                "Export must not mutate the in-memory library.");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [TestMethod]
    public async Task Import_RejectsUnsupportedSchemaBeforeReturningScripts()
    {
        var directory = CreateTestDirectory();
        try
        {
            var path = Path.Combine(directory, "future.memuscript");
            await File.WriteAllTextAsync(path,
                """
                { "schemaVersion": 99, "format": "MEmuScriptStudio.ScriptTransfer", "scripts": [] }
                """);

            await Assert.ThrowsExceptionAsync<InvalidDataException>(() =>
                new JsonScriptTransferService().ImportAsync(path, CancellationToken.None));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [TestMethod]
    public async Task Import_ScrubsSecretValuesFromEditedFiles()
    {
        var directory = CreateTestDirectory();
        try
        {
            var path = Path.Combine(directory, "edited.memuscript");
            var script = new ScriptDefinition
            {
                Name = "Edited",
                Variables = [new ScriptVariable { Name = "password", IsSecret = true }],
                Steps = [new NoteStep { Name = "Note", Text = "Safe" }]
            };
            var service = new JsonScriptTransferService();
            await service.ExportAsync(path, [script], CancellationToken.None);
            var json = await File.ReadAllTextAsync(path);
            await File.WriteAllTextAsync(path, json.Replace("\"Value\": null", "\"Value\": \"injected-secret\"", StringComparison.Ordinal));

            var imported = await service.ImportAsync(path, CancellationToken.None);

            Assert.IsNull(imported.Single().Variables.Single().Value);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static string CreateTestDirectory()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "TransferTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}
