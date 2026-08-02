using System.Text.Json;
using MEmuScriptStudio.Core.MEmu;
using MEmuScriptStudio.Core.Models;

namespace MEmuScriptStudio.Infrastructure.Persistence;

public sealed class JsonSettingsStore : ISettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };
    private readonly string settingsPath;

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
        if (!File.Exists(settingsPath)) return new ApplicationSettings();

        await using var stream = File.OpenRead(settingsPath);
        return await JsonSerializer.DeserializeAsync<ApplicationSettings>(stream, SerializerOptions, cancellationToken).ConfigureAwait(false)
            ?? new ApplicationSettings();
    }

    public async Task SaveAsync(ApplicationSettings settings, CancellationToken cancellationToken)
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
}
