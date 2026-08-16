using System.Collections.Frozen;
using System.Collections.ObjectModel;
using System.IO;
using System.Runtime.CompilerServices;
using MEmuScriptStudio.App.Services;
using MEmuScriptStudio.Core.Android;
using MEmuScriptStudio.Core.Execution;
using MEmuScriptStudio.Core.Formatting;
using MEmuScriptStudio.Core.MEmu;
using MEmuScriptStudio.Core.Models;
using MEmuScriptStudio.Core.Scripts;
using LaunchSpacingModeValue = MEmuScriptStudio.Core.Models.LaunchSpacingMode;
using ScriptAssignmentModeValue = MEmuScriptStudio.Core.Models.ScriptAssignmentMode;

namespace MEmuScriptStudio.App.ViewModels;

public sealed partial class MainViewModel
{
    public event Action<IReadOnlyList<ScriptItemViewModel>, bool>? ScriptSelectionRestoreRequested;

    private readonly IScriptStore scriptStore;
    private readonly IScriptTransferService? scriptTransferService;
    private readonly IScriptImportConflictService? scriptImportConflictService;
    private readonly SemaphoreSlim scriptSaveGate = new(1, 1);
    private string scriptName = string.Empty;
    private bool isScriptPersistenceBlocked;
    private readonly List<ScriptItemViewModel> selectedScripts = [];
    private bool synchronizingSelectedScripts;

    public IReadOnlyList<ScriptItemViewModel> SelectedScripts => selectedScripts;
    public int SelectedScriptCount => selectedScripts.Count;
    public bool HasMultipleSelectedScripts => SelectedScriptCount > 1;
    public string ScriptLibrarySelectionSummary => HasMultipleSelectedScripts
        ? $"Đã chọn: {SelectedScriptCount}"
        : string.Empty;

    private async Task CreateScriptAsync()
    {
        if (!await ResolvePendingEditorChangesAsync()) return;
        var transaction = CaptureLibraryMutationTransaction();
        var script = new ScriptDefinition { Name = $"Kịch bản {Scripts.Count + 1}" };
        var item = new ScriptItemViewModel(script);
        Scripts.Add(item);
        SelectedScript = item;
        await SaveScriptsWithRollbackAsync(transaction);
        RefreshScriptCollections();
        EnsureScriptVisible(item);
        RequestScriptSelectionRestore(focus: true);
    }

    private async Task RenameScriptAsync()
    {
        if (SelectedScript is null) return;
        ArgumentException.ThrowIfNullOrWhiteSpace(ScriptName);
        var target = SelectedScript;
        var draftAtSaveStart = ScriptName;
        var savedName = ScriptName.Trim();
        var previousName = target.Model.Name;
        var previousUpdatedAt = target.Model.UpdatedAt;
        using var persistence = BeginEditorPersistence();
        target.Model.Name = savedName;
        TouchSelectedScript();
        try { await SaveScriptsAsync(); }
        catch
        {
            target.Model.Name = previousName;
            target.Model.UpdatedAt = previousUpdatedAt;
            target.Refresh();
            throw;
        }
        if (ReferenceEquals(target, SelectedScript))
        {
            scriptNameBaseline = savedName;
            if (string.Equals(ScriptName, draftAtSaveStart, StringComparison.Ordinal))
                SetScriptNameFromModel(savedName);
            else
            {
                OnPropertyChanged(nameof(IsScriptNameDirty));
                OnPropertyChanged(nameof(CanRenameScript));
                OnPropertyChanged(nameof(HasAnyEditorDraft));
                OnPropertyChanged(nameof(EditorSaveState));
                RaiseCommandStates();
            }
        }
        RefreshScriptCollections();
        RefreshAssignedScriptLabels();
        await PersistAssignmentsAsync();
    }

    private async Task DuplicateScriptAsync()
    {
        var source = GetSelectedScriptsForMutation();
        if (source.Count == 0) return;
        if (!await ResolvePendingEditorChangesAsync()) return;
        var transaction = CaptureLibraryMutationTransaction();
        var insertionIndex = source.Select(Scripts.IndexOf).Max() + 1;
        var clones = source.Select(item => new ScriptItemViewModel(ScriptCloner.Clone(item.Model))).ToList();
        for (var index = 0; index < clones.Count; index++)
            Scripts.Insert(insertionIndex + index, clones[index]);
        RefreshScriptCollections();
        await SaveScriptsWithRollbackAsync(transaction);
        SetScriptSelection(clones, clones[^1], focus: true);
        StatusMessage = $"Đã nhân bản {clones.Count} kịch bản.";
    }

