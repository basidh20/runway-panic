using System;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RunwayPanic.PlayerTools
{
    /// <summary>
    /// Tools > S2 > Generate Player Locomotion Clips: creates the officer's idle, walk and run as Humanoid
    /// <c>.anim</c> clips, procedurally, from the officer's own rig — no downloaded animations needed.
    /// <para>
    /// How it works: a hidden copy of <c>SK_Player_Officer</c> is posed frame by frame (leg swing, knee lift, arm
    /// swing, lean, breathing). Limbs are aimed with world-space directions relative to the character's own
    /// right/up/forward axes, so the result doesn't depend on how the bones were oriented in Blender (T- or A-pose).
    /// Each pose is converted to Humanoid muscle values with <see cref="HumanPoseHandler"/> and keyed into a clip,
    /// so the clips retarget like any Mixamo clip. Clips are "in place": the CharacterController moves the player.
    /// </para>
    /// <para>Re-runnable: existing clips are overwritten in place and keep their GUIDs.</para>
    /// </summary>
    public static class PlayerClipGenerator
    {
        private const string ModelPath = "Assets/_Project/Art/Models/Characters/SK_Player_Officer.fbx";
        private const string OutputFolder = "Assets/_Project/Art/Animations/Player";
        private const int FramesPerSecond = 30;

        // A curve whose values all stay within this of each other is stored as 2 keys (fingers, toes, …).
        private const float ConstantTolerance = 0.0005f;

        /// <summary>One gait: all angles in degrees, distances in metres.</summary>
        private struct Gait
        {
            public string Name;
            public float Period;      // seconds per full cycle (two steps)
            public float ThighSwing;  // forward/back swing of each thigh
            public float KneeBase;    // knee bend always present
            public float KneeLift;    // extra knee bend while the leg swings forward
            public float FootRoll;    // toes up in front, down behind
            public float ArmSwing;    // forward/back swing of each upper arm
            public float Elbow;       // elbow bend
            public float Lean;        // forward lean of the spine
            public float Twist;       // chest twist following the arms
            public float Bob;         // body rises and falls twice per cycle
            public float Breath;      // chest rise (idle)
            public float ArmSpread;   // how far the arms hang away from the body
            public int KeyStep;       // key every Nth frame (slow motions need fewer keys); 0 = every frame
        }

        private static readonly Gait Idle = new Gait
        {
            Name = "A_Player_Idle", Period = 3f, KeyStep = 5, KneeBase = 4f, Elbow = 14f, Lean = 2f,
            Bob = 0.006f, Breath = 1.5f, ArmSpread = 0.14f,
        };

        // Walk speed is 4.5 m/s (a jog), so the "walk" is a light jog.
        private static readonly Gait Walk = new Gait
        {
            Name = "A_Player_Walk", Period = 0.7f, ThighSwing = 32f, KneeBase = 12f, KneeLift = 70f, FootRoll = 15f,
            ArmSwing = 32f, Elbow = 70f, Lean = 7f, Twist = 6f, Bob = 0.035f, ArmSpread = 0.12f,
        };

        private static readonly Gait Run = new Gait
        {
            Name = "A_Player_Run", Period = 0.6f, ThighSwing = 45f, KneeBase = 18f, KneeLift = 95f, FootRoll = 20f,
            ArmSwing = 45f, Elbow = 90f, Lean = 12f, Twist = 9f, Bob = 0.05f, ArmSpread = 0.1f,
        };

        [MenuItem("Tools/S2/Generate Player Locomotion Clips")]
        public static void GenerateLocomotion()
        {
            using (var rig = Rig.Load())
            {
                if (rig == null)
                    return;

                foreach (Gait gait in new[] { Idle, Walk, Run })
                    Save(rig.Bake(gait), gait.Name);
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[Player Animator] Generated {Idle.Name}, {Walk.Name} and {Run.Name} in {OutputFolder}.");
        }

        private static void Save(AnimationClip clip, string name)
        {
            string path = $"{OutputFolder}/{name}.anim";
            var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (existing != null)
            {
                EditorUtility.CopySerialized(clip, existing);
                existing.name = name;
                EditorUtility.SetDirty(existing);
                Object.DestroyImmediate(clip);
            }
            else
            {
                clip.name = name;
                AssetDatabase.CreateAsset(clip, path);
            }
        }

        /// <summary>A hidden, poseable copy of the officer model.</summary>
        private sealed class Rig : IDisposable
        {
            private static readonly string[] RootCurves =
                { "RootT.x", "RootT.y", "RootT.z", "RootQ.x", "RootQ.y", "RootQ.z", "RootQ.w" };

            private readonly GameObject instance;
            private readonly Animator animator;
            private readonly HumanPoseHandler poseHandler;
            private readonly Transform[] transforms;
            private readonly Quaternion[] restRotations;
            private readonly Vector3[] restPositions;

            // The character's own axes, measured from the hips (independent of the export orientation).
            private readonly Vector3 right;
            private readonly Vector3 up = Vector3.up;
            private readonly Vector3 forward;

            private readonly Vector3 restThighL, restThighR, restShinL, restShinR, restFootL, restFootR;

            private Rig(GameObject instance, Animator animator)
            {
                this.instance = instance;
                this.animator = animator;
                poseHandler = new HumanPoseHandler(animator.avatar, instance.transform);

                transforms = instance.GetComponentsInChildren<Transform>(true);
                restRotations = new Quaternion[transforms.Length];
                restPositions = new Vector3[transforms.Length];
                for (int i = 0; i < transforms.Length; i++)
                {
                    restRotations[i] = transforms[i].localRotation;
                    restPositions[i] = transforms[i].localPosition;
                }

                Vector3 hips = Bone(HumanBodyBones.RightUpperLeg).position - Bone(HumanBodyBones.LeftUpperLeg).position;
                hips.y = 0f;
                right = hips.normalized;
                forward = Vector3.Cross(right, up); // rotating "up" about "right" by +θ tips it towards this

                restThighL = Dir(HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg);
                restThighR = Dir(HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg);
                restShinL = Dir(HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot);
                restShinR = Dir(HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot);
                restFootL = FootDir(HumanBodyBones.LeftFoot, HumanBodyBones.LeftToes);
                restFootR = FootDir(HumanBodyBones.RightFoot, HumanBodyBones.RightToes);
            }

            public static Rig Load()
            {
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
                if (model == null)
                {
                    Debug.LogError($"[Player Animator] Model not found at {ModelPath}.");
                    return null;
                }

                GameObject instance = Object.Instantiate(model);
                instance.hideFlags = HideFlags.HideAndDontSave;
                instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

                Animator animator = instance.GetComponentInChildren<Animator>();
                if (animator == null || animator.avatar == null || !animator.avatar.isHuman)
                {
                    Debug.LogError($"[Player Animator] {ModelPath} has no Humanoid avatar.");
                    Object.DestroyImmediate(instance);
                    return null;
                }

                return new Rig(instance, animator);
            }

            public void Dispose()
            {
                poseHandler.Dispose();
                Object.DestroyImmediate(instance);
            }

            public AnimationClip Bake(Gait gait)
            {
                int step = Mathf.Max(1, gait.KeyStep);
                int frames = Mathf.Max(2, Mathf.RoundToInt(gait.Period * FramesPerSecond / step)) * step;
                int muscleCount = HumanTrait.MuscleCount;
                var muscleCurves = new AnimationCurve[muscleCount];
                var rootCurves = new AnimationCurve[RootCurves.Length];
                for (int i = 0; i < muscleCount; i++) muscleCurves[i] = new AnimationCurve();
                for (int i = 0; i < rootCurves.Length; i++) rootCurves[i] = new AnimationCurve();

                var pose = new HumanPose();
                for (int f = 0; f <= frames; f += step)
                {
                    float time = f / (float)FramesPerSecond;
                    float phase = (f % frames) / (float)frames * Mathf.PI * 2f; // last frame = first: seamless loop

                    ApplyPose(gait, phase);
                    poseHandler.GetHumanPose(ref pose);
                    pose.bodyPosition += up * (BodyOffset(gait, phase) / Mathf.Max(0.01f, animator.humanScale));

                    for (int m = 0; m < muscleCount; m++)
                        muscleCurves[m].AddKey(time, pose.muscles[m]);

                    rootCurves[0].AddKey(time, pose.bodyPosition.x);
                    rootCurves[1].AddKey(time, pose.bodyPosition.y);
                    rootCurves[2].AddKey(time, pose.bodyPosition.z);
                    rootCurves[3].AddKey(time, pose.bodyRotation.x);
                    rootCurves[4].AddKey(time, pose.bodyRotation.y);
                    rootCurves[5].AddKey(time, pose.bodyRotation.z);
                    rootCurves[6].AddKey(time, pose.bodyRotation.w);
                }

                ResetPose();

                var clip = new AnimationClip { frameRate = FramesPerSecond };
                for (int m = 0; m < muscleCount; m++)
                    SetCurve(clip, HumanTrait.MuscleName[m], muscleCurves[m]);
                for (int i = 0; i < RootCurves.Length; i++)
                    SetCurve(clip, RootCurves[i], rootCurves[i]);

                AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
                settings.loopTime = true;
                settings.loopBlendOrientation = true;  // in place: no turning,
                settings.loopBlendPositionY = true;    // no drifting up/down,
                settings.loopBlendPositionXZ = true;   // no travelling
                settings.keepOriginalOrientation = true;
                settings.keepOriginalPositionY = true;
                settings.keepOriginalPositionXZ = true;
                AnimationUtility.SetAnimationClipSettings(clip, settings);
                return clip;
            }

            private static void SetCurve(AnimationClip clip, string attribute, AnimationCurve curve)
            {
                curve = DropIfConstant(curve);
                for (int k = 0; k < curve.length; k++)
                    curve.SmoothTangents(k, 0f);
                AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Animator), attribute), curve);
            }

            // Most muscles (fingers, toes, jaw, eyes) never move: 2 keys instead of one per frame keeps clips small.
            private static AnimationCurve DropIfConstant(AnimationCurve curve)
            {
                if (curve.length < 3)
                    return curve;

                float min = float.PositiveInfinity, max = float.NegativeInfinity;
                for (int k = 0; k < curve.length; k++)
                {
                    min = Mathf.Min(min, curve[k].value);
                    max = Mathf.Max(max, curve[k].value);
                }

                if (max - min > ConstantTolerance)
                    return curve;

                float value = curve[0].value;
                return new AnimationCurve(new Keyframe(0f, value), new Keyframe(curve[curve.length - 1].time, value));
            }

            // ---- Posing ----

            private void ApplyPose(Gait g, float phase)
            {
                ResetPose();

                float sin = Mathf.Sin(phase);
                float breath = g.Breath * sin;

                // Spine: lean forward, chest follows the arms, head stays level.
                Rotate(HumanBodyBones.Spine, right, g.Lean * 0.5f);
                Rotate(HumanBodyBones.Chest, right, g.Lean * 0.5f - breath);
                Rotate(HumanBodyBones.Chest, up, g.Twist * sin);
                Rotate(HumanBodyBones.Head, right, -g.Lean * 0.8f + breath * 0.5f);

                // Arms swing opposite to the legs: left arm forward while the right leg is forward.
                PoseArm(HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand,
                    -1f, -g.ArmSwing * sin + breath, g);
                PoseArm(HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand,
                    1f, g.ArmSwing * sin + breath, g);

                PoseLeg(HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot,
                    HumanBodyBones.LeftToes, restThighL, restShinL, restFootL, phase, g);
                PoseLeg(HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot,
                    HumanBodyBones.RightToes, restThighR, restShinR, restFootR, phase + Mathf.PI, g);
            }

            private void PoseArm(HumanBodyBones upperArm, HumanBodyBones lowerArm, HumanBodyBones hand,
                float side, float swing, Gait g)
            {
                // Hanging down, slightly away from the body, then swung forward by 'swing' degrees.
                Vector3 down = (-up + right * (side * g.ArmSpread)).normalized;
                Aim(upperArm, lowerArm, Swing(down, swing));
                Aim(lowerArm, hand, Swing(down, swing + g.Elbow));
            }

            private void PoseLeg(HumanBodyBones upperLeg, HumanBodyBones lowerLeg, HumanBodyBones foot,
                HumanBodyBones toes, Vector3 restThigh, Vector3 restShin, Vector3 restFoot, float phase, Gait g)
            {
                float swing = g.ThighSwing * Mathf.Sin(phase);                          // + = leg in front
                float knee = g.KneeBase + g.KneeLift * Mathf.Max(0f, Mathf.Cos(phase)); // bends while swinging forward
                float roll = g.FootRoll * Mathf.Sin(phase);                             // + = toes up

                Aim(upperLeg, lowerLeg, Swing(restThigh, swing));
                Aim(lowerLeg, foot, Swing(restShin, swing - knee));
                if (Bone(toes) != null)
                    Aim(foot, toes, Swing(restFoot, -roll));
            }

            // Body height: lowest when the legs are furthest apart (twice per cycle); idle just breathes.
            private static float BodyOffset(Gait g, float phase) =>
                g.ThighSwing > 0f ? g.Bob * Mathf.Cos(2f * phase) : g.Bob * Mathf.Sin(phase);

            // Rotates a direction forward (towards 'forward') by degrees, around the character's right axis.
            private Vector3 Swing(Vector3 direction, float degreesForward) =>
                Quaternion.AngleAxis(-degreesForward, right) * direction;

            // Turns 'bone' so the direction from it to 'child' becomes 'target' (children follow).
            private void Aim(HumanBodyBones bone, HumanBodyBones child, Vector3 target)
            {
                Transform b = Bone(bone), c = Bone(child);
                if (b == null || c == null)
                    return;
                Vector3 current = c.position - b.position;
                if (current.sqrMagnitude < 1e-8f)
                    return;
                b.rotation = Quaternion.FromToRotation(current, target) * b.rotation;
            }

            private void Rotate(HumanBodyBones bone, Vector3 axis, float degrees)
            {
                Transform b = Bone(bone);
                if (b != null)
                    b.rotation = Quaternion.AngleAxis(degrees, axis) * b.rotation;
            }

            private void ResetPose()
            {
                for (int i = 0; i < transforms.Length; i++)
                {
                    transforms[i].localRotation = restRotations[i];
                    transforms[i].localPosition = restPositions[i];
                }
            }

            private Vector3 Dir(HumanBodyBones from, HumanBodyBones to)
            {
                Transform a = Bone(from), b = Bone(to);
                return a != null && b != null ? (b.position - a.position).normalized : -up;
            }

            // Foot direction; without a toe bone, a flat forward foot.
            private Vector3 FootDir(HumanBodyBones foot, HumanBodyBones toes) =>
                Bone(toes) != null ? Dir(foot, toes) : forward;

            private Transform Bone(HumanBodyBones bone) => animator.GetBoneTransform(bone);
        }
    }
}
