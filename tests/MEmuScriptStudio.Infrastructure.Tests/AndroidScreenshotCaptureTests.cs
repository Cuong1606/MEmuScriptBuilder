using System.Buffers.Binary;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using MEmuScriptStudio.App;
using MEmuScriptStudio.App.Services;
using MEmuScriptStudio.Core.Android;
using MEmuScriptStudio.Core.Models;
using MEmuScriptStudio.Core.Processes;
using MEmuScriptStudio.Infrastructure.Android;
using MEmuScriptStudio.Infrastructure.Processes;

namespace MEmuScriptStudio.Infrastructure.Tests;

[TestClass]
public sealed class AndroidScreenshotCaptureTests
{
    [TestMethod]
    public async Task ScreenshotCapture_UsesExactSerialScopedExecOutCommandAndPreservesBytes()
    {
        var png = ValidPngBytes();
        var runner = new RecordingBinaryProcessRunner(new BinaryProcessResult(
            0, png, false, string.Empty, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
        var service = new AndroidScreenshotCaptureService(runner, new AdbCommandBuilder());

        var result = await service.CaptureAsync(@"C:\Android\adb.exe", "SERIAL-B", CancellationToken.None);

        Assert.AreEqual(@"C:\Android\adb.exe", runner.LastRequest!.FileName);
        CollectionAssert.AreEqual(
            new[] { "-s", "SERIAL-B", "exec-out", "screencap", "-p" },
            runner.LastRequest.Arguments.ToArray());
        CollectionAssert.AreEqual(png, result.PngBytes);
        Assert.AreSame(png, result.PngBytes);
    }

    [TestMethod]
    public async Task ScreenshotCapture_RejectsNonPngAndNonzeroExitWithoutReturningData()
    {
        var invalidPng = new AndroidScreenshotCaptureService(
            new RecordingBinaryProcessRunner(Result(0, [1, 2, 3], string.Empty)),
            new AdbCommandBuilder());
        var offline = new AndroidScreenshotCaptureService(
            new RecordingBinaryProcessRunner(Result(1, [], "error: device offline\r\n")),
            new AdbCommandBuilder());

        await Assert.ThrowsExceptionAsync<InvalidDataException>(() =>
            invalidPng.CaptureAsync(@"C:\Android\adb.exe", "SERIAL", CancellationToken.None));
        var exception = await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
            offline.CaptureAsync(@"C:\Android\adb.exe", "SERIAL", CancellationToken.None));
        StringAssert.Contains(exception.Message, "device offline");
    }

    [TestMethod]
    public async Task BinaryProcessRunner_ReadsStandardOutputAsExactBytes()
    {
        var runner = new BinaryProcessRunner();
        var command = "$b=[byte[]](0,13,10,26,255,128);" +
            "$s=[Console]::OpenStandardOutput();$s.Write($b,0,$b.Length);$s.Flush()";

        var result = await runner.RunAsync(
            new BinaryProcessRequest(
                "powershell.exe",
                ["-NoProfile", "-NonInteractive", "-Command", command],
                TimeSpan.FromSeconds(5),
                "Test:binary-output"),
            CancellationToken.None);

        Assert.AreEqual(0, result.ExitCode);
        Assert.IsFalse(result.StandardOutputTruncated);
        CollectionAssert.AreEqual(new byte[] { 0, 13, 10, 26, 255, 128 }, result.StandardOutput);
    }

    [TestMethod]
    public async Task BinaryProcessRunner_HonorsTimeoutAndCallerCancellation()
    {
        var runner = new BinaryProcessRunner();
        var request = new BinaryProcessRequest(
            "powershell.exe",
            ["-NoProfile", "-NonInteractive", "-Command", "Start-Sleep -Seconds 10"],
            TimeSpan.FromMilliseconds(150),
            "Test:timeout");

        await Assert.ThrowsExceptionAsync<TimeoutException>(() =>
            runner.RunAsync(request, CancellationToken.None));

        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
        var cancellableRequest = request with { Timeout = TimeSpan.FromSeconds(10) };
        await Assert.ThrowsExceptionAsync<OperationCanceledException>(() =>
            runner.RunAsync(cancellableRequest, cancellation.Token));
    }

    [TestMethod]
    public async Task BinaryProcessRunner_DeadlineIncludesStreamDrainAndClosesRetainedPipes()
    {
        var process = new FakeBinaryProcessHandle();
        process.CompleteExit();
        var runner = new BinaryProcessRunner(
            new FakeBinaryProcessHandleFactory(process),
            TimeSpan.FromMilliseconds(20));
        var stopwatch = Stopwatch.StartNew();

        await Assert.ThrowsExceptionAsync<TimeoutException>(() => runner.RunAsync(
            FakeRequest(TimeSpan.FromMilliseconds(50)), CancellationToken.None));

        Assert.IsTrue(stopwatch.Elapsed < TimeSpan.FromSeconds(2));
        Assert.AreEqual(1, process.CloseStreamsCount);
        Assert.AreEqual(0, process.KillTreeCount);
    }

    [TestMethod]
    public async Task BinaryProcessRunner_KillFailureQuarantinesUntilExactProcessExits()
    {
        var process = new FakeBinaryProcessHandle { ThrowOnKill = true };
        var runner = new BinaryProcessRunner(
            new FakeBinaryProcessHandleFactory(process),
            TimeSpan.FromMilliseconds(20));
        var runTask = runner.RunAsync(FakeRequest(TimeSpan.FromMilliseconds(30)), CancellationToken.None);

        for (var attempt = 0; attempt < 100 && process.KillTreeCount == 0; attempt++)
            await Task.Delay(5);
        Assert.AreEqual(1, process.KillTreeCount);
        await Task.Delay(50);
        Assert.IsFalse(runTask.IsCompleted, "A failed kill must not detach from a still-running ADB process.");

        process.CompleteExit();
        await Assert.ThrowsExceptionAsync<TimeoutException>(() => runTask.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.AreEqual(1, process.CloseStreamsCount);
    }

    [TestMethod]
    public void AndroidPngDecoder_LoadsValidPngWithoutTextRoundTrip()
    {
        var bytes = ValidPngBytes();

        var bitmap = AndroidCoordinateCaptureWindow.DecodePng(bytes);

        Assert.AreEqual(1, bitmap.PixelWidth);
        Assert.AreEqual(1, bitmap.PixelHeight);
        Assert.IsTrue(bitmap.IsFrozen);
    }

    [TestMethod]
    public void AndroidPngDecoder_RejectsOversizedIhdrBeforeWpfDecode()
    {
        var bytes = ValidPngBytes();
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(16, 4), 100_000);

        var exception = Assert.ThrowsException<InvalidDataException>(() =>
            AndroidCoordinateCaptureWindow.DecodePng(bytes));

        StringAssert.Contains(exception.Message, "vượt giới hạn");
    }

    [STATestMethod]
    public void AndroidCaptureRefresh_DisablesAndClearsStaleScreenshotUntilReplacementCompletes()
    {
        if (Application.Current is null)
        {
            var application = new MEmuScriptStudio.App.App();
            application.InitializeComponent();
        }
        var service = new QueuedScreenshotCaptureService(ValidPngBytes());
        var device = new AndroidAdbDevice(
            "SERIAL", "Xiaomi", "Redmi 9C", "10", 29, 720, 1600, 320, 0,
            AndroidConnectionState.Device);
        var window = new AndroidCoordinateCaptureWindow(
            service, @"C:\Android\adb.exe", device, AndroidCoordinateCaptureMode.Tap);
        var previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(
            new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        try
        {
            window.RefreshScreenshotAsync().GetAwaiter().GetResult();
            var image = (Image)window.FindName("ScreenshotImage");
            var useButton = (Button)window.FindName("UseButton");
            Assert.IsNotNull(image.Source);
            Assert.IsTrue(image.IsEnabled);

            var refreshTask = window.RefreshScreenshotAsync();
            Assert.IsNull(image.Source);
            Assert.IsFalse(image.IsEnabled);
            Assert.IsFalse(useButton.IsEnabled);

            service.CompletePending();
            PumpDispatcherUntil(() => refreshTask.IsCompleted, TimeSpan.FromSeconds(2));
            refreshTask.GetAwaiter().GetResult();
            Assert.IsNotNull(image.Source);
            Assert.IsTrue(image.IsEnabled);
        }
        finally
        {
            window.Close();
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }
    }

    private static BinaryProcessRequest FakeRequest(TimeSpan timeout) =>
        new("adb.exe", ["-s", "SERIAL", "exec-out", "screencap", "-p"], timeout, "Test:screencap");

    private static byte[] ValidPngBytes() => Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");

    private static void PumpDispatcherUntil(Func<bool> condition, TimeSpan timeout)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(10)
        };
        var deadline = DateTime.UtcNow + timeout;
        timer.Tick += (_, _) =>
        {
            if (!condition() && DateTime.UtcNow < deadline) return;
            timer.Stop();
            frame.Continue = false;
        };
        timer.Start();
        Dispatcher.PushFrame(frame);
        if (!condition()) Assert.Fail("Timed out while pumping the WPF dispatcher.");
    }

