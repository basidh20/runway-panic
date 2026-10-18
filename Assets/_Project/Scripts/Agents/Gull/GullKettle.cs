using System.Collections.Generic;
using RunwayPanic.Core;
using UnityEngine;

namespace RunwayPanic.Agents.Gull
{
    /// <summary>Ordered rotating slots and one exclusively owned attack token for the scene's Gull flock.</summary>
    [DisallowMultipleComponent]
    public sealed class GullKettle : MonoBehaviour
    {
        private static GullKettle instance;
        private static bool quitting;
        [SerializeField] private GullStrategyConfig config;
        [Tooltip("Allow the bottom Gull to take the token. Disable temporarily to inspect the whole formation.")]
        [SerializeField] private bool dispatchEnabled = true;
        private readonly List<GullBrain> members = new List<GullBrain>();
        private readonly List<BirdPoint> centres = new List<BirdPoint>();
        private BirdPoint marker;
        private GullBrain tokenHolder;
        private GameManager boundGame;
        private float rotation, tokenAge;
        public const float TokenTimeout = 12f;

        public static GullKettle Instance
        {
            get
            {
                if (instance != null || quitting || !Application.isPlaying) return instance;
                instance = FindAnyObjectByType<GullKettle>();
                if (instance == null) instance = new GameObject("[GullKettle]").AddComponent<GullKettle>();
                return instance;
            }
        }
        public IReadOnlyList<GullBrain> Members => members;
        public GullBrain TokenHolder => tokenHolder;
        public float Rotation => rotation;
        public float TokenAge => tokenAge;
        public bool DispatchEnabled { get => dispatchEnabled; set => dispatchEnabled = value; }
        public Vector3 Centre => marker != null ? marker.Position : new Vector3(0, 12, 0);
        // The plan's y=10 marker is an anchor above the runway, not the origin for adding slot heights.
        public float GroundHeight => marker != null ? marker.Position.y - 10f : 0f;
        public Vector3 JoinPosition => Slot(0, 2); // Stable top-slot rule even when nobody has joined yet.
        public Vector3 DropStagingPosition => new Vector3(Centre.x, GroundHeight + (config != null ? config.dropSlotHeight : 7f), Centre.z);

        private void Awake()
        {
            if (instance != null && instance != this) { Destroy(gameObject); return; }
            instance = this; RefreshMarker();
        }
        private void OnEnable() { BindGame(); }
        private void OnDisable() { UnbindGame(); Clear(); }
        public void Configure(GullStrategyConfig strategy) { if (config == null) config = strategy; }
        public bool Contains(GullBrain bird) => members.Contains(bird);
        public bool Join(GullBrain bird)
        {
            if (!Participates(bird)) return false;
            Configure(bird.Config);
            if (!members.Contains(bird)) members.Insert(0, bird);
            return true;
        }
        public void Leave(GullBrain bird)
        {
            members.Remove(bird);
            if (ReferenceEquals(tokenHolder, bird)) { tokenHolder = null; tokenAge = 0f; }
        }
        public Vector3 GetSlotPosition(GullBrain bird)
        {
            int index = members.IndexOf(bird);
            return index < 0 ? JoinPosition : Slot(index, members.Count);
        }
        public bool HasToken(GullBrain bird) => dispatchEnabled && tokenHolder == null && Participates(bird) &&
            bird.State == GullState.InKettle && members.Count > 0 && members[members.Count - 1] == bird;
        public bool TakeToken(GullBrain bird)
        {
            if (!HasToken(bird)) return false;
            tokenHolder = bird; tokenAge = 0f;
            return true;
        }
        public bool ReturnToken(GullBrain bird)
        {
            if (bird == null || tokenHolder != bird) return false;
            tokenHolder = null; tokenAge = 0f;
            members.Remove(bird);
            if (Participates(bird)) members.Insert(0, bird);
            return true;
        }

        private Vector3 Slot(int index, int count)
        {
            float top = config != null ? config.kettleTopHeight : 14f;
            float bottom = config != null ? config.kettleBottomHeight : 6f;
            float height = count <= 1 ? bottom : Mathf.Lerp(top, bottom, index / (float)(count - 1));
            float angle = (rotation + index * (360f / Mathf.Max(1, count))) * Mathf.Deg2Rad;
            float radius = config != null ? config.kettleRadius : 8f;
            return new Vector3(Centre.x + Mathf.Cos(angle) * radius, GroundHeight + height, Centre.z + Mathf.Sin(angle) * radius);
        }
        private static bool Participates(GullBrain bird) => bird != null && bird.isActiveAndEnabled && bird.IsAlive;
        private void Update()
        {
            BindGame();
            if (marker == null) RefreshMarker();
            for (int i = members.Count - 1; i >= 0; i--)
                if (!Participates(members[i])) Leave(members[i]);
            if (boundGame != null && boundGame.CurrentState != GameState.Playing) return;
            rotation = Mathf.Repeat(rotation + (config != null ? config.kettleAngularSpeed : 25f) * Time.deltaTime, 360f);
            if (tokenHolder == null) return;
            tokenAge += Time.deltaTime;
            if (tokenAge < TokenTimeout) return;
            var expired = tokenHolder;
            ReturnToken(expired);
            expired.OnKettleTokenExpired();
        }
        private void RefreshMarker()
        {
            BirdPoint.GetAll(BirdPointType.KettleCenter, centres);
            marker = centres.Count > 0 ? centres[0] : null;
        }
        public void Clear() { members.Clear(); tokenHolder = null; tokenAge = rotation = 0f; }
        private void OnWaveCompleted(int wave) { Clear(); }
        private void BindGame()
        {
            if (boundGame == GameManager.Instance) return;
            UnbindGame(); boundGame = GameManager.Instance;
            if (boundGame != null) boundGame.WaveCompleted += OnWaveCompleted;
        }
        private void UnbindGame()
        {
            if (boundGame != null) boundGame.WaveCompleted -= OnWaveCompleted;
            boundGame = null;
        }
        private void OnDestroy() { if (instance == this) { UnbindGame(); Clear(); instance = null; } }
        private void OnApplicationQuit() { quitting = true; }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { instance = null; quitting = false; }

        private void OnDrawGizmos()
        {
            if (config == null) return;
            Gizmos.color = new Color(.1f, .85f, 1f, .7f);
            Vector3 previous = Vector3.zero;
            for (int i = 0; i <= 64; i++)
            {
                float t = i / 64f, angle = (rotation + t * 360f) * Mathf.Deg2Rad;
                Vector3 point = new Vector3(Centre.x + Mathf.Cos(angle) * config.kettleRadius,
                    GroundHeight + Mathf.Lerp(config.kettleTopHeight, config.kettleBottomHeight, t), Centre.z + Mathf.Sin(angle) * config.kettleRadius);
                if (i > 0) Gizmos.DrawLine(previous, point);
                previous = point;
            }
            foreach (var bird in members)
            {
                if (bird == null) continue;
                Gizmos.color = bird == tokenHolder ? Color.red : Color.cyan;
                Gizmos.DrawWireSphere(GetSlotPosition(bird), .45f);
                if (bird == tokenHolder)
                {
                    Vector3 attack = bird.PredictedDropPoint + Vector3.up * config.dropSlotHeight;
                    Gizmos.DrawLine(bird.transform.position, attack); Gizmos.DrawWireSphere(attack, .6f);
                }
            }
        }
    }
}
