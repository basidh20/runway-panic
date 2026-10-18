using System.Collections.Generic;
using RunwayPanic.AI;
using RunwayPanic.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace RunwayPanic.Agents.Hawk
{
    /// <summary>
    /// The hawk's brain: a two-level (hierarchical) state machine. The top level is the <see cref="HawkMode"/>
    /// (Hunt, Herd, Withdraw, Dead); Hunt has its own sub-states (<see cref="HuntPhase"/>). Every mode and phase has
    /// an enter and an exit step and its own timer.
    /// <para>The FSM decides <i>what</i> the hawk does; utility AI (<see cref="PerchSelector"/>) decides <i>where it
    /// watches from</i>. The hawk never sits still (a still bird is an easy shot): SelectVantage scores every Perch marker
    /// as a vantage point, FlyToVantage flies there along an A* path, Soaring circles above it (re-scoring every
    /// <c>perchReevaluateInterval</c>, moving only for a clearly better vantage) and attacks when the
    /// <see cref="PlayerWeaknessModel"/>'s meter is full while a weakness window is open (or after <c>maxWatchTime</c>).</para>
    /// <para>Attack: against a slow gun it may first <b>feint</b> (dive, pull up short, circle while the player's gun
    /// refires, then dive for real). ApproachStoop flies to a start point on the sun's side of the player
    /// (<see cref="StoopPlanner"/>), Scream hovers at the top, Stoop dives through where the player will be, Recover
    /// climbs away.</para>
    /// <para>Withdraw (H5): every hit that doesn't kill marks the vantage it last watched from in <see cref="PerchMemory"/>
    /// ("the player has found where I hunt from") and sends it to the vantage the player can't see (withdraw scoring:
    /// sight is a penalty, the sun is ignored). It circles there for <c>withdrawWait</c>, then hunts again. A hit during
    /// the real Stoop lets the dive finish first; any other hit breaks off at once.</para>
    /// <para>Herd (H6): with enough other birds alive the hawk stops watching from vantage points and circles on the far
    /// side of the flock, projecting a fear field (<see cref="HawkFearSource"/>) that pushes pigeons and crows towards the
    /// player. While the player is busy with them it screams and dives in from there (the flock is its cover).</para>
    /// </summary>
    public sealed class HawkBrain : BirdBrain
    {
        private const string LogPrefix = "[HawkFSM]";
        // Within this share of the soaring radius from the circle (and height), the hawk counts as on it and starts circling.
        private const float JoinCircleTolerance = 0.5f;
        // m/s: below this the dive direction is noise, so "passed the player" is not judged.
        private const float MinPassSpeed = 1f;
        // The ground guard triggers at 1.5 × the predicted pull-out drop: the motor shares its turn rate between pulling
        // up and turning sideways, so a turning dive levels out slower than the pure pitch-up the formula assumes.
        private const float GroundGuardSafety = 1.5f;

        // Cached label strings, indexed by the enums: CurrentStateName never allocates.
        private static readonly string[] HuntPhaseNames = BuildHuntPhaseNames();
        private static readonly string[] ModeNames = { "Hunt", "Herd", "Withdraw", "Dead" };
        private const string WithdrawToCoverName = "Withdraw/ToCover";
        private const string WithdrawHidingName = "Withdraw/Hiding";

        // Vantage points taken by a hawk, so two hawks never circle the same one.
        private static readonly List<BirdPoint> ClaimedPerches = new List<BirdPoint>(8);

        // Every enabled hawk, for the debug overlay.
        private static readonly List<HawkBrain> ActiveHawks = new List<HawkBrain>(4);

        // "Hunt/Soaring  meter 60 [Reloading]" labels, built once per (meter step, window) on first use: no per-frame allocation.
        private const int MeterLabelSteps = 10;
        private static readonly string[,] SoaringLabels = new string[MeterLabelSteps + 1, (int)WeaknessWindow.Busy + 2];
        private static readonly string[,] HerdLabels = new string[MeterLabelSteps + 1, (int)WeaknessWindow.Busy + 2];

        [Tooltip("The hawk's strategy numbers (ScriptableObjects/Birds/Strategy/HawkStrategyConfig).")]
        [SerializeField] private HawkStrategyConfig config;
        [Tooltip("How fast the motor turns, in degrees per second — must match the motion profile (BirdMotion_Hawk: maxTurnRate 90). " +
                 "Used to start the feint's pull-up early enough to level out before reaching the player.")]
        [SerializeField, Min(1f)] private float motorTurnRate = 90f;
        [Tooltip("Height above the player's feet the dive aims at, in metres (chest height).")]
        [SerializeField, Min(0f)] private float stoopAimHeight = 1.2f;
        [Tooltip("The dive never aims lower than this far above the aim point (chest), in metres, so it sweeps in over the player instead of into the ground.")]
        [SerializeField, Min(0f)] private float stoopFlareHeight = 1f;
        [Tooltip("Ground guard: the hawk never dives lower than this above the player's feet, in metres. It levels out early " +
                 "enough for its pull-out arc (radius = speed / turn rate), so it never goes into the ground.")]
        [SerializeField, Min(0f)] private float minDiveClearance = 1.5f;
        [Tooltip("A dive counts as having passed the player when it is this much further away than its closest approach, in metres.")]
        [SerializeField, Min(0.1f)] private float stoopPassMargin = 2f;
        [Tooltip("Distance that counts as arrived at the stoop start point, in metres.")]
        [SerializeField, Min(0.1f)] private float stoopStartArriveDistance = 2f;
        [Tooltip("Optional scream played at the top of every dive (the audio tell). Skipped if empty.")]
        [SerializeField] private AudioClip screamClip;
        [Tooltip("Plays the scream. Taken from this object if empty; skipped if there is none.")]
        [SerializeField] private AudioSource screamSource;
        [Tooltip("Write every mode/phase change, vantage choice and attack event to the Console / Editor.log (prefix [HawkFSM]).")]
        [SerializeField] private bool logTransitions = true;
        [Tooltip("Editor and development builds only: this key gives the hawk a real 1-damage hit (never lethal), to test the " +
                 "withdrawal without shooting. Same key as the Agent Controller's TestPathFeeder. None = off.")]
        [SerializeField] private Key debugHitKey = Key.H;

        private readonly List<BirdPoint> perches = new List<BirdPoint>(16);
        private readonly List<PerchScore> scores = new List<PerchScore>(16);
        private readonly PerchMemory memory = new PerchMemory();
        private PlayerWeaknessModel weakness;

        private HawkMode mode = HawkMode.Hunt;
        private HuntPhase phase = HuntPhase.SelectVantage;
        private float modeTime;
        private float phaseTime;
        private bool started;

        // Vantage (H2).
        private BirdPoint currentPerch;
        private PerchScore currentScore;
        private BirdPoint unreachablePerch;
        private float nextReevaluateTime;
        private float commitUntil;
        private bool needsPath;
        private bool warnedNoPerches;

        // Attack (H4).
        private bool planFeint;
        private bool feintedThisEngagement;
        private bool feintPulledUp;
        private float stoopClosest;
        private float feintWaitUntil;
        private float feintClosest;
        private float shotTimeAtFeintStart;
        private bool hasStoopStart;
        private Vector3 stoopStart;
        private float stoopYaw;
        private float nextRepathTime;
        private Vector3 recoverTarget;

        // Withdraw (H5).
        private BirdPoint lastVantage;
        private bool pendingWithdraw;
        private bool withdrawArrived;
        private float withdrawHideTime;
        private Vector3 hideCenter;
        private GameManager gameManager;

        // Herd (H6).
        private HawkFearSource fear;
        private float nextHerdCheckTime;
        private int othersAlive;
        private Vector3 herdCentroid;
        private Vector3 herdPoint;
        private Vector3 herdPathTarget;

        // Counters (H7).
        private readonly HawkStats stats = new HawkStats();
        private bool stoopAfterFeint;
        private bool stoopFromHerd;

        private Light sun;
        private bool sunSearched;

        /// <inheritdoc />
        public override string CurrentStateName
        {
            get
            {
                if (mode == HawkMode.Withdraw)
                    return withdrawArrived ? WithdrawHidingName : WithdrawToCoverName;
                if (mode == HawkMode.Herd && weakness != null && config != null)
                    return MeterLabel(HerdLabels, "Herd", weakness.Meter / config.MeterMax, weakness.StrongestOpenWindow);
                if (mode != HawkMode.Hunt)
                    return ModeNames[(int)mode];
                if (phase == HuntPhase.Soaring && weakness != null && config != null)
                    return MeterLabel(SoaringLabels, "Hunt/Soaring", weakness.Meter / config.MeterMax, weakness.StrongestOpenWindow);
                return HuntPhaseNames[(int)phase];
            }
        }

        /// <summary>Every enabled hawk (for the debug overlay).</summary>
        public static IReadOnlyList<HawkBrain> Active => ActiveHawks;

        /// <summary>This hawk's counters (stoops, hits, feints, withdrawals, …).</summary>
        public HawkStats Stats => stats;

        /// <summary>True while the fear field is registered (Herd mode).</summary>
        public bool IsProjectingFear => fear != null && fear.IsProjecting;

        /// <summary>Living non-hawk birds at the last Herd check.</summary>
        public int OtherBirdsAlive => othersAlive;

        /// <summary>The strategy numbers in use.</summary>
        public HawkStrategyConfig Config => config;

        /// <summary>Current top-level mode.</summary>
        public HawkMode Mode => mode;

        /// <summary>Current Hunt sub-state. Only meaningful while <see cref="Mode"/> is <see cref="HawkMode.Hunt"/>.</summary>
        public HuntPhase Phase => phase;

        /// <summary>Seconds of active gameplay spent in the current mode.</summary>
        public float ModeTime => modeTime;

        /// <summary>Seconds of active gameplay spent in the current Hunt phase.</summary>
        public float PhaseTime => phaseTime;

        /// <summary>The vantage point (Perch marker) the hawk is flying to or circling over (null if none).</summary>
        public BirdPoint CurrentPerch => currentPerch;

        /// <summary>Score of <see cref="CurrentPerch"/> at its last evaluation.</summary>
        public PerchScore CurrentPerchScore => currentScore;

        /// <summary>This hawk's memory of vantage points where it was shot.</summary>
        public PerchMemory Memory => memory;

        /// <summary>This hawk's model of the player (weakness windows, aggression meter, learned weights).</summary>
        public PlayerWeaknessModel Weakness => weakness;

        protected override void Awake()
        {
            base.Awake();

            if (config == null)
            {
                Debug.LogError($"{nameof(HawkBrain)} on '{name}' has no {nameof(HawkStrategyConfig)} assigned.", this);
                enabled = false;
            }

            weakness = GetComponent<PlayerWeaknessModel>();
            if (weakness == null)
            {
                Debug.LogError($"{nameof(HawkBrain)} on '{name}' needs a {nameof(PlayerWeaknessModel)} on the same object.", this);
                enabled = false;
            }

            if (screamSource == null)
                screamSource = GetComponent<AudioSource>();

            fear = new HawkFearSource(transform, config);
            EnableShadows();
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            if (Health != null)
                Health.Damaged += HandleDamaged;
            if (!ActiveHawks.Contains(this))
                ActiveHawks.Add(this);
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            if (Health != null)
                Health.Damaged -= HandleDamaged;
            ReleasePerch();
            fear?.SetProjecting(false);
            if (weakness != null)
                weakness.BusyEnabled = false;
            ActiveHawks.Remove(this);

            // One line of measured numbers per session (death, removal or leaving Play mode) for the notes.
            if (started)
                Debug.Log($"[HawkStats] {name}: {stats}", this);
        }

        private void Start()
        {
            started = true;
            modeTime = 0f;
            EnterMode(mode, HuntPhase.SelectVantage);

            // In Start, not OnEnable: the GameManager has finished its Awake by now.
            gameManager = GameManager.Instance;
            if (gameManager != null)
                gameManager.WaveStarted += HandleWaveStarted;
        }

        private void OnDestroy()
        {
            if (gameManager != null)
                gameManager.WaveStarted -= HandleWaveStarted;
        }

        private void Update()
        {
            // Timers only run during active gameplay, so pausing or the shop never counts as watching time.
            if (!IsGameplayActive)
                return;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Keyboard keyboard = Keyboard.current;
            if (debugHitKey != Key.None && keyboard != null && keyboard[debugHitKey].wasPressedThisFrame)
                DebugHit();
#endif

            float dt = Time.deltaTime;
            modeTime += dt;
            if (mode == HawkMode.Hunt)
                phaseTime += dt;
            memory.Decay(config.RecentlyShotDecayPerSecond * dt);

            switch (mode)
            {
                case HawkMode.Hunt:
                    UpdateHunt();
                    // A hit during the real dive is acted on once the dive is over: the attack is never cancelled half-way.
                    if (pendingWithdraw && mode == HawkMode.Hunt && phase != HuntPhase.Stoop)
                        ChangeMode(HawkMode.Withdraw);
                    break;
                case HawkMode.Herd:
                    UpdateHerd();
                    break;
                case HawkMode.Withdraw:
                    UpdateWithdraw();
                    break;
            }
        }

        // ---------------------------------------------------------------- Hunt phases

        private void UpdateHunt()
        {
            if (Hub == null || !Hub.HasPlayer)
                return;

            // Mixed wave? Only between attacks (choosing or watching from a vantage), never mid-dive.
            if ((phase == HuntPhase.SelectVantage || phase == HuntPhase.Soaring) && Time.time >= nextHerdCheckTime)
            {
                nextHerdCheckTime = Time.time + config.HerdCheckInterval;
                othersAlive = CountOtherBirds();
                if (othersAlive >= config.HerdMinOtherBirds)
                {
                    Log($"{LogPrefix} {name} herd: {othersAlive} other birds alive (needs {config.HerdMinOtherBirds})");
                    ChangeMode(HawkMode.Herd);
                    return;
                }
            }

            switch (phase)
            {
                case HuntPhase.SelectVantage:
                    UpdateSelectVantage();
                    break;
                case HuntPhase.FlyToVantage:
                    UpdateFlyToVantage();
                    break;
                case HuntPhase.Soaring:
                    UpdateSoaring();
                    break;
                case HuntPhase.ApproachStoop:
                    UpdateApproachStoop();
                    break;
                case HuntPhase.Scream:
                    if (phaseTime >= config.ScreamHoverTime)
                        ChangePhase(planFeint ? HuntPhase.Feint : HuntPhase.Stoop);
                    break;
                case HuntPhase.Feint:
                    UpdateFeint();
                    break;
                case HuntPhase.Stoop:
                    UpdateStoop();
                    break;
                case HuntPhase.Recover:
                    if (Motor.ReachedDestination || phaseTime >= config.MaxManoeuvreTime)
                        ChangePhase(HuntPhase.SelectVantage);
                    break;
            }
        }

        private void UpdateSelectVantage()
        {
            if (Time.time < nextReevaluateTime)
                return;
            nextReevaluateTime = Time.time + config.PerchReevaluateInterval;

            int best = ScorePerches(false);
            if (best < 0)
            {
                // Nothing reachable: forget the failed vantage and try everything again next time.
                unreachablePerch = null;
                if (!warnedNoPerches)
                {
                    warnedNoPerches = true;
                    Debug.LogWarning($"{LogPrefix} {name}: no free Perch BirdPoint in the scene; the hawk waits until one exists.", this);
                }
                return;
            }

            SetPerch(scores[best]);
            ChangePhase(HuntPhase.FlyToVantage);
        }

        private void UpdateFlyToVantage()
        {
            if (currentPerch == null || !currentPerch.isActiveAndEnabled)
            {
                ChangePhase(HuntPhase.SelectVantage);
                return;
            }

            if (phaseTime >= config.TravelTimeout)
            {
                Log($"{LogPrefix} {name} vantage {currentPerch.name} not reached after {config.TravelTimeout:0}s, circling from here");
                ChangePhase(HuntPhase.Soaring);
                return;
            }

            // On (or near) the circle, or at the end of the path: start circling. The orbit steering pulls it onto the circle.
            Vector3 center = SoarCenter(currentPerch);
            if (IsNearCircle(center) || (!needsPath && Motor.ReachedDestination))
            {
                ChangePhase(HuntPhase.Soaring);
                return;
            }

            if (needsPath)
            {
                if (RequestPath(SoarEntryPoint(center)))
                {
                    needsPath = false;
                }
                else if (!LastRequestWasThrottled)
                {
                    Log($"{LogPrefix} {name} no path to vantage {currentPerch.name}, choosing another");
                    unreachablePerch = currentPerch;
                    ChangePhase(HuntPhase.SelectVantage);
                }
                return;
            }

            if (Time.time >= nextReevaluateTime)
            {
                nextReevaluateTime = Time.time + config.PerchReevaluateInterval;
                if (TrySwitchToBetterPerch())
                    needsPath = true;
            }
        }

        private void UpdateSoaring()
        {
            if (currentPerch == null || !currentPerch.isActiveAndEnabled)
            {
                ChangePhase(HuntPhase.SelectVantage);
                return;
            }

            // Attack when the meter is full AND the player is weak right now; or after maxWatchTime anyway (never passive).
            bool ready = weakness.IsMeterFull && weakness.AnyWindowOpen;
            if (ready || phaseTime >= config.MaxWatchTime)
            {
                if (ready)
                    Log($"{LogPrefix} {name} attack: meter full, window {weakness.StrongestOpenWindow} open, after {phaseTime:0.0}s soaring");
                else
                    Log($"{LogPrefix} {name} attack: max watch time {config.MaxWatchTime:0}s (meter {weakness.Meter:0})");
                if (ready)
                    stats.MeterAttacks++;
                else
                    stats.TimeoutAttacks++;
                stats.WatchSeconds += phaseTime;
                weakness.ConsumeMeter();
                PlanAttack();
                ChangePhase(HuntPhase.ApproachStoop);
                return;
            }

            // Keep circling: a moving target is much harder to shoot than a perched one.
            Motor.FlyTo(StoopPlanner.GetOrbitTarget(SoarCenter(currentPerch), config.SoarRadius, config.SoarLeadAngle, Motor.Position));

            if (Time.time < nextReevaluateTime)
                return;
            nextReevaluateTime = Time.time + config.PerchReevaluateInterval;

            // The player hid or moved away: move to a clearly better vantage point.
            if (TrySwitchToBetterPerch())
                ChangePhase(HuntPhase.FlyToVantage);
        }

        // Re-scores every vantage point; switches when one beats the current one by more than the hysteresis margin.
        // Not during the commit time after a choice or arrival (decision inertia).
        private bool TrySwitchToBetterPerch()
        {
            if (Time.time < commitUntil)
                return false;

            int best = ScorePerches(false);
            if (best < 0)
                return false;

            float currentTotal = float.NegativeInfinity;
            for (int i = 0; i < scores.Count; i++)
                if (scores[i].Perch == currentPerch)
                {
                    currentScore = scores[i];
                    currentTotal = scores[i].Total;
                }

            PerchScore candidate = scores[best];
            if (candidate.Perch == currentPerch || candidate.Total <= currentTotal + config.PerchSwitchMargin)
                return false;

            Log($"{LogPrefix} {name} switching {(currentPerch != null ? currentPerch.name : "none")} ({currentTotal:0.00}) -> {candidate.Perch.name} ({candidate.Total:0.00})");
            SetPerch(candidate);
            return true;
        }

        // ---------------------------------------------------------------- Attack phases (H4)

        // Decided once when leaving the vantage: feint only against a slow gun (it exploits the refire time), once per engagement.
        private void PlanAttack()
        {
            float fireInterval = Hub.CurrentWeapon != null && Hub.CurrentWeapon.Data != null ? Hub.CurrentWeapon.Data.FireInterval : 0f;
            bool slowGun = Hub.CurrentWeapon != null && fireInterval >= config.SlowGunFireInterval;
            planFeint = slowGun && !feintedThisEngagement && Random.value < config.FeintChance;
            Log($"{LogPrefix} {name} attack plan: {(planFeint ? "FEINT, then stoop" : "stoop")} " +
                $"(gun fire interval {fireInterval:0.00}s, slow from {config.SlowGunFireInterval:0.00}s, feint chance {(slowGun ? config.FeintChance : 0f):0.0})");
        }

        private void UpdateApproachStoop()
        {
            if (phaseTime >= config.ApproachTimeout)
            {
                Log($"{LogPrefix} {name} stoop start not reached after {config.ApproachTimeout:0}s, diving from here");
                ChangePhase(HuntPhase.Scream);
                return;
            }

            // The player moves: re-plan the start point and the path now and then.
            if (Time.time >= nextRepathTime)
            {
                nextRepathTime = Time.time + config.RepathInterval;
                stoopStart = FindStoopStart();
                needsPath = true;
            }

            if (needsPath)
            {
                if (RequestPath(stoopStart))
                {
                    needsPath = false;
                }
                else if (!LastRequestWasThrottled)
                {
                    // No grid path (e.g. start snapped into a corner): fly straight, the start point is in open air.
                    Motor.FlyTo(stoopStart);
                    needsPath = false;
                }
                return;
            }

            float arrive = stoopStartArriveDistance;
            if (Motor.ReachedDestination || (stoopStart - Motor.Position).sqrMagnitude < arrive * arrive)
                ChangePhase(HuntPhase.Scream);
        }

        private void UpdateFeint()
        {
            Vector3 aim = AimPoint();
            float distance = Vector3.Distance(aim, Motor.Position);
            if (!feintPulledUp)
            {
                // The fake dive aims pullUpDistance ABOVE the player's chest: it looks like a stoop, but geometrically it
                // cannot come closer than that (a turn-limited bird can't be trusted to pull up in time on command).
                feintClosest = Mathf.Min(feintClosest, distance);
                Motor.FlyTo(DiveTarget(aim, config.PullUpDistance));

                Vector3 flat = aim - Motor.Position;
                flat.y = 0f;
                if (flat.sqrMagnitude <= config.PullUpDistance * config.PullUpDistance)
                {
                    float refire = Hub.CurrentWeapon != null && Hub.CurrentWeapon.Data != null ? Hub.CurrentWeapon.Data.FireInterval : 0f;
                    feintPulledUp = true;
                    feintedThisEngagement = true;
                    stats.Feints++;
                    feintWaitUntil = phaseTime + refire;
                    Motor.SpeedMultiplier = 1f;
                    Anim.SetDiving(false);
                    Anim.SetWingsSpread(1f);
                    Log($"{LogPrefix} {name} feint: pulled up {distance:0.0} m from the player ({Motor.Position.y - aim.y:0.0} m above the chest) " +
                        $"at {Motor.Velocity.magnitude:0} m/s, circling for {refire:0.00}s (refire time)");
                }
                else if (phaseTime >= config.MaxManoeuvreTime)
                {
                    ChangePhase(HuntPhase.Stoop);
                }
                return;
            }

            // Waiting out the refire time: circle above the player, out of reach, still moving.
            feintClosest = Mathf.Min(feintClosest, distance);
            Vector3 center = aim + Vector3.up * config.FeintPullUpHeight;
            Motor.FlyTo(StoopPlanner.GetOrbitTarget(center, config.FeintOrbitRadius, config.SoarLeadAngle, Motor.Position));

            if (phaseTime >= feintWaitUntil)
            {
                bool baited = Hub.LastShotTime > shotTimeAtFeintStart;
                Log($"{LogPrefix} {name} feint {(baited ? "BAITED a shot" : "drew no shot")} (closest {feintClosest:0.0} m), real stoop now");
                if (baited)
                    stats.FeintsBaited++;
                stoopAfterFeint = true;
                ChangePhase(HuntPhase.Stoop); // from here: the point is to hit during the refire
            }
        }

        private void UpdateStoop()
        {
            Vector3 aim = AimPoint();
            Motor.FlyTo(DiveTarget(aim, 0f));

            // Turn first, then dive: the turn circle grows with speed (r = v / ω), so line up at normal speed and only
            // then accelerate to dive speed.
            Vector3 heading = Motor.Velocity;
            heading.y = 0f;
            if (heading.sqrMagnitude < MinPassSpeed * MinPassSpeed)
                heading = new Vector3(transform.forward.x, 0f, transform.forward.z);
            bool linedUp = Vector3.Angle(heading, FlatDirectionToPlayer()) <= config.StoopTurnInAngle;
            Motor.SpeedMultiplier = linedUp ? config.StoopSpeedMultiplier : 1f;

            // In range = the F3 rule (bird pivot to the closest point of the player's collider), so the hawk sweeping over
            // the player's head counts. Without a BirdContactAttack: distance to the chest.
            Vector3 toAim = aim - Motor.Position;
            float range = Species != null ? Species.AttackRange : 0f;
            bool inRange = Attack != null ? Attack.IsPlayerInRange() : toAim.sqrMagnitude <= range * range;
            if (inRange)
            {
                float damage = (Species != null ? Species.AttackDamage : 0f) * config.StoopDamageMultiplier;
                bool dealt = Attack != null && Attack.TryAttackWithDamage(damage);
                stats.StoopHits++;
                if (stoopAfterFeint)
                    stats.HitsAfterFeint++;
                Log($"{LogPrefix} {name} stoop HIT range after {phaseTime:0.0}s at {Motor.Velocity.magnitude:0} m/s: damage {damage:0} " +
                    $"{(dealt ? "dealt" : "not dealt (BirdContactAttack refused: cooldown or out of its range)")}");
                ChangePhase(HuntPhase.Recover);
                return;
            }

            // "Passed the player" = it really came close (within the overshoot) and is now clearly further away again.
            // Judging by the velocity alone fails for a turn-limited bird: while it turns in from the feint's circle
            // its velocity swings toward and away from the player.
            float distance = toAim.magnitude;
            stoopClosest = Mathf.Min(stoopClosest, distance);
            bool passed = stoopClosest < config.StoopOvershoot && distance > stoopClosest + stoopPassMargin;
            if (passed || phaseTime >= config.MaxManoeuvreTime)
            {
                Log($"{LogPrefix} {name} stoop MISSED ({(passed ? "passed the player" : "timed out")}, closest {stoopClosest:0.0} m, now {distance:0.0} m)");
                ChangePhase(HuntPhase.Recover);
            }
        }

        // Through the lead point (raised by heightAboveAim; 0 for the real stoop) and beyond — the motor brakes to stop at a
        // seek target — never below chest + flare height, and with the ground guard applied.
        private Vector3 DiveTarget(Vector3 aim, float heightAboveAim)
        {
            Vector3 lead = StoopPlanner.GetLeadPoint(aim, Hub.PlayerVelocity, config.StoopLeadTime) + Vector3.up * heightAboveAim;
            float minHeight = aim.y + Mathf.Max(heightAboveAim, stoopFlareHeight);
            return GuardAltitude(StoopPlanner.GetStrikeThroughTarget(Motor.Position, lead, config.StoopOvershoot, minHeight));
        }

        // Ground guard. Levelling out from a dive of angle θ at speed v and turn rate ω drops r·(1 − cos θ), r = v / ω.
        // If that (with a safety factor) would take the hawk below the floor, it only pulls up — level, straight ahead,
        // no turning — until it is safe: the motor levels out fastest when it doesn't also have to turn.
        private Vector3 GuardAltitude(Vector3 target)
        {
            float floor = Hub.PlayerPosition.y + minDiveClearance;
            Vector3 position = Motor.Position;
            Vector3 velocity = Motor.Velocity;
            float speed = velocity.magnitude;
            float descent = speed > MinPassSpeed ? Mathf.Asin(Mathf.Clamp(-velocity.y / speed, 0f, 1f)) : 0f;
            float pullOutDrop = speed / (motorTurnRate * Mathf.Deg2Rad) * (1f - Mathf.Cos(descent));
            if (position.y - pullOutDrop * GroundGuardSafety > floor)
                return target;

            Vector3 ahead = new Vector3(velocity.x, 0f, velocity.z);
            ahead = ahead.sqrMagnitude > 1e-4f ? ahead.normalized : FlatDirectionToPlayer();
            Vector3 level = position + ahead * Mathf.Max(speed, MinPassSpeed);
            level.y = Mathf.Max(position.y, floor);
            return level;
        }

        // Sun-side start point with a clear dive line to the player; steps around the player when a building is in the way.
        private Vector3 FindStoopStart()
        {
            Vector3 player = Hub.PlayerPosition;
            Vector3 aim = AimPoint();
            Vector3 sunForward = GetSunForward();
            IPathProvider paths = PathService.Current;
            FlightGrid grid = FlightGrid.Instance;
            LayerMask mask = grid != null ? grid.ObstacleMask : (LayerMask)Physics.DefaultRaycastLayers;

            Vector3 fallback = StoopPlanner.GetStoopStart(player, sunForward, config.StoopStartDistance, config.MinStoopHeight, config.MaxStoopAngle, 0f);
            bool haveFallback = false;
            int attempts = 1 + 2 * config.StoopYawTries;
            for (int i = 0; i < attempts; i++)
            {
                // 0, +step, -step, +2·step, -2·step, ...
                float yaw = (i + 1) / 2 * config.StoopYawStep * (i % 2 == 1 ? 1f : -1f);
                Vector3 start = StoopPlanner.GetStoopStart(player, sunForward, config.StoopStartDistance, config.MinStoopHeight, config.MaxStoopAngle, yaw);
                if (grid != null)
                {
                    // Pulling the start back inside the grid (player near the edge) shortens it and makes the dive
                    // steeper: lower it again so the angle stays within what the motor can fly.
                    start = StoopPlanner.ClampToVolume(start, grid.VolumeBounds, grid.CellSize);
                    Vector3 offset = start - player;
                    float maxHeight = player.y + new Vector2(offset.x, offset.z).magnitude * Mathf.Tan(config.MaxStoopAngle * Mathf.Deg2Rad);
                    start.y = Mathf.Min(start.y, maxHeight);
                }
                if (!StoopPlanner.TrySnapUpToFlyable(start, paths, config.StoopSnapSearchHeight, config.StoopSnapStep, out start))
                    continue;

                if (!haveFallback)
                {
                    fallback = start;
                    haveFallback = true;
                }

                if (PerchSelector.HasLineOfSight(start, aim, mask))
                {
                    LogStoopStart(start, yaw, true);
                    stoopYaw = yaw;
                    hasStoopStart = true;
                    return start;
                }
            }

            LogStoopStart(fallback, 0f, false);
            stoopYaw = 0f;
            hasStoopStart = true;
            return fallback;
        }

        private void LogStoopStart(Vector3 start, float yaw, bool clearLine)
        {
            // Once per approach (the start is re-planned every repath while the player moves).
            if (hasStoopStart && Mathf.Approximately(yaw, stoopYaw))
                return;
            Vector3 fromPlayer = start - Hub.PlayerPosition;
            float angle = Mathf.Atan2(fromPlayer.y, new Vector2(fromPlayer.x, fromPlayer.z).magnitude) * Mathf.Rad2Deg;
            Log($"{LogPrefix} {name} stoop start {start} ({yaw:+0;-0;0}° from the sun side, {fromPlayer.y:0.0} m up, dive angle {angle:0}°, " +
                $"{(clearLine ? "clear dive line" : "NO clear dive line found")})");
        }

        private Vector3 AimPoint()
        {
            return Hub.PlayerPosition + Vector3.up * stoopAimHeight;
        }

        private Vector3 FlatDirectionToPlayer()
        {
            Vector3 toPlayer = Hub != null && Hub.HasPlayer ? Hub.PlayerPosition - Motor.Position : transform.forward;
            toPlayer.y = 0f;
            return toPlayer.sqrMagnitude > 1e-4f ? toPlayer.normalized : Vector3.forward;
        }

        // ---------------------------------------------------------------- Withdraw (H5)

        // Every hit that doesn't kill. Dying is handled by OnBirdDied (BirdHealth raises Died after Damaged).
        private void HandleDamaged(float taken, float hpAfter)
        {
            if (!started || hpAfter <= 0f || mode == HawkMode.Dead)
                return;

            stats.HitsTaken++;

            // Already fleeing: keep going. Being hit on the way says nothing about the hiding spot (the hawk isn't
            // there yet), and a fast gun (pistol: 3 shots/s) would otherwise mark every hidden spot within a second.
            if (mode == HawkMode.Withdraw && !withdrawArrived)
            {
                Log($"{LogPrefix} {name} HIT in {CurrentStateName}: {taken:0.#} damage, {hpAfter:0.#} HP left — already fleeing, keeps going");
                return;
            }

            // "The player has found where I hunt (or hide) from": that vantage scores lower for a while.
            memory.Mark(lastVantage, config.WithdrawShotMark);
            bool finishDive = mode == HawkMode.Hunt && phase == HuntPhase.Stoop;
            Log($"{LogPrefix} {name} HIT in {CurrentStateName}: {taken:0.#} damage, {hpAfter:0.#} HP left; " +
                $"vantage {(lastVantage != null ? lastVantage.name : "none")} shot value now {memory.Get(lastVantage):0.00}" +
                (finishDive ? " — finishing the dive, then withdrawing" : " — withdrawing"));

            if (finishDive)
                pendingWithdraw = true;
            else
                ChangeMode(HawkMode.Withdraw); // also when already withdrawing: the hiding place was found, pick another
        }

        private void HandleWaveStarted(int wave)
        {
            memory.Clear(); // a new wave starts with no bad memories (the player model's learned weights stay)
        }

        // Withdraw scoring: seeing the player is a penalty and the sun is ignored, so the winner is the best vantage the
        // player can't see. With no vantage at all it hides where it is.
        private void SelectHidingVantage()
        {
            int best = ScorePerches(true);
            if (best < 0)
            {
                Log($"{LogPrefix} {name} withdraw: no free vantage point, hiding where it is");
                ReleasePerch();
                ArriveAtCover();
                return;
            }

            SetPerch(scores[best]);
            needsPath = true;
        }

        private void UpdateWithdraw()
        {
            if (withdrawArrived)
            {
                // Keep circling while it waits: hidden, but never a sitting target.
                withdrawHideTime += Time.deltaTime;
                Motor.FlyTo(StoopPlanner.GetOrbitTarget(hideCenter, config.SoarRadius, config.SoarLeadAngle, Motor.Position));
                if (withdrawHideTime >= config.WithdrawWait)
                    ChangeMode(HawkMode.Hunt);
                return;
            }

            if (currentPerch == null || !currentPerch.isActiveAndEnabled || modeTime >= config.TravelTimeout)
            {
                ArriveAtCover();
                return;
            }

            Vector3 center = SoarCenter(currentPerch);
            if (IsNearCircle(center) || (!needsPath && Motor.ReachedDestination))
            {
                ArriveAtCover();
                return;
            }

            if (!needsPath)
                return;
            if (RequestPath(SoarEntryPoint(center)))
            {
                needsPath = false;
            }
            else if (!LastRequestWasThrottled)
            {
                // One retry with the next-best vantage; if that has no path either, hide where it is.
                Log($"{LogPrefix} {name} no path to hiding vantage {currentPerch.name}");
                if (unreachablePerch == null)
                {
                    unreachablePerch = currentPerch;
                    SelectHidingVantage();
                }
                else
                {
                    ReleasePerch();
                    ArriveAtCover();
                }
            }
        }

        // Arrived (or gave up travelling): circle here for withdrawWait, at normal speed.
        private void ArriveAtCover()
        {
            withdrawArrived = true;
            withdrawHideTime = 0f;
            Motor.SpeedMultiplier = 1f;
            hideCenter = currentPerch != null ? SoarCenter(currentPerch) : Motor.Position;
            Log($"{LogPrefix} {name} hiding {(currentPerch != null ? $"over {currentPerch.name} (player in sight: {(currentScore.LineOfSight > 0f ? "yes" : "no")})" : "in place")} " +
                $"for {config.WithdrawWait:0.#}s after {modeTime:0.0}s of flight");
        }

        // ---------------------------------------------------------------- Herd (H6)

        private void UpdateHerd()
        {
            if (Hub == null || !Hub.HasPlayer)
                return;

            // Counted every frame (a few dozen birds at most): the centroid moves with the flock.
            othersAlive = CountOtherBirds();
            if (Time.time >= nextHerdCheckTime)
            {
                nextHerdCheckTime = Time.time + config.HerdCheckInterval;
                if (othersAlive < config.HerdMinOtherBirds)
                {
                    Log($"{LogPrefix} {name} herd over: {othersAlive} other birds left");
                    ChangeMode(HawkMode.Hunt);
                    return;
                }
            }

            // Strike while the player is busy with the flock: from here, no vantage or sun approach — the flock is the
            // cover. Never passive: after maxWatchTime it strikes anyway. It still screams first (tuning 18 Oct): a
            // dive with no tell was the hardest attack to survive, and the wave hint promises "when it screams, look up".
            bool ready = weakness.IsMeterFull && weakness.AnyWindowOpen;
            if (ready || modeTime >= config.MaxWatchTime)
            {
                Log($"{LogPrefix} {name} herd strike: " + (ready
                    ? $"meter full, window {weakness.StrongestOpenWindow} open, after {modeTime:0.0}s herding"
                    : $"max watch time {config.MaxWatchTime:0}s (meter {weakness.Meter:0})"));
                weakness.ConsumeMeter();
                stoopFromHerd = true;
                planFeint = false; // no feint from the herd circle (a plan left over from an earlier approach must not count)
                ChangeMode(HawkMode.Hunt, HuntPhase.Scream);
                return;
            }

            herdPoint = HerdPoint();
            float radius = config.HerdOrbitRadius;
            bool onCircle = IsNearCircle(herdPoint, radius);

            // Far from the circle: A* path to it, re-planned only when the herd point has really moved.
            float repath = config.HerdRepathDistance;
            if (!onCircle && (needsPath || (herdPoint - herdPathTarget).sqrMagnitude > repath * repath))
            {
                Vector3 entry = CircleEntryPoint(herdPoint, radius);
                if (RequestPath(entry))
                {
                    needsPath = false;
                    herdPathTarget = herdPoint;
                }
                else if (!LastRequestWasThrottled)
                {
                    Motor.FlyTo(entry); // no grid path: the herd point is in open air above the flock
                    needsPath = false;
                    herdPathTarget = herdPoint;
                }
                return;
            }

            if (onCircle || Motor.ReachedDestination)
                Motor.FlyTo(StoopPlanner.GetOrbitTarget(herdPoint, radius, config.SoarLeadAngle, Motor.Position));
        }

        // The far side of the flock from the player, herdHeight above it: from there the fear field pushes the flock
        // towards the player. Moved so the whole circle stays inside the flight grid.
        private Vector3 HerdPoint()
        {
            Vector3 away = herdCentroid - Hub.PlayerPosition;
            away.y = 0f;
            Vector3 direction = away.sqrMagnitude > 1e-4f ? away.normalized : -FlatDirectionToPlayer();
            Vector3 point = herdCentroid + direction * config.HerdOffset;
            point.y = herdCentroid.y + config.HerdHeight;

            FlightGrid grid = FlightGrid.Instance;
            return grid != null ? StoopPlanner.ClampCircleToVolume(point, config.HerdOrbitRadius, grid.VolumeBounds, grid.CellSize) : point;
        }

        // Living birds that are not hawks (and not this bird); also updates herdCentroid when there are any.
        private int CountOtherBirds()
        {
            IReadOnlyList<BirdHealth> alive = BirdRegistry.Alive;
            int count = 0;
            Vector3 sum = Vector3.zero;
            for (int i = 0; i < alive.Count; i++)
            {
                BirdHealth bird = alive[i];
                if (bird == null || bird == Health || !bird.IsAlive)
                    continue;
                if (bird.Species != null && bird.Species.Species == BirdSpecies.Hawk)
                    continue;
                sum += bird.transform.position;
                count++;
            }

            if (count > 0)
                herdCentroid = sum / count;
            return count;
        }

        // Fills 'scores' with every free Perch marker; returns the index of the best one, or -1 if there is none.
        private int ScorePerches(bool withdrawMode)
        {
            BirdPoint.GetAll(BirdPointType.Perch, perches);
            scores.Clear();

            Vector3 sunForward = GetSunForward();
            LayerMask mask = FlightGrid.Instance != null ? FlightGrid.Instance.ObstacleMask : (LayerMask)Physics.DefaultRaycastLayers;
            int best = -1;
            for (int i = 0; i < perches.Count; i++)
            {
                BirdPoint perch = perches[i];
                if (perch == unreachablePerch || IsClaimedByOther(perch))
                    continue;

                // Sight is checked from the soaring height: that is where the hawk watches from.
                PerchScore score = PerchSelector.Score(perch, Hub.PlayerPosition, Hub.PlayerHeadPosition, sunForward,
                    memory, config, withdrawMode, config.SoarHeight, mask);
                scores.Add(score);
                if (best < 0 || score.Total > scores[best].Total)
                    best = scores.Count - 1;
            }
            return best;
        }

        // ---------------------------------------------------------------- State machine

        /// <summary>
        /// Leaves the current mode (its exit step, and the current phase's exit step when leaving Hunt) and enters
        /// <paramref name="next"/>. Entering Hunt starts at <paramref name="huntEntryPhase"/>, so e.g. Herd can jump
        /// straight into <see cref="HuntPhase.Stoop"/>. Dead is final: no change leaves it.
        /// </summary>
        private void ChangeMode(HawkMode next, HuntPhase huntEntryPhase = HuntPhase.SelectVantage)
        {
            if (mode == HawkMode.Dead)
                return;

            LogTransition(CurrentStateName, next == HawkMode.Hunt ? HuntPhaseNames[(int)huntEntryPhase] : ModeNames[(int)next], modeTime);
            ExitMode(mode);
            mode = next;
            modeTime = 0f;
            EnterMode(next, huntEntryPhase);
        }

        /// <summary>Moves to another Hunt sub-state (exit step of the old one, enter step of the new one). Hunt mode only.</summary>
        private void ChangePhase(HuntPhase next)
        {
            if (mode != HawkMode.Hunt)
            {
                Debug.LogWarning($"{LogPrefix} {name}: ChangePhase({next}) ignored in mode {mode}.", this);
                return;
            }

            LogTransition(HuntPhaseNames[(int)phase], HuntPhaseNames[(int)next], phaseTime);
            ExitPhase(phase);
            phase = next;
            phaseTime = 0f;
            EnterPhase(next);
        }

        private void EnterMode(HawkMode entered, HuntPhase huntEntryPhase)
        {
            switch (entered)
            {
                case HawkMode.Hunt:
                    phase = huntEntryPhase;
                    phaseTime = 0f;
                    EnterPhase(huntEntryPhase);
                    break;
                case HawkMode.Herd:
                    stats.HerdEntries++;
                    feintedThisEngagement = false;
                    needsPath = true;
                    nextHerdCheckTime = Time.time + config.HerdCheckInterval;
                    Motor.SpeedMultiplier = 1f;
                    Anim.SetDiving(false);
                    Anim.SetFlying(true);
                    Anim.SetWingsSpread(1f);
                    fear.SetProjecting(true);
                    weakness.BusyEnabled = true; // shooting at the flock now counts as a weakness window
                    break;
                case HawkMode.Withdraw:
                    stats.Withdrawals++;
                    pendingWithdraw = false;
                    stoopFromHerd = false; // shot during a herd strike's scream: that strike never happened
                    feintedThisEngagement = false; // the engagement is over, hit or not
                    withdrawArrived = false;
                    withdrawHideTime = 0f;
                    unreachablePerch = null;
                    Motor.SpeedMultiplier = config.WithdrawSpeedMultiplier;
                    Anim.SetDiving(false);
                    Anim.SetFlying(true);
                    Anim.SetWingsSpread(1f);
                    SelectHidingVantage();
                    break;
                case HawkMode.Dead:
                    ReleasePerch();
                    fear.SetProjecting(false);
                    break;
            }
        }

        private void ExitMode(HawkMode exited)
        {
            switch (exited)
            {
                case HawkMode.Hunt:
                    ExitPhase(phase);
                    // Leaving Hunt mid-attack (e.g. hit during the approach) must not keep the dive speed.
                    Motor.SpeedMultiplier = 1f;
                    ReleasePerch();
                    break;
                case HawkMode.Herd:
                    fear.SetProjecting(false);
                    weakness.BusyEnabled = false;
                    break;
                case HawkMode.Withdraw:
                    Motor.SpeedMultiplier = 1f;
                    ReleasePerch();
                    unreachablePerch = null;
                    break;
            }
        }

        private void EnterPhase(HuntPhase entered)
        {
            switch (entered)
            {
                case HuntPhase.SelectVantage:
                    nextReevaluateTime = 0f;
                    hasStoopStart = false;
                    Motor.SpeedMultiplier = 1f;
                    Anim.SetFlying(true);
                    break;
                case HuntPhase.FlyToVantage:
                    needsPath = true;
                    nextReevaluateTime = Time.time + config.PerchReevaluateInterval;
                    Anim.SetFlying(true);
                    break;
                case HuntPhase.Soaring:
                    unreachablePerch = null;
                    commitUntil = Time.time + config.PerchCommitTime;
                    nextReevaluateTime = Time.time + config.PerchReevaluateInterval;
                    Anim.SetFlying(true);
                    Anim.SetWingsSpread(1f);
                    break;
                case HuntPhase.ApproachStoop:
                    ReleasePerch(); // leaving it: another hawk may take it
                    hasStoopStart = false;
                    nextRepathTime = 0f;
                    Motor.SpeedMultiplier = config.ApproachSpeedMultiplier;
                    Anim.SetFlying(true);
                    break;
                case HuntPhase.Scream:
                    // Hover at the top of the dive with wings tucked: the visual + audio tell before every stoop. The hold
                    // point is a little toward the player, so the turn-limited motor already starts turning into the dive.
                    Motor.HoldPosition(Motor.Position + FlatDirectionToPlayer() * stoopStartArriveDistance);
                    Anim.SetWingsSpread(0f);
                    if (screamClip != null && screamSource != null)
                        screamSource.PlayOneShot(screamClip);
                    break;
                case HuntPhase.Feint:
                    feintPulledUp = false;
                    feintClosest = float.PositiveInfinity;
                    shotTimeAtFeintStart = Hub != null ? Hub.LastShotTime : 0f;
                    Motor.SpeedMultiplier = config.StoopSpeedMultiplier;
                    Anim.SetDiving(true);
                    break;
                case HuntPhase.Stoop:
                    stats.Stoops++;
                    if (stoopAfterFeint)
                        stats.StoopsAfterFeint++;
                    if (stoopFromHerd)
                        stats.HerdStrikes++;
                    stoopClosest = float.PositiveInfinity;
                    Motor.SpeedMultiplier = config.StoopSpeedMultiplier;
                    Anim.SetWingsSpread(0f);
                    Anim.SetDiving(true);
                    break;
                case HuntPhase.Recover:
                    Motor.SpeedMultiplier = 1f;
                    Anim.SetDiving(false);
                    Anim.SetWingsSpread(1f);
                    feintedThisEngagement = false;
                    recoverTarget = RecoverPoint();
                    Motor.FlyTo(recoverTarget);
                    break;
            }
        }

        private void ExitPhase(HuntPhase exited)
        {
            switch (exited)
            {
                case HuntPhase.Feint:
                    Anim.SetDiving(false);
                    break;
                case HuntPhase.Stoop:
                    Anim.SetDiving(false);
                    stoopAfterFeint = false;
                    stoopFromHerd = false;
                    break;
            }
        }

        /// <inheritdoc />
        protected override void OnBirdDied()
        {
            ChangeMode(HawkMode.Dead);
            if (weakness != null)
                weakness.enabled = false;
            base.OnBirdDied();
        }

        // ---------------------------------------------------------------- Helpers

        // Centre of the soaring circle: soarHeight above the vantage point, moved so the whole circle stays in the grid.
        private Vector3 SoarCenter(BirdPoint perch)
        {
            Vector3 center = perch.Position + Vector3.up * config.SoarHeight;
            FlightGrid grid = FlightGrid.Instance;
            return grid != null ? StoopPlanner.ClampCircleToVolume(center, config.SoarRadius, grid.VolumeBounds, grid.CellSize) : center;
        }

        // The point of the circle nearest to the hawk: where the A* path ends before circling starts.
        private Vector3 SoarEntryPoint(Vector3 center)
        {
            return CircleEntryPoint(center, config.SoarRadius);
        }

        private Vector3 CircleEntryPoint(Vector3 center, float radius)
        {
            Vector3 offset = Motor.Position - center;
            offset.y = 0f;
            Vector3 direction = offset.sqrMagnitude > 1e-4f ? offset.normalized : Vector3.forward;
            return center + direction * radius;
        }

        private bool IsNearCircle(Vector3 center)
        {
            return IsNearCircle(center, config.SoarRadius);
        }

        private bool IsNearCircle(Vector3 center, float radius)
        {
            Vector3 offset = Motor.Position - center;
            float flatDistance = new Vector2(offset.x, offset.z).magnitude;
            float tolerance = radius * JoinCircleTolerance;
            return Mathf.Abs(flatDistance - radius) <= tolerance && Mathf.Abs(offset.y) <= tolerance;
        }

        // Climb away ahead of the hawk (forward and up at recoverClimbAngle; the motor can't climb straight up) to
        // recoverClimbHeight above the player, inside the flight volume.
        private Vector3 RecoverPoint()
        {
            Vector3 position = Motor.Position;
            Vector3 forward = Motor.Velocity;
            forward.y = 0f;
            if (forward.sqrMagnitude < 1e-4f)
                forward = new Vector3(transform.forward.x, 0f, transform.forward.z);
            forward = forward.sqrMagnitude > 1e-4f ? forward.normalized : Vector3.forward;

            float playerY = Hub != null && Hub.HasPlayer ? Hub.PlayerPosition.y : 0f;
            float climb = Mathf.Max(0f, playerY + config.RecoverClimbHeight - position.y);
            float horizontal = Mathf.Max(climb / Mathf.Tan(config.RecoverClimbAngle * Mathf.Deg2Rad), config.SoarRadius);
            Vector3 point = position + forward * horizontal + Vector3.up * climb;

            FlightGrid grid = FlightGrid.Instance;
            return grid != null ? StoopPlanner.ClampToVolume(point, grid.VolumeBounds, grid.CellSize) : point;
        }

        private void SetPerch(PerchScore score)
        {
            ReleasePerch();
            currentPerch = score.Perch;
            currentScore = score;
            commitUntil = Time.time + config.PerchCommitTime;
            if (currentPerch != null)
            {
                ClaimedPerches.Add(currentPerch);
                lastVantage = currentPerch; // kept after the vantage is released, for the "shot here" memory (H5)
            }
            Log($"{LogPrefix} {name} {(mode == HawkMode.Withdraw ? "hiding vantage" : "vantage")} -> {currentPerch.name} total {score.Total:0.00} " +
                $"(height {score.Height:0.00}, sight {score.LineOfSight:0}, distance {score.Distance:0.00} @ {score.Metres:0.0} m, sun {score.Sun:0.00}, shot {score.RecentlyShot:0.00})");
        }

        private void ReleasePerch()
        {
            if (currentPerch != null)
                ClaimedPerches.Remove(currentPerch);
            currentPerch = null;
        }

        private bool IsClaimedByOther(BirdPoint perch)
        {
            return perch != currentPerch && ClaimedPerches.Contains(perch);
        }

        /// <summary>
        /// Direction the sunlight travels. Uses <c>RenderSettings.sun</c>, else the first directional light; when there
        /// is none or it is below the horizon, the camera's forward stands in as the "glare" direction.
        /// </summary>
        private Vector3 GetSunForward()
        {
            if (!sunSearched)
            {
                sunSearched = true;
                sun = RenderSettings.sun;
                if (sun == null)
                {
                    Light[] lights = FindObjectsByType<Light>();
                    for (int i = 0; i < lights.Length && sun == null; i++)
                        if (lights[i].type == LightType.Directional)
                            sun = lights[i];
                }
            }

            if (sun != null && sun.isActiveAndEnabled && sun.transform.forward.y < 0f)
                return sun.transform.forward;
            Transform cam = Hub != null ? Hub.CameraTransform : null;
            return cam != null ? cam.forward : Vector3.down;
        }

        // The hawk's shadow sweeping over the ground is its fairness tell before a stoop, so it must always cast one.
        // Runtime only: the prefab asset is not changed. The state label's text is skipped (it must not cast a shadow).
        private void EnableShadows()
        {
            Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
                if (renderers[i].GetComponent<TextMesh>() == null)
                    renderers[i].shadowCastingMode = ShadowCastingMode.On;
        }

        private void LogTransition(string from, string to, float after)
        {
            if (logTransitions && started)
                Debug.Log($"{LogPrefix} {name} {from} -> {to} after {after:0.00}s", this);
        }

        private void Log(string message)
        {
            if (logTransitions)
                Debug.Log(message, this);
        }

        // "<prefix>  meter 60 [Reloading]", cached in 'cache' per (meter step, window).
        private static string MeterLabel(string[,] cache, string prefix, float meterFraction, WeaknessWindow? window)
        {
            int step = Mathf.Clamp(Mathf.FloorToInt(meterFraction * MeterLabelSteps), 0, MeterLabelSteps);
            int column = window.HasValue ? (int)window.Value + 1 : 0;
            string label = cache[step, column];
            if (label == null)
            {
                label = $"{prefix}  meter {step * 100 / MeterLabelSteps}" + (window.HasValue ? $" [{window.Value}]" : "");
                cache[step, column] = label;
            }
            return label;
        }

        private static string[] BuildHuntPhaseNames()
        {
            var names = new string[(int)HuntPhase.Recover + 1];
            for (int i = 0; i < names.Length; i++)
                names[i] = "Hunt/" + (HuntPhase)i;
            return names;
        }

        // Statics survive Play mode when domain reload is off: start every session with no claimed vantage points.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            ClaimedPerches.Clear();
            ActiveHawks.Clear();
        }

        // ---------------------------------------------------------------- Debug

        // Test hooks for the two-level machine (right-click the component in Play mode).
        [ContextMenu("Hawk FSM/Force Hunt")]
        private void ForceHunt()
        {
            if (Application.isPlaying)
                ChangeMode(HawkMode.Hunt);
        }

        [ContextMenu("Hawk FSM/Force Herd")]
        private void ForceHerd()
        {
            if (Application.isPlaying)
                ChangeMode(HawkMode.Herd);
        }

        [ContextMenu("Hawk FSM/Force Withdraw")]
        private void ForceWithdraw()
        {
            if (Application.isPlaying)
                ChangeMode(HawkMode.Withdraw);
        }

        // A real hit through BirdHealth (Damaged event, flinch animation), so the whole chain is tested without shooting.
        // 1 damage with the species' own gun rarity; skipped at 1 HP or less so it can never kill.
        [ContextMenu("Hawk FSM/Simulate Non-Lethal Hit")]
        private void DebugHit()
        {
            if (!Application.isPlaying || Health == null || Health.CurrentHP <= 1f)
                return;
            Log($"{LogPrefix} {name} debug hit (key {debugHitKey} / context menu)");
            Health.TakeDamage(1f, Species != null ? Species.RequiredWeaponRarity : WeaponRarity.Common);
        }

        [ContextMenu("Hawk FSM/Force Hunt Stoop (herd strike)")]
        private void ForceHuntStoop()
        {
            if (Application.isPlaying)
                ChangeMode(HawkMode.Hunt, HuntPhase.Stoop);
        }

#if UNITY_EDITOR
        // Select the hawk in Play mode: every scored vantage point shows its total and terms (H height, L sight,
        // D distance, S sun, X recently shot) from the last evaluation; the chosen one is yellow with its soaring circle,
        // others green (can see the player) or red (can't).
        private void OnDrawGizmosSelected()
        {
            if (!Application.isPlaying || config == null)
                return;

            var style = new GUIStyle(EditorStyles.boldLabel);
            for (int i = 0; i < scores.Count; i++)
            {
                PerchScore score = scores[i];
                if (score.Perch == null)
                    continue;

                bool chosen = score.Perch == currentPerch;
                Color colour = chosen ? Color.yellow : score.LineOfSight > 0f ? Color.green : Color.red;
                Vector3 point = SoarCenter(score.Perch);
                Gizmos.color = colour;
                Gizmos.DrawWireSphere(point, chosen ? 0.6f : 0.4f);

                style.normal.textColor = colour;
                Handles.Label(point + Vector3.up * 1.2f,
                    $"{score.Perch.name}\n{score.Total:0.00}  ({score.Metres:0} m)\nH {score.Height:0.00}  L {score.LineOfSight:0}  D {score.Distance:0.00}\nS {score.Sun:0.00}  X {score.RecentlyShot:0.00}",
                    style);
            }

            if (currentPerch != null)
            {
                Vector3 center = SoarCenter(currentPerch);
                Handles.color = Color.yellow;
                Handles.DrawWireDisc(center, Vector3.up, config.SoarRadius);
                Gizmos.color = Color.yellow;
                Gizmos.DrawLine(transform.position, center);
            }

            // Stoop (H4): sun ray from the player towards the sun, the planned start point, and the dive line.
            if (hasStoopStart && Hub != null && Hub.HasPlayer)
            {
                Vector3 aim = AimPoint();
                Gizmos.color = new Color(1f, 0.9f, 0.2f);
                Gizmos.DrawLine(aim, aim - GetSunForward().normalized * config.StoopStartDistance * 1.2f);
                Gizmos.color = new Color(1f, 0.5f, 0f);
                Gizmos.DrawSphere(stoopStart, 0.6f);
                Gizmos.color = Color.red;
                Gizmos.DrawLine(stoopStart, aim);
                style.normal.textColor = new Color(1f, 0.5f, 0f);
                Handles.Label(stoopStart + Vector3.up * 1f, $"stoop start ({stoopYaw:+0;-0;0}° from sun)", style);
            }

            // Herd (H6): flock centre, herd point with its circle, and the fear field while it is registered.
            if (mode == HawkMode.Herd && othersAlive > 0)
            {
                Gizmos.color = Color.cyan;
                Gizmos.DrawSphere(herdCentroid, 0.5f);
                Gizmos.DrawLine(herdCentroid, herdPoint);
                Handles.color = Color.cyan;
                Handles.DrawWireDisc(herdPoint, Vector3.up, config.HerdOrbitRadius);
                style.normal.textColor = Color.cyan;
                Handles.Label(herdCentroid + Vector3.up * 1f, $"flock centre ({othersAlive} birds)", style);
                Handles.Label(herdPoint + Vector3.up * 1f, "herd point", style);
            }
            if (IsProjectingFear)
            {
                Gizmos.color = new Color(1f, 0.2f, 0.6f, 0.15f);
                Gizmos.DrawSphere(transform.position, config.FearRadius);
                Gizmos.color = new Color(1f, 0.2f, 0.6f);
                Gizmos.DrawWireSphere(transform.position, config.FearRadius);
            }
        }
#endif
    }
}
