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

## C2: grass and existing landmarks

The existing flat ground has a repeatable grass-coloured texture, generated
locally by `MainAirportDressing.cs` from periodic noise; it is not a downloaded
texture. Short 3D grass blades are combined into a small set of mesh chunks,
with no added colliders or cast shadows. Placement avoids the runway, aprons,
building and aircraft footprints, and a clear strip around the runway-side
Gull FOD points. The original ground collider and navigation geometry remain.
There are no new cones, carts or other props in this pass.

The runway, shelter and hangar remain recognizable landmarks with their existing
signs, doors and C1 materials. A slim, non-colliding yellow frame accents the
hangar's main entrance without changing its walls or opening. The enterable hangar is still the Unity-built
placeholder, not a downloaded model. The modeller's final terminal and aircraft
are pending; retain the placeholders until they are delivered. When replacing
them, review their footprints, colliders and matching Hawk/Pigeon marker surfaces
before the final lighting bake. Bird behaviour and wave settings are unchanged.

The initial grass look was reviewed in an editor-camera image from
(-10, 5, -27) facing (-20, 0.5, 4); the checker-like ground pattern was replaced
with softer noise. This is not a gameplay walk-through or a performance test.
The player should check the approaches, shelter doors, hangar entrances and
visibility of objects in Play mode.
