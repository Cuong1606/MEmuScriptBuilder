using System.Net;
using System.Text;
using MEmuScriptStudio.Core.Execution;
using MEmuScriptStudio.Core.MEmu;
using MEmuScriptStudio.Core.Models;
using MEmuScriptStudio.Core.Processes;
using MEmuScriptStudio.Infrastructure.MEmu;
using MEmuScriptStudio.Infrastructure.Persistence;

namespace MEmuScriptStudio.Infrastructure.Tests;

[TestClass]
public sealed class RetiredStepMigrationTests
{
    [TestMethod]
    public async Task StoreMigratesBothRetiredDiscriminatorsToDisabledNotesAndSaveRemovesThem()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var recentId = Guid.NewGuid();
            var cacheId = Guid.NewGuid();
            var path = Path.Combine(directory, "scripts.json");
            var json = $$"""
                {"SchemaVersion":1,"Scripts":[{"Id":"{{Guid.NewGuid()}}","Name":"Legacy","Steps":[
                  {"$type":"clearRecentApps","Id":"{{recentId}}","Name":"Recent cũ","IsEnabled":true},
                  {"$type":"note","Id":"{{Guid.NewGuid()}}","Name":"Ở giữa","Text":"keep"},
                  {"$type":"clearAppCache","Id":"{{cacheId}}","Name":"Cache cũ","PackageName":"com.example.app","IsEnabled":true}
                ]}]}
                """;
            await File.WriteAllTextAsync(path, json);
            using var store = new JsonScriptStore(path);

            var scripts = await store.LoadAsync(CancellationToken.None);

            Assert.AreEqual(3, scripts.Single().Steps.Count);
            var recent = (NoteStep)scripts.Single().Steps[0];
            var cache = (NoteStep)scripts.Single().Steps[2];
            Assert.AreEqual(recentId, recent.Id);
            Assert.AreEqual("Recent cũ", recent.Name);
            Assert.IsFalse(recent.IsEnabled);
            Assert.AreEqual(cacheId, cache.Id);
            Assert.AreEqual("Cache cũ", cache.Name);
            Assert.IsFalse(cache.IsEnabled);
            await store.SaveAsync(scripts, CancellationToken.None);
            var saved = await File.ReadAllTextAsync(path);
            Assert.IsFalse(saved.Contains("clearRecentApps", StringComparison.Ordinal));
            Assert.IsFalse(saved.Contains("clearAppCache", StringComparison.Ordinal));
            Assert.AreEqual(3, CountOccurrences(saved, "\"$type\": \"note\""));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [TestMethod]
    public async Task ImportMigratesBothRetiredDiscriminatorsBeforeValidation()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var firstId = Guid.NewGuid();
            var secondId = Guid.NewGuid();
            var path = Path.Combine(directory, "legacy.memuscript");
            var json = $$"""
                {"SchemaVersion":1,"Format":"MEmuScriptStudio.ScriptTransfer","Scripts":[
                  {"Id":"{{Guid.NewGuid()}}","Name":"Imported","Steps":[
                    {"$type":"clearRecentApps","Id":"{{firstId}}","Name":"Recent import"},
                    {"$type":"clearAppCache","Id":"{{secondId}}","Name":"Cache import","PackageName":"com.example.app"}
                  ]}
                ]}
                """;
            await File.WriteAllTextAsync(path, json);

            var scripts = await new JsonScriptTransferService().ImportAsync(path, CancellationToken.None);

            CollectionAssert.AreEqual(new[] { firstId, secondId }, scripts.Single().Steps.Select(step => step.Id).ToArray());
            Assert.IsTrue(scripts.Single().Steps.All(step => step is NoteStep { IsEnabled: false }));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [TestMethod]
    public void ProductionDoesNotContainRetiredOrDangerousCommands()
    {
        var sourceRoot = Path.Combine(FindRepositoryRoot(), "src");
        var production = string.Join("\n", Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
            .Select(File.ReadAllText));
        foreach (var forbidden in new[]
                 {
                     "am clear-recent-apps", "pm clear --cache-only", "rm -rf"
                 })
            Assert.IsFalse(production.Contains(forbidden, StringComparison.OrdinalIgnoreCase), forbidden);

        var chromeSource = string.Join("\n",
            File.ReadAllText(Path.Combine(sourceRoot, "MEmuScriptStudio.Infrastructure", "MEmu", "ChromeCdpTabService.cs")),
            File.ReadAllText(Path.Combine(sourceRoot, "MEmuScriptStudio.Infrastructure", "MEmu", "ChromeSpecializedStepExecutor.cs")));
        foreach (var forbidden in new[]
                 {
                     "pm clear", "am force-stop", "input tap", "input swipe", "rm -rf", "about:blank",
                     "Target.createTarget", "Target.activateTarget"
                 })
            Assert.IsFalse(chromeSource.Contains(forbidden, StringComparison.OrdinalIgnoreCase), forbidden);
    }

