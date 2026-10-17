using System;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RunwayPanic.PlayerTools
{
    /// <summary>
    /// Tools > S2 > Generate Player Clips: creates the officer's Humanoid <c>.anim</c> clips procedurally, from the
    /// officer's own rig — no downloaded animations needed:
    /// <list type="bullet">
    /// <item>Locomotion cycles: idle, walk (a light jog), run.</item>
    /// <item>Jump: jump start (snap push-off), fall (airborne loop), land (absorb and stand up).</item>
    /// </list>
    /// <para>
    /// How it works: a hidden copy of <c>SK_Player_Officer</c> is posed frame by frame from a <see cref="Pose"/>
    /// (thigh swing, knee bend, arm swing, lean, …). Limbs are aimed with world-space directions relative to the
    /// character's own right/up/forward axes, so the result doesn't depend on how the bones were oriented in Blender.
    /// Each pose is converted to Humanoid muscle values with <see cref="HumanPoseHandler"/> and keyed into a clip, so
    /// the clips retarget like any Mixamo clip. Cycles compute their pose from a phase; one-shot clips blend between
    /// hand-set key poses. Clips are "in place": the CharacterController moves the player.
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

        /// <summary>
        /// One full-body pose. Angles in degrees: thigh/arm + = forward, knee/elbow + = bent, foot + = toes up,
        /// lean + = forward, head + = looking down. Distances in metres.
        /// </summary>
        private struct Pose
        {
            public float ThighL, KneeL, FootL, ThighR, KneeR, FootR;
            public float ArmL, ArmR, ElbowL, ElbowR;
            public float Spread;  // how far the arms hang away from the body (0 = straight down)
            public float Lean, Twist, Head, Breath;
            public float Body;    // extra body height
            public float Plant;   // 1 = lower the body so the lowest foot stays on the ground (crouches)

            public static Pose Lerp(Pose a, Pose b, float t) => new Pose
            {
                ThighL = Mathf.Lerp(a.ThighL, b.ThighL, t), KneeL = Mathf.Lerp(a.KneeL, b.KneeL, t),
                FootL = Mathf.Lerp(a.FootL, b.FootL, t), ThighR = Mathf.Lerp(a.ThighR, b.ThighR, t),
                KneeR = Mathf.Lerp(a.KneeR, b.KneeR, t), FootR = Mathf.Lerp(a.FootR, b.FootR, t),
                ArmL = Mathf.Lerp(a.ArmL, b.ArmL, t), ArmR = Mathf.Lerp(a.ArmR, b.ArmR, t),
                ElbowL = Mathf.Lerp(a.ElbowL, b.ElbowL, t), ElbowR = Mathf.Lerp(a.ElbowR, b.ElbowR, t),
                Spread = Mathf.Lerp(a.Spread, b.Spread, t), Lean = Mathf.Lerp(a.Lean, b.Lean, t),
                Twist = Mathf.Lerp(a.Twist, b.Twist, t), Head = Mathf.Lerp(a.Head, b.Head, t),
                Breath = Mathf.Lerp(a.Breath, b.Breath, t), Body = Mathf.Lerp(a.Body, b.Body, t),
                Plant = Mathf.Lerp(a.Plant, b.Plant, t),
            };
        }

        // ---- Locomotion cycles ----

        /// <summary>A repeating gait: all angles in degrees, distances in metres.</summary>
        private struct Gait
        {
            public string Name;
            public float Period;      // seconds per full cycle (two steps)
            public int KeyStep;       // key every Nth frame (slow motions need fewer keys); 0 = every frame
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

            public Pose Sample(float phase)
            {
                float sin = Mathf.Sin(phase);
                float breath = Breath * sin;
                return new Pose
                {
                    ThighL = ThighSwing * sin,
                    KneeL = KneeBase + KneeLift * Mathf.Max(0f, Mathf.Cos(phase)), // bends while swinging forward
                    FootL = FootRoll * sin,
                    ThighR = -ThighSwing * sin,
                    KneeR = KneeBase + KneeLift * Mathf.Max(0f, -Mathf.Cos(phase)),
                    FootR = -FootRoll * sin,
                    // Arms swing opposite to the legs: left arm forward while the right leg is forward.
                    ArmL = -ArmSwing * sin + breath,
                    ArmR = ArmSwing * sin + breath,
                    ElbowL = Elbow, ElbowR = Elbow, Spread = ArmSpread,
                    Lean = Lean, Twist = Twist * sin, Breath = breath,
                    // Lowest when the legs are furthest apart (twice per cycle); idle just breathes.
                    Body = ThighSwing > 0f ? Bob * Mathf.Cos(2f * phase) : Bob * sin,
                };
            }
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

        // ---- Jump key poses ----

        private static readonly Pose Stand = new Pose
        {
            KneeL = 4f, KneeR = 4f, ElbowL = 14f, ElbowR = 14f, Spread = 0.14f, Lean = 2f, Plant = 1f,
        };

        // Loading the jump: deep crouch, arms swung back.
        private static readonly Pose Crouch = new Pose
        {
            ThighL = 40f, KneeL = 72f, ThighR = 40f, KneeR = 72f, ArmL = -40f, ArmR = -40f,
            ElbowL = 20f, ElbowR = 20f, Spread = 0.2f, Lean = 22f, Head = -10f, Plant = 1f,
        };

        // Push-off: body fully extended, toes pointed, arms thrown up.
        private static readonly Pose TakeOff = new Pose
        {
            ThighL = -8f, KneeL = 4f, FootL = -30f, ThighR = -8f, KneeR = 4f, FootR = -30f,
            ArmL = 100f, ArmR = 100f, ElbowL = 25f, ElbowR = 25f, Spread = 0.25f, Lean = -2f, Body = 0.04f,
        };

        // Airborne: legs tucked unevenly, arms out for balance (two poses the fall loop sways between).
        private static readonly Pose FallA = new Pose
        {
            ThighL = 30f, KneeL = 50f, FootL = 10f, ThighR = 12f, KneeR = 30f,
            ArmL = 70f, ArmR = 55f, ElbowL = 35f, ElbowR = 45f, Spread = 0.45f, Lean = 6f, Head = -4f,
        };

        private static readonly Pose FallB = new Pose
        {
            ThighL = 14f, KneeL = 32f, ThighR = 28f, KneeR = 48f, FootR = 10f,
            ArmL = 58f, ArmR = 72f, ElbowL = 45f, ElbowR = 35f, Spread = 0.5f, Lean = 6f, Head = -4f,
        };

        // Just before touch-down: legs reaching for the ground.
        private static readonly Pose LandBrace = new Pose
        {
            ThighL = 22f, KneeL = 28f, FootL = 12f, ThighR = 22f, KneeR = 28f, FootR = 12f,
            ArmL = 65f, ArmR = 65f, ElbowL = 35f, ElbowR = 35f, Spread = 0.45f, Lean = 4f,
        };

        // Impact: knees absorb, body folds forward.
        private static readonly Pose LandImpact = new Pose
        {
            ThighL = 48f, KneeL = 85f, ThighR = 48f, KneeR = 85f, ArmL = 25f, ArmR = 25f,
            ElbowL = 30f, ElbowR = 30f, Spread = 0.25f, Lean = 24f, Head = -12f, Plant = 1f,
        };

        /// <summary>A one-shot (or looping) clip that blends between key poses.</summary>
        private struct KeyedClip
        {
            public string Name;
            public bool Loop;
            public float[] Times;
            public Pose[] Poses;

            public float Duration => Times[Times.Length - 1];

            // Eased blend between the two surrounding keys.
            public Pose Sample(float time)
            {
                if (time <= Times[0])
                    return Poses[0];
                for (int i = 1; i < Times.Length; i++)
                {
                    if (time <= Times[i])
                    {
                        float t = Mathf.InverseLerp(Times[i - 1], Times[i], time);
                        return Pose.Lerp(Poses[i - 1], Poses[i], Mathf.SmoothStep(0f, 1f, t));
                    }
                }

                return Poses[Poses.Length - 1];
            }
        }

        // The jump launches on the key press, so there's no time for a full wind-up: a quick half-crouch snaps into
        // the push-off, then flows into the first fall pose (where the Fall loop starts).
        private static readonly KeyedClip JumpStart = new KeyedClip
        {
            Name = "A_Player_JumpStart",
            Times = new[] { 0f, 0.08f, 0.24f },
            Poses = new[] { Pose.Lerp(Stand, Crouch, 0.5f), TakeOff, FallA },
        };

        private static readonly KeyedClip Fall = new KeyedClip
        {
            Name = "A_Player_Fall", Loop = true,
            Times = new[] { 0f, 0.4f, 0.8f },
            Poses = new[] { FallA, FallB, FallA },
        };

        private static readonly KeyedClip Land = new KeyedClip
        {
            Name = "A_Player_Land",
            Times = new[] { 0f, 0.06f, 0.4f },
            Poses = new[] { LandBrace, LandImpact, Stand },
        };

        // ---- Menu ----

        [MenuItem("Tools/S2/Generate Player Clips")]
        public static void GenerateAll()
        {
            using (var rig = Rig.Load())
            {
                if (rig == null)
                    return;

                foreach (Gait gait in new[] { Idle, Walk, Run })
                {
                    Gait g = gait;
                    Save(rig.Bake(g.Period, g.KeyStep, true, t => g.Sample(t / g.Period * Mathf.PI * 2f)), g.Name);
                }

                foreach (KeyedClip keyed in new[] { JumpStart, Fall, Land })
                {
                    KeyedClip k = keyed;
                    Save(rig.Bake(k.Duration, 1, k.Loop, k.Sample), k.Name);
                }
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[Player Animator] Generated idle, walk, run, jump start, fall and land clips in {OutputFolder}.");
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
            private readonly float restFootHeight;

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
                restFootHeight = LowestFootHeight();
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

            /// <summary>
            /// Samples <paramref name="poseAt"/> (time in seconds → pose) into a Humanoid clip of
            /// <paramref name="duration"/> seconds. A looping clip's last key equals its first, so it loops seamlessly.
            /// </summary>
            public AnimationClip Bake(float duration, int keyStep, bool loop, Func<float, Pose> poseAt)
            {
                int step = Mathf.Max(1, keyStep);
                int frames = Mathf.Max(2, Mathf.RoundToInt(duration * FramesPerSecond / step)) * step;
                int muscleCount = HumanTrait.MuscleCount;
                var muscleCurves = new AnimationCurve[muscleCount];
                var rootCurves = new AnimationCurve[RootCurves.Length];
                for (int i = 0; i < muscleCount; i++) muscleCurves[i] = new AnimationCurve();
                for (int i = 0; i < rootCurves.Length; i++) rootCurves[i] = new AnimationCurve();

                var pose = new HumanPose();
                for (int f = 0; f <= frames; f += step)
                {
                    float time = f / (float)FramesPerSecond;
                    float sampleTime = (loop ? f % frames : f) / (float)FramesPerSecond;

                    Pose p = poseAt(sampleTime);
                    ApplyPose(p);
                    float body = p.Body - p.Plant * (LowestFootHeight() - restFootHeight);

                    poseHandler.GetHumanPose(ref pose);
                    pose.bodyPosition += up * (body / Mathf.Max(0.01f, animator.humanScale));

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
                settings.loopTime = loop;
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

            private void ApplyPose(Pose p)
            {
                ResetPose();

                // Spine: lean forward, chest follows the arms, head stays level (plus any extra nod).
                Rotate(HumanBodyBones.Spine, right, p.Lean * 0.5f);
                Rotate(HumanBodyBones.Chest, right, p.Lean * 0.5f - p.Breath);
                Rotate(HumanBodyBones.Chest, up, p.Twist);
                Rotate(HumanBodyBones.Head, right, -p.Lean * 0.8f + p.Breath * 0.5f + p.Head);

                PoseArm(HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand,
                    -1f, p.ArmL, p.ElbowL, p.Spread);
                PoseArm(HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand,
                    1f, p.ArmR, p.ElbowR, p.Spread);

                PoseLeg(HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot,
                    HumanBodyBones.LeftToes, restThighL, restShinL, restFootL, p.ThighL, p.KneeL, p.FootL);
                PoseLeg(HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot,
                    HumanBodyBones.RightToes, restThighR, restShinR, restFootR, p.ThighR, p.KneeR, p.FootR);
            }

            private void PoseArm(HumanBodyBones upperArm, HumanBodyBones lowerArm, HumanBodyBones hand,
                float side, float swing, float elbow, float spread)
            {
                // Hanging down, slightly away from the body, then swung forward by 'swing' degrees.
                Vector3 down = (-up + right * (side * spread)).normalized;
                Aim(upperArm, lowerArm, Swing(down, swing));
                Aim(lowerArm, hand, Swing(down, swing + elbow));
            }

            private void PoseLeg(HumanBodyBones upperLeg, HumanBodyBones lowerLeg, HumanBodyBones foot,
                HumanBodyBones toes, Vector3 restThigh, Vector3 restShin, Vector3 restFoot,
                float swing, float knee, float roll)
            {
                Aim(upperLeg, lowerLeg, Swing(restThigh, swing));
                Aim(lowerLeg, foot, Swing(restShin, swing - knee));
                if (Bone(toes) != null)
                    Aim(foot, toes, Swing(restFoot, -roll));
            }

            private float LowestFootHeight() =>
                Mathf.Min(Bone(HumanBodyBones.LeftFoot).position.y, Bone(HumanBodyBones.RightFoot).position.y);

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
