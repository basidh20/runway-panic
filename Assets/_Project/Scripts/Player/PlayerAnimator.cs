using RunwayPanic.Data;
using RunwayPanic.Weapons;
using UnityEngine;

namespace RunwayPanic.Player
{
    /// <summary>
    /// Drives the officer's Animator from the player's real movement. The <see cref="CharacterController"/> moves the
    /// player (no root motion); this only tells the Animator what the player is doing:
    /// <list type="bullet">
    /// <item><c>Speed</c> — real horizontal speed, so the locomotion blend tree picks idle, walk or run (running into a
    /// wall reads as standing still, not as running).</item>
    /// <item><c>Jump</c> — trigger on take-off; <c>IsGrounded</c> and <c>VerticalSpeed</c> — for falling and landing,
    /// including walking off a ledge.</item>
    /// <item>Weapons: <c>WeaponType</c> (0 none, 1 pistol, 2 rifle), <c>Equip</c> and <c>Fire</c> triggers. The
    /// upper-body layer fades in while a gun is held, so the arms hold it while the legs keep moving.</item>
    /// <item>Reload: <c>Reload</c> trigger and <c>IsReloading</c>, with <c>ReloadSpeed</c> = clip length ÷ the gun's
    /// reload time, so the animation ends exactly when the magazine is full again.</item>
    /// <item>Actions: <c>UseMedkit</c>, <c>UseShield</c> and <c>Interact</c> triggers, <c>IsPushing</c> while pushing a
    /// barricade (gun lowered, left hand busy).</item>
    /// <item>Reactions: <c>Hit</c> trigger on every survivable hit (an additive flinch); <c>Dead</c> while health is
    /// 0, which plays the death and puts the gun arm down until the respawn heals the player.</item>
    /// </list>
    /// </summary>
    [DefaultExecutionOrder(-50)]
    [RequireComponent(typeof(CharacterController))]
    public sealed class PlayerAnimator : MonoBehaviour
    {
        private static readonly int SpeedId = Animator.StringToHash("Speed");
        private static readonly int JumpId = Animator.StringToHash("Jump");
        private static readonly int IsGroundedId = Animator.StringToHash("IsGrounded");
        private static readonly int VerticalSpeedId = Animator.StringToHash("VerticalSpeed");
        private static readonly int WeaponTypeId = Animator.StringToHash("WeaponType");
        private static readonly int EquipId = Animator.StringToHash("Equip");
        private static readonly int FireId = Animator.StringToHash("Fire");
        private static readonly int ReloadId = Animator.StringToHash("Reload");
        private static readonly int IsReloadingId = Animator.StringToHash("IsReloading");
        private static readonly int ReloadSpeedId = Animator.StringToHash("ReloadSpeed");
        private static readonly int HitId = Animator.StringToHash("Hit");
        private static readonly int DeadId = Animator.StringToHash("Dead");
        private static readonly int UseMedkitId = Animator.StringToHash("UseMedkit");
        private static readonly int UseShieldId = Animator.StringToHash("UseShield");
        private static readonly int InteractId = Animator.StringToHash("Interact");
        private static readonly int IsPushingId = Animator.StringToHash("IsPushing");

        // Reload clips are played faster or slower so they last exactly the gun's reload time.
        private const string PistolReloadClip = "A_Player_PistolReload";
        private const string RifleReloadClip = "A_Player_RifleReload";
        private const float MinReloadTime = 0.1f;

        private const int UpperBodyLayer = 1;
        private const int NoWeapon = 0;
        private const int PistolWeapon = 1;
        private const int RifleWeapon = 2;

