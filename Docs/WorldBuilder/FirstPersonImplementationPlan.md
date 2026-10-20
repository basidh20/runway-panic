# First-person conversion: implementation and S2 handoff

Prepared 20 October 2026. This is a plan, not an implemented conversion.

## Goal and boundaries

Deliver a playable first-person foundation, then give S2 ownership of weapon
and animation polish. Reuse the existing camera/input/shooting systems and gun
assets. Keep four waves, the 75-degree upward look limit, existing movement
speeds, weapon stats, shop progression, damage rules and shield immunity.

Initial presentation: an eye-height camera and a visible gun using the existing
models. Dedicated first-person arms, animated reload hands, head bob, leaning,
weapon-camera stacking and elaborate weapon collision animations are deferred
to S2 or omitted for the deadline. A floating gun is acceptable for the first
foundation review; it is not evidence that animation polish is finished.

World polish is already in develop. Do not combine this conversion with another
airport redesign or lighting bake. Pigeon work belongs to S3. Crow and Gull
behaviours do not need rewriting for this camera conversion.

## Current checkout and verified source facts

- Branch: `feature/first-person-player`, tracking its origin branch.
- Base: `824dd60`, including the shield-immunity and Medkit PRs, plus newer
  Crow and Pigeon PRs. This inspection did not playtest the updated species.
- Tracked working tree was clean. Existing untracked files
  `Docs/IS/Crow/MainMovementRepair.md` and
  `Docs/WorldBuilder/CrowPigeonRepairPlan.md` are outside this work.
- `PlayerCameraRig` drives Cinemachine and already owns mouse/controller input,
  sensitivity/invert-Y, aim/scope blending, recoil and shake. Its public camera
  properties are consumed by shooting, movement, crosshair and scope UI.
- The existing scope moves the camera to eye level. Normal hip/aim views are
  still over the shoulder: hip distance 3.2 m, aimed distance 1.6 m.
- `CameraTarget` is a stable child of the player at 1.6 m. It is not attached to
  the animated head. The CharacterController is 1.8 m high, radius 0.35 m.
- `PlayerController` already supports camera-relative movement, but turns the
  character toward movement with allowances for aiming. First person needs a
  consistent view-facing body during strafing and backwards movement.
- `PlayerWeaponHolder` creates a socket under RightHand and caches actual gun
  instances, retaining each gun's magazine when switching.
- `PlayerAimIK` turns the body and adjusts the torso/support hand. It currently
  assumes the held gun is attached to the character's hand.
- `PlayerShooting` chooses hits from the crosshair, checks cover from the actual
  muzzle and launches RPG projectiles from that muzzle. Its ray origin contains
  third-person camera-to-player compensation that needs review for eye height.
- `PlayerCameraRig.ShowBody` currently hides every renderer under the player
  while scoped. Reusing this indiscriminately would also hide guns/effects and
  can mishandle weapons created after the renderer list was cached.
- `AimingSetup.BuildPlayer` configures the shoulder camera and offsets. It must
  respect first-person configuration so rerunning S2's tool cannot undo it.
- `PlayerRespawn` warps the player/camera but the camera rig keeps its own yaw
  and pitch. Respawn orientation must be checked explicitly.

No first-person runtime result is claimed by these source facts.

## Working agreement and Git workflow

The human runs all Git mutations. The AI must not stage, commit, push, switch
branches, merge or rebase. Check branch/status and applicable AGENTS.md before
each task. Read targeted sources first. Stop at each meaningful commit boundary
so the owner can review and commit. Use real incremental work, not artificial
history splitting. Include new Unity `.meta` files with their scripts.

Tell S2 before implementation: "We are building the first-person foundation on
feature/first-person-player. Please coordinate edits to PlayerCameraRig,
PlayerController, PlayerWeaponHolder, PlayerAimIK, AimingSetup and the gameplay
player prefab. You will own final weapon/arms/animation polish after handoff."

The branch already exists; do not recreate it. At each boundary:

```bash
cd /Users/tathira/gv_game/runway-panic
git status --short --branch
git diff --check
git diff
```

Stage only the exact changed files for that task. Do not use `git add .`.
Paths below are intended scopes; implementation must list the actual files
and any new metas before the owner runs the commit command.

