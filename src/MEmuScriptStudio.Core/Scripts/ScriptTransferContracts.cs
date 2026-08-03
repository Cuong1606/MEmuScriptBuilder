using MEmuScriptStudio.Core.Models;

namespace MEmuScriptStudio.Core.Scripts;

public interface IScriptTransferService
{
    Task ExportAsync(string path, IReadOnlyCollection<ScriptDefinition> scripts, CancellationToken cancellationToken);
    Task<IReadOnlyList<ScriptDefinition>> ImportAsync(string path, CancellationToken cancellationToken);
}

public enum ScriptImportConflictResolution
{
    CreateCopy,
    Overwrite,
    Skip
}