    private async Task DeleteScriptAsync()
    {
        var scriptsToDelete = GetSelectedScriptsForMutation();
        if (scriptsToDelete.Count == 0) return;
        var deletedIds = scriptsToDelete.Select(item => item.Id).ToHashSet();
        var usedBy = Scripts.Where(candidate => !deletedIds.Contains(candidate.Id) &&
            candidate.Model.Kind == ScriptKind.Composite &&
            candidate.Model.CompositeItems.OfType<ScriptReferenceItem>().Any(reference => deletedIds.Contains(reference.ScriptId)))
            .Select(candidate => candidate.Name).ToList();
        if (usedBy.Count > 0)
        {
            StatusMessage = scriptsToDelete.Count == 1
                ? $"Không thể xóa '{scriptsToDelete[0].Name}' vì đang được dùng bởi: {string.Join(", ", usedBy)}."
                : $"Không thể xóa các kịch bản đã chọn vì đang được dùng bởi: {string.Join(", ", usedBy)}.";
            return;
        }
        var confirmationMessage = scriptsToDelete.Count == 1
            ? $"Xóa kịch bản '{scriptsToDelete[0].Name}'?"
            : $"Xóa {scriptsToDelete.Count} kịch bản đã chọn?";
        if (!confirmationService.Confirm(confirmationMessage, "Xác nhận xóa")) return;
        if (!await ResolvePendingEditorChangesAsync()) return;
        var transaction = CaptureLibraryMutationTransaction();
        var previousPrimary = SelectedScript;
        var index = scriptsToDelete.Select(Scripts.IndexOf).Where(value => value >= 0).DefaultIfEmpty(0).Min();
        foreach (var script in scriptsToDelete)
        {
            Scripts.Remove(script);
            ClearAssignmentsForScript(script.Id);
            stepHistories.Remove(script.Id);
            compositeHistories.Remove(script.Id);
        }
        SelectedScript = previousPrimary is not null && !deletedIds.Contains(previousPrimary.Id) && Scripts.Contains(previousPrimary)
            ? previousPrimary
            : Scripts.Count == 0 ? null : Scripts[Math.Min(index, Scripts.Count - 1)];
        if (CommonRunScript is not null && deletedIds.Contains(CommonRunScript.Id))
        {
            commonRunScript = SelectedScript;
            configuredCommonScriptId = SelectedScript?.Id;
            OnPropertyChanged(nameof(CommonRunScript));
            UpdateRunConfigurationState();
        }
        if (ControlCenterSelectedScript is not null && deletedIds.Contains(ControlCenterSelectedScript.Id))
            ControlCenterSelectedScript = Scripts.FirstOrDefault();
        await SaveScriptsWithRollbackAsync(transaction);
        await PersistAssignmentsAsync();
        RefreshScriptCollections();
        RequestScriptSelectionRestore(focus: true);
        StatusMessage = $"Đã xóa {scriptsToDelete.Count} kịch bản.";
    }

    private async Task ExportSelectedScriptAsync()
    {
        if (scriptTransferService is null || SelectedScript is null) return;
        if (!await ResolvePendingEditorChangesAsync()) return;
        var path = fileDialogService.SelectScriptExportPath(ToSafeFileName(SelectedScript.Name));
        if (path is null) return;
        var closure = ScriptLibraryValidator.BuildExportClosure(
            [SelectedScript.Model], Scripts.Select(item => item.Model).ToList());
        await scriptTransferService.ExportAsync(path, closure, CancellationToken.None);
        StatusMessage = $"Đã xuất kịch bản '{SelectedScript.Name}'.";
    }

    private async Task ExportAllScriptsAsync()
    {
        if (scriptTransferService is null || Scripts.Count == 0) return;
        if (!await ResolvePendingEditorChangesAsync()) return;
        var path = fileDialogService.SelectScriptExportPath("thu-vien-kich-ban");
        if (path is null) return;
        await scriptTransferService.ExportAsync(path, Scripts.Select(item => item.Model).ToList(), CancellationToken.None);
        StatusMessage = $"Đã xuất {Scripts.Count} kịch bản.";
    }

