using UnityEngine;

namespace RunwayPanic.Weapons
{
    /// <summary>
    /// A one-shot effect (P_RocketExplosion, P_RocketLaunch): its particle systems play once on spawn, its light
    /// flashes and fades out, and the object removes itself when the last smoke has gone.
    /// </summary>
    public sealed class VfxBurst : MonoBehaviour
    {
        [Tooltip("Optional flash; starts at its set intensity and fades to nothing.")]
        [SerializeField] private Light flash;
        [SerializeField, Min(0.01f)] private float flashFade = 0.4f;
        [Tooltip("Seconds until the object is removed (longer than the longest particle).")]
        [SerializeField, Min(0.1f)] private float lifetime = 5f;

        private float startTime;
        private float flashIntensity;

        private void Awake()
        {
            startTime = Time.time;
            if (flash != null)
                flashIntensity = flash.intensity;
            Destroy(gameObject, lifetime);
        }

        private void Update()
        {
            if (flash == null || !flash.enabled)
                return;

            float t = (Time.time - startTime) / flashFade;
            if (t >= 1f)
            {
                flash.enabled = false;
                return;
            }

            flash.intensity = flashIntensity * (1f - t) * (1f - t);
        }
    }
}
