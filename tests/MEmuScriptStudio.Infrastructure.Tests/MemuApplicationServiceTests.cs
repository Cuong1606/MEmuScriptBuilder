using MEmuScriptStudio.Core.MEmu;
using MEmuScriptStudio.Core.Processes;
using MEmuScriptStudio.Infrastructure.MEmu;

namespace MEmuScriptStudio.Infrastructure.Tests;

[TestClass]
public sealed class MemuApplicationServiceTests
{
    private const string MetadataQuery =
        "cmd package query-activities --user 0 -a android.intent.action.MAIN -c android.intent.category.LAUNCHER";

    [TestMethod]
    public async Task GetApplicationsAsync_EmptyObservedOutputFallsBackAndEnrichesExplicitLabel()
    {
        var runner = new QueueProcessRunner(
            Result(0, string.Empty),
            Result(0, "com.android.chrome/com.google.android.apps.chrome.Main\ncom.example/.Launcher"),
            Result(0, "packageName=com.android.chrome\nnonLocalizedLabel=Chrome\npackageName=com.example\nnonLocalizedLabel=null"));
        var service = CreateService(runner);

        var applications = await service.GetApplicationsAsync(@"C:\MEmu\memuc.exe", 4, CancellationToken.None);

        Assert.AreEqual(3, runner.Requests.Count);
        CollectionAssert.AreEqual(new[] { "-i", "4", "getappinfolist" }, runner.Requests[0].Arguments.ToArray());
        CollectionAssert.AreEqual(new[]
        {
            "-i", "4", "execcmd",
            "cmd package query-activities --brief --components --user 0 -a android.intent.action.MAIN -c android.intent.category.LAUNCHER"
        }, runner.Requests[1].Arguments.ToArray());
        Assert.AreEqual(MetadataQuery, runner.Requests[2].Arguments[^1]);
        Assert.AreEqual("Chrome", applications[0].DisplayName);
        Assert.AreEqual("Chưa xác định", applications[1].DisplayName);
        Assert.IsFalse(applications[1].HasResolvedApplicationLabel);
    }

    [TestMethod]
    public async Task GetApplicationsAsync_DirectComponentOutputSkipsComponentFallbackButQueriesMetadata()
    {
        var runner = new QueueProcessRunner(Result(0, "com.example/.Launcher"), Result(5, string.Empty, "unsupported"));
        var service = CreateService(runner);

        var applications = await service.GetApplicationsAsync(@"C:\MEmu\memuc.exe", 1, CancellationToken.None);

        Assert.AreEqual(2, runner.Requests.Count);
        Assert.AreEqual(MetadataQuery, runner.Requests[1].Arguments[^1]);
        Assert.AreEqual(".Launcher", applications.Single().ActivityName);
        Assert.AreEqual("Chưa xác định", applications.Single().DisplayName);
    }

    [TestMethod]
    public async Task GetApplicationsAsync_FallbackFailureReportsExitCode()
    {
        var runner = new QueueProcessRunner(Result(0, string.Empty), Result(9, string.Empty, "denied"));
        var service = CreateService(runner);

        var exception = await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
            service.GetApplicationsAsync(@"C:\MEmu\memuc.exe", 1, CancellationToken.None));

        StringAssert.Contains(exception.Message, "exit code 9");
    }

    [TestMethod]
    public async Task GetApplicationsAsync_MetadataTimeoutKeepsResolvedApplications()
    {
        var runner = new ThrowingMetadataRunner();
        var service = CreateService(runner);

        var applications = await service.GetApplicationsAsync(@"C:\MEmu\memuc.exe", 1, CancellationToken.None);

        Assert.AreEqual("Chưa xác định", applications.Single().DisplayName);
        Assert.AreEqual(2, runner.RequestCount);
    }

    [TestMethod]
    public async Task GetForegroundApplicationAsync_UsesReadOnlyFallbackAndExactInstance()
    {
        var runner = new QueueProcessRunner(
            Result(0, "activity output without component"),
            Result(0, "mCurrentFocus=Window{abc u0 com.example.app/.Main}"));
        var service = CreateService(runner);

        var application = await service.GetForegroundApplicationAsync(@"C:\MEmu\memuc.exe", 6, CancellationToken.None);

        Assert.AreEqual("com.example.app", application.PackageName);
        Assert.AreEqual(".Main", application.ActivityName);
        CollectionAssert.AreEqual(new[] { "-i", "6", "execcmd", "dumpsys activity activities" }, runner.Requests[0].Arguments.ToArray());
        CollectionAssert.AreEqual(new[] { "-i", "6", "execcmd", "dumpsys window windows" }, runner.Requests[1].Arguments.ToArray());
    }

    private static MemuApplicationService CreateService(IProcessRunner runner) =>
        new(runner, new MemuCommandBuilder(), new AndroidLauncherActivityParser(), new AndroidApplicationLabelParser(),
            new AndroidForegroundApplicationParser());

    private static ProcessResult Result(int exitCode, string stdout, string stderr = "") =>
        new(exitCode, stdout, stderr, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    private sealed class QueueProcessRunner(params ProcessResult[] results) : IProcessRunner
    {
        private readonly Queue<ProcessResult> results = new(results);
        public List<ProcessRequest> Requests { get; } = [];
        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(results.Dequeue());
        }
    }

    private sealed class ThrowingMetadataRunner : IProcessRunner
    {
        public int RequestCount { get; private set; }

        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken)
        {
            RequestCount++;
            if (RequestCount == 1) return Task.FromResult(Result(0, "com.example/.Launcher"));
            throw new TimeoutException("metadata timeout");
        }
    }
}
