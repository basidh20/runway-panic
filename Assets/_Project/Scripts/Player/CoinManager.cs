using System;
using UnityEngine;

namespace RunwayPanic.Player
{
    /// <summary>
    /// The player's coin wallet. Birds award coins when they die; the shop spends them.
    /// There is one player, so other systems reach it through <see cref="Instance"/>.
    /// </summary>
    public sealed class CoinManager : MonoBehaviour
    {
        [SerializeField, Min(0)] private int startingCoins;

        /// <summary>The active player's wallet, or null when no player is loaded.</summary>
        public static CoinManager Instance { get; private set; }

        /// <summary>Raised whenever the balance changes. Argument: new balance.</summary>
        public event Action<int> CoinsChanged;

        /// <summary>Raised when coins are earned. Argument: amount earned.</summary>
        public event Action<int> CoinsEarned;

        public int Coins { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this)
                Debug.LogWarning($"More than one {nameof(CoinManager)} is active; '{name}' replaces '{Instance.name}'.", this);

            Instance = this;
            Coins = startingCoins;
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        private void Start()
        {
            CoinsChanged?.Invoke(Coins);
        }

        public void AddCoins(int amount)
        {
            if (amount <= 0)
                return;

            Coins += amount;
            CoinsEarned?.Invoke(amount);
            CoinsChanged?.Invoke(Coins);
        }

        public bool CanAfford(int cost) => cost <= Coins;

        /// <summary>
        /// Removes <paramref name="cost"/> coins if the player has enough. Returns false and spends nothing otherwise.
        /// </summary>
        public bool TrySpend(int cost)
        {
            if (cost < 0 || !CanAfford(cost))
                return false;

            if (cost == 0)
                return true;

            Coins -= cost;
            CoinsChanged?.Invoke(Coins);
            return true;
        }

        private void OnValidate()
        {
            startingCoins = Mathf.Max(0, startingCoins);
        }
    }
}
