using MEmuScriptStudio.Core.Android;

namespace MEmuScriptStudio.Infrastructure.Android;

public sealed class AdbPathDiscovery : IAdbPathDiscovery
{
    public string? FindAdbPath(string? memucPath = null)
    {
        var pathValue = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var pathCandidates = pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(directory => Path.Combine(directory.Trim(), "adb.exe"));

        var sdkRoots = new[]
        {
            Environment.GetEnvironmentVariable("ANDROID_SDK_ROOT"),
            Environment.GetEnvironmentVariable("ANDROID_HOME"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Android", "Sdk")
        }
        .Where(path => !string.IsNullOrWhiteSpace(path))
        .Select(path => Path.Combine(path!, "platform-tools", "adb.exe"));

        var memuSibling = string.IsNullOrWhiteSpace(memucPath)
            ? []
            : new[] { Path.Combine(Path.GetDirectoryName(memucPath) ?? string.Empty, "adb.exe") };

        var programFilesCandidates = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
        }
        .Where(path => !string.IsNullOrWhiteSpace(path))
        .SelectMany(root => new[]
        {
            Path.Combine(root, "Microvirt", "MEmu", "adb.exe"),
            Path.Combine(root, "Microvirt", "MEmuHyperv", "adb.exe")
        });

        return pathCandidates
            .Concat(sdkRoots)
            .Concat(memuSibling)
            .Concat(programFilesCandidates)
            .FirstOrDefault(IsValidAdbPath);
    }

    public bool IsValidAdbPath(string? path) =>
        !string.IsNullOrWhiteSpace(path) &&
        string.Equals(Path.GetFileName(path), "adb.exe", StringComparison.OrdinalIgnoreCase) &&
        File.Exists(path);
}
