using UnityEngine;
using UnityEngine.InputSystem;

namespace RunwayPanic.Player
{
    /// <summary>
    /// Camera-relative third-person movement with walk and sprint speeds.
    /// Jumping and camera control are intentionally handled by later features.
    /// </summary>
    [RequireComponent(typeof(CharacterController), typeof(PlayerInput))]
    public sealed class PlayerController : MonoBehaviour
    {
        private const string MoveActionName = "Move";
        private const string SprintActionName = "Sprint";

        [Header("Movement")]
        [SerializeField, Min(0f)] private float walkSpeed = 4.5f;
        [SerializeField, Min(0f)] private float sprintSpeed = 7.5f;
        [SerializeField, Min(0f)] private float rotationSpeed = 12f;
        [SerializeField] private Transform movementReference;

        [Header("Grounding")]
        [SerializeField] private float gravity = -25f;
        [SerializeField] private float groundedVelocity = -2f;

        private CharacterController characterController;
        private InputAction moveAction;
        private InputAction sprintAction;
        private float verticalVelocity;

        public Vector2 MoveInput { get; private set; }
        public bool IsSprinting { get; private set; }
        public float CurrentSpeed { get; private set; }

        private void Awake()
        {
            characterController = GetComponent<CharacterController>();

            PlayerInput playerInput = GetComponent<PlayerInput>();
            moveAction = playerInput.actions?.FindAction(MoveActionName, false);
            sprintAction = playerInput.actions?.FindAction(SprintActionName, false);

            if (moveAction == null || sprintAction == null)
            {
                Debug.LogError(
                    $"{nameof(PlayerController)} requires '{MoveActionName}' and " +
                    $"'{SprintActionName}' actions on {nameof(PlayerInput)}.",
                    this);
                enabled = false;
            }
        }

        private void Update()
        {
            MoveInput = Vector2.ClampMagnitude(moveAction.ReadValue<Vector2>(), 1f);
            IsSprinting = sprintAction.IsPressed() && MoveInput.sqrMagnitude > 0f;
            CurrentSpeed = IsSprinting ? sprintSpeed : walkSpeed;

            Vector3 moveDirection = GetMoveDirection(MoveInput);
            RotateTowards(moveDirection);
            ApplyGravity();

            Vector3 velocity = moveDirection * (CurrentSpeed * MoveInput.magnitude);
            velocity.y = verticalVelocity;
            characterController.Move(velocity * Time.deltaTime);
        }

        private Vector3 GetMoveDirection(Vector2 input)
        {
            Transform reference = movementReference;
            if (reference == null && Camera.main != null)
                reference = Camera.main.transform;

            Vector3 forward = reference != null ? reference.forward : transform.forward;
            Vector3 right = reference != null ? reference.right : transform.right;
            forward.y = 0f;
            right.y = 0f;
            forward.Normalize();
            right.Normalize();

            Vector3 direction = forward * input.y + right * input.x;
            return direction.sqrMagnitude > 1f ? direction.normalized : direction;
        }

        private void RotateTowards(Vector3 moveDirection)
        {
            if (moveDirection.sqrMagnitude < 0.0001f)
                return;

            Quaternion targetRotation = Quaternion.LookRotation(moveDirection, Vector3.up);
            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                targetRotation,
                rotationSpeed * Time.deltaTime);
        }

        private void ApplyGravity()
        {
            if (characterController.isGrounded && verticalVelocity < 0f)
                verticalVelocity = groundedVelocity;
            else
                verticalVelocity += gravity * Time.deltaTime;
        }

        private void OnValidate()
        {
            walkSpeed = Mathf.Max(0f, walkSpeed);
            sprintSpeed = Mathf.Max(walkSpeed, sprintSpeed);
            rotationSpeed = Mathf.Max(0f, rotationSpeed);
            gravity = Mathf.Min(0f, gravity);
            groundedVelocity = Mathf.Min(0f, groundedVelocity);
        }
    }
}
