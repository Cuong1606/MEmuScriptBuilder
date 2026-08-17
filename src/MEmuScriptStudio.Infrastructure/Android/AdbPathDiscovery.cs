using MEmuScriptStudio.Core.Android;

namespace MEmuScriptStudio.Infrastructure.Android;

public sealed class AdbPathDiscovery : IAdbPathDiscovery
{
    private readonly Func<string, string?> getEnvironmentVariable;
    private readonly Func<Environment.SpecialFolder, string> getFolderPath;
    private readonly Func<string, bool> fileExists;
    private readonly Func<string> getBaseDirectory;

    public AdbPathDiscovery()
        : this(Environment.GetEnvironmentVariable, Environment.GetFolderPath, File.Exists, () => AppContext.BaseDirectory)
    {
    }

    internal AdbPathDiscovery(
        Func<string, string?> getEnvironmentVariable,
        Func<Environment.SpecialFolder, string> getFolderPath,
        Func<string, bool> fileExists,
        Func<string>? getBaseDirectory = null)
    {
        this.getEnvironmentVariable = getEnvironmentVariable;
        this.getFolderPath = getFolderPath;
        this.fileExists = fileExists;
        this.getBaseDirectory = getBaseDirectory ?? (() => AppContext.BaseDirectory);
    }

    public string? FindAdbPath(string? memucPath = null)
    {
        var bundledCandidate = Path.Combine(getBaseDirectory(), "tools", "adb", "adb.exe");
        var pathValue = getEnvironmentVariable("PATH") ?? string.Empty;
        var pathCandidates = pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(directory => Path.Combine(directory.Trim(), "adb.exe"));

        var sdkRoots = new[]
        {
            getEnvironmentVariable("ANDROID_SDK_ROOT"),
            getEnvironmentVariable("ANDROID_HOME"),
            Path.Combine(getFolderPath(Environment.SpecialFolder.LocalApplicationData), "Android", "Sdk")
        }
        .Where(path => !string.IsNullOrWhiteSpace(path))
        .Select(path => Path.Combine(path!, "platform-tools", "adb.exe"));

        var memuSibling = string.IsNullOrWhiteSpace(memucPath)
            ? []
            : new[] { Path.Combine(Path.GetDirectoryName(memucPath) ?? string.Empty, "adb.exe") };

        var programFilesCandidates = new[]
        {
            getFolderPath(Environment.SpecialFolder.ProgramFiles),
            getFolderPath(Environment.SpecialFolder.ProgramFilesX86)
        }
        .Where(path => !string.IsNullOrWhiteSpace(path))
        .SelectMany(root => new[]
        {
            Path.Combine(root, "Microvirt", "MEmu", "adb.exe"),
            Path.Combine(root, "Microvirt", "MEmuHyperv", "adb.exe")
        });

        return new[] { bundledCandidate }
            .Concat(sdkRoots)
            .Concat(pathCandidates)
            .Concat(memuSibling)
            .Concat(programFilesCandidates)
            .FirstOrDefault(IsValidAdbPath);
    }

    public bool IsValidAdbPath(string? path) =>
        !string.IsNullOrWhiteSpace(path) &&
        string.Equals(Path.GetFileName(path), "adb.exe", StringComparison.OrdinalIgnoreCase) &&
        fileExists(path);
}
