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
    private const int StepHistoryLimit = 50;
    private static readonly IReadOnlyList<ScriptStepKind> AllStepKinds = Enum.GetValues<ScriptStepKind>();
    private static readonly IReadOnlyList<ScriptStepKind> AuthorableStepKinds =
        AllStepKinds.Where(kind => kind != ScriptStepKind.AndroidShell).ToArray();
    private readonly List<StepItemViewModel> selectedSteps = [];
    private readonly Dictionary<Guid, StepHistory> stepHistories = [];
    private IReadOnlyList<ScriptStep> copiedSteps = [];
    private string? copiedFromScriptName;
    private ScriptStepKind editorKind = ScriptStepKind.AndroidShell;
    private string editorName = "Bước mới";
    private bool editorIsEnabled = true;
    private bool editorContinueOnError;
    private int editorTimeoutSeconds = 30;
    private string editorCommand = string.Empty;
    private string editorApplicationDisplayName = string.Empty;
    private string editorPackageName = string.Empty;
    private string editorActivityName = string.Empty;
    private int editorDelayMilliseconds = 1000;
    private bool isEditorDelayInputValid = true;
    private bool hasEditorBindingErrors;
    private long editorDelayInputRefreshToken;
    private int editorX;
    private int editorY;
    private int editorHoldDuration = 500;
    private int editorX2;
    private int editorY2;
    private int editorSwipeDuration = 300;
    private string editorText = string.Empty;
    private bool editorPressEnterAfterInput;
    private bool editorPressEnterAfterPaste;
    private AndroidKeyEvent editorKey = AndroidKeyEvent.Home;
    private bool synchronizingSelectedSteps;
    private bool suppressEditorDirty;
    private bool isApplyingStepHistory;
    private bool isStepMutationBusy;
    private StepMutationTransaction? pendingToggleTransaction;
    private bool isEditorDirty;
    private long editorVersion;
    private RegularStepEditorMode stepEditorMode;

    private async Task PrepareNewStepAsync()
    {
        if (!await ResolvePendingEditorChangesAsync()) return;
        SetStepSelection([], null);
        ResetEditor();
        StepEditorMode = RegularStepEditorMode.Create;
    }

    private void CancelStepCreate()
    {
        if (StepEditorMode != RegularStepEditorMode.Create) return;
        DiscardEditorChanges();
        ResetEditor();
        StepEditorMode = RegularStepEditorMode.None;
    }

    private async Task AddStepAsync()
    {
        if (SelectedScript is null || StepEditorMode != RegularStepEditorMode.Create || !TryBeginStepMutation()) return;
        try
        {
            var owner = SelectedScript;
            var step = CreateStep(null);
            stepCommandBuilder.Validate(step);
            ApplyCanonicalEditorName(step.Name);
            var savedEditorVersion = editorVersion;
            var savedDraft = CaptureRegularEditorDraft();
            var before = CaptureStepListSnapshot();
            var previousUpdatedAt = owner.Model.UpdatedAt;
            var item = CreateStepItem(step);
            Steps.Add(item);
            PushUndoSnapshot(before);
            SyncStepsToModel();
            TouchSelectedScript();
            using (BeginEditorPersistence())
            {
                try { await SaveScriptsAsync(); }
                catch
                {
                    Steps.Remove(item);
                    SyncStepsToModel(owner);
                    owner.Model.UpdatedAt = previousUpdatedAt;
                    owner.Refresh();
                    var history = GetStepHistory(owner.Id);
                    if (history.Undo.Count > 0) history.Undo.RemoveLast();
                    RefreshRegularEditorDirty();
                    throw;
                }
            }
            if (StepEditorMode == RegularStepEditorMode.Create && editorVersion == savedEditorVersion)
            {
                DiscardEditorChanges();
                StepEditorMode = RegularStepEditorMode.None;
                SetStepSelection([item], item);
                StepFocusRequested?.Invoke(item);
                StatusMessage = "Đã thêm bước.";
            }
            else if (StepEditorMode == RegularStepEditorMode.Create)
            {
                regularEditorBaseline = savedDraft;
                RefreshRegularEditorDirty();
                StatusMessage = IsEditorDirty
                    ? "Đã thêm bước; còn thay đổi mới trong trình tạo."
                    : "Đã thêm bước.";
            }
            else StatusMessage = "Đã thêm bước.";
        }
        finally { EndStepMutation(); }
    }

    private async Task SaveStepAsync()
    {
        if (SelectedScript is null || SelectedStep is null || StepEditorMode != RegularStepEditorMode.Edit ||
            !TryBeginStepMutation()) return;
        try
        {
            var target = SelectedStep;
            var owner = SelectedScript;
            using var persistence = BeginEditorPersistence();
            var previousModel = ScriptCloner.CloneStepPreservingId(target.Model);
            var previousUpdatedAt = owner.Model.UpdatedAt;
            var step = CreateStep(SelectedStep.Id);
            step.IsEnabled = SelectedStep.IsEnabled;
            stepCommandBuilder.Validate(step);
            ApplyCanonicalEditorName(step.Name);
            var savedEditorVersion = editorVersion;
            var savedDraft = CaptureRegularEditorDraft();
            var before = CaptureStepListSnapshot();
            SelectedStep.ReplaceModel(step);
            OnPropertyChanged(nameof(ShowRegularSaveButton));
            PushUndoSnapshot(before);
            SyncStepsToModel();
            TouchSelectedScript();
            UpdatePreview();
            try { await SaveScriptsAsync(); }
            catch
            {
                target.ReplaceModel(previousModel);
                SyncStepsToModel(owner);
                owner.Model.UpdatedAt = previousUpdatedAt;
                owner.Refresh();
                var history = GetStepHistory(owner.Id);
                if (history.Undo.Count > 0) history.Undo.RemoveLast();
                UpdatePreview();
                RefreshRegularEditorDirty();
                throw;
            }
            if (ReferenceEquals(target, SelectedStep))
            {
                regularEditorBaseline = savedDraft;
                if (editorVersion == savedEditorVersion) SetEditorDirty(false);
                else RefreshRegularEditorDirty();
            }
            StatusMessage = IsEditorDirty ? "Đã lưu bước; còn thay đổi chưa lưu." : "Đã lưu bước.";
        }
        finally { EndStepMutation(); }
    }

    private async Task DuplicateStepAsync()
    {
        var source = GetSelectedStepsForMutation();
        if (source.Count == 0 || !await ResolveRegularEditorChangesAsync() || !TryBeginStepMutation()) return;
        try
        {
            var transaction = CaptureStepMutationTransaction();
            var before = transaction.Snapshot;
            var insertionIndex = source.Select(Steps.IndexOf).Max() + 1;
            var clones = source.Select(step => CreateStepItem(ScriptCloner.CloneStep(step.Model))).ToList();
            for (var index = 0; index < clones.Count; index++)
                Steps.Insert(insertionIndex + index, clones[index]);
            SetStepSelection(clones, clones[^1]);
            PushUndoSnapshot(before);
            await PersistStepMutationCoreAsync(transaction);
            StepFocusRequested?.Invoke(clones[^1]);
            StatusMessage = $"Đã nhân bản {clones.Count} bước.";
        }
        finally { EndStepMutation(); }
    }

    private async Task DeleteStepAsync()
    {
        var stepsToDelete = GetSelectedStepsForMutation();
        if (stepsToDelete.Count == 0 ||
            !confirmationService.Confirm($"Xóa {stepsToDelete.Count} bước đã chọn?", "Xác nhận xóa")) return;
        if (!await ResolveRegularEditorChangesAsync() || !TryBeginStepMutation()) return;
        try
        {
            var indexes = stepsToDelete.Select(Steps.IndexOf).Where(index => index >= 0).OrderBy(index => index).ToList();
            if (indexes.Count == 0) return;
            var transaction = CaptureStepMutationTransaction();
            var before = transaction.Snapshot;
            var nextSelectionIndex = indexes[0];
            for (var index = indexes.Count - 1; index >= 0; index--)
                Steps.RemoveAt(indexes[index]);

            var next = Steps.Count == 0 ? null : Steps[Math.Min(nextSelectionIndex, Steps.Count - 1)];
            SetStepSelection(next is null ? [] : [next], next);
            PushUndoSnapshot(before);
            await PersistStepMutationCoreAsync(transaction);
            StatusMessage = $"Đã xóa {indexes.Count} bước.";
        }
        finally { EndStepMutation(); }
    }

    private async Task MoveStepAsync(int offset)
    {
        var group = GetSelectedStepsForMutation();
        if (group.Count == 0) return;
        var groupSet = group.ToHashSet();
        var remaining = Steps.Where(item => !groupSet.Contains(item)).ToList();
        var currentBlockIndex = Steps.TakeWhile(item => !ReferenceEquals(item, group[0])).Count(item => !groupSet.Contains(item));
        await MoveSelectedBlockAsync(group, remaining, currentBlockIndex + offset);
    }

    private bool CanMoveStep(int offset)
    {
        if (!CanMutateSteps) return false;
        var group = GetSelectedStepsForMutation();
        if (group.Count == 0) return false;
        var groupSet = group.ToHashSet();
        var currentBlockIndex = Steps.TakeWhile(item => !ReferenceEquals(item, group[0])).Count(item => !groupSet.Contains(item));
        var target = currentBlockIndex + offset;
        return target >= 0 && target <= Steps.Count - group.Count;
    }

    public async Task MoveStepToAsync(StepItemViewModel item, int insertionIndex)
    {
        if (!CanDragStep(item)) return;
        var group = GetSelectedStepsForMutation();
        var groupSet = group.ToHashSet();
        var original = Steps.ToList();
        var normalized = Math.Clamp(insertionIndex, 0, original.Count);
        var adjustedInsertionIndex = normalized - original.Take(normalized).Count(groupSet.Contains);
        var remaining = original.Where(step => !groupSet.Contains(step)).ToList();
        await MoveSelectedBlockAsync(group, remaining, adjustedInsertionIndex);
    }

    private async Task MoveSelectedBlockAsync(
        IReadOnlyList<StepItemViewModel> group,
        IReadOnlyList<StepItemViewModel> remaining,
        int insertionIndex)
    {
        var normalized = Math.Clamp(insertionIndex, 0, remaining.Count);
        var desired = remaining.ToList();
        desired.InsertRange(normalized, group);
        if (Steps.SequenceEqual(desired) || !await ResolveRegularEditorChangesAsync() || !TryBeginStepMutation()) return;
        try
        {
            var transaction = CaptureStepMutationTransaction();
            var before = transaction.Snapshot;
            synchronizingSelectedSteps = true;
            try
            {
                for (var targetIndex = 0; targetIndex < desired.Count; targetIndex++)
                {
                    var currentIndex = Steps.IndexOf(desired[targetIndex]);
                    if (currentIndex != targetIndex) Steps.Move(currentIndex, targetIndex);
                }
            }
            finally { synchronizingSelectedSteps = false; }
            var primary = SelectedStep is not null && group.Contains(SelectedStep) ? SelectedStep : group[0];
            SetStepSelection(group, primary);
            PushUndoSnapshot(before);
            await PersistStepMutationCoreAsync(transaction);
            StatusMessage = $"Đã di chuyển {group.Count} bước.";
        }
        finally { EndStepMutation(); }
    }

    private void CopySelectedSteps()
    {
        if (!CanChangeSelection) return;
        var source = GetSelectedStepsForMutation();
        if (source.Count == 0) return;
        var visibleCopies = new List<ScriptStep>(source.Count);
        foreach (var item in source)
        {
            if (ReferenceEquals(item, SelectedStep) && StepEditorMode == RegularStepEditorMode.Edit &&
                (IsEditorDirty || HasInvalidRegularEditorDraft || IsEditorPersistenceBusy))
            {
                if (HasInvalidRegularEditorDraft || !IsRegularEditorDraftSemanticallyValid())
                {
                    StatusMessage = "Không thể sao chép vì dữ liệu đang hiển thị không hợp lệ.";
                    return;
                }
                var visibleDraft = CreateStep(item.Id);
                visibleDraft.IsEnabled = item.IsEnabled;
                visibleCopies.Add(visibleDraft);
            }
            else visibleCopies.Add(item.Model);
        }
        copiedSteps = visibleCopies.Select(ScriptCloner.CloneStep).ToList();
        copiedFromScriptName = SelectedScript?.Name;
        OnPropertyChanged(nameof(HasCopiedSteps));
        OnPropertyChanged(nameof(CopiedStepCount));
        OnPropertyChanged(nameof(CopiedFromScriptName));
        OnPropertyChanged(nameof(StepClipboardSummary));
        PasteStepsCommand.RaiseCanExecuteChanged();
        StatusMessage = $"Đã sao chép {copiedSteps.Count} bước.";
    }

    private async Task PasteCopiedStepsAsync()
    {
        if (SelectedScript is null || copiedSteps.Count == 0 ||
            !await ResolveRegularEditorChangesAsync() || !TryBeginStepMutation()) return;
        try
        {
            var transaction = CaptureStepMutationTransaction();
            var before = transaction.Snapshot;
            var selectedIndexes = GetSelectedStepsForMutation().Select(Steps.IndexOf).Where(index => index >= 0).ToList();
            var insertionIndex = selectedIndexes.Count == 0 ? Steps.Count : selectedIndexes.Max() + 1;
            var pasted = copiedSteps.Select(step => CreateStepItem(ScriptCloner.CloneStep(step))).ToList();
            for (var index = 0; index < pasted.Count; index++)
                Steps.Insert(insertionIndex + index, pasted[index]);
            SetStepSelection(pasted, pasted[^1]);
            PushUndoSnapshot(before);
            await PersistStepMutationCoreAsync(transaction);
            StatusMessage = $"Đã dán {pasted.Count} bước.";
        }
        finally { EndStepMutation(); }
    }

    public bool CanDragStep(StepItemViewModel item) =>
        CanMutateSteps && GetSelectedStepsForMutation().Contains(item);

    public void SynchronizeSelectedSteps(IEnumerable<StepItemViewModel> selection)
    {
        ArgumentNullException.ThrowIfNull(selection);
        if (!CanChangeSelection || synchronizingSelectedSteps) return;

        var normalized = selection
            .Where(Steps.Contains)
            .Distinct()
            .OrderBy(Steps.IndexOf)
            .ToList();
        var primary = SelectedStep is not null && normalized.Contains(SelectedStep)
            ? SelectedStep
            : normalized.FirstOrDefault();
        if (ReferenceEquals(primary, SelectedStep))
        {
            ReplaceSelectedSteps(normalized);
            return;
        }

        var previousSelection = GetSelectedStepsForMutation();
        synchronizingSelectedSteps = true;
        try { SelectedStep = primary; }
        finally { synchronizingSelectedSteps = false; }
        if (!ReferenceEquals(SelectedStep, primary))
        {
            RestoreStepSelection(previousSelection);
            return;
        }
        ReplaceSelectedSteps(normalized);
    }

    public bool TryClearStepSelection()
    {
        return TryClearStepSelectionFromBlank();
    }

    public Task<bool> CommitAndClearStepSelectionAsync() =>
        Task.FromResult(TryClearStepSelectionFromBlank());

    private IReadOnlyList<StepItemViewModel> GetSelectedStepsForMutation()
    {
        var valid = selectedSteps.Where(Steps.Contains).Distinct().OrderBy(Steps.IndexOf).ToList();
        if (valid.Count == 0 && SelectedStep is not null && Steps.Contains(SelectedStep)) valid.Add(SelectedStep);
        return valid;
    }

    private void ReplaceSelectedSteps(IReadOnlyCollection<StepItemViewModel> selection)
    {
        if (selectedSteps.SequenceEqual(selection)) return;
        selectedSteps.Clear();
        selectedSteps.AddRange(selection);
        OnPropertyChanged(nameof(SelectedSteps));
        OnPropertyChanged(nameof(SelectedStepCount));
        RaiseCommandStates();
    }

    private void SetStepSelection(IReadOnlyList<StepItemViewModel> selection, StepItemViewModel? primary)
    {
        synchronizingSelectedSteps = true;
        try
        {
            SelectedStep = primary;
            ReplaceSelectedSteps(selection);
            StepSelectionRestoreRequested?.Invoke(selection);
        }
        finally { synchronizingSelectedSteps = false; }
    }

    private void RestoreStepSelection(IReadOnlyList<StepItemViewModel> selection)
    {
        synchronizingSelectedSteps = true;
        try { StepSelectionRestoreRequested?.Invoke(selection); }
        finally { synchronizingSelectedSteps = false; }
    }

    private StepItemViewModel CreateStepItem(ScriptStep step)
    {
        var item = new StepItemViewModel(step);
        item.IsEnabledChanging += OnStepEnabledChanging;
        item.IsEnabledChanged += OnStepEnabledChanged;
        return item;
    }

    private void OnStepEnabledChanging(object? sender, StepEnabledChangingEventArgs args)
    {
        if (isApplyingStepHistory) return;
        if (!CanMutateSteps || SelectedScript is null)
        {
            args.Cancel = true;
            return;
        }

        pendingToggleTransaction = CaptureStepMutationTransaction();
        SetStepMutationBusy(true);
    }

    private async void OnStepEnabledChanged(object? sender, EventArgs e)
    {
        try
        {
            if (sender is StepItemViewModel item && ReferenceEquals(item, SelectedStep))
            {
                suppressEditorDirty = true;
                try { EditorIsEnabled = item.IsEnabled; }
                finally { suppressEditorDirty = false; }
                UpdatePreview();
            }
            if (pendingToggleTransaction is null) return;
            PushUndoSnapshot(pendingToggleTransaction.Snapshot);
            await PersistStepMutationCoreAsync(pendingToggleTransaction);
        }
        catch (Exception exception)
        {
            ReportUnexpectedError(exception);
        }
        finally
        {
            pendingToggleTransaction = null;
            EndStepMutation();
        }
    }

    private async Task PersistStepMutationCoreAsync(StepMutationTransaction transaction)
    {
        var persistence = BeginEditorPersistence();
        try
        {
            SyncStepsToModel(transaction.Owner);
            TouchScript(transaction.Owner);
            await SaveScriptsAsync();
        }
        catch
        {
            persistence.Dispose();
            RestoreStepMutationTransaction(transaction);
            throw;
        }
        finally { persistence.Dispose(); }
    }

    private bool CanAddStep() => SelectedScript is not null && CanMutateSteps &&
        StepEditorMode == RegularStepEditorMode.Create && IsRegularEditorDraftSemanticallyValid();

    private bool CanSaveStep() => SelectedScript is not null && SelectedStep is not null && CanMutateSteps &&
        StepEditorMode == RegularStepEditorMode.Edit &&
        IsEditorDirty && IsRegularEditorDraftSemanticallyValid();

    private bool IsRegularEditorDraftSemanticallyValid()
    {
        if (HasInvalidRegularEditorDraft) return false;
        try
        {
            stepCommandBuilder.Validate(CreateStep(SelectedStep?.Id));
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }

    private bool TryBeginStepMutation()
    {
        if (!CanMutateSteps) return false;
        SetStepMutationBusy(true);
        return true;
    }

    private void EndStepMutation() => SetStepMutationBusy(false);

    private void SetStepMutationBusy(bool value)
    {
        if (isStepMutationBusy == value) return;
        isStepMutationBusy = value;
        RaiseCommandStates();
    }

    private bool CanUndoStepList() =>
        CanMutateSteps && SelectedScript is not null &&
        stepHistories.TryGetValue(SelectedScript.Id, out var history) && history.Undo.Count > 0;

    private async Task RestoreStepHistoryAsync()
    {
        if (SelectedScript is null || !await ResolveRegularEditorChangesAsync() || !TryBeginStepMutation()) return;
        try
        {
            var transaction = CaptureStepMutationTransaction();
            var history = GetStepHistory(SelectedScript.Id);
            if (history.Undo.Count == 0) return;

            var target = history.Undo.Last!.Value;
            history.Undo.RemoveLast();
            ApplyStepListSnapshot(target);
            await PersistStepMutationCoreAsync(transaction);
            StatusMessage = "Đã hoàn tác thao tác danh sách bước.";
        }
        finally { EndStepMutation(); }
    }

    private StepListSnapshot CaptureStepListSnapshot() => new(
        Steps.Select(item => ScriptCloner.CloneStepPreservingId(item.Model)).ToList(),
        selectedSteps.Where(Steps.Contains).Select(item => item.Id).ToList(),
        SelectedStep is not null && Steps.Contains(SelectedStep) ? SelectedStep.Id : null);

    private StepMutationTransaction CaptureStepMutationTransaction()
    {
        var owner = SelectedScript ?? throw new InvalidOperationException("Chưa chọn kịch bản để thay đổi.");
        var hadHistory = stepHistories.TryGetValue(owner.Id, out var history);
        return new StepMutationTransaction(
            owner,
            CaptureStepListSnapshot(),
            owner.Model.UpdatedAt,
            hadHistory,
            history?.Undo.ToList() ?? []);
    }

    private void RestoreStepMutationTransaction(StepMutationTransaction transaction)
    {
        ApplyStepListSnapshot(transaction.Snapshot);
        SyncStepsToModel(transaction.Owner);
        transaction.Owner.Model.UpdatedAt = transaction.UpdatedAt;
        transaction.Owner.Refresh();

        if (!transaction.HadHistory)
        {
            stepHistories.Remove(transaction.Owner.Id);
        }
        else
        {
            var history = GetStepHistory(transaction.Owner.Id);
            history.Undo.Clear();
            foreach (var snapshot in transaction.History) history.Undo.AddLast(snapshot);
        }
        RaiseCommandStates();
    }

    private void ApplyStepListSnapshot(StepListSnapshot snapshot)
    {
        isApplyingStepHistory = true;
        try
        {
            Steps.Clear();
            foreach (var step in snapshot.Steps.Select(ScriptCloner.CloneStepPreservingId))
                Steps.Add(CreateStepItem(step));

            var selectedIds = snapshot.SelectedStepIds.ToHashSet();
            var selection = Steps.Where(item => selectedIds.Contains(item.Id)).ToList();
            var primary = snapshot.PrimaryStepId is Guid primaryId
                ? selection.FirstOrDefault(item => item.Id == primaryId)
                : null;
            primary ??= selection.FirstOrDefault();
            SetStepSelection(selection, primary);
        }
        finally { isApplyingStepHistory = false; }
    }

    private void PushUndoSnapshot(StepListSnapshot snapshot)
    {
        if (SelectedScript is null) return;
        var history = GetStepHistory(SelectedScript.Id);
        AddHistorySnapshot(history.Undo, snapshot);
        RaiseCommandStates();
    }

    private StepHistory GetStepHistory(Guid scriptId)
    {
        if (!stepHistories.TryGetValue(scriptId, out var history))
        {
            history = new StepHistory();
            stepHistories[scriptId] = history;
        }
        return history;
    }

    private static void AddHistorySnapshot(LinkedList<StepListSnapshot> history, StepListSnapshot snapshot)
    {
        history.AddLast(snapshot);
        while (history.Count > StepHistoryLimit) history.RemoveFirst();
    }

    private ScriptStep CreateStep(Guid? id)
    {
        var name = CanonicalizeStepName(EditorKind, EditorName);
        ScriptStep step = EditorKind switch
        {
            ScriptStepKind.AndroidShell => new AndroidShellStep { Id = id ?? Guid.NewGuid(), Name = name, Command = EditorCommand },
            ScriptStepKind.ForceStop => new ForceStopStep
            {
                Id = id ?? Guid.NewGuid(),
                Name = name,
                PackageName = EditorPackageName,
                ApplicationDisplayName = NormalizeOptionalDisplayName(EditorApplicationDisplayName)
            },
            ScriptStepKind.OpenApp => new OpenAppStep
            {
                Id = id ?? Guid.NewGuid(),
                Name = name,
                PackageName = EditorPackageName,
                ActivityName = EditorActivityName,
                ApplicationDisplayName = NormalizeOptionalDisplayName(EditorApplicationDisplayName)
            },
            ScriptStepKind.Delay => new DelayStep { Id = id ?? Guid.NewGuid(), Name = ScriptStepDisplayName.DelayCanonicalName, DurationMilliseconds = EditorDelayMilliseconds },
            ScriptStepKind.Tap => new TapStep { Id = id ?? Guid.NewGuid(), Name = name, X = EditorX, Y = EditorY },
            ScriptStepKind.Hold => new HoldStep
            {
                Id = id ?? Guid.NewGuid(),
                Name = name,
                X = EditorX,
                Y = EditorY,
                DurationMilliseconds = EditorHoldDuration
            },
            ScriptStepKind.Swipe => new SwipeStep { Id = id ?? Guid.NewGuid(), Name = name, X1 = EditorX, Y1 = EditorY, X2 = EditorX2, Y2 = EditorY2, DurationMilliseconds = EditorSwipeDuration },
            ScriptStepKind.InputText => new InputTextStep
            {
                Id = id ?? Guid.NewGuid(),
                Name = name,
                Text = EditorText,
                PressEnterAfterInput = EditorPressEnterAfterInput
            },
            ScriptStepKind.AndroidClipboardPaste => new AndroidClipboardPasteStep
            {
                Id = id ?? Guid.NewGuid(),
                Name = name,
                PressEnterAfterPaste = EditorPressEnterAfterPaste
            },
            ScriptStepKind.KeyEvent => new KeyEventStep { Id = id ?? Guid.NewGuid(), Name = name, Key = EditorKey },
            ScriptStepKind.Note => new NoteStep { Id = id ?? Guid.NewGuid(), Name = name, Text = EditorText },
            ScriptStepKind.CloseChromeTabs => new CloseChromeTabsStep { Id = id ?? Guid.NewGuid(), Name = name },
            _ => throw new ArgumentOutOfRangeException()
        };
        step.IsEnabled = EditorIsEnabled;
        step.ContinueOnError = EditorContinueOnError;
        step.TimeoutSeconds = EditorTimeoutSeconds;
        return step;
    }

    private void LoadEditor(ScriptStep step)
    {
        suppressEditorDirty = true;
        try
        {
            ResetEditorValues();
            EditorKind = step.Kind;
            EditorName = step is DelayStep ? ScriptStepDisplayName.DelayCanonicalName : step.Name;
            EditorIsEnabled = step.IsEnabled;
            EditorContinueOnError = step.ContinueOnError; EditorTimeoutSeconds = step.TimeoutSeconds;
            switch (step)
            {
                case AndroidShellStep value: EditorCommand = value.Command; break;
                case ForceStopStep value: EditorApplicationDisplayName = value.ApplicationDisplayName ?? string.Empty; EditorPackageName = value.PackageName; break;
                case OpenAppStep value: EditorApplicationDisplayName = value.ApplicationDisplayName ?? string.Empty; EditorPackageName = value.PackageName; EditorActivityName = value.ActivityName; break;
                case DelayStep value: EditorDelayMilliseconds = value.DurationMilliseconds; break;
                case TapStep value: EditorX = value.X; EditorY = value.Y; break;
                case HoldStep value: EditorX = value.X; EditorY = value.Y; EditorHoldDuration = value.DurationMilliseconds; break;
                case SwipeStep value: EditorX = value.X1; EditorY = value.Y1; EditorX2 = value.X2; EditorY2 = value.Y2; EditorSwipeDuration = value.DurationMilliseconds; break;
                case InputTextStep value:
                    EditorText = value.Text;
                    EditorPressEnterAfterInput = value.PressEnterAfterInput;
                    break;
                case AndroidClipboardPasteStep value: EditorPressEnterAfterPaste = value.PressEnterAfterPaste; break;
                case KeyEventStep value: EditorKey = value.Key; break;
                case NoteStep value: EditorText = value.Text; break;
            }
        }
        finally { suppressEditorDirty = false; }
        RefreshEditorDelayInput();
        UpdateSelectedStepDraftPreview();
        AcceptRegularEditorBaseline();
    }

    private void ResetEditor()
    {
        suppressEditorDirty = true;
        try { ResetEditorValues(); }
        finally { suppressEditorDirty = false; }
        RefreshEditorDelayInput();
        AcceptRegularEditorBaseline();
    }

    private void RefreshEditorDelayInput()
    {
        HasEditorBindingErrors = false;
        IsEditorDelayInputValid = true;
        editorDelayInputRefreshToken = unchecked(editorDelayInputRefreshToken + 1);
        OnPropertyChanged(nameof(EditorDelayInputRefreshToken));
    }

    private void ResetEditorValues()
    {
        EditorKind = ScriptStepKind.ForceStop;
        EditorName = ScriptStepDisplayName.GetDefaultName(EditorKind);
        EditorIsEnabled = true;
        EditorContinueOnError = false; EditorTimeoutSeconds = 30; EditorCommand = string.Empty;
        EditorApplicationDisplayName = string.Empty; EditorPackageName = string.Empty; EditorActivityName = string.Empty; EditorDelayMilliseconds = 1000;
        EditorX = 0; EditorY = 0; EditorHoldDuration = 500; EditorX2 = 0; EditorY2 = 0; EditorSwipeDuration = 300;
        EditorText = string.Empty; EditorPressEnterAfterInput = false; EditorPressEnterAfterPaste = false; EditorKey = AndroidKeyEvent.Home;
    }

    private bool SetEditorProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (!SetProperty(ref field, value, propertyName)) return false;
        if (!suppressEditorDirty)
        {
            editorVersion++;
            RefreshRegularEditorDirty();
            NotifyRegularEditorDraftContentChanged();
        }
        return true;
    }

    private static string CanonicalizeStepName(ScriptStepKind kind, string value) =>
        kind == ScriptStepKind.Delay
            ? ScriptStepDisplayName.DelayCanonicalName
            : string.IsNullOrWhiteSpace(value)
                ? ScriptStepDisplayName.GetDefaultName(kind)
                : value.Trim();

    private static string? NormalizeOptionalDisplayName(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private void ApplyCanonicalEditorName(string canonicalName)
    {
        if (string.Equals(EditorName, canonicalName, StringComparison.Ordinal)) return;
        suppressEditorDirty = true;
        try { EditorName = canonicalName; }
        finally { suppressEditorDirty = false; }
        NotifyRegularEditorDraftContentChanged();
    }

    private void NotifyRegularEditorDraftContentChanged()
    {
        UpdateSelectedStepDraftPreview();
        UpdatePreview();
        RaiseCommandStates();
    }

    private void UpdateSelectedStepDraftPreview() =>
        SelectedStep?.PreviewDraft(EditorKind, EditorDelayMilliseconds);

    private void DiscardEditorChanges()
    {
        editorVersion++;
        AcceptRegularEditorBaseline();
    }

    private bool HasInvalidRegularEditorDraft => HasEditorBindingErrors ||
        (EditorKind == ScriptStepKind.Delay && !IsEditorDelayInputValid);

    private bool AreCurrentEditorInputsValid => !HasEditorBindingErrors &&
        (!IsRegularScriptSelected || EditorKind != ScriptStepKind.Delay || IsEditorDelayInputValid) &&
        (!IsCompositeScriptSelected || SelectedCompositeItem?.Model is not CompositeDelayItem ||
         IsCompositeDelayInputValid);

    private void SetEditorDirty(bool value)
    {
        if (!SetProperty(ref isEditorDirty, value, nameof(IsEditorDirty))) return;
        OnPropertyChanged(nameof(HasRegularEditorDraft));
        OnPropertyChanged(nameof(EditorSaveState));
        OnPropertyChanged(nameof(HasAnyEditorDraft));
        OnPropertyChanged(nameof(RunConfigurationError));
        RaiseCommandStates();
    }

    private sealed class StepHistory
    {
        public LinkedList<StepListSnapshot> Undo { get; } = [];
    }

    private sealed record StepListSnapshot(
        IReadOnlyList<ScriptStep> Steps,
        IReadOnlyList<Guid> SelectedStepIds,
        Guid? PrimaryStepId);

    private sealed record StepMutationTransaction(
        ScriptItemViewModel Owner,
        StepListSnapshot Snapshot,
        DateTimeOffset UpdatedAt,
        bool HadHistory,
        IReadOnlyList<StepListSnapshot> History);
}
