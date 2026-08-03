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
    public const int CurrentSchemaVersion = 2;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public string? MemucPath { get; set; }
    public Dictionary<string, string> ApplicationDisplayNames { get; init; } = [];
    public MultiInstanceRunSettings MultiInstanceRun { get; init; } = new();
}

public enum RunTargetScope
{
    Selected,
    All
}

public enum MaximumConcurrencyMode
{
    All,
    Limited
}

public enum LaunchSpacingMode
{
    Fixed,
    Random
}

public sealed class MultiInstanceRunSettings
{
    public RunTargetScope TargetScope { get; set; } = RunTargetScope.Selected;
    public MaximumConcurrencyMode MaximumConcurrencyMode { get; set; } = MaximumConcurrencyMode.All;
    public int MaximumConcurrency { get; set; } = 1;
    public LaunchSpacingMode LaunchSpacingMode { get; set; } = LaunchSpacingMode.Fixed;
    public int FixedSpacingMilliseconds { get; set; }
    public int RandomMinimumSpacingMilliseconds { get; set; }
    public int RandomMaximumSpacingMilliseconds { get; set; }
    public bool StopAllOnInvalidTarget { get; set; }
}
