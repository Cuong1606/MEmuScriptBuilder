using System.Text.Json;
using MEmuScriptStudio.Core.MEmu;
using MEmuScriptStudio.Core.Models;

namespace MEmuScriptStudio.Infrastructure.Persistence;

public sealed class JsonSettingsStore : ISettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };
    private readonly string settingsPath;
    private readonly SemaphoreSlim mutationGate = new(1, 1);
    private bool existingFileValidated;
    private string? writeBlockedReason;
    private ApplicationSettings? pendingRecoveredSettings;

    public JsonSettingsStore() : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MEmuScriptStudio",
            "settings.json")) { }

    public JsonSettingsStore(string settingsPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsPath);
        this.settingsPath = Path.GetFullPath(settingsPath);
    }

    public string? RecoveryNotice { get; private set; }

    public async Task<ApplicationSettings> LoadAsync(CancellationToken cancellationToken)
    {
        await mutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { return await LoadCoreAsync(cancellationToken).ConfigureAwait(false); }
        finally { mutationGate.Release(); }
    }

    public async Task SaveAsync(ApplicationSettings settings, CancellationToken cancellationToken)
    {
        await mutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { await SaveCoreAsync(settings, cancellationToken).ConfigureAwait(false); }
        finally { mutationGate.Release(); }
    }

    public async Task<ApplicationSettings> UpdateAsync(
        Action<ApplicationSettings> update,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(update);
        await mutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var settings = await LoadCoreAsync(cancellationToken).ConfigureAwait(false);
            update(settings);
            await SaveCoreAsync(settings, cancellationToken).ConfigureAwait(false);
            return settings;
        }
        finally { mutationGate.Release(); }
    }

    private async Task<ApplicationSettings> LoadCoreAsync(CancellationToken cancellationToken)
    {
        if (writeBlockedReason is not null) throw new InvalidDataException(writeBlockedReason);
        if (pendingRecoveredSettings is not null) return pendingRecoveredSettings;
        if (!File.Exists(settingsPath)) return new ApplicationSettings();

        try
        {
            await using var stream = File.OpenRead(settingsPath);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            var version = JsonPersistenceSafety.ReadRequiredSchemaVersion(json.RootElement, "Cấu hình");
            ValidateSupportedVersion(version);
            var settings = DeserializeValidatedSettings(json.RootElement);
            settings.ControlCenterLayout ??= new ControlCenterLayoutSettings();
            existingFileValidated = true;
            return version == ApplicationSettings.CurrentSchemaVersion
                ? settings
                : Upgrade(settings);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (InvalidDataException) when (writeBlockedReason is not null) { throw; }
        catch (Exception exception) when (exception is JsonException or NotSupportedException or InvalidDataException)
        {
            var backupPath = await JsonPersistenceSafety.BackupCorruptFileAsync(settingsPath, cancellationToken).ConfigureAwait(false);
            RecoveryNotice = $"Cấu hình bị lỗi đã được sao lưu tại '{backupPath}'. Ứng dụng đang dùng cấu hình mặc định có thể lưu lại bình thường.";
            pendingRecoveredSettings = new ApplicationSettings();
            existingFileValidated = true;
            return pendingRecoveredSettings;
        }
    }

    private async Task SaveCoreAsync(ApplicationSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        await EnsureExistingFileIsWritableAsync(cancellationToken).ConfigureAwait(false);
        var directory = Path.GetDirectoryName(settingsPath)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(settingsPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await JsonSerializer.SerializeAsync(stream, settings, SerializerOptions, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporaryPath, settingsPath, overwrite: true);
            existingFileValidated = true;
            pendingRecoveredSettings = null;
        }
        finally
        {
            try { File.Delete(temporaryPath); }
            catch (Exception) { }
        }
    }

    private async Task EnsureExistingFileIsWritableAsync(CancellationToken cancellationToken)
    {
        if (writeBlockedReason is not null) throw new InvalidDataException(writeBlockedReason);
        if (existingFileValidated || !File.Exists(settingsPath)) return;

        try
        {
            await using var stream = File.OpenRead(settingsPath);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            var version = JsonPersistenceSafety.ReadRequiredSchemaVersion(json.RootElement, "Cấu hình");
            ValidateSupportedVersion(version);
            _ = DeserializeValidatedSettings(json.RootElement);
            existingFileValidated = true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (InvalidDataException) when (writeBlockedReason is not null) { throw; }
        catch (Exception exception) when (exception is JsonException or NotSupportedException or InvalidDataException)
        {
            var backupPath = await JsonPersistenceSafety.BackupCorruptFileAsync(settingsPath, cancellationToken).ConfigureAwait(false);
            RecoveryNotice = $"Cấu hình bị lỗi đã được sao lưu tại '{backupPath}'.";
            pendingRecoveredSettings = new ApplicationSettings();
            existingFileValidated = true;
        }
    }

    private void ValidateSupportedVersion(int version)
    {
        if (version > ApplicationSettings.CurrentSchemaVersion)
        {
            writeBlockedReason = $"Cấu hình dùng schema {version}, mới hơn schema {ApplicationSettings.CurrentSchemaVersion} mà ứng dụng hỗ trợ. Dữ liệu gốc không bị ghi đè.";
            throw new InvalidDataException(writeBlockedReason);
        }

        if (version < 1)
        {
            writeBlockedReason = $"Cấu hình dùng schema {version} nhưng không có migration hợp lệ đến schema {ApplicationSettings.CurrentSchemaVersion}. Dữ liệu gốc không bị ghi đè.";
            throw new InvalidDataException(writeBlockedReason);
        }
    }

    private static ApplicationSettings DeserializeValidatedSettings(JsonElement root)
    {
        var settings = root.Deserialize<ApplicationSettings>(SerializerOptions)
            ?? throw new InvalidDataException("Cấu hình trống hoặc không hợp lệ.");
        if (settings.ApplicationDisplayNames is null || settings.MultiInstanceRun is null)
            throw new InvalidDataException("Cấu hình thiếu nhóm dữ liệu bắt buộc.");
        return settings;
    }

    private static ApplicationSettings Upgrade(ApplicationSettings settings)
    {
        var upgraded = new ApplicationSettings
        {
            MemucPath = settings.MemucPath,
            MultiInstanceRun = settings.MultiInstanceRun,
            ControlCenterLayout = ControlCenterLayoutSettings.Normalize(settings.ControlCenterLayout)
        };
        foreach (var pair in settings.ApplicationDisplayNames)
            upgraded.ApplicationDisplayNames[pair.Key] = pair.Value;
        return upgraded;
    }
}
