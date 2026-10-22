# Main scene prop organisation

Moved 27 loose prefab instances into `World_Props`, grouped by type.

## Aircraft

- `P_Prop_Aircraft_WhiteRed`
- `SM_aircraft_`

## Safety_Equipment/Fire_Hydrants

- `firehydrant_01_a_02`
- `firehydrant_01_a_02 (1)`
- `firehydrant_01_a_02 (2)`

## Safety_Equipment/Cord_Reels

- `ExtensionCord`
- `ExtensionCord (1)`

## Safety_Equipment/Traffic_Cones

- `TrafficCone`
- `TrafficCone (1)`
- `TrafficCone (2)`
- `TrafficCone (3)`
- `TrafficCone (4)`
- `TrafficCone (5)`
- `TrafficCone (6)`
- `TrafficCone (7)`
- `TrafficCone (8)`
- `TrafficCone (9)`
- `TrafficCone (10)`
- `TrafficCone (11)`
- `TrafficCone (12)`
- `TrafficCone (13)`
- `TrafficCone (14)`
- `TrafficCone (15)`
- `TrafficCone (16)`

## Vegetation

- `Bush1`

## Emergency_Vehicles

- `ForestFireTruck3`
- `ForestFireTruck1`

Verified 64 world-space transforms, 27 prefab links/active states, and unchanged total collider count (143).

Object names, placement, rotation, size, components, materials, layers and gameplay references are retained. No assets were deleted or unpacked. No navigation or lighting bake was run.

`World_Props` is separate from `Environment` because Environment carries an inherited navigation modifier. Existing organised environment groups and gameplay/system roots were left untouched.

For future scenery: drag the prefab into its matching World_Props category and position it normally. Do not move or scale the grouping objects; keep their transforms at position/rotation zero and scale one. Leave player, waves, shop and bird systems outside the scenery group.
