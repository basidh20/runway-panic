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
    /// re-scores too and relocates when the player hides.</para>
    /// <para>Not built yet: Perched leaves after <c>maxPerchTime</c> (the aggression meter comes in H3); ApproachStoop
    /// to Recover, Herd and Withdraw still just wait <c>skeletonPhaseDuration</c> and move on (H4–H6).</para>
    /// </summary>
    public sealed class HawkBrain : BirdBrain
    {
        private const string LogPrefix = "[HawkFSM]";

        // Cached label strings, indexed by the enums: CurrentStateName never allocates.
        private static readonly string[] HuntPhaseNames = BuildHuntPhaseNames();
        private static readonly string[] ModeNames = { "Hunt", "Herd", "Withdraw", "Dead" };

        // Perches taken by a hawk (chosen or sat on), so two hawks never pick the same one.
        private static readonly List<BirdPoint> ClaimedPerches = new List<BirdPoint>(8);

        [Tooltip("The hawk's strategy numbers (ScriptableObjects/Birds/Strategy/HawkStrategyConfig).")]
        [SerializeField] private HawkStrategyConfig config;
        [Tooltip("Height of the hawk's pivot above the perch surface when it sits there, in metres (feet to centre of mass).")]
        [SerializeField, Min(0f)] private float perchPivotHeight = 0.24f;
        [Tooltip("Height above the perch surface the line-of-sight check starts from (the hawk's eyes), in metres.")]
        [SerializeField, Min(0f)] private float eyeHeight = 0.3f;
        [Tooltip("Skeleton phases only (ApproachStoop to Recover, Herd, Withdraw): seconds before moving on. Replaced by real behaviour in H4–H6.")]
        [SerializeField, Min(0.1f)] private float skeletonPhaseDuration = 2f;
        [Tooltip("Write every mode/phase change and perch choice to the Console / Editor.log (prefix [HawkFSM]).")]
        [SerializeField] private bool logTransitions = true;

        private readonly List<BirdPoint> perches = new List<BirdPoint>(16);
        private readonly List<PerchScore> scores = new List<PerchScore>(16);
        private readonly PerchMemory memory = new PerchMemory();

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

        private Light sun;
        private bool sunSearched;

        /// <inheritdoc />
        public override string CurrentStateName => mode == HawkMode.Hunt ? HuntPhaseNames[(int)phase] : ModeNames[(int)mode];

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

        protected override void Awake()
        {
            base.Awake();

            if (config == null)
            {
                Debug.LogError($"{nameof(HawkBrain)} on '{name}' has no {nameof(HawkStrategyConfig)} assigned.", this);
                enabled = false;
            }

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
                default:
                    // Skeleton until H4: walk through the attack phases in order, then pick a perch again.
                    if (phaseTime >= skeletonPhaseDuration)
                        ChangePhase(phase == HuntPhase.Recover ? HuntPhase.SelectPerch : phase + 1);
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
            // H3 replaces this with the aggression meter; maxPerchTime stays as the "never passive" fallback.
            if (phaseTime >= config.MaxPerchTime)
            {
                ChangePhase(HuntPhase.ApproachStoop);
                return;
            }

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
                    Anim.SetFlying(true);
                    break;
                case HuntPhase.FlyToPerch:
                    needsPath = true;
                    landing = false;
                    nextReevaluateTime = Time.time + config.PerchReevaluateInterval;
                    Anim.SetFlying(true);
                    break;
                case HuntPhase.ApproachStoop:
                    Anim.SetFlying(true);
                    break;
                case HuntPhase.Perched:
                    unreachablePerch = null;
                    commitUntil = Time.time + config.PerchCommitTime;
                    nextReevaluateTime = Time.time + config.PerchReevaluateInterval;
                    Anim.SetFlying(false);
                    break;
                case HuntPhase.Scream:
                    Anim.SetWingsSpread(0f);
                    break;
                case HuntPhase.Feint:
                case HuntPhase.Stoop:
                    Anim.SetDiving(true);
                    break;
                case HuntPhase.Recover:
                    Motor.SpeedMultiplier = 1f;
                    Anim.SetWingsSpread(1f);
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
            base.OnBirdDied();
        }

        // ---------------------------------------------------------------- Helpers

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
        }
#endif
    }
}
