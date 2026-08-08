using MEmuScriptStudio.Core.Models;

namespace MEmuScriptStudio.Core.Android;

public interface IAdbPathDiscovery
{
    string? FindAdbPath(string? memucPath = null);
    bool IsValidAdbPath(string? path);
}

public interface IAndroidAdbDeviceService
{
    Task<IReadOnlyList<AndroidAdbDevice>> GetDevicesAsync(
        string adbPath,
        CancellationToken cancellationToken);
}

public interface IAndroidAdbTransportService
{
    Task<IReadOnlyList<AdbDeviceListEntry>> GetTransportsAsync(
        string adbPath,
        CancellationToken cancellationToken);
}

public interface IAndroidTargetClassifier
{
    AndroidTargetClassification Classify(
        string serial,
        IReadOnlyDictionary<string, string>? properties = null);
}

public sealed record AndroidApplicationInfo(
    string PackageName,
    string ActivityName,
    string? ApplicationLabel = null)
{
    public bool HasResolvedApplicationLabel => !string.IsNullOrWhiteSpace(ApplicationLabel);
    public string DisplayName => HasResolvedApplicationLabel ? ApplicationLabel!.Trim() : "Không xác định";
}

public interface IAndroidApplicationService
{
    Task<IReadOnlyList<AndroidApplicationInfo>> GetApplicationsAsync(
        string adbPath,
        string serial,
        CancellationToken cancellationToken);
}

public interface IAndroidForegroundApplicationService
{
    Task<AndroidApplicationInfo> GetForegroundApplicationAsync(
        string adbPath,
        string serial,
        CancellationToken cancellationToken);
}

public sealed record AndroidApplicationLibraryEntry(
    string PackageName,
    string ActivityName,
    string FriendlyName);

public interface IAndroidApplicationLibraryTransferService
{
    Task ExportAsync(
        string path,
        IReadOnlyCollection<AndroidApplicationLibraryEntry> entries,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<AndroidApplicationLibraryEntry>> ImportAsync(
        string path,
        CancellationToken cancellationToken);
}

public sealed record AndroidAdbStateResult(AndroidConnectionState State, string? Diagnostic = null)
{
    public bool IsRunnable => State == AndroidConnectionState.Device;
}

public interface IAndroidAdbStateProbe
{
    Task<AndroidAdbStateResult> CheckStateAsync(
        string adbPath,
        string serial,
        CancellationToken cancellationToken);
}

public sealed class AndroidAdbDeviceUnavailableException(string message) : Exception(message);
