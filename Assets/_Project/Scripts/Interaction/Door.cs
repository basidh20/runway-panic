using System;
using RunwayPanic.Core;
using UnityEngine;

namespace RunwayPanic.Interaction
{
    /// <summary>
    /// Physics door on a vertical HingeJoint. The joint's spring swings it open or shut, so it moves with
    /// real physics and settles naturally. It always opens away from whoever uses it.
    /// The hinge sits on the panel's local -X edge; the panel's forward (+Z) is the side it opens away from.
    /// Safe rooms can drive it through <see cref="Open"/>, <see cref="Close"/> and <see cref="SetLocked"/>.
    /// </summary>
    [RequireComponent(typeof(Rigidbody), typeof(HingeJoint))]
    public sealed class Door : MonoBehaviour, IInteractable
    {
        [Header("Swing")]
        [SerializeField, Range(10f, 170f)] private float openAngle = 100f;
        [Tooltip("Spring strength pulling the door to its target angle.")]
        [SerializeField, Min(0f)] private float spring = 1200f;
        [SerializeField, Min(0f)] private float damper = 150f;

        [Header("State")]
        [SerializeField] private bool startLocked;

        [Header("Prompts")]
        [SerializeField] private string openPrompt = "Open door";
        [SerializeField] private string closePrompt = "Close door";

        /// <summary>Raised when the door starts opening.</summary>
        public event Action Opened;

        /// <summary>Raised when the door starts closing.</summary>
        public event Action Closed;

        private HingeJoint hinge;
        private Vector3 closedForward;
        private Vector3 closedPosition;

        public bool IsOpen { get; private set; }
        public bool IsLocked { get; private set; }

        /// <summary>Current swing in degrees (0 = shut). Sign shows which way it opened.</summary>
        public float CurrentAngle => hinge != null ? hinge.angle : 0f;

        public bool CanInteract => !IsLocked;
        public string InteractionPrompt => IsOpen ? closePrompt : openPrompt;

        private void Awake()
        {
            hinge = GetComponent<HingeJoint>();
            closedForward = transform.forward;
            closedPosition = transform.position;

            Rigidbody body = GetComponent<Rigidbody>();
            body.useGravity = false;   // The hinge carries the weight; gravity only adds sag and jitter.
            body.interpolation = RigidbodyInterpolation.Interpolate;

            hinge.axis = Vector3.up;
            hinge.useLimits = true;
            hinge.limits = new JointLimits { min = -openAngle, max = openAngle };
            hinge.useSpring = true;

            IsLocked = startLocked;
            SetTargetAngle(0f);
        }

        public void Interact(GameObject player)
        {
            if (IsOpen)
                Close();
            else
                Open(player != null ? player.transform.position : (Vector3?)null);
        }

        /// <summary>
        /// Opens the door, swinging away from <paramref name="awayFrom"/> when given. Does nothing while locked.
        /// </summary>
        public void Open(Vector3? awayFrom = null)
        {
            if (IsLocked || IsOpen)
                return;

            // A positive hinge angle swings the free edge toward -forward, i.e. away from someone standing in front.
            float direction = 1f;
            if (awayFrom.HasValue)
            {
                direction = Vector3.Dot(awayFrom.Value - closedPosition, closedForward) >= 0f ? 1f : -1f;
            }

            IsOpen = true;
            SetTargetAngle(openAngle * direction);
            Opened?.Invoke();
        }

        public void Close()
        {
            if (!IsOpen)
                return;

            IsOpen = false;
            SetTargetAngle(0f);
            Closed?.Invoke();
        }

        /// <summary>
        /// Locks or unlocks the door. Locking also closes it, so a safe room can shut birds out.
        /// </summary>
        public void SetLocked(bool locked)
        {
            IsLocked = locked;
            if (locked)
                Close();
        }

        private void SetTargetAngle(float angle)
        {
            hinge.spring = new JointSpring { spring = spring, damper = damper, targetPosition = angle };
        }
    }
}
