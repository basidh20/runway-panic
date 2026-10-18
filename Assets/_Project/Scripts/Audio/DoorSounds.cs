using RunwayPanic.Interaction;
using UnityEngine;

namespace RunwayPanic.Audio
{
    /// <summary>A door's creak when it opens and thud when it shuts (on the door prefab, next to <see cref="Door"/>).</summary>
    [RequireComponent(typeof(Door))]
    public sealed class DoorSounds : MonoBehaviour
    {
        [SerializeField] private SoundCue open;
        [SerializeField] private SoundCue close;

        private Door door;

        private void Awake() => door = GetComponent<Door>();

        private void OnEnable()
        {
            door.Opened += HandleOpened;
            door.Closed += HandleClosed;
        }

        private void OnDisable()
        {
            door.Opened -= HandleOpened;
            door.Closed -= HandleClosed;
        }

        private void HandleOpened() => SoundPlayer.Play(open, transform.position + Vector3.up);

        private void HandleClosed() => SoundPlayer.Play(close, transform.position + Vector3.up);
    }
}
