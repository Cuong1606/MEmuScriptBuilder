using System.Collections.Specialized;
using MEmuScriptStudio.App.ViewModels;

namespace MEmuScriptStudio.Infrastructure.Tests;

[TestClass]
public sealed class RangeObservableCollectionTests
{
    [TestMethod]
    public void AddAndRemoveRangeEachRaiseOneResetForLargeFixture()
    {
        var collection = new RangeObservableCollection<int>();
        var changes = new List<NotifyCollectionChangedAction>();
        collection.CollectionChanged += (_, args) => changes.Add(args.Action);

        collection.AddRange(Enumerable.Range(0, 100));
        collection.RemoveRange(Enumerable.Range(0, 100).Where(value => value % 2 == 0));

        CollectionAssert.AreEqual(
            new[] { NotifyCollectionChangedAction.Reset, NotifyCollectionChangedAction.Reset },
            changes);
        CollectionAssert.AreEqual(
            Enumerable.Range(0, 100).Where(value => value % 2 != 0).ToArray(),
            collection.ToArray());
    }
}
