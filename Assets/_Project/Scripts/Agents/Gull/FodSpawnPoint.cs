using RunwayPanic.Core;
using RunwayPanic.Data;
using UnityEngine;

namespace RunwayPanic.Agents.Gull
{
    /// <summary>A shared FodSpawn marker with one supply cooldown and an exclusive Gull claim.</summary>
    [DisallowMultipleComponent, RequireComponent(typeof(BirdPoint))]
    public sealed class FodSpawnPoint : MonoBehaviour
    {
        [SerializeField] private FodItem prefab;
        [SerializeField] private GullStrategyConfig config;
        [SerializeField] private BirdSpeciesData species;
        private BirdPoint marker;
        private GullBrain claimant;
        private float cooldown;
        public float CooldownRemaining => cooldown;
        public GullBrain Claimant => claimant != null && claimant.isActiveAndEnabled ? claimant : null;
        public Vector3 ApproachPosition => transform.position + Vector3.up * .8f;
        public bool IsReady => isActiveAndEnabled && marker != null && marker.isActiveAndEnabled &&
            marker.Type == BirdPointType.FodSpawn && prefab != null && config != null && species != null &&
            species.Species == BirdSpecies.Gull && cooldown <= 0f;
        private bool GameplayActive => GameManager.Instance == null || GameManager.Instance.CurrentState == GameState.Playing;

        private void Awake() { marker = GetComponent<BirdPoint>(); }
        private void Update() { if (GameplayActive) cooldown = Mathf.Max(0f, cooldown - Time.deltaTime); }
        private void OnDisable() { claimant = null; }

        public bool TryClaim(GullBrain owner)
        {
            if (owner == null || !owner.isActiveAndEnabled || !GameplayActive || !IsReady || (Claimant != null && Claimant != owner)) return false;
            claimant = owner;
            return true;
        }
        public void ReleaseClaim(GullBrain owner) { if (claimant == owner) claimant = null; }
        public bool TryTake(out FodItem item) => TryTake(null, out item);
        public bool TryTake(GullBrain owner, out FodItem item)
        {
            item = null;
            if (!GameplayActive || !IsReady || (Claimant != null && Claimant != owner)) return false;
            item = Instantiate(prefab, transform.position + Vector3.up * .12f, Quaternion.identity);
            item.Configure(config, species);
            cooldown = Mathf.Max(0f, config.fodPickupCooldown);
            claimant = null;
            return true;
        }
        public void ResetForWave() { cooldown = 0f; claimant = null; }
    }
}
