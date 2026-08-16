using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using MEmuScriptStudio.App.Services;
using MEmuScriptStudio.Core.Models;
using MEmuScriptStudio.Core.Scripts;

namespace MEmuScriptStudio.App.ViewModels;

public enum ScriptLibraryFilter
{
    All,
    Regular,
    Composite
}

public enum ScriptLibrarySortMode
{
    Default,
    NameAscending,
    NameDescending
}

public sealed record ScriptLibrarySortOption(ScriptLibrarySortMode Value, string Label);

public sealed partial class MainViewModel
{
    public event Action<IReadOnlyList<CompositeItemViewModel>>? CompositeSelectionRestoreRequested;
    private readonly List<CompositeItemViewModel> selectedCompositeItems = [];
    private readonly Dictionary<Guid, LinkedList<CompositeListSnapshot>> compositeHistories = [];
    private IReadOnlyList<CompositeScriptItem> copiedCompositeItems = [];
    private CompositeItemViewModel? selectedCompositeItem;
    private ScriptItemViewModel? compositeReferenceScript;
    private int compositeDelayMilliseconds = 1000;
    private bool isCompositeDelayInputValid = true;
    private long compositeDelayInputRefreshToken;
    private bool compositeContinueOnFailure;
    private ScriptLibraryFilter scriptLibraryFilter;
    private string scriptLibrarySearchText = string.Empty;
    private ScriptLibrarySortMode selectedScriptLibrarySortMode;
    private bool compositeMutationBusy;
    private CompositeMutationTransaction? pendingCompositeToggleTransaction;
    private bool isCompositeEditorDirty;
    private bool suppressCompositeEditorDirty;
    private long compositeEditorVersion;
    private CompositeEditorDraftSnapshot? compositeEditorBaseline;

    public ObservableCollection<CompositeItemViewModel> CompositeItems { get; } = [];
    public ObservableCollection<ScriptItemViewModel> RegularScripts { get; } = [];
    public IReadOnlyList<ScriptLibraryFilter> ScriptLibraryFilters { get; } = Enum.GetValues<ScriptLibraryFilter>();
    public IReadOnlyList<ScriptLibrarySortOption> ScriptLibrarySortOptions { get; } =
    [
        new(ScriptLibrarySortMode.Default, "Mặc định"),
        new(ScriptLibrarySortMode.NameAscending, "Tên A → Z"),
        new(ScriptLibrarySortMode.NameDescending, "Tên Z → A")
    ];
    public ICollectionView ScriptLibraryView { get; private set; } = null!;
    public bool CanReorderScriptLibrary => SelectedScriptLibrarySortMode == ScriptLibrarySortMode.Default &&
        ScriptLibraryFilter == ScriptLibraryFilter.All && string.IsNullOrWhiteSpace(ScriptLibrarySearchText) &&
        CanChangeSelection && !IsEditorPersistenceBusy && !IsScriptPersistenceBlocked;
    public bool IsRegularScriptSelected => SelectedScript?.Model.Kind == ScriptKind.Regular;
    public bool IsCompositeScriptSelected => SelectedScript?.Model.Kind == ScriptKind.Composite;
    public int SelectedCompositeItemCount => selectedCompositeItems.Count;
    public IReadOnlyList<CompositeItemViewModel> SelectedCompositeItems => selectedCompositeItems;
    public bool IsCompositeEditorDirty => isCompositeEditorDirty;
    public bool HasCompositeEditorDraft => SelectedCompositeItem is not null &&
                                           (IsCompositeEditorDirty || HasInvalidCompositeEditorDraft);
    public bool HasCopiedCompositeItems => copiedCompositeItems.Count > 0;
    public string CompositeClipboardSummary => HasCopiedCompositeItems
        ? $"Clipboard mục gộp: {copiedCompositeItems.Count} mục"
        : "Clipboard mục gộp: trống";

    public ScriptLibraryFilter ScriptLibraryFilter
    {
        get => scriptLibraryFilter;
        set
        {
            if (!SetProperty(ref scriptLibraryFilter, value)) return;
            OnPropertyChanged(nameof(CanReorderScriptLibrary));
            RefreshScriptLibraryView();
        }
    }

    public string ScriptLibrarySearchText
    {
        get => scriptLibrarySearchText;
        set
        {
            if (!SetProperty(ref scriptLibrarySearchText, value)) return;
            OnPropertyChanged(nameof(CanReorderScriptLibrary));
            RefreshScriptLibraryView();
        }
    }

    public ScriptLibrarySortMode SelectedScriptLibrarySortMode
    {
        get => selectedScriptLibrarySortMode;
        set
        {
            if (!SetProperty(ref selectedScriptLibrarySortMode, value)) return;
            OnPropertyChanged(nameof(CanReorderScriptLibrary));
            ApplyScriptLibrarySort();
        }
    }

