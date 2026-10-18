using System;
using System.Collections.Generic;
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

        private PlayerWeaponHolder weaponHolder;
        private InputAction nextAction;
        private InputAction previousAction;

        public IReadOnlyList<GameObject> OwnedWeapons => ownedWeapons;
        public int CurrentIndex { get; private set; } = -1;

        private void Awake()
        {
            weaponHolder = GetComponent<PlayerWeaponHolder>();

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
            if (nextAction.WasPressedThisFrame())
                Cycle(1);
            else if (previousAction.WasPressedThisFrame())
                Cycle(-1);
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
