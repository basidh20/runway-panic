using RunwayPanic.Core;

namespace RunwayPanic.Agents.Crow
{
    /// <summary>
    /// The grudge: every crow death stamps danger around the shooter's position and along the line of fire,
    /// so surviving crows learn to avoid where the player shoots from.
    /// </summary>
    public sealed partial class CrowDangerMap
    {
        // The line of fire is marked weaker than the shooter's spot: it is a corridor to cross carefully, not avoid.
        private const float LineStrengthFraction = 0.6f;

        partial void SubscribeGrudge()
        {
            BirdRegistry.BirdDied += HandleBirdDied;
        }

        partial void UnsubscribeGrudge()
        {
            BirdRegistry.BirdDied -= HandleBirdDied;
        }

        private void HandleBirdDied(BirdDeathInfo info)
        {
            if (info.Species != BirdSpecies.Crow || config == null)
                return;

            MarkKill(info.ShotOrigin, info.Position);
        }

        /// <summary>Stamps the danger for one crow killed at <paramref name="deathPosition"/> by a shot from <paramref name="shotOrigin"/>.</summary>
        public void MarkKill(UnityEngine.Vector3 shotOrigin, UnityEngine.Vector3 deathPosition)
        {
            Stamp(shotOrigin, config.dangerMarkRadius, config.dangerMarkStrength);
            StampLine(shotOrigin, deathPosition, config.dangerLineRadius, LineStrengthFraction * config.dangerMarkStrength);
            CrowStats.DangerStamps++;
        }
    }
}
