using System;
using RunwayPanic.Core;
using RunwayPanic.Data;
using RunwayPanic.Player;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RunwayPanic.Weapons
{
    /// <summary>
    /// Fires the equipped weapon while Attack is held. Aims through the centre of the screen — the aim ray starts at
    /// the player, so nothing between the camera and the player can catch it — inside a spread cone (wider from the
    /// hip, tight when aiming, none through a scope, blooming with sustained fire). That ray decides the hit at any
    /// distance — no parallax, so a bird in open sky is hit exactly where the crosshair is — and it is as wide as the
    /// gun's hit radius, so small fast birds don't need pixel-perfect aim. Cover between the gun's muzzle and the impact
    /// still blocks the shot. Damages anything implementing <see cref="IDamageable"/> and kicks the camera with the
    /// gun's recoil. A gun with a projectile (the RPG) launches a <see cref="Rocket"/> at that point instead, which
    /// does the damage when it goes off.
    /// </summary>
    [RequireComponent(typeof(PlayerInput), typeof(PlayerWeaponHolder))]
    public sealed class PlayerShooting : MonoBehaviour
    {
        private const string AttackActionName = "Attack";
        private const string ReloadActionName = "Reload";
        private const int MaxHits = 16;

        [Tooltip("Camera used for aiming. Uses Camera.main if left empty.")]
        [SerializeField] private Camera aimCamera;
        [SerializeField] private LayerMask hitLayers = ~0;

        /// <summary>
        /// Raised for every shot, before damage is applied to the target.
        /// Arguments: muzzle position, end point, the damageable that was hit (or null).
        /// </summary>
        public event Action<Vector3, Vector3, IDamageable> ShotFired;

        /// <summary>Raised after a shot's damage is applied. Arguments: what was hit, whether that killed it.</summary>
        public event Action<IDamageable, bool> HitConfirmed;

        private const float PivotHeight = 1.5f; // third-person aim-ray compensation height

        private readonly RaycastHit[] hitBuffer = new RaycastHit[MaxHits];
        private PlayerWeaponHolder weaponHolder;
        private PlayerCameraRig cameraRig;
        private InputAction attackAction;
        private InputAction reloadAction;
        private float bloom;

        /// <summary>The current spread (cone half-angle, degrees) a shot would have now.</summary>
        public float CurrentSpread
        {
            get
            {
                WeaponBase weapon = weaponHolder != null ? weaponHolder.CurrentWeapon : null;
                WeaponData data = weapon != null ? weapon.Data : null;
                if (data == null || (cameraRig != null && cameraRig.IsScoped))
                    return 0f;
                float aim = cameraRig != null ? cameraRig.AimBlend : 0f;
                float baseSpread = Mathf.Lerp(data.HipSpread, data.AimSpread, aim);
                return Mathf.Min(baseSpread + bloom, Mathf.Max(baseSpread, data.MaxSpread));
            }
        }

        private void Awake()
        {
            weaponHolder = GetComponent<PlayerWeaponHolder>();
            cameraRig = GetComponent<PlayerCameraRig>();
            attackAction = GetComponent<PlayerInput>().actions?.FindAction(AttackActionName, false);
            // Optional: without a Reload action, guns still reload automatically when the magazine runs dry.
            reloadAction = GetComponent<PlayerInput>().actions?.FindAction(ReloadActionName, false);

            if (attackAction == null)
            {
                Debug.LogError($"{nameof(PlayerShooting)} requires an '{AttackActionName}' action on {nameof(PlayerInput)}.", this);
                enabled = false;
            }
        }

        private void Update()
        {
            WeaponBase current = weaponHolder.CurrentWeapon;
            if (current != null && current.Data != null)
                bloom = Mathf.MoveTowards(bloom, 0f, current.Data.SpreadRecovery * Time.deltaTime);

            // UI clicks use the same mouse button as Attack. Gameplay weapon input is valid only during a live wave,
            // never while a pause/shop/end screen has released the cursor for menu interaction.
            if (GameManager.Instance != null && GameManager.Instance.CurrentState != GameState.Playing)
                return;

            if (reloadAction != null && reloadAction.WasPressedThisFrame())
            {
                WeaponBase weapon = weaponHolder.CurrentWeapon;
                if (weapon != null)
                    weapon.StartReload(); // ignored while reloading or with a full magazine
            }
            else if (attackAction.IsPressed())
                TryShoot();
        }

        /// <summary>
        /// Fires one shot if the equipped weapon is ready. Returns true when a shot was fired.
        /// </summary>
        public bool TryShoot()
        {
            WeaponBase weapon = weaponHolder.CurrentWeapon;
            Camera cam = aimCamera != null ? aimCamera : Camera.main;
            if (weapon == null || cam == null || !weapon.TryFire())
                return false;

            WeaponData data = weapon.Data;

            // 1. Find what the crosshair is pointing at, inside the spread cone.
            Ray aimRay = AimRay(cam);
            float spread = CurrentSpread;
            if (spread > 0f)
            {
                Vector2 offset = UnityEngine.Random.insideUnitCircle * Mathf.Tan(spread * Mathf.Deg2Rad);
                Transform view = cam.transform;
                aimRay.direction = (view.forward + view.right * offset.x + view.up * offset.y).normalized;
            }

            // 2. The crosshair ray decides the hit, at any distance (a muzzle-to-far-point line would miss a bird in open
            //    sky by the gun's offset from the camera). Walls stop it with a thin ray; targets are found with the
            //    gun's hit radius up to that wall.
            float shotDistance = weapon.Range;
            Vector3 endPoint = aimRay.GetPoint(shotDistance);
            IDamageable target = null;

            if (TryRaycastIgnoringSelf(aimRay, shotDistance, out RaycastHit aimHit))
            {
                endPoint = aimHit.point;
                shotDistance = aimHit.distance;
                target = aimHit.collider.GetComponentInParent<IDamageable>();
            }

            if (target == null && data != null && data.HitRadius > 0f &&
                TrySphereCastForTarget(aimRay, data.HitRadius, shotDistance, out RaycastHit near,
                    out IDamageable nearTarget))
            {
                target = nearTarget;
                endPoint = near.point != Vector3.zero ? near.point : aimRay.GetPoint(near.distance);
            }

            // 3. Cover in front of the gun still counts: if something stands between the muzzle and the impact, the
            //    shot hits that instead.
            Vector3 origin = weapon.Muzzle.position;
            Vector3 toEnd = endPoint - origin;
            if (toEnd.sqrMagnitude > 0.01f &&
                TryRaycastIgnoringSelf(new Ray(origin, toEnd.normalized), toEnd.magnitude - 0.05f, out RaycastHit cover) &&
                cover.collider.GetComponentInParent<IDamageable>() != target)
            {
                endPoint = cover.point;
                target = cover.collider.GetComponentInParent<IDamageable>();
            }

            Debug.DrawLine(origin, endPoint, target != null ? Color.red : Color.yellow, 0.2f);

            if (data != null && data.IsProjectile)
            {
                // A rocket flies there and does its own damage when it goes off; nothing is hit yet.
                ShotFired?.Invoke(origin, endPoint, null);
                Vector3 direction = toEnd.sqrMagnitude > 0.01f ? toEnd.normalized : aimRay.direction;
                GameObject projectile = Instantiate(data.ProjectilePrefab, origin, Quaternion.LookRotation(direction));
                if (projectile.TryGetComponent(out Rocket rocket))
                    rocket.Launch(this, weapon, direction);
            }
            else
            {
                // Announce the shot BEFORE applying damage: a bird that dies from this hit reads the shot's
                // origin (via PlayerEventHub) to know where the shooter stood.
                ShotFired?.Invoke(origin, endPoint, target);

                if (target != null && target.IsAlive)
                {
                    target.TakeDamage(weapon.Damage, weapon.Rarity);
                    HitConfirmed?.Invoke(target, !target.IsAlive);
                }
            }

            // 4. Feel: the spread blooms and the view kicks (less while aiming).
            if (data != null)
            {
                bloom = Mathf.Min(bloom + data.SpreadPerShot, data.MaxSpread);
                float steadiness = cameraRig != null ? cameraRig.AimBlend : 0f;
                if (cameraRig != null)
                    cameraRig.AddRecoil(data.Recoil * Mathf.Lerp(1f, 0.6f, steadiness));
            }

            return true;
        }

        /// <summary>Reports a hit done later by one of this shooter's projectiles (raises <see cref="HitConfirmed"/>).</summary>
        public void ReportHit(IDamageable target, bool killed) => HitConfirmed?.Invoke(target, killed);

        /// <summary>
        /// The point under the crosshair (no spread), up to <paramref name="maxDistance"/>: what the gun should point
        /// at. Used by the aim IK so the barrel lines up with where shots will go.
        /// </summary>
        public Vector3 GetAimPoint(float maxDistance)
        {
            Camera cam = aimCamera != null ? aimCamera : Camera.main;
            if (cam == null)
                return transform.position + transform.forward * maxDistance;
            Ray ray = AimRay(cam);
            return TryRaycastIgnoringSelf(ray, maxDistance, out RaycastHit hit) ? hit.point : ray.GetPoint(maxDistance);
        }

        // In first person the eye is the origin of the crosshair, so use the exact camera ray. The third-person path
        // retains its upper-body compensation so a wall or bird behind the character cannot catch the aim.
        private Ray AimRay(Camera cam)
        {
            Ray ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            if (cameraRig != null && cameraRig.IsFirstPerson)
                return ray;

            Vector3 pivot = transform.position + Vector3.up * PivotHeight;
            float skip = Mathf.Max(0f, Vector3.Dot(pivot - ray.origin, ray.direction));
            ray.origin += ray.direction * skip;
            return ray;
        }

        // The nearest damageable within `radius` of the ray, closer than `maxDistance` (the first wall).
        private bool TrySphereCastForTarget(Ray ray, float radius, float maxDistance, out RaycastHit nearest,
            out IDamageable target)
        {
            int count = Physics.SphereCastNonAlloc(ray, radius, hitBuffer, maxDistance, hitLayers,
                QueryTriggerInteraction.Ignore);
            nearest = default;
            target = null;
            float nearestDistance = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = hitBuffer[i];
                if (hit.collider.transform.IsChildOf(transform) || hit.distance >= nearestDistance)
                    continue;
                IDamageable damageable = hit.collider.GetComponentInParent<IDamageable>();
                if (damageable == null || !damageable.IsAlive)
                    continue;
                nearest = hit;
                nearestDistance = hit.distance;
                target = damageable;
            }

            return target != null;
        }

        /// <summary>
        /// Raycast that skips the player's own colliders and trigger volumes, returning the nearest other hit.
        /// </summary>
        private bool TryRaycastIgnoringSelf(Ray ray, float maxDistance, out RaycastHit nearest)
        {
            int count = Physics.RaycastNonAlloc(ray, hitBuffer, maxDistance, hitLayers, QueryTriggerInteraction.Ignore);
            nearest = default;
            float nearestDistance = float.MaxValue;

            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = hitBuffer[i];
                if (hit.collider.transform.IsChildOf(transform) || hit.distance >= nearestDistance)
                    continue;

                nearest = hit;
                nearestDistance = hit.distance;
            }

            return nearestDistance < float.MaxValue;
        }
    }
}
