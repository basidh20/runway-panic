# Hand-off notes (Agent Controller → team)

Requests and delivery notes for other members. Newest first. I never edit another member's files directly: changes I need are written here.

Template:
```markdown
## <YYYY-MM-DD> — <to: World Builder | Systems Engineer | Core Developer | IS owners | all> — <subject>
- **What:** prefab / asset path
- **Details:** API, settings, events, …
- **Action needed:** what the other member should do
- **Status:** open | done
```

---

## 2026-10-17 — to: all — open questions about the species
- **What:** `ScriptableObjects/Birds/BirdSpecies_*.asset`, `Prefabs/Birds/P_Bird_*.prefab`
- **Details:**
  - `BirdSpecies_Eagle` exists, but there is no eagle prefab (its `prefab` field is empty).
  - `P_Bird_Gull` exists, but there is no `BirdSpecies_Gull`. Tools > Agents > Setup Bird Prefabs leaves the gull without species stats: it flies with the default motion profile and fallback HP (30) and speed (5 m/s), and logs a warning.
  - I did not change either asset or prefab: the species assets belong to the Systems Engineer, the bird models and variants to the Core Developer.
- **Action needed:** decide together:
  - (a) Is the 4th species the **gull**? Then add `BirdSpecies_Gull` and drop or rename Eagle.
  - (b) Is it the **eagle**? Then the gull variant becomes the eagle: new variant or rename, plus colours.
  - Tell me the answer: I then set the species and a motion profile on that prefab with one line in `BirdPrefabSetup.cs`.
- **Status:** open

