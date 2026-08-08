namespace MEmuScriptStudio.Core.Formatting;

public static class DurationFormatter
{
    public static string FormatMilliseconds(int totalMilliseconds)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(totalMilliseconds);

        var remaining = totalMilliseconds;
        var hours = remaining / 3_600_000;
        remaining %= 3_600_000;
        var minutes = remaining / 60_000;
        remaining %= 60_000;
        var seconds = remaining / 1_000;
        var milliseconds = remaining % 1_000;

        var parts = new List<string>(4);
        if (hours > 0) parts.Add($"{hours} giờ");
        if (minutes > 0) parts.Add($"{minutes} phút");
        if (seconds > 0) parts.Add($"{seconds} giây");
        if (milliseconds > 0 || parts.Count == 0) parts.Add($"{milliseconds} ms");
        return string.Join(' ', parts);
    }
}
