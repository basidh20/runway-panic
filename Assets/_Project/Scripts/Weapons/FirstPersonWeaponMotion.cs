using RunwayPanic.Data;
using RunwayPanic.Player;
using UnityEngine;

namespace RunwayPanic.Weapons
{
    /// <summary>
    /// Presentation on the existing camera socket. Never changes camera aim, weapon stats,
    /// ammo, fire timing or the cached weapon instances. Recoil events only enqueue motion;
    /// transforms are applied in LateUpdate, after shooting has resolved its muzzle/cover ray.
    /// </summary>
    [DisallowMultipleComponent, DefaultExecutionOrder(100)]
    public sealed class FirstPersonWeaponMotion : MonoBehaviour
    {
        [SerializeField, Range(0f, 2f)] private float motionAmount = 1f;
        [SerializeField, Min(0.01f)] private float poseSmoothTime = 0.065f;
        [SerializeField, Min(0.01f)] private float recoilSmoothTime = 0.085f;

        private PlayerWeaponHolder holder;
        private PlayerCameraRig rig;
        private PlayerController controller;
        private WeaponBase weapon;
        private Vector3 restPosition;
        private Quaternion restRotation;
        private Vector3 posePosition, poseVelocity, poseAngles, angleVelocity;
        private Quaternion lastViewRotation;
        private Vector3 lastPlayerPosition;
        private Vector2 lookLag;
        private float gaitPhase, gaitWeight, reloadStart, equipStart;
        private float kick, kickVelocity, landing, landingVelocity, lastVerticalSpeed;
        private bool wasGrounded, subscribed, reloading;
        private Renderer[] renderers = System.Array.Empty<Renderer>();
        private bool[] originalHidden = System.Array.Empty<bool>();
        private bool scopeHidden;

        internal void Initialize(PlayerWeaponHolder owner, PlayerCameraRig cameraRig, PlayerController movement)
        {
            holder = owner;
            rig = cameraRig;
            controller = movement;
            restPosition = transform.localPosition;
            restRotation = transform.localRotation;
            ResetMotion();
            Subscribe();
        }

        private void OnEnable()
        {
            if (holder == null) return; // Initialize runs after AddComponent's OnEnable.
            ResetMotion();
            Subscribe();
        }

        private void Subscribe()
        {
            if (subscribed) return;
            holder.WeaponEquipped += Equipped;
            subscribed = true;
            Equipped(holder.CurrentWeapon);
        }

        private void OnDisable()
        {
            if (subscribed && holder != null) holder.WeaponEquipped -= Equipped;
            subscribed = false;
            UnbindWeapon();
            transform.SetLocalPositionAndRotation(restPosition, restRotation);
        }

        private void Equipped(WeaponBase next)
        {
            if (weapon == next) return;
            UnbindWeapon();
            weapon = next;
            ResetMotion();
            equipStart = Time.time;
            if (weapon == null) return;
            weapon.Fired += Fired;
            weapon.ReloadStarted += ReloadStarted;
            weapon.ReloadFinished += ReloadFinished;
            renderers = weapon.GetComponentsInChildren<Renderer>(true);
            originalHidden = new bool[renderers.Length];
            for (int i = 0; i < renderers.Length; i++)
                originalHidden[i] = renderers[i].forceRenderingOff;
            reloading = weapon.IsReloading;
            reloadStart = Time.time;
        }

        private void UnbindWeapon()
        {
            SetScopeHidden(false);
            if (weapon != null)
            {
                weapon.Fired -= Fired;
                weapon.ReloadStarted -= ReloadStarted;
                weapon.ReloadFinished -= ReloadFinished;
            }
            weapon = null;
            renderers = System.Array.Empty<Renderer>();
            originalHidden = System.Array.Empty<bool>();
            reloading = false;
        }

        private void Fired()
        {
            float weight = weapon != null && weapon.Data != null ? weapon.Data.Recoil : 1f;
            // A bounded impulse, independent of the gameplay camera recoil.
            kick = Mathf.Min(kick + Mathf.Clamp(weight * 0.55f, 0.55f, 1.8f), 2.4f);
            kickVelocity = 0f;
        }

        private void ReloadStarted() { reloading = true; reloadStart = Time.time; }
        private void ReloadFinished() { reloading = false; }

        private void ResetMotion()
        {
            posePosition = poseVelocity = poseAngles = angleVelocity = Vector3.zero;
            lookLag = Vector2.zero;
            kick = kickVelocity = landing = landingVelocity = gaitPhase = gaitWeight = 0f;
            lastViewRotation = transform.parent != null ? transform.parent.rotation : Quaternion.identity;
            lastPlayerPosition = holder != null ? holder.transform.position : Vector3.zero;
            wasGrounded = controller != null && controller.IsGrounded;
            lastVerticalSpeed = 0f;
            transform.SetLocalPositionAndRotation(restPosition, restRotation);
        }

