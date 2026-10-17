using UnityEngine;

namespace RunwayPanic.Agents.Hawk
{
    /// <summary>
    /// Every tunable number of the hawk's strategy: perch scoring, aggression meter, learning, feint, stoop, withdrawal,
    /// herding. One asset (<c>ScriptableObjects/Birds/Strategy/HawkStrategyConfig.asset</c>) shared by every hawk, so
    /// tuning in Play mode changes all of them and survives leaving Play mode.
    /// </summary>
    [CreateAssetMenu(fileName = "HawkStrategyConfig", menuName = "Runway Panic/AI/Hawk Strategy")]
    public sealed class HawkStrategyConfig : ScriptableObject
    {
        [Header("Perch scoring (utility AI)")]
        [Tooltip("Weight of the height term (how far the perch is above the player).")]
        [SerializeField, Min(0f)] private float wHeight = 0.35f;
        [Tooltip("Weight of the line-of-sight term (1 when the perch can see the player's head). Subtracted in Withdraw mode. " +
                 "0.45, not the plan's 0.30: at 0.30 a perch that can't see the player could win on sun + distance alone (playtest 17 Oct).")]
        [SerializeField, Min(0f)] private float wLineOfSight = 0.45f;
        [Tooltip("Weight of the distance term (1 at the ideal distance, falling off like a bell curve).")]
        [SerializeField, Min(0f)] private float wDistance = 0.20f;
        [Tooltip("Weight of the sun term (1 when the perch is between the player and the sun). Dropped in Withdraw mode.")]
        [SerializeField, Min(0f)] private float wSun = 0.15f;
        [Tooltip("Weight of the 'I was shot here recently' penalty (subtracted).")]
        [SerializeField, Min(0f)] private float wRecentlyShot = 0.40f;
        [Tooltip("Perch-to-player distance that scores best, in metres.")]
        [SerializeField, Min(0f)] private float idealDistance = 25f;
        [Tooltip("Width of the distance bell curve, in metres: at idealDistance ± this the term is ~0.37.")]
        [SerializeField, Min(0.1f)] private float distanceWidth = 10f;
        [Tooltip("Height above the player at which the height term reaches 1, in metres.")]
        [SerializeField, Min(0.1f)] private float maxHeightBonus = 15f;
        [Tooltip("Seconds between re-scoring the perches while flying to one.")]
        [SerializeField, Min(0.1f)] private float perchReevaluateInterval = 2f;
        [Tooltip("How fast a perch's 'recently shot' value fades, per second (1 = gone after 1 s).")]
        [SerializeField, Min(0f)] private float recentlyShotDecayPerSecond = 0.05f;
        [Tooltip("Hysteresis: a new perch must beat the current one's total by more than this before the hawk switches (stops flip-flopping).")]
        [SerializeField, Min(0f)] private float perchSwitchMargin = 0.15f;
        [Tooltip("Decision inertia: after choosing a perch or landing on one, the hawk doesn't consider switching for this many seconds " +
                 "(the sight term flips each time the player steps in or out of view, which outweighs the margin alone).")]
        [SerializeField, Min(0f)] private float perchCommitTime = 4f;

