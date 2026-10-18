using RunwayPanic.Agents;
using RunwayPanic.Core;
using UnityEngine;

namespace RunwayPanic.Audio
{
    /// <summary>
    /// Sounds every bird makes (on the bird base prefabs, so all species get it): wing flaps that speed up with flight
    /// speed (none while perched or diving with wings tucked), a whoosh at the start of a dive, a squawk when hit, a
    /// cry on death, and now and then its species call. The hawk's scream before a stoop is played by HawkBrain; the
    /// crow's funeral, theft and rage calls by CrowBrain — their idle calls here are rarer.
    /// </summary>
    [RequireComponent(typeof(BirdHealth))]
    public sealed class BirdSounds : MonoBehaviour
    {
        [Header("Flight")]
        [SerializeField] private SoundCue flap;
        [Tooltip("Flaps per second hovering / at full speed.")]
        [SerializeField] private Vector2 flapRate = new Vector2(2.5f, 5f);
        [SerializeField] private SoundCue dive;
        [Tooltip("Falling faster than this (m/s) counts as a dive: wings tucked, one whoosh.")]
        [SerializeField, Min(1f)] private float diveSpeed = 6f;

        [Header("Health")]
        [SerializeField] private SoundCue hit;
        [SerializeField] private SoundCue death;

        [Header("Species calls (idle)")]
        [SerializeField] private SoundCue gullCall;
        [SerializeField] private SoundCue pigeonCall;
        [SerializeField] private SoundCue crowCall;
        [Tooltip("Seconds between idle calls (random in this range); crows call half as often.")]
        [SerializeField] private Vector2 callInterval = new Vector2(6f, 14f);

        private BirdHealth health;
        private IBirdMotor motor;
        private SoundCue call;
        private float callRateScale = 1f;
        private float nextFlapTime;
        private float nextCallTime;
        private bool diving;

        private void Awake()
        {
            health = GetComponent<BirdHealth>();
            motor = GetComponent<IBirdMotor>();
        }

        private void Start()
        {
            BirdSpecies species = health.Species != null ? health.Species.Species : BirdSpecies.Pigeon;
            switch (species)
            {
                case BirdSpecies.Gull: call = gullCall; break;
                case BirdSpecies.Pigeon: call = pigeonCall; break;
                case BirdSpecies.Crow: call = crowCall; callRateScale = 2f; break;
                default: call = null; break; // hawk: its scream is HawkBrain's tell
            }

            nextCallTime = Time.time + Random.Range(callInterval.x, callInterval.y) * callRateScale;
        }

        private void OnEnable()
        {
            health.Damaged += HandleDamaged;
            health.Died += HandleDied;
        }

        private void OnDisable()
        {
            health.Damaged -= HandleDamaged;
            health.Died -= HandleDied;
        }

        private void Update()
        {
            if (!health.IsAlive || motor == null)
                return;

            Vector3 velocity = motor.Velocity;
            bool nowDiving = velocity.y < -diveSpeed;
            if (nowDiving && !diving)
                SoundPlayer.Play(dive, transform.position);
            diving = nowDiving;

            // Wing beats: faster the faster it flies; silent while perched or tucked in a dive.
            if (!motor.IsLanded && !diving && Time.time >= nextFlapTime)
            {
                float speed01 = motor.BaseSpeed > 0f ? Mathf.Clamp01(velocity.magnitude / motor.BaseSpeed) : 0.5f;
                nextFlapTime = Time.time + 1f / Mathf.Lerp(flapRate.x, flapRate.y, speed01);
                SoundPlayer.Play(flap, transform.position);
            }

            if (call != null && Time.time >= nextCallTime)
            {
                nextCallTime = Time.time + Random.Range(callInterval.x, callInterval.y) * callRateScale;
                SoundPlayer.Play(call, transform.position);
            }
        }

        private void HandleDamaged(float applied, float hpAfter)
        {
            if (hpAfter > 0f)
                SoundPlayer.Play(hit, transform.position);
        }

        private void HandleDied(BirdHealth bird) => SoundPlayer.Play(death, transform.position);
    }
}
