using MEmuScriptStudio.Core.MEmu;

namespace MEmuScriptStudio.Infrastructure.MEmu;

public sealed class MemucPathDiscovery : IMemucPathDiscovery
{
    public string? FindMemucPath()
    {
        var pathValue = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var pathCandidates = pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(directory => Path.Combine(directory.Trim(), "memuc.exe"));

        var installCandidates = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
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
        File.Exists(path);
}
