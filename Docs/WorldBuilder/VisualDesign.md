# Airport visual design

## C1: surface materials and markings

The airport keeps its existing stylized, low-poly shapes and daylight. Neutral
asphalt, concrete and muted painted metal distinguish the runway, apron and
buildings; white runway paint and restrained yellow taxiway markings provide
wayfinding. Existing sky, sun, post-processing, collision layout and gameplay
systems were not intentionally changed as part of C1.

The five 256 x 256 repeating albedo textures in
`Assets/_Project/Art/Textures/WorldSurfaces` were generated specifically for
this project by `MainAirportSurfacePolish.cs`, using deterministic procedural
noise and simple panel/joint patterns. They do not use downloaded images or
third-party texture assets. Their matching URP Lit materials are in
`Assets/_Project/Art/Materials/WorldSurfaces`. Asphalt, concrete and slabs use
rough surfaces; hangar and roof panels have a modest metallic response. The
maps repeat with mipmaps to suit the different surfaces. No normal maps are
included. Other teams' character, bird and weapon materials were not edited.

The long white edge markings now follow the existing runway length, and eight
yellow taxiway dashes have no colliders. Shelter and hangar walls retain their
existing meshes and collision. The airport buildings are still the existing
greybox structures; C2 covers any later model integration or dressing.

## Fixed comparison views

Before and after previews were captured from the following world-space camera
positions, looking at the listed targets (field of view 60 degrees):

| View | Position | Target |
| --- | --- | --- |
| Runway | (0, 5, -30) | (0, 0.5, 3) |
| Shelter | (-9, 4, 20) | (-21, 1.7, 12) |
| Hangar | (-22, 5, -21) | (-38, 2.5, -13) |

These are editor camera renders, not a substitute for a gameplay walk-through.
With saved Main open outside Play mode, `Tools > Runway Panic > Capture Airport
Surface Views` captures the same views to `/private/tmp/RunwayPanic-C1/` on this
Mac; this temporary folder is not part of the repository. Lighting and a saved
light bake are separate C3 and C4 tasks.
