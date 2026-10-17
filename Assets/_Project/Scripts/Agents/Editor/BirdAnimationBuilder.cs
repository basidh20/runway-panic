using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Profiling;
using Object = UnityEngine.Object;

namespace RunwayPanic.Agents.EditorTools
{
    /// <summary>
    /// Tools > Agents > Build Bird Animations: generates the bird clips and the AC_Bird Animator Controller from the
    /// numbers at the top of this file, so the animation is reproducible and every value can be explained.
    ///
    /// Every pose is written as rotations about the BIRD's own axes (beak = +Z, up = +Y, right wing = +X), applied at
    /// each bone's joint. The tool reads each bone's rest rotation from the real model and converts the offset into
    /// that bone's local space, so "raise the wing 45°" means the same on every bird. The hawk's bones rest in
    /// different directions from the gull's (its wings by ~34°), so it gets its own clip set A_Bird_Hawk_* from its
    /// own rest pose, plugged into the same controller through AOC_Bird_Hawk (an Animator Override Controller).
    ///
    /// Only m_LocalRotation and the Wings_Spread / Tail_Spread blend shapes are keyed: never positions or scale, so
    /// the clips cannot stretch a bird or fight the species scale on the prefab root.
    /// Re-running the tool overwrites the clips and controllers in place (their GUIDs, and so every reference to
    /// them, stay the same). Edits made by hand in the Animation window are lost on the next run.
    /// </summary>
    public static class BirdAnimationBuilder
    {
        // ---------- Output ----------
        const string OutputFolder = "Assets/_Project/Art/Animations/Birds";
        const string ControllerPath = OutputFolder + "/AC_Bird.controller";
        const string HawkOverridePath = OutputFolder + "/AOC_Bird_Hawk.overrideController";
        const string ReportPath = "Docs/AgentController/animation_report.md";
        const string GullModelPath = "Assets/_Project/Art/Models/Birds/SK_Bird.fbx";
        const string HawkModelPath = "Assets/_Project/Art/Models/Birds/SK_Bird_Hawk.fbx";
        const string GullClipPrefix = "A_Bird_";
        const string HawkClipPrefix = "A_Bird_Hawk_";

        // ---------- Rig (names from SourceArt/HANDOFF.md, 8 Oct) ----------
        static readonly string[] BoneNames = { "Body", "Head", "Tail", "Wing_L", "Wing_R", "Leg_L", "Leg_R" };
        const string MeshName = "Bird_Mesh";
        const string WingsSpreadShape = "Wings_Spread";
        const string TailSpreadShape = "Tail_Spread";

        // ---------- Axes ----------
        // Rotation axes in bird space. If a motion goes the wrong way in Unity, negate its axis and run the tool again.
        static readonly Vector3 FlapAxis = Vector3.forward;  // wing tips up / down (about the beak-to-tail line)
        static readonly Vector3 SweepAxis = Vector3.up;      // wing tips back / forward
        static readonly Vector3 PitchAxis = Vector3.right;   // + = head and body nose-down, tail up, legs swing back
        static readonly Vector3 RollAxis = Vector3.forward;  // body roll (death tumble)

        // ---------- Locomotion: Fly, Glide and Dive share one cycle length ----------
        // A blend tree plays its clips in step, so clips of different lengths would change the wing-beat tempo while
        // they blend. One shared length keeps the tempo constant.
        const float FrameRate = 30f;
        const float CycleSeconds = 0.5f;       // one wing beat at FlapSpeed 1 (2 beats per second)
        const int CycleKeys = 8;               // keys per cycle before key reduction
        const float LegTuck = 60f;             // legs swing back toward the tail in flight

        // A_Bird_Fly (loops)
        const float FlapTop = 45f;             // wing raise at the top of the upstroke
        const float FlapBottom = -30f;         // wing raise at the bottom of the downstroke
        const float FlapSpreadAtTop = 75f;     // Wings_Spread at the top: the wing narrows on the upstroke
        const float FlapBodyBob = 2f;          // body pitch wobble from each beat (the head cancels it)
        const float FlapTailBob = 3f;

        // A_Bird_Glide (loops)
        const float GlideDihedral = 8f;        // wings held a little above level
        const float GlideWobble = 1.5f;        // tiny movement so the glide never looks frozen

        // A_Bird_Dive (plays once and holds; also the Attack state)
        const float DiveSweep = 55f;           // wing tips swept back
        const float DiveRaise = 10f;
        const float DiveWingsSpread = 35f;     // wings narrowed
        const float DiveHeadDown = 15f;
        const float DiveLegTuck = 80f;

