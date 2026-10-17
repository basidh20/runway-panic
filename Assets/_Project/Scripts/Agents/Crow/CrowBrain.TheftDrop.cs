namespace RunwayPanic.Agents.Crow
{
    /// <summary>
    /// Shooting a thief gets the loot back: a crow that dies while carrying drops the pickup where it falls,
    /// and a crow that dies on the way to a pickup releases its claim so another crow (or the player) can have it.
    /// </summary>
    public sealed partial class CrowBrain
    {
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
