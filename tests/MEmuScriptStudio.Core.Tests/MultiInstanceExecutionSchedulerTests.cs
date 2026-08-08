using System.Collections.Concurrent;
using MEmuScriptStudio.Core.Execution;
using MEmuScriptStudio.Core.MEmu;
using MEmuScriptStudio.Core.Models;

namespace MEmuScriptStudio.Core.Tests;

[TestClass]
public sealed class MultiInstanceExecutionSchedulerTests
{
    [TestMethod]
    public async Task Preflight_DefaultPolicySkipsUnavailableAndRunsEveryValidTargetOnce()
    {
        var engine = new RecordingEngine();
        var scheduler = CreateScheduler(
            [Instance(0, running: true), Instance(1, running: false)],
            engine);

        using var session = scheduler.Start(Request([Instance(0), Instance(1), Instance(2)]));
        var result = await session.Completion;

        CollectionAssert.AreEqual(new[] { 0 }, engine.StartedIndices.ToArray());
        Assert.AreEqual(InstanceExecutionStatus.Succeeded, ResultFor(result, 0).Status);
        Assert.AreEqual(InstanceExecutionStatus.Unavailable, ResultFor(result, 1).Status);
        Assert.AreEqual(InstanceExecutionStatus.Unavailable, ResultFor(result, 2).Status);
        Assert.IsFalse(result.WasStoppedByInvalidTargetPolicy);
    }

    [TestMethod]
    public async Task Preflight_StopAllPolicyDoesNotLaunchAnyValidTarget()
    {
        var engine = new RecordingEngine();
        var scheduler = CreateScheduler([Instance(0, running: true)], engine);
        var request = WithStopAllOnInvalidTarget(Request([Instance(0), Instance(9)]));

        using var session = scheduler.Start(request);
        var result = await session.Completion;

        Assert.AreEqual(0, engine.StartedIndices.Count);
        Assert.AreEqual(InstanceExecutionStatus.Cancelled, ResultFor(result, 0).Status);
        Assert.AreEqual(InstanceExecutionStatus.Unavailable, ResultFor(result, 9).Status);
        Assert.IsTrue(result.WasStoppedByInvalidTargetPolicy);
    }

    [TestMethod]
    public async Task PreflightFailureProducesTerminalFailedRowsInsteadOfFaultingTheGroup()
    {
        var scheduler = new MultiInstanceExecutionScheduler(
            new ThrowingInstanceService(),
            new RecordingEngine(),
            new RecordingLaunchDelay(),
            new QueueRandom());
        using var session = scheduler.Start(Request([Instance(0), Instance(1)]));

        var result = await session.Completion;

        Assert.IsTrue(result.Instances.All(item => item.Status == InstanceExecutionStatus.Failed));
        Assert.IsTrue(result.Instances.All(item => item.Message == "preflight failed"));
    }

    [TestMethod]
    public async Task Launching_FirstStartsImmediately_ThenUsesDelayWithoutWaitingForCompletion()
    {
        var engine = new ControlledEngine([0, 1]);
        var delay = new ControlledLaunchDelay();
        var scheduler = CreateScheduler([Instance(0), Instance(1)], engine, delay);
        var request = Request([Instance(0), Instance(1)], fixedSpacingMilliseconds: 250);

        using var session = scheduler.Start(request);
        await engine.WaitForStartsAsync(1);
        await delay.WaitForCallsAsync(1);
        Assert.AreEqual(1, engine.ActiveCount, "Máy đầu tiên vẫn chạy trong khi nhóm chờ mốc khởi chạy tiếp theo.");
        Assert.AreEqual(TimeSpan.FromMilliseconds(250), delay.Durations[0]);
        CollectionAssert.AreEqual(new[] { 0 }, engine.StartedIndices.ToArray(), "Máy kế tiếp chưa được chạy trước khi delay hoàn tất.");

        delay.Release(0);
        await engine.WaitForStartsAsync(2);
        Assert.AreEqual(2, engine.ActiveCount);
        engine.Complete(0);
        engine.Complete(1);
        await session.Completion;

        CollectionAssert.AreEqual(new[] { 0, 1 }, engine.StartedIndices.ToArray());
        Assert.AreEqual(2, engine.MaximumObservedConcurrency);
    }

