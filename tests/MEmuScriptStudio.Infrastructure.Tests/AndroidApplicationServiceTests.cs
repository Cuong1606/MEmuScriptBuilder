using MEmuScriptStudio.Core.Android;
using MEmuScriptStudio.Core.MEmu;
using MEmuScriptStudio.Core.Processes;
using MEmuScriptStudio.Infrastructure.Android;

namespace MEmuScriptStudio.Infrastructure.Tests;

[TestClass]
public sealed class AndroidApplicationServiceTests
{
    private const string AdbPath = @"C:\Tools\adb.exe";

    [TestMethod]
    public async Task Discovery_UsesOnlyExactSerialAndParsesLauncherComponents()
    {
        var runner = new ScriptedRunner(request => request.Arguments.Contains("--brief")
            ? Success("""
                2 activities found:
                com.android.chrome/com.google.android.apps.chrome.Main
                com.example.app/.LauncherActivity
                """)
            : Success("""
                ActivityInfo:
                  packageName=com.android.chrome
                  nonLocalizedLabel=Chrome
                ActivityInfo:
                  packageName=com.example.app
                  nonLocalizedLabel=null
                """));
        var service = CreateService(runner);

        var applications = await service.GetApplicationsAsync(AdbPath, "SERIAL-B", CancellationToken.None);

        Assert.AreEqual(2, applications.Count);
        Assert.AreEqual("com.example.app", applications.Single(application => application.ActivityName == ".LauncherActivity").PackageName);
        Assert.AreEqual(2, runner.Requests.Count);
        Assert.IsTrue(runner.Requests.All(request => request.FileName == AdbPath));
        Assert.IsTrue(runner.Requests.All(request => request.Arguments[1] == "SERIAL-B"));
        var request = runner.Requests[0];
        CollectionAssert.AreEqual(
            new[]
            {
                "-s", "SERIAL-B", "shell", "cmd", "package", "query-activities", "--brief", "--components",
                "--user", "0", "-a", "android.intent.action.MAIN", "-c", "android.intent.category.LAUNCHER"
            },
            request.Arguments.ToArray());
        Assert.AreEqual("Chrome", applications.Single(application => application.PackageName == "com.android.chrome").DisplayName);
        Assert.AreEqual("Không xác định", applications.Single(application => application.PackageName == "com.example.app").DisplayName);
    }

    [TestMethod]
    public async Task Discovery_CatalogsRemainIsolatedAcrossDeviceSerials()
    {
        var runner = new ScriptedRunner(request => request.Arguments[1] switch
        {
            "SERIAL-A" => Success("com.alpha.app/.Main"),
            "SERIAL-B" => Success("com.beta.app/.Home"),
            _ => throw new AssertFailedException("Unexpected serial")
        });
        var service = CreateService(runner);

        var first = await service.GetApplicationsAsync(AdbPath, "SERIAL-A", CancellationToken.None);
        var second = await service.GetApplicationsAsync(AdbPath, "SERIAL-B", CancellationToken.None);

        CollectionAssert.AreEqual(new[] { "com.alpha.app" }, first.Select(application => application.PackageName).ToArray());
        CollectionAssert.AreEqual(new[] { "com.beta.app" }, second.Select(application => application.PackageName).ToArray());
        CollectionAssert.AreEqual(new[] { "SERIAL-A", "SERIAL-A", "SERIAL-B", "SERIAL-B" },
            runner.Requests.Select(request => request.Arguments[1]).ToArray());
    }

    [TestMethod]
    public async Task Discovery_LabelMetadataFailureKeepsCatalogWithoutPackageNameFallback()
    {
        var runner = new ScriptedRunner(request => request.Arguments.Contains("--brief")
            ? Success("com.example.app/.Main")
            : Result(1, string.Empty, "metadata unavailable"));

        var application = (await CreateService(runner)
            .GetApplicationsAsync(AdbPath, "SERIAL-A", CancellationToken.None)).Single();

        Assert.IsFalse(application.HasResolvedApplicationLabel);
        Assert.AreEqual("Không xác định", application.DisplayName);
        Assert.AreEqual("com.example.app", application.PackageName);
    }

    [TestMethod]
    public async Task Discovery_FailureIsVisibleAndDoesNotReturnAStaleCatalog()
    {
        var runner = new ScriptedRunner(_ => Result(1, string.Empty, "error: device offline"));
        var service = CreateService(runner);

        var exception = await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
            service.GetApplicationsAsync(AdbPath, "SERIAL-A", CancellationToken.None));

