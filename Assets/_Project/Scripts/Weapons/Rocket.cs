using System.Collections.Generic;
using RunwayPanic.Audio;
using RunwayPanic.Core;
using RunwayPanic.Player;
using UnityEngine;

namespace RunwayPanic.Weapons
{
    /// <summary>
    /// An RPG rocket (P_Rocket), fired by <see cref="PlayerShooting"/> for a weapon with a projectile. It leaves the
    /// tube slowly, the motor kicks in and it speeds up, roaring and trailing smoke, wobbling and spinning slightly. It
    /// goes off when it hits something, when a bird comes within the proximity fuse (a near miss still counts) or at
    /// the gun's range (an air burst). The blast damages everything within the weapon's explosion radius — full
    /// damage at the centre, less towards the edge — unless a wall or shelter is in the way; the shooter is never hurt.
    /// The launch kicks out a backblast behind the tube; the blast flashes, shakes the camera and leaves its smoke trail
    /// hanging in the air.
    /// </summary>
    public sealed class Rocket : MonoBehaviour
    {
        private const int MaxOverlaps = 32;
        private const int MaxHits = 16;

        [Header("Flight")]
        [Tooltip("Speed leaving the tube (m/s); the motor then accelerates it up to the top speed.")]
        [SerializeField, Min(1f)] private float launchSpeed = 35f;
        [SerializeField, Min(1f)] private float maxSpeed = 110f;
        [SerializeField, Min(0f)] private float acceleration = 180f;
        [Tooltip("Thickness of the rocket when testing what it hits (metres).")]
        [SerializeField, Min(0.01f)] private float collisionRadius = 0.12f;
        [Tooltip("Goes off when a bird comes this close (metres).")]
        [SerializeField, Min(0f)] private float proximityFuse = 1.6f;
        [Tooltip("How much the body wobbles in flight (degrees) and how fast it rolls (degrees/s). Looks only.")]
        [SerializeField, Min(0f)] private float wobble = 1.5f;
        [SerializeField] private float spin = 720f;

        [Header("Parts")]
        [Tooltip("The visible rocket; wobbles and spins.")]
        [SerializeField] private Transform body;
        [Tooltip("Left behind to drift and fade when the rocket goes off.")]
        [SerializeField] private ParticleSystem smokeTrail;
        [Tooltip("The motor lights (and the trail starts) this far out (metres), so the smoke doesn't fill the view.")]
        [SerializeField, Min(0f)] private float trailStart = 3f;
        [Tooltip("Spawned at the muzzle on launch: flash and smoke out of both ends of the tube (P_RocketLaunch).")]
        [SerializeField] private GameObject launchEffect;
        [SerializeField] private GameObject explosionEffect;
        [SerializeField] private SoundCue flightSound;
        [SerializeField] private SoundCue explosionSound;

        [Header("Blast")]
        [Tooltip("Damage at the edge of the blast, as a share of the full damage.")]
        [SerializeField, Range(0f, 1f)] private float edgeDamage = 0.35f;
        [Tooltip("Camera shake (degrees) on launch, and from a blast right next to the player.")]
        [SerializeField, Min(0f)] private float launchShake = 0.6f;
        [SerializeField, Min(0f)] private float blastShake = 2.5f;
        [Tooltip("A blast this far from the player (metres) no longer shakes the camera.")]
        [SerializeField, Min(1f)] private float shakeDistance = 45f;

        private static readonly Collider[] Overlaps = new Collider[MaxOverlaps];
        private static readonly RaycastHit[] Hits = new RaycastHit[MaxHits];

        private readonly HashSet<IDamageable> damaged = new HashSet<IDamageable>();
        private PlayerShooting owner;
        private Transform ownerRoot;
        private PlayerCameraRig ownerCamera;
        private float damage;
        private WeaponRarity rarity;
        private float range;
        private float radius;
        private float speed;
        private float travelled;
        private float flightTime;
        private SoundLoop flight;
        private bool launched;

        /// <summary>Sends the rocket off from <paramref name="weapon"/>'s muzzle along <paramref name="direction"/>.</summary>
        public void Launch(PlayerShooting shooter, WeaponBase weapon, Vector3 direction)
        {
            owner = shooter;
            ownerRoot = shooter.transform;
            ownerCamera = shooter.GetComponent<PlayerCameraRig>();
            damage = weapon.Damage;
            rarity = weapon.Rarity;
            range = weapon.Range;
            radius = weapon.Data.ExplosionRadius;

            transform.SetPositionAndRotation(weapon.Muzzle.position, Quaternion.LookRotation(direction));
            speed = launchSpeed;
            launched = true;
            SetTrail(trailStart <= 0f);

            if (launchEffect != null)
                Instantiate(launchEffect, weapon.Muzzle.position, weapon.Muzzle.rotation);
            flight = SoundPlayer.StartLoop(flightSound, transform);
            if (ownerCamera != null)
                ownerCamera.AddShake(launchShake);
        }

