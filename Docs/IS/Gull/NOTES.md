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

## G3 — rotating kettle and single drop token

References consulted: `BIRD_STRATEGIES.md` section 2 and `01_GULL_PLAN.md` G3. The completed G2b collection flows into the formation automatically; there is no additional saved scene object to install. `GullKettle` creates one scene-local instance when first needed.

### Slots and joining

The centre comes from the shared KettleCenter marker. The airport marker `(0,10,0)` yields a ground reference of y=0, an **8 m radius**, and slots from **14 m at the top to 6 m at the bottom**. Five members occupy heights 14/12/10/8/6. The rotation is **25 degrees per active-gameplay second**. One member uses the bottom slot, with an explicit branch avoiding division by zero. Without a marker, the descriptive centre falls back to `(0,12,0)` while heights remain ground-relative.

A Gull paths to the moving top entry through shared `RequestPath`, honouring the budget/repath interval. When it is within 3 m, `Join` inserts it at the top and it enters InKettle. Non-members use a defined top entry rather than an invalid negative index. Members follow their rotating slots through `IBirdMotor.FlyTo`; the runway's prepared orbit volume is clear. Joining/removing a member remaps the logical slots, so the real birds steer into the new arrangement rather than snapping their transforms.

### Token and current staging behaviour

- Only the logical bottom member in InKettle can take a free token. A brief 0.75 s state dwell prevents an immediate transition on the joining frame. During G3, a Gull also needs carried FOD to request a turn; the no-FOD swoop remains G4 work.
- `TakeToken` returns success/failure and records an explicit owner. Other birds cannot take an occupied token or return someone else's token. `HasToken` means eligible for a free token; `TokenHolder` identifies its current owner.
- The holder enters DescendToDrop and flies to a **staging position at runway centre, 7 m above ground**, retaining its prop. **G3 does not release it or target the player yet.** This makes one visible departure from the spiral while G4's predictive drop/telegraph is still pending.
- At **12 active-gameplay seconds**, the safety timeout returns the holder to the top, resets ownership and sends that Gull to ClimbToTop. It keeps the undropped FOD and returns to InKettle on reaching its slot. Other members continue moving, and the next bottom Gull may take its turn while the previous one climbs.
- ClimbToTop also returns a currently held token on entry, preparing the G4 post-drop path. Once G4 releases the prop, reaching the top will leave the kettle and return to GatherFod; a Gull still carrying its prop stays in the formation instead of creating an extra supply item.
- Disable/death leaves the kettle immediately, including token release; a defensive membership sweep also removes invalid entries. WaveCompleted clears members, ownership and rotation. Event subscriptions use the cached GameManager; the singleton is not created during teardown.
- Shopping/Paused freezes rotation and token age. Gull decisions stop issuing flight commands while gameplay is inactive; the existing motor brakes after Stop in Shopping and Time.timeScale pauses physics in Paused. On resume, the join path is eligible for an immediate refresh.

### Preview and verification

Open the Gull sandbox, Play, click Game and press **B once**. The five Gulls collect props, climb to the runway and form the rotating stack. One then shows **DescendToDrop** and moves toward the centre staging position. Other birds show InKettle, and the timed-out holder shows ClimbToTop. F3 toggles labels.

To see it, select a spawned Gull, frame it in Scene view with F, then zoom out to include the runway centre. Keep **Gizmos** enabled: the cyan spiral/slot spheres show the formation, and the holder's slot/staging guide is red. Select the runtime `[GullKettle]` in the Hierarchy and temporarily uncheck **Dispatch Enabled** on that component to watch all members circling. Play-mode Inspector changes are temporary.

**Tools > IS > Gull > Run Focused G3 Kettle Check** is an opt-in sandbox check: it observes five real Gulls collecting/joining/orbiting, measures the slot geometry and movement, then checks shopping freeze, exclusive ownership, the real 12 s timeout, holder death/recovery and WaveCompleted reset. It temporarily suspends dispatch while observing the full five-member formation. Results are written to `G3Validation.txt`; G4/G5 attacks, full-wave balance and stress profiling are separate.

The older G2 check now accepts birds progressing beyond JoinKettle, since G3 makes that state a real journey. It still checks the original collection/manager obligations.

Recorded G3 result: **12 checks passed, zero failures**. Five real Gulls reached and followed the formation; exclusivity, timed handoff, real holder death/recovery within one second, shop scheduling freeze and wave reset all passed in the focused run. The sample demonstrates functional movement/scheduling, not final accuracy or performance tuning.

Suggested G3 commit: `feat(ai-gull): add rotating kettle formation with single drop token`.

## G4 — predictive drop, warning and no-FOD swoop

References consulted: `BIRD_STRATEGIES.md` section 2 and `01_GULL_PLAN.md` G4. This replaces G3's centre staging with a real attack on the player; the existing token still controls one attack at a time.

### Prediction and fair counterplay

`GullTargeting.PredictDropPoint` is a pure maths helper:

```text
fallTime = sqrt(2 × dropHeight / gravity)
leadTime = fallTime + telegraphDuration
prediction = playerPosition + clampMagnitude(horizontalVelocity × leadTime, 8 m)
```

Vertical player velocity is ignored. Height/warning are bounded at zero and gravity magnitude at 0.01 for finite results with invalid zero/negative tuning. The normal values remain 7 m height, gravity 9.81 and 0.6 s warning. The brain additionally clamps x/z within the airport grid with a 1 m boundary margin.