    public CompositeItemViewModel? SelectedCompositeItem
    {
        get => selectedCompositeItem;
        set
        {
            if (!IsInitializing && value != selectedCompositeItem &&
                (HasCompositeEditorDraft || IsEditorPersistenceBusy)) return;
            var previous = selectedCompositeItem;
            if (!SetProperty(ref selectedCompositeItem, value)) return;
            previous?.ClearDraftPreview();
            suppressCompositeEditorDirty = true;
            try
            {
                if (value is null)
                {
                    CompositeReferenceScript = RegularScripts.FirstOrDefault();
                    CompositeDelayMilliseconds = 1000;
                    CompositeContinueOnFailure = false;
                }
                else
                {
                    CompositeReferenceScript = value.Model is ScriptReferenceItem reference
                        ? RegularScripts.FirstOrDefault(script => script.Id == reference.ScriptId)
                        : null;
                    CompositeDelayMilliseconds = value.Model is CompositeDelayItem delay ? delay.DurationMilliseconds : 1000;
                    CompositeContinueOnFailure = value.Model is ScriptReferenceItem referenceItem && referenceItem.ContinueOnFailure;
                }
            }
            finally { suppressCompositeEditorDirty = false; }
            IsCompositeDelayInputValid = true;
            compositeDelayInputRefreshToken = unchecked(compositeDelayInputRefreshToken + 1);
            OnPropertyChanged(nameof(CompositeDelayInputRefreshToken));
            HasEditorBindingErrors = false;
            AcceptCompositeEditorBaseline();
            RaiseCompositeCommandStates();
        }
    }

    public ScriptItemViewModel? CompositeReferenceScript
    {
        get => compositeReferenceScript;
        set
        {
            if (!SetProperty(ref compositeReferenceScript, value)) return;
            MarkCompositeEditorDirty();
        }
    }

    public int CompositeDelayMilliseconds
    {
        get => compositeDelayMilliseconds;
        set
        {
            if (!SetProperty(ref compositeDelayMilliseconds, value)) return;
            SelectedCompositeItem?.PreviewDelayDuration(value);
            MarkCompositeEditorDirty();
        }
    }

    public bool IsCompositeDelayInputValid
    {
        get => isCompositeDelayInputValid;
        set
        {
            if (!SetProperty(ref isCompositeDelayInputValid, value)) return;
            MarkCompositeEditorDirty();
            OnPropertyChanged(nameof(HasCompositeEditorDraft));
            OnPropertyChanged(nameof(HasAnyEditorDraft));
            OnPropertyChanged(nameof(EditorSaveState));
            RaiseCommandStates();
        }
    }

    public long CompositeDelayInputRefreshToken => compositeDelayInputRefreshToken;

    public bool CompositeContinueOnFailure
    {
        get => compositeContinueOnFailure;
        set
        {
            if (!SetProperty(ref compositeContinueOnFailure, value)) return;
            MarkCompositeEditorDirty();
        }
    }

    public AsyncCommand CreateCompositeScriptCommand { get; private set; } = null!;
    public AsyncCommand AddCompositeReferenceCommand { get; private set; } = null!;
    public AsyncCommand AddCompositeDelayCommand { get; private set; } = null!;
    public AsyncCommand SaveCompositeItemCommand { get; private set; } = null!;
    public AsyncCommand DeleteCompositeItemsCommand { get; private set; } = null!;
    public AsyncCommand MoveCompositeItemUpCommand { get; private set; } = null!;
    public AsyncCommand MoveCompositeItemDownCommand { get; private set; } = null!;
    public RelayCommand CopyCompositeItemsCommand { get; private set; } = null!;
    public AsyncCommand PasteCompositeItemsCommand { get; private set; } = null!;
    public AsyncCommand UndoCompositeItemsCommand { get; private set; } = null!;
    public AsyncCommand OpenReferencedScriptCommand { get; private set; } = null!;

    private void InitializeCompositeWorkspace()
    {
        ScriptLibraryView = CollectionViewSource.GetDefaultView(Scripts);
        ScriptLibraryView.Filter = FilterScriptLibraryItem;
        ApplyScriptLibrarySort();
        CreateCompositeScriptCommand = new AsyncCommand(CreateCompositeScriptAsync,
            () => !IsCapturing && !IsScriptPersistenceBlocked, ReportUnexpectedError);
        AddCompositeReferenceCommand = new AsyncCommand(AddCompositeReferenceAsync,
            () => CanMutateComposite && RegularScripts.Count > 0, ReportUnexpectedError);
        AddCompositeDelayCommand = new AsyncCommand(AddCompositeDelayAsync,
            () => CanMutateComposite && IsCompositeDelayInputValid, ReportUnexpectedError);
        SaveCompositeItemCommand = new AsyncCommand(SaveCompositeItemAsync,
            () => CanMutateComposite && IsCompositeEditorDirty &&
                  SelectedCompositeItem is not null && !HasInvalidCompositeEditorDraft,
            ReportUnexpectedError);
        DeleteCompositeItemsCommand = new AsyncCommand(DeleteCompositeItemsAsync,
            () => CanMutateComposite && selectedCompositeItems.Count > 0, ReportUnexpectedError);
        MoveCompositeItemUpCommand = new AsyncCommand(() => MoveCompositeItemsAsync(-1), () => CanMoveComposite(-1), ReportUnexpectedError);
        MoveCompositeItemDownCommand = new AsyncCommand(() => MoveCompositeItemsAsync(1), () => CanMoveComposite(1), ReportUnexpectedError);
        CopyCompositeItemsCommand = new RelayCommand(CopyCompositeItems,
            () => IsCompositeScriptSelected && selectedCompositeItems.Count > 0 && CanChangeSelection);
        PasteCompositeItemsCommand = new AsyncCommand(PasteCompositeItemsAsync,
            () => CanMutateComposite && copiedCompositeItems.Count > 0, ReportUnexpectedError);
        UndoCompositeItemsCommand = new AsyncCommand(UndoCompositeItemsAsync, CanUndoCompositeItems, ReportUnexpectedError);
        OpenReferencedScriptCommand = new AsyncCommand(OpenReferencedScriptAsync,
            () => SelectedCompositeItem?.Model is ScriptReferenceItem);
        RefreshScriptCollections();
    }

