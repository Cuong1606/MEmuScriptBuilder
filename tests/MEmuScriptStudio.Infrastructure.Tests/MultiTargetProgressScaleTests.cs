using MEmuScriptStudio.App.ViewModels;
using MEmuScriptStudio.Core.Models;

namespace MEmuScriptStudio.Infrastructure.Tests;

[TestClass]
public sealed class MultiTargetProgressScaleTests
{
    [DataTestMethod]
    [DataRow(3)]
    [DataRow(20)]
    [DataRow(50)]
    [DataRow(100)]
    public void ProgressBurstKeepsOnePostedDrainAndBoundedStatePerTarget(int targetCount)
    {
        var context = new QueuedContext();
        var applied = new List<InstanceExecutionUpdate>();
        var pump = new InstanceExecutionProgressPump(context, applied.Add);
        var groupId = Guid.NewGuid();

        foreach (var index in Enumerable.Range(0, targetCount))
        {
            var targetKey = $"memu:{index}";
            pump.Report(Update(groupId, index, targetKey, InstanceExecutionStatus.Running));
            foreach (var burst in Enumerable.Range(0, 200))
            {
                pump.Report(Update(
                    groupId,
                    index,
                    targetKey,
                    InstanceExecutionStatus.Running,
                    new StepExecutionUpdate(Guid.NewGuid(), StepExecutionStatus.Succeeded)));
            }
            pump.Report(Update(groupId, index, targetKey, InstanceExecutionStatus.Succeeded));
        }

        Assert.AreEqual(1, context.PostCount);
        context.DrainAll();

        Assert.IsTrue(applied.Count <= targetCount * 3);
        Assert.AreEqual(targetCount, applied.Select(update => update.TargetKey).Distinct().Count());
        Assert.IsTrue(Enumerable.Range(0, targetCount).All(index =>
            applied.Last(update => update.TargetKey == $"memu:{index}").Status ==
            InstanceExecutionStatus.Succeeded));
    }

    private static InstanceExecutionUpdate Update(
        Guid groupId,
        int index,
        string targetKey,
        InstanceExecutionStatus status,
        StepExecutionUpdate? stepUpdate = null) =>
        new(groupId, index, $"VM {index}", status, stepUpdate)
        {
            TargetKey = targetKey,
            DeviceKind = DeviceKind.MEmu,
            TargetIdentifier = index.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };

    private sealed class QueuedContext : SynchronizationContext
    {
        private readonly Queue<(SendOrPostCallback Callback, object? State)> callbacks = [];
        public int PostCount { get; private set; }

        public override void Post(SendOrPostCallback d, object? state)
        {
            PostCount++;
            callbacks.Enqueue((d, state));
        }

        public void DrainAll()
        {
            while (callbacks.TryDequeue(out var callback))
                callback.Callback(callback.State);
        }
    }
}
