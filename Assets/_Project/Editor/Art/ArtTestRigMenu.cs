using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace RunwayPanic.ArtTools
{
    /// <summary>
    /// Tools > S3 > Create Art Test Rig: adds scale and orientation references to the open sandbox scene
    /// (20 m ground, 1.8 m player capsule, 1 m cube, +Z forward / +X right arrows). Tagged EditorOnly,
    /// so it never ends up in a build. Running it again replaces the existing rig.
    /// </summary>
    public static class ArtTestRigMenu
    {
        const string RigName = "_ArtTestRig";
        const float PlayerHeight = 1.8f;

        [MenuItem("Tools/S3/Create Art Test Rig")]
        public static void CreateRig()
        {
            var existing = GameObject.Find(RigName);
            if (existing != null) Undo.DestroyObjectImmediate(existing);

            var root = new GameObject(RigName) { tag = "EditorOnly" };
            Undo.RegisterCreatedObjectUndo(root, "Create Art Test Rig");

            var grey = CreateMaterial("Rig_Grey", new Color(0.55f, 0.55f, 0.55f));
            var orange = CreateMaterial("Rig_Orange", new Color(1f, 0.55f, 0.1f));
            var blue = CreateMaterial("Rig_Blue", new Color(0.15f, 0.35f, 1f));
            var red = CreateMaterial("Rig_Red", new Color(1f, 0.15f, 0.15f));

            // Unity's Plane is 10x10 m at scale 1.
            Create(PrimitiveType.Plane, "Ground_20m", root, Vector3.zero, Quaternion.identity, new Vector3(2f, 1f, 2f), grey, true);

            // Unity's Capsule is 2 m tall at scale 1, so Y scale 0.9 = 1.8 m. Feet on the ground.
            Create(PrimitiveType.Capsule, "PlayerScale_1.8m", root, new Vector3(-1.5f, PlayerHeight * 0.5f, 0f),
                Quaternion.identity, new Vector3(0.5f, PlayerHeight * 0.5f, 0.5f), orange, true);

            Create(PrimitiveType.Cube, "Ref_1m_Cube", root, new Vector3(1.5f, 0.5f, 0f),
                Quaternion.identity, Vector3.one, grey, true);

            // +Z = model forward (a bird's beak / the player's face must point along this arrow).
            Create(PrimitiveType.Cube, "Arrow_+Z_Forward", root, new Vector3(0f, 0.01f, 1f),
                Quaternion.identity, new Vector3(0.05f, 0.02f, 2f), blue, false);
            Create(PrimitiveType.Cube, "Arrow_+Z_Head", root, new Vector3(0f, 0.01f, 2f),
                Quaternion.Euler(0f, 45f, 0f), new Vector3(0.25f, 0.02f, 0.25f), blue, false);
            Create(PrimitiveType.Cube, "Arrow_+X_Right", root, new Vector3(0.5f, 0.01f, 0f),
                Quaternion.identity, new Vector3(1f, 0.02f, 0.05f), red, false);

            Selection.activeGameObject = root;
            EditorSceneManager.MarkSceneDirty(root.scene);
        }

        static Material CreateMaterial(string name, Color color)
        {
            var pipelineMaterial = GraphicsSettings.currentRenderPipeline != null
                ? GraphicsSettings.currentRenderPipeline.defaultMaterial
                : null;
            var material = pipelineMaterial != null
                ? new Material(pipelineMaterial)
                : new Material(Shader.Find("Universal Render Pipeline/Lit"));
            material.name = name;
            material.color = color; // URP Lit marks _BaseColor as [MainColor]
            return material;
        }

        static void Create(PrimitiveType type, string name, GameObject parent, Vector3 position,
            Quaternion rotation, Vector3 scale, Material material, bool keepCollider)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.tag = "EditorOnly";
            go.transform.SetParent(parent.transform, false);
            go.transform.SetLocalPositionAndRotation(position, rotation);
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = material;
            if (!keepCollider) Object.DestroyImmediate(go.GetComponent<Collider>());
        }
    }
}
