# Gull — FOD Bomber implementation notes

Branch: `feature/is-gull`, based on develop after PR #44 (airport AI marker preparation). Unity: **6000.6.4f1**. Human commits/pushes; merge target is **develop**, not main.

## References and scope

For every bird task consult the workspace's `BIRD_STRATEGIES.md`; for Gull also consult `01_GULL_PLAN.md`. The team's latest decision is **four waves / four species**. Earlier three-wave references below describe historical work and are superseded.

## Current sandbox and Main wave isolation

`Sandbox_IS_Gull` disables only its own inherited `WaveManager` through a scene prefab override. The shared GameManager prefab and Main remain enabled for production waves. Open the sandbox, press Play, click Game view and press **B once** for five test Gulls; **F3** toggles labels and **F4** shows the statistics panel. These are manual test birds, not the production wave spawner. Stop Play and open `Main` for normal automatic countdowns and waves.

The controlled **Tools > IS > Gull > Run Focused Main Gull Check** temporarily suspends the Main WaveManager during its own Play session, then restores its prior enabled state when Play exits, including interruptions. The check uses three temporary birds and explicit GameManager wave hooks. Existing reports below are historical and do not certify the latest combined build. The owner has since observed that Gull drops work in normal gameplay; that observation is not a new automated test result.

For the current normal-Main Wave 1 observation and the owner's separate nonlethal-hit/distress-recovery check, see `MainWaveRegression.md`. Gull transition logging is now off by default; to investigate a particular Gull, enable **Log State Transitions** on its GullBrain in the Editor or a development build. F3 labels and F4 statistics remain separate opt-in diagnostics. Errors about missing required references remain visible.

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

## G5 — distress call and one group dive

References: `BIRD_STRATEGIES.md` section 2 and `01_GULL_PLAN.md` G5.

A surviving positive-damage hit raises one audible call. The registry supplies living Gulls within **25 m** of the hit Gull, including itself. Each eligible responder returns any bombing token, leaves the kettle, keeps its carried FOD and commits to the shooter's recorded position plus **1 m**. Speed is **2.2×** normal. Contact damage uses the existing attack component and requires clear player line of sight; blocked travel uses the shared budgeted pathfinder rather than flying directly through cover.

A successful contact attack, passing the committed point, or **4 active-gameplay seconds** ends the pass. Speed/diving/wings reset and the Gull rejoins the kettle. Each responder has its own **8 active-gameplay second cooldown**; further damage/calls cannot restart that pass. Shop/pause time does not consume the cooldown. Lethal hits do not broadcast. Health-event subscriptions are removed on disable/death.

Small safety refinement to the plan: use `BirdHealth.LastHitShotOrigin`, which records the matching shot before `Damaged`, rather than blindly using the hub's potentially stale last shot. Non-gun damage uses the health component's existing bird-position fallback. A distress dive commits once; it does not home continuously on the player.

Preview: in the saved Gull sandbox, Play and B once, let the Gulls form, then hit one without killing it. Nearby Gulls should break formation together, make one pass and reform. **Tools > IS > Gull > Run Focused G5 Distress Check** exercises one group response using a simulated shot-recording callback and actual damage/motor components. See `G5Validation.txt` for the measured result and explicit test limitations. Final audio, end-to-end weapon/cover checks and broader balance remain integration work.

Suggested G5 commit: `feat(ai-gull): add distress call that triggers a group dive on the shooter`.

Recorded G5 result: **11 checks passed, zero failures** on Unity 6000.6.4f1. Five nearby Gulls responded, a distant control did not, carried props survived interruption, token/membership cleared, a repeated hit did not restart the pass, and all five recovered within four active seconds with normal speed. This was one focused simulated-shot/damage run, not an end-to-end gun, roof, lethal-hit, cooldown-expiry or performance test.

## G6a — debug statistics overlay

References: `BIRD_STRATEGIES.md` section 2 and `01_GULL_PLAN.md` G6. `GullDebugStats` is a scene-local singleton, created by the first valid Gull. It is hidden initially. In Play, click Game and press **F4** (Fn+F4 if macOS uses the function key for a system action). F4 also toggles the existing Crow/Hawk overlays; the Gull panel is bottom-right to avoid their top panels. No saved scene/prefab wiring is required.

