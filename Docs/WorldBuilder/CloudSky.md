# Cloudy sky background

## Asset and credits

- Asset: **Sunflowers (Pure Sky)**, Poly Haven.
- Source: https://polyhaven.com/a/sunflowers_puresky
- Authors listed by the source: Jarod Guest (sky edits), Sergej Majboroda (original).
- Licence: CC0 — https://polyhaven.com/license
- Imported file: `Assets/_Project/Art/Textures/Sky/sunflowers_puresky_4k.hdr`.
- Valid Radiance HDR, 4096 x 2048 (2:1 panoramic image), copied unchanged from
  the owner's Downloads folder. The source file remains in Downloads.
- This is a bright, partly cloudy midday panorama, not an evening photograph.
  The airport retains its existing warm late-afternoon lighting.

## Setup and review

Main uses `M_CloudyAfternoonSky` with Unity's Skybox/Panoramic shader. The image
is in the Spherical (HDR) slot, latitude/longitude mapping, mono, full 360 degrees.
Initial Exposure is 1, Rotation is 0, with a slight warm tint.

The project's ordinary surface-texture importer was reducing this panorama to
1K and applying the standard colour-texture rule. A narrow exception for HDR/EXR
files under `Art/Textures/Sky/` preserves 4K, linear HDR colour, mipmaps and
trilinear filtering. Other art folders retain their original import behaviour.
No global importer version bump or blanket texture reimport was requested.

Applied through `Tools > Runway Panic > Apply Cloudy Sky Background` and saved.
Separate editor-camera ground/upward previews confirmed visible cloud detail.
The sole Main scene change is its skybox material reference. Sun transform,
intensity and colour, ambient settings, interior lights, cameras and existing
baked data were retained. No lighting bake, gameplay run or new build was done.

The clouds are a static distant background, not objects at an assigned altitude.
They do not introduce collisions, moving cloud shadows or new bird behaviour.
The photographed sun's elevation is part of the image, not a new gameplay light;
the existing directional sun is deliberately not reoriented to match it.

## Manual tweaks and reverting

Stop Play mode, search the Project window for `M_CloudyAfternoonSky`, then use:

- **Exposure**: sky brightness only.
- **Tint Color**: sky colour balance.
- **Rotation**: horizontal arrangement of clouds; leave the directional sun alone.

Save Project after material edits. Rerunning Apply preserves those existing
material adjustments. Do not rerun Apply Bright Late Afternoon just to tweak
clouds: that older command assigns the previous procedural sky.

To revert only the background, open Window > Rendering > Lighting > Environment
and assign `M_BrightAfternoonSky` to Skybox Material, then save Main. Both sky
materials are retained. The HDR extension is already covered by repository LFS
rules; stage the new texture and its meta file normally.
