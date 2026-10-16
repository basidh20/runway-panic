namespace RunwayPanic.Core
{
    /// <summary>
    /// The four AI-controlled bird species, in the order their waves introduce them.
    /// Values are explicit so serialized data keeps its meaning if the list is reordered.
    /// </summary>
    public enum BirdSpecies
    {
        Pigeon = 0,
        Crow = 1,
        Hawk = 2,
        Eagle = 3
    }
}
