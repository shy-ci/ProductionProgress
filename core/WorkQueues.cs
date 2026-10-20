namespace MossHarbor.ProductionProgress.Core;

public sealed class DirtyQueue<T> where T : notnull
{
    private readonly Queue<T> queue = new();
    private readonly HashSet<T> pending = new();
    public int Count => pending.Count;
    public void Add(T item) { if (pending.Add(item)) queue.Enqueue(item); }
    public bool TryTake(out T item)
    {
        if (queue.TryDequeue(out item!)) { pending.Remove(item); return true; }
        return false;
    }
    public void Clear() { queue.Clear(); pending.Clear(); }
}

public sealed class DueSchedule<T> where T : notnull
{
    private sealed record Entry(long At, long Order, T Key);
    private readonly SortedSet<Entry> entries = new(Comparer<Entry>.Create((a, b) =>
        a.At != b.At ? a.At.CompareTo(b.At) : a.Order.CompareTo(b.Order)));
    private readonly Dictionary<T, Entry> byKey = new();
    private long sequence;
    public int Count => entries.Count;
    public void Set(T key, long at)
    {
        Remove(key);
        var entry = new Entry(at, ++sequence, key);
        byKey[key] = entry; entries.Add(entry);
    }
    public void Remove(T key) { if (byKey.Remove(key, out Entry? e)) entries.Remove(e); }
    public bool TryTake(long now, out T key)
    {
        if (entries.Min is Entry e && e.At <= now)
        {
            entries.Remove(e); byKey.Remove(e.Key); key = e.Key; return true;
        }
        key = default!; return false;
    }
    public void Clear() { entries.Clear(); byKey.Clear(); }
}
