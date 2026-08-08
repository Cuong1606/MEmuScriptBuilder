namespace MEmuScriptStudio.Core.Processes;

public enum ProcessCancellationPolicy
{
    DirectProcessOnly,
    EntireProcessTree,
    WaitForNaturalExit
}

public enum ProcessTimeoutPolicy
{
    DirectProcessOnly,
    EntireProcessTree
}

public sealed record ProcessDiagnosticContext(
    int? InstanceIndex,
    string CommandCategory);

public enum ProcessLifecycleEventKind
{
    Started,
    NaturalExit,
    UserCancellationRequested,
    UserCancellationNaturalExit,
    UserCancellationDirectKill,
    UserCancellationTreeKill,
    TimeoutDetected,
    TimeoutNaturalExit,
    TimeoutDirectKill,
    TimeoutTreeKill,
    TimeoutTerminationFailed,
    TimeoutQuarantined,
    TimeoutQuarantineExited,
    CleanupCompleted
}

public sealed record ProcessLifecycleDiagnostic(
    ProcessLifecycleEventKind EventKind,
    DateTimeOffset Timestamp,
    TimeSpan Duration,
    int ProcessId,
    int? InstanceIndex,
    string CommandCategory,
    int? ExitCode = null,
    string? Marker = null);

public interface IProcessLifecycleLogger
{
    void Write(ProcessLifecycleDiagnostic diagnostic);
}

public sealed record ProcessRequest(
    string FileName,
    IReadOnlyList<string> Arguments,
    TimeSpan Timeout,
    ProcessCancellationPolicy CancellationPolicy = ProcessCancellationPolicy.DirectProcessOnly,
    ProcessTimeoutPolicy TimeoutPolicy = ProcessTimeoutPolicy.DirectProcessOnly,
    ProcessDiagnosticContext? DiagnosticContext = null);

public sealed record ProcessResult(
    int ExitCode,
    string StandardOutput,
    string StandardError,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt);

public interface IProcessRunner
{
    Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken);
}
