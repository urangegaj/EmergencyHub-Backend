using System.Collections.Concurrent;

namespace AssessmentService.Kafka;

public sealed class AssignmentCache
{
    private readonly ConcurrentDictionary<Guid, List<string>> _store = new();

    public void Add(Guid emergencyId, string departmentType)
        => _store.AddOrUpdate(emergencyId,
            _ => [departmentType],
            (_, existing) => { lock (existing) { existing.Add(departmentType); } return existing; });

    public List<string>? Get(Guid emergencyId)
        => _store.TryGetValue(emergencyId, out var depts) ? depts : null;

    public void Remove(Guid emergencyId) => _store.TryRemove(emergencyId, out _);
}
