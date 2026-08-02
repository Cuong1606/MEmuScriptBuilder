using MEmuScriptStudio.Core.Execution;
using MEmuScriptStudio.Core.MEmu;
using MEmuScriptStudio.Core.Models;
using MEmuScriptStudio.Core.Processes;

namespace MEmuScriptStudio.Core.Tests;

[TestClass]
public sealed class ScriptExecutionEngineTests
{
    [TestMethod]
    public async Task ExecuteAsync_RunsProcessAndDelayStepsInOrder()
    {
        var events = new List<string>();
        var runner = new FakeRunner(events, Success(), Success());
        var delay = new RecordingDelay(events);
        var engine = CreateEngine(runner, delay);
        var script = Script(
            new AndroidShellStep { Name = "First", Command = "first" },
            new DelayStep { Name = "Wait", DurationMilliseconds = 250 },
            new AndroidShellStep { Name = "Last", Command = "last" });

        var result = await engine.ExecuteAsync(Request(script), null, CancellationToken.None);

        CollectionAssert.AreEqual(new[] { "process:first", "delay:250", "process:last" }, events);
        CollectionAssert.AreEqual(
            new[] { StepExecutionStatus.Succeeded, StepExecutionStatus.Succeeded, StepExecutionStatus.Succeeded },
            result.Steps.Select(item => item.Status).ToArray());
    }

    [TestMethod]
    public async Task ExecuteAsync_StopsAfterFailureByDefault()
    {
        var runner = new FakeRunner([], Failure(), Success());
        var engine = CreateEngine(runner, new RecordingDelay([]));
        var script = Script(
            new AndroidShellStep { Name = "Fail", Command = "fail" },
            new AndroidShellStep { Name = "Must not run", Command = "second" });

        var result = await engine.ExecuteAsync(Request(script), null, CancellationToken.None);

        Assert.AreEqual(1, runner.Requests.Count);
        Assert.AreEqual(1, result.Steps.Count);
        Assert.AreEqual(StepExecutionStatus.Failed, result.Steps[0].Status);
    }

    [TestMethod]
    public async Task ExecuteAsync_ContinuesAfterFailureWhenEnabled()
    {
        var runner = new FakeRunner([], Failure(), Success());
        var engine = CreateEngine(runner, new RecordingDelay([]));
        var script = Script(
            new AndroidShellStep { Name = "Fail", Command = "fail", ContinueOnError = true },
            new AndroidShellStep { Name = "Continue", Command = "second" });

        var result = await engine.ExecuteAsync(Request(script), null, CancellationToken.None);

        Assert.AreEqual(2, runner.Requests.Count);
        CollectionAssert.AreEqual(
            new[] { StepExecutionStatus.Failed, StepExecutionStatus.Succeeded },
            result.Steps.Select(item => item.Status).ToArray());
    }

    [TestMethod]
    public async Task ExecuteAsync_MapsTimeoutToFailedResult()
    {
        var runner = new FakeRunner([], new TimeoutException("timed out"));
        var engine = CreateEngine(runner, new RecordingDelay([]));

        var result = await engine.ExecuteAsync(Request(Script(
            new TapStep { Name = "Tap", X = 1, Y = 2, TimeoutSeconds = 3 })), null, CancellationToken.None);

        Assert.AreEqual(StepExecutionStatus.Failed, result.Steps[0].Status);
        StringAssert.Contains(result.Steps[0].StandardError, "timed out");
        Assert.AreEqual(TimeSpan.FromSeconds(3), runner.Requests[0].Timeout);
    }

    [TestMethod]
    public async Task ExecuteAsync_CancelsRunningDelay()
    {
        var engine = CreateEngine(new FakeRunner([]), new BlockingDelay());
        using var cancellationSource = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        var result = await engine.ExecuteAsync(Request(Script(
            new DelayStep { Name = "Wait", DurationMilliseconds = 30_000 },
            new AndroidShellStep { Name = "Never", Command = "never" })), null, cancellationSource.Token);

        Assert.IsTrue(result.WasCancelled);
        Assert.AreEqual(1, result.Steps.Count);
        Assert.AreEqual(StepExecutionStatus.Cancelled, result.Steps[0].Status);
    }

    [TestMethod]
    public async Task ExecuteAsync_SkipsDisabledAndNoteStepsWithoutStartingProcess()
    {
        var runner = new FakeRunner([], Success());
        var engine = CreateEngine(runner, new RecordingDelay([]));
        var script = Script(
            new TapStep { Name = "Disabled", X = 1, Y = 2, IsEnabled = false },
            new NoteStep { Name = "Note", Text = "explain" },
            new KeyEventStep { Name = "Home", Key = AndroidKeyEvent.Home });

        var result = await engine.ExecuteAsync(Request(script), null, CancellationToken.None);

        Assert.AreEqual(1, runner.Requests.Count);
        CollectionAssert.AreEqual(
            new[] { StepExecutionStatus.Skipped, StepExecutionStatus.Skipped, StepExecutionStatus.Succeeded },
            result.Steps.Select(item => item.Status).ToArray());
    }

    private static ScriptExecutionEngine CreateEngine(IProcessRunner runner, IDelayProvider delay) =>
        new(runner, new ScriptStepCommandBuilder(new MemuCommandBuilder()), delay);

    private static ExecutionRequest Request(ScriptDefinition script) => new()
    {
        Script = script,
        MemucPath = @"C:\MEmu\memuc.exe",
        InstanceIndex = 5
    };

    private static ScriptDefinition Script(params ScriptStep[] steps) => new() { Name = "Test", Steps = steps.ToList() };

    private static ProcessResult Success() => new(0, "ok", string.Empty, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
    private static ProcessResult Failure() => new(9, string.Empty, "failed", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    private sealed class FakeRunner(List<string> events, params object[] outcomes) : IProcessRunner
    {
        private readonly Queue<object> outcomes = new(outcomes);
        public List<ProcessRequest> Requests { get; } = [];

        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            events.Add($"process:{request.Arguments[3]}");
            var outcome = outcomes.Dequeue();
            return outcome is Exception exception
                ? Task.FromException<ProcessResult>(exception)
                : Task.FromResult((ProcessResult)outcome);
        }
    }

    private sealed class RecordingDelay(List<string> events) : IDelayProvider
    {
        public Task DelayAsync(TimeSpan duration, CancellationToken cancellationToken)
        {
            events.Add($"delay:{duration.TotalMilliseconds:0}");
            return Task.CompletedTask;
        }
    }

    private sealed class BlockingDelay : IDelayProvider
    {
        public Task DelayAsync(TimeSpan duration, CancellationToken cancellationToken) =>
            Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
    }
}
