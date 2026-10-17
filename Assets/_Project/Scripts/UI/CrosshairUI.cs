using RunwayPanic.Core;
using RunwayPanic.Weapons;
using UnityEngine;
using UnityEngine.UI;

namespace RunwayPanic.UI
{
    /// <summary>
    /// Screen-centre crosshair. PlayerShooting aims through the exact centre of the screen,
    /// so this marks where shots go. Briefly turns the hit colour when a shot damages something.
    /// </summary>
    public sealed class CrosshairUI : MonoBehaviour
    {
        [Tooltip("Shooter to listen to for hits. Found in parents if left empty.")]
        [SerializeField] private PlayerShooting shooter;
        [SerializeField] private Graphic[] parts;
        [SerializeField] private Color normalColor = new Color(1f, 1f, 1f, 0.9f);
        [SerializeField] private Color hitColor = new Color(1f, 0.25f, 0.2f, 1f);
        [SerializeField, Min(0f)] private float hitFlashDuration = 0.15f;

        private float hitFlashEndTime;
        private bool isShowingHit;

        private void Awake()
        {
            if (shooter == null)
                shooter = GetComponentInParent<PlayerShooting>();

            SetColor(normalColor);
        }

        private void OnEnable()
        {
            if (shooter != null)
                shooter.ShotFired += HandleShotFired;
        }

        private void OnDisable()
        {
            if (shooter != null)
                shooter.ShotFired -= HandleShotFired;
        }

        private void Update()
        {
            if (isShowingHit && Time.unscaledTime >= hitFlashEndTime)
            {
                isShowingHit = false;
                SetColor(normalColor);
            }
        }

        private void HandleShotFired(Vector3 origin, Vector3 endPoint, IDamageable target)
        {
            if (target == null)
                return;

            isShowingHit = true;
            hitFlashEndTime = Time.unscaledTime + hitFlashDuration;
            SetColor(hitColor);
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
