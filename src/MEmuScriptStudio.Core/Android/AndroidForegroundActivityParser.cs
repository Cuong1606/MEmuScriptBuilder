using System.Text.RegularExpressions;

namespace MEmuScriptStudio.Core.Android;

public sealed partial class AndroidForegroundActivityParser
{
    private static readonly string[] ActivityManagerMarkers =
        ["topResumedActivity", "mResumedActivity", "ResumedActivity"];
    private static readonly string[] WindowManagerMarkers = ["mCurrentFocus", "mFocusedApp"];

    public AndroidApplicationInfo? ParseActivityManager(string? output) =>
        ParseByMarkerPriority(output, ActivityManagerMarkers);

    public AndroidApplicationInfo? ParseWindowManager(string? output) =>
        ParseByMarkerPriority(output, WindowManagerMarkers);

    private static AndroidApplicationInfo? ParseByMarkerPriority(string? output, IReadOnlyList<string> markers)
    {
        if (string.IsNullOrWhiteSpace(output)) return null;
        var lines = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        foreach (var marker in markers)
        {
            foreach (var line in lines)
            {
                var payload = FindMarkerPayload(line, marker);
                if (payload is null) continue;
                var match = ComponentPattern().Match(payload);
                if (match.Success)
                    return new AndroidApplicationInfo(
                        match.Groups["package"].Value,
                        match.Groups["activity"].Value);
            }
        }
        return null;
    }

    private static string? FindMarkerPayload(string line, string marker)
    {
        var searchIndex = 0;
        while (searchIndex < line.Length)
        {
            var index = line.IndexOf(marker, searchIndex, StringComparison.OrdinalIgnoreCase);
            if (index < 0) return null;
            var hasFieldBoundary = index == 0 || !IsIdentifierCharacter(line[index - 1]);
            var separatorIndex = index + marker.Length;
            while (separatorIndex < line.Length && char.IsWhiteSpace(line[separatorIndex])) separatorIndex++;
            if (hasFieldBoundary && separatorIndex < line.Length && line[separatorIndex] is ':' or '=')
                return line[(separatorIndex + 1)..];
            searchIndex = index + 1;
        }
        return null;
    }

    private static bool IsIdentifierCharacter(char value) => char.IsLetterOrDigit(value) || value == '_';

    [GeneratedRegex("(?<package>[A-Za-z][A-Za-z0-9_]*(?:\\.[A-Za-z0-9_]+)+)/(?<activity>\\.?[A-Za-z_$][A-Za-z0-9_.$]*)")]
    private static partial Regex ComponentPattern();
}
