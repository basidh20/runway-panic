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

## Remaining phases / evidence

- G2: actual FOD items, splash/debris lifecycle, cooldown/claims and gathering; add `FodSpawnPoint` next to sandbox markers when the component exists.
- G3: rotating kettle membership/slots and one attack token, including death/timeout release.
- G4: lead prediction with telegraph and real drop/swoop.
- G5: damage-triggered distress reaction and cooldown.
- G6: debug metrics, measured accuracy/performance and viva evidence.

Do not present G1's timer cycle as completed intelligent behaviour. No hit-rate, miss-distance or performance results exist yet. Keep future phases in separate meaningful commits; do not bundle the full Gull implementation into the G1 commit.

Suggested G1 commit: `feat(ai-gull): add gull strategy config and state machine skeleton`.