        // A_Bird_Hit (Reactions layer, plays once)
        const float HitSeconds = 0.3f;
        const float HitBodyJolt = -20f;        // nose snaps up
        const float HitBodyRoll = 12f;
        const float HitHeadJerk = 20f;
        const float HitWingFlick = 55f;

        // A_Bird_Death (plays once, holds the last frame)
        const float DeathSeconds = 0.8f;
        const float DeathRoll = 120f;          // rolls onto its back
        const float DeathNoseDown = 40f;
        const float DeathHeadDroop = 60f;
        const float DeathWingDroop = -60f;
        const float DeathWingsSpread = 20f;

        // ---------- Key reduction ----------
        // A key is dropped when the curve without it stays this close to the original.
        const float RotationTolerance = 0.0005f; // quaternion component, about 0.06°
        const float BlendShapeTolerance = 0.05f; // blend shape weight, 0-100

        [Flags]
        enum Channels
        {
            None = 0,
            Body = 1,
            Head = 2,
            Tail = 4,
            Wings = 8,
            Legs = 16,
            WingsSpread = 32,
            TailSpread = 64,
            Locomotion = Body | Head | Tail | Wings | Legs | WingsSpread
        }

        /// <summary>Offsets from the rest pose, in degrees about the bird axes (blend shapes: weight 0-100).</summary>
        struct Pose
        {
            public float BodyPitch, BodyRoll, HeadPitch, TailPitch, WingRaise, WingSweep, LegTuck, WingsSpread, TailSpread;
        }

        sealed class ClipSpec
        {
            public string Name;
            public bool Loop;
            public Channels Channels;
            public float[] Times;
            public Pose[] Poses;
        }

        struct BoneRest
        {
            public string Path;             // from the Animator (model root) to the bone
            public Quaternion Local;        // rest local rotation
            public Quaternion ParentInModel; // parent's rest rotation in model space
            public float Side;              // +1 right of the centre line, -1 left
        }

        sealed class Rig
        {
            public string ModelPath;
            public string MeshPath;
            public bool HasWingsSpread;
            public bool HasTailSpread;
            public readonly Dictionary<string, BoneRest> Bones = new Dictionary<string, BoneRest>();
        }

        struct ClipStats
        {
            public string Name;
            public float Length;
            public bool Loop;
            public int Curves;
            public int KeysBefore;
            public int KeysAfter;
            public long Memory;
        }

        [MenuItem("Tools/Agents/Build Bird Animations")]
        public static void Build()
        {
            Rig gull = ReadRig(GullModelPath);
            Rig hawk = ReadRig(HawkModelPath);
            if (gull == null || hawk == null)
                return;

            List<ClipSpec> specs = DefineClips();
            var stats = new List<ClipStats>();
            var gullClips = new Dictionary<string, AnimationClip>();
            var hawkClips = new Dictionary<string, AnimationClip>();

            foreach (ClipSpec spec in specs)
            {
                gullClips[spec.Name] = WriteClip(spec, gull, GullClipPrefix + spec.Name, stats);
                hawkClips[spec.Name] = WriteClip(spec, hawk, HawkClipPrefix + spec.Name, stats);
            }
            AssetDatabase.SaveAssets();

            AnimatorController controller = BuildController(gullClips);
            BuildHawkOverride(controller, gullClips, hawkClips);
            AssetDatabase.SaveAssets();

            for (int i = 0; i < stats.Count; i++)
            {
                ClipStats row = stats[i];
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>($"{OutputFolder}/{row.Name}.anim");
                row.Memory = clip != null ? Profiler.GetRuntimeMemorySizeLong(clip) : 0;
                stats[i] = row;
            }
            WriteReport(stats, gull, hawk);

            Debug.Log($"[Agents] Build Bird Animations: {stats.Count} clips, {ControllerPath}, {HawkOverridePath}. Report: {ReportPath}");
        }

        // ---------- Clip definitions ----------

        static List<ClipSpec> DefineClips() => new List<ClipSpec> { Fly(), Glide(), Dive(), Hit(), Death() };

