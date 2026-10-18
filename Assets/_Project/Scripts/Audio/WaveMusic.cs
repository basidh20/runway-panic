using RunwayPanic.Core;
using UnityEngine;

namespace RunwayPanic.Audio
{
    /// <summary>
    /// Music that follows the birds' waves (on the GameManager prefab). A soft pad always plays; while a wave is on,
    /// tension layers fade in on top of it, one more each wave — a string ostinato with a heartbeat drum, then war
    /// drums, then full drums and brass, and in the final wave a racing heartbeat over grinding low strings — louder the
    /// harder the wave, and fade out again when it is cleared. In the late waves the music also speeds up the longer the
    /// wave lasts, like a rising heart rate, and settles back when it ends. All layers share key, tempo and length and
    /// play in step, so any mix fits. The music drops out for the game-over and victory stingers and is muffled while
    /// paused.
    /// </summary>
    public sealed class WaveMusic : MonoBehaviour
    {
        [SerializeField] private SoundCue pad;
        [Tooltip("Tension layers, lightest first. Their loudness is set on each cue.")]
        [SerializeField] private SoundCue[] tension;
        [Tooltip("Wave progress (0 = first wave, 1 = final wave) from which each tension layer joins during a wave.")]
        [SerializeField] private float[] tensionFrom = { 0f, 0.3f, 0.6f, 1f };
        [Tooltip("Seconds for a layer to fade in / out.")]
        [SerializeField, Min(0.1f)] private float fadeInTime = 1.5f;
        [SerializeField, Min(0.1f)] private float fadeOutTime = 5f;
        [Tooltip("Seconds for all music to drop out at game over / victory, so the stinger stands clear.")]
        [SerializeField, Min(0.1f)] private float endFadeTime = 0.8f;

        [Tooltip("Loudness of the tension layers in the first wave and in the final wave (in between: by wave).")]
        [SerializeField] private Vector2 waveLoudness = new Vector2(1f, 1.5f);

        [Header("Rising heart rate")]
        [Tooltip("Wave progress from which the music speeds up as the wave goes on.")]
        [SerializeField, Range(0f, 1f)] private float rushFrom = 0.6f;
        [Tooltip("Top playback speed (1.08 = 8% faster and a little higher), reached after Rush Time seconds of a wave.")]
        [SerializeField, Range(1f, 1.2f)] private float maxRush = 1.08f;
        [SerializeField, Min(1f)] private float rushTime = 90f;

        [Header("Paused")]
        [Tooltip("Music volume and low-pass cut-off (Hz) while paused.")]
        [SerializeField, Range(0f, 1f)] private float pausedVolume = 0.5f;
        [SerializeField, Min(100f)] private float pausedCutoff = 900f;

        private Stem[] stems;
        private float pause;
        private float waveTime;
        private float rush = 1f;
        private float loudness = 1f;

        /// <summary>How many tension layers are currently wanted (0 between waves).</summary>
        public int TensionLevel { get; private set; }

        /// <summary>Current music playback speed (1 = normal; above 1 while the heart rate rises).</summary>
        public float Rush => rush;

        private void Awake()
        {
            int count = 1 + (tension != null ? tension.Length : 0);
            stems = new Stem[count];
            stems[0] = new Stem(pad);
            for (int i = 1; i < count; i++)
                stems[i] = new Stem(tension[i - 1]);
        }

        private void Update()
        {
            GameManager game = GameManager.Instance;
            GameState state = game != null ? game.CurrentState : GameState.MainMenu;
            bool ended = state == GameState.GameOver || state == GameState.Victory;
            bool inWave = game != null && game.IsWaveActive && (state == GameState.Playing || state == GameState.Paused);

            TensionLevel = 0;
            float progress = 0f;
            if (inWave)
            {
                progress = game.TotalWaves > 1 ? Mathf.Clamp01((game.CurrentWave - 1f) / (game.TotalWaves - 1f)) : 1f;
                for (int i = 0; i < stems.Length - 1; i++)
                    if (progress >= (i < tensionFrom.Length ? tensionFrom[i] : 1f) || game.IsFinalWave)
                        TensionLevel = i + 1;
            }

            // Heart rate: in the late waves the music creeps faster the longer the wave lasts; between waves it settles.
            waveTime = inWave ? waveTime + Time.deltaTime : 0f;
            float rushTarget = inWave && progress >= rushFrom ? Mathf.Lerp(1f, maxRush, Mathf.Clamp01(waveTime / rushTime)) : 1f;
            rush = Mathf.MoveTowards(rush, rushTarget, Time.unscaledDeltaTime * 0.02f);

            // Harder waves play their tension louder (kept while a cleared wave fades out).
            if (inWave)
                loudness = Mathf.Lerp(waveLoudness.x, waveLoudness.y, progress);

            pause = Mathf.MoveTowards(pause, state == GameState.Paused ? 1f : 0f, Time.unscaledDeltaTime / 0.4f);
            float volume = Mathf.Lerp(1f, pausedVolume, pause);
            float cutoff = Mathf.Lerp(22000f, pausedCutoff, pause);

            AudioSource reference = null;
            foreach (Stem stem in stems)
                if (stem.IsPlaying)
                {
                    reference = stem.Source;
                    break;
                }

            for (int i = 0; i < stems.Length; i++)
            {
                bool wanted = !ended && (i == 0 || i <= TensionLevel);
                stems[i].Fade(wanted ? 1f : 0f, wanted ? fadeInTime : ended ? endFadeTime : fadeOutTime);
                stems[i].Update(i == 0 ? volume : volume * loudness, cutoff, rush, reference);
            }
        }

        private void OnDisable()
        {
            if (stems == null)
                return;
            foreach (Stem stem in stems)
                stem.Stop();
        }

        /// <summary>One music layer: a loop kept running (silent when not wanted) so it stays in step with the rest.</summary>
        private sealed class Stem
        {
            private readonly SoundCue cue;
            private SoundLoop loop;
            private AudioLowPassFilter filter;
            private float level;

            public Stem(SoundCue cue) => this.cue = cue;

            public bool IsPlaying => loop != null && loop.IsPlaying && loop.AudioSource != null;
            public AudioSource Source => loop.AudioSource;

            public void Fade(float target, float seconds) =>
                level = Mathf.MoveTowards(level, target, Time.unscaledDeltaTime / seconds);

            public void Update(float volume, float cutoff, float pitch, AudioSource reference)
            {
                if (!IsPlaying)
                {
                    loop = SoundPlayer.StartLoop(cue, null, 0f);
                    filter = null;
                    if (loop == null)
                        return;

                    // Join in step with the layers already playing.
                    AudioClip clip = loop.AudioSource.clip;
                    if (reference != null && reference.clip != null && clip != null && reference.isPlaying)
                        loop.AudioSource.timeSamples = reference.timeSamples % clip.samples;
                }

                loop.VolumeScale = volume * level;
                loop.Pitch = pitch; // the same for every layer, so they stay in step
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