If develop changes during this work, save Main outside Play mode, commit the
current completed task, close Unity, then the human can integrate develop:

```bash
git fetch origin
git merge origin/develop
```

Resolve overlapping player/prefab changes deliberately with S2's contracts in
mind. Do not replace the entire player prefab with one branch's version.

## FP1 — Eye-height camera, body visibility and movement

### FP1.1: define a first-person setting in the current camera rig

- Extend `PlayerCameraRig` with an explicit first-person setting readable by
  movement, weapon presentation, visibility and setup code. Enable it on the
  gameplay prefab. Retain existing serialized third-person configuration as
  compatibility where practical; no user-facing view-switch key is required.
- Continue using the existing Cinemachine rig, camera input, public API and
  update timing. Do not add a second independent mouse-look controller.
- Place the view at the stable 1.6 m target with zero shoulder distance/arm
  offset in hip, aim and scope. Aim/scope changes FOV and sensitivity without
  moving the camera back outside the player.
- Preserve pitch limits (-75 up, +70 down), recoil, breathing sway and shake.
  Start level (pitch zero) for first person, rather than inheriting the old
  8-degree downward starting pitch. Preserve spawn yaw.
- Start with a configurable 75-degree vertical hip FOV, tune after preview,
  and retain per-weapon aim FOV. Record Unity's vertical FOV convention for S2.
- Use a small appropriate near plane (existing virtual lens is 0.05 m).
  Check the Cinemachine-driven output lens; do not assume changing only the
  raw GameplayCamera field changes the final view.
- Review the shoulder camera's obstacle avoidance. Keep the eye inside the
  capsule and check walls/corners; the old 0.2 m shoulder-camera collision
  radius must not push or jitter the eye into an unintended position.

### FP1.2: hide only the character visuals

- Explicitly identify the character model renderers. Hide them from the player
  view while retaining their shadows where feasible. Leave the model object,
  Animator, skeleton, health, controller and collider active.
- Exclude held weapons, muzzle effects, HUD and ShieldBubble. Scope transitions
  must not run a second visibility loop that overrides these rules.
- Restore original renderer state on disable/exit; include inactive model
  children if required. Do not cache a stale list of dynamically created guns.
- Check that an eye inside the shield does not produce an opaque blue screen.
  Its active/expired state and HP protection must still be readable.

### FP1.3: adapt body facing

- Keep the horizontal movement basis tied to the camera yaw while strafing,
  backing up, sprinting and jumping, including when unarmed.
- In first person, face the body toward view yaw instead of the direction of
  strafing. Coordinate with `PlayerAimIK.UpdateFacing`; only one mechanism
  should own the final first-person facing decision.
- Avoid feedback between parent rotation and camera-target rotation; retain
  the stable yaw basis and render-frame camera timing that S2 already added.
- Adapt `AimingSetup` to respect the selected perspective and preserve the
  first-person eye target/lens/configuration when rebuilding the prefab.

Short acceptance: in Main, look through the 75-degree upward limit, strafe,
back up, jump and walk close to a shelter corner. Camera stays at eye height
without body/head obstruction, orbiting, visible jitter or wall peeking.
The gun may still require FP2 presentation at this boundary.

Intended scope: `PlayerCameraRig.cs`, `PlayerController.cs`, the specific
`PlayerAimIK.cs` facing adaptation if needed, `AimingSetup.cs`,
`P_Player_Gameplay.prefab` and this plan's execution notes.

```bash
git add Assets/_Project/Scripts/Player/PlayerCameraRig.cs Assets/_Project/Scripts/Player/PlayerController.cs Assets/_Project/Scripts/Player/Editor/AimingSetup.cs Assets/_Project/Prefabs/Player/P_Player_Gameplay.prefab Docs/WorldBuilder/FirstPersonImplementationPlan.md
# Add PlayerAimIK.cs too only if FP1 actually changed it.
git diff --cached --stat
git commit -m "feat(player): add first-person camera and view-relative movement"
git push origin feature/first-person-player
```

## FP2 — Present the existing guns in first person

### FP2.1: separate the mount from the hand bone

- Add an explicit first-person weapon anchor driven by the view. Use the
  existing cached gun instance as the gameplay weapon; no duplicate active
  WeaponBase, ammo state, firing script or projectile shooter.
