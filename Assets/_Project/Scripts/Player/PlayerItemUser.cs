using RunwayPanic.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RunwayPanic.Player
{
    /// <summary>
    /// Uses carried items from the <see cref="PlayerInventory"/>: Q (or gamepad D-pad up) uses a medkit.
    /// Keys are read directly so the shared input actions asset does not need new actions.
    /// </summary>
    [RequireComponent(typeof(PlayerInventory), typeof(PlayerHealth))]
    public sealed class PlayerItemUser : MonoBehaviour
    {
        [SerializeField, Min(1f)] private float medkitHealAmount = 30f;

        private PlayerInventory inventory;
        private PlayerHealth health;

        private void Awake()
        {
            inventory = GetComponent<PlayerInventory>();
            health = GetComponent<PlayerHealth>();
        }

        private void Update()
        {
            bool useMedkit = (Keyboard.current != null && Keyboard.current.qKey.wasPressedThisFrame) ||
                             (Gamepad.current != null && Gamepad.current.dpad.up.wasPressedThisFrame);
            if (useMedkit)
                TryUseMedkit();
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
