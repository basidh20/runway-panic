using UnityEngine;

namespace RunwayPanic.Player
{
    /// <summary>
    /// Drives the officer's Animator from the player's real movement. The <see cref="CharacterController"/> moves the
    /// player (no root motion); this only tells the Animator how fast the player is actually going, so the
    /// locomotion blend tree picks idle, walk or run. Running into a wall reads as standing still, not as running.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class PlayerAnimator : MonoBehaviour
    {
        private static readonly int SpeedId = Animator.StringToHash("Speed");

        [Tooltip("Animator on the officer model. Found in children when left empty.")]
        [SerializeField] private Animator animator;
        [Tooltip("Seconds the Speed parameter takes to follow the real speed. Higher = softer blends.")]
        [SerializeField, Min(0f)] private float speedDampTime = 0.1f;

        private CharacterController characterController;

        /// <summary>The Animator being driven (null if the model has none).</summary>
        public Animator Animator => animator;

        private void Awake()
        {
            characterController = GetComponent<CharacterController>();
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

        private void Update()
        {
            Vector3 velocity = characterController.velocity;
            velocity.y = 0f;
            animator.SetFloat(SpeedId, velocity.magnitude, speedDampTime, Time.deltaTime);
        }
    }
}
