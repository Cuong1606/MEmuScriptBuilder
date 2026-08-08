using System.Collections.Concurrent;
using MEmuScriptStudio.Core.Android;
using MEmuScriptStudio.Core.Execution;
using MEmuScriptStudio.Core.MEmu;
using MEmuScriptStudio.Core.Models;
using MEmuScriptStudio.Core.Processes;

namespace MEmuScriptStudio.Core.Tests;

[TestClass]
public sealed class MultiTargetScaleTests
{
    private const string MemucPath = @"C:\MEmu\memuc.exe";
    private const string AdbPath = @"C:\Android\adb.exe";

    [DataTestMethod]
    [DataRow(3)]
    [DataRow(20)]
    [DataRow(50)]
    [DataRow(100)]
    public async Task MemuScaleFixturesUseOneBatchAndPreserveConcurrencySequenceAndIdentity(int targetCount)
    {
        var targets = Enumerable.Range(0, targetCount)
            .Select(index => new MemuInstance(index, $"VM {index:D3}", true, 10_000 + index))
            .ToArray();
        var runner = new FirstStepBarrierRunner(targetCount);
        var regularEngine = new ScriptExecutionEngine(
            runner,
            new ScriptStepCommandBuilder(new MemuCommandBuilder()),
            new ImmediateDelay());
        var resolver = new RecordingBatchResolver();
        var scheduler = new MultiInstanceExecutionScheduler(
            new FixedMemuService(targets),
            regularEngine,
            new ImmediateLaunchDelay(),
            new MinimumRandom(),
            resolver);
        var script = new ScriptDefinition
        {
            Name = "Scale fixture",
            Steps =
            [
                new AndroidShellStep { Name = "First", Command = "echo first" },
                new AndroidShellStep { Name = "Second", Command = "echo second" }
            ]
        };

        using var session = scheduler.Start(new MultiInstanceExecutionRequest
        {
            Script = script,
            MemucPath = MemucPath,
            Targets = targets,
            FixedSpacing = TimeSpan.Zero
        });
        var result = await session.Completion;

        Assert.AreEqual(1, resolver.BatchCallCount);
        Assert.AreEqual(0, resolver.SingleCallCount);
        Assert.AreEqual(targetCount, resolver.LastBatchSize);
        Assert.AreEqual(targetCount * 2, runner.RequestCount);
        Assert.AreEqual(targetCount, runner.FirstStepConcurrentCount);
        Assert.AreEqual(targetCount, result.Instances.Count);
        Assert.AreEqual(targetCount, result.Instances.Select(item => item.Target.TargetKey).Distinct().Count());
        Assert.IsTrue(result.Instances.All(item => item.Status == InstanceExecutionStatus.Succeeded));
        CollectionAssert.AreEquivalent(
            targets.Select(target => target.TargetKey).ToArray(),
            result.Instances.Select(item => item.Target.TargetKey).ToArray());
        Assert.IsTrue(targets.All(target => runner.StageFor(target.Index) == 3));
    }

    [DataTestMethod]
    [DataRow(1)]
    [DataRow(20)]
    [DataRow(100)]
    public async Task AndroidScaleFixturesUseOneTransportSnapshotAndExactSerialRuntimeHealth(int targetCount)
    {
        var targets = Enumerable.Range(0, targetCount)
            .Select(index => AndroidDevice($"SERIAL-{index:D3}"))
            .ToArray();
        var transportDiscovery = new RecordingAndroidTransportDiscovery(targets.Select(Transport).ToArray());
        var androidHealth = new RecordingAndroidStateProbe(string.Empty);
        var scheduler = new MultiInstanceExecutionScheduler(
            new FixedMemuService([]),
            new SuccessfulEngine(),
            new ImmediateLaunchDelay(),
            new MinimumRandom(),
            androidTransportService: transportDiscovery,
            androidStateProbe: androidHealth);

        using var session = scheduler.Start(new MultiInstanceExecutionRequest
        {
            Script = new ScriptDefinition { Name = "Android scale fixture", Steps = [new NoteStep { Name = "Note" }] },
            AdbPath = AdbPath,
            Targets = targets,
            FixedSpacing = TimeSpan.Zero
        });
        var result = await session.Completion;

        Assert.AreEqual(1, transportDiscovery.CallCount);
        Assert.AreEqual(targetCount, result.Instances.Count);
        Assert.IsTrue(result.Instances.All(item => item.Status == InstanceExecutionStatus.Succeeded));
        Assert.AreEqual(targetCount, androidHealth.Serials.Count);
        CollectionAssert.AreEquivalent(
            targets.Select(target => target.Serial).ToArray(),
            androidHealth.Serials.ToArray());
        CollectionAssert.AreEquivalent(
            targets.Select(target => target.TargetKey).ToArray(),
            result.Instances.Select(item => item.Target.TargetKey).ToArray());
    }

