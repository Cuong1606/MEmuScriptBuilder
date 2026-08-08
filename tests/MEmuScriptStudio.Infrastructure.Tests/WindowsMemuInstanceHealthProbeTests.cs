using MEmuScriptStudio.Core.Execution;
using MEmuScriptStudio.Core.MEmu;
using MEmuScriptStudio.Core.Models;
using MEmuScriptStudio.Core.Processes;
using MEmuScriptStudio.Infrastructure.MEmu;

namespace MEmuScriptStudio.Infrastructure.Tests;

[TestClass]
public sealed class WindowsMemuInstanceHealthProbeTests
{
    private const long ObservedCoreStartTime = 134306563187907103;

    [TestMethod]
    public async Task Resolver_PrimaryCommandLineReaderSuccess_ResolvesObservedMEmu1Topology()
    {
        var metadata = new FallbackWindowsProcessCommandLineMetadataProvider(
            new FixedCommandLineReader(new Dictionary<int, ProcessCommandLineMetadata>
            {
                [22160] = Success(HostCommand("MEmu_1")),
                [5152] = Success(HeadlessCommand("MEmu_1", ObservedVmId))
            }),
            new FixedCommandLineReader(defaultResult: Failure("fallback must not run")));
        var resolver = Resolver(RealMEmu1Layout(), metadata);

        var result = await resolver.ResolveAsync(Instance(1, "MEmu - 1", 22160), CancellationToken.None);

        Assert.AreEqual(MemuInstanceHealthStatus.Healthy, result.Status);
        Assert.AreEqual(5152, result.CoreProcessId);
        Assert.AreEqual(ObservedCoreStartTime, result.CoreIdentity?.CreationTimeUtcFileTime);
        Assert.AreEqual("MEmu_1", result.CoreIdentity?.VerifiedInstanceIdentity);
    }

    [TestMethod]
    public async Task Resolver_PrimaryFailsAndWmiFallbackSucceeds_ResolvesObservedMEmu1Topology()
    {
        var metadata = new FallbackWindowsProcessCommandLineMetadataProvider(
            new FixedCommandLineReader(defaultResult: new ProcessCommandLineMetadata(
                null,
                "NT_QUERY_INFORMATION_PROCESS",
                "NT_QUERY_COMMAND_LINE_FAILED",
                "NtStatus=0xC0000001")),
            new FixedCommandLineReader(new Dictionary<int, ProcessCommandLineMetadata>
            {
                [22160] = Success(HostCommand("MEmu_1"), "WMI_WIN32_PROCESS"),
                [5152] = Success(HeadlessCommand("MEmu_1", ObservedVmId), "WMI_WIN32_PROCESS")
            }));
        var diagnostics = new RecordingHealthLogger();
        var resolver = Resolver(RealMEmu1Layout(), metadata, diagnostics);

        var result = await resolver.ResolveAsync(Instance(1, "MEmu - 1", 22160), CancellationToken.None);

        Assert.AreEqual(MemuInstanceHealthStatus.Healthy, result.Status);
        Assert.AreEqual(5152, result.CoreProcessId);
        Assert.AreEqual("COMMAND_LINE_FALLBACK_USED", diagnostics.Items.Single().ReasonCode);
        StringAssert.Contains(diagnostics.Items.Single().ResolverSource, "WMI_WIN32_PROCESS");
    }

