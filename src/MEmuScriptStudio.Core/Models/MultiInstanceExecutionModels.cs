namespace MEmuScriptStudio.Core.Models;

public enum InstanceExecutionStatus
{
    Queued,
    WaitingForLaunch,
    Running,
    Succeeded,
    Failed,
    Cancelled,
    Unavailable
}

public sealed class MultiInstanceExecutionRequest
{
    public required ScriptDefinition Script { get; init; }
    public required string MemucPath { get; init; }
    public required IReadOnlyList<MemuInstance> Targets { get; init; }
    public int? MaximumConcurrency { get; init; }
    public LaunchSpacingMode LaunchSpacingMode { get; init; }
    public TimeSpan FixedSpacing { get; init; }
    public TimeSpan RandomMinimumSpacing { get; init; }
    public TimeSpan RandomMaximumSpacing { get; init; }
    public bool StopAllOnInvalidTarget { get; init; }
    public IReadOnlyDictionary<string, string> Variables { get; init; } = new Dictionary<string, string>();
}

public sealed record InstanceExecutionUpdate(
    int InstanceIndex,
    string InstanceName,
    InstanceExecutionStatus Status,
    StepExecutionUpdate? StepUpdate = null,
    ExecutionResult? Result = null,
    string? Message = null);

public sealed class InstanceExecutionResult
{
    public required MemuInstance Target { get; init; }
    public InstanceExecutionStatus Status { get; init; }
    public ExecutionResult? Execution { get; init; }
    public string? Message { get; init; }
}

public sealed class MultiInstanceExecutionResult
{
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset EndedAt { get; init; }
    public bool WasCancelled { get; init; }
    public bool WasStoppedByInvalidTargetPolicy { get; init; }
    public IReadOnlyList<InstanceExecutionResult> Instances { get; init; } = [];
}