    private async Task ImportScriptsAsync()
    {
        if (scriptTransferService is null || scriptImportConflictService is null) return;
        var path = fileDialogService.SelectScriptImportPath();
        if (path is null) return;
        var imported = await scriptTransferService.ImportAsync(path, CancellationToken.None);
        ScriptLibraryValidator.Validate(imported);
        foreach (var script in imported)
        foreach (var step in script.Steps)
            stepCommandBuilder.Validate(step);

        var plan = imported.Select(script =>
        {
            var existing = Scripts.FirstOrDefault(item => item.Id == script.Id);
            var resolution = existing is null
                ? (ScriptImportConflictResolution?)null
                : scriptImportConflictService.Resolve(script);
            return (Script: script, Existing: existing, Resolution: resolution);
        }).ToList();
        var skippedCount = plan.Count(item => item.Resolution == ScriptImportConflictResolution.Skip);
        if (plan.All(item => item.Resolution == ScriptImportConflictResolution.Skip))
        {
            StatusMessage = $"Đã nhập 0 kịch bản; bỏ qua {skippedCount}.";
            return;
        }
        var importCopies = plan
            .Where(item => item.Resolution == ScriptImportConflictResolution.CreateCopy)
            .ToDictionary(item => item.Script.Id, item => ScriptCloner.Clone(item.Script));
        var destinationIds = plan.ToDictionary(
            item => item.Script.Id,
            item => importCopies.TryGetValue(item.Script.Id, out var copy) ? copy.Id : item.Script.Id);
        foreach (var copy in importCopies.Values.Where(script => script.Kind == ScriptKind.Composite))
        foreach (var reference in copy.CompositeItems.OfType<ScriptReferenceItem>())
            reference.ScriptId = destinationIds[reference.ScriptId];

        var prospectiveLibrary = Scripts.Select(item => item.Model).ToList();
        foreach (var item in plan)
        {
            if (item.Existing is null)
            {
                prospectiveLibrary.Add(item.Script);
                continue;
            }
            if (item.Resolution == ScriptImportConflictResolution.CreateCopy)
                prospectiveLibrary.Add(importCopies[item.Script.Id]);
            else if (item.Resolution == ScriptImportConflictResolution.Overwrite)
                prospectiveLibrary[prospectiveLibrary.FindIndex(script => script.Id == item.Script.Id)] = item.Script;
        }
        ScriptLibraryValidator.Validate(prospectiveLibrary);
        foreach (var script in prospectiveLibrary)
        foreach (var step in script.Steps)
            stepCommandBuilder.Validate(step);
        if (!await ResolvePendingEditorChangesAsync()) return;
        var transaction = CaptureLibraryMutationTransaction();

        var importedCount = 0;
        ScriptItemViewModel? lastImported = null;
        foreach (var item in plan)
        {
            if (item.Existing is null)
            {
                lastImported = new ScriptItemViewModel(item.Script);
                Scripts.Add(lastImported);
                importedCount++;
                continue;
            }

            switch (item.Resolution)
            {
                case ScriptImportConflictResolution.CreateCopy:
                    lastImported = new ScriptItemViewModel(importCopies[item.Script.Id]);
                    Scripts.Add(lastImported);
                    importedCount++;
                    break;
                case ScriptImportConflictResolution.Overwrite:
                    var index = Scripts.IndexOf(item.Existing);
                    stepHistories.Remove(item.Existing.Id);
                    compositeHistories.Remove(item.Existing.Id);
                    lastImported = new ScriptItemViewModel(item.Script);
                    Scripts[index] = lastImported;
                    if (CommonRunScript?.Id == item.Existing.Id)
                    {
                        commonRunScript = lastImported;
                        configuredCommonScriptId = lastImported.Id;
                        OnPropertyChanged(nameof(CommonRunScript));
                        UpdateRunConfigurationState();
                    }
                    if (BulkAssignmentScript?.Id == item.Existing.Id)
                        BulkAssignmentScript = lastImported;
                    if (ControlCenterSelectedScript?.Id == item.Existing.Id)
                        ControlCenterSelectedScript = lastImported;
                    importedCount++;
                    break;
            }
        }

        if (importedCount > 0)
        {
            SelectedScript = lastImported;
            RefreshScriptCollections();
            await SaveScriptsWithRollbackAsync(transaction);
            RefreshAssignedScriptLabels();
            await PersistAssignmentsAsync();
            if (lastImported is not null) EnsureScriptVisible(lastImported);
            RequestScriptSelectionRestore(focus: true);
        }
        StatusMessage = $"Đã nhập {importedCount} kịch bản; bỏ qua {skippedCount}.";
        RaiseCommandStates();
    }

