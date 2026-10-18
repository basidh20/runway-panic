using UnityEngine;

namespace RunwayPanic.Agents.Gull
{
    /// <summary>Gull strategy tuning from BIRD_STRATEGIES §2 and 01_GULL_PLAN G1. Stats stay in BirdSpeciesData.</summary>
    [CreateAssetMenu(fileName = "GullStrategyConfig", menuName = "Runway Panic/AI/Gull Strategy")]
    public sealed class GullStrategyConfig : ScriptableObject
    {
        [Header("Kettle (G3)")]
        [Tooltip("Horizontal spiral radius, in metres.")]
        [Min(.1f)] public float kettleRadius = 8f;
        [Tooltip("Top slot height above ground (KettleCenter.y minus 10), NOT above the marker itself.")]
        [Min(.5f)] public float kettleTopHeight = 14f;
        [Tooltip("Bottom slot height above ground (KettleCenter.y minus 10), NOT above the marker itself.")]
        [Min(.5f)] public float kettleBottomHeight = 6f;
        [Tooltip("Spiral rotation, in degrees per second.")]
        [Min(0f)] public float kettleAngularSpeed = 25f;

        [Header("Predictive drop (G4)")]
        [Tooltip("Metres above the player where the dropper flies.")]
        [Min(.1f)] public float dropSlotHeight = 7f;
        [Tooltip("Warning time before releasing debris, in seconds.")]
        [Min(0f)] public float telegraphDuration = .6f;

        [Header("FOD (G2)")]
        [Tooltip("Splash radius in metres.")]
        [Min(0f)] public float fodSplashRadius = 1.5f;
        [Tooltip("Damage multiplier applied to species.AttackDamage; not a separate hard-coded damage stat.")]
        [Min(0f)] public float fodDamageMultiplier = 1f;
        [Tooltip("Seconds landed debris remains on the ground.")]
        [Min(.1f)] public float fodLifetime = 40f;
        [Tooltip("Maximum landed pieces; the oldest will be removed first.")]
        [Min(1)] public int maxFodOnGround = 20;
        [Tooltip("Seconds before one FodSpawn point can supply again.")]
        [Min(0f)] public float fodPickupCooldown = 6f;

        [Header("Distress (G5)")]
        [Tooltip("Metres within which gulls hear the distress call.")]
        [Min(0f)] public float distressRadius = 25f;
        [Tooltip("Multiplier of the shared motor's normal speed during one distress dive.")]
        [Min(1f)] public float distressDiveSpeedMultiplier = 2.2f;
        [Tooltip("Seconds before the same gull can respond to distress again.")]
        [Min(0f)] public float distressCooldown = 8f;

        [Header("Pathing")]
        [Tooltip("Seconds between shared A* requests; a refused budget must be retried next frame.")]
        [Min(.1f)] public float repathInterval = .75f;
    }
}
