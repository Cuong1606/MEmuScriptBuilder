using System.Collections.Concurrent;
using MEmuScriptStudio.Core.Execution;
using MEmuScriptStudio.Core.MEmu;
using MEmuScriptStudio.Core.Models;
using MEmuScriptStudio.Core.Processes;

namespace MEmuScriptStudio.Core.Tests;

[TestClass]
public sealed class MemuInstanceHealthExecutionTests
{
    [TestMethod]
    public async Task HealthyCommandsAndFinalBoundaryProduceSucceeded()
    {
        var probe = new SequenceHealthProbe();
        var runner = new RecordingRunner();
        using var session = CreateScheduler([Instance(0)], runner, probe).Start(Request(
            [Instance(0)],
            new AndroidShellStep { Name = "Shell", Command = "echo ok" }));

        var result = await session.Completion;

        Assert.AreEqual(InstanceExecutionStatus.Succeeded, ResultFor(result, 0).Status);
        Assert.AreEqual(1, runner.Requests.Count);
        Assert.AreEqual(3, probe.CallCount(0), "Preflight, trước process-backed step và final phải được probe đúng một lần.");
    }

    [TestMethod]
    public async Task HealthBecomesUnavailableDuringDelay_DoesNotStartNextStep()
    {
        var probe = new SequenceHealthProbe(new Dictionary<int, MemuInstanceHealthStatus[]>
        {
            [0] = [MemuInstanceHealthStatus.Healthy, MemuInstanceHealthStatus.Healthy, MemuInstanceHealthStatus.Unavailable]
        });
        var runner = new RecordingRunner();
        var home = new KeyEventStep { Name = "Home", Key = AndroidKeyEvent.Home };
        var updates = new List<InstanceExecutionUpdate>();
        using var session = CreateScheduler([Instance(0)], runner, probe).Start(Request(
            [Instance(0)],
            new OpenAppStep
            {
                Name = "Open Chrome",
                PackageName = "com.android.chrome",
                ActivityName = "com.google.android.apps.chrome.Main"
            },
            new DelayStep { Name = "Wait for Chrome", DurationMilliseconds = 1 },
            home), new InlineProgress<InstanceExecutionUpdate>(updates.Add));

        var result = await session.Completion;

        Assert.AreEqual(InstanceExecutionStatus.Unavailable, ResultFor(result, 0).Status);
        Assert.AreEqual("Core MEmu không còn hoạt động.", ResultFor(result, 0).Message);
        Assert.AreEqual(1, runner.Requests.Count);
        Assert.IsFalse(updates.Any(update => update.StepUpdate?.StepId == home.Id));
    }

    [TestMethod]
    public async Task FinalHealthUnavailable_PreventsFalseSuccessAfterExitZero()
    {
        var probe = new SequenceHealthProbe(new Dictionary<int, MemuInstanceHealthStatus[]>
        {
            [0] = [MemuInstanceHealthStatus.Healthy, MemuInstanceHealthStatus.Healthy, MemuInstanceHealthStatus.Unavailable]
        });
        var runner = new RecordingRunner();
        using var session = CreateScheduler([Instance(0)], runner, probe).Start(Request(
            [Instance(0)],
            new AndroidShellStep { Name = "Last command", Command = "echo ok" }));

        var result = await session.Completion;

        Assert.AreEqual(1, runner.Requests.Count);
        Assert.AreEqual(InstanceExecutionStatus.Unavailable, ResultFor(result, 0).Status);
    }

    [TestMethod]
    public async Task UnavailableInstanceDoesNotAffectHealthyInstance()
    {
        var probe = new SequenceHealthProbe(new Dictionary<int, MemuInstanceHealthStatus[]>
        {
            [0] = [MemuInstanceHealthStatus.Healthy, MemuInstanceHealthStatus.Unavailable],
            [1] = [MemuInstanceHealthStatus.Healthy, MemuInstanceHealthStatus.Healthy, MemuInstanceHealthStatus.Healthy]
        });
        var runner = new RecordingRunner();
        using var session = CreateScheduler([Instance(0), Instance(1)], runner, probe).Start(Request(
            [Instance(0), Instance(1)],
            new AndroidShellStep { Name = "Shell", Command = "echo ok" }));

        var result = await session.Completion;

        Assert.AreEqual(InstanceExecutionStatus.Unavailable, ResultFor(result, 0).Status);
        Assert.AreEqual(InstanceExecutionStatus.Succeeded, ResultFor(result, 1).Status);
        Assert.AreEqual(1, runner.Requests.Count);
        CollectionAssert.AreEqual(new[] { "1" }, runner.Requests.Select(request => request.Arguments[1]).ToArray());
    }