        private void Update()
        {
            if (!launched)
                return;

            float dt = Time.deltaTime;
            speed = Mathf.Min(maxSpeed, speed + acceleration * dt);
            float step = speed * dt;
            Vector3 from = transform.position;
            Vector3 direction = transform.forward;

            if (TrySphereCast(from, direction, step, out RaycastHit hit))
            {
                transform.position = hit.point;
                Explode(hit.point, hit.collider.GetComponentInParent<IDamageable>());
                return;
            }

            transform.position = from + direction * step;
            float before = travelled;
            travelled += step;
            if (before < trailStart && travelled >= trailStart)
                SetTrail(true);
            if (BirdWithinFuse() || travelled >= range)
            {
                Explode(transform.position, null);
                return;
            }

            if (body != null)
            {
                flightTime += dt;
                body.localRotation = Quaternion.Euler(Mathf.Sin(flightTime * 23f) * wobble,
                    Mathf.Cos(flightTime * 19f) * wobble, flightTime * spin);
            }
        }

        private void OnDestroy()
        {
            flight?.Stop();
        }

        private void Explode(Vector3 point, IDamageable direct)
        {
            launched = false;
            flight?.Stop();
            flight = null;

            if (explosionEffect != null)
                Instantiate(explosionEffect, point, Quaternion.identity);
            SoundPlayer.Play(explosionSound, point);

            ApplyBlast(point, direct);

            if (ownerCamera != null)
            {
                float distance = Vector3.Distance(ownerCamera.transform.position, point);
                ownerCamera.AddShake(blastShake * Mathf.Clamp01(1f - distance / shakeDistance));
            }

            // The smoke trail stays and fades on its own.
            if (smokeTrail != null)
            {
                smokeTrail.transform.SetParent(null, true);
                smokeTrail.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                Destroy(smokeTrail.gameObject, smokeTrail.main.startLifetime.constantMax + 0.5f);
            }

            Destroy(gameObject);
        }

        private void ApplyBlast(Vector3 point, IDamageable direct)
        {
            damaged.Clear();
            if (direct != null && direct.IsAlive)
                Hit(direct, damage);
            if (radius <= 0f)
                return;

            // Test lines of sight from just behind the impact, so the wall the rocket hit doesn't block its own blast.
            Vector3 centre = point - transform.forward * 0.3f;
            int count = Physics.OverlapSphereNonAlloc(point, radius, Overlaps, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Collider collider = Overlaps[i];
                if (IsOwner(collider))
                    continue;
                IDamageable target = collider.GetComponentInParent<IDamageable>();
                if (target == null || !target.IsAlive || damaged.Contains(target))
                    continue;

                Vector3 closest = collider.bounds.ClosestPoint(point);
                if (IsShielded(centre, closest))
                    continue;
                float falloff = Mathf.Clamp01(Vector3.Distance(point, closest) / radius);
                Hit(target, damage * Mathf.Lerp(1f, edgeDamage, falloff));
            }
        }

        private void Hit(IDamageable target, float amount)
        {
            damaged.Add(target);
            target.TakeDamage(amount, rarity);
            if (owner != null)
                owner.ReportHit(target, !target.IsAlive);
        }

        // True when something solid (not a damageable) stands between the blast and the target.
        private bool IsShielded(Vector3 from, Vector3 to)
        {
            Vector3 line = to - from;
            float distance = line.magnitude;
            if (distance < 0.05f)
                return false;

            int count = Physics.RaycastNonAlloc(from, line / distance, Hits, distance - 0.05f, ~0,
                QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Collider collider = Hits[i].collider;
                // The target's own colliders and other birds don't shield it; walls and shelters do.
                if (!IsOwner(collider) && collider.GetComponentInParent<IDamageable>() == null)
                    return true;
            }

            return false;
        }

        private bool BirdWithinFuse()
        {
            if (proximityFuse <= 0f)
                return false;
            int count = Physics.OverlapSphereNonAlloc(transform.position, proximityFuse, Overlaps, ~0,
                QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                if (IsOwner(Overlaps[i]))
                    continue;
                IDamageable target = Overlaps[i].GetComponentInParent<IDamageable>();
                if (target != null && target.IsAlive)
                    return true;
            }

            return false;
        }

        // The nearest thing (not the shooter) the rocket would touch moving `distance` along `direction`.
        private bool TrySphereCast(Vector3 from, Vector3 direction, float distance, out RaycastHit nearest)
        {
            int count = Physics.SphereCastNonAlloc(from, collisionRadius, direction, Hits, distance, ~0,
                QueryTriggerInteraction.Ignore);
            nearest = default;
            float nearestDistance = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = Hits[i];
                if (IsOwner(hit.collider) || hit.distance >= nearestDistance)
                    continue;
                nearest = hit;
                nearestDistance = hit.distance;
            }

            if (nearestDistance < float.MaxValue && nearest.point == Vector3.zero)
                nearest.point = from + direction * nearest.distance; // overlapping at the start
            return nearestDistance < float.MaxValue;
        }

        private void SetTrail(bool on)
        {
            if (smokeTrail == null)
                return;
            ParticleSystem.EmissionModule emission = smokeTrail.emission;
            emission.enabled = on;
        }

        private bool IsOwner(Collider collider) => ownerRoot != null && collider.transform.IsChildOf(ownerRoot);
    }
}
