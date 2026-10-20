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

The ground uses the grass texture from Hipernt's [Anime Nature](https://assetstore.unity.com/packages/3d/environments/anime-nature-276931)
(`Assets/Anime Trees/Textures/Grass.png` in the downloaded package). Grass
tufts use the alpha-cutout billboard texture from FlipGameDev's [Terrain & Grass Pack](https://assetstore.unity.com/packages/3d/environments/terrain-grass-pack-379910)
(`Assets/FlipGameDev/Terrain&GrassPack/Art/Textures/Grass_Billboard/Grass_1_Billboard.png`).
Only these two textures were brought into the project; no other pack contents
or scripts are needed. They are third-party assets and their Unity Asset Store
licence terms should be checked when distributing the project. Grass cards are
combined into mesh chunks with no added colliders or cast shadows. Overlapping
cards give the open ground denser, more continuous coverage. Placement avoids the
runway, aprons, building and aircraft footprints. The grass now extends toward
the runway-side Gull FOD points; its ability to conceal pickup spawning depends
on viewing angle and needs a gameplay check. The original ground collider and
navigation geometry remain.
There are no new cones, carts or other props in this pass.

The runway, shelter and hangar remain recognizable landmarks with their existing
signs, doors and C1 materials. A slim, non-colliding yellow frame accents the
hangar's main entrance without changing its walls or opening. The enterable hangar is still the Unity-built
placeholder, not a downloaded model. The modeller's final terminal is pending.
All four aircraft now use the modeller's parked-aircraft prefab. Plane A uses
the grey/teal model at 1.35 scale, about 21% larger than its first replacement;
the other three use the modeller's placed variants. Plane A's wing roost and tail perch were
aligned to the new collision surfaces. The focused aircraft clearance check
passed after excluding the stationary gameplay player from its simulated walk;
an actual bird landing and gameplay walk still need checking. Check grass around
the enlarged aircraft in gameplay. Review ground navigation after the collision
change and before the final lighting bake. Bird
behaviour and wave settings are unchanged.

The initial grass look was reviewed in an editor-camera image from
(-10, 5, -27) facing (-20, 0.5, 4). This is not a gameplay walk-through or a performance test.
The player should check the approaches, shelter doors, hangar entrances and
visibility of objects in Play mode.

## C3: outdoor daylight

Main keeps its existing directional sun, skybox, sun rotation and shadow
settings. The sun intensity is 1.25 and the three ambient colours are gently
lifted to soften dark sides while retaining a warm daylight palette. Hawk AI
still refers to the same sun. No fog, post-processing or baked lighting was
added in this step. The outdoor editor-camera preview was inspected; gameplay
aiming, bird shadows and interior visibility still need a player review.

### Interior lighting review

Both shelter instances already have one enabled, shadow-free point light;
the hangar has two. An editor-camera view from player height in Shelter A and
another in the hangar showed the walls, floor and shelter door clearly, so no
additional lights or changes to the existing prefabs were added. Recreate the
temporary views with `Tools > Runway Panic > Capture Interior Lighting Views`;
they are written outside the repository to `/private/tmp/RunwayPanic-C3/`.
The shared shelter prefab subsequently received a small, non-colliding bulb
and cable at its existing light, giving both shelters a visible fixture without
adding another light source. The bulb uses a bright unlit material, while the
warm point light at the bulb's centre lights the room. The fixture hangs below
the ceiling to reduce the concentrated bright patch above it.
The hangar similarly has two non-colliding hanging bulbs, aligned with its
existing point lights; those lights retain their original intensity and range.
Shelter B, pickup visibility, closed/open door operation and the interiors in
normal gameplay still require a player check. The Shelter A preview also shows
reversed text visible through the wall; check whether this appears in gameplay
before making a separate signage/UI fix.

## C4: lighting bake readiness (19 October 2026)

A read-only audit of saved Main in Unity 6000.6.4f1 found 212 active environment
mesh renderers, none marked Contribute GI, and no loaded lightmaps. There are no
active Light Probe Groups or Reflection Probes. The directional sun and all
four interior point lights are realtime. There is no scene-assigned Lighting
Settings asset. No bake or lighting configuration change was made in this step.

The aircraft meshes and built-in cubes have a second UV channel; this confirms
presence only, not that the unwrap is suitable or non-overlapping. Grass chunks
and the built-in fixture spheres and cylinders lack that channel. Eight sign and
runway-text renderers have no MeshFilter mesh available to this audit, so they
need separate review, not an assumption that their visible text is broken.
The aircraft FBX importer already has Generate Lightmap UVs enabled; no import
setting was changed.

Proposed preview: choose only stable, non-moving environment renderers with
suitable UVs to contribute GI, keep grass, doors, movable cover, player and
birds dynamic, and place light probes for dynamic objects. Investigate a mixed
sun so dynamic shadows remain visible, and compare a small low-quality preview
before deciding how to bake interior lights. Verify the project's URP settings
and actual preview results; this is not a completed bake. The final terminal
may require another bake once integrated.

The ground and runway base are now marked Contribute GI as the first small
preparation step. Their built-in cube meshes have a second UV channel. No
other objects, lights or lighting settings were changed, and this does not yet
create a bake. Continue by checking the remaining fixed geometry and setting up
preview lighting and probes; the terminal is still pending.

The two shelter instances and the hangar now also have Contribute GI enabled
on 28 selected fixed floors, walls, roofs and doorway framing pieces in Main.
These are scene-instance overrides, not changes to the source prefabs. Doors,
fixtures, signs and movable objects were not marked. The current realtime lights
remain unchanged; a preview bake is still needed to assess seams and leaks.

Main now has one `Airport_LightProbes` group with 244 positions: a sparse
outdoor grid at player and flying-bird heights, plus denser samples inside and
near the entrances of both shelters and the hangar. Placement skips points
intersecting colliders and near-duplicates. These are initial positions only:
they have not been baked or visually validated, and interior/exterior light
leakage and dynamic actor appearance must be checked in the preview bake.

The preview uses `MainPreviewLighting.asset`: Progressive CPU lightmapper,
4 texels per unit, 1024 maximum atlas size, 16 direct / 32 indirect / 32
environment samples, one bounce and a probe multiplier of one. The sun is
Mixed with Baked Indirect mode, keeping direct illumination and shadows
realtime; interior point lights remain realtime. PC and mobile URP assets have
mixed lighting enabled. Unity 6000.6 labels Progressive CPU deprecated, so a
later quality bake should consider the Unity Compute Light Baker if supported.
This low-quality configuration is for assessment, not the final deliverable.

On 20 October 2026, the preview bake completed in 37.78 seconds and saved one
1024x1024 baked lightmap (2.7 MB) plus lighting data under
`Assets/_Project/Scenes/Main/`. Main now references that data. This confirms
the pipeline saves successfully; it is not the final quality bake. The gameplay
view remained readable at the shop/player camera after the bake, but shelter
and hangar interiors still need a targeted player-height check before final
lighting is approved.

To repeat the audit, open saved Main outside Play mode and choose
`Tools > Runway Panic > Audit Main Lighting Bake Readiness`. The detailed report
is written to `/private/tmp/RunwayPanic-C4/lighting-audit.txt` on this Mac and
is not a project asset.
