using System.Collections.Generic;
using RunwayPanic.Core;
using RunwayPanic.Data;
using RunwayPanic.Player;
using UnityEngine;
using UnityEngine.UI;

namespace RunwayPanic.UI
{
    /// <summary>
    /// Shop screen shown while the GameManager is in the Shopping state. Builds one row per
    /// <see cref="ShopItem"/> from a template, keeps prices and buttons up to date with the player's coins,
    /// and has a "Next wave" button that closes the shop.
    /// </summary>
    public sealed class ShopPanelUI : MonoBehaviour
    {
        [SerializeField] private ShopManager shop;
        [Tooltip("Everything that is shown or hidden with the shop.")]
        [SerializeField] private GameObject panelRoot;
        [SerializeField] private Text titleText;
        [SerializeField] private Text coinsText;
        [SerializeField] private Text messageText;
        [Tooltip("Rows are created under this.")]
        [SerializeField] private Transform rowContainer;
        [Tooltip("Inactive row with children Name, Description, Price and BuyButton/Label.")]
        [SerializeField] private GameObject rowTemplate;
        [SerializeField] private Button nextWaveButton;

        private readonly List<(ShopItem item, Button button, Text label)> rows = new List<(ShopItem, Button, Text)>();
        private GameManager subscribedGameManager;
        private CoinManager subscribedCoins;

        private void Awake()
        {
            if (shop == null)
                shop = GetComponentInParent<ShopManager>();

            UiEventSystem.Ensure();
            BuildRows();
            nextWaveButton.onClick.AddListener(() => shop.ContinueToNextWave());
            panelRoot.SetActive(false);
        }

        private void Start()
        {
            subscribedGameManager = GameManager.Instance;
            if (subscribedGameManager != null)
            {
                subscribedGameManager.StateChanged += HandleStateChanged;
                HandleStateChanged(subscribedGameManager.CurrentState, subscribedGameManager.CurrentState);
            }

            subscribedCoins = CoinManager.Instance;
            if (subscribedCoins != null)
                subscribedCoins.CoinsChanged += HandleCoinsChanged;

            shop.ItemPurchased += HandlePurchased;
            shop.PurchaseFailed += HandlePurchaseFailed;
        }

        private void OnDestroy()
        {
            if (subscribedGameManager != null)
                subscribedGameManager.StateChanged -= HandleStateChanged;
            if (subscribedCoins != null)
                subscribedCoins.CoinsChanged -= HandleCoinsChanged;
            if (shop != null)
            {
                shop.ItemPurchased -= HandlePurchased;
                shop.PurchaseFailed -= HandlePurchaseFailed;
            }
        }

        private void HandleStateChanged(GameState previous, GameState current)
        {
            bool show = current == GameState.Shopping;
            panelRoot.SetActive(show);
            if (!show)
                return;

            int wave = subscribedGameManager != null ? subscribedGameManager.CurrentWave : 0;
            titleText.text = wave > 0 ? $"SHOP  -  Wave {wave} cleared" : "SHOP";
            messageText.text = "Buy the right gun for the next birds.";
            Refresh();
        }

        private void HandleCoinsChanged(int coins) => Refresh();

        private void HandlePurchased(ShopItem item)
        {
            messageText.text = item.ItemKind == ShopItem.Kind.Weapon
                ? $"Bought {item.DisplayName} - equipped. Switch guns with 1 / 2."
                : $"Bought {item.DisplayName}.";
            Refresh();
        }

        private void HandlePurchaseFailed(ShopItem item, ShopManager.PurchaseResult reason)
        {
            messageText.text = reason switch
            {
                ShopManager.PurchaseResult.NotEnoughCoins => $"Not enough coins for {item.DisplayName}.",
                ShopManager.PurchaseResult.AlreadyOwned => $"You already own the {item.DisplayName}.",
                ShopManager.PurchaseResult.InventoryFull => $"You can't carry more {item.DisplayName}s.",
                _ => "The shop is closed."
            };
        }

        private void Refresh()
        {
            int coins = CoinManager.Instance != null ? CoinManager.Instance.Coins : 0;
            coinsText.text = $"Coins: {coins}";

            foreach ((ShopItem item, Button button, Text label) in rows)
            {
                ShopManager.PurchaseResult check = shop.CheckPurchase(item);
                button.interactable = check == ShopManager.PurchaseResult.Success;
                label.text = check switch
                {
                    ShopManager.PurchaseResult.Success => "BUY",
                    ShopManager.PurchaseResult.AlreadyOwned => "OWNED",
                    ShopManager.PurchaseResult.InventoryFull => "FULL",
                    ShopManager.PurchaseResult.NotEnoughCoins => $"NEED {item.Price - coins}",
                    _ => "-"
                };
            }
        }

        private void BuildRows()
        {
            rowTemplate.SetActive(false);
            foreach (ShopItem item in shop.Items)
            {
                if (item == null)
                    continue;

                GameObject row = Instantiate(rowTemplate, rowContainer);
                row.name = "Row_" + item.name;
                row.SetActive(true);
                row.transform.Find("Name").GetComponent<Text>().text = item.DisplayName;
                row.transform.Find("Description").GetComponent<Text>().text = item.Description;
                row.transform.Find("Price").GetComponent<Text>().text = $"{item.Price} coins";

                Button button = row.transform.Find("BuyButton").GetComponent<Button>();
                Text label = row.transform.Find("BuyButton/Label").GetComponent<Text>();
                ShopItem captured = item;
                button.onClick.AddListener(() => shop.TryBuy(captured));
                rows.Add((item, button, label));
            }
        }
    }
}