        static ClipSpec Fly() => Cycle("Fly", Channels.Locomotion, phase =>
        {
            float wave = Mathf.Cos(phase * 2f * Mathf.PI); // +1 at the top of the beat, -1 at the bottom
            float up01 = (wave + 1f) * 0.5f;
            float bodyBob = FlapBodyBob * Mathf.Sin(phase * 2f * Mathf.PI);
            return new Pose
            {
                WingRaise = Mathf.Lerp(FlapBottom, FlapTop, up01),
                WingsSpread = Mathf.Lerp(100f, FlapSpreadAtTop, up01),
                BodyPitch = bodyBob,
                HeadPitch = -bodyBob, // the head cancels the body's bob, like a real bird keeping its eyes steady
                TailPitch = FlapTailBob * wave,
                LegTuck = LegTuck
            };
        });

        static ClipSpec Glide() => Cycle("Glide", Channels.Locomotion, phase =>
        {
            float wobble = GlideWobble * Mathf.Sin(phase * 2f * Mathf.PI);
            return new Pose
            {
                WingRaise = GlideDihedral + wobble,
                WingsSpread = 100f,
                BodyPitch = -0.5f * wobble,
                HeadPitch = 0.5f * wobble,
                TailPitch = wobble,
                LegTuck = LegTuck
            };
        });

        static ClipSpec Dive()
        {
            var pose = new Pose
            {
                WingRaise = DiveRaise,
                WingSweep = DiveSweep,
                WingsSpread = DiveWingsSpread,
                TailSpread = 0f, // tail closed
                HeadPitch = DiveHeadDown,
                LegTuck = DiveLegTuck
            };
            return Keys("Dive", false, Channels.Locomotion | Channels.TailSpread, (0f, pose), (CycleSeconds, pose));
        }

        // Only Body, Head and Wings: on the override Reactions layer every other bone keeps flying underneath.
        static ClipSpec Hit() => Keys("Hit", false, Channels.Body | Channels.Head | Channels.Wings,
            (0f, new Pose { WingRaise = GlideDihedral }),
            (0.06f, new Pose { BodyPitch = HitBodyJolt, BodyRoll = HitBodyRoll, HeadPitch = HitHeadJerk, WingRaise = HitWingFlick }),
            (0.15f, new Pose { BodyPitch = 5f, BodyRoll = -4f, HeadPitch = -5f, WingRaise = 20f }),
            (HitSeconds, new Pose { WingRaise = GlideDihedral }));

        static ClipSpec Death() => Keys("Death", false, Channels.Locomotion,
            (0f, new Pose { WingRaise = GlideDihedral, WingsSpread = 100f, LegTuck = LegTuck }),
            (0.15f, new Pose { BodyPitch = -10f, BodyRoll = 30f, HeadPitch = -20f, TailPitch = 20f, WingRaise = 70f, WingsSpread = 100f, LegTuck = 30f }),
            (0.4f, new Pose { BodyPitch = 20f, BodyRoll = 90f, HeadPitch = 40f, TailPitch = 10f, WingRaise = -20f, WingsSpread = 50f }),
            (DeathSeconds, new Pose
            {
                BodyPitch = DeathNoseDown, BodyRoll = DeathRoll, HeadPitch = DeathHeadDroop, TailPitch = 15f,
                WingRaise = DeathWingDroop, WingsSpread = DeathWingsSpread, LegTuck = -10f
            }));

        /// <summary>A looping clip sampled CycleKeys times from a pose function of the phase (0-1).</summary>
        static ClipSpec Cycle(string name, Channels channels, Func<float, Pose> poseAtPhase)
        {
            var times = new float[CycleKeys + 1];
            var poses = new Pose[CycleKeys + 1];
            for (int i = 0; i <= CycleKeys; i++)
            {
                float phase = (float)i / CycleKeys;
                times[i] = phase * CycleSeconds;
                poses[i] = poseAtPhase(i == CycleKeys ? 0f : phase); // the last key repeats the first: seamless loop
            }
            return new ClipSpec { Name = name, Loop = true, Channels = channels, Times = times, Poses = poses };
        }

        /// <summary>A clip from hand-placed keys.</summary>
        static ClipSpec Keys(string name, bool loop, Channels channels, params (float Time, Pose Pose)[] keys)
        {
            var spec = new ClipSpec { Name = name, Loop = loop, Channels = channels, Times = new float[keys.Length], Poses = new Pose[keys.Length] };
            for (int i = 0; i < keys.Length; i++)
            {
                spec.Times[i] = keys[i].Time;
                spec.Poses[i] = keys[i].Pose;
            }
            return spec;
        }

        // ---------- Pose -> curves ----------

