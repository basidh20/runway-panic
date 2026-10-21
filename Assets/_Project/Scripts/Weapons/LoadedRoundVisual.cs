using UnityEngine;

namespace RunwayPanic.Weapons
{
    /// <summary>
    /// Shows the round sitting in the weapon only while it is loaded — the RPG's warhead in the front of the tube is
    /// gone once fired and comes back when the reload finishes. Goes on the root of the gun prefab, next to
    /// <see cref="WeaponBase"/>.
    /// </summary>
    [RequireComponent(typeof(WeaponBase))]
    public sealed class LoadedRoundVisual : MonoBehaviour
    {
        [Tooltip("The visible round (P_Gun_Tier4: RPG_Rocket).")]
        [SerializeField] private GameObject round;

        private WeaponBase weapon;
        private bool started;

        private void Awake()
        {
            weapon = GetComponent<WeaponBase>();
        }

        private void OnEnable()
        {
            weapon.AmmoChanged += HandleAmmoChanged;
            if (started)
                Show(weapon.AmmoInMagazine > 0);
        }

        private void Start()
        {
            started = true;
            Show(weapon.AmmoInMagazine > 0);
        }

        private void OnDisable()
        {
            weapon.AmmoChanged -= HandleAmmoChanged;
        }

        private void HandleAmmoChanged(int ammo, int magazineSize) => Show(ammo > 0);

        private void Show(bool loaded)
        {
            if (round != null)
                round.SetActive(loaded);
        }
    }
}