        private void LateUpdate()
        {
            if (weapon == null || weapon.Data == null || rig == null || !rig.IsFirstPerson) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            // Respawn/teleport must not carry the old fall impulse or look lag into the new view.
            if ((holder.transform.position - lastPlayerPosition).sqrMagnitude > 9f) ResetMotion();
            lastPlayerPosition = holder.transform.position;
            float aim = Mathf.SmoothStep(0f, 1f, rig.AimBlend);
            float steadiness = Mathf.Lerp(1f, 0.16f, aim) * motionAmount;
            WeaponData data = weapon.Data;
            bool pistol = data.HoldType == WeaponHoldType.Pistol && !data.IsProjectile;
            Vector3 hip = pistol ? restPosition : data.IsProjectile
                ? new Vector3(0.32f, -0.28f, 0.62f) : new Vector3(0.27f, -0.24f, 0.51f);
            // The opaque imported optics are not scope cameras. Keep their tops below the
            // crosshair; a launcher stays at the shoulder instead of putting its tube over the eye.
            // Preserve the sniper's existing transition into its scope overlay.
            Vector3 aimed = data.HasScope ? new Vector3(0f, -0.16f, 0.72f) : data.IsProjectile
                ? new Vector3(0.18f, -0.20f, 0.74f) : pistol
                ? new Vector3(0f, -0.125f, 0.56f) : new Vector3(0f, -0.215f, 0.68f);
            Vector3 position = Vector3.Lerp(hip, aimed, aim) - restPosition;
            Vector3 angles = Vector3.zero;

            Quaternion view = transform.parent.rotation;
            Vector3 previous = lastViewRotation.eulerAngles;
            Vector3 current = view.eulerAngles;
            Vector2 angularSpeed = new Vector2(Mathf.DeltaAngle(previous.y, current.y),
                Mathf.DeltaAngle(previous.x, current.x)) / Mathf.Max(dt, 0.001f);
            lastViewRotation = view;
            angularSpeed = Vector2.ClampMagnitude(angularSpeed, 300f);
            lookLag = Vector2.Lerp(lookLag, angularSpeed, 1f - Mathf.Exp(-14f * dt));
            angles += new Vector3(lookLag.y, -lookLag.x, -lookLag.x * 0.35f) * (0.009f * steadiness);

            float speed = controller != null && controller.enabled ? controller.CurrentSpeed : 0f;
            bool grounded = controller != null && controller.enabled && controller.IsGrounded;
            bool sprint = controller != null && controller.IsSprinting;
            gaitWeight = Mathf.Lerp(gaitWeight, grounded ? Mathf.Clamp01(speed / 4.5f) : 0f,
                1f - Mathf.Exp(-12f * dt));
            // Distance-driven gait stops at walls, with no added camera bob or startup delay.
            gaitPhase = Mathf.Repeat(gaitPhase + speed * dt * (sprint ? 2.5f : 3.1f), Mathf.PI * 2f);
            float gait = gaitWeight * steadiness;
            position += new Vector3(Mathf.Sin(gaitPhase) * 0.008f,
                Mathf.Cos(2f * gaitPhase) * 0.006f, 0f) * gait;
            angles.z += Mathf.Sin(gaitPhase) * 0.65f * gait;
            float sprintWeight = sprint ? gaitWeight * (1f - aim) * motionAmount : 0f;
            position += new Vector3(0.025f, -0.045f, -0.015f) * sprintWeight;
            angles += new Vector3(9f, -7f, -6f) * sprintWeight;
            float breathing = Mathf.Sin(Time.time * 1.7f) * 0.0012f * steadiness;
            position.y += breathing;

            if (!wasGrounded && grounded)
                landing = Mathf.Clamp(-lastVerticalSpeed * 0.004f, 0f, 0.035f);
            wasGrounded = grounded;
            lastVerticalSpeed = controller != null ? controller.VerticalSpeed : 0f;
            landing = Mathf.SmoothDamp(landing, 0f, ref landingVelocity, 0.14f, Mathf.Infinity, dt);
            position.y -= landing * motionAmount;
            angles.x += landing * 100f * motionAmount;

            if (reloading && weapon.IsReloading)
            {
                float progress = Mathf.Clamp01((Time.time - reloadStart) / Mathf.Max(0.01f, data.ReloadTime));
                // Enter, hold the inspection pose, then ease back before ammo is replenished.
                float envelope = Mathf.SmoothStep(0f, 1f, progress / 0.18f)
                    * (1f - Mathf.SmoothStep(0f, 1f, (progress - 0.72f) / 0.28f));
                position += new Vector3(-0.035f, 0.04f, pistol ? -0.015f : 0.08f) * envelope;
                angles += new Vector3(18f, -12f, pistol ? -28f : -18f) * envelope;
                if (data.IsProjectile)
                {
                    position += new Vector3(-0.04f, 0.08f, -0.20f) * envelope;
                    angles += new Vector3(-15f, -35f, -8f) * envelope;
                }
            }
            else reloading = false;

            float equip = 1f - Mathf.SmoothStep(0f, 1f, (Time.time - equipStart) / 0.28f);
            position.y -= equip * 0.14f;
            angles += new Vector3(15f, 0f, -10f) * equip;
            posePosition = Vector3.SmoothDamp(posePosition, position, ref poseVelocity, poseSmoothTime,
                Mathf.Infinity, dt);
            poseAngles = Vector3.SmoothDamp(poseAngles, angles, ref angleVelocity, poseSmoothTime,
                Mathf.Infinity, dt);
            kick = Mathf.SmoothDamp(kick, 0f, ref kickVelocity, recoilSmoothTime, Mathf.Infinity, dt);
            float recoil = kick * Mathf.Lerp(1f, 0.55f, aim);
            transform.SetLocalPositionAndRotation(restPosition + posePosition + Vector3.back * (recoil * 0.028f),
                restRotation * Quaternion.Euler(poseAngles + new Vector3(-recoil * 4.5f, 0f, recoil * 0.6f)));
            SetScopeHidden(rig.ScopeBlend > 0.95f);
        }

        private void SetScopeHidden(bool hidden)
        {
            if (scopeHidden == hidden) return;
            scopeHidden = hidden;
            for (int i = 0; i < renderers.Length; i++)
                if (renderers[i] != null) renderers[i].forceRenderingOff = hidden || originalHidden[i];
        }
    }
}
