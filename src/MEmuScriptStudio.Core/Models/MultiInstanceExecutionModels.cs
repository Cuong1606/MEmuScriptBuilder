namespace MEmuScriptStudio.Core.Models;

using MEmuScriptStudio.Core.Scripts;

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
    public Guid LaunchGroupId { get; init; } = Guid.NewGuid();
    public required ScriptDefinition Script { get; init; }
    public IReadOnlyDictionary<int, ScriptDefinition> ScriptsByInstance { get; init; } =
        new Dictionary<int, ScriptDefinition>();
    public IReadOnlyDictionary<string, ScriptDefinition> ScriptsByTarget { get; init; } =
        new Dictionary<string, ScriptDefinition>(StringComparer.Ordinal);
    public ExecutionScriptLibrarySnapshot? ScriptLibrarySnapshot { get; init; }
    public string MemucPath { get; init; } = string.Empty;
    public string AdbPath { get; init; } = string.Empty;
    public required IReadOnlyList<IExecutionTarget> Targets { get; init; }
    public LaunchSpacingMode LaunchSpacingMode { get; init; }
    public TimeSpan FixedSpacing { get; init; }
    public TimeSpan RandomMinimumSpacing { get; init; }
    public TimeSpan RandomMaximumSpacing { get; init; }
    public bool StopAllOnInvalidTarget { get; init; }
    public IReadOnlyDictionary<string, string> Variables { get; init; } = new Dictionary<string, string>();
}

public sealed record InstanceExecutionUpdate(
    Guid LaunchGroupId,
    int InstanceIndex,
    string InstanceName,
    InstanceExecutionStatus Status,
    StepExecutionUpdate? StepUpdate = null,
    ExecutionResult? Result = null,
    string? Message = null,
    Guid? ScriptId = null,
    string? ScriptName = null)
{
    public string TargetKey { get; init; } = InstanceIndex >= 0
        ? ExecutionTargetKeys.ForMemu(InstanceIndex)
        : string.Empty;
    public DeviceKind DeviceKind { get; init; } = DeviceKind.MEmu;
    public string TargetIdentifier { get; init; } = InstanceIndex >= 0
        ? InstanceIndex.ToString(System.Globalization.CultureInfo.InvariantCulture)
        : string.Empty;
}

public sealed class InstanceExecutionResult
{
    public Guid LaunchGroupId { get; init; }
    public required IExecutionTarget Target { get; init; }
    public Guid? ScriptId { get; init; }
    public string? ScriptName { get; init; }
    public InstanceExecutionStatus Status { get; init; }
    public ExecutionResult? Execution { get; init; }
    public string? Message { get; init; }
}

public sealed class MultiInstanceExecutionResult
{
    public Guid LaunchGroupId { get; init; }
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset EndedAt { get; init; }
    public bool WasCancelled { get; init; }
    public bool WasStoppedByInvalidTargetPolicy { get; init; }
    public IReadOnlyList<InstanceExecutionResult> Instances { get; init; } = [];
}
