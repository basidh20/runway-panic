using RunwayPanic.Core;
using RunwayPanic.Player;
using UnityEngine;

namespace RunwayPanic.Interaction
{
    /// <summary>
    /// Shield lying in the level. Starts a shield right away if none is running; otherwise it is stored
    /// in the inventory (used later with <see cref="PlayerItemUser"/>). Duration and strength live on <see cref="PlayerShield"/>.
    /// </summary>
    public sealed class ShieldPickup : Pickup
    {
        protected override bool TryCollect(GameObject player)
        {
            if (player.TryGetComponent(out PlayerShield shield) && shield.TryActivate())
                return true;

            return player.TryGetComponent(out PlayerInventory inventory) && inventory.AddItem(ItemType.Shield) > 0;
        }
    }
}
