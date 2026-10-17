using System.Collections.Generic;
using RunwayPanic.Interaction;
using UnityEngine;

namespace RunwayPanic.Agents.Crow
{
    /// <summary>
    /// Theft: while patrolling or approaching, crows look for medkits and shields lying unguarded (not right under
    /// the player's feet), claim one, fly it to the nearest Stash marker and drop it there. The player can chase the
    /// thief or walk to the stash to get it back. Goal priority: stealing beats attacking.
    /// </summary>
    public sealed partial class CrowBrain
    {
        private const string HeadBoneName = "Head";
        private const float GrabDistance = 1.2f;
        private const float FinalHopDistance = 3f;
        private const float DeliverDistance = 1.5f;
        private const float HoverAbovePickup = 0.8f;
        private const float MaxTheftTime = 25f;
        private const float StashFallbackDistance = 25f;
        private const float UnreachableMemory = 15f;

        private static readonly List<BirdPoint> stashBuffer = new List<BirdPoint>(4);

        private Pickup targetPickup;
        private CarriedPickup carried;
        private Transform headSocket;
        private Vector3 stashTarget;
        private float nextTheftScanTime;
        private float nextTheftRepathTime;

        partial void CheckCarrying(ref bool carrying)
        {
            carrying = carried != null && carried.Item != null;
        }

        partial void UpdateTheftScan()
        {
            if (Time.time < nextTheftScanTime || (carried != null && carried.Item != null))
                return;
            nextTheftScanTime = Time.time + config.stealScanInterval;

            if (CrowPickupClaims.ThiefCount >= config.maxConcurrentThieves)
                return;

            Pickup best = null;
            float bestSq = config.stealMaxDistance * config.stealMaxDistance;
            float guardSq = config.minPlayerDistanceToSteal * config.minPlayerDistanceToSteal;
            Vector3 position = Motor.Position;
            BirdPoint.GetAll(BirdPointType.Stash, stashBuffer);

            foreach (Pickup p in CrowPickupClaims.Pickups)
            {
                if (p == null || !p.isActiveAndEnabled || CrowPickupClaims.IsClaimed(p) || CrowPickupClaims.IsUnreachable(p) ||
                    CrowPickupClaims.IsStashed(p) || p.GetComponentInParent<CrowBrain>() != null || IsAtStash(p.transform.position))
                    continue; // already crow loot, or out of reach
                if ((p.transform.position - Hub.PlayerPosition).sqrMagnitude < guardSq)
                    continue; // the player is standing on it

                float sq = (p.transform.position - position).sqrMagnitude;
                if (sq < bestSq)
                {
                    bestSq = sq;
                    best = p;
                }
            }

            if (best != null && CrowPickupClaims.TryClaim(best, this, config.maxConcurrentThieves))
            {
                targetPickup = best;
                CrowStats.TheftsAttempted++;
                ChangeState(CrowState.StealFly);
            }
        }

        partial void EnterStealFly()
        {
            Motor.SpeedMultiplier = 1f;
            nextTheftRepathTime = 0f;
        }

        partial void TickStealFly()
        {
            // The player collected it first (destroyed), or it vanished: give up.
            if (targetPickup == null || !targetPickup.isActiveAndEnabled || stateTime > MaxTheftTime)
            {
                AbandonTheft();
                ChangeState(CrowState.Patrol);
                return;
            }

            Vector3 above = targetPickup.transform.position + Vector3.up * HoverAbovePickup;
            float sq = (above - Motor.Position).sqrMagnitude;

            if (sq <= GrabDistance * GrabDistance)
            {
                carried ??= new CarriedPickup();
                carried.Attach(targetPickup, HeadSocket);
                ChangeState(CrowState.CarryToStash);
                return;
            }

            if (sq <= FinalHopDistance * FinalHopDistance)
            {
                Motor.FlyTo(above); // grid paths stay ≥ 1.5 m up; the last hop is direct
                return;
            }

            if (Time.time < nextTheftRepathTime)
                return;

            if (RequestPath(above, ApproachCosts))
            {
                nextTheftRepathTime = Time.time + config.repathInterval;
            }
            else if (!LastRequestWasThrottled)
            {
                // No route (e.g. it lies inside a sealed shelter): remember that for everyone and do something else.
                CrowPickupClaims.MarkUnreachable(targetPickup, Time.time + UnreachableMemory);
                AbandonTheft();
                ChangeState(CrowState.Patrol);
            }
        }

