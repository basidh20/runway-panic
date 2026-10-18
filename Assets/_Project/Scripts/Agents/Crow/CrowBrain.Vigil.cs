using System.Collections.Generic;
using RunwayPanic.Core;
using UnityEngine;

namespace RunwayPanic.Agents.Crow
{
    /// <summary>
    /// Vigil: when the player hides in a shelter, the nearest crows (up to <c>maxVigilCrows</c>) each take a
    /// different exit of that shelter, land there facing the door and wait. The moment the player comes out they
    /// get a short burst of rage. Crows decide independently: each ranks itself by distance among living crows,
    /// so no central coordinator is needed.
    /// </summary>
    public sealed partial class CrowBrain
    {
        private const float VigilCheckInterval = 0.5f;
        private const float VigilLandDistance = 3f;
        private const float VigilApproachHeight = 2f;
        private const float VigilBurstRage = 3f;
        private const float VigilHoverRadius = 1.5f; // a crow that can't land stops this close to its exit

        private static readonly List<BirdPoint> exitBuffer = new List<BirdPoint>(4);
        private static readonly List<BirdHealth> crowBuffer = new List<BirdHealth>(16);

        private ShelterZone vigilZone;
        private BirdPoint vigilExit;
        private bool vigilLanding;
        private bool vigilHovering;
        private float vigilLandStartTime;
        private float nextVigilCheckTime;
        private float nextVigilRepathTime;

        partial void SubscribeVigil()
        {
            ShelterZone.PlayerExited += HandlePlayerLeftShelter;
        }

        partial void UnsubscribeVigil()
        {
            ShelterZone.PlayerExited -= HandlePlayerLeftShelter;
        }

        partial void UpdateVigilCheck()
        {
            ShelterZone zone = ShelterZone.PlayerInside;
            if (zone == null || Time.time < nextVigilCheckTime)
                return;
            nextVigilCheckTime = Time.time + VigilCheckInterval;

            if (Time.time - ShelterZone.PlayerInsideSince < config.vigilEnterDelay)
                return;
            if (state == CrowState.Vigil || state == CrowState.Funeral || state == CrowState.Enraged || IsCarrying)
                return;

            // My rank = how many living crows are closer to the shelter than me.
            Vector3 centre = zone.Centre;
            float mySq = (Motor.Position - centre).sqrMagnitude;
            BirdRegistry.GetAlive(BirdSpecies.Crow, crowBuffer);
            int rank = 0;
            foreach (BirdHealth other in crowBuffer)
                if (other != Health && (other.transform.position - centre).sqrMagnitude < mySq)
                    rank++;
            if (rank >= config.maxVigilCrows)
                return;

            exitBuffer.Clear();
            for (int i = 0; i < BirdPoint.All.Count; i++)
            {
                BirdPoint p = BirdPoint.All[i];
                if (p.Type == BirdPointType.ShelterExit && p.Group == zone.Group)
                    exitBuffer.Add(p);
            }
            if (exitBuffer.Count == 0)
                return;

            if (state == CrowState.StealFly)
                AbandonTheft();

            vigilZone = zone;
            vigilExit = exitBuffer[rank % exitBuffer.Count]; // spread over the exits, round-robin
            ChangeState(CrowState.Vigil);
        }

        partial void EnterVigil()
        {
            Motor.SpeedMultiplier = 1f;
            vigilLanding = false;
            nextVigilRepathTime = 0f;
            CrowStats.Vigils++;
        }

        partial void TickVigil()
        {
            if (vigilExit == null || ShelterZone.PlayerInside != vigilZone)
            {
                ChangeState(CrowState.Approach);
                return;
            }

            if (vigilLanding)
            {
                // BirdMotor.LandAt can circle its point forever when the bird arrives from the side (open bug in
                // Docs/IS/HANDOFF.md): its turn radius stays wider than the landing snap distance, and HoldPosition
                // steers the same way. So if the crow hasn't landed in time, it waits until its circle passes close to
                // the exit and simply stops there, hovering — still guarding the door, never circling.
                float landing = Time.time - vigilLandStartTime;
                if (!vigilHovering && !Motor.IsLanded && landing > config.vigilLandTimeout)
                {
                    Vector3 offset = Motor.Position - vigilExit.Position;
                    offset.y = 0f;
                    if (offset.sqrMagnitude <= VigilHoverRadius * VigilHoverRadius || landing > 2f * config.vigilLandTimeout)
                    {
                        Motor.Stop();
                        vigilHovering = true;
                    }
                }

                return; // perched (or hovering), waiting
            }

            Vector3 above = vigilExit.Position + Vector3.up * VigilApproachHeight;
            if ((above - Motor.Position).sqrMagnitude <= VigilLandDistance * VigilLandDistance)
            {
                Vector3 facing = vigilZone.Centre - vigilExit.Position;
                facing.y = 0f;
                Motor.LandAt(vigilExit.Position, facing.sqrMagnitude > 0.0001f ? facing.normalized : Vector3.forward);
                vigilLanding = true;
                vigilLandStartTime = Time.time;
                return;
            }

            if (Time.time >= nextVigilRepathTime && (RequestPath(above, ApproachCosts) || !LastRequestWasThrottled))
                nextVigilRepathTime = Time.time + config.repathInterval;
        }

        partial void ExitVigil()
        {
            vigilZone = null;
            vigilExit = null;
            vigilLanding = false;
            vigilHovering = false;
        }

        private void HandlePlayerLeftShelter(ShelterZone zone)
        {
            if (state != CrowState.Vigil || zone != vigilZone)
                return;

            rageDurationOverride = VigilBurstRage;
            ChangeState(CrowState.Enraged);
        }
    }
}
