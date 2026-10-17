using UnityEngine;

namespace RunwayPanic.Agents
{
    /// <summary>
    /// Test and example brain: chases the player along A* paths, re-planning every few tenths of a second, and
    /// hovers once close. Checks the whole chain brain → PathService → motor before the real species brains exist,
    /// and shows species owners the minimum a brain needs. Not used in the game.
    /// </summary>
    public sealed class TestChaseBrain : BirdBrain
    {
        private const string StateWaiting = "Test/Waiting";
        private const string StateChasing = "Test/Chasing";
        private const string StateClose = "Test/Close";
        private const string StateNoPath = "Test/NoPath";

        [Tooltip("Seconds between path requests while chasing.")]
        [SerializeField, Min(0.1f)] private float repathInterval = 0.75f;
        [Tooltip("Height above the player's feet the bird aims for, in metres.")]
        [SerializeField, Min(0f)] private float aimHeight = 2.5f;
        [Tooltip("Within this distance the bird stops path-finding and hovers next to the player, in metres.")]
        [SerializeField, Min(0.5f)] private float closeDistance = 3f;

        private string state = StateWaiting;
        private float nextRepathTime;

        /// <inheritdoc />
        public override string CurrentStateName => state;

        private void Update()
        {
            if (!IsGameplayActive || Hub == null || !Hub.HasPlayer)
            {
                state = StateWaiting;
                return;
            }

            Vector3 target = Hub.PlayerPosition + Vector3.up * aimHeight;
            if ((target - Motor.Position).sqrMagnitude < closeDistance * closeDistance)
            {
                state = StateClose;
                Motor.HoldPosition(target);
                return;
            }

            if (Time.time < nextRepathTime)
                return;

            // A throttled request leaves the timer unchanged, so it is retried next frame.
            if (RequestPath(target))
            {
                state = StateChasing;
                nextRepathTime = Time.time + repathInterval;
            }
            else if (!LastRequestWasThrottled)
            {
                state = StateNoPath;
                nextRepathTime = Time.time + repathInterval;
            }
        }
    }
}
