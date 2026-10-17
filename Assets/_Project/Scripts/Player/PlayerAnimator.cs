using UnityEngine;

namespace RunwayPanic.Player
{
    /// <summary>
    /// Drives the officer's Animator from the player's real movement. The <see cref="CharacterController"/> moves the
    /// player (no root motion); this only tells the Animator what the player is doing:
    /// <list type="bullet">
    /// <item><c>Speed</c> — real horizontal speed, so the locomotion blend tree picks idle, walk or run (running into a
    /// wall reads as standing still, not as running).</item>
    /// <item><c>Jump</c> — trigger on take-off; <c>IsGrounded</c> and <c>VerticalSpeed</c> — for falling and landing,
    /// including walking off a ledge.</item>
    /// </list>
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class PlayerAnimator : MonoBehaviour
    {
        private static readonly int SpeedId = Animator.StringToHash("Speed");
        private static readonly int JumpId = Animator.StringToHash("Jump");
        private static readonly int IsGroundedId = Animator.StringToHash("IsGrounded");
        private static readonly int VerticalSpeedId = Animator.StringToHash("VerticalSpeed");

        [Tooltip("Animator on the officer model. Found in children when left empty.")]
        [SerializeField] private Animator animator;
        [Tooltip("Seconds the Speed parameter takes to follow the real speed. Higher = softer blends.")]
        [SerializeField, Min(0f)] private float speedDampTime = 0.1f;

        private CharacterController characterController;
        private PlayerController playerController;

        /// <summary>The Animator being driven (null if the model has none).</summary>
        public Animator Animator => animator;

        private void Awake()
        {
            characterController = GetComponent<CharacterController>();
            playerController = GetComponent<PlayerController>();
            if (animator == null)
                animator = GetComponentInChildren<Animator>();

            if (animator == null)
            {
                Debug.LogError($"{nameof(PlayerAnimator)} on '{name}' found no Animator in its children.", this);
                enabled = false;
                return;
            }

            // Movement belongs to the CharacterController; clips are "In Place".
            animator.applyRootMotion = false;
        }

        private void OnEnable()
        {
            if (playerController != null)
                playerController.Jumped += HandleJumped;
        }

        private void OnDisable()
        {
            if (playerController != null)
                playerController.Jumped -= HandleJumped;
        }

        private void Update()
        {
            Vector3 velocity = characterController.velocity;
            velocity.y = 0f;
            animator.SetFloat(SpeedId, velocity.magnitude, speedDampTime, Time.deltaTime);

            if (playerController != null)
            {
                animator.SetBool(IsGroundedId, playerController.IsGrounded);
                animator.SetFloat(VerticalSpeedId, playerController.VerticalSpeed);
            }
        }

        private void HandleJumped()
        {
            animator.SetTrigger(JumpId);
        }
    }
}