**Deliberate resolution of a plan conflict:** the G4 prose says to track/re-predict through the warning and release, but its acceptance/counterplay says a sharp direction change *during the call* should dodge. G4 now commits the aim point **when the warning begins**. It tracks before the call, then holds that point through release. This keeps the stated counter learnable and avoids silently correcting the aim after the player reacts. The bomb is a normal gravity object after release, with no homing.

### Attack flow

- DescendToDrop follows the predicted point plus 7 m height. In clear air it uses the motor; if the direct route is blocked, it requests the shared A* route with the existing budget/repath rules. The maximum speed multiplier is 1.3, reduced near the destination to avoid the shared motor's wide close-target circles.
- Once sufficiently above the aim point (or after the six-second approach limit), it enters Telegraph if the target is clear flight space. Otherwise it aborts back to the formation with its prop.
- Telegraph commits aim/height, spreads/pulses the wings and plays a warning call. An optional `Call Clip` on GullBrain accepts final audio. Until assigned, a short synthetic chirp is generated once per Gull and reused; it is a prototype warning, not final Gull sound art. The clip/source are runtime objects and the generated clip is cleaned up with its Gull.
- After at least 0.6 active-gameplay seconds, Drop releases the carried piece **from its real transform**. It requires horizontal alignment within 0.85 m and height within 1 m. A short final motor correction is allowed; if alignment is still poor after 1.25 s, it returns with the prop rather than teleporting into position. Extra alignment time can affect moving-target accuracy and is a future tuning consideration.
- Release clears the carried reference, raises `FodDropped(item, committedAim)` for measurement, returns the token and climbs. Reaching the top without FOD leaves the kettle to gather/restock. `TelegraphStarted(brain)` exposes the instant aim locks for testing and future evidence.
- The normal FodItem collision path applies the one splash, settles as debris, blocks the grid, and remains under the manager's cap/lifetime/wave cleanup.
- An empty Gull can now take its token turn. It uses the same warning, then a single contact swoop toward the player's warning-time trajectory. It uses the shared `BirdContactAttack` range/damage/cooldown, with no fabricated FOD. A hit, passing the aim point, or four seconds ends the pass and returns the token.
- Missing/dead player, lost token, death, disable or timeout stops the attack/returns to the existing recovery flow. The warning timer follows active gameplay; wings/diving/speed are reset when climbing or disabling.

### Preview / evidence

In `Sandbox_IS_Gull`: Play, click Game, press B once, and stay on the runway to observe a pass. Gulls collect, form the kettle, approach, show Telegraph, chirp, release and climb/restock. Keep moving or change direction when the warning sounds to dodge. Physical roofs/walls can intercept a falling prop. Stop Play before making saved edits.

F3 toggles labels. Scene view/Gizmos show the line and splash-radius disc at the current prediction (red during approach/release, yellow while committed/warning). The disc is a debug guide, not extra game-world collision.

**Tools > IS > Gull > Run Focused G4 Bombing Check** checks the lead helper and runs real stationary, straight-walking, warning-time dodge and no-supply fallback scenarios. It moves the real sandbox player transiently with its CharacterController; no player/shared config assets are edited. Each case uses one Gull and stops after one completed drop/pass. `G4Validation.txt` records actual measured aim error/HP loss and checks; these small samples are functional evidence, not a statistical hit rate or final wave balance.

The old G3 check now observes completed-turn return instead of expecting every turn to last 12 s. The historical G3 result remains evidence for the timeout, whose safety implementation is retained.

Recorded G4 result: **14 checks passed, zero failures**. A close-target overshoot found during verification was corrected by braking earlier and lowering warning-time correction speed through the existing motor interface. The measured successful run was:

| Single scripted scenario | Warning to release | Landing distance from committed aim | Player HP loss |
|---|---:|---:|---:|
| Standing still | 0.61 s | 0.05 m | 6 |
| Walking straight at 2 m/s | 0.61 s | 0.73 m | 6 |
| Turn sideways at 4.5 m/s when warned | 0.62 s | 0.78 m | 0 |

The no-FOD case made one warned, bounded swoop and returned its token; that one pass missed (0 HP loss). Contact damage remains governed by the existing attack component and range/line-of-sight checks. Do not turn these one-off functional samples into a percentage hit rate. G6 still needs repeated representative samples and performance measurements.

Suggested G4 commit: `feat(ai-gull): add predictive FOD drop with telegraph call`.

## Remaining phases / evidence

- G2 collection/item/manager implementation is delivered in the Gull sandbox. Production Main wiring is deferred until the remaining strategy phases are integrated.
- G3 formation/token implementation is delivered in the Gull sandbox; its current departure uses staging until G4 supplies the actual attack.
- G4 prediction, warning, real drop and fallback swoop are implemented in the Gull sandbox; final tuning/art/sound and wider balance checks remain integration work.
- G5: damage-triggered distress reaction and cooldown.
- G6: debug metrics, measured accuracy/performance and viva evidence.

Do not present G1's timer cycle as completed intelligent behaviour. No hit-rate, miss-distance or performance results exist yet. Keep future phases in separate meaningful commits; do not bundle the full Gull implementation into the G1 commit.

Suggested G1 commit: `feat(ai-gull): add gull strategy config and state machine skeleton`.
