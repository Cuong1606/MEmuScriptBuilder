namespace MEmuScriptStudio.Core.Models;

public enum StepExecutionStatus
{
    NotRun,
    Running,
    Succeeded,
    Failed,
    Skipped,
    Cancelled
}

public sealed class ExecutionRequest
{
    public required ScriptDefinition Script { get; init; }
    public required string MemucPath { get; init; }
    public required int InstanceIndex { get; init; }
    public IReadOnlyDictionary<string, string> Variables { get; init; } = new Dictionary<string, string>();
}

public sealed class ExecutionResult
{
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset EndedAt { get; init; }
    public bool WasCancelled { get; init; }
    public IReadOnlyList<StepExecutionResult> Steps { get; init; } = [];
}

public sealed class StepExecutionResult
{
    public required Guid StepId { get; init; }
    public StepExecutionStatus Status { get; init; }
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset EndedAt { get; init; }
    public int? ExitCode { get; init; }
    public string StandardOutput { get; init; } = string.Empty;
    public string StandardError { get; init; } = string.Empty;
    public string CommandPreview { get; init; } = string.Empty;
}

public sealed record StepExecutionUpdate(Guid StepId, StepExecutionStatus Status, StepExecutionResult? Result = null);

public sealed class ApplicationSettings
{
    public int SchemaVersion { get; init; } = 1;
    public string? MemucPath { get; set; }
}
