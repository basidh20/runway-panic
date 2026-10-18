using System;
using System.Collections.Generic;
using RunwayPanic.AI;
using RunwayPanic.Core;
using RunwayPanic.Data;
using RunwayPanic.Player;
using UnityEngine;
using UnityEngine.AI;

namespace RunwayPanic.Agents.Gull
{
    /// <summary>One carry/drop lifecycle; the scene FodManager owns landed lifetime, cap and wave cleanup.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody), typeof(BoxCollider))]
    public sealed class FodItem : MonoBehaviour
    {
        public enum FodState { Lying, Carried, Falling, Landed }

        [Tooltip("Gull strategy: splash radius, damage multiplier and landed lifetime.")]
        [SerializeField] private GullStrategyConfig config;
        [Tooltip("Gull species data; damage is AttackDamage times the strategy multiplier.")]
        [SerializeField] private BirdSpeciesData species;

        private readonly Collider[] splashBuffer = new Collider[32];
        private readonly List<Collider> ignoredColliders = new List<Collider>();
        private Rigidbody body;
        private BoxCollider itemCollider;
        private NavMeshObstacle obstacle;
        private FlightGrid registeredGrid;
        private Bounds registeredBounds;
        private bool registered, impacted, despawning;
        private float fallingAge;
        private FodManager manager;
        private const float MaxFallSeconds = 15f;

        public FodState State { get; private set; } = FodState.Lying;
        public GullStrategyConfig Config => config;
        public float Damage => species != null && config != null ? species.AttackDamage * config.fodDamageMultiplier : 0f;
        /// <summary>Once per release, on the first non-bird collision. hitPlayer means the splash reduced HP.</summary>
        public event Action<FodItem, bool> Impacted;
        /// <summary>Raised once when the item actually settles on scenery (after a possible player hit).</summary>
        public event Action<FodItem> Settled;

        private bool GameplayActive => GameManager.Instance == null || GameManager.Instance.CurrentState == GameState.Playing;

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
            itemCollider = GetComponent<BoxCollider>();
            obstacle = GetComponent<NavMeshObstacle>();
            if (obstacle != null) obstacle.enabled = false;
            body.isKinematic = true;
            body.useGravity = false;
        }

        /// <summary>May be set by a future supply point before pickup; never edits the shared assets.</summary>
        public void Configure(GullStrategyConfig strategy, BirdSpeciesData gullSpecies)
        {
            if (strategy == null || gullSpecies == null || gullSpecies.Species != BirdSpecies.Gull)
                throw new ArgumentException("FOD requires a strategy config and the Gull species asset.");
            if (State != FodState.Lying) throw new InvalidOperationException("Configure FOD before its carry/drop lifecycle.");
            config = strategy;
            species = gullSpecies;
        }

        public void AttachTo(Transform carrier, Vector3 localOffset)
        {
            if (carrier == null) throw new ArgumentNullException(nameof(carrier));
            if (despawning || (State != FodState.Lying && State != FodState.Landed)) return;
            if (!HasSetup()) return;
            itemCollider.enabled = false;
            if (obstacle != null) obstacle.enabled = false;
            if (manager != null) manager.RemoveLanded(this);
            RemoveGridBlock();
            RestoreCollisionPairs();
            body.isKinematic = true;
            body.useGravity = false;
            // Keep the prop's world dimensions even though species roots have a scale such as 0.6.
            transform.SetParent(carrier, true);
            transform.localPosition = localOffset;
            transform.localRotation = Quaternion.identity;
            State = FodState.Carried;
            fallingAge = 0f;
        }

        [ContextMenu("Debug Release")]
        public void Release()
        {
            if (!Application.isPlaying || despawning || (State != FodState.Carried && State != FodState.Lying) || !HasSetup()) return;
            var carrier = transform.parent;
            transform.SetParent(null, true);
            body.isKinematic = false;
            body.useGravity = true;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            itemCollider.enabled = true;
            if (carrier != null)
                foreach (var collider in carrier.GetComponentsInChildren<Collider>()) Ignore(collider);
            impacted = false;
            fallingAge = 0f;
            State = FodState.Falling;
        }

        private bool HasSetup()
        {
            if (config != null && species != null && species.Species == BirdSpecies.Gull) return true;
            Debug.LogError("FodItem requires its Gull strategy and species references.", this);
            return false;
        }

