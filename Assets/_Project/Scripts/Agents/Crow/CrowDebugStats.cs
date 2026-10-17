using System.Collections.Generic;
using System.Text;
using RunwayPanic.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RunwayPanic.Agents.Crow
{
    /// <summary>
    /// Debug overlay for the crow strategy: <b>F4</b> shows live crow states, the danger map's totals and the
    /// <see cref="CrowStats"/> counters (thefts, funerals, rages, vigils). Evidence for the demo video and the viva
    /// that each behaviour really happens. One per scene, created by the first crow (Editor and development builds only).
    /// </summary>
    public sealed class CrowDebugStats : MonoBehaviour
    {
        private const float RefreshInterval = 0.25f;
        private const float PanelWidth = 300f;
        private const float PanelHeight = 330f;
        private const float Margin = 10f;

        private static readonly int StateCount = System.Enum.GetValues(typeof(CrowState)).Length;
        private static readonly string[] StateNames = System.Enum.GetNames(typeof(CrowState));

        private readonly List<BirdHealth> crowBuffer = new List<BirdHealth>(16);
        private readonly int[] stateCounts = new int[StateCount];
        private readonly StringBuilder builder = new StringBuilder(512);

        private bool visible;
        private float nextRefreshTime;
        private string text = "";
        private GUIStyle style;

        /// <summary>The scene's overlay, or null.</summary>
        public static CrowDebugStats Instance { get; private set; }

        /// <summary>Creates the overlay if the scene has none yet.</summary>
        public static void EnsureExists()
        {
            if (Instance == null)
                new GameObject("[CrowDebugStats]").AddComponent<CrowDebugStats>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.f4Key.wasPressedThisFrame)
            {
                visible = !visible;
                nextRefreshTime = 0f;
            }

            // Rebuild the text a few times per second, and only while shown (it walks every crow and map cell).
            if (!visible || Time.unscaledTime < nextRefreshTime)
                return;

            nextRefreshTime = Time.unscaledTime + RefreshInterval;
            text = BuildText();
        }

        private string BuildText()
        {
            System.Array.Clear(stateCounts, 0, stateCounts.Length);
            int carrying = 0;
            BirdRegistry.GetAlive(BirdSpecies.Crow, crowBuffer);
            foreach (BirdHealth crow in crowBuffer)
            {
                if (crow == null || !crow.TryGetComponent(out CrowBrain brain))
                    continue;
                stateCounts[(int)brain.State]++;
                if (brain.IsCarrying)
                    carrying++;
            }

            StringBuilder b = builder.Clear();
            b.Append("CROW AI  (F4 to hide)\n\n");
            b.Append("Crows alive: ").Append(crowBuffer.Count).Append("   carrying: ").Append(carrying).Append('\n');
            for (int i = 0; i < StateCount; i++)
                if (stateCounts[i] > 0)
                    b.Append("  ").Append(StateNames[i]).Append(": ").Append(stateCounts[i]).Append('\n');

            b.Append("\nDanger map (grudge)\n");
            CrowDangerMap map = CrowDangerMap.Instance;
            if (map != null)
            {
                b.Append("  total: ").Append(map.TotalDanger.ToString("F1"))
                 .Append("   hottest cell: ").Append(map.HottestCell.ToString("F2")).Append('\n');
                if (map.Config != null)
                    b.Append("  half-life: ").Append(map.Config.DangerHalfLife.ToString("F0")).Append(" s\n");
            }
            else
            {
                b.Append("  (not created yet)\n");
            }

            b.Append("\nCounters (this Play session)\n");
            b.Append("  danger stamps: ").Append(CrowStats.DangerStamps).Append('\n');
            b.Append("  approaches: ").Append(CrowStats.Approaches).Append("   hits: ").Append(CrowStats.AttackHits).Append('\n');
            b.Append("  thefts: ").Append(CrowStats.TheftsAttempted).Append(" tried / ")
             .Append(CrowStats.TheftsSucceeded).Append(" stashed / ")
             .Append(CrowStats.TheftsRecovered).Append(" recovered\n");
            b.Append("  thieves now: ").Append(CrowPickupClaims.ThiefCount).Append('\n');
            b.Append("  funerals: ").Append(CrowStats.FuneralAttendances)
             .Append("   rages: ").Append(CrowStats.Enrages)
             .Append("   vigils: ").Append(CrowStats.Vigils).Append('\n');
            return b.ToString();
        }

        private void OnGUI()
        {
            if (!visible)
                return;

            style ??= new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 13, wordWrap = false };

            var panel = new Rect(Screen.width - PanelWidth - Margin, Margin, PanelWidth, PanelHeight);
            GUI.Box(panel, text, style);

            if (GUI.Button(new Rect(panel.x + 8f, panel.yMax - 30f, 120f, 22f), "Reset counters"))
            {
                CrowStats.Reset();
                nextRefreshTime = 0f;
            }
        }
    }
}
