using System.Buffers;
using System.Diagnostics;
using System.Text;
using MEmuScriptStudio.Core.Processes;

namespace MEmuScriptStudio.Infrastructure.Processes;

internal interface IProcessHandleFactory
{
    IProcessHandle Start(ProcessRequest request);
}

internal interface IProcessHandle : IDisposable
{
    int Id { get; }
    bool HasExited { get; }
    int ExitCode { get; }
    Task<BoundedTextCapture> ReadStandardOutputToEndAsync(int maximumCharacters);
    Task<BoundedTextCapture> ReadStandardErrorToEndAsync(int maximumCharacters);
    Task WaitForExitAsync(CancellationToken cancellationToken);
    void KillDirect();
    void KillTree();
    void CloseStandardStreams();
}

internal readonly record struct BoundedTextCapture(string Text, bool WasTruncated);

public sealed class ProcessRunner : IProcessRunner
{
    internal const int MaximumCapturedCharactersPerStream = 64 * 1024;
    private const string NoKillUserCancellationMarker = "NO_KILL_USER_CANCELLATION";
    private static readonly TimeSpan TimeoutGracePeriod = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan TerminationGracePeriod = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan StreamDrainGracePeriod = TimeSpan.FromMilliseconds(500);
    private readonly IProcessHandleFactory processFactory;
    private readonly IProcessLifecycleLogger? lifecycleLogger;

    public ProcessRunner(IProcessLifecycleLogger? lifecycleLogger = null)
        : this(new SystemProcessHandleFactory(), lifecycleLogger)
    {
    }

    internal ProcessRunner(IProcessHandleFactory processFactory, IProcessLifecycleLogger? lifecycleLogger = null)
    {
        this.processFactory = processFactory ?? throw new ArgumentNullException(nameof(processFactory));
        this.lifecycleLogger = lifecycleLogger;
    }

