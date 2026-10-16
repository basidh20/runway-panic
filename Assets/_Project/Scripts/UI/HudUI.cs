using RunwayPanic.Core;
using RunwayPanic.Player;
using RunwayPanic.Weapons;
using UnityEngine;
using UnityEngine.UI;

namespace RunwayPanic.UI
{
    /// <summary>
    /// The player's heads-up display: HP bar, lives, coins, gun and ammo, wave number, carried items,
    /// shield countdown and a red flash on taking damage. Lives on the player prefab's HUD canvas and
    /// updates from the player's events rather than polling.
    /// </summary>
    public sealed class HudUI : MonoBehaviour
    {
        [Header("Health")]
        [Tooltip("Fill bar: its width is scaled with the HP fraction (anchored to the left).")]
        [SerializeField] private RectTransform healthFill;
        [SerializeField] private Image healthFillImage;
        [SerializeField] private Text healthText;
        [SerializeField] private Text livesText;
        [SerializeField] private Color healthyColor = new Color(0.3f, 0.85f, 0.35f);
        [SerializeField] private Color hurtColor = new Color(0.95f, 0.8f, 0.2f);
        [SerializeField] private Color criticalColor = new Color(0.9f, 0.2f, 0.2f);

        [Header("Status")]
        [SerializeField] private Text coinsText;
        [SerializeField] private Text weaponText;
        [SerializeField] private Text waveText;
        [SerializeField] private Text itemsText;
        [SerializeField] private Text shieldText;

        [Header("Damage flash")]
        [SerializeField] private Image damageFlash;
        [SerializeField, Range(0f, 1f)] private float flashAlpha = 0.35f;
        [SerializeField, Min(0.01f)] private float flashFadeTime = 0.4f;

        private PlayerHealth health;
        private PlayerLifeManager lives;
        private CoinManager coins;
        private PlayerInventory inventory;
        private PlayerWeaponHolder weaponHolder;
        private PlayerShield shield;
        private GameManager gameManager;
        private WeaponBase trackedWeapon;
        private float flashAmount;

        private void Awake()
        {
            health = GetComponentInParent<PlayerHealth>();
            lives = GetComponentInParent<PlayerLifeManager>();
            coins = GetComponentInParent<CoinManager>();
            inventory = GetComponentInParent<PlayerInventory>();
            weaponHolder = GetComponentInParent<PlayerWeaponHolder>();
            shield = GetComponentInParent<PlayerShield>();
            SetFlash(0f);
        }

        private void OnEnable()
        {
            if (health != null) { health.HealthChanged += ShowHealth; health.Damaged += Flash; }
            if (lives != null) lives.LivesChanged += ShowLives;
            if (coins != null) coins.CoinsChanged += ShowCoins;
            if (inventory != null) inventory.ItemCountChanged += ShowItems;
            if (weaponHolder != null) weaponHolder.WeaponEquipped += TrackWeapon;
        }

        private void OnDisable()
        {
            if (health != null) { health.HealthChanged -= ShowHealth; health.Damaged -= Flash; }
            if (lives != null) lives.LivesChanged -= ShowLives;
            if (coins != null) coins.CoinsChanged -= ShowCoins;
            if (inventory != null) inventory.ItemCountChanged -= ShowItems;
            if (weaponHolder != null) weaponHolder.WeaponEquipped -= TrackWeapon;
            TrackWeapon(null);
            if (gameManager != null) gameManager.WaveStarted -= ShowWave;
        }

        private void Start()
        {
            ConnectGameManager();

            if (health != null) ShowHealth(health.CurrentHP, health.MaxHP);
            if (lives != null) ShowLives(lives.CurrentLives, lives.MaxLives);
            if (coins != null) ShowCoins(coins.Coins);
            if (weaponHolder != null) TrackWeapon(weaponHolder.CurrentWeapon);
            ShowItems(ItemType.Medkit, 0);
            if (gameManager == null)
                ShowWave(0);
        }

