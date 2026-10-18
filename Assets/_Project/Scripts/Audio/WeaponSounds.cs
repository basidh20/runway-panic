using RunwayPanic.Core;
using RunwayPanic.Weapons;
using UnityEngine;

namespace RunwayPanic.Audio
{
    /// <summary>
    /// Weapon sounds on the player: each gun's shot (picked by its rarity tier), reload start and finish, drawing a
    /// gun, and the bullet hitting a surface (bird hits are <see cref="BirdSounds"/>' job). Only listens to existing
    /// events — <see cref="PlayerShooting.ShotFired"/>, <see cref="PlayerWeaponHolder.WeaponEquipped"/>,
    /// <see cref="WeaponBase.ReloadStarted"/> / <see cref="WeaponBase.ReloadFinished"/>.
    /// </summary>
    public sealed class WeaponSounds : MonoBehaviour
    {
        [Tooltip("Shot per weapon tier: Common (pistol), Uncommon (assault rifle), Rare (sniper), Legendary (RPG).")]
        [SerializeField] private SoundCue[] shotByRarity = new SoundCue[4];
        [SerializeField] private SoundCue reloadStart;
        [SerializeField] private SoundCue reloadFinish;
        [SerializeField] private SoundCue equip;
        [Tooltip("A bullet hitting the ground, a wall or an aircraft.")]
        [SerializeField] private SoundCue impact;

        private PlayerShooting shooting;
        private PlayerWeaponHolder holder;
        private WeaponBase watched;

        private void Awake()
        {
            shooting = GetComponent<PlayerShooting>();
            holder = GetComponent<PlayerWeaponHolder>();
        }

        private void OnEnable()
        {
            if (shooting != null)
                shooting.ShotFired += HandleShot;
            if (holder != null)
            {
                holder.WeaponEquipped += HandleEquipped;
                Watch(holder.CurrentWeapon);
            }
        }

        private void OnDisable()
        {
            if (shooting != null)
                shooting.ShotFired -= HandleShot;
            if (holder != null)
                holder.WeaponEquipped -= HandleEquipped;
            Watch(null);
        }

        private void HandleShot(Vector3 muzzle, Vector3 end, IDamageable target)
        {
            WeaponBase weapon = holder != null ? holder.CurrentWeapon : null;
            int tier = weapon != null ? Mathf.Clamp((int)weapon.Rarity, 0, shotByRarity.Length - 1) : 0;
            SoundPlayer.Play(shotByRarity[tier], muzzle);

            // A miss ends exactly at the gun's range; anything shorter hit something solid.
            bool hitSurface = target == null && weapon != null && (end - muzzle).magnitude < weapon.Range - 0.5f;
            if (hitSurface)
                SoundPlayer.Play(impact, end);
        }

        private void HandleEquipped(WeaponBase weapon)
        {
            Watch(weapon);
            SoundPlayer.Play(equip, transform.position + Vector3.up);
        }

        private void Watch(WeaponBase weapon)
        {
            if (watched != null)
            {
                watched.ReloadStarted -= HandleReloadStarted;
                watched.ReloadFinished -= HandleReloadFinished;
            }

            watched = weapon;
            if (watched != null)
            {
                watched.ReloadStarted += HandleReloadStarted;
                watched.ReloadFinished += HandleReloadFinished;
            }
        }

        private void HandleReloadStarted() => SoundPlayer.Play(reloadStart, transform.position + Vector3.up);

        private void HandleReloadFinished() => SoundPlayer.Play(reloadFinish, transform.position + Vector3.up);
    }
}