    public async Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.FileName);
        if (request.Timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(request));
        cancellationToken.ThrowIfCancellationRequested();

        var startedAt = DateTimeOffset.UtcNow;
        using var process = processFactory.Start(request);
        var processId = process.Id;
        WriteDiagnostic(request, ProcessLifecycleEventKind.Started, startedAt, startedAt, processId);

        var outputTask = process.ReadStandardOutputToEndAsync(MaximumCapturedCharactersPerStream);
        var errorTask = process.ReadStandardErrorToEndAsync(MaximumCapturedCharactersPerStream);
        using var timeoutSource = new CancellationTokenSource(request.Timeout);
        using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);

        try
        {
            await process.WaitForExitAsync(linkedSource.Token).ConfigureAwait(false);
            WriteDiagnostic(
                request,
                ProcessLifecycleEventKind.NaturalExit,
                startedAt,
                DateTimeOffset.UtcNow,
                processId,
                TryGetExitCode(process));
            var (output, error) = await DrainStreamsAsync(process, outputTask, errorTask).ConfigureAwait(false);
            var endedAt = DateTimeOffset.UtcNow;
            var exitCode = TryGetExitCode(process);
            WriteDiagnostic(request, ProcessLifecycleEventKind.CleanupCompleted, startedAt, endedAt, processId, exitCode);
            return new ProcessResult(exitCode ?? -1, output, error, startedAt, endedAt);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            var cancellationAt = DateTimeOffset.UtcNow;
            var marker = request.CancellationPolicy == ProcessCancellationPolicy.WaitForNaturalExit
                ? NoKillUserCancellationMarker
                : null;
            WriteDiagnostic(
                request,
                ProcessLifecycleEventKind.UserCancellationRequested,
                startedAt,
                cancellationAt,
                processId,
                TryGetExitCode(process),
                marker);

            if (request.CancellationPolicy == ProcessCancellationPolicy.WaitForNaturalExit)
            {
                // SAFE STOP: the caller token only prevents later work. It must never terminate
                // or detach from the currently running MEMUC command. The original timeout remains
                // an independent deadline and may apply its own direct-process policy later.
                try
                {
                    await process.WaitForExitAsync(timeoutSource.Token).ConfigureAwait(false);
                    WriteDiagnostic(
                        request,
                        ProcessLifecycleEventKind.UserCancellationNaturalExit,
                        startedAt,
                        DateTimeOffset.UtcNow,
                        processId,
                        TryGetExitCode(process),
                        NoKillUserCancellationMarker);
                }
                catch (OperationCanceledException) when (timeoutSource.IsCancellationRequested)
                {
                    WriteDiagnostic(
                        request,
                        ProcessLifecycleEventKind.TimeoutDetected,
                        startedAt,
                        DateTimeOffset.UtcNow,
                        processId,
                        TryGetExitCode(process));
                    await TerminateForTimeoutAsync(process, request, startedAt, processId).ConfigureAwait(false);
                }
            }
            else
            {
                await TerminateForUserCancellationAsync(process, request, startedAt, processId).ConfigureAwait(false);
            }

            await DrainStreamsPreservingTerminalStateAsync(process, outputTask, errorTask).ConfigureAwait(false);
            var cancellationCleanupAt = DateTimeOffset.UtcNow;
            var cancellationExitCode = TryGetExitCode(process);
            WriteDiagnostic(
                request,
                ProcessLifecycleEventKind.CleanupCompleted,
                startedAt,
                cancellationCleanupAt,
                processId,
                cancellationExitCode,
                marker);
            throw new OperationCanceledException(cancellationToken);
        }
        catch (OperationCanceledException) when (timeoutSource.IsCancellationRequested)
        {
            WriteDiagnostic(
                request,
                ProcessLifecycleEventKind.TimeoutDetected,
                startedAt,
                DateTimeOffset.UtcNow,
                processId,
                TryGetExitCode(process));

            await TerminateForTimeoutAsync(process, request, startedAt, processId).ConfigureAwait(false);
            await DrainStreamsPreservingTerminalStateAsync(process, outputTask, errorTask).ConfigureAwait(false);
            var timeoutCleanupAt = DateTimeOffset.UtcNow;
            var timeoutExitCode = TryGetExitCode(process);
            WriteDiagnostic(
                request,
                ProcessLifecycleEventKind.CleanupCompleted,
                startedAt,
                timeoutCleanupAt,
                processId,
                timeoutExitCode);
            throw new TimeoutException($"Process vượt quá timeout {request.Timeout}.");
        }
    }

    private async Task TerminateForUserCancellationAsync(
        IProcessHandle process,
        ProcessRequest request,
        DateTimeOffset startedAt,
        int processId)
    {
        if (await WaitForExitWithinAsync(process, TimeoutGracePeriod).ConfigureAwait(false)) return;

        try
        {
            if (request.CancellationPolicy == ProcessCancellationPolicy.EntireProcessTree)
            {
                process.KillTree();
                WriteDiagnostic(
                    request,
                    ProcessLifecycleEventKind.UserCancellationTreeKill,
                    startedAt,
                    DateTimeOffset.UtcNow,
                    processId,
                    TryGetExitCode(process));
            }
            else
            {
                process.KillDirect();
                WriteDiagnostic(
                    request,
                    ProcessLifecycleEventKind.UserCancellationDirectKill,
                    startedAt,
                    DateTimeOffset.UtcNow,
                    processId,
                    TryGetExitCode(process));
            }
        }
        catch (Exception)
        {
            // Best effort for generic process requests. MEMUC requests use WaitForNaturalExit.
        }

        await WaitForExitWithinAsync(process, TerminationGracePeriod).ConfigureAwait(false);
    }

    private async Task TerminateForTimeoutAsync(
        IProcessHandle process,
        ProcessRequest request,
        DateTimeOffset startedAt,
        int processId)
    {
        if (await WaitForExitWithinAsync(process, TimeoutGracePeriod).ConfigureAwait(false))
        {
            WriteDiagnostic(
                request,
                ProcessLifecycleEventKind.TimeoutNaturalExit,
                startedAt,
                DateTimeOffset.UtcNow,
                processId,
                TryGetExitCode(process));
            return;
        }

        Exception? terminationError = null;
        try
        {
            if (request.TimeoutPolicy == ProcessTimeoutPolicy.EntireProcessTree)
            {
                process.KillTree();
                WriteDiagnostic(
                    request,
                    ProcessLifecycleEventKind.TimeoutTreeKill,
                    startedAt,
                    DateTimeOffset.UtcNow,
                    processId,
                    TryGetExitCode(process));
            }
            else
            {
                process.KillDirect();
                WriteDiagnostic(
                    request,
                    ProcessLifecycleEventKind.TimeoutDirectKill,
                    startedAt,
                    DateTimeOffset.UtcNow,
                    processId,
                    TryGetExitCode(process));
            }
        }
        catch (Exception exception)
        {
            terminationError = exception;
            WriteDiagnostic(
                request,
                ProcessLifecycleEventKind.TimeoutTerminationFailed,
                startedAt,
                DateTimeOffset.UtcNow,
                processId,
                TryGetExitCode(process),
                $"{request.TimeoutPolicy}:{exception.GetType().Name}");
        }

        if (terminationError is null &&
            await WaitForExitWithinAsync(process, TerminationGracePeriod).ConfigureAwait(false))
            return;

        WriteDiagnostic(
            request,
            ProcessLifecycleEventKind.TimeoutQuarantined,
            startedAt,
            DateTimeOffset.UtcNow,
            processId,
            TryGetExitCode(process),
            terminationError is null ? "PROCESS_STILL_RUNNING_AFTER_TERMINATION_GRACE" : "TERMINATION_FAILED");

        // The timed-out command still owns this handle and its caller still owns the
        // target reservation. Do not detach, drain, dispose, or report a terminal
        // timeout until the exact child process is confirmed exited.
        while (!process.HasExited)
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);

        WriteDiagnostic(
            request,
            ProcessLifecycleEventKind.TimeoutQuarantineExited,
            startedAt,
            DateTimeOffset.UtcNow,
            processId,
            TryGetExitCode(process));
    }

    private static async Task<(string Output, string Error)> DrainStreamsAsync(
        IProcessHandle process,
        Task<BoundedTextCapture> outputTask,
        Task<BoundedTextCapture> errorTask)
    {
        var streamsTask = Task.WhenAll(outputTask, errorTask);
        if (await Task.WhenAny(streamsTask, Task.Delay(StreamDrainGracePeriod)).ConfigureAwait(false) == streamsTask)
        {
            await streamsTask.ConfigureAwait(false);
        }
        else
        {
            if (outputTask.IsFaulted) await outputTask.ConfigureAwait(false);
            if (errorTask.IsFaulted) await errorTask.ConfigureAwait(false);
            process.CloseStandardStreams();
            await ObserveWithinAsync(streamsTask, StreamDrainGracePeriod).ConfigureAwait(false);
            if (outputTask.IsFaulted || outputTask.IsCanceled) await outputTask.ConfigureAwait(false);
            if (errorTask.IsFaulted || errorTask.IsCanceled) await errorTask.ConfigureAwait(false);
        }

        return (
            outputTask.IsCompletedSuccessfully
                ? FormatCapturedStream(outputTask.Result, "stdout")
                : "[stdout capture unavailable after stream-drain timeout]",
            errorTask.IsCompletedSuccessfully
                ? FormatCapturedStream(errorTask.Result, "stderr")
                : "[stderr capture unavailable after stream-drain timeout]");
    }

    private static async Task DrainStreamsPreservingTerminalStateAsync(
        IProcessHandle process,
        Task<BoundedTextCapture> outputTask,
        Task<BoundedTextCapture> errorTask)
    {
        try { await DrainStreamsAsync(process, outputTask, errorTask).ConfigureAwait(false); }
        catch (Exception) { }
    }

    private static string FormatCapturedStream(BoundedTextCapture capture, string streamName)
    {
        if (!capture.WasTruncated) return capture.Text;

        var marker = $"{Environment.NewLine}[{streamName} truncated after {MaximumCapturedCharactersPerStream} characters]";
        var maximumPrefixLength = Math.Max(0, MaximumCapturedCharactersPerStream - marker.Length);
        var prefix = capture.Text.Length <= maximumPrefixLength
            ? capture.Text
            : capture.Text[..maximumPrefixLength];
        return prefix + marker;
    }

    private static async Task<bool> WaitForExitWithinAsync(IProcessHandle process, TimeSpan timeout)
    {
        try
        {
            if (process.HasExited) return true;
            using var cleanupSource = new CancellationTokenSource(timeout);
            await process.WaitForExitAsync(cleanupSource.Token).ConfigureAwait(false);
            return true;
        }
        catch (Exception)
        {
            return process.HasExited;
        }
    }

    private static async Task ObserveWithinAsync(Task task, TimeSpan timeout)
    {
        var completed = await Task.WhenAny(task, Task.Delay(timeout)).ConfigureAwait(false);
        if (completed == task)
        {
            try { await task.ConfigureAwait(false); }
            catch (Exception) { }
            return;
        }

        _ = task.ContinueWith(
            completedTask => _ = completedTask.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private static int? TryGetExitCode(IProcessHandle process)
    {
        try { return process.HasExited ? process.ExitCode : null; }
        catch (Exception) { return null; }
    }

    private void WriteDiagnostic(
        ProcessRequest request,
        ProcessLifecycleEventKind eventKind,
        DateTimeOffset startedAt,
        DateTimeOffset timestamp,
        int processId,
        int? exitCode = null,
        string? marker = null)
    {
        if (lifecycleLogger is null) return;

        var context = request.DiagnosticContext;
        var category = string.IsNullOrWhiteSpace(context?.CommandCategory)
            ? "Process"
            : context.CommandCategory.Trim();
        if (category.Length > 80) category = category[..80];

        try
        {
            lifecycleLogger.Write(new ProcessLifecycleDiagnostic(
                eventKind,
                timestamp,
                timestamp - startedAt,
                processId,
                context?.InstanceIndex,
                category,
                exitCode,
                marker));
        }
        catch (Exception)
        {
            // Diagnostics are best-effort and cannot affect process lifecycle semantics.
        }
    }
}

internal sealed class SystemProcessHandleFactory : IProcessHandleFactory
{
    public IProcessHandle Start(ProcessRequest request)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = request.FileName,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };

        foreach (var argument in request.Arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        try
        {
            if (!process.Start())
                throw new InvalidOperationException($"Không thể khởi động process '{request.FileName}'.");
            return new SystemProcessHandle(process);
        }
        catch
        {
            process.Dispose();
            throw;
        }
    }
}

