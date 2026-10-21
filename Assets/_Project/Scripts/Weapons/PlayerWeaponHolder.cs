using System;
using System.Collections.Generic;
using RunwayPanic.Player;
using UnityEngine;

namespace RunwayPanic.Weapons
{
    /// <summary>
    /// Holds the player's equipped gun. In third-person it uses the Humanoid RightHand bone; in first-person it
    /// uses a camera-relative socket, so the gun never clips through the view when the hidden body animates.
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

        [Header("First-Person View")]
        [Tooltip("Gameplay camera that owns the first-person weapon socket.")]
        [SerializeField] private Transform firstPersonCamera;
        [SerializeField] private Vector3 firstPersonLocalPosition = new Vector3(0.30f, -0.24f, 0.45f);
        [SerializeField] private Vector3 firstPersonLocalEulerAngles;
        [Tooltip("Adds weapon sway, gait, firing, equip and reload presentation without moving the aiming camera.")]
        [SerializeField] private bool animateFirstPersonWeapon = true;

        /// <summary>Raised after a gun is equipped. Argument: its WeaponBase, or null if the gun has none.</summary>
        public event Action<WeaponBase> WeaponEquipped;

        private readonly Dictionary<GameObject, GameObject> spawnedWeapons = new Dictionary<GameObject, GameObject>();
        private GameObject currentInstance;
        private PlayerCameraRig cameraRig;

        public Transform HandSocket { get; private set; }
        public GameObject CurrentWeaponObject => currentInstance;
        public WeaponBase CurrentWeapon { get; private set; }

        private void Awake()
        {
            cameraRig = GetComponent<PlayerCameraRig>();
            if (characterAnimator == null)
                characterAnimator = GetComponentInChildren<Animator>();

            if (cameraRig != null && cameraRig.IsFirstPerson)
            {
                CreateSocket(firstPersonCamera != null ? firstPersonCamera : Camera.main != null ? Camera.main.transform : null,
                    "Socket_ViewWeapon", firstPersonLocalPosition, firstPersonLocalEulerAngles);
                if (HandSocket != null && animateFirstPersonWeapon)
                    HandSocket.gameObject.AddComponent<FirstPersonWeaponMotion>().Initialize(
                        this, cameraRig, GetComponent<PlayerController>());
                if (HandSocket != null)
                    FirstPersonArms.Attach(this, cameraRig, HandSocket.parent);
                return;
            }

            Transform hand = FindHandBone();
            if (hand == null)
            {
                Debug.LogError($"{nameof(PlayerWeaponHolder)} could not find the '{HandBoneName}' bone.", this);
                enabled = false;
                return;
            }

            CreateSocket(hand, SocketName, gripLocalPosition, gripLocalEulerAngles);
        }

        private void CreateSocket(Transform parent, string socketName, Vector3 localPosition, Vector3 localEulerAngles)
        {
            if (parent == null)
            {
                Debug.LogError($"{nameof(PlayerWeaponHolder)} could not find a parent for its weapon socket.", this);
                enabled = false;
                return;
            }

            HandSocket = new GameObject(socketName).transform;
            HandSocket.SetParent(parent, false);
            HandSocket.SetLocalPositionAndRotation(localPosition, Quaternion.Euler(localEulerAngles));
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
