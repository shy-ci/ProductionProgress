using System.Globalization;
using System.Text;
using MossHarbor.ProductionProgress.Core;
using Xunit;

namespace MossHarbor.ProductionProgress.Tests;

public sealed class ClockAndNameTests
{
    [Theory]
    [InlineData(210, 0, 930)]
    [InlineData(211, 0, 940)]
    [InlineData(1170, 0, 2530)]
    [InlineData(1191, 1, 600)]
    [InlineData(1300, 1, 600)]
    [InlineData(1600, 1, 600)]
    [InlineData(1810, 1, 930)]
    [InlineData(3410, 2, 930)]
    public void MinuteDeadlineMapsToTheNextGameTickAndOvernightMorning(int offset, int days, int time)
    {
        long now = ProgressMath.ProcessingClock(7, 600);
        Assert.Equal(new FinishClock(days, time), ProgressMath.CompletionClock(now, now + offset));
    }

    [Fact]
    public void DailyOrCompletedRowsDoNotInventClockTimes()
    {
        long now = ProgressMath.ProcessingClock(7, 600);
        var row = new ProgressRow { Clock = ProgressClock.Daily, Due = now + 210, Total = 4, Remaining = 3 };
        Assert.Null(row.EstimatedClock(now));
        row.Clock = ProgressClock.ProcessingMinutes;
        Assert.Equal(new FinishClock(0, 930), row.EstimatedClock(now));
        row.State = ProductionState.Ready;
        Assert.Null(row.EstimatedClock(now));
        row.State = ProductionState.Working; row.Due = now - 10;
        Assert.Null(row.EstimatedClock(now));
    }

    private static float Measure(string value) => StringInfo.ParseCombiningCharacters(value).Length;

    [Fact]
    public void LongNameKeepsActivityAndQuantity()
    {
        string fitted = NameFitting.Fit("这是一个非常非常长的自定义作物名称", "陈酿", 12, 16, Measure);
        Assert.EndsWith("...(陈酿)(12)", fitted);
        Assert.True(Measure(fitted) <= 16);
        Assert.Equal("杨桃酒(陈酿)(12)", NameFitting.Fit("杨桃酒", "陈酿", 12, 30, Measure));
    }

    [Fact]
    public void PlainNameKeepsQuantityAndNarrowSpacePrioritizesSuffix()
    {
        Assert.EndsWith("...(24)", NameFitting.Fit("非常长的作物名字", "", 24, 10, Measure));
        Assert.Equal("(陈酿)(12)", NameFitting.Fit("很长的名称", "陈酿", 12, 8, Measure));
        Assert.Equal("(12)", NameFitting.Fit("很长的名称", "陈酿", 12, 4, Measure));
    }

    [Fact]
    public void NameTruncationKeepsUnicodeCharactersIntact()
    {
        string fitted = NameFitting.Fit("🍓e\u0301🍎🌾自定义作物", "陈酿", 2, 12, Measure);
        Assert.DoesNotContain(fitted.EnumerateRunes(), rune => rune == Rune.ReplacementChar);
        Assert.EndsWith("(陈酿)(2)", fitted);
        Assert.True(Measure(fitted) <= 12);
    }
}