    [TestMethod]
    public async Task Launching_UsesIndependentSpacingWithinTheGroup()
    {
        var engine = new ControlledEngine([0, 1, 2]);
        var delay = new ControlledLaunchDelay();
        var scheduler = CreateScheduler([Instance(0), Instance(1), Instance(2)], engine, delay);
        var request = Request([Instance(0), Instance(1), Instance(2)], fixedSpacingMilliseconds: 100);

        using var session = scheduler.Start(request);
        await engine.WaitForStartsAsync(1);
        await delay.WaitForCallsAsync(1);
        delay.Release(0);
        await engine.WaitForStartsAsync(2);

        await delay.WaitForCallsAsync(2);
        Assert.AreEqual(2, engine.ActiveCount, "Khoảng chờ tiếp theo không phụ thuộc target trước đã hoàn tất.");
        delay.Release(1);
        await engine.WaitForStartsAsync(3);
        engine.Complete(0);
        engine.Complete(1);
        engine.Complete(2);
        await session.Completion;

        Assert.AreEqual(3, engine.MaximumObservedConcurrency);
    }

    [TestMethod]
    public async Task RandomSpacing_UsesANewInclusiveSampleForEverySubsequentLaunch()
    {
        var delay = new RecordingLaunchDelay();
        var random = new QueueRandom(12, 34);
        var scheduler = CreateScheduler(
            [Instance(0), Instance(1), Instance(2)],
            new RecordingEngine(),
            delay,
            random);
        var request = Request(
            [Instance(0), Instance(1), Instance(2)],
            spacingMode: LaunchSpacingMode.Random,
            randomMinimumMilliseconds: 10,
            randomMaximumMilliseconds: 40);

        using var session = scheduler.Start(request);
        await session.Completion;

        CollectionAssert.AreEqual(
            new[] { TimeSpan.FromMilliseconds(12), TimeSpan.FromMilliseconds(34) },
            delay.Durations.ToArray());
        CollectionAssert.AreEqual(new[] { (10, 40), (10, 40) }, random.Ranges.ToArray());
    }

    [TestMethod]
    public async Task FailureOfOneTarget_DoesNotStopOtherValidTargets()
    {
        var engine = new RecordingEngine(failedIndices: [0]);
        var scheduler = CreateScheduler([Instance(0), Instance(1)], engine);

        using var session = scheduler.Start(Request([Instance(0), Instance(1)]));
        var result = await session.Completion;

        CollectionAssert.AreEqual(new[] { 0, 1 }, engine.StartedIndices.ToArray());
        Assert.AreEqual(InstanceExecutionStatus.Failed, ResultFor(result, 0).Status);
        Assert.AreEqual(InstanceExecutionStatus.Succeeded, ResultFor(result, 1).Status);
    }

    [TestMethod]
    public async Task StopInstance_DuringLaunchGapCancelsOnlyThatTarget()
    {
        var engine = new ControlledEngine([0]);
        var delay = new ControlledLaunchDelay();
        var scheduler = CreateScheduler([Instance(0), Instance(1)], engine, delay);

        using var session = scheduler.Start(Request([Instance(0), Instance(1)], fixedSpacingMilliseconds: 100));
        await engine.WaitForStartsAsync(1);
        await delay.WaitForCallsAsync(1);
        session.StopInstance(1);
        engine.Complete(0);
        var result = await session.Completion;

        CollectionAssert.AreEqual(new[] { 0 }, engine.StartedIndices.ToArray());
        Assert.AreEqual(InstanceExecutionStatus.Succeeded, ResultFor(result, 0).Status);
        Assert.AreEqual(InstanceExecutionStatus.Cancelled, ResultFor(result, 1).Status);
        Assert.IsFalse(result.WasCancelled);
    }

