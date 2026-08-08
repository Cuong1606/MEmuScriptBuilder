using System.Globalization;
using System.Text.RegularExpressions;
using MEmuScriptStudio.Core.Models;

namespace MEmuScriptStudio.Core.Android;

public sealed record AdbDeviceListEntry(
    string Serial,
    AndroidConnectionState State,
    string? Product,
    string? Model,
    string? Device);

public sealed class AdbDevicesParser
{
    public IReadOnlyList<AdbDeviceListEntry> Parse(string output)
    {
        if (string.IsNullOrWhiteSpace(output)) return [];

        var results = new List<AdbDeviceListEntry>();
        foreach (var rawLine in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith("List of devices attached", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith('*')) continue;

            var columns = Regex.Split(line, "\\s+");
            if (columns.Length < 2) continue;
            var serial = columns[0].Trim();
            if (serial.Length == 0) continue;
            var state = ParseState(columns[1]);
            var metadata = columns.Skip(2)
                .Select(value => value.Split(':', 2))
                .Where(parts => parts.Length == 2 && parts[0].Length > 0)
                .GroupBy(parts => parts[0], StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First()[1], StringComparer.OrdinalIgnoreCase);
            results.Add(new AdbDeviceListEntry(
                serial,
                state,
                metadata.GetValueOrDefault("product"),
                metadata.GetValueOrDefault("model"),
                metadata.GetValueOrDefault("device")));
        }

        return results;
    }

    public static AndroidConnectionState ParseState(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "device" => AndroidConnectionState.Device,
        "unauthorized" => AndroidConnectionState.Unauthorized,
        "offline" => AndroidConnectionState.Offline,
        _ => AndroidConnectionState.Unknown
    };
}

public static partial class AndroidAdbMetadataParser
{
    [GeneratedRegex("^\\[(?<key>[^]]+)\\]: \\[(?<value>.*)\\]$", RegexOptions.Multiline)]
    private static partial Regex PropertyLineRegex();

    public static IReadOnlyDictionary<string, string> ParseProperties(string output) =>
        PropertyLineRegex().Matches(output ?? string.Empty)
            .Select(match => (Key: match.Groups["key"].Value, Value: match.Groups["value"].Value))
            .GroupBy(item => item.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().Value, StringComparer.Ordinal);

    public static (int Width, int Height)? ParseSize(string output)
    {
        var matches = Regex.Matches(output ?? string.Empty, "(?:Physical|Override) size:\\s*(?<width>\\d+)x(?<height>\\d+)", RegexOptions.IgnoreCase);
        var selected = matches.Cast<Match>().LastOrDefault();
        return selected is not null &&
               int.TryParse(selected.Groups["width"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var width) &&
               int.TryParse(selected.Groups["height"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var height)
            ? (width, height)
            : null;
    }

    public static int? ParseDensity(string output)
    {
        var matches = Regex.Matches(output ?? string.Empty, "(?:Physical|Override) density:\\s*(?<density>\\d+)", RegexOptions.IgnoreCase);
        var selected = matches.Cast<Match>().LastOrDefault();
        return selected is not null && int.TryParse(selected.Groups["density"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var density)
            ? density
            : null;
    }

    public static int? ParseInteger(string? output) =>
        int.TryParse(output?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;
}
