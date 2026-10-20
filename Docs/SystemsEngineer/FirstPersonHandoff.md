# First-person player foundation handoff

Prepared 20 October 2026 on `feature/first-person-player`.

## Delivered foundation

The game now uses the existing Cinemachine-based player camera as a first-person
view. This is one camera/input path, not a separate controller. The final
foundation commits are:

- `f4216c9` — eye-height view, hidden player body and view-relative movement.
- `765d798` — camera-mounted first-person weapon presentation.
- `44af4e1` — first-person shooting ray and respawn view reset.

The following contracts must remain unchanged:

- Four waves stay enabled.
- The upward look limit remains 75 degrees (`pitchLimits.x = -75`).
- `PlayerCameraRig` continues to own look input, FOV, aim sensitivity, recoil
  and shake.
- `PlayerShooting` continues to own weapon fire, damage, ammo, spread and
  crosshair hit detection.
- `PlayerWeaponHolder` keeps one cached instance per weapon, preserving ammo
  during weapon switching. Do not create a duplicate active `WeaponBase` for
  a viewmodel.
- Existing shop progression, weapon data, bird behaviours, shield immunity and
  player movement values are not part of this handoff.

## Key files and setup

| File | Responsibility |
| --- | --- |
| `Assets/_Project/Scripts/Player/PlayerCameraRig.cs` | First-person flag, 75 FOV, first-person camera placement, body visibility, `ResetViewTo`. |
| `Assets/_Project/Scripts/Player/PlayerController.cs` | Faces the hidden body toward camera yaw while retaining strafing/backwards movement. |
| `Assets/_Project/Scripts/Weapons/PlayerWeaponHolder.cs` | Uses `GameplayCamera` as the first-person weapon socket parent. |
| `Assets/_Project/Scripts/Player/PlayerAimIK.cs` | Skips third-person torso/support-hand IK in first person. |
| `Assets/_Project/Scripts/Weapons/PlayerShooting.cs` | Uses the exact camera-centre ray in first person; third-person compensation remains intact. |
| `Assets/_Project/Scripts/Player/PlayerRespawn.cs` | Resets first-person yaw/pitch to the checkpoint or spawn rotation after respawn. |
| `Assets/_Project/Prefabs/Player/P_Player_Gameplay.prefab` | Enables `firstPerson`, references `PlayerVisual` and `GameplayCamera`, and stores the lower-right weapon offset. |
| `Assets/_Project/Scripts/Player/Editor/AimingSetup.cs` | Does not overwrite first-person shoulder settings when S2's setup tool is rerun. |

The current universal weapon pose is camera-local position `(0.30, -0.24,
0.45)` with rotation `(0, 0, 0)`. It was confirmed suitable for the Tier 1
pistol. Keep gameplay muzzle references; do not change WeaponData damage,
range or ammo merely to change presentation.

## Actual checks completed

- Changed scripts imported/compiled in Unity without C# errors.
- Main scene, Unity Editor Play mode: camera was at eye height, body was not
  visible, and the Tier 1 pistol was visible at the lower-right without the old
  hand-mount clipping.
- Main scene, Unity Editor Play mode: a Gull was shot successfully from the
  crosshair; ammo and damage behaviour remained correct.
- Main scene, Unity Editor Play mode: player death and respawn returned to a
  level view facing the expected spawn/checkpoint direction.

Not performed: standalone build verification, four-weapon pose comparison,
scope-specific visual polish, or a full four-wave campaign. These are not
failures; they remain final-integration checks.

## Remaining work for S2

### Accepted for this deadline foundation

- The gun is a floating existing model; there are no dedicated first-person
  arms or reload-hand animations.
- There is one shared lower-right weapon offset. Tier 2–4 weapons may require
  their own presentation offsets.
- No head bob, leaning, viewmodel collision animation or separate weapon camera
  has been added.

### Check before any release (medium priority)

1. Purchase/equip every weapon and confirm it remains visible after switching,
   reloading, dying and respawning.
2. Check scoped weapon presentation and the existing scope overlay. Preserve
   `PlayerCameraRig` aim/scope FOV and sensitivity behaviour.
3. Stand close to a wall/door with each weapon. Presentation clipping is
   cosmetic; shots through solid walls, broken interactions or failed respawn
   are blockers and must be fixed.

## Safe continuation workflow

This branch should be merged into `develop` before a separate polish branch is
created. Do not edit `PlayerCameraRig`, `PlayerWeaponHolder`, `PlayerAimIK` or
`P_Player_Gameplay.prefab` in parallel with another person. After the PR merge,
S2 should branch from updated `develop` as `feature/first-person-polish`.

## Prompt for S2's AI

```text
Project: /Users/tathira/gv_game/runway-panic
Read Docs/SystemsEngineer/FirstPersonHandoff.md and inspect the current branch
before changing anything. The first-person foundation is complete: keep the
existing PlayerCameraRig/Cinemachine/input path, four waves, 75-degree upward
look limit, PlayerShooting gameplay contracts, weapon data and shop economy.
Do not rebuild the camera or create duplicate active WeaponBase instances.

If polish is needed, work from a new feature/first-person-polish branch after
the foundation is merged into develop. First test Tier 1-4 weapon switching,
reload, respawn and scopes. Add only presentation polish such as safe
per-weapon view offsets, first-person arms, animation/recoil feel or cosmetic
near-wall handling. Preserve the exact first-person shooting ray and respawn
view reset. Do not edit S3's Pigeon work or bird balance unless separately
assigned. Make small commits and report concrete Unity observations.
```
