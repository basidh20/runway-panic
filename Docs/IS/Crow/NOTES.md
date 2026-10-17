# Crow — "The Thief With a Grudge" (IS notes)

Design and evidence for the crow agent (SE3062). Design reference: `BIRD_STRATEGIES.md` §4, build plan: `03_CROW_PLAN.md`.
Code: `Assets/_Project/Scripts/Agents/Crow/`. Tuning: `ScriptableObjects/Birds/Strategy/CrowStrategyConfig.asset`.
Sandbox: `Scenes/Sandbox/Sandbox_IS_Crow.unity` (**B** spawns 4 crows, **F3** state labels, **F4** crow stats overlay).

**AI techniques:** influence map feeding dynamic A\* costs · agent memory with decay · goal switching (attack / steal / mourn / guard).

---

## 1. State machine

```
Patrol ──player within 30 m──► Approach ──arrived at approach point──► Attack ──hit / 6 s──► Retreat ──► Approach
   ▲  ◄──player beyond 45 m─────┘                                                                         │
   │                                                                                                       │
   ├── unguarded pickup (Patrol/Approach) ──► StealFly ──grab──► CarryToStash ──drop at stash──► Patrol
   │
   ├── ally crow died ≤ 15 m (not carrying) ──► Funeral (3 s) ──► Enraged (10 s) ──► Approach
   │
   └── player in a shelter ≥ 1 s (nearest 3 crows) ──► Vigil (land at an exit) ──player leaves──► Enraged (3 s)

Any state + own death ──► Dead (drops carried loot / releases claim)
```

- **Goal priority:** Funeral > Steal > Attack. Enraged overrides caution (flies straight, ignores the danger map).
- A crow **carrying loot** keeps it: it skips funerals and vigils.
- One partial file per behaviour (`CrowBrain.Patrol.cs`, `.Approach.cs`, …). The core (`CrowBrain.cs`) only owns the
  state switch, so every behaviour can be read and explained on its own.

## 2. The grudge — danger influence map

`CrowDangerMap` is a 2D grid (XZ) of floats over the flight grid's footprint, cell size `dangerCellSize` = 2 m
(airport 100 × 80 m → 50 × 40 = 2 000 cells).

**Writing (on every crow death)** — `BirdRegistry.BirdDied` gives the death position and the shooter's position:

| Stamp | Shape | Value at distance *d* |
|---|---|---|
| Shooter's spot | disc, radius *R* = 6 m | `s · (1 − d/R)`, *s* = 1.0 |
| Line of fire (shooter → body) | capsule, radius *r* = 2 m | `0.6 · s · (1 − d/r)`, *d* = distance to the segment |

Each cell is clamped to `maxDanger` = 5, so repeated kills can never wall an area off completely.

**Reading:** `GetDanger(p)` bilinearly interpolates the four nearest cell centres (smooth costs, no stair steps).

**Plugging into A\*:** the map implements `ICostModifier`:

```
extraCost(p) = danger(p) × dangerCostScale        (dangerCostScale = 20)
stepCost     = |step| × cellSize + Σ modifiers.extraCost(next cell)
```

- A grid step costs 2 (cell size). A cell at danger 1 adds 20 → worth a 10-cell detour; a maxed cell (5) adds 100.
- **A\* stays optimal:** extra costs are always ≥ 0 and the heuristic (octile distance × cell size) is unchanged,
  so it still never overestimates the true cost → admissible → the path found is the cheapest for the *modified* costs.
- Every crow path passes `[dangerMap, FearCostModifier(Crow)]`, so the hawk's fear field also bends crow paths.
- **Path smoothing must respect costs.** Plain string-pulling only checks walls, so in open air it straightened every
  danger detour back through the danger. `PathSmoother.Smooth(..., costModifiers, cellSize)` only takes a shortcut if
  it carries no more extra cost than the part of the path it replaces (shared change to `Scripts/AI/`, own commit, reviewed by the F1 owner).

## 3. Forgetting — decay maths

Every `dangerDecayInterval` *T* = 5 s every cell is multiplied by *f* = 0.9 (values below 0.01 are zeroed).

```
danger(t) = danger₀ · f^(t/T)

half-life  t½  = T · ln 0.5 / ln f = 5 · (−0.693) / (−0.105) ≈ 32.9 s
to 10 %    t   = T · ln 0.1 / ln f = 5 · (−2.303) / (−0.105) ≈ 109 s
```

