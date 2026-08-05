using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using MEmuScriptStudio.Core.Models;
using MEmuScriptStudio.Core.Scripts;

namespace MEmuScriptStudio.App.ViewModels;

public enum ScriptLibraryFilter
{
    All,
    Regular,
    Composite
}

public sealed partial class MainViewModel
{
    public event Action<IReadOnlyList<CompositeItemViewModel>>? CompositeSelectionRestoreRequested;
    private readonly List<CompositeItemViewModel> selectedCompositeItems = [];
    private readonly Dictionary<Guid, LinkedList<CompositeListSnapshot>> compositeHistories = [];
    private IReadOnlyList<CompositeScriptItem> copiedCompositeItems = [];
    private CompositeItemViewModel? selectedCompositeItem;
    private ScriptItemViewModel? compositeReferenceScript;
    private int compositeDelayMilliseconds = 1000;
    private bool compositeContinueOnFailure;
    private ScriptLibraryFilter scriptLibraryFilter;
    private bool compositeMutationBusy;

    public ObservableCollection<CompositeItemViewModel> CompositeItems { get; } = [];
    public ObservableCollection<ScriptItemViewModel> RegularScripts { get; } = [];
    public IReadOnlyList<ScriptLibraryFilter> ScriptLibraryFilters { get; } = Enum.GetValues<ScriptLibraryFilter>();
    public ICollectionView ScriptLibraryView { get; private set; } = null!;
    public bool IsRegularScriptSelected => SelectedScript?.Model.Kind == ScriptKind.Regular;
    public bool IsCompositeScriptSelected => SelectedScript?.Model.Kind == ScriptKind.Composite;
    public int SelectedCompositeItemCount => selectedCompositeItems.Count;
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
            ScriptLibraryView.Refresh();
        }
    }

    public CompositeItemViewModel? SelectedCompositeItem
    {
        get => selectedCompositeItem;
        set
        {
            if (!SetProperty(ref selectedCompositeItem, value)) return;
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
            RaiseCompositeCommandStates();
        }
    }

    public ScriptItemViewModel? CompositeReferenceScript
    {
        get => compositeReferenceScript;
        set => SetProperty(ref compositeReferenceScript, value);
    }

    public int CompositeDelayMilliseconds
    {
        get => compositeDelayMilliseconds;
        set => SetProperty(ref compositeDelayMilliseconds, value);
    }

    public bool CompositeContinueOnFailure
    {
        get => compositeContinueOnFailure;
        set => SetProperty(ref compositeContinueOnFailure, value);
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
    public RelayCommand OpenReferencedScriptCommand { get; private set; } = null!;

    private void InitializeCompositeWorkspace()
    {
        ScriptLibraryView = CollectionViewSource.GetDefaultView(Scripts);
        ScriptLibraryView.Filter = item => item is ScriptItemViewModel script && ScriptLibraryFilter switch
        {
            ScriptLibraryFilter.Regular => script.Model.Kind == ScriptKind.Regular,
            ScriptLibraryFilter.Composite => script.Model.Kind == ScriptKind.Composite,
            _ => true
        };
        CreateCompositeScriptCommand = new AsyncCommand(CreateCompositeScriptAsync,
            () => !IsCapturing, ReportUnexpectedError);
        AddCompositeReferenceCommand = new AsyncCommand(AddCompositeReferenceAsync,
            () => CanMutateComposite && RegularScripts.Count > 0, ReportUnexpectedError);
        AddCompositeDelayCommand = new AsyncCommand(AddCompositeDelayAsync, () => CanMutateComposite, ReportUnexpectedError);
        SaveCompositeItemCommand = new AsyncCommand(SaveCompositeItemAsync,
            () => CanMutateComposite && SelectedCompositeItem is not null, ReportUnexpectedError);
        DeleteCompositeItemsCommand = new AsyncCommand(DeleteCompositeItemsAsync,
            () => CanMutateComposite && selectedCompositeItems.Count > 0, ReportUnexpectedError);
        MoveCompositeItemUpCommand = new AsyncCommand(() => MoveCompositeItemsAsync(-1), () => CanMoveComposite(-1), ReportUnexpectedError);
        MoveCompositeItemDownCommand = new AsyncCommand(() => MoveCompositeItemsAsync(1), () => CanMoveComposite(1), ReportUnexpectedError);
        CopyCompositeItemsCommand = new RelayCommand(CopyCompositeItems,
            () => IsCompositeScriptSelected && selectedCompositeItems.Count > 0 && CanChangeSelection);
        PasteCompositeItemsCommand = new AsyncCommand(PasteCompositeItemsAsync,
            () => CanMutateComposite && copiedCompositeItems.Count > 0, ReportUnexpectedError);
        UndoCompositeItemsCommand = new AsyncCommand(UndoCompositeItemsAsync, CanUndoCompositeItems, ReportUnexpectedError);
        OpenReferencedScriptCommand = new RelayCommand(OpenReferencedScript,
            () => SelectedCompositeItem?.Model is ScriptReferenceItem);
        RefreshScriptCollections();
    }

    private bool CanMutateComposite => IsCompositeScriptSelected && CanChangeSelection && !compositeMutationBusy;

    private async Task CreateCompositeScriptAsync()
    {
        if (!TryDiscardEditorChangesForMutation()) return;
        var script = new ScriptDefinition
        {
            Name = $"Kịch bản gộp {Scripts.Count(item => item.Model.Kind == ScriptKind.Composite) + 1}",
            Kind = ScriptKind.Composite
        };
        var item = new ScriptItemViewModel(script);
        Scripts.Add(item);
        RefreshScriptCollections();
        SelectedScript = item;
        await SaveScriptsAsync();
    }

    private async Task AddCompositeReferenceAsync()
    {
        var referenceScript = CompositeReferenceScript ?? RegularScripts.FirstOrDefault();
        if (referenceScript is null) return;
        await AddCompositeItemAsync(new ScriptReferenceItem { ScriptId = referenceScript.Id });
    }

    private Task AddCompositeDelayAsync() =>
        AddCompositeItemAsync(new CompositeDelayItem { DurationMilliseconds = Math.Max(0, CompositeDelayMilliseconds) });

    private async Task AddCompositeItemAsync(CompositeScriptItem item)
    {
        if (!TryBeginCompositeMutation()) return;
        try
        {
            var before = CaptureCompositeSnapshot();
            var viewModel = CreateCompositeItem(item);
            CompositeItems.Add(viewModel);
            PushCompositeUndo(before);
            SetCompositeSelection([viewModel], viewModel);
            await PersistCompositeMutationAsync();
        }
        finally { EndCompositeMutation(); }
    }

    private async Task SaveCompositeItemAsync()
    {
        if (SelectedCompositeItem is null || !TryBeginCompositeMutation()) return;
        try
        {
            var before = CaptureCompositeSnapshot();
            CompositeScriptItem replacement = SelectedCompositeItem.Model switch
            {
                ScriptReferenceItem when CompositeReferenceScript is not null => new ScriptReferenceItem
                {
                    Id = SelectedCompositeItem.Id,
                    IsEnabled = SelectedCompositeItem.IsEnabled,
                    ScriptId = CompositeReferenceScript.Id,
                    ContinueOnFailure = CompositeContinueOnFailure
                },
                CompositeDelayItem => new CompositeDelayItem
                {
                    Id = SelectedCompositeItem.Id,
                    IsEnabled = SelectedCompositeItem.IsEnabled,
                    DurationMilliseconds = CompositeDelayMilliseconds >= 0
                        ? CompositeDelayMilliseconds
                        : throw new ArgumentOutOfRangeException(nameof(CompositeDelayMilliseconds), "Thời gian chờ không được âm.")
                },
                _ => throw new InvalidOperationException("Hãy chọn một kịch bản thường hợp lệ.")
            };
            SelectedCompositeItem.ReplaceModel(replacement);
            PushCompositeUndo(before);
            await PersistCompositeMutationAsync();
        }
        finally { EndCompositeMutation(); }
    }

    private async Task DeleteCompositeItemsAsync()
    {
        var items = GetSelectedCompositeItems();
        if (items.Count == 0 || !confirmationService.Confirm($"Xóa {items.Count} mục khỏi kịch bản gộp?", "Xác nhận xóa")) return;
        if (!TryBeginCompositeMutation()) return;
        try
        {
            var before = CaptureCompositeSnapshot();
            var firstIndex = items.Select(CompositeItems.IndexOf).Where(index => index >= 0).DefaultIfEmpty(0).Min();
            foreach (var item in items) CompositeItems.Remove(item);
            var next = CompositeItems.Count == 0 ? null : CompositeItems[Math.Min(firstIndex, CompositeItems.Count - 1)];
            PushCompositeUndo(before);
            SetCompositeSelection(next is null ? [] : [next], next);
            await PersistCompositeMutationAsync();
        }
        finally { EndCompositeMutation(); }
    }

    private async Task MoveCompositeItemsAsync(int direction)
    {
        var group = GetSelectedCompositeItems();
        if (group.Count == 0 || !CanMoveComposite(direction) || !TryBeginCompositeMutation()) return;
        try
        {
            var before = CaptureCompositeSnapshot();
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
            await PersistCompositeMutationAsync();
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
        if (!TryBeginCompositeMutation()) return;
        try
        {
            var before = CaptureCompositeSnapshot();
            for (var index = 0; index < remaining.Count; index++)
            {
                var current = CompositeItems.IndexOf(remaining[index]);
                if (current != index) CompositeItems.Move(current, index);
            }
            PushCompositeUndo(before);
            SetCompositeSelection(group, SelectedCompositeItem ?? group[0]);
            await PersistCompositeMutationAsync();
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
        copiedCompositeItems = GetSelectedCompositeItems()
            .Select(item => ScriptCloner.CloneCompositeItem(item.Model)).ToList();
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
        if (!TryBeginCompositeMutation()) return;
        try
        {
            var before = CaptureCompositeSnapshot();
            var selectedIndexes = GetSelectedCompositeItems().Select(CompositeItems.IndexOf).ToList();
            var insertionIndex = selectedIndexes.Count == 0 ? CompositeItems.Count : selectedIndexes.Max() + 1;
            var pasted = copiedCompositeItems.Select(item => CreateCompositeItem(ScriptCloner.CloneCompositeItem(item))).ToList();
            for (var index = 0; index < pasted.Count; index++) CompositeItems.Insert(insertionIndex + index, pasted[index]);
            PushCompositeUndo(before);
            SetCompositeSelection(pasted, pasted.FirstOrDefault());
            await PersistCompositeMutationAsync();
        }
        finally { EndCompositeMutation(); }
    }

    private async Task UndoCompositeItemsAsync()
    {
        if (SelectedScript is null || !TryBeginCompositeMutation()) return;
        try
        {
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
            await PersistCompositeMutationAsync();
        }
        finally { EndCompositeMutation(); }
    }

    private bool CanUndoCompositeItems() => CanMutateComposite && SelectedScript is not null &&
        compositeHistories.TryGetValue(SelectedScript.Id, out var history) && history.Count > 0;

    private void OpenReferencedScript()
    {
        if (SelectedCompositeItem?.Model is not ScriptReferenceItem reference) return;
        SelectedScript = Scripts.FirstOrDefault(script => script.Id == reference.ScriptId);
    }

    public void SynchronizeSelectedCompositeItems(IEnumerable<CompositeItemViewModel> selection)
    {
        var normalized = selection.Where(CompositeItems.Contains).Distinct().OrderBy(CompositeItems.IndexOf).ToList();
        selectedCompositeItems.Clear();
        selectedCompositeItems.AddRange(normalized);
        if (SelectedCompositeItem is null || !normalized.Contains(SelectedCompositeItem))
            SelectedCompositeItem = normalized.FirstOrDefault();
        OnPropertyChanged(nameof(SelectedCompositeItemCount));
        RaiseCompositeCommandStates();
    }

    public bool TryClearCompositeSelection()
    {
        if (!CanChangeSelection) return false;
        SetCompositeSelection([], null);
        CompositeSelectionRestoreRequested?.Invoke([]);
        return true;
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
        viewModel.IsEnabledChanged += OnCompositeEnabledChanged;
        return viewModel;
    }

    private async void OnCompositeEnabledChanged(object? sender, EventArgs e)
    {
        try
        {
            if (sender is CompositeItemViewModel changed)
            {
                var before = CaptureCompositeSnapshot();
                var previous = before.Items.First(item => item.Id == changed.Id);
                previous.IsEnabled = !changed.IsEnabled;
                PushCompositeUndo(before);
            }
            await PersistCompositeMutationAsync();
        }
        catch (Exception exception) { ReportUnexpectedError(exception); }
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

    private CompositeListSnapshot CaptureCompositeSnapshot() => new(
        CompositeItems.Select(item => ScriptCloner.CloneCompositeItemPreservingId(item.Model)).ToList(),
        selectedCompositeItems.Where(CompositeItems.Contains).Select(item => item.Id).ToList(),
        SelectedCompositeItem?.Id);

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
        if (!CanMutateComposite) return false;
        compositeMutationBusy = true;
        RaiseCompositeCommandStates();
        return true;
    }

    private void EndCompositeMutation()
    {
        compositeMutationBusy = false;
        RaiseCompositeCommandStates();
    }

    private void RefreshScriptCollections()
    {
        RegularScripts.Clear();
        foreach (var script in Scripts.Where(script => script.Model.Kind == ScriptKind.Regular)) RegularScripts.Add(script);
        ScriptLibraryView?.Refresh();
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

    private sealed record CompositeListSnapshot(
        IReadOnlyList<CompositeScriptItem> Items,
        IReadOnlyList<Guid> SelectedIds,
        Guid? PrimaryId);
}