        /// <summary>The offset of one bone for a pose, as a rotation in bird (model) space.</summary>
        static Quaternion ModelSpaceOffset(string bone, Pose pose, float side)
        {
            switch (bone)
            {
                case "Body":
                    return Quaternion.AngleAxis(pose.BodyRoll, RollAxis) * Quaternion.AngleAxis(pose.BodyPitch, PitchAxis);
                case "Head":
                    return Quaternion.AngleAxis(pose.HeadPitch, PitchAxis);
                case "Tail":
                    return Quaternion.AngleAxis(pose.TailPitch, PitchAxis);
                case "Wing_L":
                case "Wing_R":
                    // The side sign mirrors the motion: the same angle lifts both wing tips and sweeps both back.
                    return Quaternion.AngleAxis(side * pose.WingSweep, SweepAxis) * Quaternion.AngleAxis(side * pose.WingRaise, FlapAxis);
                case "Leg_L":
                case "Leg_R":
                    return Quaternion.AngleAxis(pose.LegTuck, PitchAxis);
                default:
                    return Quaternion.identity;
            }
        }

        static IEnumerable<string> BonesFor(Channels channels)
        {
            if ((channels & Channels.Body) != 0) yield return "Body";
            if ((channels & Channels.Head) != 0) yield return "Head";
            if ((channels & Channels.Tail) != 0) yield return "Tail";
            if ((channels & Channels.Wings) != 0) { yield return "Wing_L"; yield return "Wing_R"; }
            if ((channels & Channels.Legs) != 0) { yield return "Leg_L"; yield return "Leg_R"; }
        }

        static AnimationClip WriteClip(ClipSpec spec, Rig rig, string clipName, List<ClipStats> stats)
        {
            var bindings = new List<EditorCurveBinding>();
            var curves = new List<AnimationCurve>();
            int keysBefore = 0;
            int keysAfter = 0;
            int keyCount = spec.Times.Length;

            foreach (string bone in BonesFor(spec.Channels))
            {
                BoneRest rest = rig.Bones[bone];
                var components = new float[4][];
                for (int c = 0; c < 4; c++)
                    components[c] = new float[keyCount];

                Quaternion previous = rest.Local;
                for (int k = 0; k < keyCount; k++)
                {
                    // Offset in bird space -> the bone's local space: local' = P⁻¹ · offset · P · local,
                    // where P is the parent's rest rotation in model space.
                    Quaternion offset = ModelSpaceOffset(bone, spec.Poses[k], rest.Side);
                    Quaternion local = Quaternion.Inverse(rest.ParentInModel) * offset * rest.ParentInModel * rest.Local;

                    // q and -q are the same rotation; keep the sign that is closest to the previous key, or the
                    // curve would swing the long way round between keys.
                    if (Quaternion.Dot(previous, local) < 0f)
                        local = new Quaternion(-local.x, -local.y, -local.z, -local.w);
                    previous = local;

                    components[0][k] = local.x;
                    components[1][k] = local.y;
                    components[2][k] = local.z;
                    components[3][k] = local.w;
                }

                string[] properties = { "m_LocalRotation.x", "m_LocalRotation.y", "m_LocalRotation.z", "m_LocalRotation.w" };
                for (int c = 0; c < 4; c++)
                {
                    AnimationCurve full = MakeCurve(spec.Times, components[c], spec.Loop);
                    AnimationCurve reduced = Reduce(full, RotationTolerance);
                    keysBefore += full.length;
                    keysAfter += reduced.length;
                    bindings.Add(EditorCurveBinding.FloatCurve(rest.Path, typeof(Transform), properties[c]));
                    curves.Add(reduced);
                }
            }

            AddBlendShapeCurve(spec, rig, Channels.WingsSpread, rig.HasWingsSpread, WingsSpreadShape, p => p.WingsSpread, bindings, curves, ref keysBefore, ref keysAfter);
            AddBlendShapeCurve(spec, rig, Channels.TailSpread, rig.HasTailSpread, TailSpreadShape, p => p.TailSpread, bindings, curves, ref keysBefore, ref keysAfter);

            AnimationClip clip = SaveClip(clipName, spec.Loop, bindings, curves);
            stats.Add(new ClipStats
            {
                Name = clipName,
                Length = spec.Times[keyCount - 1],
                Loop = spec.Loop,
                Curves = curves.Count,
                KeysBefore = keysBefore,
                KeysAfter = keysAfter
            });
            return clip;
        }

