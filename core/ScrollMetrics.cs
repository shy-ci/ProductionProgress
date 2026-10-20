namespace MossHarbor.ProductionProgress.Core;

// Geometry for the game's 4x pixel scrollbar, independent of graphics and world state.
public readonly record struct ScrollMetrics(int MaximumIndex, int RunnerTop, int RunnerHeight, int ThumbTop)
{
    public const int ArrowHeight = 48;
    public const int Gap = 4;
    public const int ThumbHeight = 40;
    public bool Visible => MaximumIndex > 0 && RunnerHeight > ThumbHeight;

    public static ScrollMetrics Create(int top, int height, int totalRows, int visibleRows, int index)
    {
        int maximum = visibleRows > 0 ? Math.Max(0, totalRows - visibleRows) : 0;
        int runnerTop = top + ArrowHeight + Gap;
        int runnerHeight = Math.Max(0, height - 2 * (ArrowHeight + Gap));
        int travel = Math.Max(0, runnerHeight - ThumbHeight);
        int offset = maximum == 0 ? 0 : (int)Math.Round((double)Math.Clamp(index, 0, maximum) * travel / maximum);
        return new(maximum, runnerTop, runnerHeight, runnerTop + offset);
    }

    public int IndexAt(int pointerY, int grabOffset)
    {
        int travel = RunnerHeight - ThumbHeight;
        if (!Visible || travel <= 0) return 0;
        double offset = Math.Clamp((double)pointerY - grabOffset - RunnerTop, 0, travel);
        return (int)Math.Round(offset * MaximumIndex / travel);
    }
}
