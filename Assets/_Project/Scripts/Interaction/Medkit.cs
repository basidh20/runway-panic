using RunwayPanic.Core;
using RunwayPanic.Player;
using UnityEngine;

namespace RunwayPanic.Interaction
{
    /// <summary>
    /// Medkit lying in the level. A hurt player is healed on the spot; a player at full HP
    /// stores it in their inventory (used later with <see cref="PlayerItemUser"/>).
    /// </summary>
    public sealed class Medkit : Pickup
    {
        [SerializeField, Min(1f)] private float healAmount = 30f;

        protected override bool TryCollect(GameObject player)
        {
            if (player.TryGetComponent(out PlayerHealth health) && health.CurrentHP < health.MaxHP)
            {
                health.Heal(healAmount);
                return true;
            }

            return player.TryGetComponent(out PlayerInventory inventory) && inventory.AddItem(ItemType.Medkit) > 0;
        }
    }
}
