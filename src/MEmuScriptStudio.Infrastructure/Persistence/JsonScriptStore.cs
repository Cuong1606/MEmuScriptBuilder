using System.Text.Json;
using MEmuScriptStudio.Core.Models;
using MEmuScriptStudio.Core.Scripts;

namespace MEmuScriptStudio.Infrastructure.Persistence;

public sealed class JsonScriptStore : IScriptStore, IDisposable
{
    private const int CurrentSchemaVersion = 1;
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };
    private readonly string scriptsPath;
    private readonly SemaphoreSlim saveGate = new(1, 1);
    private string? writeBlockedReason;
    private string? recoveryBackupPath;
    private bool existingFileValidated;

    public JsonScriptStore() : this(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MEmuScriptStudio",
        "scripts.json")) { }

    public JsonScriptStore(string scriptsPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scriptsPath);
        this.scriptsPath = Path.GetFullPath(scriptsPath);
    }

    public bool IsWriteBlocked => writeBlockedReason is not null;
    public bool IsRecoveryRequired => recoveryBackupPath is not null;
    public string? RecoveryBackupPath => recoveryBackupPath;

    public async Task<IReadOnlyList<ScriptDefinition>> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(scriptsPath)) return [];
        if (writeBlockedReason is not null)
            throw CreateBlockedException();

        try
        {
            await using var stream = File.OpenRead(scriptsPath);
            var document = await ReadValidatedDocumentAsync(stream, cancellationToken).ConfigureAwait(false);
            existingFileValidated = true;
            return document.Scripts;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (InvalidDataException exception) when (writeBlockedReason is not null)
        {
            throw new InvalidDataException(writeBlockedReason, exception);
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException or InvalidDataException)
        {
            await BlockForRecoveryAsync(exception, cancellationToken).ConfigureAwait(false);
            throw CreateBlockedException(exception);
        }
    }

    public async Task SaveAsync(IReadOnlyCollection<ScriptDefinition> scripts, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scripts);
        ScriptLibraryValidator.Validate(scripts);
        await saveGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureExistingFileIsWritableAsync(cancellationToken).ConfigureAwait(false);
            var directory = Path.GetDirectoryName(scriptsPath)!;
            Directory.CreateDirectory(directory);
            var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(scriptsPath)}.{Guid.NewGuid():N}.tmp");
            try
            {
                await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    await JsonSerializer.SerializeAsync(
                        stream,
                        new ScriptCollectionDocument { Scripts = scripts.ToList() },
                        SerializerOptions,
                        cancellationToken).ConfigureAwait(false);
                    await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                }

                File.Move(temporaryPath, scriptsPath, overwrite: true);
                existingFileValidated = true;
            }
            finally
            {
                try { File.Delete(temporaryPath); }
                catch (Exception) { }
            }
        }
        finally
        {
            saveGate.Release();
        }
    }

    public async Task RecoverAsync(CancellationToken cancellationToken)
    {
        await saveGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (recoveryBackupPath is null)
                throw new InvalidOperationException("Không có dữ liệu kịch bản lỗi đang chờ phục hồi.");

            writeBlockedReason = null;
            existingFileValidated = true;
            try
            {
                await SaveCoreAsync([], cancellationToken).ConfigureAwait(false);
                recoveryBackupPath = null;
            }
            catch
            {
                writeBlockedReason = "Kho kịch bản vẫn bị khóa vì thao tác phục hồi chưa hoàn tất.";
                throw;
            }
        }
        finally { saveGate.Release(); }
    }

    private async Task EnsureExistingFileIsWritableAsync(CancellationToken cancellationToken)
    {
        if (writeBlockedReason is not null) throw CreateBlockedException();
        if (existingFileValidated || !File.Exists(scriptsPath)) return;

        try
        {
            await using var stream = File.OpenRead(scriptsPath);
            await ReadValidatedDocumentAsync(stream, cancellationToken).ConfigureAwait(false);
            existingFileValidated = true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (InvalidDataException exception) when (writeBlockedReason is not null)
        {
            throw new InvalidDataException(writeBlockedReason, exception);
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException or InvalidDataException)
        {
            await BlockForRecoveryAsync(exception, cancellationToken).ConfigureAwait(false);
            throw CreateBlockedException(exception);
        }
    }

    private async Task ValidateSchemaAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        var documentVersion = JsonPersistenceSafety.ReadRequiredSchemaVersion(json.RootElement, "Tài liệu kịch bản");
        if (documentVersion > CurrentSchemaVersion)
        {
            writeBlockedReason = $"Tài liệu kịch bản dùng schema {documentVersion}, mới hơn schema {CurrentSchemaVersion} mà ứng dụng hỗ trợ. Dữ liệu gốc không bị ghi đè.";
            throw new InvalidDataException(writeBlockedReason);
        }
        if (documentVersion != CurrentSchemaVersion)
        {
            writeBlockedReason = $"Tài liệu kịch bản dùng schema {documentVersion} nhưng không có migration hợp lệ đến schema {CurrentSchemaVersion}. Dữ liệu gốc không bị ghi đè.";
            throw new InvalidDataException(writeBlockedReason);
        }

        if (!JsonPersistenceSafety.TryGetPropertyIgnoreCase(json.RootElement, "Scripts", out var scripts) ||
            scripts.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Tài liệu kịch bản không có danh sách Scripts hợp lệ.");

        foreach (var script in scripts.EnumerateArray())
        {
            if (script.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("Danh sách Scripts chứa phần tử không phải object hợp lệ.");
            var scriptVersion = JsonPersistenceSafety.TryGetPropertyIgnoreCase(script, "SchemaVersion", out var versionElement) &&
                                versionElement.TryGetInt32(out var explicitVersion)
                ? explicitVersion
                : CurrentSchemaVersion;
            if (scriptVersion > CurrentSchemaVersion)
            {
                writeBlockedReason = $"Một kịch bản dùng schema {scriptVersion}, mới hơn schema {CurrentSchemaVersion} mà ứng dụng hỗ trợ. Dữ liệu gốc không bị ghi đè.";
                throw new InvalidDataException(writeBlockedReason);
            }
            if (scriptVersion != CurrentSchemaVersion)
            {
                writeBlockedReason = $"Một kịch bản dùng schema {scriptVersion} nhưng không có migration hợp lệ đến schema {CurrentSchemaVersion}. Dữ liệu gốc không bị ghi đè.";
                throw new InvalidDataException(writeBlockedReason);
            }
        }
    }

    private async Task<ScriptCollectionDocument> ReadValidatedDocumentAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        await ValidateSchemaAsync(stream, cancellationToken).ConfigureAwait(false);
        stream.Position = 0;
        var document = await RetiredScriptStepJsonMigration.DeserializeAsync<ScriptCollectionDocument>(
            stream,
            SerializerOptions,
            cancellationToken).ConfigureAwait(false);
        if (document is null || document.Scripts is null)
            throw new InvalidDataException("Tài liệu kịch bản trống hoặc không hợp lệ.");
        ScriptLibraryValidator.Validate(document.Scripts);
        return document;
    }

    private async Task BlockForRecoveryAsync(Exception exception, CancellationToken cancellationToken)
    {
        if (recoveryBackupPath is null)
            recoveryBackupPath = await JsonPersistenceSafety.BackupCorruptFileAsync(scriptsPath, cancellationToken).ConfigureAwait(false);
        writeBlockedReason = $"Dữ liệu kịch bản bị lỗi và đã được sao lưu tại '{recoveryBackupPath}'. Cần xác nhận phục hồi trước khi thay đổi thư viện.";
    }

    private Exception CreateBlockedException(Exception? innerException = null) => recoveryBackupPath is not null
        ? new ScriptDataRecoveryRequiredException(writeBlockedReason!, recoveryBackupPath, innerException)
        : new InvalidDataException(writeBlockedReason ?? "Kho kịch bản đang bị khóa để bảo vệ dữ liệu gốc.", innerException);

    private async Task SaveCoreAsync(
        IReadOnlyCollection<ScriptDefinition> scripts,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(scriptsPath)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(scriptsPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    new ScriptCollectionDocument { Scripts = scripts.ToList() },
                    SerializerOptions,
                    cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            File.Move(temporaryPath, scriptsPath, overwrite: true);
        }
        finally
        {
            try { File.Delete(temporaryPath); }
            catch (Exception) { }
        }
    }

    public void Dispose() => saveGate.Dispose();

    public sealed class ScriptCollectionDocument
    {
        public int SchemaVersion { get; init; } = CurrentSchemaVersion;
        public List<ScriptDefinition> Scripts { get; init; } = [];
    }
}
