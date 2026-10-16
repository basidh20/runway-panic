using System;
using System.Collections.Generic;
using RunwayPanic.Player;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RunwayPanic.Core
{
    /// <summary>
    /// Owns the top-level game state. Only allowed transitions are accepted (e.g. GameOver cannot jump to Shopping).
    /// Pausing freezes time; the cursor is locked only while Playing. One per scene, reached through <see cref="Instance"/>.
    /// </summary>
    public sealed class GameManager : MonoBehaviour
    {
        private static readonly Dictionary<GameState, GameState[]> AllowedTransitions = new Dictionary<GameState, GameState[]>
        {
            { GameState.MainMenu, new[] { GameState.Playing } },
            { GameState.Playing, new[] { GameState.Shopping, GameState.Paused, GameState.GameOver, GameState.Victory } },
            { GameState.Shopping, new[] { GameState.Playing, GameState.Paused } },
            { GameState.Paused, new[] { GameState.Playing, GameState.Shopping, GameState.MainMenu } },
            { GameState.GameOver, new[] { GameState.MainMenu, GameState.Playing } },
            { GameState.Victory, new[] { GameState.MainMenu, GameState.Playing } }
        };

        [Tooltip("State on scene start. Playing until a main menu scene exists.")]
        [SerializeField] private GameState initialState = GameState.Playing;
        [Tooltip("Player whose last lost life ends the game. Found in the scene if left empty.")]
        [SerializeField] private PlayerLifeManager playerLives;

        public static GameManager Instance { get; private set; }

        /// <summary>Raised after every state change. Arguments: previous state, new state.</summary>
        public event Action<GameState, GameState> StateChanged;

        private GameState stateBeforePause;

        public GameState CurrentState { get; private set; }
        public bool IsPaused => CurrentState == GameState.Paused;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning($"A second {nameof(GameManager)} on '{name}' was destroyed; '{Instance.name}' is already active.", this);
                Destroy(gameObject);
                return;
            }

            Instance = this;

            if (playerLives == null)
                playerLives = FindAnyObjectByType<PlayerLifeManager>();
        }

        private void OnEnable()
        {
            if (playerLives != null)
                playerLives.GameOver += HandlePlayerGameOver;
        }

        private void OnDisable()
        {
            if (playerLives != null)
                playerLives.GameOver -= HandlePlayerGameOver;
        }

        private void OnDestroy()
        {
            if (Instance != this)
                return;

            Instance = null;
            Time.timeScale = 1f;
        }

        private void Start()
        {
            CurrentState = initialState;
            ApplyStateEffects();
            StateChanged?.Invoke(initialState, initialState);
        }

        private void Update()
        {
            bool pausePressed = (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) ||
                                (Gamepad.current != null && Gamepad.current.startButton.wasPressedThisFrame);
            if (pausePressed)
                TogglePause();
        }

        /// <summary>
        /// Moves to <paramref name="newState"/> if that transition is allowed. Returns false and stays put otherwise.
        /// </summary>
        public bool ChangeState(GameState newState)
        {
            if (newState == CurrentState || !CanChangeTo(newState))
                return false;

            GameState previous = CurrentState;
            if (newState == GameState.Paused)
                stateBeforePause = previous;

            CurrentState = newState;
            ApplyStateEffects();
            StateChanged?.Invoke(previous, newState);
            return true;
        }

        public bool CanChangeTo(GameState newState) =>
            AllowedTransitions.TryGetValue(CurrentState, out GameState[] targets) && Array.IndexOf(targets, newState) >= 0;

        public void Pause() => ChangeState(GameState.Paused);

        /// <summary>Returns to the state that was active before pausing.</summary>
        public void Resume()
        {
            if (IsPaused)
                ChangeState(stateBeforePause);
        }

        public void TogglePause()
        {
            if (IsPaused)
                Resume();
            else
                Pause();
        }

        public void TriggerGameOver() => ChangeState(GameState.GameOver);

        public void TriggerVictory() => ChangeState(GameState.Victory);

        private void HandlePlayerGameOver() => TriggerGameOver();

        private void ApplyStateEffects()
        {
            Time.timeScale = CurrentState == GameState.Paused ? 0f : 1f;

            bool lockCursor = CurrentState == GameState.Playing;
            Cursor.lockState = lockCursor ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !lockCursor;
        }
    }
}
