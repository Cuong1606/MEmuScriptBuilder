using MEmuScriptStudio.App.Services;
using MEmuScriptStudio.Core.Formatting;
using MEmuScriptStudio.Core.Models;

namespace MEmuScriptStudio.App.ViewModels;

public sealed partial class MainViewModel
{
    private bool suppressScriptNameDirty;

    private RegularEditorDraftSnapshot? regularEditorBaseline;
    private string scriptNameBaseline = string.Empty;
    private readonly object editorPersistenceSync = new();
    private int editorPersistenceOperationCount;
    private TaskCompletionSource? editorPersistenceCompletion;

    public bool IsEditorPersistenceBusy
    {
        get
        {
            lock (editorPersistenceSync) return editorPersistenceOperationCount > 0;
        }
    }

    public bool HasRegularEditorDraft => StepEditorMode != RegularStepEditorMode.None &&
                                         (IsEditorDirty || HasInvalidRegularEditorDraft);

    public bool HasPendingNavigationDraft => HasAnyEditorDraft;

    private bool HasBlockingExecutionDraft => IsEditorPersistenceBusy || HasAnyEditorDraft;

    private void SetScriptNameFromModel(string value)
    {
        scriptNameBaseline = value;
        suppressScriptNameDirty = true;
        try { ScriptName = value; }
        finally { suppressScriptNameDirty = false; }
        OnPropertyChanged(nameof(IsScriptNameDirty));
        OnPropertyChanged(nameof(CanRenameScript));
        OnPropertyChanged(nameof(EditorSaveState));
        RaiseCommandStates();
    }

    private static string NormalizeScriptName(string value) => value.Trim();

    private RegularEditorDraftSnapshot CaptureRegularEditorDraft() => new(
        EditorKind,
        EditorKind == ScriptStepKind.Delay ? ScriptStepDisplayName.DelayCanonicalName : EditorName,
        EditorIsEnabled,
        EditorContinueOnError,
        EditorTimeoutSeconds,
        EditorKind == ScriptStepKind.AndroidShell ? EditorCommand : string.Empty,
        EditorKind is ScriptStepKind.ForceStop or ScriptStepKind.OpenApp ? EditorApplicationDisplayName : string.Empty,
        EditorKind is ScriptStepKind.ForceStop or ScriptStepKind.OpenApp ? EditorPackageName : string.Empty,
        EditorKind == ScriptStepKind.OpenApp ? EditorActivityName : string.Empty,
        EditorKind == ScriptStepKind.Delay ? EditorDelayMilliseconds : 0,
        EditorKind is ScriptStepKind.Tap or ScriptStepKind.Hold or ScriptStepKind.Swipe ? EditorX : 0,
        EditorKind is ScriptStepKind.Tap or ScriptStepKind.Hold or ScriptStepKind.Swipe ? EditorY : 0,
        EditorKind == ScriptStepKind.Hold ? EditorHoldDuration : 0,
        EditorKind == ScriptStepKind.Swipe ? EditorX2 : 0,
        EditorKind == ScriptStepKind.Swipe ? EditorY2 : 0,
        EditorKind == ScriptStepKind.Swipe ? EditorSwipeDuration : 0,
        EditorKind is ScriptStepKind.InputText or ScriptStepKind.Note ? EditorText : string.Empty,
        EditorKind == ScriptStepKind.InputText && EditorPressEnterAfterInput,
        EditorKind == ScriptStepKind.AndroidClipboardPaste && EditorPressEnterAfterPaste,
        EditorKind == ScriptStepKind.KeyEvent ? EditorKey : default);

    private void AcceptRegularEditorBaseline(RegularEditorDraftSnapshot? snapshot = null)
    {
        regularEditorBaseline = snapshot ?? CaptureRegularEditorDraft();
        SetEditorDirty(false);
    }

    private void RefreshRegularEditorDirty()
    {
        if (suppressEditorDirty) return;
        SetEditorDirty(regularEditorBaseline is null || regularEditorBaseline != CaptureRegularEditorDraft());
    }

    private void CancelScriptRename() => SetScriptNameFromModel(SelectedScript is null ? string.Empty : scriptNameBaseline);

    public async Task<bool> NavigateToScriptAsync(ScriptItemViewModel? target)
    {
        if (ReferenceEquals(target, SelectedScript)) return true;
        await WaitForEditorPersistenceAsync();
        if (!await ResolvePendingEditorChangesAsync()) return false;
        SelectedScript = target;
        return ReferenceEquals(SelectedScript, target);
    }

    public async Task<bool> NavigateToStepAsync(StepItemViewModel? target)
    {
        if (ReferenceEquals(target, SelectedStep)) return true;
        await WaitForEditorPersistenceAsync();
        if (!await ResolveRegularEditorChangesAsync()) return false;
        SetStepSelection(target is null ? [] : [target], target);
        return ReferenceEquals(SelectedStep, target);
    }

