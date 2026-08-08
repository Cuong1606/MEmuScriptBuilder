using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace MEmuScriptStudio.App.ViewModels;

internal sealed class RangeObservableCollection<T> : ObservableCollection<T>
{
    public void AddRange(IEnumerable<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        var additions = items.ToList();
        if (additions.Count == 0) return;

        CheckReentrancy();
        foreach (var item in additions) Items.Add(item);
        NotifyReset();
    }

    public void RemoveRange(IEnumerable<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        var removals = items.ToHashSet();
        if (removals.Count == 0) return;
        var survivors = this.Where(item => !removals.Contains(item)).ToList();
        if (survivors.Count == Count) return;

        CheckReentrancy();
        Items.Clear();
        foreach (var item in survivors) Items.Add(item);
        NotifyReset();
    }

    private void NotifyReset()
    {
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}
