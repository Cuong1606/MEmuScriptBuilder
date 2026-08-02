using MEmuScriptStudio.App.Services;
using MEmuScriptStudio.App.ViewModels;
using MEmuScriptStudio.Core.Execution;
using MEmuScriptStudio.Core.MEmu;
using MEmuScriptStudio.Core.Models;
using MEmuScriptStudio.Core.Scripts;

namespace MEmuScriptStudio.Infrastructure.Tests;

[TestClass]
public sealed class MainViewModelMvpTests
{
    [TestMethod]
    public async Task ScriptCommands_CreateRenameDuplicateDeleteAndAutosave()
    {
        var store = new RecordingScriptStore();
        var viewModel = CreateViewModel(store, new ImmediateEngine());
        await viewModel.InitializeAsync(CancellationToken.None);
        var initialSaveCount = store.SaveCount;

        await viewModel.CreateScriptCommand.ExecuteAsync();
        viewModel.ScriptName = "Automation";
        await viewModel.RenameScriptCommand.ExecuteAsync();
        var sourceId = viewModel.SelectedScript!.Id;
        await viewModel.DuplicateScriptCommand.ExecuteAsync();
        var cloneId = viewModel.SelectedScript!.Id;
        await viewModel.DeleteScriptCommand.ExecuteAsync();

        Assert.AreNotEqual(sourceId, cloneId);
        Assert.IsTrue(store.SaveCount >= initialSaveCount + 4);
        Assert.IsTrue(store.LastSaved.Any(script => script.Name == "Automation"));
        Assert.AreEqual(2, viewModel.Scripts.Count);
    }

    [TestMethod]
    public async Task StepCommands_AddEditDuplicateMoveDeleteAndAutosave()
    {
        var store = new RecordingScriptStore();
        var viewModel = CreateViewModel(store, new ImmediateEngine());
        await viewModel.InitializeAsync(CancellationToken.None);
        var originalCount = viewModel.Steps.Count;

        viewModel.NewStepCommand.Execute(null);
        viewModel.EditorKind = ScriptStepKind.Tap;
        viewModel.EditorName = "Tap login";
        viewModel.EditorX = 100;
        viewModel.EditorY = 200;
        viewModel.EditorIsEnabled = false;
        viewModel.EditorContinueOnError = true;
        await viewModel.SaveStepCommand.ExecuteAsync();
        var originalId = viewModel.SelectedStep!.Id;
        await viewModel.DuplicateStepCommand.ExecuteAsync();
        var cloneId = viewModel.SelectedStep!.Id;
        await viewModel.MoveStepUpCommand.ExecuteAsync();
        viewModel.EditorName = "Tap edited";
        await viewModel.SaveStepCommand.ExecuteAsync();
        await viewModel.DeleteStepCommand.ExecuteAsync();

        Assert.AreNotEqual(originalId, cloneId);
        Assert.AreEqual(originalCount + 1, viewModel.Steps.Count);
        var original = viewModel.Steps.Single(item => item.Id == originalId).Model;
        Assert.IsFalse(original.IsEnabled);
        Assert.IsTrue(original.ContinueOnError);
        Assert.IsTrue(store.SaveCount >= 5);
    }

    [TestMethod]
    public async Task RunCommand_UsesExactlySelectedInstance()
    {
        var engine = new ImmediateEngine();
        var viewModel = CreateViewModel(new RecordingScriptStore(), engine);
        await viewModel.InitializeAsync(CancellationToken.None);
        viewModel.Instances.Add(new MemuInstance(8, "Selected", true, 123));
        viewModel.SelectedInstance = viewModel.Instances[0];

        await viewModel.RunCommand.ExecuteAsync();

        Assert.IsNotNull(engine.LastRequest);
        Assert.AreEqual(8, engine.LastRequest.InstanceIndex);
        Assert.AreEqual(viewModel.SelectedScript!.Id, engine.LastRequest.Script.Id);
    }

