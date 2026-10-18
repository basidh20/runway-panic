# Airport greybox — first world-building milestone

Scene: `Assets/_Project/Scenes/Main.unity`  
Unity: `6000.6.4f1`  
Current integration workflow: task-specific feature branches → `develop` → `main` for release.

## Current Main scene status

The first gameplay integration is documented in `PlayerIntegration.md`: Main now uses S2's connected gameplay player and GameManager prefabs, and `GreyboxPreview_TEMPORARY` is retained but disabled. `CheckpointIntegration.md` records the follow-up: the two original checkpoint markers now have active triggers, runway-facing respawns and visible floor markings. `ShelterDoorIntegration.md` records the connected S2 doors now fitted to both shelters. The greybox geometry below remains the scale/layout reference. Historical cube-stage reports and first-person checks are not final gameplay acceptance.

## What is built

Bird-work reference rule: consult the parent workspace's `BIRD_STRATEGIES.md` for bird-related tasks; also consult `01_GULL_PLAN.md` for Gull work. The user's current three-wave/four-species decision takes precedence over the strategy document's older four-wave example.

- Solid 100 x 80 m ground and perimeter walls.
- Central 75 x 14 m runway (extended by the student), with aligned threshold bars, centre stripes, edge markings, and runway numbers.
- Four recognisable aircraft greyboxes under Environment/Aircraft, refined from the student's parking cubes. Plane A uses a 12 m-long / 9 m-span utility-aircraft prefab; B/C/D share a 9 m-long / 8 m-span light-aircraft prefab. Each has fuselage, cockpit, wings, tail, engines, wheels, and compound collision. Parking was adjusted for wing clearance and terminal/hangar routes. See `AircraftArtHandoff.md` for final-model specifications.
- An enterable Hangar_Greybox under Environment/Buildings, converted from the student's massing cube. The original 14 x 25 m footprint and 6 m exterior height are preserved. Its reusable P_Greybox_Hangar prefab has separate walls, floor, roof, a 10 m wide / 4.8 m high runway-facing entrance, and a 2.8 m wide / 3.2 m high north-side exit. Interior lights and flush maintenance-bay markings leave the central path clear.
- Two 10 x 8 m shelter prefabs at opposite sides of the runway. Each contains a floor, separate walls and roof. Main has scene-only overrides fitting each entrance with a 1.4 x 2.4 m interactable hinged door, frame, low threshold and opaque flexible edge seals; the shared greybox prefab retains its original opening.
- Six barricade prefab instances placed beside the runway, with routes left open.
- A simple control-tower and terminal placeholder to establish airport scale and landmarks.
- Basic reusable URP colour materials and realtime preview lighting.
- Player-start, eight bird-spawn, four pickup, two checkpoint, and reserved-shop markers. Checkpoints are functional; the four pickup markers feed the shared spawner (see `PickupIntegration.md`). The shared between-wave shop screen is installed (`ShopIntegration.md`), but is not a physical vendor at the reserved marker. The eight bird-spawn markers are now typed `BirdPoint` Spawn points and Main has a shared FlightGrid (`FlightIntegration.md`); production bird spawning is not implemented yet.
- A temporary first-person walkthrough rig and overview camera.

The greybox is made of ordinary saved Unity objects and reusable prefabs. It is not generated on every Play. You can edit positions and sizes directly in the Inspector.

## Explore the original preview (historical controls)

Main now starts with the third-person gameplay player. For its controls and checks see `PlayerIntegration.md`. The instructions below describe the old preview only; never enable it alongside the gameplay player.

1. Open Main from the Project panel and press Play.
2. Click the Game view to give it keyboard focus.
3. Press Tab, or click the on-screen button, to change between overview and walking.
4. WASD moves; mouse looks; Left Shift sprints; Space jumps.
5. Press 1 for the runway, 2/3 for shelter entrances, 4 for the hangar entrance, or 5 for the aircraft aisle. Walk forward into the hangar; the EXIT sign marks its secondary escape route.
6. Press Escape to release the cursor; click below the on-screen panel to capture it again.
7. Stop Play before making permanent scene changes.

