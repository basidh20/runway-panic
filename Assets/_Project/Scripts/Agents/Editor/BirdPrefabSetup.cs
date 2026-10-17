using UnityEditor;
using UnityEngine;

namespace RunwayPanic.Agents.EditorTools
{
    /// <summary>
    /// Tools > Agents > Setup Bird Prefabs: the only way the Agent Controller changes the bird prefabs (F4 of the shared
    /// bird AI plan: swap the F2 stand-ins for the real components).
    /// 1. Creates the motion profiles in ScriptableObjects/Agents/ if they are missing (existing ones are kept, so
    ///    values tuned in the Inspector survive).
    /// 2. P_Bird_Base and P_Bird_Hawk (the two prefabs with a model): replaces the stand-ins SimpleBirdMotor and
    ///    BlendShapeBirdAnimator with BirdMotor and BirdAnimatorDriver, makes sure the kinematic interpolated Rigidbody, BirdHealth and
    ///    BirdContactAttack are there, and puts AC_Bird / AOC_Bird_Hawk on the model child's Animator with root motion
    ///    off and transform culling.
    /// 3. Gives each species its motion profile (Pigeon and Crow on their variants, Hawk on its prefab).
    /// Species stats stay on BirdHealth, set by the Core Developer's Tools > S3 > Add Bird AI Components.
    /// Safe to run any number of times. Run Tools > Agents > Build Bird Animations first.
    /// </summary>
    public static class BirdPrefabSetup
    {
        const string PrefabFolder = "Assets/_Project/Prefabs/Birds/";
        const string ProfileParent = "Assets/_Project/ScriptableObjects";
        const string ProfileFolder = ProfileParent + "/Agents";
        const string ControllerPath = "Assets/_Project/Art/Animations/Birds/AC_Bird.controller";
        const string HawkOverridePath = "Assets/_Project/Art/Animations/Birds/AOC_Bird_Hawk.overrideController";

        readonly struct Profile
        {
            public readonly string Name;
            public readonly float TurnRate, MaxPitch, MaxBank, BankSmoothTime, Acceleration, LookAhead, FlapSpeed;

            public Profile(string name, float turnRate, float maxPitch, float maxBank, float bankSmoothTime,
                float acceleration, float lookAhead, float flapSpeed)
            {
                Name = name;
                TurnRate = turnRate;
                MaxPitch = maxPitch;
                MaxBank = maxBank;
                BankSmoothTime = bankSmoothTime;
                Acceleration = acceleration;
                LookAhead = lookAhead;
                FlapSpeed = flapSpeed;
            }
        }

        // Starting values only: once an asset exists it is never overwritten (tune it in the Inspector).
        //   Pigeon: small and twitchy - fast turns, quick roll, short look-ahead, fast wing beat.
        //   Crow:   in between.
        //   Hawk:   wide, smooth banking turns and slow, powerful wing beats.
        //   Default (the gull, and anything without its own profile): calm.
        static readonly Profile[] Profiles =
        {
            //          name                turn  pitch bank  bankTime accel look  flap
            new Profile("BirdMotion_Default", 140f, 35f, 30f, 0.20f, 6f, 2.5f, 1.00f),
            new Profile("BirdMotion_Pigeon", 260f, 40f, 35f, 0.07f, 10f, 1.2f, 1.60f),
            new Profile("BirdMotion_Crow", 170f, 35f, 35f, 0.15f, 7f, 2.0f, 1.15f),
            new Profile("BirdMotion_Hawk", 90f, 30f, 35f, 0.35f, 5f, 3.5f, 0.75f),
        };

        [MenuItem("Tools/Agents/Setup Bird Prefabs")]
        public static void SetupAll()
        {
            var controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ControllerPath);
            var hawkController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(HawkOverridePath);
            if (controller == null || hawkController == null)
            {
                Debug.LogWarning("[Agents] AC_Bird / AOC_Bird_Hawk not found - run Tools > Agents > Build Bird Animations first.");
                return;
            }

            EnsureProfiles();

            // Models first, so the variants inherit the new components.
            SetupModelPrefab("P_Bird_Base", controller, LoadProfile("BirdMotion_Default"));
            SetupModelPrefab("P_Bird_Hawk", hawkController, LoadProfile("BirdMotion_Hawk"));

            SetupVariant("P_Bird_Pigeon", LoadProfile("BirdMotion_Pigeon"));
            SetupVariant("P_Bird_Crow", LoadProfile("BirdMotion_Crow"));
            // P_Bird_Gull keeps the base prefab's default profile.

