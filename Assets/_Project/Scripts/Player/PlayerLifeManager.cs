using System;
using UnityEngine;

namespace RunwayPanic.Player
{
    /// <summary>
    /// Tracks the player's remaining lives. Each death costs one life; losing the last one raises GameOver.
    /// Respawning is handled by a separate feature that listens to LifeLost.
    /// </summary>
    [RequireComponent(typeof(PlayerHealth))]
    public sealed class PlayerLifeManager : MonoBehaviour
    {
        [SerializeField, Min(1)] private int maxLives = 3;

        /// <summary>Raised whenever the life count changes. Arguments: remaining lives, max lives.</summary>
        public event Action<int, int> LivesChanged;

        /// <summary>Raised when a death costs a life and at least one life is left. Argument: remaining lives.</summary>
        public event Action<int> LifeLost;

        /// <summary>Raised once when the last life is lost.</summary>
        public event Action GameOver;

        private PlayerHealth playerHealth;

        public int MaxLives => maxLives;
        public int CurrentLives { get; private set; }
        public bool IsGameOver => CurrentLives <= 0;

        private void Awake()
        {
            playerHealth = GetComponent<PlayerHealth>();
            CurrentLives = maxLives;
        }

        private void OnEnable()
        {
            playerHealth.Died += HandleDied;
        }

        private void OnDisable()
        {
            playerHealth.Died -= HandleDied;
        }

        private void Start()
        {
            LivesChanged?.Invoke(CurrentLives, maxLives);
        }

        private void HandleDied()
        {
            if (IsGameOver)
                return;

            CurrentLives--;
            LivesChanged?.Invoke(CurrentLives, maxLives);

            if (IsGameOver)
                GameOver?.Invoke();
            else
                LifeLost?.Invoke(CurrentLives);
        }

        private void OnValidate()
        {
            maxLives = Mathf.Max(1, maxLives);
        }
    }
}
