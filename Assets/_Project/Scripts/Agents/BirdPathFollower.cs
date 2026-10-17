using System;
using System.Collections.Generic;
using UnityEngine;

namespace RunwayPanic.Agents
{
    /// <summary>
    /// Flies the bird along a path from the IS pathfinding: an ordered list of world-space positions for the bird's
    /// pivot (its centre of mass).
    /// <see cref="SetPath(IReadOnlyList{Vector3})"/> smooths the waypoints once (<see cref="CatmullRomPath"/>). Every
    /// physics step the bird then steers its velocity toward a point a little ahead of it on that curve (a look-ahead
    /// target), turning no faster than its species allows, and faces the way it flies: yaw and pitch from its
    /// velocity, roll (bank) from how fast it turns.
    /// It moves a kinematic Rigidbody, so gun raycasts and triggers always find the bird where it is drawn.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class BirdPathFollower : MonoBehaviour
    {
        // Progress search window around the last known progress, in metres.
        private const float SearchBehind = 1f;
        private const float SearchAhead = 2f;
        // Below this speed the bird counts as hovering.
        private const float HoverSpeed = 0.2f;
        // Speed factor when the target is straight behind the bird: slow down for sharp turns, like a car in a corner.
        private const float SharpTurnSpeedFactor = 0.4f;

        [Header("Waypoints")]
        [Tooltip("Added to the height of every waypoint, e.g. when the IS grid returns points on the ground.")]
        [SerializeField] private float heightOffset;
        [Tooltip("Waypoints lower than this world height are raised to it, so a bird never flies into the ground.")]
        [SerializeField] private float minimumAltitude = 1f;
        [Tooltip("Leading waypoints closer than this to the bird count as already reached (an A* path starts in the bird's own cell).")]
        [SerializeField, Min(0f)] private float skipWaypointsWithin = 1f;

        [Header("Smoothing")]
        [Tooltip("Waypoints where the route bends by less than this many degrees are dropped before smoothing.")]
        [SerializeField, Range(0f, 20f)] private float collinearTolerance = 2f;
        [Tooltip("Distance between samples of the smoothed curve, in metres. Smaller = closer to the true curve, more memory.")]
        [SerializeField, Range(0.05f, 1f)] private float sampleSpacing = 0.25f;
        [Tooltip("Seconds of straight flight along the current velocity that a new path starts with, so the bird never snaps around.")]
        [SerializeField, Range(0f, 1f)] private float leadInTime = 0.25f;

        [Header("Arrival")]
        [Tooltip("The path is complete once the bird is this close to the last waypoint, in metres.")]
        [SerializeField, Min(0.05f)] private float arrivalRadius = 0.3f;
        [Tooltip("Top speed used when the bird has no species asset, in m/s.")]
        [SerializeField, Min(0.1f)] private float fallbackSpeed = 5f;

        [Header("Debug")]
        [Tooltip("Scene view: raw waypoints (red), smoothed curve (green), look-ahead target (yellow).")]
        [SerializeField] private bool drawPathGizmos = true;

        /// <summary>Raised when the bird reaches the last waypoint. It then slows to a hover until it gets a new path.</summary>
        public event Action PathCompleted;

        /// <summary>
        /// Raised once for every index of the array passed to SetPath, in order, when the bird passes that waypoint
        /// (also for waypoints that smoothing removed or that were already reached when the path started).
        /// </summary>
        public event Action<int> WaypointReached;

        private readonly CatmullRomPath path = new CatmullRomPath();
        private readonly List<Vector3> waypoints = new List<Vector3>(64);
        private float[] waypointDistances = new float[64];

        private Rigidbody body;
        private BirdAgent agent;
        private Vector3 heading;
        private float speed;
        private float progress;
        private int nextWaypoint;
        private int pathVersion;
        private Vector3 lookAheadPoint;
        private float yaw;
        private float pitch;
        private float bank;
        private float bankVelocity;
        private float gravity;

        /// <summary>True while the bird has a path it has not finished.</summary>
        public bool IsFollowing { get; private set; }

        /// <summary>Current velocity in m/s (world space).</summary>
        public Vector3 Velocity => heading * speed;

        /// <summary>Current speed in m/s.</summary>
        public float Speed => speed;

        /// <summary>Top speed in m/s, from the species asset.</summary>
        public float MaxSpeed => agent != null && agent.Species != null ? agent.Species.MoveSpeed : fallbackSpeed;

        /// <summary>Steepest climb or dive in degrees, from the motion profile.</summary>
        public float MaxPitch => Motion.MaxPitch;

        /// <summary>Metres left along the smoothed path, or 0 when not following one.</summary>
        public float RemainingDistance => IsFollowing ? Mathf.Max(0f, path.Length - progress) : 0f;

        /// <summary>
        /// Scene-view path gizmos. Off for crowds (stress tests): with Gizmos on in the Game view, drawing hundreds of
        /// lines per bird every frame costs far more than the flying itself.
        /// </summary>
        public bool DrawPathGizmos
        {
            get => drawPathGizmos;
            set => drawPathGizmos = value;
        }

        /// <summary>Current roll into the turn in degrees (+ = banking right).</summary>
        public float BankAngle => bank;

        /// <summary>Current climb angle in degrees (+ = nose up).</summary>
        public float PitchAngle => pitch;

        private BirdMotionProfile Motion => agent != null ? agent.Motion : FallbackMotion;
        private static BirdMotionProfile fallbackMotion;

        private static BirdMotionProfile FallbackMotion
        {
            get
            {
                if (fallbackMotion == null)
                {
                    fallbackMotion = ScriptableObject.CreateInstance<BirdMotionProfile>();
                    fallbackMotion.hideFlags = HideFlags.DontSave;
                }

                return fallbackMotion;
            }
        }

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
            agent = GetComponent<BirdAgent>();

            // Kinematic: the bird is moved by this script, not pushed by physics, but still has a collider that
            // raycasts hit. Interpolation draws it smoothly between the 50 Hz physics steps.
            body.isKinematic = true;
            body.useGravity = false;
            body.interpolation = RigidbodyInterpolation.Interpolate;

            gravity = Mathf.Max(Physics.gravity.magnitude, 0.01f);
            heading = transform.forward;
            yaw = transform.eulerAngles.y;
        }

