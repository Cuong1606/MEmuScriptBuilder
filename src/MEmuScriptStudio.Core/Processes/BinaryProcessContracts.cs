namespace MEmuScriptStudio.Core.Processes;

public sealed record BinaryProcessRequest(
    string FileName,
    IReadOnlyList<string> Arguments,
    TimeSpan Timeout,
    string CommandCategory);

public sealed record BinaryProcessResult(
    int ExitCode,
    byte[] StandardOutput,
    bool StandardOutputTruncated,
    string StandardError,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt);

public interface IBinaryProcessRunner
{
    Task<BinaryProcessResult> RunAsync(BinaryProcessRequest request, CancellationToken cancellationToken);
}
