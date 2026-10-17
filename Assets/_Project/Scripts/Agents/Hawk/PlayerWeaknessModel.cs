using RunwayPanic.Core;
using RunwayPanic.Interaction;
using RunwayPanic.Weapons;
using UnityEngine;

namespace RunwayPanic.Agents.Hawk
{
    /// <summary>
    /// The hawk's model of the player (event-driven opponent modelling). Watches the player through
    /// <see cref="PlayerEventHub"/> for <see cref="WeaknessWindow"/>s, fills an aggression <see cref="Meter"/> while they
    /// are open and drains it while none is, and <b>learns</b>: every window type that opens often gets a higher weight
    /// (capped), so the hawk reacts more strongly to the weaknesses this player shows most.
    /// <para>Learned counts last the whole session (it learns you); the meter resets when a wave starts.
    /// Sits on the hawk next to <see cref="HawkBrain"/> and uses its config.</para>
    /// </summary>
    [RequireComponent(typeof(HawkBrain))]
    public sealed class PlayerWeaknessModel : MonoBehaviour
    {
        private const string LogPrefix = "[HawkWeak]";
        private static readonly int WindowCount = (int)WeaknessWindow.Busy + 1;

        [Tooltip("Write every window opening (with the meter and learned weight) to the Console / Editor.log (prefix [HawkWeak]).")]
        [SerializeField] private bool logWindows = true;

        private readonly bool[] open = new bool[WindowCount];
        private readonly int[] openCount = new int[WindowCount];
        private readonly float[] closeTime = new float[WindowCount];

        private HawkStrategyConfig config;
        private PlayerEventHub hub;
        private GameManager gameManager;
        private Barricade[] barricades = new Barricade[0];
        private WeaponBase lastWeapon;
        private bool reloading;
        private bool lowHealth;
        private bool barricadePushed;
        private float nextBarricadePoll;
        private float lookAwayTime;
        private bool warnedNoCamera;
        private float meter;

        /// <summary>Aggression, 0 to <c>meterMax</c>.</summary>
        public float Meter => meter;

        /// <summary>True when the meter is full.</summary>
        public bool IsMeterFull => config != null && meter >= config.MeterMax;

        /// <summary>True while at least one weakness window is open.</summary>
        public bool AnyWindowOpen { get; private set; }

        /// <summary>The open window with the highest learned weight, or null if none is open.</summary>
        public WeaknessWindow? StrongestOpenWindow { get; private set; }

        /// <summary>When true, the <see cref="WeaknessWindow.Busy"/> window is tracked (the hawk sets it while herding).</summary>
        public bool BusyEnabled { get; set; }

        /// <summary>True while <paramref name="window"/> is open.</summary>
        public bool IsOpen(WeaknessWindow window) => open[(int)window];

        /// <summary>How often <paramref name="window"/> has opened this session.</summary>
        public int GetOpenCount(WeaknessWindow window) => openCount[(int)window];

        /// <summary>
        /// Learned weight of <paramref name="window"/>: <c>clamp(1 + learnRate × count / max(1, average count), 1, maxWindowWeight)</c>.
        /// A window opened more often than average weighs more; the cap keeps the hawk beatable.
        /// </summary>
        public float GetWeight(WeaknessWindow window)
        {
            if (config == null)
                return 1f;

            int total = 0;
            for (int i = 0; i < WindowCount; i++)
                total += openCount[i];
            float average = Mathf.Max(1f, (float)total / WindowCount);
            return Mathf.Clamp(1f + config.LearnRate * openCount[(int)window] / average, 1f, config.MaxWindowWeight);
        }

        /// <summary>Empties the meter (the hawk calls it when it commits to an attack).</summary>
        public void ConsumeMeter()
        {
            meter = 0f;
        }

        private void Awake()
        {
            config = GetComponent<HawkBrain>().Config;
            if (config == null)
                enabled = false; // HawkBrain already reports the missing config.
        }