        partial void EnterCarryToStash()
        {
            nextTheftRepathTime = 0f;
            stashTarget = NearestStash();
        }

        partial void TickCarryToStash()
        {
            if (carried == null || carried.Item == null)
            {
                ChangeState(CrowState.Patrol);
                return;
            }

            Vector3 target = stashTarget + Vector3.up * HoverAbovePickup;
            float sq = (target - Motor.Position).sqrMagnitude;
            if (sq <= DeliverDistance * DeliverDistance || stateTime > MaxTheftTime)
            {
                DeliverLoot(stashTarget);
                return;
            }

            if (sq <= FinalHopDistance * FinalHopDistance)
            {
                Motor.FlyTo(target);
                return;
            }

            if (Time.time < nextTheftRepathTime)
                return;

            if (RequestPath(target, ApproachCosts))
            {
                nextTheftRepathTime = Time.time + config.repathInterval;
            }
            else if (!LastRequestWasThrottled)
            {
                // The stash can't be reached from here: drop the loot where we are — it's still been moved away.
                DeliverLoot(Motor.Position);
            }
        }

        // Puts the loot down as delivered: crow loot from now on, never stolen again (until the player takes it back).
        private void DeliverLoot(Vector3 near)
        {
            if (carried != null)
                CrowPickupClaims.MarkStashed(carried.Item);
            DropLoot(near);
            CrowStats.TheftsSucceeded++;
            ChangeState(CrowState.Patrol);
        }

        /// <summary>Drops whatever is carried at <paramref name="near"/> and releases the claim.</summary>
        private void DropLoot(Vector3 near)
        {
            if (carried != null && carried.Item != null)
            {
                Pickup item = carried.Item;
                carried.Drop(near);
                CrowPickupClaims.Release(item);
            }

            targetPickup = null;
        }

        // Loot already lying in a stash is "ours": crows guard it rather than steal it again.
        // Uses stashBuffer, which the caller has just filled.
        private static bool IsAtStash(Vector3 position)
        {
            foreach (BirdPoint stash in stashBuffer)
            {
                float r = stash.Radius + 0.5f;
                Vector3 d = position - stash.Position;
                d.y = 0f;
                if (d.sqrMagnitude <= r * r)
                    return true;
            }

            return false;
        }

        /// <summary>Gives up a theft that hasn't picked anything up yet.</summary>
        private void AbandonTheft()
        {
            if (targetPickup != null)
                CrowPickupClaims.Release(targetPickup);
            targetPickup = null;
        }

        private Transform HeadSocket
        {
            get
            {
                if (headSocket == null)
                    headSocket = FindChild(transform, HeadBoneName) ?? transform;
                return headSocket;
            }
        }

        // Nearest Stash marker; without one, a spot far from the player so the theft still hurts.
        private Vector3 NearestStash()
        {
            BirdPoint.GetAll(BirdPointType.Stash, stashBuffer);
            Vector3 position = Motor.Position;
            Vector3 best = default;
            float bestSq = float.PositiveInfinity;
            foreach (BirdPoint point in stashBuffer)
            {
                float sq = (point.Position - position).sqrMagnitude;
                if (sq < bestSq)
                {
                    bestSq = sq;
                    best = point.Position;
                }
            }

            if (bestSq < float.PositiveInfinity)
                return best;

            Vector3 away = position - Hub.PlayerPosition;
            away.y = 0f;
            away = away.sqrMagnitude > 0.0001f ? away.normalized : Vector3.forward;
            Vector3 fallback = Hub.PlayerPosition + away * StashFallbackDistance;
            fallback.y = GroundHeightAt(fallback);
            return fallback;
        }

        private static Transform FindChild(Transform parent, string childName)
        {
            foreach (Transform child in parent)
            {
                if (child.name == childName)
                    return child;
                Transform found = FindChild(child, childName);
                if (found != null)
                    return found;
            }

            return null;
        }
    }
}
