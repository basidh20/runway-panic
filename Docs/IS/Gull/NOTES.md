# Gull — FOD Bomber implementation notes

Branch: `feature/is-gull`, based on develop after PR #44 (airport AI marker preparation). Unity: **6000.6.4f1**. Human commits/pushes; merge target is **develop**, not main.

## References and scope

For every bird task consult the workspace's `BIRD_STRATEGIES.md`; for Gull also consult `01_GULL_PLAN.md`. The team's **three waves / four species** decision overrides the older four-wave example. G1 does not configure waves.

`00_SHARED_FOUNDATION.md`, referenced by the Gull plan, was not present in the workspace or Downloads during this implementation. The actual checked-in `BirdBrain`, motor/animator interfaces, health, markers, state label, test spawner and handoff notes were inspected instead. F1/F2 and the current F3/F4 components exist; Gull is already a valid species. No shared contract was reimplemented.

## G1 delivery

- `GullStrategyConfig`: all fifteen tuning fields/defaults from the G1 table, with tooltips and Inspector bounds. These are future strategy settings, not a replacement for shared species health/speed/damage/coin data. The asset is created and assigned.
- `GullState`: Idle, GatherFod, JoinKettle, InKettle, DescendToDrop, Telegraph, Drop, ClimbToTop, DistressDive, Dead.
- `GullBrain`: `BirdBrain` subclass with entry/exit hooks, state timer, cached label strings and shared death handling. Requires the Gull species asset and a strategy config; a missing/wrong assignment disables the brain with an error.
- **Temporary G1 preview:** living state labels advance every two active-gameplay seconds. The bird does not fly to the player, pick up/drop objects, predict or attack. DistressDive is a preview label, not a damage-triggered reaction yet. Disable `Preview State Cycle` to hold Idle. Later phases replace timer-driven transitions with real conditions.
- Death is terminal, never part of the automatic preview loop. Shared health death enters Dead, disables/unsubscribes the brain and leaves the existing health/motor death fall in control. The existing label hides on a disabled brain; inspect State/logs for Dead rather than expecting a persistent floating death label.
- Only `P_Bird_Gull` gains this brain/config; neither `P_Bird_Base` nor any other bird prefab/stat is changed.

## Sandbox and manual preview

`Assets/_Project/Scenes/Sandbox/Sandbox_IS_Gull.unity` is a separate copy of the completed Main airport. Main and build settings are unchanged. It includes the real player/GameManager, one flight grid, existing Gull markers and original eight Spawn points. There is no need to add duplicate spawn markers.

1. Open the Gull sandbox outside Play.
2. Press Play, click Game view, then **B** once to spawn five Gull previews. B can spawn more for manual inspection; the saved scene contains no live gulls.
3. **F3** toggles the shared state labels. The original spawn points are spread around the airport; use Scene view, select a spawned `P_Bird_Gull(Clone)` in the Hierarchy and press **F** to frame it if it is outside the gameplay camera view.
4. Labels advance through the planned states every two gameplay seconds. They freeze in Shopping/Paused/non-playing game states. No real combat behaviour is expected in G1.
5. Stop Play before editing/saving. No spawner is installed in Main and nothing was added to Build Settings.

**Tools > IS > Gull > Set Up G1 Sandbox and Prefab** is a one-time setup tool: it creates the config, adds the brain to the Gull-only variant, copies Main into a new sandbox and adds a five-bird B-key test spawner. It refuses an existing Gull sandbox or an already-brained Gull prefab to prevent overwriting ongoing work. Do not rerun it to reset your scene.

**Run Focused G1 Check** is opt-in and sandbox-only. It runs one short preview, checks five prefab instances/config/labels, state progression, a short Shopping freeze/resume and one actual shared-health death, writes `G1Validation.txt`, and stops Play. It does not run full bird AI/pathfinding/stress suites. See the saved report for the actual result.

Recorded G1 run: **eight checks passed, zero failures**. All nine living labels were observed; one real death was verified separately. Pause uses the same inactive-gameplay guard but was not separately exercised in this short check.

## Coordinate convention for G3

The kettle marker is at `(0,10,0)`. The strategy's 6–14 m slot heights are **above runway ground**, using `KettleCenter.y - 10` as the reference. Do not add 6–14 to the marker's y=10 again. The G3 prose shorthand is ambiguous; implement the ground-relative interpretation consistent with the strategy table and prepared airport markers.

## G2a — FOD item, carry/drop, splash and debris

References: `BIRD_STRATEGIES.md` section 2 and `01_GULL_PLAN.md` G2. This is the first of G2's two proposed commits. The item is implemented independently of the future supply/manager code.

