using RunwayPanic.Core;
using RunwayPanic.Player;
using RunwayPanic.Weapons;
using UnityEngine;
using UnityEngine.UI;

namespace RunwayPanic.UI
{
    /// <summary>
    /// Screen-centre crosshair. PlayerShooting aims through the exact centre of the screen, so this marks where shots
    /// go. Its four lines open with the current shot spread (hip fire, sustained fire) and close in when aiming; it hides
    /// while looking through a scope (the scope has its own reticle). Shots that hit flash an "X" hit marker — red when
    /// the hit killed — and briefly tint the crosshair.
    /// </summary>
    public sealed class CrosshairUI : MonoBehaviour
    {
        [Tooltip("Shooter to listen to for hits. Found in parents if left empty.")]
        [SerializeField] private PlayerShooting shooter;
        [SerializeField] private Graphic[] parts;
        [SerializeField] private Color normalColor = new Color(1f, 1f, 1f, 0.9f);
        [SerializeField] private Color hitColor = new Color(1f, 0.25f, 0.2f, 1f);
        [SerializeField, Min(0f)] private float hitFlashDuration = 0.15f;

        [Header("Spread")]
        [Tooltip("Top, bottom, left and right lines; they move out from the centre with the spread.")]
        [SerializeField] private RectTransform[] lines;
        [Tooltip("Gap between the centre and each line with no spread, in canvas units.")]
        [SerializeField, Min(0f)] private float baseGap = 14f;
        [Tooltip("How fast the lines follow the spread (per second).")]
        [SerializeField, Min(1f)] private float followSpeed = 18f;

        [Header("Hit marker")]
        [Tooltip("The four diagonal strokes of the X shown on a hit.")]
        [SerializeField] private Graphic[] hitMarker;
        [SerializeField] private Color hitMarkerColor = Color.white;
        [SerializeField] private Color killMarkerColor = new Color(1f, 0.2f, 0.15f, 1f);
        [SerializeField, Min(0.01f)] private float hitMarkerDuration = 0.18f;

        private PlayerCameraRig cameraRig;
        private Canvas canvas;
        private Vector2[] lineDirections;
        private float gap;
        private float hitFlashEndTime;
        private float hitMarkerEndTime;
        private bool isShowingHit;

        private void Awake()
        {
            if (shooter == null)
                shooter = GetComponentInParent<PlayerShooting>();
            cameraRig = GetComponentInParent<PlayerCameraRig>();
            canvas = GetComponentInParent<Canvas>();

            lineDirections = new Vector2[lines != null ? lines.Length : 0];
            for (int i = 0; i < lineDirections.Length; i++)
                lineDirections[i] = lines[i] != null ? lines[i].anchoredPosition.normalized : Vector2.zero;
            gap = baseGap;

            SetColor(normalColor);
            ShowHitMarker(false, hitMarkerColor);
        }

        private void OnEnable()
        {
            if (shooter != null)
            {
                shooter.ShotFired += HandleShotFired;
                shooter.HitConfirmed += HandleHitConfirmed;
            }
        }

        private void OnDisable()
        {
            if (shooter != null)
            {
                shooter.ShotFired -= HandleShotFired;
                shooter.HitConfirmed -= HandleHitConfirmed;
            }
        }

        private void Update()
        {
            if (isShowingHit && Time.unscaledTime >= hitFlashEndTime)
            {
                isShowingHit = false;
                SetColor(normalColor);
            }

            if (hitMarkerEndTime > 0f && Time.unscaledTime >= hitMarkerEndTime)
            {
                hitMarkerEndTime = 0f;
                ShowHitMarker(false, hitMarkerColor);
            }

            bool scoped = cameraRig != null && cameraRig.IsScoped;
            foreach (Graphic part in parts)
                if (part != null)
                    part.enabled = !scoped;

            UpdateSpread();
        }

        // Spread is an angle; its size on screen depends on the field of view: tan(spread) / tan(fov / 2) of half the
        // screen height, in canvas units.
        private void UpdateSpread()
        {
            if (lines == null || shooter == null)
                return;

            float spread = shooter.CurrentSpread;
            float fov = cameraRig != null ? cameraRig.FieldOfView
                : Camera.main != null ? Camera.main.fieldOfView
                : 60f;
            float halfHeight = canvas != null
                ? ((RectTransform)canvas.transform).rect.height * 0.5f
                : Screen.height * 0.5f;
            float ratio = Mathf.Tan(spread * Mathf.Deg2Rad) / Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad);
            float target = baseGap + ratio * halfHeight;
            gap = Mathf.Lerp(gap, target, 1f - Mathf.Exp(-followSpeed * Time.unscaledDeltaTime));

            for (int i = 0; i < lines.Length; i++)
                if (lines[i] != null)
                    lines[i].anchoredPosition = lineDirections[i] * gap;
        }

        private void HandleShotFired(Vector3 origin, Vector3 endPoint, IDamageable target)
        {
            if (target == null)
                return;

            isShowingHit = true;
            hitFlashEndTime = Time.unscaledTime + hitFlashDuration;
            SetColor(hitColor);
        }

        private void HandleHitConfirmed(IDamageable target, bool killed)
        {
            hitMarkerEndTime = Time.unscaledTime + hitMarkerDuration * (killed ? 1.6f : 1f);
            ShowHitMarker(true, killed ? killMarkerColor : hitMarkerColor);
        }

        private void ShowHitMarker(bool show, Color color)
        {
            if (hitMarker == null)
                return;
            foreach (Graphic stroke in hitMarker)
            {
                if (stroke == null)
                    continue;
                stroke.enabled = show;
                stroke.color = color;
            }
        }

        private void SetColor(Color color)
        {
            foreach (Graphic part in parts)
            {
                if (part != null)
                    part.color = color;
            }
        }
    }
}