        [Header("Aggression meter")]
        [Tooltip("Meter value at which the hawk is ready to attack.")]
        [SerializeField, Min(1f)] private float meterMax = 100f;
        [Tooltip("Meter added once when the player starts reloading.")]
        [SerializeField, Min(0f)] private float reloadBonus = 40f;
        [Tooltip("Meter added once when the player switches weapon.")]
        [SerializeField, Min(0f)] private float switchBonus = 30f;
        [Tooltip("Meter added once when the player opens or closes a door.")]
        [SerializeField, Min(0f)] private float interactBonus = 35f;
        [Tooltip("Meter added per second while the player pushes a barricade.")]
        [SerializeField, Min(0f)] private float barricadeBonusPerSecond = 20f;
        [Tooltip("Meter added once when the player's shield runs out.")]
        [SerializeField, Min(0f)] private float shieldExpiredBonus = 30f;
        [Tooltip("Meter added per second while the player looks away from the hawk.")]
        [SerializeField, Min(0f)] private float lookAwayPerSecond = 25f;
        [Tooltip("Meter added per second while the player's health is low.")]
        [SerializeField, Min(0f)] private float lowHpPerSecond = 20f;
        [Tooltip("Meter added per second while the player is busy shooting other birds (Herd mode only).")]
        [SerializeField, Min(0f)] private float playerBusyPerSecond = 15f;
        [Tooltip("Meter removed per second while no weakness window is open.")]
        [SerializeField, Min(0f)] private float meterDecayPerSecond = 10f;
        [Tooltip("Angle between the camera's forward and the direction to the hawk above which the player counts as looking away, in degrees.")]
        [SerializeField, Range(0f, 180f)] private float lookAwayAngle = 100f;
        [Tooltip("Seconds the player must look away before the window opens.")]
        [SerializeField, Min(0f)] private float lookAwayMinTime = 1.5f;
        [Tooltip("Health fraction (current / max) below which the low-health window is open.")]
        [SerializeField, Range(0f, 1f)] private float lowHpFraction = 0.35f;
        [Tooltip("Seconds the weapon-switch window stays open.")]
        [SerializeField, Min(0f)] private float switchWindowDuration = 0.8f;
        [Tooltip("Seconds the door-interaction window stays open.")]
        [SerializeField, Min(0f)] private float interactWindowDuration = 1.2f;
        [Tooltip("Seconds the shield-expired window stays open.")]
        [SerializeField, Min(0f)] private float shieldWindowDuration = 2f;
        [Tooltip("Longest the hawk stays perched before it attacks anyway, in seconds (so it is never passive).")]
        [SerializeField, Min(1f)] private float maxPerchTime = 15f;
        [Tooltip("The player counts as busy while their last shot is younger than this, in seconds (Herd mode only).")]
        [SerializeField, Min(0f)] private float busyShotWindow = 2f;
        [Tooltip("Seconds between checks of whether any barricade is being pushed.")]
        [SerializeField, Min(0.02f)] private float barricadePollInterval = 0.2f;

        [Header("Learning (opponent model)")]
        [Tooltip("How strongly a window type that opens often gets a higher weight.")]
        [SerializeField, Min(0f)] private float learnRate = 0.15f;
        [Tooltip("Highest learned weight of a window type (cap, so the hawk never becomes unbeatable).")]
        [SerializeField, Min(1f)] private float maxWindowWeight = 2f;

        [Header("Feint")]
        [Tooltip("Guns with a fire interval at or above this count as slow (sniper, RPG), in seconds.")]
        [SerializeField, Min(0f)] private float slowGunFireInterval = 0.75f;
        [Tooltip("Chance of a feint before the real stoop when the player holds a slow gun (0..1).")]
        [SerializeField, Range(0f, 1f)] private float feintChance = 0.6f;
        [Tooltip("Distance from the player at which the feint pulls up, in metres.")]
        [SerializeField, Min(0f)] private float pullUpDistance = 6f;

        [Header("Stoop")]
        [Tooltip("Distance from the player of the stoop start point, towards the sun, in metres.")]
        [SerializeField, Min(1f)] private float stoopStartDistance = 25f;
        [Tooltip("Lowest height of the stoop start point above the player, in metres.")]
        [SerializeField, Min(0f)] private float minStoopHeight = 12f;
        [Tooltip("Speed multiplier during the dive.")]
        [SerializeField, Min(0.1f)] private float stoopSpeedMultiplier = 2.5f;
        [Tooltip("Speed multiplier while flying to the stoop start point.")]
        [SerializeField, Min(0.1f)] private float approachSpeedMultiplier = 1.5f;
        [Tooltip("Seconds the hawk hovers and screams at the top of the dive.")]
        [SerializeField, Min(0f)] private float screamHoverTime = 0.5f;
        [Tooltip("Damage multiplier of a stoop hit (times the species' attack damage).")]
        [SerializeField, Min(0f)] private float stoopDamageMultiplier = 1.5f;
        [Tooltip("Height above the player the hawk climbs to after an attack, in metres.")]
        [SerializeField, Min(0f)] private float recoverClimbHeight = 15f;

