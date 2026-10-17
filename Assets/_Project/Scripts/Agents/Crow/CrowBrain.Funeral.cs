using RunwayPanic.Core;
using UnityEngine;

namespace RunwayPanic.Agents.Crow
{
    /// <summary>
    /// Funeral: when a crow dies, nearby crows fly to the body and circle above it slowly for a few seconds
    /// (easy targets — the player's chance), then become enraged. Goal priority: mourning beats stealing and
    /// attacking; a crow carrying loot keeps it and skips the funeral.
    /// </summary>
    public sealed partial class CrowBrain
    {
        private const float FuneralSpeedMultiplier = 0.7f;
        private const int FuneralGizmoSegments = 24;

        private Vector3 funeralCentre;
        private float funeralAngle;

        partial void SubscribeFuneral()
        {
            BirdRegistry.BirdDied += HandleAllyDied;
        }

        partial void UnsubscribeFuneral()
        {
            BirdRegistry.BirdDied -= HandleAllyDied;
        }

        private void HandleAllyDied(BirdDeathInfo info)
        {
            if (!started || info.Species != BirdSpecies.Crow || info.Bird == Health)
                return;
            if (state == CrowState.Dead || state == CrowState.Funeral || state == CrowState.Enraged || state == CrowState.Vigil || IsCarrying)
                return;

            float radius = config.funeralRadius;
            if ((info.Position - Motor.Position).sqrMagnitude > radius * radius)
                return;

            if (state == CrowState.StealFly)
                AbandonTheft();

            funeralCentre = info.Position;
            ChangeState(CrowState.Funeral);
        }

        partial void EnterFuneral()
        {
            Motor.SpeedMultiplier = FuneralSpeedMultiplier;
            Vector3 offset = Motor.Position - funeralCentre;
            funeralAngle = Mathf.Atan2(offset.z, offset.x); // join the circle where we already are
            CrowStats.FuneralAttendances++;
        }

        partial void TickFuneral()
        {
            // Advance around the circle at the bird's (slowed) speed: angular speed = linear speed / radius.
            float radius = config.funeralOrbitRadius;
            funeralAngle += Motor.BaseSpeed * FuneralSpeedMultiplier / radius * Time.deltaTime;
            Vector3 orbitPoint = funeralCentre + Vector3.up * config.funeralOrbitHeight
                                 + new Vector3(Mathf.Cos(funeralAngle), 0f, Mathf.Sin(funeralAngle)) * radius;
            Motor.FlyTo(orbitPoint);

            if (stateTime >= config.funeralDuration)
                ChangeState(CrowState.Enraged);
        }

        partial void ExitFuneral()
        {
            Motor.SpeedMultiplier = 1f;
        }

        partial void DrawFuneralGizmos()
        {
            if (state != CrowState.Funeral)
                return;

            Gizmos.color = new Color(0.5f, 0.5f, 0.5f);
            Gizmos.DrawWireSphere(funeralCentre, 0.3f);
            Vector3 centre = funeralCentre + Vector3.up * config.funeralOrbitHeight;
            float radius = config.funeralOrbitRadius;
            Vector3 previous = centre + new Vector3(radius, 0f, 0f);
            for (int i = 1; i <= FuneralGizmoSegments; i++)
            {
                float a = i * Mathf.PI * 2f / FuneralGizmoSegments;
                Vector3 next = centre + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius;
                Gizmos.DrawLine(previous, next);
                previous = next;
            }
        }
    }
}
