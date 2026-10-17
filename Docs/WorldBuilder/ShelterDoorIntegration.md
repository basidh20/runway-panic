# Shelter doors — integration batch, commit 2 of 4

Unity: **6000.6.4f1**. Branch: **feature/world-level-integration**. Merge target: **develop**, after this four-commit batch is ready.

## Placement and ownership

Main now contains one connected S2 `P_Door_Shelter` prefab under each original shelter:

- `Environment/Buildings/Shelter_A/P_Door_Shelter`
- `Environment/Buildings/Shelter_B/P_Door_Shelter`

Both keep the supplied root transform: local position `(0, 0.055, -3.8)`, rotation `(0, 180, 0)`, scale `(1, 1, 1)`. Following visual feedback, each panel is now **1.4 m wide x 2.4 m tall**, rather than the original 2.7 x 2.82 m. Mesh, collider, hinge edge, kickplate and handle positions are resized together without scaling the physics hierarchy. Scene-only overrides extend the front walls and lintel to create a centred **1.5 x 2.6 m opening**, and narrow the entrance stripe. Shelter footprints, checkpoints and all shared runtime scripts/prefab assets are unchanged. The door component remains on the prefab's `DoorPanel` child.

To reduce swinging/overshoot, the scene instances use spring **1000**, damper **240**, Rigidbody mass **20 kg** and angular damping **1**, instead of spring 1200, damper 150, mass 40 kg and angular damping 0.5. The damping is close to critical for the narrower panel's inertia. Both still open away from the player to 90 degrees; no new door logic or runtime scripts were introduced.

In Play mode, approach either entrance, aim at the door and press **E** to open/close it. The existing S2 interaction system supplies the prompt; the door swings away from the player's side. Doors start closed and unlocked. These are physical doors only: no invulnerability, timers, bird-exclusion logic or automatic safe-room locking. No hangar doors were added.

## Focused verification

**Tools > Runway Panic > Integrate Main Shelter Doors** installs missing connected prefabs and saves Main. It can be rerun without adding duplicate doors; unexpected existing door objects are refused.

**Run Main Shelter Door Smoke Check** runs one short Play-mode check of both doors in parallel: placement/size, closed passage blocking, physical hinge rotation to +90 and -90 degrees, human-sized doorway clearance when open, closing after each direction, and closing overshoot. The adjusted doors are sampled after 1.5 seconds per phase, instead of the original five-second settling allowance. It writes `ShelterDoorValidation.txt`, then exits Play; temporary test motion is not saved. It calls the public `Door.Open/Close` API, so it does **not** claim automated keyboard-E or interaction-prompt coverage. No other feature's test suite is run.

For the remaining quick manual interaction check: press Play, approach Shelter A or B, aim at the panel, confirm the prompt and press E. Walk through, close it, then open it from inside. Stop Play afterwards; do not save Play-mode changes.

The original wide-door report is superseded by `ShelterDoorValidation.txt`: all **24 checks passed** for the resized, damped doors. They reached +/-89.9 degrees, settled within 0.1 degrees of closed after 1.5 seconds, and showed 0.00 degrees of measured closing overshoot in this API-driven run.

Next milestones on this same branch are pickup spawning (commit 3) and shop integration (commit 4). Commit this milestone separately; the human stages, commits and pushes.
