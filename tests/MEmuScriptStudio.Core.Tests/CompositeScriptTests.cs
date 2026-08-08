using MEmuScriptStudio.Core.Execution;
using MEmuScriptStudio.Core.MEmu;
using MEmuScriptStudio.Core.Models;
using MEmuScriptStudio.Core.Processes;
using MEmuScriptStudio.Core.Scripts;

namespace MEmuScriptStudio.Core.Tests;

[TestClass]
public sealed class CompositeScriptTests
{
    [TestMethod]
    public void LibraryValidationRejectsNegativeRegularDelayBeforeFormattingOrExecution()
    {
        var script = new ScriptDefinition
        {
            Name = "Invalid regular",
            Steps = [new DelayStep { Name = "Legacy", DurationMilliseconds = -1 }]
        };

        Assert.ThrowsException<InvalidDataException>(() => ScriptLibraryValidator.Validate([script]));
    }

    [TestMethod]
    public void LegacyJsonWithoutKindMigratesToRegularAndKeepsId()
    {
        var id = Guid.NewGuid();
        var json = $$"""{"Id":"{{id}}","Name":"Legacy","Steps":[]}""";
        var script = System.Text.Json.JsonSerializer.Deserialize<ScriptDefinition>(json)!;

        Assert.AreEqual(id, script.Id);
        Assert.AreEqual(ScriptKind.Regular, script.Kind);
        Assert.AreEqual(0, script.CompositeItems.Count);
    }

    [TestMethod]
    public void ValidatorRejectsMissingWrongTypeAndNestedCompositeReferences()
    {
        var regular = new ScriptDefinition { Name = "Regular" };
        var missing = new ScriptDefinition
        {
            Name = "Missing",
            Kind = ScriptKind.Composite,
            CompositeItems = [new ScriptReferenceItem { ScriptId = Guid.NewGuid() }]
        };
        Assert.ThrowsException<InvalidDataException>(() => ScriptLibraryValidator.Validate([regular, missing]));

        var nested = new ScriptDefinition { Name = "Nested", Kind = ScriptKind.Composite };
        var root = new ScriptDefinition
        {
            Name = "Root",
            Kind = ScriptKind.Composite,
            CompositeItems = [new ScriptReferenceItem { ScriptId = nested.Id }]
        };
        var exception = Assert.ThrowsException<InvalidDataException>(() =>
            ScriptLibraryValidator.Validate([regular, nested, root]));
        StringAssert.Contains(exception.Message, "chỉ được tham chiếu kịch bản thường");
    }

    [TestMethod]
    public void ExportClosureAndBundleCopyKeepReferencesConsistent()
    {
        var child = new ScriptDefinition { Name = "Child", Steps = [new NoteStep { Name = "N" }] };
        var composite = new ScriptDefinition
        {
            Name = "Composite",
            Kind = ScriptKind.Composite,
            CompositeItems = [new ScriptReferenceItem { ScriptId = child.Id }]
        };

        var closure = ScriptLibraryValidator.BuildExportClosure([composite], [child, composite]);
        CollectionAssert.AreEquivalent(new[] { child.Id, composite.Id }, closure.Select(script => script.Id).ToArray());

        var copy = ScriptBundleCloner.CloneWithRemappedIds(closure);
        Assert.IsFalse(copy.Select(script => script.Id).Intersect(closure.Select(script => script.Id)).Any());
        var copiedComposite = copy.Single(script => script.Kind == ScriptKind.Composite);
        var copiedChild = copy.Single(script => script.Kind == ScriptKind.Regular);
        Assert.AreEqual(copiedChild.Id, copiedComposite.CompositeItems.OfType<ScriptReferenceItem>().Single().ScriptId);
    }

