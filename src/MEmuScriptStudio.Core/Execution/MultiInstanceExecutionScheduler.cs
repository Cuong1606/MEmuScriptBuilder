using MEmuScriptStudio.Core.Android;
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
    private readonly IReadOnlyDictionary<string, CancellationTokenSource> targetCancellations;
    private readonly IReadOnlyDictionary<string, int> targetIndexes;
    private readonly HashSet<string> stopRequested = [];
    private readonly HashSet<string> terminalCommitted = [];
    private bool disposed;

    internal MultiInstanceExecutionSession(IEnumerable<IExecutionTarget> targets)
    {
        var targetList = targets.ToList();
        targetCancellations = targetList.ToDictionary(target => target.TargetKey, _ => new CancellationTokenSource(), StringComparer.Ordinal);
        targetIndexes = targetList.ToDictionary(target => target.TargetKey, target => target.Index, StringComparer.Ordinal);
    }

    public Task<MultiInstanceExecutionResult> Completion { get; internal set; } =
        Task.FromResult(new MultiInstanceExecutionResult());

    internal CancellationToken BatchToken => batchCancellation.Token;

    internal CancellationToken GetTargetToken(string targetKey) => targetCancellations[targetKey].Token;

    internal CancellationToken GetInstanceToken(int instanceIndex) =>
        GetTargetToken(ExecutionTargetKeys.ForMemu(instanceIndex));

    public bool StopTarget(string targetKey, Action? onAccepted = null)
    {
        CancellationTokenSource cancellation;
        lock (lifecycleSync)
        {
            if (disposed || terminalCommitted.Contains(targetKey) || stopRequested.Contains(targetKey) ||
                !targetCancellations.TryGetValue(targetKey, out cancellation!)) return false;
            stopRequested.Add(targetKey);
            onAccepted?.Invoke();
        }
        try { cancellation.Cancel(); }
        catch (ObjectDisposedException) { }
        return true;
    }

    public bool StopInstance(int instanceIndex, Action? onAccepted = null)
        => StopTarget(ExecutionTargetKeys.ForMemu(instanceIndex), onAccepted);

    public IReadOnlySet<string> StopAllTargets(Action<string>? onAccepted = null)
    {
        HashSet<string> accepted;
        lock (lifecycleSync)
        {
            if (disposed) return new HashSet<string>(StringComparer.Ordinal);
            accepted = targetCancellations.Keys
                .Where(key => !terminalCommitted.Contains(key) && !stopRequested.Contains(key))
                .ToHashSet(StringComparer.Ordinal);
            stopRequested.UnionWith(accepted);
            foreach (var key in accepted) onAccepted?.Invoke(key);
        }
        try { batchCancellation.Cancel(); }
        catch (ObjectDisposedException) { }
        return accepted;
    }

    public IReadOnlySet<int> StopAll(Action<int>? onAccepted = null)
    {
        var acceptedTargets = StopAllTargets(key =>
        {
            if (targetIndexes.GetValueOrDefault(key, -1) is var index && index >= 0) onAccepted?.Invoke(index);
        });
        return acceptedTargets
            .Select(key => targetIndexes.GetValueOrDefault(key, -1))
            .Where(index => index >= 0)
            .ToHashSet();
    }

    internal InstanceExecutionStatus CommitTerminal(
        string targetKey,
        InstanceExecutionStatus intendedStatus)
    {
        lock (lifecycleSync)
        {
            var cancellationWon = stopRequested.Contains(targetKey) ||
                batchCancellation.IsCancellationRequested ||
                targetCancellations[targetKey].IsCancellationRequested;
            terminalCommitted.Add(targetKey);
            return cancellationWon ? InstanceExecutionStatus.Cancelled : intendedStatus;
        }
    }

    internal InstanceExecutionStatus CommitTerminal(int instanceIndex, InstanceExecutionStatus intendedStatus) =>
        CommitTerminal(ExecutionTargetKeys.ForMemu(instanceIndex), intendedStatus);

    public void Dispose()
    {
        lock (lifecycleSync)
        {
            if (disposed) return;
            disposed = true;
        }
        batchCancellation.Dispose();
        foreach (var cancellation in targetCancellations.Values) cancellation.Dispose();
    }
}

