# Main flight navigation — AI-level batch, milestone 1 of 4

Unity: **6000.6.4f1**. Branch: **feature/world-ai-integration**. Merge target: **develop** after the batch, not main. Team direction: **three waves, four bird species**; wave composition is separate from this scene setup.

## Saved scene changes

- One scene-root `FlightGrid` uses the existing shared `RunwayPanic.AI.FlightGrid` component. No new pathfinding algorithm or ground NavMesh is added.
- World bounds: centre `(0, 12.5, 0)`, size `(100, 24, 80)` m. This covers x=-50..50, y=0.5..24.5 and z=-40..40. Cell size **2 m**, clearance **0.4 m**: **24,000 cells**.
- All obstacle layers are considered. The shared implementation ignores trigger volumes and player/bird colliders with `IDamageable`. Search limits remain **8,000 expanded nodes** and **six searches per frame**; point flyability radius remains **0.3 m**.
- The original `GameplayMarkers/BirdSpawnPoints/BirdSpawn_01` through `08` now each have a shared `BirdPoint`: type **Spawn**, radius **1.5**, group **AirportSpawn**. Their names, positions and rotations are preserved.
- The old `GreyboxPreview_TEMPORARY` remains disabled. Shared grid/marker/path-service scripts, bird prefabs, world geometry and existing gameplay systems are unchanged.

The grid builds automatically at normal Play start; `PathService.Current` now selects the real grid A* provider instead of its straight-line fallback. There is no path service GameObject to add. The shared grid already supports settled movable-barricade updates and manual debris blocks; no duplicate obstacle bridge is created here.

**No enemies are spawned yet.** This supplies usable navigation and spawn metadata, not WaveManager/BirdSpawner or a Gull brain. Existing player/pickup/shop behaviour remains as before. Do not interpret narrow shelter entrances being unavailable to grid paths as invulnerability or a completed timed safe-room system.

## Reproduce the focused check

Open Main outside Play. Under **Tools > Runway Panic**:

1. **Integrate Main Flight Grid and Spawns** adds only a missing grid/marker components, applies the settings above and saves. It refuses duplicate grids/marker components and missing original markers. Running it again does not create additional grid objects or points.
2. **Run Main Flight Smoke Check** enters Play and waits for the normal shared grid startup. It checks grid/provider selection, marker registration/clearance, one A* route from each spawn to open runway air `(0,6,0)`, and rejection of outdoor paths into the two enclosed shelter interiors. It honours the real six-searches-per-frame budget, writes `FlightValidation.txt`, then exits Play. It neither instantiates birds nor runs the complete AI/stress/other feature suites.

Only the configured settings and components are saved. The runtime cell arrays and test paths are not baked into the scene.

Recorded run: **24 checks passed, zero failures**. The grid had 1,336 blocked cells and three free-air regions (outdoors plus the two shelter interiors). All eight original positions passed without adjustment; `FlightValidation.txt` records their exact coordinates and path results. These measurements describe this saved airport layout, not a general guarantee after later geometry changes.

## Handoff to the future spawner / bird owners

- Find spawn points via `BirdPoint.GetAll(BirdPointType.Spawn, list)` and optionally filter `Group == "AirportSpawn"`. The eight original transforms remain available for explicit references too.
- `Radius=1.5` is a maximum candidate scatter radius, **not a guarantee that every offset is clear**. A future spawner must validate each candidate with the shared grid/physics and avoid already occupied spawn positions. Some markers are near buildings; do not scatter blindly into their walls.
- A marker can be flyable while its coarse grid cell is blocked. The shared path provider handles a visible nearby free cell; do not delete walls or weaken clearance merely to force the marker's own cell to be free.
- Use `PathService.TryConsumeBudget()` before requests. If refused, retry next frame. Species brains already wrap this through their shared `RequestPath` helper.
- Three-wave configuration and gun/economy timing are still pending; never use numeric species enum order as the wave order.

## Commit-sized milestones on this branch

1. FlightGrid and original spawn markers — this milestone.
2. Eight Hawk vantage/perch markers — implemented; see `HawkPerchIntegration.md` and its focused report.
3. Supplied `P_CrowSetup_Main` installed at the origin — see `CrowSetupIntegration.md`; its stashes/zones/exits are reused unchanged.
4. Gull/Pigeon behaviour markers based on their current plans.

The human commits/pushes each milestone. Suggested message for this first milestone:

```text
feat(level): connect airport flight grid and bird spawn markers
```
