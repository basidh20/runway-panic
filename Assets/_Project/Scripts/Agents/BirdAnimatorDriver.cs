using UnityEngine;

namespace RunwayPanic.Agents
{
    /// <summary>
    /// The bird's animation (F4, Agent Controller): the Animator-based <see cref="IBirdAnimator"/> that species brains
    /// animate birds with. It feeds AC_Bird (on the model child) from the bird's movement: how fast it flies and how
    /// fast it climbs or dives pick the wing motion in the Locomotion blend tree. On top of that the brain can perch it
    /// (<see cref="SetFlying"/>), hold the dive pose (<see cref="SetDiving"/>), fold the wings
    /// (<see cref="SetWingsSpread"/>) and play the hit and death reactions; <see cref="BirdContactAttack"/> plays the
    /// strike. A bird that lands with <see cref="IBirdMotor.LandAt"/> perches by itself.
    /// Root motion is off (BirdMotor moves the bird) and off-screen birds skip writing their bones.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BirdMotor))]
    public sealed class BirdAnimatorDriver : MonoBehaviour, IBirdAnimator
    {
        // Parameter names, shared with the editor tool that builds AC_Bird.
        public const string SpeedParameter = "Speed";
        public const string VerticalSpeedParameter = "VerticalSpeed";
        public const string FlapSpeedParameter = "FlapSpeed";
        public const string CycleOffsetParameter = "CycleOffset";
        public const string FlyingParameter = "Flying";
        public const string DivingParameter = "Diving";
        public const string AttackTrigger = "Attack";
        public const string HitTrigger = "Hit";
        public const string DeathTrigger = "Death";

        private const string MeshName = "Bird_Mesh";
        private const string WingsShape = "Wings_Spread";
        // A perched bird that moves faster than this has taken off, even if the brain forgot SetFlying(true).
        private const float TakeOffSpeed = 0.5f;

        // Hashed once: SetFloat(int) skips hashing the string on every call.
        private static readonly int SpeedId = Animator.StringToHash(SpeedParameter);
        private static readonly int VerticalSpeedId = Animator.StringToHash(VerticalSpeedParameter);
        private static readonly int FlapSpeedId = Animator.StringToHash(FlapSpeedParameter);
        private static readonly int CycleOffsetId = Animator.StringToHash(CycleOffsetParameter);
        private static readonly int FlyingId = Animator.StringToHash(FlyingParameter);
        private static readonly int DivingId = Animator.StringToHash(DivingParameter);
        private static readonly int AttackId = Animator.StringToHash(AttackTrigger);
        private static readonly int HitId = Animator.StringToHash(HitTrigger);
        private static readonly int DeathId = Animator.StringToHash(DeathTrigger);

        [Tooltip("Animator on the model child. Found automatically if left empty.")]
        [SerializeField] private Animator animator;
        [Tooltip("Random ± change to each bird's wing-beat speed, so a flock does not flap in sync.")]
        [SerializeField, Range(0f, 0.3f)] private float flapSpeedVariation = 0.1f;
        [Tooltip("Seconds Speed and VerticalSpeed take to follow the bird, so the blend does not flicker.")]
        [SerializeField, Range(0f, 0.5f)] private float parameterDampTime = 0.15f;
        [Tooltip("How fast the wings move toward a SetWingsSpread value, in full spreads per second.")]
        [SerializeField, Min(0.1f)] private float wingSpreadSpeed = 4f;

        private BirdMotor motor;
        private SkinnedMeshRenderer wingsRenderer;
        private int wingsIndex = -1;
        private bool hasController;
        private bool dead;
        private bool flyingRequested = true;
        private bool flyingSent = true;
        private bool divingSent;
        private float wingsTarget = 1f;
        private float wingsScale = 1f;

        private void Awake()
        {
            motor = GetComponent<BirdMotor>();
            if (animator == null)
                animator = GetComponentInChildren<Animator>();

            hasController = animator != null && animator.runtimeAnimatorController != null;
            if (!hasController)
            {
                Debug.LogWarning($"[Agents] {name}: no Animator with a controller - run Tools > Agents > Setup Bird Prefabs.", this);
                enabled = false;
                return;
            }

            // BirdMotor moves the bird; clips only rotate bones.
            animator.applyRootMotion = false;
            // Off-screen birds keep their state machine running (triggers still work) but skip writing bones.
            animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;

            foreach (SkinnedMeshRenderer candidate in GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (candidate.name == MeshName && candidate.sharedMesh != null)
                {
                    wingsRenderer = candidate;
                    wingsIndex = candidate.sharedMesh.GetBlendShapeIndex(WingsShape);
                    break;
                }
            }
        }