        [Header("Withdraw")]
        [Tooltip("Seconds the wounded hawk waits on its hidden perch before hunting again.")]
        [SerializeField, Min(0f)] private float withdrawWait = 4f;

        [Header("Herd")]
        [Tooltip("Fewest other alive birds needed for the hawk to switch to Herd mode.")]
        [SerializeField, Min(1)] private int herdMinOtherBirds = 3;
        [Tooltip("How far behind the flock (away from the player) the herd point is, in metres.")]
        [SerializeField, Min(0f)] private float herdOffset = 10f;
        [Tooltip("Radius of the circle the hawk flies around the herd point, in metres.")]
        [SerializeField, Min(0.5f)] private float herdOrbitRadius = 4f;
        [Tooltip("Height of the herd point above the flock's centre, in metres.")]
        [SerializeField, Min(0f)] private float herdHeight = 6f;
        [Tooltip("Radius of the fear field the hawk projects while herding, in metres.")]
        [SerializeField, Min(0f)] private float fearRadius = 12f;
        [Tooltip("Strength of the fear field (how hard other birds are pushed away).")]
        [SerializeField, Min(0f)] private float fearStrength = 1.5f;

        [Header("General")]
        [Tooltip("Seconds between path requests while following a moving target.")]
        [SerializeField, Min(0.1f)] private float repathInterval = 0.75f;

        /// <summary>Weight of the perch height term.</summary>
        public float WHeight => wHeight;
        /// <summary>Weight of the perch line-of-sight term.</summary>
        public float WLineOfSight => wLineOfSight;
        /// <summary>Weight of the perch distance term.</summary>
        public float WDistance => wDistance;
        /// <summary>Weight of the perch sun term.</summary>
        public float WSun => wSun;
        /// <summary>Weight of the recently-shot penalty.</summary>
        public float WRecentlyShot => wRecentlyShot;
        /// <summary>Best perch-to-player distance, in metres.</summary>
        public float IdealDistance => idealDistance;
        /// <summary>Width of the distance bell curve, in metres.</summary>
        public float DistanceWidth => distanceWidth;
        /// <summary>Height above the player at which the height term reaches 1, in metres.</summary>
        public float MaxHeightBonus => maxHeightBonus;
        /// <summary>Seconds between perch re-scores while flying to a perch.</summary>
        public float PerchReevaluateInterval => perchReevaluateInterval;
        /// <summary>Decay of a perch's recently-shot value, per second.</summary>
        public float RecentlyShotDecayPerSecond => recentlyShotDecayPerSecond;
        /// <summary>Score margin a new perch needs over the current one before the hawk switches.</summary>
        public float PerchSwitchMargin => perchSwitchMargin;
        /// <summary>Seconds after choosing or landing on a perch before a switch is considered.</summary>
        public float PerchCommitTime => perchCommitTime;

