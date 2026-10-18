using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RunwayPanic.Agents.Hawk
{
    /// <summary>
    /// On-screen panel for every hawk (H7), toggled with <b>F4</b>: mode/phase, the aggression meter, each weakness
    /// window (open now, times opened, learned weight), the current vantage score with every term, the herd state and
    /// the counters from <see cref="HawkStats"/>. One scene object (sandbox); editor and development builds only — in
    /// a release build it draws nothing.
    /// </summary>
    public sealed class HawkDebugOverlay : MonoBehaviour
    {
        private const int WindowCount = (int)WeaknessWindow.Busy + 1;

        // Release builds compile the drawing code out, which leaves these fields unread (CS0414).
#pragma warning disable 0414
        [Tooltip("Key that shows / hides the panel.")]
        [SerializeField] private Key toggleKey = Key.F4;
        [Tooltip("Show the panel when Play starts.")]
        [SerializeField] private bool visible = true;
        [Tooltip("Panel width in pixels.")]
        [SerializeField, Min(200f)] private float width = 380f;
#pragma warning restore 0414

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private static readonly string[] WindowNames = BuildWindowNames();
        private GUIStyle textStyle;

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (toggleKey != Key.None && keyboard != null && keyboard[toggleKey].wasPressedThisFrame)
                visible = !visible;
        }

        private void OnGUI()
        {
            if (!visible)
                return;

            if (textStyle == null)
                textStyle = new GUIStyle(GUI.skin.label) { richText = true, fontSize = 12 };

            IReadOnlyList<HawkBrain> hawks = HawkBrain.Active;
            GUILayout.BeginArea(new Rect(10f, 10f, width, Screen.height - 20f));
            if (hawks.Count == 0)
                GUILayout.Label("<b>Hawk debug</b> (F4): no hawk alive", textStyle);
            for (int i = 0; i < hawks.Count; i++)
                if (hawks[i] != null && hawks[i].Config != null && hawks[i].Weakness != null)
                    DrawHawk(hawks[i]);
            GUILayout.EndArea();
        }

        private void DrawHawk(HawkBrain hawk)
        {
            HawkStrategyConfig config = hawk.Config;
            PlayerWeaknessModel weakness = hawk.Weakness;
            HawkStats stats = hawk.Stats;

            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label($"<b>{hawk.name}</b>   {hawk.CurrentStateName}", textStyle);

            // Aggression meter.
            float fraction = Mathf.Clamp01(weakness.Meter / config.MeterMax);
            Rect bar = GUILayoutUtility.GetRect(width - 20f, 12f);
            Color previous = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.5f);
            GUI.DrawTexture(bar, Texture2D.whiteTexture);
            GUI.color = weakness.IsMeterFull ? new Color(1f, 0.3f, 0.2f) : new Color(1f, 0.75f, 0.2f);
            GUI.DrawTexture(new Rect(bar.x, bar.y, bar.width * fraction, bar.height), Texture2D.whiteTexture);
            GUI.color = previous;
            GUILayout.Label($"meter {weakness.Meter:0} / {config.MeterMax:0}{(weakness.AnyWindowOpen ? "   window open" : "")}", textStyle);

            // Weakness windows: open now (green), times opened, learned weight (capped at maxWindowWeight).
            GUILayout.Label("<b>Windows</b>   open · times · weight", textStyle);
            for (int w = 0; w < WindowCount; w++)
            {
                var window = (WeaknessWindow)w;
                string line = $"{WindowNames[w],-16} ×{weakness.GetOpenCount(window),-3} w {weakness.GetWeight(window):0.00}";
                GUILayout.Label(weakness.IsOpen(window) ? $"<color=#5f5>● {line}</color>" : $"○ {line}", textStyle);
            }

            // Current vantage and its utility terms.
            BirdPoint perch = hawk.CurrentPerch;
            if (perch != null)
            {
                PerchScore s = hawk.CurrentPerchScore;
                GUILayout.Label($"<b>Vantage</b> {perch.name}  total {s.Total:0.00} @ {s.Metres:0} m\n" +
                                $"height {s.Height:0.00}  sight {s.LineOfSight:0}  distance {s.Distance:0.00}  sun {s.Sun:0.00}  shot {hawk.Memory.Get(perch):0.00}", textStyle);
            }

            GUILayout.Label($"<b>Herd</b> other birds {hawk.OtherBirdsAlive} (needs {config.HerdMinOtherBirds})   " +
                            $"fear field {(hawk.IsProjectingFear ? "<color=#f6a>ON</color>" : "off")}", textStyle);

            GUILayout.Label($"<b>Counters</b>\n" +
                            $"stoops {stats.Stoops}  hits {stats.StoopHits} ({stats.HitRate:P0})  herd strikes {stats.HerdStrikes}\n" +
                            $"feints {stats.Feints}  baited {stats.FeintsBaited}  hit rate after feint {stats.HitRateAfterFeint:P0} / without {stats.HitRateWithoutFeint:P0}\n" +
                            $"hits taken {stats.HitsTaken}  withdrawals {stats.Withdrawals}  herd entries {stats.HerdEntries}\n" +
                            $"attacks: meter {stats.MeterAttacks} / timeout {stats.TimeoutAttacks}  avg watch {stats.AverageWatchSeconds:0.0}s", textStyle);
            GUILayout.EndVertical();
        }

        private static string[] BuildWindowNames()
        {
            var names = new string[WindowCount];
            for (int i = 0; i < names.Length; i++)
                names[i] = ((WeaknessWindow)i).ToString();
            return names;
        }
#endif
    }
}
