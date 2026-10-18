# Hand-off notes (Intelligent Systems → team)

Requests and delivery notes for the shared bird AI (`Scripts/AI/`, `Scripts/Agents/Shared/`). Newest first.
The build plan is `00_SHARED_FOUNDATION.md` (phases F1–F5); each species owner then builds only their own brain.

Template:
```markdown
## <YYYY-MM-DD> — <to: World Builder | Systems Engineer | Core Developer | Agent Controller | species owners | all> — <subject>
- **What:** script / prefab / asset path
- **Details:** settings, events, …
- **Action needed:** what the other member should do
- **Status:** open | done
```

---

## 2026-10-18 — to: all — crow re-checked with the new stats and in the real game
- **What:** `Scripts/Agents/Crow/` (theft scan, evasion, `ShelterZone.Contains`), crow test tools in `Scripts/Agents/Crow/Debug/`, reports `Docs/IS/Crow/evidence.md`, `main_integration.md`, `wave3_playtest.md`; summary in `Docs/IS/Crow/NOTES.md` §7–8.
- **Details:**
  - Stats: crow 60 HP, one sniper shot kills; other guns do 10% (pistol 60 hits, rifle 50, RPG 4). A near miss makes the crow dodge, and the next sniper shot (inside the dodge cooldown) still kills it.
  - Evidence re-run in the crow sandbox (kills now use the real sniper), the `Main` check (7 of 7 with the real `FlightGrid`, waves paused during it), and a new `Tools > IS > Crow > Run Wave 3 Playtest` that plays the WaveManager's real wave 3.
  - Fixed: crows no longer try to steal loot inside a shelter (two of Main's pickup spots are indoors, where birds can't fly); patrolling crows now dodge near misses too (the sniper reaches 150 m, so crows are mostly shot at while patrolling).
- **Action needed:**
  - World Builder: nothing. Pickups inside the shelters are fine — they are the player's safe loot, and crows now ignore them.
  - Agent Controller: a carrying crow overshot its final direct hop and brushed the invisible north boundary near the terminal stash — the same steering overshoot as `LandAt` (entry below).
- **Status:** done

## 2026-10-18 — to: Gull / Pigeon owners — Main behaviour markers prepared
- **What:** Main's `GullPigeonBehaviourPoints`: one KettleCenter at (0,10,0), eight ground-level FodSpawn markers along the runway edges, and three Roost markers on the hangar roof, terminal roof and Plane A wing. See `Docs/WorldBuilder/GullPigeonMarkerIntegration.md` for positions/radii and runtime wiring still required.
- **Details:** Existing shared `BirdPoint` only; no brains, FOD source components/prefabs or test spawners added. Existing eight Spawn points, Hawk perches and Crow setup retained. One focused Edit-mode surface/approach check passed, not live species testing.
- **Action needed:** after the four-milestone branch merges into **develop**, use these markers in the species sandboxes/runtime integration. Gull: add G2 FodSpawnPoint wiring when implemented; keep kettle heights 6–14 m above ground, not added again to the y=10 anchor. Pigeon: respect each roost radius (small wing radius 0.6 m); generate rally goals dynamically. All bird work refers to `BIRD_STRATEGIES.md`, plus `01_GULL_PLAN.md` for Gull. The current team decision is **three waves with four species**, overriding older four-wave examples below.
- **Status:** scene preparation complete on `feature/world-ai-integration`; human fourth commit/PR into develop pending. Gull/Pigeon runtime AI and production waves remain separate.

