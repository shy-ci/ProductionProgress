namespace MossHarbor.ProductionProgress.Core;

public sealed class BatchStore
{
    private readonly Dictionary<long, BatchKey> membership = new();
    private readonly Dictionary<BatchKey, ProgressRow> groups = new();
    private readonly Dictionary<long, ProgressRow> changed = new();
    private readonly HashSet<long> removed = new();
    private long nextId;
    public long Revision { get; private set; }
    public int SourceCount => membership.Count;
    public int BatchCount => groups.Count;
    public bool HasChanges => changed.Count != 0 || removed.Count != 0;

    public void Set(long source, BatchKey key, string name)
    {
        if (membership.TryGetValue(source, out BatchKey old) && old == key)
            return;
        Remove(source);
        if (!groups.TryGetValue(key, out ProgressRow? row))
        {
            row = new ProgressRow { Id = ++nextId, Location = key.Location, ItemId = key.ItemId,
                FlavorId = key.FlavorId, PreserveKind = key.PreserveKind, Name = name, Kind = key.Kind,
                State = key.State, Clock = key.Clock, FromQuality = key.FromQuality, ToQuality = key.ToQuality,
                Total = key.Total, Remaining = key.Remaining, Due = key.Due, Rate = key.Rate,
                CompletedDay = key.CompletedDay, CompletedTime = key.CompletedTime, ObservedDay = key.ObservedDay };
            groups.Add(key, row);
        }
        membership[source] = key;
        row.Count++;
        changed[row.Id] = row;
        removed.Remove(row.Id);
        Revision++;
    }

    public void Remove(long source)
    {
        if (!membership.Remove(source, out BatchKey key)) return;
        ProgressRow row = groups[key];
        if (--row.Count == 0)
        {
            groups.Remove(key);
            changed.Remove(row.Id);
            removed.Add(row.Id);
        }
        else changed[row.Id] = row;
        Revision++;
    }

    public List<ProgressRow> Snapshot() => groups.Values.Select(x => x.Copy()).ToList();

    // Read-only diagnostics for one source. Never expose a mutable cached row.
    public ProgressRow? GetSourceRow(long source)
        => membership.TryGetValue(source, out BatchKey key) ? groups[key].Copy() : null;

    public ChangePacket TakeChanges(bool initializing)
    {
        var packet = new ChangePacket { Revision = Revision, Initializing = initializing,
            Rows = changed.Values.Select(x => x.Copy()).ToList(), Removed = removed.ToList() };
        changed.Clear(); removed.Clear();
        return packet;
    }

    public void Clear()
    {
        membership.Clear(); groups.Clear(); changed.Clear(); removed.Clear();
        Revision++; // don't reuse row identities within the service lifetime
    }
}