    private static int CountOccurrences(string value, string token) =>
        value.Split(token, StringSplitOptions.None).Length - 1;

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"memu-retired-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "MEmuScriptStudio.sln")))
            current = current.Parent;
        return current?.FullName ?? throw new DirectoryNotFoundException();
    }
}

[TestClass]
public sealed class ChromeCdpTabServiceTests
{
    private const string MemucPath = "C:\\MEmu\\memuc.exe";

    [TestMethod]
    public async Task ModernClosesEveryPageLeavesNonPageAndVerifiesZeroWithoutLegacy()
    {
        var forward = new FakeForwardTransport();
        var modern = new FakeModernClient(
            [new("p1", "page"), new("worker", "service_worker"), new("p2", "page")],
            [new("worker", "service_worker")]);
        var legacyFactory = new FakeLegacyFactory(new FakeLegacyClient([], []));
        var result = await CreateService(forward, new FakeModernFactory(modern), legacyFactory)
            .CloseAllTabsAsync(MemucPath, 9, TimeSpan.FromSeconds(2), CancellationToken.None);

        Assert.IsTrue(result.Succeeded);
        CollectionAssert.AreEquivalent(new[] { "p1", "p2" }, modern.Closed.ToArray());
        Assert.IsFalse(modern.Closed.Contains("worker"));
        Assert.AreEqual(0, legacyFactory.ConnectCount);
        CollectionAssert.AreEqual(new[] { 41000 }, forward.RemovedPorts.ToArray());
    }