    [TestMethod]
    public async Task StopAll_CancelsRunningAndQueuedTargets()
    {
        var engine = new ControlledEngine([0]);
        var delay = new ControlledLaunchDelay();
        var scheduler = CreateScheduler([Instance(0), Instance(1)], engine, delay);

        using var session = scheduler.Start(Request([Instance(0), Instance(1)], fixedSpacingMilliseconds: 100));
        await engine.WaitForStartsAsync(1);
        await delay.WaitForCallsAsync(1);
        session.StopAll();
        var result = await session.Completion;

        Assert.AreEqual(InstanceExecutionStatus.Cancelled, ResultFor(result, 0).Status);
        Assert.AreEqual(InstanceExecutionStatus.Cancelled, ResultFor(result, 1).Status);
        Assert.IsTrue(result.WasCancelled);
    }

    [TestMethod]
    public async Task StopInstance_DuringPreflightIsIdempotentAndWinsOverUnavailableResult()
    {
        var instances = new BlockingInstanceService([Instance(4, running: false)]);
        var engine = new RecordingEngine();
        var scheduler = new MultiInstanceExecutionScheduler(
            instances, engine, new RecordingLaunchDelay(), new QueueRandom());
        using var session = scheduler.Start(Request([Instance(4)]));
        await instances.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        session.StopInstance(4);
        session.StopInstance(4);
        instances.Release.TrySetResult();
        var result = await session.Completion.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.AreEqual(InstanceExecutionStatus.Cancelled, ResultFor(result, 4).Status);
        Assert.AreEqual(0, engine.StartedIndices.Count);
    }

    [TestMethod]
    public async Task StopInstance_DuringPreflightRemainsCancelledWhenDiscoveryFails()
    {
        var instances = new BlockingInstanceService(
            [], new InvalidOperationException("preflight failed after stop"));
        var scheduler = new MultiInstanceExecutionScheduler(
            instances, new RecordingEngine(), new RecordingLaunchDelay(), new QueueRandom());
        using var session = scheduler.Start(Request([Instance(7)]));
        await instances.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        session.StopInstance(7);
        instances.Release.TrySetResult();
        var result = await session.Completion.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.AreEqual(InstanceExecutionStatus.Cancelled, ResultFor(result, 7).Status);
    }

    [TestMethod]
    public async Task StopAll_DuringPreflightIsIdempotentAndDoesNotLaunchCommands()
    {
        var instances = new BlockingInstanceService([Instance(1), Instance(2)]);
        var engine = new RecordingEngine();
        var scheduler = new MultiInstanceExecutionScheduler(
            instances, engine, new RecordingLaunchDelay(), new QueueRandom());
        using var session = scheduler.Start(Request([Instance(1), Instance(2)]));
        await instances.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        session.StopAll();
        session.StopAll();
        instances.Release.TrySetResult();
        var result = await session.Completion.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.IsTrue(result.WasCancelled);
        Assert.IsTrue(result.Instances.All(item => item.Status == InstanceExecutionStatus.Cancelled));
        Assert.AreEqual(0, engine.StartedIndices.Count);
    }

    [TestMethod]
    public async Task StopInstance_AfterTerminalCommitIsRejectedWithoutStopFeedback()
    {
        var scheduler = CreateScheduler([Instance(4)], new RecordingEngine());
        using var session = scheduler.Start(Request([Instance(4)]));
        var result = await session.Completion;
        var stopFeedbackRequested = false;

        var accepted = session.StopInstance(4, () => stopFeedbackRequested = true);

        Assert.AreEqual(InstanceExecutionStatus.Succeeded, ResultFor(result, 4).Status);
        Assert.IsFalse(accepted);
        Assert.IsFalse(stopFeedbackRequested);
    }

