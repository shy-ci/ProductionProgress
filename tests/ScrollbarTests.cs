using MossHarbor.ProductionProgress.Core;
using Xunit;

namespace MossHarbor.ProductionProgress.Tests;

public sealed class ScrollbarTests
{
    [Theory]
    [InlineData(0, 6, 520)]
    [InlineData(6, 6, 520)]
    [InlineData(10, 0, 0)]
    [InlineData(10, 1, 144)]
    public void NoScrollbarWhenRowsFitOrControlsCannotFit(int total, int visible, int height)
        => Assert.False(ScrollMetrics.Create(100, height, total, visible, 0).Visible);

    [Fact]
    public void DraggingPastEitherEndReachesFirstAndLastPage()
    {
        var metrics = ScrollMetrics.Create(100, 520, 1000, 6, 500);
        Assert.True(metrics.Visible);
        Assert.Equal(0, metrics.IndexAt(-1000, 20));
        Assert.Equal(994, metrics.IndexAt(2000, 20));
        var lastPage = ScrollMetrics.Create(100, 520, 1000, 6, 994);
        Assert.Equal(lastPage.RunnerTop + lastPage.RunnerHeight, lastPage.ThumbTop + ScrollMetrics.ThumbHeight);
    }

    [Fact]
    public void GrabbingNearEitherEdgePreservesTheSameRow()
    {
        var metrics = ScrollMetrics.Create(100, 520, 40, 6, 17);
        Assert.Equal(17, metrics.IndexAt(metrics.ThumbTop + 1, 1));
        Assert.Equal(17, metrics.IndexAt(metrics.ThumbTop + 39, 39));
    }

    [Fact]
    public void RemovedRowsAndResizingCannotPlaceThumbOutsideTrack()
    {
        var shorter = ScrollMetrics.Create(200, 300, 10, 4, 900);
        Assert.Equal(6, shorter.MaximumIndex);
        Assert.Equal(shorter.RunnerTop + shorter.RunnerHeight, shorter.ThumbTop + ScrollMetrics.ThumbHeight);
        Assert.False(ScrollMetrics.Create(200, 300, 4, 4, 900).Visible);
    }
}
