using UnityEngine;
using UnityEngine.InputSystem;

namespace RunwayPanic.Agents
{
    /// <summary>
    /// Floating text above a bird showing its brain's current state (e.g. "Hunt/Perched"), always facing the camera.
    /// <b>F3</b> toggles every label at once. On by default in the Editor, off in builds.
    /// Made for debugging, the demo video and the viva: it shows the AI's decisions live.
    /// </summary>
    public sealed class BirdStateLabel : MonoBehaviour
    {
        private const float CharacterSize = 0.05f;
        private const int FontSize = 48;

        // Set in ResetStatics before the first scene loads (Unity APIs are not allowed in static initialisers).
        private static bool visible;
        private static int lastToggleFrame = -1;

        [Tooltip("Height of the label above the bird's pivot, in metres (world space).")]
        [SerializeField, Min(0f)] private float heightOffset = 0.6f;
        [Tooltip("Text colour.")]
        [SerializeField] private Color colour = Color.white;

        private BirdBrain brain;
        private TextMesh text;
        private Transform labelTransform;
        private string shownState;

        /// <summary>Whether labels are shown (shared by all birds).</summary>
        public static bool Visible
        {
            get => visible;
            set => visible = value;
        }

        private void Awake()
        {
            brain = GetComponent<BirdBrain>();

            var label = new GameObject("StateLabel");
            labelTransform = label.transform;
            labelTransform.SetParent(transform, false);

            text = label.AddComponent<TextMesh>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var textRenderer = text.GetComponent<MeshRenderer>();
            textRenderer.sharedMaterial = text.font.material;
            textRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            textRenderer.receiveShadows = false;
            text.anchor = TextAnchor.LowerCenter;
            text.alignment = TextAlignment.Center;
            text.characterSize = CharacterSize;
            text.fontSize = FontSize;
            text.color = colour;
            text.text = "";
        }

        private void Update()
        {
            // Every label sees the key press; only the first one in a frame toggles.
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.f3Key.wasPressedThisFrame && lastToggleFrame != Time.frameCount)
            {
                lastToggleFrame = Time.frameCount;
                visible = !visible;
            }
        }

        private void LateUpdate()
        {
            bool show = visible && brain != null && brain.enabled;
            if (labelTransform.gameObject.activeSelf != show)
                labelTransform.gameObject.SetActive(show);
            if (!show)
                return;

            string state = brain.CurrentStateName;
            if (!ReferenceEquals(state, shownState))
            {
                shownState = state;
                text.text = state;
            }

            // Constant world size and height whatever the bird's scale (species are scaled 0.6–1.0).
            float scale = Mathf.Max(transform.lossyScale.x, 0.0001f);
            labelTransform.localScale = Vector3.one / scale;
            labelTransform.position = transform.position + Vector3.up * heightOffset;

            Camera cam = Camera.main;
            if (cam != null)
                labelTransform.rotation = cam.transform.rotation;
        }

        // Statics survive Play mode when domain reload is off: start every session with the default.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            visible = Application.isEditor;
            lastToggleFrame = -1;
        }
    }
}
