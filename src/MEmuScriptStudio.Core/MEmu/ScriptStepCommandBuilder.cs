using System.Globalization;
using MEmuScriptStudio.Core.Models;

namespace MEmuScriptStudio.Core.MEmu;

public sealed class ScriptStepCommandBuilder(MemuCommandBuilder commandBuilder)
{
    public MemuCommand BuildProcessCommand(ScriptStep step, string memucPath, int instanceIndex)
    {
        var commands = BuildProcessCommands(step, memucPath, instanceIndex);
        if (commands.Count != 1)
            throw new InvalidOperationException("Bước này tạo nhiều process; hãy dùng BuildProcessCommands.");
        return commands[0];
    }

    public IReadOnlyList<MemuCommand> BuildProcessCommands(ScriptStep step, string memucPath, int instanceIndex)
    {
        ArgumentNullException.ThrowIfNull(step);
        if (step.TimeoutSeconds <= 0) throw new ArgumentOutOfRangeException(nameof(step), "Timeout phải lớn hơn 0 giây.");

        var command = commandBuilder.BuildAndroidShell(memucPath, instanceIndex, BuildShellCommand(step));
        if (!HasFollowUpEnter(step)) return [command];

        return
        [
            command,
            commandBuilder.BuildAndroidShell(memucPath, instanceIndex,
                step is AndroidClipboardPasteStep ? "input keyevent 66" : "input keyevent KEYCODE_ENTER")
        ];
    }

    public void Validate(ScriptStep step)
    {
        ArgumentNullException.ThrowIfNull(step);
        if (step is NoteStep) return;
        if (step is DelayStep delay)
        {
            if (delay.DurationMilliseconds < 0)
                throw new ArgumentOutOfRangeException(nameof(step), "Delay không được âm.");
            return;
        }
        if (step.TimeoutSeconds <= 0) throw new ArgumentOutOfRangeException(nameof(step), "Timeout phải lớn hơn 0 giây.");
        _ = BuildShellCommand(step);
    }

    private static string BuildShellCommand(ScriptStep step) => step switch
        {
            AndroidShellStep shell => Required(shell.Command, nameof(shell.Command)),
            ForceStopStep forceStop => $"am force-stop {ValidatePackageName(forceStop.PackageName)}",
            OpenAppStep open => $"am start -n {ValidatePackageName(open.PackageName)}/{ValidateActivityName(open.ActivityName)}",
            TapStep tap => $"input tap {Invariant(tap.X)} {Invariant(tap.Y)}",
            HoldStep hold when hold.DurationMilliseconds > 0 =>
                $"input swipe {Invariant(hold.X)} {Invariant(hold.Y)} {Invariant(hold.X)} {Invariant(hold.Y)} {Invariant(hold.DurationMilliseconds)}",
            HoldStep => throw new ArgumentOutOfRangeException(nameof(step), "Thời gian nhấn giữ phải lớn hơn 0 ms."),
            SwipeStep swipe when swipe.DurationMilliseconds >= 0 =>
                $"input swipe {Invariant(swipe.X1)} {Invariant(swipe.Y1)} {Invariant(swipe.X2)} {Invariant(swipe.Y2)} {Invariant(swipe.DurationMilliseconds)}",
            SwipeStep => throw new ArgumentOutOfRangeException(nameof(step), "Thời lượng swipe không được âm."),
            InputTextStep input => $"input text {EncodeInputText(Required(input.Text, nameof(input.Text)))}",
            AndroidClipboardPasteStep => "input keyevent 279",
            KeyEventStep key => $"input keyevent {MapKey(key.Key)}",
            DelayStep or NoteStep => throw new InvalidOperationException("Delay và note không khởi chạy process."),
            _ => throw new NotSupportedException($"Loại bước {step.GetType().Name} chưa được hỗ trợ.")
        };

    public string BuildPreview(ScriptStep step, string? memucPath, int? instanceIndex)
    {
        ArgumentNullException.ThrowIfNull(step);
        if (!step.IsEnabled) return "[Đã tắt]";
        if (step is DelayStep delay) return $"[Delay {delay.DurationMilliseconds} ms]";
        if (step is NoteStep note) return $"[Note] {note.Text}";
        if (string.IsNullOrWhiteSpace(memucPath) || instanceIndex is null) return "Chọn memuc.exe và một instance để xem preview.";
        return string.Join(Environment.NewLine, BuildProcessCommands(step, memucPath, instanceIndex.Value).Select(command => command.Preview));
    }

    private static string Required(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        return value.Trim();
    }

    private static string Invariant(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string ValidatePackageName(string value)
    {
        value = Required(value, nameof(value));
        var segments = value.Split('.');
        if (segments.Length < 2 || segments.Any(segment => segment.Length == 0 || !IsJavaIdentifier(segment)))
            throw new ArgumentException("Package name không hợp lệ.", nameof(value));
        return value;
    }

    private static string ValidateActivityName(string value)
    {
        value = Required(value, nameof(value));
        var candidate = value.StartsWith(".", StringComparison.Ordinal) ? value[1..] : value;
        var segments = candidate.Split('.');
        if (segments.Any(segment => segment.Length == 0 || !IsJavaIdentifier(segment)))
            throw new ArgumentException("Activity name không hợp lệ.", nameof(value));
        return value;
    }

    private static bool IsJavaIdentifier(string value) =>
        (char.IsLetter(value[0]) || value[0] == '_') &&
        value.Skip(1).All(character => char.IsLetterOrDigit(character) || character == '_');

    private static string EncodeInputText(string value)
    {
        value = Required(value, nameof(value));
        if (value.Any(character => !(char.IsLetterOrDigit(character) || character is ' ' or '.' or ',' or '_' or '-' or '@' or '%')))
            throw new ArgumentException("Văn bản chứa ký tự shell không an toàn. Chỉ dùng chữ, số, khoảng trắng và . , _ - @ %.", nameof(value));
        return value.Replace("%", "%25", StringComparison.Ordinal).Replace(" ", "%s", StringComparison.Ordinal);
    }

    private static string MapKey(AndroidKeyEvent key) => key switch
    {
        AndroidKeyEvent.Back => "KEYCODE_BACK",
        AndroidKeyEvent.Home => "KEYCODE_HOME",
        AndroidKeyEvent.RecentApps => "187",
        AndroidKeyEvent.Menu => "82",
        AndroidKeyEvent.VolumeUp => "KEYCODE_VOLUME_UP",
        AndroidKeyEvent.VolumeDown => "KEYCODE_VOLUME_DOWN",
        _ => throw new ArgumentOutOfRangeException(nameof(key))
    };

    private static bool HasFollowUpEnter(ScriptStep step) => step switch
    {
        InputTextStep { PressEnterAfterInput: true } => true,
        AndroidClipboardPasteStep { PressEnterAfterPaste: true } => true,
        _ => false
    };
}
