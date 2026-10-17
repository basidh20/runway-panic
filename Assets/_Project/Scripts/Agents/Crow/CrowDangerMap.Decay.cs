using RunwayPanic.Core;
using UnityEngine;

namespace RunwayPanic.Agents.Crow
{
    /// <summary>
    /// Forgetting: every <c>dangerDecayInterval</c> seconds all danger is multiplied by <c>dangerDecayFactor</c>,
    /// and a new wave keeps only <c>dangerKeptOnNewWave</c> of it. With 0.9 every 5 s the half-life is
    /// 5 · ln 0.5 / ln 0.9 ≈ 33 s, so crows "forget" a spot about half a minute after the player leaves it.
    /// The cost is O(cells) once per interval, never per frame.
    /// </summary>
    public sealed partial class CrowDangerMap
    {
        private float decayTimer;
        private GameManager subscribedGameManager;

        partial void SubscribeDecay()
        {
            decayTimer = 0f;
        }

        partial void UnsubscribeDecay()
        {
            if (subscribedGameManager != null)
                subscribedGameManager.WaveStarted -= HandleWaveStarted;
            subscribedGameManager = null;
        }

        partial void TickDecay()
        {
            // The GameManager may be created after the map; connect once it exists.
            if (subscribedGameManager == null && GameManager.Instance != null)
            {
                subscribedGameManager = GameManager.Instance;
                subscribedGameManager.WaveStarted += HandleWaveStarted;
            }

            if (config == null)
                return;

            decayTimer += Time.deltaTime;
            if (decayTimer < config.dangerDecayInterval)
                return;

            decayTimer -= config.dangerDecayInterval;
            Scale(config.dangerDecayFactor);
        }

        private void HandleWaveStarted(int wave)
        {
            if (config != null)
                Scale(config.dangerKeptOnNewWave);
        }
    }
}