    [TestMethod]
    public async Task SchedulerPassesCoordinateStepsUnchangedToEveryInstance()
    {
        var engine = new RecordingEngine();
        var script = new ScriptDefinition
        {
            Name = "Coordinates",
            Steps = { new SwipeStep { Name = "Swipe", X1 = 11, Y1 = 22, X2 = 333, Y2 = 444, DurationMilliseconds = 500 } }
        };
        var scheduler = CreateScheduler([Instance(0), Instance(1)], engine);
        var request = WithScript(Request([Instance(0), Instance(1)]), script);

        using var session = scheduler.Start(request);
        await session.Completion;

        Assert.AreEqual(2, engine.Requests.Count);
        foreach (var executionRequest in engine.Requests)
        {
            var swipe = (SwipeStep)executionRequest.Script.Steps.Single();
            Assert.AreEqual((11, 22, 333, 444), (swipe.X1, swipe.Y1, swipe.X2, swipe.Y2));
        }
    }

    [TestMethod]
    public async Task SchedulerUsesTheSnapshottedScriptAssignedToEachInstance()
    {
        var engine = new RecordingEngine();
        var common = new ScriptDefinition { Name = "Common", Steps = { new NoteStep { Name = "Common note" } } };
        var first = new ScriptDefinition { Name = "First", Steps = { new NoteStep { Name = "First note" } } };
        var second = new ScriptDefinition { Name = "Second", Steps = { new NoteStep { Name = "Second note" } } };
        var scheduler = CreateScheduler([Instance(0), Instance(1)], engine);
        var request = Request([Instance(0), Instance(1)]);
        request = new MultiInstanceExecutionRequest
        {
            Script = common,
            ScriptsByInstance = new Dictionary<int, ScriptDefinition> { [0] = first, [1] = second },
            MemucPath = request.MemucPath,
            Targets = request.Targets,
            LaunchSpacingMode = request.LaunchSpacingMode
        };

        using var session = scheduler.Start(request);
        var result = await session.Completion;

        var requests = engine.Requests.OrderBy(item => item.InstanceIndex).ToList();
        Assert.AreEqual(first.Id, requests[0].Script.Id);
        Assert.AreEqual(second.Id, requests[1].Script.Id);
        Assert.AreEqual("First", ResultFor(result, 0).ScriptName);
        Assert.AreEqual("Second", ResultFor(result, 1).ScriptName);
    }

    private static MultiInstanceExecutionScheduler CreateScheduler(
        IReadOnlyList<MemuInstance> currentInstances,
        IScriptExecutionEngine engine,
        ILaunchDelayProvider? delay = null,
        ILaunchSpacingRandom? random = null) =>
        new(
            new FixedInstanceService(currentInstances),
            engine,
            delay ?? new RecordingLaunchDelay(),
            random ?? new QueueRandom(),
            new AlwaysPinnedHealthProbe());

    private static MultiInstanceExecutionRequest Request(
        IReadOnlyList<MemuInstance> targets,
        int fixedSpacingMilliseconds = 0,
        LaunchSpacingMode spacingMode = LaunchSpacingMode.Fixed,
        int randomMinimumMilliseconds = 0,
        int randomMaximumMilliseconds = 0) => new()
        {
            Script = new ScriptDefinition { Name = "Script", Steps = { new NoteStep { Name = "Note" } } },
            MemucPath = @"C:\MEmu\memuc.exe",
            Targets = targets,
            LaunchSpacingMode = spacingMode,
            FixedSpacing = TimeSpan.FromMilliseconds(fixedSpacingMilliseconds),
            RandomMinimumSpacing = TimeSpan.FromMilliseconds(randomMinimumMilliseconds),
            RandomMaximumSpacing = TimeSpan.FromMilliseconds(randomMaximumMilliseconds)
        };

