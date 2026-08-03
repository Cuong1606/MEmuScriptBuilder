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
                    var commands = commandBuilder.BuildProcessCommands(step, request.MemucPath, request.InstanceIndex);
                    result = await ExecuteProcessCommandsAsync(
                        step,
                        commands,
                        startedAt,
                        cancellationToken).ConfigureAwait(false);
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

    private async Task<StepExecutionResult> ExecuteProcessCommandsAsync(
        ScriptStep step,
        IReadOnlyList<MemuCommand> commands,
        DateTimeOffset stepStartedAt,
        CancellationToken cancellationToken)
    {
        var processResults = new List<(MemuCommand Command, ProcessResult Result)>();
        for (var index = 0; index < commands.Count; index++)
        {
            var command = commands[index];
            try
            {
                var processResult = await processRunner.RunAsync(
                    new ProcessRequest(command.ExecutablePath, command.Arguments, TimeSpan.FromSeconds(step.TimeoutSeconds)),
                    cancellationToken).ConfigureAwait(false);
                processResults.Add((command, processResult));
                if (processResult.ExitCode != 0) break;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return CreateInterruptedProcessResult(
                    step,
                    commands,
                    processResults,
                    index,
                    StepExecutionStatus.Cancelled,
                    stepStartedAt,
                    "Đã hủy theo yêu cầu.");
            }
            catch (TimeoutException exception)
            {
                return CreateInterruptedProcessResult(
                    step,
                    commands,
                    processResults,
                    index,
                    StepExecutionStatus.Failed,
                    stepStartedAt,
                    exception.Message);
            }
            catch (Exception exception)
            {
                return CreateInterruptedProcessResult(
                    step,
                    commands,
                    processResults,
                    index,
                    StepExecutionStatus.Failed,
                    stepStartedAt,
                    exception.Message);
            }
        }

        var firstResult = processResults[0].Result;
        var lastResult = processResults[^1].Result;
        var status = lastResult.ExitCode == 0 && processResults.Count == commands.Count
            ? StepExecutionStatus.Succeeded
            : StepExecutionStatus.Failed;
        var standardError = CombineProcessStream(processResults, item => item.StandardError, commands.Count > 1);
        if (step is InputTextStep { PressEnterAfterInput: true } && processResults.Count < commands.Count)
        {
            standardError = AppendLine(
                standardError,
                "Không chạy thao tác Nhấn Enter vì lệnh nhập văn bản không thành công.");
        }

        return CreateResult(
            step,
            status,
            firstResult.StartedAt,
            lastResult.EndedAt,
            string.Join(Environment.NewLine, commands.Select(command => command.Preview)),
            lastResult.ExitCode,
            CombineProcessStream(processResults, item => item.StandardOutput, commands.Count > 1),
            standardError);
    }

    private static StepExecutionResult CreateInterruptedProcessResult(
        ScriptStep step,
        IReadOnlyList<MemuCommand> commands,
        IReadOnlyList<(MemuCommand Command, ProcessResult Result)> processResults,
        int interruptedCommandIndex,
        StepExecutionStatus status,
        DateTimeOffset stepStartedAt,
        string error)
    {
        var standardError = CombineProcessStream(processResults, item => item.StandardError, commands.Count > 1);
        standardError = AppendLine(
            standardError,
            $"[{interruptedCommandIndex + 1}] {commands[interruptedCommandIndex].Preview}{Environment.NewLine}{error}");

        return CreateResult(
            step,
            status,
            processResults.Count > 0 ? processResults[0].Result.StartedAt : stepStartedAt,
            DateTimeOffset.UtcNow,
            string.Join(Environment.NewLine, commands.Select(command => command.Preview)),
            processResults.Count > 0 ? processResults[^1].Result.ExitCode : null,
            CombineProcessStream(processResults, item => item.StandardOutput, commands.Count > 1),
            standardError);
    }

    private static string CombineProcessStream(
        IReadOnlyList<(MemuCommand Command, ProcessResult Result)> processResults,
        Func<ProcessResult, string> selector,
        bool labelCommands)
    {
        if (!labelCommands && processResults.Count == 1) return selector(processResults[0].Result);

        return string.Join(
            Environment.NewLine,
            processResults
                .Select((item, index) => (item.Command, Index: index + 1, Value: selector(item.Result)))
                .Where(item => !string.IsNullOrWhiteSpace(item.Value))
                .Select(item => $"[{item.Index}] {item.Command.Preview}{Environment.NewLine}{item.Value}"));
    }

    private static string AppendLine(string current, string value) =>
        string.IsNullOrWhiteSpace(current) ? value : $"{current}{Environment.NewLine}{value}";

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
