using System.Text.Json;
using MossHarbor.ProductionProgress.Core;
using Xunit;

namespace MossHarbor.ProductionProgress.Tests;

public sealed class SunnyProgressTests
{
    private static ProgressRow Panel(double total = 8800, double remaining = 4000) => new()
    {
        Id = 1, Count = 1, Location = "Farm", ItemId = "(O)787", Name = "Battery Pack",
        Kind = ProductionKind.Machine, State = ProductionState.Working,
        Clock = ProgressClock.SunnyMinutes, Total = total, Remaining = remaining
    };

    [Theory]
    [InlineData(1, 1)]
    [InlineData(1600, 1)]
    [InlineData(1601, 2)]
    [InlineData(4000, 3)]
    public void RemainingWorkDoesNotRequireAnObservedCycleStart(double remaining, int days)
    {
        var row = Panel(total: 0, remaining);
        Assert.Equal(days, row.RemainingSunnyDays());
        Assert.Null(row.Fraction(0)); // report useful remaining work without fabricating a denominator
        Assert.Null(row.DisplayDate(0));
        Assert.Null(row.EstimatedDays(0));
        Assert.Null(row.EstimatedClock(0));
    }

    [Fact]
    public void PassingRainyDaysCannotAdvanceProgressWithoutMachineChanges()
    {
        var row = Panel();
        long today = ProgressMath.ProcessingClock(7, 600);
        long afterRain = ProgressMath.ProcessingClock(10, 1800);
        Assert.Equal(row.Fraction(today), row.Fraction(afterRain));
        Assert.Equal(3, row.RemainingSunnyDays());
        Assert.Null(row.DisplayDate(afterRain));
        row.Remaining = 2400; // one real active day's work, as reported by the game
        Assert.Equal(2, row.RemainingSunnyDays());
        Assert.Equal(1 - 2400d / 8800, row.Fraction(afterRain));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(-1)]
    [InlineData(double.MaxValue)]
    public void InvalidRemainingWorkDoesNotInventSunnyDays(double remaining)
        => Assert.Null(Panel(remaining: remaining).RemainingSunnyDays());

    [Fact]
    public void SolarAndLightningBatteriesKeepDifferentTiming()
    {
        var panel = Panel();
        var lightning = panel.Copy();
        lightning.Id = 2; lightning.Clock = ProgressClock.ProcessingMinutes; lightning.Due = 560;
        Assert.Equal(2, ProgressDisplayGroups.Create(new[] { panel, lightning }, 0).Count);
        Assert.Null(lightning.RemainingSunnyDays());
        Assert.NotNull(lightning.DisplayDate(0));
    }

    [Fact]
    public void DifferentLocationsDoNotMergeByAnUncertainCalendarDate()
    {
        var farm = Panel();
        var island = farm.Copy(); island.Id = 2; island.Location = "IslandWest";
        Assert.Equal(2, ProgressDisplayGroups.Create(new[] { farm, island }, 0).Count);
        island.Remaining -= 1600; // island weather can differ from the farm's weather
        Assert.Equal(3, farm.RemainingSunnyDays());
        Assert.Equal(2, island.RemainingSunnyDays());
    }

    [Fact]
    public void ReadyPanelsKeepActualCompletionDatesAndRoundTripThroughSnapshots()
    {
        var row = Panel();
        row.State = ProductionState.Ready; row.CompletedDay = 12; row.CompletedTime = 1530;
        var packet = new ChangePacket { Full = true, Session = "test", Revision = 1, Rows = new() { row } };
        var copy = JsonSerializer.Deserialize<ChangePacket>(JsonSerializer.Serialize(packet))!;
        var remote = new RemoteView(); remote.Apply(copy);
        var received = Assert.Single(remote.Rows);
        Assert.Null(received.RemainingSunnyDays());
        Assert.Equal(1, received.Fraction(20 * 1600));
        Assert.Equal(new CompletionDate(12, 1530), received.DisplayDate(20 * 1600));
    }
}