        static void AddBlendShapeCurve(ClipSpec spec, Rig rig, Channels channel, bool exists, string shape, Func<Pose, float> value,
            List<EditorCurveBinding> bindings, List<AnimationCurve> curves, ref int keysBefore, ref int keysAfter)
        {
            if ((spec.Channels & channel) == 0 || !exists)
                return;

            var values = new float[spec.Times.Length];
            for (int k = 0; k < values.Length; k++)
                values[k] = value(spec.Poses[k]);

            AnimationCurve full = MakeCurve(spec.Times, values, spec.Loop);
            AnimationCurve reduced = Reduce(full, BlendShapeTolerance);
            keysBefore += full.length;
            keysAfter += reduced.length;
            bindings.Add(EditorCurveBinding.FloatCurve(rig.MeshPath, typeof(SkinnedMeshRenderer), "blendShape." + shape));
            curves.Add(reduced);
        }

        /// <summary>
        /// Keys with smooth tangents (slope from the neighbouring keys). Looping clips wrap around, so the end flows
        /// into the start; one-shot clips start and end with a flat tangent.
        /// </summary>
        static AnimationCurve MakeCurve(float[] times, float[] values, bool loop)
        {
            int n = times.Length;
            var keys = new Keyframe[n];
            for (int i = 0; i < n; i++)
            {
                float slope;
                if (n < 3)
                {
                    slope = 0f;
                }
                else if (loop && (i == 0 || i == n - 1))
                {
                    // First and last keys are the same pose: use the key before the end and the key after the start.
                    float span = (times[n - 1] - times[n - 2]) + (times[1] - times[0]);
                    slope = (values[1] - values[n - 2]) / span;
                }
                else if (i == 0 || i == n - 1)
                {
                    slope = 0f;
                }
                else
                {
                    slope = (values[i + 1] - values[i - 1]) / (times[i + 1] - times[i - 1]);
                }
                keys[i] = new Keyframe(times[i], values[i], slope, slope);
            }
            return new AnimationCurve(keys);
        }

        /// <summary>
        /// Keyframe reduction: removes every key the curve does not need, i.e. where the curve without it stays within
        /// <paramref name="tolerance"/> of the original at the key and halfway to its neighbours. A constant curve
        /// (legs held tucked) ends with 2 keys.
        /// </summary>
        static AnimationCurve Reduce(AnimationCurve original, float tolerance)
        {
            var keys = new List<Keyframe>(original.keys);
            int i = 1;
            while (i < keys.Count - 1)
            {
                var trial = new List<Keyframe>(keys);
                trial.RemoveAt(i);
                var test = new AnimationCurve(trial.ToArray());

                float before = keys[i - 1].time;
                float at = keys[i].time;
                float after = keys[i + 1].time;
                bool unneeded = Close(original, test, at, tolerance) &&
                                Close(original, test, (before + at) * 0.5f, tolerance) &&
                                Close(original, test, (at + after) * 0.5f, tolerance);
                if (unneeded)
                    keys.RemoveAt(i);
                else
                    i++;
            }
            return new AnimationCurve(keys.ToArray());
        }

        static bool Close(AnimationCurve a, AnimationCurve b, float time, float tolerance) =>
            Mathf.Abs(a.Evaluate(time) - b.Evaluate(time)) <= tolerance;

        static AnimationClip SaveClip(string name, bool loop, List<EditorCurveBinding> bindings, List<AnimationCurve> curves)
        {
            string path = $"{OutputFolder}/{name}.anim";
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            bool created = clip == null;
            if (created)
            {
                clip = new AnimationClip();
            }
            else
            {
                // Overwrite in place: the asset (and its GUID) stays, only the curves change.
                foreach (EditorCurveBinding old in AnimationUtility.GetCurveBindings(clip))
                    AnimationUtility.SetEditorCurve(clip, old, null);
            }

            clip.name = name;
            clip.frameRate = FrameRate;
            AnimationUtility.SetEditorCurves(clip, bindings.ToArray(), curves.ToArray());
            clip.EnsureQuaternionContinuity();

            AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = loop; // Fly and Glide loop, the same rule as ArtModelPostprocessor's clip names
            AnimationUtility.SetAnimationClipSettings(clip, settings);

            if (created)
                AssetDatabase.CreateAsset(clip, path);
            else
                EditorUtility.SetDirty(clip);
            return clip;
        }

        // ---------- Animator Controller ----------

        static AnimatorController BuildController(Dictionary<string, AnimationClip> clips)
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (controller == null)
                controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            ClearController(controller);

