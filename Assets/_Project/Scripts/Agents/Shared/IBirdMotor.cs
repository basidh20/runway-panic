using System.Collections.Generic;
using UnityEngine;

namespace RunwayPanic.Agents
{
    /// <summary>
    /// How a brain moves its bird. Brains decide where to go; the motor does the flying.
    /// <see cref="SimpleBirdMotor"/> implements it for now; the Agent Controller's BirdMotor (F4) replaces it on the
    /// prefabs without any brain code changing, so species code must only ever use this interface.
    /// </summary>
    public interface IBirdMotor
    {
        /// <summary>World position of the bird (its pivot = centre of mass).</summary>
        Vector3 Position { get; }

        /// <summary>Current velocity in m/s.</summary>
        Vector3 Velocity { get; }

        /// <summary>Normal flight speed in m/s, from <c>BirdSpeciesData.MoveSpeed</c>.</summary>
        float BaseSpeed { get; }

        /// <summary>1 = normal. Brains set it for bursts, e.g. 1.4 (enraged) or 2.5 (stoop).</summary>
        float SpeedMultiplier { get; set; }

        /// <summary>True while following a path from <see cref="FollowPath"/>.</summary>
        bool HasPath { get; }

        /// <summary>True when within the arrive distance of the current destination (last waypoint, seek or hold point).</summary>
        bool ReachedDestination { get; }

        /// <summary>True after <see cref="LandAt"/> has finished: the bird sits still at the landing point.</summary>
        bool IsLanded { get; }

        /// <summary>Follows these world waypoints in order. The list is copied, so the caller may reuse it.</summary>
        void FollowPath(IReadOnlyList<Vector3> worldPath);

        /// <summary>Flies straight at <paramref name="target"/> with no pathfinding (short hops, dives). Call every frame to chase.</summary>
        void FlyTo(Vector3 target);

        /// <summary>Adds a velocity offset for the next physics step only (boids, fear). Consumed and cleared every FixedUpdate.</summary>
        void AddSteering(Vector3 velocityOffset);

        /// <summary>Hovers at <paramref name="point"/>.</summary>
        void HoldPosition(Vector3 point);

        /// <summary>Flies to <paramref name="point"/>, then stays still facing <paramref name="facing"/>; <see cref="IsLanded"/> becomes true.</summary>
        void LandAt(Vector3 point, Vector3 facing);

        /// <summary>Drops the current path or target and slows to a stop in the air.</summary>
        void Stop();
    }
}
