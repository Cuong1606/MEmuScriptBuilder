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
    public async Task RunAsync_LargeOutputIsCappedAndMarkedIndependentlyPerStream()
    {
        var process = new FakeProcessHandle();
        var boundedRunner = new ProcessRunner(new FakeProcessHandleFactory(process));
        var runTask = boundedRunner.RunAsync(SafeMemucRequest(TimeSpan.FromSeconds(5)), CancellationToken.None);
        await process.WaitStartedAsync();
        process.CompleteNaturalExit(closeStreams: false);
        process.CompleteStreams(
            new string('o', ProcessRunner.MaximumCapturedCharactersPerStream + 100),
            new string('e', ProcessRunner.MaximumCapturedCharactersPerStream + 200));

        var result = await runTask.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.AreEqual(ProcessRunner.MaximumCapturedCharactersPerStream, result.StandardOutput.Length);
        Assert.AreEqual(ProcessRunner.MaximumCapturedCharactersPerStream, result.StandardError.Length);
        StringAssert.StartsWith(result.StandardOutput, "ooo");
        StringAssert.StartsWith(result.StandardError, "eee");
        StringAssert.Contains(result.StandardOutput, "[stdout truncated after 65536 characters]");
        StringAssert.Contains(result.StandardError, "[stderr truncated after 65536 characters]");
    }

    [TestMethod]
    public async Task RunAsync_LargeStdOutAndStdErrDrainConcurrentlyWithoutDeadlock()
    {
        const string script = "for ($i=0; $i -lt 128; $i++) { " +
                              "[Console]::Out.Write(('o' * 2048)); " +
                              "[Console]::Error.Write(('e' * 2048)) }";

        var result = await runner.RunAsync(
                PowerShellRequest(script, TimeSpan.FromSeconds(10)),
                CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(15));

        Assert.AreEqual(0, result.ExitCode);
        Assert.AreEqual(ProcessRunner.MaximumCapturedCharactersPerStream, result.StandardOutput.Length);
        Assert.AreEqual(ProcessRunner.MaximumCapturedCharactersPerStream, result.StandardError.Length);
        StringAssert.Contains(result.StandardOutput, "[stdout truncated after 65536 characters]");
        StringAssert.Contains(result.StandardError, "[stderr truncated after 65536 characters]");
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
    public async Task RunAsync_CallerCancellationTerminatesOnlyDirectProcessAndDoesNotDeadlockStreams()
    {
        var directory = CreateTestDirectory();
        var childPidPath = Path.Combine(directory, "child.pid");
        int? childPid = null;
        try
        {
            using var cancellationSource = new CancellationTokenSource();
            var stopwatch = Stopwatch.StartNew();
            var runTask = runner.RunAsync(
                PowerShellRequest(StartSleepingChildScript(childPidPath), TimeSpan.FromSeconds(30)),
                cancellationSource.Token);
            childPid = await WaitForProcessIdAsync(childPidPath);

            cancellationSource.Cancel();
            await AssertCancellationAsync(runTask);

            Assert.IsTrue(stopwatch.Elapsed < TimeSpan.FromSeconds(5));
            Assert.IsTrue(IsProcessAlive(childPid.Value),
                "Direct-process cancellation must not terminate a child process from the created command hierarchy.");
        }
        finally
        {
            if (childPid is int processId) StopTestProcess(processId);
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task RunAsync_TimeoutTerminatesOnlyDirectProcess()
    {
        var directory = CreateTestDirectory();
        var childPidPath = Path.Combine(directory, "child.pid");
        int? childPid = null;
        try
        {
            var runTask = runner.RunAsync(
                PowerShellRequest(StartSleepingChildScript(childPidPath), TimeSpan.FromSeconds(2)),
                CancellationToken.None);
            childPid = await WaitForProcessIdAsync(childPidPath);

            await Assert.ThrowsExceptionAsync<TimeoutException>(() => runTask);

            Assert.IsTrue(IsProcessAlive(childPid.Value),
                "Direct-process timeout cleanup must not tree-kill descendants of the command process.");
        }
        finally
        {
            if (childPid is int processId) StopTestProcess(processId);
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task RunAsync_ProcessExitingDuringCancellationGraceIsNotForceKilled()
    {
        var directory = CreateTestDirectory();
        var completionPath = Path.Combine(directory, "completed.txt");
        try
        {
            using var cancellationSource = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
            var escapedPath = EscapePowerShellLiteral(completionPath);

            await AssertCancellationAsync(runner.RunAsync(
                PowerShellRequest(
                    $"Start-Sleep -Milliseconds 150; [System.IO.File]::WriteAllText('{escapedPath}', 'completed')",
                    TimeSpan.FromSeconds(10)),
                cancellationSource.Token));

            Assert.AreEqual("completed", await File.ReadAllTextAsync(completionPath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task RunAsync_MemucUserCancellationNeverKillsAndWaitsForNaturalExitAndStreamCleanup()
    {
        var process = new FakeProcessHandle();
        var logger = new RecordingLifecycleLogger();
        var safeRunner = new ProcessRunner(new FakeProcessHandleFactory(process), logger);
        using var cancellationSource = new CancellationTokenSource();
        var runTask = safeRunner.RunAsync(SafeMemucRequest(TimeSpan.FromSeconds(30)), cancellationSource.Token);
        await process.WaitStartedAsync();

        cancellationSource.Cancel();
        await Task.Delay(650);

        Assert.IsFalse(runTask.IsCompleted,
            "SAFE STOP must remain pending after the old 500 ms grace while MEMUC is still running.");
        Assert.AreEqual(0, process.DirectKillCount);
        Assert.AreEqual(0, process.TreeKillCount);
        Assert.IsTrue(logger.Events.Any(item =>
            item.EventKind == ProcessLifecycleEventKind.UserCancellationRequested &&
            item.Marker == "NO_KILL_USER_CANCELLATION"));

        process.CompleteNaturalExit(closeStreams: false);
        await Task.Delay(100);
        Assert.IsFalse(runTask.IsCompleted, "Cancellation must wait for redirected stream cleanup after process exit.");

        process.CompleteStreams("safe-output", "safe-error");
        await AssertCancellationAsync(runTask.WaitAsync(TimeSpan.FromSeconds(2)));

        Assert.AreEqual(0, process.DirectKillCount);
        Assert.AreEqual(0, process.TreeKillCount);
        CollectionAssert.IsSubsetOf(
            new[]
            {
                ProcessLifecycleEventKind.UserCancellationRequested,
                ProcessLifecycleEventKind.UserCancellationNaturalExit,
                ProcessLifecycleEventKind.CleanupCompleted
            },
            logger.Events.Select(item => item.EventKind).ToArray());
        Assert.IsTrue(logger.Events
            .Where(item => item.EventKind is ProcessLifecycleEventKind.UserCancellationNaturalExit or ProcessLifecycleEventKind.CleanupCompleted)
            .All(item => item.Marker == "NO_KILL_USER_CANCELLATION"));
    }

    [TestMethod]
    public async Task RunAsync_MemucTimeoutDirectKillsWithoutTreeKillAndLogsDistinctLifecycle()
    {
        var process = new FakeProcessHandle();
        var logger = new RecordingLifecycleLogger();
        var timeoutRunner = new ProcessRunner(new FakeProcessHandleFactory(process), logger);

        await Assert.ThrowsExceptionAsync<TimeoutException>(() => timeoutRunner.RunAsync(
            SafeMemucRequest(TimeSpan.FromMilliseconds(20)),
            CancellationToken.None));

        Assert.AreEqual(1, process.DirectKillCount);
        Assert.AreEqual(0, process.TreeKillCount);
        Assert.IsTrue(logger.Events.Any(item => item.EventKind == ProcessLifecycleEventKind.TimeoutDetected));
        Assert.IsTrue(logger.Events.Any(item => item.EventKind == ProcessLifecycleEventKind.TimeoutDirectKill));
        Assert.IsFalse(logger.Events.Any(item => item.Marker == "NO_KILL_USER_CANCELLATION"));
        Assert.IsFalse(logger.Events.Any(item => item.EventKind == ProcessLifecycleEventKind.TimeoutTreeKill));
    }

    [TestMethod]
    public async Task RunAsync_MemucTimeoutKillDirectThrows_QuarantinesUntilProcessExits()
    {
        var process = new FakeProcessHandle
        {
            DirectKillException = new InvalidOperationException("simulated direct-kill failure")
        };
        var logger = new RecordingLifecycleLogger();
        var timeoutRunner = new ProcessRunner(new FakeProcessHandleFactory(process), logger);
        var runTask = timeoutRunner.RunAsync(
            SafeMemucRequest(TimeSpan.FromMilliseconds(20)),
            CancellationToken.None);

        await process.DirectKillAttempted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await WaitUntilAsync(
            () => logger.Events.Any(item => item.EventKind == ProcessLifecycleEventKind.TimeoutQuarantined),
            TimeSpan.FromSeconds(2));

        Assert.IsFalse(runTask.IsCompleted);
        Assert.AreEqual(0, process.DisposeCount);
        Assert.AreEqual(0, process.TreeKillCount);
        Assert.IsTrue(logger.Events.Any(item =>
            item.EventKind == ProcessLifecycleEventKind.TimeoutTerminationFailed &&
            item.Marker?.Contains(nameof(InvalidOperationException), StringComparison.Ordinal) == true));
        Assert.IsFalse(logger.Events.Any(item => item.EventKind == ProcessLifecycleEventKind.CleanupCompleted));

        process.CompleteNaturalExit();
        await Assert.ThrowsExceptionAsync<TimeoutException>(() => runTask.WaitAsync(TimeSpan.FromSeconds(2)));

        Assert.AreEqual(1, process.DisposeCount);
        Assert.IsTrue(logger.Events.Any(item => item.EventKind == ProcessLifecycleEventKind.TimeoutQuarantineExited));
        Assert.IsTrue(logger.Events.Any(item => item.EventKind == ProcessLifecycleEventKind.CleanupCompleted));
    }

    [TestMethod]
    public async Task RunAsync_MemucTimeoutKillDirectReturnsWhileAlive_QuarantinesUntilProcessExits()
    {
        var process = new FakeProcessHandle { ExitOnDirectKill = false };
        var logger = new RecordingLifecycleLogger();
        var timeoutRunner = new ProcessRunner(new FakeProcessHandleFactory(process), logger);
        var runTask = timeoutRunner.RunAsync(
            SafeMemucRequest(TimeSpan.FromMilliseconds(20)),
            CancellationToken.None);

        await process.DirectKillAttempted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await WaitUntilAsync(
            () => logger.Events.Any(item => item.EventKind == ProcessLifecycleEventKind.TimeoutQuarantined),
            TimeSpan.FromSeconds(4));

        Assert.IsFalse(runTask.IsCompleted);
        Assert.AreEqual(0, process.DisposeCount);
        Assert.AreEqual(1, process.DirectKillCount);
        Assert.AreEqual(0, process.TreeKillCount);
        Assert.IsFalse(logger.Events.Any(item => item.EventKind == ProcessLifecycleEventKind.CleanupCompleted));

        process.CompleteNaturalExit();
        await Assert.ThrowsExceptionAsync<TimeoutException>(() => runTask.WaitAsync(TimeSpan.FromSeconds(2)));

        Assert.AreEqual(1, process.DisposeCount);
        Assert.IsTrue(logger.Events.Any(item => item.EventKind == ProcessLifecycleEventKind.TimeoutQuarantineExited));
    }

    [TestMethod]
    public async Task RunAsync_MemucTimeoutLateExit_DrainsStreamsBeforeTerminalCleanup()
    {
        var process = new FakeProcessHandle { ExitOnDirectKill = false };
        var logger = new RecordingLifecycleLogger();
        var timeoutRunner = new ProcessRunner(new FakeProcessHandleFactory(process), logger);
        var runTask = timeoutRunner.RunAsync(
            SafeMemucRequest(TimeSpan.FromMilliseconds(20)),
            CancellationToken.None);

        await WaitUntilAsync(
            () => logger.Events.Any(item => item.EventKind == ProcessLifecycleEventKind.TimeoutQuarantined),
            TimeSpan.FromSeconds(4));
        process.CompleteNaturalExit(closeStreams: false);
        await Task.Delay(100);

        Assert.IsFalse(runTask.IsCompleted, "Terminal timeout must wait for redirected streams after the late PID exit.");
        Assert.AreEqual(0, process.DisposeCount);
        Assert.IsFalse(logger.Events.Any(item => item.EventKind == ProcessLifecycleEventKind.CleanupCompleted));

        process.CompleteStreams("late-output", "late-error");
        await Assert.ThrowsExceptionAsync<TimeoutException>(() => runTask.WaitAsync(TimeSpan.FromSeconds(2)));

        Assert.AreEqual(1, process.DisposeCount);
        Assert.IsTrue(logger.Events.Any(item => item.EventKind == ProcessLifecycleEventKind.CleanupCompleted));
    }

    [TestMethod]
    public async Task RunAsync_MemucUserCancellationKeepsIndependentTimeoutDeadline()
    {
        var process = new FakeProcessHandle();
        var logger = new RecordingLifecycleLogger();
        var timeoutRunner = new ProcessRunner(new FakeProcessHandleFactory(process), logger);
        using var cancellationSource = new CancellationTokenSource();
        var runTask = timeoutRunner.RunAsync(SafeMemucRequest(TimeSpan.FromMilliseconds(100)), cancellationSource.Token);
        await process.WaitStartedAsync();

        cancellationSource.Cancel();
        await AssertCancellationAsync(runTask.WaitAsync(TimeSpan.FromSeconds(2)));

        Assert.AreEqual(1, process.DirectKillCount,
            "Only the independent timeout policy may terminate a MEMUC process that did not exit after Stop.");
        Assert.AreEqual(0, process.TreeKillCount);
        Assert.IsTrue(logger.Events.Any(item =>
            item.EventKind == ProcessLifecycleEventKind.UserCancellationRequested &&
            item.Marker == "NO_KILL_USER_CANCELLATION"));
        Assert.IsTrue(logger.Events.Any(item => item.EventKind == ProcessLifecycleEventKind.TimeoutDetected));
        Assert.IsTrue(logger.Events.Any(item => item.EventKind == ProcessLifecycleEventKind.TimeoutDirectKill));
        Assert.IsFalse(logger.Events.Any(item =>
            item.EventKind is ProcessLifecycleEventKind.UserCancellationDirectKill or
                ProcessLifecycleEventKind.UserCancellationTreeKill));
    }

    [TestMethod]
    public async Task RunAsync_MemucNaturalExitDrainsStreamsWithoutDeadlock()
    {
        var process = new FakeProcessHandle();
        var safeRunner = new ProcessRunner(new FakeProcessHandleFactory(process));
        var runTask = safeRunner.RunAsync(SafeMemucRequest(TimeSpan.FromSeconds(5)), CancellationToken.None);
        await process.WaitStartedAsync();

        process.CompleteNaturalExit(closeStreams: false);
        await Task.Delay(100);
        Assert.IsFalse(runTask.IsCompleted);
        process.CompleteStreams("out", "err");

        var result = await runTask.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.AreEqual("out", result.StandardOutput);
        Assert.AreEqual("err", result.StandardError);
    }

    [DataTestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task RunAsync_NaturalStreamReadFailure_IsNotConvertedToSuccessfulEmptyOutput(bool failStandardOutput)
    {
        var process = new FakeProcessHandle();
        var logger = new RecordingLifecycleLogger();
        var safeRunner = new ProcessRunner(new FakeProcessHandleFactory(process), logger);
        var runTask = safeRunner.RunAsync(SafeMemucRequest(TimeSpan.FromSeconds(5)), CancellationToken.None);
        await process.WaitStartedAsync();

        process.CompleteNaturalExit(closeStreams: false);
        if (failStandardOutput)
        {
            process.FailStandardOutput(new IOException("simulated stdout read failure"));
            process.CompleteStandardError("stderr");
        }
        else
        {
            process.CompleteStandardOutput("stdout");
            process.FailStandardError(new IOException("simulated stderr read failure"));
        }

        await Assert.ThrowsExceptionAsync<IOException>(() => runTask.WaitAsync(TimeSpan.FromSeconds(2)));

        Assert.AreEqual(1, process.DisposeCount);
        Assert.IsFalse(logger.Events.Any(item => item.EventKind == ProcessLifecycleEventKind.CleanupCompleted));
    }

    [TestMethod]
    public async Task RunAsync_UserCancellationPreservesCancellationWhenStreamDrainFails()
    {
        var process = new FakeProcessHandle();
        var safeRunner = new ProcessRunner(new FakeProcessHandleFactory(process));
        using var cancellationSource = new CancellationTokenSource();
        var runTask = safeRunner.RunAsync(SafeMemucRequest(TimeSpan.FromSeconds(5)), cancellationSource.Token);
        await process.WaitStartedAsync();

        cancellationSource.Cancel();
        process.CompleteNaturalExit(closeStreams: false);
        process.FailStandardOutput(new IOException("simulated cancellation drain failure"));
        process.CompleteStandardError("stderr");

        await AssertCancellationAsync(runTask.WaitAsync(TimeSpan.FromSeconds(2)));
    }

    [TestMethod]
    public async Task RunAsync_TimeoutPreservesTimeoutWhenStreamDrainFails()
    {
        var process = new FakeProcessHandle { ExitOnDirectKill = false };
        var timeoutRunner = new ProcessRunner(new FakeProcessHandleFactory(process));
        var runTask = timeoutRunner.RunAsync(
            SafeMemucRequest(TimeSpan.FromMilliseconds(20)),
            CancellationToken.None);

        await process.DirectKillAttempted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        process.CompleteNaturalExit(closeStreams: false);
        process.FailStandardOutput(new IOException("simulated timeout drain failure"));
        process.CompleteStandardError("stderr");

        await Assert.ThrowsExceptionAsync<TimeoutException>(() => runTask.WaitAsync(TimeSpan.FromSeconds(2)));
    }

    [TestMethod]
    public async Task RunAsync_StreamReadFailureTriggeredByForcedCloseIsNotReportedAsSuccess()
    {
        var process = new FakeProcessHandle
        {
            StandardOutputCloseException = new IOException("simulated delayed stdout failure")
        };
        var safeRunner = new ProcessRunner(new FakeProcessHandleFactory(process));
        var runTask = safeRunner.RunAsync(SafeMemucRequest(TimeSpan.FromSeconds(5)), CancellationToken.None);
        await process.WaitStartedAsync();

        process.CompleteNaturalExit(closeStreams: false);

        await Assert.ThrowsExceptionAsync<IOException>(() => runTask.WaitAsync(TimeSpan.FromSeconds(2)));
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
        new("powershell.exe", ["-NoProfile", "-NonInteractive", "-Command", script], timeout,
            ProcessCancellationPolicy.DirectProcessOnly,
            ProcessTimeoutPolicy.DirectProcessOnly);

    private static ProcessRequest SafeMemucRequest(TimeSpan timeout) =>
        new(
            "memuc.exe",
            ["-i", "4", "adb", "shell", "safe-category"],
            timeout,
            ProcessCancellationPolicy.WaitForNaturalExit,
            ProcessTimeoutPolicy.DirectProcessOnly,
            new ProcessDiagnosticContext(4, "ScriptStep:AndroidShell"));

    private static string StartSleepingChildScript(string childPidPath) => $"""
        $child = Start-Process -FilePath 'powershell.exe' -WindowStyle Hidden -ArgumentList @('-NoProfile','-NonInteractive','-Command','Start-Sleep -Seconds 30') -PassThru
        [System.IO.File]::WriteAllText('{EscapePowerShellLiteral(childPidPath)}', $child.Id.ToString())
        [Console]::Out.Write('parent-ready')
        Start-Sleep -Seconds 30
        """;

    private static string EscapePowerShellLiteral(string value) => value.Replace("'", "''", StringComparison.Ordinal);

    private static string CreateTestDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "MEmuScriptStudio-ProcessRunnerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static async Task<int> WaitForProcessIdAsync(string path)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (File.Exists(path) && int.TryParse(await File.ReadAllTextAsync(path), out var processId))
                return processId;
            await Task.Delay(20);
        }

        Assert.Fail("Timed out waiting for the child process id.");
        return 0;
    }

    private static async Task AssertCancellationAsync(Task task)
    {
        Exception? captured = null;
        try { await task; }
        catch (Exception exception) { captured = exception; }
        Assert.IsInstanceOfType<OperationCanceledException>(captured);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition() && DateTime.UtcNow < deadline) await Task.Delay(10);
        Assert.IsTrue(condition(), "Timed out waiting for the expected process lifecycle transition.");
    }

    private static bool IsProcessAlive(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static void StopTestProcess(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            if (process.HasExited) return;
            process.Kill();
            process.WaitForExit(2000);
        }
        catch (ArgumentException)
        {
        }
    }

    private sealed class FakeProcessHandleFactory(FakeProcessHandle process) : IProcessHandleFactory
    {
        public IProcessHandle Start(ProcessRequest request)
        {
            process.MarkStarted();
            return process;
        }
    }

    private sealed class FakeProcessHandle : IProcessHandle
    {
        private readonly TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource exited = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<string> output = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<string> error = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int hasExited;

        public int Id => 4242;
        public bool HasExited => Volatile.Read(ref hasExited) != 0;
        public int ExitCode => HasExited ? 0 : throw new InvalidOperationException("Process is still running.");
        public int DirectKillCount { get; private set; }
        public int TreeKillCount { get; private set; }
        public int DisposeCount { get; private set; }
        public bool ExitOnDirectKill { get; init; } = true;
        public Exception? DirectKillException { get; init; }
        public Exception? StandardOutputCloseException { get; init; }
        public TaskCompletionSource DirectKillAttempted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void MarkStarted() => started.TrySetResult();
        public Task WaitStartedAsync() => started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        public async Task<BoundedTextCapture> ReadStandardOutputToEndAsync(int maximumCharacters) =>
            Capture(await output.Task, maximumCharacters);
        public async Task<BoundedTextCapture> ReadStandardErrorToEndAsync(int maximumCharacters) =>
            Capture(await error.Task, maximumCharacters);
        public Task WaitForExitAsync(CancellationToken cancellationToken) => exited.Task.WaitAsync(cancellationToken);

        public void KillDirect()
        {
            DirectKillCount++;
            DirectKillAttempted.TrySetResult();
            if (DirectKillException is not null) throw DirectKillException;
            if (ExitOnDirectKill) CompleteNaturalExit();
        }

        public void KillTree()
        {
            TreeKillCount++;
            CompleteNaturalExit();
        }

        public void CompleteNaturalExit(bool closeStreams = true)
        {
            Interlocked.Exchange(ref hasExited, 1);
            exited.TrySetResult();
            if (closeStreams) CompleteStreams(string.Empty, string.Empty);
        }

        public void CompleteStreams(string standardOutput, string standardError)
        {
            CompleteStandardOutput(standardOutput);
            CompleteStandardError(standardError);
        }

        public void CompleteStandardOutput(string value) => output.TrySetResult(value);
        public void CompleteStandardError(string value) => error.TrySetResult(value);
        public void FailStandardOutput(Exception exception) => output.TrySetException(exception);
        public void FailStandardError(Exception exception) => error.TrySetException(exception);

        public void CloseStandardStreams()
        {
            if (StandardOutputCloseException is null) CompleteStandardOutput(string.Empty);
            else FailStandardOutput(StandardOutputCloseException);
            CompleteStandardError(string.Empty);
        }
        public void Dispose() => DisposeCount++;

        private static BoundedTextCapture Capture(string value, int maximumCharacters) =>
            value.Length <= maximumCharacters
                ? new BoundedTextCapture(value, false)
                : new BoundedTextCapture(value[..maximumCharacters], true);
    }

    private sealed class RecordingLifecycleLogger : IProcessLifecycleLogger
    {
        private readonly object syncRoot = new();
        private readonly List<ProcessLifecycleDiagnostic> events = [];

        public IReadOnlyList<ProcessLifecycleDiagnostic> Events
        {
            get
            {
                lock (syncRoot) return events.ToArray();
            }
        }

        public void Write(ProcessLifecycleDiagnostic diagnostic)
        {
            lock (syncRoot) events.Add(diagnostic);
        }
    }
}
