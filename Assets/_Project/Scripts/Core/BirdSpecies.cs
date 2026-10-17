namespace RunwayPanic.Core
{
    /// <summary>
    /// The four AI-controlled bird species. Wave order is Gull (1) → Pigeon (2) → Crow (3) → Hawk (4).
    /// The numeric values are NOT the wave order: they are kept from the first version (Gull replaced Eagle as 3)
    /// so serialized data keeps its meaning. Never renumber them.
    /// </summary>
    public enum BirdSpecies
    {
        Pigeon = 0,
        Crow = 1,
        Hawk = 2,
        Gull = 3
    }
}
