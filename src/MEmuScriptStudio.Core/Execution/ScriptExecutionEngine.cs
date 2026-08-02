using MEmuScriptStudio.Core.MEmu;
using MEmuScriptStudio.Core.Models;
using MEmuScriptStudio.Core.Processes;

namespace MEmuScriptStudio.Core.Execution;

public interface IDelayProvider
{
    Task DelayAsync(TimeSpan duration, CancellationToken cancellationToken);
}

public sealed class TaskDelayProvider : IDelayProvider
{
    public Task DelayAsync(TimeSpan duration, CancellationToken cancellationToken) => Task.Delay(duration, cancellationToken);
}

public interface IScriptExecutionEngine
{
    Task<ExecutionResult> ExecuteAsync(
        ExecutionRequest request,
        IProgress<StepExecutionUpdate>? progress,
        CancellationToken cancellationToken);
}

public sealed class ScriptExecutionEngine(
    IProcessRunner processRunner,
    ScriptStepCommandBuilder commandBuilder,
    IDelayProvider delayProvider) : IScriptExecutionEngine
{
    public async Task<ExecutionResult> ExecuteAsync(
        ExecutionRequest request,
        IProgress<StepExecutionUpdate>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.InstanceIndex < 0) throw new ArgumentOutOfRangeException(nameof(request));
        ArgumentException.ThrowIfNullOrWhiteSpace(request.MemucPath);

        var executionStartedAt = DateTimeOffset.UtcNow;
        var results = new List<StepExecutionResult>();

        foreach (var step in request.Script.Steps)
        {
            if (cancellationToken.IsCancellationRequested) break;

            if (!step.IsEnabled || step is NoteStep)
            {
                var skipped = CreateResult(step, StepExecutionStatus.Skipped, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
                    commandBuilder.BuildPreview(step, request.MemucPath, request.InstanceIndex));
                results.Add(skipped);
                progress?.Report(new StepExecutionUpdate(step.Id, StepExecutionStatus.Skipped, skipped));
                continue;
            }

            var startedAt = DateTimeOffset.UtcNow;
            progress?.Report(new StepExecutionUpdate(step.Id, StepExecutionStatus.Running));
            StepExecutionResult result;

            try
            {
                if (step is DelayStep delay)
                {
                    if (delay.DurationMilliseconds < 0) throw new ArgumentOutOfRangeException(nameof(step), "Delay không được âm.");
                    await delayProvider.DelayAsync(TimeSpan.FromMilliseconds(delay.DurationMilliseconds), cancellationToken).ConfigureAwait(false);
                    result = CreateResult(step, StepExecutionStatus.Succeeded, startedAt, DateTimeOffset.UtcNow,
                        commandBuilder.BuildPreview(step, request.MemucPath, request.InstanceIndex));
                }
                else
                {
                    var command = commandBuilder.BuildProcessCommand(step, request.MemucPath, request.InstanceIndex);
                    var processResult = await processRunner.RunAsync(
                        new ProcessRequest(command.ExecutablePath, command.Arguments, TimeSpan.FromSeconds(step.TimeoutSeconds)),
                        cancellationToken).ConfigureAwait(false);
                    var status = processResult.ExitCode == 0 ? StepExecutionStatus.Succeeded : StepExecutionStatus.Failed;
                    result = CreateResult(step, status, processResult.StartedAt, processResult.EndedAt, command.Preview,
                        processResult.ExitCode, processResult.StandardOutput, processResult.StandardError);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                result = CreateResult(step, StepExecutionStatus.Cancelled, startedAt, DateTimeOffset.UtcNow,
                    SafePreview(step, request), standardError: "Đã hủy theo yêu cầu.");
            }
            catch (TimeoutException exception)
            {
                result = CreateResult(step, StepExecutionStatus.Failed, startedAt, DateTimeOffset.UtcNow,
                    SafePreview(step, request), standardError: exception.Message);
            }
            catch (Exception exception)
            {
                result = CreateResult(step, StepExecutionStatus.Failed, startedAt, DateTimeOffset.UtcNow,
                    SafePreview(step, request), standardError: exception.Message);
            }

            results.Add(result);
            progress?.Report(new StepExecutionUpdate(step.Id, result.Status, result));

            if (result.Status == StepExecutionStatus.Cancelled ||
                (result.Status == StepExecutionStatus.Failed && !step.ContinueOnError))
            {
                break;
            }
        }

        return new ExecutionResult
        {
            StartedAt = executionStartedAt,
            EndedAt = DateTimeOffset.UtcNow,
            WasCancelled = cancellationToken.IsCancellationRequested,
            Steps = results
        };
    }

    private string SafePreview(ScriptStep step, ExecutionRequest request)
    {
        try { return commandBuilder.BuildPreview(step, request.MemucPath, request.InstanceIndex); }
        catch (Exception) { return $"[{step.Kind}] {step.Name}"; }
    }

    private static StepExecutionResult CreateResult(
        ScriptStep step,
        StepExecutionStatus status,
        DateTimeOffset startedAt,
        DateTimeOffset endedAt,
        string commandPreview,
        int? exitCode = null,
        string standardOutput = "",
        string standardError = "") => new()
        {
            StepId = step.Id,
            Status = status,
            StartedAt = startedAt,
            EndedAt = endedAt,
            ExitCode = exitCode,
            StandardOutput = standardOutput,
            StandardError = standardError,
            CommandPreview = commandPreview
        };
}
