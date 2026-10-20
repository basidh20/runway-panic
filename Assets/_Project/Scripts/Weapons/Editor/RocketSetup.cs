using System.IO;
using RunwayPanic.Audio;
using RunwayPanic.AudioTools;
using RunwayPanic.Data;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace RunwayPanic.Weapons.EditorTools
{
    /// <summary>
    /// Tools > S2 > Build RPG Rocket, re-runnably — turns the RPG from a hitscan gun into a real rocket launcher:
    /// <list type="number">
    /// <item>synthesises the launch, rocket-in-flight and explosion sounds (only those clips) and their cues;</item>
    /// <item>makes a soft puff texture and the fire (additive) and smoke (alpha) particle materials in <c>Art/VFX</c>;</item>
    /// <item>builds <c>P_RocketLaunch</c> (flash and smoke out of both ends of the tube), <c>P_RocketExplosion</c>
    /// (flash, fireball, smoke, sparks, dust ring, light) and <c>P_Rocket</c> (the tube's own warhead mesh, motor
    /// flame, light and smoke trail) in <c>Prefabs/Weapons</c>;</item>
    /// <item>gives <c>Weapon_Tier4_RPG</c> the rocket and a blast radius, and hides the warhead in
    /// <c>P_Gun_Tier4</c> while the tube is empty.</item>
    /// </list>
    /// </summary>
    public static class RocketSetup
    {
        private const string VfxFolder = "Assets/_Project/Art/VFX";
        private const string PuffTexture = VfxFolder + "/T_VFX_SoftPuff.png";
        private const string ShotMaterial = VfxFolder + "/M_ShotEffect.mat";
        private const string FireMaterial = VfxFolder + "/M_VFX_Fire.mat";
        private const string SmokeMaterial = VfxFolder + "/M_VFX_Smoke.mat";
        private const string WeaponPrefabs = "Assets/_Project/Prefabs/Weapons";
        private const string LauncherPrefab = WeaponPrefabs + "/P_Gun_Tier4.prefab";
        private const string RocketPrefab = WeaponPrefabs + "/P_Rocket.prefab";
        private const string ExplosionPrefab = WeaponPrefabs + "/P_RocketExplosion.prefab";
        private const string LaunchPrefab = WeaponPrefabs + "/P_RocketLaunch.prefab";
        private const string RpgData = "Assets/_Project/ScriptableObjects/Weapons/Weapon_Tier4_RPG.asset";
        private const string CueFolder = "Assets/_Project/Audio/Cues";
        private const string WarheadName = "RPG_Rocket";
        private const float ExplosionRadius = 5f;
        private const float TubeLength = 1.6f; // muzzle (warhead tip) to the back of the tube

        private static readonly Color FireHot = new Color(1f, 0.93f, 0.75f);
        private static readonly Color FireMid = new Color(1f, 0.55f, 0.16f);
        private static readonly Color FireCool = new Color(0.45f, 0.1f, 0.02f);

        [MenuItem("Tools/S2/Build RPG Rocket")]
        public static void Build()
        {
            GameAudioGenerator.GenerateOnly("SFX_Shot_RPG", "SFX_Rocket_Flight", "SFX_Explosion");

            Texture2D puff = BuildPuffTexture();
            Material fire = BuildMaterial(FireMaterial, puff, additive: true);
            Material smoke = BuildMaterial(SmokeMaterial, puff, additive: false);

            GameObject launch = BuildLaunch(fire, smoke);
            GameObject explosion = BuildExplosion(fire, smoke);
            GameObject rocket = BuildRocket(fire, smoke, launch, explosion);
            if (rocket == null)
                return;

            var data = AssetDatabase.LoadAssetAtPath<WeaponData>(RpgData);
            var so = new SerializedObject(data);
            so.FindProperty("projectilePrefab").objectReferenceValue = rocket;
            so.FindProperty("explosionRadius").floatValue = ExplosionRadius;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(data);

            HideWarheadWhenEmpty();
            AssetDatabase.SaveAssets();
            Debug.Log("[RPG] Built the rocket, its launch and explosion effects and sounds, and wired the RPG.");
        }

        // ---------- Texture and materials ----------

        // A soft round puff with lumpy density, white (particles tint it), alpha falling to zero at the edge.
        private static Texture2D BuildPuffTexture()
        {
            const int size = 128;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f) / (size * 0.5f) - 1f, dy = (y + 0.5f) / (size * 0.5f) - 1f;
                float edge = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                edge = edge * edge * (3f - 2f * edge);
                float lumps = Mathf.PerlinNoise(x * 0.06f, y * 0.06f) * 0.6f + Mathf.PerlinNoise(x * 0.14f + 40f, y * 0.14f + 40f) * 0.4f;
                byte alpha = (byte)Mathf.RoundToInt(255f * edge * Mathf.Lerp(0.6f, 1f, lumps));
                pixels[y * size + x] = new Color32(255, 255, 255, alpha);
            }

            texture.SetPixels32(pixels);
            File.WriteAllBytes(PuffTexture, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(PuffTexture);

            var importer = (TextureImporter)AssetImporter.GetAtPath(PuffTexture);
            importer.textureType = TextureImporterType.Default;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.mipmapEnabled = true;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(PuffTexture);
        }

        // Both start from M_ShotEffect (URP Particles Unlit, additive); smoke switches to alpha blending.
        private static Material BuildMaterial(string path, Texture2D texture, bool additive)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(AssetDatabase.LoadAssetAtPath<Material>(ShotMaterial));
                AssetDatabase.CreateAsset(material, path);
            }

            material.SetTexture("_BaseMap", texture);
            material.SetFloat("_Blend", additive ? 2f : 0f);
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
            material.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            material.SetFloat("_DstBlendAlpha", (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
            EditorUtility.SetDirty(material);
            return material;
        }

        // ---------- Prefabs ----------

        private static GameObject BuildLaunch(Material fire, Material smoke)
        {
            var root = new GameObject("P_RocketLaunch");

            // Out of the front of the tube (the muzzle socket is at the warhead's tip, ~0.5 m ahead of the mouth).
            Vector3 mouth = new Vector3(0f, 0f, -0.45f);
            ParticleSystem flash = Particles(root.transform, "MuzzleFlash", fire, mouth, Quaternion.identity);
            Burst(flash, 6, Life(0.06f, 0.1f), Speed(2f, 5f), Size(0.4f, 0.8f));
            Cone(flash, 18f, 0.05f);
            FireColours(flash);

            ParticleSystem front = Particles(root.transform, "MuzzleSmoke", smoke, mouth, Quaternion.identity);
            Burst(front, 8, Life(1f, 2f), Speed(0.5f, 2.5f), Size(0.4f, 0.8f));
            Cone(front, 25f, 0.05f);
            SmokeColours(front, new Color(0.82f, 0.81f, 0.78f), 0.4f);
            Grow(front, 2.5f);
            Drift(front, 2.5f, 0.3f);

            // The backblast: a jet of flame and a cloud of smoke out of the back of the tube. It blows towards the
            // over-the-shoulder camera, so it stays thin and short enough not to blind the player.
            var back = new GameObject("Backblast").transform;
            back.SetParent(root.transform, false);
            back.localPosition = new Vector3(0f, 0f, -TubeLength);
            back.localRotation = Quaternion.Euler(0f, 180f, 0f);

            ParticleSystem backFlash = Particles(back, "BackFlash", fire, Vector3.zero, Quaternion.identity);
            Burst(backFlash, 8, Life(0.06f, 0.1f), Speed(5f, 9f), Size(0.3f, 0.6f));
            Cone(backFlash, 12f, 0.06f);
            FireColours(backFlash);

            ParticleSystem backSmoke = Particles(back, "BackSmoke", smoke, Vector3.zero, Quaternion.identity);
            Burst(backSmoke, 16, Life(0.8f, 1.6f), Speed(3f, 7f), Size(0.3f, 0.6f));
            Cone(backSmoke, 16f, 0.08f);
            SmokeColours(backSmoke, new Color(0.8f, 0.79f, 0.76f), 0.35f);
            Grow(backSmoke, 2.5f);
            Drift(backSmoke, 3f, 0.3f);

            Light light = AddLight(root.transform, mouth, FireMid, 5f, 7f);
            AddBurst(root, light, 0.15f, 4f);
            return Save(root, LaunchPrefab);
        }

        private static GameObject BuildExplosion(Material fire, Material smoke)
        {
            var root = new GameObject("P_RocketExplosion");

            ParticleSystem flash = Particles(root.transform, "Flash", fire, Vector3.zero, Quaternion.identity);
            Burst(flash, 1, Life(0.12f, 0.12f), Speed(0f, 0f), Size(9f, 9f));
            Colours(flash, (0f, new Color(1f, 0.92f, 0.75f)), (1f, Color.black));

            ParticleSystem fireball = Particles(root.transform, "Fireball", fire, Vector3.zero, Quaternion.identity);
            Burst(fireball, 28, Life(0.4f, 0.8f), Speed(4f, 11f), Size(2.4f, 4.2f));
            Sphere(fireball, 0.8f);
            Colours(fireball, (0f, FireHot), (0.3f, FireMid), (0.7f, FireCool), (1f, Color.black));
            Grow(fireball, 1.4f, 0.6f);
            Drift(fireball, 3f, 0f);
            Spin(fireball);

            ParticleSystem cloud = Particles(root.transform, "Smoke", smoke, Vector3.zero, Quaternion.identity);
            Burst(cloud, 20, Life(2.5f, 4.5f), Speed(1.5f, 4.5f), Size(3f, 5.5f), 0.08f);
            Sphere(cloud, 0.8f);
            SmokeColours(cloud, new Color(0.22f, 0.21f, 0.2f), 0.85f);
            Grow(cloud, 1.8f, 0.5f);
            Drift(cloud, 1.2f, 1.2f);
            Spin(cloud);

            ParticleSystem sparks = Particles(root.transform, "Sparks", fire, Vector3.zero, Quaternion.identity);
            Burst(sparks, 45, Life(0.5f, 1.2f), Speed(12f, 28f), Size(0.04f, 0.09f));
            Sphere(sparks, 0.2f);
            Colours(sparks, (0f, new Color(1f, 0.85f, 0.45f)), (0.6f, FireMid), (1f, Color.black));
            ParticleSystem.MainModule main = sparks.main;
            main.gravityModifier = 1.2f;
            var renderer = sparks.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.velocityScale = 0.04f;
            renderer.lengthScale = 1f;

            // A ring of dust pushed out sideways by the shockwave.
            ParticleSystem dust = Particles(root.transform, "DustRing", smoke, Vector3.zero, Quaternion.Euler(90f, 0f, 0f));
            Burst(dust, 14, Life(1.2f, 2f), Speed(6f, 10f), Size(1.2f, 2f));
            ParticleSystem.ShapeModule ring = dust.shape;
            ring.enabled = true;
            ring.shapeType = ParticleSystemShapeType.Circle;
            ring.radius = 0.5f;
            SmokeColours(dust, new Color(0.55f, 0.5f, 0.45f), 0.6f);
            Grow(dust, 2f);
            Drift(dust, 3f, 0.2f);

            Light light = AddLight(root.transform, Vector3.zero, new Color(1f, 0.65f, 0.3f), 35f, 25f);
            AddBurst(root, light, 0.45f, 6f);
            return Save(root, ExplosionPrefab);
        }

        private static GameObject BuildRocket(Material fire, Material smoke, GameObject launch, GameObject explosion)
        {
            var launcher = AssetDatabase.LoadAssetAtPath<GameObject>(LauncherPrefab);
            Transform warhead = launcher != null ? FindChild(launcher.transform, WarheadName) : null;
            var warheadMesh = warhead != null ? warhead.GetComponent<MeshFilter>() : null;
            if (warheadMesh == null || warheadMesh.sharedMesh == null)
            {
                Debug.LogError($"[RPG] {LauncherPrefab} has no {WarheadName} mesh to use for the rocket.");
                return null;
            }

            var root = new GameObject("P_Rocket");
            var body = new GameObject("Body").transform;
            body.SetParent(root.transform, false);

            // The warhead mesh sits where it was in the tube; centre it on the rocket.
            Bounds bounds = warheadMesh.sharedMesh.bounds;
            var mesh = new GameObject("Mesh");
            mesh.transform.SetParent(body, false);
            mesh.transform.localPosition = -bounds.center;
            mesh.AddComponent<MeshFilter>().sharedMesh = warheadMesh.sharedMesh;
            var meshRenderer = mesh.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterials = warhead.GetComponent<MeshRenderer>().sharedMaterials;
            meshRenderer.shadowCastingMode = ShadowCastingMode.On;

            Vector3 tail = new Vector3(0f, 0f, -bounds.extents.z - 0.02f);
            Quaternion backwards = Quaternion.Euler(0f, 180f, 0f);

            ParticleSystem flame = Particles(root.transform, "Flame", fire, tail, backwards);
            ParticleSystem.MainModule flameMain = flame.main;
            flameMain.loop = true;
            flameMain.duration = 1f;
            flameMain.simulationSpace = ParticleSystemSimulationSpace.Local;
            flameMain.startLifetime = Life(0.05f, 0.09f);
            flameMain.startSpeed = Speed(4f, 7f);
            flameMain.startSize = Size(0.18f, 0.32f);
            ParticleSystem.EmissionModule flameRate = flame.emission;
            flameRate.rateOverTime = 90f;
            Cone(flame, 6f, 0.03f);
            FireColours(flame);
            Grow(flame, 0.3f, 1f);

            ParticleSystem trail = Particles(root.transform, "SmokeTrail", smoke, tail + new Vector3(0f, 0f, -0.05f), backwards);
            ParticleSystem.MainModule trailMain = trail.main;
            trailMain.loop = true;
            trailMain.duration = 1f;
            trailMain.startLifetime = Life(1.6f, 2.4f);
            trailMain.startSpeed = Speed(0.2f, 0.6f);
            trailMain.startSize = Size(0.25f, 0.4f);
            trailMain.maxParticles = 400;
            ParticleSystem.EmissionModule trailRate = trail.emission;
            trailRate.rateOverDistance = 12f;
            Cone(trail, 8f, 0.05f);
            SmokeColours(trail, new Color(0.85f, 0.85f, 0.83f), 0.4f);
            Grow(trail, 2.6f, 0.6f);
            Drift(trail, 0.5f, 0.25f);
            Spin(trail);
            ParticleSystem.NoiseModule noise = trail.noise;
            noise.enabled = true;
            noise.strength = 0.3f;
            noise.frequency = 0.5f;

            AddLight(root.transform, tail, new Color(1f, 0.6f, 0.25f), 4f, 7f);

            var rocket = root.AddComponent<Rocket>();
            var so = new SerializedObject(rocket);
            so.FindProperty("body").objectReferenceValue = body;
            so.FindProperty("smokeTrail").objectReferenceValue = trail;
            so.FindProperty("launchEffect").objectReferenceValue = launch;
            so.FindProperty("explosionEffect").objectReferenceValue = explosion;
            so.FindProperty("flightSound").objectReferenceValue = LoadCue("SC_Rocket_Flight");
            so.FindProperty("explosionSound").objectReferenceValue = LoadCue("SC_Explosion");
            so.ApplyModifiedPropertiesWithoutUndo();
            return Save(root, RocketPrefab);
        }

        private static void HideWarheadWhenEmpty()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(LauncherPrefab);
            try
            {
                Transform warhead = FindChild(root.transform, WarheadName);
                var visual = root.GetComponent<LoadedRoundVisual>();
                if (visual == null)
                    visual = root.AddComponent<LoadedRoundVisual>();
                var so = new SerializedObject(visual);
                so.FindProperty("round").objectReferenceValue = warhead != null ? warhead.gameObject : null;
                so.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, LauncherPrefab);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        // ---------- Particle helpers ----------

        // A one-shot, world-space system with no shape and no continuous emission, ready to configure.
        private static ParticleSystem Particles(Transform parent, string name, Material material, Vector3 position,
            Quaternion rotation)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localRotation = rotation;

            var system = go.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule main = system.main;
            main.loop = false;
            main.duration = 1f;
            main.playOnAwake = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.startColor = Color.white;
            ParticleSystem.EmissionModule emission = system.emission;
            emission.rateOverTime = 0f;
            ParticleSystem.ShapeModule shape = system.shape;
            shape.enabled = false;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return system;
        }

        private static ParticleSystem.MinMaxCurve Life(float min, float max) => new ParticleSystem.MinMaxCurve(min, max);
        private static ParticleSystem.MinMaxCurve Speed(float min, float max) => new ParticleSystem.MinMaxCurve(min, max);
        private static ParticleSystem.MinMaxCurve Size(float min, float max) => new ParticleSystem.MinMaxCurve(min, max);

        private static void Burst(ParticleSystem system, int count, ParticleSystem.MinMaxCurve life,
            ParticleSystem.MinMaxCurve speed, ParticleSystem.MinMaxCurve size, float delay = 0f)
        {
            ParticleSystem.MainModule main = system.main;
            main.startLifetime = life;
            main.startSpeed = speed;
            main.startSize = size;
            main.maxParticles = Mathf.Max(count, 1);
            ParticleSystem.EmissionModule emission = system.emission;
            emission.SetBursts(new[] { new ParticleSystem.Burst(delay, (short)count) });
        }

        private static void Cone(ParticleSystem system, float angle, float radius)
        {
            ParticleSystem.ShapeModule shape = system.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = angle;
            shape.radius = radius;
        }

        private static void Sphere(ParticleSystem system, float radius)
        {
            ParticleSystem.ShapeModule shape = system.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = radius;
        }

        // Additive: fading to black fades out.
        private static void FireColours(ParticleSystem system) =>
            Colours(system, (0f, FireHot), (0.4f, FireMid), (1f, Color.black));

        private static void Colours(ParticleSystem system, params (float time, Color colour)[] keys)
        {
            var colours = new GradientColorKey[keys.Length];
            for (int i = 0; i < keys.Length; i++)
                colours[i] = new GradientColorKey(keys[i].colour, keys[i].time);
            var gradient = new Gradient();
            gradient.SetKeys(colours, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            ParticleSystem.ColorOverLifetimeModule over = system.colorOverLifetime;
            over.enabled = true;
            over.color = gradient;
        }

        // Alpha-blended smoke: fades in quickly, thins out, lightens a little as it spreads.
        private static void SmokeColours(ParticleSystem system, Color colour, float opacity)
        {
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(colour, 0f), new GradientColorKey(Color.Lerp(colour, Color.white, 0.25f), 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(opacity, 0.06f), new GradientAlphaKey(opacity * 0.5f, 0.5f), new GradientAlphaKey(0f, 1f) });
            ParticleSystem.ColorOverLifetimeModule over = system.colorOverLifetime;
            over.enabled = true;
            over.color = gradient;
        }

        private static void Grow(ParticleSystem system, float end, float start = 1f)
        {
            ParticleSystem.SizeOverLifetimeModule size = system.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, start, 0f, (end - start) * 2f), new Keyframe(1f, end, 0f, 0f)));
        }

        // Air drag slows the burst down; smoke rises.
        private static void Drift(ParticleSystem system, float drag, float rise)
        {
            ParticleSystem.LimitVelocityOverLifetimeModule limit = system.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.limit = 1000f;
            limit.drag = drag;
            if (rise <= 0f)
                return;
            ParticleSystem.VelocityOverLifetimeModule velocity = system.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.x = 0f;
            velocity.y = rise;
            velocity.z = 0f;
        }

        private static void Spin(ParticleSystem system)
        {
            ParticleSystem.MainModule main = system.main;
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            ParticleSystem.RotationOverLifetimeModule rotation = system.rotationOverLifetime;
            rotation.enabled = true;
            rotation.z = new ParticleSystem.MinMaxCurve(-0.35f, 0.35f);
        }

        private static Light AddLight(Transform parent, Vector3 position, Color colour, float intensity, float range)
        {
            var light = new GameObject("Light").AddComponent<Light>();
            light.transform.SetParent(parent, false);
            light.transform.localPosition = position;
            light.type = LightType.Point;
            light.color = colour;
            light.intensity = intensity;
            light.range = range;
            light.shadows = LightShadows.None;
            return light;
        }

        private static void AddBurst(GameObject root, Light light, float fade, float lifetime)
        {
            var burst = root.AddComponent<VfxBurst>();
            var so = new SerializedObject(burst);
            so.FindProperty("flash").objectReferenceValue = light;
            so.FindProperty("flashFade").floatValue = fade;
            so.FindProperty("lifetime").floatValue = lifetime;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ---------- Misc ----------

        private static GameObject Save(GameObject root, string path)
        {
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return prefab;
        }

        private static SoundCue LoadCue(string name)
        {
            var cue = AssetDatabase.LoadAssetAtPath<SoundCue>($"{CueFolder}/{name}.asset");
            if (cue == null)
                Debug.LogWarning($"[RPG] Cue {name} is missing.");
            return cue;
        }

        private static Transform FindChild(Transform parent, string childName)
        {
            foreach (Transform child in parent)
            {
                if (child.name == childName)
                    return child;
                Transform found = FindChild(child, childName);
                if (found != null)
                    return found;
            }

            return null;
        }
    }
}