`FodItem.cs` and `Prefabs/Birds/FOD/P_FOD_Debris.prefab` provide:

- Four states: Lying, Carried, Falling and Landed. The temporary prop is 0.3 × 0.15 × 0.4 m, mass 2, with the existing greybox metal material, one BoxCollider and Rigidbody.
- `AttachTo(carrier, localOffset)` keeps world dimensions on scaled bird roots, follows the carrier and disables item collisions. `Release()` detaches, enables gravity/continuous collision detection and starts with zero velocity. Repeated Release calls cannot restart a falling item's splash.
- First non-bird contact applies one splash: radius from the Gull config, damage from **Gull species AttackDamage × fodDamageMultiplier** (currently 6 × 1). The existing PlayerHealth/shield rules handle the result. Multiple player colliders never multiply the hit. Bird contacts do not trigger the impact. The normal splash query uses a fixed buffer; an overflow uses a complete query so it cannot silently omit the player.
- A direct player hit applies the splash once, then ignores that player's collision shapes so the item can reach scenery. It becomes fixed debris when it hits scenery, rather than freezing on the player's head. This refines the plan's single-contact shorthand. `Impacted(item, hitPlayer)` describes the first contact; `Settled(item)` separately describes its final debris state for the later manager.
- Landed debris enables a correctly sized carving NavMeshObstacle and a shared FlightGrid manual block. The airport currently uses the flight grid; obstacle component setup is supplied for future ground navigation, with no new baked NavMesh in this step.
- The 0.15 m-high ground prop lies below the flight volume's y=0.5 edge. The shared grid clamps its bounds to the bottom cell, intentionally obstructing a coarse low-altitude lane. This is a grid cell footprint, not a claim that the little prop fills that entire 2 m cell physically.
- Despawn, disabling, pickup after landing and destruction release the **exact bounds/grid** registered earlier, once. Removal also refreshes that small region's geometry in case the grid was rebuilt while debris existed. Overlapping pieces retain their own block counts. Re-enabling a landed item restores its block.
- For this standalone step, the item expires after the configured landed lifetime (40 active-gameplay seconds); a lost falling item expires after 15 active-gameplay seconds. The global 20-piece cap and WaveCompleted cleanup remain G2b manager work.

`Configure(strategy, gullSpecies)` lets the next supply point assign references before pickup. The saved prefab already has the current Gull config/species assigned. Shared species/weapon data, Gull brain transitions, Main and the sandbox scene file are not edited by G2a.

### Manual drop preview

1. Outside Play, choose **Tools > IS > Gull > Open Gull Sandbox** (save any current scene edits first).
2. Press Play. There is no need to spawn birds for this item preview.
3. Choose **Tools > IS > Gull > Drop FOD Above Player (Play)** once. A small metal prop drops from 4 m above the player; staying still lets you check the hit, while stepping aside shows the miss/splash radius.
4. Find the selected `P_FOD_Debris(Clone)` in Scene view (F to frame). It should reach the ground and remain there; the item Inspector exposes references, while the runtime State is available through the API/check report. `Debug Release` is also available from a FodItem component's context menu for a manually placed instance during Play.
5. Stop Play afterwards. Menu-created drops are temporary and are not saved into the level.

**Create FOD Debris Prefab** is a one-time tool that refuses to overwrite an existing prefab. Normal prefab editing is used afterwards. The shape is a prototype; final bolt/rubble/luggage-tag artwork can replace its visual child while retaining the root collision/lifecycle contract.

**Run Focused FOD Item Check** runs only in the saved Gull sandbox. It checks a carry/drop using real physics and the real player, one splash, ground settling, overlapping grid blocks and cleanup, plus lifetime expiry using a transient config copy set to 0.25 s. The saved 40 s config is preserved. Results are in `G2aValidation.txt`; this does not exercise a baked ground NavMesh or a full AI suite.

Recorded G2a result: **15 checks passed, zero failures**. The measured direct-hit HP loss was 6. The lifetime check uses a cell verified clear before dropping; cells next to existing barricades can remain blocked for valid geometry reasons after debris disappears.

Suggested G2a commit: `feat(ai-gull): add FOD item with carry, splash damage and debris state`.

## G2b — supply, manager and actual collection

References consulted again: `BIRD_STRATEGIES.md` section 2 and `01_GULL_PLAN.md` G2. Branch remains `feature/is-gull`; merge target remains develop.

The Gull sandbox's existing eight ground FodSpawn markers now also have `FodSpawnPoint`, with the FOD prefab, strategy and Gull species references assigned. One scene-local `FodManager` has the strategy assigned. Its singleton can also create itself on first item use in another scene. Main is not modified by this sandbox setup.

### Real collection