Editor fallback: **Tools > IS > Gull > Preview or Toggle Stats Overlay** starts Play and shows the panel, or toggles it when already playing. **Stop Stats Preview** exits Play explicitly. These commands avoid relying on OS function-key handling.

Counters collect while hidden. Debug instrumentation calls are compiled out of non-development players with conditional attributes; a manually placed component also disables itself in a release build. Text refreshes only while visible, at most four times per second. This is not a claim that the whole strategy meets its Profiler target.

| Display | Exact meaning |
|---|---|
| Drops / pending | Real brain FOD releases / tracked releases awaiting their first non-bird impact. A despawned/cancelled piece leaves pending but does not become an impact. |
| FOD HP hits / impacts | First impacts whose splash actually reduced player HP / all recorded first impacts. Shielded/invulnerable hits do not count as HP hits. Contact swoops/distress attacks are excluded. |
| Hit percentage | HP-damaging FOD impacts divided by resolved impacts, not by all releases. An empty sample displays `--`. |
| Avg impact-to-player | Mean horizontal distance from the piece's centre to the player's feet at first impact, in metres. Only samples with a bound player are included; the sample count is shown. This differs from G4's landing-to-committed-aim error. |
| Ground FOD | Current manager `LandedCount`, not cumulative drops. |
| Token hand-offs / minute | Grants to a different owner after the initial grant, divided by active-gameplay seconds since stats creation/reset, times 60. Initial and repeated same-owner grants are not hand-offs. This is a session average, not a rolling-minute window. |

**Reset Gull stats** clears session counters and unsubscribes in-flight samples without changing real birds/debris. Impacts from pre-reset drops are deliberately excluded. New Play sessions/scenes start fresh; wave changes do not silently erase evidence. Drop resolution, despawn, disable and reset remove event subscriptions.

`Tools > IS > Gull > Run Focused G6 Stats Check` exercises one real hit and one miss with controlled physical debris drops, plus diagnostic token-grant calls. It writes `G6Validation.txt` and stops Play. Those deliberately arranged drops verify the measuring tool, **not autonomous Gull accuracy**. The player hub is created at stats startup so its normal binding occurs before the first physical impact.

Recorded counter-check result: **10 checks passed, zero failures** on Unity 6000.6.4f1 after correcting first-impact hub initialization. The diagnostic arranged one real HP-damaging impact and one miss; their mean impact-to-player distance was **4.00 m**. This validates measurement/counter semantics, not game balance or predictive-AI accuracy.

**Visual verification:** the project owner subsequently confirmed that the panel appears and its counters work in the Gull sandbox. This is user-observed confirmation, separate from the automated ten-check counter result. The prior native-editor automation attempts alone had not confirmed rendering.

### Current strategy flow (not the historical G1 label demo)

```text
GatherFod -> JoinKettle -> InKettle
                            | bottom member takes token
                      DescendToDrop -> Telegraph -> Drop / no-FOD swoop
                                                        |
                            ClimbToTop <----------------+
                         (return token, move to top)
                            |                 |
                   carrying: InKettle    empty: GatherFod

Any living eligible state + surviving hit/call
    -> return token, leave kettle, retain FOD
    -> DistressDive (one pass, <=4 s) -> JoinKettle
Any state + death -> Dead (terminal until shared respawn lifecycle)
```

The ordinary drop token serializes bombing/swoop turns; the planned distress exception can dispatch several Gulls together. Aim prediction, warning-time locking, capped lead and close-target braking are documented in G4 above. The defaults remain unchanged by G6.

### G6b — accuracy evidence and remaining profiling

