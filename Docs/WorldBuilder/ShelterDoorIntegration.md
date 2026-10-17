# Shelter doors — integration batch, commit 2 of 4

Unity: **6000.6.4f1**. Branch: **feature/world-level-integration**. Merge target: **develop**, after this four-commit batch is ready.

## Placement and ownership

Main now contains one connected S2 `P_Door_Shelter` prefab under each original shelter:

- `Environment/Buildings/Shelter_A/P_Door_Shelter`
- `Environment/Buildings/Shelter_B/P_Door_Shelter`

Both use the supplied handoff transform: local position `(0, 0.055, -3.8)`, rotation `(0, 180, 0)`, scale `(1, 1, 1)`. The 2.7 x 2.82 m panels fit the existing 2.8 x 3 m openings. Shelter layout, checkpoints and all shared runtime scripts/prefabs are unchanged. The door component remains on the prefab's `DoorPanel` child.

In Play mode, approach either entrance, aim at the door and press **E** to open/close it. The existing S2 interaction system supplies the prompt; the door swings away from the player's side. Doors start closed and unlocked. These are physical doors only: no invulnerability, timers, bird-exclusion logic or automatic safe-room locking. No hangar doors were added.

## Focused verification

**Tools > Runway Panic > Integrate Main Shelter Doors** installs missing connected prefabs and saves Main. It can be rerun without adding duplicate doors; unexpected existing door objects are refused.

**Run Main Shelter Door Smoke Check** runs one short Play-mode check of both doors in parallel: placement, closed passage blocking, physical hinge rotation to +90 and -90 degrees, human-sized doorway clearance when open, and closing after each direction. It writes `ShelterDoorValidation.txt`, then exits Play; temporary test motion is not saved. It calls the public `Door.Open/Close` API, so it does **not** claim automated keyboard-E or interaction-prompt coverage. No other feature's test suite is run.

For the remaining quick manual interaction check: press Play, approach Shelter A or B, aim at the panel, confirm the prompt and press E. Walk through, close it, then open it from inside. Stop Play afterwards; do not save Play-mode changes.

Recorded result: **all 21 focused checks passed**. The heavy spring-driven panels needed five seconds per phase to settle for angle measurement (the initial three-second sampling was premature). Both reached +90/-90 degrees and settled within 0.6 degrees of closed. No spring/damper overrides or shared-code changes were needed.

Next milestones on this same branch are pickup spawning (commit 3) and shop integration (commit 4). Commit this milestone separately; the human stages, commits and pushes.
