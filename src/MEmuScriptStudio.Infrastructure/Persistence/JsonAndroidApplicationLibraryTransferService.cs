using System.Text.Json;
using System.Text.RegularExpressions;
using MEmuScriptStudio.Core.Android;

namespace MEmuScriptStudio.Infrastructure.Persistence;

public sealed partial class JsonAndroidApplicationLibraryTransferService
    : IAndroidApplicationLibraryTransferService
{
    private const int SupportedSchemaVersion = 1;
    private const string SupportedProvider = "AndroidAdb";
    private const string SupportedExtension = ".androidappnames";
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public async Task ExportAsync(
        string path,
        IReadOnlyCollection<AndroidApplicationLibraryEntry> entries,
        CancellationToken cancellationToken)
    {
        ValidatePath(path);
        ArgumentNullException.ThrowIfNull(entries);
        if (entries.Count == 0)
            throw new InvalidOperationException("Không có tên ứng dụng Android đã lưu để xuất.");

        var validated = entries.Select(ValidateEntry)
            .OrderBy(entry => entry.PackageName, StringComparer.Ordinal)
            .ThenBy(entry => entry.ActivityName, StringComparer.Ordinal)
            .ToList();
        if (validated.Select(entry => entry.PackageName).Distinct(StringComparer.Ordinal).Count() != validated.Count)
            throw new InvalidDataException("Thư viện Android chỉ cho phép một mục cho mỗi package.");

        var document = new AndroidApplicationLibraryDocument
        {
            SchemaVersion = SupportedSchemaVersion,
            Format = AndroidApplicationLibraryDocument.FormatName,
            Provider = SupportedProvider,
            Applications = validated
        };
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await JsonSerializer.SerializeAsync(stream, document, SerializerOptions, cancellationToken)
                    .ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            try { File.Delete(temporaryPath); }
            catch (Exception) { }
        }
    }

    public async Task<IReadOnlyList<AndroidApplicationLibraryEntry>> ImportAsync(
        string path,
        CancellationToken cancellationToken)
    {
        ValidatePath(path);
        await using var stream = File.OpenRead(Path.GetFullPath(path));
        var document = await JsonSerializer.DeserializeAsync<AndroidApplicationLibraryDocument>(
            stream, SerializerOptions, cancellationToken).ConfigureAwait(false);
        if (document is null || document.SchemaVersion != SupportedSchemaVersion ||
            !string.Equals(document.Format, AndroidApplicationLibraryDocument.FormatName, StringComparison.Ordinal) ||
            !string.Equals(document.Provider, SupportedProvider, StringComparison.Ordinal))
            throw new InvalidDataException("Định dạng, provider hoặc phiên bản .androidappnames không được hỗ trợ.");
        if (document.Applications is null || document.Applications.Count == 0)
            throw new InvalidDataException("File .androidappnames không chứa ứng dụng.");

        var result = document.Applications.Select(ValidateEntry).ToList();
        if (result.Select(entry => entry.PackageName).Distinct(StringComparer.Ordinal).Count() != result.Count)
            throw new InvalidDataException("File .androidappnames chứa package trùng.");
        return result;
    }

    private static AndroidApplicationLibraryEntry ValidateEntry(AndroidApplicationLibraryEntry? entry)
    {
        if (entry is null || entry.ActivityName is null || string.IsNullOrWhiteSpace(entry.PackageName) ||
            string.IsNullOrWhiteSpace(entry.FriendlyName))
            throw new InvalidDataException("Package, Activity và friendly name phải có trong .androidappnames.");
        var packageName = entry.PackageName.Trim();
        var activityName = entry.ActivityName?.Trim() ?? string.Empty;
        if (!PackagePattern().IsMatch(packageName) ||
            activityName.Length > 0 && !ActivityPattern().IsMatch(activityName))
            throw new InvalidDataException("Package hoặc Activity trong .androidappnames không hợp lệ.");
        return new AndroidApplicationLibraryEntry(packageName, activityName, entry.FriendlyName.Trim());
    }

    private static void ValidatePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!string.Equals(Path.GetExtension(path), SupportedExtension, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("File thư viện Android phải có phần mở rộng .androidappnames.", nameof(path));
    }

    public sealed class AndroidApplicationLibraryDocument
    {
        public const string FormatName = "MEmuScriptStudio.AndroidApplicationLibrary";
        public int SchemaVersion { get; init; }
        public string? Format { get; init; }
        public string? Provider { get; init; }
        public List<AndroidApplicationLibraryEntry>? Applications { get; init; }
    }

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_]*(?:\\.[A-Za-z0-9_]+)+$")]
    private static partial Regex PackagePattern();

    [GeneratedRegex("^\\.?[A-Za-z_$][A-Za-z0-9_.$]*$")]
    private static partial Regex ActivityPattern();
}
