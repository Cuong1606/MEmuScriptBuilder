namespace MEmuScriptStudio.Core.MEmu;

public interface IApplicationNameTransferService
{
    Task ExportAsync(
        string path,
        IReadOnlyDictionary<string, string> applicationNames,
        CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<string, string>> ImportAsync(
        string path,
        CancellationToken cancellationToken);
}

public enum ApplicationNameImportConflictResolution
{
    Overwrite,
    Skip,
    Cancel
}
