using RunwayPanic.Data;
using UnityEngine;

namespace RunwayPanic.Agents
{
    /// <summary>
    /// The one component the IS scripts talk to on a bird. It holds the bird's species stats and motion profile and
    /// hands out the other bird components, so AI code needs a single GetComponent&lt;BirdAgent&gt;().
    /// Lives on the prefab root (P_Bird_Base and P_Bird_Hawk); the species variants set their own assets.
    /// Adding it adds everything a bird needs (RequireComponent), so a prefab cannot end up half set up.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody), typeof(BirdPathFollower), typeof(BirdAnimatorDriver))]
    [RequireComponent(typeof(BirdHealth), typeof(BirdContactAttack))]
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

        // Available from Start on (set in Awake).

        /// <summary>Moves the bird along IS paths: SetPath, ClearPath, PathCompleted, WaypointReached.</summary>
        public BirdPathFollower Follower { get; private set; }

        /// <summary>HP and death: IsAlive, Died.</summary>
        public BirdHealth Health { get; private set; }

        /// <summary>Range and cooldown checked melee attack on the player: TryAttack, IsPlayerInRange.</summary>
        public BirdContactAttack Attack { get; private set; }

        /// <summary>Animator parameters and one-shot animations.</summary>
        public BirdAnimatorDriver AnimatorDriver { get; private set; }

        private void Awake()
        {
            Follower = GetComponent<BirdPathFollower>();
            Health = GetComponent<BirdHealth>();
            Attack = GetComponent<BirdContactAttack>();
            AnimatorDriver = GetComponent<BirdAnimatorDriver>();

            if (species == null)
                Debug.LogWarning($"[Agents] {name} has no BirdSpeciesData: it uses fallback HP and speed.", this);
        }
    }
}
