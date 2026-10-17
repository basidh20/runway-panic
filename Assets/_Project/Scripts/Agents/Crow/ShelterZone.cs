using System;
using RunwayPanic.Player;
using UnityEngine;

namespace RunwayPanic.Agents.Crow
{
    /// <summary>
    /// Trigger volume filling a shelter's interior. Knows whether the player is inside (and since when), so crows can
    /// hold a vigil at the exits marked with <see cref="BirdPoint"/>s of type ShelterExit and the same
    /// <see cref="Group"/>. Birds cannot path into shelters (the doorway is narrower than a grid cell), so this is how
    /// crows deal with a hiding player.
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public sealed class ShelterZone : MonoBehaviour
    {
        [Tooltip("Matches the 'group' of this shelter's ShelterExit markers, e.g. ShelterA.")]
        [SerializeField] private string group = "ShelterA";

        /// <summary>Raised when the player walks into any shelter zone.</summary>
        public static event Action<ShelterZone> PlayerEntered;

        /// <summary>Raised when the player leaves the shelter zone they were in.</summary>
        public static event Action<ShelterZone> PlayerExited;

        /// <summary>The zone the player is inside, or null.</summary>
        public static ShelterZone PlayerInside { get; private set; }

        /// <summary>Time.time when the player entered <see cref="PlayerInside"/>.</summary>
        public static float PlayerInsideSince { get; private set; }

        /// <summary>Exit-marker group of this shelter.</summary>
        public string Group => group;

        /// <summary>World centre of the zone.</summary>
        public Vector3 Centre => GetComponent<BoxCollider>().bounds.center;

        private void Reset()
        {
            GetComponent<BoxCollider>().isTrigger = true;
        }

        private void OnTriggerEnter(Collider other)
        {
            if (other.GetComponentInParent<PlayerHealth>() == null)
                return;

            PlayerInside = this;
            PlayerInsideSince = Time.time;
            PlayerEntered?.Invoke(this);
        }

        private void OnTriggerExit(Collider other)
        {
            if (PlayerInside != this || other.GetComponentInParent<PlayerHealth>() == null)
                return;

            PlayerInside = null;
            PlayerExited?.Invoke(this);
        }

        private void OnDisable()
        {
            if (PlayerInside != this)
                return;

            PlayerInside = null;
            PlayerExited?.Invoke(this);
        }

        private void OnDrawGizmos()
        {
            var box = GetComponent<BoxCollider>();
            Gizmos.color = new Color(0.2f, 0.6f, 1f, PlayerInside == this ? 0.35f : 0.12f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawCube(box.center, box.size);
        }

        // Statics survive Play mode when domain reload is off: start every session clean.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            PlayerEntered = null;
            PlayerExited = null;
            PlayerInside = null;
            PlayerInsideSince = 0f;
        }
    }
}
