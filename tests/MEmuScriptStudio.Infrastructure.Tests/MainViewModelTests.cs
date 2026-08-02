using System.Text.Json;
using MEmuScriptStudio.App.Services;
using MEmuScriptStudio.App.ViewModels;
using MEmuScriptStudio.Core.MEmu;
using MEmuScriptStudio.Core.Models;

namespace MEmuScriptStudio.Infrastructure.Tests;

[TestClass]
public sealed class MainViewModelTests
{
    [TestMethod]
    public async Task InitializeAsync_CorruptSettingsKeepsViewModelUsable()
    {
        var viewModel = CreateViewModel(new ThrowingSettingsStore(failLoad: true, failSave: false));

        await viewModel.InitializeAsync(CancellationToken.None);

        Assert.IsTrue(viewModel.IsPathValid);
        StringAssert.Contains(viewModel.StatusMessage, "Không thể đọc cấu hình đã lưu");
    }

    [TestMethod]
    public async Task InitializeAsync_AutoSaveFailureShowsRecoveryMessage()
    {
        var viewModel = CreateViewModel(new ThrowingSettingsStore(failLoad: false, failSave: true));

        await viewModel.InitializeAsync(CancellationToken.None);

        Assert.IsTrue(viewModel.IsPathValid);
        StringAssert.Contains(viewModel.StatusMessage, "Không thể lưu đường dẫn tự tìm thấy");
    }

    [TestMethod]
    public async Task BrowseCommand_SaveFailureKeepsSelectedPathForCurrentSession()
    {
        var store = new ThrowingSettingsStore(failLoad: false, failSave: true);
        var viewModel = CreateViewModel(store);

        await viewModel.BrowseCommand.ExecuteAsync();

        Assert.AreEqual(@"C:\Selected\memuc.exe", viewModel.MemucPath);
        StringAssert.Contains(viewModel.StatusMessage, "Có thể dùng đường dẫn trong phiên này");
    }

    private static MainViewModel CreateViewModel(ISettingsStore settingsStore) => new(
        new EmptyInstanceService(),
        new ValidPathDiscovery(),
        settingsStore,
        new SelectedFileDialog());

    private sealed class EmptyInstanceService : IMemuInstanceService
    {
        public Task<IReadOnlyList<MemuInstance>> GetInstancesAsync(string memucPath, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<MemuInstance>>([]);
    }

    private sealed class ValidPathDiscovery : IMemucPathDiscovery
    {
        public string FindMemucPath() => @"C:\Discovered\memuc.exe";
        public bool IsValidMemucPath(string? path) => !string.IsNullOrWhiteSpace(path);
    }

    private sealed class SelectedFileDialog : IFileDialogService
    {
        public string SelectMemucPath(string? currentPath) => @"C:\Selected\memuc.exe";
    }

    private sealed class ThrowingSettingsStore(bool failLoad, bool failSave) : ISettingsStore
    {
        public Task<ApplicationSettings> LoadAsync(CancellationToken cancellationToken) =>
            failLoad
                ? Task.FromException<ApplicationSettings>(new JsonException("corrupt"))
                : Task.FromResult(new ApplicationSettings());

        public Task SaveAsync(ApplicationSettings settings, CancellationToken cancellationToken) =>
            failSave ? Task.FromException(new IOException("read-only")) : Task.CompletedTask;
    }
}
