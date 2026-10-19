using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace RunwayPanic.EditorTools
{
    /// <summary>Adjusts outdoor daylight without changing the sun's direction or gameplay.</summary>
    public static class MainDaylightSetup
    {
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
