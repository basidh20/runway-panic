using System;
using System.Collections.Generic;
using RunwayPanic.Core;
using RunwayPanic.Player;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RunwayPanic.Weapons
{
    /// <summary>
    /// Keeps the list of guns the player owns and cycles through them with the Next/Previous actions.
    /// The first owned gun is equipped on start. The shop adds guns with <see cref="AddWeapon"/>.
    /// </summary>
    [RequireComponent(typeof(PlayerInput), typeof(PlayerWeaponHolder))]
    public sealed class WeaponSwitcher : MonoBehaviour
    {
        private const string NextActionName = "Next";
        private const string PreviousActionName = "Previous";

        [Tooltip("Gun prefabs owned at the start, in switching order.")]
        [SerializeField] private List<GameObject> ownedWeapons = new List<GameObject>();

        /// <summary>Raised after a gun is added. Argument: the gun prefab.</summary>
        public event Action<GameObject> WeaponAdded;

        private static readonly Key[] SlotKeys = { Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4 };

        private PlayerWeaponHolder weaponHolder;
        private PlayerHealth health;
        private InputAction nextAction;
        private InputAction previousAction;

        public IReadOnlyList<GameObject> OwnedWeapons => ownedWeapons;
        public int CurrentIndex { get; private set; } = -1;

        private void Awake()
        {
            weaponHolder = GetComponent<PlayerWeaponHolder>();
            health = GetComponent<PlayerHealth>();

            PlayerInput playerInput = GetComponent<PlayerInput>();
            nextAction = playerInput.actions?.FindAction(NextActionName, false);
            previousAction = playerInput.actions?.FindAction(PreviousActionName, false);

            if (nextAction == null || previousAction == null)
            {
                Debug.LogError(
                    $"{nameof(WeaponSwitcher)} requires '{NextActionName}' and '{PreviousActionName}' actions on {nameof(PlayerInput)}.",
                    this);
                enabled = false;
            }

            ownedWeapons.RemoveAll(weapon => weapon == null);
        }

        private void Start()
        {
            if (ownedWeapons.Count > 0)
                EquipAt(0);
        }

        private void Update()
        {
            if (!PlayerInputGate.CanAct(health))
                return;

            // Keys 1-4 pick a gun by its tier (1 pistol, 2 assault rifle, 3 sniper, 4 RPG), whatever order it was bought in.
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null)
            {
                for (int slot = 0; slot < SlotKeys.Length; slot++)
                    if (keyboard[SlotKeys[slot]].wasPressedThisFrame)
                    {
                        SelectTier((WeaponRarity)slot);
                        return;
                    }
            }

            // Next / Previous: mouse wheel and D-pad left/right.
            if (nextAction.WasPressedThisFrame())
                Cycle(1);
            else if (previousAction.WasPressedThisFrame())
                Cycle(-1);
        }

        /// <summary>Equips the owned gun of tier <paramref name="tier"/>. Returns false if none is owned.</summary>
        public bool SelectTier(WeaponRarity tier)
        {
            for (int i = 0; i < ownedWeapons.Count; i++)
            {
                WeaponBase weapon = ownedWeapons[i].GetComponent<WeaponBase>();
                if (weapon != null && weapon.Data != null && weapon.Data.Rarity == tier)
                {
                    if (i != CurrentIndex)
                        EquipAt(i);
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Gives the player a gun. Returns false if they already own it.
        /// </summary>
        public bool AddWeapon(GameObject weaponPrefab, bool equip = false)
        {
            if (weaponPrefab == null || ownedWeapons.Contains(weaponPrefab))
                return false;

            ownedWeapons.Add(weaponPrefab);
            WeaponAdded?.Invoke(weaponPrefab);

            if (equip || CurrentIndex < 0)
                EquipAt(ownedWeapons.Count - 1);

            return true;
        }

        public bool Owns(GameObject weaponPrefab) => ownedWeapons.Contains(weaponPrefab);

        /// <summary>
        /// Moves to the next (+1) or previous (-1) owned gun, wrapping around.
        /// </summary>
        public void Cycle(int direction)
        {
            if (ownedWeapons.Count < 2)
                return;

            int count = ownedWeapons.Count;
            EquipAt(((CurrentIndex + direction) % count + count) % count);
        }

        private void EquipAt(int index)
        {
            CurrentIndex = index;
            weaponHolder.Equip(ownedWeapons[index]);
        }
    }
}
