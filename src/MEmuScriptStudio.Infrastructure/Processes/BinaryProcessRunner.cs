using System.Diagnostics;
using System.Text;
using MEmuScriptStudio.Core.Processes;

namespace MEmuScriptStudio.Infrastructure.Processes;

internal interface IBinaryProcessHandleFactory
{
    IBinaryProcessHandle Start(BinaryProcessRequest request);
}

internal interface IBinaryProcessHandle : IDisposable
{
    bool HasExited { get; }
    int ExitCode { get; }
    Task<BoundedBinaryCapture> ReadStandardOutputToEndAsync(int maximumBytes);
    Task<string> ReadStandardErrorToEndAsync(int maximumCharacters);
    Task WaitForExitAsync(CancellationToken cancellationToken);
    void KillTree();
    void CloseStandardStreams();
}

internal readonly record struct BoundedBinaryCapture(byte[] Bytes, bool WasTruncated);

public sealed class BinaryProcessRunner : IBinaryProcessRunner
{
    public const int MaximumCapturedBytes = 32 * 1024 * 1024;
    public const int MaximumCapturedErrorCharacters = 64 * 1024;
    private static readonly TimeSpan DefaultTerminationGrace = TimeSpan.FromSeconds(2);
    private readonly IBinaryProcessHandleFactory processFactory;
    private readonly TimeSpan terminationGrace;

    public BinaryProcessRunner() : this(new SystemBinaryProcessHandleFactory(), DefaultTerminationGrace) { }

    internal BinaryProcessRunner(IBinaryProcessHandleFactory processFactory, TimeSpan terminationGrace)
    {
        this.processFactory = processFactory ?? throw new ArgumentNullException(nameof(processFactory));
        if (terminationGrace <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(terminationGrace));
        this.terminationGrace = terminationGrace;
    }

    public async Task<BinaryProcessResult> RunAsync(
        BinaryProcessRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.FileName);
        if (request.Timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(request));
        cancellationToken.ThrowIfCancellationRequested();

        var startedAt = DateTimeOffset.UtcNow;
        using var process = processFactory.Start(request);
        var standardOutputTask = process.ReadStandardOutputToEndAsync(MaximumCapturedBytes);
        var standardErrorTask = process.ReadStandardErrorToEndAsync(MaximumCapturedErrorCharacters);
        using var timeoutCancellation = new CancellationTokenSource(request.Timeout);
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutCancellation.Token);

        try
        {
            await process.WaitForExitAsync(linkedCancellation.Token).ConfigureAwait(false);
            await Task.WhenAll(standardOutputTask, standardErrorTask)
                .WaitAsync(linkedCancellation.Token)
                .ConfigureAwait(false);

            var output = await standardOutputTask.ConfigureAwait(false);
            return new BinaryProcessResult(
                process.ExitCode,
                output.Bytes,
                output.WasTruncated,
                await standardErrorTask.ConfigureAwait(false),
                startedAt,
                DateTimeOffset.UtcNow);
        }
        catch (OperationCanceledException) when (linkedCancellation.IsCancellationRequested)
        {
            await TerminateAndDrainAsync(process, standardOutputTask, standardErrorTask).ConfigureAwait(false);
            if (cancellationToken.IsCancellationRequested)
                throw new OperationCanceledException(cancellationToken);
            throw new TimeoutException(
                $"{request.CommandCategory} vượt quá thời gian chờ {request.Timeout.TotalSeconds:0.#} giây.");
        }
        catch
        {
            await TerminateAndDrainAsync(process, standardOutputTask, standardErrorTask).ConfigureAwait(false);
            throw;
        }
    }

    private async Task TerminateAndDrainAsync(
        IBinaryProcessHandle process,
        Task<BoundedBinaryCapture> standardOutputTask,
        Task<string> standardErrorTask)
    {
        if (!SafeHasExited(process))
        {
            try { process.KillTree(); }
            catch (Exception) { }

            if (!await WaitForExitWithinAsync(process, terminationGrace).ConfigureAwait(false))
            {
                // Do not detach from a screenshot command that could still own ADB or inherited
                // redirected handles. This quarantine is rare, but keeps lifecycle ownership exact.
                while (!SafeHasExited(process))
                    await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }

        process.CloseStandardStreams();
        try { await Task.WhenAll(standardOutputTask, standardErrorTask).ConfigureAwait(false); }
        catch (Exception) { }
    }

    private static async Task<bool> WaitForExitWithinAsync(IBinaryProcessHandle process, TimeSpan timeout)
    {
        try
        {
            if (process.HasExited) return true;
            using var cancellation = new CancellationTokenSource(timeout);
            await process.WaitForExitAsync(cancellation.Token).ConfigureAwait(false);
            return true;
        }
        catch (Exception) { return SafeHasExited(process); }
    }

    private static bool SafeHasExited(IBinaryProcessHandle process)
    {
        try { return process.HasExited; }
        catch (Exception) { return false; }
    }
}

internal sealed class SystemBinaryProcessHandleFactory : IBinaryProcessHandleFactory
{
    public IBinaryProcessHandle Start(BinaryProcessRequest request)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = request.FileName,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardErrorEncoding = Encoding.UTF8
            }
        };
        foreach (var argument in request.Arguments) process.StartInfo.ArgumentList.Add(argument);

        try
        {
            if (!process.Start())
                throw new InvalidOperationException($"Không thể khởi chạy {request.CommandCategory}.");
            return new SystemBinaryProcessHandle(process);
        }
        catch
        {
            process.Dispose();
            throw;
        }
    }
}

internal sealed class SystemBinaryProcessHandle(Process process) : IBinaryProcessHandle
{
    public bool HasExited => process.HasExited;
    public int ExitCode => process.ExitCode;
    public Task<BoundedBinaryCapture> ReadStandardOutputToEndAsync(int maximumBytes) =>
        ReadBinaryAsync(process.StandardOutput.BaseStream, maximumBytes);
    public Task<string> ReadStandardErrorToEndAsync(int maximumCharacters) =>
        ReadTextAsync(process.StandardError, maximumCharacters);
    public Task WaitForExitAsync(CancellationToken cancellationToken) => process.WaitForExitAsync(cancellationToken);
    public void KillTree() => process.Kill(entireProcessTree: true);

    public void CloseStandardStreams()
    {
        try { process.StandardOutput.Close(); }
        catch (Exception) { }
        try { process.StandardError.Close(); }
        catch (Exception) { }
    }

    public void Dispose() => process.Dispose();

    private static async Task<BoundedBinaryCapture> ReadBinaryAsync(Stream stream, int maximumBytes)
    {
        using var captured = new MemoryStream();
        var buffer = new byte[81920];
        var wasTruncated = false;
        while (true)
        {
            var read = await stream.ReadAsync(buffer).ConfigureAwait(false);
            if (read == 0) break;
            var remaining = maximumBytes - (int)captured.Length;
            if (remaining > 0) captured.Write(buffer, 0, Math.Min(remaining, read));
            if (read > remaining) wasTruncated = true;
        }
        return new BoundedBinaryCapture(captured.ToArray(), wasTruncated);
    }

    private static async Task<string> ReadTextAsync(StreamReader reader, int maximumCharacters)
    {
        var captured = new StringBuilder(Math.Min(4096, maximumCharacters));
        var buffer = new char[4096];
        while (true)
        {
            var read = await reader.ReadAsync(buffer).ConfigureAwait(false);
            if (read == 0) break;
            var remaining = maximumCharacters - captured.Length;
            if (remaining > 0) captured.Append(buffer, 0, Math.Min(remaining, read));
        }
        return captured.ToString();
    }
}
