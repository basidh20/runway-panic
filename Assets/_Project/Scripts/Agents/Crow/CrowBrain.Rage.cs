namespace RunwayPanic.Agents.Crow
{
    /// <summary>
    /// Enraged: after a funeral the crow attacks fast and directly for a while. Rage overrides caution — it flies
    /// straight at the player and ignores the danger map — which makes it dangerous but predictable.
    /// </summary>
    public sealed partial class CrowBrain
    {
        private float rageDuration;

        // Set by other behaviours (e.g. the vigil) that want a shorter rage than a funeral gives. 0 = use config.
        private float rageDurationOverride;

        partial void EnterEnraged()
        {
            Motor.SpeedMultiplier = config.enragedSpeedMultiplier;
            rageDuration = rageDurationOverride > 0f ? rageDurationOverride : config.enragedDuration;
            rageDurationOverride = 0f;
            CrowStats.Enrages++;
        }

        partial void TickEnraged()
        {
            Motor.FlyTo(PlayerChest);

            if (Attack != null && Attack.TryAttack())
            {
                CrowStats.AttackHits++;
                PlayStrike();
            }

            if (stateTime >= rageDuration)
                ChangeState(CrowState.Approach);
        }

        partial void ExitEnraged()
        {
            Motor.SpeedMultiplier = 1f;
        }
    }
}
