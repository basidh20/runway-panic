using RunwayPanic.Data;
using RunwayPanic.Weapons;
using UnityEngine;

namespace RunwayPanic.Player
{
    /// <summary>
    /// Finishes the armed pose after the Animator, every frame:
    /// <list type="number">
    /// <item><b>Aim:</b> shots go to the centre of the screen, but the body faces where the player walks. The spine
    /// and chest twist (up to <see cref="maxAimAngle"/>) so the gun barrel points at what the camera aims at.</item>
    /// <item><b>Support hand:</b> two-bone IK puts the left hand on the gun actually held — under the grip for a
    /// pistol, on the handguard for a rifle — so it fits every gun model.</item>
    /// </list>
    /// Active only while the upper-body layer plays an armed state (tag "Armed"), not while drawing the gun. A hit
    /// flinch (tag "Hit") briefly loosens the aim so the hit knocks the gun. While a gun is held it also tells
    /// <see cref="PlayerController"/> where the aim is, so the body turns to keep it within the torso's reach.
    /// </summary>
    [RequireComponent(typeof(PlayerAnimator))]
    public sealed class PlayerAimIK : MonoBehaviour
    {
        private const string ArmedTag = "Armed";
        private const string HitTag = "Hit";
        private const int UpperBodyLayer = 1;
        private const int ReactionsLayer = 2;

        [Tooltip("Most the torso may twist (degrees) to point the gun at the aim point.")]
        [SerializeField, Range(0f, 120f)] private float maxAimAngle = 70f;
        [Tooltip("Distance of the aim point along the camera's view, in metres.")]
        [SerializeField, Min(1f)] private float aimDistance = 40f;
        [Tooltip("How fast aiming and the support hand blend in and out (per second).")]
        [SerializeField, Min(0.1f)] private float blendSpeed = 8f;
        [Tooltip("How much aim is lost at the peak of a hit flinch (0 = none, 1 = the gun follows the flinch fully).")]
        [SerializeField, Range(0f, 1f)] private float hitAimLoss = 0.7f;
        [Tooltip("Seconds after a shot that the body keeps squared up to the aim (see PlayerController's limits).")]
        [SerializeField, Min(0f)] private float shootingFacingTime = 0.6f;
        [Tooltip("Left-hand point on a pistol, in the gun's local space (under and beside the grip).")]
        [SerializeField] private Vector3 pistolSupport = new Vector3(-0.03f, -0.03f, 0f);
        [Tooltip("Left-hand point on a rifle as a fraction of the way from the grip to the muzzle.")]
        [SerializeField, Range(0f, 1f)] private float rifleSupportAlongBarrel = 0.5f;
        [Tooltip("Furthest along the barrel the left hand reaches, in metres (long guns are held nearer the body).")]
        [SerializeField, Min(0f)] private float rifleSupportMaxDistance = 0.33f;
        [Tooltip("How far below the barrel the left hand holds a rifle, in metres.")]
        [SerializeField, Min(0f)] private float rifleSupportBelowBarrel = 0.05f;

        private PlayerAnimator playerAnimator;
        private PlayerWeaponHolder weaponHolder;
        private PlayerController playerController;
        private PlayerShooting shooting;
        private Animator animator;
        private Transform spine, chest, leftUpperArm, leftLowerArm, leftHand;
        private float weight;
        private float lastShotTime = float.NegativeInfinity;

        private void Awake()
        {
            playerAnimator = GetComponent<PlayerAnimator>();
            weaponHolder = GetComponent<PlayerWeaponHolder>();
            playerController = GetComponent<PlayerController>();
            shooting = GetComponent<PlayerShooting>();
        }

        private void OnEnable()
        {
            if (shooting != null)
                shooting.ShotFired += HandleShotFired;
        }

        private void OnDisable()
        {
            if (shooting != null)
                shooting.ShotFired -= HandleShotFired;
            if (playerController != null)
                playerController.ClearAim();
        }

        private void HandleShotFired(Vector3 origin, Vector3 end, RunwayPanic.Core.IDamageable target)
        {
            lastShotTime = Time.time;
        }