    [TestMethod]
    public async Task Resolver_MultipleEmulators_MapsEachExactCommentIdentity()
    {
        var snapshot = new[]
        {
            Process(22160, 18056, "MEmu.exe"),
            Process(23160, 18056, "MEmu.exe"),
            Process(5152, 6004, "MEmuHeadless.exe", ObservedCoreStartTime),
            Process(6152, 6004, "MEmuHeadless.exe", ObservedCoreStartTime + 1),
            Process(6004, 1064, "MEmuSVC.exe")
        };
        var metadata = new FixedMetadataProvider(new Dictionary<int, ProcessCommandLineMetadata>
        {
            [22160] = Success(HostCommand("MEmu_1")),
            [23160] = Success(HostCommand("MEmu_2")),
            [5152] = Success(HeadlessCommand("MEmu_1", ObservedVmId)),
            [6152] = Success(HeadlessCommand("MEmu_2", "20260806-bbbb-bbbb-bbbb-000000000002"))
        });
        var resolver = Resolver(snapshot, metadata);

        var first = await resolver.ResolveAsync(Instance(1, "MEmu - 1", 22160), CancellationToken.None);
        var second = await resolver.ResolveAsync(Instance(2, "MEmu - 2", 23160), CancellationToken.None);

        Assert.AreEqual(5152, first.CoreProcessId);
        Assert.AreEqual(6152, second.CoreProcessId);
    }

    [TestMethod]
    public async Task Resolver_OnlyAnotherInstancesHeadless_DoesNotFalseMatch()
    {
        var resolver = Resolver(
            [Process(22160, 18056, "MEmu.exe"), Process(6152, 6004, "MEmuHeadless.exe", ObservedCoreStartTime)],
            new FixedMetadataProvider(new Dictionary<int, ProcessCommandLineMetadata>
            {
                [22160] = Success(HostCommand("MEmu_1")),
                [6152] = Success(HeadlessCommand("MEmu_2", "other-vm"))
            }));

        var result = await resolver.ResolveAsync(Instance(1, "MEmu - 1", 22160), CancellationToken.None);

        Assert.AreEqual(MemuInstanceHealthStatus.Unknown, result.Status);
        Assert.IsNull(result.CoreIdentity);
    }

    [TestMethod]
    public async Task Resolver_AnyUnreadableHeadlessMetadata_ReturnsUnknownInsteadOfFalseUniqueMatch()
    {
        var resolver = Resolver(
            [
                Process(22160, 18056, "MEmu.exe"),
                Process(5152, 6004, "MEmuHeadless.exe", ObservedCoreStartTime),
                Process(6152, 6004, "MEmuHeadless.exe", ObservedCoreStartTime + 1)
            ],
            new FixedMetadataProvider(new Dictionary<int, ProcessCommandLineMetadata>
            {
                [22160] = Success(HostCommand("MEmu_1")),
                [5152] = Success(HeadlessCommand("MEmu_1", ObservedVmId)),
                [6152] = new(null, "NATIVE+WMI", "COMMAND_LINE_METADATA_FAILED", "both readers failed")
            }));

        var result = await resolver.ResolveAsync(Instance(1, "MEmu - 1", 22160), CancellationToken.None);

        Assert.AreEqual(MemuInstanceHealthStatus.Unknown, result.Status);
        StringAssert.Contains(result.Diagnostic, "6152");
    }

    [TestMethod]
    public async Task Resolver_BlockedFallbackTimesOutWithoutHoldingAdmissionIndefinitely()
    {
        var metadata = new BlockingMetadataProvider();
        var diagnostics = new RecordingHealthLogger();
        var resolver = new WindowsMemuCoreIdentityResolver(
            new FixedSnapshotProvider(RealMEmu1Layout()),
            metadata,
            diagnostics,
            TimeSpan.FromMilliseconds(100));

        try
        {
            var resolutionTask = resolver.ResolveAsync(Instance(1, "MEmu - 1", 22160), CancellationToken.None);
            await metadata.Started.Task.WaitAsync(TimeSpan.FromSeconds(1));

            var result = await resolutionTask.WaitAsync(TimeSpan.FromSeconds(1));

            Assert.AreEqual(MemuInstanceHealthStatus.Unknown, result.Status);
            Assert.AreEqual("RESOLVER_TIMEOUT", diagnostics.Items.Single().ReasonCode);
        }
        finally
        {
            metadata.Release.TrySetResult();
        }
    }