            controller.AddParameter(BirdAnimatorDriver.SpeedParameter, AnimatorControllerParameterType.Float);
            controller.AddParameter(BirdAnimatorDriver.VerticalSpeedParameter, AnimatorControllerParameterType.Float);
            controller.AddParameter(BirdAnimatorDriver.FlapSpeedParameter, AnimatorControllerParameterType.Float);
            controller.AddParameter(BirdAnimatorDriver.CycleOffsetParameter, AnimatorControllerParameterType.Float);
            controller.AddParameter(BirdAnimatorDriver.AttackTrigger, AnimatorControllerParameterType.Trigger);
            controller.AddParameter(BirdAnimatorDriver.HitTrigger, AnimatorControllerParameterType.Trigger);
            controller.AddParameter(BirdAnimatorDriver.DeathTrigger, AnimatorControllerParameterType.Trigger);

            // FlapSpeed defaults to 1 so the controller also plays correctly without the driver (e.g. in a preview).
            AnimatorControllerParameter[] parameters = controller.parameters;
            foreach (AnimatorControllerParameter parameter in parameters)
            {
                if (parameter.name == BirdAnimatorDriver.FlapSpeedParameter)
                    parameter.defaultFloat = 1f;
            }
            controller.parameters = parameters;

            controller.AddLayer("Base Layer");
            controller.AddLayer("Reactions");
            AnimatorControllerLayer[] layers = controller.layers;
            layers[1].defaultWeight = 1f;
            layers[1].blendingMode = AnimatorLayerBlendingMode.Override;
            controller.layers = layers;

            BuildBaseLayer(controller, clips);
            BuildReactionsLayer(controller.layers[1].stateMachine, clips["Hit"]);

            EditorUtility.SetDirty(controller);
            return controller;
        }

        /// <summary>Empties a controller made by an earlier run, keeping the asset (and its GUID).</summary>
        static void ClearController(AnimatorController controller)
        {
            foreach (AnimatorControllerParameter parameter in controller.parameters)
                controller.RemoveParameter(parameter);
            for (int i = controller.layers.Length - 1; i >= 0; i--)
                controller.RemoveLayer(i);

            // States, transitions and blend trees are sub-assets of the controller file: remove any left behind.
            foreach (Object subAsset in AssetDatabase.LoadAllAssetsAtPath(ControllerPath))
            {
                if (subAsset != null && subAsset != controller)
                    Object.DestroyImmediate(subAsset, true);
            }
        }

        /// <summary>
        /// Base layer: Locomotion (2D blend tree) is the default state; Attack plays the dive and returns; Death is
        /// reachable from any state and never exits.
        /// </summary>
        static void BuildBaseLayer(AnimatorController controller, Dictionary<string, AnimationClip> clips)
        {
            AnimatorStateMachine machine = controller.layers[0].stateMachine;

            AnimatorState locomotion = controller.CreateBlendTreeInController("Locomotion", out BlendTree tree, 0);
            tree.blendType = BlendTreeType.FreeformCartesian2D;
            tree.blendParameter = BirdAnimatorDriver.SpeedParameter;          // x: 0 = hovering, 1 = top speed
            tree.blendParameterY = BirdAnimatorDriver.VerticalSpeedParameter; // y: -1 = steepest dive, +1 = steepest climb
            tree.useAutomaticThresholds = false;
            // Slow or climbing: flap. Fast and level: half flap, half glide. Fast and descending: glide, then dive.
            tree.AddChild(clips["Fly"], new Vector2(0f, 0f));
            tree.AddChild(clips["Fly"], new Vector2(0f, 1f));
            tree.AddChild(clips["Glide"], new Vector2(0f, -1f));
            tree.AddChild(clips["Fly"], new Vector2(1f, 1f));
            tree.AddChild(clips["Fly"], new Vector2(1f, 0.35f));
            tree.AddChild(clips["Glide"], new Vector2(1f, -0.35f));
            tree.AddChild(clips["Dive"], new Vector2(1f, -1f));

            // FlapSpeed (per species, ±10 % per bird) scales the wing beat; CycleOffset starts each bird at a random
            // point of it. Both are set once per bird by BirdAnimatorDriver.
            locomotion.speedParameterActive = true;
            locomotion.speedParameter = BirdAnimatorDriver.FlapSpeedParameter;
            locomotion.cycleOffsetParameterActive = true;
            locomotion.cycleOffsetParameter = BirdAnimatorDriver.CycleOffsetParameter;
            machine.defaultState = locomotion;

            AnimatorState attack = machine.AddState("Attack", new Vector3(520f, 120f, 0f));
            attack.motion = clips["Dive"];

            AnimatorState death = machine.AddState("Death", new Vector3(520f, -20f, 0f));
            death.motion = clips["Death"];

            AnimatorStateTransition toAttack = locomotion.AddTransition(attack);
            toAttack.hasExitTime = false;
            toAttack.duration = 0.1f;
            toAttack.AddCondition(AnimatorConditionMode.If, 0f, BirdAnimatorDriver.AttackTrigger);

            AnimatorStateTransition fromAttack = attack.AddTransition(locomotion);
            fromAttack.hasExitTime = true;
            fromAttack.exitTime = 0.9f;
            fromAttack.duration = 0.2f;

            AnimatorStateTransition toDeath = machine.AddAnyStateTransition(death);
            toDeath.hasExitTime = false;
            toDeath.duration = 0.1f;
            toDeath.canTransitionToSelf = false;
            toDeath.AddCondition(AnimatorConditionMode.If, 0f, BirdAnimatorDriver.DeathTrigger);
        }