    [DataTestMethod]
    [DataRow("error: device offline")]
    [DataRow("error: device unauthorized")]
    [DataRow("device\r\nerror: device offline")]
    public async Task AdbPreflightFailureIsClearAndNeverCreatesForwardOrRepairsEnvironment(string adbError)
    {
        var runner = new RecordingProcessRunner(
            new ProcessResult(0, "already connected to 127.0.0.1:21503", adbError,
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
        var transport = new MemucAdbForwardTransport(runner, new MemuCommandBuilder());

        var exception = await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
            transport.CreateChromeForwardAsync(MemucPath, 7, TimeSpan.FromSeconds(2), CancellationToken.None));

        Assert.AreEqual(
            "ADB của giả lập đang offline hoặc chưa được cấp quyền. Không thể điều khiển tab Chrome trên instance này.",
            exception.Message);
        Assert.AreEqual(1, runner.Requests.Count);
        CollectionAssert.AreEqual(new[] { "-i", "7", "adb", "get-state" }, runner.Requests[0].Arguments.ToArray());
        var invokedArguments = string.Join(' ', runner.Requests.SelectMany(request => request.Arguments));
        Assert.IsFalse(invokedArguments.Contains("forward", StringComparison.OrdinalIgnoreCase));
        foreach (var forbidden in new[] { "adb_keys", "kill-server", "ctl.restart", "reboot", "pm clear", "force-stop" })
            Assert.IsFalse(invokedArguments.Contains(forbidden, StringComparison.OrdinalIgnoreCase), forbidden);
    }

    [TestMethod]
    public async Task AdbDevicePreflightThenCreatesDynamicForward()
    {
        var now = DateTimeOffset.UtcNow;
        var runner = new RecordingProcessRunner(
            new ProcessResult(0, "device\r\n", string.Empty, now, now),
            new ProcessResult(0, "41000\r\n", string.Empty, now, now));
        var transport = new MemucAdbForwardTransport(runner, new MemuCommandBuilder());

        var port = await transport.CreateChromeForwardAsync(
            MemucPath, 9, TimeSpan.FromSeconds(2), CancellationToken.None);

        Assert.AreEqual(41000, port);
        Assert.AreEqual(2, runner.Requests.Count);
        CollectionAssert.AreEqual(new[] { "-i", "9", "adb", "get-state" }, runner.Requests[0].Arguments.ToArray());
        CollectionAssert.AreEqual(
            new[] { "-i", "9", "adb", "forward tcp:0 localabstract:chrome_devtools_remote" },
            runner.Requests[1].Arguments.ToArray());
    }

    [TestMethod]
    public async Task CapabilityFailureFallsBackToLegacyWhichClosesEveryPageAndVerifiesZero()
    {
        var forward = new FakeForwardTransport();
        var legacy = new FakeLegacyClient(
            [new("old/id with space", "page"), new("worker", "service_worker")],
            [new("worker", "service_worker")]);
        var legacyFactory = new FakeLegacyFactory(legacy);
        var result = await CreateService(
                forward,
                new ThrowingModernFactory(new ChromeProtocolCapabilityException("Target domain unavailable")),
                legacyFactory)
            .CloseAllTabsAsync(MemucPath, 1, TimeSpan.FromSeconds(2), CancellationToken.None);

        Assert.IsTrue(result.Succeeded);
        CollectionAssert.AreEqual(new[] { "old/id with space" }, legacy.Closed.ToArray());
        Assert.AreEqual(1, legacyFactory.ConnectCount);
        Assert.AreEqual(1, forward.RemovedPorts.Count);
    }

    [TestMethod]
    public async Task BusinessFailureAndRecreatedPageDoNotUseLegacy()
    {
        var forward = new FakeForwardTransport();
        var legacyFactory = new FakeLegacyFactory(new FakeLegacyClient([], []));
        var businessFailure = new FakeModernClient([new("p", "page")], [])
        {
            CloseException = new InvalidOperationException("close rejected")
        };
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => CreateService(
                forward, new FakeModernFactory(businessFailure), legacyFactory)
            .CloseAllTabsAsync(MemucPath, 1, TimeSpan.FromSeconds(2), CancellationToken.None));
        Assert.AreEqual(0, legacyFactory.ConnectCount);

        var recreated = new FakeModernClient([new("p", "page")], [new("new", "page")]);
        var result = await CreateService(forward, new FakeModernFactory(recreated), legacyFactory)
            .CloseAllTabsAsync(MemucPath, 2, TimeSpan.FromSeconds(2), CancellationToken.None);
        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(0, legacyFactory.ConnectCount);
        Assert.AreEqual(2, forward.RemovedPorts.Count);
    }

    [TestMethod]
    public async Task TimeoutCancellationAndCleanupPreserveOriginalOutcome()
    {
        var timeoutForward = new FakeForwardTransport();
        await Assert.ThrowsExceptionAsync<TimeoutException>(() => CreateService(
                timeoutForward,
                new FakeModernFactory(new BlockingModernClient()),
                new FakeLegacyFactory(new FakeLegacyClient([], [])))
            .CloseAllTabsAsync(MemucPath, 1, TimeSpan.FromMilliseconds(25), CancellationToken.None));
        Assert.AreEqual(1, timeoutForward.RemovedPorts.Count);

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var cancelForward = new FakeForwardTransport { CleanupException = new InvalidOperationException("cleanup failed") };
        await Assert.ThrowsExceptionAsync<TaskCanceledException>(() => CreateService(
                cancelForward,
                new FakeModernFactory(new BlockingModernClient()),
                new FakeLegacyFactory(new FakeLegacyClient([], [])))
            .CloseAllTabsAsync(MemucPath, 2, TimeSpan.FromSeconds(1), cancellation.Token));
        Assert.AreEqual(1, cancelForward.RemoveAttempts);

        var cleanupForward = new FakeForwardTransport { CleanupException = new InvalidOperationException("cleanup failed") };
        var cleanupResult = await CreateService(
                cleanupForward,
                new FakeModernFactory(new FakeModernClient([], [])),
                new FakeLegacyFactory(new FakeLegacyClient([], [])))
            .CloseAllTabsAsync(MemucPath, 3, TimeSpan.FromSeconds(1), CancellationToken.None);
        Assert.IsFalse(cleanupResult.Succeeded);
    }

