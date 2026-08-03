using System.Text.Json;
using MEmuScriptStudio.Core.MEmu;

namespace MEmuScriptStudio.Infrastructure.Persistence;

public sealed class JsonApplicationNameTransferService : IApplicationNameTransferService
{
    private const int SupportedSchemaVersion = 1;
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public async Task ExportAsync(
        string path,
        IReadOnlyDictionary<string, string> applicationNames,
        CancellationToken cancellationToken)
    {
        ValidatePath(path);
        ArgumentNullException.ThrowIfNull(applicationNames);
        if (applicationNames.Count == 0)
            throw new InvalidOperationException("Không có tên ứng dụng đã lưu để xuất.");

        var entries = applicationNames
            .Select(pair => CreateValidatedEntry(pair.Key, pair.Value))
            .OrderBy(entry => entry.PackageName, StringComparer.Ordinal)
            .ToList();
        var document = new ApplicationNameTransferDocument { Names = entries };
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

    public async Task<IReadOnlyDictionary<string, string>> ImportAsync(
        string path,
        CancellationToken cancellationToken)
    {
        ValidatePath(path);
        await using var stream = File.OpenRead(Path.GetFullPath(path));
        var document = await JsonSerializer.DeserializeAsync<ApplicationNameTransferDocument>(
            stream, SerializerOptions, cancellationToken).ConfigureAwait(false);
        if (document is null || document.SchemaVersion != SupportedSchemaVersion ||
            !string.Equals(document.Format, ApplicationNameTransferDocument.FormatName, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Định dạng hoặc phiên bản .memuappnames không được hỗ trợ.");
        }
        if (document.Names is null || document.Names.Count == 0)
            throw new InvalidDataException("File .memuappnames không chứa tên ứng dụng.");

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in document.Names)
        {
            if (entry is null)
                throw new InvalidDataException("File .memuappnames chứa mục tên ứng dụng không hợp lệ.");
            var validated = CreateValidatedEntry(entry.PackageName, entry.DisplayName);
            if (!result.TryAdd(validated.PackageName, validated.DisplayName))
                throw new InvalidDataException($"File .memuappnames chứa package trùng: {validated.PackageName}.");
        }

        return result;
    }

    private static ApplicationNameEntry CreateValidatedEntry(string? packageName, string? displayName)
    {
        if (string.IsNullOrWhiteSpace(packageName) || string.IsNullOrWhiteSpace(displayName))
            throw new InvalidDataException("Tên package và tên ứng dụng trong .memuappnames không được để trống.");
        return new ApplicationNameEntry
        {
            PackageName = packageName.Trim(),
            DisplayName = displayName.Trim()
        };
    }

    private static void ValidatePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!string.Equals(Path.GetExtension(path), ".memuappnames", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("File thư viện tên phải có phần mở rộng .memuappnames.", nameof(path));
    }

    public sealed class ApplicationNameTransferDocument
    {
        public const string FormatName = "MEmuScriptStudio.ApplicationNames";
        public int SchemaVersion { get; init; } = SupportedSchemaVersion;
        public string Format { get; init; } = FormatName;
        public List<ApplicationNameEntry>? Names { get; init; } = [];
    }

    public sealed class ApplicationNameEntry
    {
        public string PackageName { get; init; } = string.Empty;
        public string DisplayName { get; init; } = string.Empty;
    }
}