        private void Start()
        {
            animator = playerAnimator.Animator;
            if (animator == null || !animator.isHuman)
            {
                enabled = false;
                return;
            }

            spine = animator.GetBoneTransform(HumanBodyBones.Spine);
            chest = animator.GetBoneTransform(HumanBodyBones.UpperChest) ?? animator.GetBoneTransform(HumanBodyBones.Chest);
            leftUpperArm = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            leftLowerArm = animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
            leftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
        }

        private void LateUpdate()
        {
            Transform gun = weaponHolder != null && weaponHolder.CurrentWeaponObject != null &&
                            weaponHolder.CurrentWeaponObject.activeInHierarchy
                ? weaponHolder.CurrentWeaponObject.transform
                : null;

            UpdateFacing(gun != null);

            bool armed = gun != null && animator.layerCount > UpperBodyLayer &&
                         animator.GetCurrentAnimatorStateInfo(UpperBodyLayer).IsTag(ArmedTag);
            float target = armed ? animator.GetLayerWeight(UpperBodyLayer) : 0f;
            weight = Mathf.MoveTowards(weight, target, blendSpeed * Time.deltaTime);
            if (weight <= 0f || gun == null)
                return;

            AimTorso(gun, weight * (1f - HitAimLoss()));
            PlaceSupportHand(gun);
        }

        // Holding a gun: the body turns to keep the camera's aim within reach of the torso twist (closer right after
        // a shot), so the gun can always point where the shots go.
        private void UpdateFacing(bool holdingGun)
        {
            if (playerController == null)
                return;

            Camera cam = Camera.main;
            if (!holdingGun || cam == null)
            {
                playerController.ClearAim();
                return;
            }

            playerController.SetAim(cam.transform.forward, Time.time - lastShotTime < shootingFacingTime);
        }

        // While the hit flinch plays, aim correction dips and recovers (a sine over the clip), so the hit knocks
        // the gun off target for a moment instead of being cancelled out by the aim.
        private float HitAimLoss()
        {
            if (animator.layerCount <= ReactionsLayer)
                return 0f;
            AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(ReactionsLayer);
            return state.IsTag(HitTag) ? hitAimLoss * Mathf.Sin(Mathf.PI * Mathf.Clamp01(state.normalizedTime)) : 0f;
        }

        // Half the turn on the spine, the rest (re-measured) on the chest, so the barrel ends up on the aim line.
        private void AimTorso(Transform gun, float amount)
        {
            Camera cam = Camera.main;
            if (cam == null || spine == null || chest == null || amount <= 0f)
                return;

            Vector3 aimPoint = cam.transform.position + cam.transform.forward * aimDistance;

            spine.rotation = Quaternion.Slerp(Quaternion.identity, AimRotation(gun, aimPoint), 0.5f * amount) *
                             spine.rotation;
            chest.rotation = Quaternion.Slerp(Quaternion.identity, AimRotation(gun, aimPoint), amount) * chest.rotation;
        }

        private Quaternion AimRotation(Transform gun, Vector3 aimPoint)
        {
            Vector3 toAim = aimPoint - gun.position;
            if (toAim.sqrMagnitude < 0.0001f)
                return Quaternion.identity;
            Quaternion full = Quaternion.FromToRotation(gun.forward, toAim);
            return Quaternion.RotateTowards(Quaternion.identity, full, maxAimAngle);
        }

        private void PlaceSupportHand(Transform gun)
        {
            if (leftUpperArm == null || leftLowerArm == null || leftHand == null)
                return;

            WeaponBase weapon = weaponHolder.CurrentWeapon;
            bool rifle = weapon != null && weapon.Data != null && weapon.Data.HoldType == WeaponHoldType.Rifle;

            Vector3 support;
            if (rifle)
            {
                Vector3 muzzle = gun.InverseTransformPoint(weapon.Muzzle.position);
                support = gun.TransformPoint(new Vector3(0f, muzzle.y - rifleSupportBelowBarrel,
                    Mathf.Min(muzzle.z * rifleSupportAlongBarrel, rifleSupportMaxDistance)));
            }
            else
            {
                support = gun.TransformPoint(pistolSupport);
            }

            // Elbow down and out to the left of the body.
            Vector3 pole = leftUpperArm.position + Vector3.down * 0.5f - transform.right * 0.5f;
            TwoBoneIK.Solve(leftUpperArm, leftLowerArm, leftHand, support, pole, weight);
        }
    }
}
