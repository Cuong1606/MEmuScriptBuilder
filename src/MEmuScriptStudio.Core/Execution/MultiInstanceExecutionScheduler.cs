using MEmuScriptStudio.Core.MEmu;
using MEmuScriptStudio.Core.Models;
using MEmuScriptStudio.Core.Scripts;

namespace MEmuScriptStudio.Core.Execution;

public interface ILaunchDelayProvider
{
    Task DelayAsync(TimeSpan duration, CancellationToken cancellationToken);
}

public sealed class LaunchDelayProvider : ILaunchDelayProvider
{
    public Task DelayAsync(TimeSpan duration, CancellationToken cancellationToken) =>
        Task.Delay(duration, cancellationToken);
}

public interface ILaunchSpacingRandom
{
    int NextInclusive(int minimumMilliseconds, int maximumMilliseconds);
}

public sealed class LaunchSpacingRandom : ILaunchSpacingRandom
{
    public int NextInclusive(int minimumMilliseconds, int maximumMilliseconds) =>
        (int)Random.Shared.NextInt64(minimumMilliseconds, (long)maximumMilliseconds + 1);
}

public interface IMultiInstanceExecutionScheduler
{
    MultiInstanceExecutionSession Start(
        MultiInstanceExecutionRequest request,
        IProgress<InstanceExecutionUpdate>? progress = null);
}

public sealed class MultiInstanceExecutionSession : IDisposable
{
    private readonly object lifecycleSync = new();
    private readonly CancellationTokenSource batchCancellation = new();
    private readonly IReadOnlyDictionary<int, CancellationTokenSource> instanceCancellations;
    private readonly HashSet<int> stopRequested = [];
    private readonly HashSet<int> terminalCommitted = [];
    private bool disposed;

    internal MultiInstanceExecutionSession(IEnumerable<int> instanceIndices)
    {
        instanceCancellations = instanceIndices.ToDictionary(index => index, _ => new CancellationTokenSource());
    }

    public Task<MultiInstanceExecutionResult> Completion { get; internal set; } =
        Task.FromResult(new MultiInstanceExecutionResult());

    internal CancellationToken BatchToken => batchCancellation.Token;

    internal CancellationToken GetInstanceToken(int instanceIndex) =>
        instanceCancellations[instanceIndex].Token;

    public bool StopInstance(int instanceIndex, Action? onAccepted = null)
    {
        CancellationTokenSource cancellation;
        lock (lifecycleSync)
        {
            if (disposed || terminalCommitted.Contains(instanceIndex) || stopRequested.Contains(instanceIndex) ||
                !instanceCancellations.TryGetValue(instanceIndex, out cancellation!)) return false;
            stopRequested.Add(instanceIndex);
            onAccepted?.Invoke();
        }
        try { cancellation.Cancel(); }
        catch (ObjectDisposedException) { }
        return true;
    }

    public IReadOnlySet<int> StopAll(Action<int>? onAccepted = null)
    {
        HashSet<int> accepted;
        lock (lifecycleSync)
        {
            if (disposed) return new HashSet<int>();
            accepted = instanceCancellations.Keys
                .Where(index => !terminalCommitted.Contains(index) && !stopRequested.Contains(index))
                .ToHashSet();
            stopRequested.UnionWith(accepted);
            foreach (var index in accepted) onAccepted?.Invoke(index);
        }
        try { batchCancellation.Cancel(); }
        catch (ObjectDisposedException) { }
        return accepted;
    }

    internal InstanceExecutionStatus CommitTerminal(
        int instanceIndex,
        InstanceExecutionStatus intendedStatus)
    {
        lock (lifecycleSync)
        {
            var cancellationWon = stopRequested.Contains(instanceIndex) ||
                batchCancellation.IsCancellationRequested ||
                instanceCancellations[instanceIndex].IsCancellationRequested;
            terminalCommitted.Add(instanceIndex);
            return cancellationWon ? InstanceExecutionStatus.Cancelled : intendedStatus;
        }
    }

    public void Dispose()
    {
        lock (lifecycleSync)
        {
            if (disposed) return;
            disposed = true;
        }
        batchCancellation.Dispose();
        foreach (var cancellation in instanceCancellations.Values) cancellation.Dispose();
    }
}

