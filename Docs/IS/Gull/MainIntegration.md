# Gull runtime in Main — environment integration

Branch: `feature/is-gull`. Merge target: **develop**, not main. Unity: **6000.6.4f1**.

References: workspace `BIRD_STRATEGIES.md` section 2 and `01_GULL_PLAN.md` integration/Gull requirements. Updated team decision (18 Oct): retain S2's **four waves with four species**.

## Current status after integrating S2's develop changes

S2's production WaveManager, wave HUD and four-wave assets are now present; reuse them rather than building another wave loop. The earlier environment-only results below are historical evidence from before this integration, not a merged production-wave test. Re-check Main and isolate Gull sandbox/focused fixtures from automatic waves before rerunning those controlled checks. The historical three-wave follow-up proposal below is superseded by the four-wave decision.

## What is saved

- Eight existing `GullPigeonBehaviourPoints/FodSpawn_*` objects now also have `FodSpawnPoint`. Each references the existing debris prefab, Gull strategy config and Gull species data. Marker transforms, radius/group/type and all Pigeon roosts remain unchanged.
- One `GullRuntime_Main` root holds `FodManager` and `GullKettle` children with their shared Gull config assigned. Kettle dispatch is enabled. The existing single KettleCenter remains the location source: the y=10 marker anchors ground-relative orbit heights 6–14 m; heights are not added to y=10 again.
- The existing `BirdSpecies_Gull` already resolves to `P_Bird_Gull`, whose real brain/config and disabled historical label-preview setting were checked. No shared species/prefab/motor/animation changes were needed.
- Existing flight grid, player, doors/shop/pickups, airport spawns, Hawk/Crow metadata and Pigeon roosts are retained. No live birds or B-key test spawner are saved into Main. The Gull sandbox is not copied over Main.

`Tools > IS > Gull > Connect Gull Runtime in Main` opens Main only from a saved scene outside Play, performs preflight checks, adds/reuses the expected components, checks marker metadata preservation and saves Main. It refuses different existing asset references instead of replacing teammates' settings. `Validate Main Gull Runtime` writes `MainEditValidation.txt`. The older World Builder marker validator now accepts intentional FOD supply components while still rejecting brains/spawners on the marker group; its original layout writer remains guarded against overwriting later runtime setup.

## Historical state before S2's wave integration

**There is no production WaveManager/Spawner in the checked-in runtime scripts.** `GameManager` exposes state hooks and `WaveData` stores configuration, but neither starts timed waves or spawns birds. Pressing Play in Main alone therefore does not start a Gull wave. Do not confuse the focused test below with an automatic gameplay loop.

The role plan assigns **WaveManager + Spawner to S1/World Builder, co-owned with S2**. It belongs in a separate wave-loop milestone, not a hidden temporary Gull spawner. Main's existing GameManager still reports **four total waves**; this integration does not silently modify shared progression. The future wave milestone must apply the confirmed three-wave count together with the real controller/data/shop progression.

## One focused Main check

After saving Main outside Play, choose **Tools > IS > Gull > Run Focused Main Gull Check**. It:

1. Calls the existing `GameManager.BeginWave(1)` hook explicitly and spawns three temporary birds from `BirdSpecies_Gull.Prefab` at actual shared Spawn markers. The normal player controller is disabled transiently and the fixture keeps the stationary player alive.
2. Waits for actual collection and kettle membership with dispatch temporarily disabled, then permits a real token-controlled drop to settle as managed Main debris.
3. Records a simulated shot context through the existing player hub callback and applies a real surviving wrong-rarity health hit. It checks nearby distress response, carried-FOD retention, token release and bounded speed/state recovery. This is not a real weapon raycast test.
4. Calls `CompleteWave()` explicitly, checking FOD/kettle/supply cleanup and the existing Shopping state. The fixture deactivates leftover birds; a future production wave controller must own leftover-bird cleanup too. GameManager alone does not remove them.
5. Writes `MainPlayValidation.txt` and exits Play. No runtime fixture objects/player movement are saved.

This is not a complete three-wave playthrough, all-species/door regression suite, or performance certification. Eight-Gull Editor timings and their over-budget brain peak remain documented in `PerformanceSample.md`; do not claim that integration fixed the spike.

Recorded result on Unity 6000.6.4f1: **8 Edit-mode reference checks passed and 10 Play-mode checks passed, zero failures**. Three actual Gulls collected distinct debris and formed the kettle, one naturally dropped piece settled in Main, nearby Gulls responded to the simulated-shot damage context, and the existing wave-completion hooks cleared FOD/kettle/supplies and entered Shopping. The saved scene diff contains only the eight added supply components and the new manager/kettle root; existing marker layout and other gameplay settings are unchanged.

## Historical wave-loop proposal — superseded by S2's implementation

After human review/commits and merge into **develop**, start the separate wave feature from updated develop and coordinate with S2. Its implementation must:

- Use `WaveData`/species prefab references and the existing `BirdPointType.Spawn` markers, not numeric enum ordering or sandbox spawners.
- Implement the agreed **three waves with Gull, Pigeon, Crow and Hawk**, confirming mixed-wave counts, timing, economy and weapon/shop availability with the team rather than inventing final balance here.
- Set the GameManager's total waves consistently to three, call `BeginWave`, run spawning/timer/kill completion, and call `CompleteWave` once.
- Let the existing between-wave shop open; respond to `ShopClosed` by advancing to the next wave. Final wave completion must reach Victory rather than a fourth wave.
- Remove remaining birds/projectiles safely on timeout/game over/restart, avoiding accidental kill rewards for cleanup. Existing Gull FOD/kettle cleanup responds to `WaveCompleted`; do not duplicate managers.
- Exercise at least the real Gull weapon interaction, shelter/cover behaviour and all-species coexistence as production integration evidence. Keep detailed performance-spike investigation/development-player profiling as a tracked follow-up.

No Git changes are staged, committed, pushed or merged by the integration tools.

This completes the Main **environment-readiness** delivery for review on the Gull branch. After the human commits and reviews the changes, merge into **develop**. The absence of production waves and the performance spike are documented follow-ups, not completed acceptance items. Do not describe the whole game or every G6 requirement as finished when merging this delivery.
