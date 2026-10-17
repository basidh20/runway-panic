namespace RunwayPanic.Core
{
    /// <summary>
    /// Consumable items the player can carry. Guns are tracked separately as owned weapon prefabs.
    /// Values are explicit so serialized data keeps its meaning if the list is reordered.
    /// </summary>
    public enum ItemType
    {
        Medkit = 0,
        Shield = 1
    }
}
