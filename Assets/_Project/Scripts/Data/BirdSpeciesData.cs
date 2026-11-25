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
        [Tooltip("The weakest gun tier that does full damage. That tier and every stronger one hit for 100%.")]
        [SerializeField] private WeaponRarity requiredWeaponRarity;
        [Tooltip("Damage share from guns weaker than the required tier: element 0 = one tier weaker, 1 = two tiers, " +
                 "2 = three tiers. E.g. a pigeon (needs Uncommon) hit by the pistol (Common) takes element 0.")]
        [SerializeField] private float[] weakerWeaponMultipliers = { 0.5f, 0.25f, 0.1f };

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

        /// <summary>
        /// Share of a hit's damage this species takes from a gun of <paramref name="weaponRarity"/>: 1 from the required
        /// tier or any stronger one (a better gun never does worse), less for each tier the gun is weaker.
        /// </summary>
        public float GetDamageMultiplier(WeaponRarity weaponRarity)
        {
            int tiersWeaker = (int)requiredWeaponRarity - (int)weaponRarity;
            if (tiersWeaker <= 0)
                return 1f;
            if (weakerWeaponMultipliers == null || weakerWeaponMultipliers.Length == 0)
                return 0.1f;
            return Mathf.Clamp01(weakerWeaponMultipliers[Mathf.Min(tiersWeaker, weakerWeaponMultipliers.Length) - 1]);
        }

        /// <summary>
        /// Damage this species actually takes from a hit of the given rarity.
        /// </summary>
        public float GetDamageTaken(float amount, WeaponRarity weaponRarity) => amount * GetDamageMultiplier(weaponRarity);
    }
}
