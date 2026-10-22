# Hangar interior lighting polish

Applied in Main on `feature/video-ui-polish` using
`Tools > Runway Panic > Improve Hangar Interior Lighting`.

- Retained the two original lamps and their ceiling positions. Reduced their
  point-light intensity from 3 to 1.1 and range from 15m to 11m to reduce ceiling
  hotspots while retaining a little surrounding fill.
- Added one downward spotlight just beneath each bulb: intensity 45, range
  18m, outer/inner angles 120/90 degrees, warm neutral colour.
- Each spotlight uses hard realtime shadows at a custom 512px resolution,
  strength 0.65. This limits the setup to two spotlight shadow views rather
  than enabling omnidirectional point-light shadows. Performance was not profiled.
- Assigned dedicated HDR-emissive `M_Hangar_Bulb_Emissive` material to the
  hangar bulbs only; shelter bulbs are unchanged. URP requires an emissive GI
  flag to retain the emission keyword. Existing bloom handles the visible glow;
  no global post-processing settings were changed.
- Enlarged the bulbs slightly and added non-colliding metal sockets/shades.
  Fixture renderers do not cast shadows, avoiding self-shadowing light sources.

Changes are Main scene-instance overrides, not a rebuild of the hangar prefab.
No sun, camera, navigation, collision, gameplay or existing baked lighting data
was changed. No new lighting bake was run. The added realtime lights illuminate
surfaces; the emissive material provides visible brightness, not realtime GI.

## Checks

The editor command compiled and saved Main. Separate wide and close-up editor
camera previews confirmed floor lighting and bright visible bulbs. A shadow
on/off comparison was captured with shadow settings restored afterward; the
shadow difference was subtle in these stills. A Play-mode walkthrough and
performance approval remain for the owner; no full tests or build were run.

## Simple adjustments

1. Stop Play mode. Expand `Environment > Buildings > Hangar_Greybox >
   P_Greybox_Hangar` in the Hierarchy.
2. Each `Interior_Preview_Light` contains a `Downlight`. Select it and adjust
   **Light > Intensity** to brighten/dim the floor. Keep range/angles unchanged
   unless deliberately altering the coverage.
3. To reduce cost if needed, set both Downlights' **Shadow Type** to **No Shadows**.
4. For bulb glow, select `M_Hangar_Bulb_Emissive` in the Project window and
   adjust **Emission > HDR colour/intensity**. Do not edit the shared shelter
   bulb material for this.
5. Save Main and choose File > Save Project. No bake is needed for these
   realtime-light or visual-emission adjustments.

The Apply command resets light/fixture values to the defaults above when rerun;
normal Inspector edits do not require rerunning it. Existing bulb-material
colour choices are preserved on rerun.