    [TestMethod]
    public async Task PinnedCheck_ExitedCoreIsUnavailableEvenWhenReplacementMatchesInstance()
    {
        var check = new WindowsPinnedMemuCoreHealthCheck(new FixedSnapshotProvider(
        [
            Process(22160, 18056, "MEmu.exe"),
            Process(5252, 6004, "MEmuHeadless.exe", ObservedCoreStartTime + 1)
        ]));
        var pinned = new MemuInstanceCoreIdentity(5152, ObservedCoreStartTime, "MEmu_1");

        var result = await check.CheckAsync(
            Instance(1, "MEmu_1", 22160), pinned, "AfterDelay", CancellationToken.None);

        Assert.AreEqual(MemuInstanceHealthStatus.Unavailable, result.Status);
    }

    [TestMethod]
    public async Task PinnedCheck_ReusedPidIsUnavailable()
    {
        var check = new WindowsPinnedMemuCoreHealthCheck(new FixedSnapshotProvider(
            [Process(5152, 6004, "MEmuHeadless.exe", ObservedCoreStartTime + 1)]));
        var pinned = new MemuInstanceCoreIdentity(5152, ObservedCoreStartTime, "MEmu_1");

        var result = await check.CheckAsync(
            Instance(1, "MEmu_1", 22160), pinned, "FinalSuccessGate", CancellationToken.None);

        Assert.AreEqual(MemuInstanceHealthStatus.Unavailable, result.Status);
    }

    [TestMethod]
    public async Task Scheduler_HealthyObservedLayoutPreflightPinsAndRunsFirstStep()
    {
        var instance = Instance(1, "MEmu - 1", 22160);
        var snapshot = new FixedSnapshotProvider(RealMEmu1Layout());
        var resolver = new WindowsMemuCoreIdentityResolver(
            snapshot,
            new FixedMetadataProvider(new Dictionary<int, ProcessCommandLineMetadata>
            {
                [22160] = Success(HostCommand("MEmu_1")),
                [5152] = Success(HeadlessCommand("MEmu_1", ObservedVmId))
            }));
        var pinnedCheck = new WindowsPinnedMemuCoreHealthCheck(snapshot);
        var runner = new RecordingRunner();
        var commandBuilder = new ScriptStepCommandBuilder(new MemuCommandBuilder());
        var regularEngine = new ScriptExecutionEngine(runner, commandBuilder, new ImmediateDelay(), pinnedCoreHealthCheck: pinnedCheck);
        var executionEngine = new CompositeScriptExecutionEngine(regularEngine, new ImmediateDelay(), pinnedCheck);
        var scheduler = new MultiInstanceExecutionScheduler(
            new FixedInstanceService([instance]),
            executionEngine,
            new ImmediateLaunchDelay(),
            new MinimumLaunchRandom(),
            resolver,
            pinnedCheck);
        using var session = scheduler.Start(new MultiInstanceExecutionRequest
        {
            Script = new ScriptDefinition
            {
                Name = "Real topology",
                Steps = [new AndroidShellStep { Name = "First", Command = "echo ok" }]
            },
            MemucPath = @"C:\MEmu\memuc.exe",
            Targets = [instance],
            LaunchSpacingMode = LaunchSpacingMode.Fixed
        });

        var result = await session.Completion;

        Assert.AreEqual(InstanceExecutionStatus.Succeeded, result.Instances.Single().Status);
        Assert.AreEqual(1, runner.Requests.Count);
    }

    [TestMethod]
    public void NativeCommandLineReader_ReadsCurrentProcessWithDetailedSuccessReason()
    {
        var result = new NativeWindowsProcessCommandLineReader().Read(Environment.ProcessId);
        var creation = ToolHelpProcessSnapshotProvider.ReadCreationTime(Environment.ProcessId);

        Assert.IsTrue(result.Succeeded, result.Detail);
        Assert.AreEqual("COMMAND_LINE_PRIMARY_SUCCESS", result.ReasonCode);
        Assert.IsTrue(creation.CreationTimeUtcFileTime > 0, creation.FailureReason);
    }

    private const string ObservedVmId = "20260806-aaaa-aaaa-aaaa-000000000001";

