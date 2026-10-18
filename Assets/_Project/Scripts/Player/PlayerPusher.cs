using RunwayPanic.Interaction;
using UnityEngine;

namespace RunwayPanic.Player
{
    /// <summary>
    /// Lets the CharacterController push barricades by walking into them.
    /// A CharacterController never moves rigidbodies on its own, so this passes its movement on.
    /// Only <see cref="Barricade"/>s are pushed; other physics objects are left alone.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class PlayerPusher : MonoBehaviour
    {
        [Tooltip("Ignore contacts from mostly-downward movement, e.g. standing on top of a barricade.")]
        [SerializeField, Range(0f, 1f)] private float maxDownwardness = 0.3f;

        // Contacts arrive only on frames the controller moves into the barricade, so pushing counts as ongoing
        // for this long after the last one.
        private const float PushMemory = 0.15f;

        private float lastPushTime = float.NegativeInfinity;

        /// <summary>True while the player is pushing a barricade.</summary>
        public bool IsPushing => Time.time - lastPushTime < PushMemory;

        private void OnControllerColliderHit(ControllerColliderHit hit)
        {
            Rigidbody body = hit.collider.attachedRigidbody;
            if (body == null || body.isKinematic)
                return;

            if (!body.TryGetComponent(out Barricade barricade))
                return;

            Vector3 move = hit.moveDirection;
            if (move.y < -maxDownwardness)
                return;

            move.y = 0f;
            if (move.sqrMagnitude < 0.01f)
                return;

            barricade.Push(move);
            lastPushTime = Time.time;
        }
    }
}
