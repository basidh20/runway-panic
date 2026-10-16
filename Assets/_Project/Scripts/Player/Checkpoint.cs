using UnityEngine;

namespace RunwayPanic.Player
{
    /// <summary>
    /// Trigger volume that becomes the player's respawn point when they walk through it.
    /// The player respawns at this transform's position and rotation.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public sealed class Checkpoint : MonoBehaviour
    {
        private void Reset()
        {
            GetComponent<Collider>().isTrigger = true;
        }

        private void OnTriggerEnter(Collider other)
        {
            if (other.TryGetComponent(out PlayerRespawn respawn))
                respawn.SetCheckpoint(transform);
        }
    }
}
