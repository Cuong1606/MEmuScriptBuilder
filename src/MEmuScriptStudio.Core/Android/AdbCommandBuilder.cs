using System.Globalization;
using MEmuScriptStudio.Core.MEmu;
using MEmuScriptStudio.Core.Models;

namespace MEmuScriptStudio.Core.Android;

public sealed class AdbCommandBuilder
{
    public MemuCommand BuildDevices(string adbPath) => Build(adbPath, "devices", "-l");

    public MemuCommand BuildGetState(string adbPath, string serial) =>
        BuildForSerial(adbPath, serial, "get-state");

    public MemuCommand BuildGetProperties(string adbPath, string serial) =>
        BuildForSerial(adbPath, serial, "shell", "getprop");

    public MemuCommand BuildWmSize(string adbPath, string serial) =>
        BuildForSerial(adbPath, serial, "shell", "wm", "size");

    public MemuCommand BuildWmDensity(string adbPath, string serial) =>
        BuildForSerial(adbPath, serial, "shell", "wm", "density");

    public MemuCommand BuildOrientation(string adbPath, string serial) =>
        BuildForSerial(adbPath, serial, "shell", "settings", "get", "system", "user_rotation");

    public MemuCommand BuildScreenCapture(string adbPath, string serial) =>
        BuildForSerial(adbPath, serial, "exec-out", "screencap", "-p");

    public MemuCommand BuildQueryLauncherActivities(string adbPath, string serial) =>
        BuildForSerial(
            adbPath,
            serial,
            "shell", "cmd", "package", "query-activities", "--brief", "--components", "--user", "0",
            "-a", "android.intent.action.MAIN", "-c", "android.intent.category.LAUNCHER");

    public MemuCommand BuildQueryLauncherActivityMetadata(string adbPath, string serial) =>
        BuildForSerial(
            adbPath,
            serial,
            "shell", "cmd", "package", "query-activities", "--user", "0",
            "-a", "android.intent.action.MAIN", "-c", "android.intent.category.LAUNCHER");

    public MemuCommand BuildQueryForegroundActivity(string adbPath, string serial) =>
        BuildForSerial(adbPath, serial, "shell", "dumpsys", "activity", "activities");

    public MemuCommand BuildQueryForegroundWindow(string adbPath, string serial) =>
        BuildForSerial(adbPath, serial, "shell", "dumpsys", "window");

    public IReadOnlyList<MemuCommand> BuildStepCommands(ScriptStep step, string adbPath, string serial)
    {
        ArgumentNullException.ThrowIfNull(step);
        AndroidScriptCapabilities.ThrowIfUnsupported(step);
        if (step.TimeoutSeconds <= 0) throw new ArgumentOutOfRangeException(nameof(step), "Timeout phải lớn hơn 0 giây.");

        var command = step switch
        {
            TapStep tap => BuildForSerial(adbPath, serial, "shell", "input", "tap", Invariant(tap.X), Invariant(tap.Y)),
            HoldStep hold when hold.DurationMilliseconds > 0 => BuildForSerial(
                adbPath, serial, "shell", "input", "swipe",
                Invariant(hold.X), Invariant(hold.Y), Invariant(hold.X), Invariant(hold.Y), Invariant(hold.DurationMilliseconds)),
            HoldStep => throw new ArgumentOutOfRangeException(nameof(step), "Thời gian nhấn giữ phải lớn hơn 0 ms."),
            SwipeStep swipe when swipe.DurationMilliseconds >= 0 => BuildForSerial(
                adbPath, serial, "shell", "input", "swipe",
                Invariant(swipe.X1), Invariant(swipe.Y1), Invariant(swipe.X2), Invariant(swipe.Y2), Invariant(swipe.DurationMilliseconds)),
            SwipeStep => throw new ArgumentOutOfRangeException(nameof(step), "Thời lượng swipe không được âm."),
            InputTextStep input => BuildForSerial(adbPath, serial, "shell", "input", "text", EncodeInputText(Required(input.Text))),
            AndroidClipboardPasteStep => BuildForSerial(
                adbPath, serial, "shell", "input", "keyevent", "KEYCODE_PASTE"),
            ForceStopStep forceStop => BuildForSerial(
                adbPath, serial, "shell", "am", "force-stop", ValidatePackageName(forceStop.PackageName)),
            OpenAppStep open => BuildForSerial(adbPath, serial, "shell", "am", "start", "-n",
                $"{ValidatePackageName(open.PackageName)}/{EscapeActivityForRemoteShell(ValidateActivityName(open.ActivityName))}"),
            KeyEventStep key => BuildForSerial(adbPath, serial, "shell", "input", "keyevent", MapKey(key.Key)),
            _ => throw new NotSupportedException(AndroidScriptCapabilities.UnsupportedMessage(step))
        };

        if (step is InputTextStep { PressEnterAfterInput: true } or
            AndroidClipboardPasteStep { PressEnterAfterPaste: true })
            return [command, BuildForSerial(adbPath, serial, "shell", "input", "keyevent", "KEYCODE_ENTER")];
        return [command];
    }

