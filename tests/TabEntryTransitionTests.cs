using MossHarbor.ProductionProgress.Core;
using Xunit;

namespace MossHarbor.ProductionProgress.Tests;

public sealed class TabEntryTransitionTests
{
    private sealed record Page(string Name);
    private readonly Page before = new("skills"), after = new("social"), progress = new("progress");

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void EnterFromEitherNeighborOnlyAfterTheNativeTransition(bool next)
    {
        var entry = new TabEntryTransition<Page>();
        entry.Capture(next ? before : after, before, after, next, !next);
        Assert.True(entry.Complete(next ? after : before));
        Assert.False(entry.Complete(next ? after : before)); // one press cannot be consumed twice
    }

    [Fact]
    public void ShallowPressDeepPressAndPartialReleaseDoNotSkipProgress()
    {
        // SDV's SDL flags use >0; SMAPI ButtonPressed uses >0.2. Use native
        // edges throughout: crossing the second threshold is not a new press.
        float[] levels = { 0, 0.1f, 0.3f, 1, 0.3f, 0.1f, 0 };
        var entry = new TabEntryTransition<Page>();
        Page current = before;
        bool wasDown = false;
        int transitions = 0;
        foreach (float level in levels)
        {
            bool down = level > 0;
            bool pressed = down && !wasDown;
            entry.Capture(current, before, after, pressed, false);
            if (pressed && ReferenceEquals(current, before)) current = after;
            if (entry.Complete(current)) { current = progress; transitions++; }
            wasDown = down;
        }
        Assert.Same(progress, current);
        Assert.Equal(1, transitions);
        entry.Capture(before, before, after, true, false); // released and pressed again
        Assert.True(entry.Complete(after));
    }

    [Fact]
    public void APageThatRefusesToCloseDoesNotEnterProgressLater()
    {
        var entry = new TabEntryTransition<Page>();
        entry.Capture(before, before, after, true, false);
        Assert.False(entry.Complete(before));
        Assert.False(entry.Complete(after));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void HoldingTriggerDoesNotReenterButReleasingAllowsTheNextPress(bool next)
    {
        var entry = new TabEntryTransition<Page>();
        Page start = next ? before : after, nativeTarget = next ? after : before;
        entry.Capture(start, before, after, next, !next);
        Assert.True(entry.Complete(nativeTarget));
        for (int frame = 0; frame < 300; frame++)
        {
            // Neither a held trigger nor its changing analog level is a digital press edge.
            entry.Capture(nativeTarget, before, after, false, false);
            Assert.False(entry.Complete(nativeTarget));
        }
        entry.Capture(nativeTarget, before, after, false, false); // release
        Assert.False(entry.Complete(nativeTarget));
        entry.Capture(nativeTarget, before, after, !next, next); // new press back toward Progress
        Assert.True(entry.Complete(start));
    }

    [Fact]
    public void OtherModsReplacementPageAndMenuClosureCancelTheTransition()
    {
        var entry = new TabEntryTransition<Page>();
        entry.Capture(before, before, after, true, false);
        Assert.False(entry.Complete(new Page("social"))); // same label is not the same page
        entry.Capture(before, before, after, true, false);
        entry.Reset();
        Assert.False(entry.Complete(after));
    }

    [Fact]
    public void NonAdjacentAndAmbiguousInputRemainNative()
    {
        var entry = new TabEntryTransition<Page>();
        entry.Capture(new Page("inventory"), before, after, true, false);
        Assert.False(entry.Complete(before));
        entry.Capture(before, before, after, true, true);
        Assert.False(entry.Complete(after));
        entry.Capture(before, before, after, false, false); // mouse or SMAPI threshold crossing
        Assert.False(entry.Complete(after));
    }
}
