using UnityEngine;
using UnityEngine.InputSystem;

namespace RunwayPanic.Player
{
    /// <summary>
    /// Camera-relative third-person movement with walking, sprinting, and jumping.
    /// Camera control is intentionally handled by a separate feature.
    /// </summary>
    [RequireComponent(typeof(CharacterController), typeof(PlayerInput))]
    public sealed class PlayerController : MonoBehaviour
    {
        private const string MoveActionName = "Move";
        private const string SprintActionName = "Sprint";
        private const string JumpActionName = "Jump";

        [Header("Movement")]
        [SerializeField, Min(0f)] private float walkSpeed = 4.5f;
        [SerializeField, Min(0f)] private float sprintSpeed = 7.5f;
        [SerializeField, Min(0f)] private float rotationSpeed = 12f;
        [SerializeField] private Transform movementReference;

        [Header("Grounding")]
        [SerializeField, Min(0f)] private float jumpHeight = 1.2f;
        [SerializeField] private float gravity = -25f;
        [SerializeField] private float groundedVelocity = -2f;

        private CharacterController characterController;
        private InputAction moveAction;
        private InputAction sprintAction;
        private InputAction jumpAction;
        private float verticalVelocity;

        public Vector2 MoveInput { get; private set; }
        public bool IsSprinting { get; private set; }
        public bool IsGrounded { get; private set; }
        public float CurrentSpeed { get; private set; }

        private void Awake()
        {
            characterController = GetComponent<CharacterController>();

            PlayerInput playerInput = GetComponent<PlayerInput>();
            moveAction = playerInput.actions?.FindAction(MoveActionName, false);
            sprintAction = playerInput.actions?.FindAction(SprintActionName, false);
            jumpAction = playerInput.actions?.FindAction(JumpActionName, false);

            if (moveAction == null || sprintAction == null || jumpAction == null)
            {
                Debug.LogError(
                    $"{nameof(PlayerController)} requires '{MoveActionName}', " +
                    $"'{SprintActionName}', and '{JumpActionName}' actions on " +
                    $"{nameof(PlayerInput)}.",
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
            ApplyVerticalMovement();

            Vector3 velocity = moveDirection * (CurrentSpeed * MoveInput.magnitude);
            velocity.y = verticalVelocity;
            CollisionFlags collisionFlags = characterController.Move(velocity * Time.deltaTime);
            IsGrounded = characterController.isGrounded ||
                (collisionFlags & CollisionFlags.Below) != 0;
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

        private void ApplyVerticalMovement()
        {
            IsGrounded = characterController.isGrounded;

            if (IsGrounded && verticalVelocity < 0f)
                verticalVelocity = groundedVelocity;

            if (IsGrounded && jumpAction.WasPressedThisFrame())
            {
                verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
                IsGrounded = false;
            }

            verticalVelocity += gravity * Time.deltaTime;
        }

        private void OnValidate()
        {
            walkSpeed = Mathf.Max(0f, walkSpeed);
            sprintSpeed = Mathf.Max(walkSpeed, sprintSpeed);
            rotationSpeed = Mathf.Max(0f, rotationSpeed);
            jumpHeight = Mathf.Max(0f, jumpHeight);
            gravity = Mathf.Min(-0.01f, gravity);
            groundedVelocity = Mathf.Min(0f, groundedVelocity);
        }
    }
}
