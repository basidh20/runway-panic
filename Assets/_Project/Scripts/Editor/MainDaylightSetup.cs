using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace RunwayPanic.EditorTools
{
    /// <summary>Adjusts outdoor daylight without changing the sun's direction or gameplay.</summary>
    public static class MainDaylightSetup
    {
        [MenuItem("Tools/Runway Panic/Capture Interior Lighting Views")]
        private static void CaptureInteriors()
        {
            var scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || scene.path != MainPlayerIntegration.ScenePath)
                throw new InvalidOperationException("Open Main outside Play mode first.");
            var shelter = GameObject.Find("Environment/Buildings/Shelter_A");
            var hangar = GameObject.Find("Environment/Buildings/Hangar_Greybox/P_Greybox_Hangar");
            if (shelter == null || hangar == null) throw new InvalidOperationException("Main interiors are missing.");
            const string folder = "/private/tmp/RunwayPanic-C3";
            Directory.CreateDirectory(folder);
            Capture(shelter.transform, new Vector3(0, 1.6f, 1.6f), new Vector3(0, 1.5f, -3.5f), folder + "/shelter.png");
            Capture(hangar.transform, new Vector3(0, 1.6f, 0), new Vector3(-5, 1.5f, -9), folder + "/hangar.png");
            Debug.Log("[AirportC3] Captured interior lighting previews to " + folder);
        }

        private static void Capture(Transform space, Vector3 position, Vector3 target, string path)
        {
            var go = new GameObject("C3_InteriorPreview_TEMP") { hideFlags = HideFlags.HideAndDontSave };
            var camera = go.AddComponent<Camera>();
            camera.enabled = false; camera.fieldOfView = 60; camera.farClipPlane = 100;
            camera.transform.position = space.TransformPoint(position);
            camera.transform.LookAt(space.TransformPoint(target));
            go.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing = true;
            var old = RenderTexture.active;
            var texture = RenderTexture.GetTemporary(960, 540, 24);
            var image = new Texture2D(960, 540, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = texture; camera.Render(); RenderTexture.active = texture;
                image.ReadPixels(new Rect(0, 0, 960, 540), 0, 0); image.Apply();
                File.WriteAllBytes(path, image.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = old; camera.targetTexture = null;
                RenderTexture.ReleaseTemporary(texture); UnityEngine.Object.DestroyImmediate(image);
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [MenuItem("Tools/Runway Panic/Configure Airport Daylight")]
        private static void Configure()
        {
            var scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || scene.path != MainPlayerIntegration.ScenePath || scene.isDirty)
                throw new InvalidOperationException("Open saved Main outside Play mode first.");

            var sun = RenderSettings.sun;
            if (sun == null || sun.type != LightType.Directional || sun.gameObject.name != "Sun_Directional")
                throw new InvalidOperationException("The expected Main directional sun is missing.");

            Undo.RecordObject(sun, "Adjust airport daylight");
            sun.color = new Color(1f, .96f, .89f);
            sun.intensity = 1.25f;
            // Leave rotation, shadow settings, light mode and sun reference unchanged for Hawk AI.
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(.58f, .66f, .74f);
            RenderSettings.ambientEquatorColor = new Color(.45f, .49f, .50f);
            RenderSettings.ambientGroundColor = new Color(.28f, .30f, .29f);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Could not save Main scene.");
            Debug.Log("[AirportC3] Saved outdoor daylight; sun direction and shadows unchanged.");
        }
    }
}
