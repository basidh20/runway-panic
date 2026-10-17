using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RunwayPanic.Agents.Testing
{
    /// <summary>
    /// Stand-in for the IS pathfinding until it exists: feeds one bird an A*-style grid path (one point per grid cell,
    /// right-angle corners) around a closed route, and re-plans every few seconds onto a slightly different route so
    /// mid-flight path changes can be seen. The route comes from this object's children (corner points) or, with no
    /// children, a generated zigzag around this object.
    /// Keys: H damages the bird, K kills it, R brings it back to the start.
    /// </summary>
    public sealed class TestPathFeeder : MonoBehaviour
    {
        [Tooltip("The bird to drive (a scene instance).")]
        [SerializeField] private BirdAgent bird;
        [Tooltip("Grid cell size in metres: the route is sent as one waypoint per cell, like an A* path.")]
        [SerializeField, Min(0.25f)] private float cellSize = 2f;

        [Header("Generated zigzag (used when this object has no children)")]
        [Tooltip("Number of zigzag legs.")]
        [SerializeField, Min(2)] private int zigzagLegs = 4;
        [Tooltip("Cells per zigzag leg.")]
        [SerializeField, Min(1)] private int legCells = 4;
        [Tooltip("Cells the return leg climbs above the zigzag, to test pitch.")]
        [SerializeField, Min(0)] private int returnClimbCells = 1;

        [Header("Re-planning")]
        [Tooltip("Seconds between re-plans. 0 = only when the bird finishes the route.")]
        [SerializeField, Min(0f)] private float repathInterval = 5f;
        [Tooltip("Cells the far corners move on every other re-plan, so each new path differs from the last.")]
        [SerializeField] private int repathShiftCells = 1;

        [Header("Test keys")]
        [SerializeField] private bool respondToKeys = true;
        [SerializeField, Min(0f)] private float damagePerPress = 10f;

        private readonly List<Vector3> corners = new List<Vector3>(16);
        private readonly List<Vector3> route = new List<Vector3>(128);
        private readonly List<Vector3> pathBuffer = new List<Vector3>(128);
        private BirdPathFollower follower;
        private BirdHealth health;
        private float nextRepathTime;
        private bool shifted;

        private void Start()
        {
            if (bird == null)
            {
                Debug.LogWarning($"[Agents] {name}: no bird assigned.", this);
                enabled = false;
                return;
            }

            follower = bird.GetComponent<BirdPathFollower>();
            health = bird.GetComponent<BirdHealth>();
            follower.PathCompleted += HandlePathCompleted;

            BuildRoute();
            follower.Teleport(route[0], route[1] - route[0]);
            SendPath();
        }

        private void OnDestroy()
        {
            if (follower != null)
                follower.PathCompleted -= HandlePathCompleted;
        }

        private void Update()
        {
            if (bird == null)
                return;

            if (repathInterval > 0f && Time.time >= nextRepathTime && bird.gameObject.activeInHierarchy && health.IsAlive)
            {
                shifted = !shifted; // alternate between two slightly different routes
                BuildRoute();
                SendPath();
            }

            Keyboard keyboard = Keyboard.current;
            if (!respondToKeys || keyboard == null)
                return;

            if (keyboard.hKey.wasPressedThisFrame && health.IsAlive)
                health.TakeDamage(damagePerPress, bird.Species != null ? bird.Species.RequiredWeaponRarity : default);
            if (keyboard.kKey.wasPressedThisFrame)
                health.Kill();
            if (keyboard.rKey.wasPressedThisFrame)
                Respawn();
        }

        private void Respawn()
        {
            bird.gameObject.SetActive(true);
            health.Revive();
            shifted = false;
            BuildRoute();
            follower.Teleport(route[0], route[1] - route[0]);
            SendPath();
        }

        private void HandlePathCompleted() => SendPath();

        /// <summary>One lap of the route, starting at the cell after the one nearest the bird.</summary>
        private void SendPath()
        {
            int nearest = 0;
            float nearestSqr = float.MaxValue;
            Vector3 position = bird.transform.position;
            for (int i = 0; i < route.Count; i++)
            {
                float sqr = (route[i] - position).sqrMagnitude;
                if (sqr < nearestSqr)
                {
                    nearestSqr = sqr;
                    nearest = i;
                }
            }

            pathBuffer.Clear();
            for (int i = 1; i <= route.Count; i++)
                pathBuffer.Add(route[(nearest + i) % route.Count]);

            follower.SetPath(pathBuffer);
            nextRepathTime = Time.time + repathInterval;
        }

        /// <summary>Corners -> a closed loop of cell centres (straight runs included, as an A* grid path would have).</summary>
        private void BuildRoute()
        {
            corners.Clear();
            if (transform.childCount >= 2)
            {
                for (int i = 0; i < transform.childCount; i++)
                    corners.Add(transform.GetChild(i).position);
            }
            else
            {
                BuildZigzag();
            }

            route.Clear();
            for (int i = 0; i < corners.Count; i++)
            {
                Vector3 from = corners[i];
                Vector3 to = corners[(i + 1) % corners.Count];
                int steps = Mathf.Max(1, Mathf.RoundToInt(Vector3.Distance(from, to) / cellSize));
                for (int s = 0; s < steps; s++)
                    route.Add(Vector3.Lerp(from, to, (float)s / steps));
            }
        }

        /// <summary>
        /// A square wave along +X (right-angle corners every legCells cells), then straight back one level higher,
        /// centred on this object. Every other re-plan pushes the crests out by repathShiftCells.
        /// </summary>
        private void BuildZigzag()
        {
            float leg = legCells * cellSize;
            float crest = leg + (shifted ? repathShiftCells * cellSize : 0f);
            float width = zigzagLegs * leg;
            Vector3 origin = transform.position - new Vector3(width * 0.5f, 0f, 0f);

            for (int i = 0; i <= zigzagLegs; i++)
            {
                float x = i * leg;
                bool up = i % 2 == 1;
                corners.Add(origin + new Vector3(x, 0f, up ? crest : 0f));
                if (i < zigzagLegs)
                    corners.Add(origin + new Vector3(x, 0f, up ? 0f : crest));
            }

            float climb = returnClimbCells * cellSize;
            corners.Add(origin + new Vector3(width, climb, -leg));
            corners.Add(origin + new Vector3(0f, climb, -leg));
        }
    }
}
