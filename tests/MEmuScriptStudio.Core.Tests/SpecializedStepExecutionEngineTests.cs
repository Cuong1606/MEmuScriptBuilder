using MEmuScriptStudio.Core.Execution;
using MEmuScriptStudio.Core.MEmu;
using MEmuScriptStudio.Core.Models;
using MEmuScriptStudio.Core.Processes;

namespace MEmuScriptStudio.Core.Tests;

[TestClass]
public sealed class SpecializedStepExecutionEngineTests
{
    [TestMethod]
    public async Task SpecializedStepsHonorContinueOnErrorAndCommandPreview()
    {
        var specialized = new QueueSpecializedExecutor(false, true);
        var builder = new ScriptStepCommandBuilder(new MemuCommandBuilder(), specialized);
        var engine = new ScriptExecutionEngine(new NeverProcessRunner(), builder, new ImmediateDelay(), specialized);
        var script = new ScriptDefinition
        {
            Steps =
            [
                new CloseChromeTabsStep { Name = "Chrome 1", ContinueOnError = true },
                new CloseChromeTabsStep { Name = "Chrome 2" }
            ]
        };

        var result = await engine.ExecuteAsync(new ExecutionRequest
        {
            Script = script,
            MemucPath = "C:\\MEmu\\memuc.exe",
            InstanceIndex = 4
        }, null, CancellationToken.None);

        CollectionAssert.AreEqual(
            new[] { StepExecutionStatus.Failed, StepExecutionStatus.Succeeded },
            result.Steps.Select(step => step.Status).ToArray());
        Assert.IsTrue(result.Steps.All(step => step.CommandPreview.StartsWith("preview:", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task SpecializedCancellationIsNeverConvertedToContinue()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();
        var specialized = new CancellingSpecializedExecutor();
        var engine = new ScriptExecutionEngine(
            new NeverProcessRunner(),
            new ScriptStepCommandBuilder(new MemuCommandBuilder(), specialized),
            new ImmediateDelay(),
            specialized);
        var script = new ScriptDefinition
        {
            Steps =
            [
                new CloseChromeTabsStep { Name = "Chrome", ContinueOnError = true },
                new CloseChromeTabsStep { Name = "Never" }
            ]
        };

        var result = await engine.ExecuteAsync(new ExecutionRequest
        {
            Script = script,
            MemucPath = "C:\\MEmu\\memuc.exe",
            InstanceIndex = 4
        }, null, source.Token);

        Assert.IsTrue(result.WasCancelled);
        Assert.AreEqual(0, result.Steps.Count);
    }

    [TestMethod]
    public async Task SpecializedAdbPreflightFailureRemainsShortFailedStepReason()
    {
        const string message =
            "ADB của giả lập đang offline hoặc chưa được cấp quyền. Không thể điều khiển tab Chrome trên instance này.";
        var specialized = new ThrowingSpecializedExecutor(new InvalidOperationException(message));
        var engine = new ScriptExecutionEngine(
            new NeverProcessRunner(),
            new ScriptStepCommandBuilder(new MemuCommandBuilder(), specialized),
            new ImmediateDelay(),
            specialized);

        var result = await engine.ExecuteAsync(new ExecutionRequest
        {
            Script = new ScriptDefinition { Steps = [new CloseChromeTabsStep { Name = "Chrome" }] },
            MemucPath = "C:\\MEmu\\memuc.exe",
            InstanceIndex = 0
        }, null, CancellationToken.None);

        Assert.AreEqual(StepExecutionStatus.Failed, result.Steps.Single().Status);
        Assert.AreEqual(message, result.Steps.Single().StandardError);
    }

    private sealed class QueueSpecializedExecutor(params bool[] outcomes) : ISpecializedStepExecutor
    {
        private readonly Queue<bool> outcomes = new(outcomes);
        public string BuildPreview(ScriptStep step, string memucPath, int instanceIndex) => $"preview:{step.Kind}";
        public Task<SpecializedStepExecutionResult> ExecuteAsync(
            ScriptStep step, string memucPath, int instanceIndex, CancellationToken cancellationToken)
        {
            var now = DateTimeOffset.UtcNow;
            var succeeded = outcomes.Dequeue();
            return Task.FromResult(new SpecializedStepExecutionResult(
                succeeded, succeeded ? 0 : 1, string.Empty, succeeded ? string.Empty : "failed",
                BuildPreview(step, memucPath, instanceIndex), now, now));
        }
    }

    private sealed class CancellingSpecializedExecutor : ISpecializedStepExecutor
    {
        public string BuildPreview(ScriptStep step, string memucPath, int instanceIndex) => "preview";
        public Task<SpecializedStepExecutionResult> ExecuteAsync(
            ScriptStep step, string memucPath, int instanceIndex, CancellationToken cancellationToken) =>
            Task.FromCanceled<SpecializedStepExecutionResult>(cancellationToken);
    }

    private sealed class ThrowingSpecializedExecutor(Exception exception) : ISpecializedStepExecutor
    {
        public string BuildPreview(ScriptStep step, string memucPath, int instanceIndex) => "preview";
        public Task<SpecializedStepExecutionResult> ExecuteAsync(
            ScriptStep step, string memucPath, int instanceIndex, CancellationToken cancellationToken) =>
            Task.FromException<SpecializedStepExecutionResult>(exception);
    }

    private sealed class NeverProcessRunner : IProcessRunner
    {
        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken) =>
            throw new AssertFailedException("Specialized step must not use the regular process-command path.");
    }

    private sealed class ImmediateDelay : IDelayProvider
    {
        public Task DelayAsync(TimeSpan duration, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