## 2026-10-17 — to: S2 + all species owners — confirmed species/weapon data corrected
- **What:** shared bird enum/data, weapon target matching, shop text; details and tuning rationale in `Docs/WorldBuilder/BirdProgression.md`.
- **Details:** Gull replaces Eagle at the same serialized ID 3 and keeps the asset GUID. IDs Pigeon=0, Crow=1, Hawk=2 remain unchanged; introduction order is explicitly Gull → Pigeon → Crow → Hawk, not numeric enum order. Pistol → Gull (30 HP), AR → Pigeon (20 HP), sniper → Crow (60 HP), RPG → Hawk (300 HP). Kill coins are 8/10/15/25. One focused asset/reference/damage check passed (41 checks); no full-wave balance claim.
- **Action needed:** after merge, pull develop before editing shared data. Do not recreate an Eagle asset or reorder enum IDs. S2 can tune the baseline further; AI owners should check their existing brains against updated values. Wave counts/economy still need integration playtesting.
- **Status:** implemented on `fix/gull-species-progression`; human commit/merge into develop pending.

## Who owns what

| Part | Folder | Owner | Status (17 Oct) |
|---|---|---|---|
| F1 Pathfinding (3D flight grid + A*) | `Scripts/AI/` | Basidh (Hawk owner) | ✅ merged (PR #18/#19) |
| F2 Player event hub, registries, `BirdPoint` markers, base brain | `Scripts/Agents/Shared/` | Basidh (Hawk owner) | ✅ merged (PR #18/#19) |
| F3 `BirdHealth`, `BirdContactAttack` | `Scripts/Agents/Shared/` | Agent Controller | ✅ merged (PR #23) |
| F4 Real `BirdMotor` + Animator-based bird animator | `Scripts/Agents/Movement/` | Agent Controller (GV graded) | ✅ merged (PR #23) — landing bug open (below) |
| F5 Shared components on the bird prefabs | `Prefabs/Birds/` | Core Developer (Basidh) | ✅ merged (PR #19) |
| Species brains | `Scripts/Agents/<Species>/` | one owner per species | Hawk merged (PR #25–#27); Crow C1–C7 merged (PR #28); Gull, Pigeon open |

Species and wave order (team decision, 17 Oct): **Gull (wave 1) → Pigeon → Crow → Hawk (wave 4, toughest)**.

---

## 2026-10-17 (night) — to: World Builder — hawk vantage markers in `Main`, and remove `GreyboxPreview_TEMPORARY`
- **What:** `Scenes/Main.unity` (yours — I don't edit it).
- **Details:**
  - **Vantage markers:** the hawk circles 4 m above `BirdPoint` markers of type **Perch** and picks one by score (height, view of the player, distance, sun side). Without any in `Main` it never leaves its spawn point. Please add an empty parent `HawkPerches` with 8 children, each an empty GameObject + `BirdPoint` component (defaults: type Perch, radius 1.5), at these world positions (the top surfaces of your greybox, read 17 Oct):

    | Name | Position (x, y, z) | Where |
    |---|---|---|
    | `Perch_ControlTower` | (-29.4, 14.6, 24.4) | tower roof, corner facing the runway |
    | `Perch_TerminalRoof` | (20.0, 6.4, 25.9) | terminal roof, runway-side edge |
    | `Perch_Hangar_NE` | (-31.9, 6.0, -0.6) | hangar roof, north-east corner |
    | `Perch_Hangar_SE` | (-31.9, 6.0, -24.8) | hangar roof, south-east corner |
    | `Perch_PlaneA_Tail` | (-17.6, 3.5, -28.45) | utility aircraft tail fin |
    | `Perch_PlaneB_Tail` | (36.45, 3.3, -1.0) | light aircraft B tail fin |
    | `Perch_PlaneD_Tail` | (41.15, 3.3, 23.0) | light aircraft D tail fin |
    | `Perch_ShelterB_Roof` | (21.0, 4.3, -12.0) | shelter B roof centre |

    If you move a building, move its marker with it (or parent the marker to the building). The same list is in `Scripts/Agents/Hawk/Editor/HawkPerchSetup.cs`; its menu (`Tools > IS > Hawk > Place Sandbox Perches`) refuses to run on `Main` on purpose.
  - **`GreyboxPreview_TEMPORARY`:** its `CharacterController` blocks the birds' line-of-sight checks (in the hawk sandbox the hawk was blind at spawn until it was disabled), and its camera is a second one tagged `MainCamera`, so anything using `Camera.main` may get the wrong camera. The real player is `P_Player_Gameplay`.
- **Action needed:** Hawk owner — after the AI-level batch merges into develop, test live Hawk behaviour in Main. The eight exact handoff markers are placed as Perch points (group HawkVantage), the FlightGrid is installed, and `GreyboxPreview_TEMPORARY` stays disabled. See `Docs/WorldBuilder/HawkPerchIntegration.md`.
- **Status:** scene setup implemented on `feature/world-ai-integration`, human commit/merge pending. Focused Edit-mode geometry/selector check passed; live Hawk flight not tested in this milestone.

## 2026-10-17 (evening) — to: Agent Controller + species owners — `BirdMotor.LandAt` can circle its landing point forever
- **What:** `Scripts/Agents/Movement/BirdMotor.cs` — `LandAt` (and `HoldPosition`) via `SteerToTarget` / `SteerToPoint`.
- **Details:**
  - Seen with the hawk (`P_Bird_Hawk`, `BirdMotion_Hawk`: turn 90°/s, acceleration 5): after an attack it was sent `LandAt` the control-tower roof and **circled it from t = 42 s to t = 98 s+** (`Logs/Editor.log`: height swinging 13.8 ↔ 15.9 m every ~2 s, never `IsLanded`). The first landing worked only because the hawk arrived head-on.
  - Cause (reading the code): near the point the target speed is `min(cornering speed, √(2·a·d))`, but the heading may only turn `maxTurnRate`. When the point is beside the bird, the turn radius `v / ω` stays larger than the distance, so it orbits at ~3–4 m and never gets within `landSnapDistance` (0.3 m). The orbit is stable: braking lowers `v`, but `√(2·a·d)` keeps it just high enough.
  - It will hit every brain that lands: pigeon roosts, crow stashes, gull debris — and any bird whose approach isn't head-on.
- **Action needed:** Agent Controller — e.g. in `Land`/`Hold` mode, within a few metres, let the bird slow to a hover and move straight to the point regardless of turn rate (a flutter-in), or cap speed so `v / maxTurnRate < distance`. Species owners: until then, give your `LandAt` calls a timeout. The hawk no longer lands at all (it soars over its vantage point instead), so it is not blocked.
  - Crow (17 Oct, `fix/is-crow-vigil-landing`): vigil crows that haven't landed after `CrowStrategyConfig.vigilLandTimeout` (3 s) call `Motor.Stop()` the next time their circle passes within 1.5 m of the exit, and hover there. `HoldPosition` is not a workaround — it steers the same way. Remove the timeout once `LandAt` is fixed.
- **Status:** open

## 2026-10-17 — to: World Builder — crow setup for `Main`: drag in one prefab
- **What:** `Prefabs/Birds/Crow/P_CrowSetup_Main.prefab` (replaces the earlier "place crow markers" request).
- **Details:**
  - Everything the crow needs in a level, already positioned for `Main` (its shelters, hangar and terminal sit exactly where the crow sandbox has them):
    - `Stashes/` — 2 × `BirdPoint` **Stash** on open ground the player can walk to: behind the hangar (−47.4, 0, −16.2) and beside the terminal (25.0, 0, 37.5). Crows drop stolen pickups there.
    - `ShelterA/`, `ShelterB/` — a `ShelterZone` trigger filling each shelter and a `BirdPoint` **ShelterExit** at its doorway. Crows guard the exit while the player hides inside.
  - Checked in `Main` (17 Oct): zones inside the shelters, exits at the doorways with a clear view in, stashes on `Ground_100x80` with nothing in the way.
  - Played in `Main` with this prefab and a `FlightGrid` added for the test only: **7 of 7 crow checks pass** (A\* routes, approach, steal Main's pickups, deliver to a stash, vigil, rage on leaving, funeral) — `Docs/IS/Crow/main_integration.md`. Re-run any time: `Tools > IS > Crow > Run Main Integration Check` in Play mode.
  - Keep the prefab root at (0, 0, 0). If you move a shelter, the hangar or the terminal, tell the crow owner (or move the matching child with it).
  - The crow also needs, in `Main`: a **`FlightGrid`** (pathfinding; without it crows fly in straight lines). `P_PickupSpawner` is already there (thanks).
- **Action needed:** Crow owner — after the AI-level batch merges, the delivered setup is saved at Main's origin; the FlightGrid and pickup spawner are present. See `Docs/WorldBuilder/CrowSetupIntegration.md`. Shared Crow code/config/prefab is unchanged; test normal production-wave behaviour when the wave loop is connected.
- **Status:** done — merged with `feature/world-ai-integration` (PR #44). Live crow check re-run in `Main` on 18 Oct with the real `FlightGrid`: 7 of 7 pass (`Docs/IS/Crow/main_integration.md`).

## 2026-10-17 — to: Systems Engineer — crows steal pickups (no change needed)
- **What:** `Pickup`, `PickupSpawner` (read-only use from `Scripts/Agents/Crow/`)
- **Details:**
  - Crows carry medkits and shields to a stash: they disable the pickup's colliders, re-parent it, and later put it back on the ground with its colliders on. No `Pickup` method is called and no Systems Engineer file is edited.
  - Side effect: `PickupSpawner` still counts a stolen pickup as occupying its marker until the player collects it, so theft also delays respawns. This is intended (it makes theft hurt).
- **Action needed:** none — tell the crow owner if a future `Pickup` change (e.g. pooling instead of `Destroy`) would break this.
- **Status:** done

## 2026-10-17 — to: Basidh (F1 owner) — cost-aware path smoothing
- **What:** `Scripts/AI/PathSmoother.cs`, `Scripts/AI/GridPathProvider.cs` (own commit `fix(ai): keep cost-driven detours when smoothing paths` on `feature/is-crow-core`)
- **Details:**
  - String-pulling only checked walls, so in open air it straightened every detour A\* made around cost modifiers (crow danger, hawk fear) back through the costly area.
  - New overload `PathSmoother.Smooth(path, radius, mask, costModifiers, sampleSpacing)`: a shortcut is taken only if it is clear **and** its extra cost is ≤ the part of the path it replaces. With no modifiers it behaves exactly like before. `GridPathProvider` now calls it.
- **Action needed:** none — merged into `develop` with the crow (PR #28).
- **Status:** done

## 2026-10-17 — to: species owners + Agent Controller — the bird prefabs now carry the shared AI components (F5)
- **What:** `Prefabs/Birds/P_Bird_Base` (inherited by `P_Bird_Gull`, `_Pigeon`, `_Crow`) and `P_Bird_Hawk` (own prefab, set up separately). Re-runnable tool: `Tools > S3 > Add Bird AI Components`.
- **Details:**
  - Root: kinematic `Rigidbody` (no gravity, interpolated), `SimpleBirdMotor`, `BirdHealth`, `BirdContactAttack`, `BirdStateLabel`. Model child: `BlendShapeBirdAnimator`.
  - `BirdHealth.species` per prefab: Gull → `BirdSpecies_Eagle` (becomes `BirdSpecies_Gull` after the rename; same GUID, nothing to redo), Pigeon → `BirdSpecies_Pigeon`, Crow → `BirdSpecies_Crow`, Hawk → `BirdSpecies_Hawk`. `P_Bird_Base` has none.
  - No brain is on any prefab. An idle `SimpleBirdMotor` (no orders) does not touch the Rigidbody, so prototypes that move the transform directly still work.
  - ⚠ If you added these components by hand to a bird **in a scene**, that bird now has them twice (e.g. two `BirdHealth` → counted twice in `BirdRegistry`). Delete the scene bird and place the prefab again.
- **Action needed:**
  - Species owners: add **only your brain** to **your** prefab (`P_Bird_Gull` / `_Pigeon` / `_Crow` / `_Hawk`) — never to `P_Bird_Base`.
  - Agent Controller (F4): replace `SimpleBirdMotor` and `BlendShapeBirdAnimator` on **`P_Bird_Base` and `P_Bird_Hawk`** (the variants follow the base). Assign the bird Animator Controller on both model children (see `SourceArt/HANDOFF.md`, 11 Oct).
- **Status:** done

## 2026-10-17 — to: Agent Controller — F3 replaces two stubs; F4 implements two interfaces
- **What:** `Scripts/Agents/Shared/BirdHealth.cs`, `BirdContactAttack.cs` (stubs), `IBirdMotor.cs`, `IBirdAnimator.cs`
- **Details:**
  - F2 had to create `BirdHealth` and `BirdContactAttack` so the shared code compiles. They are **API-only stubs**: no damage, never attack. `BirdHealth` already registers with `BirdRegistry` and implements `IDamageable` (pathfinding ignores colliders with an `IDamageable`).
  - **F3: edit these two files in place** (branch from `develop` after F2 is merged — don't create new files with the same names, or git reports an add/add conflict). Keep every public member and the serialized field name `species` (prefabs store it). On death call `BirdRegistry.NotifyDied(this, LastHitShotOrigin)`.
  - **F4:** `BirdMotor : MonoBehaviour, IBirdMotor` and an Animator-based `IBirdAnimator`. `SimpleBirdMotor` / `BlendShapeBirdAnimator` are throwaway stand-ins; swap them on the prefabs and no brain changes.
- **Action needed:** Agent Controller — F3 then F4 as in `00_SHARED_FOUNDATION.md`.
- **Status:** open

## 2026-10-17 — to: species owners — shared bird plumbing (F2) is available
- **What:** `Scripts/Agents/Shared/` — `BirdBrain`, `IBirdMotor`, `IBirdAnimator`, `PlayerEventHub`, `BirdRegistry`, `FearRegistry` (+ `IFearSource`, `FearCostModifier`), `BirdPoint`, `BirdStateLabel`, `BirdTestSpawner`; examples/tests in `Scripts/Agents/Shared/Debug/`
- **Details:**
  - Your brain: `public sealed class GullBrain : BirdBrain` in `Scripts/Agents/Gull/`. `TestChaseBrain` is a short working example (chases the player with A*).
  - Move with `Motor.*`, animate with `Anim.*`, read the player only through `Hub` (position, velocity, `CurrentWeapon`, `IsReloading`, events). Path with `RequestPath(target, costModifiers)`; if it returns false and `LastRequestWasThrottled` is true, just retry next frame.
  - Overriding `Awake` / `OnEnable` / `OnDisable`: call `base.` first. Return cached strings from `CurrentStateName` (it is shown above the bird; **F3** toggles the labels).
  - Unsubscribe from the hub you subscribed to (store the reference); don't read `PlayerEventHub.Instance` in `OnDestroy`.
  - Sandbox shortcut: `Tools > IS > Set Up Agents Test Scene` adds a grid, the player (with all four guns), the GameManager, a test hawk and `AgentsDebugLogger` to the open scene.
- **Action needed:** F5 is done (see the entry above): add only your own brain to your species prefab.
- **Status:** done

## 2026-10-17 — to: species owners — flight pathfinding (F1) is available
- **What:** `Scripts/AI/` — `FlightGrid`, `PathService`, `IPathProvider`, `ICostModifier`; test tool `Scripts/AI/Debug/PathDebugProbe`
- **Details:**
  - Add one empty GameObject with `FlightGrid` to your sandbox scene. Default bounds cover the 100 × 80 m airport from 0.5 m to 24.5 m, in 2 m cells (Bounds Center Y = 12.5 — check it if you copied an older grid). It builds itself on Play (one log line with cell count, regions and build time).
  - Check it any time in Edit mode with `Tools > IS > Pathfinding Self-Test` (results in `Docs/IS/pathfinding_report.md`).
  - Ask for paths only through `PathService`: `if (PathService.TryConsumeBudget()) PathService.Current.TryFindPath(from, to, costModifiers, list);`. A refused budget means "try again next frame" (max 6 searches per frame, set on the grid).
  - To make your species avoid places without blocking them, pass an `ICostModifier` (extra cost ≥ 0 per cell).
  - Gull debris: `FlightGrid.Instance.SetManualBlock(bounds, true)` when it lands, `(bounds, false)` with the **same bounds** when it is removed. Blocks stack, so overlapping debris is safe.
  - Moved barricades update the grid automatically (`Barricade.Settled`).
  - Grid paths fly at ≥ ~1.5 m; the last waypoint is the exact target when it is flyable and in sight of the last cell. Use `IBirdMotor.FlyTo` (F2) for the final swoop or landing.
  - **Shelters are sealed air to the grid** (the self-test finds 3 free-air regions: outside + 2 shelters): the 2.8 m doorway is no wider than one cell plus its clearance (2 m + 2 × 0.4 m), so no cell in it is free, door open or not. A path to a player inside a safe room fails instantly (`TryFindPath` returns false — no frame spike). Plan your brain's "player is hiding" behaviour around that (e.g. crow Vigil: wait outside the door). The shelter doors themselves are not placed in `Main` yet (Systems Engineer's request to the World Builder, 16 Oct).
- **Action needed:** none yet; brains start after F2 (`BirdBrain.RequestPath` wraps all of the above).
- **Status:** done

## 2026-10-17 — to: Systems Engineer — rename Eagle → Gull and re-map stats and guns to the wave order
- **What:** `Scripts/Core/BirdSpecies.cs`, `ScriptableObjects/Birds/BirdSpecies_*.asset`, `ScriptableObjects/Weapons/Weapon_Tier*.asset`
- **Details:**
  - The enum and assets still use `Pigeon = 0, Crow = 1, Hawk = 2, Eagle = 3`, and the guns follow that order: Pistol → Pigeon, AR → Crow, Sniper → Hawk, RPG → Eagle. The team's species are Gull, Pigeon, Crow, Hawk, introduced in that order.
  - Only renaming Eagle → Gull would make the **wave-1 gull need the RPG** and let the **wave-4 hawk die to the sniper**.
  - Suggested change (keeps every enum value, so no serialized data breaks):

    | Wave | Species (enum value) | Required gun (`requiredWeaponRarity`) | Weapon asset whose `targetSpecies` points to it | Stats now |
    |---|---|---|---|---|
    | 1 | Gull (3, renamed from Eagle) | Pistol — Common | `Weapon_Tier1_Pistol` → 3 | Eagle's: 300 HP — lower to wave-1 level |
    | 2 | Pigeon (0) | AR — Uncommon | `Weapon_Tier2_AssaultRifle` → 0 | 20 HP |
    | 3 | Crow (1) | Sniper — Rare | `Weapon_Tier3_Sniper` → 1 | 40 HP |
    | 4 | Hawk (2) | RPG — Legendary | `Weapon_Tier4_RPG` → 2 | 60 HP — raise to the toughest (e.g. Eagle's old 300 HP) |
  - Also: rename `BirdSpecies_Eagle.asset` → `BirdSpecies_Gull.asset` (move it with its `.meta` in Unity so the GUID stays), set its `speciesName` to Gull and its `prefab` to `Prefabs/Birds/P_Bird_Gull`. Update the enum's summary comment (it says the values are in wave order).
- **Action needed:** original rename/remap request implemented by the shared-data fix described above; S2 reviews subsequent balancing rather than repeating the migration. The table above records the old values/proposed mapping, not current tuning.
- **Status:** implemented on `fix/gull-species-progression`; merge pending. See `Docs/WorldBuilder/BirdProgression.md` for current values.
