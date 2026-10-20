namespace MossHarbor.ProductionProgress.Core;

public readonly record struct FinishClock(int Days, int TimeOfDay);

public static class ProgressMath
{
    // The overnight processing interval is 400 minutes, not four ordinary hours.
    public static long ProcessingClock(int totalDays, int timeOfDay)
        => (long)totalDays * 1600 + (timeOfDay / 100 - 6) * 60 + timeOfDay % 100;

    public static int DaysUntilDeadline(long now, long due)
    {
        if (due <= now) return 0;
        return CompletionClock(now, due)?.Days ?? 0;
    }

    public static FinishClock? CompletionClock(long now, long due)
    {
        if (now < 0 || due < now || due <= 0 || due > long.MaxValue - 9) return null;
        long rounded = (due + 9) / 10 * 10; // machines are processed on ten-minute game ticks
        long day = rounded / 1600;
        int offset = (int)(rounded % 1600);
        int minutes;
        if (offset >= 1200) { day++; minutes = 360; } // overnight work becomes available at 06:00
        else minutes = 360 + offset;
        int days = (int)Math.Min(int.MaxValue, Math.Max(0, day - now / 1600));
        return new(days, minutes / 60 * 100 + minutes % 60);
    }

    public static (double Total, double Remaining) Crop(IReadOnlyList<int> phases, int phase, int dayInPhase)
    {
        int count = Math.Max(0, phases.Count - 1); // final phase is a sentinel, not growth time
        double total = 0, elapsed = 0;
        for (int i = 0; i < count; i++)
        {
            int days = Math.Max(0, phases[i]);
            total += days;
            if (i < phase) elapsed += days;
            else if (i == phase) elapsed += Math.Clamp(dayInPhase, 0, days);
        }
        return (total, Math.Max(0, total - elapsed));
    }

    public static double? Fraction(double total, double remaining, bool ready)
    {
        if (ready) return 1;
        if (!double.IsFinite(total) || !double.IsFinite(remaining) || total <= 0) return null;
        return Math.Clamp(1 - remaining / total, 0, 0.99);
    }
}
