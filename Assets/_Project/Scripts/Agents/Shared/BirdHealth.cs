using System;
using System.Collections;
using RunwayPanic.Core;
using RunwayPanic.Data;
using RunwayPanic.Player;
using UnityEngine;

namespace RunwayPanic.Agents
{
    /// <summary>
    /// Bird HP (F3, Agent Controller). The player's guns find it through <see cref="IDamageable"/> on the bird's
    /// collider or a parent; pathfinding ignores colliders with an <see cref="IDamageable"/>.
    /// HP comes from the species asset, and a hit from the wrong gun is reduced by the species' damage rule.
    /// A hit plays the flinch; death plays the death clip, pays the coin reward, reports the death to
    /// <see cref="BirdRegistry"/>, stops the motor and lets the body fall under gravity. Once it lands (or after
    /// Max Fall Time) it is removed.
    /// Registers with <see cref="BirdRegistry"/> while enabled and alive.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class BirdHealth : MonoBehaviour, IDamageable
    {
        /// <summary>What happens to the body at the end: Destroy for normal waves, Deactivate for pooling or tests.</summary>
        public enum DeathCleanup
        {
            Destroy = 0,
            Deactivate = 1
        }

        // A surface counts as ground when its normal points at least this much up.
        private const float GroundNormalY = 0.5f;
        // A shot counts as the cause of a hit if it was fired this recently, in seconds.
        private const float ShotMatchSeconds = 0.1f;

        [Tooltip("This bird's species asset (ScriptableObjects/Birds). Sets HP, speed, attack and coins.")]
        [SerializeField] private BirdSpeciesData species;
        [Tooltip("HP used when the bird has no species asset.")]
        [SerializeField, Min(1f)] private float fallbackMaxHP = 30f;
        [Tooltip("What happens to the body once it has landed and Remove Delay has passed.")]
        [SerializeField] private DeathCleanup cleanup = DeathCleanup.Destroy;
        [Tooltip("Seconds the body stays on the ground before it is removed.")]
        [SerializeField, Min(0f)] private float removeDelay = 2f;
        [Tooltip("The body is removed after this many seconds even if it never lands (e.g. it fell off the map).")]
        [SerializeField, Min(0.5f)] private float maxFallTime = 6f;
        [Tooltip("Nose-down spin given to the body when it starts to fall, in degrees per second.")]
        [SerializeField, Min(0f)] private float deathTumble = 120f;

        /// <summary>Raised when damage is applied. Arguments: damage applied, HP after.</summary>
        public event Action<float, float> Damaged;

        /// <summary>Raised once when the bird dies, before it falls. The WaveManager can count kills with it.</summary>
        public event Action<BirdHealth> Died;

        private IBirdMotor motor;
        private Behaviour motorBehaviour;
        private IBirdAnimator birdAnimator;
        private Rigidbody body;
        private Collider bodyCollider;
        private Coroutine deathRoutine;
        private bool landed;

        /// <summary>The species asset.</summary>
        public BirdSpeciesData Species => species;

        /// <summary>Full HP, from the species asset.</summary>
        public float MaxHP => species != null ? species.MaxHP : fallbackMaxHP;

        /// <summary>Current HP.</summary>
        public float CurrentHP { get; private set; }

        /// <summary>False once dead.</summary>
        public bool IsAlive => CurrentHP > 0f;

        /// <summary>Muzzle position of the shot that last hit this bird (the bird's own position for non-gun damage).</summary>
        public Vector3 LastHitShotOrigin { get; private set; }

        /// <summary>Time.time of the last hit (−∞ if never hit).</summary>
        public float LastHitTime { get; private set; } = float.NegativeInfinity;

        private void Awake()
        {
            motor = GetComponent<IBirdMotor>();
            motorBehaviour = motor as Behaviour;
            birdAnimator = GetComponentInChildren<IBirdAnimator>();
            body = GetComponent<Rigidbody>();
            bodyCollider = GetComponent<Collider>();
            CurrentHP = MaxHP;
        }

        private void OnEnable()
        {
            if (IsAlive)
                BirdRegistry.Register(this);
        }

        private void OnDisable()
        {
            BirdRegistry.Unregister(this);
        }

        /// <summary>
        /// Applies a hit. A gun of the species' required rarity does full damage; any other does the reduced amount
        /// from <see cref="BirdSpeciesData.GetDamageTaken"/>.
        /// </summary>
        public void TakeDamage(float amount, WeaponRarity weaponRarity)
        {
            if (!IsAlive || amount <= 0f)
                return;

            float taken = species != null ? species.GetDamageTaken(amount, weaponRarity) : amount;
            CurrentHP = Mathf.Max(0f, CurrentHP - taken);
            RecordHit();
            Damaged?.Invoke(taken, CurrentHP);

            if (IsAlive)
                birdAnimator?.PlayHit();
            else
                Die();
        }

        /// <summary>Kills the bird outright (end of a wave, tests). Still pays the coin reward.</summary>
        public void Kill()
        {
            if (!IsAlive)
                return;

            RecordHit();
            CurrentHP = 0f;
            Die();
        }

        /// <summary>
        /// Brings a dead or deactivated bird back to full HP, flying (pooling, sandbox respawn). Activate the
        /// GameObject first, then place it with the motor.
        /// </summary>
        public void Revive()
        {
            if (deathRoutine != null)
            {
                StopCoroutine(deathRoutine);
                deathRoutine = null;
            }

            CurrentHP = MaxHP;
            landed = false;
            body.isKinematic = true;
            body.useGravity = false;
            if (bodyCollider != null)
                bodyCollider.enabled = true;
            if (motorBehaviour != null)
                motorBehaviour.enabled = true;
            BirdAnimatorDriver driver = GetComponentInChildren<BirdAnimatorDriver>();
            if (driver != null)
                driver.ResetAnimation(); // back to the flying state; IBirdAnimator has no reset
            if (isActiveAndEnabled)
                BirdRegistry.Register(this);
        }

        // The player's last shot is the cause if it was fired just now; otherwise (tests, debris) the bird itself.
        private void RecordHit()
        {
            PlayerEventHub hub = PlayerEventHub.Instance;
            bool fromShot = hub != null && Time.time - hub.LastShotTime <= ShotMatchSeconds;
            LastHitShotOrigin = fromShot ? hub.LastShotOrigin : transform.position;
            LastHitTime = Time.time;
        }

        private void Die()
        {
            birdAnimator?.PlayDeath();

            if (species != null && CoinManager.Instance != null)
                CoinManager.Instance.AddCoins(species.CoinReward);

            // Hand the body from the motor to physics, keeping its momentum so it arcs down naturally.
            Vector3 velocity = Vector3.zero;
            if (motor != null)
            {
                velocity = motor.Velocity;
                motor.Stop();
                if (motorBehaviour != null)
                    motorBehaviour.enabled = false;
            }

            body.isKinematic = false;
            body.useGravity = true;
            body.linearVelocity = velocity;
            body.angularVelocity = transform.right * (deathTumble * Mathf.Deg2Rad);

            landed = false;
            deathRoutine = StartCoroutine(FallAndRemove());

            Died?.Invoke(this);
            BirdRegistry.NotifyDied(this, LastHitShotOrigin);
        }

        // A coroutine instead of Update: a living bird pays nothing per frame for its death sequence.
        private IEnumerator FallAndRemove()
        {
            float giveUpTime = Time.time + maxFallTime;
            while (!landed && Time.time < giveUpTime)
                yield return null;

            if (landed && removeDelay > 0f)
                yield return new WaitForSeconds(removeDelay);

            deathRoutine = null;
            if (cleanup == DeathCleanup.Destroy)
                Destroy(gameObject);
            else
                gameObject.SetActive(false);
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (IsAlive || landed || collision.contactCount == 0)
                return;

            // Only ground counts (not a wall or another bird): the contact normal must point up.
            if (collision.GetContact(0).normal.y < GroundNormalY)
                return;

            // Rest where it fell: no sliding, no further hits, and the player can walk over it.
            landed = true;
            body.isKinematic = true;
            if (bodyCollider != null)
                bodyCollider.enabled = false;
        }
    }
}
