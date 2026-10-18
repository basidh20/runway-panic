using System.Collections.Generic;
using RunwayPanic.Core;
using RunwayPanic.Data;
using RunwayPanic.Player;
using UnityEngine;
using UnityEngine.UI;

namespace RunwayPanic.Audio
{
    /// <summary>
    /// Game and interface sounds (on the GameManager prefab): the airport horn when a wave starts, a fanfare when it is
    /// cleared, coins earned, shop purchases and refusals, the game-over and victory stingers, and a click on every
    /// UI button. Listens to <see cref="GameManager"/>, <see cref="CoinManager"/> and <see cref="ShopManager"/> events,
    /// connecting to each as soon as it exists.
    /// </summary>
    public sealed class GameSounds : MonoBehaviour
    {
        [SerializeField] private SoundCue waveStart;
        [SerializeField] private SoundCue waveCleared;
        [SerializeField] private SoundCue gameOver;
        [SerializeField] private SoundCue victory;
        [SerializeField] private SoundCue coin;
        [SerializeField] private SoundCue purchase;
        [SerializeField] private SoundCue purchaseDenied;
        [SerializeField] private SoundCue buttonClick;
        [Tooltip("Seconds between UI button scans (menus and the shop create buttons while running).")]
        [SerializeField, Min(0.1f)] private float buttonScanInterval = 1f;

        private readonly HashSet<Button> hookedButtons = new HashSet<Button>();
        private GameManager game;
        private CoinManager coins;
        private ShopManager shop;
        private float nextButtonScan;

        private void Update()
        {
            Connect();

            if (Time.unscaledTime >= nextButtonScan)
            {
                nextButtonScan = Time.unscaledTime + buttonScanInterval;
                HookButtons();
            }
        }

        private void OnDisable()
        {
            if (game != null)
            {
                game.WaveStarted -= HandleWaveStarted;
                game.WaveCompleted -= HandleWaveCompleted;
                game.StateChanged -= HandleStateChanged;
            }
            if (coins != null) coins.CoinsEarned -= HandleCoins;
            if (shop != null)
            {
                shop.ItemPurchased -= HandlePurchased;
                shop.PurchaseFailed -= HandlePurchaseFailed;
            }
            game = null;
            coins = null;
            shop = null;
        }

        // The managers may be created after this component; connect to each once it exists.
        private void Connect()
        {
            if (game == null && GameManager.Instance != null)
            {
                game = GameManager.Instance;
                game.WaveStarted += HandleWaveStarted;
                game.WaveCompleted += HandleWaveCompleted;
                game.StateChanged += HandleStateChanged;
            }

            if (coins == null && CoinManager.Instance != null)
            {
                coins = CoinManager.Instance;
                coins.CoinsEarned += HandleCoins;
            }

            if (shop == null && ShopManager.Instance != null)
            {
                shop = ShopManager.Instance;
                shop.ItemPurchased += HandlePurchased;
                shop.PurchaseFailed += HandlePurchaseFailed;
            }
        }

        private void HookButtons()
        {
            if (buttonClick == null)
                return;

            foreach (Button button in FindObjectsByType<Button>(FindObjectsInactive.Include))
            {
                if (hookedButtons.Add(button))
                    button.onClick.AddListener(PlayClick);
            }

            hookedButtons.RemoveWhere(b => b == null);
        }

        private void PlayClick() => SoundPlayer.Play2D(buttonClick);

        private void HandleWaveStarted(int wave) => SoundPlayer.Play2D(waveStart);

        private void HandleWaveCompleted(int wave) => SoundPlayer.Play2D(waveCleared);

        private void HandleStateChanged(GameState from, GameState to)
        {
            if (to == GameState.GameOver)
                SoundPlayer.Play2D(gameOver);
            else if (to == GameState.Victory)
                SoundPlayer.Play2D(victory);
        }

        private void HandleCoins(int amount) => SoundPlayer.Play2D(coin);

        private void HandlePurchased(ShopItem item) => SoundPlayer.Play2D(purchase);

        private void HandlePurchaseFailed(ShopItem item, ShopManager.PurchaseResult result) => SoundPlayer.Play2D(purchaseDenied);
    }
}