    [TestMethod]
    public async Task AndroidTransportStatesAreIsolatedByExactSerialAtAdmission()
    {
        var available = AndroidDevice("SERIAL-A");
        var offline = AndroidDevice("SERIAL-B");
        var unauthorized = AndroidDevice("SERIAL-C");
        var missing = AndroidDevice("SERIAL-MISSING");
        var transportDiscovery = new RecordingAndroidTransportDiscovery(
        [
            Transport(available),
            Transport(offline) with { State = AndroidConnectionState.Offline },
            Transport(unauthorized) with { State = AndroidConnectionState.Unauthorized },
            new AdbDeviceListEntry("SERIAL-OTHER", AndroidConnectionState.Device, null, null, null)
        ]);
        var androidHealth = new RecordingAndroidStateProbe(string.Empty);
        var scheduler = new MultiInstanceExecutionScheduler(
            new FixedMemuService([]),
            new SuccessfulEngine(),
            new ImmediateLaunchDelay(),
            new MinimumRandom(),
            androidTransportService: transportDiscovery,
            androidStateProbe: androidHealth);

        using var session = scheduler.Start(new MultiInstanceExecutionRequest
        {
            Script = new ScriptDefinition { Name = "Transport states", Steps = [new NoteStep { Name = "Note" }] },
            AdbPath = AdbPath,
            Targets = [available, offline, unauthorized, missing],
            FixedSpacing = TimeSpan.Zero
        });
        var result = await session.Completion;

        Assert.AreEqual(1, transportDiscovery.CallCount);
        Assert.AreEqual(InstanceExecutionStatus.Succeeded, StatusFor(result, available.TargetKey));
        Assert.AreEqual(InstanceExecutionStatus.Unavailable, StatusFor(result, offline.TargetKey));
        Assert.AreEqual(InstanceExecutionStatus.Unavailable, StatusFor(result, unauthorized.TargetKey));
        Assert.AreEqual(InstanceExecutionStatus.Unavailable, StatusFor(result, missing.TargetKey));
        CollectionAssert.AreEqual(new[] { available.Serial }, androidHealth.Serials.ToArray());
    }

    [TestMethod]
    public async Task MixedScaleFailureIsolationKeepsDiscoveryAndHealthPerProviderBounded()
    {
        const int memuCount = 10;
        const int androidCount = 10;
        var memuTargets = Enumerable.Range(0, memuCount)
            .Select(index => new MemuInstance(index, $"VM {index:D2}", true, 10_000 + index))
            .ToArray();
        var androidTargets = Enumerable.Range(0, androidCount)
            .Select(index => AndroidDevice($"SERIAL-{index:D2}"))
            .ToArray();
        IExecutionTarget[] targets = [.. memuTargets, .. androidTargets];
        var resolver = new RecordingBatchResolver();
        var deadMemuIndex = 4;
        var offlineSerial = "SERIAL-06";
        var pinnedHealth = new SelectivePinnedHealthCheck(deadMemuIndex);
        var androidDiscovery = new RecordingAndroidTransportDiscovery(androidTargets.Select(Transport).ToArray());
        var androidHealth = new RecordingAndroidStateProbe(offlineSerial);
        var scheduler = new MultiInstanceExecutionScheduler(
            new FixedMemuService(memuTargets),
            new SuccessfulEngine(),
            new ImmediateLaunchDelay(),
            new MinimumRandom(),
            resolver,
            pinnedHealth,
            androidDiscovery,
            androidHealth);

        using var session = scheduler.Start(new MultiInstanceExecutionRequest
        {
            Script = new ScriptDefinition { Name = "Mixed fixture", Steps = [new NoteStep { Name = "Note" }] },
            MemucPath = MemucPath,
            AdbPath = AdbPath,
            Targets = targets,
            FixedSpacing = TimeSpan.Zero
        });
        var result = await session.Completion;

        Assert.AreEqual(1, resolver.BatchCallCount);
        Assert.AreEqual(memuCount, resolver.LastBatchSize);
        Assert.AreEqual(1, androidDiscovery.CallCount);
        Assert.AreEqual(androidCount, androidHealth.Serials.Count);
        CollectionAssert.AreEquivalent(
            androidTargets.Select(target => target.Serial).ToArray(),
            androidHealth.Serials.ToArray());
        Assert.AreEqual(InstanceExecutionStatus.Unavailable, StatusFor(result, $"memu:{deadMemuIndex}"));
        Assert.AreEqual(InstanceExecutionStatus.Unavailable, StatusFor(result, $"android-adb:{offlineSerial}"));
        Assert.IsTrue(result.Instances
            .Where(item => item.Target.TargetKey != $"memu:{deadMemuIndex}" &&
                           item.Target.TargetKey != $"android-adb:{offlineSerial}")
            .All(item => item.Status == InstanceExecutionStatus.Succeeded));
    }

