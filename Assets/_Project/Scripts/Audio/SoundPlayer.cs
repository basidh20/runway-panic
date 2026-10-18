using System.Collections.Generic;
using UnityEngine;

namespace RunwayPanic.Audio
{
    /// <summary>
    /// Plays <see cref="SoundCue"/>s: <c>SoundPlayer.Play(cue, position)</c> for one-shots, <see cref="StartLoop"/> for
    /// sounds that last (shield hum, barricade scrape, wind). Sources are pooled (no per-shot allocation) and each cue
    /// caps its own overlapping copies. Volumes are per <see cref="SoundCategory"/> (a settings menu can call
    /// <see cref="SetVolume"/>). A cue without clips plays nothing — sounds can be added later without code changes.
    /// </summary>
    public static class SoundPlayer
    {
        private static readonly float[] volumes = { 1f, 1f, 1f, 1f, 1f, 1f, 1f };
        private static SoundPool pool;

        /// <summary>Overall volume, applied on top of the category volumes.</summary>
        public static float MasterVolume { get; private set; } = 1f;

        /// <summary>Sounds started since entering Play mode (for tests and the debug overlay).</summary>
        public static int PlayCount { get; private set; }

        /// <summary>Volume of one category (0..1).</summary>
        public static float GetVolume(SoundCategory category) => volumes[(int)category];

        /// <summary>Sets one category's volume (0..1); playing loops follow at once.</summary>
        public static void SetVolume(SoundCategory category, float volume)
        {
            volumes[(int)category] = Mathf.Clamp01(volume);
            if (pool != null)
                pool.RefreshLoops();
        }

        /// <summary>Sets the overall volume (0..1).</summary>
        public static void SetMasterVolume(float volume)
        {
            MasterVolume = Mathf.Clamp01(volume);
            if (pool != null)
                pool.RefreshLoops();
        }

        /// <summary>Plays <paramref name="cue"/> once at <paramref name="position"/> (ignored for 2D cues).</summary>
        public static AudioSource Play(SoundCue cue, Vector3 position, float volumeScale = 1f)
        {
            if (cue == null || !cue.HasClips || !Application.isPlaying)
                return null;

            AudioSource source = Pool.Rent(cue);
            if (source == null)
                return null;

            Configure(source, cue, cue.PickClip(), volumeScale, loop: false);
            source.transform.position = position;
            source.Play();
            PlayCount++;
            return source;
        }

        /// <summary>Plays <paramref name="cue"/> once, not placed in the world (UI, stingers).</summary>
        public static AudioSource Play2D(SoundCue cue, float volumeScale = 1f) => Play(cue, Vector3.zero, volumeScale);

        /// <summary>
        /// Starts <paramref name="cue"/> looping, following <paramref name="follow"/> (or fixed, if null). Keep the
        /// returned handle and call <see cref="SoundLoop.Stop"/>.
        /// </summary>
        public static SoundLoop StartLoop(SoundCue cue, Transform follow = null, float volumeScale = 1f)
        {
            if (cue == null || !cue.HasClips || !Application.isPlaying)
                return null;

            AudioSource source = Pool.RentForLoop();
            if (source == null)
                return null;

            var loop = new SoundLoop(source, cue, follow, volumeScale);
            Configure(source, cue, cue.PickClip(), volumeScale, loop: true);
            if (follow != null)
                source.transform.position = follow.position;
            source.Play();
            Pool.TrackLoop(loop);
            PlayCount++;
            return loop;
        }

        internal static float VolumeFor(SoundCue cue, float scale) =>
            cue.Volume * scale * volumes[(int)cue.Category] * MasterVolume;

        private static void Configure(AudioSource source, SoundCue cue, AudioClip clip, float scale, bool loop)
        {
            source.clip = clip;
            source.loop = loop;
            source.volume = VolumeFor(cue, scale);
            source.pitch = loop ? 1f : cue.PickPitch();
            source.spatialBlend = cue.Spatial ? 1f : 0f;
            source.rolloffMode = AudioRolloffMode.Logarithmic;
            source.minDistance = cue.MinDistance;
            source.maxDistance = cue.MaxDistance;
            source.dopplerLevel = 0f;
            source.playOnAwake = false;
        }

        private static SoundPool Pool
        {
            get
            {
                if (pool == null)
                {
                    var go = new GameObject("[SoundPlayer]");
                    Object.DontDestroyOnLoad(go);
                    pool = go.AddComponent<SoundPool>();
                }

                return pool;
            }
        }

