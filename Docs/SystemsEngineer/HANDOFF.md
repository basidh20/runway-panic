# Hand-off notes (Systems Engineer → team)

Requests and delivery notes for other members. Newest first. Everything below is delivered as **prefabs** — drag them into your scene; do not copy their scripts or rebuild them. Changes I make to a prefab later update every copy automatically.

Template:
```markdown
## <YYYY-MM-DD> — <to: World Builder | Core Developer | Agent Controller | all> — <subject>
- **What:** prefab / asset path
- **Details:** placement, settings, events, …
- **Action needed:** what the other member should do
- **Status:** open | done
```

---

## 2026-10-18 — to: all — main menu, settings, and Main Menu buttons in the game
- **What:** `Scenes/MainMenu.unity` (new, first in the build), `Prefabs/UI/P_SettingsPanel.prefab`, `Prefabs/Core/P_GameManager.prefab` (screens updated); builder `Tools > S2 > Build Main Menu`.
- **Details:**
  - The game now starts at the main menu: **Play** (loading screen with tips, then `Main`), **How to Play** (goal, the right gun per bird, all controls for keyboard and gamepad), **Settings**, **Credits**, **Quit** (asks first). Calm menu music and airport ambience; animated dusk backdrop.
  - **Settings** (saved between sessions): master / music / effects / ambience volume, look sensitivity, invert Y, fullscreen, V-Sync (graphics quality appears only if the project gets more than one quality level). The same panel opens from the **pause screen**.
  - **Pause** now has Resume · Restart · Settings · Main Menu · Quit; **Game Over** and **Victory** have Try Again · Main Menu · Quit. Esc / B in Settings closes Settings without unpausing.
  - Mouse, keyboard (arrows, Enter, Esc) and gamepad (stick / D-pad, A, B) all work; a button is always highlighted, and hovering selects it.
  - `GameManager.ConsumePauseInput()` lets UI use an Esc press without also pausing / resuming.
- **Action needed:**
  - World Builder: nothing — `Main.unity` is unchanged. Keep `MainMenu` at index 0 and `Main` in the build list (`SceneFlow.GameScene` loads it by name).
  - Anyone changing a key or the bird / gun pairing: update the How to Play text in `MainMenuBuilder.BuildHowToPlay` and re-run `Tools > S2 > Build Main Menu`.
  - Credits list roles only; add names in `MainMenuBuilder` (credits text) if the team wants them, then re-run the builder.
- **Status:** done

## 2026-10-17 — to: all — the player is fully animated (move, jump, guns, reload, hit, death)
- **What:** `Prefabs/Player/P_Player_Gameplay` (already in `Main`); controller `Art/Animations/Player/AC_Player.controller`; tools `Tools > S2 > Build Player Animator` and `Tools > S2 > Generate Player Clips`.
- **Details:**
  - Clips are generated from the officer's own Humanoid rig (no downloads): idle, walk (a jog), run, jump start/fall/land, pistol and rifle aim/fire/equip/reload, hit, death.
  - Three layers: **Base** (locomotion, jump, death) · **UpperBody** (arms only: holding, firing, drawing and reloading the gun, so the legs keep moving) · **Reactions** (additive hit flinch on spine, head and arms).
  - Everything is driven by events that already exist — `PlayerController.Jumped`, `PlayerWeaponHolder.WeaponEquipped`, `WeaponBase.Fired / ReloadStarted / ReloadFinished`, `PlayerHealth.Damaged`, health reaching 0. No other script needs to call the Animator.
  - `PlayerAimIK` twists the torso so the barrel points where the camera aims, and puts the left hand on the held gun (any gun; long guns are held nearer the body).
  - Reloads last exactly each gun's `ReloadTime`. Manual reload: **R** / gamepad **RB**. Each gun's `WeaponData.holdType` (Pistol / Rifle) picks the arm pose.
  - Walk and run play faster to keep pace with the move speeds (8% / 19% foot slide left); the builder logs the numbers.
- **Action needed:**
  - Core Developer: if `SK_Player_Officer` is re-exported, keep it **Humanoid** with the same bone names, then run `Tools > S2 > Generate Player Clips` and `Build Player Animator`. Don't add an Animator Controller to `P_Player_Officer` itself — `P_Player_Gameplay` sets it.
  - Anyone adding a gun: set its `holdType` on the weapon asset; nothing else.
  - Anyone changing `walkSpeed` / `sprintSpeed`: re-run `Build Player Animator` so the foot speed matches again.