        /// <summary>
        /// Reactions layer (override, weight 1): an empty default state, and Hit on top of whatever the base layer
        /// plays. Write Defaults is off on both states: with it on, the empty state would write the rest pose over the
        /// flying base layer. A new hit restarts the flinch (Any State, can transition to itself).
        /// </summary>
        static void BuildReactionsLayer(AnimatorStateMachine machine, AnimationClip hitClip)
        {
            AnimatorState empty = machine.AddState("Empty", new Vector3(300f, 0f, 0f));
            empty.writeDefaultValues = false;
            machine.defaultState = empty;

            AnimatorState hit = machine.AddState("Hit", new Vector3(300f, 120f, 0f));
            hit.motion = hitClip;
            hit.writeDefaultValues = false;

            AnimatorStateTransition toHit = machine.AddAnyStateTransition(hit);
            toHit.hasExitTime = false;
            toHit.duration = 0.05f;
            toHit.canTransitionToSelf = true;
            toHit.AddCondition(AnimatorConditionMode.If, 0f, BirdAnimatorDriver.HitTrigger);

            AnimatorStateTransition back = hit.AddTransition(empty);
            back.hasExitTime = true;
            back.exitTime = 1f;
            back.duration = 0.1f;
        }

        /// <summary>The hawk runs AC_Bird with every A_Bird_* clip swapped for its own A_Bird_Hawk_* version.</summary>
        static void BuildHawkOverride(AnimatorController controller, Dictionary<string, AnimationClip> gullClips,
            Dictionary<string, AnimationClip> hawkClips)
        {
            var hawkFor = new Dictionary<AnimationClip, AnimationClip>();
            foreach (KeyValuePair<string, AnimationClip> pair in gullClips)
                hawkFor[pair.Value] = hawkClips[pair.Key];

            var overrideController = AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>(HawkOverridePath);
            bool created = overrideController == null;
            if (created)
                overrideController = new AnimatorOverrideController(controller);
            else
                overrideController.runtimeAnimatorController = controller;

            var overrides = new List<KeyValuePair<AnimationClip, AnimationClip>>(overrideController.overridesCount);
            overrideController.GetOverrides(overrides);
            for (int i = 0; i < overrides.Count; i++)
            {
                AnimationClip original = overrides[i].Key;
                hawkFor.TryGetValue(original, out AnimationClip replacement);
                overrides[i] = new KeyValuePair<AnimationClip, AnimationClip>(original, replacement);
            }
            overrideController.ApplyOverrides(overrides);

            if (created)
                AssetDatabase.CreateAsset(overrideController, HawkOverridePath);
            else
                EditorUtility.SetDirty(overrideController);
        }

        // ---------- Rig ----------

        /// <summary>
        /// Reads the rest pose of a bird model: for each bone its path from the model root, rest local rotation, its
        /// parent's rest rotation in model space and which side of the bird it is on.
        /// </summary>
        static Rig ReadRig(string modelPath)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (model == null)
            {
                Debug.LogError($"[Agents] {modelPath} not found. Run 'git lfs pull' so the real FBX is on disk.");
                return null;
            }

            GameObject instance = Object.Instantiate(model);
            instance.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                Transform root = instance.transform;
                root.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                root.localScale = Vector3.one;

