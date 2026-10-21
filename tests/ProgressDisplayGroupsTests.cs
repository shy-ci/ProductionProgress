using MossHarbor.ProductionProgress.Core;
using Xunit;

namespace MossHarbor.ProductionProgress.Tests;

public sealed class ProgressDisplayGroupsTests
{
    private const long Now = 20 * 1600;

    private static ProgressRow Crop(long id, int count, string location = "Farm") => new()
    {
        Id = id, Count = count, Location = location, Name = "Pineapple", ItemId = "(O)832",
        Kind = ProductionKind.Regrowth, Clock = ProgressClock.Daily, Total = 7,
        Remaining = 3, ObservedDay = 20
    };

    private static ProgressRow Wine(long id, int count, int minutes) => new()
    {
        Id = id, Count = count, Name = "Starfruit Wine", ItemId = "(O)348", FlavorId = "268", PreserveKind = 0,
        Kind = ProductionKind.Machine, Clock = ProgressClock.ProcessingMinutes,
        Total = 10000, Due = Now + minutes
    };

    [Fact]
    public void PineapplesFromDifferentLocationsAndTrackingBatchesShareOneDailyRow()
    {
        var rows = new[] { Crop(1, 5), Crop(2, 1), Crop(3, 5, "Greenhouse"), Crop(4, 8, "IslandWest") };
        ProgressDisplayGroup group = Assert.Single(ProgressDisplayGroups.Create(rows, Now));
        Assert.Equal("19", group.Quantity);
        Assert.Equal(ProductionKind.Crop, group.Current.Kind);
        Assert.Equal(new CompletionDate(23, null), group.Current.DisplayDate(Now));
        Assert.Equal(4d / 7, group.Current.Fraction(Now));
        Assert.All(rows, row => Assert.Equal(ProductionKind.Regrowth, row.Kind)); // projection never mutates tracking
    }

    [Fact]
    public void FirstGrowthAndRegrowthUseActualEarliestCycleInsteadOfAveraging()
    {
        var regrowth = Crop(1, 5);
        var firstGrowth = Crop(2, 1);
        firstGrowth.Kind = ProductionKind.Crop;
        firstGrowth.Total = 14;
        ProgressDisplayGroup group = Assert.Single(ProgressDisplayGroups.Create(new[] { regrowth, firstGrowth }, Now));
        Assert.Equal("6", group.Quantity); // all mature at the same morning, despite different growth lengths
        Assert.Equal(11d / 14, group.Current.Fraction(Now));
        Assert.Equal(ProductionKind.Crop, group.Current.Kind);
    }

    [Fact]
    public void HundredsOfStaggeredMachinesUseEarliestBatchAndAdvanceAfterCompletionAndCollection()
    {
        var early = Wine(1, 80, 560); // 15:20
        var middle = Wine(2, 200, 570);
        var late = Wine(3, 220, 580);
        var rows = new List<ProgressRow> { late, early, middle };
        ProgressDisplayGroup group = Assert.Single(ProgressDisplayGroups.Create(rows, Now));
        Assert.Equal("80/500", group.Quantity);
        Assert.Equal(0.944, group.Current.Fraction(Now));
        Assert.Equal(new CompletionDate(20, 1520), group.Current.DisplayDate(Now));

        early.State = ProductionState.Ready; early.CompletedDay = 20; early.CompletedTime = 1520;
        var groups = ProgressDisplayGroups.Create(rows, Now + 560);
        var working = Assert.Single(groups, g => g.Current.State == ProductionState.Working);
        Assert.Equal("200/420", working.Quantity);
        Assert.Equal(new CompletionDate(20, 1530), working.Current.DisplayDate(Now + 560));
        Assert.Equal("80", Assert.Single(groups, g => g.Current.State == ProductionState.Ready).Quantity);
        rows.Remove(early); // collection removes only the completed sources
        Assert.Equal("200/420", Assert.Single(ProgressDisplayGroups.Create(rows, Now + 560)).Quantity);
    }

    [Fact]
    public void DifferentProductsFlavorsQualitiesDaysAndStatesNeverMerge()
    {
        var rows = Enumerable.Range(1, 7).Select(id => Wine(id, 1, 560)).ToArray();
        rows[1].FlavorId = "400";
        rows[2].ItemId = "(O)432";
        rows[3].FromQuality = rows[3].ToQuality = 1;
        rows[4].Due += 1600;
        rows[5].State = ProductionState.Ready; rows[5].CompletedDay = 20;
        rows[6].Kind = ProductionKind.Aging;
        Assert.Equal(7, ProgressDisplayGroups.Create(rows, Now).Count);
    }

    [Fact]
    public void ReadyGroupsKeepKnownDayButOmitConflictingTimesAndUnknownHistory()
    {
        var a = Wine(1, 2, 0); a.State = ProductionState.Ready; a.CompletedDay = 20; a.CompletedTime = 1520;
        var b = a.Copy(); b.Id = 2; b.Count = 3; b.CompletedTime = 1530;
        var unknown = a.Copy(); unknown.Id = 3; unknown.CompletedDay = null; unknown.CompletedTime = null;
        var groups = ProgressDisplayGroups.Create(new[] { a, b, unknown }, Now + 1600);
        Assert.Equal(2, groups.Count);
        var known = Assert.Single(groups, g => g.Current.CompletedDay.HasValue);
        Assert.Equal("5", known.Quantity);
        Assert.Equal(new CompletionDate(20, null), known.Current.DisplayDate(Now + 1600));
        Assert.Null(Assert.Single(groups, g => !g.Current.CompletedDay.HasValue).Current.DisplayDate(Now));
        Assert.Equal(1520, a.CompletedTime);
    }

    [Fact]
    public void UnknownWorkingDatesDoNotInventACommonCompletionDay()
    {
        var a = Crop(1, 5); a.Total = 0;
        var b = Crop(2, 1); b.Total = 0;
        Assert.Equal(2, ProgressDisplayGroups.Create(new[] { a, b }, Now).Count);
    }

    [Fact]
    public void TruncatedNamesKeepBatchCountsAndAgingSuffix()
    {
        string name = NameFitting.Fit("A very long custom wine name", "Aging", "80/500", 24, text => text.Length);
        Assert.EndsWith("...(Aging)(80/500)", name);
        Assert.True(name.Length <= 24);
    }
}
