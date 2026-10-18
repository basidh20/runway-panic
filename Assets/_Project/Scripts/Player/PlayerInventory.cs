using System;
using System.Collections.Generic;
using RunwayPanic.Core;
using RunwayPanic.Weapons;
using UnityEngine;

namespace RunwayPanic.Player
{
    /// <summary>
    /// What the player carries: guns (delegated to <see cref="WeaponSwitcher"/>, which owns the list)
    /// and stackable items such as medkits and shields. The shop and pickups add to it;
    /// item effects listen to <see cref="ItemUsed"/>.
    /// </summary>
    [RequireComponent(typeof(WeaponSwitcher))]
    public sealed class PlayerInventory : MonoBehaviour
    {
        [Serializable]
        private struct ItemLimit
        {
            public ItemType item;
            [Min(1)] public int maxCount;
        }

        [SerializeField] private ItemLimit[] itemLimits =
        {
            new ItemLimit { item = ItemType.Medkit, maxCount = 3 },
            new ItemLimit { item = ItemType.Shield, maxCount = 2 }
        };

        /// <summary>Raised whenever an item count changes. Arguments: item, new count.</summary>
        public event Action<ItemType, int> ItemCountChanged;

        /// <summary>Raised when an item is used. Argument: the item.</summary>
        public event Action<ItemType> ItemUsed;

        private readonly Dictionary<ItemType, int> itemCounts = new Dictionary<ItemType, int>();
        private WeaponSwitcher weaponSwitcher;

        private void Awake()
        {
            weaponSwitcher = GetComponent<WeaponSwitcher>();
        }

        // ---------- Weapons ----------

        public IReadOnlyList<GameObject> OwnedWeapons => weaponSwitcher.OwnedWeapons;

        public bool OwnsWeapon(GameObject weaponPrefab) => weaponSwitcher.Owns(weaponPrefab);

        /// <summary>Gives the player a gun. Returns false if they already own it.</summary>
        public bool AddWeapon(GameObject weaponPrefab, bool equip = false) => weaponSwitcher.AddWeapon(weaponPrefab, equip);

        // ---------- Items ----------

        public int GetCount(ItemType item) => itemCounts.TryGetValue(item, out int count) ? count : 0;

        public int GetMaxCount(ItemType item)
        {
            foreach (ItemLimit limit in itemLimits)
            {
                if (limit.item == item)
                    return limit.maxCount;
            }

            return int.MaxValue;
        }

        public bool HasItem(ItemType item) => GetCount(item) > 0;

        public bool IsFull(ItemType item) => GetCount(item) >= GetMaxCount(item);

        /// <summary>
        /// Adds up to <paramref name="amount"/> of an item without passing its limit. Returns how many were added.
        /// </summary>
        public int AddItem(ItemType item, int amount = 1)
        {
            if (amount <= 0)
                return 0;

            int current = GetCount(item);
            int added = Mathf.Min(amount, GetMaxCount(item) - current);
            if (added <= 0)
                return 0;

            itemCounts[item] = current + added;
            ItemCountChanged?.Invoke(item, current + added);
            return added;
        }

        /// <summary>
        /// Uses one of an item if the player has it. Returns false and does nothing otherwise.
        /// </summary>
        public bool TryUseItem(ItemType item)
        {
            int current = GetCount(item);
            if (current <= 0)
                return false;

            itemCounts[item] = current - 1;
            ItemCountChanged?.Invoke(item, current - 1);
            ItemUsed?.Invoke(item);
            return true;
        }
    }
}
