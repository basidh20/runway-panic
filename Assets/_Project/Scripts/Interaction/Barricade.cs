using UnityEngine;

namespace RunwayPanic.Interaction
{
    /// <summary>
    /// Heavy cover the player can shove into place by walking into it. It slides at a slow push speed,
    /// cannot tip over, and stops quickly once the player lets go. Bumps from birds barely move it.
    /// The player side is <see cref="RunwayPanic.Player.PlayerPusher"/>.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public sealed class Barricade : MonoBehaviour
    {
        [SerializeField, Min(1f)] private float mass = 300f;
        [Tooltip("Sliding speed while pushed, in m/s. Slower than walking, so it feels heavy.")]
        [SerializeField, Min(0.1f)] private float pushSpeed = 1.5f;
        [Tooltip("How fast it comes to rest after the push stops.")]
        [SerializeField, Min(0f)] private float stopDamping = 6f;

        private Rigidbody body;
        private float lastPushTime = float.NegativeInfinity;

        /// <summary>True while the player has pushed it within the last physics step or so.</summary>
        public bool IsBeingPushed => Time.time - lastPushTime < 0.1f;

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
            body.mass = mass;
            body.linearDamping = stopDamping;
            body.angularDamping = 5f;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            // Slide only: no tipping or spinning, so it always stays usable as cover.
            body.constraints = RigidbodyConstraints.FreezeRotation;
        }

        /// <summary>
        /// Pushes the barricade along <paramref name="direction"/> (only the horizontal part is used).
        /// Call it every frame the pusher is in contact.
        /// </summary>
        public void Push(Vector3 direction)
        {
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f)
                return;

            // Set (not add) the sliding velocity: this can be called several times per physics step,
            // once per contact and once per rendered frame, and must not stack up.
            Vector3 target = direction.normalized * pushSpeed;
            body.linearVelocity = new Vector3(target.x, body.linearVelocity.y, target.z);
            lastPushTime = Time.time;
        }
    }
}
