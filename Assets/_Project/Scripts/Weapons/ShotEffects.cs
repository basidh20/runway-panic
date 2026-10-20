using RunwayPanic.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace RunwayPanic.Weapons
{
    /// <summary>
    /// Makes every shot visible: a muzzle flash (a short tapered streak out of the barrel plus a brief light), a
    /// tracer from the muzzle to where the bullet landed, and a spark where it hit something. Listens to
    /// <see cref="PlayerShooting.ShotFired"/>; uses pooled LineRenderers with one additive material, so a shot costs
    /// no allocations and no textures.
    /// </summary>
    [RequireComponent(typeof(PlayerShooting))]
    public sealed class ShotEffects : MonoBehaviour
    {
        private const int LinesPerShot = 4; // flash, tracer, two impact strokes

        [Tooltip("Additive unlit material shared by every effect (M_ShotEffect).")]
        [SerializeField] private Material effectMaterial;
        [Tooltip("Shots whose effects can be on screen at once (fast guns need more).")]
        [SerializeField, Min(1)] private int maxShots = 8;

        [Header("Muzzle flash")]
        [SerializeField] private Color flashColor = new Color(1f, 0.82f, 0.45f);
        [SerializeField, Min(0.01f)] private float flashDuration = 0.05f;
        [Tooltip("Length of the flash streak in front of the muzzle, in metres (varies ±20% per shot).")]
        [SerializeField, Min(0.01f)] private float flashLength = 0.28f;
        [SerializeField, Min(0.01f)] private float flashWidth = 0.14f;
        [SerializeField, Min(0f)] private float lightIntensity = 4f;
        [SerializeField, Min(0.1f)] private float lightRange = 5f;

        [Header("Tracer")]
        [SerializeField] private Color tracerColor = new Color(1f, 0.72f, 0.35f);
        [SerializeField, Min(0.01f)] private float tracerDuration = 0.07f;
        [SerializeField, Min(0.001f)] private float tracerWidth = 0.025f;

        [Header("Impact")]
        [SerializeField] private Color impactColor = new Color(1f, 0.9f, 0.6f);
        [SerializeField, Min(0.01f)] private float impactDuration = 0.08f;
        [SerializeField, Min(0.01f)] private float impactSize = 0.18f;

        private struct Effect
        {
            public LineRenderer Line;
            public Color Color;
            public float Start;
            public float Duration;
        }

        private PlayerShooting shooting;
        private PlayerWeaponHolder weaponHolder;
        private Effect[] effects;
        private int next;
        private Light flashLight;
        private float lightOffTime;
        private AnimationCurve flashTaper;
        private AnimationCurve constantWidth;

        private void Awake()
        {
            shooting = GetComponent<PlayerShooting>();
            weaponHolder = GetComponent<PlayerWeaponHolder>();
            if (effectMaterial == null)
            {
                Debug.LogWarning($"{nameof(ShotEffects)} on '{name}' has no effect material; shots will not show.", this);
                enabled = false;
                return;
            }

            flashTaper = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0f));
            constantWidth = AnimationCurve.Constant(0f, 1f, 1f);
            var root = new GameObject("[ShotEffects]").transform; // world space, so effects don't follow the player
            effects = new Effect[maxShots * LinesPerShot];
            for (int i = 0; i < effects.Length; i++)
                effects[i].Line = CreateLine(root, i);

            flashLight = new GameObject("MuzzleFlashLight").AddComponent<Light>();
            flashLight.transform.SetParent(root, false);
            flashLight.type = LightType.Point;
            flashLight.color = flashColor;
            flashLight.range = lightRange;
            flashLight.intensity = lightIntensity;
            flashLight.shadows = LightShadows.None;
            flashLight.enabled = false;
        }

        private void OnEnable()
        {
            if (shooting != null)
                shooting.ShotFired += HandleShotFired;
        }

        private void OnDisable()
        {
            if (shooting != null)
                shooting.ShotFired -= HandleShotFired;
        }

        private void OnDestroy()
        {
            if (flashLight != null)
                Destroy(flashLight.transform.parent.gameObject);
        }

        private void HandleShotFired(Vector3 muzzle, Vector3 end, IDamageable target)
        {
            WeaponBase weapon = weaponHolder != null ? weaponHolder.CurrentWeapon : null;
            if (weapon != null && weapon.Data != null && weapon.Data.IsProjectile)
                return; // a rocket brings its own launch flash, trail and explosion

            Vector3 toEnd = end - muzzle;
            Vector3 barrel = weapon != null ? weapon.Muzzle.forward : toEnd.normalized;

            // Muzzle flash: a tapered streak along the barrel, slightly different every shot.
            float length = flashLength * Random.Range(0.8f, 1.2f);
            Show(muzzle, muzzle + barrel * length, flashWidth, flashTaper, flashColor, flashDuration);

            // Tracer: from just past the flash to the impact point.
            float distance = toEnd.magnitude;
            if (distance > length)
                Show(muzzle + toEnd / distance * length, end, tracerWidth, null, tracerColor, tracerDuration);

            // Spark where the shot hit something (a miss ends exactly at the gun's range).
            bool hit = target != null || (weapon != null && distance < weapon.Range - 0.5f);
            if (hit)
            {
                Vector3 a = Random.onUnitSphere * impactSize, b = Vector3.Cross(a, toEnd).normalized * impactSize;
                Show(end - a, end + a, tracerWidth * 2f, flashTaper, impactColor, impactDuration);
                Show(end - b, end + b, tracerWidth * 2f, flashTaper, impactColor, impactDuration);
            }

            flashLight.transform.position = muzzle + barrel * 0.1f;
            flashLight.enabled = true;
            lightOffTime = Time.time + flashDuration;
        }

        private void Update()
        {
            if (flashLight.enabled && Time.time >= lightOffTime)
                flashLight.enabled = false;

            for (int i = 0; i < effects.Length; i++)
            {
                LineRenderer line = effects[i].Line;
                if (!line.enabled)
                    continue;

                float age = (Time.time - effects[i].Start) / effects[i].Duration;
                if (age >= 1f)
                {
                    line.enabled = false;
                    continue;
                }

                // Additive: fading the colour to black fades the effect out.
                Color c = effects[i].Color * (1f - age);
                line.startColor = c;
                line.endColor = c;
            }
        }

        private void Show(Vector3 from, Vector3 to, float width, AnimationCurve taper, Color color, float duration)
        {
            ref Effect effect = ref effects[next];
            next = (next + 1) % effects.Length;

            LineRenderer line = effect.Line;
            line.SetPosition(0, from);
            line.SetPosition(1, to);
            line.widthMultiplier = width;
            line.widthCurve = taper ?? constantWidth;
            line.startColor = color;
            line.endColor = color;
            line.enabled = true;

            effect.Color = color;
            effect.Start = Time.time;
            effect.Duration = duration;
        }

        private LineRenderer CreateLine(Transform parent, int index)
        {
            var line = new GameObject("ShotLine" + index).AddComponent<LineRenderer>();
            line.transform.SetParent(parent, false);
            line.sharedMaterial = effectMaterial;
            line.useWorldSpace = true;
            line.positionCount = 2;
            line.alignment = LineAlignment.View;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.enabled = false;
            return line;
        }
    }
}
