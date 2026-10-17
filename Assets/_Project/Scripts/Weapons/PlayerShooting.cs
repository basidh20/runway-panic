using System;
using RunwayPanic.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RunwayPanic.Weapons
{
    /// <summary>
    /// Fires the equipped weapon while Attack is held. Aims through the centre of the screen,
    /// then traces the actual shot from the gun's muzzle to that aim point so cover between
    /// the gun and the target still blocks it. Damages anything implementing <see cref="IDamageable"/>.
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

        private readonly RaycastHit[] hitBuffer = new RaycastHit[MaxHits];
        private PlayerWeaponHolder weaponHolder;
        private InputAction attackAction;
        private InputAction reloadAction;

        private void Awake()
        {
            weaponHolder = GetComponent<PlayerWeaponHolder>();
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

            // 1. Find what the crosshair is pointing at.
            Ray aimRay = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            Vector3 aimPoint = TryRaycastIgnoringSelf(aimRay, weapon.Range, out RaycastHit aimHit)
                ? aimHit.point
                : aimRay.GetPoint(weapon.Range);

            // 2. Trace the bullet from the muzzle to that point.
            Vector3 origin = weapon.Muzzle.position;
            Vector3 toAim = aimPoint - origin;
            Ray shotRay = new Ray(origin, toAim.normalized);
            float shotDistance = Mathf.Min(toAim.magnitude + 0.1f, weapon.Range);

            Vector3 endPoint = shotRay.GetPoint(shotDistance);
            IDamageable target = null;

            if (TryRaycastIgnoringSelf(shotRay, shotDistance, out RaycastHit shotHit))
            {
                endPoint = shotHit.point;
                target = shotHit.collider.GetComponentInParent<IDamageable>();
            }

            Debug.DrawLine(origin, endPoint, target != null ? Color.red : Color.yellow, 0.2f);

            // Announce the shot BEFORE applying damage: a bird that dies from this hit reads the shot's
            // origin (via PlayerEventHub) to know where the shooter stood.
            ShotFired?.Invoke(origin, endPoint, target);

            if (target != null && target.IsAlive)
                target.TakeDamage(weapon.Damage, weapon.Rarity);

            return true;
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
