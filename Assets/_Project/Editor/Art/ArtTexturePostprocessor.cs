using System.IO;
using UnityEditor;
using UnityEngine;

namespace RunwayPanic.ArtTools
{
    /// <summary>
    /// Applies the agreed texture import settings to everything under Art/Textures/.
    /// Suffix rules: _Normal = normal map; _Mask/_ORM/_Roughness/_Metallic/_AO/_Height = linear data (sRGB off).
    /// Names containing "Palette" are the shared colour atlas: 256 max, Point filter, Clamp.
    /// </summary>
    public class ArtTexturePostprocessor : AssetPostprocessor
    {
        static readonly string[] LinearSuffixes = { "_Mask", "_ORM", "_Roughness", "_Metallic", "_AO", "_Height" };

        // Bump when the rules below change so Unity re-imports the affected textures.
        public override uint GetVersion() => 1;

        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(ArtBudgets.TexturesRoot)) return;

            var importer = (TextureImporter)assetImporter;
            string textureName = Path.GetFileNameWithoutExtension(assetPath);

            importer.textureType = textureName.EndsWith("_Normal")
                ? TextureImporterType.NormalMap
                : TextureImporterType.Default;
            importer.sRGBTexture = !IsLinearData(textureName);
            importer.isReadable = false;
            importer.mipmapEnabled = true;
            importer.textureCompression = TextureImporterCompression.Compressed; // "Normal Quality" -> BC/DXT on PC
            importer.maxTextureSize = ArtBudgets.TextureMaxSizeFor(assetPath, textureName);

            if (ArtBudgets.IsPalette(textureName))
            {
                // Flat colour swatches: Point keeps edges crisp, Clamp stops UVs on the border sampling the far side.
                importer.filterMode = FilterMode.Point;
                importer.wrapMode = TextureWrapMode.Clamp;
            }
            else
            {
                importer.filterMode = FilterMode.Bilinear;
                importer.wrapMode = TextureWrapMode.Repeat;
            }
        }

        static bool IsLinearData(string textureName)
        {
            foreach (string suffix in LinearSuffixes)
            {
                if (textureName.EndsWith(suffix)) return true;
            }
            return false;
        }
    }
}
