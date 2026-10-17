using System;
using System.Collections.Generic;
using UnityEngine;

namespace RunwayPanic.Weapons
{
    /// <summary>
    /// Holds the player's equipped gun in the right hand. Creates a hand socket under the
    /// Humanoid RightHand bone and parents gun instances to it at local zero, as the gun prefabs expect.
    /// Guns are spawned once and then shown or hidden, so each keeps its own ammo between switches.
    /// </summary>
    public sealed class PlayerWeaponHolder : MonoBehaviour
    {
        private const string HandBoneName = "RightHand";
        private const string SocketName = "Socket_RightHand";

        [Tooltip("Animator of the player model. Found in children if left empty.")]
        [SerializeField] private Animator characterAnimator;
        [Tooltip("Gun prefab equipped on start. Leave empty to start unarmed.")]
        [SerializeField] private GameObject startingWeapon;

        [Header("Grip Offset (relative to the hand bone)")]
        [SerializeField] private Vector3 gripLocalPosition;
        [SerializeField] private Vector3 gripLocalEulerAngles;

        /// <summary>Raised after a gun is equipped. Argument: its WeaponBase, or null if the gun has none.</summary>
        public event Action<WeaponBase> WeaponEquipped;

        private readonly Dictionary<GameObject, GameObject> spawnedWeapons = new Dictionary<GameObject, GameObject>();
        private GameObject currentInstance;

        public Transform HandSocket { get; private set; }
        public GameObject CurrentWeaponObject => currentInstance;
        public WeaponBase CurrentWeapon { get; private set; }

        private void Awake()
        {
            if (characterAnimator == null)
                characterAnimator = GetComponentInChildren<Animator>();

            Transform hand = FindHandBone();
            if (hand == null)
            {
                Debug.LogError($"{nameof(PlayerWeaponHolder)} could not find the '{HandBoneName}' bone.", this);
                enabled = false;
                return;
            }

            HandSocket = new GameObject(SocketName).transform;
            HandSocket.SetParent(hand, false);
            HandSocket.SetLocalPositionAndRotation(gripLocalPosition, Quaternion.Euler(gripLocalEulerAngles));
        }

        private void Start()
        {
            if (startingWeapon != null)
                Equip(startingWeapon);
        }

        /// <summary>
        /// Puts the given gun prefab in the player's hand and hides the previous one.
        /// </summary>
        public void Equip(GameObject weaponPrefab)
        {
            if (!enabled || weaponPrefab == null)
                return;

            if (!spawnedWeapons.TryGetValue(weaponPrefab, out GameObject instance))
            {
                instance = Instantiate(weaponPrefab, HandSocket, false);
                instance.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
                PrepareForHolding(instance);
                spawnedWeapons.Add(weaponPrefab, instance);
            }

            if (currentInstance != null && currentInstance != instance)
                currentInstance.SetActive(false);

            currentInstance = instance;
            currentInstance.SetActive(true);
            CurrentWeapon = currentInstance.GetComponent<WeaponBase>();
            WeaponEquipped?.Invoke(CurrentWeapon);
        }

        private Transform FindHandBone()
        {
            if (characterAnimator != null && characterAnimator.isHuman)
            {
                Transform bone = characterAnimator.GetBoneTransform(HumanBodyBones.RightHand);
                if (bone != null)
                    return bone;
            }

            return FindChildRecursive(transform, HandBoneName);
        }

        private static void PrepareForHolding(GameObject weapon)
        {
            // A held gun must not block the player's own shots or push the CharacterController.
            foreach (Collider weaponCollider in weapon.GetComponentsInChildren<Collider>(true))
                weaponCollider.enabled = false;

            foreach (Rigidbody body in weapon.GetComponentsInChildren<Rigidbody>(true))
                body.isKinematic = true;
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
