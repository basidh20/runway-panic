using RunwayPanic.Agents.Crow;
using RunwayPanic.Core;
using RunwayPanic.Interaction;
using RunwayPanic.Player;
using UnityEngine;

namespace RunwayPanic.World
{
    /// <summary>Coordinates an existing shelter zone and door; it does not grant invulnerability.</summary>
    public sealed class SafeRoomController : MonoBehaviour
    {
        public enum ShelterState { Available, Active, Cooldown }

        [SerializeField] private ShelterZone zone;
        [SerializeField] private Door door;
        [SerializeField, Min(0.1f)] private float useSeconds = 15f;
        [SerializeField, Min(0.1f)] private float cooldownSeconds = 30f;

        public ShelterState State { get; private set; }
        public bool Occupied { get; private set; }
        public float RemainingSeconds { get; private set; }

        private BoxCollider zoneBox;
        private PlayerHealth player;
        private CharacterController playerBody;
        private GameManager game;

        private void OnEnable()
        {
            if (zone == null || door == null || !zone.TryGetComponent(out zoneBox))
            {
                Debug.LogWarning("Safe room needs an existing ShelterZone and Door; controller disabled.", this);
                enabled = false;
                return;
            }
            door.Closed += HandleDoorClosed;
            ConnectGame();
        }

        private void OnDisable()
        {
            if (door != null) door.Closed -= HandleDoorClosed;
            DisconnectPlayer();
            DisconnectGame();
            Occupied = false;
            State = ShelterState.Available;
            RemainingSeconds = 0f;
        }

        private void Update()
        {
            ConnectGame();
            ConnectPlayer();
            if (door == null || zone == null || game == null || game.CurrentState != GameState.Playing)
                return; // Shopping does not set timeScale to zero, so gate it explicitly.

            bool inside = PlayerInside();
            if (Occupied && !inside && State == ShelterState.Active)
                BeginCooldown();
            Occupied = inside;

            if (State == ShelterState.Available && Occupied && !door.IsOpen)
                BeginUse();

            if (State == ShelterState.Active || State == ShelterState.Cooldown)
            {
                RemainingSeconds = Mathf.Max(0f, RemainingSeconds - Time.deltaTime);
                if (RemainingSeconds <= 0f)
                {
                    if (State == ShelterState.Active) BeginCooldown();
                    else State = ShelterState.Available;
                }
            }
        }

        private bool PlayerInside()
        {
            if (player == null || !player.isActiveAndEnabled || !player.IsAlive || zone == null ||
                !zone.isActiveAndEnabled || zoneBox == null || !zoneBox.enabled)
                return false;
            Vector3 point = playerBody != null
                ? playerBody.transform.TransformPoint(playerBody.center)
                : player.transform.position + Vector3.up;
            return (zoneBox.ClosestPoint(point) - point).sqrMagnitude < 0.0001f;
        }

        private void BeginUse()
        {
            State = ShelterState.Active;
            RemainingSeconds = useSeconds;
        }

        private void BeginCooldown()
        {
            State = ShelterState.Cooldown;
            RemainingSeconds = cooldownSeconds;
            OpenAwayFromPlayer();
        }

        private void HandleDoorClosed()
        {
            if (State == ShelterState.Cooldown)
                OpenAwayFromPlayer(); // One response to each close command; never lock the exit.
            else if (State == ShelterState.Available && Occupied && game != null && game.CurrentState == GameState.Playing)
                BeginUse();
        }

        private void OpenAwayFromPlayer()
        {
            if (door == null) return;
            Vector3 awayFrom = player != null ? player.transform.position :
                zone != null ? zone.Centre : door.transform.position;
            door.Open(awayFrom);
        }

        private void ConnectPlayer()
        {
            if (player != null) return;
            player = FindAnyObjectByType<PlayerHealth>();
            if (player == null) return;
            playerBody = player.GetComponent<CharacterController>();
            player.Died += HandlePlayerDied;
        }

        private void DisconnectPlayer()
        {
            if (player != null) player.Died -= HandlePlayerDied;
            player = null;
            playerBody = null;
        }

        private void HandlePlayerDied()
        {
            if (State == ShelterState.Active) BeginCooldown();
            Occupied = false;
        }

        private void ConnectGame()
        {
            if (game == GameManager.Instance) return;
            DisconnectGame();
            game = GameManager.Instance;
            if (game == null) return;
            game.WaveStarted += HandleWaveBoundary;
            game.WaveCompleted += HandleWaveBoundary;
            game.StateChanged += HandleGameState;
        }

        private void DisconnectGame()
        {
            if (game != null)
            {
                game.WaveStarted -= HandleWaveBoundary;
                game.WaveCompleted -= HandleWaveBoundary;
                game.StateChanged -= HandleGameState;
            }
            game = null;
        }

        private void HandleWaveBoundary(int wave)
        {
            if (State == ShelterState.Active) BeginCooldown();
            Occupied = false;
        }

        private void HandleGameState(GameState previous, GameState next)
        {
            if (next != GameState.GameOver && next != GameState.Victory && next != GameState.MainMenu) return;
            if (State == ShelterState.Active) OpenAwayFromPlayer();
            Occupied = false;
            State = ShelterState.Available;
            RemainingSeconds = 0f;
        }

        private void OnValidate()
        {
            useSeconds = Mathf.Max(0.1f, useSeconds);
            cooldownSeconds = Mathf.Max(0.1f, cooldownSeconds);
        }
    }
}