        private void OnEnable()
        {
            if (hasController)
                Desynchronise();
        }

        private void Update()
        {
            if (dead)
                return;

            float maxSpeed = Mathf.Max(motor.BaseSpeed, 0.01f);
            float speed01 = Mathf.Clamp01(motor.Speed / maxSpeed);

            // -1 = diving at the steepest allowed angle and full speed, +1 = climbing the same way.
            float maxClimbRate = maxSpeed * Mathf.Sin(motor.MaxPitch * Mathf.Deg2Rad);
            float vertical = Mathf.Clamp(motor.Velocity.y / Mathf.Max(maxClimbRate, 0.01f), -1f, 1f);

            animator.SetFloat(SpeedId, speed01, parameterDampTime, Time.deltaTime);
            animator.SetFloat(VerticalSpeedId, vertical, parameterDampTime, Time.deltaTime);

            // Perched while landed or told not to fly; flying again as soon as the bird actually moves off.
            if (!flyingRequested && !motor.IsLanded && motor.Speed > TakeOffSpeed)
                flyingRequested = true;
            bool flying = flyingRequested && !motor.IsLanded;
            if (flying != flyingSent)
            {
                flyingSent = flying;
                animator.SetBool(FlyingId, flying); // written only when it changes
            }
        }

        // After the Animator has written this frame's pose: fold the animated wing spread by the brain's amount.
        private void LateUpdate()
        {
            if (wingsIndex < 0 || dead)
                return;

            wingsScale = Mathf.MoveTowards(wingsScale, wingsTarget, wingSpreadSpeed * Time.deltaTime);
            if (wingsScale < 0.999f)
                wingsRenderer.SetBlendShapeWeight(wingsIndex, wingsRenderer.GetBlendShapeWeight(wingsIndex) * wingsScale);
        }

        // ---------- IBirdAnimator ----------

        /// <inheritdoc />
        public void SetFlying(bool flying)
        {
            flyingRequested = flying;
        }

        /// <inheritdoc />
        public void SetDiving(bool diving)
        {
            if (!hasController || dead || diving == divingSent)
                return;

            divingSent = diving;
            animator.SetBool(DivingId, diving);
        }

        /// <inheritdoc />
        public void SetWingsSpread(float amount)
        {
            wingsTarget = Mathf.Clamp01(amount);
        }

        /// <inheritdoc />
        public void PlayHit()
        {
            if (hasController && !dead)
                animator.SetTrigger(HitId);
        }

        /// <inheritdoc />
        public void PlayDeath()
        {
            if (!hasController || dead)
                return;

            dead = true;
            animator.ResetTrigger(AttackId);
            animator.ResetTrigger(HitId);
            animator.SetTrigger(DeathId);
        }

        // ---------- Extra ----------

        /// <summary>The strike: a quick dive. Played by <see cref="BirdContactAttack"/>.</summary>
        public void PlayAttack()
        {
            if (hasController && !dead)
                animator.SetTrigger(AttackId);
        }

        /// <summary>Back to flying from the start (respawn or pooling).</summary>
        public void ResetAnimation()
        {
            dead = false;
            flyingRequested = true;
            flyingSent = true;
            divingSent = false;
            wingsTarget = 1f;
            wingsScale = 1f;
            if (!hasController)
                return;

            animator.Rebind();
            animator.Update(0f);
            Desynchronise();
        }

        /// <summary>
        /// Each bird starts its wing beat at a random point and flaps slightly faster or slower than the others, so a
        /// flock of the same species never moves in lockstep. Set once, not every frame.
        /// </summary>
        private void Desynchronise()
        {
            float multiplier = motor.Motion.FlapSpeedMultiplier;
            animator.SetFloat(CycleOffsetId, Random.value);
            animator.SetFloat(FlapSpeedId, multiplier * Random.Range(1f - flapSpeedVariation, 1f + flapSpeedVariation));
        }
    }
}
