using RunwayPanic.Core;
using UnityEngine;

namespace RunwayPanic.Data
{
    /// <summary>
    /// Tunable stats for one weapon. One asset per gun tier.
    /// </summary>
    [CreateAssetMenu(fileName = "Weapon_New", menuName = "Runway Panic/Data/Weapon")]
    public sealed class WeaponData : ScriptableObject
    {
        [Header("Identity")]
        [SerializeField] private string weaponName = "New Weapon";
        [SerializeField] private WeaponRarity rarity;
        [Tooltip("The species this gun is meant for. Effectiveness is decided by BirdSpeciesData.RequiredWeaponRarity.")]
        [SerializeField] private BirdSpecies targetSpecies;
        [Tooltip("One of the P_Gun_Tier1-4 prefabs.")]
        [SerializeField] private GameObject prefab;
        [Tooltip("How the officer holds this gun (picks the upper-body animations).")]
        [SerializeField] private WeaponHoldType holdType;

        [Header("Firing")]
        [SerializeField, Min(0f)] private float damage = 10f;
        [Tooltip("Shots per second.")]
        [SerializeField, Min(0.01f)] private float fireRate = 4f;
        [SerializeField, Min(0f)] private float range = 60f;

        [Header("Ammo")]
        [SerializeField, Min(1)] private int magazineSize = 12;
        [SerializeField, Min(0f)] private float reloadTime = 1.5f;

        [Header("Aiming")]
        [Tooltip("Camera field of view (degrees) while aiming with this gun. Hip fire uses the camera's own.")]
        [SerializeField, Range(5f, 90f)] private float aimFieldOfView = 45f;
        [Tooltip("Aiming looks through a scope: first-person view, scope overlay, no crosshair, no spread.")]
        [SerializeField] private bool hasScope;
        [Tooltip("Look sensitivity while aiming, as a multiple of the normal sensitivity.")]
        [SerializeField, Range(0.05f, 1f)] private float aimSensitivity = 0.75f;
        [Tooltip("Shot spread (cone half-angle, degrees) from the hip and while aiming.")]
        [SerializeField, Min(0f)] private float hipSpread = 2f;
        [SerializeField, Min(0f)] private float aimSpread = 0.5f;
        [Tooltip("Spread added by each shot (sustained fire blooms), its cap, and how fast it settles (degrees/s).")]
        [SerializeField, Min(0f)] private float spreadPerShot = 0.5f;
        [SerializeField, Min(0f)] private float maxSpread = 5f;
        [SerializeField, Min(0f)] private float spreadRecovery = 8f;
        [Tooltip("Camera kick upward per shot (degrees). Most of it recovers by itself.")]
        [SerializeField, Min(0f)] private float recoil = 1f;
        [Tooltip("Radius of the bullet when testing targets (metres): small, fast birds need a little forgiveness.")]
        [SerializeField, Min(0f)] private float hitRadius = 0.1f;

        [Header("Shop")]
        [SerializeField, Min(0)] private int cost;

        public string WeaponName => weaponName;
        public WeaponRarity Rarity => rarity;
        public BirdSpecies TargetSpecies => targetSpecies;
        public GameObject Prefab => prefab;
        public WeaponHoldType HoldType => holdType;
        public float Damage => damage;
        public float FireRate => fireRate;
        public float FireInterval => 1f / fireRate;
        public float Range => range;
        public int MagazineSize => magazineSize;
        public float ReloadTime => reloadTime;
        public float AimFieldOfView => aimFieldOfView;
        public bool HasScope => hasScope;
        public float AimSensitivity => aimSensitivity;
        public float HipSpread => hipSpread;
        public float AimSpread => hasScope ? 0f : aimSpread;
        public float SpreadPerShot => spreadPerShot;
        public float MaxSpread => maxSpread;
        public float SpreadRecovery => spreadRecovery;
        public float Recoil => recoil;
        public float HitRadius => hitRadius;
        public int Cost => cost;
    }
}
