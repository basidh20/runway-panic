# Shelter doors — integration batch, commit 2 of 4

Unity: **6000.6.4f1**. Branch: **feature/world-level-integration**. Merge target: **develop**, after this four-commit batch is ready.

## Placement and ownership

Main now contains one connected S2 `P_Door_Shelter` prefab under each original shelter:

- `Environment/Buildings/Shelter_A/P_Door_Shelter`
- `Environment/Buildings/Shelter_B/P_Door_Shelter`

Both keep the supplied root transform: local position `(0, 0.055, -3.8)`, rotation `(0, 180, 0)`, scale `(1, 1, 1)`. Following visual feedback, each panel is now **1.4 m wide x 2.4 m tall**, rather than the original 2.7 x 2.82 m. Mesh, collider, hinge edge, kickplate and handle positions are resized together without scaling the physics hierarchy. Scene-only overrides extend the front walls and lintel, and narrow the entrance stripe. Shelter footprints, checkpoints and all shared runtime scripts/prefab assets are unchanged. The door component remains on the prefab's `DoorPanel` child.

The visible edge gaps are closed with metal jambs/header, a low 2 cm threshold above the floor, and four opaque flexible seals. The panel is lowered 6 cm (mesh/collider centre local y=1.22), and the lintel lowered to bottom y=2.485, leaving a structural opening 1.5 m wide and 2.415 m high above the threshold. The closed panel runs from world y=.075 to 2.475. Seal meshes overlap its edges by about 15 mm, blocking sightlines while closed. They deliberately have no colliders: flexible seals must allow a double-action door to brush through them. The rigid frame stays outside the swing clearance; door width, height, hinge and damping remain as approved.

To reduce swinging/overshoot, the scene instances use spring **1000**, damper **240**, Rigidbody mass **20 kg** and angular damping **1**, instead of spring 1200, damper 150, mass 40 kg and angular damping 0.5. The damping is close to critical for the narrower panel's inertia. Both still open away from the player to 90 degrees; no new door logic or runtime scripts were introduced.

In Play mode, approach either entrance, aim at the door and press **E** to open/close it. The existing S2 interaction system supplies the prompt; the door swings away from the player's side. Doors start closed and unlocked. These are physical doors only: no invulnerability, timers, bird-exclusion logic or automatic safe-room locking. No hangar doors were added.

## Focused verification

**Tools > Runway Panic > Integrate Main Shelter Doors** installs missing connected prefabs and saves Main. It can be rerun without adding duplicate doors; unexpected existing door objects are refused.

**Run Main Shelter Door Smoke Check** runs one short Play-mode check of both doors in parallel: placement/size, overlap of all four opaque edge seals with the closed panel, closed passage blocking, physical hinge rotation to +90 and -90 degrees, human-sized doorway clearance when open, closing after each direction, and closing overshoot. The adjusted doors are sampled after 1.5 seconds per phase, instead of the original five-second settling allowance. It writes `ShelterDoorValidation.txt`, then exits Play; temporary test motion is not saved. It calls the public `Door.Open/Close` API, so it does **not** claim automated keyboard-E or interaction-prompt coverage. No other feature's test suite is run.

For the remaining quick manual interaction check: press Play, approach Shelter A or B, aim at the panel, confirm the prompt and press E. Walk through, close it, then open it from inside. Stop Play afterwards; do not save Play-mode changes.

The current results are recorded in `ShelterDoorValidation.txt`: all **26 focused checks passed**, including four-edge seal overlap on each door and unchanged opening/closing/clearance behaviour. This supersedes the original wide-door and unframed-door reports.

Next milestones on this same branch are pickup spawning (commit 3) and shop integration (commit 4). Commit this milestone separately; the human stages, commits and pushes.
