using System;
using RunwayPanic.Core;
using RunwayPanic.Player;
using RunwayPanic.Weapons;
using UnityEngine;

namespace RunwayPanic.Agents
{
    /// <summary>
    /// One place where birds read the player. It finds the player's components once and re-raises their events,
    /// so brains never search for player scripts themselves and never care which object each script sits on.
    /// Created automatically the first time <see cref="Instance"/> is used in Play mode.
    /// <para>Unsubscribe from the same hub you subscribed to (keep the reference): reading <see cref="Instance"/>
    /// while a scene is being torn down could create a new hub.</para>
    /// </summary>
    public sealed class PlayerEventHub : MonoBehaviour
    {
        [Tooltip("How quickly the measured player velocity follows changes (per second). Higher = more responsive, noisier.")]
        [SerializeField, Min(0.1f)] private float velocitySmoothing = 8f;
        [Tooltip("Height of the player's head above their pivot (feet), in metres. Used for line-of-sight checks.")]
        [SerializeField, Min(0f)] private float headHeight = 1.6f;
        [Tooltip("Seconds between attempts to find the player while none is found.")]
        [SerializeField, Min(0.1f)] private float searchInterval = 1f;

        private static PlayerEventHub instance;
        private static bool isQuitting;

        private PlayerHealth health;
        private PlayerShooting shooting;
        private PlayerWeaponHolder weaponHolder;
        private PlayerInteraction interaction;
        private PlayerShield shield;
        private PlayerRespawn respawn;
        private WeaponBase boundWeapon;
        private Camera mainCamera;
        private Vector3 lastPosition;
        private bool reloadOpen;
        private float nextSearchTime;

        /// <summary>The hub, created on first use in Play mode. Null outside Play mode or while quitting.</summary>
        public static PlayerEventHub Instance
        {
            get
            {
                if (instance != null || isQuitting || !Application.isPlaying)
                    return instance;

                instance = FindAnyObjectByType<PlayerEventHub>();
                if (instance == null)
                    instance = new GameObject("[PlayerEventHub]").AddComponent<PlayerEventHub>();
                return instance;
            }
        }

        /// <summary>The player shot. Arguments: muzzle origin, end point (hit or max range), what was hit (may be null).</summary>
        public event Action<Vector3, Vector3, IDamageable> ShotFired;

        /// <summary>The held gun started reloading.</summary>
        public event Action ReloadStarted;

        /// <summary>The reload finished, or was cancelled by switching guns.</summary>
        public event Action ReloadFinished;

        /// <summary>The player took a gun in hand (switch or purchase).</summary>
        public event Action<WeaponBase> WeaponEquipped;

        /// <summary>The player used something (door, pickup, …).</summary>
        public event Action<IInteractable> Interacted;

        /// <summary>A shield was switched on. Argument: its duration in seconds.</summary>
        public event Action<float> ShieldActivated;

        /// <summary>The shield ran out.</summary>
        public event Action ShieldExpired;

        /// <summary>Player HP changed. Arguments: current HP, max HP.</summary>
        public event Action<float, float> HealthChanged;

        /// <summary>The player lost a life.</summary>
        public event Action PlayerDied;

        /// <summary>The player came back at a checkpoint.</summary>
        public event Action PlayerRespawned;

        /// <summary>True once the player has been found.</summary>
        public bool HasPlayer => health != null;

        /// <summary>The player's root transform (feet), or null.</summary>
        public Transform Player => health != null ? health.transform : null;

        /// <summary>The player's position (feet). Zero when there is no player.</summary>
        public Vector3 PlayerPosition => health != null ? health.transform.position : Vector3.zero;

        /// <summary>The player's head, for line-of-sight checks.</summary>
        public Vector3 PlayerHeadPosition => PlayerPosition + Vector3.up * headHeight;

        /// <summary>The player's velocity, measured from movement and smoothed.</summary>
        public Vector3 PlayerVelocity { get; private set; }

        /// <summary>The main camera's transform (the player's view), or null.</summary>
        public Transform CameraTransform => mainCamera != null ? mainCamera.transform : null;

        /// <summary>The player's health component, or null.</summary>
        public PlayerHealth Health => health;

        /// <summary>The gun in the player's hand, or null.</summary>
        public WeaponBase CurrentWeapon => weaponHolder != null ? weaponHolder.CurrentWeapon : null;

        /// <summary>True while the held gun is reloading.</summary>
        public bool IsReloading => CurrentWeapon != null && CurrentWeapon.IsReloading;

        /// <summary>True while a shield is active.</summary>
        public bool IsShieldActive => shield != null && shield.IsActive;

        /// <summary>Time.time of the player's last shot (−∞ before the first).</summary>
        public float LastShotTime { get; private set; } = float.NegativeInfinity;

        /// <summary>Muzzle position of the last shot.</summary>
        public Vector3 LastShotOrigin { get; private set; }

        /// <summary>End point of the last shot.</summary>
        public Vector3 LastShotEnd { get; private set; }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Debug.LogWarning($"A second {nameof(PlayerEventHub)} on '{name}' was destroyed; '{instance.name}' is already active.", this);
                Destroy(gameObject);
                return;
            }

