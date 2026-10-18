using RunwayPanic.Core;
using RunwayPanic.Data;
using RunwayPanic.Weapons;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace RunwayPanic.Player
{
    /// <summary>
    /// The shooter camera: an over-the-shoulder Cinemachine <see cref="CinemachineThirdPersonFollow"/> on a camera
    /// target that this script turns from the Look input (mouse or stick; the Settings screen's sensitivity and invert
    /// Y apply). The character stands left of the crosshair so it never hides the target, and walls push the camera in
    /// instead of it clipping through them.
    /// <list type="bullet">
    /// <item><b>Aim</b> (hold right mouse / left trigger with a gun): the camera moves in over the shoulder and zooms to
    /// the gun's aim field of view, look sensitivity drops to the gun's aim sensitivity, the player walks (no sprint)
    /// and faces the aim. Reloading drops the aim.</item>
    /// <item><b>Scope</b> (aiming with a scoped gun): the camera goes to the eyes, the character is hidden (its shadow
    /// stays) and the view sways gently with breathing; <c>ScopeOverlayUI</c> draws the scope.</item>
    /// <item><b>Recoil</b>: <see cref="AddRecoil"/> kicks the view up; most of the kick settles back by itself.</item>
    /// </list>
    /// </summary>
    [RequireComponent(typeof(PlayerInput))]
    public sealed class PlayerCameraRig : MonoBehaviour
    {
        private const string LookActionName = "Look";
        private const string AimActionName = "Aim";

        [Header("Parts")]
        [Tooltip("Turned by this script; the virtual camera follows it. A child of the player at head height.")]
        [SerializeField] private Transform cameraTarget;
        [SerializeField] private CinemachineCamera virtualCamera;
        [SerializeField] private CinemachineThirdPersonFollow follow;

        [Header("Look")]
        [Tooltip("Degrees per pixel of mouse movement at sensitivity 1.")]
        [SerializeField, Min(0.001f)] private float mouseDegreesPerPixel = 0.08f;
        [Tooltip("Gamepad stick turn speed at full tilt (degrees/s): horizontal, vertical.")]
        [SerializeField] private Vector2 stickDegreesPerSecond = new Vector2(220f, 150f);
        [Tooltip("Lowest (looking up) and highest (looking down) pitch, in degrees.")]
        [SerializeField] private Vector2 pitchLimits = new Vector2(-60f, 70f);

        [Header("Hip (not aiming)")]
        [SerializeField, Range(30f, 90f)] private float hipFieldOfView = 60f;
        [SerializeField, Min(0f)] private float hipDistance = 3.2f;
        [SerializeField] private Vector3 hipShoulder = new Vector3(0.8f, 0.1f, 0f);
        [SerializeField, Min(0f)] private float hipArmLength = 0.35f;
        [SerializeField] private Vector3 hipDamping = new Vector3(0.1f, 0.35f, 0.25f);

        [Header("Aim (over the shoulder)")]
        [SerializeField, Min(0f)] private float aimDistance = 1.6f;
        [SerializeField] private Vector3 aimShoulder = new Vector3(0.65f, 0.08f, 0f);
        [SerializeField] private Vector3 aimDamping = new Vector3(0f, 0.05f, 0f);
        [Tooltip("Seconds to go from hip to aim (and back).")]
        [SerializeField, Min(0.01f)] private float aimBlendTime = 0.14f;
        [Tooltip("Walking speed while aiming, as a fraction of the walk speed.")]
        [SerializeField, Range(0.1f, 1f)] private float aimMoveSpeed = 0.55f;

        [Header("Scope (eyes)")]
        [SerializeField] private Vector3 scopeShoulder = new Vector3(0.05f, 0.05f, 0f);
        [SerializeField, Min(0.01f)] private float scopeBlendTime = 0.12f;
        [Tooltip("Breathing sway while scoped: size (degrees) and speed.")]
        [SerializeField, Min(0f)] private float scopeSway = 0.12f;
        [SerializeField, Min(0f)] private float scopeSwaySpeed = 0.35f;

        [Header("Recoil")]
        [Tooltip("Share of each kick that settles back by itself.")]
        [SerializeField, Range(0f, 1f)] private float recoilReturn = 0.7f;
        [Tooltip("How fast the kick settles back (per second).")]
        [SerializeField, Min(0.1f)] private float recoilRecovery = 9f;

        private InputAction lookAction;
        private InputAction aimAction;
        private PlayerWeaponHolder weapons;
        private PlayerController controller;
        private Renderer[] hiddenRenderers = System.Array.Empty<Renderer>();
        private ShadowCastingMode[] hiddenModes = System.Array.Empty<ShadowCastingMode>();
        private float yaw;
        private float pitch = 8f;
        private float recoilToReturn;
        private float swayTime;

        /// <summary>True while the aim button is held with a gun (not while reloading).</summary>
        public bool IsAiming { get; private set; }

        /// <summary>0 at the hip, 1 fully aimed.</summary>
        public float AimBlend { get; private set; }

        /// <summary>0 outside the scope, 1 looking through it.</summary>
        public float ScopeBlend { get; private set; }

        /// <summary>True when looking through a scope (the overlay is up, the crosshair hidden).</summary>
        public bool IsScoped => ScopeBlend > 0.5f;

        /// <summary>The camera's current field of view (for converting spread to screen size).</summary>
        public float FieldOfView => virtualCamera != null ? virtualCamera.Lens.FieldOfView : hipFieldOfView;

        private void Awake()
        {
            var input = GetComponent<PlayerInput>();
            lookAction = input.actions?.FindAction(LookActionName, false);
            aimAction = input.actions?.FindAction(AimActionName, false);
            weapons = GetComponent<PlayerWeaponHolder>();
            controller = GetComponent<PlayerController>();
        }

        private void Start()
        {
            yaw = transform.eulerAngles.y;
            ApplyCamera(null);
        }

        private void OnDisable()
        {
            ShowBody(true);
            if (controller != null)
                controller.SetAimMovement(1f, false);
        }

        private void Update()
        {
            bool playing = GameManager.Instance == null || GameManager.Instance.CurrentState == GameState.Playing;
            WeaponBase weapon = weapons != null ? weapons.CurrentWeapon : null;
            WeaponData data = weapon != null && weapon.gameObject.activeInHierarchy ? weapon.Data : null;

            IsAiming = playing && data != null && aimAction != null && aimAction.IsPressed() && !weapon.IsReloading;
            AimBlend = Mathf.MoveTowards(AimBlend, IsAiming ? 1f : 0f, Time.deltaTime / aimBlendTime);
            bool scoping = IsAiming && data.HasScope && AimBlend > 0.6f;
            ScopeBlend = Mathf.MoveTowards(ScopeBlend, scoping ? 1f : 0f, Time.deltaTime / scopeBlendTime);

            if (playing && Cursor.lockState == CursorLockMode.Locked)
                Look(data);
            RecoverRecoil();

            Vector2 sway = Sway();
            if (cameraTarget != null)
                cameraTarget.rotation = Quaternion.Euler(pitch + sway.y, yaw + sway.x, 0f);

            ApplyCamera(data);
            ShowBody(ScopeBlend < 0.5f);
            if (controller != null)
                controller.SetAimMovement(Mathf.Lerp(1f, aimMoveSpeed, AimBlend), IsAiming);
        }

        /// <summary>Kicks the view up by <paramref name="degrees"/>; most of it settles back over a moment.</summary>
        public void AddRecoil(float degrees)
        {
            pitch = Mathf.Clamp(pitch - degrees, pitchLimits.x, pitchLimits.y);
            recoilToReturn += degrees * recoilReturn;
        }

        private void Look(WeaponData data)
        {
            if (lookAction == null)
                return;

            Vector2 input = lookAction.ReadValue<Vector2>();
            bool stick = lookAction.activeControl != null && lookAction.activeControl.device is Gamepad;
            Vector2 degrees = stick
                ? Vector2.Scale(input, stickDegreesPerSecond) * Time.deltaTime
                : input * mouseDegreesPerPixel;

            float aimSensitivity = data != null ? data.AimSensitivity : 1f;
            float sensitivity = GameSettings.LookSensitivity * Mathf.Lerp(1f, aimSensitivity, AimBlend);
            yaw += degrees.x * sensitivity;
            pitch -= degrees.y * sensitivity * (GameSettings.InvertY ? -1f : 1f);
            pitch = Mathf.Clamp(pitch, pitchLimits.x, pitchLimits.y);
        }

        private void RecoverRecoil()
        {
            if (recoilToReturn <= 0f)
                return;
            float step = Mathf.Min(recoilToReturn, (recoilToReturn + 0.2f) * recoilRecovery * Time.deltaTime);
            recoilToReturn -= step;
            pitch = Mathf.Clamp(pitch + step, pitchLimits.x, pitchLimits.y);
        }

        // A slow figure-of-eight drift while scoped, like breathing; shots follow it (the aim is the view).
        private Vector2 Sway()
        {
            if (ScopeBlend <= 0f || scopeSway <= 0f)
                return Vector2.zero;
            swayTime += Time.deltaTime * scopeSwaySpeed * 2f * Mathf.PI;
            return new Vector2(Mathf.Sin(swayTime), Mathf.Sin(swayTime * 2f) * 0.5f) * (scopeSway * ScopeBlend);
        }

        private void ApplyCamera(WeaponData data)
        {
            if (follow != null)
            {
                follow.CameraSide = 1f;
                follow.CameraDistance = Mathf.Lerp(Mathf.Lerp(hipDistance, aimDistance, AimBlend), 0f, ScopeBlend);
                Vector3 shoulder = Vector3.Lerp(hipShoulder, aimShoulder, AimBlend);
                follow.ShoulderOffset = Vector3.Lerp(shoulder, scopeShoulder, ScopeBlend);
                follow.VerticalArmLength = Mathf.Lerp(hipArmLength, 0f, Mathf.Max(AimBlend, ScopeBlend));
                follow.Damping = Vector3.Lerp(hipDamping, aimDamping, AimBlend);
            }

            if (virtualCamera != null)
            {
                LensSettings lens = virtualCamera.Lens;
                float aimFieldOfView = data != null ? data.AimFieldOfView : hipFieldOfView;
                lens.FieldOfView = Mathf.Lerp(hipFieldOfView, aimFieldOfView, AimBlend);
                virtualCamera.Lens = lens;
            }
        }

        // Through a scope the character would fill the view: hide its meshes but keep their shadows.
        private void ShowBody(bool show)
        {
            if (!show && hiddenRenderers.Length == 0)
            {
                hiddenRenderers = GetComponentsInChildren<Renderer>();
                hiddenModes = new ShadowCastingMode[hiddenRenderers.Length];
                for (int i = 0; i < hiddenRenderers.Length; i++)
                {
                    hiddenModes[i] = hiddenRenderers[i].shadowCastingMode;
                    hiddenRenderers[i].shadowCastingMode = ShadowCastingMode.ShadowsOnly;
                }
            }
            else if (show && hiddenRenderers.Length > 0)
            {
                for (int i = 0; i < hiddenRenderers.Length; i++)
                    if (hiddenRenderers[i] != null)
                        hiddenRenderers[i].shadowCastingMode = hiddenModes[i];
                hiddenRenderers = System.Array.Empty<Renderer>();
                hiddenModes = System.Array.Empty<ShadowCastingMode>();
            }
        }
    }
}
