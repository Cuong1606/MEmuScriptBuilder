using System.Text.Json;
using MEmuScriptStudio.App.Services;
using MEmuScriptStudio.App.ViewModels;
using MEmuScriptStudio.Core.Execution;
using MEmuScriptStudio.Core.MEmu;
using MEmuScriptStudio.Core.Models;
using MEmuScriptStudio.Core.Scripts;

namespace MEmuScriptStudio.Infrastructure.Tests;

[TestClass]
public sealed class MainViewModelTests
{
    [TestMethod]
    public async Task InitializeAsync_CorruptSettingsKeepsViewModelUsable()
    {
        var logger = new RecordingStartupIssueLogger();
        var viewModel = CreateViewModel(new ThrowingSettingsStore(failLoad: true, failSave: false), startupIssueLogger: logger);

        await viewModel.InitializeAsync(CancellationToken.None);

        Assert.IsTrue(viewModel.IsPathValid);
        Assert.IsTrue(viewModel.CanUseApplication);
        Assert.IsTrue(logger.Exceptions.Count >= 1);
        StringAssert.Contains(viewModel.StatusMessage, "Không thể đọc cấu hình đã lưu");
    }

    [TestMethod]
    public async Task InitializeAsync_AutoSaveFailureShowsRecoveryMessage()
    {
        var viewModel = CreateViewModel(new ThrowingSettingsStore(failLoad: false, failSave: true));

        await viewModel.InitializeAsync(CancellationToken.None);

        Assert.IsTrue(viewModel.IsPathValid);
        StringAssert.Contains(viewModel.StatusMessage, "Không thể lưu đường dẫn");
    }

    [TestMethod]
    public async Task InitializeAsync_ExposesLoadingStateAndDisablesCommandsUntilReady()
    {
        var scripts = new BlockingScriptStore();
        var viewModel = CreateViewModel(new ThrowingSettingsStore(failLoad: false, failSave: false), scripts);

        Assert.IsTrue(viewModel.IsInitializing);
        Assert.IsFalse(viewModel.CanUseApplication);
        Assert.AreEqual("Đang khởi tạo…", viewModel.StatusMessage);
        Assert.IsFalse(viewModel.BrowseCommand.CanExecute(null));

        var initialization = viewModel.InitializeAsync(CancellationToken.None);
        await scripts.LoadStarted.Task;
        Assert.IsTrue(viewModel.IsInitializing);
        Assert.IsFalse(viewModel.CanUseApplication);

        scripts.ReleaseLoad.SetResult();
        await initialization;

        Assert.IsFalse(viewModel.IsInitializing);
        Assert.IsTrue(viewModel.CanUseApplication);
        Assert.IsFalse(viewModel.IsStartupOverlayVisible);
        Assert.IsTrue(viewModel.BrowseCommand.CanExecute(null));
    }

    [TestMethod]
    public void ReportInitializationError_KeepsWorkspaceDisabledAndExposesRecoveryMessage()
    {
        var viewModel = CreateViewModel(new ThrowingSettingsStore(failLoad: false, failSave: false));

        viewModel.ReportInitializationError(new InvalidOperationException("broken startup"), @"C:\logs\startup-error.log");

        Assert.IsFalse(viewModel.IsInitializing);
        Assert.IsTrue(viewModel.HasInitializationError);
        Assert.IsTrue(viewModel.IsStartupOverlayVisible);
        Assert.IsFalse(viewModel.CanUseApplication);
        StringAssert.Contains(viewModel.StatusMessage, "broken startup");
        StringAssert.Contains(viewModel.StatusMessage, "startup-error.log");
    }

    [TestMethod]
    public async Task BrowseCommand_SaveFailureKeepsSelectedPathForCurrentSession()
    {
        var store = new ThrowingSettingsStore(failLoad: false, failSave: true);
        var viewModel = CreateViewModel(store);
        await viewModel.InitializeAsync(CancellationToken.None);

        await viewModel.BrowseCommand.ExecuteAsync();

        Assert.AreEqual(@"C:\Selected\memuc.exe", viewModel.MemucPath);
        StringAssert.Contains(viewModel.StatusMessage, "Có thể dùng đường dẫn trong phiên này");
    }

