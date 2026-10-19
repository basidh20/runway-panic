using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RunwayPanic.Player
{
    /// <summary>
    /// Camera-relative third-person movement with walking, sprinting, and jumping.
    /// The camera is <see cref="PlayerCameraRig"/>. While a gun is held, <see cref="SetAim"/> keeps the body turned
    /// towards the aim; while aiming down the sights, <see cref="SetAimMovement"/> slows it to a walk.
    /// </summary>
    [DefaultExecutionOrder(-100)]
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

        [Header("Locomotion smoothing")]
        [SerializeField, Min(0.1f)] private float acceleration = 22f;
        [SerializeField, Min(0.1f)] private float deceleration = 30f;
        [Tooltip("Seconds to reach the requested speed when starting from idle. Other speed changes use acceleration.")]
        [SerializeField, Min(0.01f)] private float startAccelerationTime = 0.06f;
        [SerializeField, Range(0f, 0.5f)] private float inputDeadZone = 0.1f;
        [SerializeField, Range(0f, 1f)] private float airControl = 0.35f;
        [SerializeField, Min(0f)] private float coyoteTime = 0.1f;
        [SerializeField, Min(0f)] private float jumpBufferTime = 0.12f;

        [Header("Facing the aim (while holding a gun)")]
        [Tooltip("Most the body may face away from the aim, in degrees. The torso twists the rest of the way.")]
        [SerializeField, Range(0f, 180f)] private float maxAimAngle = 60f;
        [Tooltip("The same limit right after shooting, so the officer squares up to the target.")]
        [SerializeField, Range(0f, 180f)] private float maxAimAngleWhileShooting = 20f;

        [Header("Grounding")]
        [SerializeField, Min(0f)] private float jumpHeight = 1.2f;
        [SerializeField] private float gravity = -25f;
        [SerializeField] private float groundedVelocity = -2f;

        private CharacterController characterController;
        private InputAction moveAction;
        private InputAction sprintAction;
        private InputAction jumpAction;
        private float verticalVelocity;
        private Vector3 planarVelocity;
        private PlayerCameraRig cameraRig;
        private float lastGroundedTime = float.NegativeInfinity;
        private float lastJumpPressedTime = float.NegativeInfinity;
        private bool jumpConsumed;
        private float startAccelerationRemaining;

        public Vector2 MoveInput { get; private set; }
        public bool IsSprinting { get; private set; }
        public bool IsGrounded { get; private set; }
        public float CurrentSpeed { get; private set; }

        /// <summary>Vertical velocity in m/s: positive while rising, negative while falling.</summary>
        public float VerticalSpeed => verticalVelocity;

        /// <summary>Raised on the frame the player leaves the ground by jumping.</summary>
        public event Action Jumped;

        private Vector3 aimDirection;
        private bool aimShooting;

        /// <summary>
        /// Keeps the body turned towards <paramref name="direction"/> (flattened): it may face where it walks, but no
        /// further than the aim limit from it. Set every frame while aiming; <see cref="ClearAim"/> when not.
        /// </summary>
        public void SetAim(Vector3 direction, bool shooting)
        {
            direction.y = 0f;
            aimDirection = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.zero;
            aimShooting = shooting;
        }

        /// <summary>Stops facing the aim: the body turns only towards where it walks.</summary>
        public void ClearAim()
        {
            aimDirection = Vector3.zero;
        }

        private float aimSpeedScale = 1f;
        private bool aiming;

        /// <summary>
        /// While aiming down the sights (from <see cref="PlayerCameraRig"/>): movement is scaled by
        /// <paramref name="speedScale"/> and sprinting is not possible.
        /// </summary>
        public void SetAimMovement(float speedScale, bool isAiming)
        {
            aimSpeedScale = Mathf.Clamp01(speedScale);
            aiming = isAiming;
        }

        private void Awake()
        {
            characterController = GetComponent<CharacterController>();
            cameraRig = GetComponent<PlayerCameraRig>();

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
            if (!characterController.enabled || Time.deltaTime <= 0f)
                return;

            bool hadMoveInput = MoveInput.sqrMagnitude > 0f;
            MoveInput = Vector2.ClampMagnitude(moveAction.ReadValue<Vector2>(), 1f);
            if (MoveInput.magnitude < inputDeadZone)
                MoveInput = Vector2.zero;
            if (!hadMoveInput && MoveInput.sqrMagnitude > 0f && planarVelocity.sqrMagnitude < 0.01f)
                startAccelerationRemaining = startAccelerationTime;
            if (MoveInput == Vector2.zero)
                startAccelerationRemaining = 0f;
            IsSprinting = sprintAction.IsPressed() && MoveInput.sqrMagnitude > 0f && !aiming;
            float targetSpeed = (IsSprinting ? sprintSpeed : walkSpeed) * aimSpeedScale;

            Vector3 moveDirection = GetMoveDirection(MoveInput);
            RotateTowards(moveDirection);
            ApplyVerticalMovement();

            Vector3 targetVelocity = moveDirection * targetSpeed;
            float rate = targetVelocity.sqrMagnitude < planarVelocity.sqrMagnitude ? deceleration : acceleration;
            if (IsGrounded && startAccelerationRemaining > 0f)
                rate = Mathf.Max(rate, targetVelocity.magnitude / Mathf.Max(0.01f, startAccelerationTime));
            startAccelerationRemaining = Mathf.Max(0f, startAccelerationRemaining - Time.deltaTime);
            planarVelocity = Vector3.MoveTowards(planarVelocity, targetVelocity,
                rate * (IsGrounded ? 1f : airControl) * Time.deltaTime);
            Vector3 velocity = planarVelocity;
            velocity.y = verticalVelocity;
            CollisionFlags collisionFlags = characterController.Move(velocity * Time.deltaTime);
            IsGrounded = verticalVelocity <= 0f && (characterController.isGrounded ||
                (collisionFlags & CollisionFlags.Below) != 0);
            if ((collisionFlags & CollisionFlags.Above) != 0 && verticalVelocity > 0f)
                verticalVelocity = 0f;
            Vector3 actualVelocity = characterController.velocity;
            actualVelocity.y = 0f;
            CurrentSpeed = actualVelocity.magnitude;
            // Remove velocity blocked by walls, avoiding stored momentum when the obstacle is cleared.
            planarVelocity = Vector3.ClampMagnitude(actualVelocity, planarVelocity.magnitude);
        }

        private Vector3 GetMoveDirection(Vector2 input)
        {
            Transform reference = movementReference;
            if (cameraRig != null && cameraRig.isActiveAndEnabled &&
                (reference == null || reference.TryGetComponent<Camera>(out _)))
                return cameraRig.MovementRotation * new Vector3(input.x, 0f, input.y);
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
            bool moving = moveDirection.sqrMagnitude >= 0.0001f;
            Vector3 facing = moving ? moveDirection : transform.forward;
            facing.y = 0f;

            // Holding a gun: face where we walk, but never further from the aim than the limit allows.
            if (aimDirection != Vector3.zero)
            {
                float limit = aimShooting ? maxAimAngleWhileShooting : maxAimAngle;
                float angle = Vector3.SignedAngle(aimDirection, facing, Vector3.up);
                if (Mathf.Abs(angle) > limit)
                    facing = Quaternion.AngleAxis(Mathf.Sign(angle) * limit, Vector3.up) * aimDirection;
            }
            else if (!moving)
            {
                return;
            }

            if (facing.sqrMagnitude < 0.0001f)
                return;

            Quaternion targetRotation = Quaternion.LookRotation(facing, Vector3.up);
            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                targetRotation,
                1f - Mathf.Exp(-rotationSpeed * Time.deltaTime));
        }

        private void ApplyVerticalMovement()
        {
            IsGrounded = characterController.isGrounded;

            if (jumpAction.WasPressedThisFrame())
                lastJumpPressedTime = Time.time;

            if (IsGrounded && verticalVelocity <= 0f)
            {
                lastGroundedTime = Time.time;
                jumpConsumed = false;
            }

            if (IsGrounded && verticalVelocity < 0f)
                verticalVelocity = groundedVelocity;

            if (!jumpConsumed && Time.time - lastGroundedTime <= coyoteTime &&
                Time.time - lastJumpPressedTime <= jumpBufferTime)
            {
                verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
                IsGrounded = false;
                jumpConsumed = true;
                lastJumpPressedTime = float.NegativeInfinity;
                Jumped?.Invoke();
            }

            verticalVelocity += gravity * Time.deltaTime;
        }

        private void OnDisable()
        {
            planarVelocity = Vector3.zero;
            verticalVelocity = 0f;
            CurrentSpeed = 0f;
            MoveInput = Vector2.zero;
            IsSprinting = false;
            IsGrounded = false;
            startAccelerationRemaining = 0f;
            jumpConsumed = false;
            lastGroundedTime = lastJumpPressedTime = float.NegativeInfinity;
        }

        private void OnValidate()
        {
            walkSpeed = Mathf.Max(0f, walkSpeed);
            sprintSpeed = Mathf.Max(walkSpeed, sprintSpeed);
            rotationSpeed = Mathf.Max(0f, rotationSpeed);
            jumpHeight = Mathf.Max(0f, jumpHeight);
            gravity = Mathf.Min(-0.01f, gravity);
            groundedVelocity = Mathf.Min(0f, groundedVelocity);
            acceleration = Mathf.Max(0.1f, acceleration);
            deceleration = Mathf.Max(0.1f, deceleration);
            startAccelerationTime = Mathf.Max(0.01f, startAccelerationTime);
            coyoteTime = Mathf.Max(0f, coyoteTime);
            jumpBufferTime = Mathf.Max(0f, jumpBufferTime);
        }
    }
}