    [TestMethod]
    public async Task CompositeExecutesDuplicateOccurrencesInOrderWithDistinctRuntimeIdentity()
    {
        var runner = new RecordingRunner();
        var delays = new RecordingDelayProvider();
        var regular = CreateRegularEngine(runner, delays);
        var engine = new CompositeScriptExecutionEngine(regular, delays);
        var child = new ScriptDefinition
        {
            Name = "Child",
            Steps = [new ForceStopStep { Name = "Stop", PackageName = "com.example.app" }]
        };
        var first = new ScriptReferenceItem { ScriptId = child.Id };
        var second = new ScriptReferenceItem { ScriptId = child.Id };
        var delay = new CompositeDelayItem { DurationMilliseconds = 25 };
        var composite = new ScriptDefinition
        {
            Name = "Composite",
            Kind = ScriptKind.Composite,
            CompositeItems = [first, delay, second]
        };

        var result = await engine.ExecuteAsync(Request(composite, child), null, CancellationToken.None);

        Assert.AreEqual(3, result.Steps.Count);
        Assert.AreEqual(2, runner.Requests.Count);
        Assert.AreEqual(1, delays.Durations.Count(duration => duration == TimeSpan.FromMilliseconds(25)));
        var delayResult = result.Steps.Single(step => step.StepId == delay.Id);
        Assert.AreEqual("[Chờ 25 ms]", delayResult.CommandPreview);
        Assert.AreEqual("Composite → Chờ · 25 ms", delayResult.CompositeContext!.DisplayName);
        Assert.AreEqual("Composite → Chờ · 25 ms", delayResult.CompositeContext.FullDisplayName);
        var occurrences = result.Steps.Where(step => step.CompositeContext?.ChildScriptId == child.Id)
            .Select(step => step.CompositeContext!.OccurrenceId).ToList();
        Assert.AreEqual(2, occurrences.Distinct().Count());
    }

    [TestMethod]
    public async Task LegacyChildDelayUsesDurationBasedDisplayNameInCompositeContext()
    {
        var delays = new RecordingDelayProvider();
        var engine = new CompositeScriptExecutionEngine(CreateRegularEngine(new RecordingRunner(), delays), delays);
        var childDelay = new DelayStep { Name = "Tên tùy chỉnh cũ", DurationMilliseconds = 100_000 };
        var child = new ScriptDefinition { Name = "Child", Steps = [childDelay] };
        var composite = new ScriptDefinition
        {
            Name = "Composite",
            Kind = ScriptKind.Composite,
            CompositeItems = [new ScriptReferenceItem { ScriptId = child.Id }]
        };

        var result = await engine.ExecuteAsync(Request(composite, child), null, CancellationToken.None);
        var delayResult = result.Steps.Single();

        Assert.AreEqual("Composite → Child", delayResult.CompositeContext!.DisplayName);
        Assert.AreEqual("Composite → Child → Chờ · 1 phút 40 giây", delayResult.CompositeContext.FullDisplayName);
        Assert.AreEqual("Tên tùy chỉnh cũ", childDelay.Name);
    }

    [TestMethod]
    public async Task CompositeReferenceContinuePolicyStopsOrContinuesButCancellationAlwaysStops()
    {
        var delays = new RecordingDelayProvider();
        var child = new ScriptDefinition
        {
            Name = "Child",
            Steps = [new ForceStopStep { Name = "Stop", PackageName = "com.example.app" }]
        };
        var runner = new RecordingRunner(1, 0);
        var engine = new CompositeScriptExecutionEngine(CreateRegularEngine(runner, delays), delays);
        var composite = new ScriptDefinition
        {
            Name = "Continue",
            Kind = ScriptKind.Composite,
            CompositeItems =
            [
                new ScriptReferenceItem { ScriptId = child.Id, ContinueOnFailure = true },
                new ScriptReferenceItem { ScriptId = child.Id }
            ]
        };
        var result = await engine.ExecuteAsync(Request(composite, child), null, CancellationToken.None);
        Assert.AreEqual(2, runner.Requests.Count);
        Assert.AreEqual(2, result.Steps.Count);

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var cancelled = await engine.ExecuteAsync(Request(composite, child), null, cancellation.Token);
        Assert.IsTrue(cancelled.WasCancelled);
        Assert.AreEqual(0, cancelled.Steps.Count);
    }

