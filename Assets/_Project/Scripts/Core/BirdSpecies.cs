namespace RunwayPanic.Core
{
    /// <summary>
    /// The four AI-controlled bird species. Wave introduction order is Gull, Pigeon, Crow, Hawk;
    /// it is NOT numeric enum order. Preserve these IDs so existing serialized references keep their meaning.
    /// </summary>
    public enum BirdSpecies
    {
        Pigeon = 0,
        Crow = 1,
        Hawk = 2,
        Gull = 3
    }
}
