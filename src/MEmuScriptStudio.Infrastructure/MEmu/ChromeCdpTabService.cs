using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.WebSockets;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using MEmuScriptStudio.Core.Execution;
using MEmuScriptStudio.Core.MEmu;
using MEmuScriptStudio.Core.Processes;

namespace MEmuScriptStudio.Infrastructure.MEmu;

public sealed class ChromeCdpTabService(
    IAdbForwardTransport forwardTransport,
    IChromeDevToolsClientFactory modernClientFactory,
    ILegacyChromeDevToolsClientFactory legacyClientFactory) : IChromeTabService
{
    private static readonly TimeSpan CleanupTimeout = TimeSpan.FromSeconds(5);

    public async Task<ChromeTabCleanupResult> CloseAllTabsAsync(
        string memucPath,
        int instanceIndex,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));
        using var timeoutSource = new CancellationTokenSource(timeout);
        using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);
        ChromeTabCleanupResult? result = null;
        Exception? operationException = null;
        var port = 0;

        try
        {
            port = await forwardTransport.CreateChromeForwardAsync(
                memucPath, instanceIndex, timeout, linkedSource.Token).ConfigureAwait(false);
            try
            {
                result = await ExecuteModernAsync(port, linkedSource.Token).ConfigureAwait(false);
            }
            catch (ChromeProtocolCapabilityException)
            {
                result = await ExecuteLegacyAsync(port, linkedSource.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException exception)
        {
            operationException = timeoutSource.IsCancellationRequested && !cancellationToken.IsCancellationRequested
                ? new TimeoutException($"Thao tác Chrome CDP vượt quá timeout {timeout}.", exception)
                : exception;
        }
        catch (Exception exception)
        {
            operationException = exception;
        }
        finally
        {
            if (port > 0)
            {
                using var cleanupSource = new CancellationTokenSource(CleanupTimeout);
                try
                {
                    await forwardTransport.RemoveForwardAsync(
                        memucPath, instanceIndex, port, cleanupSource.Token).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    if (operationException is null && result?.Succeeded == true)
                        result = new ChromeTabCleanupResult(false, $"Đã đóng tab nhưng không thể gỡ ADB forward tạm thời: {exception.Message}");
                }
            }
        }

        if (operationException is not null) ExceptionDispatchInfo.Capture(operationException).Throw();
        return result ?? new ChromeTabCleanupResult(false, "Không nhận được kết quả đóng tab Chrome.");
    }

    private async Task<ChromeTabCleanupResult> ExecuteModernAsync(int localPort, CancellationToken cancellationToken)
    {
        await using var client = await modernClientFactory.ConnectAsync(localPort, cancellationToken).ConfigureAwait(false);
        return await CloseAndVerifyAsync(
            "Modern CDP",
            client.GetTargetsAsync,
            client.CloseTargetAsync,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<ChromeTabCleanupResult> ExecuteLegacyAsync(int localPort, CancellationToken cancellationToken)
    {
        await using var client = await legacyClientFactory.ConnectAsync(localPort, cancellationToken).ConfigureAwait(false);
        return await CloseAndVerifyAsync(
            "Legacy /json",
            client.GetTargetsAsync,
            client.CloseTargetAsync,
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task<ChromeTabCleanupResult> CloseAndVerifyAsync(
        string strategy,
        Func<CancellationToken, Task<IReadOnlyList<ChromePageTarget>>> getTargets,
        Func<string, CancellationToken, Task> closeTarget,
        CancellationToken cancellationToken)
    {
        var pages = (await getTargets(cancellationToken).ConfigureAwait(false))
            .Where(target => string.Equals(target.Type, "page", StringComparison.Ordinal))
            .ToList();
        foreach (var page in pages)
            await closeTarget(page.Id, cancellationToken).ConfigureAwait(false);

        var remainingPageCount = (await getTargets(cancellationToken).ConfigureAwait(false))
            .Count(target => string.Equals(target.Type, "page", StringComparison.Ordinal));
        return remainingPageCount == 0
            ? new ChromeTabCleanupResult(true, $"{strategy} đã xác minh Chrome còn 0 page target.")
            : new ChromeTabCleanupResult(false,
                $"{strategy} không thể xác minh Chrome còn 0 page target; Chrome có thể đã tự tạo lại page. Không dùng fallback xóa dữ liệu, force-stop hoặc UI.");
    }
}

public sealed class MemucAdbForwardTransport(
    IProcessRunner processRunner,
    MemuCommandBuilder commandBuilder) : IAdbForwardTransport
{
    public const string AdbUnavailableMessage =
        "ADB của giả lập đang offline hoặc chưa được cấp quyền. Không thể điều khiển tab Chrome trên instance này.";

    public async Task<int> CreateChromeForwardAsync(
        string memucPath,
        int instanceIndex,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var preflightCommand = commandBuilder.BuildAdbCommand(memucPath, instanceIndex, "get-state");
        var preflightResult = await processRunner.RunAsync(
            new ProcessRequest(preflightCommand.ExecutablePath, preflightCommand.Arguments, TimeSpan.FromSeconds(5)),
            cancellationToken).ConfigureAwait(false);
        EnsureAdbReady(preflightResult);

        var command = commandBuilder.BuildAdbCommand(
            memucPath, instanceIndex, "forward tcp:0 localabstract:chrome_devtools_remote");
        var result = await processRunner.RunAsync(
            new ProcessRequest(command.ExecutablePath, command.Arguments, timeout), cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
            throw new InvalidOperationException("Không thể tạo ADB forward cho Chrome DevTools trên đúng instance.");
        return ParseAllocatedPort(result.StandardOutput);
    }

    internal static void EnsureAdbReady(ProcessResult result)
    {
        var combinedOutput = string.Concat(result.StandardOutput, "\n", result.StandardError);
        var outputLines = combinedOutput
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var isDevice = outputLines.Any(line => string.Equals(line, "device", StringComparison.OrdinalIgnoreCase));
        var hasUnavailableState = combinedOutput.Contains("offline", StringComparison.OrdinalIgnoreCase) ||
                                  combinedOutput.Contains("unauthorized", StringComparison.OrdinalIgnoreCase);
        if (result.ExitCode != 0 || !isDevice || hasUnavailableState)
            throw new InvalidOperationException(AdbUnavailableMessage);
    }

    public async Task RemoveForwardAsync(
        string memucPath,
        int instanceIndex,
        int localPort,
        CancellationToken cancellationToken)
    {
        var command = commandBuilder.BuildAdbCommand(
            memucPath, instanceIndex, $"forward --remove tcp:{localPort.ToString(CultureInfo.InvariantCulture)}");
        var result = await processRunner.RunAsync(
            new ProcessRequest(command.ExecutablePath, command.Arguments, TimeSpan.FromSeconds(5)), cancellationToken)
            .ConfigureAwait(false);
        if (result.ExitCode != 0)
            throw new InvalidOperationException("Không thể gỡ ADB forward tạm thời của Chrome DevTools.");
    }

    public static int ParseAllocatedPort(string output)
    {
        var token = output.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
        if (!int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out var port) || port is <= 0 or > 65535)
            throw new InvalidDataException("ADB không trả về local port hợp lệ cho tcp:0.");
        return port;
    }
}

public sealed class ChromeDevToolsClientFactory(HttpClient httpClient) : IChromeDevToolsClientFactory
{
    public async Task<IChromeDevToolsClient> ConnectAsync(int localPort, CancellationToken cancellationToken)
    {
        try
        {
            var json = await httpClient.GetStringAsync(
                new Uri($"http://127.0.0.1:{localPort.ToString(CultureInfo.InvariantCulture)}/json/version"),
                cancellationToken).ConfigureAwait(false);
            var endpoint = ChromeDevToolsJson.ParseBrowserWebSocketEndpoint(json, localPort);
            var socket = new ClientWebSocket();
            try
            {
                await socket.ConnectAsync(endpoint, cancellationToken).ConfigureAwait(false);
                return new ChromeDevToolsClient(socket);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (HttpRequestException exception) when (
            exception.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed)
        {
            throw new ChromeProtocolCapabilityException("Browser WebSocket endpoint không khả dụng.", exception);
        }
        catch (Exception exception) when (exception is WebSocketException or InvalidDataException or JsonException)
        {
            throw new ChromeProtocolCapabilityException("Browser WebSocket hoặc Modern CDP không tương thích.", exception);
        }
    }
}

public sealed class LegacyChromeDevToolsClientFactory(HttpClient httpClient) : ILegacyChromeDevToolsClientFactory
{
    public Task<ILegacyChromeDevToolsClient> ConnectAsync(int localPort, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<ILegacyChromeDevToolsClient>(new LegacyChromeDevToolsClient(httpClient, localPort));
    }
}

public static class ChromeDevToolsJson
{
    public static Uri ParseBrowserWebSocketEndpoint(string json, int expectedLocalPort)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("webSocketDebuggerUrl", out var property) ||
            property.ValueKind != JsonValueKind.String ||
            !Uri.TryCreate(property.GetString(), UriKind.Absolute, out var endpoint) ||
            endpoint.Scheme is not ("ws" or "wss"))
            throw new InvalidDataException("Chrome không trả về browser WebSocket endpoint hợp lệ.");
        var builder = new UriBuilder(endpoint) { Host = "127.0.0.1", Port = expectedLocalPort };
        return builder.Uri;
    }

    public static IReadOnlyList<ChromePageTarget> ParseModernTargets(JsonElement result)
    {
        if (!result.TryGetProperty("targetInfos", out var targets) || targets.ValueKind != JsonValueKind.Array)
            throw new ChromeProtocolCapabilityException("Phản hồi Target.getTargets không tương thích.");
        return targets.EnumerateArray().Select(target => ParseTarget(target, "targetId", "Modern target")).ToList();
    }

    public static IReadOnlyList<ChromePageTarget> ParseLegacyTargets(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
            throw new ChromeProtocolCapabilityException("Phản hồi /json/list không tương thích.");
        return document.RootElement.EnumerateArray().Select(target => ParseTarget(target, "id", "Legacy target")).ToList();
    }

    private static ChromePageTarget ParseTarget(JsonElement target, string idPropertyName, string description)
    {
        if (target.ValueKind != JsonValueKind.Object ||
            !target.TryGetProperty(idPropertyName, out var id) || id.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(id.GetString()) ||
            !target.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String)
            throw new ChromeProtocolCapabilityException($"{description} thiếu ID hoặc type hợp lệ.");
        return new ChromePageTarget(id.GetString()!, type.GetString() ?? string.Empty);
    }
}

internal sealed class ChromeDevToolsClient(ClientWebSocket socket) : IChromeDevToolsClient
{
    private int nextId;

    public async Task<IReadOnlyList<ChromePageTarget>> GetTargetsAsync(CancellationToken cancellationToken) =>
        ChromeDevToolsJson.ParseModernTargets(
            await SendAsync("Target.getTargets", null, cancellationToken).ConfigureAwait(false));

    public async Task CloseTargetAsync(string targetId, CancellationToken cancellationToken)
    {
        var result = await SendAsync("Target.closeTarget", new { targetId }, cancellationToken).ConfigureAwait(false);
        if (result.TryGetProperty("success", out var success) && success.ValueKind == JsonValueKind.False)
            throw new InvalidOperationException("Chrome từ chối đóng một page target.");
    }

    private async Task<JsonElement> SendAsync(string method, object? parameters, CancellationToken cancellationToken)
    {
        var id = Interlocked.Increment(ref nextId);
        var message = new Dictionary<string, object?> { ["id"] = id, ["method"] = method };
        if (parameters is not null) message["params"] = parameters;
        var payload = JsonSerializer.SerializeToUtf8Bytes(message);
        await socket.SendAsync(payload, WebSocketMessageType.Text, true, cancellationToken).ConfigureAwait(false);
        while (true)
        {
            var response = await ReceiveMessageAsync(cancellationToken).ConfigureAwait(false);
            JsonDocument document;
            try { document = JsonDocument.Parse(response); }
            catch (JsonException exception)
            {
                throw new ChromeProtocolCapabilityException("Phản hồi Modern CDP không phải JSON hợp lệ.", exception);
            }
            using (document)
            {
            var root = document.RootElement;
            if (!root.TryGetProperty("id", out var responseId)) continue;
            if (responseId.ValueKind != JsonValueKind.Number || !responseId.TryGetInt32(out var receivedId))
                throw new ChromeProtocolCapabilityException("Phản hồi Modern CDP có request ID không hợp lệ.");
            if (receivedId != id) continue;
            if (root.TryGetProperty("error", out var error))
            {
                var code = error.TryGetProperty("code", out var codeValue) && codeValue.TryGetInt32(out var errorCode)
                    ? errorCode
                    : 0;
                if (code == -32601)
                    throw new ChromeProtocolCapabilityException("Modern CDP không hỗ trợ Target domain cần thiết.");
                throw new InvalidOperationException($"Chrome CDP trả về lỗi mã {code}.");
            }
            if (!root.TryGetProperty("result", out var result))
                throw new ChromeProtocolCapabilityException("Phản hồi Chrome CDP thiếu result.");
            return result.Clone();
            }
        }
    }

    private async Task<byte[]> ReceiveMessageAsync(CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream();
        var buffer = new byte[8192];
        WebSocketReceiveResult result;
        do
        {
            result = await socket.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close)
                throw new WebSocketException("Chrome CDP đóng kết nối trước khi trả lời.");
            stream.Write(buffer, 0, result.Count);
        } while (!result.EndOfMessage);
        return stream.ToArray();
    }

    public async ValueTask DisposeAsync()
    {
        if (socket.State == WebSocketState.Open)
        {
            using var source = new CancellationTokenSource(TimeSpan.FromSeconds(1));
            try { await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty, source.Token).ConfigureAwait(false); }
            catch (Exception) { socket.Abort(); }
        }
        socket.Dispose();
    }
}

internal sealed class LegacyChromeDevToolsClient(HttpClient httpClient, int localPort) : ILegacyChromeDevToolsClient
{
    private readonly Uri baseAddress = new($"http://127.0.0.1:{localPort.ToString(CultureInfo.InvariantCulture)}/");

    public async Task<IReadOnlyList<ChromePageTarget>> GetTargetsAsync(CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(new Uri(baseAddress, "json/list"), cancellationToken).ConfigureAwait(false);
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed)
            throw new ChromeProtocolCapabilityException("Legacy /json/list không khả dụng.");
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        try { return ChromeDevToolsJson.ParseLegacyTargets(json); }
        catch (JsonException exception)
        {
            throw new ChromeProtocolCapabilityException("Phản hồi Legacy /json/list không hợp lệ.", exception);
        }
    }

    public async Task CloseTargetAsync(string targetId, CancellationToken cancellationToken)
    {
        var encodedTargetId = Uri.EscapeDataString(targetId);
        using var response = await httpClient.GetAsync(
            new Uri(baseAddress, $"json/close/{encodedTargetId}"), cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Legacy Chrome endpoint từ chối đóng target (HTTP {(int)response.StatusCode}).");
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
