using System.Text.Json;
using MEmuScriptStudio.Core.Models;
using MEmuScriptStudio.Core.Scripts;

namespace MEmuScriptStudio.Infrastructure.Persistence;

public sealed class JsonScriptStore : IScriptStore, IDisposable
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };
    private readonly string scriptsPath;
    private readonly SemaphoreSlim saveGate = new(1, 1);

    public JsonScriptStore() : this(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MEmuScriptStudio",
        "scripts.json")) { }

    public JsonScriptStore(string scriptsPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scriptsPath);
        this.scriptsPath = Path.GetFullPath(scriptsPath);
    }

    public async Task<IReadOnlyList<ScriptDefinition>> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(scriptsPath)) return [];
        await using var stream = File.OpenRead(scriptsPath);
        var document = await JsonSerializer.DeserializeAsync<ScriptCollectionDocument>(
            stream,
            SerializerOptions,
            cancellationToken).ConfigureAwait(false);

        if (document is null || document.SchemaVersion != 1)
        {
            throw new InvalidDataException("Phiên bản dữ liệu kịch bản không được hỗ trợ.");
        }

        return document.Scripts;
    }

    public async Task SaveAsync(IReadOnlyCollection<ScriptDefinition> scripts, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scripts);
        await saveGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
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
        finally
        {
            saveGate.Release();
        }
    }

    public void Dispose() => saveGate.Dispose();

    public sealed class ScriptCollectionDocument
    {
        public int SchemaVersion { get; init; } = 1;
        public List<ScriptDefinition> Scripts { get; init; } = [];
    }
}