    [TestMethod]
    public async Task CancellationWinsWhenRequestedDuringFinalHealthProbe()
    {
        var probe = new BlockingFinalHealthProbe();
        using var session = CreateScheduler([Instance(0)], new RecordingRunner(), probe).Start(Request(
            [Instance(0)],
            new NoteStep { Name = "No process" }));
        await probe.FinalCheckStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        session.StopInstance(0);
        probe.ReleaseFinalCheck.TrySetResult();
        var result = await session.Completion.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.AreEqual(InstanceExecutionStatus.Cancelled, ResultFor(result, 0).Status);
    }

    [TestMethod]
    public async Task UnknownProbeResultDoesNotCreateFalseCoreDeadStatus()
    {
        var probe = new ThrowingHealthProbe();
        using var session = CreateScheduler([Instance(0)], new RecordingRunner(), probe).Start(Request(
            [Instance(0)],
            new AndroidShellStep { Name = "Shell", Command = "echo ok" }));

        var result = await session.Completion;

        Assert.AreEqual(InstanceExecutionStatus.Failed, ResultFor(result, 0).Status);
        Assert.AreEqual("Không thể xác minh trạng thái Core MEmu.", ResultFor(result, 0).Message);
    }

    [TestMethod]
    public async Task UnknownPreflightFailsAdmissionWithoutRunningFirstStep()
    {
        var probe = new SequenceHealthProbe(new Dictionary<int, MemuInstanceHealthStatus[]>
        {
            [0] = [MemuInstanceHealthStatus.Unknown, MemuInstanceHealthStatus.Healthy, MemuInstanceHealthStatus.Healthy]
        });
        var runner = new RecordingRunner();
        using var session = CreateScheduler([Instance(0)], runner, probe).Start(Request(
            [Instance(0)],
            new AndroidShellStep { Name = "Shell", Command = "echo ok" }));

        var result = await session.Completion;

        Assert.AreEqual(0, runner.Requests.Count);
        Assert.AreEqual(InstanceExecutionStatus.Failed, ResultFor(result, 0).Status);
        Assert.AreEqual("Không thể xác minh trạng thái Core MEmu.", ResultFor(result, 0).Message);
    }

    [TestMethod]
    public async Task HealthChecksAreBoundedToLifecycleBoundaries()
    {
        var probe = new SequenceHealthProbe();
        using var session = CreateScheduler([Instance(0)], new RecordingRunner(), probe).Start(Request(
            [Instance(0)],
            new AndroidShellStep { Name = "First", Command = "echo first" },
            new DelayStep { Name = "Wait", DurationMilliseconds = 1 },
            new AndroidShellStep { Name = "Second", Command = "echo second" }));

        var result = await session.Completion;

        Assert.AreEqual(InstanceExecutionStatus.Succeeded, ResultFor(result, 0).Status);
        Assert.AreEqual(5, probe.CallCount(0));
    }

    [TestMethod]
    public async Task PreflightCorePidIsPinnedForStepAndFinalChecks()
    {
        var probe = new PinningHealthProbe(900);
        using var session = CreateScheduler([Instance(0)], new RecordingRunner(), probe).Start(Request(
            [Instance(0)],
            new AndroidShellStep { Name = "Shell", Command = "echo ok" }));

        var result = await session.Completion;

        Assert.AreEqual(InstanceExecutionStatus.Succeeded, ResultFor(result, 0).Status);
        CollectionAssert.AreEqual(new int?[] { null, 900, 900 }, probe.ExpectedCoreProcessIds.ToArray());
    }

    private static MultiInstanceExecutionScheduler CreateScheduler(
        IReadOnlyList<MemuInstance> instances,
        IProcessRunner runner,
        IMemuInstanceHealthProbe probe)
    {
        var commandBuilder = new ScriptStepCommandBuilder(new MemuCommandBuilder());
        var regularEngine = new ScriptExecutionEngine(runner, commandBuilder, new ImmediateDelay(), pinnedCoreHealthCheck: probe);
        var engine = new CompositeScriptExecutionEngine(regularEngine, new ImmediateDelay(), probe);
        return new MultiInstanceExecutionScheduler(
            new FixedInstanceService(instances),
            engine,
            new ImmediateLaunchDelay(),
            new MinimumLaunchRandom(),
            probe,
            probe);
    }

    private static MultiInstanceExecutionRequest Request(
        IReadOnlyList<MemuInstance> targets,
        params ScriptStep[] steps) => new()
        {
            Script = new ScriptDefinition { Name = "Health test", Steps = steps.ToList() },
            MemucPath = @"C:\MEmu\memuc.exe",
            Targets = targets,
            LaunchSpacingMode = LaunchSpacingMode.Fixed
        };

    private static MemuInstance Instance(int index) =>
        new(index, $"Instance {index}", true, 100 + index);

    private static InstanceExecutionResult ResultFor(MultiInstanceExecutionResult result, int index) =>
        result.Instances.Single(item => item.Target.Index == index);

