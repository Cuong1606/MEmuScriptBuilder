using System.Diagnostics;
using MEmuScriptStudio.Core.Processes;

namespace MEmuScriptStudio.Infrastructure.Processes;

public sealed class ProcessRunner : IProcessRunner
{
    private static readonly TimeSpan CleanupGracePeriod = TimeSpan.FromSeconds(2);

    public async Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.FileName);
        if (request.Timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(request));
        cancellationToken.ThrowIfCancellationRequested();

        using var process = new Process
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

        var startedAt = DateTimeOffset.UtcNow;
        if (!process.Start()) throw new InvalidOperationException($"Không thể khởi động process '{request.FileName}'.");

        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        using var timeoutSource = new CancellationTokenSource(request.Timeout);
        using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);

        try
        {
            await process.WaitForExitAsync(linkedSource.Token).ConfigureAwait(false);
            var output = await outputTask.ConfigureAwait(false);
            var error = await errorTask.ConfigureAwait(false);
            return new ProcessResult(process.ExitCode, output, error, startedAt, DateTimeOffset.UtcNow);
        }
        catch (OperationCanceledException) when (timeoutSource.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            await CleanupAfterCancellationAsync(process, outputTask, errorTask).ConfigureAwait(false);
            throw new TimeoutException($"Process vượt quá timeout {request.Timeout}.");
        }
        catch (OperationCanceledException)
        {
            await CleanupAfterCancellationAsync(process, outputTask, errorTask).ConfigureAwait(false);
            throw;
        }
    }

    private static async Task CleanupAfterCancellationAsync(
        Process process,
        Task<string> outputTask,
        Task<string> errorTask)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (Exception)
        {
            // Cleanup is best-effort; it must not replace the timeout or cancellation requested by the caller.
        }

        using var cleanupSource = new CancellationTokenSource(CleanupGracePeriod);
        try
        {
            await process.WaitForExitAsync(cleanupSource.Token).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // The cleanup deadline is intentionally finite, even if the child cannot be terminated.
        }

        await ObserveIfCompletedAsync(outputTask).ConfigureAwait(false);
        await ObserveIfCompletedAsync(errorTask).ConfigureAwait(false);
    }

    private static async Task ObserveIfCompletedAsync(Task task)
    {
        if (!task.IsCompleted) return;
        try { await task.ConfigureAwait(false); }
        catch (Exception) { }
    }
}
