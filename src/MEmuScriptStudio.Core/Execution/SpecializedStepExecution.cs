using MEmuScriptStudio.Core.Models;

namespace MEmuScriptStudio.Core.Execution;

public sealed record SpecializedStepExecutionResult(
    bool Succeeded,
    int? ExitCode,
    string StandardOutput,
    string StandardError,
    string CommandPreview,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt);

public interface ISpecializedStepExecutor
{
    string BuildPreview(ScriptStep step, string memucPath, int instanceIndex);

    Task<SpecializedStepExecutionResult> ExecuteAsync(
        ScriptStep step,
        string memucPath,
        int instanceIndex,
        CancellationToken cancellationToken);
}

public sealed record ChromeTabCleanupResult(bool Succeeded, string Message);

public interface IChromeTabService
{
    Task<ChromeTabCleanupResult> CloseAllTabsAsync(
        string memucPath,
        int instanceIndex,
        TimeSpan timeout,
        CancellationToken cancellationToken);
}

public sealed record ChromePageTarget(string Id, string Type);

public sealed class ChromeProtocolCapabilityException(string message, Exception? innerException = null)
    : Exception(message, innerException);

public interface IAdbForwardTransport
{
    Task<int> CreateChromeForwardAsync(
        string memucPath,
        int instanceIndex,
        TimeSpan timeout,
        CancellationToken cancellationToken);

    Task RemoveForwardAsync(
        string memucPath,
        int instanceIndex,
        int localPort,
        CancellationToken cancellationToken);
}

public interface IChromeDevToolsClient : IAsyncDisposable
{
    Task<IReadOnlyList<ChromePageTarget>> GetTargetsAsync(CancellationToken cancellationToken);
    Task CloseTargetAsync(string targetId, CancellationToken cancellationToken);
}

public interface IChromeDevToolsClientFactory
{
    Task<IChromeDevToolsClient> ConnectAsync(int localPort, CancellationToken cancellationToken);
}

public interface ILegacyChromeDevToolsClient : IAsyncDisposable
{
    Task<IReadOnlyList<ChromePageTarget>> GetTargetsAsync(CancellationToken cancellationToken);
    Task CloseTargetAsync(string targetId, CancellationToken cancellationToken);
}

public interface ILegacyChromeDevToolsClientFactory
{
    Task<ILegacyChromeDevToolsClient> ConnectAsync(int localPort, CancellationToken cancellationToken);
}
