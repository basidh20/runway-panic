using RunwayPanic.Agents.Crow;
using RunwayPanic.Core;
using UnityEngine;

namespace RunwayPanic.Audio
{
    /// <summary>
    /// The game's background sound (on the GameManager prefab): a bed of distant jets and building hum, a light breeze
    /// that picks up a little as the waves go on, and now and then a distant event (a jet climbing out, the PA chime, a
    /// far gull, a fence rattling) placed somewhere around the listener. The music is <see cref="WaveMusic"/>.
    /// Inside a shelter (<see cref="ShelterZone"/>) or while paused everything is muffled and quieter, so the outside
    /// feels outside.
    /// </summary>
    public sealed class AmbienceLoop : MonoBehaviour
    {
        [SerializeField] private SoundCue ambience;
        [SerializeField] private SoundCue wind;
        [Tooltip("Wind volume in calm moments (menu, shop, early waves) and in the final wave.")]
        [SerializeField] private Vector2 windVolume = new Vector2(0.8f, 1f);

        [Header("Distant events")]
        [SerializeField] private SoundCue[] events;
        [Tooltip("Seconds between distant events (random in this range).")]
        [SerializeField] private Vector2 eventInterval = new Vector2(40f, 90f);
        [Tooltip("How far from the listener (m) a placed event sounds.")]
        [SerializeField] private Vector2 eventDistance = new Vector2(25f, 60f);

        [Header("Muffling")]
        [Tooltip("Volume and low-pass cut-off (Hz) while the player is inside a shelter or the game is paused.")]
        [SerializeField, Range(0f, 1f)] private float muffledVolume = 0.45f;
        [SerializeField, Min(100f)] private float muffledCutoff = 700f;
        [Tooltip("Seconds to blend in and out of muffled.")]
        [SerializeField, Min(0.05f)] private float blendTime = 0.6f;

        private Layer bed;
        private Layer gusts;
        private float muffle;
        private float windLevel;
        private float nextEventTime;
        private int lastEvent = -1;

        private void Start() => nextEventTime = Time.unscaledTime + Random.Range(eventInterval.x, eventInterval.y) * 0.5f;

        private void Update()
        {
            GameManager game = GameManager.Instance;
            bool paused = game != null && game.CurrentState == GameState.Paused;
            bool sheltered = ShelterZone.PlayerInside != null;
            muffle = Mathf.MoveTowards(muffle, paused || sheltered ? 1f : 0f, Time.unscaledDeltaTime / blendTime);
            float muffledScale = Mathf.Lerp(1f, muffledVolume, muffle);
            float cutoff = Mathf.Lerp(22000f, muffledCutoff, muffle);

            // The weather turns as the waves go on: wind rises from calm to its strongest by the final wave.
            float storm = game != null && game.CurrentState == GameState.Playing && game.TotalWaves > 1
                ? Mathf.Clamp01((game.CurrentWave - 1f) / (game.TotalWaves - 1f))
                : 0f;
            windLevel = Mathf.MoveTowards(windLevel, Mathf.Lerp(windVolume.x, windVolume.y, storm), Time.unscaledDeltaTime * 0.1f);

            bed.Update(ambience, muffledScale, cutoff);
            gusts.Update(wind, muffledScale * windLevel, cutoff);

            if (!paused && Time.unscaledTime >= nextEventTime)
            {
                nextEventTime = Time.unscaledTime + Random.Range(eventInterval.x, eventInterval.y);
                PlayEvent(muffledScale * (1f - 0.5f * muffle));
            }
        }

        private void OnDisable()
        {
            bed.Stop();
            gusts.Stop();
        }

        private void PlayEvent(float volumeScale)
        {
            if (events == null || events.Length == 0)
                return;

            // Never the same event twice in a row.
            int index = Random.Range(0, events.Length);
            if (events.Length > 1 && index == lastEvent)
                index = (index + 1) % events.Length;
            lastEvent = index;

            // Placed cues sound from a random direction around the listener; 2D cues ignore the position.
            Vector3 centre = Camera.main != null ? Camera.main.transform.position : transform.position;
            Vector2 direction = Random.insideUnitCircle.normalized;
            float distance = Random.Range(eventDistance.x, eventDistance.y);
            Vector3 position = centre + new Vector3(direction.x, 0f, direction.y) * distance + Vector3.up * Random.Range(2f, 12f);
            SoundPlayer.Play(events[index], position, volumeScale);
        }

        /// <summary>One looping 2D layer with its own muffling filter; restarts itself if its source is lost.</summary>
        private struct Layer
        {
            private SoundLoop loop;
            private AudioLowPassFilter filter;

            public void Update(SoundCue cue, float volume, float cutoff)
            {
                if (loop == null || !loop.IsPlaying)
                {
                    loop = SoundPlayer.StartLoop(cue);
                    filter = null;
                    if (loop == null)
                        return;
                }

                loop.VolumeScale = volume;
                if (filter == null && !loop.AudioSource.TryGetComponent(out filter))
                    filter = loop.AudioSource.gameObject.AddComponent<AudioLowPassFilter>();
                filter.enabled = cutoff < 21999f;
                filter.cutoffFrequency = cutoff;
            }

            public void Stop()
            {
                if (loop != null)
                    loop.Stop();
                loop = null;
                filter = null;
            }
        }
    }
}
