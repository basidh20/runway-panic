using System.Collections.Generic;
using RunwayPanic.AI;
using UnityEngine;
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
    /// <para>The FSM decides <i>what</i> the hawk does; utility AI (<see cref="PerchSelector"/>) decides <i>which perch</i>.
    /// SelectPerch picks the best-scoring perch, FlyToPerch flies there along an A* path (re-scoring every
    /// <c>perchReevaluateInterval</c>, switching only for a clearly better perch) and lands facing the player, Perched
    /// re-scores too and relocates when the player hides, turns to keep facing the player, and attacks when the
    /// <see cref="PlayerWeaknessModel"/>'s meter is full while a weakness window is open (or after <c>maxPerchTime</c>).</para>
    /// <para>Attack (H4): against a slow gun it may first <b>feint</b> (dive, pull up short, wait out the player's refire
    /// time, then dive for real). ApproachStoop flies to a start point on the sun's side of the player
    /// (<see cref="StoopPlanner"/>), Scream hovers at the top, Stoop dives at where the player will be, Recover climbs away.</para>
    /// <para>Not built yet: Herd and Withdraw just wait <c>skeletonPhaseDuration</c> and return to Hunt (H5–H6).</para>
    /// </summary>
    public sealed class HawkBrain : BirdBrain
    {
        private const string LogPrefix = "[HawkFSM]";

        // Cached label strings, indexed by the enums: CurrentStateName never allocates.
        private static readonly string[] HuntPhaseNames = BuildHuntPhaseNames();
        private static readonly string[] ModeNames = { "Hunt", "Herd", "Withdraw", "Dead" };

        // Perches taken by a hawk (chosen or sat on), so two hawks never pick the same one.
        private static readonly List<BirdPoint> ClaimedPerches = new List<BirdPoint>(8);

        // "Hunt/Perched  meter 60 [Reloading]" labels, built once per (meter step, window) on first use: no per-frame allocation.
        private const int MeterLabelSteps = 10;
        private static readonly string[,] PerchedLabels = new string[MeterLabelSteps + 1, (int)WeaknessWindow.Busy + 2];

        [Tooltip("The hawk's strategy numbers (ScriptableObjects/Birds/Strategy/HawkStrategyConfig).")]
        [SerializeField] private HawkStrategyConfig config;
        [Tooltip("Height of the hawk's pivot above the perch surface when it sits there, in metres (feet to centre of mass).")]
        [SerializeField, Min(0f)] private float perchPivotHeight = 0.24f;
        [Tooltip("Height above the perch surface the line-of-sight check starts from (the hawk's eyes), in metres.")]
        [SerializeField, Min(0f)] private float eyeHeight = 0.3f;
        [Tooltip("How fast the perched hawk turns (yaw only) to keep facing the player, in degrees per second.")]
        [SerializeField, Min(0f)] private float perchTurnSpeed = 120f;
        [Tooltip("Height above the player's feet the dive aims at, in metres (chest height).")]
        [SerializeField, Min(0f)] private float stoopAimHeight = 1.2f;
        [Tooltip("How hard the motor can brake, in m/s² — must match the motor (SimpleBirdMotor.acceleration on P_Bird_Hawk = 40). " +
                 "Used to start the feint's pull-up and the stoop's flare early enough: braking distance = speed² / (2 × this).")]
        [SerializeField, Min(1f)] private float diveBrakeAcceleration = 40f;
        [Tooltip("The dive levels out this far above the aim point (chest), in metres, so it never runs into the ground.")]
        [SerializeField, Min(0f)] private float stoopFlareHeight = 1f;
        [Tooltip("Share of the motor's braking assumed free for pulling out of the dive. Below 1 because the motor also turns " +
                 "to chase a moving player with the same acceleration (playtest 17 Oct: at 1.0 the hawk skimmed the runway at 0.2 m).")]
        [SerializeField, Range(0.1f, 1f)] private float flareBrakeShare = 0.5f;
        [Tooltip("Distance that counts as arrived at the stoop start point, in metres.")]
        [SerializeField, Min(0.1f)] private float stoopStartArriveDistance = 2f;
        [Tooltip("Optional scream played at the top of every dive (the audio tell). Skipped if empty.")]
        [SerializeField] private AudioClip screamClip;
        [Tooltip("Plays the scream. Taken from this object if empty; skipped if there is none.")]
        [SerializeField] private AudioSource screamSource;
        [Tooltip("Skeleton modes only (Herd, Withdraw): seconds before returning to Hunt. Replaced by real behaviour in H5–H6.")]
        [SerializeField, Min(0.1f)] private float skeletonPhaseDuration = 2f;
        [Tooltip("Write every mode/phase change and perch choice to the Console / Editor.log (prefix [HawkFSM]).")]
        [SerializeField] private bool logTransitions = true;

        private readonly List<BirdPoint> perches = new List<BirdPoint>(16);
        private readonly List<PerchScore> scores = new List<PerchScore>(16);
        private readonly PerchMemory memory = new PerchMemory();
        private PlayerWeaknessModel weakness;

        private HawkMode mode = HawkMode.Hunt;
        private HuntPhase phase = HuntPhase.SelectPerch;
        private float modeTime;
        private float phaseTime;
        private bool started;

        private BirdPoint currentPerch;
        private PerchScore currentScore;
        private BirdPoint unreachablePerch;
        private float nextReevaluateTime;
        private float commitUntil;
        private bool needsPath;
        private bool landing;
        private bool warnedNoPerches;
        private Vector3 perchFacing;

        // Attack state (H4).
        private const float MinPassSpeed = 1f; // m/s: below this the dive direction is noise, so "passed the player" is not judged
        private bool planFeint;
        private bool feintedThisEngagement;
        private bool feintPulledUp;
        private bool stoopWasClosing;
        private float feintWaitUntil;
        private float shotTimeAtFeintStart;
        private bool hasStoopStart;
        private Vector3 stoopStart;
        private float stoopYaw;
        private float nextRepathTime;

        private Light sun;
        private bool sunSearched;

        /// <inheritdoc />
        public override string CurrentStateName
        {
            get
            {
                if (mode != HawkMode.Hunt)
                    return ModeNames[(int)mode];
                if (phase == HuntPhase.Perched && weakness != null && config != null)
                    return PerchedLabel(weakness.Meter / config.MeterMax, weakness.StrongestOpenWindow);
                return HuntPhaseNames[(int)phase];
            }
        }

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

        /// <summary>The perch the hawk is flying to or sitting on (null if none).</summary>
        public BirdPoint CurrentPerch => currentPerch;

        /// <summary>Score of <see cref="CurrentPerch"/> at its last evaluation.</summary>
        public PerchScore CurrentPerchScore => currentScore;

        /// <summary>This hawk's memory of perches where it was shot.</summary>
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

            EnableShadows();
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            ReleasePerch();
        }

        private void Start()
        {
            started = true;
            modeTime = 0f;
            EnterMode(mode, HuntPhase.SelectPerch);
        }

        private void Update()
        {
            // Timers only run during active gameplay, so pausing or the shop never counts as perch time.
            if (!IsGameplayActive)
                return;

            float dt = Time.deltaTime;
            modeTime += dt;
            if (mode == HawkMode.Hunt)
                phaseTime += dt;
            memory.Decay(config.RecentlyShotDecayPerSecond * dt);

            switch (mode)
            {
                case HawkMode.Hunt:
                    UpdateHunt();
                    break;
                case HawkMode.Herd:
                case HawkMode.Withdraw:
                    if (modeTime >= skeletonPhaseDuration)
                        ChangeMode(HawkMode.Hunt);
                    break;
            }
        }

        // ---------------------------------------------------------------- Hunt phases

        private void UpdateHunt()
        {
            if (Hub == null || !Hub.HasPlayer)
                return;

            switch (phase)
            {
                case HuntPhase.SelectPerch:
                    UpdateSelectPerch();
                    break;
                case HuntPhase.FlyToPerch:
                    UpdateFlyToPerch();
                    break;
                case HuntPhase.Perched:
                    UpdatePerched();
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
                        ChangePhase(HuntPhase.SelectPerch);
                    break;
            }
        }

        private void UpdateSelectPerch()
        {
            if (Time.time < nextReevaluateTime)
                return;
            nextReevaluateTime = Time.time + config.PerchReevaluateInterval;

            int best = ScorePerches(false);
            if (best < 0)
            {
                // Nothing reachable: forget the failed perch and try everything again next time.
                unreachablePerch = null;
                if (!warnedNoPerches)
                {
                    warnedNoPerches = true;
                    Debug.LogWarning($"{LogPrefix} {name}: no free Perch BirdPoint in the scene; the hawk hovers until one exists.", this);
                }
                return;
            }

            SetPerch(scores[best]);
            ChangePhase(HuntPhase.FlyToPerch);
        }

        private void UpdateFlyToPerch()
        {
            if (currentPerch == null || !currentPerch.isActiveAndEnabled)
            {
                ChangePhase(HuntPhase.SelectPerch);
                return;
            }

            Vector3 landPoint = LandPoint(currentPerch);
            if (landing)
            {
                if (Motor.IsLanded)
                    ChangePhase(HuntPhase.Perched);
                return;
            }

            // Close enough (or at the end of the path): the last leg is a straight hop. The path's last cell always
            // has a clear line to the perch (F1 snapping rule), so the hop never goes through a roof edge.
            float landRadius = currentPerch.Radius;
            if ((landPoint - Motor.Position).sqrMagnitude < landRadius * landRadius || (!needsPath && Motor.ReachedDestination))
            {
                landing = true;
                Motor.LandAt(landPoint, FacingPlayer(landPoint));
                return;
            }

            if (needsPath)
            {
                if (RequestPath(landPoint))
                {
                    needsPath = false;
                }
                else if (!LastRequestWasThrottled)
                {
                    Log($"{LogPrefix} {name} no path to {currentPerch.name}, choosing another perch");
                    unreachablePerch = currentPerch;
                    ChangePhase(HuntPhase.SelectPerch);
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

        private void UpdatePerched()
        {
            // Attack when the meter is full AND the player is weak right now; or after maxPerchTime anyway (never passive).
            bool ready = weakness.IsMeterFull && weakness.AnyWindowOpen;
            if (ready || phaseTime >= config.MaxPerchTime)
            {
                if (ready)
                    Log($"{LogPrefix} {name} attack: meter full, window {weakness.StrongestOpenWindow} open, after {phaseTime:0.0}s perched");
                else
                    Log($"{LogPrefix} {name} attack: max perch time {config.MaxPerchTime:0}s (meter {weakness.Meter:0})");
                weakness.ConsumeMeter();
                PlanAttack();
                ChangePhase(HuntPhase.ApproachStoop);
                return;
            }

            FacePlayer();

            if (Time.time < nextReevaluateTime)
                return;
            nextReevaluateTime = Time.time + config.PerchReevaluateInterval;

            // The player hid or moved away: take off for a clearly better perch.
            if (TrySwitchToBetterPerch())
                ChangePhase(HuntPhase.FlyToPerch);
        }

        // Re-scores every perch; switches when one beats the current perch by more than the hysteresis margin.
        // Not during the commit time after a choice or landing (decision inertia).
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

        // Decided once when leaving the perch: feint only against a slow gun (it exploits the refire time), once per engagement.
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
            if (!feintPulledUp)
            {
                Motor.FlyTo(StoopPlanner.GetLeadPoint(aim, Hub.PlayerVelocity, config.StoopLeadTime));

                // Pull up early enough that, after braking, the closest approach is still pullUpDistance.
                Vector3 toAim = aim - Motor.Position;
                float distance = toAim.magnitude;
                float closingSpeed = distance > 0.001f ? Vector3.Dot(Motor.Velocity, toAim / distance) : 0f;
                if (distance - BrakingDistance(closingSpeed) <= config.PullUpDistance)
                {
                    // Pull up and swing back, then wait out the player's refire time before the real dive.
                    Vector3 away = Motor.Position - aim;
                    away.y = 0f;
                    away = away.sqrMagnitude > 0.0001f ? away.normalized : -transform.forward;
                    Vector3 pullUp = Motor.Position + Vector3.up * config.FeintPullUpHeight + away * config.FeintPullUpBack;
                    if (FlightGrid.Instance != null)
                        pullUp = StoopPlanner.ClampToVolume(pullUp, FlightGrid.Instance.VolumeBounds, FlightGrid.Instance.CellSize);
                    Motor.FlyTo(pullUp);

                    float refire = Hub.CurrentWeapon != null && Hub.CurrentWeapon.Data != null ? Hub.CurrentWeapon.Data.FireInterval : 0f;
                    feintPulledUp = true;
                    feintedThisEngagement = true;
                    feintWaitUntil = phaseTime + refire;
                    Anim.SetDiving(false);
                    Anim.SetWingsSpread(1f);
                    Log($"{LogPrefix} {name} feint: pulled up {distance:0.0} m from the player at {closingSpeed:0} m/s " +
                        $"(braking {BrakingDistance(closingSpeed):0.0} m), waiting {refire:0.00}s (refire time)");
                }
                else if (phaseTime >= config.MaxManoeuvreTime)
                {
                    ChangePhase(HuntPhase.Stoop);
                }
                return;
            }

            if (phaseTime >= feintWaitUntil)
            {
                bool baited = Hub.LastShotTime > shotTimeAtFeintStart;
                Log($"{LogPrefix} {name} feint {(baited ? "BAITED a shot" : "drew no shot")}, real stoop now");
                ChangePhase(HuntPhase.Stoop); // straight from here: the point is to hit during the refire
            }
        }

        private void UpdateStoop()
        {
            Vector3 aim = AimPoint();
            Vector3 target = StoopPlanner.GetLeadPoint(aim, Hub.PlayerVelocity, config.StoopLeadTime);

            // Flare: once the hawk could no longer stop above the flare height, aim level at it instead of at the chest.
            // It then sweeps in just above the player (still inside the attack range) rather than into the ground.
            float flareY = aim.y + stoopFlareHeight;
            float lowestPoint = Motor.Position.y - BrakingDistance(Mathf.Max(0f, -Motor.Velocity.y)) / flareBrakeShare;
            if (lowestPoint <= flareY)
                target.y = Mathf.Max(target.y, flareY);
            Motor.FlyTo(target);

            Vector3 toAim = aim - Motor.Position;
            float range = Species != null ? Species.AttackRange : 0f;
            if (toAim.sqrMagnitude <= range * range)
            {
                float damage = (Species != null ? Species.AttackDamage : 0f) * config.StoopDamageMultiplier;
                bool dealt = Attack != null && Attack.TryAttackWithDamage(damage);
                Log($"{LogPrefix} {name} stoop HIT range after {phaseTime:0.0}s: damage {damage:0} " +
                    $"{(dealt ? "dealt" : "not dealt (BirdContactAttack refused: F2 stub until F3, or cooldown)")}");
                ChangePhase(HuntPhase.Recover);
                return;
            }

            // "Passed the player" only counts after the dive has been closing in: at the start the hawk may still carry
            // velocity away from the player (from the approach or the feint's climb).
            Vector3 velocity = Motor.Velocity;
            bool moving = velocity.sqrMagnitude > MinPassSpeed * MinPassSpeed;
            float closing = Vector3.Dot(velocity, toAim);
            if (moving && closing > 0f)
                stoopWasClosing = true;
            bool passed = stoopWasClosing && moving && closing < 0f;
            if (passed || phaseTime >= config.MaxManoeuvreTime)
            {
                Log($"{LogPrefix} {name} stoop MISSED ({(passed ? "passed the player" : "timed out")}, {toAim.magnitude:0.0} m away)");
                ChangePhase(HuntPhase.Recover);
            }
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

            Vector3 fallback = StoopPlanner.GetStoopStart(player, sunForward, config.StoopStartDistance, config.MinStoopHeight, 0f);
            bool haveFallback = false;
            int attempts = 1 + 2 * config.StoopYawTries;
            for (int i = 0; i < attempts; i++)
            {
                // 0, +step, -step, +2·step, -2·step, ...
                float yaw = (i + 1) / 2 * config.StoopYawStep * (i % 2 == 1 ? 1f : -1f);
                Vector3 start = StoopPlanner.GetStoopStart(player, sunForward, config.StoopStartDistance, config.MinStoopHeight, yaw);
                if (grid != null)
                    start = StoopPlanner.ClampToVolume(start, grid.VolumeBounds, grid.CellSize);
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
            Log($"{LogPrefix} {name} stoop start {start} ({yaw:+0;-0;0}° from the sun side, {start.y - Hub.PlayerPosition.y:0.0} m above the player, " +
                $"{(clearLine ? "clear dive line" : "NO clear dive line found")})");
        }

        // Distance the motor needs to shed 'speed' (m/s) at diveBrakeAcceleration: v² / 2a.
        private float BrakingDistance(float speed)
        {
            return speed > 0f ? speed * speed / (2f * diveBrakeAcceleration) : 0f;
        }

        private Vector3 AimPoint()
        {
            return Hub.PlayerPosition + Vector3.up * stoopAimHeight;
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

                PerchScore score = PerchSelector.Score(perch, Hub.PlayerPosition, Hub.PlayerHeadPosition, sunForward,
                    memory, config, withdrawMode, eyeHeight, mask);
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
        private void ChangeMode(HawkMode next, HuntPhase huntEntryPhase = HuntPhase.SelectPerch)
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
                    Anim.SetFlying(true);
                    break;
                case HawkMode.Withdraw:
                    Anim.SetFlying(true);
                    break;
                case HawkMode.Dead:
                    ReleasePerch();
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
                    break;
                case HawkMode.Withdraw:
                    break;
            }
        }

        private void EnterPhase(HuntPhase entered)
        {
            switch (entered)
            {
                case HuntPhase.SelectPerch:
                    nextReevaluateTime = 0f;
                    hasStoopStart = false;
                    Anim.SetFlying(true);
                    break;
                case HuntPhase.FlyToPerch:
                    needsPath = true;
                    landing = false;
                    nextReevaluateTime = Time.time + config.PerchReevaluateInterval;
                    Anim.SetFlying(true);
                    break;
                case HuntPhase.ApproachStoop:
                    ReleasePerch(); // leaving it: another hawk may take it
                    hasStoopStart = false;
                    nextRepathTime = 0f;
                    Motor.SpeedMultiplier = config.ApproachSpeedMultiplier;
                    Anim.SetFlying(true);
                    break;
                case HuntPhase.Perched:
                    unreachablePerch = null;
                    perchFacing = currentPerch != null ? FacingPlayer(LandPoint(currentPerch)) : transform.forward;
                    commitUntil = Time.time + config.PerchCommitTime;
                    nextReevaluateTime = Time.time + config.PerchReevaluateInterval;
                    Anim.SetFlying(false);
                    break;
                case HuntPhase.Scream:
                    // Hover at the top of the dive with wings tucked: the visual + audio tell before every stoop.
                    Motor.HoldPosition(Motor.Position);
                    Anim.SetWingsSpread(0f);
                    if (screamClip != null && screamSource != null)
                        screamSource.PlayOneShot(screamClip);
                    break;
                case HuntPhase.Feint:
                    feintPulledUp = false;
                    shotTimeAtFeintStart = Hub != null ? Hub.LastShotTime : 0f;
                    Motor.SpeedMultiplier = config.StoopSpeedMultiplier;
                    Anim.SetDiving(true);
                    break;
                case HuntPhase.Stoop:
                    stoopWasClosing = false;
                    Motor.SpeedMultiplier = config.StoopSpeedMultiplier;
                    Anim.SetWingsSpread(0f);
                    Anim.SetDiving(true);
                    break;
                case HuntPhase.Recover:
                    Motor.SpeedMultiplier = 1f;
                    Anim.SetDiving(false);
                    Anim.SetWingsSpread(1f);
                    feintedThisEngagement = false;
                    Motor.FlyTo(RecoverPoint());
                    break;
            }
        }

        private void ExitPhase(HuntPhase exited)
        {
            switch (exited)
            {
                case HuntPhase.FlyToPerch:
                    landing = false;
                    break;
                case HuntPhase.Perched:
                    Anim.SetFlying(true);
                    break;
                case HuntPhase.Feint:
                case HuntPhase.Stoop:
                    Anim.SetDiving(false);
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

        // Straight up to recoverClimbHeight above the player (never down), kept inside the flight volume.
        private Vector3 RecoverPoint()
        {
            Vector3 point = Motor.Position;
            float playerY = Hub != null && Hub.HasPlayer ? Hub.PlayerPosition.y : 0f;
            point.y = Mathf.Max(point.y, playerY + config.RecoverClimbHeight);
            FlightGrid grid = FlightGrid.Instance;
            return grid != null ? StoopPlanner.ClampToVolume(point, grid.VolumeBounds, grid.CellSize) : point;
        }

        private Vector3 LandPoint(BirdPoint perch)
        {
            return perch.Position + Vector3.up * perchPivotHeight;
        }

        private Vector3 FacingPlayer(Vector3 from)
        {
            Vector3 toPlayer = Hub.PlayerPosition - from;
            toPlayer.y = 0f;
            return toPlayer.sqrMagnitude > 0.0001f ? toPlayer : transform.forward;
        }

        // While perched: turn (yaw only) toward the player at perchTurnSpeed. Goes through the motor (LandAt on the same
        // spot with a new facing), only on frames where the facing actually changes.
        private void FacePlayer()
        {
            if (currentPerch == null)
                return;

            Vector3 landPoint = LandPoint(currentPerch);
            Vector3 target = FacingPlayer(landPoint);
            Vector3 turned = Vector3.RotateTowards(perchFacing, target, perchTurnSpeed * Mathf.Deg2Rad * Time.deltaTime, 0f);
            if (turned == perchFacing)
                return;

            perchFacing = turned;
            Motor.LandAt(landPoint, perchFacing);
        }

        private void SetPerch(PerchScore score)
        {
            ReleasePerch();
            currentPerch = score.Perch;
            currentScore = score;
            commitUntil = Time.time + config.PerchCommitTime;
            if (currentPerch != null)
                ClaimedPerches.Add(currentPerch);
            Log($"{LogPrefix} {name} perch -> {currentPerch.name} total {score.Total:0.00} " +
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

        private static string PerchedLabel(float meterFraction, WeaknessWindow? window)
        {
            int step = Mathf.Clamp(Mathf.FloorToInt(meterFraction * MeterLabelSteps), 0, MeterLabelSteps);
            int column = window.HasValue ? (int)window.Value + 1 : 0;
            string label = PerchedLabels[step, column];
            if (label == null)
            {
                label = $"Hunt/Perched  meter {step * 100 / MeterLabelSteps}" + (window.HasValue ? $" [{window.Value}]" : "");
                PerchedLabels[step, column] = label;
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

        // Statics survive Play mode when domain reload is off: start every session with no claimed perches.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            ClaimedPerches.Clear();
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

        [ContextMenu("Hawk FSM/Force Hunt Stoop (herd strike)")]
        private void ForceHuntStoop()
        {
            if (Application.isPlaying)
                ChangeMode(HawkMode.Hunt, HuntPhase.Stoop);
        }

#if UNITY_EDITOR
        // Select the hawk in Play mode: every scored perch shows its total and terms (H height, L sight, D distance,
        // S sun, X recently shot) from the last evaluation; the chosen perch is yellow, others green (can see the player)
        // or red (can't).
        private void OnDrawGizmosSelected()
        {
            if (!Application.isPlaying)
                return;

            var style = new GUIStyle(EditorStyles.boldLabel);
            for (int i = 0; i < scores.Count; i++)
            {
                PerchScore score = scores[i];
                if (score.Perch == null)
                    continue;

                bool chosen = score.Perch == currentPerch;
                Color colour = chosen ? Color.yellow : score.LineOfSight > 0f ? Color.green : Color.red;
                Vector3 point = LandPoint(score.Perch);
                Gizmos.color = colour;
                Gizmos.DrawWireSphere(point, chosen ? 0.6f : 0.4f);

                style.normal.textColor = colour;
                Handles.Label(point + Vector3.up * 1.2f,
                    $"{score.Perch.name}\n{score.Total:0.00}  ({score.Metres:0} m)\nH {score.Height:0.00}  L {score.LineOfSight:0}  D {score.Distance:0.00}\nS {score.Sun:0.00}  X {score.RecentlyShot:0.00}",
                    style);
            }

            if (currentPerch != null)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawLine(transform.position, LandPoint(currentPerch));
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
        }
#endif
    }
}
