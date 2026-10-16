namespace RunwayPanic.Core
{
    /// <summary>
    /// Weapon rarity tier. Each tier matches the P_Gun_Tier1-4 prefab of the same rank.
    /// Values are explicit so serialized data keeps its meaning if the list is reordered.
    /// </summary>
    public enum WeaponRarity
    {
        Common = 0,
        Uncommon = 1,
        Rare = 2,
        Legendary = 3
    }
}
