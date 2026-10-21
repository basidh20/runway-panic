# First-person weapon presentation polish

20 October 2026. Continues the foundation described in
`Docs/SystemsEngineer/FirstPersonHandoff.md` on `feature/first-person-polish`.

## Implementation

`PlayerWeaponHolder` attaches `FirstPersonWeaponMotion` to its existing runtime
camera socket. No scene or prefab edits are needed. Its new
`animateFirstPersonWeapon` setting can disable this presentation.

The supplied PSX pack is now the visible arm rig. `FirstPersonArms` loads
`Resources/FirstPerson/PSXArms.prefab`, uses the supplied grab poses for gripping,
layers the supplied finger-fire and handling animation over the fingers, and
solves both arms against the existing weapon. Reload hand travel and cosmetic
magazine/rocket handling follow the actual `WeaponBase` reload duration.
The earlier extracted officer-arm meshes and builder have been removed.

## Supplied asset locations

| Content | Location |
| --- | --- |
| Runtime FBX | `Assets/_Project/Art/Models/Characters/FirstPerson/PSXArms/arms_rig.fbx` |
| Runtime textures | `Assets/_Project/Art/Textures/Characters/FirstPerson/PSXArms/` |
| URP materials | `Assets/_Project/Art/Materials/Characters/FirstPerson/PSXArms/` |
| 18 extracted source animation clips and finger masks | `Assets/_Project/Art/Animations/Player/FirstPerson/PSXArms/` |
| Ready-to-use runtime prefab | `Assets/_Project/Resources/FirstPerson/PSXArms.prefab` |
| Original download, Blender/GLB files, UV guide and older versions | `SourceArt/ThirdParty/PSXFirstPersonArms/` |

The loose `psx-first-person-arms-free-game-assets` folder was moved into the
SourceArt location. Runtime copies of the FBX and textures preserve the original
source files. The builder is **Tools > S2 > Build PSX First Person Arms**.

The socket now has:

- Bounded look inertia, gentle breathing and distance-driven walking motion.
- A smoothly lowered sprint pose and a damped landing response.
- Separate pistol, long-gun and launcher hip positions, with centred aimed poses.
- A short equip raise, event-driven firing recoil and a reload inspection tilt
  that follows the existing weapon's actual reload duration.
- Scoped renderer suppression, restored when leaving scope or switching weapons.

Only the existing weapon instance moves. Camera aim, movement, weapon stats,
ammo, fire timing, shopping and bird behaviour retain their existing owners.
Firing callbacks enqueue recoil; socket transforms change in LateUpdate, after
the shot has resolved. No camera bob or second camera/input path was added.
Motion resets after a teleport and releases event subscriptions when disabled.

## Verified in the Unity Editor

The final PSX-rig live probe advanced through 1,347 gameplay frames with zero failed
checks. It covered each of the four weapon tiers:

- Existing camera parent, visible hip presentation, exact camera-centre aim ray.
- The supplied `hand.R` skeleton and animation player were active. Both hands
  rendered, the firing hand reached its grip and the support hand left the gun
  during reload. Arms disappeared in the scope with the weapon.
- One round consumed per successful shot; recoil applied after shot resolution.
- Visible reload motion and the correct magazine restored at completion.
- Aim blend completed; the sniper model was hidden behind its scope overlay and
  restored after releasing aim. Hip, aim and reload captures were inspected.
- Returning to the pistol reused its original WeaponBase. There were exactly
  four cached weapons and switching preserved magazine ammo.
- Walking retained 4.5 m/s and sprinting retained 7.5 m/s. Weapon gait moved
  without rotating the camera. Sprint lowering, jump and landing worked.
- Death/respawn restored health, movement and a level view with motion active.
- Four waves and the -75-degree upward pitch limit remained configured.

Unity reported no C# compilation errors. The console history includes earlier
connection/test-setup errors; these are not gameplay failures from the final run.

Reproduce in Play mode with the opt-in script outside Assets:

```powershell
unity command run_script --caller plugin --skill unity-cli --format json --file AgentScripts/FirstPersonPolishCheck.cs --entry FirstPersonPolishCheck.Start
```

The probe uses temporary input settings/devices and temporarily suspends wave
updates so the shop cannot interrupt the animation checks. It restores them at
completion. It consumes a life to test respawn; restart Play mode after running.
Screenshots and the detailed report are in `Temp/FirstPersonPolishCheck`.

## Boundaries and remaining art work

### 21 October: non-scoped aim correction

The pistol, assault rifle and launcher now use separate aimed positions. The
pistol support shoulder stays back instead of folding its arm up beside the
sights. The rifle grips sit closer to the receiver, and the launcher stays at
the shoulder rather than putting its tube over the camera centre. The sniper's
scope pose, FOV and overlay behavior are unchanged.

The extended live probe passed over 1,847 gameplay frames with zero failures.
It now checks both hands reach their grips while aimed and casts the centre
view ray against the visible gun/hand meshes to verify the reticle is clear.
All three non-scoped weapons passed these checks, along with the existing
fire/reload/switch/scope/movement/respawn checks. Aimed-view captures are saved
under `Docs/WorldBuilder/Evidence/FirstPersonPSX/Aim-*.png`.

SHA-256 comparison confirmed these files are unchanged:
`Assets/_Project/Scenes/MainMenu.unity`, `Assets/_Project/Scenes/Main.unity`,
`Assets/_Project/Scripts/Core/GameManager.cs`, and
`ProjectSettings/EditorBuildSettings.asset`.
The pre-existing lightmap metadata and medkit material changes were left untouched.

The supplied arms are PSX-style assets. The pack contains gestures and finger-gun
animations, with no authored firearm reload clip. Reloads therefore combine its
rig/finger poses with procedural hand paths, cosmetic magazines and the existing
rocket mesh. Individual gun slides, bolts and magazines are not separately
rigged by this pass. Standalone builds, a complete campaign and all close-wall/
door cases were not verified in this pass.
