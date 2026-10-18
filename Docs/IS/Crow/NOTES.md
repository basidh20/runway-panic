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

Patrol / Approach / Attack / Retreat + a shot passes within 3 m (miss) ──► Evade (jink sideways, 0.7 s) ──► Approach (new side)

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
  ≤ 40 m away, not inside a shelter (`ShelterZone.Contains` — birds cannot fly indoors, so it is never claimed),
  and not remembered as unreachable (any other spot A\* can't reach, remembered 15 s for all crows).
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

### Tells (every surprise warns the player first)

| Behaviour | Tell |
|---|---|
| Theft | The pickup is visible in the crow's beak, and a quick high "ca-caw" (`SFX_Crow_Call`) when it grabs it |
| Funeral | A slow repeating caw · caw · caw pattern (`SFX_Crow_Caw`, 3 caws per 1.8 s) — no other behaviour sounds like it |
| Rage (after a funeral or vigil) | A long, low, rough call (`SFX_Crow_Rage`) as the crow turns on the player |

The calls are synthesised by `Tools > IS > Crow > Generate Crow Calls` (buzzy voice through two nasal resonances, rising-then-falling
pitch, rasp and breath noise; caw ≈ 680 Hz, call ≈ 800 Hz, rage ≈ 540 Hz). Each call's pitch varies ±8% so several crows
never sound like one.

### Evasion

A crow that is patrolling, approaching, attacking or retreating dodges when a shot passes within `evadeRadius` (3 m) without
hitting it (patrol was added on 18 Oct: the crow's gun is now the 150 m sniper, so crows are mostly shot at while patrolling):
it jinks `evadeDistance` (4 m) sideways — away from the line of fire, on its own side of it — at 1.5× speed, then picks a new
approach point. The novelty term in the approach score makes it come from a different side (measured: 161° change). A
`evadeCooldown` (2.5 s) stops endless dodging. Missing a crow costs the player the crow's position, not just the bullet.

## 7. Evidence (measured 17 Oct, re-measured 18 Oct with the gull-progression stats)

Measured by scripted playtests, re-runnable from the menu: **`Tools > IS > Crow > Run Evidence Playtest`** (Play mode in
`Sandbox_IS_Crow`; full results in [evidence.md](evidence.md)), **`Run Main Integration Check`** (Play mode in `Main`;
[main_integration.md](main_integration.md)) and **`Run Wave 3 Playtest`** (plays the real wave 3 in `Main`;
[wave3_playtest.md](wave3_playtest.md)). Kills go through the real death path: `PlayerShooting.ShotFired`, then one hit
with the real sniper's damage.

| Measurement | Result |
|---|---|
| Stats (18 Oct) | Crow **60 HP**, needs the **sniper** (60 damage): **1 shot** kills. Wrong guns do 10%: pistol 60 hits (24.5 s), assault rifle 50 (7.9 s), RPG 4 (15 s) |
| Near miss (18 Oct) | A sniper shot 1.5 m past an approaching crow: it **dodged** 4.5 m; the next sniper shot 1.25 s later (inside the 2.5 s dodge cooldown) **killed it** — a miss costs one shot, not the crow |
| Grudge written by deaths | 3 crows shot from one spot (one sniper shot each, all died) → 3 danger stamps; danger at the shooter's spot **3.01** |
| A\* with vs without danger (same 44 m flight across the kill zone) | Without danger costs: 44.0 m, crosses **19.5** danger (peak 3.01). With danger costs: 47.2 m (**+7%**), crosses **0.1** (peak 0.02) |
| Picture | ![paths](path_comparison.png) Blue = no danger costs (straight through), green = what crows fly (around) |
| Approach side after kills | Crows at 8 bearings around the player: approaches within 30° of the line of fire **1 of 8 before → 0 of 8 after**; the crow sitting on the kill line switches to **−90°**; a live crow there also came in at −90° |
| Forgetting | Danger at the shooter's spot **3.01 → 1.44** after 33 s (7 steps × 0.9; the map decays on its own 5 s tick, so 33 s holds 6 or 7 steps) — matches the 32.9 s half-life |
| Thefts | 2 tried, **1 delivered** to a stash, **1 recovered** (thief shot in flight); delivered loot left alone afterwards |
| Script cost, 6 crows + map (Editor) | **0.019 ms** per frame on average (worst frame 0.40 ms, an A\* search) — target was < 0.5 ms |
| In the real level (`Main`, 18 Oct) | **7 of 7** checks pass with the real `FlightGrid`, `P_CrowSetup_Main` and waves (paused for the check): A\* routes (27–30 m flown in 6 s each), approach, steal, deliver to a stash, vigil at the shelter exit, rage on leaving, funeral → rage |
| Wave 3 of the real game (18 Oct) | 8 crows + 5 pigeons from the WaveManager. Watching 40 s: no crow stuck; approaches and attacks on the player; 1 theft tried, 1 delivered. Fighting (sniper, every third crow shot a near miss): **3 of 3 near misses dodged**, wave cleared in 22 s |

## 8. Tuning and fixes

No tuning values needed changing: every measurement above uses the defaults in `CrowStrategyConfig`.

Bugs the playtests found and fixed (17 Oct):

| Found by | Bug | Fix |
|---|---|---|
| Theft session | Crows re-stole loot lying near a stash: a crow that can't path the last metres drops it a few metres short, outside the "at a stash" radius, so the loot was shuffled around forever | `CrowPickupClaims.MarkStashed`: a delivered pickup is crow loot and never stolen again until the player collects it |
| `Main` check | Vigil crows waited forever if the player left the shelter without walking out (respawn after dying inside): Unity sends no trigger-exit when the `CharacterController` is switched off to move the player | `ShelterZone` also checks every frame that the player is still inside its box |
| Vigil test | Crows circled their shelter exit forever (`BirdMotor.LandAt` bug) | After `vigilLandTimeout` the crow stops and hovers at the exit |

Re-checked 18 Oct against the gull-progression stats (60 HP, sniper) and in the real game. Still no tuning value changed.

| Found by | Problem | Fix |
|---|---|---|
| Wave 3 playtest | 6 thefts tried, 0 delivered: two of Main's four pickup spots are inside the shelters, which birds can't fly into. Crows kept claiming that loot, failing to path, and retrying every 15 s | Crows skip loot inside a `ShelterZone` before claiming it (`ShelterZone.Contains`). Re-run: 1 tried, 1 delivered |
| Wave 3 playtest | Near misses on patrolling crows never caused a dodge (0 dodges): evasion only ran while approaching, attacking or retreating, but the 150 m sniper mostly shoots crows that are still patrolling | Patrol added to the states that dodge. Re-run: 3 of 3 near misses dodged |
| `Main` check | "A\* routes" failed 2 of 3 with the real grid — a test error: it measured straight-line distance from the start, and a patrolling crow often flies out and back | The check measures the distance actually flown (27–30 m per crow in 6 s) |
| Wave 3 playtest (open) | A crow carrying loot brushed the invisible north boundary during the final hop to the terminal stash (the stash sits mid-way in a 5.5 m gap between the terminal and the boundary) | Not changed: it is the motor overshooting a direct hop, the same steering issue as `LandAt` (Agent Controller) |

## 9. Viva one-liner

"Crow deaths write danger into an influence map that raises A\* costs, so crows learn to avoid the player's firing
positions. The memory decays with a 33-second half-life, so they forget when you move — and the heuristic stays
admissible because the extra costs are never negative."