    private sealed class AlwaysPinnedHealthProbe : IMemuInstanceHealthProbe
    {
        public Task<MemuInstanceHealthResult> CheckAsync(
            MemuInstance instance,
            MemuInstanceCoreIdentity? expectedCoreIdentity,
            CancellationToken cancellationToken) =>
            Task.FromResult(MemuInstanceHealthResult.HealthyFor(
                expectedCoreIdentity?.ProcessId ?? 900 + instance.Index,
                expectedCoreIdentity?.CreationTimeUtcFileTime ?? 10_000 + instance.Index));
    }

    private static MultiInstanceExecutionRequest WithStopAllOnInvalidTarget(MultiInstanceExecutionRequest source) => new()
    {
        Script = source.Script,
        MemucPath = source.MemucPath,
        Targets = source.Targets,
        LaunchSpacingMode = source.LaunchSpacingMode,
        FixedSpacing = source.FixedSpacing,
        RandomMinimumSpacing = source.RandomMinimumSpacing,
        RandomMaximumSpacing = source.RandomMaximumSpacing,
        StopAllOnInvalidTarget = true
    };

    private static MultiInstanceExecutionRequest WithScript(MultiInstanceExecutionRequest source, ScriptDefinition script) => new()
    {
        Script = script,
        MemucPath = source.MemucPath,
        Targets = source.Targets,
        LaunchSpacingMode = source.LaunchSpacingMode,
        FixedSpacing = source.FixedSpacing,
        RandomMinimumSpacing = source.RandomMinimumSpacing,
        RandomMaximumSpacing = source.RandomMaximumSpacing,
        StopAllOnInvalidTarget = source.StopAllOnInvalidTarget
    };

    private static MemuInstance Instance(int index, bool running = true) =>
        new(index, $"Instance {index}", running, running ? index + 100 : null);

    private static InstanceExecutionResult ResultFor(MultiInstanceExecutionResult result, int instanceIndex) =>
        result.Instances.Single(item => item.Target.Index == instanceIndex);

    private sealed class FixedInstanceService(IReadOnlyList<MemuInstance> instances) : IMemuInstanceService
    {
        public Task<IReadOnlyList<MemuInstance>> GetInstancesAsync(string memucPath, CancellationToken cancellationToken) =>
            Task.FromResult(instances);
    }

    private sealed class ThrowingInstanceService : IMemuInstanceService
    {
        public Task<IReadOnlyList<MemuInstance>> GetInstancesAsync(string memucPath, CancellationToken cancellationToken) =>
            Task.FromException<IReadOnlyList<MemuInstance>>(new InvalidOperationException("preflight failed"));
    }

