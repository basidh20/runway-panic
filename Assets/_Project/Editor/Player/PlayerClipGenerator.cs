using System;
using RunwayPanic.Player;
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
    /// <item>Weapons (upper body): pistol and rifle aim, fire, equip and reload, holding the real gun the way
    /// <c>PlayerWeaponHolder</c> mounts it.</item>
    /// <item>Reactions: hit flinch (played additively), death (fall onto the back), and a still stand.</item>
    /// <item>Actions (gun lowered, left hand busy): use medkit, use shield, interact, push — pistol and rifle.</item>
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
        private const string PlayerPrefabPath = "Assets/_Project/Prefabs/Player/P_Player_Gameplay.prefab";

        // Where the left hand holds a gun, in the gun's local space (same rule as PlayerAimIK's defaults).
        // Rifle: half way to a typical rifle muzzle (0, 0.10, 0.71), just under the barrel.
        private static readonly Vector3 PistolSupport = new Vector3(-0.03f, -0.03f, 0f);
        private static readonly Vector3 RifleSupport = new Vector3(0f, 0.05f, 0.355f);

        // Where a magazine goes in, in the gun's local space: the bottom of a pistol grip; under a rifle's receiver.
        private static readonly Vector3 PistolMagWell = new Vector3(0f, -0.10f, -0.02f);
        private static readonly Vector3 RifleMagWell = new Vector3(0f, -0.10f, 0.10f);

        // Finger curl around the grip ("Stretched" muscles: -1 = fully curled). The index finger stays on the trigger.
        private const float FingerCurl = -0.75f;
        private const float IndexCurl = -0.35f;
        private const float ThumbCurl = -0.3f;
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
            public float Body;      // extra body height
            public float Plant;    // 1 = lower the body so the lowest foot stays on the ground (crouches)
            public float BodyPitch; // whole body tipped backwards around the hips, degrees (90 = lying on the back)
            public float GaitStride, GaitLift, GaitPhase;

            // Holding a gun (upper-body clips): the arms are placed with two-bone IK instead of swing angles.
            public float Armed;    // 0 = arms from the angles above, 1 = right hand at 'Hand' holding the gun
            public Vector3 Hand;   // right hand relative to the right shoulder: x right, y up, z forward (m)
            public float GunPitch; // barrel angle, + = muzzle up
            public float GunYaw;   // barrel angle, + = muzzle to the right
            public float MagBlend; // left hand: 0 = grip / handguard, 1 = magazine well (reloading)
            public float Support;  // 0 = left arm from the angles above, 1 = left hand on the gun
            public float Grip;     // finger curl around the gun, 0..1
            public bool Rifle;     // left hand on a rifle's handguard instead of under a pistol grip
            public float LeftReach;   // 0 = left arm as above, 1 = left hand at 'LeftHand' (medkit, door, push…)
            public Vector3 LeftHand;  // left hand relative to the left shoulder: x right, y up, z forward (m)

            public static Pose Lerp(Pose a, Pose b, float t) => new Pose
            {
                Rifle = a.Rifle || b.Rifle,
                LeftReach = Mathf.Lerp(a.LeftReach, b.LeftReach, t), LeftHand = Vector3.Lerp(a.LeftHand, b.LeftHand, t),
                Armed = Mathf.Lerp(a.Armed, b.Armed, t), Hand = Vector3.Lerp(a.Hand, b.Hand, t),
                GunPitch = Mathf.Lerp(a.GunPitch, b.GunPitch, t), Support = Mathf.Lerp(a.Support, b.Support, t),
                GunYaw = Mathf.Lerp(a.GunYaw, b.GunYaw, t), MagBlend = Mathf.Lerp(a.MagBlend, b.MagBlend, t),
                Grip = Mathf.Lerp(a.Grip, b.Grip, t),
                ThighL = Mathf.Lerp(a.ThighL, b.ThighL, t), KneeL = Mathf.Lerp(a.KneeL, b.KneeL, t),
                FootL = Mathf.Lerp(a.FootL, b.FootL, t), ThighR = Mathf.Lerp(a.ThighR, b.ThighR, t),
                KneeR = Mathf.Lerp(a.KneeR, b.KneeR, t), FootR = Mathf.Lerp(a.FootR, b.FootR, t),
                ArmL = Mathf.Lerp(a.ArmL, b.ArmL, t), ArmR = Mathf.Lerp(a.ArmR, b.ArmR, t),
                ElbowL = Mathf.Lerp(a.ElbowL, b.ElbowL, t), ElbowR = Mathf.Lerp(a.ElbowR, b.ElbowR, t),
                Spread = Mathf.Lerp(a.Spread, b.Spread, t), Lean = Mathf.Lerp(a.Lean, b.Lean, t),
                Twist = Mathf.Lerp(a.Twist, b.Twist, t), Head = Mathf.Lerp(a.Head, b.Head, t),
                Breath = Mathf.Lerp(a.Breath, b.Breath, t), Body = Mathf.Lerp(a.Body, b.Body, t),
                Plant = Mathf.Lerp(a.Plant, b.Plant, t), BodyPitch = Mathf.Lerp(a.BodyPitch, b.BodyPitch, t),
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
            public float Plant;       // 1 = keep the lower foot on the ground (the hips dip as the legs spread)
            public float Stride, Lift;

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
                    Body = (ThighSwing > 0f ? Bob * Mathf.Cos(2f * phase) : Bob * sin) - Stride * 0.25f,
                    Plant = Plant,
                    GaitStride = Stride, GaitLift = Lift, GaitPhase = phase,
                };
            }
        }

        private static readonly Gait Idle = new Gait
        {
            Name = "A_Player_Idle", Period = 3f, KeyStep = 5, KneeBase = 4f, Elbow = 14f, Lean = 2f,
            Bob = 0.006f, Breath = 1.5f, ArmSpread = 0.14f,
        };

        // Walk speed is 4.5 m/s (a jog), so the "walk" is a light jog. Strides are as long as the officer's short legs
        // allow; PlayerAnimatorBuilder then plays each clip faster to match the real move speed (no foot sliding).
        private static readonly Gait Walk = new Gait
        {
            Name = "A_Player_Walk", Period = 0.7f, ThighSwing = 42f, KneeBase = 12f, KneeLift = 75f, FootRoll = 18f,
            ArmSwing = 24f, Elbow = 45f, Lean = 5f, Twist = 3f, ArmSpread = 0.12f,
            Stride = 0.25f, Lift = 0.10f, Bob = 0.008f,
        };

        private static readonly Gait Run = new Gait
        {
            Name = "A_Player_Run", Period = 0.6f, ThighSwing = 58f, KneeBase = 18f, KneeLift = 100f, FootRoll = 22f,
            ArmSwing = 36f, Elbow = 75f, Lean = 9f, Twist = 4f, ArmSpread = 0.1f,
            Stride = 0.34f, Lift = 0.18f, Bob = 0.015f,
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

        // ---- Weapon poses (upper body: only the arms and fingers are used, through an avatar mask) ----

        // Two-handed handgun grip, arms forward at chest height.
        private static readonly Pose PistolAim = new Pose
        {
            KneeL = 4f, KneeR = 4f, ElbowL = 14f, Spread = 0.14f, Lean = 2f,
            Armed = 1f, Hand = new Vector3(-0.13f, -0.06f, 0.40f), Support = 1f, Grip = 1f,
        };

        // Gun at the hip, muzzle down: where a draw starts.
        private static readonly Pose PistolLow = new Pose
        {
            KneeL = 4f, KneeR = 4f, ElbowL = 14f, Spread = 0.14f, Lean = 2f,
            Armed = 1f, Hand = new Vector3(0.06f, -0.46f, 0.08f), GunPitch = -75f, Grip = 1f,
        };

        // Rifle at the shoulder. The chest is turned 25° (left shoulder forward) while the arms are posed; only the
        // arms are kept by the mask, so at runtime PlayerAimIK turns the chest back onto the aim line — which gives
        // the bladed rifle stance and lets the officer's short left arm reach the handguard.
        private static readonly Pose RifleAim = new Pose
        {
            KneeL = 4f, KneeR = 4f, ElbowL = 14f, Spread = 0.14f, Lean = 2f, Twist = 25f,
            Armed = 1f, Hand = new Vector3(-0.06f, -0.20f, 0.20f), Support = 1f, Grip = 1f, Rifle = true,
        };

        // Low ready: muzzle angled at the ground in front.
        private static readonly Pose RifleLow = new Pose
        {
            KneeL = 4f, KneeR = 4f, ElbowL = 14f, Spread = 0.14f, Lean = 2f, Twist = 25f,
            Armed = 1f, Hand = new Vector3(0f, -0.34f, 0.12f), GunPitch = -45f, Support = 0.6f, Grip = 1f, Rifle = true,
        };

        private static Pose Offset(Pose pose, Vector3 hand, float pitch)
        {
            pose.Hand += hand;
            pose.GunPitch += pitch;
            return pose;
        }

        // Aim loops: a slight breathing drift so a held gun never looks frozen.
        private static readonly KeyedClip PistolAimClip = new KeyedClip
        {
            Name = "A_Player_PistolAim", Loop = true,
            Times = new[] { 0f, 0.8f, 1.6f },
            Poses = new[] { PistolAim, Offset(PistolAim, new Vector3(0f, 0.006f, 0f), 0.6f), PistolAim },
        };

        private static readonly KeyedClip RifleAimClip = new KeyedClip
        {
            Name = "A_Player_RifleAim", Loop = true,
            Times = new[] { 0f, 0.8f, 1.6f },
            Poses = new[] { RifleAim, Offset(RifleAim, new Vector3(0f, 0.005f, 0f), 0.4f), RifleAim },
        };

        // Fire: a sharp kick back and up, then settle. Short, so fast-firing guns can restart it every shot.
        private static readonly KeyedClip PistolFireClip = new KeyedClip
        {
            Name = "A_Player_PistolFire",
            Times = new[] { 0f, 0.04f, 0.18f },
            Poses = new[] { PistolAim, Offset(PistolAim, new Vector3(0f, 0.03f, -0.05f), 12f), PistolAim },
        };

        private static readonly KeyedClip RifleFireClip = new KeyedClip
        {
            Name = "A_Player_RifleFire",
            Times = new[] { 0f, 0.03f, 0.12f },
            Poses = new[] { RifleAim, Offset(RifleAim, new Vector3(0f, 0.01f, -0.035f), 4f), RifleAim },
        };

        // Equip: draw from the hip / low ready up to the aim.
        private static readonly KeyedClip PistolEquipClip = new KeyedClip
        {
            Name = "A_Player_PistolEquip",
            Times = new[] { 0f, 0.3f },
            Poses = new[] { PistolLow, PistolAim },
        };

        private static readonly KeyedClip RifleEquipClip = new KeyedClip
        {
            Name = "A_Player_RifleEquip",
            Times = new[] { 0f, 0.35f },
            Poses = new[] { RifleLow, RifleAim },
        };

        // Reload: gun tilted in and up, left hand to the belt pouch, up to the magazine well, seat the magazine,
        // back to the grip. Played at a speed that makes it last exactly the gun's reload time (PlayerAnimator).
        private static Pose ReloadTilt(Pose aim, Vector3 hand, float pitch, float yaw)
        {
            aim.Hand = hand;
            aim.GunPitch = pitch;
            aim.GunYaw = yaw;
            aim.MagBlend = 1f;
            return aim;
        }

        private static Pose LeftAtPouch(Pose pose)
        {
            pose.Support = 0f;
            pose.ArmL = -15f;
            pose.ElbowL = 70f;
            return pose;
        }

        private static Pose LeftAt(Pose pose, float support, float magBlend)
        {
            pose.Support = support;
            pose.MagBlend = magBlend;
            return pose;
        }

        private static readonly Pose PistolTilt =
            ReloadTilt(PistolAim, new Vector3(-0.10f, -0.12f, 0.30f), 25f, -15f);

        private static readonly Pose RifleTilt =
            ReloadTilt(RifleAim, new Vector3(-0.04f, -0.26f, 0.16f), 15f, -10f);

        private static readonly KeyedClip PistolReloadClip = new KeyedClip
        {
            Name = "A_Player_PistolReload",
            Times = new[] { 0f, 0.15f, 0.40f, 0.70f, 0.85f, 1.00f, 1.20f },
            Poses = new[]
            {
                PistolAim,
                LeftAt(PistolTilt, 0.3f, 1f),                    // gun comes in, left hand lets go
                LeftAtPouch(PistolTilt),                         // fresh magazine from the belt
                LeftAt(PistolTilt, 1f, 1f),                      // magazine into the grip
                Offset(LeftAt(PistolTilt, 1f, 1f), new Vector3(0f, 0.02f, 0f), 3f), // slap it home
                LeftAt(PistolTilt, 1f, 0f),                      // hand back round the grip
                PistolAim,
            },
        };

        private static readonly KeyedClip RifleReloadClip = new KeyedClip
        {
            Name = "A_Player_RifleReload",
            Times = new[] { 0f, 0.20f, 0.50f, 0.90f, 1.05f, 1.30f, 1.60f },
            Poses = new[]
            {
                RifleAim,
                LeftAt(RifleTilt, 0.5f, 1f),                     // rifle canted in, left hand to the old magazine
                LeftAtPouch(RifleTilt),                          // fresh magazine from the belt
                LeftAt(RifleTilt, 1f, 1f),                       // into the magazine well
                Offset(LeftAt(RifleTilt, 1f, 1f), new Vector3(0f, 0.02f, 0f), 2f), // seat it
                LeftAt(RifleTilt, 1f, 0f),                       // back to the handguard
                RifleAim,
            },
        };

        // ---- Actions (upper body, gun lowered in the right hand, left hand busy) ----

        private static Pose Lowered(Pose aim, Vector3 hand, float pitch)
        {
            aim.Hand = hand;
            aim.GunPitch = pitch;
            aim.Support = 0f;
            aim.MagBlend = 0f;
            return aim;
        }

        private static Pose LeftTo(Pose pose, Vector3 leftHand, float reach = 1f)
        {
            pose.LeftHand = leftHand;
            pose.LeftReach = reach;
            pose.Support = 0f;
            return pose;
        }

        private static readonly Pose PistolLowered = Lowered(PistolAim, new Vector3(0.02f, -0.36f, 0.20f), -55f);
        private static readonly Pose RifleLowered = Lowered(RifleAim, new Vector3(0f, -0.30f, 0.14f), -40f);

        // Left-hand targets (from the left shoulder): chest, a guard in front of the face, a push forward.
        private static readonly Vector3 ChestSpot = new Vector3(0.18f, -0.22f, 0.16f);
        private static readonly Vector3 GuardSpot = new Vector3(0.14f, 0.02f, 0.28f);
        private static readonly Vector3 ReachSpot = new Vector3(0.10f, -0.06f, 0.46f);
        private static readonly Vector3 PushSpot = new Vector3(0.12f, -0.10f, 0.44f);

        // Medkit: gun down, left hand to the belt pouch, press the kit to the chest, back to aim.
        private static KeyedClip MedkitClip(string name, Pose aim, Pose lowered) => new KeyedClip
        {
            Name = name,
            Times = new[] { 0f, 0.18f, 0.42f, 0.62f, 0.78f, 0.95f },
            Poses = new[]
            {
                aim, LeftAtPouch(lowered), LeftTo(lowered, ChestSpot),
                LeftTo(lowered, ChestSpot + new Vector3(0f, -0.03f, -0.03f)), LeftTo(lowered, ChestSpot, 0f), aim,
            },
        };

        // Shield: a quick guard — left forearm up in front of the face — then back to aim.
        private static KeyedClip ShieldClip(string name, Pose aim, Pose lowered) => new KeyedClip
        {
            Name = name,
            Times = new[] { 0f, 0.12f, 0.35f, 0.6f },
            Poses = new[] { aim, LeftTo(lowered, GuardSpot), LeftTo(lowered, GuardSpot), aim },
        };

        // Interact (door, switch): reach forward with the left hand and push, then back to aim.
        private static KeyedClip InteractClip(string name, Pose aim, Pose lowered) => new KeyedClip
        {
            Name = name,
            Times = new[] { 0f, 0.15f, 0.3f, 0.6f },
            Poses = new[] { aim, LeftTo(lowered, ReachSpot), LeftTo(lowered, ReachSpot + new Vector3(0f, 0f, 0.04f)), aim },
        };

        // Push (loops while pushing a barricade): left palm forward, straining in a slow rhythm.
        private static KeyedClip PushClip(string name, Pose lowered) => new KeyedClip
        {
            Name = name, Loop = true,
            Times = new[] { 0f, 0.4f, 0.8f },
            Poses = new[]
            {
                LeftTo(lowered, PushSpot), LeftTo(lowered, PushSpot + new Vector3(0f, 0.01f, -0.03f)),
                LeftTo(lowered, PushSpot),
            },
        };

        private static readonly KeyedClip[] ActionClips =
        {
            MedkitClip("A_Player_PistolMedkit", PistolAim, PistolLowered),
            MedkitClip("A_Player_RifleMedkit", RifleAim, RifleLowered),
            ShieldClip("A_Player_PistolShield", PistolAim, PistolLowered),
            ShieldClip("A_Player_RifleShield", RifleAim, RifleLowered),
            InteractClip("A_Player_PistolInteract", PistolAim, PistolLowered),
            InteractClip("A_Player_RifleInteract", RifleAim, RifleLowered),
            PushClip("A_Player_PistolPush", PistolLowered),
            PushClip("A_Player_RiflePush", RifleLowered),
        };

        // ---- Reactions ----

        // Standing still: the additive Reactions layer's "no reaction" state (its delta from itself is zero).
        private static readonly KeyedClip StandClip = new KeyedClip
        {
            Name = "A_Player_Stand", Loop = true,
            Times = new[] { 0f, 0.5f },
            Poses = new[] { Stand, Stand },
        };

        // Hit: a sharp flinch — chest and head snap back and twist, arms jerk — then recover. Played additively
        // (only the change from its first frame is added), so it shows on top of running, aiming or reloading.
        private static readonly Pose Flinch = new Pose
        {
            KneeL = 4f, KneeR = 4f, ArmL = 10f, ArmR = 10f, ElbowL = 40f, ElbowR = 40f, Spread = 0.25f,
            Lean = -8f, Twist = 10f, Head = -14f, Plant = 1f,
        };

        private static readonly KeyedClip HitClip = new KeyedClip
        {
            Name = "A_Player_Hit",
            Times = new[] { 0f, 0.07f, 0.35f },
            Poses = new[] { Stand, Flinch, Stand },
        };

        // Death: knees buckle, the body tips over backwards and lands on its back, arms falling to the sides.
        private static readonly Pose DeathBuckle = new Pose
        {
            ThighL = 28f, KneeL = 65f, ThighR = 22f, KneeR = 55f, ArmL = -5f, ArmR = -5f,
            ElbowL = 25f, ElbowR = 25f, Spread = 0.25f, Lean = 18f, Head = 15f, Plant = 1f,
        };

        private static readonly Pose DeathFalling = new Pose
        {
            ThighL = 25f, KneeL = 45f, ThighR = 15f, KneeR = 35f, ArmL = 40f, ArmR = 25f,
            ElbowL = 30f, ElbowR = 40f, Spread = 0.55f, Lean = 5f, Head = -10f, BodyPitch = 45f, Body = -0.33f,
        };

        private static readonly Pose DeathGround = new Pose
        {
            ThighL = 12f, KneeL = 20f, FootL = 10f, ThighR = 5f, KneeR = 10f, FootR = 15f, ArmL = 15f, ArmR = 10f,
            ElbowL = 35f, ElbowR = 25f, Spread = 0.9f, Lean = -4f, Head = -12f, BodyPitch = 88f, Body = -0.83f,
        };

        private static Pose Settled(Pose pose)
        {
            pose.Body -= 0.02f;
            pose.Head -= 3f;
            return pose;
        }

        private static readonly KeyedClip DeathClip = new KeyedClip
        {
            Name = "A_Player_Death",
            Times = new[] { 0f, 0.25f, 0.6f, 0.9f, 1.2f },
            Poses = new[] { Stand, DeathBuckle, DeathFalling, DeathGround, Settled(DeathGround) },
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

                foreach (KeyedClip keyed in new[]
                         {
                             JumpStart, Fall, Land,
                             PistolAimClip, PistolFireClip, PistolEquipClip, RifleAimClip, RifleFireClip, RifleEquipClip,
                             PistolReloadClip, RifleReloadClip,
                             StandClip, HitClip, DeathClip,
                         })
                {
                    KeyedClip k = keyed;
                    Save(rig.Bake(k.Duration, 1, k.Loop, k.Sample), k.Name);
                }

                foreach (KeyedClip action in ActionClips)
                {
                    KeyedClip k = action;
                    Save(rig.Bake(k.Duration, 1, k.Loop, k.Sample), k.Name);
                }
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[Player Animator] Generated locomotion, jump, weapon (aim, fire, equip, reload), action, hit and death clips in {OutputFolder}.");
        }

        [MenuItem("Tools/S2/Generate Smooth Locomotion")]
        public static void GenerateLocomotion()
        {
            using (var rig = Rig.Load())
            {
                if (rig == null) return;
                foreach (Gait gait in new[] { Idle, Walk, Run })
                {
                    Gait g = gait;
                    Save(rig.Bake(g.Period, g.KeyStep, true,
                        t => g.Sample(t / g.Period * Mathf.PI * 2f)), g.Name);
                }
            }
            AssetDatabase.SaveAssets();
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

            // How PlayerWeaponHolder mounts a gun on the right hand bone.
            private readonly Vector3 gripPosition;
            private readonly Quaternion gripRotation;

            // Muscle index → curl target, for each hand's finger "Stretched" muscles.
            private readonly (int index, float curl)[] rightFingers, leftFingers;
            private readonly Vector3 restLeftFoot, restRightFoot, restBodyPosition;
            private readonly Quaternion restBodyRotation;

            private Rig(GameObject instance, Animator animator, Vector3 gripPosition, Quaternion gripRotation)
            {
                this.instance = instance;
                this.animator = animator;
                this.gripPosition = gripPosition;
                this.gripRotation = gripRotation;
                rightFingers = FingerMuscles("Right");
                leftFingers = FingerMuscles("Left");
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
                restLeftFoot = Bone(HumanBodyBones.LeftFoot).position;
                restRightFoot = Bone(HumanBodyBones.RightFoot).position;
                var neutral = new HumanPose();
                poseHandler.GetHumanPose(ref neutral);
                restBodyPosition = neutral.bodyPosition;
                restBodyRotation = neutral.bodyRotation;
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

                ReadGrip(out Vector3 gripPosition, out Quaternion gripRotation);
                return new Rig(instance, animator, gripPosition, gripRotation);
            }

            private static void ReadGrip(out Vector3 position, out Quaternion rotation)
            {
                position = Vector3.zero;
                rotation = Quaternion.identity;

                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
                var holder = prefab != null ? prefab.GetComponent<RunwayPanic.Weapons.PlayerWeaponHolder>() : null;
                if (holder == null)
                {
                    Debug.LogWarning($"[Player Animator] No PlayerWeaponHolder on {PlayerPrefabPath}; weapon clips assume no grip offset.");
                    return;
                }

                var serialized = new SerializedObject(holder);
                position = serialized.FindProperty("gripLocalPosition").vector3Value;
                rotation = Quaternion.Euler(serialized.FindProperty("gripLocalEulerAngles").vector3Value);
            }

            private static (int, float)[] FingerMuscles(string side)
            {
                var result = new System.Collections.Generic.List<(int, float)>();
                for (int m = 0; m < HumanTrait.MuscleCount; m++)
                {
                    string name = HumanTrait.MuscleName[m];
                    if (!name.StartsWith(side + " ") || !name.EndsWith("Stretched"))
                        continue;
                    float curl = name.Contains("Thumb") ? ThumbCurl : name.Contains("Index") ? IndexCurl : FingerCurl;
                    result.Add((m, curl));
                }

                return result.ToArray();
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
                    if (p.GaitStride > 0f)
                    {
                        // Humanoid's inferred centre of mass and orientation vary as limbs swing.
                        // Keep the locomotion root stable; only the small authored bob moves it.
                        pose.bodyPosition = restBodyPosition;
                        pose.bodyRotation = restBodyRotation;
                    }
                    pose.bodyPosition += up * (body / Mathf.Max(0.01f, animator.humanScale));
                    if (p.BodyPitch != 0f)
                        pose.bodyRotation = Quaternion.AngleAxis(-p.BodyPitch, right) * pose.bodyRotation;
                    CurlFingers(pose.muscles, rightFingers, p.Grip * p.Armed);
                    CurlFingers(pose.muscles, leftFingers, p.Grip * p.Armed * p.Support);

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

            private static void CurlFingers(float[] muscles, (int index, float curl)[] fingers, float amount)
            {
                if (amount <= 0f)
                    return;
                foreach ((int index, float curl) in fingers)
                    muscles[index] = Mathf.Lerp(muscles[index], curl, amount);
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

                if (p.GaitStride > 0f)
                {
                    // Bend the legs around a continuous foot path instead of raising/lowering the
                    // entire body to whichever foot happens to be lowest each frame.
                    PoseGaitLeg(HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg,
                        HumanBodyBones.LeftFoot, restLeftFoot, p, p.GaitPhase);
                    PoseGaitLeg(HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg,
                        HumanBodyBones.RightFoot, restRightFoot, p, p.GaitPhase + Mathf.PI);
                }

                if (p.Armed > 0f)
                    HoldGun(p);
            }

            private void PoseGaitLeg(HumanBodyBones upper, HumanBodyBones lower, HumanBodyBones foot,
                Vector3 restFoot, Pose pose, float phase)
            {
                float swing = Mathf.Max(0f, Mathf.Cos(phase));
                Vector3 target = restFoot + forward * (pose.GaitStride * Mathf.Sin(phase)) +
                    up * (pose.GaitLift * swing * swing - pose.Body);
                Transform end = Bone(foot);
                Quaternion rotation = end.rotation;
                Vector3 pole = Bone(upper).position + forward;
                TwoBoneIK.Solve(Bone(upper), Bone(lower), end, target, pole);
                end.rotation = rotation;
            }

            // Right hand to 'Hand' (two-bone IK), turned so the mounted gun points along 'GunPitch'; then the left hand
            // onto the gun's support point. Uses the same IK as PlayerAimIK at runtime.
            private void HoldGun(Pose p)
            {
                Transform upperR = Bone(HumanBodyBones.RightUpperArm), lowerR = Bone(HumanBodyBones.RightLowerArm);
                Transform handR = Bone(HumanBodyBones.RightHand);
                Transform upperL = Bone(HumanBodyBones.LeftUpperArm), lowerL = Bone(HumanBodyBones.LeftLowerArm);
                Transform handL = Bone(HumanBodyBones.LeftHand);

                Vector3 target = upperR.position + right * p.Hand.x + up * p.Hand.y + forward * p.Hand.z;
                Vector3 poleR = upperR.position - up * 0.5f + right * 0.5f;
                TwoBoneIK.Solve(upperR, lowerR, handR, target, poleR, p.Armed);

                Vector3 barrel = Quaternion.AngleAxis(p.GunYaw, up) * (Quaternion.AngleAxis(-p.GunPitch, right) * forward);
                Quaternion gunRotation = Quaternion.LookRotation(barrel, up);
                handR.rotation = Quaternion.Slerp(handR.rotation, gunRotation * Quaternion.Inverse(gripRotation), p.Armed);

                if (p.LeftReach > 0f)
                {
                    // Free left hand (medkit, shield, door, push): an IK target relative to the left shoulder.
                    Vector3 reach = upperL.position + right * p.LeftHand.x + up * p.LeftHand.y + forward * p.LeftHand.z;
                    Vector3 pole = upperL.position - up * 0.5f - right * 0.5f;
                    TwoBoneIK.Solve(upperL, lowerL, handL, Vector3.Lerp(handL.position, reach, p.LeftReach), pole);
                    return;
                }

                if (p.Support <= 0f)
                    return;

                // Gun pose from the hand, exactly as PlayerWeaponHolder mounts it.
                Vector3 gunPosition = handR.TransformPoint(gripPosition);
                Quaternion gunWorld = handR.rotation * gripRotation;
                Vector3 grip = p.Rifle ? RifleSupport : PistolSupport;
                Vector3 magWell = p.Rifle ? RifleMagWell : PistolMagWell;
                Vector3 support = gunPosition + gunWorld * Vector3.Lerp(grip, magWell, p.MagBlend);

                Vector3 leftTarget = Vector3.Lerp(handL.position, support, p.Support);
                Vector3 poleL = upperL.position - up * 0.5f - right * 0.5f;
                TwoBoneIK.Solve(upperL, lowerL, handL, leftTarget, poleL);
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
