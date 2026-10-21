# Release build preparation and smoke check

Prepared 21 October 2026. Unity 6000.6.4f1. Integration target: develop.
The owner confirmed that playable builds are needed for both Windows and macOS.

## Prepared source configuration

- Enabled global build scenes: MainMenu (index 0), Main (index 1).
- Sandbox_Basidh is disabled in the build list, not deleted from the project.
- Builds/ is ignored by Git. Keep generated players out of Assets and source commits.
- The final ground NavMesh is refreshed. The owner approved the remaining truck
  and abandoned-aircraft footprint views after the agent's focused route checks.
- Keep the current saved lighting. The owner reported that S2's merged fix
  resolved the dark-frontage problem. No additional lighting bake is claimed.
- The inspected Unity installation has MacStandaloneSupport in its
  PlaybackEngines directory, but no Windows standalone support directory.
  Confirm Windows availability in Build Profiles; install the matching support
  through Unity Hub if offered, or have a teammate with Windows build support
  produce and test that player using the same integrated source revision.

## Remaining build steps (not yet executed)

1. In Unity, open File > Build Profiles and verify the enabled scene list is
   MainMenu followed by Main. If a profile overrides the global scene list,
   make that profile use the same two scenes.
2. Build a Windows x86_64 player into Builds/Windows/RunwayPanic.exe. Keep its
   accompanying data folder and DLLs together when distributing it.
3. Build a macOS player into Builds/macOS/RunwayPanic.app. Choose the architecture
   supported by the intended test Macs and the installed toolchain.
4. Launch each player outside Unity on its target operating system. A Mac
   Editor run or successful compilation is not a Windows runtime check.
5. In one normal full run per platform, check menu > four waves/countdowns >
   shop progression > victory. Include shooting/reloading/switching, visible
   hands, pause buttons not firing, Gull drops, shelter access, shield and
   medkit. Separately check death/checkpoint/restart as necessary.
6. Record the tested commit, OS/architecture, build settings, actual outcome
   and any issues below. Stop new feature work while testing the release.

## Actual standalone results

Pending. Neither platform's standalone build or four-wave runtime test has
been verified in this preparation task. Record failures and teammate owners
honestly; do not replace manual results with Editor observations.
