using System;
using System.Collections.Generic;
using UnityEngine;

namespace RunwayPanic.Agents
{
    /// <summary>
    /// The bird's flight (F4, Agent Controller): the real <see cref="IBirdMotor"/> that species brains move birds with.
    /// <para><b>Paths</b> (<see cref="FollowPath"/>): the A* waypoints are smoothed once (<see cref="CatmullRomPath"/>);
    /// every physics step the bird then steers its velocity toward a point a little ahead of it on that curve (a
    /// look-ahead target), turning no faster than its species allows. A new path starts at the bird along its current
    /// velocity, so re-planning never snaps.</para>
    /// <para><b>Targets</b> (<see cref="FlyTo"/>, <see cref="HoldPosition"/>, <see cref="LandAt"/>): the same steering
    /// toward a point, braking so it can stop there.</para>
    /// <para>In every mode the bird faces the way it flies: yaw and pitch from its velocity, roll (bank) from how fast
    /// it turns. It moves a kinematic Rigidbody, so gun raycasts and triggers always find the bird where it is drawn.
    /// Speed comes from the species asset (<see cref="BirdHealth.Species"/>), turning and banking from the
    /// <see cref="BirdMotionProfile"/>.</para>
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class BirdMotor : MonoBehaviour, IBirdMotor
    {
        private enum Mode
        {
            Idle,
            Path,
            Seek,
            Hold,
            Land
        }

        // Progress search window around the last known progress, in metres.
        private const float SearchBehind = 1f;
        private const float SearchAhead = 2f;
        // Below this speed the bird counts as hovering.
        private const float HoverSpeed = 0.2f;
        // Speed factor when the target is straight behind the bird: slow down for sharp turns, like a car in a corner.
        private const float SharpTurnSpeedFactor = 0.4f;

        [Tooltip("How this species flies: turn rate, banking, acceleration, flap speed (ScriptableObjects/Agents).")]
        [SerializeField] private BirdMotionProfile motionProfile;
        [Tooltip("Top speed used when the bird has no species asset, in m/s.")]
        [SerializeField, Min(0.1f)] private float fallbackSpeed = 5f;

        [Header("Waypoints")]
        [Tooltip("Added to the height of every waypoint, e.g. when a grid returns points on the ground.")]
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
        [Tooltip("Distance to the destination that counts as arrived, in metres.")]
        [SerializeField, Min(0.05f)] private float arriveDistance = 0.6f;
        [Tooltip("Within this distance of a landing point the bird settles onto it and lands, in metres.")]
        [SerializeField, Min(0.01f)] private float landSnapDistance = 0.3f;

        [Header("Debug")]
        [Tooltip("Scene view: raw waypoints (red), smoothed curve (green), look-ahead or seek target (yellow).")]
        [SerializeField] private bool drawPathGizmos = true;

        /// <summary>Raised once when the bird reaches the end of a path from <see cref="FollowPath"/>.</summary>
        public event Action PathCompleted;

        /// <summary>
        /// Raised once for every index of the list passed to <see cref="FollowPath"/>, in order, when the bird passes
        /// that waypoint (also for waypoints that smoothing removed or that were already reached when the path started).
        /// </summary>
        public event Action<int> WaypointReached;

        private static BirdMotionProfile fallbackMotion;

        private readonly CatmullRomPath path = new CatmullRomPath();
        private readonly List<Vector3> waypoints = new List<Vector3>(64);
        private float[] waypointDistances = new float[64];

        private Rigidbody body;
        private BirdHealth health;
        private Mode mode = Mode.Idle;
        private Vector3 target;
        private Vector3 landFacing;
        private Vector3 heading;
        private float speed;
        private Vector3 steering;
        private Vector3 velocity;
        private float progress;
        private int nextWaypoint;
        private int pathVersion;
        private bool pathFinished;
        private Vector3 aimPoint;
        private float yaw;
        private float pitch;
        private float bank;
        private float bankVelocity;
        private float gravity;

        // ---------- IBirdMotor ----------

        /// <inheritdoc />
        public Vector3 Position => body != null ? body.position : transform.position;

        /// <inheritdoc />
        public Vector3 Velocity => velocity;

        /// <inheritdoc />
        public float BaseSpeed => health != null && health.Species != null ? health.Species.MoveSpeed : fallbackSpeed;

        /// <inheritdoc />
        public float SpeedMultiplier { get; set; } = 1f;

        /// <inheritdoc />
        public bool HasPath => mode == Mode.Path;

        /// <inheritdoc />
        public bool ReachedDestination { get; private set; }

        /// <inheritdoc />
        public bool IsLanded { get; private set; }

        // ---------- Extra (Agent Controller, tests, animation) ----------

        /// <summary>The motion profile in use (default values when none is assigned).</summary>
        public BirdMotionProfile Motion
        {
            get
            {
                if (motionProfile != null)
                    return motionProfile;

                if (fallbackMotion == null)
                {
                    fallbackMotion = ScriptableObject.CreateInstance<BirdMotionProfile>();
                    fallbackMotion.hideFlags = HideFlags.DontSave;
                }

                return fallbackMotion;
            }
        }

        /// <summary>Current speed along the flight direction in m/s (without the frame's steering).</summary>
        public float Speed => speed;

        /// <summary>Steepest climb or dive in degrees, from the motion profile.</summary>
        public float MaxPitch => Motion.MaxPitch;

        /// <summary>Metres left along the current path, or 0 when not following one.</summary>
        public float RemainingDistance => mode == Mode.Path ? Mathf.Max(0f, path.Length - progress) : 0f;

        /// <summary>Current roll into the turn in degrees (+ = banking right).</summary>
        public float BankAngle => bank;

        /// <summary>Current climb angle in degrees (+ = nose up).</summary>
        public float PitchAngle => pitch;

        /// <summary>
        /// Scene-view path gizmos. Off for crowds (stress tests): with Gizmos on in the Game view, drawing hundreds of
        /// lines per bird every frame costs far more than the flying itself.
        /// </summary>
        public bool DrawPathGizmos
        {
            get => drawPathGizmos;
            set => drawPathGizmos = value;
        }

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
            health = GetComponent<BirdHealth>();

            // Kinematic: the bird is moved by this script, not pushed by physics, but still has a collider that
            // raycasts hit. Interpolation draws it smoothly between the 50 Hz physics steps.
            body.isKinematic = true;
            body.useGravity = false;
            body.interpolation = RigidbodyInterpolation.Interpolate;

            gravity = Mathf.Max(Physics.gravity.magnitude, 0.01f);
            heading = transform.forward;
            yaw = transform.eulerAngles.y;
        }

