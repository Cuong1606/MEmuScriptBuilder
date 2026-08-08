using MEmuScriptStudio.Core.Android;
using MEmuScriptStudio.Core.Formatting;
using MEmuScriptStudio.Core.MEmu;
using MEmuScriptStudio.Core.Models;
using MEmuScriptStudio.Core.Scripts;

namespace MEmuScriptStudio.Core.Execution;

public sealed class CompositeScriptExecutionEngine(
    ScriptExecutionEngine regularEngine,
    IDelayProvider delayProvider,
    IPinnedMemuCoreHealthCheck? pinnedCoreHealthCheck = null,
    IAndroidAdbStateProbe? androidStateProbe = null) : IScriptExecutionEngine
{
    public async Task<ExecutionResult> ExecuteAsync(
        ExecutionRequest request,
        IProgress<StepExecutionUpdate>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Script.Kind == ScriptKind.Regular)
            return await regularEngine.ExecuteAsync(request, progress, cancellationToken).ConfigureAwait(false);

        if (request.ScriptLibrary.Any(pair => pair.Key != pair.Value.Id))
            throw new InvalidDataException("Snapshot thư viện có key không khớp ScriptId.");
        var library = request.ScriptLibrary.Values
            .Where(script => script.Id != request.Script.Id)
            .Append(request.Script)
            .ToList();
        ScriptLibraryValidator.Validate(library);
        var byId = library.ToDictionary(script => script.Id);
        var startedAt = DateTimeOffset.UtcNow;
        var results = new List<StepExecutionResult>();

        foreach (var item in request.Script.CompositeItems)
        {
            if (cancellationToken.IsCancellationRequested) break;
            var occurrenceId = Guid.NewGuid();
            if (!item.IsEnabled)
            {
                var context = CreateContext(request.Script, item, occurrenceId, null, null);
                var skipped = CreateCompositeResult(item.Id, StepExecutionStatus.Skipped,
                    "[Mục gộp đã tắt]", context, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
                results.Add(skipped);
                progress?.Report(new StepExecutionUpdate(item.Id, skipped.Status, skipped, context));
                continue;
            }

            if (item is CompositeDelayItem delay)
            {
                var context = CreateContext(request.Script, item, occurrenceId, null, null);
                var itemStartedAt = DateTimeOffset.UtcNow;
                var preview = $"[Chờ {DurationFormatter.FormatMilliseconds(delay.DurationMilliseconds)}]";
                progress?.Report(new StepExecutionUpdate(item.Id, StepExecutionStatus.Running, null, context));
                try
                {
                    await delayProvider.DelayAsync(TimeSpan.FromMilliseconds(delay.DurationMilliseconds), cancellationToken)
                        .ConfigureAwait(false);
                    await EnsureTargetHealthyAsync(request, cancellationToken).ConfigureAwait(false);
                    var completed = CreateCompositeResult(item.Id, StepExecutionStatus.Succeeded,
                        preview, context, itemStartedAt, DateTimeOffset.UtcNow);
                    results.Add(completed);
                    progress?.Report(new StepExecutionUpdate(item.Id, completed.Status, completed, context));
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    var cancelled = CreateCompositeResult(item.Id, StepExecutionStatus.Cancelled,
                        preview, context, itemStartedAt, DateTimeOffset.UtcNow,
                        "Đã hủy theo yêu cầu.");
                    results.Add(cancelled);
                    progress?.Report(new StepExecutionUpdate(item.Id, cancelled.Status, cancelled, context));
                    break;
                }
                continue;
            }

            var reference = (ScriptReferenceItem)item;
            var child = byId[reference.ScriptId];
            var occurrenceContext = CreateContext(request.Script, item, occurrenceId, child, null);
            progress?.Report(new StepExecutionUpdate(item.Id, StepExecutionStatus.Running, null, occurrenceContext));
            var childProgress = progress is null ? null : new ForwardingProgress<StepExecutionUpdate>(update =>
            {
                var context = CreateContext(request.Script, item, occurrenceId, child, update.StepId);
                progress.Report(update with { CompositeContext = context });
            });
            var childResult = await regularEngine.ExecuteAsync(new ExecutionRequest
            {
                Script = child,
                ScriptLibrary = request.ScriptLibrary,
                MemucPath = request.MemucPath,
                AdbPath = request.AdbPath,
                InstanceIndex = request.InstanceIndex,
                Target = request.Target,
                ExpectedCoreIdentity = request.ExpectedCoreIdentity,
                Variables = request.Variables
            }, childProgress, cancellationToken).ConfigureAwait(false);

            var contextualResults = childResult.Steps.Select(step => CopyWithContext(
                step,
                CreateContext(request.Script, item, occurrenceId, child, step.StepId))).ToList();
            results.AddRange(contextualResults);
            var childFailed = contextualResults.Any(step => step.Status == StepExecutionStatus.Failed);
            var childCancelled = childResult.WasCancelled || contextualResults.Any(step => step.Status == StepExecutionStatus.Cancelled);
            var terminalStatus = childCancelled
                ? StepExecutionStatus.Cancelled
                : childFailed ? StepExecutionStatus.Failed : StepExecutionStatus.Succeeded;
            progress?.Report(new StepExecutionUpdate(item.Id, terminalStatus, null, occurrenceContext));
            if (childCancelled || (childFailed && !reference.ContinueOnFailure)) break;
        }

        return new ExecutionResult
        {
            StartedAt = startedAt,
            EndedAt = DateTimeOffset.UtcNow,
            WasCancelled = cancellationToken.IsCancellationRequested ||
                results.Any(result => result.Status == StepExecutionStatus.Cancelled),
            Steps = results
        };
    }

    private async Task EnsureTargetHealthyAsync(
        ExecutionRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Target is MemuInstance memuTarget)
        {
            if (pinnedCoreHealthCheck is null) return;
            if (request.ExpectedCoreIdentity is null)
                throw new InvalidOperationException(MemuInstanceHealthChecks.UnknownMessage);
            var result = await MemuInstanceHealthChecks.CheckPinnedSafelyAsync(
                pinnedCoreHealthCheck,
                memuTarget,
                request.ExpectedCoreIdentity,
                "AfterCompositeDelay",
                cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (result.Status == MemuInstanceHealthStatus.Unavailable)
                throw new MemuInstanceUnavailableException(result.Diagnostic);
            return;
        }

        if (request.Target is not AndroidAdbDevice androidTarget || androidStateProbe is null)
            throw new InvalidOperationException("Android / ADB health probe chưa được cấu hình.");
        var androidState = await androidStateProbe
            .CheckStateAsync(request.AdbPath, androidTarget.Serial, cancellationToken)
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (!androidState.IsRunnable)
            throw new AndroidAdbDeviceUnavailableException(
                androidState.Diagnostic ?? "Android device không còn ở trạng thái device trong ADB.");
    }

    private static CompositeExecutionContext CreateContext(
        ScriptDefinition composite,
        CompositeScriptItem item,
        Guid occurrenceId,
        ScriptDefinition? child,
        Guid? childStepId)
    {
        var childStep = childStepId is Guid id ? child?.Steps.FirstOrDefault(step => step.Id == id) : null;
        return new(
            composite.Id,
            composite.Name,
            item.Id,
            occurrenceId,
            child?.Id,
            child?.Name,
            childStepId,
            item is CompositeDelayItem delay
                ? ScriptStepDisplayName.GetDelay(delay.DurationMilliseconds)
                : childStep is null ? null : ScriptStepDisplayName.Get(childStep));
    }

    private static StepExecutionResult CopyWithContext(
        StepExecutionResult source,
        CompositeExecutionContext context) => new()
        {
            StepId = source.StepId,
            Status = source.Status,
            StartedAt = source.StartedAt,
            EndedAt = source.EndedAt,
            ExitCode = source.ExitCode,
            StandardOutput = source.StandardOutput,
            StandardError = source.StandardError,
            CommandPreview = source.CommandPreview,
            CompositeContext = context
        };

    private static StepExecutionResult CreateCompositeResult(
        Guid id,
        StepExecutionStatus status,
        string preview,
        CompositeExecutionContext context,
        DateTimeOffset startedAt,
        DateTimeOffset endedAt,
        string error = "") => new()
        {
            StepId = id,
            Status = status,
            StartedAt = startedAt,
            EndedAt = endedAt,
            CommandPreview = preview,
            StandardError = error,
            CompositeContext = context
        };

    private sealed class ForwardingProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