        /// <summary>
        /// Starts following <paramref name="worldWaypoints"/>: world-space positions for the bird's pivot, in order.
        /// Replaces any current path without a jump: the new curve starts at the bird, along its current velocity.
        /// The list is copied, so the caller may reuse it.
        /// </summary>
        public void SetPath(IReadOnlyList<Vector3> worldWaypoints)
        {
            if (worldWaypoints == null || worldWaypoints.Count == 0)
            {
                ClearPath();
                return;
            }

            waypoints.Clear();
            for (int i = 0; i < worldWaypoints.Count; i++)
            {
                Vector3 point = worldWaypoints[i];
                point.y = Mathf.Max(point.y + heightOffset, minimumAltitude);
                waypoints.Add(point);
            }

            Vector3 start = body.position;
            int first = 0;
            float skipSqr = skipWaypointsWithin * skipWaypointsWithin;
            while (first < waypoints.Count - 1 && (waypoints[first] - start).sqrMagnitude < skipSqr)
                first++;

            bool moving = speed > HoverSpeed;
            float leadIn = moving ? speed * leadInTime : 0f;
            path.Build(start, moving ? heading : Vector3.zero, leadIn, waypoints, first, collinearTolerance, sampleSpacing);

            MapWaypointsToCurve(start, first, leadIn);

            progress = 0f;
            nextWaypoint = 0;
            pathVersion++;
            IsFollowing = true;
        }

        /// <summary>Same as the list version; for IS code that produces arrays.</summary>
        public void SetPath(Vector3[] worldWaypoints) => SetPath((IReadOnlyList<Vector3>)worldWaypoints);

        /// <summary>Forgets the path. The bird slows down and hovers where it is.</summary>
        public void ClearPath()
        {
            waypoints.Clear();
            path.Clear();
            progress = 0f;
            nextWaypoint = 0;
            pathVersion++;
            IsFollowing = false;
        }

        /// <summary>Forgets the path and stops dead this step (e.g. when the bird dies).</summary>
        public void Stop()
        {
            ClearPath();
            speed = 0f;
        }

