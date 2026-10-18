using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RunwayPanic.UI
{
    /// <summary>
    /// Selects this button / slider / toggle when the mouse moves over it, so the mouse and the keyboard or gamepad
    /// always share one highlight instead of showing two.
    /// </summary>
    [RequireComponent(typeof(Selectable))]
    public sealed class SelectOnHover : MonoBehaviour, IPointerEnterHandler
    {
        public void OnPointerEnter(PointerEventData eventData)
        {
            if (EventSystem.current != null && TryGetComponent(out Selectable selectable) && selectable.IsInteractable())
                EventSystem.current.SetSelectedGameObject(gameObject);
        }
    }
}