        StringAssert.Contains(exception.Message, "device offline");
    }

    [TestMethod]
    public async Task Foreground_UsesActivityManagerFirstAndExactSerial()
    {
        var runner = new ScriptedRunner(_ => Success(
            "mResumedActivity: ActivityRecord{2255eec u0 com.miui.home/.launcher.Launcher t1}"));

        var application = await CreateService(runner).GetForegroundApplicationAsync(
            AdbPath, "SERIAL-A", CancellationToken.None);

        Assert.AreEqual("com.miui.home", application.PackageName);
        Assert.AreEqual(".launcher.Launcher", application.ActivityName);
        Assert.AreEqual(1, runner.Requests.Count);
        CollectionAssert.AreEqual(
            new[] { "-s", "SERIAL-A", "shell", "dumpsys", "activity", "activities" },
            runner.Requests.Single().Arguments.ToArray());
    }

    [TestMethod]
    public async Task Foreground_FallsBackToWindowManagerWhenActivityManagerHasNoVerifiedResult()
    {
        var runner = new ScriptedRunner(request => request.Arguments.Contains("activity")
            ? Success("mLastPausedActivity: ActivityRecord{old u0 com.example.old/.Old t1}")
            : Success("mCurrentFocus=Window{now u0 com.example.current/.Current}"));

        var application = await CreateService(runner).GetForegroundApplicationAsync(
            AdbPath, "SERIAL-B", CancellationToken.None);

        Assert.AreEqual("com.example.current", application.PackageName);
        Assert.AreEqual(".Current", application.ActivityName);
        Assert.AreEqual(2, runner.Requests.Count);
        Assert.IsTrue(runner.Requests.All(request => request.Arguments[1] == "SERIAL-B"));
        CollectionAssert.AreEqual(
            new[] { "-s", "SERIAL-B", "shell", "dumpsys", "window" },
            runner.Requests[1].Arguments.ToArray());
    }

    [TestMethod]
    public async Task Foreground_NoVerifiedMarkerDoesNotReturnBackgroundOrStaleResult()
    {
        var runner = new ScriptedRunner(_ => Success(
            "ActivityRecord{old u0 com.example.background/.Background t1}"));

        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
            CreateService(runner).GetForegroundApplicationAsync(
                AdbPath, "SERIAL-A", CancellationToken.None));

        Assert.AreEqual(2, runner.Requests.Count);
    }

    [TestMethod]
    public async Task Foreground_QueriesRemainIsolatedAcrossSerials()
    {
        var runner = new ScriptedRunner(request => Success(
            request.Arguments[1] == "SERIAL-A"
                ? "mResumedActivity: ActivityRecord{x u0 com.alpha.app/.Main t1}"
                : "mResumedActivity: ActivityRecord{y u0 com.beta.app/.Home t2}"));
        var service = CreateService(runner);

        var first = await service.GetForegroundApplicationAsync(AdbPath, "SERIAL-A", CancellationToken.None);
        var second = await service.GetForegroundApplicationAsync(AdbPath, "SERIAL-B", CancellationToken.None);

        Assert.AreEqual("com.alpha.app", first.PackageName);
        Assert.AreEqual("com.beta.app", second.PackageName);
        CollectionAssert.AreEqual(new[] { "SERIAL-A", "SERIAL-B" },
            runner.Requests.Select(request => request.Arguments[1]).ToArray());
    }

    [TestMethod]
    public async Task Foreground_DisconnectedDeviceFailsUnavailableWithoutCachedResult()
    {
        var runner = new ScriptedRunner(request => request.Arguments[1] == "SERIAL-A"
            ? Success("mResumedActivity: ActivityRecord{x u0 com.alpha.app/.Main t1}")
            : Result(1, string.Empty, "error: device offline"));
        var service = CreateService(runner);
        _ = await service.GetForegroundApplicationAsync(AdbPath, "SERIAL-A", CancellationToken.None);

        var exception = await Assert.ThrowsExceptionAsync<AndroidAdbDeviceUnavailableException>(() =>
            service.GetForegroundApplicationAsync(AdbPath, "SERIAL-B", CancellationToken.None));

        StringAssert.Contains(exception.Message, "SERIAL-B");
        StringAssert.Contains(exception.Message, "device offline");
        Assert.IsTrue(runner.Requests.Skip(1).All(request => request.Arguments[1] == "SERIAL-B"));
    }

    private static AndroidApplicationService CreateService(IProcessRunner runner) =>
        new(runner, new AdbCommandBuilder(), new AndroidLauncherApplicationParser(),
            new AndroidApplicationLabelParser(), new AndroidForegroundActivityParser());

    private static ProcessResult Success(string output) => Result(0, output, string.Empty);

    private static ProcessResult Result(int exitCode, string output, string error)
    {
        var now = DateTimeOffset.UtcNow;
        return new ProcessResult(exitCode, output, error, now, now);
    }

    private sealed class ScriptedRunner(Func<ProcessRequest, ProcessResult> run) : IProcessRunner
    {
        public List<ProcessRequest> Requests { get; } = [];

        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(run(request));
        }
    }
}