- The crows "forget" a spot about half a minute after the player stops killing there.
- A new wave keeps `dangerKeptOnNewWave` = 50 % (`GameManager.WaveStarted`).
- Cost: O(cells) = 2 000 multiplications once every 5 s — never per frame.
- The half-life is also computed live by `CrowStrategyConfig.DangerHalfLife` and shown in the F4 overlay.

## 4. Approach-point selection

`ApproachPointSelector` scores `approachSamples` = 8 points on a ring (radius 6 m, 2 m above the player):

```
score(P) = − danger(P)
           − 0.5 · danger(midpoint crow → P)          don't cross a kill zone on the way
           + 0.3 · angle(P, last approach dir) / 180    come from a NEW side each time
           − 0.05 · |crow − P| / (2 · radius)           tie-break: the crow's own side
```

Non-flyable candidates are discarded. The approach point **moves with the player** (same direction and distance),
and the path is re-planned every `repathInterval` = 0.75 s.

## 5. Theft

- Scan every 1 s (Patrol/Approach only); the scene's pickups are cached, refreshed every 5 s and on a new wave.
- A pickup is a target if it is: active, unclaimed, not already carried, not lying in a stash, ≥ 4 m from the player,
  ≤ 40 m away, and not remembered as unreachable (e.g. inside a sealed shelter, remembered 15 s for all crows).
- `CrowPickupClaims`: one crow per pickup, at most `maxConcurrentThieves` = 2 thieves at once.
- Carrying: the pickup's colliders are disabled and it is parented to the crow's `Head` bone, so it is visible
  (the tell) and the player can't collect it in mid-air. `Pickup.cs` is not modified.
- Shooting the thief drops the pickup where it falls (`TheftsRecovered`).
- **Side effect (intended):** `PickupSpawner` still counts a stolen pickup as occupying its marker until the player
  collects it, so theft also delays respawns.

## 6. Funeral, rage, vigil

- **Funeral:** crows within 15 m of a dead crow circle 3 m above the body (radius 2.5 m, 70 % speed) for 3 s — slow
  and predictable: the player's window. Then **Enraged** for 10 s at 140 % speed, flying straight at the player.
  Risk–reward: shoot the mourners (easy kills, but the survivors get angrier) or wait it out.
- **Vigil:** the A\* grid treats shelters as sealed (the doorway is narrower than a cell plus clearance), so crows
  can't follow the player inside. Instead, once the player has been inside ≥ 1 s, each crow ranks itself by distance
  to the shelter among living crows; the nearest 3 take the shelter's `ShelterExit` markers round-robin, land and face
  the door. When the player walks out: 3 s burst of rage. No central coordinator — each crow decides independently.
  A crow that hasn't landed within `vigilLandTimeout` (3 s) stops and hovers within 1.5 m of its exit instead —
  a workaround for the shared motor's landing bug (`BirdMotor.LandAt` can circle its point; see IS handoff).

## 7. Evidence (fill in from playtests)

| Measurement | How | Result |
|---|---|---|
| Crow path with vs without danger | Sandbox: kill 3 crows from one spot, screenshot the path gizmo; then `[CrowDangerMap]` → *Debug: Clear*, screenshot again | _screenshots: `Docs/IS/Crow/path_danger.png`, `path_clear.png`_ |
| Approaches from a new side after kills | F4 overlay + watch: same spot, 5 kills | _e.g. 4 of 5 approaches from a different side_ |
| Thefts tried / stashed / recovered | F4 overlay after a 3-minute session | _ / _ / _ |
| Danger half-life observed | F4 *hottest cell* at t = 0 and t = 33 s | _expected ≈ half_ |
| Script cost, 6 crows + map | Profiler, `CrowBrain.Update` + `CrowDangerMap.Update` | _target < 0.5 ms_ |

## 8. Tuning changes

| Date | Value | From → to | Why |
|---|---|---|---|
| | | | |

## 9. Viva one-liner

"Crow deaths write danger into an influence map that raises A\* costs, so crows learn to avoid the player's firing
positions. The memory decays with a 33-second half-life, so they forget when you move — and the heuristic stays
admissible because the extra costs are never negative."
