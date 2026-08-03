using MEmuScriptStudio.Core.MEmu;
using MEmuScriptStudio.Core.Models;

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
    private readonly CancellationTokenSource batchCancellation = new();
    private readonly IReadOnlyDictionary<int, CancellationTokenSource> instanceCancellations;
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

    public void StopInstance(int instanceIndex)
    {
        if (!disposed && instanceCancellations.TryGetValue(instanceIndex, out var cancellation))
            cancellation.Cancel();
    }

    public void StopAll()
    {
        if (!disposed) batchCancellation.Cancel();
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        batchCancellation.Dispose();
        foreach (var cancellation in instanceCancellations.Values) cancellation.Dispose();
    }
}

public sealed class MultiInstanceExecutionScheduler(
    IMemuInstanceService instanceService,
    IScriptExecutionEngine executionEngine,
    ILaunchDelayProvider launchDelayProvider,
    ILaunchSpacingRandom launchSpacingRandom) : IMultiInstanceExecutionScheduler
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
            AddCancelledResults(request, request.Targets, results, progress, "Đã dừng trước khi hoàn tất kiểm tra giả lập.");
            return CreateResult(request.LaunchGroupId, startedAt, results, wasCancelled: true);
        }
        catch (Exception exception)
        {
            foreach (var target in request.Targets)
            {
                var script = ResolveScript(request, target.Index);
                results[target.Index] = new InstanceExecutionResult
                {
                    LaunchGroupId = request.LaunchGroupId,
                    Target = target,
                    ScriptId = script.Id,
                    ScriptName = script.Name,
                    Status = InstanceExecutionStatus.Failed,
                    Message = exception.Message
                };
                progress?.Report(CreateUpdate(
                    request.LaunchGroupId,
                    target,
                    script,
                    InstanceExecutionStatus.Failed,
                    message: exception.Message));
            }
            return CreateResult(request.LaunchGroupId, startedAt, results);
        }

        var currentByIndex = currentInstances
            .GroupBy(instance => instance.Index)
            .ToDictionary(group => group.Key, group => group.ToList());
        var validTargets = new List<MemuInstance>();
        foreach (var requestedTarget in request.Targets)
        {
            if (requestedTarget.Index >= 0 &&
                currentByIndex.TryGetValue(requestedTarget.Index, out var matches) &&
                matches.Count == 1 && matches[0].IsRunning)
            {
                validTargets.Add(matches[0]);
                continue;
            }

            const string message = "Giả lập đang tắt, đã mất hoặc không hợp lệ tại preflight; không tự khởi động.";
            var unavailable = new InstanceExecutionResult
            {
                LaunchGroupId = request.LaunchGroupId,
                Target = requestedTarget,
                ScriptId = ResolveScript(request, requestedTarget.Index).Id,
                ScriptName = ResolveScript(request, requestedTarget.Index).Name,
                Status = InstanceExecutionStatus.Unavailable,
                Message = message
            };
            results[requestedTarget.Index] = unavailable;
            progress?.Report(CreateUpdate(request.LaunchGroupId,
                requestedTarget,
                ResolveScript(request, requestedTarget.Index),
                InstanceExecutionStatus.Unavailable,
                message: message));
        }

        if (request.StopAllOnInvalidTarget && results.Values.Any(result => result.Status == InstanceExecutionStatus.Unavailable))
        {
            AddCancelledResults(request, validTargets, results, progress, "Không chạy vì tùy chọn dừng toàn bộ khi có giả lập không hợp lệ đang bật.");
            return CreateResult(request.LaunchGroupId, startedAt, results, stoppedByInvalidTargetPolicy: true);
        }

        var active = new List<Task<InstanceExecutionResult>>();
        var hasLaunchedAnyTarget = false;

        foreach (var target in validTargets)
        {
            if (session.BatchToken.IsCancellationRequested)
            {
                AddCancelledResult(request, target, results, progress, "Đã dừng trước khi khởi chạy.");
                continue;
            }

            using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                session.BatchToken,
                session.GetInstanceToken(target.Index));
            if (linkedCancellation.IsCancellationRequested)
            {
                AddCancelledResult(request, target, results, progress, "Đã dừng trước khi khởi chạy.");
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
                    AddCancelledResult(request, target, results, progress, "Đã dừng trong khi chờ khởi chạy.");
                    continue;
                }
            }

            if (linkedCancellation.IsCancellationRequested)
            {
                AddCancelledResult(request, target, results, progress, "Đã dừng trước khi khởi chạy.");
                continue;
            }

            active.Add(RunTargetAsync(request, target, progress, session));
            hasLaunchedAnyTarget = true;
        }

        foreach (var result in await Task.WhenAll(active).ConfigureAwait(false))
            results[result.Target.Index] = result;

        return CreateResult(request.LaunchGroupId, startedAt, results, session.BatchToken.IsCancellationRequested);
    }

    private async Task<InstanceExecutionResult> RunTargetAsync(
        MultiInstanceExecutionRequest request,
        MemuInstance target,
        IProgress<InstanceExecutionUpdate>? progress,
        MultiInstanceExecutionSession session)
    {
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            session.BatchToken,
            session.GetInstanceToken(target.Index));
        var script = ResolveScript(request, target.Index);
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
                MemucPath = request.MemucPath,
                InstanceIndex = target.Index,
                Variables = request.Variables
            }, stepProgress, linkedCancellation.Token).ConfigureAwait(false);
            var status = execution.WasCancelled || linkedCancellation.IsCancellationRequested
                ? InstanceExecutionStatus.Cancelled
                : execution.Steps.Any(step => step.Status == StepExecutionStatus.Failed)
                    ? InstanceExecutionStatus.Failed
                    : InstanceExecutionStatus.Succeeded;
            var result = new InstanceExecutionResult
            {
                LaunchGroupId = request.LaunchGroupId,
                Target = target,
                ScriptId = script.Id,
                ScriptName = script.Name,
                Status = status,
                Execution = execution
            };
            progress?.Report(CreateUpdate(request.LaunchGroupId, target, script, status, result: execution));
            return result;
        }
        catch (OperationCanceledException) when (linkedCancellation.IsCancellationRequested)
        {
            const string message = "Đã dừng theo yêu cầu.";
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
        catch (Exception exception)
        {
            progress?.Report(CreateUpdate(request.LaunchGroupId, target, script, InstanceExecutionStatus.Failed, message: exception.Message));
            return new InstanceExecutionResult
            {
                LaunchGroupId = request.LaunchGroupId,
                Target = target,
                ScriptId = script.Id,
                ScriptName = script.Name,
                Status = InstanceExecutionStatus.Failed,
                Message = exception.Message
            };
        }
    }

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
        string message)
    {
        foreach (var target in targets.Where(target => !results.ContainsKey(target.Index)))
            AddCancelledResult(request, target, results, progress, message);
    }

    private static void AddCancelledResult(
        MultiInstanceExecutionRequest request,
        MemuInstance target,
        IDictionary<int, InstanceExecutionResult> results,
        IProgress<InstanceExecutionUpdate>? progress,
        string message)
    {
        var script = ResolveScript(request, target.Index);
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