    private static string ToSafeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var sanitized = new string(value.Trim().Select(character => invalid.Contains(character) ? '-' : character).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "kich-ban" : sanitized;
    }

    private async void PersistCommonRunScriptSelection()
    {
        try
        {
            var scriptId = CommonRunScript?.Id;
            await UpdateApplicationSettingsAsync(
                settings => settings.MultiInstanceRun.CommonScriptId = scriptId,
                CancellationToken.None);
        }
        catch (Exception exception)
        {
            StatusMessage = $"Đã đổi kịch bản dùng chung trong phiên nhưng không thể lưu ({exception.Message}).";
        }
    }

    private async Task SaveScriptsAsync()
    {
        if (IsScriptPersistenceBlocked || scriptStore.IsWriteBlocked)
            throw new InvalidOperationException("Thư viện kịch bản đang bị khóa để bảo vệ dữ liệu gốc.");
        await scriptSaveGate.WaitAsync(CancellationToken.None);
        try
        {
            var snapshot = Scripts.Select(item => SnapshotScript(item.Model)).ToList();
            ScriptStepDisplayName.NormalizeDelayNames(snapshot);
            await scriptStore.SaveAsync(snapshot, CancellationToken.None);
            ScriptStepDisplayName.NormalizeDelayNames(Scripts.Select(item => item.Model));
            foreach (var item in Steps) item.NotifyCanonicalNameChanged();
        }
        finally { scriptSaveGate.Release(); }
    }

    private async Task SaveScriptsWithRollbackAsync(LibraryMutationTransaction transaction)
    {
        var persistence = BeginEditorPersistence();
        try { await SaveScriptsAsync(); }
        catch
        {
            persistence.Dispose();
            RestoreLibraryMutationTransaction(transaction);
            throw;
        }
        finally { persistence.Dispose(); }
    }

    public void SynchronizeSelectedScripts(
        IReadOnlyList<ScriptItemViewModel> selection,
        ScriptItemViewModel? primary)
    {
        var normalized = selection.Where(Scripts.Contains).Distinct().ToList();
        primary = primary is not null && normalized.Contains(primary) ? primary : normalized.FirstOrDefault();
        synchronizingSelectedScripts = true;
        try
        {
            SelectedScript = primary;
            ReplaceSelectedScripts(normalized);
        }
        finally { synchronizingSelectedScripts = false; }
    }

    public bool CanDragScript(ScriptItemViewModel item) =>
        CanReorderScriptLibrary && GetSelectedScriptsForMutation().Contains(item);

    public async Task MoveScriptsToAsync(ScriptItemViewModel item, int insertionIndex)
    {
        if (!CanDragScript(item)) return;
        var group = GetSelectedScriptsForMutation();
        var groupSet = group.ToHashSet();
        var original = Scripts.ToList();
        var normalized = Math.Clamp(insertionIndex, 0, original.Count);
        var adjustedInsertionIndex = normalized - original.Take(normalized).Count(groupSet.Contains);
        var remaining = original.Where(candidate => !groupSet.Contains(candidate)).ToList();
        var desired = remaining.ToList();
        desired.InsertRange(Math.Clamp(adjustedInsertionIndex, 0, remaining.Count), group);
        if (Scripts.SequenceEqual(desired) || !await ResolvePendingEditorChangesAsync()) return;

        var transaction = CaptureLibraryMutationTransaction();
        for (var targetIndex = 0; targetIndex < desired.Count; targetIndex++)
        {
            var currentIndex = Scripts.IndexOf(desired[targetIndex]);
            if (currentIndex != targetIndex) Scripts.Move(currentIndex, targetIndex);
        }
        RefreshScriptCollections();
        await SaveScriptsWithRollbackAsync(transaction);
        SetScriptSelection(group, SelectedScript is not null && group.Contains(SelectedScript) ? SelectedScript : group[0], focus: true);
        StatusMessage = $"Đã sắp xếp lại {group.Count} kịch bản.";
    }

