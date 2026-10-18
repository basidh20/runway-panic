using RunwayPanic.Core;
using RunwayPanic.Player;
using UnityEngine;

namespace RunwayPanic.Audio
{
    /// <summary>
    /// The player's own sounds: footsteps (by distance travelled, so the rhythm follows the real speed), jump and
    /// landing, getting hurt, dying, respawning, a heartbeat while badly hurt, using a medkit, the shield (on, hum,
    /// off) and picking items up. Only listens to existing player events; nothing else needs to call it.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class PlayerSounds : MonoBehaviour
    {
        [Header("Movement")]
        [SerializeField] private SoundCue footstepWalk;
        [SerializeField] private SoundCue footstepRun;
        [Tooltip("Metres between footsteps when walking / running.")]
        [SerializeField, Min(0.2f)] private float walkStride = 1.6f;
        [SerializeField, Min(0.2f)] private float runStride = 2.2f;
        [Tooltip("Above this horizontal speed (m/s) the run footsteps are used.")]
        [SerializeField, Min(0f)] private float runSpeed = 6f;
        [SerializeField] private SoundCue jump;
        [SerializeField] private SoundCue land;
        [Tooltip("Shorter falls than this (seconds in the air) land silently.")]
        [SerializeField, Min(0f)] private float minAirTimeForLanding = 0.25f;

        [Header("Health")]
        [SerializeField] private SoundCue hurt;
        [Tooltip("Seconds between hurt sounds, so a flurry of hits doesn't stack them.")]
        [SerializeField, Min(0f)] private float hurtCooldown = 0.35f;
        [SerializeField] private SoundCue death;
        [SerializeField] private SoundCue respawn;
        [Tooltip("Looping heartbeat while health is below this fraction.")]
        [SerializeField, Range(0f, 1f)] private float heartbeatBelow = 0.3f;
        [SerializeField] private SoundCue heartbeat;

        [Header("Items")]
        [SerializeField] private SoundCue medkit;
        [SerializeField] private SoundCue shieldOn;
        [SerializeField] private SoundCue shieldHum;
        [SerializeField] private SoundCue shieldOff;
        [SerializeField] private SoundCue pickup;

        private CharacterController characterController;
        private PlayerController controller;
        private PlayerHealth health;
        private PlayerShield shield;
        private PlayerItemUser itemUser;
        private PlayerInventory inventory;
        private PlayerRespawn respawner;

        private float stepDistance;
        private bool wasGrounded = true;
        private float airTime;
        private float nextHurtTime;
        private SoundLoop heartbeatLoop;
        private SoundLoop shieldLoop;
        private readonly int[] itemCounts = new int[8];

        private void Awake()
        {
            characterController = GetComponent<CharacterController>();
            controller = GetComponent<PlayerController>();
            health = GetComponent<PlayerHealth>();
            shield = GetComponent<PlayerShield>();
            itemUser = GetComponent<PlayerItemUser>();
            inventory = GetComponent<PlayerInventory>();
            respawner = GetComponent<PlayerRespawn>();
        }

        private void OnEnable()
        {
            if (controller != null) controller.Jumped += HandleJumped;
            if (health != null)
            {
                health.Damaged += HandleDamaged;
                health.Died += HandleDied;
            }
            if (respawner != null) respawner.Respawned += HandleRespawned;
            if (itemUser != null) itemUser.MedkitUsed += HandleMedkit;
            if (shield != null)
            {
                shield.Activated += HandleShieldOn;
                shield.Expired += HandleShieldOff;
            }
            if (inventory != null) inventory.ItemCountChanged += HandleItemCount;
        }

        private void OnDisable()
        {
            if (controller != null) controller.Jumped -= HandleJumped;
            if (health != null)
            {
                health.Damaged -= HandleDamaged;
                health.Died -= HandleDied;
            }
            if (respawner != null) respawner.Respawned -= HandleRespawned;
            if (itemUser != null) itemUser.MedkitUsed -= HandleMedkit;
            if (shield != null)
            {
                shield.Activated -= HandleShieldOn;
                shield.Expired -= HandleShieldOff;
            }
            if (inventory != null) inventory.ItemCountChanged -= HandleItemCount;

            StopLoop(ref heartbeatLoop);
            StopLoop(ref shieldLoop);
        }

        private void Update()
        {
            bool alive = health == null || health.IsAlive;
            bool grounded = controller != null ? controller.IsGrounded : characterController.isGrounded;
            Vector3 velocity = characterController.velocity;
            velocity.y = 0f;
            float speed = velocity.magnitude;

            // Footsteps: one per stride of distance actually covered on the ground.
            if (alive && grounded && speed > 0.5f)
            {
                bool running = speed >= runSpeed;
                stepDistance += speed * Time.deltaTime;
                if (stepDistance >= (running ? runStride : walkStride))
                {
                    stepDistance = 0f;
                    SoundPlayer.Play(running ? footstepRun : footstepWalk, transform.position);
                }
            }

            // Landing: after real air time, louder the faster the fall.
            if (!grounded)
            {
                airTime += Time.deltaTime;
            }
            else if (!wasGrounded)
            {
                if (alive && airTime >= minAirTimeForLanding)
                    SoundPlayer.Play(land, transform.position, Mathf.Clamp(0.5f + airTime, 0.6f, 1f));
                airTime = 0f;
                stepDistance = 0f;
            }

            wasGrounded = grounded;

            // Heartbeat while badly hurt.
            bool lowHealth = alive && health != null && health.CurrentHP < health.MaxHP * heartbeatBelow;
            if (lowHealth && heartbeatLoop == null)
                heartbeatLoop = SoundPlayer.StartLoop(heartbeat, transform);
            else if (!lowHealth)
                StopLoop(ref heartbeatLoop);
        }

        private void HandleJumped() => SoundPlayer.Play(jump, transform.position);

        private void HandleDamaged(float amount)
        {
            if (health.IsAlive && Time.time >= nextHurtTime)
            {
                nextHurtTime = Time.time + hurtCooldown;
                SoundPlayer.Play(hurt, transform.position + Vector3.up * 1.5f);
            }
        }

        private void HandleDied()
        {
            StopLoop(ref heartbeatLoop);
            SoundPlayer.Play(death, transform.position + Vector3.up);
        }

        private void HandleRespawned() => SoundPlayer.Play(respawn, transform.position + Vector3.up);

        private void HandleMedkit() => SoundPlayer.Play(medkit, transform.position + Vector3.up);

        private void HandleShieldOn(float duration)
        {
            SoundPlayer.Play(shieldOn, transform.position + Vector3.up);
            StopLoop(ref shieldLoop);
            shieldLoop = SoundPlayer.StartLoop(shieldHum, transform);
        }

        private void HandleShieldOff()
        {
            StopLoop(ref shieldLoop);
            SoundPlayer.Play(shieldOff, transform.position + Vector3.up);
        }

        // An item count going up is a pickup (or a purchase): a short chime.
        private void HandleItemCount(ItemType item, int count)
        {
            int index = (int)item;
            if (index >= 0 && index < itemCounts.Length)
            {
                if (count > itemCounts[index])
                    SoundPlayer.Play(pickup, transform.position + Vector3.up);
                itemCounts[index] = count;
            }
        }

        private static void StopLoop(ref SoundLoop loop)
        {
            if (loop != null)
                loop.Stop();
            loop = null;
        }
    }
}
