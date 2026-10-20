using System.Text.Json;
using MossHarbor.ProductionProgress.Core;
using Xunit;

namespace MossHarbor.ProductionProgress.Tests;

public sealed class CompletionDateTests
{
    [Fact]
    public void DailyDatesRemainStableUntilTheDailyStateIsRefreshed()
    {
        var row = new ProgressRow { Clock = ProgressClock.Daily, Total = 4, Remaining = 3, ObservedDay = 68 };
        Assert.Equal(new CompletionDate(71, null), row.DisplayDate(ProgressMath.ProcessingClock(68, 1800)));
        Assert.Equal(new CompletionDate(71, null), row.DisplayDate(ProgressMath.ProcessingClock(69, 600)));
        row.ObservedDay = 69; row.Remaining = 2;
        Assert.Equal(new CompletionDate(71, null), row.DisplayDate(ProgressMath.ProcessingClock(69, 600)));
    }

    [Fact]
    public void CompletedMinuteDateDoesNotAdvanceWhenLeftUncollected()
    {
        var memory = new CompletionMemory();
        memory.Observe(ProductionState.Working, ProductionKind.Machine, ProgressClock.ProcessingMinutes, 68, 800, 0);
        memory.Observe(ProductionState.Ready, ProductionKind.Machine, ProgressClock.ProcessingMinutes, 68, 930, 0);
        memory.Observe(ProductionState.Ready, ProductionKind.Machine, ProgressClock.ProcessingMinutes, 75, 1600, 0);
        Assert.Equal(68, memory.Day); Assert.Equal(930, memory.TimeOfDay);
        var row = new ProgressRow { State = ProductionState.Ready, CompletedDay = memory.Day, CompletedTime = memory.TimeOfDay };
        Assert.Equal(new CompletionDate(68, 930), row.DisplayDate(ProgressMath.ProcessingClock(100, 600)));
        Assert.Equal(1, row.Fraction(ProgressMath.ProcessingClock(100, 600)));
    }

    [Fact]
    public void DailyCompletionHasDateWithoutAnInventedHour()
    {
        var memory = new CompletionMemory();
        memory.Observe(ProductionState.Working, ProductionKind.Crop, ProgressClock.Daily, 68, 1800, 0);
        memory.Observe(ProductionState.Ready, ProductionKind.Crop, ProgressClock.Daily, 71, 600, 0);
        Assert.Equal(71, memory.Day); Assert.Null(memory.TimeOfDay);
    }

    [Fact]
    public void CompletedAgingKeepsTheStageOriginQuality()
    {
        var memory = new CompletionMemory();
        memory.Observe(ProductionState.Working, ProductionKind.Aging, ProgressClock.Daily, 68, 600, 1);
        memory.Observe(ProductionState.Ready, ProductionKind.Aging, ProgressClock.Daily, 77, 600, 2);
        Assert.Equal(1, memory.AgingFromQuality);
        memory.Observe(ProductionState.Ready, ProductionKind.Aging, ProgressClock.Daily, 80, 600, 2);
        Assert.Equal(77, memory.Day); Assert.Equal(1, memory.AgingFromQuality);
    }

    [Fact]
    public void NewlyDiscoveredReadyItemsDoNotInventHistoricalDates()
    {
        var memory = new CompletionMemory();
        memory.Observe(ProductionState.Ready, ProductionKind.Machine, ProgressClock.ProcessingMinutes, 68, 930, 0);
        Assert.Null(memory.Day);
        Assert.Null(new ProgressRow { State = ProductionState.Ready }.DisplayDate(68 * 1600));
        memory.Observe(ProductionState.Ready, ProductionKind.Machine, ProgressClock.ProcessingMinutes, 69, 600, 0);
        Assert.Null(memory.Day);
    }

    [Fact]
    public void RestartedCycleDoesNotReuseThePreviousCompletion()
    {
        var memory = new CompletionMemory(); memory.Restore(68, 930, null);
        memory.BeginCycle(); // two notifications can be coalesced into one final ready snapshot
        memory.Observe(ProductionState.Ready, ProductionKind.Machine, ProgressClock.ProcessingMinutes, 69, 1000, 0);
        Assert.Equal(69, memory.Day); Assert.Equal(1000, memory.TimeOfDay);
        memory.Reset();
        memory.Observe(ProductionState.Ready, ProductionKind.Machine, ProgressClock.ProcessingMinutes, 70, 600, 0);
        Assert.Null(memory.Day);
    }

    [Fact]
    public void RestorePreservesEvenTheFirstGameDayAndStageOrigin()
    {
        var memory = new CompletionMemory(); memory.Restore(0, null, 1);
        memory.Observe(ProductionState.Ready, ProductionKind.Aging, ProgressClock.Daily, 15, 1800, 2);
        Assert.Equal(0, memory.Day); Assert.Null(memory.TimeOfDay); Assert.Equal(1, memory.AgingFromQuality);
    }

    [Fact]
    public void CompletedBatchesWithDifferentDatesStaySeparateAndSerializeDates()
    {
        var store = new BatchStore();
        var key = new BatchKey("Farm", "(O)348", "268", 0, ProductionKind.Machine, ProductionState.Ready,
            ProgressClock.ProcessingMinutes, 0, 0, 1, 0, 0, 1, "wine", 68, 930);
        store.Set(1, key, "Wine"); store.Set(2, key, "Wine");
        store.Set(3, key with { CompletedDay = 69 }, "Wine");
        Assert.Equal(2, store.BatchCount);
        var copied = JsonSerializer.Deserialize<ChangePacket>(JsonSerializer.Serialize(store.TakeChanges(false)))!;
        Assert.Equal(2, copied.Rows.Count);
        Assert.Contains(copied.Rows, r => r.CompletedDay == 68 && r.CompletedTime == 930 && r.Count == 2);
        Assert.Contains(copied.Rows, r => r.CompletedDay == 69 && r.CompletedTime == 930 && r.Count == 1);
    }
}