        /// <summary>Moves the bird instantly (spawning, respawning), stopped and facing <paramref name="forward"/>.</summary>
        public void Teleport(Vector3 position, Vector3 forward)
        {
            Stop();
            body.position = position;
            transform.position = position;

            Vector3 flat = new Vector3(forward.x, 0f, forward.z);
            heading = flat.sqrMagnitude > 1e-6f ? flat.normalized : transform.forward;
            yaw = Mathf.Atan2(heading.x, heading.z) * Mathf.Rad2Deg;
            pitch = 0f;
            bank = 0f;
            bankVelocity = 0f;
            Quaternion rotation = Quaternion.LookRotation(heading, Vector3.up);
            body.rotation = rotation;
            transform.rotation = rotation;
        }

        private void FixedUpdate()
        {
            float deltaTime = Time.fixedDeltaTime;
            BirdMotionProfile motion = Motion;
            Vector3 position = body.position;
            Vector3 desiredDirection = new Vector3(heading.x, 0f, heading.z); // no path: level out and hover
            float targetSpeed = 0f;

            if (IsFollowing)
                Steer(position, motion, ref desiredDirection, ref targetSpeed);

            // Turn the velocity toward the target, no faster than the species can turn.
            if (desiredDirection.sqrMagnitude > 1e-6f)
                heading = Vector3.RotateTowards(heading, desiredDirection.normalized, motion.MaxTurnRate * Mathf.Deg2Rad * deltaTime, 0f);
            heading = ClampPitch(heading, motion.MaxPitch);

            speed = Mathf.MoveTowards(speed, targetSpeed, motion.Acceleration * deltaTime);
            body.MovePosition(position + heading * (speed * deltaTime));

            UpdateRotation(deltaTime);
        }

        /// <summary>
        /// Path following: finds how far along the curve the bird is, aims at the point LookAheadDistance further on,
        /// and picks a speed that lets it brake to a stop at the end.
        /// </summary>
        private void Steer(Vector3 position, BirdMotionProfile motion, ref Vector3 desiredDirection, ref float targetSpeed)
        {
            float lookAhead = motion.LookAheadDistance;
            float found = path.ClosestDistance(position, progress - SearchBehind, progress + lookAhead + SearchAhead);
            progress = Mathf.Max(progress, found); // never slide backwards along the path

            int version = pathVersion;
            ReportPassedWaypoints(version);
            if (version != pathVersion)
                return; // a WaypointReached handler set a new path

            // Arrived = near the end ALONG the path, and either at the last waypoint or stopped. Checking only the
            // distance to the last waypoint would end a closed loop (last waypoint = start) on its first step.
            float remaining = path.Length - progress;
            Vector3 toEnd = path.EndPoint - position;
            bool arrived = remaining <= arrivalRadius &&
                           (toEnd.sqrMagnitude <= arrivalRadius * arrivalRadius || speed <= HoverSpeed);
            if (arrived)
            {
                CompletePath(version);
                return;
            }

            lookAheadPoint = path.PointAtDistance(progress + lookAhead);
            Vector3 toTarget = lookAheadPoint - position;
            if (toTarget.sqrMagnitude > 1e-6f)
                desiredDirection = toTarget;

            // Stopping distance: v² = 2·a·d, so the fastest speed that can still stop in "remaining" metres.
            float brakingSpeed = Mathf.Sqrt(2f * motion.Acceleration * Mathf.Max(remaining, toEnd.magnitude));
            float alignment = Vector3.Dot(heading, desiredDirection.normalized); // 1 = straight ahead, -1 = behind
            float turnFactor = Mathf.Lerp(SharpTurnSpeedFactor, 1f, (alignment + 1f) * 0.5f);
            targetSpeed = Mathf.Min(MaxSpeed * turnFactor, brakingSpeed);
        }

