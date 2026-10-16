using UnityEngine;

namespace RunwayPanic.Core
{
    /// <summary>
    /// Something the player can use with the Interact action: doors, barricades, throwables, pickups.
    /// </summary>
    public interface IInteractable
    {
        bool CanInteract { get; }

        /// <summary>Short prompt shown on the HUD, e.g. "Open door".</summary>
        string InteractionPrompt { get; }

        void Interact(GameObject player);
    }
}
