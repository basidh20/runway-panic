using RunwayPanic.Core;
using RunwayPanic.Player;
using RunwayPanic.Weapons;
using UnityEngine;
using UnityEngine.UI;

namespace RunwayPanic.UI
{
    /// <summary>
    /// The player's heads-up display: HP bar, lives, coins, gun and ammo, wave number, wave timer and birds left,
    /// carried items, shield countdown, a red flash on taking damage, and a banner for the countdown before a wave,
    /// its start and its end. Lives on the player prefab's HUD canvas and updates from the player's and the
    /// <see cref="WaveManager"/>'s events; only the timers are read every frame.
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

        [Header("Wave")]
        [Tooltip("Time left in the running wave, m:ss. Turns red and pulses in the last seconds.")]
        [SerializeField] private Text waveTimerText;
        [SerializeField] private Text birdsLeftText;
        [SerializeField, Min(0f)] private float timerWarning = 10f;
        [SerializeField] private Color timerColor = Color.white;
        [SerializeField] private Color timerWarningColor = new Color(1f, 0.3f, 0.25f);
        [Tooltip("Centre banner: the countdown before a wave, its title as it starts, \"wave cleared\" / \"time up\".")]
        [SerializeField] private GameObject banner;
        [SerializeField] private Text bannerTitle;
        [SerializeField] private Text bannerSubtitle;
        [SerializeField, Min(0.1f)] private float bannerTime = 2.5f;

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
        private WaveManager waves;
        private float bannerHideTime;
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
            if (waves != null)
            {
                waves.CountdownStarted -= HandleCountdown;
                waves.WaveEnded -= HandleWaveEnded;
            }
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

        private void ConnectWaves()
        {
            if (waves != null || WaveManager.Instance == null)
                return;

            waves = WaveManager.Instance;
            waves.CountdownStarted += HandleCountdown;
            waves.WaveEnded += HandleWaveEnded;
        }

        private void Update()
        {
            if (gameManager == null)
                ConnectGameManager();
            if (waves == null)
                ConnectWaves();
            UpdateWave();

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

            if (wave > 0 && banner != null && waves != null && gameManager != null && gameManager.IsWaveActive)
                ShowBanner(BannerTitle(wave), "Defend the runway!", bannerTime);
        }

        // ---------- Wave timer, birds left and banner ----------

        private void UpdateWave()
        {
            bool running = waves != null && waves.Phase == WaveManager.WavePhase.Running;

            if (waveTimerText != null)
            {
                waveTimerText.enabled = running;
                if (running)
                {
                    float left = waves.TimeRemaining;
                    int seconds = Mathf.CeilToInt(left);
                    waveTimerText.text = $"{seconds / 60}:{seconds % 60:00}";
                    bool warning = left <= timerWarning;
                    waveTimerText.color = warning ? timerWarningColor : timerColor;
                    float pulse = warning ? 1f + 0.12f * Mathf.Abs(Mathf.Sin(Time.time * Mathf.PI)) : 1f;
                    waveTimerText.rectTransform.localScale = new Vector3(pulse, pulse, 1f);
                }
            }

            if (birdsLeftText != null)
            {
                birdsLeftText.enabled = running;
                if (running)
                    birdsLeftText.text = $"BIRDS LEFT  {waves.BirdsLeft}";
            }

            if (banner == null)
                return;

            if (waves != null && waves.Phase == WaveManager.WavePhase.Countdown && waves.CurrentData != null)
            {
                // The countdown keeps the banner up and ticks its last line.
                int seconds = Mathf.CeilToInt(waves.CountdownRemaining);
                string hint = string.IsNullOrEmpty(waves.CurrentData.Hint) ? "" : waves.CurrentData.Hint + "\n";
                int coming = gameManager != null ? gameManager.CurrentWave + 1 : 1;
                ShowBanner(BannerTitle(coming), $"{hint}Starts in {seconds}", 0.2f);
            }

            if (banner.activeSelf && Time.unscaledTime >= bannerHideTime)
                banner.SetActive(false);
        }

        private string BannerTitle(int wave)
        {
            string title = waves != null && waves.CurrentData != null ? waves.CurrentData.Title.ToUpperInvariant() : "";
            return string.IsNullOrEmpty(title) ? $"WAVE {wave}" : $"WAVE {wave}  ·  {title}";
        }

        private void HandleCountdown(int wave, Data.WaveData data) => UpdateWave();

        private void HandleWaveEnded(int wave, bool cleared, int bonus)
        {
            if (cleared)
                ShowBanner("WAVE CLEARED", bonus > 0 ? $"+{bonus} coins bonus" : "", bannerTime);
            else
                ShowBanner("TIME UP", "The birds fly off. You held the runway.", bannerTime);
        }

        private void ShowBanner(string title, string subtitle, float seconds)
        {
            banner.SetActive(true);
            if (bannerTitle != null) bannerTitle.text = title;
            if (bannerSubtitle != null) bannerSubtitle.text = subtitle;
            bannerHideTime = Time.unscaledTime + seconds;
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