        // ---------- Orders ----------

        /// <summary>
        /// Follows <paramref name="worldPath"/>: world-space positions for the bird's pivot, in order. Replaces any
        /// current order without a jump: the new curve starts at the bird, along its current velocity. The list is
        /// copied, so the caller may reuse it.
        /// </summary>
        public void FollowPath(IReadOnlyList<Vector3> worldPath)
        {
            if (worldPath == null || worldPath.Count == 0)
            {
                Stop();
                return;
            }

            waypoints.Clear();
            for (int i = 0; i < worldPath.Count; i++)
            {
                Vector3 point = worldPath[i];
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
            pathFinished = false;
            Begin(Mode.Path, path.EndPoint);
        }

        /// <inheritdoc />
        public void FlyTo(Vector3 destination)
        {
            if (mode != Mode.Seek)
                Begin(Mode.Seek, destination);
            target = destination; // called every frame while chasing: only the target moves
        }

        /// <inheritdoc />
        public void AddSteering(Vector3 velocityOffset)
        {
            steering += velocityOffset;
        }

        /// <inheritdoc />
        public void HoldPosition(Vector3 point)
        {
            Begin(Mode.Hold, point);
        }

        /// <inheritdoc />
        public void LandAt(Vector3 point, Vector3 facing)
        {
            landFacing = facing;
            Begin(Mode.Land, point);
        }

        /// <inheritdoc />
        public void Stop()
        {
            waypoints.Clear();
            path.Clear();
            Begin(Mode.Idle, body != null ? body.position : transform.position);
        }

        /// <summary>Moves the bird instantly (spawning, respawning), stopped and facing <paramref name="forward"/>.</summary>
        public void Teleport(Vector3 position, Vector3 forward)
        {
            Stop();
            speed = 0f;
            velocity = Vector3.zero;
            steering = Vector3.zero;
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

        private void Begin(Mode newMode, Vector3 destination)
        {
            // Brains may repeat the same order every frame (e.g. HoldPosition on the same point): keep its state.
            if (newMode == mode && newMode != Mode.Path && (destination - target).sqrMagnitude < 1e-4f)
                return;

            pathVersion++; // stops a WaypointReached loop that is still running for the old order
            mode = newMode;
            target = destination;
            IsLanded = false;
            ReachedDestination = false;
        }

        // ---------- Flight ----------

        private void FixedUpdate()
        {
            float deltaTime = Time.fixedDeltaTime;
            Vector3 position = body.position;

            // Landed, or idle and at rest: leave the body alone, so a motor with no orders never fights other scripts.
            bool atRest = mode == Mode.Idle && speed < 0.01f && steering.sqrMagnitude < 1e-4f && Mathf.Abs(bank) < 0.1f;
            if (IsLanded || atRest)
            {
                steering = Vector3.zero;
                velocity = Vector3.zero;
                return;
            }

            BirdMotionProfile motion = Motion;
            float maxSpeed = BaseSpeed * Mathf.Max(SpeedMultiplier, 0f);
            Vector3 desiredDirection = new Vector3(heading.x, 0f, heading.z); // default: level out
            float targetSpeed = 0f;

            switch (mode)
            {
                case Mode.Path:
                    SteerAlongPath(position, motion, maxSpeed, ref desiredDirection, ref targetSpeed);
                    break;
                case Mode.Seek:
                case Mode.Hold:
                case Mode.Land:
                    if (SteerToTarget(position, motion, maxSpeed, ref desiredDirection, ref targetSpeed))
                        return; // landed this step
                    break;
            }

            // Turn the velocity toward the target, no faster than the species can turn.
            if (desiredDirection.sqrMagnitude > 1e-6f)
                heading = Vector3.RotateTowards(heading, desiredDirection.normalized, motion.MaxTurnRate * Mathf.Deg2Rad * deltaTime, 0f);
            heading = ClampPitch(heading, motion.MaxPitch);

            // Faster bursts (SpeedMultiplier > 1, e.g. a stoop) also accelerate harder.
            float acceleration = motion.Acceleration * Mathf.Max(1f, SpeedMultiplier);
            speed = Mathf.MoveTowards(speed, targetSpeed, acceleration * deltaTime);

            // The frame's steering (boids, fear) nudges the movement for this step only; the bird keeps facing its heading.
            velocity = heading * speed + steering;
            steering = Vector3.zero;
            body.MovePosition(position + velocity * deltaTime);

            UpdateRotation(deltaTime, motion);
        }

        /// <summary>
        /// Path following: finds how far along the curve the bird is, aims at the point LookAheadDistance further on,
        /// and picks a speed that lets it brake to a stop at the end. After the end it hovers there.
        /// </summary>
        private void SteerAlongPath(Vector3 position, BirdMotionProfile motion, float maxSpeed, ref Vector3 desiredDirection, ref float targetSpeed)
        {
            Vector3 toEnd = path.EndPoint - position;
            if (pathFinished)
            {
                ReachedDestination = toEnd.sqrMagnitude <= arriveDistance * arriveDistance;
                SteerToPoint(path.EndPoint, position, motion, maxSpeed, ref desiredDirection, ref targetSpeed);
                return;
            }

            float lookAhead = motion.LookAheadDistance;
            float found = path.ClosestDistance(position, progress - SearchBehind, progress + lookAhead + SearchAhead);
            progress = Mathf.Max(progress, found); // never slide backwards along the path

            int version = pathVersion;
            ReportPassedWaypoints(version);
            if (version != pathVersion)
                return; // a WaypointReached handler gave a new order

            // Arrived = near the end ALONG the path, and either at the last waypoint or stopped. Checking only the
            // distance to the last waypoint would end a closed loop (last waypoint = start) on its first step.
            float remaining = path.Length - progress;
            bool arrived = remaining <= arriveDistance &&
                           (toEnd.sqrMagnitude <= arriveDistance * arriveDistance || speed <= HoverSpeed);
            if (arrived)
            {
                CompletePath(version);
                return;
            }

            aimPoint = path.PointAtDistance(progress + lookAhead);
            Vector3 toTarget = aimPoint - position;
            if (toTarget.sqrMagnitude > 1e-6f)
                desiredDirection = toTarget;

            targetSpeed = CorneringSpeed(desiredDirection, maxSpeed, motion, Mathf.Max(remaining, toEnd.magnitude));
        }

        /// <summary>Seek, hold and land. Returns true when the bird has just landed.</summary>
        private bool SteerToTarget(Vector3 position, BirdMotionProfile motion, float maxSpeed, ref Vector3 desiredDirection, ref float targetSpeed)
        {
            Vector3 toTarget = target - position;
            ReachedDestination = toTarget.sqrMagnitude <= arriveDistance * arriveDistance;

            if (mode == Mode.Land && toTarget.sqrMagnitude <= landSnapDistance * landSnapDistance)
            {
                SettleOnLandingPoint();
                return true;
            }

            SteerToPoint(target, position, motion, maxSpeed, ref desiredDirection, ref targetSpeed);
            return false;
        }

        private void SteerToPoint(Vector3 point, Vector3 position, BirdMotionProfile motion, float maxSpeed, ref Vector3 desiredDirection, ref float targetSpeed)
        {
            aimPoint = point;
            Vector3 toPoint = point - position;
            float distance = toPoint.magnitude;

            // Close enough and slow: hover facing the same way instead of spinning round the point.
            if (distance <= arriveDistance * 0.5f && mode != Mode.Land && mode != Mode.Seek)
                return;

            if (distance > 1e-3f)
                desiredDirection = toPoint;
            targetSpeed = CorneringSpeed(desiredDirection, maxSpeed, motion, distance);
        }

        /// <summary>
        /// Top speed, reduced for sharp turns and limited to the braking speed: v² = 2·a·d is the fastest speed that can
        /// still stop in <paramref name="distanceLeft"/> metres.
        /// </summary>
        private float CorneringSpeed(Vector3 desiredDirection, float maxSpeed, BirdMotionProfile motion, float distanceLeft)
        {
            float brakingSpeed = Mathf.Sqrt(2f * motion.Acceleration * Mathf.Max(SpeedMultiplier, 1f) * distanceLeft);
            float alignment = Vector3.Dot(heading, desiredDirection.normalized); // 1 = straight ahead, -1 = behind
            float turnFactor = Mathf.Lerp(SharpTurnSpeedFactor, 1f, (alignment + 1f) * 0.5f);
            return Mathf.Min(maxSpeed * turnFactor, brakingSpeed);
        }

        private void SettleOnLandingPoint()
        {
            body.MovePosition(target);
            Vector3 flat = new Vector3(landFacing.x, 0f, landFacing.z);
            if (flat.sqrMagnitude < 1e-4f)
                flat = new Vector3(heading.x, 0f, heading.z);
            if (flat.sqrMagnitude > 1e-4f)
            {
                heading = flat.normalized;
                body.MoveRotation(Quaternion.LookRotation(heading, Vector3.up));
            }

            yaw = Mathf.Atan2(heading.x, heading.z) * Mathf.Rad2Deg;
            pitch = 0f;
            bank = 0f;
            bankVelocity = 0f;
            speed = 0f;
            velocity = Vector3.zero;
            steering = Vector3.zero;
            ReachedDestination = true;
            IsLanded = true;
        }

        /// <summary>
        /// Faces the bird the way it flies: yaw and pitch come from the velocity, roll (bank) from how fast it turns.
        /// The rotation is built from three angles about world up, so the bird can never end up upside down.
        /// </summary>
        private void UpdateRotation(float deltaTime, BirdMotionProfile motion)
        {
            Vector3 flat = new Vector3(heading.x, 0f, heading.z);
            float newYaw = flat.sqrMagnitude > 1e-6f ? Mathf.Atan2(heading.x, heading.z) * Mathf.Rad2Deg : yaw;
            float yawRate = deltaTime > 0f ? Mathf.DeltaAngle(yaw, newYaw) / deltaTime : 0f; // deg/s, + = turning right
            yaw = newYaw;
            pitch = Mathf.Asin(Mathf.Clamp(heading.y, -1f, 1f)) * Mathf.Rad2Deg; // + = climbing

            // Coordinated turn: to turn at rate ω while flying at speed v, a bird (or a plane) tilts its lift into the
            // turn by tan(bank) = v·ω / g. Clamped to the species' limit and smoothed so the roll eases in and out.
            float targetBank = Mathf.Atan(speed * yawRate * Mathf.Deg2Rad / gravity) * Mathf.Rad2Deg;
            targetBank = Mathf.Clamp(targetBank, -motion.MaxBank, motion.MaxBank);
            bank = Mathf.SmoothDamp(bank, targetBank, ref bankVelocity, motion.BankSmoothTime, Mathf.Infinity, deltaTime);

            // Euler(x, y, z) applies roll, then pitch, then yaw about world up. Unity's +x tilts the nose down and +z
            // lifts the right wing, so climbing and banking right are both negative here.
            body.MoveRotation(Quaternion.Euler(-pitch, yaw, -bank));
        }

        // ---------- Path bookkeeping ----------

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

            pathFinished = true;
            ReachedDestination = true;
            PathCompleted?.Invoke();
        }

        /// <summary>Limits how steeply <paramref name="direction"/> climbs or dives.</summary>
        private static Vector3 ClampPitch(Vector3 direction, float maxPitchDegrees)
        {
            float horizontal = new Vector2(direction.x, direction.z).magnitude;
            float angle = Mathf.Atan2(direction.y, horizontal) * Mathf.Rad2Deg;
            if (Mathf.Abs(angle) <= maxPitchDegrees)
                return direction;

            Vector3 flat = horizontal > 1e-6f ? new Vector3(direction.x, 0f, direction.z) / horizontal : Vector3.forward;
            float clamped = Mathf.Sign(angle) * maxPitchDegrees * Mathf.Deg2Rad;
            return flat * Mathf.Cos(clamped) + Vector3.up * Mathf.Sin(clamped);
        }

        private void OnDrawGizmos()
        {
            if (!drawPathGizmos || mode == Mode.Idle || IsLanded)
                return;

            if (mode == Mode.Path)
            {
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
            }

            Gizmos.color = Color.yellow;
            Gizmos.DrawSphere(aimPoint, 0.18f);
            Gizmos.DrawLine(transform.position, aimPoint);
        }
    }
}
