# Gull / Pigeon level markers — milestone 4 of 4

Unity **6000.6.4f1**, scene `Assets/_Project/Scenes/Main.unity`, branch **feature/world-ai-integration**. Merge the completed four-commit batch into **develop**, not main. Three waves / four species remains the team decision; this milestone does not configure waves.

References consulted: workspace `BIRD_STRATEGIES.md` sections 2–3, `01_GULL_PLAN.md` setup/G1–G3, and `02_PIGEON_PLAN.md` setup/P3–P4. This is World Builder marker preparation in Main, not implementation of either species owner's sandbox/brain phases.

## Saved layout

One scene-root `GullPigeonBehaviourPoints` contains twelve empty objects with the existing shared `BirdPoint` component. There are no renderers, colliders, brains or test spawners on these objects.

| Type / name | World position (m) | Radius | Group |
|---|---|---|---|
| KettleCenter / `KettleCenter_Runway` | (0, 10, 0) | 8 | GullKettle |
| FodSpawn / eight `FodSpawn_West_*` / `FodSpawn_East_*` | x = −8.5 or +8.5; y = 0; z = −30, −10, +10, +30 | 0.5 | GullFod |
| Roost / `Roost_HangarRoof` | (−38.46, 6, −12.7) | 2 | PigeonRoost |
| Roost / `Roost_TerminalRoof` | (20, 6.4, 30) | 2 | PigeonRoost |
| Roost / `Roost_PlaneA_Wing` | approximately (−17.375, 1.35, −23.345) | 0.6 | PigeonRoost |

The wing point was calculated from Plane A local `(3.1, 1.35, −0.15)`, away from its fuselage and engine. Its smaller radius fits the actual swept wing; the roofs offer larger flock landing areas. The marker root is not parented to scenery: after moving/replacing a building or aircraft, reposition its marker and recheck the landing surface. Future flock code must limit occupied landing slots/separation to the available area; a supported ring is not proof that every flock size fits on the small wing.

FOD collection points are just outside the 14 m-wide runway, on solid ground, clear of existing cover. They do not create debris or block the grid yet. The original eight shared Spawn points, eight Hawk perches and connected Crow setup are retained unchanged; no extra species-specific spawns are needed.

## Gull owner handoff

- Discover the points through `BirdPoint.GetAll(BirdPointType.KettleCenter, list)` / `FodSpawn`, not names. Group strings are descriptive filters, not new shared API.
- **Coordinate convention:** anchor `(0,10,0)` follows the setup instructions. The planned orbit radius is 8 m; its heights are **6–14 m above runway ground**. Use the config table's ground reference `KettleCenter.y - 10`; do not blindly add 6–14 to the anchor's y=10. Reconcile this with G3's shorthand when implementing the kettle. The marker radius is metadata, not a substitute for the future strategy config.
- G2 still needs the actual `FodSpawnPoint` component next to each FodSpawn marker, cooldown/reservation behaviour, FOD prefabs, and damage/obstacle lifecycle. None exists as a result of this marker milestone. Add runtime wiring deliberately when those scripts are implemented, not a placeholder with the same class name now.
- The check used **point + 0.8 m** as a prospective pickup approach. It is not a finished motor/landing implementation. Final descent and carry/drop behaviour need the Gull sandbox work and a small live check.
- Do Gull implementation on its own feature branch from updated develop, using both required design references and the current shared foundation. Do not modify other species' health, damage or brains.

## Pigeon owner handoff

- Discover `BirdPointType.Roost`, optionally filtering group `PigeonRoost`; use each marker's own radius for landing offsets.
- Three roosts satisfy the plan's 3–4 target: hangar roof, terminal roof and parked aircraft wing.
- The check used **roost + 1.5 m** as an approach. The surface itself need not be a flyable coarse-grid cell: approach via shared A*, then use the motor for the final landing, with a timeout while the known shared landing issue is unresolved.
- No static Rally points are added. P4 selects rally goals dynamically around/behind the player and rejects obstructed candidates; there is no Rally member in the shared marker enum.
- No Pigeon brain/config/prefab edits, flock election, boids or ambush behaviour are included here.

## Focused verification and tool

Outside Play with Main open: **Tools > Runway Panic > Integrate Main Gull and Pigeon Markers**. It refuses unexpected/duplicate marker setups, adds or updates only the twelve planned points, runs the focused placement check and saves. Undo groups the scene edit. **Validate Main Gull and Pigeon Markers** reruns that check separately if geometry changes.

One Edit-mode check passed with zero failures; see `GullPigeonMarkerValidation.txt`. It checked metadata/counts, solid support, eight support samples around each roost ring, a prospective A* approach to each FOD/roost point, and radius-8 kettle samples at heights 6/10/14. Grid preview arrays are nonserialized. No Play-mode AI suite, waves, continuous-orbit proof or full-flock landing simulation was run.

Existing world geometry, shared navigation/marker code, bird prefabs/stats, player/pickups/shop and disabled preview rig are unchanged.

This finishes the four World Builder AI-level preparation milestones. Human review/commit/push and the PR into develop are next; production spawning/waves and live species integration remain separate tasks.

Suggested commit: `feat(level): add gull debris and pigeon roost markers`.
