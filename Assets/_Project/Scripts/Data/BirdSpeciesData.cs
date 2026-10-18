using RunwayPanic.Core;
using UnityEngine;

namespace RunwayPanic.Data
{
    /// <summary>
    /// Tunable stats for one bird species. One asset per species.
    /// </summary>
    [CreateAssetMenu(fileName = "BirdSpecies_New", menuName = "Runway Panic/Data/Bird Species")]
    public sealed class BirdSpeciesData : ScriptableObject
    {
        [Header("Identity")]
        [SerializeField] private string speciesName = "New Bird";
        [SerializeField] private BirdSpecies species;
        [SerializeField] private GameObject prefab;

        [Header("Stats")]
        [SerializeField, Min(1f)] private float maxHP = 30f;
        [SerializeField, Min(0f)] private float moveSpeed = 4f;
        [SerializeField, Min(0f)] private float attackDamage = 10f;
        [SerializeField, Min(0f)] private float attackRange = 1.5f;
        [SerializeField, Min(0f)] private float attackCooldown = 1.5f;
        [SerializeField, Min(0)] private int coinReward = 5;

        [Header("Weapon Effectiveness")]
        [SerializeField] private WeaponRarity requiredWeaponRarity;
        [Tooltip("Damage multiplier for hits from any rarity other than the required one.")]
        [SerializeField, Range(0f, 1f)] private float wrongWeaponDamageMultiplier = 0.1f;

        public string SpeciesName => speciesName;
        public BirdSpecies Species => species;
        public GameObject Prefab => prefab;
        public float MaxHP => maxHP;
        public float MoveSpeed => moveSpeed;
        public float AttackDamage => attackDamage;
        public float AttackRange => attackRange;
        public float AttackCooldown => attackCooldown;
        public int CoinReward => coinReward;
        public WeaponRarity RequiredWeaponRarity => requiredWeaponRarity;
        public float WrongWeaponDamageMultiplier => wrongWeaponDamageMultiplier;

        /// <summary>
        /// Damage this species actually takes from a hit of the given rarity.
        /// </summary>
        public float GetDamageTaken(float amount, WeaponRarity weaponRarity)
        {
            return weaponRarity == requiredWeaponRarity
                ? amount
                : amount * wrongWeaponDamageMultiplier;
        }
    }
}
