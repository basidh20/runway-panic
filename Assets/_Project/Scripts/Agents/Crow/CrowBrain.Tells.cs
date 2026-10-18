using UnityEngine;

namespace RunwayPanic.Agents.Crow
{
    /// <summary>
    /// Tells: every crow surprise warns the player first, so it reads as intelligence, not as a bug.
    /// Funeral crows caw in a distinct repeating pattern, a thief calls out the moment it grabs a pickup, and an
    /// enraged crow gives a rage call. A hit on the player sounds like a crow strike (wings, beak, "kraa!"), not only
    /// the player's own hurt sound. Clips are optional: with none assigned the crow is simply silent.
    /// (The other theft tell — the pickup visible in the beak — is in CrowBrain.Theft.cs.)
    /// </summary>
    public sealed partial class CrowBrain
    {
        private const float PitchVariation = 0.08f;

        [Header("Tells (audio, optional)")]
        [Tooltip("Single caw. Played in the funeral pattern while circling a dead crow.")]
        [SerializeField] private AudioClip funeralCawClip;
        [Tooltip("Call played when the crow grabs a pickup and starts carrying it away.")]
        [SerializeField] private AudioClip theftCallClip;
        [Tooltip("Call played when the crow becomes enraged (after a funeral or a vigil).")]
        [SerializeField] private AudioClip rageCallClip;
        [Tooltip("Wing beats, a beak strike and a harsh \"kraa!\": played the moment the crow's attack hits the player.")]
        [SerializeField] private AudioClip strikeClip;
        [Tooltip("Volume of all crow calls.")]
        [SerializeField, Range(0f, 1f)] private float callVolume = 0.8f;
        [Tooltip("Caws in one funeral pattern.")]
        [SerializeField, Range(1, 6)] private int funeralCawsPerPattern = 3;
        [Tooltip("Seconds between caws inside a funeral pattern.")]
        [SerializeField, Min(0.05f)] private float funeralCawSpacing = 0.4f;
        [Tooltip("Seconds from the start of one funeral pattern to the next.")]
        [SerializeField, Min(0.2f)] private float funeralPatternInterval = 1.8f;
        [Tooltip("Distance in metres at which calls start to get quieter.")]
        [SerializeField, Min(0.5f)] private float callMinDistance = 3f;
        [Tooltip("Distance in metres beyond which calls can no longer be heard.")]
        [SerializeField, Min(1f)] private float callMaxDistance = 45f;

        private AudioSource callSource;
        private float nextFuneralCawTime;
        private int funeralCawsLeft;

        /// <summary>Calls played so far (for tests and the debug overlay).</summary>
        public int CallsPlayed { get; private set; }

        partial void OnStateEntered(CrowState entered)
        {
            switch (entered)
            {
                case CrowState.Funeral:
                    funeralCawsLeft = 0;
                    nextFuneralCawTime = Time.time;
                    break;
                case CrowState.CarryToStash:
                    PlayCall(theftCallClip);
                    break;
                case CrowState.Enraged:
                    PlayCall(rageCallClip);
                    break;
            }
        }

        partial void TickTells()
        {
            if (state != CrowState.Funeral || funeralCawClip == null || Time.time < nextFuneralCawTime)
                return;

            // caw · caw · caw ........ caw · caw · caw: a rhythm no other crow behaviour uses.
            if (funeralCawsLeft == 0)
                funeralCawsLeft = funeralCawsPerPattern;

            PlayCall(funeralCawClip);
            funeralCawsLeft--;
            nextFuneralCawTime += funeralCawsLeft > 0
                ? funeralCawSpacing
                : funeralPatternInterval - funeralCawSpacing * (funeralCawsPerPattern - 1);
        }

        /// <summary>The crow's attack just hit the player.</summary>
        private void PlayStrike() => PlayCall(strikeClip);

        private void PlayCall(AudioClip clip)
        {
            if (clip == null)
                return;

            if (callSource == null)
            {
                // Created on first use, so crows without clips carry no AudioSource at all.
                callSource = gameObject.AddComponent<AudioSource>();
                callSource.playOnAwake = false;
                callSource.spatialBlend = 1f;
                callSource.rolloffMode = AudioRolloffMode.Linear;
                callSource.minDistance = callMinDistance;
                callSource.maxDistance = Mathf.Max(callMinDistance, callMaxDistance);
            }

            // Slightly different pitch per call, so several crows never sound like one.
            callSource.pitch = 1f + Random.Range(-PitchVariation, PitchVariation);
            callSource.PlayOneShot(clip, callVolume);
            CallsPlayed++;
        }
    }
}