- Existing G4 single samples are real functional results: standing lost 6 HP, straight walking lost 6 HP, warning-time direction change lost 0 HP. They are not a statistically meaningful standing/moving hit-rate comparison.
- **Tools > IS > Gull > Measure Six Accuracy Attempts** runs one bounded sample: three standing and three straight-walking attempts, with three different Gull starting points repeated for each condition. Each fresh Gull must collect real FOD, enter the kettle, warn and drop normally. Player HP/position, supply/formation/debris and debug counters reset between attempts; no forced attack state or final game assets are changed. The player's normal controller is temporarily replaced by scripted +Z movement commanded at 2 m/s during approach/warning/fall. Actual hub speed at the warning is recorded because Editor-driven movement is not guaranteed to run on every Play frame.
- `AccuracySamples.md` records every outcome, HP loss, first-impact-to-player distance, warning-time speed, warning-to-release delay and per-condition totals. First-impact measurements are checked against the live overlay counters. A 32-second unresolved attempt is reported as a timeout, not silently replaced or counted as a resolved miss. This small repeated sample is functional evidence, not a statistically reliable population hit rate. Human-input, cover/shield and broader movement tests remain future balance work.

Recorded six-attempt run on Unity 6000.6.4f1 / Apple M1 / macOS 15.7.7:

| Condition | Resolved / attempted | HP-damaging impacts | Mean first-impact-to-player distance |
|---|---:|---:|---:|
| Standing | 3 / 3 | 3 / 3 | 0.44 m |
| Straight walking, commanded 2 m/s | 3 / 3 | 3 / 3 | 0.80 m |

All six releases followed a measured **0.61 s warning**, each damaging impact removed **6 HP**, and the overlay matched all six observed impacts. No attempts timed out. Walking hub speeds at the warning were **2.00, 1.38 and 1.98 m/s**: the fixture is explicitly not a guarantee of perfectly constant 2 m/s movement. The complete individual rows are in `AccuracySamples.md`.

Interpretation: the lead prediction can still hit a slowly moving player who continues straight; movement alone is not the intended counter. The existing G4 single sharp-turn example avoided damage after changing direction at the warning. Six attempts do not prove a true 100% hit probability, final fairness, or a reliable standing-versus-walking difference. No balance tuning was changed on this evidence. The earlier close-target braking and warning-time aim lock remain the documented tuning decisions.

- The controlled G6 diagnostic's arranged 1-hit/1-miss result must never be presented as a 50% autonomous strategy hit rate.
- The plan's **eight Gulls <0.5 ms of scripts/frame** target needs a clearly scoped measurement. The Editor-only sampler below is preliminary evidence; a representative development-player capture is still needed before claiming build-wide or every-frame compliance. Do not substitute whole-frame FPS or this overlay's refresh rate for script timing.
- Production Main wiring and end-to-end weapon/cover/wave checks remain separate integration work. No Main scene, other bird strategy, species balance or three-wave configuration was changed by this overlay milestone.

Suggested G6a commit: `feat(ai-gull): add gull debug stats overlay`.
Suggested sampler commit: `test(ai-gull): add bounded autonomous accuracy sampling`.
Suggested evidence commit: `docs(ai-gull): record gull strategy design, formulas and playtest results`.

### G6c — bounded eight-Gull Editor performance sample

**Tools > IS > Gull > Measure Eight Gull Editor Performance** starts a fresh saved Gull sandbox with exactly eight actual prefab instances, each above one of the existing FOD supply points. They gather, form the kettle and attack naturally. The fixture hides labels/Gull statistics, requires Deep Profile to be off, and enables Unity CPU profiling temporarily. The normal controller is disabled and player HP is restored below 25% solely to keep the stationary fixture alive. No gun shots/distress stimulus is injected. Saved scenes, prefabs, tuning and teammates' movement/animation scripts are not edited.

Warmup is at least 15 seconds and also waits for one actual FOD impact, so Unity has registered the collision-message marker. The subsequent capture is 15 seconds, with an overall 65-second safety limit. `ProfilerRecorder` reads Unity's existing script-message markers, sums repeated samples within a frame and restricts capture to the main thread. This measures the flock's combined work, not a per-Gull figure multiplied from FPS. Required markers cover GullBrain, GullKettle, FodManager, FodItem update/collision, FodSpawnPoint, BirdMotor physics updates and BirdAnimatorDriver update/late-update. Missing markers, full buffers, insufficient frames or brain samples without exactly eight calls make the outcome inconclusive.