    [TestMethod]
    public async Task StopSubsetCancelsOnlySelectedRunningTargetsAndLeavesNoOpenTerminalAdmission()
    {
        const int targetCount = 20;
        var targets = Enumerable.Range(0, targetCount)
            .Select(index => new MemuInstance(index, $"VM {index:D2}", true, 10_000 + index))
            .ToArray();
        var engine = new SubsetGateEngine(targetCount);
        var resolver = new RecordingBatchResolver();
        var scheduler = new MultiInstanceExecutionScheduler(
            new FixedMemuService(targets),
            engine,
            new ImmediateLaunchDelay(),
            new MinimumRandom(),
            resolver);
        using var session = scheduler.Start(new MultiInstanceExecutionRequest
        {
            Script = new ScriptDefinition { Name = "Stop subset", Steps = [new NoteStep { Name = "Note" }] },
            MemucPath = MemucPath,
            Targets = targets,
            FixedSpacing = TimeSpan.Zero
        });
        await engine.AllStarted.Task;
        var stoppedKeys = targets
            .Where(target => target.Index % 4 == 0)
            .Select(target => target.TargetKey)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var targetKey in stoppedKeys) Assert.IsTrue(session.StopTarget(targetKey));
        engine.Release.TrySetResult();
        var result = await session.Completion;