- Let `PlayerWeaponHolder` choose the first-person anchor or its existing hand
  socket according to the rig's perspective. Do not remove the Humanoid model
  just because it is hidden.
- Start with a simple lower-right gun pose. Use small per-weapon presentation
  offsets where pistol, rifle, sniper and RPG dimensions require them.
  Do not resize shared art or alter weapon damage/range to fit the camera.
- Keep the muzzle reference valid after mounting. Set update timing so view,
  weapon pose and shooting do not visibly lag or disagree by a frame.

### FP2.2: integrate with IK and scope

- Stop third-person torso/support-hand IK from moving or fighting a
  camera-mounted weapon in first person. Preserve the Animator's event and
  gameplay hooks and its existing third-person path for S2.
- Keep the gun visible in hip view and ordinary aim. Blend an appropriate aim
  pose; sniper scope uses the existing overlay and hides only the relevant
  gun presentation until scope exit. No arms asset is required yet.
- Support newly purchased weapons, switching away/back, reload cancellation,
  persistent per-gun ammo, disable/respawn and scope exit without stale visuals.
- A single-camera gun can intersect nearby geometry in this foundation. Reduce
  obvious problems with sensible offsets; hand off advanced viewmodel clipping
  polish. Shooting through walls is a blocker and is covered in FP3.

Short acceptance: pistol visible; purchased weapons each equip correctly;
scope opens/closes; reload and switching retain their existing behaviour.
Only the selected weapon shows, and firing happens once per allowed shot.

Intended scope: `PlayerWeaponHolder.cs`, `PlayerAimIK.cs`, presentation-specific
parts of `PlayerCameraRig.cs`, player prefab, minimal new presentation
script/config if useful plus its meta. Name exact new files at implementation.

```bash
git add Assets/_Project/Scripts/Weapons/PlayerWeaponHolder.cs Assets/_Project/Scripts/Player/PlayerAimIK.cs Assets/_Project/Scripts/Player/PlayerCameraRig.cs Assets/_Project/Prefabs/Player/P_Player_Gameplay.prefab
# Add only the new first-person presentation files/metas if created.
git diff --cached --stat
git commit -m "feat(weapons): mount equipped guns in the first-person view"
git push origin feature/first-person-player
```

## FP3 — Align shots, interactions and player lifecycle

### FP3.1: aim from the visible view and preserve solid cover

- Review `PlayerShooting.AimRay`: use the visible camera ray in first person;
  retain third-person compensation only for its original view. In particular,
  a downward ray must not skip nearby floor/cover because of the chest pivot.
- Keep camera targeting, spread, wrong-weapon damage, hit markers and coins.
- Retain physical obstruction checks from a sensible actual muzzle. Check a
  gun extending past a thin wall or starting inside a solid: camera-mounting
  must not allow hits/projectiles through it. Add a short eye-to-muzzle
  obstruction check or muzzle retraction only if the existing check misses it.
- RPG launches must use a physically valid muzzle/clearance and retain their
  current shooter ownership and self-collision behaviour. Do not solve clipping
  by globally ignoring walls or birds in the hit mask.

### FP3.2: keep interactions and UI usable

- Check E door interaction at eye height, pickup collection, stored items,
  shop purchase/equip/Next Wave, pause/resume, crosshair and sniper overlay.
- Preserve reach limits and wall occlusion in `PlayerInteraction`. Change its
  ray/range calculation only if a demonstrated first-person problem requires it.
- Check pause, shop, death and Victory input gating: opening a UI must release
  the pointer and prevent gameplay input as designed. Fix any proven regression
  through existing game-state/input handling rather than a new parallel system.
- Observe one shield activation/hit/expiry; camera/body visibility must not
  interfere with full damage protection.

### FP3.3: reset the view on respawn

- Synchronize rig yaw/pitch with the respawn/checkpoint orientation and clear
  residual view kick/shake when appropriate. Preserve CharacterController
  teleport handling and Cinemachine warp notification.
- Verify only one gameplay camera/audio listener remains active after restart;
  leave the temporary greybox camera disabled.
- Bird systems must continue to see the actual player camera through the
  existing hub. Report species-specific problems to their owner; no AI changes
  are planned in this conversion.

