using System.Collections.Generic;
using System.Diagnostics;
using RunwayPanic.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RunwayPanic.Agents.Gull
{
    /// <summary>Scene-local, event-driven Gull evidence. F4 shows the panel in Editor/development builds only.</summary>
    [DisallowMultipleComponent]
    public sealed class GullDebugStats : MonoBehaviour
    {
        private static bool quitting;
        private readonly HashSet<FodItem> pending = new HashSet<FodItem>();
        private GullBrain lastTokenOwner;
        private bool hasGrantedToken;
        private float activeSeconds, totalMissDistance, nextRefresh;
        private FodManager manager;
        private PlayerEventHub playerHub;
        private string text = "";
        private GUIStyle style;

        public static GullDebugStats Instance { get; private set; }
        public bool Visible { get; set; }
        public int Drops { get; private set; }
        public int ResolvedDrops { get; private set; }
        public int PlayerHits { get; private set; }
        public int MissDistanceSamples { get; private set; }
        public int TokenHandOffs { get; private set; }
        public int PendingDrops => pending.Count;
        public float AverageMissDistance => MissDistanceSamples > 0 ? totalMissDistance / MissDistanceSamples : 0f;
        public float HitRate => ResolvedDrops > 0 ? PlayerHits / (float)ResolvedDrops : 0f;
        public float HandOffsPerMinute => activeSeconds > 0f ? TokenHandOffs * 60f / activeSeconds : 0f;
        public int GroundFodCount
        {
            get
            {
                if (manager == null) manager = FindAnyObjectByType<FodManager>();
                return manager != null ? manager.LandedCount : 0;
            }
        }
        private bool GameplayActive => GameManager.Instance == null || GameManager.Instance.CurrentState == GameState.Playing;

        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
        public static void EnsureExists()
        {
            if (!Application.isPlaying || quitting || (!Application.isEditor && !UnityEngine.Debug.isDebugBuild)) return;
            if (Instance == null) Instance = FindAnyObjectByType<GullDebugStats>();
            if (Instance == null) new GameObject("[GullDebugStats]").AddComponent<GullDebugStats>();
        }

        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
        public static void RecordDrop(FodItem item)
        {
            EnsureExists();
            if (Instance == null || !Instance.isActiveAndEnabled || item == null || item.State != FodItem.FodState.Falling || !Instance.pending.Add(item)) return;
            Instance.Drops++;
            item.Impacted += Instance.OnImpact;
        }

        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
        public static void RecordTokenGrant(GullBrain owner)
        {
            EnsureExists();
            if (Instance == null || !Instance.isActiveAndEnabled || owner == null) return;
            if (Instance.hasGrantedToken && !ReferenceEquals(Instance.lastTokenOwner, owner)) Instance.TokenHandOffs++;
            Instance.lastTokenOwner = owner;
            Instance.hasGrantedToken = true;
        }

        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
        public static void ForgetDrop(FodItem item)
        {
            if (Instance == null || item == null) return; // Never create a singleton during teardown.
            Instance.pending.Remove(item);
            item.Impacted -= Instance.OnImpact;
        }

        private void OnImpact(FodItem item, bool hitPlayer)
        {
            if (!pending.Remove(item)) return;
            item.Impacted -= OnImpact;
            ResolvedDrops++;
            if (hitPlayer) PlayerHits++;
            if (playerHub == null) playerHub = PlayerEventHub.Instance;
            if (playerHub == null || !playerHub.HasPlayer) return;
            Vector3 delta = item.transform.position - playerHub.PlayerPosition;
            totalMissDistance += new Vector2(delta.x, delta.z).magnitude;
            MissDistanceSamples++;
        }

        private void Awake()
        {
            if (!Application.isEditor && !UnityEngine.Debug.isDebugBuild) { enabled = false; return; }
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
            // Bind before the first physics impact, not lazily inside its collision callback.
            playerHub = PlayerEventHub.Instance;
        }

        private void Update()
        {
            if (GameplayActive) activeSeconds += Time.deltaTime;
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.f4Key.wasPressedThisFrame) { Visible = !Visible; nextRefresh = 0f; }
            if (!Visible || Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + .25f;
            string hitRate = ResolvedDrops > 0 ? (HitRate * 100f).ToString("F1") + "%" : "--";
            string miss = MissDistanceSamples > 0 ? AverageMissDistance.ToString("F2") + " m" : "--";
            text = "GULL AI  (F4 to hide)\nThis session / since reset\n\n" +
                "Alive: " + BirdRegistry.CountAlive(BirdSpecies.Gull) + "    Ground FOD: " + GroundFodCount + "\n" +
                "Drops: " + Drops + "    Pending: " + PendingDrops + "\n" +
                "FOD HP hits: " + PlayerHits + " / " + ResolvedDrops + " impacts (" + hitRate + ")\n" +
                "Avg impact-to-player: " + miss + " (n=" + MissDistanceSamples + ")\n" +
                "Token hand-offs: " + TokenHandOffs + "  (" + HandOffsPerMinute.ToString("F1") + "/min)\n" +
                "Active time: " + activeSeconds.ToString("F1") + " s\nContact dives not counted as FOD hits.";
        }

        private void OnGUI()
        {
            if (!Visible) return;
            style ??= new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 13, wordWrap = true };
            var panel = new Rect(Mathf.Max(10, Screen.width - 350), Mathf.Max(10, Screen.height - 245), 340, 235);
            GUI.Box(panel, text, style);
            if (GUI.Button(new Rect(panel.x + 8, panel.yMax - 29, 125, 22), "Reset Gull stats")) ResetStatistics();
        }

        [ContextMenu("Reset Gull Statistics")]
        public void ResetStatistics()
        {
            ClearPending();
            Drops = ResolvedDrops = PlayerHits = MissDistanceSamples = TokenHandOffs = 0;
            activeSeconds = totalMissDistance = nextRefresh = 0f;
            lastTokenOwner = null; hasGrantedToken = false;
        }
        private void ClearPending()
        {
            foreach (var item in pending) if (item != null) item.Impacted -= OnImpact;
            pending.Clear();
        }
        private void OnDisable() { ClearPending(); }
        private void OnDestroy() { if (Instance == this) Instance = null; }
        private void OnApplicationQuit() { quitting = true; }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { Instance = null; quitting = false; }
    }
}