        [Tooltip("Animator on the officer model. Found in children when left empty.")]
        [SerializeField] private Animator animator;
        [Tooltip("Seconds the Speed parameter takes to follow the real speed. Higher = softer blends.")]
        [SerializeField, Min(0f)] private float speedDampTime = 0.1f;
        [Tooltip("Faster blend into the first step, so the body does not slide before the legs start walking.")]
        [SerializeField, Min(0f)] private float startSpeedDampTime = 0.025f;
        [Tooltip("How fast the arms blend into or out of holding a gun (per second).")]
        [SerializeField, Min(0.1f)] private float armsBlendSpeed = 6f;

        private CharacterController characterController;
        private PlayerController playerController;
        private PlayerWeaponHolder weaponHolder;
        private PlayerHealth playerHealth;
        private PlayerItemUser itemUser;
        private PlayerInteraction interaction;
        private PlayerPusher pusher;
        private WeaponBase currentWeapon;
        private float pistolReloadLength;
        private float rifleReloadLength;

        /// <summary>The Animator being driven (null if the model has none).</summary>
        public Animator Animator => animator;

        private void Awake()
        {
            characterController = GetComponent<CharacterController>();
            playerController = GetComponent<PlayerController>();
            weaponHolder = GetComponent<PlayerWeaponHolder>();
            playerHealth = GetComponent<PlayerHealth>();
            itemUser = GetComponent<PlayerItemUser>();
            interaction = GetComponent<PlayerInteraction>();
            pusher = GetComponent<PlayerPusher>();
            if (animator == null)
                animator = GetComponentInChildren<Animator>();

            if (animator == null)
            {
                Debug.LogError($"{nameof(PlayerAnimator)} on '{name}' found no Animator in its children.", this);
                enabled = false;
                return;
            }

            // Movement belongs to the CharacterController; clips are "In Place".
            animator.applyRootMotion = false;
            animator.updateMode = AnimatorUpdateMode.Normal;

            if (animator.runtimeAnimatorController != null)
            {
                foreach (AnimationClip clip in animator.runtimeAnimatorController.animationClips)
                {
                    if (clip.name == PistolReloadClip)
                        pistolReloadLength = clip.length;
                    else if (clip.name == RifleReloadClip)
                        rifleReloadLength = clip.length;
                }
            }
        }

        private void OnEnable()
        {
            if (playerController != null)
                playerController.Jumped += HandleJumped;
            if (playerHealth != null)
                playerHealth.Damaged += HandleDamaged;
            if (itemUser != null)
            {
                itemUser.MedkitUsed += HandleMedkitUsed;
                itemUser.ShieldUsed += HandleShieldUsed;
            }
            if (interaction != null)
                interaction.Interacted += HandleInteracted;
            if (weaponHolder != null)
            {
                weaponHolder.WeaponEquipped += HandleWeaponEquipped;
                if (weaponHolder.CurrentWeaponObject != null)
                    WatchWeapon(weaponHolder.CurrentWeapon); // re-enabled while holding a gun
            }
        }

        private void OnDisable()
        {
            if (playerController != null)
                playerController.Jumped -= HandleJumped;
            if (playerHealth != null)
                playerHealth.Damaged -= HandleDamaged;
            if (itemUser != null)
            {
                itemUser.MedkitUsed -= HandleMedkitUsed;
                itemUser.ShieldUsed -= HandleShieldUsed;
            }
            if (interaction != null)
                interaction.Interacted -= HandleInteracted;
            if (weaponHolder != null)
                weaponHolder.WeaponEquipped -= HandleWeaponEquipped;
            WatchWeapon(null);
        }

        private void Start()
        {
            // A gun equipped before this component was enabled.
            if (weaponHolder != null && weaponHolder.CurrentWeaponObject != null)
                HandleWeaponEquipped(weaponHolder.CurrentWeapon);
        }