    private static BinaryProcessResult Result(int exitCode, byte[] output, string error) =>
        new(exitCode, output, false, error, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    private sealed class RecordingBinaryProcessRunner(BinaryProcessResult result) : IBinaryProcessRunner
    {
        public BinaryProcessRequest? LastRequest { get; private set; }

        public Task<BinaryProcessResult> RunAsync(
            BinaryProcessRequest request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(result);
        }
    }

    private sealed class FakeBinaryProcessHandleFactory(FakeBinaryProcessHandle process)
        : IBinaryProcessHandleFactory
    {
        public IBinaryProcessHandle Start(BinaryProcessRequest request) => process;
    }

    private sealed class FakeBinaryProcessHandle : IBinaryProcessHandle
    {
        private readonly TaskCompletionSource exit = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<BoundedBinaryCapture> output =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<string> error =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool ThrowOnKill { get; init; }
        public int KillTreeCount { get; private set; }
        public int CloseStreamsCount { get; private set; }
        public bool HasExited => exit.Task.IsCompletedSuccessfully;
        public int ExitCode => 0;

        public Task<BoundedBinaryCapture> ReadStandardOutputToEndAsync(int maximumBytes) => output.Task;
        public Task<string> ReadStandardErrorToEndAsync(int maximumCharacters) => error.Task;
        public Task WaitForExitAsync(CancellationToken cancellationToken) => exit.Task.WaitAsync(cancellationToken);

        public void KillTree()
        {
            KillTreeCount++;
            if (ThrowOnKill) throw new InvalidOperationException("Simulated kill failure.");
            CompleteExit();
        }

        public void CloseStandardStreams()
        {
            CloseStreamsCount++;
            output.TrySetException(new ObjectDisposedException("stdout"));
            error.TrySetException(new ObjectDisposedException("stderr"));
        }

        public void CompleteExit() => exit.TrySetResult();
        public void Dispose() { }
    }

    private sealed class QueuedScreenshotCaptureService(byte[] png) : IAndroidScreenshotCaptureService
    {
        private readonly TaskCompletionSource<AndroidScreenshotData> pending =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int callCount;

        public Task<AndroidScreenshotData> CaptureAsync(
            string adbPath,
            string serial,
            CancellationToken cancellationToken) =>
            Interlocked.Increment(ref callCount) == 1
                ? Task.FromResult(new AndroidScreenshotData(png))
                : pending.Task.WaitAsync(cancellationToken);

        public void CompletePending() => pending.TrySetResult(new AndroidScreenshotData(png));
    }
}
