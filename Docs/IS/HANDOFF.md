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

## Who owns what

| Part | Folder | Owner |
|---|---|---|
| F1 Pathfinding (3D flight grid + A*) | `Scripts/AI/` | Basidh (Hawk owner) |
| F2 Player event hub, registries, `BirdPoint` markers, base brain | `Scripts/Agents/Shared/` | Basidh (Hawk owner) |
| F3 `BirdHealth`, `BirdContactAttack` | `Scripts/Agents/Shared/` | Agent Controller |
| F4 Real `BirdMotor` + Animator-based bird animator | `Scripts/Agents/Movement/` | Agent Controller (GV graded) |
| F5 Shared components on the bird prefabs | `Prefabs/Birds/` | Core Developer (Basidh) |
| Species brains | `Scripts/Agents/<Species>/` | one owner per species |

Species and wave order (team decision, 17 Oct): **Gull (wave 1) → Pigeon → Crow → Hawk (wave 4, toughest)**.

---

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
  - **Shelters are sealed air to the grid**: their doorways are narrower than a cell plus clearance (~2.8 m). A path to a player inside a safe room fails instantly (`TryFindPath` returns false — no frame spike). Plan your brain's "player is hiding" behaviour around that (e.g. crow Vigil: wait outside the door).
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
- **Action needed:** Systems Engineer — make the change, or reply if you prefer a different mapping. HP/damage/coin numbers are yours to balance.
- **Status:** open