The temporary controller starts in overview mode and remains available only as a historical scale/collision reference. It is now disabled in favour of S2's gameplay player/camera. Keep only the intended gameplay camera/audio listener active.

## Validate after layout edits

Outside Play mode, open Main and choose Tools > Runway Panic > Validate Airport Greybox.

The validation report is written to `Docs/WorldBuilder/GreyboxValidation.txt`. It checks material assignments, ground support, marker/cover counts, passage into and out of both shelters, wall/perimeter blocking, and landing on the ground using an actual 1.8 m CharacterController. Revisit the test positions if you deliberately relocate the shelters.

Play-mode smoke checks also verified switching from overview to walking and walking inside Shelter A. The final third-person controller and camera still need a separate walkthrough once available.

The earlier cube-stage additions review is recorded in `Docs/WorldBuilder/GreyboxAdditionsReview.txt`. BirdSpawn_05 was moved away from the hangar to (-47, 8, 0). That report is a historical snapshot; the Adjust User Greybox Additions menu now refuses to run on the converted hangar.

Use Tools > Runway Panic > Validate Enterable Hangar after hangar edits. `Docs/WorldBuilder/HangarValidation.txt` records entry and exit through both openings, wall/roof blocking, floor support, obstacle/perimeter clearance, and bird-spawn clearance. All checks passed. Play-mode walking through the main entrance and out of the side exit was also verified. The Make Hangar Enterable menu is a one-off converter and refuses to replace an existing hangar prefab.

The hangar is ordinary geometric cover, not an extra automatic safe room: it has no invulnerability trigger, closable doors, timers, or enemy-navigation behaviour. The wide main opening keeps part of the interior exposed and the side exit avoids a dead-end retreat. Integrate and test S2's third-person camera separately; the temporary walkthrough is first-person. Final models and gameplay can reuse this footprint and open routes.

Use Tools > Runway Panic > Validate Aircraft Greyboxes after plane edits. `AircraftValidation.txt` records aircraft grounding, obstacle intersections, runway/perimeter clearance, wing collision, routes around each nose, parking aisles, and runway traversal. All tests passed after moving D clear of the terminal-front route. The final-model dimensions, positions, collision contract, and remaining integration tests are in `AircraftArtHandoff.md`. Aircraft are parked props, not flyable or enterable vehicles.

## Why the layout is arranged this way

- The central runway gives a clear open combat space and long sight lines.
- Offset shelters on opposite sides encourage crossing and make the locations distinct.
- Cover groups sit beside the runway rather than closing off the central movement route.
- Generous doorways allow initial human-sized collision testing and room for later camera testing.
- The perimeter contains the preview player while placeholders establish background scale.
- Shared prefabs make later geometry/material improvements easy to apply consistently.

## Next milestones

Main now has the shared 3D FlightGrid/A*, typed spawn points, eight Hawk vantage markers and the connected Crow stash/zone/exit setup; see `FlightIntegration.md`, `HawkPerchIntegration.md` and `CrowSetupIntegration.md`. Do not rebuild navigation from the older NavMesh-sampling proposal. Next are Gull/Pigeon behaviour markers, then production waves (three waves with four species), movable cover/safe-room integration and final environment presentation. Bird movement/animation and Hawk/Crow brains already exist, but full Main gameplay still needs their deliberate integration.

The builder menu creates Main only when it does not already exist; it deliberately refuses to overwrite a saved scene. Use normal scene editing for future work. The teammate's Sandbox_Basidh scene and existing render/input configuration are preserved. Main is added as the first build scene, with the existing sandbox entry retained.

## Git handoff

The initial greybox is already merged into `develop`. The first gameplay integration uses `feature/world-player-integration`; subsequent tasks should have their own focused commits/branches. PRs target `develop`, not directly `main`. Include asset `.meta` partners; exclude Library, Logs and builds. No commit or push is performed automatically.