    private IReadOnlyList<ScriptItemViewModel> GetSelectedScriptsForMutation()
    {
        var selected = selectedScripts.Where(item => Scripts.Contains(item) && ScriptLibraryView.Contains(item)).ToHashSet();
        return Scripts.Where(selected.Contains).ToList();
    }

    private bool HasSelectedScriptsForMutation() =>
        ScriptLibraryView is not null &&
        selectedScripts.Any(item => Scripts.Contains(item) && ScriptLibraryView.Contains(item));

    private void ReplaceSelectedScripts(IReadOnlyCollection<ScriptItemViewModel> selection)
    {
        if (selectedScripts.SequenceEqual(selection)) return;
        selectedScripts.Clear();
        selectedScripts.AddRange(selection);
        OnPropertyChanged(nameof(SelectedScripts));
        OnPropertyChanged(nameof(SelectedScriptCount));
        OnPropertyChanged(nameof(HasMultipleSelectedScripts));
        OnPropertyChanged(nameof(ScriptLibrarySelectionSummary));
        RaiseCommandStates();
    }

    private void SetScriptSelection(
        IReadOnlyList<ScriptItemViewModel> selection,
        ScriptItemViewModel? primary,
        bool focus)
    {
        SynchronizeSelectedScripts(selection, primary);
        ScriptSelectionRestoreRequested?.Invoke(selection, focus);
    }

    private void RequestScriptSelectionRestore(bool focus = false) =>
        ScriptSelectionRestoreRequested?.Invoke(selectedScripts, focus);

    private void EnsureScriptVisible(ScriptItemViewModel item)
    {
        if (ScriptLibraryView.Contains(item)) return;
        ScriptLibraryFilter = ScriptLibraryFilter.All;
        ScriptLibrarySearchText = string.Empty;
    }

    internal void EnsureCurrentScriptSelectionVisible()
    {
        if (SelectedScript is not null) EnsureScriptVisible(SelectedScript);
    }

