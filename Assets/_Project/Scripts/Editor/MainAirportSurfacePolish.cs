using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace RunwayPanic.EditorTools
{
    /// <summary>One-time C1 scene integration using locally generated, tileable airport surface maps.</summary>
    public static class MainAirportSurfacePolish
    {
        private const string TextureFolder = "Assets/_Project/Art/Textures/WorldSurfaces";
        private const string MaterialFolder = "Assets/_Project/Art/Materials/WorldSurfaces";
        private const string PreviewFolder = "/private/tmp/RunwayPanic-C1";
        private static readonly string[] ShelterWalls = { "Front_Left", "Front_Right", "Door_Lintel", "Wall_Left", "Wall_Right", "Wall_Back" };
        private static readonly string[] HangarWalls = { "Wall_South", "Wall_West", "Side_Exit_Jamb_West", "Side_Exit_Jamb_East", "Side_Exit_Lintel", "Front_Jamb_North", "Front_Jamb_South", "Front_Lintel", "Roof" };

        [MenuItem("Tools/Runway Panic/Apply Main Airport Surface Polish")]
        private static void Apply()
        {
            var scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || scene.path != MainPlayerIntegration.ScenePath || scene.isDirty)
                throw new InvalidOperationException("Open saved Main outside Play mode first.");
            if (AssetDatabase.IsValidFolder(TextureFolder) || AssetDatabase.IsValidFolder(MaterialFolder) || GameObject.Find("C1_TaxiwayMarkings") != null)
                throw new InvalidOperationException("C1 assets or markings already exist; inspect before applying again.");

            var runway = Require("Environment/Runway");
            var buildings = Require("Environment/Buildings");
            var shelterA = Require("Environment/Buildings/Shelter_A");
            var shelterB = Require("Environment/Buildings/Shelter_B");
            var hangar = Require("Environment/Buildings/Hangar_Greybox");
            var hangarShell = hangar.transform.Find("P_Greybox_Hangar");
            if (hangarShell == null) throw new InvalidOperationException("Expected existing hangar shell.");
            foreach (string part in ShelterWalls)
                if (shelterA.transform.Find(part) == null || shelterB.transform.Find(part) == null) throw new InvalidOperationException("Missing shelter part " + part);
            foreach (string part in HangarWalls.Concat(new[] { "Floor", "Entrance_Apron" }))
                if (hangarShell.Find(part) == null) throw new InvalidOperationException("Missing hangar part " + part);

            Capture("before");
            AssetDatabase.CreateFolder("Assets/_Project/Art/Textures", "WorldSurfaces");
            AssetDatabase.CreateFolder("Assets/_Project/Art/Materials", "WorldSurfaces");
            Texture2D asphalt = CreateTexture("T_Asphalt", 11, 0);
            Texture2D concrete = CreateTexture("T_Concrete", 23, 1);
            Texture2D slabs = CreateTexture("T_ConcreteSlabs", 29, 4);
            Texture2D metal = CreateTexture("T_PaintedMetal", 37, 2);
            Texture2D paint = CreateTexture("T_Paint", 47, 3);

            Material runwayMat = CreateMaterial("M_Runway_Asphalt", asphalt, new Color(.29f, .31f, .32f), new Vector2(3.5f, 18.75f), .1f, 0);
            Material apronMat = CreateMaterial("M_Apron_Concrete", slabs, new Color(.68f, .69f, .65f), new Vector2(5f, 3.5f), .12f, 0);
            Material groundMat = CreateMaterial("M_Airfield_Ground", concrete, new Color(.38f, .40f, .34f), new Vector2(25f, 20f), .08f, 0);
            Material wallMat = CreateMaterial("M_Building_Concrete", concrete, new Color(.73f, .73f, .68f), new Vector2(2f, 1f), .18f, 0);
            Material metalMat = CreateMaterial("M_Hangar_Metal", metal, new Color(.52f, .57f, .58f), new Vector2(2f, 1f), .3f, .2f);
            Material whiteMat = CreateMaterial("M_Runway_Paint", paint, new Color(.94f, .94f, .87f), Vector2.one, .16f, 0);
            Material yellowMat = CreateMaterial("M_Taxiway_Paint", paint, new Color(.95f, .70f, .18f), Vector2.one, .16f, 0);

            Undo.IncrementCurrentGroup();
            int undo = Undo.GetCurrentGroup();
            try
            {
                Assign(Require("Environment/Ground_100x80"), groundMat);
                Assign(Require("Environment/Runway/Runway_75x14"), runwayMat);
                Assign(Require("Environment/Runway/Apron_A"), apronMat);
                Assign(Require("Environment/Runway/Apron_B"), apronMat);
                Assign(Require("Environment/Background/Terminal_Placeholder"), wallMat);
                Assign(Require("Environment/Background/TerminalRoof"), metalMat);
                foreach (var shelter in new[] { shelterA, shelterB })
                {
                    foreach (string part in ShelterWalls) Assign(shelter.transform.Find(part).gameObject, wallMat);
                    Assign(shelter.transform.Find("Floor").gameObject, apronMat);
                    Assign(shelter.transform.Find("Roof").gameObject, metalMat);
                }
                foreach (string part in HangarWalls) Assign(hangarShell.Find(part).gameObject, metalMat);
                Assign(hangarShell.Find("Floor").gameObject, apronMat);
                Assign(hangarShell.Find("Entrance_Apron").gameObject, apronMat);
                foreach (Transform mark in runway.transform)
                {
                    if (mark.name.StartsWith("CentreStripe_") || mark.name.StartsWith("Threshold_") || mark.name.StartsWith("RunwayEdge_"))
                    {
                        Assign(mark.gameObject, whiteMat);
                        if (mark.name.StartsWith("RunwayEdge_"))
                        {
                            Undo.RecordObject(mark, "Extend runway edge paint");
                            var scale = mark.localScale;
                            scale.z = 73f;
                            mark.localScale = scale;
                        }
                    }
                }
                AddTaxiwayMarks(runway.transform, yellowMat);
                EditorSceneManager.MarkSceneDirty(scene);
                AssetDatabase.SaveAssets();
                if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Could not save Main scene.");
                Capture("after");
                Debug.Log("[AirportC1] Saved surface materials, extended runway edge paint and eight non-colliding taxiway dashes. Preview: " + PreviewFolder);
            }
            catch { Undo.RevertAllDownToGroup(undo); throw; }
            finally { Undo.CollapseUndoOperations(undo); }
        }

        private static GameObject Require(string path)
        {
            var item = GameObject.Find(path);
            if (item == null) throw new InvalidOperationException("Missing expected object " + path);
            return item;
        }

        private static void Assign(GameObject obj, Material material)
        {
            var renderer = obj.GetComponent<MeshRenderer>();
            if (renderer == null) throw new InvalidOperationException("Missing renderer on " + obj.name);
            Undo.RecordObject(renderer, "Apply airport surface");
            renderer.sharedMaterial = material;
            PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
        }

        private static void AddTaxiwayMarks(Transform runway, Material material)
        {
            var root = new GameObject("C1_TaxiwayMarkings");
            Undo.RegisterCreatedObjectUndo(root, "Add taxiway paint");
            root.transform.SetParent(runway, false);
            var cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            if (cube == null) throw new InvalidOperationException("Unity cube mesh is unavailable.");
            for (int side = -1; side <= 1; side += 2)
            for (int i = 0; i < 4; i++)
            {
                var mark = new GameObject((side < 0 ? "ApronA" : "ApronB") + "_TaxiDash_" + (i + 1));
                mark.transform.SetParent(root.transform, false);
                mark.transform.localPosition = new Vector3(side * (11f + i * 4f), .052f, side < 0 ? 17f : -17f);
                mark.transform.localScale = new Vector3(2f, .006f, .14f);
                mark.AddComponent<MeshFilter>().sharedMesh = cube;
                var renderer = mark.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
        }

        // Five original, deterministic grayscale albedo maps; every noise octave wraps at its edges.
        private static Texture2D CreateTexture(string name, int seed, int kind)
        {
            const int size = 256;
            var texture = new Texture2D(size, size, TextureFormat.RGB24, true);
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (float)x / size, v = (float)y / size;
                float coarse = Noise(u, v, 8, seed), medium = Noise(u, v, 32, seed + 7), fine = Noise(u, v, 128, seed + 13);
                float value = kind == 0 ? .69f + .12f * coarse + .10f * medium + .09f * fine :
                              kind == 1 || kind == 4 ? .75f + .13f * coarse + .08f * medium + .04f * fine :
                              kind == 2 ? .82f + .07f * coarse + .06f * medium + .05f * fine :
                                          .87f + .04f * coarse + .04f * medium + .05f * fine;
                if (kind == 0 && fine > .82f) value += .045f;
                if (kind == 2 && (x % 32 < 2 || x % 32 > 29)) value *= .73f;
                if (kind == 3 && fine < .19f) value *= .78f;
                if (kind == 4 && (x < 3 || y < 3 || x > size - 3 || y > size - 3)) value *= .63f;
                byte c = (byte)Mathf.RoundToInt(Mathf.Clamp01(value) * 255f);
                pixels[y * size + x] = new Color32(c, c, c, 255);
            }
            texture.SetPixels32(pixels);
            texture.Apply();
            string path = TextureFolder + "/" + name + ".png";
            File.WriteAllBytes(path, texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.mipmapEnabled = true;
            importer.maxTextureSize = size;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private static float Noise(float u, float v, int period, int seed)
        {
            float px = u * period, py = v * period;
            int ix = Mathf.FloorToInt(px), iy = Mathf.FloorToInt(py);
            float fx = px - ix, fy = py - iy;
            fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
            return Mathf.Lerp(Mathf.Lerp(Hash(ix % period, iy % period, seed), Hash((ix + 1) % period, iy % period, seed), fx),
                Mathf.Lerp(Hash(ix % period, (iy + 1) % period, seed), Hash((ix + 1) % period, (iy + 1) % period, seed), fx), fy);
        }

        private static float Hash(int x, int y, int seed)
        {
            unchecked { uint n = (uint)(x * 374761393 + y * 668265263 + seed * 1442695041); n = (n ^ (n >> 13)) * 1274126177u; return (n & 0xffff) / 65535f; }
        }

        private static Material CreateMaterial(string name, Texture2D texture, Color color, Vector2 scale, float smoothness, float metallic)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null || texture == null) throw new InvalidOperationException("URP Lit shader or surface texture missing.");
            var material = new Material(shader) { name = name };
            material.SetTexture("_BaseMap", texture);
            material.SetTextureScale("_BaseMap", scale);
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", smoothness);
            material.SetFloat("_Metallic", metallic);
            AssetDatabase.CreateAsset(material, MaterialFolder + "/" + name + ".mat");
            return material;
        }

        [MenuItem("Tools/Runway Panic/Capture Airport Surface Views")]
        private static void CaptureViews()
        {
            var scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || scene.path != MainPlayerIntegration.ScenePath || scene.isDirty)
                throw new InvalidOperationException("Open saved Main outside Play mode first.");
            Capture("review");
            Debug.Log("[AirportC1] Captured airport views to " + PreviewFolder);
        }

        private static void Capture(string stage)
        {
            Directory.CreateDirectory(PreviewFolder);
            RenderView(stage + "_runway", new Vector3(0, 5, -30), new Vector3(0, .5f, 3));
            RenderView(stage + "_shelter", new Vector3(-9, 4, 20), new Vector3(-21, 1.7f, 12));
            RenderView(stage + "_hangar", new Vector3(-22, 5, -21), new Vector3(-38, 2.5f, -13));
        }

        private static void RenderView(string name, Vector3 position, Vector3 target)
        {
            var go = new GameObject("C1_Preview_TEMP") { hideFlags = HideFlags.HideAndDontSave };
            var camera = go.AddComponent<Camera>();
            camera.enabled = false;
            camera.fieldOfView = 60;
            camera.farClipPlane = 300;
            camera.transform.position = position;
            camera.transform.LookAt(target);
            go.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing = true;
            var previous = RenderTexture.active;
            var targetTexture = RenderTexture.GetTemporary(960, 540, 24, RenderTextureFormat.Default);
            var image = new Texture2D(960, 540, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = targetTexture;
                camera.Render();
                RenderTexture.active = targetTexture;
                image.ReadPixels(new Rect(0, 0, 960, 540), 0, 0);
                image.Apply();
                File.WriteAllBytes(Path.Combine(PreviewFolder, name + ".png"), image.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previous;
                camera.targetTexture = null;
                RenderTexture.ReleaseTemporary(targetTexture);
                UnityEngine.Object.DestroyImmediate(image);
                UnityEngine.Object.DestroyImmediate(go);
            }
        }
    }
}
