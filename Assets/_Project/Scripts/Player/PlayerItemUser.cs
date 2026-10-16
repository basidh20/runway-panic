using RunwayPanic.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RunwayPanic.Player
{
    /// <summary>
    /// Uses carried items from the <see cref="PlayerInventory"/>: Q (or gamepad D-pad up) uses a medkit,
    /// F (or D-pad down) starts a shield. Keys are read directly so the shared input actions asset does not need new actions.
    /// </summary>
    [RequireComponent(typeof(PlayerInventory), typeof(PlayerHealth), typeof(PlayerShield))]
    public sealed class PlayerItemUser : MonoBehaviour
    {
        [SerializeField, Min(1f)] private float medkitHealAmount = 30f;

        private PlayerInventory inventory;
        private PlayerHealth health;
        private PlayerShield shield;

        private void Awake()
        {
            inventory = GetComponent<PlayerInventory>();
            health = GetComponent<PlayerHealth>();
            shield = GetComponent<PlayerShield>();
        }

        private void Update()
        {
            bool useMedkit = (Keyboard.current != null && Keyboard.current.qKey.wasPressedThisFrame) ||
                             (Gamepad.current != null && Gamepad.current.dpad.up.wasPressedThisFrame);
            bool useShield = (Keyboard.current != null && Keyboard.current.fKey.wasPressedThisFrame) ||
                             (Gamepad.current != null && Gamepad.current.dpad.down.wasPressedThisFrame);
            if (useMedkit)
                TryUseMedkit();
            if (useShield)
                TryUseShield();
        }

        /// <summary>
        /// Starts a stored shield if none is running. Returns true when one was used.
        /// </summary>
        public bool TryUseShield()
        {
            if (shield.IsActive || !health.IsAlive || !inventory.HasItem(ItemType.Shield))
                return false;

            inventory.TryUseItem(ItemType.Shield);
            return shield.TryActivate();
        }

        /// <summary>
        /// Uses a stored medkit if the player is alive and hurt. Returns true when one was used.
        /// </summary>
        public bool TryUseMedkit()
        {
            if (!health.IsAlive || health.CurrentHP >= health.MaxHP || !inventory.TryUseItem(ItemType.Medkit))
                return false;

            health.Heal(medkitHealAmount);
            return true;
        }
    }
}