        private void OnEnable()
        {
            hub = PlayerEventHub.Instance;
            if (hub == null)
                return;

            hub.ReloadStarted += HandleReloadStarted;
            hub.ReloadFinished += HandleReloadFinished;
            hub.WeaponEquipped += HandleWeaponEquipped;
            hub.Interacted += HandleInteracted;
            hub.ShieldExpired += HandleShieldExpired;
            hub.HealthChanged += HandleHealthChanged;
            hub.PlayerRespawned += HandleRespawned;

            lastWeapon = hub.CurrentWeapon;
            reloading = hub.IsReloading;
            if (hub.Health != null)
                lowHealth = hub.Health.CurrentHP < hub.Health.MaxHP * config.LowHpFraction;
        }

        private void OnDisable()
        {
            if (hub != null)
            {
                hub.ReloadStarted -= HandleReloadStarted;
                hub.ReloadFinished -= HandleReloadFinished;
                hub.WeaponEquipped -= HandleWeaponEquipped;
                hub.Interacted -= HandleInteracted;
                hub.ShieldExpired -= HandleShieldExpired;
                hub.HealthChanged -= HandleHealthChanged;
                hub.PlayerRespawned -= HandleRespawned;
                hub = null;
            }

            if (gameManager != null)
            {
                gameManager.WaveStarted -= HandleWaveStarted;
                gameManager = null;
            }
        }

        private void Start()
        {
            // Barricades are scene objects: find them once. Done in Start so the GameManager has finished Awake.
            barricades = FindObjectsByType<Barricade>();

            gameManager = GameManager.Instance;
            if (gameManager != null)
                gameManager.WaveStarted += HandleWaveStarted;
        }

        private void Update()
        {
            bool gameplayActive = GameManager.Instance == null || GameManager.Instance.CurrentState == GameState.Playing;
            if (!gameplayActive || hub == null || !hub.HasPlayer)
                return;

            float dt = Time.deltaTime;
            float now = Time.time;

            // The hub may bind the player after this hawk spawned: pick up the held gun, so the first real switch counts.
            if (lastWeapon == null)
                lastWeapon = hub.CurrentWeapon;

            // Event windows: reloading stays open until the reload ends, the others for a fixed time.
            SetOpen(WeaknessWindow.Reloading, reloading);
            SetOpen(WeaknessWindow.SwitchingWeapon, now < closeTime[(int)WeaknessWindow.SwitchingWeapon]);
            SetOpen(WeaknessWindow.InteractingDoor, now < closeTime[(int)WeaknessWindow.InteractingDoor]);
            SetOpen(WeaknessWindow.ShieldExpired, now < closeTime[(int)WeaknessWindow.ShieldExpired]);

            // Polled / continuous windows: they fill the meter per second while open.
            if (now >= nextBarricadePoll)
            {
                nextBarricadePoll = now + config.BarricadePollInterval;
                barricadePushed = false;
                for (int i = 0; i < barricades.Length && !barricadePushed; i++)
                    barricadePushed = barricades[i] != null && barricades[i].IsBeingPushed;
            }

            OpenContinuous(WeaknessWindow.PushingBarricade, barricadePushed, config.BarricadeBonusPerSecond, dt);
            OpenContinuous(WeaknessWindow.LookingAway, UpdateLookAway(dt), config.LookAwayPerSecond, dt);
            OpenContinuous(WeaknessWindow.LowHealth, lowHealth, config.LowHpPerSecond, dt);
            OpenContinuous(WeaknessWindow.Busy, BusyEnabled && now - hub.LastShotTime < config.BusyShotWindow, config.PlayerBusyPerSecond, dt);

            // Summary + decay.
            AnyWindowOpen = false;
            StrongestOpenWindow = null;
            float strongestWeight = 0f;
            for (int i = 0; i < WindowCount; i++)
            {
                if (!open[i])
                    continue;
                AnyWindowOpen = true;
                float weight = GetWeight((WeaknessWindow)i);
                if (weight > strongestWeight)
                {
                    strongestWeight = weight;
                    StrongestOpenWindow = (WeaknessWindow)i;
                }
            }

            if (!AnyWindowOpen)
                AddMeter(-config.MeterDecayPerSecond * dt);
        }

