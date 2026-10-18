using UnityEngine;

namespace RunwayPanic.Audio
{
    /// <summary>
    /// One game sound: a few clip variants and how to play them (volume, pitch spread, 2D/3D, distance, how many may
    /// overlap). Variation keeps repeated sounds — gunshots, footsteps — from sounding mechanical. Swap a cue's clips
    /// for recorded ones any time; no code changes.
    /// </summary>
    [CreateAssetMenu(fileName = "SC_New", menuName = "Runway Panic/Audio/Sound Cue")]
    public sealed class SoundCue : ScriptableObject
    {
        [Tooltip("Variants; one is picked at random each time (never the same one twice in a row).")]
        [SerializeField] private AudioClip[] clips = new AudioClip[0];
        [SerializeField] private SoundCategory category = SoundCategory.World;
        [SerializeField, Range(0f, 1f)] private float volume = 1f;
        [Tooltip("Random pitch range per play (1 = unchanged).")]
        [SerializeField] private Vector2 pitchRange = new Vector2(0.95f, 1.05f);
        [Tooltip("3D: placed in the world and quieter with distance. Off: always heard the same (UI, ambience beds).")]
        [SerializeField] private bool spatial = true;
        [Tooltip("3D: full volume within this distance (m).")]
        [SerializeField, Min(0.1f)] private float minDistance = 3f;
        [Tooltip("3D: silent beyond this distance (m).")]
        [SerializeField, Min(1f)] private float maxDistance = 50f;
        [Tooltip("Most copies of this cue playing at once; more are skipped (a fast gun never floods the mix).")]
        [SerializeField, Min(1)] private int maxInstances = 4;

        private int lastIndex = -1;

        public SoundCategory Category => category;
        public float Volume => volume;
        public bool Spatial => spatial;
        public float MinDistance => minDistance;
        public float MaxDistance => maxDistance;
        public int MaxInstances => maxInstances;
        public bool HasClips => clips != null && clips.Length > 0;

        /// <summary>A random clip, avoiding the one picked last time when there is a choice.</summary>
        public AudioClip PickClip()
        {
            if (!HasClips)
                return null;
            if (clips.Length == 1)
                return clips[0];

            int index = Random.Range(0, clips.Length - 1);
            if (index >= lastIndex && lastIndex >= 0)
                index++;
            lastIndex = index;
            return clips[index];
        }

        /// <summary>A random pitch within the cue's range.</summary>
        public float PickPitch() => Random.Range(pitchRange.x, pitchRange.y);
    }
}
