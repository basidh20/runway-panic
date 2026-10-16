using RunwayPanic.Core;
using RunwayPanic.Data;
using UnityEngine;

namespace RunwayPanic.Weapons
{
    /// <summary>
    /// Shooting-range target for testing weapons. With a <see cref="BirdSpeciesData"/> assigned it takes
    /// damage exactly like that bird (wrong-rarity hits are reduced); without one it takes raw damage.
    /// Shows its HP above it, flashes when hit and resets a moment after it dies.
    /// </summary>
    public sealed class DamageDummy : MonoBehaviour, IDamageable
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [Tooltip("Bird to imitate. Leave empty for a plain target that uses Fallback Max HP.")]
        [SerializeField] private BirdSpeciesData species;
        [SerializeField, Min(1f)] private float fallbackMaxHP = 100f;
        [SerializeField, Min(0f)] private float resetDelay = 2f;

        [Header("Feedback")]
        [SerializeField] private TextMesh label;
        [SerializeField] private Renderer bodyRenderer;
        [SerializeField] private Color hitColor = new Color(1f, 0.3f, 0.25f);
        [SerializeField, Min(0f)] private float hitFlashDuration = 0.1f;

        private MaterialPropertyBlock propertyBlock;
        private Color baseColor = Color.white;
        private float flashEndTime;
        private float resetTime;
        private float lastDamage;

        public float MaxHP => species != null ? species.MaxHP : fallbackMaxHP;
        public float CurrentHP { get; private set; }
        public bool IsAlive => CurrentHP > 0f;

        private void Awake()
        {
            propertyBlock = new MaterialPropertyBlock();
            if (bodyRenderer != null && bodyRenderer.sharedMaterial != null && bodyRenderer.sharedMaterial.HasProperty(BaseColorId))
                baseColor = bodyRenderer.sharedMaterial.GetColor(BaseColorId);

            CurrentHP = MaxHP;
            UpdateLabel();
        }

        private void Update()
        {
            if (flashEndTime > 0f && Time.time >= flashEndTime)
            {
                flashEndTime = 0f;
                SetBodyColor(baseColor);
            }

            if (!IsAlive && Time.time >= resetTime)
            {
                CurrentHP = MaxHP;
                lastDamage = 0f;
                UpdateLabel();
            }
        }

        public void TakeDamage(float amount, WeaponRarity weaponRarity)
        {
            if (!IsAlive || amount <= 0f)
                return;

            lastDamage = species != null ? species.GetDamageTaken(amount, weaponRarity) : amount;
            CurrentHP = Mathf.Max(0f, CurrentHP - lastDamage);

            if (!IsAlive)
                resetTime = Time.time + resetDelay;

            flashEndTime = Time.time + hitFlashDuration;
            SetBodyColor(hitColor);
            UpdateLabel();

            Debug.Log($"[DamageDummy] {name}: {weaponRarity} hit for {amount}, took {lastDamage} -> {CurrentHP}/{MaxHP}", this);
        }

        private void UpdateLabel()
        {
            if (label == null)
                return;

            string title = species != null ? $"{species.SpeciesName}\nneeds {species.RequiredWeaponRarity}" : name;
            string status = IsAlive ? $"{CurrentHP:0.#}/{MaxHP:0.#} HP" : "DOWN";
            string hit = lastDamage > 0f ? $"\n-{lastDamage:0.#}" : string.Empty;
            label.text = $"{title}\n{status}{hit}";
        }

        private void SetBodyColor(Color color)
        {
            if (bodyRenderer == null)
                return;

            bodyRenderer.GetPropertyBlock(propertyBlock);
            propertyBlock.SetColor(BaseColorId, color);
            bodyRenderer.SetPropertyBlock(propertyBlock);
        }
    }
}