        // True once the player has looked away from the hawk (camera forward vs direction to the hawk) long enough.
        private bool UpdateLookAway(float dt)
        {
            Transform cam = hub.CameraTransform;
            if (cam == null)
            {
                if (!warnedNoCamera && logWindows)
                    Debug.LogWarning($"{LogPrefix} {name}: no camera from PlayerEventHub (Camera.main is null) - LookingAway can't be measured.", this);
                warnedNoCamera = true;
                lookAwayTime = 0f;
                return false;
            }

            Vector3 toHawk = transform.position - cam.position;
            float angle = Vector3.Angle(cam.forward, toHawk);
            if (angle > config.LookAwayAngle)
            {
                if (lookAwayTime == 0f && logWindows)
                    Debug.Log($"{LogPrefix} {name} look-away timer started (angle {angle:0}°, camera '{cam.name}')", this);
                lookAwayTime += dt;
            }
            else
            {
                if (lookAwayTime > 0f && lookAwayTime < config.LookAwayMinTime && logWindows)
                    Debug.Log($"{LogPrefix} {name} look-away ended after {lookAwayTime:0.0}s (needs {config.LookAwayMinTime:0.0}s; angle now {angle:0}°)", this);
                lookAwayTime = 0f;
            }
            return lookAwayTime >= config.LookAwayMinTime;
        }

        // A one-off weakness: opens (or extends) the window and adds its bonus × learned weight once.
        private void OpenEvent(WeaknessWindow window, float duration, float bonus)
        {
            int index = (int)window;
            CountOpening(window);
            if (duration > 0f)
                closeTime[index] = Mathf.Max(closeTime[index], Time.time + duration);
            AddMeter(bonus * GetWeight(window));
            LogOpening(window);
        }

        // A lasting weakness: counted when it opens, fills the meter per second while it stays open.
        private void OpenContinuous(WeaknessWindow window, bool isOpen, float bonusPerSecond, float dt)
        {
            int index = (int)window;
            if (isOpen && !open[index])
            {
                CountOpening(window);
                LogOpening(window);
            }

            open[index] = isOpen;
            if (isOpen)
                AddMeter(bonusPerSecond * GetWeight(window) * dt);
        }

        private void SetOpen(WeaknessWindow window, bool isOpen)
        {
            open[(int)window] = isOpen;
        }

        private void CountOpening(WeaknessWindow window)
        {
            openCount[(int)window]++;
        }

        private void AddMeter(float amount)
        {
            meter = Mathf.Clamp(meter + amount, 0f, config.MeterMax);
        }

        private void LogOpening(WeaknessWindow window)
        {
            if (logWindows)
                Debug.Log($"{LogPrefix} {name} open {window} (#{openCount[(int)window]}, weight {GetWeight(window):0.00}) meter {meter:0}", this);
        }

        private void HandleReloadStarted()
        {
            reloading = true;
            OpenEvent(WeaknessWindow.Reloading, 0f, config.ReloadBonus);
            open[(int)WeaknessWindow.Reloading] = true;
        }

        private void HandleReloadFinished()
        {
            reloading = false;
        }

        private void HandleWeaponEquipped(WeaponBase weapon)
        {
            // The first equip at spawn (no previous weapon) and re-equipping the same gun are not weaknesses.
            WeaponBase previous = lastWeapon;
            lastWeapon = weapon;
            if (previous == null || weapon == previous)
                return;
            OpenEvent(WeaknessWindow.SwitchingWeapon, config.SwitchWindowDuration, config.SwitchBonus);
        }

        private void HandleInteracted(IInteractable target)
        {
            if (target is Door)
                OpenEvent(WeaknessWindow.InteractingDoor, config.InteractWindowDuration, config.InteractBonus);
        }

        private void HandleShieldExpired()
        {
            OpenEvent(WeaknessWindow.ShieldExpired, config.ShieldWindowDuration, config.ShieldExpiredBonus);
        }

        private void HandleHealthChanged(float current, float max)
        {
            lowHealth = max > 0f && current < max * config.LowHpFraction;
        }

        private void HandleRespawned()
        {
            reloading = false;
            lookAwayTime = 0f;
        }

        private void HandleWaveStarted(int wave)
        {
            meter = 0f; // learned counts are kept: "it learns you" across waves
        }
    }
}
