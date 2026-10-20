namespace MossHarbor.ProductionProgress.Core;

public static class CropIdentity
{
    // Naturally generated forage crops don't represent a planted production cycle.
    // Seasonal wild seeds do: their harvest is random, so identify the seed batch.
    public static string? DisplayItemId(bool naturalForage, bool wildSeeds, string? harvestId, string? wildSeedId)
    {
        if (naturalForage) return null;
        string? id = wildSeeds ? wildSeedId : harvestId;
        return string.IsNullOrWhiteSpace(id) || id is "0" or "(O)0" ? null : id;
    }
}
