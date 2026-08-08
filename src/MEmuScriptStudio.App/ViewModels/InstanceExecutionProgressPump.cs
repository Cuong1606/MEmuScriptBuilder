using MEmuScriptStudio.Core.Models;

namespace MEmuScriptStudio.App.ViewModels;

internal sealed class InstanceExecutionProgressPump(
    SynchronizationContext? synchronizationContext,
    Action<InstanceExecutionUpdate> handler) : IProgress<InstanceExecutionUpdate>
{
    private readonly object gate = new();
    private readonly Dictionary<(Guid LaunchGroupId, string TargetKey), PendingInstanceUpdates> pendingByTarget = [];
    private bool drainPosted;

    internal int PostedDrainCount { get; private set; }

    public void Report(InstanceExecutionUpdate update)
    {
        if (synchronizationContext is null)
        {
            handler(update);
            return;
        }

        var shouldPostDrain = false;
        lock (gate)
        {
            var key = (update.LaunchGroupId, update.TargetKey);
            if (!pendingByTarget.TryGetValue(key, out var pending))
            {
                pending = new PendingInstanceUpdates();
                pendingByTarget.Add(key, pending);
            }

            pending.Add(update);
            if (drainPosted) return;
            drainPosted = true;
            PostedDrainCount++;
            shouldPostDrain = true;
        }

        if (shouldPostDrain)
            synchronizationContext.Post(static state => ((InstanceExecutionProgressPump)state!).Drain(), this);
    }

    internal void DrainPending() => Drain();

    private void Drain()
    {
        IReadOnlyList<InstanceExecutionUpdate> updates;
        lock (gate)
        {
            updates = pendingByTarget.Values.SelectMany(pending => pending.TakeAll()).ToList();
            pendingByTarget.Clear();
            drainPosted = false;
        }

        foreach (var update in updates) handler(update);
    }

    private sealed class PendingInstanceUpdates
    {
        private readonly Queue<InstanceExecutionUpdate> orderedUpdates = [];
        private PendingUpdate? latestIntermediate;
        private PendingUpdate? latestImportantIntermediate;
        private InstanceExecutionStatus lastObservedStatus;
        private bool hasObservedStatus;
        private bool hasObservedTerminal;
        private long sequence;

        public void Add(InstanceExecutionUpdate update)
        {
            var updateSequence = ++sequence;
            var isTerminal = IsTerminal(update.Status);
            if (hasObservedTerminal && !isTerminal) return;

            var statusChanged = !hasObservedStatus || lastObservedStatus != update.Status;
            var isImportantStep = update.StepUpdate?.Status is StepExecutionStatus.Failed or StepExecutionStatus.Cancelled;
            var isImportantIntermediate = isImportantStep || !string.IsNullOrWhiteSpace(update.Message);

            hasObservedStatus = true;
            lastObservedStatus = update.Status;
            hasObservedTerminal |= isTerminal;

            if (!isTerminal && !statusChanged)
            {
                if (isImportantIntermediate)
                    latestImportantIntermediate = new PendingUpdate(updateSequence, update);
                else
                    latestIntermediate = new PendingUpdate(updateSequence, update);
                return;
            }

            FlushIntermediate();
            orderedUpdates.Enqueue(update);
        }

        public IReadOnlyList<InstanceExecutionUpdate> TakeAll()
        {
            FlushIntermediate();
            return orderedUpdates.ToArray();
        }

        private void FlushIntermediate()
        {
            if (latestIntermediate is null && latestImportantIntermediate is null) return;
            if (latestIntermediate is { } intermediate && latestImportantIntermediate is { } important)
            {
                if (intermediate.Sequence < important.Sequence)
                {
                    orderedUpdates.Enqueue(intermediate.Update);
                    orderedUpdates.Enqueue(important.Update);
                }
                else
                {
                    orderedUpdates.Enqueue(important.Update);
                    orderedUpdates.Enqueue(intermediate.Update);
                }
            }
            else if (latestIntermediate is { } onlyIntermediate)
                orderedUpdates.Enqueue(onlyIntermediate.Update);
            else if (latestImportantIntermediate is { } onlyImportant)
                orderedUpdates.Enqueue(onlyImportant.Update);

            latestIntermediate = null;
            latestImportantIntermediate = null;
        }

        private static bool IsTerminal(InstanceExecutionStatus status) =>
            status is InstanceExecutionStatus.Succeeded or InstanceExecutionStatus.Failed or
                InstanceExecutionStatus.Cancelled or InstanceExecutionStatus.Unavailable;

        private sealed record PendingUpdate(long Sequence, InstanceExecutionUpdate Update);
    }
}
