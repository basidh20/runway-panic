using UnityEngine;

namespace RunwayPanic.Agents.Crow
{
    /// <summary>
    /// Debug view: select the "[CrowDangerMap]" object to see the danger as a red heatmap on the ground,
    /// and use the component's context menu to stamp danger at the player without killing crows.
    /// </summary>
    public sealed partial class CrowDangerMap
    {
        private const float HeatmapHeight = 0.1f;

        [ContextMenu("Debug: Stamp At Player")]
        private void DebugStampAtPlayer()
        {
            PlayerEventHub hub = PlayerEventHub.Instance;
            if (hub == null || !hub.HasPlayer || config == null)
                return;

            // As if a crow 8 m in front of the player had just been shot.
            Vector3 shooter = hub.PlayerPosition;
            Vector3 forward = hub.Player.forward;
            MarkKill(shooter, shooter + forward * 8f);
        }

        [ContextMenu("Debug: Clear")]
        private void DebugClear() => Clear();

        private void OnDrawGizmosSelected()
        {
            if (cells == null)
                return;

            float max = config != null ? config.maxDanger : 5f;
            Vector3 size = new Vector3(cellSize * 0.95f, 0.02f, cellSize * 0.95f);
            for (int z = 0; z < sizeZ; z++)
            {
                for (int x = 0; x < sizeX; x++)
                {
                    float v = cells[x + z * sizeX];
                    if (v <= 0f)
                        continue;

                    Gizmos.color = new Color(1f, 0.1f, 0.05f, Mathf.Clamp01(v / max) * 0.85f);
                    Gizmos.DrawCube(CellCentre(x, z) + Vector3.up * HeatmapHeight, size);
                }
            }
        }
    }
}