            instance = this;
        }

        private void Start()
        {
            TryBind();
        }

        private void Update()
        {
            if (health == null)
            {
                // The player may be spawned later, or was destroyed: look again now and then, never every frame.
                if (Time.time >= nextSearchTime)
                {
                    nextSearchTime = Time.time + searchInterval;
                    TryBind();
                }

                return;
            }

            if (mainCamera == null)
                mainCamera = Camera.main;

            Vector3 position = health.transform.position;
            float dt = Time.deltaTime;
            if (dt > 0f)
            {
                Vector3 measured = (position - lastPosition) / dt;
                PlayerVelocity = Vector3.Lerp(PlayerVelocity, measured, 1f - Mathf.Exp(-velocitySmoothing * dt));
            }

            lastPosition = position;
        }

        private void OnDestroy()
        {
            Unbind();
            if (instance == this)
                instance = null;
        }

        private void OnApplicationQuit()
        {
            isQuitting = true;
        }

        private void TryBind()
        {
            Unbind();

            health = FindAnyObjectByType<PlayerHealth>();
            if (health == null)
                return;

            // Usually all on one object; searched separately so a different player setup still works.
            shooting = FindAnyObjectByType<PlayerShooting>();
            weaponHolder = FindAnyObjectByType<PlayerWeaponHolder>();
            interaction = FindAnyObjectByType<PlayerInteraction>();
            shield = FindAnyObjectByType<PlayerShield>();
            respawn = FindAnyObjectByType<PlayerRespawn>();
            mainCamera = Camera.main;
            lastPosition = health.transform.position;
            PlayerVelocity = Vector3.zero;

            health.HealthChanged += HandleHealthChanged;
            health.Died += HandleDied;
            if (shooting != null) shooting.ShotFired += HandleShotFired;
            if (weaponHolder != null) weaponHolder.WeaponEquipped += HandleWeaponEquipped;
            if (interaction != null) interaction.Interacted += HandleInteracted;
            if (shield != null)
            {
                shield.Activated += HandleShieldActivated;
                shield.Expired += HandleShieldExpired;
            }

            if (respawn != null) respawn.Respawned += HandleRespawned;

            BindWeapon(weaponHolder != null ? weaponHolder.CurrentWeapon : null);
        }

        private void Unbind()
        {
            if (health != null)
            {
                health.HealthChanged -= HandleHealthChanged;
                health.Died -= HandleDied;
            }

            if (shooting != null) shooting.ShotFired -= HandleShotFired;
            if (weaponHolder != null) weaponHolder.WeaponEquipped -= HandleWeaponEquipped;
            if (interaction != null) interaction.Interacted -= HandleInteracted;
            if (shield != null)
            {
                shield.Activated -= HandleShieldActivated;
                shield.Expired -= HandleShieldExpired;
            }

            if (respawn != null) respawn.Respawned -= HandleRespawned;

            BindWeapon(null);
            health = null;
            shooting = null;
            weaponHolder = null;
            interaction = null;
            shield = null;
            respawn = null;
        }

        // Moves the reload subscriptions to the newly held gun.
        private void BindWeapon(WeaponBase weapon)
        {
            if (boundWeapon != null)
            {
                boundWeapon.ReloadStarted -= HandleReloadStarted;
                boundWeapon.ReloadFinished -= HandleReloadFinished;
            }

            // Holstering a gun mid-reload cancels the reload without an event: close the window here.
            if (reloadOpen && weapon != boundWeapon)
                HandleReloadFinished();

            boundWeapon = weapon;
            if (boundWeapon != null)
            {
                boundWeapon.ReloadStarted += HandleReloadStarted;
                boundWeapon.ReloadFinished += HandleReloadFinished;
            }
        }

        private void HandleShotFired(Vector3 origin, Vector3 end, IDamageable target)
        {
            LastShotTime = Time.time;
            LastShotOrigin = origin;
            LastShotEnd = end;
            ShotFired?.Invoke(origin, end, target);
        }

        private void HandleWeaponEquipped(WeaponBase weapon)
        {
            BindWeapon(weapon);
            WeaponEquipped?.Invoke(weapon);
        }

        private void HandleReloadStarted()
        {
            reloadOpen = true;
            ReloadStarted?.Invoke();
        }

        private void HandleReloadFinished()
        {
            reloadOpen = false;
            ReloadFinished?.Invoke();
        }

        private void HandleInteracted(IInteractable target) => Interacted?.Invoke(target);

        private void HandleShieldActivated(float duration) => ShieldActivated?.Invoke(duration);

        private void HandleShieldExpired() => ShieldExpired?.Invoke();

        private void HandleHealthChanged(float current, float max) => HealthChanged?.Invoke(current, max);

        private void HandleDied() => PlayerDied?.Invoke();

        private void HandleRespawned()
        {
            lastPosition = PlayerPosition;
            PlayerVelocity = Vector3.zero;
            PlayerRespawned?.Invoke();
        }

        // Statics survive Play mode when domain reload is off: start every session clean.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            instance = null;
            isQuitting = false;
        }
    }
}