                var rig = new Rig { ModelPath = modelPath };
                Transform[] all = instance.GetComponentsInChildren<Transform>(true);
                foreach (string boneName in BoneNames)
                {
                    Transform bone = Array.Find(all, t => t.name == boneName);
                    if (bone == null || bone.parent == null)
                    {
                        Debug.LogError($"[Agents] {modelPath}: bone '{boneName}' not found.");
                        return null;
                    }

                    float x = root.InverseTransformPoint(bone.position).x;
                    if (boneName.StartsWith("Wing") && Mathf.Abs(x) < 0.001f)
                        Debug.LogWarning($"[Agents] {modelPath}: {boneName} sits on the centre line; flapping direction may be wrong.");

                    rig.Bones[boneName] = new BoneRest
                    {
                        Path = AnimationUtility.CalculateTransformPath(bone, root),
                        Local = bone.localRotation,
                        ParentInModel = Quaternion.Inverse(root.rotation) * bone.parent.rotation,
                        Side = x >= 0f ? 1f : -1f
                    };
                }

                SkinnedMeshRenderer mesh = Array.Find(instance.GetComponentsInChildren<SkinnedMeshRenderer>(true), r => r.name == MeshName);
                if (mesh == null || mesh.sharedMesh == null)
                {
                    Debug.LogError($"[Agents] {modelPath}: SkinnedMeshRenderer '{MeshName}' not found.");
                    return null;
                }

                rig.MeshPath = AnimationUtility.CalculateTransformPath(mesh.transform, root);
                rig.HasWingsSpread = mesh.sharedMesh.GetBlendShapeIndex(WingsSpreadShape) >= 0;
                rig.HasTailSpread = mesh.sharedMesh.GetBlendShapeIndex(TailSpreadShape) >= 0;
                if (!rig.HasWingsSpread || !rig.HasTailSpread)
                    Debug.LogWarning($"[Agents] {modelPath}: blend shape {WingsSpreadShape} or {TailSpreadShape} missing; it is not keyed.");
                return rig;
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        // ---------- Report ----------

        static void WriteReport(List<ClipStats> stats, Rig gull, Rig hawk)
        {
            var text = new StringBuilder();
            text.AppendLine("# Bird animation report");
            text.AppendLine();
            text.AppendLine($"Written by **Tools > Agents > Build Bird Animations** on {DateTime.Now:yyyy-MM-dd HH:mm}. Every run overwrites this file.");
            text.AppendLine();
            text.AppendLine("## Clips");
            text.AppendLine();
            text.AppendLine("| Clip | Length (s) | Loops | Curves | Keys generated | Keys after reduction | Editor memory |");
            text.AppendLine("|---|---:|---|---:|---:|---:|---:|");
            int before = 0, after = 0;
            long memory = 0;
            foreach (ClipStats row in stats)
            {
                text.AppendLine(string.Format(CultureInfo.InvariantCulture, "| `{0}` | {1:0.00} | {2} | {3} | {4} | {5} | {6:0.0} KB |",
                    row.Name, row.Length, row.Loop ? "yes" : "no", row.Curves, row.KeysBefore, row.KeysAfter, row.Memory / 1024f));
                before += row.KeysBefore;
                after += row.KeysAfter;
                memory += row.Memory;
            }
            text.AppendLine(string.Format(CultureInfo.InvariantCulture, "| **Total** | | | | **{0}** | **{1}** ({2:0}% removed) | **{3:0.0} KB** |",
                before, after, before > 0 ? 100f * (before - after) / before : 0f, memory / 1024f));
            text.AppendLine();
            text.AppendLine("Editor memory = `Profiler.GetRuntimeMemorySizeLong` in the Editor; a build stores clips more compactly.");
            text.AppendLine();
            text.AppendLine("## Rest pose, gull vs hawk");
            text.AppendLine();
            text.AppendLine("Angle between each bone's rest local rotation on `SK_Bird` and on `SK_Bird_Hawk`. A clip keys absolute local");
            text.AppendLine("rotations, so one clip set on both birds would be off by this much; that is why the hawk has its own clips.");
            text.AppendLine();
            text.AppendLine("| Bone | Difference |");
            text.AppendLine("|---|---:|");
            foreach (string bone in BoneNames)
            {
                float angle = Quaternion.Angle(gull.Bones[bone].Local, hawk.Bones[bone].Local);
                text.AppendLine(string.Format(CultureInfo.InvariantCulture, "| `{0}` | {1:0.0}° |", bone, angle));
            }

            string fullPath = Path.Combine(Directory.GetCurrentDirectory(), ReportPath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
            File.WriteAllText(fullPath, text.ToString());
        }
    }
}
