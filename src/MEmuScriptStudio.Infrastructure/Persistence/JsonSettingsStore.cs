using System.Text.Json;
using MEmuScriptStudio.Core.MEmu;
using MEmuScriptStudio.Core.Models;

namespace MEmuScriptStudio.Infrastructure.Persistence;

public sealed class JsonSettingsStore : ISettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };
    private readonly string settingsPath;
    private readonly SemaphoreSlim mutationGate = new(1, 1);

    public JsonSettingsStore() : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MEmuScriptStudio",
            "settings.json")) { }

    public JsonSettingsStore(string settingsPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsPath);
        this.settingsPath = Path.GetFullPath(settingsPath);
    }

    public async Task<ApplicationSettings> LoadAsync(CancellationToken cancellationToken)
    {
        return await LoadCoreAsync(cancellationToken).ConfigureAwait(false);
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
        if (!File.Exists(settingsPath)) return new ApplicationSettings();

        await using var stream = File.OpenRead(settingsPath);
        var settings = await JsonSerializer.DeserializeAsync<ApplicationSettings>(stream, SerializerOptions, cancellationToken).ConfigureAwait(false)
            ?? new ApplicationSettings();
        return settings.SchemaVersion >= ApplicationSettings.CurrentSchemaVersion
            ? settings
            : Upgrade(settings);
    }

    private async Task SaveCoreAsync(ApplicationSettings settings, CancellationToken cancellationToken)
    {
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
        }
        finally
        {
            try { File.Delete(temporaryPath); }
            catch (Exception) { }
        }
    }

    private static ApplicationSettings Upgrade(ApplicationSettings settings)
    {
        var upgraded = new ApplicationSettings
        {
            MemucPath = settings.MemucPath,
            MultiInstanceRun = settings.MultiInstanceRun,
            WindowLayout = settings.WindowLayout
        };
        foreach (var pair in settings.ApplicationDisplayNames)
            upgraded.ApplicationDisplayNames[pair.Key] = pair.Value;
        return upgraded;
    }
}
