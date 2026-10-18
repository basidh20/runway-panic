using System.Collections.Generic;
using RunwayPanic.Core;
using UnityEngine;

namespace RunwayPanic.Agents.Gull
{
    /// <summary>Scene-local FOD ownership, oldest-first landed cap, lifetime and wave cleanup.</summary>
    [DisallowMultipleComponent]
    public sealed class FodManager : MonoBehaviour
    {
        private sealed class Entry { public FodItem Item; public float Age; }
        [SerializeField] private GullStrategyConfig config;
        private static FodManager instance;
        private static bool quitting;
        private readonly HashSet<FodItem> active = new HashSet<FodItem>();
        private readonly LinkedList<Entry> landed = new LinkedList<Entry>();
        private readonly Dictionary<FodItem, LinkedListNode<Entry>> nodes = new Dictionary<FodItem, LinkedListNode<Entry>>();
        private GameManager boundGame;
        private bool clearing;

        public static FodManager Instance
        {
            get
            {
                if (instance != null || quitting || !Application.isPlaying) return instance;
                instance = FindAnyObjectByType<FodManager>();
                if (instance == null) instance = new GameObject("[FodManager]").AddComponent<FodManager>();
                return instance;
            }
        }
        public int ActiveCount => active.Count;
        public int LandedCount => landed.Count;
        public GullStrategyConfig Config => config;

        private void Awake()
        {
            if (instance != null && instance != this) { Destroy(gameObject); return; }
            instance = this;
        }
        private void OnEnable() { BindGame(); }
        private void OnDisable() { UnbindGame(); }

        public void Configure(GullStrategyConfig strategy)
        {
            if (strategy == null) throw new System.ArgumentNullException(nameof(strategy));
            config = strategy;
            EnforceCap();
        }

        public void Track(FodItem item)
        {
            if (item == null || clearing) return;
            active.Add(item);
            if (config == null) config = item.Config;
        }
        public void Untrack(FodItem item) { RemoveLanded(item); active.Remove(item); }
        public void RemoveLanded(FodItem item)
        {
            if (!nodes.TryGetValue(item, out var node)) return;
            landed.Remove(node);
            nodes.Remove(item);
        }
        public void MarkLanded(FodItem item)
        {
            if (item == null || clearing || nodes.ContainsKey(item)) return;
            Track(item);
            nodes.Add(item, landed.AddLast(new Entry { Item = item }));
            EnforceCap();
        }
        private void EnforceCap()
        {
            int cap = config != null ? Mathf.Max(1, config.maxFodOnGround) : 20;
            while (landed.Count > cap)
            {
                var oldest = landed.First.Value.Item;
                if (oldest != null) oldest.Despawn();
                else { nodes.Remove(oldest); landed.RemoveFirst(); }
            }
        }

        private void Update()
        {
            BindGame();
            if (boundGame != null && boundGame.CurrentState != GameState.Playing) return;
            for (var node = landed.First; node != null;)
            {
                var next = node.Next;
                var entry = node.Value;
                entry.Age += Time.deltaTime;
                float lifetime = entry.Item != null && entry.Item.Config != null ? entry.Item.Config.fodLifetime : 40f;
                if (entry.Item == null) { nodes.Remove(entry.Item); landed.Remove(node); }
                else if (entry.Age >= Mathf.Max(.01f, lifetime)) entry.Item.Despawn();
                node = next;
            }
        }

        public void ClearAll()
        {
            if (clearing) return;
            clearing = true;
            try
            {
                foreach (var item in new List<FodItem>(active)) if (item != null) item.Despawn();
                active.Clear(); nodes.Clear(); landed.Clear();
            }
            finally { clearing = false; }
        }
        private void OnWaveCompleted(int wave)
        {
            ClearAll(); // Includes carried/falling pieces, so nothing can land and damage someone in the shop.
            foreach (var point in FindObjectsByType<FodSpawnPoint>(FindObjectsInactive.Include, FindObjectsSortMode.None)) point.ResetForWave();
        }
        private void BindGame()
        {
            if (boundGame == GameManager.Instance) return;
            UnbindGame();
            boundGame = GameManager.Instance;
            if (boundGame != null) boundGame.WaveCompleted += OnWaveCompleted;
        }
        private void UnbindGame()
        {
            if (boundGame != null) boundGame.WaveCompleted -= OnWaveCompleted;
            boundGame = null;
        }
        private void OnDestroy()
        {
            if (instance != this) return;
            UnbindGame(); ClearAll(); instance = null;
        }
        private void OnApplicationQuit() { quitting = true; }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { instance = null; quitting = false; }
    }
}
