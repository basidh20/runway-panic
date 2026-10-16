using RunwayPanic.Core;
using RunwayPanic.Player;
using UnityEngine;
using UnityEngine.UI;

namespace RunwayPanic.UI
{
    /// <summary>
    /// Shows "[E] Open door" style text while the player is aiming at something they can use.
    /// </summary>
    [RequireComponent(typeof(Text))]
    public sealed class InteractionPromptUI : MonoBehaviour
    {
        [Tooltip("Interaction to listen to. Found in parents if left empty.")]
        [SerializeField] private PlayerInteraction interaction;
        [Tooltip("{0} is replaced by the object's prompt.")]
        [SerializeField] private string format = "[E] {0}";

        private Text label;

        private void Awake()
        {
            label = GetComponent<Text>();
            if (interaction == null)
                interaction = GetComponentInParent<PlayerInteraction>();

            Show(null);
        }

        private void OnEnable()
        {
            if (interaction != null)
            {
                interaction.FocusChanged += Show;
                Show(interaction.CurrentTarget);
            }
        }

        private void OnDisable()
        {
            if (interaction != null)
                interaction.FocusChanged -= Show;
        }

        private void Show(IInteractable target)
        {
            string prompt = target?.InteractionPrompt;
            label.enabled = !string.IsNullOrEmpty(prompt);
            if (label.enabled)
                label.text = string.Format(format, prompt);
        }
    }
}
