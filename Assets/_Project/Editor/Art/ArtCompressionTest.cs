using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using Object = UnityEngine.Object;

namespace RunwayPanic.ArtTools
{
    /// <summary>
    /// Tools > S3 > Texture Compression Test: measures what each compression choice costs and what it does to the
    /// flat colours of the "_Atlas_" textures, and writes SourceArt/compression_report.md (viva evidence for the
    /// BC7 rule in ArtTexturePostprocessor).
    /// Every atlas PNG is compressed IN MEMORY to each format, decoded again on the GPU and compared with the PNG
    /// texel by texel. Texels "on faces" are the ones a UV triangle of a prefab using the texture overlaps: only
    /// those can ever be drawn. No import setting, .meta file or asset is changed.
    /// </summary>
    public static class ArtCompressionTest
    {
        const string ReportPath = "SourceArt/compression_report.md";
        // Only the Core Developer's prefab folders (the ones ArtPrefabBuilder writes): teammates' prefabs are not read.
        static readonly string[] PrefabFolders =
        {
            "Assets/_Project/Prefabs/Player", "Assets/_Project/Prefabs/Birds", "Assets/_Project/Prefabs/Weapons",
        };
        const int VisibleError = 2;       // a texel counts as changed when any channel is off by more than this (of 255)
        const float TouchEpsilon = 1e-4f; // a triangle that only touches a texel's border does not cover it

        class Source
        {
            public string Path, Name;
            public int Width, Height;
            public Color32[] Pixels;
            public bool[] OnFaces;
        }

        class Result
        {
            public string Option;
            public long Bytes;
            public int Texels, Changed, MaxError;
            public int FaceTexels, FaceChanged, FaceMaxError;
            public double ErrorSum; // per channel, over all texels
            public Color32 WorstSource, WorstDecoded;
            public double MeanError => Texels > 0 ? ErrorSum / (Texels * 4.0) : 0;
        }

        [MenuItem("Tools/S3/Texture Compression Test")]
        static void Run()
        {
            var sources = AssetDatabase.FindAssets("t:Texture2D", new[] { ArtBudgets.TexturesRoot.TrimEnd('/') })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => ArtBudgets.IsAtlas(Path.GetFileNameWithoutExtension(p)))
                .OrderBy(p => p)
                .Select(LoadSource)
                .ToList();
            if (sources.Count == 0)
            {
                Debug.LogWarning("[Art] Texture compression test: no _Atlas_ textures under Art/Textures/.");
                return;
            }

            MarkTexelsOnFaces(sources);

            var rows = new List<(Source Source, List<Result> Results)>();
            foreach (var source in sources)
            {
                // What the importer's two compressed qualities pick on PC: Normal = BC1, or BC3 when the texture uses
                // its alpha channel (the Mask keeps smoothness there); High = BC7.
                bool usesAlpha = source.Pixels.Any(p => p.a < 255);
                rows.Add((source, new List<Result>
                {
                    Measure("Uncompressed (RGBA32)", TextureFormat.RGBA32, source),
                    Measure(usesAlpha ? "Normal quality (BC3)" : "Normal quality (BC1)",
                        usesAlpha ? TextureFormat.DXT5 : TextureFormat.DXT1, source),
                    Measure("High quality (BC7)", TextureFormat.BC7, source),
                }));
            }

            WriteReport(rows);
        }

