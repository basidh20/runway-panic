using RunwayPanic.AI;
using RunwayPanic.Core;
using UnityEngine;

namespace RunwayPanic.Agents.Pigeon
{
    public sealed partial class PigeonBrain
    {
        private readonly ICostModifier[] pathCosts = { new FearCostModifier(BirdSpecies.Pigeon) };
        private float nextPath, nextAttack, nextJoin, lastProgressTime;
        private Vector3 recoveryGoal, roostGoal, progressPosition, steering;
        private bool landing, suspended;
        private float landingTime;

        public bool CanDive => IsAlive && isActiveAndEnabled && Motor != null && Attack != null && Attack.IsReady
            && Time.time >= nextAttack && (state == PigeonState.Flocking || state == PigeonState.Solo);

        public void ResetTactics()
        {
            nextPath = nextAttack = 0f;
            if (IsAlive && started && state != PigeonState.Dead)
                ChangeState(flock != null ? PigeonState.Flocking : PigeonState.Solo);
        }

        private void HandleDamage(float amount, float remaining)
        {
            if (remaining > 0f && flock != null) flock.ReportThreat(Health.LastHitShotOrigin, config.damageAlarm);
        }

        // Steering is consumed once per physics step, so submit it at physics frequency rather than render frequency.
        private void FixedUpdate()
        {
            if (!IsAlive || Motor == null || config == null) return;
            if (!IsGameplayActive)
            {
                Motor.Stop();
                steering = Vector3.zero;
                suspended = true;
                return;
            }
            if (suspended) { suspended = false; nextPath = 0f; }
            if (state != PigeonState.Flocking || flock == null) return;
            steering = flock.Steering(this);
            // Never let local boids steering push a path-following bird through nearby geometry.
            Vector3 predicted = Position + (Velocity + steering) * 0.35f;
            if (PigeonNavigation.Clear(Position, predicted)) Motor.AddSteering(steering);
        }

        private bool Navigate(Vector3 goal)
        {
            goal = PigeonNavigation.Clamp(goal);
            if (Time.time < nextPath) return true;
            if (Vector3.Distance(progressPosition, Position) > 1f)
            { progressPosition = Position; lastProgressTime = Time.time; }
            nextPath = Time.time + Mathf.Max(0.1f, config.repathSeconds);
            if (!PigeonNavigation.Free(goal)) { PigeonStats.PathFailures++; Motor.Stop(); return false; }
            if (PigeonNavigation.Clear(Position, goal))
            {
                Motor.FlyTo(goal);
                return true;
            }
            // The shared service falls back to an unchecked straight line without a grid.
            // A blocked route must remain blocked in that situation, not bypass the safety query.
            if (FlightGrid.Instance == null) { PigeonStats.PathFailures++; Motor.Stop(); return false; }
            if (RequestPath(goal, pathCosts)) return true;
            if (LastRequestWasThrottled) { nextPath = Time.time + 0.05f; return true; }
            PigeonStats.PathFailures++;
            Motor.Stop();
            return false;
        }

        partial void EnterFlocking()
        {
            if (flock != null) flock.ReleaseRoost(this);
            Motor.SpeedMultiplier = 1f;
            nextPath = 0f;
            lastProgressTime = Time.time;
            progressPosition = Position;
        }

        partial void TickFlocking()
        {
            if (flock == null) { ChangeState(PigeonState.Solo); return; }
            Vector3 goal = flock.FormationGoal(this);
            bool moving = Navigate(goal);
            if (!moving || (Time.time - lastProgressTime > config.gatherTimeout && Vector3.Distance(Position, goal) > 4f))
            {
                // A bounded escape attempt rather than retaining a permanently failed route.
                recoveryGoal = PigeonNavigation.Clamp(Position + Vector3.up * 3f);
                nextPath = 0f;
                Navigate(recoveryGoal);
                lastProgressTime = Time.time;
            }
        }

        partial void ExitFlocking() { steering = Vector3.zero; }

        partial void EnterDive()
        {
            PigeonStats.Dives++;
            Motor.SpeedMultiplier = config.diveSpeed;
            Anim.SetDiving(true);
        }

        partial void TickDive()
        {
            if (Hub == null || !Hub.HasPlayer || !Hub.Health.IsAlive || Hub.IsShieldActive || stateTime >= config.diveTimeout)
            { ChangeState(PigeonState.Recover); return; }
            float lead = Mathf.Min(0.4f, Vector3.Distance(Position, Hub.PlayerHeadPosition) / Mathf.Max(1f, Motor.BaseSpeed * config.diveSpeed));
            Vector3 aim = Hub.PlayerPosition + Vector3.up * 1.2f + Vector3.ClampMagnitude(Hub.PlayerVelocity * lead, 2f);
            if (!PigeonNavigation.Clear(Position, aim)) { ChangeState(PigeonState.Recover); return; }
            Motor.FlyTo(aim);
            if (Attack != null && Attack.TryAttack())
            { PigeonStats.Hits++; ChangeState(PigeonState.Recover); }
        }

