using System.Diagnostics;
using MEmuScriptStudio.Core.Processes;
using MEmuScriptStudio.Infrastructure.Processes;

namespace MEmuScriptStudio.Infrastructure.Tests;

[TestClass]
public sealed class ProcessRunnerTests
{
    private readonly ProcessRunner runner = new();

    [TestMethod]
    public async Task RunAsync_CapturesOutputErrorAndExitCode()
    {
        var request = PowerShellRequest(
            "[Console]::Out.Write('standard-out'); [Console]::Error.Write('standard-error'); exit 7",
            TimeSpan.FromSeconds(10));

        var result = await runner.RunAsync(request, CancellationToken.None);

        Assert.AreEqual(7, result.ExitCode);
        Assert.AreEqual("standard-out", result.StandardOutput);
        Assert.AreEqual("standard-error", result.StandardError);
        Assert.IsTrue(result.EndedAt >= result.StartedAt);
    }

    [TestMethod]
    public async Task RunAsync_TimeoutIsFiniteAndPreservesTimeoutException()
    {
        var stopwatch = Stopwatch.StartNew();

        await Assert.ThrowsExceptionAsync<TimeoutException>(() => runner.RunAsync(
            PowerShellRequest("Start-Sleep -Seconds 30", TimeSpan.FromMilliseconds(150)),
            CancellationToken.None));

        Assert.IsTrue(stopwatch.Elapsed < TimeSpan.FromSeconds(5));
    }

    [TestMethod]
    public async Task RunAsync_CallerCancellationIsFiniteAndPreservesCancellation()
    {
        using var cancellationSource = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
        var stopwatch = Stopwatch.StartNew();

        Exception? capturedException = null;
        try
        {
            await runner.RunAsync(
                PowerShellRequest("Start-Sleep -Seconds 30", TimeSpan.FromSeconds(30)),
                cancellationSource.Token);
        }
        catch (Exception exception)
        {
            capturedException = exception;
        }

        Assert.IsTrue(capturedException is OperationCanceledException);
        Assert.IsTrue(stopwatch.Elapsed < TimeSpan.FromSeconds(5));
    }

    [TestMethod]
    public async Task RunAsync_PreCancelledTokenDoesNotStartProcess()
    {
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();

        await Assert.ThrowsExceptionAsync<OperationCanceledException>(() => runner.RunAsync(
            new ProcessRequest("missing-process-that-must-not-start.exe", [], TimeSpan.FromSeconds(1)),
            cancellationSource.Token));
    }

    [TestMethod]
    public async Task RunAsync_RejectsNonPositiveTimeout()
    {
        await Assert.ThrowsExceptionAsync<ArgumentOutOfRangeException>(() => runner.RunAsync(
            new ProcessRequest("unused.exe", [], TimeSpan.Zero),
            CancellationToken.None));
    }

    private static ProcessRequest PowerShellRequest(string script, TimeSpan timeout) =>
        new("powershell.exe", ["-NoProfile", "-NonInteractive", "-Command", script], timeout);
}