    private bool FilterScriptLibraryItem(object item)
    {
        if (item is not ScriptItemViewModel script) return false;
        var matchesType = ScriptLibraryFilter switch
        {
            ScriptLibraryFilter.Regular => script.Model.Kind == ScriptKind.Regular,
            ScriptLibraryFilter.Composite => script.Model.Kind == ScriptKind.Composite,
            _ => true
        };
        return matchesType && (string.IsNullOrWhiteSpace(ScriptLibrarySearchText) ||
            script.Name.Contains(ScriptLibrarySearchText.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    private void ApplyScriptLibrarySort()
    {
        using (ScriptLibraryView.DeferRefresh())
        {
            ScriptLibraryView.SortDescriptions.Clear();
            if (SelectedScriptLibrarySortMode != ScriptLibrarySortMode.Default)
            {
                var direction = SelectedScriptLibrarySortMode == ScriptLibrarySortMode.NameAscending
                    ? ListSortDirection.Ascending
                    : ListSortDirection.Descending;
                ScriptLibraryView.SortDescriptions.Add(
                    new SortDescription(nameof(ScriptItemViewModel.Name), direction));
            }
        }
        OnPropertyChanged(nameof(SelectedScript));
        RequestScriptSelectionRestore();
        RaiseCommandStates();
    }

    private void RefreshScriptLibraryView()
    {
        ScriptLibraryView.Refresh();
        OnPropertyChanged(nameof(SelectedScript));
        RequestScriptSelectionRestore();
        RaiseCommandStates();
    }

    private bool CanMutateComposite => IsCompositeScriptSelected && CanChangeSelection &&
                                       !IsEditorPersistenceBusy && !IsScriptPersistenceBlocked &&
                                       !compositeMutationBusy;

    private async Task CreateCompositeScriptAsync()
    {
        if (!await ResolvePendingEditorChangesAsync()) return;
        var transaction = CaptureLibraryMutationTransaction();
        var script = new ScriptDefinition
        {
            Name = $"Kịch bản gộp {Scripts.Count(item => item.Model.Kind == ScriptKind.Composite) + 1}",
            Kind = ScriptKind.Composite
        };
        var item = new ScriptItemViewModel(script);
        Scripts.Add(item);
        RefreshScriptCollections();
        SelectedScript = item;
        await SaveScriptsWithRollbackAsync(transaction);
        EnsureScriptVisible(item);
        RequestScriptSelectionRestore(focus: true);
    }

    private async Task AddCompositeReferenceAsync()
    {
        if (!await ResolveCompositeEditorChangesAsync()) return;
        var referenceScript = CompositeReferenceScript ?? RegularScripts.FirstOrDefault();
        if (referenceScript is null) return;
        await AddCompositeItemAsync(new ScriptReferenceItem { ScriptId = referenceScript.Id });
    }

    private async Task AddCompositeDelayAsync()
    {
        if (!await ResolveCompositeEditorChangesAsync()) return;
        await AddCompositeItemAsync(new CompositeDelayItem
            { DurationMilliseconds = Math.Max(0, CompositeDelayMilliseconds) });
    }

    private async Task AddCompositeItemAsync(CompositeScriptItem item)
    {
        if (!await ResolveCompositeEditorChangesAsync() || !TryBeginCompositeMutation()) return;
        try
        {
            var transaction = CaptureCompositeMutationTransaction();
            var before = transaction.Snapshot;
            var viewModel = CreateCompositeItem(item);
            CompositeItems.Add(viewModel);
            PushCompositeUndo(before);
            SetCompositeSelection([viewModel], viewModel);
            await PersistCompositeMutationAsync(transaction);
        }
        finally { EndCompositeMutation(); }
    }

    private async Task SaveCompositeItemAsync()
    {
        if (SelectedCompositeItem is null || !TryBeginCompositeMutation()) return;
        var target = SelectedCompositeItem;
        var owner = SelectedScript!;
        var targetVersion = compositeEditorVersion;
        var savedDraft = CaptureCompositeEditorDraft();
        var previousModel = ScriptCloner.CloneCompositeItemPreservingId(target.Model);
        var previousUpdatedAt = owner.Model.UpdatedAt;
        using var persistence = BeginEditorPersistence();
        try
        {
            var before = CaptureCompositeSnapshot();
            CompositeScriptItem replacement = target.Model switch
            {
                ScriptReferenceItem when CompositeReferenceScript is not null => new ScriptReferenceItem
                {
                    Id = target.Id,
                    IsEnabled = target.IsEnabled,
                    ScriptId = CompositeReferenceScript.Id,
                    ContinueOnFailure = CompositeContinueOnFailure
                },
                CompositeDelayItem => new CompositeDelayItem
                {
                    Id = target.Id,
                    IsEnabled = target.IsEnabled,
                    DurationMilliseconds = CompositeDelayMilliseconds >= 0
                        ? CompositeDelayMilliseconds
                        : throw new ArgumentOutOfRangeException(nameof(CompositeDelayMilliseconds), "Thời gian chờ không được âm.")
                },
                _ => throw new InvalidOperationException("Hãy chọn một kịch bản thường hợp lệ.")
            };
            target.ReplaceModel(replacement);
            PushCompositeUndo(before);
            try { await PersistCompositeMutationAsync(); }
            catch
            {
                target.ReplaceModel(previousModel);
                owner.Model.CompositeItems.Clear();
                owner.Model.CompositeItems.AddRange(CompositeItems.Select(item => item.Model));
                owner.Model.UpdatedAt = previousUpdatedAt;
                owner.Refresh();
                if (GetCompositeHistory(owner.Id).Count > 0)
                    GetCompositeHistory(owner.Id).RemoveLast();
                throw;
            }
            if (ReferenceEquals(target, SelectedCompositeItem))
            {
                compositeEditorBaseline = savedDraft;
                if (compositeEditorVersion == targetVersion) SetCompositeEditorDirty(false);
                else RefreshCompositeEditorDirty();
            }
            StatusMessage = IsCompositeEditorDirty ? "Đã lưu mục; còn thay đổi chưa lưu." : "Đã lưu mục.";
        }
        finally { EndCompositeMutation(); }
    }

    private async Task DeleteCompositeItemsAsync()
    {
        var items = GetSelectedCompositeItems();
        if (items.Count == 0 || !await ResolveCompositeEditorChangesAsync() ||
            !confirmationService.Confirm($"Xóa {items.Count} mục khỏi kịch bản gộp?", "Xác nhận xóa")) return;
        if (!TryBeginCompositeMutation()) return;
        try
        {
            var transaction = CaptureCompositeMutationTransaction();
            var before = transaction.Snapshot;
            var firstIndex = items.Select(CompositeItems.IndexOf).Where(index => index >= 0).DefaultIfEmpty(0).Min();
            foreach (var item in items) CompositeItems.Remove(item);
            var next = CompositeItems.Count == 0 ? null : CompositeItems[Math.Min(firstIndex, CompositeItems.Count - 1)];
            PushCompositeUndo(before);
            SetCompositeSelection(next is null ? [] : [next], next);
            await PersistCompositeMutationAsync(transaction);
        }
        finally { EndCompositeMutation(); }
    }

    private async Task MoveCompositeItemsAsync(int direction)
    {
        var group = GetSelectedCompositeItems();
        if (group.Count == 0 || !CanMoveComposite(direction) ||
            !await ResolveCompositeEditorChangesAsync() || !TryBeginCompositeMutation()) return;
        try
        {
            var transaction = CaptureCompositeMutationTransaction();
            var before = transaction.Snapshot;
            var indexes = group.Select(CompositeItems.IndexOf).OrderBy(index => index).ToList();
            if (direction < 0)
            {
                foreach (var index in indexes) CompositeItems.Move(index, index - 1);
            }
            else
            {
                foreach (var index in indexes.OrderByDescending(index => index)) CompositeItems.Move(index, index + 1);
            }
            PushCompositeUndo(before);
            SetCompositeSelection(group, SelectedCompositeItem ?? group[0]);
            await PersistCompositeMutationAsync(transaction);
        }
        finally { EndCompositeMutation(); }
    }

    public async Task MoveCompositeItemToAsync(CompositeItemViewModel item, int insertionIndex)
    {
        if (!CanMutateComposite || !CompositeItems.Contains(item)) return;
        var group = GetSelectedCompositeItems();
        if (!group.Contains(item)) group = [item];
        var originalIndexes = group.Select(CompositeItems.IndexOf).OrderBy(index => index).ToList();
        var adjustedIndex = insertionIndex - originalIndexes.Count(index => index < insertionIndex);
        var remaining = CompositeItems.Where(candidate => !group.Contains(candidate)).ToList();
        adjustedIndex = Math.Clamp(adjustedIndex, 0, remaining.Count);
        remaining.InsertRange(adjustedIndex, group);
        if (remaining.SequenceEqual(CompositeItems)) return;
        if (!await ResolveCompositeEditorChangesAsync() || !TryBeginCompositeMutation()) return;
        try
        {
            var transaction = CaptureCompositeMutationTransaction();
            var before = transaction.Snapshot;
            for (var index = 0; index < remaining.Count; index++)
            {
                var current = CompositeItems.IndexOf(remaining[index]);
                if (current != index) CompositeItems.Move(current, index);
            }
            PushCompositeUndo(before);
            SetCompositeSelection(group, SelectedCompositeItem ?? group[0]);
            await PersistCompositeMutationAsync(transaction);
        }
        finally { EndCompositeMutation(); }
    }

    public bool CanDragCompositeItem(CompositeItemViewModel item) =>
        CanMutateComposite && GetSelectedCompositeItems().Contains(item);

    private bool CanMoveComposite(int direction)
    {
        if (!CanMutateComposite) return false;
        var group = GetSelectedCompositeItems();
        if (group.Count == 0) return false;
        var indexes = group.Select(CompositeItems.IndexOf).OrderBy(index => index).ToList();
        return direction < 0 ? indexes[0] > 0 : indexes[^1] < CompositeItems.Count - 1;
    }

    private void CopyCompositeItems()
    {
        var source = GetSelectedCompositeItems();
        var visibleCopies = new List<CompositeScriptItem>(source.Count);
        foreach (var item in source)
        {
            if (ReferenceEquals(item, SelectedCompositeItem) &&
                (IsCompositeEditorDirty || HasInvalidCompositeEditorDraft || IsEditorPersistenceBusy))
            {
                if (HasInvalidCompositeEditorDraft)
                {
                    StatusMessage = "Không thể sao chép vì dữ liệu mục gộp đang hiển thị không hợp lệ.";
                    return;
                }
                visibleCopies.Add(CreateVisibleCompositeDraft(item));
            }
            else visibleCopies.Add(item.Model);
        }
        copiedCompositeItems = visibleCopies.Select(ScriptCloner.CloneCompositeItem).ToList();
        OnPropertyChanged(nameof(HasCopiedCompositeItems));
        OnPropertyChanged(nameof(CompositeClipboardSummary));
        RaiseCompositeCommandStates();
    }

    private async Task PasteCompositeItemsAsync()
    {
        if (!CompositeReferencesAreValid(copiedCompositeItems))
        {
            StatusMessage = "Không thể dán vì clipboard mục gộp chứa tham chiếu không còn hợp lệ.";
            return;
        }
        if (!await ResolveCompositeEditorChangesAsync() || !TryBeginCompositeMutation()) return;
        try
        {
            var transaction = CaptureCompositeMutationTransaction();
            var before = transaction.Snapshot;
            var selectedIndexes = GetSelectedCompositeItems().Select(CompositeItems.IndexOf).ToList();
            var insertionIndex = selectedIndexes.Count == 0 ? CompositeItems.Count : selectedIndexes.Max() + 1;
            var pasted = copiedCompositeItems.Select(item => CreateCompositeItem(ScriptCloner.CloneCompositeItem(item))).ToList();
            for (var index = 0; index < pasted.Count; index++) CompositeItems.Insert(insertionIndex + index, pasted[index]);
            PushCompositeUndo(before);
            SetCompositeSelection(pasted, pasted.FirstOrDefault());
            await PersistCompositeMutationAsync(transaction);
        }
        finally { EndCompositeMutation(); }
    }

    private async Task UndoCompositeItemsAsync()
    {
        if (SelectedScript is null || !await ResolveCompositeEditorChangesAsync() || !TryBeginCompositeMutation()) return;
        try
        {
            var transaction = CaptureCompositeMutationTransaction();
            var history = GetCompositeHistory(SelectedScript.Id);
            if (history.Count == 0) return;
            var snapshot = history.Last!.Value;
            if (!CompositeReferencesAreValid(snapshot.Items))
            {
                StatusMessage = "Không thể hoàn tác vì snapshot tham chiếu kịch bản thường không còn tồn tại.";
                return;
            }
            history.RemoveLast();
            ApplyCompositeSnapshot(snapshot);
            await PersistCompositeMutationAsync(transaction);
        }
        finally { EndCompositeMutation(); }
    }

    private bool CanUndoCompositeItems() => CanMutateComposite && SelectedScript is not null &&
        compositeHistories.TryGetValue(SelectedScript.Id, out var history) && history.Count > 0;

    private async Task OpenReferencedScriptAsync()
    {
        if (SelectedCompositeItem?.Model is not ScriptReferenceItem reference) return;
        await NavigateToScriptAsync(Scripts.FirstOrDefault(script => script.Id == reference.ScriptId));
    }

    public void SynchronizeSelectedCompositeItems(IEnumerable<CompositeItemViewModel> selection)
    {
        var previous = GetSelectedCompositeItems();
        var normalized = selection.Where(CompositeItems.Contains).Distinct().OrderBy(CompositeItems.IndexOf).ToList();
        var requestedPrimary = SelectedCompositeItem is not null && normalized.Contains(SelectedCompositeItem)
            ? SelectedCompositeItem
            : normalized.FirstOrDefault();
        if (!ReferenceEquals(requestedPrimary, SelectedCompositeItem))
            SelectedCompositeItem = requestedPrimary;
        if (!ReferenceEquals(requestedPrimary, SelectedCompositeItem))
        {
            CompositeSelectionRestoreRequested?.Invoke(previous);
            return;
        }
        selectedCompositeItems.Clear();
        selectedCompositeItems.AddRange(normalized);
        OnPropertyChanged(nameof(SelectedCompositeItemCount));
        RaiseCompositeCommandStates();
    }

    public bool TryClearCompositeSelection()
    {
        return TryClearCompositeSelectionFromBlank();
    }

    private void LoadCompositeWorkspace()
    {
        CompositeItems.Clear();
        selectedCompositeItems.Clear();
        if (SelectedScript?.Model.Kind == ScriptKind.Composite)
            foreach (var item in SelectedScript.Model.CompositeItems) CompositeItems.Add(CreateCompositeItem(item));
        SelectedCompositeItem = CompositeItems.FirstOrDefault();
        RefreshScriptCollections();
    }

    private CompositeItemViewModel CreateCompositeItem(CompositeScriptItem item)
    {
        var viewModel = new CompositeItemViewModel(item, id => Scripts.FirstOrDefault(script => script.Id == id)?.Name);
        viewModel.IsEnabledChanging += OnCompositeEnabledChanging;
        viewModel.IsEnabledChanged += OnCompositeEnabledChanged;
        return viewModel;
    }

    private void OnCompositeEnabledChanging(object? sender, StepEnabledChangingEventArgs args)
    {
        if (!CanMutateComposite || SelectedScript is null)
        {
            args.Cancel = true;
            return;
        }

        pendingCompositeToggleTransaction = CaptureCompositeMutationTransaction();
        SetCompositeMutationBusy(true);
    }

    private async void OnCompositeEnabledChanged(object? sender, EventArgs e)
    {
        try
        {
            if (pendingCompositeToggleTransaction is null) return;
            PushCompositeUndo(pendingCompositeToggleTransaction.Snapshot);
            await PersistCompositeMutationAsync(pendingCompositeToggleTransaction);
        }
        catch (Exception exception) { ReportUnexpectedError(exception); }
        finally
        {
            pendingCompositeToggleTransaction = null;
            EndCompositeMutation();
        }
    }

    private async Task PersistCompositeMutationAsync()
    {
        if (SelectedScript is null) return;
        SelectedScript.Model.CompositeItems.Clear();
        SelectedScript.Model.CompositeItems.AddRange(CompositeItems.Select(item => item.Model));
        ScriptLibraryValidator.Validate(Scripts.Select(item => item.Model).ToList());
        TouchSelectedScript();
        await SaveScriptsAsync();
        RaiseCompositeCommandStates();
    }

    private async Task PersistCompositeMutationAsync(CompositeMutationTransaction transaction)
    {
        var persistence = BeginEditorPersistence();
        try
        {
            transaction.Owner.Model.CompositeItems.Clear();
            transaction.Owner.Model.CompositeItems.AddRange(CompositeItems.Select(item => item.Model));
            ScriptLibraryValidator.Validate(Scripts.Select(item => item.Model).ToList());
            TouchScript(transaction.Owner);
            await SaveScriptsAsync();
        }
        catch
        {
            persistence.Dispose();
            RestoreCompositeMutationTransaction(transaction);
            throw;
        }
        finally { persistence.Dispose(); }
        RaiseCompositeCommandStates();
    }

    private CompositeListSnapshot CaptureCompositeSnapshot()
    {
        var selection = GetSelectedCompositeItems();
        return new CompositeListSnapshot(
            CompositeItems.Select(item => ScriptCloner.CloneCompositeItemPreservingId(item.Model)).ToList(),
            selection.Select(item => item.Id).ToList(),
            SelectedCompositeItem?.Id);
    }

    private CompositeMutationTransaction CaptureCompositeMutationTransaction()
    {
        var owner = SelectedScript ?? throw new InvalidOperationException("Chưa chọn kịch bản gộp để thay đổi.");
        var hadHistory = compositeHistories.TryGetValue(owner.Id, out var history);
        return new CompositeMutationTransaction(
            owner,
            CaptureCompositeSnapshot(),
            owner.Model.UpdatedAt,
            hadHistory,
            history?.ToList() ?? []);
    }

    private void RestoreCompositeMutationTransaction(CompositeMutationTransaction transaction)
    {
        ApplyCompositeSnapshot(transaction.Snapshot);
        transaction.Owner.Model.CompositeItems.Clear();
        transaction.Owner.Model.CompositeItems.AddRange(CompositeItems.Select(item => item.Model));
        transaction.Owner.Model.UpdatedAt = transaction.UpdatedAt;
        transaction.Owner.Refresh();

        if (!transaction.HadHistory)
        {
            compositeHistories.Remove(transaction.Owner.Id);
        }
        else
        {
            var history = GetCompositeHistory(transaction.Owner.Id);
            history.Clear();
            foreach (var snapshot in transaction.History) history.AddLast(snapshot);
        }
        RaiseCompositeCommandStates();
    }

    private void ApplyCompositeSnapshot(CompositeListSnapshot snapshot)
    {
        CompositeItems.Clear();
        foreach (var item in snapshot.Items.Select(ScriptCloner.CloneCompositeItemPreservingId))
            CompositeItems.Add(CreateCompositeItem(item));
        var ids = snapshot.SelectedIds.ToHashSet();
        var selection = CompositeItems.Where(item => ids.Contains(item.Id)).ToList();
        var primary = snapshot.PrimaryId is Guid primaryId
            ? selection.FirstOrDefault(item => item.Id == primaryId)
            : selection.FirstOrDefault();
        SetCompositeSelection(selection, primary);
    }

    private void PushCompositeUndo(CompositeListSnapshot snapshot)
    {
        if (SelectedScript is null) return;
        var history = GetCompositeHistory(SelectedScript.Id);
        history.AddLast(snapshot);
        while (history.Count > StepHistoryLimit) history.RemoveFirst();
        RaiseCompositeCommandStates();
    }

    private LinkedList<CompositeListSnapshot> GetCompositeHistory(Guid scriptId)
    {
        if (!compositeHistories.TryGetValue(scriptId, out var history))
            compositeHistories[scriptId] = history = [];
        return history;
    }

    private List<CompositeItemViewModel> GetSelectedCompositeItems()
    {
        var items = selectedCompositeItems.Where(CompositeItems.Contains).Distinct().OrderBy(CompositeItems.IndexOf).ToList();
        if (items.Count == 0 && SelectedCompositeItem is not null && CompositeItems.Contains(SelectedCompositeItem))
            items.Add(SelectedCompositeItem);
        return items;
    }

    private bool HasInvalidCompositeEditorDraft => SelectedCompositeItem?.Model switch
    {
        ScriptReferenceItem => CompositeReferenceScript is null || HasEditorBindingErrors,
        CompositeDelayItem => !IsCompositeDelayInputValid || HasEditorBindingErrors,
        _ => false
    };

    private void MarkCompositeEditorDirty()
    {
        if (suppressCompositeEditorDirty || SelectedCompositeItem is null) return;
        compositeEditorVersion++;
        RefreshCompositeEditorDirty();
        NotifyCompositeEditorDraftContentChanged();
    }

    private void NotifyCompositeEditorDraftContentChanged() => RaiseCommandStates();

    private CompositeEditorDraftSnapshot CaptureCompositeEditorDraft() => SelectedCompositeItem?.Model switch
    {
        ScriptReferenceItem => new CompositeEditorDraftSnapshot(
            CompositeEditorDraftKind.Reference,
            CompositeReferenceScript?.Id,
            CompositeContinueOnFailure,
            0),
        CompositeDelayItem => new CompositeEditorDraftSnapshot(
            CompositeEditorDraftKind.Delay,
            null,
            false,
            CompositeDelayMilliseconds),
        _ => new CompositeEditorDraftSnapshot(CompositeEditorDraftKind.None, null, false, 0)
    };

    private void AcceptCompositeEditorBaseline(CompositeEditorDraftSnapshot? snapshot = null)
    {
        compositeEditorBaseline = snapshot ?? CaptureCompositeEditorDraft();
        SetCompositeEditorDirty(false);
    }

    private void RefreshCompositeEditorDirty()
    {
        if (suppressCompositeEditorDirty) return;
        if (SelectedCompositeItem is null)
        {
            SetCompositeEditorDirty(false);
            return;
        }
        SetCompositeEditorDirty(compositeEditorBaseline is null ||
                                compositeEditorBaseline != CaptureCompositeEditorDraft());
    }

    private CompositeScriptItem CreateVisibleCompositeDraft(CompositeItemViewModel target) => target.Model switch
    {
        ScriptReferenceItem when CompositeReferenceScript is not null => new ScriptReferenceItem
        {
            Id = target.Id,
            IsEnabled = target.IsEnabled,
            ScriptId = CompositeReferenceScript.Id,
            ContinueOnFailure = CompositeContinueOnFailure
        },
        CompositeDelayItem => new CompositeDelayItem
        {
            Id = target.Id,
            IsEnabled = target.IsEnabled,
            DurationMilliseconds = CompositeDelayMilliseconds
        },
        _ => throw new InvalidOperationException("Dữ liệu mục gộp đang hiển thị không hợp lệ.")
    };

    private void SetCompositeEditorDirty(bool value)
    {
        if (!SetProperty(ref isCompositeEditorDirty, value, nameof(IsCompositeEditorDirty))) return;
        OnPropertyChanged(nameof(HasAnyEditorDraft));
        OnPropertyChanged(nameof(HasCompositeEditorDraft));
        OnPropertyChanged(nameof(EditorSaveState));
        OnPropertyChanged(nameof(RunConfigurationError));
        RaiseCommandStates();
    }

    private void SetCompositeSelection(IReadOnlyCollection<CompositeItemViewModel> selection, CompositeItemViewModel? primary)
    {
        selectedCompositeItems.Clear();
        selectedCompositeItems.AddRange(selection);
        SelectedCompositeItem = primary;
        OnPropertyChanged(nameof(SelectedCompositeItemCount));
        CompositeSelectionRestoreRequested?.Invoke(selection.ToList());
        RaiseCompositeCommandStates();
    }

    private bool TryBeginCompositeMutation()
    {
        if (!CanMutateComposite || HasInvalidCompositeEditorDraft) return false;
        SetCompositeMutationBusy(true);
        return true;
    }

    private void EndCompositeMutation() => SetCompositeMutationBusy(false);

    private void SetCompositeMutationBusy(bool value)
    {
        if (compositeMutationBusy == value) return;
        compositeMutationBusy = value;
        RaiseCompositeCommandStates();
    }

    private void RefreshScriptCollections()
    {
        var referenceId = CompositeReferenceScript?.Id;
        var desired = Scripts.Where(script => script.Model.Kind == ScriptKind.Regular).ToList();
        suppressCompositeEditorDirty = true;
        try
        {
            for (var index = 0; index < desired.Count; index++)
            {
                if (index < RegularScripts.Count && ReferenceEquals(RegularScripts[index], desired[index])) continue;
                var existingIndex = RegularScripts.IndexOf(desired[index]);
                if (existingIndex >= 0) RegularScripts.Move(existingIndex, index);
                else RegularScripts.Insert(index, desired[index]);
            }
            while (RegularScripts.Count > desired.Count) RegularScripts.RemoveAt(RegularScripts.Count - 1);
            CompositeReferenceScript = referenceId is Guid id
                ? RegularScripts.FirstOrDefault(script => script.Id == id)
                : CompositeReferenceScript;
        }
        finally { suppressCompositeEditorDirty = false; }
        RefreshCompositeEditorDirty();
        RefreshScriptLibraryView();
        OnPropertyChanged(nameof(IsRegularScriptSelected));
        OnPropertyChanged(nameof(IsCompositeScriptSelected));
    }

    private void RaiseCompositeCommandStates()
    {
        CreateCompositeScriptCommand?.RaiseCanExecuteChanged();
        AddCompositeReferenceCommand?.RaiseCanExecuteChanged();
        AddCompositeDelayCommand?.RaiseCanExecuteChanged();
        SaveCompositeItemCommand?.RaiseCanExecuteChanged();
        DeleteCompositeItemsCommand?.RaiseCanExecuteChanged();
        MoveCompositeItemUpCommand?.RaiseCanExecuteChanged();
        MoveCompositeItemDownCommand?.RaiseCanExecuteChanged();
        CopyCompositeItemsCommand?.RaiseCanExecuteChanged();
        PasteCompositeItemsCommand?.RaiseCanExecuteChanged();
        UndoCompositeItemsCommand?.RaiseCanExecuteChanged();
        OpenReferencedScriptCommand?.RaiseCanExecuteChanged();
    }

    private bool CompositeReferencesAreValid(IEnumerable<CompositeScriptItem> items)
    {
        var regularIds = Scripts.Where(script => script.Model.Kind == ScriptKind.Regular)
            .Select(script => script.Id).ToHashSet();
        return items.OfType<ScriptReferenceItem>().All(reference => regularIds.Contains(reference.ScriptId));
    }

    private async Task<bool> ResolveCompositeEditorChangesAsync()
    {
        await WaitForEditorPersistenceAsync();
        if (!HasCompositeEditorDraft) return true;
        var canSave = !HasInvalidCompositeEditorDraft;
        var decision = confirmationService.DecideEditorDraft("Thuộc tính mục gộp", canSave);
        switch (decision)
        {
            case EditorDraftDecision.Save:
                await SaveCompositeItemAsync();
                return !IsCompositeEditorDirty;
            case EditorDraftDecision.Discard:
                ReloadCompositeEditorFromSelected();
                return true;
            default:
                return false;
        }
    }

    private void ReloadCompositeEditorFromSelected()
    {
        suppressCompositeEditorDirty = true;
        try
        {
            CompositeReferenceScript = SelectedCompositeItem?.Model is ScriptReferenceItem reference
                ? RegularScripts.FirstOrDefault(script => script.Id == reference.ScriptId)
                : null;
            CompositeDelayMilliseconds = SelectedCompositeItem?.Model is CompositeDelayItem delay
                ? delay.DurationMilliseconds
                : 1000;
            CompositeContinueOnFailure = SelectedCompositeItem?.Model is ScriptReferenceItem referenceItem &&
                                         referenceItem.ContinueOnFailure;
            IsCompositeDelayInputValid = true;
        }
        finally { suppressCompositeEditorDirty = false; }
        compositeDelayInputRefreshToken = unchecked(compositeDelayInputRefreshToken + 1);
        OnPropertyChanged(nameof(CompositeDelayInputRefreshToken));
        HasEditorBindingErrors = false;
        SelectedCompositeItem?.ClearDraftPreview();
        AcceptCompositeEditorBaseline();
    }

    private sealed record CompositeListSnapshot(
        IReadOnlyList<CompositeScriptItem> Items,
        IReadOnlyList<Guid> SelectedIds,
        Guid? PrimaryId);

    private sealed record CompositeMutationTransaction(
        ScriptItemViewModel Owner,
        CompositeListSnapshot Snapshot,
        DateTimeOffset UpdatedAt,
        bool HadHistory,
        IReadOnlyList<CompositeListSnapshot> History);

    private enum CompositeEditorDraftKind { None, Reference, Delay }

    private sealed record CompositeEditorDraftSnapshot(
        CompositeEditorDraftKind Kind,
        Guid? ReferenceScriptId,
        bool ContinueOnFailure,
        int DelayMilliseconds);
}
