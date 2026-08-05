using System.Text.Json;
using MEmuScriptStudio.Core.Models;
using MEmuScriptStudio.Infrastructure.Persistence;

namespace MEmuScriptStudio.Infrastructure.Tests;

[TestClass]
public sealed class CompositePersistenceAndUiTests
{
    [TestMethod]
    public async Task ScriptStoreRoundTripsCompositeAndAllNewStepDiscriminators()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"memu-composite-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "scripts.json");
            var regular = new ScriptDefinition
            {
                Name = "Safe steps",
                Steps =
                [
                    new CloseChromeTabsStep { Name = "Chrome" }
                ]
            };
            var composite = new ScriptDefinition
            {
                Name = "Composite",
                Kind = ScriptKind.Composite,
                CompositeItems =
                [
                    new ScriptReferenceItem { ScriptId = regular.Id, ContinueOnFailure = true },
                    new CompositeDelayItem { DurationMilliseconds = 1234 }
                ]
            };
            using var store = new JsonScriptStore(path);
            await store.SaveAsync([regular, composite], CancellationToken.None);
            var loaded = await store.LoadAsync(CancellationToken.None);

            Assert.AreEqual(2, loaded.Count);
            var loadedRegular = loaded.Single(script => script.Kind == ScriptKind.Regular);
            Assert.IsInstanceOfType<CloseChromeTabsStep>(loadedRegular.Steps[0]);
            var loadedComposite = loaded.Single(script => script.Kind == ScriptKind.Composite);
            Assert.AreEqual(loadedRegular.Id, ((ScriptReferenceItem)loadedComposite.CompositeItems[0]).ScriptId);
            Assert.IsTrue(((ScriptReferenceItem)loadedComposite.CompositeItems[0]).ContinueOnFailure);
            Assert.AreEqual(1234, ((CompositeDelayItem)loadedComposite.CompositeItems[1]).DurationMilliseconds);

            var json = await File.ReadAllTextAsync(path);
            StringAssert.Contains(json, "\"$type\": \"closeChromeTabs\"");
            StringAssert.Contains(json, "\"$type\": \"scriptReference\"");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task TransferRejectsPartialCompositeBundleBeforeReturningScripts()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"memu-transfer-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "invalid.memuscript");
            var document = new JsonScriptTransferService.ScriptTransferDocument
            {
                Scripts =
                [
                    new ScriptDefinition
                    {
                        Name = "Broken",
                        Kind = ScriptKind.Composite,
                        CompositeItems = [new ScriptReferenceItem { ScriptId = Guid.NewGuid() }]
                    }
                ]
            };
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(document));
            var service = new JsonScriptTransferService();
            await Assert.ThrowsExceptionAsync<InvalidDataException>(() => service.ImportAsync(path, CancellationToken.None));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void MainAndControlCenterSurfacesExposeCompositeFilterEditorAndScriptType()
    {
        var root = FindRepositoryRoot();
        var main = File.ReadAllText(Path.Combine(root, "src", "MEmuScriptStudio.App", "MainWindow.xaml"));
        var run = File.ReadAllText(Path.Combine(root, "src", "MEmuScriptStudio.App", "Views", "RunControlPanel.xaml"));

        StringAssert.Contains(main, "ScriptLibraryFilters");
        StringAssert.Contains(main, "CreateCompositeScriptCommand");
        StringAssert.Contains(main, "CompositeItemsGrid");
        StringAssert.Contains(main, "AddCompositeReferenceCommand");
        StringAssert.Contains(main, "AddCompositeDelayCommand");
        StringAssert.Contains(main, "CompositeContinueOnFailure");
        StringAssert.Contains(run, "DisplayNameWithKind");
        StringAssert.Contains(run, "AssignedScriptDisplay");
        Assert.IsFalse(main.Contains("<ScrollViewer", StringComparison.Ordinal) &&
            main.Contains("x:Name=\"CompositeItemsGrid\"", StringComparison.Ordinal) &&
            main.IndexOf("<ScrollViewer", StringComparison.Ordinal) < main.IndexOf("x:Name=\"CompositeItemsGrid\"", StringComparison.Ordinal) &&
            main.IndexOf("</ScrollViewer>", main.IndexOf("<ScrollViewer", StringComparison.Ordinal), StringComparison.Ordinal) >
            main.IndexOf("x:Name=\"CompositeItemsGrid\"", StringComparison.Ordinal));
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "MEmuScriptStudio.sln")))
            current = current.Parent;
        return current?.FullName ?? throw new DirectoryNotFoundException("Không tìm thấy repository root.");
    }
}
