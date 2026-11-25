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

## Build workflow

For macOS, the editor command `Tools > Runway Panic > Build macOS Release
Player` builds the explicit two-scene list without launching the game. It
requires saved scenes, uses a non-development build, and refuses to overwrite
an existing `Builds/macOS/RunwayPanic.app`. Its local BuildSummary.txt records
the actual build result; neither the player nor that generated summary belongs
in the source commit. Move a previous player aside manually before rebuilding.

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

The macOS release build completed on 21 October 2026 through the editor
command, using source base 62fc5e2 plus the ReleasePlayerBuild editor helper
and Unity's build-time asset preparation. The local BuildSummary.txt reports:

- Unity 6000.6.4f1; StandaloneOSX; non-development player.
- Result: Succeeded; 0 errors; 12 warnings.
- Build duration: 21 minutes 53.95 seconds, including first-build shader work.
- Reported size: 134,836,652 bytes.
- Scenes: MainMenu, Main.
- Output: Builds/macOS/RunwayPanic.app.
- Executable inspected with `file`: Mach-O 64-bit arm64. This player targets
  Apple Silicon Macs, not Intel Macs. A universal/Intel build is needed if
  those machines must also be supported; none is claimed here.

Unity's transient PC shader-prefilter settings, URP runtime-settings list,
GraphicsSettings serialization additions and Input System preloaded-assets
rewrite were restored after inspection. The default-volume Bloom `filter`
override field added by URP was retained: the installed URP build processor
explicitly requires complete default overrides. The performance-package
Resources metadata was automatically removed by build completion. The
generated player and local summary remain ignored, outside the source commit.

The owner reported build success and the agent independently verified the
summary and executable. This is build evidence, not gameplay-test evidence.

Both platforms' standalone four-wave runtime tests remain pending. The Windows
build has not been attempted. Record failures and teammate owners honestly;
do not replace manual results with Editor observations.

## Enhancement pass re-test (fix/enhancements, 19 November 2026)

The enhancement branch changed gameplay code after the 21 October build, so the
macOS build above no longer matches the source. Rebuild both players from the
merged revision and tick each line in one normal run (Editor first, then each
standalone player). Write the actual result next to each line.

- [ ] Shop open: WASD, jump, Q, F, E and 1-4 do nothing; mouse clicks only buy.
- [ ] Pause: Q, F, E, 1-4 and the mouse wheel do nothing; gunfire, birds and
      rockets go silent; music ducks; menu clicks are heard.
- [ ] Idle 60 s in the shop: no new pickups appear in the level.
- [ ] Fire the last round, switch guns during the reload, switch back: the gun
      reloads by itself (or on the next trigger pull).
- [ ] Shield up, let a bird hit: no red flash, no hurt sound, HP unchanged.
- [ ] Die with lives left: screen fades dark red, "YOU ARE DOWN" banner, cannot
      shoot; after respawn "PROTECTED" shows ~3 s and birds cannot hurt.
- [ ] 1-4 pick pistol / rifle / sniper / RPG by tier; mouse wheel cycles.
- [ ] Walk the gun into a wall and a shelter door: it tucks back, no clipping.
- [ ] Let a wave time out: survivors fly off (no pop); a crow carrying a pickup
      drops it first; the level keeps its pickup count next wave.
- [ ] Standing inside a closed shelter: birds outside cannot peck through walls.
- [ ] Shoot a bird behind a falling dead bird: the shot reaches the live one.
- [ ] Pigeons roost and crows land at their exit without circling.
- [ ] Waves: 3 = crows + pigeons + gulls, 4 = all four species; surviving the
      wave 4 timer shows "You held the runway", clearing it "All 4 waves cleared".
- [ ] Shop with a gamepad: a button is selected and usable.

Windows build: still not attempted. macOS rebuild: pending.
