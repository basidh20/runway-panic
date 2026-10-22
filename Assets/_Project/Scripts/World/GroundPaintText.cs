using UnityEngine;

namespace RunwayPanic.World
{
    /// <summary>Keep legacy runway glyphs, but depth-test them as paint instead of overlay text.</summary>
    [ExecuteAlways, DisallowMultipleComponent, RequireComponent(typeof(TextMesh))]
    public sealed class GroundPaintText : MonoBehaviour
    {
        private TextMesh label;
        private MeshRenderer labelRenderer;
        private Font font, originalFont;
        private Material paintMaterial;
        private MaterialPropertyBlock atlasProperties, originalProperties;

        private void OnEnable()
        {
            label = GetComponent<TextMesh>();
            labelRenderer = GetComponent<MeshRenderer>();
            if (labelRenderer == null || labelRenderer.sharedMaterial == null) return;
            originalFont = label.font;
            paintMaterial = labelRenderer.sharedMaterial;
            originalProperties = new MaterialPropertyBlock();
            labelRenderer.GetPropertyBlock(originalProperties);
            atlasProperties = new MaterialPropertyBlock();
            labelRenderer.GetPropertyBlock(atlasProperties);
            font = originalFont != null ? originalFont : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null) return;
            label.font = font;
            labelRenderer.sharedMaterial = paintMaterial;
            Font.textureRebuilt += OnFontTextureRebuilt;
            font.RequestCharactersInTexture(label.text, label.fontSize, label.fontStyle);
            RefreshAtlas();
        }

        private void OnFontTextureRebuilt(Font rebuilt)
        {
            if (rebuilt == font) RefreshAtlas();
        }

        private void RefreshAtlas()
        {
            if (paintMaterial == null || font == null || labelRenderer == null) return;
            // Dynamic fonts can replace their atlas when other labels request new glyphs.
            atlasProperties.SetTexture("_MainTex", font.material.mainTexture);
            labelRenderer.SetPropertyBlock(atlasProperties);
        }

        private void OnDisable()
        {
            Font.textureRebuilt -= OnFontTextureRebuilt;
            if (paintMaterial == null) return;
            if (label != null) label.font = originalFont;
            if (labelRenderer != null)
            {
                labelRenderer.sharedMaterial = paintMaterial;
                labelRenderer.SetPropertyBlock(originalProperties);
            }
            paintMaterial = null;
        }

    }
}