    private LibraryMutationTransaction CaptureLibraryMutationTransaction() => new(
        Scripts.ToList(),
        SelectedScript,
        selectedScripts.Where(Scripts.Contains).Select(item => item.Id).ToList(),
        CommonRunScript,
        ControlCenterSelectedScript,
        configuredCommonScriptId,
        GetSelectedStepsForMutation().Select(item => item.Id).ToList(),
        SelectedStep is not null && Steps.Contains(SelectedStep) ? SelectedStep.Id : null,
        GetSelectedCompositeItems().Select(item => item.Id).ToList(),
        SelectedCompositeItem is not null && CompositeItems.Contains(SelectedCompositeItem)
            ? SelectedCompositeItem.Id
            : null,
        RunTargets.ToDictionary(item => item.TargetKey, item => item.AssignedScriptId, StringComparer.Ordinal),
        stepHistories.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<StepListSnapshot>)pair.Value.Undo.ToList()),
        compositeHistories.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<CompositeListSnapshot>)pair.Value.ToList()));

    private void RestoreLibraryMutationTransaction(LibraryMutationTransaction transaction)
    {
        Scripts.Clear();
        foreach (var item in transaction.Scripts) Scripts.Add(item);

        stepHistories.Clear();
        foreach (var pair in transaction.StepHistories)
        {
            var history = new StepHistory();
            foreach (var snapshot in pair.Value) history.Undo.AddLast(snapshot);
            stepHistories[pair.Key] = history;
        }

        compositeHistories.Clear();
        foreach (var pair in transaction.CompositeHistories)
            compositeHistories[pair.Key] = new LinkedList<CompositeListSnapshot>(pair.Value);

        SelectedScript = transaction.SelectedScript;
        var selectedScriptIds = transaction.SelectedScriptIds.ToHashSet();
        var restoredScriptSelection = Scripts.Where(item => selectedScriptIds.Contains(item.Id)).ToList();
        SynchronizeSelectedScripts(
            restoredScriptSelection,
            SelectedScript is not null && restoredScriptSelection.Contains(SelectedScript)
                ? SelectedScript
                : restoredScriptSelection.FirstOrDefault());
        if (SelectedScript?.Model.Kind == ScriptKind.Regular)
        {
            var ids = transaction.SelectedStepIds.ToHashSet();
            var selection = Steps.Where(item => ids.Contains(item.Id)).ToList();
            var primary = transaction.PrimaryStepId is Guid primaryId
                ? selection.FirstOrDefault(item => item.Id == primaryId)
                : selection.FirstOrDefault();
            SetStepSelection(selection, primary);
        }
        else if (SelectedScript?.Model.Kind == ScriptKind.Composite)
        {
            var ids = transaction.SelectedCompositeItemIds.ToHashSet();
            var selection = CompositeItems.Where(item => ids.Contains(item.Id)).ToList();
            var primary = transaction.PrimaryCompositeItemId is Guid primaryId
                ? selection.FirstOrDefault(item => item.Id == primaryId)
                : selection.FirstOrDefault();
            SetCompositeSelection(selection, primary);
        }

        configuredCommonScriptId = transaction.ConfiguredCommonScriptId;
        commonRunScript = transaction.CommonRunScript;
        OnPropertyChanged(nameof(CommonRunScript));
        controlCenterSelectedScript = transaction.ControlCenterSelectedScript;
        OnPropertyChanged(nameof(ControlCenterSelectedScript));
        OnPropertyChanged(nameof(BulkAssignmentScript));

        foreach (var target in RunTargets)
        {
            transaction.Assignments.TryGetValue(target.TargetKey, out var scriptId);
            var script = scriptId is Guid id ? Scripts.FirstOrDefault(item => item.Id == id) : null;
            target.SetAssignedScript(script?.Id, script?.Name, script?.Model.Kind);
        }

        RefreshScriptCollections();
        RequestScriptSelectionRestore();
        UpdateRunConfigurationState();
        RaiseCommandStates();
    }

    private void SyncStepsToModel() { if (SelectedScript is not null) SyncStepsToModel(SelectedScript); }
    private void SyncStepsToModel(ScriptItemViewModel owner) { if (owner.Model.Kind == ScriptKind.Regular) { owner.Model.Steps.Clear(); owner.Model.Steps.AddRange(Steps.Select(item => item.Model)); } }
    private void TouchSelectedScript() { if (SelectedScript is null) return; SelectedScript.Model.UpdatedAt = DateTimeOffset.UtcNow; SelectedScript.Refresh(); }
    private static void TouchScript(ScriptItemViewModel owner) { owner.Model.UpdatedAt = DateTimeOffset.UtcNow; owner.Refresh(); }

    private void SetScriptPersistenceBlocked(bool value)
    {
        if (!SetProperty(ref isScriptPersistenceBlocked, value, nameof(IsScriptPersistenceBlocked))) return;
        RaiseCommandStates();
    }

    private static ScriptDefinition SnapshotScript(ScriptDefinition source) => new()
    {
        SchemaVersion = source.SchemaVersion,
        Id = source.Id,
        Name = source.Name,
        Kind = source.Kind,
        DefaultInstanceIndex = source.DefaultInstanceIndex,
        UpdatedAt = source.UpdatedAt,
        Variables = source.Variables.Select(variable => new ScriptVariable
        {
            Name = variable.Name,
            Value = variable.Value,
            IsSecret = variable.IsSecret
        }).ToList(),
        Steps = source.Steps.Select(ScriptCloner.CloneStepPreservingId).ToList(),
        CompositeItems = source.CompositeItems.Select(ScriptCloner.CloneCompositeItemPreservingId).ToList()
    };

    private sealed record LibraryMutationTransaction(
        IReadOnlyList<ScriptItemViewModel> Scripts,
        ScriptItemViewModel? SelectedScript,
        IReadOnlyList<Guid> SelectedScriptIds,
        ScriptItemViewModel? CommonRunScript,
        ScriptItemViewModel? ControlCenterSelectedScript,
        Guid? ConfiguredCommonScriptId,
        IReadOnlyList<Guid> SelectedStepIds,
        Guid? PrimaryStepId,
        IReadOnlyList<Guid> SelectedCompositeItemIds,
        Guid? PrimaryCompositeItemId,
        IReadOnlyDictionary<string, Guid?> Assignments,
        IReadOnlyDictionary<Guid, IReadOnlyList<StepListSnapshot>> StepHistories,
        IReadOnlyDictionary<Guid, IReadOnlyList<CompositeListSnapshot>> CompositeHistories);
}
