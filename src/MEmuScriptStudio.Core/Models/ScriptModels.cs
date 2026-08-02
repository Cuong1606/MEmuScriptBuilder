using System.Text.Json.Serialization;

namespace MEmuScriptStudio.Core.Models;

public sealed class ScriptDefinition
{
    public int SchemaVersion { get; init; } = 1;
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; set; } = "Kịch bản mới";
    public int? DefaultInstanceIndex { get; set; }
    public List<ScriptVariable> Variables { get; init; } = [];
    public List<ScriptStep> Steps { get; init; } = [];
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class ScriptVariable
{
    public required string Name { get; init; }
    public string? Value { get; set; }
    public bool IsSecret { get; init; }
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(AndroidShellStep), "androidShell")]
[JsonDerivedType(typeof(DelayStep), "delay")]
[JsonDerivedType(typeof(NoteStep), "note")]
public abstract class ScriptStep
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Name { get; set; }
    public bool IsEnabled { get; set; } = true;
    public bool ContinueOnError { get; set; }
}

public sealed class AndroidShellStep : ScriptStep
{
    public required string Command { get; set; }
}

public sealed class DelayStep : ScriptStep
{
    public int DurationMilliseconds { get; set; }
}

public sealed class NoteStep : ScriptStep
{
    public string Text { get; set; } = string.Empty;
}
