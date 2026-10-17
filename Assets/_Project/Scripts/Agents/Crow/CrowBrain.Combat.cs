using RunwayPanic.AI;
using UnityEngine;

namespace RunwayPanic.Agents.Crow
{
    /// <summary>
    /// Attack and retreat: from the approach point the crow dives at the player's chest and tries one contact
    /// attack, then breaks off to the far side and approaches again — from a new angle, thanks to the novelty term.
    /// </summary>
    public sealed partial class CrowBrain
    {
        private const float MaxAttackTime = 6f;
        private const float MaxRetreatTime = 5f;
        private const float RetreatExtraHeight = 3f;

        private Vector3 retreatPoint;
        private bool retreatPathRequested;

        partial void EnterAttack()
        {
            Motor.SpeedMultiplier = 1f;
        }

        partial void TickAttack()
        {
            Motor.FlyTo(PlayerChest);

            if (Attack != null && Attack.TryAttack())
            {
                CrowStats.AttackHits++;
                ChangeState(CrowState.Retreat);
            }
            else if (stateTime > MaxAttackTime)
            {
                // Couldn't connect (player dodging or hiding): reset with a fresh approach.
                ChangeState(CrowState.Retreat);
            }
        }

        partial void EnterRetreat()
        {
            // Continue past the player to the opposite side, at approach height plus a little.
            Vector3 away = Motor.Position - Hub.PlayerPosition;
            away.y = 0f;
            away = away.sqrMagnitude > 0.0001f ? -away.normalized : Vector3.forward;

            retreatPoint = Hub.PlayerPosition + away * config.retreatDistance;
            retreatPoint.y = GroundHeightAt(retreatPoint) + config.approachHeight + RetreatExtraHeight;
            if (!PathService.Current.IsFlyable(retreatPoint))
                retreatPoint = Hub.PlayerPosition - away * config.retreatDistance + Vector3.up * (config.approachHeight + RetreatExtraHeight);

            retreatPathRequested = false;
        }

        partial void TickRetreat()
        {
            AddFearSteering();

            if (!retreatPathRequested)
                retreatPathRequested = RequestPath(retreatPoint, ApproachCosts) || !LastRequestWasThrottled;

            if (stateTime > MaxRetreatTime || (retreatPathRequested && Motor.ReachedDestination))
                ChangeState(CrowState.Approach);
        }
    }
}
