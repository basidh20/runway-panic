using RunwayPanic.Interaction;
using UnityEngine;

namespace RunwayPanic.Audio
{
    /// <summary>
    /// A movable barricade's scrape along the ground while it is pushed and a thud when it settles (on the barricade
    /// prefab, next to <see cref="Barricade"/>).
    /// </summary>
    [RequireComponent(typeof(Barricade))]
    public sealed class BarricadeSounds : MonoBehaviour
    {
        [SerializeField] private SoundCue scrape;
        [SerializeField] private SoundCue settle;

        private Barricade barricade;
        private SoundLoop scrapeLoop;

        private void Awake() => barricade = GetComponent<Barricade>();

        private void OnEnable() => barricade.Settled += HandleSettled;

        private void OnDisable()
        {
            barricade.Settled -= HandleSettled;
            StopScrape();
        }

        private void Update()
        {
            if (barricade.IsBeingPushed)
            {
                if (scrapeLoop == null)
                    scrapeLoop = SoundPlayer.StartLoop(scrape, transform);
            }
            else
            {
                StopScrape();
            }
        }

        private void HandleSettled(Bounds bounds) => SoundPlayer.Play(settle, bounds.center);

        private void StopScrape()
        {
            if (scrapeLoop != null)
                scrapeLoop.Stop();
            scrapeLoop = null;
        }
    }
}