        static Source LoadSource(string path)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            texture.LoadImage(File.ReadAllBytes(path));
            var source = new Source
            {
                Path = path,
                Name = Path.GetFileNameWithoutExtension(path),
                Width = texture.width,
                Height = texture.height,
                Pixels = texture.GetPixels32(),
            };
            source.OnFaces = new bool[source.Pixels.Length];
            Object.DestroyImmediate(texture);
            return source;
        }

        // ------------------------------------------------------------------ texels on faces

        /// <summary>
        /// Marks every texel that a UV triangle overlaps, for each submesh of each prefab whose material uses the texture.
        /// Prefabs, not models: the pigeon and crow Albedo are only assigned on the species prefabs.
        /// </summary>
        static void MarkTexelsOnFaces(List<Source> sources)
        {
            var byPath = sources.ToDictionary(s => s.Path);
            var done = new HashSet<(Mesh Mesh, int Submesh, string Path)>();
            string[] folders = PrefabFolders.Where(AssetDatabase.IsValidFolder).ToArray();
            if (folders.Length == 0) return;
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", folders))
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                if (prefab == null) continue;
                foreach (var renderer in prefab.GetComponentsInChildren<Renderer>(true))
                {
                    // TryGetComponent, not GetComponent<MeshFilter>()?. : on a renderer without a MeshFilter the
                    // editor returns a placeholder object that ?. does not see as null, and reading it throws.
                    Mesh mesh = null;
                    if (renderer is SkinnedMeshRenderer skinned) mesh = skinned.sharedMesh;
                    else if (renderer.TryGetComponent(out MeshFilter filter)) mesh = filter.sharedMesh;
                    if (mesh == null) continue;

                    Material[] materials = renderer.sharedMaterials;
                    for (int submesh = 0; submesh < Mathf.Min(mesh.subMeshCount, materials.Length); submesh++)
                    {
                        if (materials[submesh] == null) continue;
                        foreach (string property in materials[submesh].GetTexturePropertyNames())
                        {
                            Texture texture = materials[submesh].GetTexture(property);
                            if (texture == null || !byPath.TryGetValue(AssetDatabase.GetAssetPath(texture), out Source source)) continue;
                            if (done.Add((mesh, submesh, source.Path))) MarkSubmesh(mesh, submesh, source);
                        }
                    }
                }
            }
        }

        static void MarkSubmesh(Mesh mesh, int submesh, Source source)
        {
            if (mesh.GetTopology(submesh) != MeshTopology.Triangles) return;

            // The editor can read mesh data even with Read/Write off (the import setting for every model here).
            using Mesh.MeshDataArray dataArray = MeshUtility.AcquireReadOnlyMeshData(mesh);
            Mesh.MeshData data = dataArray[0];
            using var uvs = new NativeArray<Vector2>(data.vertexCount, Allocator.Temp);
            using var indices = new NativeArray<int>(data.GetSubMesh(submesh).indexCount, Allocator.Temp);
            data.GetUVs(0, uvs);
            data.GetIndices(indices, submesh);

            var size = new Vector2(source.Width, source.Height);
            for (int i = 0; i + 2 < indices.Length; i += 3)
            {
                MarkTriangle(uvs[indices[i]] * size, uvs[indices[i + 1]] * size, uvs[indices[i + 2]] * size, source);
            }
        }

        static void MarkTriangle(Vector2 a, Vector2 b, Vector2 c, Source source)
        {
            int minX = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.x, Mathf.Min(b.x, c.x))));
            int maxX = Mathf.Min(source.Width - 1, Mathf.FloorToInt(Mathf.Max(a.x, Mathf.Max(b.x, c.x))));
            int minY = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.y, Mathf.Min(b.y, c.y))));
            int maxY = Mathf.Min(source.Height - 1, Mathf.FloorToInt(Mathf.Max(a.y, Mathf.Max(b.y, c.y))));
            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    int index = y * source.Width + x;
                    if (!source.OnFaces[index] && TriangleOverlapsTexel(a, b, c, x, y)) source.OnFaces[index] = true;
                }
            }
        }

        /// <summary>Separating-axis test of a triangle against the texel square [x, x+1] x [y, y+1].</summary>
        static bool TriangleOverlapsTexel(Vector2 a, Vector2 b, Vector2 c, int x, int y)
        {
            float lowX = x + TouchEpsilon, highX = x + 1 - TouchEpsilon, lowY = y + TouchEpsilon, highY = y + 1 - TouchEpsilon;
            if (Mathf.Max(a.x, Mathf.Max(b.x, c.x)) < lowX || Mathf.Min(a.x, Mathf.Min(b.x, c.x)) > highX) return false;
            if (Mathf.Max(a.y, Mathf.Max(b.y, c.y)) < lowY || Mathf.Min(a.y, Mathf.Min(b.y, c.y)) > highY) return false;
            return !EdgeSeparates(a, b, c, lowX, highX, lowY, highY)
                   && !EdgeSeparates(b, c, a, lowX, highX, lowY, highY)
                   && !EdgeSeparates(c, a, b, lowX, highX, lowY, highY);
        }

        /// <summary>True if the line through edge p-q has the triangle's third corner on one side and the whole square on the other.</summary>
        static bool EdgeSeparates(Vector2 p, Vector2 q, Vector2 third, float lowX, float highX, float lowY, float highY)
        {
            var normal = new Vector2(q.y - p.y, p.x - q.x);
            float inside = Vector2.Dot(normal, third - p);
            if (inside == 0f) return false; // zero-area triangle: this edge gives no side
            if (inside < 0f) normal = -normal;
            // The square corner that lies furthest along the normal: if even that one is behind the edge, they are apart.
            var furthest = new Vector2(normal.x > 0f ? highX : lowX, normal.y > 0f ? highY : lowY);
            return Vector2.Dot(normal, furthest - p) < 0f;
        }

        // ------------------------------------------------------------------ compression

        static Result Measure(string option, TextureFormat format, Source source)
        {
            // linear = true on every texture and target: the bytes are compared as stored, with no sRGB conversion.
            var texture = new Texture2D(source.Width, source.Height, TextureFormat.RGBA32, false, true) { filterMode = FilterMode.Point };
            texture.SetPixels32(source.Pixels);
            texture.Apply(false);
            if (format != TextureFormat.RGBA32)
            {
                EditorUtility.CompressTexture(texture, format, TextureCompressionQuality.Normal);
                texture.Apply(false); // upload the compressed blocks
            }

            var result = new Result
            {
                Option = option,
                Texels = source.Pixels.Length,
                FaceTexels = source.OnFaces.Count(covered => covered),
                Bytes = GraphicsFormatUtility.ComputeMipmapSize(source.Width, source.Height, texture.graphicsFormat),
            };

            Color32[] decoded = Decode(texture, source.Width, source.Height);
            Object.DestroyImmediate(texture);
            for (int i = 0; i < decoded.Length; i++)
            {
                Color32 a = source.Pixels[i], b = decoded[i];
                int dr = Math.Abs(a.r - b.r), dg = Math.Abs(a.g - b.g), db = Math.Abs(a.b - b.b), da = Math.Abs(a.a - b.a);
                int worst = Math.Max(Math.Max(dr, dg), Math.Max(db, da));
                result.ErrorSum += dr + dg + db + da;
                result.MaxError = Math.Max(result.MaxError, worst);
                if (worst > VisibleError) result.Changed++;
                if (!source.OnFaces[i]) continue;

                if (worst > VisibleError) result.FaceChanged++;
                if (worst > result.FaceMaxError)
                {
                    result.FaceMaxError = worst;
                    result.WorstSource = a;
                    result.WorstDecoded = b;
                }
            }
            return result;
        }

        /// <summary>The GPU decodes the block format: draw the texture 1:1 into a render target and read it back.</summary>
        static Color32[] Decode(Texture2D texture, int width, int height)
        {
            var target = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            target.filterMode = FilterMode.Point;
            RenderTexture previous = RenderTexture.active;
            Graphics.Blit(texture, target);
            RenderTexture.active = target;
            var readback = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
            readback.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            readback.Apply(false);
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(target);
            Color32[] decoded = readback.GetPixels32();
            Object.DestroyImmediate(readback);
            return decoded;
        }

        // ------------------------------------------------------------------ report

        static string Kb(long bytes) => (bytes / 1024f).ToString("N1") + " KB";
        static string Rgba(Color32 c) => $"{c.r},{c.g},{c.b},{c.a}";
        static string Share(long part, long whole) => whole > 0 ? $"{part:N0} ({100.0 * part / whole:0.00}%)" : "-";

        static void WriteReport(List<(Source Source, List<Result> Results)> rows)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Texture compression report");
            sb.AppendLine();
            sb.AppendLine($"Generated {DateTime.Now:yyyy-MM-dd HH:mm} by Tools > S3 > Texture Compression Test " +
                          $"(Unity {Application.unityVersion}, {SystemInfo.graphicsDeviceType}).");
            sb.AppendLine("Each `_Atlas_` PNG is compressed in memory (`EditorUtility.CompressTexture`, quality Normal), decoded by the " +
                          "GPU and compared with the PNG texel by texel, as stored bytes (0-255). No mipmaps, as imported.");
            sb.AppendLine($"Changed = any channel off by more than {VisibleError}. " +
                          "**On faces** = texels that a UV triangle of a prefab using the texture overlaps: only these can be drawn. " +
                          "The other texels are island padding and unused background.");
            sb.AppendLine("The uncompressed rows are the control: they must show 0 everywhere, or the read-back itself is wrong.");
            sb.AppendLine();

            sb.AppendLine("## Totals");
            sb.AppendLine();
            sb.AppendLine("| Option | Memory, all atlas textures | Changed texels, all | Changed texels on faces | Largest error on faces |");
            sb.AppendLine("|---|---:|---:|---:|---:|");
            for (int option = 0; option < 3; option++)
            {
                var results = rows.Select(r => r.Results[option]).ToList();
                string label = option == 0 ? "Uncompressed (RGBA32)" : option == 1 ? "Normal quality (BC1, BC3 with alpha)" : "High quality (BC7)";
                sb.AppendLine($"| {label} | {Kb(results.Sum(r => r.Bytes))} | " +
                              $"{Share(results.Sum(r => (long)r.Changed), results.Sum(r => (long)r.Texels))} | " +
                              $"{Share(results.Sum(r => (long)r.FaceChanged), results.Sum(r => (long)r.FaceTexels))} | " +
                              $"{results.Max(r => r.FaceMaxError)} |");
            }
            sb.AppendLine();

            sb.AppendLine("## Per texture");
            sb.AppendLine();
            sb.AppendLine("| Texture | Size | Texels on faces | Option | Memory | Mean error, all | Largest error, all | Changed, all | " +
                          "Changed on faces | Largest error on faces | Worst texel on faces: PNG -> decoded (R,G,B,A) |");
            sb.AppendLine("|---|---|---:|---|---:|---:|---:|---:|---:|---:|---|");
            foreach (var (source, results) in rows)
            {
                foreach (var r in results)
                {
                    string worst = r.FaceMaxError == 0 ? "-" : $"{Rgba(r.WorstSource)} -> {Rgba(r.WorstDecoded)}";
                    sb.AppendLine($"| `{source.Name}` | {source.Width}² | {Share(r.FaceTexels, r.Texels)} | {r.Option} | {Kb(r.Bytes)} | " +
                                  $"{r.MeanError:0.000} | {r.MaxError} | {Share(r.Changed, r.Texels)} | " +
                                  $"{Share(r.FaceChanged, r.FaceTexels)} | {r.FaceMaxError} | {worst} |");
                }
            }

            string fullPath = Path.Combine(Directory.GetParent(Application.dataPath).FullName, ReportPath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
            File.WriteAllText(fullPath, sb.ToString().Replace("\r\n", "\n"));
            Debug.Log($"[Art] Texture compression report written to {ReportPath}");
            EditorUtility.RevealInFinder(fullPath);
        }
    }
}