public sealed class MultiInstanceExecutionScheduler(
    IMemuInstanceService instanceService,
    IScriptExecutionEngine executionEngine,
    ILaunchDelayProvider launchDelayProvider,
    ILaunchSpacingRandom launchSpacingRandom,
    IMemuCoreIdentityResolver? coreIdentityResolver = null,
    IPinnedMemuCoreHealthCheck? pinnedCoreHealthCheck = null,
    IAndroidAdbTransportService? androidTransportService = null,
    IAndroidAdbStateProbe? androidStateProbe = null) : IMultiInstanceExecutionScheduler
{
    public MultiInstanceExecutionSession Start(
        MultiInstanceExecutionRequest request,
        IProgress<InstanceExecutionUpdate>? progress = null)
    {
        Validate(request);
        var session = new MultiInstanceExecutionSession(request.Targets);
        session.Completion = ExecuteAsync(request, progress, session);
        return session;
    }

    private async Task<MultiInstanceExecutionResult> ExecuteAsync(
        MultiInstanceExecutionRequest request,
        IProgress<InstanceExecutionUpdate>? progress,
        MultiInstanceExecutionSession session)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var results = new Dictionary<string, InstanceExecutionResult>(StringComparer.Ordinal);
        foreach (var target in request.Targets)
        {
            var script = ResolveScript(request, target);
            progress?.Report(CreateUpdate(request.LaunchGroupId, target, script, InstanceExecutionStatus.Queued));
        }

        IReadOnlyList<MemuInstance> currentMemuInstances = [];
        IReadOnlyList<AdbDeviceListEntry> currentAndroidTransports = [];
        Exception? memuDiscoveryError = null;
        Exception? androidDiscoveryError = null;

        if (request.Targets.Any(target => target.Kind == DeviceKind.MEmu))
        {
            try
            {
                currentMemuInstances = await instanceService
                    .GetInstancesAsync(request.MemucPath, session.BatchToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (session.BatchToken.IsCancellationRequested)
            {
                AddCancelledResults(request, request.Targets, results, progress, session, "Đã dừng trước khi hoàn tất preflight.");
                return CreateResult(request.LaunchGroupId, startedAt, results, wasCancelled: true);
            }
            catch (Exception exception) { memuDiscoveryError = exception; }
        }

        if (request.Targets.Any(target => target.Kind == DeviceKind.AndroidAdb))
        {
            try
            {
                if (androidTransportService is null)
                    throw new InvalidOperationException("Android / ADB transport discovery chưa được cấu hình.");
                ArgumentException.ThrowIfNullOrWhiteSpace(request.AdbPath);
                currentAndroidTransports = await androidTransportService
                    .GetTransportsAsync(request.AdbPath, session.BatchToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (session.BatchToken.IsCancellationRequested)
            {
                AddCancelledResults(request, request.Targets, results, progress, session, "Đã dừng trước khi hoàn tất preflight.");
                return CreateResult(request.LaunchGroupId, startedAt, results, wasCancelled: true);
            }
            catch (Exception exception) { androidDiscoveryError = exception; }
        }

        var currentByIndex = currentMemuInstances
            .GroupBy(instance => instance.Index)
            .ToDictionary(group => group.Key, group => group.ToList());
        var currentBySerial = currentAndroidTransports
            .GroupBy(transport => transport.Serial, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);
        IReadOnlyDictionary<int, MemuInstanceHealthResult> memuCoreResolutions =
            new Dictionary<int, MemuInstanceHealthResult>();
        if (memuDiscoveryError is null)
        {
            var memuResolutionTargets = request.Targets
                .OfType<MemuInstance>()
                .Where(target => !session.GetTargetToken(target.TargetKey).IsCancellationRequested)
                .Select(target => currentByIndex.TryGetValue(target.Index, out var matches) &&
                                  matches.Count == 1 && matches[0].IsRunning
                    ? matches[0]
                    : null)
                .Where(target => target is not null)
                .Cast<MemuInstance>()
                .ToList();
            if (memuResolutionTargets.Count > 0)
            {
                try
                {
                    memuCoreResolutions = await ResolveCoreIdentitiesAsync(
                            memuResolutionTargets,
                            session.BatchToken)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (session.BatchToken.IsCancellationRequested)
                {
                    AddCancelledResults(
                        request,
                        request.Targets,
                        results,
                        progress,
                        session,
                        "Đã dừng trước khi hoàn tất preflight.");
                    return CreateResult(request.LaunchGroupId, startedAt, results, wasCancelled: true);
                }
            }
        }

        var validTargets = new List<IExecutionTarget>();
        var expectedCoreIdentities = new Dictionary<string, MemuInstanceCoreIdentity>(StringComparer.Ordinal);
        foreach (var requestedTarget in request.Targets)
        {
            if (session.BatchToken.IsCancellationRequested ||
                session.GetTargetToken(requestedTarget.TargetKey).IsCancellationRequested)
            {
                AddCancelledResult(request, requestedTarget, results, progress, session, "Đã dừng trước khi khởi chạy.");
                continue;
            }

            var admittedScript = ResolveScript(request, requestedTarget);
            var executionGraph = request.ScriptLibrarySnapshot?.CreateExecutionGraph(admittedScript.Id);
            var scriptLibrary = executionGraph?.ScriptLibrary ??
                new Dictionary<Guid, ScriptDefinition> { [admittedScript.Id] = admittedScript };
            if (requestedTarget.Kind == DeviceKind.AndroidAdb &&
                AndroidScriptCapabilities.FindUnsupportedStep(executionGraph?.RootScript ?? admittedScript, scriptLibrary) is { } unsupported)
            {
                AddFailedResult(request, requestedTarget, results, progress, session, unsupported);
                continue;
            }

            if (requestedTarget is MemuInstance requestedMemu)
            {
                if (memuDiscoveryError is not null)
                {
                    AddFailedResult(request, requestedTarget, results, progress, session, memuDiscoveryError.Message);
                    continue;
                }
                if (!currentByIndex.TryGetValue(requestedMemu.Index, out var matches) ||
                    matches.Count != 1 || !matches[0].IsRunning)
                {
                    AddUnavailableResult(request, requestedTarget, results, progress, session,
                        "Thiết bị đang tắt, đã mất hoặc không hợp lệ tại preflight; không tự khởi động.");
                    continue;
                }

                var currentTarget = matches[0];
                if (session.BatchToken.IsCancellationRequested ||
                    session.GetTargetToken(requestedTarget.TargetKey).IsCancellationRequested)
                {
                    AddCancelledResult(request, requestedTarget, results, progress, session, "Đã dừng trước khi khởi chạy.");
                    continue;
                }

                var health = memuCoreResolutions.GetValueOrDefault(
                    currentTarget.Index,
                    MemuInstanceHealthResult.Unknown("Batch resolver không trả về kết quả cho instance."));
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

                expectedCoreIdentities[currentTarget.TargetKey] = health.CoreIdentity;
                validTargets.Add(currentTarget);
                continue;
            }

            if (requestedTarget is AndroidAdbDevice requestedAndroid)
            {
                if (androidDiscoveryError is not null)
                {
                    AddFailedResult(request, requestedTarget, results, progress, session, androidDiscoveryError.Message);
                    continue;
                }
                if (!currentBySerial.TryGetValue(requestedAndroid.Serial, out var matches) || matches.Count != 1)
                {
                    AddUnavailableResult(request, requestedTarget, results, progress, session,
                        "Android device đã ngắt kết nối hoặc biến mất khỏi adb devices -l.");
                    continue;
                }
                var currentTransport = matches[0];
                if (currentTransport.State != AndroidConnectionState.Device)
                {
                    var message = currentTransport.State switch
                    {
                        AndroidConnectionState.Unauthorized => "Android device chưa authorize USB debugging.",
                        AndroidConnectionState.Offline => "Android device đang offline trong ADB.",
                        _ => "Android device không ở trạng thái device trong ADB."
                    };
                    AddUnavailableResult(request, requestedTarget, results, progress, session, message);
                    continue;
                }
                validTargets.Add(requestedTarget);
                continue;
            }

            AddFailedResult(request, requestedTarget, results, progress, session,
                $"Provider target {requestedTarget.Kind} chưa được hỗ trợ.");
        }

        if (session.BatchToken.IsCancellationRequested)
        {
            AddCancelledResults(request, validTargets, results, progress, session, "Đã dừng trước khi khởi chạy.");
            return CreateResult(request.LaunchGroupId, startedAt, results, wasCancelled: true);
        }

        if (request.StopAllOnInvalidTarget && results.Values.Any(result => result.Status == InstanceExecutionStatus.Unavailable))
        {
            AddCancelledResults(request, validTargets, results, progress, session, "Không chạy vì tùy chọn dừng toàn bộ khi có target không hợp lệ đang bật.");
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
                session.GetTargetToken(target.TargetKey));
            if (linkedCancellation.IsCancellationRequested)
            {
                AddCancelledResult(request, target, results, progress, session, "Đã dừng trước khi khởi chạy.");
                continue;
            }

            if (hasLaunchedAnyTarget)
            {
                progress?.Report(CreateUpdate(request.LaunchGroupId,
                    target,
                    ResolveScript(request, target),
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
                expectedCoreIdentities.GetValueOrDefault(target.TargetKey),
                progress,
                session));
            hasLaunchedAnyTarget = true;
        }

        foreach (var result in await Task.WhenAll(active).ConfigureAwait(false))
            results[result.Target.TargetKey] = result;

        return CreateResult(request.LaunchGroupId, startedAt, results, session.BatchToken.IsCancellationRequested);
    }

    private async Task<InstanceExecutionResult> RunTargetAsync(
        MultiInstanceExecutionRequest request,
        IExecutionTarget target,
        MemuInstanceCoreIdentity? expectedCoreIdentity,
        IProgress<InstanceExecutionUpdate>? progress,
        MultiInstanceExecutionSession session)
    {
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            session.BatchToken,
            session.GetTargetToken(target.TargetKey));
        var admittedScript = ResolveScript(request, target);
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
                AdbPath = request.AdbPath,
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
                if (target is MemuInstance memuTarget)
                {
                    if (expectedCoreIdentity is null)
                        throw new InvalidOperationException(MemuInstanceHealthChecks.UnknownMessage);
                    var health = await CheckPinnedHealthAsync(
                        memuTarget,
                        expectedCoreIdentity,
                        "FinalSuccessGate",
                        linkedCancellation.Token).ConfigureAwait(false);
                    if (health.Status == MemuInstanceHealthStatus.Unavailable)
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
                else if (target is AndroidAdbDevice androidTarget)
                {
                    if (androidStateProbe is null)
                        throw new InvalidOperationException("Android / ADB health probe chưa được cấu hình.");
                    var health = await androidStateProbe
                        .CheckStateAsync(request.AdbPath, androidTarget.Serial, linkedCancellation.Token)
                        .ConfigureAwait(false);
                    if (!health.IsRunnable)
                    {
                        status = InstanceExecutionStatus.Unavailable;
                        message = health.Diagnostic ?? "Android device không còn ở trạng thái device trong ADB.";
                    }
                }
                if (linkedCancellation.IsCancellationRequested) status = InstanceExecutionStatus.Cancelled;
            }

            status = session.CommitTerminal(target.TargetKey, status);
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
            session.CommitTerminal(target.TargetKey, InstanceExecutionStatus.Cancelled);
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
        catch (Exception unavailableException) when (
            unavailableException is MemuInstanceUnavailableException or AndroidAdbDeviceUnavailableException)
        {
            var status = session.CommitTerminal(target.TargetKey, InstanceExecutionStatus.Unavailable);
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

            var message = unavailableException is MemuInstanceUnavailableException
                ? MemuInstanceHealthChecks.UnavailableMessage
                : unavailableException.Message;
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
            var status = session.CommitTerminal(target.TargetKey, InstanceExecutionStatus.Failed);
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

    private Task<IReadOnlyDictionary<int, MemuInstanceHealthResult>> ResolveCoreIdentitiesAsync(
        IReadOnlyList<MemuInstance> targets,
        CancellationToken cancellationToken) =>
        MemuInstanceHealthChecks.ResolveBatchSafelyAsync(
            coreIdentityResolver ?? AssumeHealthyMemuCoreIdentityResolver.Instance,
            targets,
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
        IEnumerable<IExecutionTarget> targets,
        IDictionary<string, InstanceExecutionResult> results,
        IProgress<InstanceExecutionUpdate>? progress,
        MultiInstanceExecutionSession session,
        string message)
    {
        foreach (var target in targets.Where(target => !results.ContainsKey(target.TargetKey)))
            AddCancelledResult(request, target, results, progress, session, message);
    }

    private static void AddCancelledResult(
        MultiInstanceExecutionRequest request,
        IExecutionTarget target,
        IDictionary<string, InstanceExecutionResult> results,
        IProgress<InstanceExecutionUpdate>? progress,
        MultiInstanceExecutionSession session,
        string message)
    {
        var script = ResolveScript(request, target);
        session.CommitTerminal(target.TargetKey, InstanceExecutionStatus.Cancelled);
        var cancelled = new InstanceExecutionResult
        {
            LaunchGroupId = request.LaunchGroupId,
            Target = target,
            ScriptId = script.Id,
            ScriptName = script.Name,
            Status = InstanceExecutionStatus.Cancelled,
            Message = message
        };
        results[target.TargetKey] = cancelled;
        progress?.Report(CreateUpdate(request.LaunchGroupId, target, script, InstanceExecutionStatus.Cancelled, message: message));
    }

    private static void AddUnavailableResult(
        MultiInstanceExecutionRequest request,
        IExecutionTarget target,
        IDictionary<string, InstanceExecutionResult> results,
        IProgress<InstanceExecutionUpdate>? progress,
        MultiInstanceExecutionSession session,
        string message)
    {
        var script = ResolveScript(request, target);
        var status = session.CommitTerminal(target.TargetKey, InstanceExecutionStatus.Unavailable);
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
        results[target.TargetKey] = unavailable;
        progress?.Report(CreateUpdate(request.LaunchGroupId, target, script, status, message: message));
    }

    private static void AddFailedResult(
        MultiInstanceExecutionRequest request,
        IExecutionTarget target,
        IDictionary<string, InstanceExecutionResult> results,
        IProgress<InstanceExecutionUpdate>? progress,
        MultiInstanceExecutionSession session,
        string message)
    {
        var script = ResolveScript(request, target);
        var status = session.CommitTerminal(target.TargetKey, InstanceExecutionStatus.Failed);
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
        results[target.TargetKey] = failed;
        progress?.Report(CreateUpdate(request.LaunchGroupId, target, script, status, message: message));
    }

    private static InstanceExecutionUpdate CreateUpdate(
        Guid launchGroupId,
        IExecutionTarget target,
        ScriptDefinition script,
        InstanceExecutionStatus status,
        StepExecutionUpdate? stepUpdate = null,
        ExecutionResult? result = null,
        string? message = null) =>
        new(launchGroupId, target.Index, target.Name, status, stepUpdate, result, message, script.Id, script.Name)
        {
            TargetKey = target.TargetKey,
            DeviceKind = target.Kind,
            TargetIdentifier = target.Identifier
        };

    private static ScriptDefinition ResolveScript(MultiInstanceExecutionRequest request, IExecutionTarget target)
    {
        if (request.ScriptsByTarget.TryGetValue(target.TargetKey, out var targetScript)) return targetScript;
        return target is MemuInstance && request.ScriptsByInstance.TryGetValue(target.Index, out var instanceScript)
            ? instanceScript
            : request.Script;
    }

    private static MultiInstanceExecutionResult CreateResult(
        Guid launchGroupId,
        DateTimeOffset startedAt,
        IReadOnlyDictionary<string, InstanceExecutionResult> results,
        bool wasCancelled = false,
        bool stoppedByInvalidTargetPolicy = false) => new()
        {
            LaunchGroupId = launchGroupId,
            StartedAt = startedAt,
            EndedAt = DateTimeOffset.UtcNow,
            WasCancelled = wasCancelled,
            WasStoppedByInvalidTargetPolicy = stoppedByInvalidTargetPolicy,
            Instances = results.Values
                .OrderBy(result => result.Target.Kind)
                .ThenBy(result => result.Target.Identifier, StringComparer.OrdinalIgnoreCase)
                .ToList()
        };

    private static void Validate(MultiInstanceExecutionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Targets.Count == 0) throw new ArgumentException("Phải chọn ít nhất một target.", nameof(request));
        if (request.Targets.Select(target => target.TargetKey).Distinct(StringComparer.Ordinal).Count() != request.Targets.Count)
            throw new ArgumentException("Danh sách target không được trùng identity.", nameof(request));
        if (request.Targets.Any(target => target.Kind == DeviceKind.MEmu))
            ArgumentException.ThrowIfNullOrWhiteSpace(request.MemucPath);
        if (request.Targets.Any(target => target.Kind == DeviceKind.AndroidAdb))
            ArgumentException.ThrowIfNullOrWhiteSpace(request.AdbPath);
        var memuTargets = request.Targets.OfType<MemuInstance>().Select(target => target.Index).ToHashSet();
        var unknownAssignments = request.ScriptsByInstance.Keys.Except(memuTargets).ToList();
        if (unknownAssignments.Count > 0)
            throw new ArgumentException("Gán kịch bản chứa index không thuộc danh sách target.", nameof(request));
        var targetKeys = request.Targets.Select(target => target.TargetKey).ToHashSet(StringComparer.Ordinal);
        if (request.ScriptsByTarget.Keys.Any(key => !targetKeys.Contains(key)))
            throw new ArgumentException("Gán kịch bản chứa identity không thuộc danh sách target.", nameof(request));
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
