using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace RunwayPanic.EditorTools
{
    /// <summary>One-time scene dressing; all new meshes are decorative and have no colliders.</summary>
    public static class MainAirportDressing
    {
        private const string Models = "Assets/_Project/Art/Models/Environment/C2Dressing";
        private const string Textures = "Assets/_Project/Art/Textures/WorldSurfaces";
        private const string Materials = "Assets/_Project/Art/Materials/WorldSurfaces";
        private const string RootName = "C2_AirportDressing";

        [MenuItem("Tools/Runway Panic/Apply Airport Dressing")]
        public static void Apply()
        {
            var scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || scene.path != MainPlayerIntegration.ScenePath || scene.isDirty)
                throw new InvalidOperationException("Open saved Main outside Play mode first.");
            if (AssetDatabase.IsValidFolder(Models) || GameObject.Find(RootName) != null || AssetDatabase.LoadAssetAtPath<Material>(Materials + "/M_C2_GrassGround.mat") != null)
                throw new InvalidOperationException("C2 dressing already exists; inspect it rather than creating duplicates.");
            var environment = Require("Environment");
            var ground = Require("Environment/Ground_100x80").GetComponent<MeshRenderer>();
            var buildings = Require("Environment/Buildings");
            var aircraft = Require("Environment/Aircraft");
            var terminal = Require("Environment/Background/Terminal_Placeholder");
            var tower = Require("Environment/Background/ControlTower_Placeholder");
            var apronA = Require("Environment/Runway/Apron_A").GetComponent<MeshRenderer>();
            var apronB = Require("Environment/Runway/Apron_B").GetComponent<MeshRenderer>();
            if (ground == null || apronA == null || apronB == null) throw new InvalidOperationException("Expected ground and apron renderers.");
            if (!AssetDatabase.IsValidFolder(Textures) || !AssetDatabase.IsValidFolder(Materials)) throw new InvalidOperationException("C1 surface folders are missing.");

            var excluded = GrassExclusions(buildings.transform, aircraft.transform, terminal.transform, tower.transform, apronA, apronB);

            AssetDatabase.CreateFolder("Assets/_Project/Art/Models/Environment", "C2Dressing");
            Texture2D grassTexture = CreateGrassTexture();
            Material grassGround = CreateMaterial("M_C2_GrassGround", new Color(.32f, .40f, .23f), grassTexture, new Vector2(25, 20), false);
            Material blades = CreateMaterial("M_C2_GrassBlades", new Color(.30f, .40f, .20f), null, Vector2.one, true);

            Undo.IncrementCurrentGroup();
            int undo = Undo.GetCurrentGroup();
            try
            {
                Undo.RecordObject(ground, "Texture the airport grass ground");
                ground.sharedMaterial = grassGround;
                var root = new GameObject(RootName);
                Undo.RegisterCreatedObjectUndo(root, "Add airport dressing");
                root.transform.SetParent(environment.transform, false);
                MakeGrass(root.transform, blades, excluded);
                EditorSceneManager.MarkSceneDirty(scene);
                AssetDatabase.SaveAssets();
                if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Could not save Main scene.");
                try { CapturePreview(); } catch (Exception e) { Debug.LogWarning("[AirportC2] Preview failed: " + e.Message); }
                Debug.Log("[AirportC2] Saved grass in Main. Preview: /private/tmp/RunwayPanic-C2/airport.png");
            }
            catch { Undo.RevertAllDownToGroup(undo); throw; }
            finally { Undo.CollapseUndoOperations(undo); }
        }

        private static GameObject Require(string path)
        {
            var go = GameObject.Find(path);
            if (go == null) throw new InvalidOperationException("Missing expected scene object: " + path);
            return go;
        }

        [MenuItem("Tools/Runway Panic/Add Hangar Entrance Trim")]
        private static void AddHangarTrim()
        {
            var scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || scene.path != MainPlayerIntegration.ScenePath || scene.isDirty)
                throw new InvalidOperationException("Open saved Main outside Play mode first.");
            var parent = Require("Environment/" + RootName).transform;
            if (parent.Find("Hangar_Entrance_Trim") != null) throw new InvalidOperationException("Hangar trim already exists.");
            var hangar = Require("Environment/Buildings/Hangar_Greybox/P_Greybox_Hangar").transform;
            var paint = AssetDatabase.LoadAssetAtPath<Material>(Materials + "/M_Taxiway_Paint.mat");
            var cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            if (paint == null || cube == null) throw new InvalidOperationException("Airport paint or cube mesh is missing.");
            var root = new GameObject("Hangar_Entrance_Trim");
            Undo.RegisterCreatedObjectUndo(root, "Add hangar entrance trim");
            root.transform.SetParent(parent, false);
            root.transform.SetPositionAndRotation(hangar.position, hangar.rotation);
            AddMesh(root.transform, "Entrance_Left_Edge", cube, paint, new Vector3(7.025f, 2.42f, -5.10f), new Vector3(.04f, 4.64f, .12f), false);
            AddMesh(root.transform, "Entrance_Right_Edge", cube, paint, new Vector3(7.025f, 2.42f, 5.10f), new Vector3(.04f, 4.64f, .12f), false);
            AddMesh(root.transform, "Entrance_Top_Edge", cube, paint, new Vector3(7.025f, 4.78f, 0), new Vector3(.04f, .08f, 10.3f), false);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Could not save Main scene.");
            try { CapturePreview(); } catch (Exception e) { Debug.LogWarning("[AirportC2] Preview failed: " + e.Message); }
            Debug.Log("[AirportC2] Saved non-colliding hangar entrance trim.");
        }

        private static void AddBounds(List<Bounds> bounds, Renderer renderer, float margin)
        {
            if (renderer == null) return;
            var b = renderer.bounds; b.Expand(new Vector3(margin * 2, 0, margin * 2)); bounds.Add(b);
        }

        private static void AddCombinedBounds(List<Bounds> bounds, Transform root, float margin)
        {
            var renderers = root.GetComponentsInChildren<MeshRenderer>(true);
            if (renderers.Length == 0) return;
            var b = renderers[0].bounds;
            foreach (var r in renderers) b.Encapsulate(r.bounds);
            b.Expand(new Vector3(margin * 2, 0, margin * 2)); bounds.Add(b);
        }

        private static bool Clear(float x, float z, List<Bounds> excluded)
        {
            if (Mathf.Abs(x) < 7.25f) return false; // keep the 14 m-wide runway clear
            foreach (var b in excluded)
                if (x >= b.min.x && x <= b.max.x && z >= b.min.z && z <= b.max.z) return false;
            foreach (var hit in Physics.OverlapSphere(new Vector3(x, .12f, z), .18f, ~0, QueryTriggerInteraction.Ignore))
                if (hit.gameObject.name != "Ground_100x80") return false;
            return true;
        }

        private static void MakeGrass(Transform parent, Material material, List<Bounds> excluded)
        {
            var root = new GameObject("Grass_Patches").transform; root.SetParent(parent, false);
            BuildGrass(excluded, (name, mesh) =>
            {
                AssetDatabase.CreateAsset(mesh, Models + "/" + name + ".asset");
                AddMesh(root, name, mesh, material, Vector3.zero, Vector3.one, false);
            });
        }

        private static List<Bounds> GrassExclusions(Transform buildings, Transform aircraft, Transform terminal, Transform tower, MeshRenderer apronA, MeshRenderer apronB)
        {
            var excluded = new List<Bounds>();
            AddBounds(excluded, apronA, .6f); AddBounds(excluded, apronB, .6f);
            foreach (Transform item in buildings) AddCombinedBounds(excluded, item, .8f);
            foreach (Transform item in aircraft) AddCombinedBounds(excluded, item, .8f);
            AddCombinedBounds(excluded, terminal, .8f); AddCombinedBounds(excluded, tower, .8f);
            return excluded;
        }

        [MenuItem("Tools/Runway Panic/Refresh Airport Grass")]
        private static void RefreshGrass()
        {
            var scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || scene.path != MainPlayerIntegration.ScenePath || scene.isDirty)
                throw new InvalidOperationException("Open saved Main outside Play mode first.");
            var apronA = Require("Environment/Runway/Apron_A").GetComponent<MeshRenderer>();
            var apronB = Require("Environment/Runway/Apron_B").GetComponent<MeshRenderer>();
            if (apronA == null || apronB == null) throw new InvalidOperationException("Expected apron renderers.");
            var excluded = GrassExclusions(Require("Environment/Buildings").transform, Require("Environment/Aircraft").transform,
                Require("Environment/Background/Terminal_Placeholder").transform, Require("Environment/Background/ControlTower_Placeholder").transform, apronA, apronB);
            var existing = new Dictionary<string, Mesh>();
            for (int cx = 0; cx < 4; cx++) for (int cz = 0; cz < 4; cz++)
            {
                string name = "GrassChunk_" + cx + "_" + cz;
                var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(Models + "/" + name + ".asset");
                if (mesh == null) throw new InvalidOperationException("Missing existing grass mesh: " + name);
                existing.Add(name, mesh);
            }
            var groundTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(Textures + "/T_AnimeNature_GrassGround.png");
            var grassTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(Textures + "/T_TerrainGrass_Billboard.png");
            var ground = Require("Environment/Ground_100x80").GetComponent<MeshRenderer>();
            var grassRoot = Require("Environment/" + RootName + "/Grass_Patches");
            if (groundTexture == null || grassTexture == null || ground == null)
                throw new InvalidOperationException("Downloaded grass textures or ground renderer are missing.");
            var groundMaterial = AssetDatabase.LoadAssetAtPath<Material>(Materials + "/M_C2_AnimeGrassGround.mat");
            if (groundMaterial == null) groundMaterial = CreateMaterial("M_C2_AnimeGrassGround", new Color(.36f, .42f, .25f), groundTexture, new Vector2(25, 20), false);
            var grassMaterial = AssetDatabase.LoadAssetAtPath<Material>(Materials + "/M_C2_TerrainGrassCards.mat");
            if (grassMaterial == null)
            {
                grassMaterial = CreateMaterial("M_C2_TerrainGrassCards", new Color(.42f, .55f, .25f), grassTexture, Vector2.one, true);
                grassMaterial.SetFloat("_AlphaClip", 1);
                grassMaterial.SetFloat("_Cutoff", .35f);
                grassMaterial.EnableKeyword("_ALPHATEST_ON");
                grassMaterial.SetOverrideTag("RenderType", "TransparentCutout");
                grassMaterial.renderQueue = (int)RenderQueue.AlphaTest;
                EditorUtility.SetDirty(grassMaterial);
            }
            BuildGrass(excluded, (name, replacement) =>
            {
                var mesh = existing[name];
                Undo.RecordObject(mesh, "Refresh airport grass");
                mesh.Clear();
                mesh.vertices = replacement.vertices;
                mesh.triangles = replacement.triangles;
                mesh.normals = replacement.normals;
                mesh.uv = replacement.uv;
                mesh.RecalculateBounds();
                EditorUtility.SetDirty(mesh);
                UnityEngine.Object.DestroyImmediate(replacement);
            });
            Undo.RecordObject(ground, "Apply imported grass ground");
            ground.sharedMaterial = groundMaterial;
            foreach (var renderer in grassRoot.GetComponentsInChildren<MeshRenderer>(true))
            {
                Undo.RecordObject(renderer, "Apply imported grass cards");
                renderer.sharedMaterial = grassMaterial;
            }
            EditorSceneManager.MarkSceneDirty(scene);
            AssetDatabase.SaveAssets();
            if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Could not save Main scene.");
            try { CapturePreview(); } catch (Exception e) { Debug.LogWarning("[AirportC2] Preview failed: " + e.Message); }
            Debug.Log("[AirportC2] Refreshed airport grass with imported textures.");
        }

        private static void BuildGrass(List<Bounds> excluded, Action<string, Mesh> save)
        {
            var random = new System.Random(48162);
            for (int cx = 0; cx < 4; cx++) for (int cz = 0; cz < 4; cz++)
            {
                var vertices = new List<Vector3>(); var triangles = new List<int>(); var normals = new List<Vector3>(); var uvs = new List<Vector2>();
                float xmin = -49 + cx * 24.5f, zmin = -39 + cz * 19.5f;
                // Overlapping cards form a continuous field rather than isolated tufts.
                for (int x = 0; x < 68; x++) for (int z = 0; z < 54; z++)
                {
                    float px = xmin + (x + .3f + (float)random.NextDouble() * .4f) * (24.5f / 68f);
                    float pz = zmin + (z + .3f + (float)random.NextDouble() * .4f) * (19.5f / 54f);
                    if (!Clear(px, pz, excluded)) continue;
                    float height = .26f + (float)random.NextDouble() * .1f;
                    float half = .24f + (float)random.NextDouble() * .03f;
                    float angle = (float)random.NextDouble() * Mathf.PI;
                    for (int a = 0; a < 2; a++)
                    {
                        float theta = angle + a * Mathf.PI / 2;
                        var right = new Vector3(Mathf.Cos(theta), 0, Mathf.Sin(theta)) * half;
                        var centre = new Vector3(px, .008f, pz); int first = vertices.Count;
                        vertices.Add(centre - right); vertices.Add(centre + right);
                        vertices.Add(centre - right + Vector3.up * height); vertices.Add(centre + right + Vector3.up * height);
                        for (int i = 0; i < 4; i++) normals.Add(Vector3.up);
                        uvs.Add(new Vector2(0, 0)); uvs.Add(new Vector2(1, 0)); uvs.Add(new Vector2(0, 1)); uvs.Add(new Vector2(1, 1));
                        triangles.Add(first); triangles.Add(first + 2); triangles.Add(first + 1);
                        triangles.Add(first + 1); triangles.Add(first + 2); triangles.Add(first + 3);
                    }
                }
                if (vertices.Count == 0) continue;
                string meshName = "GrassChunk_" + cx + "_" + cz;
                var mesh = new Mesh { name = meshName };
                mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0); mesh.SetNormals(normals); mesh.SetUVs(0, uvs); mesh.RecalculateBounds();
                save(meshName, mesh);
            }
        }

        private static Texture2D CreateGrassTexture()
        {
            const int size = 256;
            var texture = new Texture2D(size, size, TextureFormat.RGB24, false);
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
            {
                float u = (float)x / size, v = (float)y / size;
                float value = .73f + .15f * Noise(u, v, 4, 11) + .08f * Noise(u, v, 16, 19) + .04f * Noise(u, v, 64, 31);
                byte c = (byte)Mathf.RoundToInt(value * 255); pixels[y * size + x] = new Color32(c, c, c, 255);
            }
            texture.SetPixels32(pixels); texture.Apply();
            string path = Textures + "/T_C2_GrassGround.png";
            File.WriteAllBytes(path, texture.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.wrapMode = TextureWrapMode.Repeat; importer.mipmapEnabled = true; importer.maxTextureSize = size; importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private static float Noise(float u, float v, int period, int seed)
        {
            float px = u * period, py = v * period;
            int x = Mathf.FloorToInt(px), y = Mathf.FloorToInt(py);
            float fx = px - x, fy = py - y;
            fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
            return Mathf.Lerp(Mathf.Lerp(Hash(x % period, y % period, seed), Hash((x + 1) % period, y % period, seed), fx),
                Mathf.Lerp(Hash(x % period, (y + 1) % period, seed), Hash((x + 1) % period, (y + 1) % period, seed), fx), fy);
        }

        private static float Hash(int x, int y, int seed)
        {
            unchecked { uint n = (uint)(x * 374761393 + y * 668265263 + seed * 1442695041); n = (n ^ (n >> 13)) * 1274126177u; return (n & 0xffff) / 65535f; }
        }

        private static Material CreateMaterial(string name, Color color, Texture texture, Vector2 scale, bool doubleSided)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) throw new InvalidOperationException("URP Lit shader missing.");
            var material = new Material(shader) { name = name };
            material.SetColor("_BaseColor", color); material.SetFloat("_Smoothness", .08f);
            if (texture != null) { material.SetTexture("_BaseMap", texture); material.SetTextureScale("_BaseMap", scale); }
            if (doubleSided) material.SetFloat("_Cull", 0);
            AssetDatabase.CreateAsset(material, Materials + "/" + name + ".mat"); return material;
        }

        private static Transform AddMesh(Transform parent, string name, Mesh mesh, Material material, Vector3 position, Vector3 scale, bool shadows = true)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false); go.transform.localPosition = position; go.transform.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material;
            if (!shadows) { renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false; }
            return go.transform;
        }

        [MenuItem("Tools/Runway Panic/Capture Airport Grass Preview")]
        private static void CapturePreview()
        {
            const string folder = "/private/tmp/RunwayPanic-C2"; Directory.CreateDirectory(folder);
            var go = new GameObject("C2_Preview_TEMP") { hideFlags = HideFlags.HideAndDontSave };
            var camera = go.AddComponent<Camera>(); camera.enabled = false; camera.fieldOfView = 60; camera.farClipPlane = 300;
            camera.transform.position = new Vector3(-10, 5, -27); camera.transform.LookAt(new Vector3(-20, .5f, 4));
            go.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing = true;
            var old = RenderTexture.active; var target = RenderTexture.GetTemporary(960, 540, 24);
            var image = new Texture2D(960, 540, TextureFormat.RGB24, false);
            try { camera.targetTexture = target; camera.Render(); RenderTexture.active = target; image.ReadPixels(new Rect(0, 0, 960, 540), 0, 0); image.Apply(); File.WriteAllBytes(folder + "/airport.png", image.EncodeToPNG()); }
            finally { RenderTexture.active = old; camera.targetTexture = null; RenderTexture.ReleaseTemporary(target); UnityEngine.Object.DestroyImmediate(image); UnityEngine.Object.DestroyImmediate(go); }
        }
    }
}
