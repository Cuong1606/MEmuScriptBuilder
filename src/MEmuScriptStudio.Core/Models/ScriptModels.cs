using System.Text.Json.Serialization;

namespace MEmuScriptStudio.Core.Models;

public sealed class ScriptDefinition
{
    public int SchemaVersion { get; init; } = 1;
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; set; } = "Kịch bản mới";
    public ScriptKind Kind { get; init; } = ScriptKind.Regular;
    public int? DefaultInstanceIndex { get; set; }
    public List<ScriptVariable> Variables { get; init; } = [];
    public List<ScriptStep> Steps { get; init; } = [];
    public List<CompositeScriptItem> CompositeItems { get; init; } = [];
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

[JsonConverter(typeof(JsonStringEnumConverter<ScriptKind>))]
public enum ScriptKind
{
    Regular = 0,
    Composite = 1
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(ScriptReferenceItem), "scriptReference")]
[JsonDerivedType(typeof(CompositeDelayItem), "delay")]
public abstract class CompositeScriptItem
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public bool IsEnabled { get; set; } = true;
}

public sealed class ScriptReferenceItem : CompositeScriptItem
{
    public Guid ScriptId { get; set; }
    public bool ContinueOnFailure { get; set; }
}

public sealed class CompositeDelayItem : CompositeScriptItem
{
    public int DurationMilliseconds { get; set; } = 1000;
}

public sealed class ScriptVariable
{
    public required string Name { get; init; }
    public string? Value { get; set; }
    public bool IsSecret { get; init; }
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(AndroidShellStep), "androidShell")]
[JsonDerivedType(typeof(ForceStopStep), "forceStop")]
[JsonDerivedType(typeof(OpenAppStep), "openApp")]
[JsonDerivedType(typeof(DelayStep), "delay")]
[JsonDerivedType(typeof(TapStep), "tap")]
[JsonDerivedType(typeof(HoldStep), "hold")]
[JsonDerivedType(typeof(SwipeStep), "swipe")]
[JsonDerivedType(typeof(InputTextStep), "inputText")]
[JsonDerivedType(typeof(AndroidClipboardPasteStep), "androidClipboardPaste")]
[JsonDerivedType(typeof(KeyEventStep), "keyEvent")]
[JsonDerivedType(typeof(NoteStep), "note")]
[JsonDerivedType(typeof(CloseChromeTabsStep), "closeChromeTabs")]
public abstract class ScriptStep
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Name { get; set; }
    public bool IsEnabled { get; set; } = true;
    public bool ContinueOnError { get; set; }
    public int TimeoutSeconds { get; set; } = 30;
    [JsonIgnore]
    public abstract ScriptStepKind Kind { get; }
}

public sealed class AndroidShellStep : ScriptStep
{
    public required string Command { get; set; }
    public override ScriptStepKind Kind => ScriptStepKind.AndroidShell;
}

public sealed class ForceStopStep : ScriptStep
{
    public required string PackageName { get; set; }
    public string? ApplicationDisplayName { get; set; }
    public override ScriptStepKind Kind => ScriptStepKind.ForceStop;
}

public sealed class OpenAppStep : ScriptStep
{
    public required string PackageName { get; set; }
    public required string ActivityName { get; set; }
    public string? ApplicationDisplayName { get; set; }
    public override ScriptStepKind Kind => ScriptStepKind.OpenApp;
}

public sealed class DelayStep : ScriptStep
{
    public int DurationMilliseconds { get; set; }
    public override ScriptStepKind Kind => ScriptStepKind.Delay;
}

public sealed class TapStep : ScriptStep
{
    public int X { get; set; }
    public int Y { get; set; }
    public override ScriptStepKind Kind => ScriptStepKind.Tap;
}

public sealed class HoldStep : ScriptStep
{
    public int X { get; set; }
    public int Y { get; set; }
    public int DurationMilliseconds { get; set; } = 500;
    public override ScriptStepKind Kind => ScriptStepKind.Hold;
}

public sealed class SwipeStep : ScriptStep
{
    public int X1 { get; set; }
    public int Y1 { get; set; }
    public int X2 { get; set; }
    public int Y2 { get; set; }
    public int DurationMilliseconds { get; set; } = 300;
    public override ScriptStepKind Kind => ScriptStepKind.Swipe;
}

public sealed class InputTextStep : ScriptStep
{
    public required string Text { get; set; }
    public bool PressEnterAfterInput { get; set; }
    public override ScriptStepKind Kind => ScriptStepKind.InputText;
}

public sealed class AndroidClipboardPasteStep : ScriptStep
{
    public bool PressEnterAfterPaste { get; set; }
    public override ScriptStepKind Kind => ScriptStepKind.AndroidClipboardPaste;
}

public sealed class KeyEventStep : ScriptStep
{
    public AndroidKeyEvent Key { get; set; }
    public override ScriptStepKind Kind => ScriptStepKind.KeyEvent;
}

public sealed class NoteStep : ScriptStep
{
    public string Text { get; set; } = string.Empty;
    public override ScriptStepKind Kind => ScriptStepKind.Note;
}

public sealed class CloseChromeTabsStep : ScriptStep
{
    public override ScriptStepKind Kind => ScriptStepKind.CloseChromeTabs;
}

public enum ScriptStepKind
{
    AndroidShell,
    ForceStop,
    OpenApp,
    Delay,
    Tap,
    Swipe,
    InputText,
    KeyEvent,
    Note,
    Hold,
    AndroidClipboardPaste,
    CloseChromeTabs
}

public enum AndroidKeyEvent
{
    Back = 0,
    Home = 1,
    Menu = 2,
    VolumeUp = 3,
    VolumeDown = 4,
    RecentApps = 5
}