public sealed class MultiInstanceExecutionScheduler(
    IMemuInstanceService instanceService,
    IScriptExecutionEngine executionEngine,
    ILaunchDelayProvider launchDelayProvider,
    ILaunchSpacingRandom launchSpacingRandom,
    IMemuCoreIdentityResolver? coreIdentityResolver = null,
    IPinnedMemuCoreHealthCheck? pinnedCoreHealthCheck = null) : IMultiInstanceExecutionScheduler
{
    public MultiInstanceExecutionSession Start(
        MultiInstanceExecutionRequest request,
        IProgress<InstanceExecutionUpdate>? progress = null)
    {
        Validate(request);
        var session = new MultiInstanceExecutionSession(request.Targets.Select(target => target.Index));
        session.Completion = ExecuteAsync(request, progress, session);
        return session;
    }

    private async Task<MultiInstanceExecutionResult> ExecuteAsync(
        MultiInstanceExecutionRequest request,
        IProgress<InstanceExecutionUpdate>? progress,
        MultiInstanceExecutionSession session)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var results = new Dictionary<int, InstanceExecutionResult>();
        foreach (var target in request.Targets)
        {
            var script = ResolveScript(request, target.Index);
            progress?.Report(CreateUpdate(request.LaunchGroupId, target, script, InstanceExecutionStatus.Queued));
        }

        IReadOnlyList<MemuInstance> currentInstances;
        try
        {
            currentInstances = await instanceService
                .GetInstancesAsync(request.MemucPath, session.BatchToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (session.BatchToken.IsCancellationRequested)
        {
            AddCancelledResults(request, request.Targets, results, progress, session, "Đã dừng trước khi hoàn tất kiểm tra giả lập.");
            return CreateResult(request.LaunchGroupId, startedAt, results, wasCancelled: true);
        }
        catch (Exception exception)
        {
            foreach (var target in request.Targets)
            {
                if (session.BatchToken.IsCancellationRequested ||
                    session.GetInstanceToken(target.Index).IsCancellationRequested)
                {
                    AddCancelledResult(request, target, results, progress, session, "Đã dừng trước khi khởi chạy.");
                    continue;
                }

                var script = ResolveScript(request, target.Index);
                var status = session.CommitTerminal(target.Index, InstanceExecutionStatus.Failed);
                var message = status == InstanceExecutionStatus.Cancelled
                    ? "Đã dừng trước khi khởi chạy."
                    : exception.Message;
                results[target.Index] = new InstanceExecutionResult
                {
                    LaunchGroupId = request.LaunchGroupId,
                    Target = target,
                    ScriptId = script.Id,
                    ScriptName = script.Name,
                    Status = status,
                    Message = message
                };
                progress?.Report(CreateUpdate(
                    request.LaunchGroupId,
                    target,
                    script,
                    status,
                    message: message));
            }
            return CreateResult(
                request.LaunchGroupId,
                startedAt,
                results,
                wasCancelled: session.BatchToken.IsCancellationRequested);
        }

        var currentByIndex = currentInstances
            .GroupBy(instance => instance.Index)
            .ToDictionary(group => group.Key, group => group.ToList());
        var validTargets = new List<MemuInstance>();
        var expectedCoreIdentities = new Dictionary<int, MemuInstanceCoreIdentity>();
        foreach (var requestedTarget in request.Targets)
        {
            if (session.BatchToken.IsCancellationRequested ||
                session.GetInstanceToken(requestedTarget.Index).IsCancellationRequested)
            {
                AddCancelledResult(request, requestedTarget, results, progress, session, "Đã dừng trước khi khởi chạy.");
                continue;
            }

            if (requestedTarget.Index >= 0 &&
                currentByIndex.TryGetValue(requestedTarget.Index, out var matches) &&
                matches.Count == 1 && matches[0].IsRunning)
            {
                var currentTarget = matches[0];
                MemuInstanceHealthResult health;
                using var preflightCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                    session.BatchToken,
                    session.GetInstanceToken(requestedTarget.Index));
                try
                {
                    health = await ResolveCoreIdentityAsync(currentTarget, preflightCancellation.Token)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (
                    session.BatchToken.IsCancellationRequested ||
                    session.GetInstanceToken(requestedTarget.Index).IsCancellationRequested)
                {
                    AddCancelledResult(request, requestedTarget, results, progress, session, "Đã dừng trước khi khởi chạy.");
                    continue;
                }

                if (session.BatchToken.IsCancellationRequested ||
                    session.GetInstanceToken(requestedTarget.Index).IsCancellationRequested)
                {
                    AddCancelledResult(request, requestedTarget, results, progress, session, "Đã dừng trước khi khởi chạy.");
                    continue;
                }

                if (health.Status == MemuInstanceHealthStatus.Unavailable)
                {
                    AddUnavailableResult(request, currentTarget, results, progress, session, MemuInstanceHealthChecks.UnavailableMessage);
                    continue;
                }

                if (health.Status != MemuInstanceHealthStatus.Healthy || health.CoreIdentity is null)
                {
                    AddFailedResult(request, currentTarget, results, progress, session, MemuInstanceHealthChecks.UnknownMessage);
                    continue;
                }

                expectedCoreIdentities[currentTarget.Index] = health.CoreIdentity;
                validTargets.Add(currentTarget);
                continue;
            }

            if (session.BatchToken.IsCancellationRequested ||
                session.GetInstanceToken(requestedTarget.Index).IsCancellationRequested)
            {
                AddCancelledResult(request, requestedTarget, results, progress, session, "Đã dừng trước khi khởi chạy.");
                continue;
            }

            const string message = "Giả lập đang tắt, đã mất hoặc không hợp lệ tại preflight; không tự khởi động.";
            AddUnavailableResult(request, requestedTarget, results, progress, session, message);
        }

        if (session.BatchToken.IsCancellationRequested)
        {
            AddCancelledResults(request, validTargets, results, progress, session, "Đã dừng trước khi khởi chạy.");
            return CreateResult(request.LaunchGroupId, startedAt, results, wasCancelled: true);
        }

        if (request.StopAllOnInvalidTarget && results.Values.Any(result => result.Status == InstanceExecutionStatus.Unavailable))
        {
            AddCancelledResults(request, validTargets, results, progress, session, "Không chạy vì tùy chọn dừng toàn bộ khi có giả lập không hợp lệ đang bật.");
            return CreateResult(request.LaunchGroupId, startedAt, results, stoppedByInvalidTargetPolicy: true);
        }

        var active = new List<Task<InstanceExecutionResult>>();
        var hasLaunchedAnyTarget = false;

        foreach (var target in validTargets)
        {
            if (session.BatchToken.IsCancellationRequested)
            {
                AddCancelledResult(request, target, results, progress, session, "Đã dừng trước khi khởi chạy.");
                continue;
            }

            using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                session.BatchToken,
                session.GetInstanceToken(target.Index));
            if (linkedCancellation.IsCancellationRequested)
            {
                AddCancelledResult(request, target, results, progress, session, "Đã dừng trước khi khởi chạy.");
                continue;
            }

            if (hasLaunchedAnyTarget)
            {
                progress?.Report(CreateUpdate(request.LaunchGroupId,
                    target,
                    ResolveScript(request, target.Index),
                    InstanceExecutionStatus.WaitingForLaunch));
                try
                {
                    await launchDelayProvider
                        .DelayAsync(GetNextSpacing(request), linkedCancellation.Token)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (linkedCancellation.IsCancellationRequested)
                {
                    AddCancelledResult(request, target, results, progress, session, "Đã dừng trong khi chờ khởi chạy.");
                    continue;
                }
            }

            if (linkedCancellation.IsCancellationRequested)
            {
                AddCancelledResult(request, target, results, progress, session, "Đã dừng trước khi khởi chạy.");
                continue;
            }

            active.Add(RunTargetAsync(
                request,
                target,
                expectedCoreIdentities[target.Index],
                progress,
                session));
            hasLaunchedAnyTarget = true;
        }

        foreach (var result in await Task.WhenAll(active).ConfigureAwait(false))
            results[result.Target.Index] = result;

        return CreateResult(request.LaunchGroupId, startedAt, results, session.BatchToken.IsCancellationRequested);
    }

    private async Task<InstanceExecutionResult> RunTargetAsync(
        MultiInstanceExecutionRequest request,
        MemuInstance target,
        MemuInstanceCoreIdentity expectedCoreIdentity,
        IProgress<InstanceExecutionUpdate>? progress,
        MultiInstanceExecutionSession session)
    {
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            session.BatchToken,
            session.GetInstanceToken(target.Index));
        var admittedScript = ResolveScript(request, target.Index);
        var executionGraph = request.ScriptLibrarySnapshot?.CreateExecutionGraph(admittedScript.Id);
        var script = executionGraph?.RootScript ?? admittedScript;
        progress?.Report(CreateUpdate(request.LaunchGroupId, target, script, InstanceExecutionStatus.Running));
        var stepProgress = progress is null
            ? null
            : new ForwardingProgress<StepExecutionUpdate>(update =>
                progress.Report(CreateUpdate(request.LaunchGroupId, target, script, InstanceExecutionStatus.Running, update)));

        try
        {
            var execution = await executionEngine.ExecuteAsync(new ExecutionRequest
            {
                Script = script,
                ScriptLibrary = executionGraph is not null
                    ? executionGraph.ScriptLibrary
                    : new Dictionary<Guid, ScriptDefinition> { [script.Id] = script },
                MemucPath = request.MemucPath,
                InstanceIndex = target.Index,
                Target = target,
                ExpectedCoreIdentity = expectedCoreIdentity,
                Variables = request.Variables
            }, stepProgress, linkedCancellation.Token).ConfigureAwait(false);
            var status = execution.WasCancelled || linkedCancellation.IsCancellationRequested
                ? InstanceExecutionStatus.Cancelled
                : execution.Steps.Any(step => step.Status == StepExecutionStatus.Failed)
                    ? InstanceExecutionStatus.Failed
                    : InstanceExecutionStatus.Succeeded;
            string? message = null;

            if (status == InstanceExecutionStatus.Succeeded)
            {
                var health = await CheckPinnedHealthAsync(
                    target,
                    expectedCoreIdentity,
                    "FinalSuccessGate",
                    linkedCancellation.Token).ConfigureAwait(false);
                if (linkedCancellation.IsCancellationRequested)
                    status = InstanceExecutionStatus.Cancelled;
                else if (health.Status == MemuInstanceHealthStatus.Unavailable)
                {
                    status = InstanceExecutionStatus.Unavailable;
                    message = MemuInstanceHealthChecks.UnavailableMessage;
                }
                else if (health.Status == MemuInstanceHealthStatus.Unknown)
                {
                    status = InstanceExecutionStatus.Failed;
                    message = MemuInstanceHealthChecks.UnknownMessage;
                }
            }

            status = session.CommitTerminal(target.Index, status);
            if (status == InstanceExecutionStatus.Cancelled)
                message = "Đã dừng theo yêu cầu.";

            var result = new InstanceExecutionResult
            {
                LaunchGroupId = request.LaunchGroupId,
                Target = target,
                ScriptId = script.Id,
                ScriptName = script.Name,
                Status = status,
                Execution = execution,
                Message = message
            };
            progress?.Report(CreateUpdate(request.LaunchGroupId, target, script, status, result: execution, message: message));
            return result;
        }
        catch (OperationCanceledException) when (linkedCancellation.IsCancellationRequested)
        {
            const string message = "Đã dừng theo yêu cầu.";
            session.CommitTerminal(target.Index, InstanceExecutionStatus.Cancelled);
            progress?.Report(CreateUpdate(request.LaunchGroupId, target, script, InstanceExecutionStatus.Cancelled, message: message));
            return new InstanceExecutionResult
            {
                LaunchGroupId = request.LaunchGroupId,
                Target = target,
                ScriptId = script.Id,
                ScriptName = script.Name,
                Status = InstanceExecutionStatus.Cancelled,
                Message = message
            };
        }
        catch (MemuInstanceUnavailableException)
        {
            var status = session.CommitTerminal(target.Index, InstanceExecutionStatus.Unavailable);
            if (status == InstanceExecutionStatus.Cancelled)
            {
                const string cancelledMessage = "Đã dừng theo yêu cầu.";
                progress?.Report(CreateUpdate(request.LaunchGroupId, target, script, InstanceExecutionStatus.Cancelled, message: cancelledMessage));
                return new InstanceExecutionResult
                {
                    LaunchGroupId = request.LaunchGroupId,
                    Target = target,
                    ScriptId = script.Id,
                    ScriptName = script.Name,
                    Status = InstanceExecutionStatus.Cancelled,
                    Message = cancelledMessage
                };
            }

            const string message = MemuInstanceHealthChecks.UnavailableMessage;
            progress?.Report(CreateUpdate(request.LaunchGroupId, target, script, InstanceExecutionStatus.Unavailable, message: message));
            return new InstanceExecutionResult
            {
                LaunchGroupId = request.LaunchGroupId,
                Target = target,
                ScriptId = script.Id,
                ScriptName = script.Name,
                Status = InstanceExecutionStatus.Unavailable,
                Message = message
            };
        }
        catch (Exception exception)
        {
            var status = session.CommitTerminal(target.Index, InstanceExecutionStatus.Failed);
            var message = status == InstanceExecutionStatus.Cancelled
                ? "Đã dừng theo yêu cầu."
                : exception.Message;
            progress?.Report(CreateUpdate(request.LaunchGroupId, target, script, status, message: message));
            return new InstanceExecutionResult
            {
                LaunchGroupId = request.LaunchGroupId,
                Target = target,
                ScriptId = script.Id,
                ScriptName = script.Name,
                Status = status,
                Message = message
            };
        }
    }

    private Task<MemuInstanceHealthResult> ResolveCoreIdentityAsync(
        MemuInstance target,
        CancellationToken cancellationToken) =>
        MemuInstanceHealthChecks.ResolveSafelyAsync(
            coreIdentityResolver ?? AssumeHealthyMemuCoreIdentityResolver.Instance,
            target,
            cancellationToken);

    private Task<MemuInstanceHealthResult> CheckPinnedHealthAsync(
        MemuInstance target,
        MemuInstanceCoreIdentity expectedCoreIdentity,
        string checkpoint,
        CancellationToken cancellationToken) =>
        MemuInstanceHealthChecks.CheckPinnedSafelyAsync(
            pinnedCoreHealthCheck ?? AssumeHealthyPinnedMemuCoreHealthCheck.Instance,
            target,
            expectedCoreIdentity,
            checkpoint,
            cancellationToken);

    private TimeSpan GetNextSpacing(MultiInstanceExecutionRequest request)
    {
        if (request.LaunchSpacingMode == LaunchSpacingMode.Fixed) return request.FixedSpacing;
        var minimum = checked((int)request.RandomMinimumSpacing.TotalMilliseconds);
        var maximum = checked((int)request.RandomMaximumSpacing.TotalMilliseconds);
        return TimeSpan.FromMilliseconds(launchSpacingRandom.NextInclusive(minimum, maximum));
    }

    private static void AddCancelledResults(
        MultiInstanceExecutionRequest request,
        IEnumerable<MemuInstance> targets,
        IDictionary<int, InstanceExecutionResult> results,
        IProgress<InstanceExecutionUpdate>? progress,
        MultiInstanceExecutionSession session,
        string message)
    {
        foreach (var target in targets.Where(target => !results.ContainsKey(target.Index)))
            AddCancelledResult(request, target, results, progress, session, message);
    }

    private static void AddCancelledResult(
        MultiInstanceExecutionRequest request,
        MemuInstance target,
        IDictionary<int, InstanceExecutionResult> results,
        IProgress<InstanceExecutionUpdate>? progress,
        MultiInstanceExecutionSession session,
        string message)
    {
        var script = ResolveScript(request, target.Index);
        session.CommitTerminal(target.Index, InstanceExecutionStatus.Cancelled);
        var cancelled = new InstanceExecutionResult
        {
            LaunchGroupId = request.LaunchGroupId,
            Target = target,
            ScriptId = script.Id,
            ScriptName = script.Name,
            Status = InstanceExecutionStatus.Cancelled,
            Message = message
        };
        results[target.Index] = cancelled;
        progress?.Report(CreateUpdate(request.LaunchGroupId, target, script, InstanceExecutionStatus.Cancelled, message: message));
    }

    private static void AddUnavailableResult(
        MultiInstanceExecutionRequest request,
        MemuInstance target,
        IDictionary<int, InstanceExecutionResult> results,
        IProgress<InstanceExecutionUpdate>? progress,
        MultiInstanceExecutionSession session,
        string message)
    {
        var script = ResolveScript(request, target.Index);
        var status = session.CommitTerminal(target.Index, InstanceExecutionStatus.Unavailable);
        if (status == InstanceExecutionStatus.Cancelled)
            message = "Đã dừng trước khi khởi chạy.";
        var unavailable = new InstanceExecutionResult
        {
            LaunchGroupId = request.LaunchGroupId,
            Target = target,
            ScriptId = script.Id,
            ScriptName = script.Name,
            Status = status,
            Message = message
        };
        results[target.Index] = unavailable;
        progress?.Report(CreateUpdate(request.LaunchGroupId, target, script, status, message: message));
    }

    private static void AddFailedResult(
        MultiInstanceExecutionRequest request,
        MemuInstance target,
        IDictionary<int, InstanceExecutionResult> results,
        IProgress<InstanceExecutionUpdate>? progress,
        MultiInstanceExecutionSession session,
        string message)
    {
        var script = ResolveScript(request, target.Index);
        var status = session.CommitTerminal(target.Index, InstanceExecutionStatus.Failed);
        if (status == InstanceExecutionStatus.Cancelled)
            message = "Đã dừng trước khi khởi chạy.";
        var failed = new InstanceExecutionResult
        {
            LaunchGroupId = request.LaunchGroupId,
            Target = target,
            ScriptId = script.Id,
            ScriptName = script.Name,
            Status = status,
            Message = message
        };
        results[target.Index] = failed;
        progress?.Report(CreateUpdate(request.LaunchGroupId, target, script, status, message: message));
    }

    private static InstanceExecutionUpdate CreateUpdate(
        Guid launchGroupId,
        MemuInstance target,
        ScriptDefinition script,
        InstanceExecutionStatus status,
        StepExecutionUpdate? stepUpdate = null,
        ExecutionResult? result = null,
        string? message = null) =>
        new(launchGroupId, target.Index, target.Name, status, stepUpdate, result, message, script.Id, script.Name);

    private static ScriptDefinition ResolveScript(MultiInstanceExecutionRequest request, int instanceIndex) =>
        request.ScriptsByInstance.TryGetValue(instanceIndex, out var script) ? script : request.Script;

    private static MultiInstanceExecutionResult CreateResult(
        Guid launchGroupId,
        DateTimeOffset startedAt,
        IReadOnlyDictionary<int, InstanceExecutionResult> results,
        bool wasCancelled = false,
        bool stoppedByInvalidTargetPolicy = false) => new()
        {
            LaunchGroupId = launchGroupId,
            StartedAt = startedAt,
            EndedAt = DateTimeOffset.UtcNow,
            WasCancelled = wasCancelled,
            WasStoppedByInvalidTargetPolicy = stoppedByInvalidTargetPolicy,
            Instances = results.Values.OrderBy(result => result.Target.Index).ToList()
        };

    private static void Validate(MultiInstanceExecutionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.MemucPath);
        if (request.Targets.Count == 0) throw new ArgumentException("Phải chọn ít nhất một giả lập.", nameof(request));
        if (request.Targets.Select(target => target.Index).Distinct().Count() != request.Targets.Count)
            throw new ArgumentException("Danh sách target không được trùng index.", nameof(request));
        var unknownAssignments = request.ScriptsByInstance.Keys.Except(request.Targets.Select(target => target.Index)).ToList();
        if (unknownAssignments.Count > 0)
            throw new ArgumentException("Gán kịch bản chứa index không thuộc danh sách target.", nameof(request));
        ValidateSpacing(request.FixedSpacing, nameof(request.FixedSpacing));
        ValidateSpacing(request.RandomMinimumSpacing, nameof(request.RandomMinimumSpacing));
        ValidateSpacing(request.RandomMaximumSpacing, nameof(request.RandomMaximumSpacing));
        if (request.RandomMinimumSpacing > request.RandomMaximumSpacing)
            throw new ArgumentException("Khoảng ngẫu nhiên tối thiểu không được lớn hơn tối đa.", nameof(request));
    }

    private static void ValidateSpacing(TimeSpan value, string parameterName)
    {
        if (value < TimeSpan.Zero || value.TotalMilliseconds > int.MaxValue)
            throw new ArgumentOutOfRangeException(parameterName, "Khoảng cách khởi chạy không hợp lệ.");
    }

    private sealed class ForwardingProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
