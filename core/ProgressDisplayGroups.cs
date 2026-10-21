using System.Globalization;

namespace MossHarbor.ProductionProgress.Core;

public sealed record ProgressDisplayGroup(ProgressRow Current, int CurrentCount, int Count)
{
    public string Quantity => CurrentCount < Count
        ? CurrentCount.ToString(CultureInfo.InvariantCulture) + "/" + Count.ToString(CultureInfo.InvariantCulture)
        : Count.ToString(CultureInfo.InvariantCulture);
}

// Display-only grouping. Tracking and network snapshots keep precise cycles and locations.
public static class ProgressDisplayGroups
{
    private readonly record struct Key(string ItemId, string FlavorId, int PreserveKind,
        ProductionKind Kind, ProductionState State, ProgressClock Clock, int FromQuality, int ToQuality,
        int? FinishDay, long UnknownCycle);

    public static List<ProgressDisplayGroup> Create(IEnumerable<ProgressRow> rows, long now)
    {
        var groups = rows.Where(r => r.Count > 0).GroupBy(r => new Key(r.ItemId, r.FlavorId, r.PreserveKind,
            r.Kind == ProductionKind.Regrowth ? ProductionKind.Crop : r.Kind, r.State, r.Clock,
            r.FromQuality, r.ToQuality, r.DisplayDate(now)?.Day,
            r.State == ProductionState.Working && r.DisplayDate(now) == null ? r.Id : 0));
        var result = new List<ProgressDisplayGroup>();
        foreach (var group in groups)
        {
            // Same-day cycles show the earliest completion, never an averaged percentage.
            // Tied finish times use the most advanced actual cycle, then stable row identity.
            ProgressRow first = group.OrderBy(r => r.DisplayDate(now)?.TimeOfDay ?? 0)
                .ThenByDescending(r => r.Fraction(now) ?? -1).ThenBy(r => r.Id).First();
            int count = group.Sum(r => r.Count);
            CompletionDate? firstDate = first.DisplayDate(now);
            int currentCount = first.State == ProductionState.Working
                ? group.Where(r => r.DisplayDate(now) == firstDate).Sum(r => r.Count) : count;
            ProgressRow display = first.Copy();
            display.Kind = group.Key.Kind;
            // A completed day's group may contain multiple actual completion times.
            // Preserve its known date without presenting the first time as everyone's time.
            if (display.State == ProductionState.Ready && group.Any(r => r.CompletedTime != first.CompletedTime))
                display.CompletedTime = null;
            result.Add(new(display, currentCount, count));
        }
        return result;
    }

}
