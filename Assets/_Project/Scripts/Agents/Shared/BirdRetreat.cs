using UnityEngine;

namespace RunwayPanic.Agents
{
    /// <summary>
    /// Flies a surviving bird off the level when its wave's time runs out, instead of making it vanish. Added at
    /// runtime by the WaveManager through <see cref="Begin"/>: the bird is retired (no damage, no coins, not counted),
    /// its brain and attack are switched off, and it climbs away from the player until it is removed a few seconds
    /// later. The fly-off order is repeated every frame so nothing else (e.g. a flock) can redirect the bird.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BirdRetreat : MonoBehaviour
    {
        private const float ExitDistance = 60f;
        private const float ExitClimb = 30f;
        private const float SpeedBoost = 1.6f;

        private IBirdMotor motor;
        private Vector3 exitPoint;

        /// <summary>Starts the fly-off of <paramref name="bird"/>, away from <paramref name="awayFrom"/>, removed after <paramref name="lifetime"/> s.</summary>
        public static void Begin(BirdHealth bird, Vector3 awayFrom, float lifetime)
        {
            if (bird == null)
                return;
            if (!bird.IsAlive || bird.GetComponent<IBirdMotor>() == null)
            {
                Destroy(bird.gameObject);
                return;
            }

            bird.Retire();
            foreach (BirdBrain brain in bird.GetComponents<BirdBrain>())
                brain.Retreat();
            if (bird.TryGetComponent(out BirdContactAttack attack))
                attack.enabled = false;

            BirdRetreat retreat = bird.gameObject.AddComponent<BirdRetreat>();
            retreat.Setup(awayFrom);
            Destroy(bird.gameObject, lifetime);
        }

        private void Setup(Vector3 awayFrom)
        {
            motor = GetComponent<IBirdMotor>();
            Vector3 away = motor.Position - awayFrom;
            away.y = 0f;
            if (away.sqrMagnitude < 0.01f)
                away = transform.forward;
            exitPoint = motor.Position + away.normalized * ExitDistance + Vector3.up * ExitClimb;
            motor.SpeedMultiplier = SpeedBoost;
            motor.FlyTo(exitPoint);
        }

        private void Update()
        {
            if (motor == null)
                return;
            motor.SpeedMultiplier = SpeedBoost;
            motor.FlyTo(exitPoint); // the motor ignores a repeated identical order
        }
    }
}
