using UnityEngine;

namespace RunwayPanic.Agents
{
    /// <summary>
    /// Feeds the bird's Animator (AC_Bird, on the model child) from its movement: how fast it flies and how fast it
    /// climbs or dives pick the wing motion in the Locomotion blend tree. Attack, Hit and Death are one-shot triggers
    /// fired by the bird's attack and health components.
    /// Root motion is off (the path follower moves the bird) and off-screen birds skip writing their bones.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BirdPathFollower))]
    public sealed class BirdAnimatorDriver : MonoBehaviour
    {
        // Parameter names, shared with the editor tool that builds AC_Bird.
        public const string SpeedParameter = "Speed";
        public const string VerticalSpeedParameter = "VerticalSpeed";
        public const string FlapSpeedParameter = "FlapSpeed";
        public const string CycleOffsetParameter = "CycleOffset";
        public const string AttackTrigger = "Attack";
        public const string HitTrigger = "Hit";
        public const string DeathTrigger = "Death";

        // Hashed once: SetFloat(int) skips hashing the string on every call.
        private static readonly int SpeedId = Animator.StringToHash(SpeedParameter);
        private static readonly int VerticalSpeedId = Animator.StringToHash(VerticalSpeedParameter);
        private static readonly int FlapSpeedId = Animator.StringToHash(FlapSpeedParameter);
        private static readonly int CycleOffsetId = Animator.StringToHash(CycleOffsetParameter);
        private static readonly int AttackId = Animator.StringToHash(AttackTrigger);
        private static readonly int HitId = Animator.StringToHash(HitTrigger);
        private static readonly int DeathId = Animator.StringToHash(DeathTrigger);

        [Tooltip("Animator on the model child. Found automatically if left empty.")]
        [SerializeField] private Animator animator;
        [Tooltip("Random ± change to each bird's wing-beat speed, so a flock does not flap in sync.")]
        [SerializeField, Range(0f, 0.3f)] private float flapSpeedVariation = 0.1f;
        [Tooltip("Seconds Speed and VerticalSpeed take to follow the bird, so the blend does not flicker.")]
        [SerializeField, Range(0f, 0.5f)] private float parameterDampTime = 0.15f;

        private BirdPathFollower follower;
        private BirdAgent agent;
        private bool hasController;
        private bool dead;

        private void Awake()
        {
            follower = GetComponent<BirdPathFollower>();
            agent = GetComponent<BirdAgent>();
            if (animator == null)
                animator = GetComponentInChildren<Animator>();

            hasController = animator != null && animator.runtimeAnimatorController != null;
            if (!hasController)
            {
                Debug.LogWarning($"[Agents] {name}: no Animator with a controller - run Tools > Agents > Setup Bird Prefabs.", this);
                enabled = false;
                return;
            }

            // The path follower moves the bird; clips only rotate bones.
            animator.applyRootMotion = false;
            // Off-screen birds keep their state machine running (triggers still work) but skip writing bones.
            animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
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

            float maxSpeed = Mathf.Max(follower.MaxSpeed, 0.01f);
            float speed01 = Mathf.Clamp01(follower.Speed / maxSpeed);

            // -1 = diving at the steepest allowed angle and full speed, +1 = climbing the same way.
            float maxClimbRate = maxSpeed * Mathf.Sin(follower.MaxPitch * Mathf.Deg2Rad);
            float vertical = Mathf.Clamp(follower.Velocity.y / Mathf.Max(maxClimbRate, 0.01f), -1f, 1f);

            animator.SetFloat(SpeedId, speed01, parameterDampTime, Time.deltaTime);
            animator.SetFloat(VerticalSpeedId, vertical, parameterDampTime, Time.deltaTime);
        }

        public void PlayAttack()
        {
            if (hasController && !dead)
                animator.SetTrigger(AttackId);
        }

        public void PlayHit()
        {
            if (hasController && !dead)
                animator.SetTrigger(HitId);
        }

        public void PlayDeath()
        {
            if (!hasController || dead)
                return;

            dead = true;
            animator.ResetTrigger(AttackId);
            animator.ResetTrigger(HitId);
            animator.SetTrigger(DeathId);
        }

        /// <summary>Back to flying from the start (respawn or pooling).</summary>
        public void ResetAnimation()
        {
            dead = false;
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
            float multiplier = agent != null ? agent.Motion.FlapSpeedMultiplier : 1f;
            animator.SetFloat(CycleOffsetId, Random.value);
            animator.SetFloat(FlapSpeedId, multiplier * Random.Range(1f - flapSpeedVariation, 1f + flapSpeedVariation));
        }
    }
}
