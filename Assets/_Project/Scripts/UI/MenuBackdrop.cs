using UnityEngine;
using UnityEngine.UI;

namespace RunwayPanic.UI
{
    /// <summary>
    /// Brings the main menu's backdrop to life: bird silhouettes drift across the dusk sky, flapping, and wrap
    /// around; the runway edge lights pulse in a wave toward the horizon; the title floats gently. Unscaled time.
    /// </summary>
    public sealed class MenuBackdrop : MonoBehaviour
    {
        [SerializeField] private RectTransform[] birds;
        [Tooltip("Pixels per second (random per bird in this range).")]
        [SerializeField] private Vector2 birdSpeed = new Vector2(40f, 110f);
        [SerializeField] private Image[] runwayLights;
        [SerializeField] private RectTransform title;

        private float[] speeds;
        private float[] phases;
        private Vector2 titleBase;
        private float width;

        private void Start()
        {
            speeds = new float[birds.Length];
            phases = new float[birds.Length];
            for (int i = 0; i < birds.Length; i++)
            {
                speeds[i] = Random.Range(birdSpeed.x, birdSpeed.y);
                phases[i] = Random.value * 10f;
            }

            if (title != null)
                titleBase = title.anchoredPosition;
            width = ((RectTransform)transform).rect.width;
        }

        private void Update()
        {
            float t = Time.unscaledTime;
            float half = width * 0.5f + 80f;

            for (int i = 0; i < birds.Length; i++)
            {
                RectTransform bird = birds[i];
                Vector2 p = bird.anchoredPosition;
                p.x += speeds[i] * Time.unscaledDeltaTime;
                p.y += Mathf.Sin(t * 0.7f + phases[i]) * 6f * Time.unscaledDeltaTime;
                if (p.x > half)
                    p.x = -half;
                bird.anchoredPosition = p;

                // Wing beat: squash vertically, faster for faster birds.
                float flap = 0.55f + 0.45f * Mathf.Abs(Mathf.Sin(t * speeds[i] * 0.06f + phases[i]));
                float size = bird.localScale.x;
                bird.localScale = new Vector3(size, size * flap, 1f);
            }

            for (int i = 0; i < runwayLights.Length; i++)
            {
                float pulse = 0.35f + 0.65f * Mathf.Pow(Mathf.Max(0f, Mathf.Sin(t * 2.2f - i * 0.5f)), 4f);
                Color c = runwayLights[i].color;
                c.a = pulse;
                runwayLights[i].color = c;
            }

            if (title != null)
                title.anchoredPosition = titleBase + new Vector2(0f, Mathf.Sin(t * 0.8f) * 6f);
        }
    }
}