        Assert.AreEqual(1, resolver.BatchCallCount);
        Assert.AreEqual(targetCount, result.Instances.Count);
        Assert.IsTrue(result.Instances.All(item => item.Status ==
            (stoppedKeys.Contains(item.Target.TargetKey)
                ? InstanceExecutionStatus.Cancelled
                : InstanceExecutionStatus.Succeeded)));
        Assert.IsTrue(targets.All(target => !session.StopTarget(target.TargetKey)));
    }

    private static InstanceExecutionStatus StatusFor(MultiInstanceExecutionResult result, string targetKey) =>
        result.Instances.Single(item => item.Target.TargetKey == targetKey).Status;

    private static AndroidAdbDevice AndroidDevice(string serial) => new(
        serial,
        "Xiaomi",
        "Redmi",
        "10",
        29,
        720,
        1600,
        320,
        0,
        AndroidConnectionState.Device);

    private sealed class RecordingBatchResolver : IMemuCoreIdentityResolver
    {
        private int batchCallCount;
        private int singleCallCount;
        public int BatchCallCount => Volatile.Read(ref batchCallCount);
        public int SingleCallCount => Volatile.Read(ref singleCallCount);
        public int LastBatchSize { get; private set; }

        public Task<MemuInstanceHealthResult> ResolveAsync(
            MemuInstance instance,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref singleCallCount);
            throw new AssertFailedException("Scheduler must use the batch resolver path.");
        }

        public Task<IReadOnlyDictionary<int, MemuInstanceHealthResult>> ResolveBatchAsync(
            IReadOnlyList<MemuInstance> instances,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref batchCallCount);
            LastBatchSize = instances.Count;
            return Task.FromResult<IReadOnlyDictionary<int, MemuInstanceHealthResult>>(
                instances.ToDictionary(
                    instance => instance.Index,
                    instance => MemuInstanceHealthResult.HealthyFor(
                        new MemuInstanceCoreIdentity(
                            20_000 + instance.Index,
                            30_000 + instance.Index,
                            $"MEmu_{instance.Index}"))));
        }
    }

    private sealed class FirstStepBarrierRunner(int targetCount) : IProcessRunner
    {
        private readonly ConcurrentDictionary<int, int> stages = [];
        private readonly TaskCompletionSource allFirstStepsStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int firstStepConcurrentCount;
        private int requestCount;

        public int FirstStepConcurrentCount => Volatile.Read(ref firstStepConcurrentCount);
        public int RequestCount => Volatile.Read(ref requestCount);
        public int StageFor(int instanceIndex) => stages.GetValueOrDefault(instanceIndex);

        public async Task<ProcessResult> RunAsync(
            ProcessRequest request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref requestCount);
            var instanceIndex = int.Parse(request.Arguments[1], System.Globalization.CultureInfo.InvariantCulture);
            var stage = stages.AddOrUpdate(instanceIndex, 1, static (_, current) => current + 1);
            if (stage == 1)
            {
                if (Interlocked.Increment(ref firstStepConcurrentCount) == targetCount)
                    allFirstStepsStarted.TrySetResult();
                await allFirstStepsStarted.Task.WaitAsync(cancellationToken);
                stages[instanceIndex] = 2;
            }
            else
            {
                Assert.AreEqual(3, stage, "Step 2 must start only after step 1 completed on the same target.");
            }

            var now = DateTimeOffset.UtcNow;
            return new ProcessResult(0, string.Empty, string.Empty, now, now);
        }
    }

    private sealed class FixedMemuService(IReadOnlyList<MemuInstance> instances) : IMemuInstanceService
    {
        public Task<IReadOnlyList<MemuInstance>> GetInstancesAsync(
            string memucPath,
            CancellationToken cancellationToken) => Task.FromResult(instances);
    }

    private static AdbDeviceListEntry Transport(AndroidAdbDevice device) =>
        new(device.Serial, device.ConnectionState, device.Product, device.Model, device.Device);

    private sealed class RecordingAndroidTransportDiscovery(IReadOnlyList<AdbDeviceListEntry> transports)
        : IAndroidAdbTransportService
    {
        private int callCount;
        public int CallCount => Volatile.Read(ref callCount);

        public Task<IReadOnlyList<AdbDeviceListEntry>> GetTransportsAsync(
            string adbPath,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref callCount);
            return Task.FromResult(transports);
        }
    }

    private sealed class RecordingAndroidStateProbe(string offlineSerial) : IAndroidAdbStateProbe
    {
        public ConcurrentBag<string> Serials { get; } = [];

        public Task<AndroidAdbStateResult> CheckStateAsync(
            string adbPath,
            string serial,
            CancellationToken cancellationToken)
        {
            Serials.Add(serial);
            return Task.FromResult(serial == offlineSerial
                ? new AndroidAdbStateResult(AndroidConnectionState.Offline, "offline")
                : new AndroidAdbStateResult(AndroidConnectionState.Device));
        }
    }

    private sealed class SelectivePinnedHealthCheck(int unavailableInstanceIndex) : IPinnedMemuCoreHealthCheck
    {
        public Task<MemuInstanceHealthResult> CheckAsync(
            MemuInstance instance,
            MemuInstanceCoreIdentity expectedCoreIdentity,
            string checkpoint,
            CancellationToken cancellationToken) =>
            Task.FromResult(instance.Index == unavailableInstanceIndex
                ? MemuInstanceHealthResult.Unavailable("core exited")
                : MemuInstanceHealthResult.HealthyFor(expectedCoreIdentity));
    }

    private sealed class SuccessfulEngine : IScriptExecutionEngine
    {
        public Task<ExecutionResult> ExecuteAsync(
            ExecutionRequest request,
            IProgress<StepExecutionUpdate>? progress,
            CancellationToken cancellationToken)
        {
            var now = DateTimeOffset.UtcNow;
            return Task.FromResult(new ExecutionResult { StartedAt = now, EndedAt = now });
        }
    }

    private sealed class SubsetGateEngine(int targetCount) : IScriptExecutionEngine
    {
        private int startedCount;
        public TaskCompletionSource AllStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<ExecutionResult> ExecuteAsync(
            ExecutionRequest request,
            IProgress<StepExecutionUpdate>? progress,
            CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref startedCount) == targetCount) AllStarted.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            var now = DateTimeOffset.UtcNow;
            return new ExecutionResult { StartedAt = now, EndedAt = now };
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

    private sealed class MinimumRandom : ILaunchSpacingRandom
    {
        public int NextInclusive(int minimumMilliseconds, int maximumMilliseconds) => minimumMilliseconds;
    }
}