## 2026-10-17 — to: IS owners (A* and species AI) — how to drive a bird
- **What:** `BirdAgent` on every bird prefab root; full API in `Docs/AgentController/README.md`
- **Details:**
  - `bird.Follower.SetPath(Vector3[] or IReadOnlyList<Vector3>)`:
    - World space, metres, in order, positions for the bird's **pivot** (centre of mass, not the feet).
    - Send one point per grid cell exactly as A* returns it: straight runs are simplified and corners smoothed for you.
    - The list is copied, so you can reuse it.
  - **Height:** add the flying height yourself, or ask me to set `heightOffset` on the prefab if your grid is on the ground. Anything below 1 m is raised to 1 m (`minimumAltitude`).
  - **Re-plan freely:** the new path starts from where the bird is, along its current velocity, with no snap (measured in `flight_check.md`). Every 0.2–1 s is fine; please not every frame, because each call rebuilds the curve.
  - **Events:**
    - `WaypointReached(int index)` fires once for every index of **your** array, in order, including points that were smoothed away or that the bird was already standing in.
    - `PathCompleted` fires at the last waypoint; the bird then slows to a hover until it gets a new path.
  - **Attacking:** `bird.Attack.TryAttack(playerHealth)` returns true when it attacked. It checks the species `AttackRange` (to the target's collider) and `AttackCooldown`, plays the dive and deals `AttackDamage`. You decide *when* to call it.
  - **Dying:** `bird.Health.Died += b => …` fires once; coins are already paid. The body then falls and is destroyed about 2 s after landing.
  - **Stats:** speed comes from `BirdSpeciesData.MoveSpeed`; turning and banking from the species `BirdMotionProfile` in `ScriptableObjects/Agents/`. Ask me if your species should turn tighter or wider.
  - **Path quality:** with a 4-connected grid, diagonal routes become staircases. They still fly well (the look-ahead cuts across the steps), but an 8-connected grid or string-pulling gives straighter flight.
  - **Movable barricades:** `Barricade.Settled` (Systems Engineer's note of 16 Oct) is yours to subscribe to for the grid. Birds simply follow whatever path you send.
- **Action needed:** call `SetPath` from your species AI and subscribe to the events you need. `TestPathFeeder` (Scripts/Agents/Testing) shows a working example.
- **Status:** open

## 2026-10-17 — to: Systems Engineer — BirdHealth implements IDamageable (your 16 Oct request)
- **What:** `Scripts/Agents/BirdHealth.cs` on every bird prefab root (added by Setup Bird Prefabs)
- **Details:**
  - **Taking hits:** `TakeDamage(amount, rarity)` uses `species.GetDamageTaken(amount, rarity)`, so the wrong gun does 10%. HP comes from `species.MaxHP`.
  - **Collider:** the hit collider is the root `CapsuleCollider` (Core Developer's), so `GetComponentInParent<IDamageable>()` in `PlayerShooting` finds it.
  - **On a hit:** plays the flinch (Hit trigger, Reactions layer); the bird keeps flying.
  - **On death:**
    - plays Death, then calls `CoinManager.Instance.AddCoins(species.CoinReward)` (null-checked) and raises `Died(BirdHealth)`;
    - stops path following and lets the body fall with its momentum;
    - on landing it turns kinematic and drops its collider (no more hits, the player can walk over it);
    - after 2 s it is **destroyed**, or deactivated if `cleanup` is set to Deactivate (pooling).
  - Also available: `Kill()` and `Revive()` (pooling and tests), plus a `Damaged(float)` event (damage actually taken) for hit markers.
  - **Bird attacks:** `BirdAttack.TryAttack(playerHealth)` calls `playerHealth.TakeDamage(species.AttackDamage, WeaponRarity.Common)`, as you specified.
- **Action needed:** none. Tell me if the HUD needs another event.
- **Status:** done

## 2026-10-17 — to: World Builder — spawning birds in Main
- **What:** bird prefabs `P_Bird_Pigeon`, `P_Bird_Crow`, `P_Bird_Hawk` (`P_Bird_Gull`: see the species question above)
- **Details:**
  - **Spawning:** instantiate `species.Prefab` at a `BirdSpawn_xx` marker. The prefab already has everything (Rigidbody, BirdAgent, animation), and the bird flies as soon as the IS AI calls `SetPath`.
  - **Counting the wave:** subscribe to `bird.Health.Died` to count kills for `CompleteWave()`.
  - **Dead birds need ground:** bodies fall under gravity and land on any upward-facing collider. Over a hole they are removed after 6 s anyway.
  - **Scene changes:** I don't touch `Main.unity`. My test scene is `Scenes/Sandbox/Sandbox_Chamika.unity`.
- **Action needed:** spawn birds from your WaveManager as above.
- **Status:** open

## 2026-10-17 — to: Core Developer — animation is on the bird prefabs
- **What:** `P_Bird_Base` (and through it Gull, Pigeon, Crow) and `P_Bird_Hawk`, changed only by **Tools > Agents > Setup Bird Prefabs**
- **Details:**
  - **Root components:** added a kinematic, interpolated `Rigidbody` (ContinuousSpeculative) and `BirdAgent`, which brings the path follower, animator driver, health and attack. Your model child, collider, materials and blend-shape weights are unchanged.
  - **Controllers:** `AC_Bird` on the `SK_Bird` Animator; `AOC_Bird_Hawk` on the `SK_Bird_Hawk` Animator. Root motion off, culling `CullUpdateTransforms`.
  - **Species on the variants:** Pigeon and Crow got their species + motion profile; the Hawk too.
  - **What the clips key:** generated by Tools > Agents > Build Bird Animations.
    - **Bones:** `m_LocalRotation` of `Body`, `Head`, `Tail`, `Wing_L`, `Wing_R`, `Leg_L`, `Leg_R`. Never positions or scale; `Foot_L/R` are not keyed.
    - **Blend shapes:** `Wings_Spread` on `Bird_Mesh` in every clip except Hit. `Tail_Spread` only in Dive (tail closed), so outside the dive each species keeps the tail fan you set.
    - **Hawk:** gets its own clip set (`A_Bird_Hawk_*`) from its own rest pose, because its wing bones rest **34.2°** away from the gull's (`animation_report.md`). Please keep the 9 bone names. If a re-export changes the rest pose, I re-run the tool and nothing else changes.
  - **Performance:** 40 moving, animated birds are GPU-bound on drawing: 21.6–23.1 ms GPU against 3–7 ms main thread on Iris Xe (`DECISIONS.md`). Movement and animation do not change the frame rate. 2-bone skinning gave no measurable gain, so I left skin quality alone. Your levers (bird LODs, bird shadows) remain the ones that would move FPS.
- **Action needed:** none. Ask me before changing bone names or the `Bird_Mesh` blend-shape names.
- **Status:** done

## 2026-10-17 — to: all — README team table
- **What:** `README.md`, "Team roles (GV)"
- **Details:** the Agent Controller row still says _TBD_. I don't edit the shared README in my feature branch.
- **Action needed:** whoever maintains the README: Agent Controller = Chamika (GitHub `chamika-kasthuri09`).
- **Status:** open
