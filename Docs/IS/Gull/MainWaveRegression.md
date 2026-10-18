# Gull — normal Main Wave 1 observation

Unity 6000.6.4f1, 18 October 2026. This is a single Editor Play-mode observation on `fix/gull-main-behaviour`, with the production WaveManager and the normal player. No manual bird spawner, forced drop, simulated damage or test fixture was used. The player was left stationary and no shots were fired.

- Main displayed its six-second Wave 1 countdown and spawned Gulls automatically. F3 labels showed states including `InKettle`, `DescendToDrop` and `JoinKettle`.
- At 23.4 s of F4 active time, the panel showed 4 live Gulls, 2 drops, 2 landed FOD pieces, 2 resolved impacts that hit the stationary player, and 2 token hand-offs.
- At 64.7 s, it showed 9 drops, 8 resolved impacts, 1 pending drop, 6 ground pieces, and 8 token hand-offs. The player had 52/100 HP and 3/3 lives.
- After Wave 1 timed out, the shop opened. F4 showed 0 live Gulls, 0 ground FOD, 0 pending drops, 12 drops and 12 resolved impacts; the player had 28/100 HP and 3/3 lives. This confirms the visible ground-debris and live-bird counts cleared in this run, not every internal cleanup detail.

The displayed 12/12 hits are one stationary, unarmed observation, not a representative accuracy or difficulty measurement. The earlier 13-check controlled Main fixture used an explicitly simulated shot and is separate evidence, not a real weapon test. The owner also reported earlier that Gull drops work during normal gameplay.

## Owner's real-shot observation

In a separate player check, the owner reported that F3 labels were visible, one hit left a Gull alive, and the hit Gull entered `DistressDive` before returning to `JoinKettle` and `InKettle`. This is the owner's observation, not the simulated shot from the earlier fixture. No observation was provided for nearby Gulls' response, so their group reaction to a real shot remains unverified.

This is not a four-wave, other-species, cover or performance certification. The historical eight-Gull Editor sample in `PerformanceSample.md` includes a 1.1237 ms GullBrain peak; the strict every-frame 0.5 ms goal was not demonstrated. No new profiling measurement was made for this task.
