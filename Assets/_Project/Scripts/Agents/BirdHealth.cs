using System;
using System.Collections;
using RunwayPanic.Core;
using RunwayPanic.Data;
using RunwayPanic.Player;
using UnityEngine;

namespace RunwayPanic.Agents
{
    /// <summary>
    /// Bird HP. The player's guns find it through <see cref="IDamageable"/> on the bird's collider or a parent.
    /// HP comes from the species asset, and a hit from the wrong gun is reduced by the species' damage rule.
    /// A hit plays the flinch; death plays the death clip, pays the coin reward, stops path following and lets the
    /// body fall under gravity. Once it lands (or after Max Fall Time) it is removed.
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

        /// <summary>Raised once when the bird dies, before it falls. The WaveManager can count kills with it.</summary>
        public event Action<BirdHealth> Died;

        /// <summary>Raised after every hit that the bird survives or dies from. Argument: damage actually taken.</summary>
        public event Action<float> Damaged;

        private BirdAgent agent;
        private BirdPathFollower follower;
        private BirdAnimatorDriver animatorDriver;
        private Rigidbody body;
        private Collider bodyCollider;
        private Coroutine deathRoutine;
        private bool landed;

        public float MaxHP => Species != null ? Species.MaxHP : fallbackMaxHP;
        public float CurrentHP { get; private set; }
        public bool IsAlive => CurrentHP > 0f;

        private BirdSpeciesData Species => agent != null ? agent.Species : null;

        private void Awake()
        {
            agent = GetComponent<BirdAgent>();
            follower = GetComponent<BirdPathFollower>();
            animatorDriver = GetComponent<BirdAnimatorDriver>();
            body = GetComponent<Rigidbody>();
            bodyCollider = GetComponent<Collider>();
            CurrentHP = MaxHP;
        }

        /// <summary>
        /// Applies a hit. A gun of the species' required rarity does full damage; any other does the reduced amount
        /// from <see cref="BirdSpeciesData.GetDamageTaken"/>.
        /// </summary>
        public void TakeDamage(float amount, WeaponRarity weaponRarity)
        {
            if (!IsAlive || amount <= 0f)
                return;

            BirdSpeciesData species = Species;
            float taken = species != null ? species.GetDamageTaken(amount, weaponRarity) : amount;
            CurrentHP = Mathf.Max(0f, CurrentHP - taken);
            Damaged?.Invoke(taken);

            if (IsAlive)
            {
                if (animatorDriver != null)
                    animatorDriver.PlayHit();
            }
            else
            {
                Die();
            }
        }

        /// <summary>Kills the bird outright (end of a wave, tests). Still pays the coin reward.</summary>
        public void Kill()
        {
            if (!IsAlive)
                return;

            CurrentHP = 0f;
            Die();
        }

        /// <summary>
        /// Brings a dead or deactivated bird back to full HP, flying (pooling, sandbox respawn). Activate the
        /// GameObject first; place it with <see cref="BirdPathFollower.Teleport"/>.
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
            if (follower != null)
                follower.enabled = true;
            if (animatorDriver != null)
                animatorDriver.ResetAnimation();
        }

        private void Die()
        {
            if (animatorDriver != null)
                animatorDriver.PlayDeath();

            BirdSpeciesData species = Species;
            if (species != null && CoinManager.Instance != null)
                CoinManager.Instance.AddCoins(species.CoinReward);

            // Hand the body from the path follower to physics, keeping its momentum so it arcs down naturally.
            Vector3 velocity = Vector3.zero;
            if (follower != null)
            {
                velocity = follower.Velocity;
                follower.Stop();
                follower.enabled = false;
            }

            body.isKinematic = false;
            body.useGravity = true;
            body.linearVelocity = velocity;
            body.angularVelocity = transform.right * (deathTumble * Mathf.Deg2Rad);

            landed = false;
            deathRoutine = StartCoroutine(FallAndRemove());
            Died?.Invoke(this);
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
