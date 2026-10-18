using System.IO;
using RunwayPanic.Core;
using RunwayPanic.Data;
using RunwayPanic.UI;
using Unity.Cinemachine;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace RunwayPanic.Player.EditorTools
{
    /// <summary>
    /// Tools > S2 > Build Aiming and Scope, re-runnably:
    /// <list type="number">
    /// <item>writes each gun's aiming stats (aim zoom, scope, sensitivity, spread, recoil, hit radius);</item>
    /// <item>replaces the free-look orbit camera on <c>P_Player_Gameplay</c> with an over-the-shoulder
    /// <see cref="CinemachineThirdPersonFollow"/> on a <c>CameraTarget</c> turned by <see cref="PlayerCameraRig"/>;</item>
    /// <item>adds the crosshair's spread lines and hit marker, and the sniper scope overlay, to the HUD.</item>
    /// </list>
    /// The Aim action (right mouse / left trigger) is in <c>Settings/InputSystem_Actions</c>.
    /// </summary>
    public static class AimingSetup
    {
        private const string PlayerPrefab = "Assets/_Project/Prefabs/Player/P_Player_Gameplay.prefab";
        private const string WeaponFolder = "Assets/_Project/ScriptableObjects/Weapons";
        private const string SpriteFolder = "Assets/_Project/Art/UI/Sprites";
        private const string ScopeSprite = SpriteFolder + "/T_UI_ScopeMask.png";
        private const float Lens = 1080f; // the HUD canvas is 1080 units tall at any aspect

        private struct GunAim
        {
            public float Fov, Sensitivity, Hip, Aim, PerShot, Max, Recovery, Recoil, Radius;
            public bool Scope;
        }

        [MenuItem("Tools/S2/Build Aiming and Scope")]
        public static void Build()
        {
            SetGunStats();
            MakeScopeSprite();
            BuildPlayer();
            AssetDatabase.SaveAssets();
            Debug.Log("[Aiming] Gun aim stats, the shoulder camera, the crosshair and the scope overlay are built.");
        }

        // ---------- Guns ----------

        private static void SetGunStats()
        {
            foreach (string guid in AssetDatabase.FindAssets("t:WeaponData", new[] { WeaponFolder }))
            {
                var gun = AssetDatabase.LoadAssetAtPath<WeaponData>(AssetDatabase.GUIDToAssetPath(guid));
                GunAim aim = gun.Rarity switch
                {
                    // Pistol: steady, quick to recover; a little forgiving for gulls.
                    WeaponRarity.Common => new GunAim
                    {
                        Fov = 50f, Sensitivity = 0.8f, Hip = 1.6f, Aim = 0.35f,
                        PerShot = 0.7f, Max = 4f, Recovery = 7f, Recoil = 1f, Radius = 0.15f,
                    },
                    // Assault rifle: blooms in long bursts, small kick per shot.
                    WeaponRarity.Uncommon => new GunAim
                    {
                        Fov = 45f, Sensitivity = 0.7f, Hip = 2.2f, Aim = 0.5f,
                        PerShot = 0.35f, Max = 5f, Recovery = 9f, Recoil = 0.45f, Radius = 0.12f,
                    },
                    // Sniper: wild from the hip, perfect through the scope, heavy kick.
                    WeaponRarity.Rare => new GunAim
                    {
                        Fov = 12f, Sensitivity = 0.3f, Hip = 5f, Aim = 0f,
                        PerShot = 3f, Max = 8f, Recovery = 6f, Recoil = 3.5f, Radius = 0.08f, Scope = true,
                    },
                    // RPG: a big rocket — wide hit radius, big kick.
                    _ => new GunAim
                    {
                        Fov = 45f, Sensitivity = 0.65f, Hip = 2.5f, Aim = 0.6f,
                        PerShot = 2f, Max = 5f, Recovery = 5f, Recoil = 4f, Radius = 0.35f,
                    },
                };

                var so = new SerializedObject(gun);
                so.FindProperty("aimFieldOfView").floatValue = aim.Fov;
                so.FindProperty("hasScope").boolValue = aim.Scope;
                so.FindProperty("aimSensitivity").floatValue = aim.Sensitivity;
                so.FindProperty("hipSpread").floatValue = aim.Hip;
                so.FindProperty("aimSpread").floatValue = aim.Aim;
                so.FindProperty("spreadPerShot").floatValue = aim.PerShot;
                so.FindProperty("maxSpread").floatValue = aim.Max;
                so.FindProperty("spreadRecovery").floatValue = aim.Recovery;
                so.FindProperty("recoil").floatValue = aim.Recoil;
                so.FindProperty("hitRadius").floatValue = aim.Radius;
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(gun);
            }
        }

        // ---------- Scope sprite: black outside a round lens with a soft, slightly darkened rim ----------

        private static void MakeScopeSprite()
        {
            const int size = 512;
            Directory.CreateDirectory(SpriteFolder);
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            float centre = size * 0.5f, lens = size * 0.485f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(centre, centre)) / lens;
                    float outside = Mathf.Clamp01((d - 0.985f) / 0.015f);      // hard but anti-aliased edge
                    float rim = Mathf.SmoothStep(0f, 0.55f, (d - 0.8f) / 0.2f); // the lens darkens toward its edge
                    texture.SetPixel(x, y, new Color(0f, 0f, 0f, Mathf.Max(outside, rim)));
                }

            File.WriteAllBytes(ScopeSprite, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(ScopeSprite, ImportAssetOptions.ForceUpdate);
            var importer = (TextureImporter)AssetImporter.GetAtPath(ScopeSprite);
            importer.textureType = TextureImporterType.Sprite;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }

        // ---------- Player prefab ----------

        private static void BuildPlayer()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(PlayerPrefab);
            try
            {
                Transform target = root.transform.Find("CameraTarget");
                if (target == null)
                {
                    target = new GameObject("CameraTarget").transform;
                    target.SetParent(root.transform, false);
                }
                target.localPosition = new Vector3(0f, 1.6f, 0f);
                target.localRotation = Quaternion.identity;

                var vcam = root.GetComponentInChildren<CinemachineCamera>(true);
                vcam.gameObject.name = "ShoulderCamera";
                RemoveIfPresent<CinemachineInputAxisController>(vcam.gameObject);
                RemoveIfPresent<CinemachineRotationComposer>(vcam.gameObject);
                RemoveIfPresent<CinemachineOrbitalFollow>(vcam.gameObject);
                if (!vcam.TryGetComponent(out CinemachineThirdPersonFollow follow))
                    follow = vcam.gameObject.AddComponent<CinemachineThirdPersonFollow>();
                vcam.Follow = target;
                vcam.LookAt = null;
                LensSettings lens = vcam.Lens;
                lens.FieldOfView = 60f;
                lens.NearClipPlane = 0.05f;
                vcam.Lens = lens;

                follow.AvoidObstacles.Enabled = true;
                follow.AvoidObstacles.CollisionFilter = ~0;
                follow.AvoidObstacles.IgnoreTag = "Player";
                follow.AvoidObstacles.CameraRadius = 0.2f;
                follow.AvoidObstacles.DampingIntoCollision = 0.05f;
                follow.AvoidObstacles.DampingFromCollision = 0.4f;

                if (!root.TryGetComponent(out PlayerCameraRig rig))
                    rig = root.AddComponent<PlayerCameraRig>();
                var rigSo = new SerializedObject(rig);
                rigSo.FindProperty("cameraTarget").objectReferenceValue = target;
                rigSo.FindProperty("virtualCamera").objectReferenceValue = vcam;
                rigSo.FindProperty("follow").objectReferenceValue = follow;
                // Character clearly left of the crosshair, as in shoulder-camera shooters.
                rigSo.FindProperty("hipShoulder").vector3Value = new Vector3(0.8f, 0.1f, 0f);
                rigSo.FindProperty("aimShoulder").vector3Value = new Vector3(0.65f, 0.08f, 0f);
                rigSo.ApplyModifiedPropertiesWithoutUndo();

                Transform hud = root.GetComponentInChildren<CrosshairUI>(true).transform;
                BuildScope(hud);
                BuildCrosshair(hud.GetComponent<CrosshairUI>());

                PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefab);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void RemoveIfPresent<T>(GameObject go) where T : Component
        {
            if (go.TryGetComponent(out T component))
                Object.DestroyImmediate(component, true);
        }

        private static void BuildCrosshair(CrosshairUI crosshair)
        {
            Transform hud = crosshair.transform;
            var lines = new[] { "Line_Top", "Line_Bottom", "Line_Left", "Line_Right" };
            var so = new SerializedObject(crosshair);
            SerializedProperty lineList = so.FindProperty("lines");
            lineList.arraySize = lines.Length;
            for (int i = 0; i < lines.Length; i++)
                lineList.GetArrayElementAtIndex(i).objectReferenceValue = hud.Find(lines[i]);

            // The hit marker: four short strokes forming an X around the centre.
            SerializedProperty markers = so.FindProperty("hitMarker");
            markers.arraySize = 4;
            for (int i = 0; i < 4; i++)
            {
                string name = $"HitMarker_{i + 1}";
                Transform stroke = hud.Find(name);
                if (stroke == null)
                    stroke = new GameObject(name, typeof(RectTransform), typeof(Image)).transform;
                stroke.SetParent(hud, false);
                stroke.gameObject.layer = hud.gameObject.layer;
                var rect = (RectTransform)stroke;
                rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
                float sx = i % 2 == 0 ? 1f : -1f, sy = i < 2 ? 1f : -1f;
                rect.anchoredPosition = new Vector2(sx * 13f, sy * 13f);
                rect.sizeDelta = new Vector2(3f, 11f);
                rect.localRotation = Quaternion.Euler(0f, 0f, sx * sy * -45f);
                var image = stroke.GetComponent<Image>();
                image.raycastTarget = false;
                image.enabled = false;
                markers.GetArrayElementAtIndex(i).objectReferenceValue = image;
                stroke.SetAsLastSibling();
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void BuildScope(Transform hud)
        {
            Transform existing = hud.Find("ScopeOverlay");
            if (existing != null)
                Object.DestroyImmediate(existing.gameObject);

            var overlay = new GameObject("ScopeOverlay",
                typeof(RectTransform), typeof(CanvasGroup), typeof(ScopeOverlayUI));
            overlay.layer = hud.gameObject.layer;
            var rect = (RectTransform)overlay.transform;
            rect.SetParent(hud, false);
            Stretch(rect);
            rect.SetSiblingIndex(1); // above the damage flash, below the HUD text and crosshair

            // The lens (a square as tall as the screen) and black bars either side of it.
            Image lens = Box(rect, "Lens", Vector2.zero, new Vector2(Lens, Lens), Color.white);
            lens.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(ScopeSprite);
            Image left = Box(rect, "Bar_Left", Vector2.zero, Vector2.zero, Color.black);
            left.rectTransform.anchorMin = new Vector2(0f, 0f);
            left.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            left.rectTransform.offsetMin = Vector2.zero;
            left.rectTransform.offsetMax = new Vector2(-Lens * 0.5f + 1f, 0f);
            Image right = Box(rect, "Bar_Right", Vector2.zero, Vector2.zero, Color.black);
            right.rectTransform.anchorMin = new Vector2(0.5f, 0f);
            right.rectTransform.anchorMax = new Vector2(1f, 1f);
            right.rectTransform.offsetMin = new Vector2(Lens * 0.5f - 1f, 0f);
            right.rectTransform.offsetMax = Vector2.zero;

            // Reticle: a fine cross, thick posts toward the edge, range dots, and a small red centre dot.
            Color ink = new Color(0f, 0f, 0f, 0.92f);
            float r = Lens * 0.485f;
            Box(rect, "Reticle_H", Vector2.zero, new Vector2(r * 1.2f, 2f), ink);
            Box(rect, "Reticle_V", Vector2.zero, new Vector2(2f, r * 1.2f), ink);
            foreach (Vector2 dir in new[] { Vector2.left, Vector2.right, Vector2.up, Vector2.down })
            {
                bool horizontal = dir.y == 0f;
                Vector2 size = horizontal ? new Vector2(r * 0.42f, 7f) : new Vector2(7f, r * 0.42f);
                Box(rect, "Post", dir * (r * 0.79f), size, ink);
                for (int i = 1; i <= 4; i++)
                    Box(rect, "Dot", dir * (i * r * 0.11f), new Vector2(6f, 6f), ink);
            }
            Box(rect, "Centre", Vector2.zero, new Vector2(4f, 4f), new Color(0.9f, 0.1f, 0.05f, 1f));
        }

        private static Image Box(RectTransform parent, string name, Vector2 position, Vector2 size, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.layer = parent.gameObject.layer;
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            var image = go.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }
    }
}
