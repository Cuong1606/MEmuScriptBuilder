using MEmuScriptStudio.Core.Android;
using MEmuScriptStudio.Core.Models;

namespace MEmuScriptStudio.Infrastructure.Android;

public sealed class AndroidTargetClassifier(
    ILocalAdbEndpointEvidenceProvider? endpointEvidenceProvider = null) : IAndroidTargetClassifier
{
    private readonly ILocalAdbEndpointEvidenceProvider endpointEvidenceProvider =
        endpointEvidenceProvider ?? new WindowsLocalAdbEndpointEvidenceProvider();

    private static readonly HashSet<string> ProductIdentityProperties = new(StringComparer.Ordinal)
    {
        "ro.product.manufacturer",
        "ro.product.brand",
        "ro.product.model",
        "ro.product.name",
        "ro.product.device",
        "ro.build.product",
        "ro.product.system.manufacturer",
        "ro.product.system.brand",
        "ro.product.system.model",
        "ro.product.system.name",
        "ro.product.system.device",
        "ro.product.vendor.manufacturer",
        "ro.product.vendor.brand",
        "ro.product.vendor.model",
        "ro.product.vendor.name",
        "ro.product.vendor.device"
    };

    public AndroidTargetClassification Classify(
        string serial,
        IReadOnlyDictionary<string, string>? properties = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serial);

        if (endpointEvidenceProvider.Inspect(serial).IsMemuOwned)
            return AndroidTargetClassification.MEmuBackedAdb;

        if (properties is null || properties.Count == 0)
            return AndroidTargetClassification.Unknown;

        if (properties.Any(pair =>
                ProductIdentityProperties.Contains(pair.Key) && IsMemuIdentity(pair.Value)))
            return AndroidTargetClassification.MEmuBackedAdb;

        return IsAmbiguousEndpoint(serial)
            ? AndroidTargetClassification.Unknown
            : AndroidTargetClassification.ExternalAndroid;
    }

    private static bool IsMemuIdentity(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var normalized = value.Trim();
        return normalized.Equals("Microvirt", StringComparison.OrdinalIgnoreCase) ||
               normalized.StartsWith("Microvirt ", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("MEmu", StringComparison.OrdinalIgnoreCase) ||
               normalized.StartsWith("MEmu ", StringComparison.OrdinalIgnoreCase) ||
               normalized.StartsWith("MEmu_", StringComparison.OrdinalIgnoreCase) ||
               normalized.StartsWith("MEmu-", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAmbiguousEndpoint(string serial) =>
        serial.Trim().StartsWith("emulator-", StringComparison.OrdinalIgnoreCase) ||
        serial.Contains(':', StringComparison.Ordinal);
}