- **Status:** done

## 2026-10-17 — to: World Builder — the between-wave shop (logic + screen)
- **What:** `Assets/_Project/Prefabs/Core/P_Shop.prefab` (ShopManager + shop screen), items in `ScriptableObjects/Shop/`
- **Details:**
  - The screen opens by itself when the GameManager enters *Shopping* (after `CompleteWave()` on waves 1–3) and hides on *Next wave*, pause or game over. The mouse cursor is freed while it is open.
  - Sells the Assault Rifle (40), Sniper (75), RPG (100), Medkit (15) and Shield (25). Gun prices come from the weapon assets' *Cost*; item prices from the shop assets.
  - Buttons show BUY / OWNED / FULL / NEED x; refused purchases explain why. A bought gun is equipped immediately.
  - *Next wave* calls `GameManager.CloseShop()` → `ShopClosed` event → your WaveManager starts the next wave.
  - Tested in my sandbox: buy, refusals (closed / owned / too poor / full), pause and resume, next wave.
- **Action needed:** drag one `P_Shop` into `Main.unity`. The screen is plain greybox UI — restyle it freely (it is all in the prefab's `ShopCanvas`), just keep the object names `Name`, `Description`, `Price`, `BuyButton/Label` in the row template.
- **Status:** open

## 2026-10-17 — to: World Builder — medkits and shields on your pickup markers
- **What:** `Assets/_Project/Prefabs/Interactables/P_PickupSpawner.prefab` (spawns `P_Pickup_Medkit` and `P_Pickup_Shield`)
- **Details:**
  - Uses your `GameplayMarkers/PickupPoints` children (`Pickup_00`–`03`) automatically — no setup needed. Pickups are dropped onto the floor below each marker.
  - Keeps **2** pickups in the level at random free markers, medkits twice as often as shields. A collected one is replaced after **25 s** at a random free marker; every wave start (via the GameManager) refills straight away.
  - Tested in `Main` (Play mode only, nothing saved): spawns on your markers, replaces collected pickups on a different marker, refills on `BeginWave`.
  - Player side: walking over a medkit heals 30 HP (or stores it, Q to use); a shield halves damage for 10 s (or stores it, F to use).
- **Action needed:** drag one `P_PickupSpawner` into `Main.unity` (anywhere). Add more markers under `PickupPoints` if you want more possible spots; change *Max Active* / *Respawn Delay* on the spawner to tune.
- **Status:** open

## 2026-10-16 — to: World Builder + Agent Controller — movable barricades
- **What:** `Assets/_Project/Prefabs/Interactables/P_Barricade_Movable.prefab` — a **prefab variant** of `P_Greybox_Barricade`
- **Details:**
  - Looks exactly like your barricade and follows any change you make to `P_Greybox_Barricade` (your prefab itself is untouched). Adds a 300 kg Rigidbody and the `Barricade` script.
  - The player pushes it by walking into it: it slides at 1.5 m/s, cannot tip over, and stops as soon as the player stops. Bird bumps barely move it.
  - A **NavMeshObstacle** carves its footprint out of the NavMesh, but only once it has stood still for 0.5 s (no constant re-carving while it slides). Tested with a temporary NavMesh: the hole follows the barricade.
  - The `Barricade.Settled` event fires once each time it comes to rest, with its new world-space bounds — use it to refresh the A* grid around that area (the grid will not see carving by itself).
- **Action needed:**
  - World Builder: in `Main.unity`, swap the static barricades you want movable for `P_Barricade_Movable` (same position/rotation). Keep some static if you want fixed cover. Leave the movable ones **out of the NavMesh bake** (their obstacle carves instead), or the bake will treat their start position as a permanent wall.
  - Grid owner: subscribe to `Settled` on each barricade and mark grid nodes inside the bounds unwalkable (and the old area walkable again).
- **Status:** open

## 2026-10-16 — to: World Builder — doors for the shelters
- **What:** `Assets/_Project/Prefabs/Interactables/P_Door_Shelter.prefab`
- **Details:**
  - Sized for the shelter doorway (2.8 m × 3.0 m opening): a 2.7 m × 2.82 m physics door on a hinge, with small gaps so it never catches on the walls.
  - Pivot = **centre of the doorway, on the floor surface, in the middle of the wall**. The prefab's forward (blue Z arrow) should point **out of the building**.
  - The player presses **E** while aiming at it ("[E] Open door"). It always swings away from the player and stops at 90°.
  - For a safe room, call `Door.Open()`, `Door.Close()` or `Door.SetLocked(true)` (locking also closes it). Events: `Opened`, `Closed`. The `Door` component is on the `DoorPanel` child.
  - Tested in `Main` (Play mode only, nothing saved) in Shelter A's doorway: fits, opens away from the player from both sides, closes fully.
- **Action needed:** in `Main.unity`, make each shelter door a **child of its shelter** and set this **local** transform:

  | Doorway | Local position | Local rotation |
  |---|---|---|
  | Shelter_A / Shelter_B entrance | (0, 0.055, -3.8) | (0, 180, 0) |
  | Hangar side exit (optional) | (0, 0.055, 12.3) | (0, 0, 0) |

  The hangar exit is 3.2 m tall, so the door leaves a 0.3 m gap at the top; tell me if you want a taller variant. The 10 m main hangar entrance has no door.
- **Status:** open

## 2026-10-16 — to: World Builder — putting the player, game manager and checkpoints into Main
- **What:** `Prefabs/Player/P_Player_Gameplay.prefab`, `Prefabs/Core/P_GameManager.prefab`, `Scripts/Player/Checkpoint.cs`
- **Details:**
  - `P_Player_Gameplay` is the complete player: movement, third-person camera, health, 3 lives, respawn, guns, coins, inventory, E to interact, crosshair and prompt HUD. It brings its own camera.
  - `P_GameManager` holds the game state (Playing / Shopping / Paused / Game Over / Victory). Escape pauses. Only one per scene. It also brings the **Pause, Game Over and Victory screens** (Resume / Restart / Quit), shown automatically for those states. For *Restart* to work in a build, `Main` must be in File → Build Profiles' scene list (it already is).
  - `Checkpoint` sets the player's respawn point when they walk through its trigger.
- **Action needed:** in `Main.unity`:
  1. Drag in `P_Player_Gameplay` at `PlayerStart` and **disable `GreyboxPreview_TEMPORARY`** (as your GreyboxGuide already says), so only one camera and audio listener are active.
  2. Drag in one `P_GameManager` (anywhere).
  3. On `Checkpoint_A` and `Checkpoint_B`: add a **BoxCollider** (about 4 × 3 × 4 m) and the **`Checkpoint`** component (it ticks *Is Trigger* for you). The player respawns at the marker's position, so keep it at floor level.
- **Status:** open

## 2026-10-16 — to: World Builder (WaveManager / shop UI) — game-state hooks
- **What:** `GameManager.Instance` (`Scripts/Core/GameManager.cs`)
- **Details:** the GameManager does not run timers or spawn birds; your WaveManager does, and tells the GameManager:

  ```
  BeginWave(1) → timer ends or all birds dead → CompleteWave()
    → shop opens (ShopOpened event) → shop's "Next wave" button calls CloseShop()
    → ShopClosed event → WaveManager calls BeginWave(2) … after wave 4, CompleteWave() → Victory
  ```

  Also available: `CurrentWave`, `TotalWaves` (4), `IsWaveActive`, `IsShopOpen`, `CurrentState`, and the events `WaveStarted`, `WaveCompleted`, `StateChanged`. Calls that make no sense (e.g. `CloseShop()` when the shop is closed) return `false` and change nothing.
- **Action needed:** build the WaveManager around these calls; ask me if you need another hook.
- **Status:** open

## 2026-10-16 — to: Agent Controller — damage and coins for birds
- **What:** `IDamageable`, `BirdSpeciesData` assets in `ScriptableObjects/Birds/`, `CoinManager`
- **Details:**
  - The player's guns hit anything implementing `IDamageable` (`TakeDamage(amount, weaponRarity)`), found on the hit collider or any parent.
  - Each species asset (`BirdSpecies_Pigeon` … `_Eagle`) has HP, speed, attack stats, coin reward and the required gun. `GetDamageTaken(amount, rarity)` applies the rule "wrong gun = 10 % damage".
  - `Prefabs/Testing/P_DamageDummy` shows the expected behaviour; it is used in `Sandbox_S2_PlayerController`.
- **Action needed:** make `BirdHealth` implement `IDamageable`, take its HP from the species asset and reduce damage with `GetDamageTaken(...)`. When a bird dies, call `CoinManager.Instance?.AddCoins(species.CoinReward)`. Birds damage the player with `playerHealth.TakeDamage(species.AttackDamage, WeaponRarity.Common)`.
- **Status:** open
