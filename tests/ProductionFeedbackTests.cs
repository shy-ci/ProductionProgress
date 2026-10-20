using MossHarbor.ProductionProgress.Core;
using Xunit;

namespace MossHarbor.ProductionProgress.Tests;

public sealed class ProductionFeedbackTests
{
    [Theory]
    [InlineData(true, false, null, "1", null)] // natural spring onion: not a planted cycle
    [InlineData(true, false, "829", "2", null)] // natural ginger
    [InlineData(false, true, "0", "495", "495")] // player-planted spring wild seeds
    [InlineData(false, true, null, "(O)498", "(O)498")]
    [InlineData(false, false, null, null, null)]
    [InlineData(false, false, "0", null, null)]
    [InlineData(false, false, "(O)0", null, null)]
    [InlineData(false, false, "(O)262", null, "(O)262")]
    [InlineData(false, false, "OtherMod_Crop", null, "OtherMod_Crop")]
    public void CropIdentityDoesNotTurnMissingOrForageDataIntoWeeds(bool natural, bool wild, string? harvest, string? seed, string? expected)
        => Assert.Equal(expected, CropIdentity.DisplayItemId(natural, wild, harvest, seed));

    [Theory]
    [InlineData(24, 21)]
    [InlineData(32, 28)]
    [InlineData(44, 38)]
    public void LabelsFollowLongBarWithoutOverlappingNames(int textHeight, int detailHeight)
    {
        const int x = 100, y = 40, width = 880, height = 680, tabsBottom = y + 80;
        var layout = ProgressLayout.Create(x, y, width, height, tabsBottom, 96, 40, textHeight, detailHeight);
        Assert.Equal(tabsBottom + 16, layout.ListTop);
        Assert.Equal(x + 40, layout.Left);
        Assert.Equal(width - 80, layout.Width);
        Assert.True(layout.Left + layout.NameWidth < layout.TimeLeft);
        Assert.Equal(layout.Left + layout.Width, layout.TimeLeft + layout.TimeWidth);
        Assert.Equal(layout.BarLeft, layout.ProgressLeft);
        Assert.Equal(layout.BarWidth, layout.ProgressWidth);
        Assert.True(layout.BarLeft >= layout.Left);
        Assert.True(layout.BarLeft + layout.BarWidth <= layout.Left + layout.Width);
        Assert.True(layout.BarOffset >= layout.NameOffset + textHeight + 2);
        Assert.True(layout.DetailOffset >= layout.BarOffset + ProgressLayout.BarHeight + 2);
        Assert.True(layout.RowHeight >= layout.DetailOffset + detailHeight);
        Assert.True(layout.NameOffset + textHeight <= layout.RowHeight);
        Assert.True(layout.VisibleRows >= 4);
        Assert.True(layout.ListTop + layout.VisibleRows * layout.RowHeight <= y + height - 40);
    }

    [Fact]
    public void TooSmallViewportDoesNotForceAnOverflowingRow()
    {
        var layout = ProgressLayout.Create(0, 0, 360, 150, 80, 96, 40, 44, 44);
        Assert.Equal(0, layout.VisibleRows);
    }

    [Fact]
    public void MinuteDeadlinesDistinguishTodayOvernightAndNextDay()
    {
        long now = ProgressMath.ProcessingClock(7, 1800);
        var row = new ProgressRow { Clock = ProgressClock.ProcessingMinutes, Due = now + 120, Total = 240 };
        Assert.Equal(0, row.EstimatedDays(now));
        row.Due = ProgressMath.ProcessingClock(7, 2600) + 100;
        Assert.Equal(1, row.EstimatedDays(now));
        row.Due = ProgressMath.ProcessingClock(8, 600);
        Assert.Equal(1, row.EstimatedDays(now));
        row.Due = ProgressMath.ProcessingClock(9, 1200);
        Assert.Equal(2, row.EstimatedDays(now));
        row.Total = 0; // loaded mid-cycle: completion is known even if the original denominator isn't
        Assert.Equal(2, row.EstimatedDays(now));
    }

    [Fact]
    public void CaskEstimateUsesStageRateAndCompletedRowsHaveNoCountdown()
    {
        var store = new BatchStore();
        var key = new BatchKey("Cellar", "(O)426", "", -1, ProductionKind.Aging, ProductionState.Working,
            ProgressClock.Daily, 1, 2, 14, 9, 0, 4, "cask");
        store.Set(1, key, "Cheese");
        var row = store.Snapshot().Single();
        Assert.Equal(3, row.EstimatedDays(0));
        row.State = ProductionState.Ready;
        Assert.Null(row.EstimatedDays(0));
        row.State = ProductionState.Withered;
        Assert.Null(row.EstimatedDays(0));
        row.State = ProductionState.Working; row.Rate = 0;
        Assert.Null(row.EstimatedDays(0));
    }

    [Fact]
    public void DailyCropEstimateDoesNotDecreaseDuringTheAfternoon()
    {
        var row = new ProgressRow { Total = 4, Remaining = 2, Clock = ProgressClock.Daily };
        Assert.Equal(2, row.EstimatedDays(ProgressMath.ProcessingClock(7, 600)));
        Assert.Equal(2, row.EstimatedDays(ProgressMath.ProcessingClock(7, 1800)));
        row.Total = 0;
        Assert.Null(row.EstimatedDays(ProgressMath.ProcessingClock(7, 1800)));
    }
}
