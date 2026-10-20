namespace MossHarbor.ProductionProgress;

public sealed class ModConfig
{
    public bool Enabled { get; set; } = true;
    public double UpdateBudgetMilliseconds { get; set; } = 0.5;
    public int MaxObjectsPerUpdate { get; set; } = 128;
    // Compatibility opt-in: normal timer decrements also produce notifications.
    public bool ObserveAllTimerChanges { get; set; }
}
