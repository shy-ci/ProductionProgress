namespace MossHarbor.ProductionProgress.Core;

public enum ProductionKind { Crop, Regrowth, Machine, Tapper, Aging }
public enum ProductionState { Working, Ready, Withered }
public enum ProgressClock { Daily, ProcessingMinutes }

public readonly record struct BatchKey(
    string Location, string ItemId, string FlavorId, int PreserveKind,
    ProductionKind Kind, ProductionState State, ProgressClock Clock,
    int FromQuality, int ToQuality, double Total, double Remaining, long Due,
    double Rate, string Rule, int? CompletedDay = null, int? CompletedTime = null, int? ObservedDay = null);

public sealed class ProgressRow
{
    public long Id { get; set; }
    public string Location { get; set; } = "";
    public string ItemId { get; set; } = "";
    public string FlavorId { get; set; } = "";
    public int PreserveKind { get; set; } = -1;
    public string Name { get; set; } = "";
    public int Count { get; set; }
    public ProductionKind Kind { get; set; }
    public ProductionState State { get; set; }
    public ProgressClock Clock { get; set; }
    public int FromQuality { get; set; }
    public int ToQuality { get; set; }
    public double Total { get; set; }
    public double Remaining { get; set; }
    public long Due { get; set; }
    public double Rate { get; set; } = 1;
    public int? CompletedDay { get; set; }
    public int? CompletedTime { get; set; }
    public int? ObservedDay { get; set; }

    public CompletionDate? DisplayDate(long now)
    {
        if (State == ProductionState.Ready)
            return CompletedDay.HasValue ? new CompletionDate(CompletedDay.Value, CompletedTime) : null;
        if (State != ProductionState.Working) return null;
        int today = (int)(now / 1600);
        if (EstimatedClock(now) is FinishClock finish) return new CompletionDate(today + finish.Days, finish.TimeOfDay);
        if (Clock == ProgressClock.Daily && EstimatedDays(now) is int days) return new CompletionDate((ObservedDay ?? today) + days, null);
        return null;
    }

    public double? Fraction(long now) => State == ProductionState.Withered ? null
        : ProgressMath.Fraction(Total, Clock == ProgressClock.ProcessingMinutes ? Math.Max(0, Due - now) : Remaining, State == ProductionState.Ready);

    public FinishClock? EstimatedClock(long now) => State == ProductionState.Working && Clock == ProgressClock.ProcessingMinutes
        ? ProgressMath.CompletionClock(now, Due) : null;

    public int? EstimatedDays(long now)
    {
        if (State != ProductionState.Working) return null;
        if (Clock == ProgressClock.ProcessingMinutes)
            return Due > 0 ? ProgressMath.DaysUntilDeadline(now, Due) : null;
        if (Total <= 0 || !double.IsFinite(Remaining) || Remaining < 0 || !double.IsFinite(Rate) || Rate <= 0) return null;
        double days = Math.Ceiling(Remaining / Rate);
        return days <= int.MaxValue ? (int)days : null;
    }

    public ProgressRow Copy() => (ProgressRow)MemberwiseClone();
}

public sealed class ChangePacket
{
    public int Protocol { get; set; } = 1;
    public string Session { get; set; } = "";
    public bool Full { get; set; }
    public long Revision { get; set; }
    public bool Initializing { get; set; }
    public List<ProgressRow> Rows { get; set; } = new();
    public List<long> Removed { get; set; } = new();
}