    [TestMethod]
    public async Task StopCommand_CancelsRunningExecution()
    {
        var engine = new BlockingEngine();
        var viewModel = CreateViewModel(new RecordingScriptStore(), engine);
        await viewModel.InitializeAsync(CancellationToken.None);
        viewModel.Instances.Add(new MemuInstance(2, "Target", true, 456));
        viewModel.SelectedInstance = viewModel.Instances[0];

        var runTask = viewModel.RunCommand.ExecuteAsync();
        await engine.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        viewModel.StopCommand.Execute(null);
        await runTask.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.IsTrue(engine.WasCancelled);
        Assert.IsFalse(viewModel.IsExecuting);
    }

    [TestMethod]
    public async Task RunCommand_RawShellDeclined_DoesNotInvokeEngine()
    {
        var engine = new ImmediateEngine();
        var rawScript = new ScriptDefinition { Name = "Raw", Steps = { new AndroidShellStep { Name = "Raw", Command = "echo ok" } } };
        var store = new RecordingScriptStore([rawScript]);
        var viewModel = CreateViewModel(store, engine, new ConfigurableConfirmation(false));
        await viewModel.InitializeAsync(CancellationToken.None);
        viewModel.Instances.Add(new MemuInstance(3, "Target", true, 1));
        viewModel.SelectedInstance = viewModel.Instances[0];

        await viewModel.RunCommand.ExecuteAsync();

        Assert.IsNull(engine.LastRequest);
        StringAssert.Contains(viewModel.StatusMessage, "chưa được xác nhận");
    }

    [TestMethod]
    public async Task InitializeAsync_TemplateSaveFails_TemplateRemainsSelectedAndUsable()
    {
        var store = new RecordingScriptStore { ThrowOnSave = true };
        var viewModel = CreateViewModel(store, new ImmediateEngine());

        await viewModel.InitializeAsync(CancellationToken.None);

        Assert.IsNotNull(viewModel.SelectedScript);
        Assert.AreEqual("Khởi động lại Chrome", viewModel.SelectedScript.Name);
        Assert.AreEqual(3, viewModel.Steps.Count);
        StringAssert.Contains(viewModel.StatusMessage, "không thể lưu");
    }

    [TestMethod]
    public async Task SelectionCannotChangeWhileExecutionIsRunning()
    {
        var engine = new BlockingEngine();
        var viewModel = CreateViewModel(new RecordingScriptStore(), engine);
        await viewModel.InitializeAsync(CancellationToken.None);
        var executingScript = viewModel.SelectedScript;
        var otherScript = new ScriptItemViewModel(new ScriptDefinition { Name = "Other" });
        viewModel.Scripts.Add(otherScript);
        var target = new MemuInstance(2, "Target", true, 456);
        var otherTarget = new MemuInstance(4, "Other", true, 789);
        viewModel.Instances.Add(target);
        viewModel.Instances.Add(otherTarget);
        viewModel.SelectedInstance = target;

        var runTask = viewModel.RunCommand.ExecuteAsync();
        await engine.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        viewModel.SelectedScript = otherScript;
        viewModel.SelectedInstance = otherTarget;

        Assert.AreSame(executingScript, viewModel.SelectedScript);
        Assert.AreSame(target, viewModel.SelectedInstance);
        Assert.IsFalse(viewModel.CanChangeSelection);
        viewModel.StopCommand.Execute(null);
        await runTask.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [TestMethod]
    public async Task LateProgressFromCompletedRun_IsIgnored()
    {
        var engine = new LateReportingEngine();
        var viewModel = CreateViewModel(new RecordingScriptStore(), engine);
        await viewModel.InitializeAsync(CancellationToken.None);
        viewModel.Instances.Add(new MemuInstance(2, "Target", true, 456));
        viewModel.SelectedInstance = viewModel.Instances[0];

        await viewModel.RunCommand.ExecuteAsync();
        engine.ReportLate(viewModel.SelectedScript!.Model.Steps[0].Id);

        Assert.AreEqual(0, viewModel.ExecutionLog.Count);
    }

    private static MainViewModel CreateViewModel(IScriptStore store, IScriptExecutionEngine engine, IConfirmationService? confirmation = null) => new(
        new EmptyInstanceService(), new ValidPathDiscovery(), new MemorySettingsStore(), new SelectedFileDialog(),
        store, engine, new ScriptStepCommandBuilder(new MemuCommandBuilder()), confirmation ?? new AlwaysConfirm());

    private sealed class RecordingScriptStore : IScriptStore
    {
        private readonly IReadOnlyList<ScriptDefinition> loaded;
        public RecordingScriptStore(IReadOnlyList<ScriptDefinition>? loaded = null) => this.loaded = loaded ?? [];
        public int SaveCount { get; private set; }
        public bool ThrowOnSave { get; init; }
        public IReadOnlyList<ScriptDefinition> LastSaved { get; private set; } = [];
        public Task<IReadOnlyList<ScriptDefinition>> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(loaded);
        public Task SaveAsync(IReadOnlyCollection<ScriptDefinition> scripts, CancellationToken cancellationToken)
        {
            if (ThrowOnSave) throw new IOException("read-only");
            SaveCount++;
            LastSaved = scripts.ToList();
            return Task.CompletedTask;
        }
    }

    private sealed class ImmediateEngine : IScriptExecutionEngine
    {
        public ExecutionRequest? LastRequest { get; private set; }
        public Task<ExecutionResult> ExecuteAsync(ExecutionRequest request, IProgress<StepExecutionUpdate>? progress, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(new ExecutionResult { StartedAt = DateTimeOffset.UtcNow, EndedAt = DateTimeOffset.UtcNow });
        }
    }

    private sealed class BlockingEngine : IScriptExecutionEngine
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool WasCancelled { get; private set; }
        public async Task<ExecutionResult> ExecuteAsync(ExecutionRequest request, IProgress<StepExecutionUpdate>? progress, CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
            catch (OperationCanceledException) { WasCancelled = true; }
            return new ExecutionResult { StartedAt = DateTimeOffset.UtcNow, EndedAt = DateTimeOffset.UtcNow, WasCancelled = WasCancelled };
        }
    }