        // Statics survive Play mode when domain reload is off: start every session clean.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            pool = null;
            PlayCount = 0;
            MasterVolume = 1f;
            for (int i = 0; i < volumes.Length; i++)
                volumes[i] = 1f;
        }
    }

    /// <summary>A playing loop started by <see cref="SoundPlayer.StartLoop"/>.</summary>
    public sealed class SoundLoop
    {
        internal readonly AudioSource Source;
        internal readonly SoundCue Cue;
        internal readonly Transform Follow;
        private float volumeScale;

        internal SoundLoop(AudioSource source, SoundCue cue, Transform follow, float volumeScale)
        {
            Source = source;
            Cue = cue;
            Follow = follow;
            this.volumeScale = volumeScale;
        }

        /// <summary>False once stopped.</summary>
        public bool IsPlaying { get; internal set; } = true;

        /// <summary>Extra volume factor (fades, distance-free mixing).</summary>
        public float VolumeScale
        {
            get => volumeScale;
            set
            {
                volumeScale = Mathf.Max(0f, value);
                Refresh();
            }
        }

        /// <summary>Playback pitch (1 = normal).</summary>
        public float Pitch
        {
            get => Source != null ? Source.pitch : 1f;
            set { if (Source != null) Source.pitch = value; }
        }

        /// <summary>The loop's AudioSource, e.g. to add a filter. Do not destroy it.</summary>
        public AudioSource AudioSource => Source;

        /// <summary>Stops the loop and returns its source to the pool.</summary>
        public void Stop()
        {
            if (!IsPlaying)
                return;
            IsPlaying = false;
            if (Source != null)
                Source.Stop();
        }

        internal void Refresh()
        {
            if (IsPlaying && Source != null)
                Source.volume = SoundPlayer.VolumeFor(Cue, volumeScale);
        }
    }

    /// <summary>The pooled AudioSources behind <see cref="SoundPlayer"/>.</summary>
    internal sealed class SoundPool : MonoBehaviour
    {
        private const int MaxSources = 48;

        private readonly List<AudioSource> free = new List<AudioSource>(MaxSources);
        private readonly List<(AudioSource source, SoundCue cue)> oneShots = new List<(AudioSource, SoundCue)>(MaxSources);
        private readonly List<SoundLoop> loops = new List<SoundLoop>(8);
        private readonly Dictionary<SoundCue, int> playing = new Dictionary<SoundCue, int>();
        private int created;

        public AudioSource Rent(SoundCue cue)
        {
            playing.TryGetValue(cue, out int count);
            if (count >= cue.MaxInstances)
                return null; // enough copies already: skip rather than flood the mix

            AudioSource source = Take();
            if (source == null)
                return null;

            playing[cue] = count + 1;
            oneShots.Add((source, cue));
            return source;
        }

        public AudioSource RentForLoop() => Take();

        public void TrackLoop(SoundLoop loop) => loops.Add(loop);

        public void RefreshLoops()
        {
            foreach (SoundLoop loop in loops)
                loop.Refresh();
        }

        private AudioSource Take()
        {
            if (free.Count > 0)
            {
                AudioSource reused = free[free.Count - 1];
                free.RemoveAt(free.Count - 1);
                reused.gameObject.SetActive(true);
                return reused;
            }

            if (created >= MaxSources)
                return null;

            var go = new GameObject("Source" + created++);
            go.transform.SetParent(transform, false);
            return go.AddComponent<AudioSource>();
        }

        private void Return(AudioSource source)
        {
            source.Stop();
            source.clip = null;
            // A loop may have been muffled (ambience while paused or in a shelter): the next user starts clean.
            if (source.TryGetComponent(out AudioLowPassFilter filter))
                filter.enabled = false;
            source.gameObject.SetActive(false);
            free.Add(source);
        }

        private void Update()
        {
            for (int i = oneShots.Count - 1; i >= 0; i--)
            {
                (AudioSource source, SoundCue cue) = oneShots[i];
                if (source.isPlaying)
                    continue;

                oneShots.RemoveAt(i);
                playing[cue] = Mathf.Max(0, playing[cue] - 1);
                Return(source);
            }

            for (int i = loops.Count - 1; i >= 0; i--)
            {
                SoundLoop loop = loops[i];
                if (!loop.IsPlaying)
                {
                    loops.RemoveAt(i);
                    Return(loop.Source);
                    continue;
                }

                if (loop.Follow != null)
                    loop.Source.transform.position = loop.Follow.position;
            }
        }
    }
}