    private static MainViewModel CreateViewModel(
        ISettingsStore settingsStore,
        IScriptStore? scriptStore = null,
        IStartupIssueLogger? startupIssueLogger = null)
    {
        var instances = new EmptyInstanceService();
        var scheduler = new MultiInstanceExecutionScheduler(
            instances,
            new NoopExecutionEngine(),
            new NoopLaunchDelay(),
            new NoopLaunchRandom());
        return new MainViewModel(
            instances,
            new ValidPathDiscovery(),
            settingsStore,
            new SelectedFileDialog(),
            scriptStore ?? new MemoryScriptStore(),
            scheduler,
            new ScriptStepCommandBuilder(new MemuCommandBuilder()),
            new AlwaysConfirm(),
            new NoopApplicationPicker(),
            new NoopInputCapture(),
            new NoopTapOverlay(),
            new NoopSwipeOverlay(),
            startupIssueLogger: startupIssueLogger);
    }

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
        public string? SelectScriptImportPath() => null;
        public string? SelectScriptExportPath(string suggestedFileName) => null;
        public string? SelectApplicationNameImportPath() => null;
        public string? SelectApplicationNameExportPath(string suggestedFileName) => null;
    }

    private sealed class AlwaysConfirm : IConfirmationService
    {
        public bool Confirm(string message, string title) => true;
    }

    private sealed class NoopApplicationPicker : IApplicationPickerService
    {
        public Task<MemuApplicationInfo?> SelectAsync(string memucPath, int instanceIndex, CancellationToken cancellationToken) =>
            Task.FromResult<MemuApplicationInfo?>(null);
    }

    private sealed class NoopInputCapture : IMemuInputCaptureService
    {
        public Task<CapturedTap> CaptureTapAsync(string memucPath, MemuInstance instance, IProgress<TapCaptureUpdate>? progress, CancellationToken cancellationToken) =>
            Task.FromResult(new CapturedTap(0, 0));
        public Task<CapturedSwipe> CaptureSwipeAsync(string memucPath, MemuInstance instance, IProgress<SwipeCaptureUpdate>? progress, CancellationToken cancellationToken) =>
            Task.FromResult(new CapturedSwipe(0, 0, 0, 0));
    }

    private sealed class NoopTapOverlay : ITapCaptureOverlayService
    {
        public ITapCaptureOverlaySession Show() => new Session();
        private sealed class Session : ITapCaptureOverlaySession
        {
            public void Report(TapCaptureUpdate value) { }
            public void Dispose() { }
        }
    }

    private sealed class NoopSwipeOverlay : ISwipeCaptureOverlayService
    {
        public ISwipeCaptureOverlaySession Show() => new Session();
        private sealed class Session : ISwipeCaptureOverlaySession
        {
            public void Report(SwipeCaptureUpdate value) { }
            public void Dispose() { }
        }
    }

    private sealed class MemoryScriptStore : IScriptStore
    {
        public Task<IReadOnlyList<ScriptDefinition>> LoadAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ScriptDefinition>>([]);
        public Task SaveAsync(IReadOnlyCollection<ScriptDefinition> scripts, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class BlockingScriptStore : IScriptStore
    {
        public TaskCompletionSource LoadStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseLoad { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<IReadOnlyList<ScriptDefinition>> LoadAsync(CancellationToken cancellationToken)
        {
            LoadStarted.SetResult();
            await ReleaseLoad.Task.WaitAsync(cancellationToken);
            return [];
        }

        public Task SaveAsync(IReadOnlyCollection<ScriptDefinition> scripts, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class RecordingStartupIssueLogger : IStartupIssueLogger
    {
        public List<Exception> Exceptions { get; } = [];
        public void Report(Exception exception) => Exceptions.Add(exception);
    }

    private sealed class NoopExecutionEngine : IScriptExecutionEngine
    {
        public Task<ExecutionResult> ExecuteAsync(ExecutionRequest request, IProgress<StepExecutionUpdate>? progress, CancellationToken cancellationToken) =>
            Task.FromResult(new ExecutionResult { StartedAt = DateTimeOffset.UtcNow, EndedAt = DateTimeOffset.UtcNow });
    }

    private sealed class NoopLaunchDelay : ILaunchDelayProvider
    {
        public Task DelayAsync(TimeSpan duration, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class NoopLaunchRandom : ILaunchSpacingRandom
    {
        public int NextInclusive(int minimumMilliseconds, int maximumMilliseconds) => minimumMilliseconds;
    }

    private sealed class ThrowingSettingsStore(bool failLoad, bool failSave) : ISettingsStore
    {
        public Task<ApplicationSettings> LoadAsync(CancellationToken cancellationToken) =>
            failLoad
                ? Task.FromException<ApplicationSettings>(new JsonException("corrupt"))
                : Task.FromResult(new ApplicationSettings());

        public Task SaveAsync(ApplicationSettings settings, CancellationToken cancellationToken) =>
            failSave ? Task.FromException(new IOException("read-only")) : Task.CompletedTask;

        public async Task<ApplicationSettings> UpdateAsync(
            Action<ApplicationSettings> update,
            CancellationToken cancellationToken)
        {
            var settings = await LoadAsync(cancellationToken);
            update(settings);
            await SaveAsync(settings, cancellationToken);
            return settings;
        }
    }
}
