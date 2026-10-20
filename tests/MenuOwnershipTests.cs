using MossHarbor.ProductionProgress.Core;
using Xunit;

namespace MossHarbor.ProductionProgress.Tests;

public sealed class MenuOwnershipTests
{
    private sealed record Page(string Name);

    [Fact]
    public void ReorderingAndEqualLookingPagesDoNotChangeIdentity()
    {
        var skills = new Page("skills"); var production = new Page("production");
        var equalLooking = new Page("production"); var social = new Page("social");
        var pages = new List<Page> { skills, social, production };
        pages.Insert(0, equalLooking);
        Assert.Equal(3, MenuOwnership.IndexOfReference(pages, production));
        pages.Remove(skills); pages.Reverse();
        Assert.Equal(0, MenuOwnership.IndexOfReference(pages, production));
        pages.Remove(production);
        Assert.Equal(-1, MenuOwnership.IndexOfReference(pages, production));
        Assert.Contains(equalLooking, pages);
    }

    [Fact]
    public void ClosingMenuDoesNotEraseAnotherModsLayout()
    {
        var bounds = new OwnedValue<(int X, int Y)>((200, 20), (264, 20));
        Assert.Equal((200, 20), bounds.Restore((264, 20)));
        Assert.Equal((300, 40), bounds.Restore((300, 40)));
        Assert.False(bounds.TryUpdate((300, 40), (328, 20)));
    }

    [Fact]
    public void ReallocatedNavigationKeepsOriginalAndRespectsForeignWrites()
    {
        var neighbor = new OwnedValue<int>(12, 876410);
        Assert.True(neighbor.TryUpdate(876410, 876411));
        Assert.Equal(12, neighbor.Restore(876411));
        Assert.False(neighbor.TryUpdate(55, 876412));
        Assert.Equal(55, neighbor.Restore(55));
    }

    [Fact]
    public void RegisteredIdsAreSkippedIncludingOverflow()
    {
        Assert.Equal(876412, MenuOwnership.AvailableId(new[] { 1, 876410, 876411, 876411 }, 876410));
        Assert.Equal(2, MenuOwnership.AvailableId(new[] { int.MaxValue, 1 }, int.MaxValue));
    }
}
