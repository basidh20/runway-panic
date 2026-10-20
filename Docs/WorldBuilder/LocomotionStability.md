# Locomotion stability verification

Verified in Unity 6000.6.4f1, Main scene, on 2026-10-19.

The generated run clip previously had a RootT.y range of 0.2326084 humanoid units, as well as animated root yaw and lateral movement. The old gait lowered the entire body to whichever foot was lowest. The corrected generator solves each leg along a continuous foot path and authors a small body bob independently. Humanoid root translation in X/Z and root orientation remain constant during locomotion. Walk/run root height is controlled explicitly. Cinemachine now uses LateUpdate alongside the CharacterController's render-frame movement.

The saved run clip's RootT.y range is 0.0291445 (87.5% smaller); all other root component ranges are zero. Walk/run clips and AC_Player.controller were regenerated through the live Editor, preserving their asset identities.

## Rendered-frame Play-mode check

`AgentScripts/LocomotionPlaybackCheck.cs` is an opt-in live probe, run through Unity Pipeline `run_script` after entering Play mode. It pairs temporary input devices, tests walk/sprint/stop/turn, observes actual camera render callbacks, and restores the previous devices on completion. No probe objects or devices are saved into Main.

- Sprint frames sampled: 94.
- Player controller height variation: 0.00000 m.
- Hips height variation: 0.02369 m.
- Sprint speed: 7.500–7.500 m/s.
- Maximum camera rotation step while sprinting straight: 0.0000 degrees.
- Speed after braking: 0.0000 m/s.
- Camera update method: LateUpdate.
- Stability check: PASS.

The probe writes its report and two running screenshots under `Temp/LocomotionCheck/`.

## Gameplay regression checks

The existing MainPlayerSmokeTest passed startup, walking (4.20 m), jumping, landing, sprinting (6.30 m), shooting/ammo, damage raycasts, pause/resume, and both hangar passage checks. Shelter_A and Shelter_B passage checks failed with 4.23 m traversal error. These checks directly move the CharacterController with PlayerController disabled; shelter collision was not changed by the locomotion work. See MainPlayerPlaytest.txt for the full results.

The checks establish stability on the tested runway route. They do not establish behavior at every obstacle, slope, or camera angle.

## Walking startup response

The initial acceleration and Animator damping previously stacked, making the first step feel delayed. Starting from idle now uses a separate 0.06 s acceleration window; increasing animation speed uses 0.025 s damping. Ordinary acceleration, braking, and air control retain their settings. Animation still follows actual movement, so blocked movement does not falsely play a full walking cycle.

Verified in a fresh Play session with the same opt-in probe:

- First movement after input: 0.0222 s.
- 90% walking speed: 0.0777 s.
- 90% walking animation speed: 0.1338 s.
- Responsive startup check: PASS.
- Steady sprint: 71 rendered frames, 7.500 m/s, zero controller height variation and zero camera rotation steps.
- Braking finished at 0.0000 m/s.
