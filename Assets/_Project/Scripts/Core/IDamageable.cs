namespace RunwayPanic.Core
{
    /// <summary>
    /// Anything that has HP and can be hit: the player, birds, test dummies.
    /// </summary>
    public interface IDamageable
    {
        float CurrentHP { get; }
        bool IsAlive { get; }

        /// <summary>
        /// Applies damage. Birds use <paramref name="weaponRarity"/> to decide how effective the hit is;
        /// damage that does not come from a weapon (a bird attacking the player) passes
        /// <see cref="WeaponRarity.Common"/> and the receiver may ignore it.
        /// </summary>
        void TakeDamage(float amount, WeaponRarity weaponRarity);
    }
}
