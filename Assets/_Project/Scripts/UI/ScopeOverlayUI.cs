using RunwayPanic.Player;
using UnityEngine;

namespace RunwayPanic.UI
{
    /// <summary>
    /// The sniper scope seen through the eyepiece: a dark mask with a round lens, a fine reticle with range marks, and
    /// black bars filling wide screens. Fades in as <see cref="PlayerCameraRig"/> goes into the scope and out as it
    /// leaves. Lives on the player's HUD canvas.
    /// </summary>
    [RequireComponent(typeof(CanvasGroup))]
    public sealed class ScopeOverlayUI : MonoBehaviour
    {
        [Tooltip("Found in parents if left empty.")]
        [SerializeField] private PlayerCameraRig cameraRig;
        [Tooltip("Scope blend at which the overlay starts to show, and is fully shown.")]
        [SerializeField] private Vector2 fadeRange = new Vector2(0.4f, 0.9f);

        private CanvasGroup group;

        private void Awake()
        {
            group = GetComponent<CanvasGroup>();
            group.interactable = false;
            group.blocksRaycasts = false;
            if (cameraRig == null)
                cameraRig = GetComponentInParent<PlayerCameraRig>();
            group.alpha = 0f;
        }

        private void LateUpdate()
        {
            float blend = cameraRig != null ? cameraRig.ScopeBlend : 0f;
            group.alpha = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(fadeRange.x, fadeRange.y, blend));
        }
    }
}