    private sealed class BlockingInstanceService(
        IReadOnlyList<MemuInstance> instances,
        Exception? failure = null) : IMemuInstanceService
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<IReadOnlyList<MemuInstance>> GetInstancesAsync(
            string memucPath,
            CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            if (failure is not null) throw failure;
            return instances;
        }
    }

    private sealed class RecordingEngine(IReadOnlyCollection<int>? failedIndices = null) : IScriptExecutionEngine
    {
        private readonly HashSet<int> failedIndices = [.. failedIndices ?? []];
        public ConcurrentQueue<int> StartedIndices { get; } = new();
        public ConcurrentBag<ExecutionRequest> Requests { get; } = [];

        public Task<ExecutionResult> ExecuteAsync(
            ExecutionRequest request,
            IProgress<StepExecutionUpdate>? progress,
            CancellationToken cancellationToken)
        {
            StartedIndices.Enqueue(request.InstanceIndex);
            Requests.Add(request);
            var now = DateTimeOffset.UtcNow;
            var status = failedIndices.Contains(request.InstanceIndex) ? StepExecutionStatus.Failed : StepExecutionStatus.Succeeded;
            return Task.FromResult(new ExecutionResult
            {
                StartedAt = now,
                EndedAt = now,
                Steps =
                [
                    new StepExecutionResult
                    {
                        StepId = request.Script.Steps[0].Id,
                        Status = status,
                        StartedAt = now,
                        EndedAt = now,
                        CommandPreview = "preview"
                    }
                ]
            });
        }
    }

    private sealed class ControlledEngine(IEnumerable<int> controlledIndices) : IScriptExecutionEngine
    {
        private readonly ConcurrentDictionary<int, TaskCompletionSource<ExecutionResult>> completions =
            new(controlledIndices.ToDictionary(
                index => index,
                _ => new TaskCompletionSource<ExecutionResult>(TaskCreationOptions.RunContinuationsAsynchronously)));
        private readonly SemaphoreSlim startedSignal = new(0);
        private int activeCount;
        private int maximumObservedConcurrency;

        public ConcurrentQueue<int> StartedIndices { get; } = new();
        public int ActiveCount => Volatile.Read(ref activeCount);
        public int MaximumObservedConcurrency => Volatile.Read(ref maximumObservedConcurrency);

        public async Task<ExecutionResult> ExecuteAsync(
            ExecutionRequest request,
            IProgress<StepExecutionUpdate>? progress,
            CancellationToken cancellationToken)
        {
            StartedIndices.Enqueue(request.InstanceIndex);
            var active = Interlocked.Increment(ref activeCount);
            UpdateMaximum(active);
            startedSignal.Release();
            try
            {
                return await completions[request.InstanceIndex].Task.WaitAsync(cancellationToken);
            }
            finally
            {
                Interlocked.Decrement(ref activeCount);
            }
        }

        public void Complete(int instanceIndex)
        {
            var now = DateTimeOffset.UtcNow;
            completions[instanceIndex].TrySetResult(new ExecutionResult { StartedAt = now, EndedAt = now });
        }

        public async Task WaitForStartsAsync(int count)
        {
            while (StartedIndices.Count < count)
                await startedSignal.WaitAsync(TimeSpan.FromSeconds(2));
        }

        private void UpdateMaximum(int value)
        {
            while (true)
            {
                var current = Volatile.Read(ref maximumObservedConcurrency);
                if (value <= current || Interlocked.CompareExchange(ref maximumObservedConcurrency, value, current) == current) return;
            }
        }
    }

    private sealed class RecordingLaunchDelay : ILaunchDelayProvider
    {
        public List<TimeSpan> Durations { get; } = [];
        public Task DelayAsync(TimeSpan duration, CancellationToken cancellationToken)
        {
            Durations.Add(duration);
            return Task.CompletedTask;
        }
    }

    private sealed class ControlledLaunchDelay : ILaunchDelayProvider
    {
        private readonly List<TaskCompletionSource> releases = [];
        private readonly SemaphoreSlim callSignal = new(0);
        private readonly object sync = new();
        public List<TimeSpan> Durations { get; } = [];
        public int CallCount { get { lock (sync) return releases.Count; } }

        public async Task DelayAsync(TimeSpan duration, CancellationToken cancellationToken)
        {
            TaskCompletionSource release;
            lock (sync)
            {
                Durations.Add(duration);
                release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                releases.Add(release);
            }
            callSignal.Release();
            await release.Task.WaitAsync(cancellationToken);
        }

        public async Task WaitForCallsAsync(int count)
        {
            while (CallCount < count)
                await callSignal.WaitAsync(TimeSpan.FromSeconds(2));
        }

        public void Release(int index)
        {
            lock (sync) releases[index].TrySetResult();
        }
    }

    private sealed class QueueRandom(params int[] values) : ILaunchSpacingRandom
    {
        private readonly Queue<int> values = new(values);
        public List<(int Minimum, int Maximum)> Ranges { get; } = [];
        public int NextInclusive(int minimumMilliseconds, int maximumMilliseconds)
        {
            Ranges.Add((minimumMilliseconds, maximumMilliseconds));
            return values.Count == 0 ? minimumMilliseconds : values.Dequeue();
        }
    }

}