        /// <summary>Meter value at which the hawk is ready to attack.</summary>
        public float MeterMax => meterMax;
        /// <summary>Meter added when the player starts reloading.</summary>
        public float ReloadBonus => reloadBonus;
        /// <summary>Meter added when the player switches weapon.</summary>
        public float SwitchBonus => switchBonus;
        /// <summary>Meter added when the player uses a door.</summary>
        public float InteractBonus => interactBonus;
        /// <summary>Meter added per second while the player pushes a barricade.</summary>
        public float BarricadeBonusPerSecond => barricadeBonusPerSecond;
        /// <summary>Meter added when the player's shield expires.</summary>
        public float ShieldExpiredBonus => shieldExpiredBonus;
        /// <summary>Meter added per second while the player looks away.</summary>
        public float LookAwayPerSecond => lookAwayPerSecond;
        /// <summary>Meter added per second while the player's health is low.</summary>
        public float LowHpPerSecond => lowHpPerSecond;
        /// <summary>Meter added per second while the player is busy (Herd mode only).</summary>
        public float PlayerBusyPerSecond => playerBusyPerSecond;
        /// <summary>Meter removed per second while no window is open.</summary>
        public float MeterDecayPerSecond => meterDecayPerSecond;
        /// <summary>Look-away angle threshold, in degrees.</summary>
        public float LookAwayAngle => lookAwayAngle;
        /// <summary>Seconds of looking away before the window opens.</summary>
        public float LookAwayMinTime => lookAwayMinTime;
        /// <summary>Health fraction below which the low-health window is open.</summary>
        public float LowHpFraction => lowHpFraction;
        /// <summary>Seconds the weapon-switch window stays open.</summary>
        public float SwitchWindowDuration => switchWindowDuration;
        /// <summary>Seconds the door window stays open.</summary>
        public float InteractWindowDuration => interactWindowDuration;
        /// <summary>Seconds the shield-expired window stays open.</summary>
        public float ShieldWindowDuration => shieldWindowDuration;
        /// <summary>Longest time on a perch before attacking anyway, in seconds.</summary>
        public float MaxPerchTime => maxPerchTime;
        /// <summary>Age of the last shot under which the player counts as busy, in seconds.</summary>
        public float BusyShotWindow => busyShotWindow;
        /// <summary>Seconds between barricade push checks.</summary>
        public float BarricadePollInterval => barricadePollInterval;

        /// <summary>Learning rate of the window weights.</summary>
        public float LearnRate => learnRate;
        /// <summary>Cap of a learned window weight.</summary>
        public float MaxWindowWeight => maxWindowWeight;

        /// <summary>Fire interval at or above which a gun counts as slow, in seconds.</summary>
        public float SlowGunFireInterval => slowGunFireInterval;
        /// <summary>Chance of a feint against a slow gun (0..1).</summary>
        public float FeintChance => feintChance;
        /// <summary>Distance from the player at which a feint pulls up, in metres.</summary>
        public float PullUpDistance => pullUpDistance;

        /// <summary>Distance of the stoop start point from the player, in metres.</summary>
        public float StoopStartDistance => stoopStartDistance;
        /// <summary>Lowest height of the stoop start point above the player, in metres.</summary>
        public float MinStoopHeight => minStoopHeight;
        /// <summary>Speed multiplier during the dive.</summary>
        public float StoopSpeedMultiplier => stoopSpeedMultiplier;
        /// <summary>Speed multiplier on the way to the stoop start point.</summary>
        public float ApproachSpeedMultiplier => approachSpeedMultiplier;
        /// <summary>Seconds of hovering and screaming before the dive.</summary>
        public float ScreamHoverTime => screamHoverTime;
        /// <summary>Damage multiplier of a stoop hit.</summary>
        public float StoopDamageMultiplier => stoopDamageMultiplier;
        /// <summary>Climb height above the player after an attack, in metres.</summary>
        public float RecoverClimbHeight => recoverClimbHeight;

        /// <summary>Seconds waited on the hidden perch after withdrawing.</summary>
        public float WithdrawWait => withdrawWait;

        /// <summary>Fewest other alive birds for Herd mode.</summary>
        public int HerdMinOtherBirds => herdMinOtherBirds;
        /// <summary>Distance of the herd point behind the flock, in metres.</summary>
        public float HerdOffset => herdOffset;
        /// <summary>Orbit radius around the herd point, in metres.</summary>
        public float HerdOrbitRadius => herdOrbitRadius;
        /// <summary>Height of the herd point above the flock's centre, in metres.</summary>
        public float HerdHeight => herdHeight;
        /// <summary>Radius of the hawk's fear field, in metres.</summary>
        public float FearRadius => fearRadius;
        /// <summary>Strength of the hawk's fear field.</summary>
        public float FearStrength => fearStrength;

        /// <summary>Seconds between path requests while following a moving target.</summary>
        public float RepathInterval => repathInterval;
    }
}
