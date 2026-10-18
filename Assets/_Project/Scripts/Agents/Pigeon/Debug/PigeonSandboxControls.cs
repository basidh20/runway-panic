using RunwayPanic.Core;
using RunwayPanic.Data;
using RunwayPanic.Player;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RunwayPanic.Agents.Pigeon
{
    /// <summary>
    /// Sandbox-only test keys for the pigeon sandbox, where the production waves are switched off:
    /// <b>G</b> gives and equips the pigeons' counter gun (the assault rifle), <b>O</b> opens or closes the
    /// between-wave shop (to check that pigeons freeze with the game state). Spawning is done by the shared
    /// <see cref="BirdTestSpawner"/>s next to this component. Not for <c>Main</c>.
    /// </summary>
    public sealed class PigeonSandboxControls : MonoBehaviour
    {
        [Tooltip("The pigeons' counter gun, given by the Give Gun key (Weapon_Tier2_AssaultRifle).")]
        [SerializeField] private WeaponData counterWeapon;
        [Tooltip("Gives the player the counter gun and puts it in hand.")]
        [SerializeField] private Key giveGunKey = Key.G;
        [Tooltip("Opens the between-wave shop, or closes it when open.")]
        [SerializeField] private Key shopKey = Key.O;

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
                return;

            if (keyboard[giveGunKey].wasPressedThisFrame)
                GiveCounterWeapon();
            if (keyboard[shopKey].wasPressedThisFrame)
                ToggleShop();
        }

        private void GiveCounterWeapon()
        {
            if (counterWeapon == null || counterWeapon.Prefab == null)
            {
                Debug.LogWarning($"{nameof(PigeonSandboxControls)}: no counter weapon assigned.", this);
                return;
            }

            PlayerInventory inventory = FindAnyObjectByType<PlayerInventory>();
            if (inventory == null)
                return;

            // Already owned: AddWeapon refuses, and the player switches to it with the normal weapon keys.
            bool added = inventory.AddWeapon(counterWeapon.Prefab, true);
            Debug.Log(added
                ? $"[PigeonSandbox] {counterWeapon.WeaponName} given and equipped."
                : $"[PigeonSandbox] the player already owns the {counterWeapon.WeaponName}; switch to it with 1 / 2.", this);
        }

        private static void ToggleShop()
        {
            GameManager game = GameManager.Instance;
            if (game == null)
                return;

            bool changed = game.IsShopOpen ? game.CloseShop() : game.OpenShop();
            if (changed)
                Debug.Log($"[PigeonSandbox] shop {(game.IsShopOpen ? "opened" : "closed")}.");
        }
    }
}
