using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RunwayPanic.EditorTools
{
    /// <summary>Paint only outward wall faces, with independent, Inspector-editable shelter materials.</summary>
    public static class ShelterExteriorPaint
    {
        private const string Materials = "Assets/_Project/Art/Materials/WorldSurfaces/";
        private const string Meshes = "Assets/_Project/Art/Models/Environment/ShelterPaint";
        private static readonly string[] Walls = { "Wall_Back", "Wall_Left", "Wall_Right", "Front_Left", "Front_Right", "Door_Lintel" };

        [MenuItem("Tools/Runway Panic/Paint Shelter Exterior Walls")]
        private static void Apply()
        {
            var scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || scene.path != MainPlayerIntegration.ScenePath || scene.isDirty)
                throw new InvalidOperationException("Open saved Main outside Play mode first.");
            var roots = new[] { GameObject.Find("Environment/Buildings/Shelter_A"), GameObject.Find("Environment/Buildings/Shelter_B") };
            var concrete = AssetDatabase.LoadAssetAtPath<Material>(Materials + "M_Building_Concrete.mat");
            if (concrete == null) throw new InvalidOperationException("Expected existing concrete material.");
            foreach (var root in roots)
            {
                if (root == null) throw new InvalidOperationException("Both shelter instances must be present.");
                foreach (string name in Walls)
                {
                    var wall = root.transform.Find(name);
                    var filter = wall != null ? wall.GetComponent<MeshFilter>() : null;
                    if (filter == null || filter.sharedMesh == null || wall.GetComponent<MeshRenderer>() == null ||
                        filter.sharedMesh.vertexCount != 24 || filter.sharedMesh.triangles.Length != 36)
                        throw new InvalidOperationException("Expected a simple cube wall: " + root.name + "/" + name);
                }
            }
            if (!AssetDatabase.IsValidFolder(Meshes))
                AssetDatabase.CreateFolder("Assets/_Project/Art/Models/Environment", "ShelterPaint");
            var paints = new[]
            {
                Paint("M_Shelter_A_Exterior_FadedBlue", new Color32(84, 110, 136, 255)),
                Paint("M_Shelter_B_Exterior_LightBrickRed", new Color32(221, 176, 165, 255))
            };
            Undo.SetCurrentGroupName("Paint shelter exterior faces");
            for (int i = 0; i < roots.Length; i++)
                foreach (string name in Walls)
                {
                    var wall = roots[i].transform.Find(name);
                    var filter = wall.GetComponent<MeshFilter>();
                    var renderer = wall.GetComponent<MeshRenderer>();
                    Vector3 outside = name == "Wall_Back" ? Vector3.forward : name == "Wall_Left" ? Vector3.left :
                        name == "Wall_Right" ? Vector3.right : Vector3.back;
                    Undo.RecordObjects(new UnityEngine.Object[] { filter, renderer }, "Assign exterior paint");
                    filter.sharedMesh = SplitOutsideFace(filter.sharedMesh, outside);
                    renderer.sharedMaterials = new[] { concrete, paints[i] };
                    PrefabUtility.RecordPrefabInstancePropertyModifications(filter);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
                }
            EditorSceneManager.MarkSceneDirty(scene);
            AssetDatabase.SaveAssets();
            if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Could not save painted shelters in Main.");
            Debug.Log("[ShelterPaint] Painted 12 exterior wall faces. Interiors, roofs, doors, colliders and navigation unchanged. Edit each material's Base Map colour to recolour it; rerunning this tool retains edited colours.");

            Material Paint(string name, Color colour)
            {
                string path = Materials + name + ".mat";
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material != null) return material; // Owner-edited colours must not reset.
                material = new Material(concrete) { name = name };
                material.SetColor("_BaseColor", colour);
                if (material.HasProperty("_Color")) material.SetColor("_Color", colour);
                AssetDatabase.CreateAsset(material, path);
                return material;
            }
        }

        [MenuItem("Tools/Runway Panic/Fix Shelter Corner Overlap")]
        private static void FixCornerOverlap()
        {
            var scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || scene.path != MainPlayerIntegration.ScenePath || scene.isDirty)
                throw new InvalidOperationException("Open saved Main outside Play mode first.");
            var sides = new List<Transform>();
            var lengths = new List<float>();
            var centres = new List<float>();
            foreach (string name in new[] { "Shelter_A", "Shelter_B" })
            {
                var shelter = GameObject.Find("Environment/Buildings/" + name);
                if (shelter == null) throw new InvalidOperationException("Missing " + name);
                var front = shelter.transform.Find("Front_Left");
                var back = shelter.transform.Find("Wall_Back");
                if (front == null || back == null)
                    throw new InvalidOperationException("Expected existing front/back walls.");
                float start = front.localPosition.z + front.localScale.z * .5f;
                float end = back.localPosition.z - back.localScale.z * .5f;
                if (end <= start) throw new InvalidOperationException("Invalid shelter wall span.");
                foreach (string side in new[] { "Wall_Left", "Wall_Right" })
                {
                    var wall = shelter.transform.Find(side);
                    if (wall == null || Quaternion.Angle(wall.localRotation, Quaternion.identity) > .01f ||
                        wall.GetComponent<BoxCollider>() == null)
                        throw new InvalidOperationException("Expected axis-aligned cube side wall.");
                    sides.Add(wall);
                    lengths.Add(end - start);
                    centres.Add((start + end) * .5f);
                }
            }
            Undo.RecordObjects(sides.ToArray(), "Remove shelter corner overlap");
            for (int i = 0; i < sides.Count; i++)
            {
                var scale = sides[i].localScale;
                scale.z = lengths[i];
                sides[i].localScale = scale;
                var position = sides[i].localPosition;
                position.z = centres[i];
                sides[i].localPosition = position;
                PrefabUtility.RecordPrefabInstancePropertyModifications(sides[i]);
                // The front/back walls fill the trimmed overlap, preserving the solid outer envelope.
            }
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Could not save shelter corner fix.");
            Debug.Log("[ShelterPaint] Fixed eight corners: four side walls now meet the front/back inner faces (7.2m rather than 7.6m). No overlapping exterior faces; paint, doors and outer building envelope retained.");
        }

        [MenuItem("Tools/Runway Panic/Preview Shelter Exterior Paint")]
        private static void Preview()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || SceneManager.GetActiveScene().path != MainPlayerIntegration.ScenePath)
                throw new InvalidOperationException("Open Main outside Play mode first.");
            foreach (string name in new[] { "Shelter_A", "Shelter_B" })
            {
                var root = GameObject.Find("Environment/Buildings/" + name).transform;
                TerminalFlowerDressing.CaptureView(root.TransformPoint(new Vector3(0, 2.8f, -13)),
                    root.TransformPoint(new Vector3(0, 1.8f, 0)), "/private/tmp/RunwayPanic-ShelterPaint-" + (name == "Shelter_A" ? "A" : "B") + ".png");
                var wall = root.Find("Front_Right").GetComponent<MeshRenderer>();
                Debug.Log("[ShelterPaint] " + name + " exterior=" + wall.sharedMaterials[1].name +
                    " tint=" + wall.sharedMaterials[1].GetColor("_BaseColor") + " submeshes=" + root.Find("Front_Right").GetComponent<MeshFilter>().sharedMesh.subMeshCount);
                TerminalFlowerDressing.CaptureView(root.TransformPoint(new Vector3(-11, 2.8f, -10)),
                    root.TransformPoint(new Vector3(-3, 1.8f, -2)), "/private/tmp/RunwayPanic-ShelterCorner-" + (name == "Shelter_A" ? "A" : "B") + ".png");
            }
        }

        private static Mesh SplitOutsideFace(Mesh source, Vector3 outward)
        {
            string side = outward == Vector3.left ? "Left" : outward == Vector3.right ? "Right" :
                outward == Vector3.forward ? "Back" : "Front";
            string path = Meshes + "/CubePaint_" + side + ".asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null) return existing;
            var rest = new List<int>();
            var exterior = new List<int>();
            var indices = source.triangles;
            var normals = source.normals;
            for (int i = 0; i < indices.Length; i += 3)
            {
                var target = Vector3.Dot(normals[indices[i]], outward) > .99f ? exterior : rest;
                target.Add(indices[i]); target.Add(indices[i + 1]); target.Add(indices[i + 2]);
            }
            if (exterior.Count != 6 || rest.Count != 30)
                throw new InvalidOperationException("Expected exactly one outward cube face.");
            var mesh = UnityEngine.Object.Instantiate(source);
            mesh.name = "CubePaint_" + side;
            mesh.subMeshCount = 2;
            mesh.SetTriangles(rest, 0);
            mesh.SetTriangles(exterior, 1);
            // Vertices, UV/UV2, normals and physical dimensions remain those of the original cube.
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }
    }
}
