using System.Collections.Concurrent;

namespace EmergencyService.Services;

// NOTE: In-process only. A multi-instance deployment would need an out-of-process
// signal bus (e.g. Redis Pub/Sub). Correct for the current single-instance deployment.
public sealed class PollRegistry
{
    private readonly ConcurrentDictionary<Guid, List<TaskCompletionSource>> _waiters = new();
    private readonly object _gate = new();

    public TaskCompletionSource Subscribe(Guid emergencyId)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
            _waiters.GetOrAdd(emergencyId, _ => new List<TaskCompletionSource>()).Add(tcs);
        return tcs;
    }

    public void Unsubscribe(Guid emergencyId, TaskCompletionSource tcs)
    {
        lock (_gate)
        {
            if (_waiters.TryGetValue(emergencyId, out var list))
            {
                list.Remove(tcs);
                if (list.Count == 0)
                    _waiters.TryRemove(emergencyId, out _);
            }
        }
    }

    public void Signal(Guid emergencyId)
    {
        List<TaskCompletionSource>? snapshot;
        lock (_gate)
        {
            if (!_waiters.TryGetValue(emergencyId, out var list)) return;
            snapshot = list.ToList();
        }
        foreach (var tcs in snapshot)
            tcs.TrySetResult();
    }
}