Short acceptance: one bird/dummy hit confirms the crosshair; one blocked shot
confirms cover; one RPG near-cover check; one door/pickup/shop/pause sequence;
one checkpoint respawn confirms view orientation. Use available owned weapons
and existing tools without committing cheats or test-spawned objects.

Intended scope: demonstrated changes in `PlayerShooting.cs`,
`PlayerRespawn.cs`, `PlayerCameraRig.cs` and, only if required,
`PlayerInteraction.cs` or existing state/input code.

```bash
git add Assets/_Project/Scripts/Weapons/PlayerShooting.cs Assets/_Project/Scripts/Player/PlayerRespawn.cs Assets/_Project/Scripts/Player/PlayerCameraRig.cs
# Add other files only for actual demonstrated fixes listed by the AI.
git diff --cached --stat
git commit -m "fix(player): align first-person shooting and respawn behaviour"
git push origin feature/first-person-player
```

If these checks require no code fix, record evidence with a docs commit instead
of manufacturing changes for this message.

## FP4 — Record the result and hand over to S2

- Create `Docs/SystemsEngineer/FirstPersonHandoff.md` documenting final files,
  camera/mount configuration, compatibility with setup tools, input/API
  contracts, test outcomes and remaining work. Record the tested commit and
  distinguish source inspection, compilation, Editor play and standalone checks.
- Include a copy/paste prompt for S2's AI: inspect this handoff and current
  branch; finish weapon poses/arms if desired, recoil/animation polish and
  edge cases; preserve gameplay contracts; do not redo the camera foundation.
- List remaining visual clipping, scope, weapon or animation limitations by
  severity. Aiming through walls, broken interactions and failed respawn are
  blockers; absent bespoke hands is an accepted foundation limitation.
- Capture useful first-person views in the runway and one shelter if editor
  access permits. Screenshots do not replace actual shooting/play observation.
- Let S2 continue on this same branch after the owner's commits are pushed and
  S2 has pulled it, or merge the reviewed foundation PR into develop and have
  S2 branch from the updated develop. Agree on one route; avoid parallel edits
  to the same camera/prefab files. Prefer foundation PR first if it is playable.

```bash
git add Docs/SystemsEngineer/FirstPersonHandoff.md Docs/WorldBuilder/FirstPersonImplementationPlan.md
git diff --cached --stat
git commit -m "docs(player): hand off first-person foundation to S2"
git push origin feature/first-person-player
```

PR title: `Add first-person player foundation`  
PR base: `develop`; compare: `feature/first-person-player`.

The human reviews and merges. Afterward, with completed work committed and
Main saved outside Play mode:

```bash
git switch develop
git pull --ff-only origin develop
```

If S2 continues after the foundation merges, suggested branch for S2:
`feature/first-person-polish`. S2 creates and pushes it from updated develop.

## Verification and deadline discipline

- Four meaningful commits are the intended structure. FP1 has several small
  implementation steps but one coherent camera/movement boundary; FP2 owns
  weapon presentation; FP3 owns gameplay/lifecycle corrections; FP4 owns handoff.
- No full test suite or full campaign after each step. Compile changed scripts,
  check a focused Main scenario and record actual results. Run one normal
  standalone campaign later in D3 once S2 and S3's work is integrated.
- If Unity control fails, provide exact short manual steps and wait for actual
  observations. Do not mark an unobserved game check passed.
- Before a single final play/build check, verify all four gun presentations,
  scope, eye-height doors/shelters and respawn. Record performance concerns if
  observed; D1's optimization work remains deferred.
- Each implementation response must report changed files, what was verified,
  any blocker, exact staging commands and that task's commit message.

## Prompt to begin implementation

```text
Project: /Users/tathira/gv_game/runway-panic
Plan: Docs/WorldBuilder/FirstPersonImplementationPlan.md
Implement FP1 only, including its camera, selective visibility and movement
subtasks. First check branch/status and applicable AGENTS.md. Keep the current
PlayerCameraRig/Cinemachine system and its public API, 75-degree upward look,
four waves and existing gameplay values. Respect S2's components and S3's bird
work. Do not stage, commit, push, switch branches or merge. Preserve unrelated
edits/untracked documents. Use focused compile/Main checks, not full suites;
report unavailable observations honestly. Stop at the FP1 commit boundary with
exact Git commands for the human. Do not implement FP2-FP4 yet.
```
