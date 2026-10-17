using System.Collections.Generic;
using System.Linq;
using RunwayPanic.Player;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace RunwayPanic.PlayerTools
{
    /// <summary>
    /// Tools > S2 > Build Player Animator: (re)builds <c>AC_Player.controller</c> from the clips in
    /// <c>Art/Animations/Player/</c> and wires it onto <c>P_Player_Gameplay</c>. Re-runnable: the controller keeps its
    /// asset (and GUID) and is rebuilt from scratch each time, so the result never depends on hand edits.
    /// <para>
    /// Base layer: <c>Locomotion</c> (idle → walk → run by <c>Speed</c>) and the jump states
    /// <c>JumpStart → Fall → Land</c> (<c>Jump</c> trigger, <c>IsGrounded</c>, <c>VerticalSpeed</c>); walking off a
    /// ledge goes straight to <c>Fall</c>. <c>UpperBody</c> layer (arms and fingers only, <c>AM_Player_UpperBody</c>):
    /// pistol/rifle aim, fire and equip (<c>WeaponType</c>, <c>Fire</c>, <c>Equip</c>).
    /// </para>
    /// <para>
    /// Clips are found by name (<c>A_Player_Idle</c>, <c>_Walk</c>, <c>_Run</c>, <c>_JumpStart</c>, <c>_Fall</c>,
    /// <c>_Land</c>). If any is missing, <see cref="PlayerClipGenerator"/> generates them from the officer's rig first.
    /// A downloaded clip can replace a
    /// generated one: <c>ArtModelPostprocessor</c> imports every FBX in <c>Art/Animations/Player/</c> as Humanoid and
    /// names its clip after the file, so <c>A_Player_Run.fbx</c> gives the clip <c>A_Player_Run</c> (delete the
    /// generated <c>.anim</c> of the same name).
    /// </para>
    /// </summary>
    public static class PlayerAnimatorBuilder
    {
        private const string ClipsFolder = "Assets/_Project/Art/Animations/Player";
        private const string ControllerPath = ClipsFolder + "/AC_Player.controller";
        private const string PlayerPrefabPath = "Assets/_Project/Prefabs/Player/P_Player_Gameplay.prefab";

        private const string IdleClip = "A_Player_Idle";
        private const string WalkClip = "A_Player_Walk";
        private const string RunClip = "A_Player_Run";
        private const string JumpStartClip = "A_Player_JumpStart";
        private const string FallClip = "A_Player_Fall";
        private const string LandClip = "A_Player_Land";
        private const string PistolAimClip = "A_Player_PistolAim";
        private const string PistolFireClip = "A_Player_PistolFire";
        private const string PistolEquipClip = "A_Player_PistolEquip";
        private const string RifleAimClip = "A_Player_RifleAim";
        private const string RifleFireClip = "A_Player_RifleFire";
        private const string RifleEquipClip = "A_Player_RifleEquip";

        private static readonly string[] RequiredClips =
        {
            IdleClip, WalkClip, RunClip, JumpStartClip, FallClip, LandClip,
            PistolAimClip, PistolFireClip, PistolEquipClip, RifleAimClip, RifleFireClip, RifleEquipClip,
        };

        private const string MaskPath = ClipsFolder + "/AM_Player_UpperBody.mask";
        private const string UpperBodyLayer = "UpperBody";
        private const string ArmedTag = "Armed"; // PlayerAimIK aims and places the left hand only in these states
        private const string EquipTag = "Equip";

        private const string WeaponTypeParameter = "WeaponType"; // 0 none, 1 pistol, 2 rifle
        private const string EquipParameter = "Equip";
        private const string FireParameter = "Fire";
        private const int PistolType = 1;
        private const int RifleType = 2;

        private const string SpeedParameter = "Speed";
        private const string JumpParameter = "Jump";
        private const string IsGroundedParameter = "IsGrounded";
        private const string VerticalSpeedParameter = "VerticalSpeed";

        // Walking off a ledge counts as falling once the player drops faster than this (m/s). Grounded, the
        // controller holds -2 m/s, so stairs and small steps never trigger a fall.
        private const float LedgeFallSpeed = -4f;

        // Used only when the prefab's PlayerController can't be read.
        private const float FallbackWalkSpeed = 4.5f;
        private const float FallbackSprintSpeed = 7.5f;

        [MenuItem("Tools/S2/Build Player Animator")]
        public static void Build()
        {
            Dictionary<string, AnimationClip> clips = FindClips();
            if (RequiredClips.Any(name => !clips.ContainsKey(name)))
            {
                // Clips missing (none downloaded): generate them from the officer's rig.
                PlayerClipGenerator.GenerateAll();
                clips = FindClips();
            }

            string[] missing = RequiredClips.Where(name => !clips.ContainsKey(name)).ToArray();
            if (missing.Length > 0)
            {
                Debug.LogError($"[Player Animator] Missing clips in {ClipsFolder}: {string.Join(", ", missing)}.");
                return;
            }

            ReadMoveSpeeds(out float walkSpeed, out float sprintSpeed);

            AnimatorController controller = LoadOrCreateController();
            ResetController(controller);
            AnimatorState locomotion = BuildLocomotion(controller, clips[IdleClip], clips[WalkClip], clips[RunClip],
                walkSpeed, sprintSpeed);
            BuildJump(controller, locomotion, clips[JumpStartClip], clips[FallClip], clips[LandClip]);
            BuildUpperBody(controller, LoadOrCreateUpperBodyMask(), clips);
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();

            WirePlayerPrefab(controller);
            Debug.Log($"[Player Animator] Built {ControllerPath} (walk {walkSpeed} m/s, run {sprintSpeed} m/s) " +
                      $"and wired it onto {PlayerPrefabPath}.");
        }

        // ---- Clips ----

        private static Dictionary<string, AnimationClip> FindClips()
        {
            var clips = new Dictionary<string, AnimationClip>();
            foreach (string guid in AssetDatabase.FindAssets("t:AnimationClip", new[] { ClipsFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                foreach (AnimationClip clip in AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>())
                {
                    if (!clip.name.StartsWith("__preview__") && !clips.ContainsKey(clip.name))
                        clips.Add(clip.name, clip);
                }
            }

            return clips;
        }

        // Blend thresholds = the speeds the player really moves at, so "walk" plays at walk speed.
        private static void ReadMoveSpeeds(out float walkSpeed, out float sprintSpeed)
        {
            walkSpeed = FallbackWalkSpeed;
            sprintSpeed = FallbackSprintSpeed;

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            PlayerController controller = prefab != null ? prefab.GetComponent<PlayerController>() : null;
            if (controller == null)
            {
                Debug.LogWarning($"[Player Animator] No PlayerController on {PlayerPrefabPath}; using default speeds.");
                return;
            }

            var serialized = new SerializedObject(controller);
            walkSpeed = serialized.FindProperty("walkSpeed").floatValue;
            sprintSpeed = Mathf.Max(walkSpeed + 0.01f, serialized.FindProperty("sprintSpeed").floatValue);
        }

        // ---- Controller ----

        private static AnimatorController LoadOrCreateController()
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            return controller != null ? controller : AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        }

        // Empties the controller but keeps the asset, so its GUID (and every reference to it) survives a rebuild.
        private static void ResetController(AnimatorController controller)
        {
            AnimatorControllerLayer baseLayer = controller.layers[0];
            AnimatorStateMachine baseMachine = baseLayer.stateMachine;

            // Delete every state, transition, blend tree and nested machine stored in the file (the base machine
            // itself stays), so nothing from an older build is left behind as an orphan sub-asset.
            foreach (Object sub in AssetDatabase.LoadAllAssetsAtPath(ControllerPath).ToArray())
            {
                if (sub is AnimatorState || sub is AnimatorStateTransition || sub is AnimatorTransition ||
                    sub is BlendTree || (sub is AnimatorStateMachine machine && machine != baseMachine))
                    Object.DestroyImmediate(sub, true);
            }

            baseMachine.states = new ChildAnimatorState[0];
            baseMachine.stateMachines = new ChildAnimatorStateMachine[0];
            baseMachine.anyStateTransitions = new AnimatorStateTransition[0];
            baseMachine.entryTransitions = new AnimatorTransition[0];
            controller.layers = new[] { baseLayer };
            controller.parameters = new AnimatorControllerParameter[0];
        }

        private static AnimatorState BuildLocomotion(AnimatorController controller, AnimationClip idle, AnimationClip walk,
            AnimationClip run, float walkSpeed, float sprintSpeed)
        {
            controller.AddParameter(SpeedParameter, AnimatorControllerParameterType.Float);

            AnimatorState locomotion = controller.CreateBlendTreeInController("Locomotion", out BlendTree tree, 0);
            tree.name = "Locomotion";
            tree.blendType = BlendTreeType.Simple1D;
            tree.blendParameter = SpeedParameter;
            tree.useAutomaticThresholds = false;
            tree.AddChild(idle, 0f);
            tree.AddChild(walk, walkSpeed);
            tree.AddChild(run, sprintSpeed);

            controller.layers[0].stateMachine.defaultState = locomotion;
            return locomotion;
        }

        //   Locomotion ──Jump──► JumpStart ──(clip ends)──► Fall ──IsGrounded──► Land ──(clip ends)──► Locomotion
        //   Locomotion ──off a ledge (not grounded, falling fast)──► Fall
        //   Land ──Jump──► JumpStart (hop again) · Land ──moving──► Locomotion early (keep running)
        private static void BuildJump(AnimatorController controller, AnimatorState locomotion,
            AnimationClip jumpStartClip, AnimationClip fallClip, AnimationClip landClip)
        {
            controller.AddParameter(JumpParameter, AnimatorControllerParameterType.Trigger);
            controller.AddParameter(new AnimatorControllerParameter
            {
                name = IsGroundedParameter, type = AnimatorControllerParameterType.Bool, defaultBool = true,
            });
            controller.AddParameter(VerticalSpeedParameter, AnimatorControllerParameterType.Float);

            AnimatorStateMachine machine = controller.layers[0].stateMachine;
            AnimatorState jumpStart = machine.AddState("JumpStart");
            jumpStart.motion = jumpStartClip;
            AnimatorState fall = machine.AddState("Fall");
            fall.motion = fallClip;
            AnimatorState land = machine.AddState("Land");
            land.motion = landClip;

            AnimatorStateTransition t = Transition(locomotion, jumpStart, 0.05f);
            t.AddCondition(AnimatorConditionMode.If, 0f, JumpParameter);

            t = Transition(jumpStart, fall, 0.1f, exitTime: 0.95f);

            t = Transition(locomotion, fall, 0.15f);
            t.AddCondition(AnimatorConditionMode.IfNot, 0f, IsGroundedParameter);
            t.AddCondition(AnimatorConditionMode.Less, LedgeFallSpeed, VerticalSpeedParameter);

            t = Transition(fall, land, 0.05f);
            t.AddCondition(AnimatorConditionMode.If, 0f, IsGroundedParameter);

            t = Transition(land, jumpStart, 0.05f);
            t.AddCondition(AnimatorConditionMode.If, 0f, JumpParameter);

            t = Transition(land, locomotion, 0.1f, exitTime: 0.35f);
            t.AddCondition(AnimatorConditionMode.Greater, 2f, SpeedParameter);

            Transition(land, locomotion, 0.15f, exitTime: 0.8f);
        }

        // ---- Upper body (weapons) ----

        // Arms and fingers only: the legs and spine keep playing locomotion and jumps underneath.
        private static AvatarMask LoadOrCreateUpperBodyMask()
        {
            var mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(MaskPath);
            if (mask == null)
            {
                mask = new AvatarMask();
                AssetDatabase.CreateAsset(mask, MaskPath);
            }

            for (var part = AvatarMaskBodyPart.Root; part < AvatarMaskBodyPart.LastBodyPart; part++)
            {
                bool arms = part == AvatarMaskBodyPart.LeftArm || part == AvatarMaskBodyPart.RightArm ||
                            part == AvatarMaskBodyPart.LeftFingers || part == AvatarMaskBodyPart.RightFingers ||
                            part == AvatarMaskBodyPart.LeftHandIK || part == AvatarMaskBodyPart.RightHandIK;
                mask.SetHumanoidBodyPartActive(part, arms);
            }

            EditorUtility.SetDirty(mask);
            return mask;
        }

        //   Any ──Equip──► PistolEquip / RifleEquip (by WeaponType) ──(clip ends)──► PistolAim / RifleAim
        //   Any ──Fire──►  PistolFire / RifleFire (restarts on every shot) ──(clip ends)──► aim
        //   PistolAim ◄──WeaponType──► RifleAim
        // The layer's weight is set by PlayerAnimator: 0 with no gun, 1 while holding one.
        private static void BuildUpperBody(AnimatorController controller, AvatarMask mask,
            Dictionary<string, AnimationClip> clips)
        {
            controller.AddParameter(WeaponTypeParameter, AnimatorControllerParameterType.Int);
            controller.AddParameter(EquipParameter, AnimatorControllerParameterType.Trigger);
            controller.AddParameter(FireParameter, AnimatorControllerParameterType.Trigger);

            controller.AddLayer(UpperBodyLayer);
            AnimatorControllerLayer[] layers = controller.layers;
            int layerIndex = layers.Length - 1;
            layers[layerIndex].avatarMask = mask;
            layers[layerIndex].blendingMode = AnimatorLayerBlendingMode.Override;
            layers[layerIndex].defaultWeight = 0f;
            controller.layers = layers;

            AnimatorStateMachine machine = controller.layers[layerIndex].stateMachine;
            AnimatorState pistolAim = State(machine, "PistolAim", clips[PistolAimClip], ArmedTag);
            AnimatorState pistolFire = State(machine, "PistolFire", clips[PistolFireClip], ArmedTag);
            AnimatorState pistolEquip = State(machine, "PistolEquip", clips[PistolEquipClip], EquipTag);
            AnimatorState rifleAim = State(machine, "RifleAim", clips[RifleAimClip], ArmedTag);
            AnimatorState rifleFire = State(machine, "RifleFire", clips[RifleFireClip], ArmedTag);
            AnimatorState rifleEquip = State(machine, "RifleEquip", clips[RifleEquipClip], EquipTag);
            machine.defaultState = pistolAim;

            AnyTransition(machine, pistolEquip, EquipParameter, PistolType, 0.05f);
            AnyTransition(machine, rifleEquip, EquipParameter, RifleType, 0.05f);
            AnyTransition(machine, pistolFire, FireParameter, PistolType, 0.02f);
            AnyTransition(machine, rifleFire, FireParameter, RifleType, 0.02f);

            Transition(pistolEquip, pistolAim, 0.08f, exitTime: 0.95f);
            Transition(rifleEquip, rifleAim, 0.08f, exitTime: 0.95f);
            Transition(pistolFire, pistolAim, 0.05f, exitTime: 0.95f);
            Transition(rifleFire, rifleAim, 0.05f, exitTime: 0.95f);

            Transition(pistolAim, rifleAim, 0.15f).AddCondition(AnimatorConditionMode.Equals, RifleType, WeaponTypeParameter);
            Transition(rifleAim, pistolAim, 0.15f).AddCondition(AnimatorConditionMode.Equals, PistolType, WeaponTypeParameter);
        }

        private static AnimatorState State(AnimatorStateMachine machine, string name, Motion motion, string tag)
        {
            AnimatorState state = machine.AddState(name);
            state.motion = motion;
            state.tag = tag;
            return state;
        }

        // From any state on a trigger, for one weapon type; may restart itself (every shot re-kicks the recoil).
        private static void AnyTransition(AnimatorStateMachine machine, AnimatorState to, string trigger,
            int weaponType, float duration)
        {
            AnimatorStateTransition transition = machine.AddAnyStateTransition(to);
            transition.hasFixedDuration = true;
            transition.duration = duration;
            transition.hasExitTime = false;
            transition.canTransitionToSelf = true;
            transition.AddCondition(AnimatorConditionMode.If, 0f, trigger);
            transition.AddCondition(AnimatorConditionMode.Equals, weaponType, WeaponTypeParameter);
        }

        // A transition with a fixed blend time in seconds; with exitTime set, it waits for that point of the clip.
        private static AnimatorStateTransition Transition(AnimatorState from, AnimatorState to, float duration,
            float? exitTime = null)
        {
            AnimatorStateTransition transition = from.AddTransition(to);
            transition.hasFixedDuration = true;
            transition.duration = duration;
            transition.hasExitTime = exitTime.HasValue;
            transition.exitTime = exitTime ?? 0f;
            transition.interruptionSource = TransitionInterruptionSource.None;
            return transition;
        }

        // ---- Prefab ----

        // The Animator is on the officer model nested in P_Player_Gameplay; setting it here is an override on
        // P_Player_Gameplay, so the Core Developer's P_Player_Officer prefab is not modified.
        private static void WirePlayerPrefab(RuntimeAnimatorController controller)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
            try
            {
                Animator animator = root.GetComponentInChildren<Animator>(true);
                if (animator == null)
                {
                    Debug.LogError($"[Player Animator] No Animator found under {PlayerPrefabPath}. " +
                                   "Check that the officer model is imported as Humanoid.");
                    return;
                }

                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = false;

                if (!root.TryGetComponent(out PlayerAnimator playerAnimator))
                    playerAnimator = root.AddComponent<PlayerAnimator>();

                var serialized = new SerializedObject(playerAnimator);
                serialized.FindProperty("animator").objectReferenceValue = animator;
                serialized.ApplyModifiedPropertiesWithoutUndo();

                if (!root.TryGetComponent(out PlayerAimIK _))
                    root.AddComponent<PlayerAimIK>();

                PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }
    }
}
