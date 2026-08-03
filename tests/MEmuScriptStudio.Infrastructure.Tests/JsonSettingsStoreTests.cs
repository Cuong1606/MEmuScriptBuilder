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
            var settings = new ApplicationSettings { MemucPath = @"C:\MEmu\memuc.exe" };
            settings.ApplicationDisplayNames["com.example.app"] = "Ứng dụng mẫu";
            await store.SaveAsync(settings, CancellationToken.None);

            var loaded = await store.LoadAsync(CancellationToken.None);

            Assert.AreEqual(@"C:\MEmu\memuc.exe", loaded.MemucPath);
            Assert.AreEqual(1, loaded.SchemaVersion);
            Assert.AreEqual("Ứng dụng mẫu", loaded.ApplicationDisplayNames["com.example.app"]);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
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
