using MossHarbor.ProductionProgress.Core;
using Xunit;

namespace MossHarbor.ProductionProgress.Tests;

public sealed class ProgressTests
{
    [Theory]
    [InlineData(600, 1600)]
    [InlineData(1300, 1180)]
    [InlineData(2600, 400)]
    public void OvernightUsesProcessingMinutes(int bedtime, int expected)
        => Assert.Equal(expected, ProgressMath.ProcessingClock(8, 600) - ProgressMath.ProcessingClock(7, bedtime));

    [Fact]
    public void PlantingHourDoesNotChangeDailyCropProgress()
    {
        var amounts = ProgressMath.Crop(new[] { 1, 1, 1, 1, 99999 }, 2, 0);
        var row = new ProgressRow { Total = amounts.Total, Remaining = amounts.Remaining, Clock = ProgressClock.Daily };
        Assert.Equal(4, amounts.Total);
        Assert.Equal(0.5, row.Fraction(ProgressMath.ProcessingClock(7, 600)));
        Assert.Equal(0.5, row.Fraction(ProgressMath.ProcessingClock(7, 1800)));
    }

    [Fact]
    public void SpeedChangeUsesEffectivePhasesAndExcludesSentinel()
    {
        var normal = ProgressMath.Crop(new[] { 2, 3, 2, 99999 }, 1, 1);
        var accelerated = ProgressMath.Crop(new[] { 1, 2, 1, 99999 }, 1, 1);
        Assert.Equal((7d, 4d), normal);
        Assert.Equal((4d, 2d), accelerated);
        Assert.Equal((4d, 0d), ProgressMath.Crop(new[] { 1, 2, 1, 99999 }, 3, 20));
    }

    [Fact]
    public void DeadlineDoesNotDeclareCompletionBeforeGameState()
    {
        var row = new ProgressRow { Total = 100, Due = 200, Clock = ProgressClock.ProcessingMinutes };
        Assert.Equal(0.5, row.Fraction(150));
        Assert.Equal(0.99, row.Fraction(250));
        row.State = ProductionState.Ready;
        Assert.Equal(1, row.Fraction(250));
        row.State = ProductionState.Withered;
        Assert.Null(row.Fraction(250));
        Assert.Null(ProgressMath.Fraction(0, 10, false));
    }

    private static BatchKey Wheat => new("Farm", "(O)262", "", -1, ProductionKind.Crop,
        ProductionState.Working, ProgressClock.Daily, 0, 0, 4, 2, 0, 1, "1,1,1,1");

    [Fact]
    public void GroupsRequireSameLocationAndProgress()
    {
        var store = new BatchStore();
        store.Set(1, Wheat, "Wheat"); store.Set(2, Wheat, "Wheat");
        store.Set(3, Wheat with { Location = "IslandWest" }, "Wheat");
        store.Set(4, Wheat with { Remaining = 3 }, "Wheat");
        Assert.Equal(4, store.SourceCount); Assert.Equal(3, store.BatchCount);
        Assert.Equal(new[] { 1, 1, 2 }, store.Snapshot().Select(r => r.Count).Order().ToArray());
        long revision = store.Revision; store.Set(2, Wheat, "Wheat"); Assert.Equal(revision, store.Revision);
    }

    [Fact]
    public void SnapshotsAreIndependentAndReplantReplacesOldState()
    {
        var store = new BatchStore(); store.Set(1, Wheat, "Wheat");
        var first = store.TakeChanges(false); long oldId = first.Rows.Single().Id;
        store.Set(2, Wheat, "Wheat");
        Assert.Equal(1, first.Rows.Single().Count);
        store.Remove(1); store.Set(2, Wheat with { Remaining = 4 }, "Wheat");
        var delta = store.TakeChanges(false);
        Assert.Contains(oldId, delta.Removed);
        Assert.Equal(4, delta.Rows.Single().Remaining);
        Assert.DoesNotContain(delta.Rows, row => row.Id == oldId);
    }

    [Fact]
    public void RepeatedNotificationsAndDeadlineChangesStayBounded()
    {
        var dirty = new DirtyQueue<int>(); var due = new DueSchedule<int>();
        for (int pass = 0; pass < 8; pass++)
            for (int i = 0; i < 10000; i++) { dirty.Add(i); due.Set(i, 100 + pass); }
        Assert.Equal(10000, dirty.Count); Assert.Equal(10000, due.Count);
        Assert.False(due.TryTake(106, out _));
        int completed = 0;
        while (dirty.TryTake(out int id)) { due.Remove(id); completed++; }
        Assert.Equal(10000, completed); Assert.Equal(0, due.Count);
        Assert.False(due.TryTake(200, out _));
        due.Set(42, 5); due.Set(43, 4);
        Assert.True(due.TryTake(5, out int first)); Assert.Equal(43, first);
        Assert.True(due.TryTake(5, out int second)); Assert.Equal(42, second);
    }

    [Fact]
    public void TenThousandMorningChangesProduceOneFinalBatch()
    {
        var store = new BatchStore();
        for (int i = 0; i < 10000; i++) store.Set(i, Wheat, "Wheat");
        var old = store.TakeChanges(false).Rows.Single();
        for (int i = 0; i < 10000; i++) store.Set(i, Wheat with { Remaining = 1 }, "Wheat");
        var delta = store.TakeChanges(false);
        Assert.Equal(10000, delta.Rows.Single().Count);
        Assert.Equal(old.Id, delta.Removed.Single());
        Assert.Equal(1, store.BatchCount);
    }

    [Fact]
    public void RemoteRejectsStalePacketsAndReplacesSnapshotOnReconnect()
    {
        var view = new RemoteView();
        view.Apply(new ChangePacket { Session = "a", Revision = 5, Rows = new() { new() { Id = 1 } } });
        Assert.False(view.HasSnapshot);
        view.Apply(new ChangePacket { Session = "a", Full = true, Revision = 6, Initializing = true, Rows = new() { new() { Id = 1 } } });
        view.Apply(new ChangePacket { Session = "a", Revision = 7, Rows = new() { new() { Id = 2 } } });
        Assert.Equal(2, view.Rows.Count); Assert.False(view.Initializing);
        view.Apply(new ChangePacket { Session = "a", Full = true, Revision = 6 });
        Assert.Equal(2, view.Rows.Count);
        view.Apply(new ChangePacket { Session = "a", Revision = 8, Removed = new() { 1 } });
        Assert.Equal(2, view.Rows.Single().Id);
        view.Apply(new ChangePacket { Session = "b", Full = true, Revision = 1 });
        Assert.Empty(view.Rows);
        view.Apply(new ChangePacket { Session = "a", Revision = 9, Rows = new() { new() { Id = 3 } } });
        Assert.Empty(view.Rows);
    }
}
