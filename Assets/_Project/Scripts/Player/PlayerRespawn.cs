using System;
using System.Collections;
using Unity.Cinemachine;
using UnityEngine;

namespace RunwayPanic.Player
{
    /// <summary>
    /// Respawns the player at the last checkpoint reached after a death that costs a life.
    /// Before any checkpoint is reached, the player respawns where they started.
    /// </summary>
    [RequireComponent(typeof(CharacterController), typeof(PlayerHealth), typeof(PlayerLifeManager))]
    public sealed class PlayerRespawn : MonoBehaviour
    {
        [SerializeField, Min(0f)] private float respawnDelay = 2f;

        /// <summary>Raised after the player has been moved and healed.</summary>
        public event Action Respawned;

        private CharacterController characterController;
        private PlayerHealth playerHealth;
        private PlayerLifeManager lifeManager;
        private PlayerController playerController;
        private Vector3 startPosition;
        private Quaternion startRotation;
        private Coroutine respawnRoutine;

        public Transform CurrentCheckpoint { get; private set; }

        private void Awake()
        {
            characterController = GetComponent<CharacterController>();
            playerHealth = GetComponent<PlayerHealth>();
            lifeManager = GetComponent<PlayerLifeManager>();
            playerController = GetComponent<PlayerController>();
            startPosition = transform.position;
            startRotation = transform.rotation;
        }

        private void OnEnable()
        {
            playerHealth.Died += HandleDied;
            lifeManager.LifeLost += HandleLifeLost;
        }

        private void OnDisable()
        {
            playerHealth.Died -= HandleDied;
            lifeManager.LifeLost -= HandleLifeLost;
        }

        /// <summary>
        /// Sets where the next respawn happens. Called by <see cref="Checkpoint"/>.
        /// </summary>
        public void SetCheckpoint(Transform checkpoint)
        {
            CurrentCheckpoint = checkpoint;
        }

        private void HandleDied()
        {
            // Stop movement input while dead. It stays off after the last life is lost.
            if (playerController != null)
                playerController.enabled = false;
        }

        private void HandleLifeLost(int remainingLives)
        {
            if (respawnRoutine != null)
                StopCoroutine(respawnRoutine);
            respawnRoutine = StartCoroutine(RespawnAfterDelay());
        }

        private IEnumerator RespawnAfterDelay()
        {
            yield return new WaitForSeconds(respawnDelay);

            Vector3 position = CurrentCheckpoint != null ? CurrentCheckpoint.position : startPosition;
            Quaternion rotation = CurrentCheckpoint != null ? CurrentCheckpoint.rotation : startRotation;
            Vector3 delta = position - transform.position;

            // A CharacterController overrides transform changes while enabled.
            characterController.enabled = false;
            transform.SetPositionAndRotation(position, rotation);
            characterController.enabled = true;

            // Snap the camera instead of letting it fly across the map.
            CinemachineCore.OnTargetObjectWarped(transform, delta);

            playerHealth.ResetHealth();
            if (playerController != null)
                playerController.enabled = true;

            respawnRoutine = null;
            Respawned?.Invoke();
        }

        private void OnValidate()
        {
            respawnDelay = Mathf.Max(0f, respawnDelay);
        }
    }
}
