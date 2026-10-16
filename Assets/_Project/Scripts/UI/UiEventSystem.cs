using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

namespace RunwayPanic.UI
{
    /// <summary>
    /// Makes sure the scene has an EventSystem so UI buttons can be clicked.
    /// Uses the Input System's UI module, matching the project's input setup.
    /// </summary>
    public static class UiEventSystem
    {
        public static void Ensure()
        {
            if (EventSystem.current != null || Object.FindAnyObjectByType<EventSystem>() != null)
                return;

            var eventSystem = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            Object.DontDestroyOnLoad(eventSystem);
        }
    }
}
