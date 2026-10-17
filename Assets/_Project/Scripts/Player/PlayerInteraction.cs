using System;
using RunwayPanic.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RunwayPanic.Player
{
    /// <summary>
    /// Finds the <see cref="IInteractable"/> under the crosshair within reach of the player and uses it
    /// when Interact (E) is pressed. Solid objects in front block the check; trigger volumes that are
    /// not interactable are looked through.
    /// </summary>
    [RequireComponent(typeof(PlayerInput))]
    public sealed class PlayerInteraction : MonoBehaviour
    {
        private const string InteractActionName = "Interact";
        private const int MaxHits = 16;

        [Tooltip("Camera used for aiming. Uses Camera.main if left empty.")]
        [SerializeField] private Camera aimCamera;
        [Tooltip("How far from the player's chest an object can be used.")]
        [SerializeField, Min(0.5f)] private float interactRange = 3f;
        [SerializeField, Min(0f)] private float chestHeight = 1.2f;
        [SerializeField] private LayerMask interactLayers = ~0;

        /// <summary>Raised when the focused object or its prompt changes. Argument: new target, or null.</summary>
        public event Action<IInteractable> FocusChanged;

        /// <summary>Raised after the player uses an object. Argument: the object.</summary>
        public event Action<IInteractable> Interacted;

        private readonly RaycastHit[] hitBuffer = new RaycastHit[MaxHits];
        private InputAction interactAction;
        private PlayerHealth playerHealth;
        private string currentPrompt;

        public IInteractable CurrentTarget { get; private set; }
        public string CurrentPrompt => currentPrompt;

        private void Awake()
        {
            playerHealth = GetComponent<PlayerHealth>();
            interactAction = GetComponent<PlayerInput>().actions?.FindAction(InteractActionName, false);

            if (interactAction == null)
            {
                Debug.LogError($"{nameof(PlayerInteraction)} requires an '{InteractActionName}' action on {nameof(PlayerInput)}.", this);
                enabled = false;
            }
        }

        private void OnDisable()
        {
            SetTarget(null);
        }

        private void Update()
        {
            bool canAct = playerHealth == null || playerHealth.IsAlive;
            SetTarget(canAct ? FindTarget() : null);

            // The Interact action uses a Hold interaction; checking the press itself makes a tap of E work.
            if (CurrentTarget != null && interactAction.WasPressedThisFrame())
                TryInteract();
        }

        /// <summary>
        /// Uses the focused object if it allows it. Returns true when an interaction happened.
        /// </summary>
        public bool TryInteract()
        {
            if (CurrentTarget == null || !CurrentTarget.CanInteract)
                return false;

            IInteractable target = CurrentTarget;
            target.Interact(gameObject);
            Interacted?.Invoke(target);

            // The prompt often changes after use (e.g. "Open door" -> "Close door").
            SetTarget(IsAlive(target) && target.CanInteract ? target : null);
            return true;
        }

        private IInteractable FindTarget()
        {
            Camera cam = aimCamera != null ? aimCamera : Camera.main;
            if (cam == null)
                return null;

            Vector3 chest = transform.position + Vector3.up * chestHeight;
            Ray ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            float maxDistance = Vector3.Distance(ray.origin, chest) + interactRange;

            int count = Physics.RaycastNonAlloc(ray, hitBuffer, maxDistance, interactLayers, QueryTriggerInteraction.Collide);
            Array.Sort(hitBuffer, 0, count, RaycastHitDistanceComparer.Instance);

            for (int i = 0; i < count; i++)
            {
                Collider hitCollider = hitBuffer[i].collider;
                if (hitCollider.transform.IsChildOf(transform))
                    continue;

                IInteractable interactable = hitCollider.GetComponentInParent<IInteractable>();
                if (interactable == null)
                {
                    if (hitCollider.isTrigger)
                        continue;   // Look through checkpoints and other invisible volumes.
                    return null;    // A wall or other solid object is in the way.
                }

                bool inReach = Vector3.Distance(chest, hitBuffer[i].point) <= interactRange;
                return inReach && interactable.CanInteract ? interactable : null;
            }

            return null;
        }

        private void SetTarget(IInteractable target)
        {
            if (target != null && !IsAlive(target))
                target = null;

            string prompt = target?.InteractionPrompt;
            if (ReferenceEquals(target, CurrentTarget) && prompt == currentPrompt)
                return;

            CurrentTarget = target;
            currentPrompt = prompt;
            FocusChanged?.Invoke(target);
        }

        private static bool IsAlive(IInteractable target)
        {
            // A destroyed component still exists as a C# object; Unity's null check catches it.
            return !(target is UnityEngine.Object unityObject) || unityObject != null;
        }

        private sealed class RaycastHitDistanceComparer : System.Collections.Generic.IComparer<RaycastHit>
        {
            public static readonly RaycastHitDistanceComparer Instance = new RaycastHitDistanceComparer();

            public int Compare(RaycastHit a, RaycastHit b) => a.distance.CompareTo(b.distance);
        }
    }
}
