using MossHarbor.ProductionProgress.Core;
using Xunit;

namespace MossHarbor.ProductionProgress.Tests;

public sealed class ScrollbarTests
{
    [Theory]
    [InlineData(0, 520)]
    [InlineData(520, 520)]
    [InlineData(800, 0)]
    [InlineData(800, 144)]
    public void NoScrollbarWhenContentFitsOrControlsCannotFit(int contentHeight, int height)
        => Assert.False(ScrollMetrics.Create(100, height, contentHeight, 0).Visible);

    [Fact]
    public void DraggingPastEitherEndReachesFirstAndLastPage()
    {
        var metrics = ScrollMetrics.Create(100, 520, 1000 * 90, 500 * 90);
        Assert.True(metrics.Visible);
        Assert.Equal(0, metrics.OffsetAt(-1000, 20));
        Assert.Equal(89480, metrics.OffsetAt(2000, 20));
        var lastPage = ScrollMetrics.Create(100, 520, 1000 * 90, 89480);
        Assert.Equal(lastPage.RunnerTop + lastPage.RunnerHeight, lastPage.ThumbTop + ScrollMetrics.ThumbHeight);
    }

    [Fact]
    public void GrabbingNearEitherEdgeMapsToTheSamePixelOffset()
    {
        var metrics = ScrollMetrics.Create(100, 520, 2400, 585);
        Assert.Equal(585, metrics.OffsetAt(metrics.ThumbTop + 1, 1));
        Assert.Equal(585, metrics.OffsetAt(metrics.ThumbTop + 39, 39));
    }

    [Fact]
    public void RemovedRowsAndResizingCannotPlaceThumbOutsideTrack()
    {
        var shorter = ScrollMetrics.Create(200, 300, 600, 900);
        Assert.Equal(300, shorter.MaximumOffset);
        Assert.Equal(shorter.RunnerTop + shorter.RunnerHeight, shorter.ThumbTop + ScrollMetrics.ThumbHeight);
        Assert.False(ScrollMetrics.Create(200, 300, 280, 900).Visible);
    }

    [Fact]
    public void EvenAPartiallyHiddenLastRowRequiresScrolling()
    {
        var metrics = ScrollMetrics.Create(100, 568, 7 * 90, 0);
        Assert.True(metrics.Visible);
        Assert.Equal(62, metrics.MaximumOffset);
        Assert.Equal(62, metrics.OffsetAt(int.MaxValue, 20));
    }
}
