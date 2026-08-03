using MEmuScriptStudio.Core.Models;

namespace MEmuScriptStudio.Core.MEmu;

public interface IMemuInstanceService
{
    Task<IReadOnlyList<MemuInstance>> GetInstancesAsync(string memucPath, CancellationToken cancellationToken);
}

public interface IMemuApplicationService
{
    Task<IReadOnlyList<MemuApplicationInfo>> GetApplicationsAsync(
        string memucPath,
        int instanceIndex,
        CancellationToken cancellationToken);
}

public interface IMemuForegroundApplicationService
{
    Task<MemuApplicationInfo> GetForegroundApplicationAsync(
        string memucPath,
        int instanceIndex,
        CancellationToken cancellationToken);
}

public interface IMemuInputCaptureService
{
    Task<CapturedTap> CaptureTapAsync(
        string memucPath,
        MemuInstance instance,
        IProgress<TapCaptureUpdate>? progress,
        CancellationToken cancellationToken);
    Task<CapturedSwipe> CaptureSwipeAsync(
        string memucPath,
        MemuInstance instance,
        IProgress<SwipeCaptureUpdate>? progress,
        CancellationToken cancellationToken);
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