        private void ConnectGameManager()
        {
            // The GameManager is a separate scene object and may be created after the player.
            if (gameManager != null || GameManager.Instance == null)
                return;

            gameManager = GameManager.Instance;
            gameManager.WaveStarted += ShowWave;
            ShowWave(gameManager.CurrentWave);
        }

        private void Update()
        {
            if (gameManager == null)
                ConnectGameManager();

            // The only values that change continuously: the shield countdown and the damage flash fading.
            if (shieldText != null)
            {
                bool active = shield != null && shield.IsActive;
                shieldText.enabled = active;
                if (active)
                    shieldText.text = $"SHIELD  {Mathf.CeilToInt(shield.RemainingTime)}s";
            }

            if (flashAmount > 0f)
            {
                flashAmount = Mathf.Max(0f, flashAmount - Time.unscaledDeltaTime / flashFadeTime);
                SetFlash(flashAmount);
            }
        }

        private void ShowHealth(float current, float max)
        {
            float fraction = max > 0f ? Mathf.Clamp01(current / max) : 0f;
            if (healthFill != null)
                healthFill.anchorMax = new Vector2(fraction, healthFill.anchorMax.y);
            if (healthFillImage != null)
                healthFillImage.color = fraction > 0.5f ? healthyColor : fraction > 0.25f ? hurtColor : criticalColor;
            if (healthText != null)
                healthText.text = $"{Mathf.CeilToInt(current)} / {Mathf.CeilToInt(max)}";
        }

        private void ShowLives(int current, int max)
        {
            if (livesText != null)
                livesText.text = $"LIVES  {current} / {max}";
        }

        private void ShowCoins(int amount)
        {
            if (coinsText != null)
                coinsText.text = $"COINS  {amount}";
        }

        private void ShowItems(ItemType changed, int count)
        {
            if (itemsText == null || inventory == null)
                return;

            itemsText.text = $"MEDKITS {inventory.GetCount(ItemType.Medkit)} [Q]     SHIELDS {inventory.GetCount(ItemType.Shield)} [F]";
        }

        private void ShowWave(int wave)
        {
            if (waveText == null)
                return;

            waveText.enabled = wave > 0;
            if (wave > 0)
                waveText.text = $"WAVE  {wave} / {(gameManager != null ? gameManager.TotalWaves : wave)}";
        }

        // ---------- Weapon and ammo ----------

        private void TrackWeapon(WeaponBase weapon)
        {
            if (trackedWeapon != null)
            {
                trackedWeapon.AmmoChanged -= HandleAmmoChanged;
                trackedWeapon.ReloadStarted -= ShowWeapon;
                trackedWeapon.ReloadFinished -= ShowWeapon;
            }

            trackedWeapon = weapon;

            if (trackedWeapon != null)
            {
                trackedWeapon.AmmoChanged += HandleAmmoChanged;
                trackedWeapon.ReloadStarted += ShowWeapon;
                trackedWeapon.ReloadFinished += ShowWeapon;
            }

            ShowWeapon();
        }

        private void HandleAmmoChanged(int ammo, int magazine) => ShowWeapon();

        private void ShowWeapon()
        {
            if (weaponText == null)
                return;

            if (trackedWeapon == null || trackedWeapon.Data == null)
            {
                weaponText.text = "UNARMED";
                return;
            }

            string ammo = trackedWeapon.IsReloading
                ? "RELOADING"
                : $"{trackedWeapon.AmmoInMagazine} / {trackedWeapon.Data.MagazineSize}";
            weaponText.text = $"{trackedWeapon.Data.WeaponName.ToUpperInvariant()}\n{ammo}";
        }

        // ---------- Damage flash ----------

        private void Flash(float damage)
        {
            flashAmount = 1f;
            SetFlash(1f);
        }

        private void SetFlash(float amount)
        {
            if (damageFlash == null)
                return;

            Color c = damageFlash.color;
            c.a = amount * flashAlpha;
            damageFlash.color = c;
            damageFlash.enabled = c.a > 0.001f;
        }
    }
}
