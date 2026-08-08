using System.Text.Json;

namespace MEmuScriptStudio.Infrastructure.Persistence;

internal static class JsonPersistenceSafety
{
    public static int ReadRequiredSchemaVersion(JsonElement root, string dataName)
    {
        if (root.ValueKind != JsonValueKind.Object ||
            !TryGetPropertyIgnoreCase(root, "SchemaVersion", out var versionElement) ||
            !versionElement.TryGetInt32(out var version))
        {
            throw new InvalidDataException($"{dataName} không có SchemaVersion hợp lệ.");
        }

        return version;
    }

    public static bool TryGetPropertyIgnoreCase(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            value = default;
            return false;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (!string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)) continue;
            value = property.Value;
            return true;
        }

        value = default;
        return false;
    }

    public static async Task<string> BackupCorruptFileAsync(
        string sourcePath,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(sourcePath)!;
        var backupPath = Path.Combine(
            directory,
            $"{Path.GetFileName(sourcePath)}.corrupt-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}.bak");

        await using var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        await using var backup = new FileStream(backupPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await source.CopyToAsync(backup, cancellationToken).ConfigureAwait(false);
        await backup.FlushAsync(cancellationToken).ConfigureAwait(false);
        return backupPath;
    }
}
