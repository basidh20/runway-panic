using System;
using RunwayPanic.Data;
using RunwayPanic.Player;
using UnityEngine;

namespace RunwayPanic.Core
{
    /// <summary>
    /// Between-wave shop: sells guns and consumables for coins and delivers them to the player's inventory.
    /// Purchases only work while the GameManager is in the Shopping state. Bought guns are equipped at once.
    /// </summary>
    public sealed class ShopManager : MonoBehaviour
    {
        public enum PurchaseResult
        {
            Success,
            ShopClosed,
            NoPlayer,
            NotEnoughCoins,
            AlreadyOwned,
            InventoryFull
        }

        [SerializeField] private ShopItem[] items = Array.Empty<ShopItem>();

        public static ShopManager Instance { get; private set; }

        /// <summary>Raised after a successful purchase. Argument: the item bought.</summary>
        public event Action<ShopItem> ItemPurchased;

        /// <summary>Raised when a purchase is refused. Arguments: the item, the reason.</summary>
        public event Action<ShopItem, PurchaseResult> PurchaseFailed;

        public ShopItem[] Items => items;

        private void Awake()
        {
            if (Instance != null && Instance != this)
                Debug.LogWarning($"More than one {nameof(ShopManager)} is active; '{name}' replaces '{Instance.name}'.", this);
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        /// <summary>
        /// What would happen if the player tried to buy <paramref name="item"/> now, without buying it.
        /// </summary>
        public PurchaseResult CheckPurchase(ShopItem item)
        {
            if (GameManager.Instance != null && !GameManager.Instance.IsShopOpen)
                return PurchaseResult.ShopClosed;

            CoinManager coins = CoinManager.Instance;
            PlayerInventory inventory = coins != null ? coins.GetComponent<PlayerInventory>() : null;
            if (item == null || inventory == null)
                return PurchaseResult.NoPlayer;

            if (item.ItemKind == ShopItem.Kind.Weapon)
            {
                if (item.WeaponPrefab == null)
                    return PurchaseResult.NoPlayer;
                if (inventory.OwnsWeapon(item.WeaponPrefab))
                    return PurchaseResult.AlreadyOwned;
            }
            else if (inventory.GetCount(item.Item) + item.Amount > inventory.GetMaxCount(item.Item))
            {
                return PurchaseResult.InventoryFull;
            }

            return coins.CanAfford(item.Price) ? PurchaseResult.Success : PurchaseResult.NotEnoughCoins;
        }

        /// <summary>
        /// Buys <paramref name="item"/> if allowed: takes the coins, then gives the gun or items.
        /// </summary>
        public PurchaseResult TryBuy(ShopItem item)
        {
            PurchaseResult result = CheckPurchase(item);
            if (result != PurchaseResult.Success)
            {
                PurchaseFailed?.Invoke(item, result);
                return result;
            }

            CoinManager coins = CoinManager.Instance;
            PlayerInventory inventory = coins.GetComponent<PlayerInventory>();
            coins.TrySpend(item.Price);

            if (item.ItemKind == ShopItem.Kind.Weapon)
                inventory.AddWeapon(item.WeaponPrefab, equip: true);
            else
                inventory.AddItem(item.Item, item.Amount);

            ItemPurchased?.Invoke(item);
            return PurchaseResult.Success;
        }

        /// <summary>Closes the shop so the WaveManager can start the next wave.</summary>
        public bool ContinueToNextWave() => GameManager.Instance != null && GameManager.Instance.CloseShop();
    }
}