    private sealed class FixedInstanceService(IReadOnlyList<MemuInstance> instances) : IMemuInstanceService
    {
        public Task<IReadOnlyList<MemuInstance>> GetInstancesAsync(
            string memucPath,
            CancellationToken cancellationToken) => Task.FromResult(instances);
    }

    private sealed class RecordingRunner : IProcessRunner
    {
        public ConcurrentQueue<ProcessRequest> Requests { get; } = new();

        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken)
        {
            Requests.Enqueue(request);
            var now = DateTimeOffset.UtcNow;
            return Task.FromResult(new ProcessResult(0, "ok", string.Empty, now, now));
        }
    }

    private sealed class SequenceHealthProbe : IMemuInstanceHealthProbe
    {
        private readonly ConcurrentDictionary<int, ConcurrentQueue<MemuInstanceHealthStatus>> results;
        private readonly ConcurrentDictionary<int, int> counts = new();

        public SequenceHealthProbe(IReadOnlyDictionary<int, MemuInstanceHealthStatus[]>? results = null)
        {
            this.results = new ConcurrentDictionary<int, ConcurrentQueue<MemuInstanceHealthStatus>>(
                (results ?? new Dictionary<int, MemuInstanceHealthStatus[]>())
                .Select(pair => new KeyValuePair<int, ConcurrentQueue<MemuInstanceHealthStatus>>(
                    pair.Key,
                    new ConcurrentQueue<MemuInstanceHealthStatus>(pair.Value))));
        }

        public Task<MemuInstanceHealthResult> CheckAsync(
            MemuInstance instance,
            MemuInstanceCoreIdentity? expectedCoreIdentity,
            CancellationToken cancellationToken)
        {
            counts.AddOrUpdate(instance.Index, 1, (_, count) => count + 1);
            var status = results.TryGetValue(instance.Index, out var queue) && queue.TryDequeue(out var next)
                ? next
                : MemuInstanceHealthStatus.Healthy;
            return Task.FromResult(status switch
            {
                MemuInstanceHealthStatus.Healthy => MemuInstanceHealthResult.HealthyFor(
                    expectedCoreIdentity?.ProcessId ?? 900 + instance.Index,
                    expectedCoreIdentity?.CreationTimeUtcFileTime ?? 10_000 + instance.Index),
                MemuInstanceHealthStatus.Unavailable => MemuInstanceHealthResult.Unavailable("core exited"),
                _ => MemuInstanceHealthResult.Unknown("mapping unavailable")
            });
        }

        public int CallCount(int instanceIndex) => counts.GetValueOrDefault(instanceIndex);
    }

    private sealed class BlockingFinalHealthProbe : IMemuInstanceHealthProbe
    {
        private int callCount;
        public TaskCompletionSource FinalCheckStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseFinalCheck { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<MemuInstanceHealthResult> CheckAsync(
            MemuInstance instance,
            MemuInstanceCoreIdentity? expectedCoreIdentity,
            CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref callCount) == 1)
                return MemuInstanceHealthResult.HealthyFor(900, 10_000, instance.Name);

            FinalCheckStarted.TrySetResult();
            await ReleaseFinalCheck.Task.ConfigureAwait(false);
            return MemuInstanceHealthResult.Unavailable("core exited");
        }
    }

    private sealed class ThrowingHealthProbe : IMemuInstanceHealthProbe
    {
        public Task<MemuInstanceHealthResult> CheckAsync(
            MemuInstance instance,
            MemuInstanceCoreIdentity? expectedCoreIdentity,
            CancellationToken cancellationToken) =>
            Task.FromException<MemuInstanceHealthResult>(new InvalidOperationException("probe failed"));
    }

    private sealed class PinningHealthProbe(int coreProcessId) : IMemuInstanceHealthProbe
    {
        public List<int?> ExpectedCoreProcessIds { get; } = [];

        public Task<MemuInstanceHealthResult> CheckAsync(
            MemuInstance instance,
            MemuInstanceCoreIdentity? expectedCoreIdentity,
            CancellationToken cancellationToken)
        {
            ExpectedCoreProcessIds.Add(expectedCoreIdentity?.ProcessId);
            return Task.FromResult(MemuInstanceHealthResult.HealthyFor(coreProcessId, 10_000));
        }
    }

    private sealed class ImmediateDelay : IDelayProvider
    {
        public Task DelayAsync(TimeSpan duration, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class ImmediateLaunchDelay : ILaunchDelayProvider
    {
        public Task DelayAsync(TimeSpan duration, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class MinimumLaunchRandom : ILaunchSpacingRandom
    {
        public int NextInclusive(int minimumMilliseconds, int maximumMilliseconds) => minimumMilliseconds;
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
