using RunwayPanic.AI;
using RunwayPanic.Core;
using UnityEngine;

namespace RunwayPanic.Agents.Crow
{
    /// <summary>
    /// Evasion: a crow that is patrolling, approaching, attacking or retreating dodges when a shot passes close without
    /// hitting it — a sideways jink away from the line of fire, then a fresh approach (the novelty term in
    /// <see cref="ApproachPointSelector"/> makes it come from a new side; a patrolling crow has now spotted the
    /// shooter). Missing a crow therefore costs the player the crow's position, not just the bullet. Patrol is included
    /// because the sniper (the crow's gun) reaches 150 m: crows are mostly shot at while still patrolling.
    /// </summary>
    public sealed partial class CrowBrain
    {
        private const float EvadeArriveDistance = 1f;
        private const float EvadeLift = 1f;

        private PlayerEventHub evasionHub; // the hub subscribed to, so the same one is unsubscribed
        private Vector3 evadeTarget;
        private float nextEvadeTime;

        partial void SubscribeEvasion()
        {
            evasionHub = PlayerEventHub.Instance;
            if (evasionHub != null)
                evasionHub.ShotFired += HandleNearMiss;
        }

        partial void UnsubscribeEvasion()
        {
            if (evasionHub != null)
                evasionHub.ShotFired -= HandleNearMiss;
            evasionHub = null;
        }

        private void HandleNearMiss(Vector3 origin, Vector3 end, IDamageable target)
        {
            if (!started || Time.time < nextEvadeTime || ReferenceEquals(target, Health))
                return; // a hit is not a near miss
            if (state != CrowState.Patrol && state != CrowState.Approach && state != CrowState.Attack &&
                state != CrowState.Retreat)
                return; // funeral, theft, rage and vigil have their own priorities

            Vector3 position = Motor.Position;
            Vector3 shot = end - origin;
            float lengthSq = shot.sqrMagnitude;
            if (lengthSq < 0.0001f)
                return;

            Vector3 closest = origin + shot * Mathf.Clamp01(Vector3.Dot(position - origin, shot) / lengthSq);
            Vector3 away = position - closest;
            if (away.sqrMagnitude > config.evadeRadius * config.evadeRadius)
                return;

            // Sideways to the shot, on the side the crow already is (random if it's right on the line).
            Vector3 side = Vector3.Cross(shot, Vector3.up).normalized;
            if (side.sqrMagnitude < 0.0001f)
                side = Vector3.Cross(shot, Vector3.right).normalized;
            float which = Vector3.Dot(away, side);
            if (Mathf.Abs(which) < 0.01f)
                which = Random.value - 0.5f;
            if (which < 0f)
                side = -side;

            if (!TryEvadeTarget(position, side, out evadeTarget) && !TryEvadeTarget(position, -side, out evadeTarget))
                return; // boxed in: nowhere to dodge to

            nextEvadeTime = Time.time + config.evadeCooldown;
            ChangeState(CrowState.Evade);
        }

        private bool TryEvadeTarget(Vector3 position, Vector3 side, out Vector3 target)
        {
            target = position + side * config.evadeDistance + Vector3.up * EvadeLift;
            return PathService.Current.IsFlyable(target);
        }

        partial void EnterEvade()
        {
            Motor.SpeedMultiplier = config.evadeSpeedMultiplier;
            CrowStats.Evasions++;
        }

        partial void TickEvade()
        {
            Motor.FlyTo(evadeTarget);

            bool arrived = (evadeTarget - Motor.Position).sqrMagnitude <= EvadeArriveDistance * EvadeArriveDistance;
            if (arrived || stateTime > config.evadeDuration)
                ChangeState(CrowState.Approach); // picks a new approach point, preferring a new side
        }

        partial void ExitEvade()
        {
            Motor.SpeedMultiplier = 1f;
        }
    }
}