        partial void ExitDive()
        {
            if (flock != null) flock.ReleaseDive(this);
            Anim.SetDiving(false);
            Motor.SpeedMultiplier = 1f;
            nextAttack = Time.time + config.soloCooldown;
        }

        partial void EnterRecover()
        {
            Vector3 away = Hub != null && Hub.HasPlayer ? Position - Hub.PlayerPosition : transform.forward;
            away.y = 0f;
            if (away.sqrMagnitude < 0.01f) away = transform.forward;
            recoveryGoal = PigeonNavigation.Clamp(Position + away.normalized * 6f + Vector3.up * 4f);
            nextPath = 0f;
        }

        partial void TickRecover()
        {
            Navigate(recoveryGoal);
            if (stateTime >= config.recoverSeconds)
                ChangeState(flock != null ? PigeonState.Flocking : PigeonState.Solo);
        }

        partial void EnterPanic()
        {
            if (flock != null) flock.ReleaseDive(this);
            Motor.SpeedMultiplier = 1.4f;
            Vector3 source = flock != null ? flock.ThreatPosition : Position - transform.forward;
            Vector3 away = Position - source;
            away.y = 0f;
            if (away.sqrMagnitude < 0.01f) away = transform.forward;
            float offset = ((GetEntityId().GetHashCode() & int.MaxValue) % 7) * 0.25f;
            recoveryGoal = PigeonNavigation.Clamp(Position + Quaternion.Euler(0f, offset * 30f, 0f) * away.normalized * 10f + Vector3.up * 3f);
            nextPath = 0f;
        }

        partial void TickPanic()
        {
            Navigate(recoveryGoal);
            if (flock == null && stateTime >= config.scatterSeconds) ChangeState(PigeonState.Solo);
            else if (flock != null && flock.Mode != PigeonFlockMode.Scattering) ChangeState(PigeonState.Flocking);
        }

        partial void ExitPanic() { Motor.SpeedMultiplier = 1f; }

        partial void EnterSolo()
        {
            if (flock != null) flock.ReleaseRoost(this);
            Motor.SpeedMultiplier = 1f; nextPath = 0f;
        }

        partial void TickSolo()
        {
            if (Time.time >= nextJoin)
            {
                nextJoin = Time.time + 2f;
                if (TryJoinFlock()) { ChangeState(PigeonState.Flocking); return; }
            }
            if (Hub == null || !Hub.HasPlayer || !Hub.Health.IsAlive) return;
            Vector3 away = Position - Hub.PlayerPosition;
            away.y = 0f;
            if (away.sqrMagnitude < 0.01f) away = transform.forward;
            Navigate(Hub.PlayerPosition + away.normalized * config.rallyRadius + Vector3.up * config.flightHeight);
            if (CanDive && stateTime >= config.soloCooldown && !Hub.IsShieldActive && PigeonNavigation.Clear(Position, Hub.PlayerHeadPosition))
                ChangeState(PigeonState.Dive);
        }

        partial void EnterFlyToRoost()
        {
            if (flock != null) flock.ReleaseDive(this);
            Motor.SpeedMultiplier = 1f;
            landing = false;
            nextPath = 0f;
            roostGoal = Position;
            if (flock == null || !flock.TryClaimRoost(this, out roostGoal))
            {
                Motor.Stop(); // No marker/slot: safe airborne rest, no invented landing surface.
                ChangeState(PigeonState.Roosting);
            }
        }

        partial void TickFlyToRoost()
        {
            Vector3 approach = roostGoal + Vector3.up * 2f;
            if (!landing && Vector3.Distance(Position, approach) < 2.5f && PigeonNavigation.Clear(Position, roostGoal))
            { landing = true; landingTime = stateTime; Motor.LandAt(roostGoal, transform.forward); }
            else if (!landing) Navigate(approach);
            if (Motor.IsLanded) { ChangeState(PigeonState.Roosting); return; }
            if (stateTime >= config.roostTimeout || (landing && stateTime - landingTime >= config.roostTimeout * 0.5f))
            { PigeonStats.LandingTimeouts++; Motor.Stop(); ChangeState(PigeonState.Roosting); }
        }

        partial void EnterRoosting() { Motor.Stop(); }
        partial void TickRoosting() { }
    }
}