internal sealed class SystemProcessHandle(Process process) : IProcessHandle
{
    public int Id => process.Id;
    public bool HasExited => process.HasExited;
    public int ExitCode => process.ExitCode;
    public Task<BoundedTextCapture> ReadStandardOutputToEndAsync(int maximumCharacters) =>
        ReadBoundedToEndAsync(process.StandardOutput, maximumCharacters);
    public Task<BoundedTextCapture> ReadStandardErrorToEndAsync(int maximumCharacters) =>
        ReadBoundedToEndAsync(process.StandardError, maximumCharacters);
    public Task WaitForExitAsync(CancellationToken cancellationToken) => process.WaitForExitAsync(cancellationToken);
    public void KillDirect() => process.Kill();
    public void KillTree() => process.Kill(entireProcessTree: true);

    public void CloseStandardStreams()
    {
        try { process.StandardOutput.Close(); }
        catch (Exception) { }
        try { process.StandardError.Close(); }
        catch (Exception) { }
    }

    public void Dispose() => process.Dispose();

    private static async Task<BoundedTextCapture> ReadBoundedToEndAsync(
        TextReader reader,
        int maximumCharacters)
    {
        if (maximumCharacters <= 0) throw new ArgumentOutOfRangeException(nameof(maximumCharacters));

        const int bufferSize = 4096;
        var buffer = ArrayPool<char>.Shared.Rent(bufferSize);
        var captured = new StringBuilder(Math.Min(bufferSize, maximumCharacters), maximumCharacters);
        var wasTruncated = false;
        try
        {
            while (true)
            {
                var count = await reader
                    .ReadAsync(buffer.AsMemory(0, bufferSize), CancellationToken.None)
                    .ConfigureAwait(false);
                if (count == 0) break;

                var remaining = maximumCharacters - captured.Length;
                var retainedCount = Math.Min(count, Math.Max(0, remaining));
                if (retainedCount > 0) captured.Append(buffer, 0, retainedCount);
                if (retainedCount < count) wasTruncated = true;
            }

            return new BoundedTextCapture(captured.ToString(), wasTruncated);
        }
        finally
        {
            ArrayPool<char>.Shared.Return(buffer);
        }
    }
}
