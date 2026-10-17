using RunwayPanic.Core;
using UnityEngine;

namespace RunwayPanic.Data
{
    /// <summary>
    /// One entry in the between-wave shop: either a gun or a stack of a consumable item.
    /// A gun's price comes from its <see cref="WeaponData"/> cost so it is only set in one place.
    /// </summary>
    [CreateAssetMenu(fileName = "Shop_New", menuName = "Runway Panic/Data/Shop Item")]
    public sealed class ShopItem : ScriptableObject
    {
        public enum Kind
        {
            Weapon = 0,
            Item = 1
        }

        [Header("Display")]
        [SerializeField] private string displayName = "New Item";
        [SerializeField, TextArea(1, 3)] private string description;
        [SerializeField] private Sprite icon;

        [Header("What it gives")]
        [SerializeField] private Kind kind;
        [Tooltip("Used when Kind is Weapon. The gun's prefab and price come from this asset.")]
        [SerializeField] private WeaponData weapon;
        [Tooltip("Used when Kind is Item.")]
        [SerializeField] private ItemType item;
        [SerializeField, Min(1)] private int amount = 1;

        [Header("Price")]
        [Tooltip("Used when Kind is Item. Guns use their WeaponData cost instead.")]
        [SerializeField, Min(0)] private int itemPrice = 10;

        public string DisplayName => displayName;
        public string Description => description;
        public Sprite Icon => icon;
        public Kind ItemKind => kind;
        public WeaponData Weapon => weapon;
        public ItemType Item => item;
        public int Amount => amount;

        public int Price => kind == Kind.Weapon && weapon != null ? weapon.Cost : itemPrice;

        /// <summary>The gun prefab to hand to the inventory, or null for consumables.</summary>
        public GameObject WeaponPrefab => kind == Kind.Weapon && weapon != null ? weapon.Prefab : null;
    }
}
