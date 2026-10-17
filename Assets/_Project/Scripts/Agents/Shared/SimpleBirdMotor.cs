using System.Collections.Generic;
using UnityEngine;

namespace RunwayPanic.Agents
{
    // Stand-in until the Agent Controller's BirdMotor (F4). Do not polish this: no banking, no easing, no animation.

    /// <summary>
    /// Minimal <see cref="IBirdMotor"/>: seeks the current target at <c>BaseSpeed × SpeedMultiplier</c> with an
    /// acceleration cap, slows down close to the destination, adds the frame's steering, and turns to face its velocity.
    /// Moves a kinematic Rigidbody with MovePosition / MoveRotation.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public sealed class SimpleBirdMotor : MonoBehaviour, IBirdMotor
    {
        private enum Mode { Idle, Path, Seek, Hold, Land }

        [Tooltip("Speed used when the bird has no BirdHealth/species asset, in m/s.")]
        [SerializeField, Min(0.1f)] private float fallbackSpeed = 5f;
        [Tooltip("Maximum change of velocity per second, in m/s².")]
        [SerializeField, Min(0.1f)] private float acceleration = 12f;
        [Tooltip("How quickly the bird turns to face its velocity (higher = snappier).")]
        [SerializeField, Min(0.1f)] private float turnSpeed = 6f;
        [Tooltip("Distance to an intermediate waypoint at which the bird moves on to the next one, in metres.")]
        [SerializeField, Min(0.05f)] private float waypointReachDistance = 1f;
        [Tooltip("Distance to the destination that counts as arrived, in metres.")]
        [SerializeField, Min(0.05f)] private float arriveDistance = 0.6f;
        [Tooltip("Within this distance of the destination the bird slows down, in metres.")]
        [SerializeField, Min(0.1f)] private float slowDownRadius = 2f;
        [Tooltip("Within this distance of a landing point the bird snaps onto it and lands, in metres.")]
        [SerializeField, Min(0.01f)] private float landSnapDistance = 0.3f;

        private readonly List<Vector3> path = new List<Vector3>(32);
        private Rigidbody body;
        private BirdHealth health;
        private Mode mode = Mode.Idle;
        private int waypoint;
        private Vector3 target;
        private Vector3 landFacing;
        private Vector3 velocity;
        private Vector3 steering;

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

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            health = GetComponent<BirdHealth>();
        }

        /// <inheritdoc />
        public void FollowPath(IReadOnlyList<Vector3> worldPath)
        {
            path.Clear();
            if (worldPath != null)
                for (int i = 0; i < worldPath.Count; i++)
                    path.Add(worldPath[i]);

            if (path.Count == 0)
            {
                Stop();
                return;
            }

            waypoint = 0;
            Begin(Mode.Path, path[path.Count - 1]);
        }

        /// <inheritdoc />
        public void FlyTo(Vector3 destination)
        {
            Begin(Mode.Seek, destination);
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
            path.Clear();
            mode = Mode.Idle;
            IsLanded = false;
            ReachedDestination = false;
        }

        private void Begin(Mode newMode, Vector3 destination)
        {
            mode = newMode;
            target = destination;
            IsLanded = false;
            ReachedDestination = false;
        }

        private void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            Vector3 position = body.position;

            // Landed, or idle and at rest: leave the body alone, so a motor with no orders never fights
            // other scripts (e.g. a prototype moving the transform directly).
            if (IsLanded || (mode == Mode.Idle && velocity.sqrMagnitude < 0.0001f && steering.sqrMagnitude < 0.0001f))
            {
                steering = Vector3.zero;
                velocity = Vector3.zero;
                return;
            }

            Vector3 desired = Vector3.zero;
            switch (mode)
            {
                case Mode.Path:
                    // Move on to the next waypoint once close enough to this one (never past the last).
                    while (waypoint < path.Count - 1 && (path[waypoint] - position).sqrMagnitude < waypointReachDistance * waypointReachDistance)
                        waypoint++;
                    desired = Arrive(position, path[waypoint], waypoint == path.Count - 1);
                    break;
                case Mode.Seek:
                case Mode.Hold:
                case Mode.Land:
                    desired = Arrive(position, target, true);
                    break;
            }

            if (mode != Mode.Idle)
                ReachedDestination = (target - position).sqrMagnitude < arriveDistance * arriveDistance &&
                                     (mode != Mode.Path || waypoint == path.Count - 1);

            if (mode == Mode.Land && (target - position).sqrMagnitude < landSnapDistance * landSnapDistance)
            {
                body.MovePosition(target);
                Vector3 flatFacing = new Vector3(landFacing.x, 0f, landFacing.z);
                if (flatFacing.sqrMagnitude > 0.0001f)
                    body.MoveRotation(Quaternion.LookRotation(flatFacing, Vector3.up));
                velocity = Vector3.zero;
                steering = Vector3.zero;
                IsLanded = true;
                return;
            }

            desired += steering;
            steering = Vector3.zero;

            velocity = Vector3.MoveTowards(velocity, desired, acceleration * dt);
            body.MovePosition(position + velocity * dt);

            if (velocity.sqrMagnitude > 0.01f)
            {
                Quaternion facing = Quaternion.LookRotation(velocity, Vector3.up);
                body.MoveRotation(Quaternion.Slerp(body.rotation, facing, turnSpeed * dt));
            }
        }

        // Desired velocity toward 'destination'; slows down near it when it is the final stop.
        private Vector3 Arrive(Vector3 position, Vector3 destination, bool isFinal)
        {
            Vector3 toTarget = destination - position;
            float distance = toTarget.magnitude;
            if (distance < 0.0001f)
                return Vector3.zero;

            float speed = BaseSpeed * SpeedMultiplier;
            if (isFinal && distance < slowDownRadius)
                speed *= distance / slowDownRadius;
            return toTarget / distance * speed;
        }
    }
}
