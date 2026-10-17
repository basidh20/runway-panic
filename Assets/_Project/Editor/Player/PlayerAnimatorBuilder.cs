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
    /// Clips are found by name: <c>A_Player_Idle</c>, <c>A_Player_Walk</c>, <c>A_Player_Run</c>. If any is missing,
    /// <see cref="PlayerClipGenerator"/> generates them from the officer's rig first. A downloaded clip can replace a
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

        private const string SpeedParameter = "Speed";

        // Used only when the prefab's PlayerController can't be read.
        private const float FallbackWalkSpeed = 4.5f;
        private const float FallbackSprintSpeed = 7.5f;

        [MenuItem("Tools/S2/Build Player Animator")]
        public static void Build()
        {
            Dictionary<string, AnimationClip> clips = FindClips();
            if (!clips.ContainsKey(IdleClip) || !clips.ContainsKey(WalkClip) || !clips.ContainsKey(RunClip))
            {
                // No clips yet (none downloaded): generate them from the officer's rig.
                PlayerClipGenerator.GenerateLocomotion();
                clips = FindClips();
            }

            if (!clips.TryGetValue(IdleClip, out AnimationClip idle) |
                !clips.TryGetValue(WalkClip, out AnimationClip walk) |
                !clips.TryGetValue(RunClip, out AnimationClip run))
            {
                Debug.LogError($"[Player Animator] Missing clips in {ClipsFolder}. Expected FBX files named " +
                               $"{IdleClip}, {WalkClip} and {RunClip}. Found: " +
                               (clips.Count > 0 ? string.Join(", ", clips.Keys) : "none") + ".");
                return;
            }

            ReadMoveSpeeds(out float walkSpeed, out float sprintSpeed);

            AnimatorController controller = LoadOrCreateController();
            ResetController(controller);
            BuildLocomotion(controller, idle, walk, run, walkSpeed, sprintSpeed);
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
            foreach (BlendTree tree in AssetDatabase.LoadAllAssetsAtPath(ControllerPath).OfType<BlendTree>().ToArray())
                Object.DestroyImmediate(tree, true);

            for (int i = controller.layers.Length - 1; i > 0; i--)
                controller.RemoveLayer(i);

            AnimatorStateMachine baseMachine = controller.layers[0].stateMachine;
            foreach (ChildAnimatorState child in baseMachine.states)
                baseMachine.RemoveState(child.state);
            foreach (ChildAnimatorStateMachine child in baseMachine.stateMachines)
                baseMachine.RemoveStateMachine(child.stateMachine);
            foreach (AnimatorStateTransition transition in baseMachine.anyStateTransitions)
                baseMachine.RemoveAnyStateTransition(transition);

            controller.parameters = new AnimatorControllerParameter[0];
        }

        private static void BuildLocomotion(AnimatorController controller, AnimationClip idle, AnimationClip walk,
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

                PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }
    }
}
