using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace RunwayPanic.Agents
{
    /// <summary>What a <see cref="BirdPoint"/> marks.</summary>
    public enum BirdPointType
    {
        Spawn,
        Perch,
        Roost,
        Stash,
        FodSpawn,
        KettleCenter,
        ShelterExit
    }

    /// <summary>
    /// Scene marker for bird behaviour: spawn points, hawk perches, pigeon roosts, crow stashes, gull debris spots,
    /// the gull kettle centre and shelter exits. Place it exactly where the bird should be (for perches, on the surface).
    /// Brains find markers through <see cref="GetAll"/>, never by name or tag.
    /// </summary>
    public sealed class BirdPoint : MonoBehaviour
    {
        private static readonly List<BirdPoint> all = new List<BirdPoint>(64);

        [Tooltip("What this marker is for.")]
        [SerializeField] private BirdPointType type = BirdPointType.Perch;
        [Tooltip("Area around the point that counts as 'at the point', in metres (spawn scatter, landing spot size).")]
        [SerializeField, Min(0f)] private float radius = 1.5f;
        [Tooltip("Optional group name linking markers together, e.g. 'ShelterA' for both exits of one shelter.")]
        [SerializeField] private string group = "";

        /// <summary>What this marker is for.</summary>
        public BirdPointType Type => type;

        /// <summary>Radius of the marked area, in metres.</summary>
        public float Radius => radius;

        /// <summary>Optional group name (empty if none).</summary>
        public string Group => group;

        /// <summary>World position of the marker.</summary>
        public Vector3 Position => transform.position;

        /// <summary>Every enabled marker in the loaded scenes.</summary>
        public static IReadOnlyList<BirdPoint> All => all;

        /// <summary>Clears <paramref name="results"/> and fills it with every enabled marker of <paramref name="pointType"/>.</summary>
        public static void GetAll(BirdPointType pointType, List<BirdPoint> results)
        {
            results.Clear();
            for (int i = 0; i < all.Count; i++)
                if (all[i].type == pointType)
                    results.Add(all[i]);
        }

        private void OnEnable()
        {
            if (!all.Contains(this))
                all.Add(this);
        }

        private void OnDisable()
        {
            all.Remove(this);
        }

        // Statics survive Play mode when domain reload is off: start every session clean.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            all.Clear();
        }

        private static Color ColourOf(BirdPointType pointType)
        {
            switch (pointType)
            {
                case BirdPointType.Spawn: return new Color(0.2f, 0.9f, 0.2f);
                case BirdPointType.Perch: return new Color(1f, 0.55f, 0f);
                case BirdPointType.Roost: return new Color(0.6f, 0.6f, 1f);
                case BirdPointType.Stash: return new Color(0.6f, 0.2f, 0.8f);
                case BirdPointType.FodSpawn: return new Color(0.9f, 0.9f, 0.2f);
                case BirdPointType.KettleCenter: return new Color(0.2f, 0.9f, 0.9f);
                default: return new Color(1f, 0.3f, 0.5f);
            }
        }

        private void OnDrawGizmos()
        {
            Color colour = ColourOf(type);
            Gizmos.color = colour;
            Gizmos.DrawSphere(transform.position, 0.25f);
            Gizmos.color = new Color(colour.r, colour.g, colour.b, 0.35f);
            Gizmos.DrawWireSphere(transform.position, radius);

#if UNITY_EDITOR
            var style = new GUIStyle(EditorStyles.boldLabel) { normal = { textColor = colour } };
            string label = type == BirdPointType.KettleCenter ? "K" : type == BirdPointType.FodSpawn ? "F" : type.ToString().Substring(0, 1);
            Handles.Label(transform.position + Vector3.up * 0.5f, label, style);
#endif
        }
    }
}