    [TestMethod]
    public async Task ConcurrentExecutionsUseDistinctForwardState()
    {
        var forward = new FakeForwardTransport();
        var factory = new QueueModernFactory(new FakeModernClient([], []), new FakeModernClient([], []));
        var service = CreateService(forward, factory, new FakeLegacyFactory(new FakeLegacyClient([], [])));
        await Task.WhenAll(
            service.CloseAllTabsAsync(MemucPath, 3, TimeSpan.FromSeconds(1), CancellationToken.None),
            service.CloseAllTabsAsync(MemucPath, 4, TimeSpan.FromSeconds(1), CancellationToken.None));
        Assert.AreEqual(2, forward.CreatedPorts.Distinct().Count());
        CollectionAssert.AreEquivalent(forward.CreatedPorts.ToArray(), forward.RemovedPorts.ToArray());
    }

    [TestMethod]
    public async Task LegacyHttpUsesListAndUrlEncodedCloseTargetWithoutReadingUrls()
    {
        var requests = new List<Uri>();
        var responses = new Queue<HttpResponseMessage>(
        [
            JsonResponse("[{\"id\":\"a/b c\",\"type\":\"page\",\"url\":\"https://secret.invalid/token\"}]"),
            new HttpResponseMessage(HttpStatusCode.OK),
            JsonResponse("[]")
        ]);
        using var httpClient = new HttpClient(new RecordingHandler(requests, responses));
        var client = await new LegacyChromeDevToolsClientFactory(httpClient)
            .ConnectAsync(45678, CancellationToken.None);

        var targets = await client.GetTargetsAsync(CancellationToken.None);
        await client.CloseTargetAsync(targets.Single().Id, CancellationToken.None);
        var remaining = await client.GetTargetsAsync(CancellationToken.None);

        Assert.AreEqual(0, remaining.Count);
        Assert.AreEqual("/json/close/a%2Fb%20c", requests[1].AbsolutePath);
        Assert.IsFalse(string.Join("\n", requests).Contains("secret.invalid", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task ModernVersionEndpointFallsBackOnlyForCapabilityHttpStatus()
    {
        var capabilityResponses = new Queue<HttpResponseMessage>(
            [new HttpResponseMessage(HttpStatusCode.NotFound)]);
        using var capabilityHttp = new HttpClient(new RecordingHandler([], capabilityResponses));
        await Assert.ThrowsExceptionAsync<ChromeProtocolCapabilityException>(() =>
            new ChromeDevToolsClientFactory(capabilityHttp).ConnectAsync(45678, CancellationToken.None));

        var serverFailureResponses = new Queue<HttpResponseMessage>(
            [new HttpResponseMessage(HttpStatusCode.InternalServerError)]);
        using var serverFailureHttp = new HttpClient(new RecordingHandler([], serverFailureResponses));
        var exception = await Assert.ThrowsExceptionAsync<HttpRequestException>(() =>
            new ChromeDevToolsClientFactory(serverFailureHttp).ConnectAsync(45678, CancellationToken.None));
        Assert.AreEqual(HttpStatusCode.InternalServerError, exception.StatusCode);
    }

    [TestMethod]
    public void ParsersRejectMalformedProtocolAndRouteMemucAdbToExactInstance()
    {
        Assert.AreEqual(43721, MemucAdbForwardTransport.ParseAllocatedPort("43721\r\n"));
        Assert.ThrowsException<InvalidDataException>(() => MemucAdbForwardTransport.ParseAllocatedPort("bad"));
        var endpoint = ChromeDevToolsJson.ParseBrowserWebSocketEndpoint(
            "{\"webSocketDebuggerUrl\":\"ws://localhost:9222/devtools/browser/x\"}", 43721);
        Assert.AreEqual("127.0.0.1", endpoint.Host);
        Assert.AreEqual(43721, endpoint.Port);
        Assert.ThrowsException<InvalidDataException>(() => ChromeDevToolsJson.ParseBrowserWebSocketEndpoint("{}", 1));
        Assert.ThrowsException<ChromeProtocolCapabilityException>(() => ChromeDevToolsJson.ParseLegacyTargets("{}"));

        var adb = new MemuCommandBuilder().BuildAdbCommand(MemucPath, 5, "forward tcp:0 localabstract:chrome_devtools_remote");
        CollectionAssert.AreEqual(
            new[] { "-i", "5", "adb", "forward tcp:0 localabstract:chrome_devtools_remote" },
            adb.Arguments.ToArray());
    }

    private static ChromeCdpTabService CreateService(
        IAdbForwardTransport forward,
        IChromeDevToolsClientFactory modern,
        ILegacyChromeDevToolsClientFactory legacy) => new(forward, modern, legacy);

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private sealed class FakeForwardTransport : IAdbForwardTransport
    {
        private int nextPort = 40999;
        public List<int> CreatedPorts { get; } = [];
        public List<int> RemovedPorts { get; } = [];
        public int RemoveAttempts { get; private set; }
        public Exception? CleanupException { get; init; }
        public Task<int> CreateChromeForwardAsync(string memucPath, int instanceIndex, TimeSpan timeout, CancellationToken cancellationToken)
        {
            var port = Interlocked.Increment(ref nextPort);
            lock (CreatedPorts) CreatedPorts.Add(port);
            return Task.FromResult(port);
        }
        public Task RemoveForwardAsync(string memucPath, int instanceIndex, int localPort, CancellationToken cancellationToken)
        {
            RemoveAttempts++;
            if (CleanupException is not null) return Task.FromException(CleanupException);
            lock (RemovedPorts) RemovedPorts.Add(localPort);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeModernClient(
        IReadOnlyList<ChromePageTarget> before,
        IReadOnlyList<ChromePageTarget> after) : IChromeDevToolsClient
    {
        private int getCalls;
        public Exception? CloseException { get; init; }
        public List<string> Closed { get; } = [];
        public Task<IReadOnlyList<ChromePageTarget>> GetTargetsAsync(CancellationToken cancellationToken) =>
            Task.FromResult(getCalls++ == 0 ? before : after);
        public Task CloseTargetAsync(string targetId, CancellationToken cancellationToken)
        {
            if (CloseException is not null) return Task.FromException(CloseException);
            Closed.Add(targetId);
            return Task.CompletedTask;
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class BlockingModernClient : IChromeDevToolsClient
    {
        public async Task<IReadOnlyList<ChromePageTarget>> GetTargetsAsync(CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return [];
        }
        public Task CloseTargetAsync(string targetId, CancellationToken cancellationToken) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeLegacyClient(
        IReadOnlyList<ChromePageTarget> before,
        IReadOnlyList<ChromePageTarget> after) : ILegacyChromeDevToolsClient
    {
        private int getCalls;
        public List<string> Closed { get; } = [];
        public Task<IReadOnlyList<ChromePageTarget>> GetTargetsAsync(CancellationToken cancellationToken) =>
            Task.FromResult(getCalls++ == 0 ? before : after);
        public Task CloseTargetAsync(string targetId, CancellationToken cancellationToken)
        {
            Closed.Add(targetId);
            return Task.CompletedTask;
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeModernFactory(IChromeDevToolsClient client) : IChromeDevToolsClientFactory
    {
        public Task<IChromeDevToolsClient> ConnectAsync(int localPort, CancellationToken cancellationToken) => Task.FromResult(client);
    }

    private sealed class ThrowingModernFactory(Exception exception) : IChromeDevToolsClientFactory
    {
        public Task<IChromeDevToolsClient> ConnectAsync(int localPort, CancellationToken cancellationToken) =>
            Task.FromException<IChromeDevToolsClient>(exception);
    }

    private sealed class QueueModernFactory(params IChromeDevToolsClient[] clients) : IChromeDevToolsClientFactory
    {
        private readonly Queue<IChromeDevToolsClient> clients = new(clients);
        public Task<IChromeDevToolsClient> ConnectAsync(int localPort, CancellationToken cancellationToken)
        {
            lock (clients) return Task.FromResult(clients.Dequeue());
        }
    }

    private sealed class FakeLegacyFactory(ILegacyChromeDevToolsClient client) : ILegacyChromeDevToolsClientFactory
    {
        public int ConnectCount { get; private set; }
        public Task<ILegacyChromeDevToolsClient> ConnectAsync(int localPort, CancellationToken cancellationToken)
        {
            ConnectCount++;
            return Task.FromResult(client);
        }
    }

    private sealed class RecordingHandler(
        ICollection<Uri> requests,
        Queue<HttpResponseMessage> responses) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            requests.Add(request.RequestUri!);
            return Task.FromResult(responses.Dequeue());
        }
    }

    private sealed class RecordingProcessRunner(params ProcessResult[] results) : IProcessRunner
    {
        private readonly Queue<ProcessResult> results = new(results);
        public List<ProcessRequest> Requests { get; } = [];

        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(results.Dequeue());
        }
    }
}
