using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace RunwayPanic.ArtTools
{
    /// <summary>
    /// Tools > S3 > Art Budget Checker: every model under Art/Models/ and texture under Art/Textures/
    /// against the budgets in ArtBudgets, plus an "Export report" button that writes SourceArt/budget_report.md
    /// (viva evidence for polygon control and texture compression).
    /// </summary>
    public class ArtBudgetWindow : EditorWindow
    {
        const string ReportPath = "SourceArt/budget_report.md";

        class ModelRow
        {
            public string Name, Path;
            public int Tris, Verts, Submeshes, Materials, Bones, Renderers;
            public long MeshBytes;
            public bool HasBudget;
            public ArtBudgets.ModelBudget Budget;
            public bool TrisOk => !HasBudget || Tris <= Budget.MaxTris;
            public bool BonesOk => !HasBudget || Budget.MaxBones == 0 || Bones <= Budget.MaxBones;
            public bool Ok => TrisOk && BonesOk;
        }

        class TextureRow
        {
            public string Name, Path, Format;
            public int Width, Height, Mips, MaxSize;
            public long Bytes;
            public bool Ok => Mathf.Max(Width, Height) <= MaxSize;
        }

        List<ModelRow> models = new List<ModelRow>();
        List<TextureRow> textures = new List<TextureRow>();
        Vector2 scroll;
        GUIStyle failStyle;

        [MenuItem("Tools/S3/Art Budget Checker")]
        public static void Open()
        {
            var window = GetWindow<ArtBudgetWindow>("Art Budget Checker");
            window.minSize = new Vector2(820, 300);
            window.Refresh();
        }

        void OnEnable() => Refresh();
        void OnProjectChange() => Refresh();

        void Refresh()
        {
            models = CollectModels();
            textures = CollectTextures();
            Repaint();
        }

        // ------------------------------------------------------------------ data

        static List<ModelRow> CollectModels()
        {
            var rows = new List<ModelRow>();
            foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { ArtBudgets.ModelsRoot.TrimEnd('/') }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (root == null) continue;

                var meshes = new List<Mesh>();
                var materials = new HashSet<Material>();
                var renderers = root.GetComponentsInChildren<Renderer>(true);
                foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (filter.sharedMesh != null) meshes.Add(filter.sharedMesh);
                }
                foreach (var skinned in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (skinned.sharedMesh != null) meshes.Add(skinned.sharedMesh);
                }
                foreach (var renderer in renderers)
                {
                    foreach (var material in renderer.sharedMaterials)
                    {
                        if (material != null) materials.Add(material);
                    }
                }

                var row = new ModelRow
                {
                    Name = root.name,
                    Path = path,
                    Tris = ArtBudgets.CountTriangles(root),
                    Verts = meshes.Sum(m => m.vertexCount),
                    Submeshes = meshes.Sum(m => m.subMeshCount),
                    Materials = materials.Count,
                    Bones = ArtBudgets.CountBones(root),
                    Renderers = renderers.Length,
                    MeshBytes = meshes.Sum(MeshGpuBytes),
                };
                row.HasBudget = ArtBudgets.TryGetModelBudget(row.Name, out row.Budget);
                rows.Add(row);
            }
            return rows.OrderBy(r => r.Path).ToList();
        }

        static List<TextureRow> CollectTextures()
        {
            var rows = new List<TextureRow>();
            foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { ArtBudgets.TexturesRoot.TrimEnd('/') }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (texture == null) continue;
                rows.Add(new TextureRow
                {
                    Name = texture.name,
                    Path = path,
                    Width = texture.width,
                    Height = texture.height,
                    Mips = texture.mipmapCount,
                    Format = texture.graphicsFormat.ToString(),
                    Bytes = TextureGpuBytes(texture),
                    MaxSize = ArtBudgets.TextureMaxSizeFor(path, texture.name),
                });
            }
            return rows.OrderBy(r => r.Path).ToList();
        }

        /// <summary>Vertex + index buffers as uploaded to the GPU (what Read/Write off leaves in memory).</summary>
        static long MeshGpuBytes(Mesh mesh)
        {
            long bytes = 0;
            for (int stream = 0; stream < mesh.vertexBufferCount; stream++)
            {
                bytes += (long)mesh.GetVertexBufferStride(stream) * mesh.vertexCount;
            }
            int indexSize = mesh.indexFormat == IndexFormat.UInt16 ? 2 : 4;
            for (int i = 0; i < mesh.subMeshCount; i++)
            {
                bytes += (long)mesh.GetIndexCount(i) * indexSize;
            }
            return bytes;
        }

        /// <summary>Size of the imported (compressed) texture including its mip chain, for the active build target.</summary>
        static long TextureGpuBytes(Texture2D texture)
        {
            long bytes = 0;
            for (int mip = 0; mip < texture.mipmapCount; mip++)
            {
                int w = Mathf.Max(1, texture.width >> mip);
                int h = Mathf.Max(1, texture.height >> mip);
                bytes += GraphicsFormatUtility.ComputeMipmapSize(w, h, texture.graphicsFormat);
            }
            return bytes;
        }

        static string Kb(long bytes) => (bytes / 1024f).ToString("N1") + " KB";
        static string Budget(ModelRow r) => r.HasBudget ? r.Budget.MaxTris.ToString("N0") : "-";
        static string BoneBudget(ModelRow r) => r.HasBudget && r.Budget.MaxBones > 0 ? r.Budget.MaxBones.ToString() : "-";

        // ------------------------------------------------------------------ UI

        void OnGUI()
        {
            failStyle ??= new GUIStyle(EditorStyles.label) { normal = { textColor = new Color(1f, 0.35f, 0.3f) } };

            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(70))) Refresh();
                if (GUILayout.Button("Export report", EditorStyles.toolbarButton, GUILayout.Width(100))) ExportReport();
                GUILayout.FlexibleSpace();
                int fails = models.Count(m => !m.Ok) + textures.Count(t => !t.Ok);
                GUILayout.Label(fails == 0 ? "All within budget" : $"{fails} over budget", fails == 0 ? EditorStyles.miniLabel : failStyle);
            }

            scroll = EditorGUILayout.BeginScrollView(scroll);

            EditorGUILayout.LabelField($"Models ({models.Count})", EditorStyles.boldLabel);
            Row(true, "Model", "Tris", "Budget", "Verts", "Submeshes", "Materials", "Bones", "Mesh GPU", "Result");
            foreach (var m in models)
            {
                Row(m.Ok, m.Name, m.Tris.ToString("N0"), Budget(m), m.Verts.ToString("N0"), m.Submeshes.ToString(),
                    m.Materials.ToString(), m.Bones > 0 ? $"{m.Bones} / {BoneBudget(m)}" : "-", Kb(m.MeshBytes),
                    m.Ok ? "OK" : m.TrisOk ? "BONES" : $"OVER x{(float)m.Tris / m.Budget.MaxTris:F1}");
            }

            EditorGUILayout.Space(12);
            EditorGUILayout.LabelField($"Textures ({textures.Count})", EditorStyles.boldLabel);
            Row(true, "Texture", "Size", "Max", "Mips", "Format", "", "", "Memory", "Result");
            foreach (var t in textures)
            {
                Row(t.Ok, t.Name, $"{t.Width}x{t.Height}", t.MaxSize.ToString(), t.Mips.ToString(), t.Format, "", "",
                    Kb(t.Bytes), t.Ok ? "OK" : "TOO BIG");
            }
            if (textures.Count == 0) EditorGUILayout.LabelField("No textures under Art/Textures/ yet.", EditorStyles.miniLabel);

            EditorGUILayout.EndScrollView();
        }

        static readonly float[] Widths = { 200, 80, 70, 80, 75, 70, 70, 90, 80 };

        void Row(bool ok, params string[] cells)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                for (int i = 0; i < cells.Length; i++)
                {
                    var style = ok || i < cells.Length - 1 ? EditorStyles.label : failStyle;
                    GUILayout.Label(cells[i], style, GUILayout.Width(Widths[i]));
                }
            }
        }

        // ------------------------------------------------------------------ report

        void ExportReport()
        {
            Refresh();
            var sb = new StringBuilder();
            sb.AppendLine("# Art budget report");
            sb.AppendLine();
            sb.AppendLine($"Generated {DateTime.Now:yyyy-MM-dd HH:mm} by Tools > S3 > Art Budget Checker " +
                          $"(Unity {Application.unityVersion}, build target {EditorUserBuildSettings.activeBuildTarget}).");
            sb.AppendLine("Budgets: `ArtBudgets.cs` (same as `SourceArt/README.md` §4). " +
                          "Submeshes ≈ draw calls per render pass. Mesh GPU = vertex + index buffers (Read/Write off).");
            sb.AppendLine();
            sb.AppendLine("## Models");
            sb.AppendLine();
            sb.AppendLine("| Model | Tris | Budget | Verts | Submeshes | Materials | Bones | Mesh GPU | Result |");
            sb.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---|");
            foreach (var m in models)
            {
                sb.AppendLine($"| `{m.Name}` | {m.Tris:N0} | {Budget(m)} | {m.Verts:N0} | {m.Submeshes} | {m.Materials} | " +
                              $"{(m.Bones > 0 ? $"{m.Bones} / {BoneBudget(m)}" : "-")} | {Kb(m.MeshBytes)} | " +
                              $"{(m.Ok ? "✅ OK" : m.TrisOk ? "❌ bones" : $"❌ {(float)m.Tris / m.Budget.MaxTris:F1}× over")} |");
            }
            sb.AppendLine();
            sb.AppendLine($"**Totals:** {models.Count} models, {models.Sum(m => m.Tris):N0} tris, " +
                          $"{models.Sum(m => m.Submeshes)} submeshes, mesh GPU {Kb(models.Sum(m => m.MeshBytes))}.");
            sb.AppendLine();
            sb.AppendLine("## Textures");
            sb.AppendLine();
            if (textures.Count == 0)
            {
                sb.AppendLine("No textures under `Art/Textures/` yet (models use flat-colour materials).");
            }
            else
            {
                sb.AppendLine("| Texture | Size | Max | Mips | Format | Memory | Result |");
                sb.AppendLine("|---|---|---:|---:|---|---:|---|");
                foreach (var t in textures)
                {
                    sb.AppendLine($"| `{t.Name}` | {t.Width}×{t.Height} | {t.MaxSize} | {t.Mips} | {t.Format} | {Kb(t.Bytes)} | " +
                                  $"{(t.Ok ? "✅ OK" : "❌ too big")} |");
                }
                sb.AppendLine();
                sb.AppendLine($"**Total texture memory:** {Kb(textures.Sum(t => t.Bytes))}.");
            }

            string fullPath = Path.Combine(Directory.GetParent(Application.dataPath).FullName, ReportPath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
            File.WriteAllText(fullPath, sb.ToString().Replace("\r\n", "\n"));
            Debug.Log($"[Art] Budget report written to {ReportPath}");
            EditorUtility.RevealInFinder(fullPath);
        }
    }
}