    private static WindowsMemuCoreIdentityResolver Resolver(
        IReadOnlyList<WindowsProcessSnapshotEntry> snapshot,
        IWindowsProcessCommandLineMetadataProvider metadata,
        IMemuHealthDiagnosticLogger? logger = null) =>
        new(new FixedSnapshotProvider(snapshot), metadata, logger);

    private static WindowsProcessSnapshotEntry[] RealMEmu1Layout() =>
    [
        Process(22160, 18056, "MEmu.exe"),
        Process(18056, 6528, "MEmuConsole.exe"),
        Process(5152, 6004, "MEmuHeadless.exe", ObservedCoreStartTime),
        Process(6004, 1064, "MEmuSVC.exe")
    ];

    private static string HeadlessCommand(string instanceName, string vmId) =>
        $@"E:\Microvirt\MEmuHyperv\MEmuHeadless.exe --comment {instanceName} --startvm {vmId} --vrde off";

    private static string HostCommand(string instanceIdentity) =>
        $"\"E:/Microvirt/MEmu/MEmu.exe\" {instanceIdentity}";

    private static ProcessCommandLineMetadata Success(string commandLine, string source = "NT_QUERY_INFORMATION_PROCESS") =>
        new(commandLine, source, "COMMAND_LINE_PRIMARY_SUCCESS");

    private static ProcessCommandLineMetadata Failure(string detail) =>
        new(null, "TEST", "COMMAND_LINE_METADATA_FAILED", detail);

    private static MemuInstance Instance(int index, string name, int processId) =>
        new(index, name, true, processId);

    private static WindowsProcessSnapshotEntry Process(
        int processId,
        int parentProcessId,
        string executableName,
        long? creationTime = null) =>
        new(processId, parentProcessId, executableName, creationTime);

    private sealed class FixedSnapshotProvider(IReadOnlyList<WindowsProcessSnapshotEntry> snapshot)
        : IWindowsProcessSnapshotProvider
    {
        public IReadOnlyList<WindowsProcessSnapshotEntry> Capture() => snapshot;
    }

    private sealed class FixedMetadataProvider(IReadOnlyDictionary<int, ProcessCommandLineMetadata> metadata)
        : IWindowsProcessCommandLineMetadataProvider
    {
        public ProcessCommandLineMetadata Read(int processId) => metadata[processId];
    }

    private sealed class FixedCommandLineReader : IWindowsProcessCommandLineReader
    {
        private readonly IReadOnlyDictionary<int, ProcessCommandLineMetadata>? results;
        private readonly ProcessCommandLineMetadata? defaultResult;

        public FixedCommandLineReader(
            IReadOnlyDictionary<int, ProcessCommandLineMetadata>? results = null,
            ProcessCommandLineMetadata? defaultResult = null)
        {
            this.results = results;
            this.defaultResult = defaultResult;
        }

        public ProcessCommandLineMetadata Read(int processId) =>
            results is not null && results.TryGetValue(processId, out var result)
                ? result
                : defaultResult ?? Failure($"No fixture for PID {processId}");
    }

    private sealed class BlockingMetadataProvider : IWindowsProcessCommandLineMetadataProvider
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ProcessCommandLineMetadata Read(int processId)
        {
            Started.TrySetResult();
            Release.Task.GetAwaiter().GetResult();
            return Failure("released blocked fallback");
        }
    }

    private sealed class RecordingHealthLogger : IMemuHealthDiagnosticLogger
    {
        public List<MemuHealthDiagnostic> Items { get; } = [];
        public void Write(MemuHealthDiagnostic diagnostic) => Items.Add(diagnostic);
    }

    private sealed class FixedInstanceService(IReadOnlyList<MemuInstance> instances) : IMemuInstanceService
    {
        public Task<IReadOnlyList<MemuInstance>> GetInstancesAsync(
            string memucPath,
            CancellationToken cancellationToken) => Task.FromResult(instances);
    }

    private sealed class RecordingRunner : IProcessRunner
    {
        public List<ProcessRequest> Requests { get; } = [];

        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            var now = DateTimeOffset.UtcNow;
            return Task.FromResult(new ProcessResult(0, "ok", string.Empty, now, now));
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
}
