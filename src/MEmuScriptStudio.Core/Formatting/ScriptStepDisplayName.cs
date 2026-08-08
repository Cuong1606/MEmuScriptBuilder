using MEmuScriptStudio.Core.Models;

namespace MEmuScriptStudio.Core.Formatting;

public static class ScriptStepDisplayName
{
    public const string DelayCanonicalName = "Chờ";

    public static string Get(ScriptStep step)
    {
        ArgumentNullException.ThrowIfNull(step);
        return step is DelayStep delay ? GetDelay(delay.DurationMilliseconds) : step.Name;
    }

    public static string GetDelay(int durationMilliseconds) =>
        $"{DelayCanonicalName} · {DurationFormatter.FormatMilliseconds(durationMilliseconds)}";

    public static string GetDefaultName(ScriptStepKind kind) => kind switch
    {
        ScriptStepKind.AndroidShell => "Lệnh Android shell",
        ScriptStepKind.ForceStop => "Buộc dừng ứng dụng",
        ScriptStepKind.OpenApp => "Mở ứng dụng",
        ScriptStepKind.Delay => DelayCanonicalName,
        ScriptStepKind.Tap => "Chạm",
        ScriptStepKind.Hold => "Nhấn giữ",
        ScriptStepKind.Swipe => "Vuốt",
        ScriptStepKind.InputText => "Nhập văn bản",
        ScriptStepKind.AndroidClipboardPaste => "Dán clipboard Android",
        ScriptStepKind.KeyEvent => "Phím Android",
        ScriptStepKind.Note => "Ghi chú — không thực thi",
        ScriptStepKind.CloseChromeTabs => "Đóng tất cả tab Chrome",
        _ => kind.ToString()
    };

    public static bool NormalizeDelayName(ScriptStep step)
    {
        ArgumentNullException.ThrowIfNull(step);
        if (step is not DelayStep || string.Equals(step.Name, DelayCanonicalName, StringComparison.Ordinal))
            return false;
        step.Name = DelayCanonicalName;
        return true;
    }

    public static void NormalizeDelayNames(IEnumerable<ScriptDefinition> scripts)
    {
        ArgumentNullException.ThrowIfNull(scripts);
        foreach (var step in scripts.SelectMany(script => script.Steps))
            NormalizeDelayName(step);
    }
}
