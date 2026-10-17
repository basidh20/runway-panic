# Main — gameplay player integration (GV milestone 1)

Unity: **6000.6.4f1**. Feature branch: **feature/world-player-integration**. PR target: **develop**, not main.

## Scope

- Reuse S2's connected `P_Player_Gameplay` prefab at `GameplayMarkers/PlayerStart` (0, 0.12, -24), facing +Z.
- Reuse one connected `P_GameManager`; explicitly bind its `playerLives` to this player's `PlayerLifeManager` as a scene override.
- Keep `GreyboxPreview_TEMPORARY`, but disable its entire root so its controller, cameras, listener and overlay cannot interfere.
- Preserve the airport geometry, all existing markers, materials, shared gameplay scripts, original prefabs and build settings.
- Supply explicit Editor-only integration/validation tools and repeatable Play-mode smoke checks. These are not runtime level generation.

Only this milestone is installed. Shelter doors/checkpoint triggers, shop/pickup spawner, flight grid/AI markers, wave spawning and safe-room protection remain subsequent integration tasks. Having the GameManager in Main does not start a timed wave. No enemies are spawned by this milestone.

## Play Main

1. Open `Assets/_Project/Scenes/Main.unity`, press Play and click the Game view.
2. Use WASD, mouse-look, Shift to sprint, Space to jump and left mouse to fire the starting pistol. Escape opens the supplied pause screen; Resume returns to play.
3. The old Tab/1–5 overview/teleport controls belong to the disabled greybox preview and are deliberately not active.
4. Stop Play before permanent changes. Do not enable the old preview while the gameplay player is enabled: that adds competing cameras/listeners and an extra controller.

The visible officer, gun, camera and HUD come from the Systems Engineer's and modeller's prefabs, not newly copied implementations. Further animation/camera/HUD polish in those shared prefabs belongs with their owners.

If the Game view crops the HUD, reduce its **Scale** to fit the panel: the inspected Editor was zoomed to 1.2x at Full HD. That viewport zoom is not a reason to move the shared HUD anchors. The officer currently uses the supplied rest pose; final player locomotion/weapon-holding animation is not supplied by this milestone.

## Repeatable checks

Open saved Main outside Play, then use **Tools > Runway Panic**:

- **Integrate Gameplay Player in Main**: preflight the scene, instantiate only missing approved prefabs, bind lives and disable the preview, validate and save. Re-running does not duplicate or reposition an existing player. Unexpected player/manager assets or duplicates cause a refusal, not silent replacement. Save again if a camera's Editor initialization subsequently marks the scene dirty.
- **Validate Main Player Integration**: writes `MainPlayerValidation.txt`. Checks exactly one player/manager, connected prefab sources, startup placement, camera/listener counts, Input System actions, lives binding, missing scripts and build-scene presence.
- **Run Main Player Smoke Test**: requires saved Main, enters Play and returns to Edit mode. It temporarily pairs a virtual keyboard/mouse to exercise W, Shift and Space; tests actual camera/muzzle raycasts through `TryShoot()` against a runtime-only damage target; checks startup, grounding, HP/lives, pause UI and Resume. It also moves the actual gameplay CharacterController through both open shelter entrances and the two hangar openings. Writes `MainPlayerPlaytest.txt`.

The test does not save Play-mode changes or create permanent targets/input devices. Passage checks validate physical controller clearance; they are not proof of camera occlusion correctness at every angle. Read the reports for actual results and explicitly untested areas. The test is opt-in, not run automatically by normal Play. Do not provide keyboard/mouse input while it runs.

No final player-animation, bird-combat, wave, shop, pickup, checkpoint-activation or full camera-clearance acceptance is implied by a passing basic integration test.

On 17 October 2026, the saved-scene checks and automated Play-mode smoke checks both passed with zero failures. A separate normal Play session also visibly displayed the officer, held pistol, crosshair and health UI; pressing Escape displayed the supplied Pause/Resume/Restart/Quit screen. Play was stopped without applying runtime changes.

## Team boundaries and next milestones

1. Install/test S2's shelter doors and checkpoint triggers, then shop/pickup prefabs in separate task commits.
2. Configure the existing shared **3D FlightGrid**, Hawk vantage points and Crow stashes/shelter zones. Do not implement a second A* or replace the current architecture with the old ground-NavMesh grid proposal.
3. Integrate movable barricades and verify navigation updates, then production waves/spawning and agreed safe-room mechanics.
4. Complete environment art/material/lighting passes after gameplay clearance is stable.

Confirmed birds: **Gull, Pigeon, Crow, Hawk**. The World Builder's IS assignment is **Gull**, not Pigeon. Gull work is deferred to a separate `feature/is-gull` branch and sandbox. The shared enum still says Eagle in this snapshot; S2 owns its coordinated rename/stat/weapon mapping before Gull implementation. Do not silently change another teammate's shared definitions in a scene-integration commit.

## Suggested manual commit

Review only this milestone's scene, two Editor tools (with their `.meta` files), guide and validation evidence. Suggested message:

```text
feat(level): integrate gameplay player and core manager in Main
```

Commit/push are performed by the human. Open a PR from `feature/world-player-integration` into `develop`. Do not bundle later doors, waves or Gull AI into this commit.
