# Main Hawk vantage markers — AI-level batch, milestone 2 of 4

Unity: **6000.6.4f1**. Branch: **feature/world-ai-integration**. Human commit/merge target: **develop**, after this batch; not main.

## Saved placement

Main now has one scene-root `HawkPerches` containing eight empty GameObjects with the existing shared `BirdPoint` component: **Perch**, radius **1.5**, group **HawkVantage**. The Hawk's shared selector discovers points by type, so the group is descriptive and requires no brain changes. These markers add no colliders, renderers or gameplay scripts.

| Marker | World position | Intended surface |
| --- | --- | --- |
| Perch_ControlTower | (-30.9, 15.09, 25.9) | Control tower roof, runway side (moved 20 Oct for the modelled tower) |
| Perch_TerminalRoof | (20, 6.4, 25.9) | Terminal roof edge |
| Perch_Hangar_NE | (-31.9, 6, -0.6) | Hangar roof NE |
| Perch_Hangar_SE | (-31.9, 6, -24.8) | Hangar roof SE |
| Perch_PlaneA_Tail | (-17.6, 3.5, -28.45) | Aircraft A tail |
| Perch_PlaneB_Tail | (36.45, 3.3, -1) | Aircraft B tail |
| Perch_PlaneD_Tail | (41.15, 3.3, 23) | Aircraft D tail |
| Perch_ShelterB_Roof | (21, 4.3, -12) | Shelter B roof |

These are the Hawk owner's exact handoff positions, not new layout choices. They sit on surfaces as reference points; **the current Hawk soars above them rather than landing**. Its shared config adds the soaring height/radius, and `StoopPlanner.ClampCircleToVolume` keeps circles within the grid. Marker positions themselves must not be raised by the soaring height a second time.

The parent is an independent root as requested. If a building/aircraft moves or final art replaces its roof/tail, reposition the corresponding marker or establish parenting in that later change. Otherwise the marker will not follow automatically.

## One focused check

**Tools > Runway Panic > Integrate Main Hawk Perches** creates only missing markers, configures their positions/type, validates and saves Main. Repeat calls preserve existing marker objects rather than replacing the parent; unexpected children or duplicate names are refused.

**Validate Main Hawk Perches** runs only in Edit mode in Main. It rebuilds the existing grid's non-serialized preview arrays and checks: eight typed/non-colliding markers, support from the correct structure within 10 cm, clear soaring centre plus eight circle samples using the actual Hawk config and volume clamping, finite scores from the actual shared utility selector, at least one visible vantage from PlayerStart, and the old preview rig remaining disabled. `HawkPerchValidation.txt` records **35 checks passed, zero failures**.

This is not a complete orbit sweep, pathfinding test, runtime registry test or Hawk behaviour/flight simulation. No bird was spawned and no other feature suites were run. The Hawk owner should test its live behaviour in Main after the branch is merged and normal bird spawning is connected.

## Unchanged and next

Grid settings, original spawn points, geometry, doors, player, bird data/stats, Hawk brain/config/prefabs and its sandbox placement helper are unchanged. No roosts for Pigeon, Crow setup, Gull behaviour points or production waves are added in this milestone. Three-wave composition remains separate.

Suggested manual commit:

```text
feat(level): add hawk vantage markers to airport scene
```

Next milestone on this same branch: install the supplied `P_CrowSetup_Main` at the origin, keeping its existing stash/zone/exit setup.
