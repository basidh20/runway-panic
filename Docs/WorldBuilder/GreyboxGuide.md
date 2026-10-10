# Airport greybox — first world-building milestone

Scene: `Assets/_Project/Scenes/Main.unity`  
Unity: `6000.6.4f1`  
Branch: `feature/world-greybox`

## What is built

- Solid 100 x 80 m ground and perimeter walls.
- Central 60 x 14 m runway, threshold bars, edge markings, and runway numbers.
- Two 10 x 8 m shelter prefabs at opposite sides of the runway. Each contains a floor, separate walls, roof, and a 2.8 m wide / 3 m high open entrance.
- Six barricade prefab instances placed beside the runway, with routes left open.
- A simple control-tower and terminal placeholder to establish airport scale and landmarks.
- Basic reusable URP colour materials and realtime preview lighting.
- Player-start, eight bird-spawn, four pickup, two checkpoint, and reserved-shop markers. These are positions only, not functioning gameplay systems.
- A temporary first-person walkthrough rig and overview camera.

The greybox is made of ordinary saved Unity objects and reusable prefabs. It is not generated on every Play. You can edit positions and sizes directly in the Inspector.

## Explore it

1. Open Main from the Project panel and press Play.
2. Click the Game view to give it keyboard focus.
3. Press Tab, or click the on-screen button, to change between overview and walking.
4. WASD moves; mouse looks; Left Shift sprints; Space jumps.
5. Press 1 to return to the runway start, 2 for Shelter A's entrance, or 3 for Shelter B's entrance.
6. Press Escape to release the cursor; click below the on-screen panel to capture it again.
7. Stop Play before making permanent scene changes.

The temporary controller starts in overview mode. It is for scale and collision checks, not the finished player system. Once S2 delivers their actual player/camera prefab, disable the GreyboxPreview_TEMPORARY root and place their prefab at PlayerStart. Keep only the intended gameplay camera/audio listener active.

## Validate after layout edits

Outside Play mode, open Main and choose Tools > Runway Panic > Validate Airport Greybox.

The validation report is written to `Docs/WorldBuilder/GreyboxValidation.txt`. It checks material assignments, ground support, marker/cover counts, passage into and out of both shelters, wall/perimeter blocking, and landing on the ground using an actual 1.8 m CharacterController. Revisit the test positions if you deliberately relocate the shelters.

Play-mode smoke checks also verified switching from overview to walking and walking inside Shelter A. The final third-person controller and camera still need a separate walkthrough once available.

## Why the layout is arranged this way

- The central runway gives a clear open combat space and long sight lines.
- Offset shelters on opposite sides encourage crossing and make the locations distinct.
- Cover groups sit beside the runway rather than closing off the central movement route.
- Generous doorways allow initial human-sized collision testing and room for later camera testing.
- The perimeter contains the preview player while placeholders establish background scale.
- Shared prefabs make later geometry/material improvements easy to apply consistently.

## Next milestones

NavMesh/grid generation, bird AI, interaction scripts, final models/textures, and baked lighting have not been implemented in this greybox step. Initial NavMesh baking is the next environment milestone.

The builder menu creates Main only when it does not already exist; it deliberately refuses to overwrite a saved scene. Use normal scene editing for future work. The teammate's Sandbox_Basidh scene and existing render/input configuration are preserved. Main is added as the first build scene, with the existing sandbox entry retained.

## Git handoff

Changes are on the feature branch. Review the files before committing with a message such as:

`feat(level): build airport greybox and walkthrough preview`

Include the assets and their .meta files. Do not add Library, Logs, or generated builds. No commit or push is performed automatically for this milestone.