    private sealed class LateReportingEngine : IScriptExecutionEngine
    {
        private IProgress<StepExecutionUpdate>? progress;
        public Task<ExecutionResult> ExecuteAsync(ExecutionRequest request, IProgress<StepExecutionUpdate>? progress, CancellationToken cancellationToken)
        {
            this.progress = progress;
            return Task.FromResult(new ExecutionResult { StartedAt = DateTimeOffset.UtcNow, EndedAt = DateTimeOffset.UtcNow });
        }

        public void ReportLate(Guid stepId)
        {
            var now = DateTimeOffset.UtcNow;
            progress?.Report(new StepExecutionUpdate(stepId, StepExecutionStatus.Succeeded, new StepExecutionResult
            {
                StepId = stepId,
                Status = StepExecutionStatus.Succeeded,
                StartedAt = now,
                EndedAt = now,
                CommandPreview = "late"
            }));
        }
    }

    private sealed class EmptyInstanceService : IMemuInstanceService
    {
        public Task<IReadOnlyList<MemuInstance>> GetInstancesAsync(string memucPath, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<MemuInstance>>([]);
    }
    private sealed class ValidPathDiscovery : IMemucPathDiscovery
    {
        public string FindMemucPath() => @"C:\MEmu\memuc.exe";
        public bool IsValidMemucPath(string? path) => !string.IsNullOrWhiteSpace(path);
    }
    private sealed class MemorySettingsStore : ISettingsStore
    {
        public Task<ApplicationSettings> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(new ApplicationSettings { MemucPath = @"C:\MEmu\memuc.exe" });
        public Task SaveAsync(ApplicationSettings settings, CancellationToken cancellationToken) => Task.CompletedTask;
    }
    private sealed class SelectedFileDialog : IFileDialogService { public string? SelectMemucPath(string? currentPath) => null; }
    private sealed class AlwaysConfirm : IConfirmationService { public bool Confirm(string message, string title) => true; }
    private sealed class ConfigurableConfirmation(bool result) : IConfirmationService
    {
        public bool Confirm(string message, string title) => result;
    }
}
