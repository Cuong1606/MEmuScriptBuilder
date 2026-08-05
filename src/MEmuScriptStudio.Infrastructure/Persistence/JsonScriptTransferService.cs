using System.Text.Json;
using MEmuScriptStudio.Core.Models;
using MEmuScriptStudio.Core.Scripts;

namespace MEmuScriptStudio.Infrastructure.Persistence;

public sealed class JsonScriptTransferService : IScriptTransferService
{
    private const int SupportedSchemaVersion = 1;
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public async Task ExportAsync(
        string path,
        IReadOnlyCollection<ScriptDefinition> scripts,
        CancellationToken cancellationToken)
    {
        ValidatePath(path);
        ArgumentNullException.ThrowIfNull(scripts);
        if (scripts.Count == 0) throw new InvalidOperationException("Không có kịch bản để xuất.");
        ScriptLibraryValidator.Validate(scripts);

        var safeScripts = DeepCopy(scripts);
        foreach (var variable in safeScripts.SelectMany(script => script.Variables).Where(variable => variable.IsSecret))
            variable.Value = null;

        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await JsonSerializer.SerializeAsync(stream, new ScriptTransferDocument { Scripts = safeScripts }, SerializerOptions, cancellationToken)
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

    public async Task<IReadOnlyList<ScriptDefinition>> ImportAsync(string path, CancellationToken cancellationToken)
    {
        ValidatePath(path);
        await using var stream = File.OpenRead(Path.GetFullPath(path));
        var document = await RetiredScriptStepJsonMigration.DeserializeAsync<ScriptTransferDocument>(stream, SerializerOptions, cancellationToken)
            .ConfigureAwait(false);
        if (document is null || document.SchemaVersion != SupportedSchemaVersion ||
            !string.Equals(document.Format, ScriptTransferDocument.FormatName, StringComparison.Ordinal))
            throw new InvalidDataException("Định dạng hoặc phiên bản .memuscript không được hỗ trợ.");
        if (document.Scripts.Count == 0) throw new InvalidDataException("File .memuscript không chứa kịch bản.");
        if (document.Scripts.Any(script => script.SchemaVersion != SupportedSchemaVersion))
            throw new InvalidDataException("Một kịch bản dùng schema version không được hỗ trợ.");
        if (document.Scripts.Select(script => script.Id).Distinct().Count() != document.Scripts.Count)
            throw new InvalidDataException("File .memuscript chứa ID kịch bản trùng nhau.");
        ScriptLibraryValidator.Validate(document.Scripts);
        foreach (var variable in document.Scripts.SelectMany(script => script.Variables).Where(variable => variable.IsSecret))
            variable.Value = null;
        return document.Scripts;
    }

    private static List<ScriptDefinition> DeepCopy(IReadOnlyCollection<ScriptDefinition> scripts)
    {
        var json = JsonSerializer.Serialize(scripts, SerializerOptions);
        return JsonSerializer.Deserialize<List<ScriptDefinition>>(json, SerializerOptions)
            ?? throw new InvalidDataException("Không thể chuẩn bị dữ liệu kịch bản để xuất.");
    }

    private static void ValidatePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!string.Equals(Path.GetExtension(path), ".memuscript", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("File kịch bản phải có phần mở rộng .memuscript.", nameof(path));
    }

    public sealed class ScriptTransferDocument
    {
        public const string FormatName = "MEmuScriptStudio.ScriptTransfer";
        public int SchemaVersion { get; init; } = SupportedSchemaVersion;
        public string Format { get; init; } = FormatName;
        public List<ScriptDefinition> Scripts { get; init; } = [];
    }
}
