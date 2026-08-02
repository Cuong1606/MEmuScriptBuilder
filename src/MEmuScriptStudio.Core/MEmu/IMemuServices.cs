using MEmuScriptStudio.Core.Models;

namespace MEmuScriptStudio.Core.MEmu;

public interface IMemuInstanceService
{
    Task<IReadOnlyList<MemuInstance>> GetInstancesAsync(string memucPath, CancellationToken cancellationToken);
}

public interface IMemucPathDiscovery
{
    string? FindMemucPath();
    bool IsValidMemucPath(string? path);
}

public interface ISettingsStore
{
    Task<ApplicationSettings> LoadAsync(CancellationToken cancellationToken);
    Task SaveAsync(ApplicationSettings settings, CancellationToken cancellationToken);
}
