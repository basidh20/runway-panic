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

        [Header("Firing")]
        [SerializeField, Min(0f)] private float damage = 10f;
        [Tooltip("Shots per second.")]
        [SerializeField, Min(0.01f)] private float fireRate = 4f;
        [SerializeField, Min(0f)] private float range = 60f;

        [Header("Ammo")]
        [SerializeField, Min(1)] private int magazineSize = 12;
        [SerializeField, Min(0f)] private float reloadTime = 1.5f;

        [Header("Shop")]
        [SerializeField, Min(0)] private int cost;

        public string WeaponName => weaponName;
        public WeaponRarity Rarity => rarity;
        public BirdSpecies TargetSpecies => targetSpecies;
        public GameObject Prefab => prefab;
        public float Damage => damage;
        public float FireRate => fireRate;
        public float FireInterval => 1f / fireRate;
        public float Range => range;
        public int MagazineSize => magazineSize;
        public float ReloadTime => reloadTime;
        public int Cost => cost;
    }
}
