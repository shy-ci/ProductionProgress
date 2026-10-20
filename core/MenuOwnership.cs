namespace MossHarbor.ProductionProgress.Core;

/// <summary>A temporary UI value we may undo only while it still equals our last write.</summary>
public sealed class OwnedValue<T>
{
    public T Original { get; }
    public T Applied { get; private set; }
    public OwnedValue(T original, T applied) { Original = original; Applied = applied; }
    public bool Owns(T current) => EqualityComparer<T>.Default.Equals(current, Applied);
    public T Restore(T current) => Owns(current) ? Original : current;
    public bool TryUpdate(T current, T next)
    {
        if (!Owns(current)) return false;
        Applied = next;
        return true;
    }
}

public static class MenuOwnership
{
    public static int IndexOfReference<T>(IReadOnlyList<T> items, T item) where T : class
    {
        for (int i = 0; i < items.Count; i++) if (ReferenceEquals(items[i], item)) return i;
        return -1;
    }

    public static int AvailableId(IEnumerable<int> occupied, int preferred)
    {
        var ids = new HashSet<int>(occupied);
        int candidate = Math.Max(1, preferred);
        while (ids.Contains(candidate)) candidate = candidate == int.MaxValue ? 1 : candidate + 1;
        return candidate;
    }
}
