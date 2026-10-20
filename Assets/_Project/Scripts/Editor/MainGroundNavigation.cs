using System;
using System.IO;
using System.Linq;
using RunwayPanic.Interaction;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace RunwayPanic.EditorTools
{
    /// <summary>One-time Main ground navigation setup using the environment and explicit dynamic exclusions.</summary>
    public static class MainGroundNavigation
    {
        private const string ScenePath = "Assets/_Project/Scenes/Main.unity";
        private const string DataPath = "Assets/_Project/Navigation/MainGroundNavMesh.asset";

        private static Collider[] Sources()
        {
            var environment = GameObject.Find("Environment");
            if (environment == null) throw new InvalidOperationException("Main Environment was not found.");
            return environment.GetComponentsInChildren<Collider>()
                .Where(c => c.enabled && !c.isTrigger && c.gameObject.activeInHierarchy &&
                            c.GetComponentInParent<Rigidbody>() == null &&
                            c.GetComponentInParent<NavMeshObstacle>() == null &&
                            c.GetComponentInParent<Door>() == null)
                .ToArray();
        }

        private static GameObject[] Exclusions()
        {
            var environment = GameObject.Find("Environment");
            return environment.GetComponentsInChildren<Collider>()
                .Where(c => c.enabled && c.gameObject.activeInHierarchy &&
                            (c.isTrigger || c.GetComponentInParent<Rigidbody>() != null ||
                             c.GetComponentInParent<NavMeshObstacle>() != null || c.GetComponentInParent<Door>() != null))
                .Select(c => c.GetComponentInParent<Door>()?.gameObject ??
                             c.GetComponentInParent<NavMeshObstacle>()?.gameObject ??
                             c.GetComponentInParent<Rigidbody>()?.gameObject ?? c.gameObject)
                .Distinct().ToArray();
        }

        [MenuItem("Tools/Runway Panic/Inspect Main Ground Navigation Sources")]
        public static void Inspect()
        {
            var sources = Sources();
            Debug.Log("[GroundNav] Static colliders: " + sources.Length + "; excluded roots:\n" +
                      string.Join("\n", Exclusions().Select(c => PathOf(c.transform))));
        }

        [MenuItem("Tools/Runway Panic/Bake Main Ground Navigation")]
        public static void Bake()
        {
            var scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || scene.path != ScenePath || scene.isDirty)
                throw new InvalidOperationException("Open saved Main outside Play mode first.");
            if (GameObject.Find("GroundNavigation") != null || File.Exists(DataPath) || AssetDatabase.LoadMainAssetAtPath(DataPath) != null ||
                UnityEngine.Object.FindObjectsByType<NavMeshModifier>().Length != 0)
                throw new InvalidOperationException("Existing navigation setup found; inspect it before rebaking.");
            var sources = Sources();
            if (sources.Length == 0 || !sources.Any(c => c.name == "Ground_100x80"))
                throw new InvalidOperationException("Expected airport ground collider not found.");

            var include = Undo.AddComponent<NavMeshModifier>(GameObject.Find("Environment"));
            include.applyToChildren = true;
            foreach (var excluded in Exclusions())
            {
                var modifier = Undo.AddComponent<NavMeshModifier>(excluded);
                modifier.ignoreFromBuild = true;
                modifier.applyToChildren = true;
            }
            var root = new GameObject("GroundNavigation");
            Undo.RegisterCreatedObjectUndo(root, "Create ground navigation");
            var surface = root.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.MarkedWithModifier;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.agentTypeID = 0;
            surface.BuildNavMesh();
            if (surface.navMeshData == null) throw new InvalidOperationException("Ground NavMesh build failed.");
            if (!AssetDatabase.IsValidFolder("Assets/_Project/Navigation"))
                AssetDatabase.CreateFolder("Assets/_Project", "Navigation");
            AssetDatabase.CreateAsset(surface.navMeshData, DataPath);
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Could not save Main.");
            Debug.Log($"[GroundNav] Baked and saved ground navigation from {sources.Length} static colliders: {DataPath}");
        }

        [MenuItem("Tools/Runway Panic/Check Main Ground Navigation")]
        public static void Check()
        {
            var surface = GameObject.Find("GroundNavigation")?.GetComponent<NavMeshSurface>();
            if (surface == null || surface.navMeshData == null)
                throw new InvalidOperationException("Ground navigation is not present.");
            bool Sample(Vector3 p) => NavMesh.SamplePosition(p, out _, 0.3f, NavMesh.AllAreas);
            string Route(Vector3 a, Vector3 b)
            {
                var path = new NavMeshPath();
                bool ok = NavMesh.SamplePosition(a, out var start, 0.5f, NavMesh.AllAreas) &&
                          NavMesh.SamplePosition(b, out var end, 0.5f, NavMesh.AllAreas) &&
                          NavMesh.CalculatePath(start.position, end.position, NavMesh.AllAreas, path);
                return ok ? path.status.ToString() : "No sample/path";
            }
            Debug.Log("[GroundNavCheck] runway=" + Sample(new Vector3(0, 0, -24)) +
                      "; shelter A floor=" + Sample(new Vector3(-21, 0, 12)) +
                      "; shelter B floor=" + Sample(new Vector3(21, 0, -12)) +
                      "; shelter A back wall=" + Sample(new Vector3(-24.8f, 0, 12)) +
                      "; A route=" + Route(new Vector3(-14, 0, 12), new Vector3(-21, 0, 12)) +
                      "; B route=" + Route(new Vector3(14, 0, -12), new Vector3(21, 0, -12)));
        }

        private static string PathOf(Transform item)
        {
            string path = item.name;
            while (item.parent != null) { item = item.parent; path = item.name + "/" + path; }
            return path;
        }
    }
}