        private void Update()
        {
            Vector3 velocity = characterController.velocity;
            velocity.y = 0f;
            float speed = velocity.magnitude;
            float dampTime = speed > animator.GetFloat(SpeedId) ? startSpeedDampTime : speedDampTime;
            animator.SetFloat(SpeedId, speed, dampTime, Time.deltaTime);

            if (playerController != null)
            {
                animator.SetBool(IsGroundedId, playerController.IsGrounded);
                animator.SetFloat(VerticalSpeedId, playerController.VerticalSpeed);
            }

            // Dead until the respawn resets the health; the Death state holds the body on the ground until then.
            bool alive = playerHealth == null || playerHealth.IsAlive;
            animator.SetBool(DeadId, !alive);
            animator.SetBool(IsPushingId, alive && pusher != null && pusher.IsPushing);

            if (animator.layerCount > UpperBodyLayer)
            {
                bool holdingGun = alive && weaponHolder != null && weaponHolder.CurrentWeaponObject != null &&
                                  weaponHolder.CurrentWeaponObject.activeInHierarchy;
                float armsWeight = Mathf.MoveTowards(animator.GetLayerWeight(UpperBodyLayer), holdingGun ? 1f : 0f,
                    armsBlendSpeed * Time.deltaTime);
                animator.SetLayerWeight(UpperBodyLayer, armsWeight);
            }
        }

        private void HandleJumped()
        {
            animator.SetTrigger(JumpId);
        }

        private void HandleMedkitUsed() => animator.SetTrigger(UseMedkitId);

        private void HandleShieldUsed() => animator.SetTrigger(UseShieldId);

        private void HandleInteracted(RunwayPanic.Core.IInteractable interactable) => animator.SetTrigger(InteractId);

        // The killing blow plays Death instead of a flinch (health is already 0 when Damaged is raised).
        private void HandleDamaged(float amount)
        {
            if (playerHealth.IsAlive)
                animator.SetTrigger(HitId);
        }

        private void HandleWeaponEquipped(WeaponBase weapon)
        {
            WatchWeapon(weapon);

            WeaponData data = weapon != null ? weapon.Data : null;
            int type = data == null ? (weapon != null ? PistolWeapon : NoWeapon)
                : data.HoldType == WeaponHoldType.Rifle ? RifleWeapon : PistolWeapon;
            animator.SetInteger(WeaponTypeId, type);
            animator.ResetTrigger(FireId);
            animator.ResetTrigger(ReloadId);
            // Switching guns cancels a reload (WeaponBase.OnDisable) without a ReloadFinished event.
            animator.SetBool(IsReloadingId, weapon != null && weapon.IsReloading);
            animator.SetTrigger(EquipId);
        }

        // Listen to the held gun's shots and reloads (and stop listening to the previous gun).
        private void WatchWeapon(WeaponBase weapon)
        {
            if (currentWeapon != null)
            {
                currentWeapon.Fired -= HandleFired;
                currentWeapon.ReloadStarted -= HandleReloadStarted;
                currentWeapon.ReloadFinished -= HandleReloadFinished;
            }

            currentWeapon = weapon;
            if (currentWeapon != null)
            {
                currentWeapon.Fired += HandleFired;
                currentWeapon.ReloadStarted += HandleReloadStarted;
                currentWeapon.ReloadFinished += HandleReloadFinished;
            }
        }

        private void HandleFired()
        {
            animator.SetTrigger(FireId);
        }

        private void HandleReloadStarted()
        {
            WeaponData data = currentWeapon != null ? currentWeapon.Data : null;
            bool rifle = data != null && data.HoldType == WeaponHoldType.Rifle;
            float clipLength = rifle ? rifleReloadLength : pistolReloadLength;
            float reloadTime = data != null ? Mathf.Max(MinReloadTime, data.ReloadTime) : clipLength;

            animator.SetFloat(ReloadSpeedId, clipLength > 0f ? clipLength / reloadTime : 1f);
            animator.SetBool(IsReloadingId, true);
            animator.ResetTrigger(FireId);
            animator.SetTrigger(ReloadId);
        }

        private void HandleReloadFinished()
        {
            animator.SetBool(IsReloadingId, false);
            animator.ResetTrigger(ReloadId);
        }
    }
}
