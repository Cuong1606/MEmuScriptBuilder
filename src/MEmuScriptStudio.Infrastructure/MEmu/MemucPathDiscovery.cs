using MEmuScriptStudio.Core.MEmu;

namespace MEmuScriptStudio.Infrastructure.MEmu;

public sealed class MemucPathDiscovery : IMemucPathDiscovery
{
    private readonly Func<string, string?> getEnvironmentVariable;
    private readonly Func<Environment.SpecialFolder, string> getFolderPath;
    private readonly Func<string, bool> fileExists;

    public MemucPathDiscovery()
        : this(Environment.GetEnvironmentVariable, Environment.GetFolderPath, File.Exists)
    {
    }

    internal MemucPathDiscovery(
        Func<string, string?> getEnvironmentVariable,
        Func<Environment.SpecialFolder, string> getFolderPath,
        Func<string, bool> fileExists)
    {
        this.getEnvironmentVariable = getEnvironmentVariable;
        this.getFolderPath = getFolderPath;
        this.fileExists = fileExists;
    }

    public string? FindMemucPath()
    {
        var pathValue = getEnvironmentVariable("PATH") ?? string.Empty;
        var pathCandidates = pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(directory => Path.Combine(directory.Trim(), "memuc.exe"));

        var installCandidates = new[]
        {
            getFolderPath(Environment.SpecialFolder.ProgramFiles),
            getFolderPath(Environment.SpecialFolder.ProgramFilesX86)
        }
        .Where(path => !string.IsNullOrWhiteSpace(path))
        .SelectMany(root => new[]
        {
            Path.Combine(root, "Microvirt", "MEmu", "memuc.exe"),
            Path.Combine(root, "Microvirt", "MEmuHyperv", "memuc.exe")
        });

        return pathCandidates.Concat(installCandidates).FirstOrDefault(IsValidMemucPath);
    }

    public bool IsValidMemucPath(string? path) =>
        !string.IsNullOrWhiteSpace(path) &&
        string.Equals(Path.GetFileName(path), "memuc.exe", StringComparison.OrdinalIgnoreCase) &&
        fileExists(path);
}
