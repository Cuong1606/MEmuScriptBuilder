using System.Diagnostics;
using MEmuScriptStudio.Core.Processes;

namespace MEmuScriptStudio.Infrastructure.Processes;

public sealed class ProcessRunner : IProcessRunner
{
    public async Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.FileName);
        if (request.Timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(request));

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

        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
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
            TryKill(process);
            await WaitForTerminationAsync(process).ConfigureAwait(false);
            throw new TimeoutException($"Process vượt quá timeout {request.Timeout}.");
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            await WaitForTerminationAsync(process).ConfigureAwait(false);
            throw;
        }
    }

    private static void TryKill(Process process)
    {
        if (!process.HasExited) process.Kill(entireProcessTree: true);
    }

    private static async Task WaitForTerminationAsync(Process process)
    {
        try { await process.WaitForExitAsync().ConfigureAwait(false); }
        catch (InvalidOperationException) { }
    }
}