    public string BuildPreview(ScriptStep step, string? adbPath, string? serial)
    {
        ArgumentNullException.ThrowIfNull(step);
        if (!step.IsEnabled) return "[Đã tắt]";
        if (step is DelayStep delay) return $"[Chờ {Formatting.DurationFormatter.FormatMilliseconds(delay.DurationMilliseconds)}]";
        if (step is NoteStep note) return $"[Note] {note.Text}";
        if (string.IsNullOrWhiteSpace(adbPath) || string.IsNullOrWhiteSpace(serial))
            return "Chọn adb.exe và một Android device để xem preview.";
        if (!AndroidScriptCapabilities.IsSupported(step)) return $"[Android / ADB] {AndroidScriptCapabilities.UnsupportedMessage(step)}";
        return string.Join(Environment.NewLine, BuildStepCommands(step, adbPath, serial).Select(command => command.Preview));
    }

    private static MemuCommand Build(string adbPath, params string[] arguments)
    {
        ValidatePath(adbPath);
        return new MemuCommand(adbPath, arguments);
    }

    private static MemuCommand BuildForSerial(string adbPath, string serial, params string[] arguments)
    {
        ValidatePath(adbPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(serial);
        return new MemuCommand(adbPath, new[] { "-s", serial.Trim() }.Concat(arguments).ToArray());
    }

    private static void ValidatePath(string adbPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(adbPath);
        if (!string.Equals(Path.GetFileName(adbPath), "adb.exe", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Đường dẫn phải trỏ tới adb.exe.", nameof(adbPath));
    }

    private static string MapKey(AndroidKeyEvent key) => key switch
    {
        AndroidKeyEvent.Home => "KEYCODE_HOME",
        AndroidKeyEvent.Back => "KEYCODE_BACK",
        AndroidKeyEvent.RecentApps => "KEYCODE_APP_SWITCH",
        _ => throw new NotSupportedException("Bước key event này chưa hỗ trợ Android / ADB.")
    };

    private static string Invariant(int value) => value.ToString(CultureInfo.InvariantCulture);
    private static string Required(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return value.Trim();
    }

    private static string EncodeInputText(string value)
    {
        value = Required(value);
        if (value.Any(character => !(char.IsLetterOrDigit(character) || character is ' ' or '.' or ',' or '_' or '-' or '@' or '%')))
            throw new ArgumentException("Văn bản chứa ký tự shell không an toàn. Chỉ dùng chữ, số, khoảng trắng và . , _ - @ %.", nameof(value));
        if (value.Contains("%s", StringComparison.Ordinal))
            throw new ArgumentException("Văn bản chứa chuỗi %s không thể biểu diễn nguyên văn bằng Android input text.", nameof(value));
        return value.Replace(" ", "%s", StringComparison.Ordinal);
    }

    private static string ValidatePackageName(string value)
    {
        value = Required(value);
        var segments = value.Split('.');
        if (segments.Length < 2 || segments.Any(segment => segment.Length == 0 || !IsJavaIdentifier(segment)))
            throw new ArgumentException("Package name không hợp lệ.", nameof(value));
        return value;
    }

    private static string ValidateActivityName(string value)
    {
        value = Required(value);
        var candidate = value.StartsWith(".", StringComparison.Ordinal) ? value[1..] : value;
        var segments = candidate.Split('.');
        if (segments.Any(segment => segment.Length == 0 || !IsJavaIdentifier(segment, allowDollar: true)))
            throw new ArgumentException("Activity name không hợp lệ.", nameof(value));
        return value;
    }

    private static string EscapeActivityForRemoteShell(string value) =>
        value.Replace("$", "\\$", StringComparison.Ordinal);

    private static bool IsJavaIdentifier(string value, bool allowDollar = false) =>
        (char.IsLetter(value[0]) || value[0] == '_' || allowDollar && value[0] == '$') &&
        value.Skip(1).All(character => char.IsLetterOrDigit(character) || character == '_' || allowDollar && character == '$');
}

public static class AndroidScriptCapabilities
{
    public static bool IsSupported(ScriptStep step) => step switch
    {
        DelayStep or NoteStep or TapStep or HoldStep or SwipeStep or InputTextStep or
            AndroidClipboardPasteStep or ForceStopStep or OpenAppStep => true,
        KeyEventStep { Key: AndroidKeyEvent.Home or AndroidKeyEvent.Back or AndroidKeyEvent.RecentApps } => true,
        _ => false
    };

    public static void ThrowIfUnsupported(ScriptStep step)
    {
        if (!IsSupported(step)) throw new NotSupportedException(UnsupportedMessage(step));
    }

    public static string UnsupportedMessage(ScriptStep step) => step switch
    {
        CloseChromeTabsStep => "Đóng tất cả tab Chrome chưa hỗ trợ Android / ADB.",
        _ => $"Bước này chưa hỗ trợ Android / ADB: {step.Name} ({step.Kind})."
    };

    public static string? FindUnsupportedStep(
        ScriptDefinition script,
        IReadOnlyDictionary<Guid, ScriptDefinition> library)
    {
        if (script.Kind == ScriptKind.Regular)
            return script.Steps.FirstOrDefault(step => step.IsEnabled && !IsSupported(step)) is { } unsupported
                ? UnsupportedMessage(unsupported)
                : null;

        foreach (var reference in script.CompositeItems.OfType<ScriptReferenceItem>().Where(item => item.IsEnabled))
        {
            if (!library.TryGetValue(reference.ScriptId, out var child))
                return $"Kịch bản gộp tham chiếu kịch bản không tồn tại: {reference.ScriptId}.";
            var unsupported = FindUnsupportedStep(child, library);
            if (unsupported is not null) return unsupported;
        }
        return null;
    }
}