        private void Update()
        {
            if (!GameplayActive || despawning) return;
            if (State == FodState.Falling)
            {
                fallingAge += Time.deltaTime;
                if (fallingAge >= MaxFallSeconds) Despawn(); // No endless lost projectiles below/outside the airport.
            }
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (despawning || State != FodState.Falling || collision.contactCount == 0) return;
            var other = collision.collider;
            if (other.GetComponentInParent<BirdHealth>() != null) { Ignore(other); return; }
            if (!impacted)
            {
                impacted = true; // Set before damage/events so multiple contacts cannot splash twice.
                bool hitPlayer = GameplayActive && Splash(collision.GetContact(0).point);
                Impacted?.Invoke(this, hitPlayer);
                if (despawning || !isActiveAndEnabled || State != FodState.Falling) return;
            }
            if (other.GetComponentInParent<PlayerHealth>() != null)
            {
                // Continue through the player's colliders after the hit; settle only on level geometry.
                foreach (var collider in other.GetComponentInParent<PlayerHealth>().GetComponentsInChildren<Collider>()) Ignore(collider);
                return;
            }

            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            body.isKinematic = true;
            body.useGravity = false;
            State = FodState.Landed;
            if (obstacle == null) obstacle = gameObject.AddComponent<NavMeshObstacle>();
            obstacle.shape = NavMeshObstacleShape.Box;
            obstacle.center = itemCollider.center;
            obstacle.size = itemCollider.size;
            obstacle.carving = true;
            obstacle.enabled = true;
            AddGridBlock();
            if (manager != null) manager.MarkLanded(this);
            Settled?.Invoke(this);
        }

        private bool Splash(Vector3 point)
        {
            int count = Physics.OverlapSphereNonAlloc(point, Mathf.Max(0f, config.fodSplashRadius), splashBuffer, ~0, QueryTriggerInteraction.Ignore);
            // An unusually dense area must not silently hide the player behind a full fixed buffer.
            Collider[] hits = count == splashBuffer.Length
                ? Physics.OverlapSphere(point, Mathf.Max(0f, config.fodSplashRadius), ~0, QueryTriggerInteraction.Ignore) : splashBuffer;
            if (hits != splashBuffer) count = hits.Length;
            for (int i = 0; i < count; i++)
            {
                var player = hits[i].GetComponentInParent<PlayerHealth>();
                if (player == null || !player.IsAlive) continue;
                float before = player.CurrentHP;
                player.TakeDamage(Damage, WeaponRarity.Common);
                return player.CurrentHP < before; // One player, possibly many colliders: apply damage once.
            }
            return false;
        }

        private void AddGridBlock()
        {
            if (registered || FlightGrid.Instance == null) return;
            var grid = FlightGrid.Instance;
            grid.EnsureBuilt();
            Bounds bounds = itemCollider.bounds;
            Bounds volume = grid.VolumeBounds;
            if (!grid.IsBuilt || bounds.max.x < volume.min.x || bounds.min.x > volume.max.x ||
                bounds.max.z < volume.min.z || bounds.min.z > volume.max.z || bounds.min.y > volume.max.y) return;
            // Ground debris sits below the grid's y=0.5 lower edge; SetManualBlock clamps it to the bottom cell.
            registeredGrid = grid;
            registeredBounds = bounds;
            registeredGrid.SetManualBlock(registeredBounds, true);
            registered = true;
        }

        private void RemoveGridBlock()
        {
            if (!registered) return;
            registered = false;
            if (registeredGrid != null && registeredGrid.IsBuilt)
            {
                registeredGrid.SetManualBlock(registeredBounds, false); // Exact original bounds; overlapping pieces keep their own blocks.
                registeredGrid.RebuildRegion(registeredBounds); // Also clear stale geometry if the grid was rebuilt while this existed.
            }
            registeredGrid = null;
        }

        private void Ignore(Collider other)
        {
            if (other == null || other == itemCollider || ignoredColliders.Contains(other)) return;
            Physics.IgnoreCollision(itemCollider, other, true);
            ignoredColliders.Add(other);
        }

        private void RestoreCollisionPairs()
        {
            foreach (var collider in ignoredColliders)
                if (collider != null) Physics.IgnoreCollision(itemCollider, collider, false);
            ignoredColliders.Clear();
        }

        public void Despawn()
        {
            if (despawning) return;
            GullDebugStats.ForgetDrop(this);
            despawning = true;
            if (manager != null) manager.Untrack(this);
            itemCollider.enabled = false;
            if (obstacle != null) obstacle.enabled = false;
            RemoveGridBlock();
            RestoreCollisionPairs();
            Destroy(gameObject);
        }

        private void OnDisable()
        {
            GullDebugStats.ForgetDrop(this);
            if (manager != null) manager.Untrack(this);
            if (itemCollider != null) itemCollider.enabled = false;
            if (obstacle != null) obstacle.enabled = false;
            RemoveGridBlock();
            RestoreCollisionPairs();
        }

        private void OnEnable()
        {
            if (itemCollider == null || despawning) return;
            manager = FodManager.Instance;
            if (manager != null) manager.Track(this);
            itemCollider.enabled = State != FodState.Carried;
            if (State == FodState.Landed)
            {
                if (obstacle != null) obstacle.enabled = true;
                AddGridBlock();
                if (manager != null) manager.MarkLanded(this);
            }
        }

        private void OnDestroy() { RemoveGridBlock(); }
    }
}
