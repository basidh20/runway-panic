using System.Collections.Generic;
using RunwayPanic.AI;
using RunwayPanic.Core;
using RunwayPanic.Data;
using UnityEngine;

namespace RunwayPanic.Agents
{
    /// <summary>
    /// Base class of the four species brains (GullBrain, PigeonBrain, CrowBrain, HawkBrain). Caches the bird's parts,
    /// gives access to the player through <see cref="Hub"/>, and wraps pathfinding in <see cref="RequestPath"/>.
    /// A brain only decides; it moves the bird through <see cref="Motor"/> and animates it through <see cref="Anim"/>.
    /// <para>Overriding <c>Awake</c>, <c>OnEnable</c> or <c>OnDisable</c>? Call the base method first.</para>
    /// </summary>
    public abstract class BirdBrain : MonoBehaviour
    {
        private readonly List<Vector3> pathBuffer = new List<Vector3>(64);

        /// <summary>This bird's health (species, HP, death event).</summary>
        protected BirdHealth Health { get; private set; }

        /// <summary>Moves the bird. Never null on a correctly set-up prefab.</summary>
        protected IBirdMotor Motor { get; private set; }

        /// <summary>Animates the bird. Falls back to a do-nothing animator, so it is never null.</summary>
        protected IBirdAnimator Anim { get; private set; }

        /// <summary>The bird's melee attack, or null if the prefab has none.</summary>
        protected BirdContactAttack Attack { get; private set; }

        /// <summary>The player: position, velocity, weapon state and events.</summary>
        protected PlayerEventHub Hub => PlayerEventHub.Instance;

        /// <summary>This bird's species asset (stats).</summary>
        protected BirdSpeciesData Species => Health != null ? Health.Species : null;

        /// <summary>False while paused, in the shop or after game over. True when there is no GameManager (sandboxes).</summary>
        protected bool IsGameplayActive => GameManager.Instance == null || GameManager.Instance.CurrentState == GameState.Playing;

        /// <summary>Short name of the current state for the debug label, e.g. "Hunt/Perched". Return cached strings (no allocation).</summary>
        public abstract string CurrentStateName { get; }

        protected virtual void Awake()
        {
            Health = GetComponent<BirdHealth>();
            Motor = GetComponent<IBirdMotor>();
            Attack = GetComponent<BirdContactAttack>();

            IBirdAnimator animator = GetComponentInChildren<IBirdAnimator>();
            Anim = animator != null ? animator : NullBirdAnimator.Instance;

            if (Health == null || Motor == null)
            {
                Debug.LogError($"{GetType().Name} on '{name}' needs a {nameof(BirdHealth)} and an {nameof(IBirdMotor)} (e.g. {nameof(SimpleBirdMotor)}) on the same object.", this);
                enabled = false;
            }
        }

        protected virtual void OnEnable()
        {
            if (Health != null)
                Health.Died += HandleDied;
        }

        protected virtual void OnDisable()
        {
            if (Health != null)
                Health.Died -= HandleDied;
        }

        /// <summary>Called once when the bird dies. The base version disables the brain; call it if you override.</summary>
        protected virtual void OnBirdDied()
        {
            enabled = false;
        }

        /// <summary>
        /// Finds a path from the bird to <paramref name="target"/> and starts following it.
        /// Returns false when this frame's search budget is used up (keep the current path, try again next frame)
        /// or when there is no path (e.g. the target is inside a sealed shelter).
        /// </summary>
        protected bool RequestPath(Vector3 target, IReadOnlyList<ICostModifier> costModifiers = null)
        {
            LastRequestWasThrottled = !PathService.TryConsumeBudget();
            if (LastRequestWasThrottled)
                return false;
            if (!PathService.Current.TryFindPath(Motor.Position, target, costModifiers, pathBuffer))
                return false;

            Motor.FollowPath(pathBuffer);
            return true;
        }

        /// <summary>
        /// True when the last <see cref="RequestPath"/> returned false only because this frame's search budget was
        /// used up (retry next frame), false when it really found no path.
        /// </summary>
        protected bool LastRequestWasThrottled { get; private set; }

        private void HandleDied(BirdHealth bird)
        {
            OnBirdDied();
        }
    }
}