    public async Task<bool> NavigateToCompositeItemAsync(CompositeItemViewModel? target)
    {
        if (ReferenceEquals(target, SelectedCompositeItem)) return true;
        await WaitForEditorPersistenceAsync();
        if (!await ResolveCompositeEditorChangesAsync()) return false;
        SetCompositeSelection(target is null ? [] : [target], target);
        return ReferenceEquals(SelectedCompositeItem, target);
    }

    public async Task<bool> TryPrepareForCloseAsync()
    {
        await WaitForEditorPersistenceAsync();
        return await ResolvePendingEditorChangesAsync();
    }

    public bool TryClearStepSelectionFromBlank()
    {
        if (!CanChangeSelection || HasRegularEditorDraft) return false;
        SetStepSelection([], null);
        return true;
    }

    public bool TryClearCompositeSelectionFromBlank()
    {
        if (!CanChangeSelection || HasCompositeEditorDraft) return false;
        SetCompositeSelection([], null);
        return true;
    }

    private async Task<bool> ResolvePendingEditorChangesAsync()
    {
        await WaitForEditorPersistenceAsync();
        if (!await ResolveRegularEditorChangesAsync()) return false;
        if (!await ResolveCompositeEditorChangesAsync()) return false;
        return await ResolveScriptNameChangesAsync();
    }

    private async Task<bool> ResolveRegularEditorChangesAsync()
    {
        await WaitForEditorPersistenceAsync();
        if (!HasRegularEditorDraft) return true;
        var canSave = IsRegularEditorDraftSemanticallyValid();
        var decision = confirmationService.DecideEditorDraft(
            StepEditorMode == RegularStepEditorMode.Create ? "Bước mới" : "Thuộc tính bước",
            canSave);
        switch (decision)
        {
            case EditorDraftDecision.Save when StepEditorMode == RegularStepEditorMode.Create:
                await AddStepAsync();
                return !HasRegularEditorDraft;
            case EditorDraftDecision.Save:
                await SaveStepAsync();
                return !HasRegularEditorDraft;
            case EditorDraftDecision.Discard:
                DiscardRegularEditorDraft();
                return true;
            default:
                return false;
        }
    }

    private void DiscardRegularEditorDraft()
    {
        if (StepEditorMode == RegularStepEditorMode.Create)
        {
            CancelStepCreate();
            return;
        }

        if (SelectedStep is not null)
        {
            LoadEditor(SelectedStep.Model);
            StepEditorMode = RegularStepEditorMode.Edit;
        }
        else
        {
            ResetEditor();
            StepEditorMode = RegularStepEditorMode.None;
        }
    }

    private async Task<bool> ResolveScriptNameChangesAsync()
    {
        if (!IsScriptNameDirty) return true;
        var decision = confirmationService.DecideEditorDraft("Tên kịch bản", CanRenameScript);
        switch (decision)
        {
            case EditorDraftDecision.Save:
                await RenameScriptAsync();
                return !IsScriptNameDirty;
            case EditorDraftDecision.Discard:
                CancelScriptRename();
                return true;
            default:
                return false;
        }
    }

    private IDisposable BeginEditorPersistence()
    {
        lock (editorPersistenceSync)
        {
            if (editorPersistenceOperationCount++ == 0)
                editorPersistenceCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        OnPropertyChanged(nameof(IsEditorPersistenceBusy));
        OnPropertyChanged(nameof(HasAnyEditorDraft));
        OnPropertyChanged(nameof(EditorSaveState));
        OnPropertyChanged(nameof(RunConfigurationError));
        RaiseCommandStates();
        return new EditorPersistenceScope(this);
    }

    private Task WaitForEditorPersistenceAsync()
    {
        lock (editorPersistenceSync)
            return editorPersistenceOperationCount == 0
                ? Task.CompletedTask
                : editorPersistenceCompletion!.Task;
    }

    private void EndEditorPersistence()
    {
        TaskCompletionSource? completion = null;
        lock (editorPersistenceSync)
        {
            if (--editorPersistenceOperationCount == 0)
            {
                completion = editorPersistenceCompletion;
                editorPersistenceCompletion = null;
            }
        }
        completion?.TrySetResult();
        OnPropertyChanged(nameof(IsEditorPersistenceBusy));
        OnPropertyChanged(nameof(HasAnyEditorDraft));
        OnPropertyChanged(nameof(EditorSaveState));
        OnPropertyChanged(nameof(RunConfigurationError));
        RaiseCommandStates();
    }


    private sealed record RegularEditorDraftSnapshot(
        ScriptStepKind Kind,
        string Name,
        bool IsEnabled,
        bool ContinueOnError,
        int TimeoutSeconds,
        string Command,
        string ApplicationDisplayName,
        string PackageName,
        string ActivityName,
        int DelayMilliseconds,
        int X,
        int Y,
        int HoldDuration,
        int X2,
        int Y2,
        int SwipeDuration,
        string Text,
        bool PressEnterAfterInput,
        bool PressEnterAfterPaste,
        AndroidKeyEvent Key);

    private sealed class EditorPersistenceScope(MainViewModel owner) : IDisposable
    {
        private MainViewModel? owner = owner;
        public void Dispose() => Interlocked.Exchange(ref owner, null)?.EndEditorPersistence();
    }
}
