namespace RunwayPanic.Agents.Crow
{
    /// <summary>
    /// Shooting a thief gets the loot back: a crow that dies while carrying drops the pickup where it falls,
    /// and a crow that dies on the way to a pickup releases its claim so another crow (or the player) can have it.
    /// </summary>
    public sealed partial class CrowBrain
    {
        /// <summary>
        /// Flying off at the end of a wave: drop the loot here (it must not leave the level with the crow, or the
        /// pickup spawner would lose that pickup for the rest of the game).
        /// </summary>
        public override void Retreat()
        {
            if (carried != null && carried.Item != null)
                DropLoot(Motor.Position);
            else
                AbandonTheft();
            base.Retreat();
        }

        partial void OnDiedDropLoot()
        {
            if (carried != null && carried.Item != null)
            {
                DropLoot(Motor.Position);
                CrowStats.TheftsRecovered++;
            }
            else
            {
                AbandonTheft();
            }
        }
    }
}
