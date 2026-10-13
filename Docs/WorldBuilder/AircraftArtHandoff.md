# Aircraft greybox handoff — core developer / modeller

Scene: `Assets/_Project/Scenes/Main.unity`  
Unity: `6000.6.4f1`  
Hierarchy: `Environment/Aircraft`  
Status: editable game-scale references, not final submission artwork. No flying, boarding, destruction, or interaction gameplay is implemented.

## Agreed reference dimensions and parking

One Unity unit represents one metre in this level. Dimensions are design targets for small utility aircraft, not a claim of real-world airliner accuracy.

| Scene root | Reference prefab | Length | Wingspan | Height | Ground-origin position (X, Y, Z) | Y rotation |
| --- | --- | --- | --- | --- | --- | --- |
| Plane_A_Greybox | P_Greybox_UtilityAircraft | 12 m | 9 m | 3.5 m | (-20, 0, -25) | -35° |
| Plane_B_Greybox | P_Greybox_LightAircraft | 9 m | 8 m | 3.3 m | (33.3, 0, -1) | -90° |
| Plane_C_Greybox | P_Greybox_LightAircraft | 9 m | 8 m | 3.3 m | (33.3, 0, 11) | -90° |
| Plane_D_Greybox | P_Greybox_LightAircraft | 9 m | 8 m | 3.3 m | (38, 0, 23) | -90° |

Both prefabs are in `Assets/_Project/Prefabs/Environment/Greybox/`. B, C, and D share one prefab so common art/collision refinements stay consistent. The generated wing mesh assets and their `.meta` files are also in that folder.

## Why these changes were made

- The original 0.5 m-high slabs communicated parking footprints but not usable aircraft height, wing cover, or camera obstruction.
- All four now include a fuselage, cockpit, swept wings, tailplane, vertical tail, engine nacelles, wheels, and landing-gear struts. Neutral shared materials keep this at greybox stage.
- Plane A is southwest of its original location so its wings do not encroach on barricades or the hangar approach.
- B/C/D are spaced 12 m apart along Z, giving 4 m between their 8 m wingspans. D is farther east because its original nose-side walking route met the terminal front wall.
- The runway remains unobstructed. Hangar entry/exit and both shelter entry/exit tests still pass.
- Both aircraft variants' local dimensions fit inside the 14 x 25 m hangar and its 10 x 4.8 m main opening when aligned appropriately. They remain parked outdoors; towing or hangar parking is not implemented or tested.

## Final-model replacement contract

1. Model the final aircraft using these sizes as the approved gameplay envelope. Keep local **+Z as the nose direction**, +Y up, and the origin at ground height beneath the centre of the fuselage. Apply scale so the Unity prefab root stays `(1, 1, 1)`.
2. Keep the named scene roots and their parking transforms. Replace the greybox geometry child or create a final prefab at the same root. Do not leave old renderers/colliders underneath the new model, and do not add final-model colliders on top of retained greybox colliders.
3. Prefer simple compound collision: capsule or boxes for fuselage, simple wheel/gear boxes, and a convex wing shape. **Do not use one large box around the whole wingspan**: it creates invisible barriers in the open space around the nose/tail.
4. Wings sit approximately 1.25 m above the ground and deliberately block the 1.8 m preview player. Keep them low enough to serve as physical cover; walking under them is not an approved route. There is no damage-protection logic attached to them.
5. Keep the 4 m parking aisles, open runway, terminal-front passage, and hangar approaches clear. If final wings/tails grow beyond this envelope, review parking and rerun collision tests before merging.
6. Use original final modelling, UVs, textures, and any required assignment evidence. These generated Unity primitives/meshes are layout references, not Blender deliverables.
7. Review Main scene changes with the World Builder. Commit the approved prefab/mesh/material assets and their `.meta` files; exclude Library, Logs, and generated builds.

## Verification and integration limits

Tools > Runway Panic > **Validate Aircraft Greyboxes** checks all four planes for grounding, collision, overlap with scene obstacles, perimeter bounds, runway clearance, wing blocking, paths around their noses, both eastern aisles, and the full runway route. Report: `Docs/WorldBuilder/AircraftValidation.txt` — zero failures.

Hangar and general greybox validation were rerun after the parking change — zero failures. Play-mode walking through the B/C aisle was also verified.

The tests use the temporary first-person controller. The final third-person player, camera collision, shooting/cover behaviour, NavMesh/grid, and bird AI still need integration tests. Plane assets have no Rigidbody and are static parked obstacles; do not add flight or movable physics unintentionally.

For a quick preview, press Play, Tab, then **5** to stand at the eastern aircraft aisle. **4** goes to the hangar entrance. Stop Play before permanent edits.

The Refine Plane Greyboxes and Finish Aircraft Greybox Parking menus are one-off construction utilities, not runtime generators. Use normal prefab/scene editing for deliberate redesigns.