The Gull prefab now has **Preview State Cycle OFF**. The default flow is `Idle → GatherFod → JoinKettle`. The last state waits for G3; it does not pretend that the formation, token or drop is already implemented.

- A Gull looks up shared BirdPoint FodSpawn markers, selects the nearest ready/unclaimed supply, and claims it. A* uses the existing `BirdBrain.RequestPath`/PathService budget; a throttled query retries next frame without dropping its claim.
- Claim ownership lives on each supply component, rather than a second static HashSet in the brain. This keeps one source of truth: one Gull owns a source at a time, competitors are refused, and disabled/dead owners no longer hold it. The brain releases its target on exit, disable, death or a failed route.
- The target is 0.8 m above the marker. A short final approach is allowed only after a shared clearance check. The motor slows near the point, and a pickup within 1.15 m attaches the item through `AttachTo`, using the planned `(0,−0.15,0.05)` local carry offset. Only the motor interface moves the bird; no root teleports, scale changes or shared motor edits.
- A failed route is excluded for that collection attempt. A 15 s travel limit also prevents indefinitely occupying a source if the motor cannot arrive. If no usable source is available for three active-gameplay seconds, the Gull proceeds to JoinKettle without FOD; G4 will supply the fallback attack.
- Pickup starts that source's **six-second** cooldown. Supply cooldowns and collection decisions tick only while Playing. The source is logical supply: it instantiates the small prop at pickup time, so it does not leave an unlimited pile of physical cubes on the runway.
- In this phase, disabling or killing a carrier despawns its carried item and frees the claim. This avoids orphaned props on inactive birds. Damage-triggered dives and token/death interactions are later phases.

### Managed debris

`FodManager` tracks all active item states and keeps a landed queue in arrival order. The normal strategy cap is **20 landed pieces**, removing the oldest first. It now owns landed lifetime (40 active-gameplay seconds per the item's strategy), replacing G2a's per-item landed timer; the item retains its 15 s lost-fall timeout.

The item informs its cached manager on enable, settlement, pickup, despawn and disable. Repeated notifications are idempotent. `Impacted` still reports the first contact/damage result; `Settled` reports final scenery landing. Individual grid blocking/unblocking remains in FodItem, with the exact saved bounds and overlapping-piece safety already checked in G2a.

The manager subscribes to the current GameManager and clears **carried, falling, lying and landed** items on `WaveCompleted`. It also resets supply cooldowns/claims. This prevents a projectile carried over from the previous wave landing on someone in the shop. It unsubscribes from the same GameManager when disabled/destroyed and does not recreate itself during teardown.

### Preview / handoff

Open `Sandbox_IS_Gull`, press Play, click Game view, and press **B once**. Five Gulls should fly toward different available ground supply points and carry small metal props. Select a spawned Gull in the Hierarchy and use Scene view/F to follow it; F3 toggles state labels. After collecting they show **JoinKettle** and wait. Stop Play afterwards.

**Tools > IS > Gull > Connect G2 Supply in Gull Sandbox** wires the eight existing markers, manager and Gull prefab; it requires the saved sandbox outside Play. It does not rebuild/copy Main. **Run Focused G2 Collection Check** is the corresponding opt-in runtime check. It uses transient config copies for a small cap and short expiry, leaving the shared 20-piece/40-second/six-second settings intact. See `G2bValidation.txt` for results.

The old G1 state-cycle check is historical and requires the preview flag; it is no longer the acceptance check for the default Gull prefab. G2a's item check remains relevant, with expiry now performed by the manager.

Recorded G2b result: **12 checks passed, zero failures**. Five Gulls collected distinct pieces through the real motor/path interfaces. The check also exercised competing claims, cooldown freeze in the shop, oldest-first removal, expiry/grid release, cleanup through the real WaveCompleted event and the three-second no-supply fallback. This is focused sandbox verification, not a full-wave balance or performance result.

Suggested G2b commit: `feat(ai-gull): add FOD manager and spawn points with cooldown`.

## Remaining phases / evidence

- G2 collection/item/manager implementation is delivered in the Gull sandbox. Production Main wiring is deferred until the remaining strategy phases are integrated.
- G3: rotating kettle membership/slots and one attack token, including death/timeout release.
- G4: lead prediction with telegraph and real drop/swoop.
- G5: damage-triggered distress reaction and cooldown.
- G6: debug metrics, measured accuracy/performance and viva evidence.

Do not present G1's timer cycle as completed intelligent behaviour. No hit-rate, miss-distance or performance results exist yet. Keep future phases in separate meaningful commits; do not bundle the full Gull implementation into the G1 commit.

Suggested G1 commit: `feat(ai-gull): add gull strategy config and state machine skeleton`.
