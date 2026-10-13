using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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
    /// texel by texel. No import setting, .meta file or asset is changed.
    /// </summary>
    public static class ArtCompressionTest
    {
        const string ReportPath = "SourceArt/compression_report.md";
        const int VisibleError = 2; // a texel counts as changed when any channel is off by more than this (of 255)

        class Result
        {
            public string Option;
            public long Bytes;
            public int Texels, Changed, MaxError;
            public double ErrorSum; // per channel, over all texels
            public Color32 WorstSource, WorstDecoded;
            public double MeanError => Texels > 0 ? ErrorSum / (Texels * 4.0) : 0;
        }

        [MenuItem("Tools/S3/Texture Compression Test")]
        static void Run()
        {
            var paths = AssetDatabase.FindAssets("t:Texture2D", new[] { ArtBudgets.TexturesRoot.TrimEnd('/') })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => ArtBudgets.IsAtlas(Path.GetFileNameWithoutExtension(p)))
                .OrderBy(p => p)
                .ToList();
            if (paths.Count == 0)
            {
                Debug.LogWarning("[Art] Texture compression test: no _Atlas_ textures under Art/Textures/.");
                return;
            }

            var rows = new List<(string Name, int Size, List<Result> Results)>();
            foreach (string path in paths)
            {
                var source = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
                source.LoadImage(File.ReadAllBytes(path));
                Color32[] pixels = source.GetPixels32();
                int width = source.width, height = source.height;
                Object.DestroyImmediate(source);

                // What the importer's two compressed qualities pick on PC: Normal = BC1, or BC3 when the texture uses
                // its alpha channel (the Mask keeps smoothness there); High = BC7.
                bool usesAlpha = pixels.Any(p => p.a < 255);
                var results = new List<Result>
                {
                    Measure("Uncompressed (RGBA32)", TextureFormat.RGBA32, pixels, width, height),
                    Measure(usesAlpha ? "Normal quality (BC3)" : "Normal quality (BC1)",
                        usesAlpha ? TextureFormat.DXT5 : TextureFormat.DXT1, pixels, width, height),
                    Measure("High quality (BC7)", TextureFormat.BC7, pixels, width, height),
                };
                rows.Add((Path.GetFileNameWithoutExtension(path), width, results));
            }

            WriteReport(rows);
        }

        static Result Measure(string option, TextureFormat format, Color32[] pixels, int width, int height)
        {
            // linear = true on every texture and target: the bytes are compared as stored, with no sRGB conversion.
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false, true) { filterMode = FilterMode.Point };
            texture.SetPixels32(pixels);
            texture.Apply(false);
            if (format != TextureFormat.RGBA32)
            {
                EditorUtility.CompressTexture(texture, format, TextureCompressionQuality.Normal);
                texture.Apply(false); // upload the compressed blocks
            }

            var result = new Result
            {
                Option = option,
                Texels = pixels.Length,
                Bytes = GraphicsFormatUtility.ComputeMipmapSize(width, height, texture.graphicsFormat),
            };

            Color32[] decoded = Decode(texture, width, height);
            Object.DestroyImmediate(texture);
            for (int i = 0; i < pixels.Length; i++)
            {
                Color32 a = pixels[i], b = decoded[i];
                int dr = Math.Abs(a.r - b.r), dg = Math.Abs(a.g - b.g), db = Math.Abs(a.b - b.b), da = Math.Abs(a.a - b.a);
                int worst = Math.Max(Math.Max(dr, dg), Math.Max(db, da));
                result.ErrorSum += dr + dg + db + da;
                if (worst > VisibleError) result.Changed++;
                if (worst > result.MaxError)
                {
                    result.MaxError = worst;
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

        static string Kb(long bytes) => (bytes / 1024f).ToString("N1") + " KB";
        static string Rgba(Color32 c) => $"{c.r},{c.g},{c.b},{c.a}";

        static void WriteReport(List<(string Name, int Size, List<Result> Results)> rows)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Texture compression report");
            sb.AppendLine();
            sb.AppendLine($"Generated {DateTime.Now:yyyy-MM-dd HH:mm} by Tools > S3 > Texture Compression Test " +
                          $"(Unity {Application.unityVersion}, {SystemInfo.graphicsDeviceType}).");
            sb.AppendLine("Each `_Atlas_` PNG is compressed in memory (`EditorUtility.CompressTexture`, quality Normal), decoded by the " +
                          "GPU and compared with the PNG texel by texel, as stored bytes (0-255), over all texels including the " +
                          "unused background. No mipmaps, as imported.");
            sb.AppendLine($"Changed texels = any channel off by more than {VisibleError}. " +
                          "The uncompressed rows are the control: they must show 0 everywhere, or the read-back itself is wrong.");
            sb.AppendLine();

            sb.AppendLine("## Totals");
            sb.AppendLine();
            sb.AppendLine("| Option | Memory, all atlas textures | Changed texels | Largest error |");
            sb.AppendLine("|---|---:|---:|---:|");
            for (int option = 0; option < 3; option++)
            {
                var results = rows.Select(r => r.Results[option]).ToList();
                long texels = results.Sum(r => (long)r.Texels), changed = results.Sum(r => (long)r.Changed);
                string label = option == 0 ? "Uncompressed (RGBA32)" : option == 1 ? "Normal quality (BC1, BC3 with alpha)" : "High quality (BC7)";
                sb.AppendLine($"| {label} | {Kb(results.Sum(r => r.Bytes))} | {changed:N0} of {texels:N0} " +
                              $"({100.0 * changed / texels:0.00}%) | {results.Max(r => r.MaxError)} |");
            }
            sb.AppendLine();

            sb.AppendLine("## Per texture");
            sb.AppendLine();
            sb.AppendLine("| Texture | Size | Option | Memory | Mean error | Largest error | Changed texels | Worst texel: PNG -> decoded (R,G,B,A) |");
            sb.AppendLine("|---|---|---|---:|---:|---:|---:|---|");
            foreach (var (name, size, results) in rows)
            {
                foreach (var r in results)
                {
                    string worst = r.MaxError == 0 ? "-" : $"{Rgba(r.WorstSource)} -> {Rgba(r.WorstDecoded)}";
                    sb.AppendLine($"| `{name}` | {size}² | {r.Option} | {Kb(r.Bytes)} | {r.MeanError:0.000} | {r.MaxError} | " +
                                  $"{r.Changed:N0} ({100.0 * r.Changed / r.Texels:0.00}%) | {worst} |");
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
