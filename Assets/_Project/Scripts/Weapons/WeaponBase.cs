using System;
using RunwayPanic.Core;
using RunwayPanic.Data;
using UnityEngine;

namespace RunwayPanic.Weapons
{
    /// <summary>
    /// A gun the player can fire. Stats come from a <see cref="WeaponData"/> asset; this component
    /// enforces the fire rate, tracks the magazine and reloads automatically when it runs dry.
    /// Hit detection is done by the shooter, which reads <see cref="Muzzle"/>, <see cref="Damage"/> and <see cref="Range"/>.
    /// Goes on the root of a P_Gun_Tier prefab.
    /// </summary>
    public class WeaponBase : MonoBehaviour
    {
        private const string MuzzleSocketName = "Socket_Muzzle";

        [SerializeField] private WeaponData data;
        [Tooltip("Barrel tip. Found automatically by the name Socket_Muzzle if left empty.")]
        [SerializeField] private Transform muzzle;

        /// <summary>Raised each time a shot is fired.</summary>
        public event Action Fired;

        /// <summary>Raised whenever ammo changes. Arguments: ammo in magazine, magazine size.</summary>
        public event Action<int, int> AmmoChanged;

        public event Action ReloadStarted;
        public event Action ReloadFinished;

        private float nextFireTime;
        private float reloadEndTime;

        public WeaponData Data => data;
        public Transform Muzzle => muzzle != null ? muzzle : transform;
        public float Damage => data.Damage;
        public float Range => data.Range;
        public WeaponRarity Rarity => data.Rarity;
        public int AmmoInMagazine { get; private set; }
        public bool IsReloading { get; private set; }
        public bool CanFire => !IsReloading && AmmoInMagazine > 0 && Time.time >= nextFireTime;

        protected virtual void Awake()
        {
            if (data == null)
            {
                Debug.LogError($"{nameof(WeaponBase)} on '{name}' has no {nameof(WeaponData)} assigned.", this);
                enabled = false;
                return;
            }

            if (muzzle == null)
                muzzle = FindChildRecursive(transform, MuzzleSocketName);

            AmmoInMagazine = data.MagazineSize;
        }

        protected virtual void Start()
        {
            AmmoChanged?.Invoke(AmmoInMagazine, data.MagazineSize);
        }

        protected virtual void Update()
        {
            if (IsReloading && Time.time >= reloadEndTime)
                FinishReload();
        }

        /// <summary>
        /// Fires one shot if the fire rate, ammo and reload state allow it.
        /// Returns true when a shot was fired, so the caller should perform the hit check.
        /// </summary>
        public bool TryFire()
        {
            if (!enabled || !CanFire)
                return false;

            nextFireTime = Time.time + data.FireInterval;
            AmmoInMagazine--;

            Fired?.Invoke();
            AmmoChanged?.Invoke(AmmoInMagazine, data.MagazineSize);

            if (AmmoInMagazine == 0)
                StartReload();

            return true;
        }

        /// <summary>
        /// Starts a reload unless one is running or the magazine is already full.
        /// </summary>
        public void StartReload()
        {
            if (IsReloading || AmmoInMagazine == data.MagazineSize)
                return;

            IsReloading = true;
            reloadEndTime = Time.time + data.ReloadTime;
            ReloadStarted?.Invoke();
        }

        private void FinishReload()
        {
            IsReloading = false;
            AmmoInMagazine = data.MagazineSize;
            AmmoChanged?.Invoke(AmmoInMagazine, data.MagazineSize);
            ReloadFinished?.Invoke();
        }

        protected virtual void OnDisable()
        {
            // Holstering mid-reload cancels it; the magazine keeps its current ammo.
            IsReloading = false;
        }

        private static Transform FindChildRecursive(Transform parent, string childName)
        {
            foreach (Transform child in parent)
            {
                if (child.name == childName)
                    return child;

                Transform found = FindChildRecursive(child, childName);
                if (found != null)
                    return found;
            }

            return null;
        }
    }
}
