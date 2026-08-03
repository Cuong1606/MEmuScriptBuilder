using System.Text.RegularExpressions;
using MEmuScriptStudio.Core.Models;

namespace MEmuScriptStudio.Core.MEmu;

public sealed partial class AndroidLauncherActivityParser
{
    public IReadOnlyList<MemuApplicationInfo> Parse(string? output)
    {
        if (string.IsNullOrWhiteSpace(output)) return [];

        return output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(ParseLine)
            .Where(application => application is not null)
            .Cast<MemuApplicationInfo>()
            .DistinctBy(application => application.PackageName, StringComparer.Ordinal)
            .OrderBy(application => application.PackageName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static MemuApplicationInfo? ParseLine(string line)
    {
        var match = ComponentPattern().Match(line);
        if (!match.Success) return null;
        var packageName = match.Groups["package"].Value;
        var activityName = match.Groups["activity"].Value;
        return new MemuApplicationInfo(packageName, activityName);
    }

    [GeneratedRegex("^(?<package>[A-Za-z][A-Za-z0-9_]*(?:\\.[A-Za-z0-9_]+)+)/(?<activity>\\.?[A-Za-z_][A-Za-z0-9_.]*)$")]
    private static partial Regex ComponentPattern();
}

public sealed partial class AndroidApplicationLabelParser
{
    public IReadOnlyDictionary<string, string> Parse(string? output)
    {
        var labels = new Dictionary<string, string>(StringComparer.Ordinal);
        string? packageName = null;

        foreach (var rawLine in (output ?? string.Empty).Split(['\r', '\n']))
        {
            var line = rawLine.Trim();
            var packageMatch = PackagePattern().Match(line);
            if (packageMatch.Success)
            {
                packageName = packageMatch.Groups["package"].Value;
                continue;
            }

            if (packageName is null) continue;
            var labelMatch = LabelPattern().Match(line);
            if (!labelMatch.Success) continue;
            var label = labelMatch.Groups["label"].Value.Trim();
            if (label.Length > 0 && !string.Equals(label, "null", StringComparison.OrdinalIgnoreCase))
                labels.TryAdd(packageName, label);
        }

        return labels;
    }

    [GeneratedRegex("(?:^|\\s)packageName=(?<package>[A-Za-z][A-Za-z0-9_]*(?:\\.[A-Za-z0-9_]+)+)(?:\\s|$)")]
    private static partial Regex PackagePattern();

    [GeneratedRegex("^nonLocalizedLabel=(?<label>.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex LabelPattern();
}

public sealed partial class AndroidForegroundApplicationParser
{
    public MemuApplicationInfo? Parse(string? output)
    {
        if (string.IsNullOrWhiteSpace(output)) return null;
        var foregroundLines = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Where(line => ForegroundMarkers.Any(marker => line.Contains(marker, StringComparison.OrdinalIgnoreCase)));
        foreach (var line in foregroundLines)
        {
            var match = ComponentPattern().Match(line);
            if (match.Success)
                return new MemuApplicationInfo(match.Groups["package"].Value, match.Groups["activity"].Value);
        }
        return null;
    }

    [GeneratedRegex("(?<package>[A-Za-z][A-Za-z0-9_]*(?:\\.[A-Za-z0-9_]+)+)/(?<activity>\\.?[A-Za-z_][A-Za-z0-9_.$]*)")]
    private static partial Regex ComponentPattern();

    private static readonly string[] ForegroundMarkers =
        ["topResumedActivity", "mResumedActivity", "mCurrentFocus", "mFocusedApp"];
}

public static class AndroidScreenSizeParser
{
    private static readonly Regex SizePattern = new("(?:Override|Physical) size:\\s*(?<width>\\d+)x(?<height>\\d+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static (int Width, int Height) Parse(string output)
    {
        var matches = SizePattern.Matches(output ?? string.Empty);
        if (matches.Count == 0) throw new InvalidDataException("Không đọc được độ phân giải Android từ 'wm size'.");
        var match = matches[^1];
        return (int.Parse(match.Groups["width"].Value, System.Globalization.CultureInfo.InvariantCulture),
            int.Parse(match.Groups["height"].Value, System.Globalization.CultureInfo.InvariantCulture));
    }
}

public static class MemuCoordinateMapper
{
    public static ScreenRectangle FitViewport(ScreenRectangle host, int guestWidth, int guestHeight)
    {
        if (host.Width <= 0 || host.Height <= 0 || guestWidth <= 0 || guestHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(host));
        var scale = Math.Min((double)host.Width / guestWidth, (double)host.Height / guestHeight);
        var width = Math.Max(1, (int)Math.Round(guestWidth * scale));
        var height = Math.Max(1, (int)Math.Round(guestHeight * scale));
        return new ScreenRectangle(host.Left + ((host.Width - width) / 2), host.Top + ((host.Height - height) / 2), width, height);
    }

    public static ScreenPoint ToGuest(ScreenPoint screenPoint, ScreenRectangle viewport, int guestWidth, int guestHeight)
    {
        if (!viewport.Contains(screenPoint)) throw new ArgumentOutOfRangeException(nameof(screenPoint));
        var x = Math.Clamp((int)((long)(screenPoint.X - viewport.Left) * guestWidth / viewport.Width), 0, guestWidth - 1);
        var y = Math.Clamp((int)((long)(screenPoint.Y - viewport.Top) * guestHeight / viewport.Height), 0, guestHeight - 1);
        return new ScreenPoint(x, y);
    }
}

public static class MemuViewportSelector
{
    public static ScreenRectangle Select(
        ScreenRectangle root,
        IReadOnlyCollection<ScreenRectangle> candidates,
        int guestWidth,
        int guestHeight)
    {
        if (root.Width <= 0 || root.Height <= 0) throw new ArgumentOutOfRangeException(nameof(root));
        var minimumArea = (long)root.Width * root.Height / 4;
        var usable = candidates
            .Where(candidate => candidate.Width >= 100 && candidate.Height >= 100)
            .Where(candidate => (long)candidate.Width * candidate.Height >= minimumArea)
            .Where(candidate => candidate.Left >= root.Left && candidate.Top >= root.Top &&
                                candidate.Right <= root.Right && candidate.Bottom <= root.Bottom)
            .Append(root)
            .Distinct()
            .Select(candidate => new
            {
                Rectangle = candidate,
                Difference = Math.Abs(((double)candidate.Width / candidate.Height) - ((double)guestWidth / guestHeight)),
                Area = (long)candidate.Width * candidate.Height
            })
            .OrderBy(candidate => candidate.Difference)
            .ThenByDescending(candidate => candidate.Area)
            .First();
        return MemuCoordinateMapper.FitViewport(usable.Rectangle, guestWidth, guestHeight);
    }
}
