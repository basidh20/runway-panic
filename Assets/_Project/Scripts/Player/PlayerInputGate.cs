using RunwayPanic.Core;

namespace RunwayPanic.Player
{
    /// <summary>
    /// One rule for every gameplay input (move, jump, shoot, reload, switch guns, use items, interact): it only works
    /// while the game is Playing and the player is alive. Pause, the shop, the end screens and the respawn delay all
    /// block it. Without a GameManager (sandbox scenes) only the alive check applies.
    /// </summary>
    public static class PlayerInputGate
    {
        /// <summary>True when the game state allows gameplay input (Playing, or no GameManager in the scene).</summary>
        public static bool IsGameplayState =>
            GameManager.Instance == null || GameManager.Instance.CurrentState == GameState.Playing;

        /// <summary>True when the player may act now: gameplay state and <paramref name="health"/> alive (null = alive).</summary>
        public static bool CanAct(PlayerHealth health) => IsGameplayState && (health == null || health.IsAlive);
    }
}
