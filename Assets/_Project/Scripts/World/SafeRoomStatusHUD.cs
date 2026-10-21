using RunwayPanic.Core;
using RunwayPanic.Player;
using UnityEngine;

namespace RunwayPanic.World
{
    /// <summary>Small independent prompt shown only near a shelter during gameplay.</summary>
    public sealed class SafeRoomStatusHUD : MonoBehaviour
    {
        [SerializeField] private SafeRoomController[] rooms;
        [SerializeField, Min(1f)] private float displayDistance = 11f;
        private PlayerHealth player;
        private GUIStyle label;

        private void OnGUI()
        {
            if (Event.current.type != EventType.Repaint || GameManager.Instance == null ||
                GameManager.Instance.CurrentState != GameState.Playing || rooms == null) return;
            if (player == null) player = FindAnyObjectByType<PlayerHealth>();
            if (player == null || !player.IsAlive) return;

            SafeRoomController nearest = null;
            float nearestDistance = displayDistance * displayDistance;
            foreach (SafeRoomController room in rooms)
            {
                if (room == null || !room.isActiveAndEnabled) continue;
                float distance = (room.transform.position - player.transform.position).sqrMagnitude;
                if (distance < nearestDistance) { nearestDistance = distance; nearest = room; }
            }
            if (nearest == null) return;

            if (label == null)
            {
                label = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.MiddleCenter, fontSize = 15, wordWrap = true };
                label.normal.textColor = Color.white;
            }
            string status = nearest.State == SafeRoomController.ShelterState.Available
                ? "READY  |  Enter and close the door"
                : nearest.State == SafeRoomController.ShelterState.Active
                    ? "SHELTER ACTIVE  |  " + Mathf.CeilToInt(nearest.RemainingSeconds) + "s left"
                    : "COOLDOWN  |  " + Mathf.CeilToInt(nearest.RemainingSeconds) + "s";
            float width = Mathf.Min(360f, Screen.width - 20f);
            GUI.Box(new Rect((Screen.width - width) * .5f, Screen.height - 115f, width, 42f), status, label);
        }
    }
}