            AssetDatabase.SaveAssets();
            Debug.Log("[Agents] Setup Bird Prefabs: done.");
        }

        static void SetupModelPrefab(string prefabName, RuntimeAnimatorController controller, BirdMotionProfile profile)
        {
            string path = PrefabFolder + prefabName + ".prefab";
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                if (!root.TryGetComponent(out Rigidbody body))
                    body = root.AddComponent<Rigidbody>();
                body.isKinematic = true;   // moved by BirdMotor, until BirdHealth drops it on death
                body.useGravity = false;
                body.interpolation = RigidbodyInterpolation.Interpolate;
                body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative; // falling bodies don't tunnel

                // F4: the real motor replaces the F2 stand-in (brains only see IBirdMotor, so nothing else changes).
                RemoveIfPresent<SimpleBirdMotor>(root);
                BirdMotor motor = Ensure<BirdMotor>(root);
                Ensure<BirdHealth>(root);
                Ensure<BirdContactAttack>(root);

                // F4: the Animator-based IBirdAnimator replaces the blend-shape stand-in on the model child.
                RemoveIfPresent<BlendShapeBirdAnimator>(root);
                BirdAnimatorDriver driver = Ensure<BirdAnimatorDriver>(root);
                Animator animator = root.GetComponentInChildren<Animator>(true);
                if (animator == null)
                {
                    Debug.LogWarning($"[Agents] {prefabName}: no Animator on the model child.");
                }
                else
                {
                    animator.runtimeAnimatorController = controller;
                    animator.applyRootMotion = false;
                    animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
                    SetReference(driver, "animator", animator);
                }

                SetReference(motor, "motionProfile", profile);
                PrefabUtility.SaveAsPrefabAsset(root, path, out bool success);
                Debug.Log(success ? $"[Agents] Set up {path}" : $"[Agents] Could not save {path}");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        static void SetupVariant(string prefabName, BirdMotionProfile profile)
        {
            string path = PrefabFolder + prefabName + ".prefab";
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                if (!root.TryGetComponent(out BirdMotor motor))
                {
                    Debug.LogWarning($"[Agents] {prefabName} has no BirdMotor - is it still a variant of P_Bird_Base?");
                    return;
                }

                SetReference(motor, "motionProfile", profile);
                PrefabUtility.SaveAsPrefabAsset(root, path, out bool success);
                Debug.Log(success ? $"[Agents] Set up {path}" : $"[Agents] Could not save {path}");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        static T Ensure<T>(GameObject target) where T : Component
        {
            return target.TryGetComponent(out T component) ? component : target.AddComponent<T>();
        }

        static void RemoveIfPresent<T>(GameObject target) where T : Component
        {
            foreach (T component in target.GetComponentsInChildren<T>(true))
                Object.DestroyImmediate(component, true);
        }

        static void SetReference(Object component, string field, Object value)
        {
            if (component == null)
                return;

            using (var serialized = new SerializedObject(component))
            {
                serialized.FindProperty(field).objectReferenceValue = value;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        static void EnsureProfiles()
        {
            if (!AssetDatabase.IsValidFolder(ProfileFolder))
                AssetDatabase.CreateFolder(ProfileParent, "Agents");

            foreach (Profile profile in Profiles)
            {
                string path = $"{ProfileFolder}/{profile.Name}.asset";
                if (AssetDatabase.LoadAssetAtPath<BirdMotionProfile>(path) != null)
                    continue;

                var asset = ScriptableObject.CreateInstance<BirdMotionProfile>();
                using (var serialized = new SerializedObject(asset))
                {
                    serialized.FindProperty("maxTurnRate").floatValue = profile.TurnRate;
                    serialized.FindProperty("maxPitch").floatValue = profile.MaxPitch;
                    serialized.FindProperty("maxBank").floatValue = profile.MaxBank;
                    serialized.FindProperty("bankSmoothTime").floatValue = profile.BankSmoothTime;
                    serialized.FindProperty("acceleration").floatValue = profile.Acceleration;
                    serialized.FindProperty("lookAheadDistance").floatValue = profile.LookAhead;
                    serialized.FindProperty("flapSpeedMultiplier").floatValue = profile.FlapSpeed;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }
                AssetDatabase.CreateAsset(asset, path);
                Debug.Log($"[Agents] Created {path}");
            }
        }

        static BirdMotionProfile LoadProfile(string name) =>
            AssetDatabase.LoadAssetAtPath<BirdMotionProfile>($"{ProfileFolder}/{name}.asset");
    }
}
