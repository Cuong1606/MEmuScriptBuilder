using System.Text.RegularExpressions;

namespace MEmuScriptStudio.Core.Android;

public sealed partial class AndroidLauncherApplicationParser
{
    public IReadOnlyList<AndroidApplicationInfo> Parse(string? output)
    {
        if (string.IsNullOrWhiteSpace(output)) return [];

        return output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(ParseLine)
            .Where(application => application is not null)
            .Cast<AndroidApplicationInfo>()
            .DistinctBy(
                application => $"{application.PackageName}/{application.ActivityName}",
                StringComparer.Ordinal)
            .OrderBy(application => application.PackageName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(application => application.ActivityName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static AndroidApplicationInfo? ParseLine(string line)
    {
        var match = ComponentPattern().Match(line);
        return match.Success
            ? new AndroidApplicationInfo(match.Groups["package"].Value, match.Groups["activity"].Value)
            : null;
    }

    [GeneratedRegex("^(?<package>[A-Za-z][A-Za-z0-9_]*(?:\\.[A-Za-z0-9_]+)+)/(?<activity>\\.?[A-Za-z_$][A-Za-z0-9_.$]*)$")]
    private static partial Regex ComponentPattern();
}
