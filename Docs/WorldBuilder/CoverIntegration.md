# Main movable cover integration

Three of Main's existing greybox barricades are connected instances of S2's
`P_Barricade_Movable` prefab. Their original positions and rotations are kept;
the corresponding fixed instances were replaced, not left underneath.

| Instance | Position (x, y, z) | Y rotation |
| --- | --- | --- |
| Cover_02_Movable | (10, 0, -21) | -18° |
| Cover_03_Movable | (-10, 0, 23) | 12° |
| Cover_05_Movable | (-13, 0, -2) | 12° |

`Cover_01`, `Cover_04` and `Cover_06` remain fixed. All six retain their
original footprints and placement; no spawn, FOD, pickup, checkpoint or shelter
marker was moved. The player prefab already has `PlayerPusher`. The movable
prefab retains S2's physics and its carving `NavMeshObstacle`.

The existing `FlightGrid` subscribes to barricade settling at startup and
retests both the previous and new footprints after a move. It does not update
continuously while the barricade is being pushed. This integration does not
add another grid subscription. Ground NavMesh baking is a separate B2 task;
carving cannot be demonstrated until a suitable ground NavMesh exists.

## Verification status

The scene integration command saved three movable and three fixed pieces. In
one Main Play-mode check, the player pushed `Cover_02_Movable` 2.92 m and it
settled. A temporary read-only observer saw the grid's cached footprint match
the settled collider, with six geometry cells freed and six newly blocked
across the grid. The fixed pieces were retained as fixed prefabs, but were not
separately pushed in that check. As with any movable object, a player can push
cover into a route or marker during play.
