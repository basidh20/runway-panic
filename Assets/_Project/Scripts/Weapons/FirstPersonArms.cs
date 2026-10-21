using RunwayPanic.Data;
using RunwayPanic.Player;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace RunwayPanic.Weapons
{
    /// <summary>Visible skinned hands/sleeves. Tracks the one real weapon with independent two-bone arm IK.</summary>
    [DefaultExecutionOrder(150)]
    public sealed class FirstPersonArms : MonoBehaviour
    {
        [SerializeField] private Transform rightUpper, rightLower, rightHand;
        [SerializeField] private Transform leftUpper, leftLower, leftHand;
        [SerializeField] private Transform rightShoulder, leftShoulder;
        [SerializeField] private bool suppliedRig;
        [SerializeField] private Quaternion rightGripRotation = Quaternion.identity;
        [SerializeField] private Quaternion leftGripRotation = Quaternion.identity;
        [SerializeField] private AnimationClip rightGrab, leftGrab, fireClip, handlingClip;
        [SerializeField] private AvatarMask leftFingerMask, triggerMask;
        private PlayableGraph animationGraph;
        private AnimationLayerMixerPlayable animationLayers;
        private AnimationClipPlayable rightPose, leftPose, firePose, handlingPose;
        private float firedAt = -100f;
        private PlayerWeaponHolder holder;
        private PlayerCameraRig rig;
        private WeaponBase weapon;
        private Renderer[] armRenderers;
        private float reloadStart;
        private Vector3 leftPosition, leftVelocity;
        private bool reloading;
        private GameObject spareMagazine;
        private GameObject spareRocket;
        private static readonly Quaternion GripRotation = Quaternion.Inverse(Quaternion.Euler(0f, 90f, 142f));

        public Transform RightHand => rightHand;
        public Transform LeftHand => leftHand;

        public void ConfigureSuppliedRig(Transform root, Quaternion rightGrip, Quaternion leftGrip,
            AnimationClip grabR, AnimationClip grabL, AnimationClip fire, AnimationClip handling,
            AvatarMask leftMask, AvatarMask rightMask)
        {
            foreach (var bone in root.GetComponentsInChildren<Transform>())
                switch (bone.name)
                {
                    case "upper_arm.R": rightUpper = bone; break;
                    case "forearm.R": rightLower = bone; break;
                    case "hand.R": rightHand = bone; break;
                    case "upper_arm.L": leftUpper = bone; break;
                    case "forearm.L": leftLower = bone; break;
                    case "hand.L": leftHand = bone; break;
                    case "shoulder.R": rightShoulder = bone; break;
                    case "shoulder.L": leftShoulder = bone; break;
                }
            suppliedRig = true;
            rightGripRotation = rightGrip; leftGripRotation = leftGrip;
            rightGrab = grabR; leftGrab = grabL; fireClip = fire; handlingClip = handling;
            leftFingerMask = leftMask; triggerMask = rightMask;
        }

        public void ConfigureBones(Animator animator)
        {
            rightUpper = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            rightLower = animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
            rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
            leftUpper = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            leftLower = animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
            leftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
        }

        internal static void Attach(PlayerWeaponHolder owner, PlayerCameraRig cameraRig, Transform camera)
        {
            var prefab = Resources.Load<GameObject>("FirstPerson/PSXArms");
            if (prefab == null) { Debug.LogWarning("First-person arms prefab is missing. Run Tools/S2/Build PSX First Person Arms.", owner); return; }
            var instance = Instantiate(prefab, camera, false);
            var arms = instance.GetComponent<FirstPersonArms>();
            arms.holder = owner;
            arms.rig = cameraRig;
            // Eye-relative rig origin; only extracted arm geometry is rendered.
            instance.transform.localScale = Vector3.one * 1.2f;
            instance.transform.localPosition = new Vector3(0f, -1.72f, 0.10f);
            instance.transform.localRotation = Quaternion.identity;
            arms.armRenderers = instance.GetComponentsInChildren<Renderer>();
            arms.BuildAnimationGraph();
            owner.WeaponEquipped += arms.Equipped;
            arms.Equipped(owner.CurrentWeapon);
        }

        private void Equipped(WeaponBase next)
        {
            if (weapon != null) { weapon.ReloadStarted -= ReloadStarted; weapon.ReloadFinished -= ReloadFinished; weapon.Fired -= Fired; }
            weapon = next;
            firedAt = -100f;
            reloading = false;
            leftVelocity = Vector3.zero;
            if (spareMagazine != null) spareMagazine.SetActive(false);
            if (spareRocket != null) spareRocket.SetActive(false);
            if (weapon == null) return;
            weapon.ReloadStarted += ReloadStarted;
            weapon.ReloadFinished += ReloadFinished;
            weapon.Fired += Fired;
            leftPosition = transform.parent.InverseTransformPoint(SupportPoint());
        }

        private void ReloadStarted() { reloading = true; reloadStart = Time.time; }
        private void ReloadFinished() { reloading = false; }
        private void Fired() { firedAt = Time.time; }

        private void BuildAnimationGraph()
        {
            if (!suppliedRig || rightGrab == null) return;
            var animator = GetComponent<Animator>();
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animationGraph = PlayableGraph.Create("PSX first-person hand poses");
            animationGraph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            animationLayers = AnimationLayerMixerPlayable.Create(animationGraph, 4);
            rightPose = AnimationClipPlayable.Create(animationGraph, rightGrab);
            leftPose = AnimationClipPlayable.Create(animationGraph, leftGrab);
            firePose = AnimationClipPlayable.Create(animationGraph, fireClip);
            handlingPose = AnimationClipPlayable.Create(animationGraph, handlingClip);
            animationGraph.Connect(rightPose, 0, animationLayers, 0);
            animationGraph.Connect(leftPose, 0, animationLayers, 1);
            animationGraph.Connect(firePose, 0, animationLayers, 2);
            animationGraph.Connect(handlingPose, 0, animationLayers, 3);
            animationLayers.SetInputWeight(0, 1f); animationLayers.SetInputWeight(1, 1f);
            animationLayers.SetLayerMaskFromAvatarMask(1, leftFingerMask);
            animationLayers.SetLayerMaskFromAvatarMask(2, triggerMask);
            animationLayers.SetLayerMaskFromAvatarMask(3, leftFingerMask);
            AnimationPlayableOutput.Create(animationGraph, "Supplied arm animations", animator).SetSourcePlayable(animationLayers);
            animationGraph.Play();
        }

        private void SampleHandAnimation(float progress)
        {
            if (!animationGraph.IsValid()) return;
            rightPose.SetTime(rightGrab.length * 0.55f); rightPose.SetSpeed(0f);
            leftPose.SetTime(leftGrab.length * 0.55f); leftPose.SetSpeed(0f);
            float shotTime = Mathf.Clamp(Time.time - firedAt, 0f, fireClip.length);
            firePose.SetTime(shotTime); firePose.SetSpeed(0f);
            animationLayers.SetInputWeight(2, 1f - Mathf.Clamp01(shotTime / 0.18f));
            handlingPose.SetTime(Mathf.Max(0f, progress) * handlingClip.length); handlingPose.SetSpeed(0f);
            animationLayers.SetInputWeight(3, progress >= 0f ? 0.65f : 0f);
            animationGraph.Evaluate(0f);
        }

        private Vector3 SupportPoint()
        {
            bool pistol = weapon.Data.HoldType == WeaponHoldType.Pistol && !weapon.Data.IsProjectile;
            return weapon.transform.TransformPoint(pistol ? new Vector3(-0.035f, -0.075f, -0.065f) :
                weapon.Data.IsProjectile ? new Vector3(-0.06f, -0.035f, 0.18f) :
                weapon.Data.HasScope ? new Vector3(-0.025f, 0.025f, 0.30f) : new Vector3(-0.025f, 0.005f, 0.24f));
        }

        private void LateUpdate()
        {
            if (holder == null || rig == null) return;
            bool visible = weapon != null && weapon.gameObject.activeInHierarchy && rig.IsFirstPerson && rig.ScopeBlend < 0.95f;
            foreach (var renderer in armRenderers) renderer.enabled = visible;
            if (!visible)
            {
                if (spareMagazine != null) spareMagazine.SetActive(false);
                if (spareRocket != null) spareRocket.SetActive(false);
                return;
            }
            // Paused frames must not feed a zero delta into SmoothDamp's velocity calculation.
            if (Time.deltaTime <= 0f) return;
            Transform gun = weapon.transform;
            float progress = reloading && weapon.IsReloading
                ? Mathf.Clamp01((Time.time - reloadStart) / Mathf.Max(0.01f, weapon.Data.ReloadTime)) : -1f;
            SampleHandAnimation(progress);
            // Presentation shoulders start below the frame; their lengths remain constant.
            Vector3 rightAnchor = transform.parent.TransformPoint(new Vector3(0.26f, -0.34f, 0.12f));
            bool pistol = weapon.Data.HoldType == WeaponHoldType.Pistol && !weapon.Data.IsProjectile;
            // Bringing the pistol's shoulder forward nearly onto its wrist folded the arm
            // against the eye. Keep that shoulder back; long guns still need a forward reach.
            Vector3 leftShoulderPosition = pistol
                ? Vector3.Lerp(new Vector3(-0.16f, -0.34f, 0.28f), new Vector3(-0.24f, -0.34f, 0.02f), rig.AimBlend)
                : new Vector3(-0.16f, -0.34f, Mathf.Lerp(0.28f, 0.42f, rig.AimBlend));
            Vector3 leftAnchor = transform.parent.TransformPoint(leftShoulderPosition);
            if (rightShoulder != null) rightShoulder.position += rightAnchor - rightUpper.position;
            else rightUpper.position = rightAnchor;
            if (leftShoulder != null) leftShoulder.position += leftAnchor - leftUpper.position;
            else leftUpper.position = leftAnchor;
            Quaternion rightRotation = gun.rotation * (suppliedRig ? rightGripRotation : GripRotation);
            Vector3 rightTarget = suppliedRig ? gun.TransformPoint(new Vector3(0.015f, -0.065f, -0.075f)) :
                gun.position - rightRotation * new Vector3(0f, 0.06f, 0f);
            TwoBoneIK.Solve(rightUpper, rightLower, rightHand, rightTarget,
                rightUpper.position + transform.right * 0.6f - transform.up * 0.45f);
            rightHand.rotation = rightRotation;

            Vector3 target = SupportPoint();
            if (progress >= 0f) target = ReloadHand(target, progress);
            else reloading = false;
            // Smooth in camera space so looking around never leaves the hand behind in world space.
            Vector3 localTarget = transform.parent.InverseTransformPoint(target);
            leftPosition = Vector3.SmoothDamp(leftPosition, localTarget, ref leftVelocity, 0.035f,
                Mathf.Infinity, Time.deltaTime);
            TwoBoneIK.Solve(leftUpper, leftLower, leftHand, transform.parent.TransformPoint(leftPosition),
                leftUpper.position - transform.right * 0.6f - transform.up * 0.5f);
            leftHand.rotation = gun.rotation * (suppliedRig ? leftGripRotation : Quaternion.Inverse(Quaternion.Euler(0f, -90f, -142f)));
            UpdateMagazine(progress);
        }

        private Vector3 ReloadHand(Vector3 support, float t)
        {
            bool pistol = weapon.Data.HoldType == WeaponHoldType.Pistol && !weapon.Data.IsProjectile;
            Vector3 well = weapon.transform.TransformPoint(pistol ? new Vector3(-0.025f, -0.105f, -0.02f) :
                weapon.Data.IsProjectile ? new Vector3(0f, 0.02f, 0.65f) : new Vector3(-0.025f, -0.10f, 0.10f));
            Vector3 pocket = transform.parent.TransformPoint(new Vector3(-0.20f, -0.34f, 0.48f));
            if (t < 0.16f) return Vector3.Lerp(support, well, Ease(t / 0.16f));
            if (t < 0.38f) return Vector3.Lerp(well, pocket, Ease((t - 0.16f) / 0.22f));
            if (t < 0.55f) return pocket;
            if (t < 0.74f) return Vector3.Lerp(pocket, well, Ease((t - 0.55f) / 0.19f));
            if (t < 0.82f) return well;
            Vector3 action = weapon.transform.TransformPoint(new Vector3(-0.035f, 0.09f, 0.04f));
            return t < 0.90f ? Vector3.Lerp(well, action, Ease((t - 0.82f) / 0.08f)) :
                Vector3.Lerp(action, support, Ease((t - 0.90f) / 0.10f));
        }

        private static float Ease(float t) => Mathf.SmoothStep(0f, 1f, t);

        private void UpdateMagazine(float progress)
        {
            bool show = progress > 0.18f && progress < 0.73f && !weapon.Data.IsProjectile;
            if (show && spareMagazine == null)
            {
                spareMagazine = GameObject.CreatePrimitive(PrimitiveType.Cube);
                spareMagazine.name = "FP_ReloadMagazine";
                Destroy(spareMagazine.GetComponent<Collider>());
                spareMagazine.transform.SetParent(leftHand, false);
                var renderer = spareMagazine.GetComponent<Renderer>();
                renderer.sharedMaterial = Resources.Load<Material>("FirstPerson/M_FP_Magazine");
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            if (spareMagazine != null)
            {
                spareMagazine.SetActive(show);
                spareMagazine.transform.SetPositionAndRotation(leftHand.position + weapon.transform.up * 0.015f,
                    weapon.transform.rotation);
                spareMagazine.transform.localScale = weapon.Data.HoldType == WeaponHoldType.Pistol
                    ? new Vector3(0.025f, 0.10f, 0.035f) : new Vector3(0.035f, 0.14f, 0.07f);
            }
            bool loadingRocket = progress > 0.38f && progress < 0.79f && weapon.Data.IsProjectile;
            if (loadingRocket && spareRocket == null)
            {
                foreach (var mesh in weapon.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (mesh.name != "RPG_Rocket") continue;
                    spareRocket = new GameObject("FP_ReloadRocket");
                    spareRocket.transform.SetParent(transform, false);
                    spareRocket.transform.localScale = Vector3.one / transform.lossyScale.x;
                    var model = new GameObject("RoundMesh");
                    model.transform.SetParent(spareRocket.transform, false);
                    model.AddComponent<MeshFilter>().sharedMesh = mesh.sharedMesh;
                    model.transform.localPosition = -mesh.sharedMesh.bounds.center;
                    var renderer = model.AddComponent<MeshRenderer>();
                    renderer.sharedMaterials = mesh.GetComponent<MeshRenderer>().sharedMaterials;
                    renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    break;
                }
            }
            if (spareRocket != null)
            {
                spareRocket.SetActive(loadingRocket);
                spareRocket.transform.SetPositionAndRotation(leftHand.position + weapon.transform.forward * 0.12f,
                    weapon.transform.rotation);
            }
        }

        private void OnDestroy()
        {
            if (holder != null) holder.WeaponEquipped -= Equipped;
            if (weapon != null) { weapon.ReloadStarted -= ReloadStarted; weapon.ReloadFinished -= ReloadFinished; weapon.Fired -= Fired; }
            if (animationGraph.IsValid()) animationGraph.Destroy();
        }
    }
}