The report is `PerformanceSample.md`. Combined mean is the sum of the captured scopes' nanoseconds, converted to milliseconds and divided by the number of non-empty eight-call brain frames. Per-marker p95/peaks are reported separately. **Adding individual marker peaks gives a conservative bound, not the actual same-frame combined peak or p95.** Different marker peaks may occur in different frames; no invented combined percentile is reported.

The scopes include synchronous work called inside those Unity callbacks. They exclude rendering/GPU, general physics-engine work, animation-system jobs, player/hub/debug scripts, and unlisted callbacks. Warmup also excludes initial spawn/first-use cost; distress/death spikes are not tested. State-transition logging was enabled for this historical capture, so a debug-log cost can appear inside a recorded brain callback. Logging is now opt-in; no new performance measurement was made. This is one Editor sample, not full standalone-build certification.

The initial marker lookup was inconclusive because this Unity version prefixes script names with `Assembly-CSharp.dll!namespace::`. Matching was corrected using actual engine metadata; missing scopes were never treated as zero cost. **Inspect Available Timing Markers** writes diagnostic metadata to the ignored `Logs/GullProfilerMarkers.txt` without modifying game assets. Profiler recording is restored to its prior enabled state and all recorders are disposed after success, failure or interruption.

Suggested tooling commit: `test(ai-gull): add scoped eight-gull editor profiling`.
Suggested evidence commit: `docs(ai-gull): record eight-gull editor performance sample`.

Recorded capture on Unity 6000.6.4f1 / Apple M1 / macOS 15.7.7: **15.2419 s warmup**, **15.0003 s capture**, **2,517 eight-call brain frames** (20,136 brain updates). All required script markers were available, including actual FOD collision callbacks.

| Recorded scope | Mean contribution per brain frame | Highest recorded marker sample |
|---|---:|---:|
| GullBrain, all eight birds combined | 0.0319 ms | 1.1237 ms |
| All listed Gull/motor/animator scopes combined | 0.0574 ms | Same-frame combined peak not measured |

The mean is below 0.5 ms, but the brain's **1.1237 ms** peak alone proves the strict every-frame 0.5 ms budget was **not maintained in this Editor capture**. The sum of marker peaks (2.0086 ms) is only an upper bound, not the actual combined peak. Do not claim a full G6 performance pass. Identifying that spike's cause requires a detailed trace; these aggregate samples cannot distinguish pathfinding, logging, first-use/native work or other causes. No runtime code, balance or teammate scripts were changed on this evidence. Retain the pending development-player/worst-case profiling task while proceeding with separately committed Main integration.

## Remaining phases / evidence

- G2 collection/item/manager implementation is delivered in the Gull sandbox and Main's eight supply markers/manager are connected. See `MainIntegration.md`; real automatic wave spawning is not implemented by the environment integration.
- G3 formation/token implementation is delivered in the Gull sandbox and now drives G4 attacks.
- G4 prediction, warning, real drop and fallback swoop are implemented in the Gull sandbox; final tuning/art/sound and wider balance checks remain integration work.
- G5 damage-triggered distress reaction and cooldown are implemented in the Gull sandbox.
- G6a debug metrics overlay is implemented and visually confirmed by the project owner. G6b includes six measured autonomous accuracy attempts. G6c records scoped eight-Gull Editor timings: low mean, but an over-budget brain peak. Detailed spike analysis, development-player confirmation and final integration/viva evidence remain outstanding.
- Main now has one configured Gull kettle/manager and the shared species prefab is ready for spawning. S2's production WaveManager runs four waves. The focused Main fixture suspends that manager and tests explicit GameManager hooks; it is not a production-wave test. No B-key test spawner or live birds are saved in Main.

Do not present G1's timer cycle as completed intelligent behaviour. G4 and G6b provide small functional samples, not a statistically reliable hit rate; the Editor performance sample does not establish a full performance pass. Keep future phases in separate meaningful commits; do not bundle the full Gull implementation into the G1 commit.

Suggested G1 commit: `feat(ai-gull): add gull strategy config and state machine skeleton`.
