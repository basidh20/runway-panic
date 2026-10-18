namespace RunwayPanic.Agents
{
    /// <summary>
    /// What a brain tells the bird's animation. <see cref="BlendShapeBirdAnimator"/> implements it for now;
    /// the Agent Controller's Animator-based version (F4) replaces it without brain code changing.
    /// </summary>
    public interface IBirdAnimator
    {
        /// <summary>Flying (true) or perched/landed (false).</summary>
        void SetFlying(bool flying);

        /// <summary>Diving pose on or off (e.g. the hawk's stoop).</summary>
        void SetDiving(bool diving);

        /// <summary>How far the wings are spread: 0 = tucked, 1 = fully spread.</summary>
        void SetWingsSpread(float amount);

        /// <summary>One-shot reaction when the bird hits or is hit.</summary>
        void PlayHit();

        /// <summary>Death animation.</summary>
        void PlayDeath();
    }

    /// <summary>Animator that does nothing. <see cref="BirdBrain"/> uses it when a bird has no animator, so brains never null-check.</summary>
    public sealed class NullBirdAnimator : IBirdAnimator
    {
        /// <summary>The shared instance.</summary>
        public static readonly NullBirdAnimator Instance = new NullBirdAnimator();

        private NullBirdAnimator()
        {
        }

        /// <inheritdoc />
        public void SetFlying(bool flying)
        {
        }

        /// <inheritdoc />
        public void SetDiving(bool diving)
        {
        }

        /// <inheritdoc />
        public void SetWingsSpread(float amount)
        {
        }

        /// <inheritdoc />
        public void PlayHit()
        {
        }

        /// <inheritdoc />
        public void PlayDeath()
        {
        }
    }
}
