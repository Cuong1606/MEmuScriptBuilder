using MEmuScriptStudio.Core.Android;
using MEmuScriptStudio.Core.MEmu;
using MEmuScriptStudio.Core.Models;
using MEmuScriptStudio.Core.Processes;

namespace MEmuScriptStudio.Infrastructure.Android;

public sealed class AndroidAdbDeviceService(
    IProcessRunner processRunner,
    AdbCommandBuilder commandBuilder,
    AdbDevicesParser devicesParser,
    IAndroidTargetClassifier? targetClassifier = null) :
    IAndroidAdbDeviceService,
    IAndroidAdbTransportService,
    IAndroidAdbStateProbe
{
    private static readonly TimeSpan DiscoveryTimeout = TimeSpan.FromSeconds(15);
    private readonly IAndroidTargetClassifier targetClassifier = targetClassifier ?? new AndroidTargetClassifier();

    public async Task<IReadOnlyList<AndroidAdbDevice>> GetDevicesAsync(
        string adbPath,
        CancellationToken cancellationToken)
    {
        var entries = await GetTransportsAsync(adbPath, cancellationToken).ConfigureAwait(false);
        var devices = new List<AndroidAdbDevice>(entries.Count);
        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (entry.State != AndroidConnectionState.Device)
            {
                var classification = targetClassifier.Classify(entry.Serial);
                if (classification == AndroidTargetClassification.MEmuBackedAdb) continue;
                devices.Add(CreateUnavailableDevice(entry, classification));
                continue;
            }
            try
            {
                var propertiesText = await RunRequiredReadAsync(
                    commandBuilder.BuildGetProperties(adbPath, entry.Serial), entry.Serial, "getprop", cancellationToken)
                    .ConfigureAwait(false);
                var properties = AndroidAdbMetadataParser.ParseProperties(propertiesText);
                var classification = targetClassifier.Classify(entry.Serial, properties);
                if (classification == AndroidTargetClassification.MEmuBackedAdb) continue;

                devices.Add(await ReadMetadataAsync(adbPath, entry, properties, classification, cancellationToken)
                    .ConfigureAwait(false));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception exception)
            {
                var classification = targetClassifier.Classify(entry.Serial);
                if (classification == AndroidTargetClassification.MEmuBackedAdb) continue;
                devices.Add(CreateUnavailableDevice(entry, classification) with
                {
                    ConnectionState = AndroidConnectionState.Device,
                    Diagnostic = CompactDiagnostic(exception.Message)
                });
            }
        }
        return devices;
    }

    public async Task<IReadOnlyList<AdbDeviceListEntry>> GetTransportsAsync(
        string adbPath,
        CancellationToken cancellationToken)
    {
        var result = await RunAsync(commandBuilder.BuildDevices(adbPath), DiscoveryTimeout, null, "AndroidDiscovery:devices", cancellationToken)
            .ConfigureAwait(false);
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"adb devices -l thất bại (exit code {result.ExitCode}): {result.StandardError.Trim()}");
        return devicesParser.Parse(result.StandardOutput);
    }

    public async Task<AndroidAdbStateResult> CheckStateAsync(
        string adbPath,
        string serial,
        CancellationToken cancellationToken)
    {
        var result = await RunAsync(
            commandBuilder.BuildGetState(adbPath, serial),
            TimeSpan.FromSeconds(10),
            null,
            "AndroidHealth:get-state",
            cancellationToken).ConfigureAwait(false);
        var combined = $"{result.StandardOutput}\n{result.StandardError}";
        var state = combined.Contains("unauthorized", StringComparison.OrdinalIgnoreCase)
            ? AndroidConnectionState.Unauthorized
            : combined.Contains("offline", StringComparison.OrdinalIgnoreCase)
                ? AndroidConnectionState.Offline
                : AdbDevicesParser.ParseState(result.StandardOutput);
        if (result.ExitCode == 0 && state == AndroidConnectionState.Device)
            return new AndroidAdbStateResult(AndroidConnectionState.Device);
        return new AndroidAdbStateResult(
            state,
            state switch
            {
                AndroidConnectionState.Unauthorized => "Android device chưa authorize USB debugging.",
                AndroidConnectionState.Offline => "Android device đang offline trong ADB.",
                _ => string.IsNullOrWhiteSpace(result.StandardError)
                    ? "Android device không còn ở trạng thái device trong ADB."
                    : result.StandardError.Trim()
            });
    }

    private async Task<AndroidAdbDevice> ReadMetadataAsync(
        string adbPath,
        AdbDeviceListEntry entry,
        IReadOnlyDictionary<string, string> parsedProperties,
        AndroidTargetClassification classification,
        CancellationToken cancellationToken)
    {
        var size = await RunRequiredReadAsync(
            commandBuilder.BuildWmSize(adbPath, entry.Serial), entry.Serial, "wm-size", cancellationToken).ConfigureAwait(false);
        var density = await RunRequiredReadAsync(
            commandBuilder.BuildWmDensity(adbPath, entry.Serial), entry.Serial, "wm-density", cancellationToken).ConfigureAwait(false);
        var orientation = await RunRequiredReadAsync(
            commandBuilder.BuildOrientation(adbPath, entry.Serial), entry.Serial, "orientation", cancellationToken).ConfigureAwait(false);

        var parsedSize = AndroidAdbMetadataParser.ParseSize(size);
        var sdkText = parsedProperties.GetValueOrDefault("ro.build.version.sdk");
        return new AndroidAdbDevice(
            entry.Serial,
            NullIfBlank(parsedProperties.GetValueOrDefault("ro.product.manufacturer")),
            NullIfBlank(parsedProperties.GetValueOrDefault("ro.product.model")) ?? entry.Model,
            NullIfBlank(parsedProperties.GetValueOrDefault("ro.build.version.release")),
            AndroidAdbMetadataParser.ParseInteger(sdkText),
            parsedSize?.Width,
            parsedSize?.Height,
            AndroidAdbMetadataParser.ParseDensity(density),
            AndroidAdbMetadataParser.ParseInteger(orientation),
            AndroidConnectionState.Device,
            entry.Product,
            entry.Device,
            Classification: classification);
    }

    private async Task<string> RunRequiredReadAsync(
        MemuCommand command,
        string serial,
        string category,
        CancellationToken cancellationToken)
    {
        var result = await RunAsync(command, DiscoveryTimeout, null, $"AndroidDiscovery:{category}", cancellationToken)
            .ConfigureAwait(false);
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"Không thể đọc metadata Android '{serial}' ({category}, exit code {result.ExitCode}): {result.StandardError.Trim()}");
        return result.StandardOutput;
    }

    private Task<ProcessResult> RunAsync(
        MemuCommand command,
        TimeSpan timeout,
        int? diagnosticIndex,
        string category,
        CancellationToken cancellationToken) =>
        processRunner.RunAsync(
            new ProcessRequest(
                command.ExecutablePath,
                command.Arguments,
                timeout,
                ProcessCancellationPolicy.WaitForNaturalExit,
                ProcessTimeoutPolicy.DirectProcessOnly,
                new ProcessDiagnosticContext(diagnosticIndex, category)),
            cancellationToken);

    private static AndroidAdbDevice CreateUnavailableDevice(
        AdbDeviceListEntry entry,
        AndroidTargetClassification classification) => new(
        entry.Serial,
        null,
        entry.Model,
        null,
        null,
        null,
        null,
        null,
        null,
        entry.State,
        entry.Product,
        entry.Device,
        Classification: classification);

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string CompactDiagnostic(string value)
    {
        var normalized = string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (normalized.Length == 0) normalized = "Không thể đọc đầy đủ metadata Android.";
        return normalized.Length <= 240 ? normalized : $"{normalized[..239]}…";
    }
}