        /// <summary>
        /// Faces the bird the way it flies: yaw and pitch come from the velocity, roll (bank) from how fast it turns.
        /// The rotation is built from three angles about world up, so the bird can never end up upside down.
        /// </summary>
        private void UpdateRotation(float deltaTime)
        {
            BirdMotionProfile motion = Motion;
            Vector3 flat = new Vector3(heading.x, 0f, heading.z);
            float newYaw = flat.sqrMagnitude > 1e-6f ? Mathf.Atan2(heading.x, heading.z) * Mathf.Rad2Deg : yaw;
            float yawRate = deltaTime > 0f ? Mathf.DeltaAngle(yaw, newYaw) / deltaTime : 0f; // deg/s, + = turning right
            yaw = newYaw;
            pitch = Mathf.Asin(Mathf.Clamp(heading.y, -1f, 1f)) * Mathf.Rad2Deg; // + = climbing

            // Coordinated turn: to turn at rate ω while flying at speed v, a bird (or a plane) tilts its lift into the
            // turn by tan(bank) = v·ω / g. Clamped to the species' limit and smoothed so the roll eases in and out.
            float targetBank = Mathf.Atan(speed * yawRate * Mathf.Deg2Rad / gravity) * Mathf.Rad2Deg;
            targetBank = Mathf.Clamp(targetBank, -motion.MaxBank, motion.MaxBank);
            bank = deltaTime > 0f
                ? Mathf.SmoothDamp(bank, targetBank, ref bankVelocity, motion.BankSmoothTime, Mathf.Infinity, deltaTime)
                : targetBank;

            // Euler(x, y, z) applies roll, then pitch, then yaw about world up. Unity's +x tilts the nose down and +z
            // lifts the right wing, so climbing and banking right are both negative here.
            body.MoveRotation(Quaternion.Euler(-pitch, yaw, -bank));
        }

        /// <summary>
        /// Distance along the new curve at which the bird passes each waypoint, so WaypointReached can fire for every
        /// input index even after simplification removed some of them.
        /// </summary>
        private void MapWaypointsToCurve(Vector3 start, int first, float leadIn)
        {
            int count = waypoints.Count;
            if (waypointDistances.Length < count)
                waypointDistances = new float[Mathf.Max(count, waypointDistances.Length * 2)];

            float searchFrom = 0f;
            for (int i = 0; i < count; i++)
            {
                if (i < first)
                {
                    waypointDistances[i] = 0f; // already reached
                    continue;
                }

                Vector3 previous = i == first ? start : waypoints[i - 1];
                float window = Vector3.Distance(previous, waypoints[i]) * 2f + leadIn + 1f;
                searchFrom = path.ClosestDistance(waypoints[i], searchFrom, searchFrom + window);
                waypointDistances[i] = searchFrom;
            }

            waypointDistances[count - 1] = path.Length;
        }

        private void ReportPassedWaypoints(int version)
        {
            while (nextWaypoint < waypoints.Count && waypointDistances[nextWaypoint] <= progress + 0.01f)
            {
                int index = nextWaypoint++;
                WaypointReached?.Invoke(index);
                if (version != pathVersion)
                    return;
            }
        }

        private void CompletePath(int version)
        {
            progress = path.Length;
            ReportPassedWaypoints(version);
            if (version != pathVersion)
                return;

            IsFollowing = false;
            PathCompleted?.Invoke();
        }

        /// <summary>Limits how steeply <paramref name="direction"/> climbs or dives.</summary>
        private static Vector3 ClampPitch(Vector3 direction, float maxPitchDegrees)
        {
            float horizontal = new Vector2(direction.x, direction.z).magnitude;
            float pitch = Mathf.Atan2(direction.y, horizontal) * Mathf.Rad2Deg;
            if (Mathf.Abs(pitch) <= maxPitchDegrees)
                return direction;

            Vector3 flat = horizontal > 1e-6f ? new Vector3(direction.x, 0f, direction.z) / horizontal : Vector3.forward;
            float clamped = Mathf.Sign(pitch) * maxPitchDegrees * Mathf.Deg2Rad;
            return flat * Mathf.Cos(clamped) + Vector3.up * Mathf.Sin(clamped);
        }

        private void OnDrawGizmos()
        {
            if (!drawPathGizmos || path.SampleCount == 0)
                return;

            Gizmos.color = Color.red;
            for (int i = 0; i < waypoints.Count; i++)
            {
                Gizmos.DrawSphere(waypoints[i], 0.12f);
                if (i > 0)
                    Gizmos.DrawLine(waypoints[i - 1], waypoints[i]);
            }

            Gizmos.color = Color.green;
            for (int i = 1; i < path.SampleCount; i++)
                Gizmos.DrawLine(path.GetSample(i - 1), path.GetSample(i));

            if (IsFollowing)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawSphere(lookAheadPoint, 0.18f);
                Gizmos.DrawLine(transform.position, lookAheadPoint);
            }
        }
    }
}
