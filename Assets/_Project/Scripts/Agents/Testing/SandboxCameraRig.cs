using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RunwayPanic.Agents.Testing
{
    /// <summary>
    /// Sandbox camera for testing and recording the demo: 0 = the overview it starts at, 1-9 = chase that bird from
    /// behind and slightly above, so its banking and wing beats are visible up close.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class SandboxCameraRig : MonoBehaviour
    {
        [Tooltip("Birds reachable with the number keys 1-9, in order.")]
        [SerializeField] private Transform[] birds = new Transform[0];
        [Tooltip("Chase distance behind the bird, in metres.")]
        [SerializeField, Min(0.5f)] private float followDistance = 3.5f;
        [Tooltip("Chase height above the bird, in metres.")]
        [SerializeField] private float followHeight = 1.2f;
        [Tooltip("Seconds the camera takes to catch up. Higher = smoother, more lag.")]
        [SerializeField, Range(0.01f, 1f)] private float smoothTime = 0.25f;

        private Vector3 overviewPosition;
        private Quaternion overviewRotation;
        private Transform target;
        private Vector3 velocity;

        private void Awake()
        {
            overviewPosition = transform.position;
            overviewRotation = transform.rotation;
        }

        /// <summary>The birds the number keys select, in order.</summary>
        public IReadOnlyList<Transform> Birds => birds;

        /// <summary>Back to the starting overview.</summary>
        public void ShowOverview()
        {
            target = null;
            transform.SetPositionAndRotation(overviewPosition, overviewRotation);
        }

        /// <summary>Chases bird <paramref name="index"/> (0-based; key 1 = index 0).</summary>
        public void Follow(int index)
        {
            if (index >= 0 && index < birds.Length && birds[index] != null)
                target = birds[index];
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
                return;

            if (keyboard.digit0Key.wasPressedThisFrame)
                ShowOverview();

            int count = Mathf.Min(birds.Length, 9);
            for (int i = 0; i < count; i++)
            {
                if (keyboard[Key.Digit1 + i].wasPressedThisFrame)
                    Follow(i);
            }
        }

        private void LateUpdate()
        {
            if (target == null || !target.gameObject.activeInHierarchy)
                return;

            // Behind the bird along its flat heading, so the camera does not roll with the bank we want to see.
            Vector3 back = -target.forward;
            back.y = 0f;
            back = back.sqrMagnitude > 1e-4f ? back.normalized : Vector3.back;
            Vector3 wanted = target.position + back * followDistance + Vector3.up * followHeight;

            transform.position = Vector3.SmoothDamp(transform.position, wanted, ref velocity, smoothTime);
            transform.rotation = Quaternion.LookRotation(target.position - transform.position, Vector3.up);
        }
    }
}