    [TestMethod]
    public async Task AdmissionLibrarySnapshotIsUnaffectedByLaterSourceMutation()
    {
        var runner = new RecordingRunner();
        var delays = new RecordingDelayProvider();
        var engine = new CompositeScriptExecutionEngine(CreateRegularEngine(runner, delays), delays);
        var child = new ScriptDefinition
        {
            Name = "Child",
            Steps = [new ForceStopStep { Name = "Original", PackageName = "com.example.original" }]
        };
        var composite = new ScriptDefinition
        {
            Name = "Composite",
            Kind = ScriptKind.Composite,
            CompositeItems = [new ScriptReferenceItem { ScriptId = child.Id }]
        };
        var snapshot = ScriptCloner.ClonePreservingIds(child);
        child.Steps[0] = new ForceStopStep { Name = "Changed", PackageName = "com.example.changed" };

        await engine.ExecuteAsync(new ExecutionRequest
        {
            Script = ScriptCloner.ClonePreservingIds(composite),
            ScriptLibrary = new Dictionary<Guid, ScriptDefinition>
            {
                [composite.Id] = ScriptCloner.ClonePreservingIds(composite),
                [child.Id] = snapshot
            },
            MemucPath = "C:\\MEmu\\memuc.exe",
            InstanceIndex = 2,
            Target = new MemuInstance(2, "Instance 2", true, 102)
        }, null, CancellationToken.None);

        StringAssert.Contains(runner.Requests.Single().Arguments.Last(), "com.example.original");
        Assert.IsFalse(runner.Requests.Single().Arguments.Last().Contains("changed", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task ExecutionValidatesTheAdmittedCompositeInsteadOfAStaleLibraryEntry()
    {
        var delays = new RecordingDelayProvider();
        var engine = new CompositeScriptExecutionEngine(CreateRegularEngine(new RecordingRunner(), delays), delays);
        var child = new ScriptDefinition { Name = "Child" };
        var valid = new ScriptDefinition
        {
            Name = "Composite",
            Kind = ScriptKind.Composite,
            CompositeItems = [new ScriptReferenceItem { ScriptId = child.Id }]
        };
        var invalidAdmission = new ScriptDefinition
        {
            Id = valid.Id,
            Name = valid.Name,
            Kind = ScriptKind.Composite,
            CompositeItems = [new ScriptReferenceItem { ScriptId = Guid.NewGuid() }]
        };

        await Assert.ThrowsExceptionAsync<InvalidDataException>(() => engine.ExecuteAsync(new ExecutionRequest
        {
            Script = invalidAdmission,
            ScriptLibrary = new Dictionary<Guid, ScriptDefinition> { [valid.Id] = valid, [child.Id] = child },
            MemucPath = "C:\\MEmu\\memuc.exe",
            InstanceIndex = 1,
            Target = new MemuInstance(1, "Instance 1", true, 101)
        }, null, CancellationToken.None));
    }

    private static ScriptExecutionEngine CreateRegularEngine(IProcessRunner runner, IDelayProvider delays) =>
        new(runner, new ScriptStepCommandBuilder(new MemuCommandBuilder()), delays);

    private static ExecutionRequest Request(ScriptDefinition composite, ScriptDefinition child) => new()
    {
        Script = composite,
        ScriptLibrary = new Dictionary<Guid, ScriptDefinition> { [composite.Id] = composite, [child.Id] = child },
        MemucPath = "C:\\MEmu\\memuc.exe",
        InstanceIndex = 1,
        Target = new MemuInstance(1, "Instance 1", true, 101)
    };

    private sealed class RecordingRunner(params int[] exitCodes) : IProcessRunner
    {
        private readonly Queue<int> exits = new(exitCodes);
        public List<ProcessRequest> Requests { get; } = [];

        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            var now = DateTimeOffset.UtcNow;
            return Task.FromResult(new ProcessResult(exits.Count == 0 ? 0 : exits.Dequeue(), string.Empty, string.Empty, now, now));
        }
    }

    private sealed class RecordingDelayProvider : IDelayProvider
    {
        public List<TimeSpan> Durations { get; } = [];
        public Task DelayAsync(TimeSpan duration, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Durations.Add(duration);
            return Task.CompletedTask;
        }
    }
}
