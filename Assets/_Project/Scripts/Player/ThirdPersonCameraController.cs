using UnityEngine;
using UnityEngine.InputSystem;

namespace RunwayPanic.Player
{
    /// <summary>
    /// Follows a player target and provides mouse or gamepad orbit controls.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    public sealed class ThirdPersonCameraController : MonoBehaviour
    {
        private const string LookActionName = "Look";

        [Header("References")]
        [SerializeField] private Transform target;
        [SerializeField] private PlayerInput playerInput;

        [Header("Framing")]
        [SerializeField] private Vector3 targetOffset = new(0f, 1.6f, 0f);
        [SerializeField, Min(0.1f)] private float followDistance = 4f;
        [SerializeField, Min(0f)] private float followSharpness = 15f;

        [Header("Look")]
        [SerializeField, Min(0f)] private float mouseSensitivity = 0.12f;
        [SerializeField, Min(0f)] private float gamepadLookSpeed = 120f;
        [SerializeField, Range(-89f, 89f)] private float minimumPitch = -30f;
        [SerializeField, Range(-89f, 89f)] private float maximumPitch = 70f;
        [SerializeField] private bool lockCursorOnEnable = true;

        private InputAction lookAction;
        private float yaw;
        private float pitch;
        private bool positionInitialized;

        public Transform Target => target;

        private void Awake()
        {
            ResolveReferences();

            if (target == null || playerInput == null)
            {
                Debug.LogError(
                    $"{nameof(ThirdPersonCameraController)} requires a target and " +
                    $"a {nameof(PlayerInput)} reference.",
                    this);
                enabled = false;
                return;
            }

            lookAction = playerInput.actions?.FindAction(LookActionName, false);
            if (lookAction == null)
            {
                Debug.LogError(
                    $"{nameof(ThirdPersonCameraController)} requires a " +
                    $"'{LookActionName}' action on {nameof(PlayerInput)}.",
                    this);
                enabled = false;
                return;
            }

            Vector3 startingAngles = transform.eulerAngles;
            yaw = startingAngles.y;
            pitch = NormalizeAngle(startingAngles.x);
        }

        private void OnEnable()
        {
            positionInitialized = false;

            if (lockCursorOnEnable && Application.isPlaying)
                SetCursorLocked(true);
        }

        private void LateUpdate()
        {
            Vector2 lookInput = lookAction.ReadValue<Vector2>();
            ApplyLookInput(lookInput);

            Vector3 focusPoint = target.position + targetOffset;
            Quaternion orbitRotation = Quaternion.Euler(pitch, yaw, 0f);
            Vector3 desiredPosition =
                focusPoint - orbitRotation * Vector3.forward * followDistance;

            if (!positionInitialized || followSharpness <= 0f)
            {
                transform.position = desiredPosition;
                positionInitialized = true;
            }
            else
            {
                float followBlend = 1f - Mathf.Exp(-followSharpness * Time.unscaledDeltaTime);
                transform.position = Vector3.Lerp(transform.position, desiredPosition, followBlend);
            }

            Vector3 focusDirection = focusPoint - transform.position;
            if (focusDirection.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.LookRotation(focusDirection, Vector3.up);
        }

        public void SetTarget(Transform newTarget, PlayerInput newPlayerInput = null)
        {
            target = newTarget;
            playerInput = newPlayerInput != null
                ? newPlayerInput
                : newTarget != null
                    ? newTarget.GetComponentInParent<PlayerInput>()
                    : null;

            lookAction = playerInput != null
                ? playerInput.actions?.FindAction(LookActionName, false)
                : null;
            positionInitialized = false;
            enabled = target != null && lookAction != null;
        }

        public void SetCursorLocked(bool isLocked)
        {
            Cursor.lockState = isLocked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !isLocked;
        }

        private void ResolveReferences()
        {
            if (target == null)
            {
                PlayerController controller = FindFirstObjectByType<PlayerController>();
                if (controller != null)
                    target = controller.transform;
            }

            if (playerInput == null && target != null)
                playerInput = target.GetComponentInParent<PlayerInput>();
        }

        private void ApplyLookInput(Vector2 lookInput)
        {
            bool isPointerInput = lookAction.activeControl?.device is Pointer;
            float scale = isPointerInput
                ? mouseSensitivity
                : gamepadLookSpeed * Time.unscaledDeltaTime;

            yaw += lookInput.x * scale;
            pitch = Mathf.Clamp(pitch - lookInput.y * scale, minimumPitch, maximumPitch);
        }

        private static float NormalizeAngle(float angle)
        {
            return angle > 180f ? angle - 360f : angle;
        }

        private void OnDisable()
        {
            if (lockCursorOnEnable && Application.isPlaying)
                SetCursorLocked(false);
        }

        private void OnValidate()
        {
            followDistance = Mathf.Max(0.1f, followDistance);
            followSharpness = Mathf.Max(0f, followSharpness);
            mouseSensitivity = Mathf.Max(0f, mouseSensitivity);
            gamepadLookSpeed = Mathf.Max(0f, gamepadLookSpeed);

            if (maximumPitch < minimumPitch)
                maximumPitch = minimumPitch;
        }
    }
}
