using RunwayPanic.Data;
using UnityEngine;

namespace RunwayPanic.Agents
{
    /// <summary>
    /// The one component the IS scripts talk to on a bird. It holds the bird's species stats and motion profile and
    /// hands out the other bird components, so AI code needs a single GetComponent&lt;BirdAgent&gt;().
    /// Lives on the prefab root (P_Bird_Base and P_Bird_Hawk); the species variants set their own assets.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BirdPathFollower))]
    public sealed class BirdAgent : MonoBehaviour
    {
        [Tooltip("Gameplay stats (HP, speed, attack, coins). Empty only on P_Bird_Base, which is never spawned itself.")]
        [SerializeField] private BirdSpeciesData species;
        [Tooltip("How this species flies: turn rate, banking, acceleration, flap speed.")]
        [SerializeField] private BirdMotionProfile motionProfile;

        // Shared stand-in for a bird without a profile, so a missing asset gives default motion instead of errors.
        private static BirdMotionProfile defaultProfile;

        public BirdSpeciesData Species => species;

        /// <summary>The bird's motion profile, or default values when none is assigned.</summary>
        public BirdMotionProfile Motion
        {
            get
            {
                if (motionProfile != null)
                    return motionProfile;

                if (defaultProfile == null)
                {
                    defaultProfile = ScriptableObject.CreateInstance<BirdMotionProfile>();
                    defaultProfile.hideFlags = HideFlags.DontSave;
                }

                return defaultProfile;
            }
        }

        /// <summary>Moves the bird along IS paths. Available from Start on.</summary>
        public BirdPathFollower Follower { get; private set; }

        private void Awake()
        {
            Follower = GetComponent<BirdPathFollower>();

            if (species == null)
                Debug.LogWarning($"[Agents] {name} has no BirdSpeciesData: it uses fallback HP and speed.", this);
        }
    }
}
