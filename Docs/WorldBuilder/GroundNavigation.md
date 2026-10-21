# Main ground navigation

`GroundNavigation` in Main contains a `NavMeshSurface` using the existing
Humanoid agent settings: radius 0.5 m, height 2 m, slope 45° and step 0.75 m.
Its baked data is `Assets/_Project/Navigation/MainGroundNavMesh.asset`. The
player's physical controller is 0.35 m radius and 1.8 m high; the bake is
slightly more conservative. It does **not** make the player follow a NavMesh.

The surface collects marked physics colliders under `Environment`: the ground,
solid buildings and frames, fixed cover, boundaries and parked aircraft. The
two door panels and three movable barricades have explicit ignore modifiers;
they are not permanently baked into their starting locations. Other objects
outside `Environment`, including players, birds, FOD and pickups, are not bake
sources. Triggers and movable physics bodies are excluded by the setup. The
movable prefab's existing NavMeshObstacle carves only after it has settled.
The static bake treats doorways as open; a closed physical door can still block
the player, and is not dynamically carved by this setup. Inspect exclusions
and rebake when level geometry or object placement changes.

Birds use the separate three-dimensional `FlightGrid`; this ground mesh does
not replace it or make flying birds NavMeshAgents.

## Focused checks

The baked mesh was sampled successfully on the runway and both shelter floors.
The Shelter A back-wall point was not walkable, and calculated paths between
the outside and interior of each shelter were complete with the doors omitted
from the static bake. In one normal Main play-mode check, the player pushed
`Cover_02_Movable` 3.19 m: its original location was blocked before the push,
free after it settled, and its new location was blocked. No full-level or
physical-door traversal test was performed in this task. The Scene-view
NavMesh overlay was not visually inspected; these checks use NavMesh sampling
and path calculation.

## Final scenery refresh workflow

The editor tool now offers `Tools > Runway Panic > Refresh Main Ground
Navigation` for the existing setup; the original Bake command remains an
initial-setup command. Open only saved Main outside Play mode. Refresh marks
placed instances of the control tower, traffic cones, hydrants, extension
cords, abandoned aircraft and the two parked fire-truck prefabs when they are
outside `Environment` and have solid colliders. It does not reparent props,
change colliders or include flower/grass render meshes. Already marked
environment geometry remains included.

Door panels, triggers, CharacterControllers, Rigidbody objects, NavMeshAgents
and NavMeshObstacles are excluded from these source hierarchies. Existing
conflicting root modifier settings cause an error rather than being silently
overwritten. Players, birds and pickups outside these explicit hierarchies
remain unmarked. No flight-grid or gameplay code is changed.

Refresh updates the existing `MainGroundNavMesh.asset` asynchronously, preserving
its GUID, surface reference and bake/agent settings, then saves Main and the
data. Do not edit the scene, switch scenes or enter Play during the update.
Wait for `[GroundNav] Refresh completed` in the Console, then run `Check Main
Ground Navigation`. Inspect the NavMesh overlay around the parked aircraft,
truck bodies and shelter entrances as well; the existing route check alone
does not certify every prop footprint.

Implementation of this command is not evidence that the final-prop bake or
footprint checks have run. Those results must be recorded after the refresh.

On 21 October 2026 the updated tool compiled in Unity 6000.6.4f1 and its
read-only Inspect command reported 118 candidate static colliders. The list
included both parked trucks, the abandoned aircraft, 17 cones, three hydrants
and two extension cords alongside Environment. It reported the two shelter
door panels and three movable cover roots as exclusions. No scene or NavMesh
asset was changed by this inspection; the refresh/footprint review is pending.
