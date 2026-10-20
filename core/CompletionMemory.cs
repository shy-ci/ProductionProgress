namespace MossHarbor.ProductionProgress.Core;

public readonly record struct CompletionDate(int Day, int? TimeOfDay);

/// <summary>Observed completion of one production cycle; reopening a menu never advances this date.</summary>
public sealed class CompletionMemory
{
    public ProductionState? LastState { get; private set; }
    public int? Day { get; private set; }
    public int? TimeOfDay { get; private set; }
    public int? AgingFromQuality { get; private set; }
    private int? workingQuality;

    public void Observe(ProductionState state, ProductionKind kind, ProgressClock clock,
        int day, int time, int quality, bool observedNewCycle = false)
    {
        if (state == ProductionState.Working)
        {
            Day = null; TimeOfDay = null; AgingFromQuality = null;
            workingQuality = kind == ProductionKind.Aging ? quality : null;
        }
        else if (state == ProductionState.Ready)
        {
            if (!Day.HasValue && (LastState == ProductionState.Working || observedNewCycle))
            {
                Day = day;
                TimeOfDay = clock == ProgressClock.ProcessingMinutes ? time : null;
            }
            if (kind == ProductionKind.Aging) AgingFromQuality ??= workingQuality ?? quality;
        }
        LastState = state;
    }

    public void Restore(int day, int? time, int? agingFromQuality)
    { Reset(); Day = day; TimeOfDay = time; AgingFromQuality = agingFromQuality; LastState = ProductionState.Ready; }

    public void Reset()
    { Day = null; TimeOfDay = null; AgingFromQuality = null; workingQuality = null; LastState = null; }

    public void BeginCycle() { Reset(); LastState = ProductionState.Working; }
}
