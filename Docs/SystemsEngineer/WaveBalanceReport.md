# Wave balance and checkpoint playtest

Written by **Tools > S2 > Run Wave and Checkpoint Playtest** on 2026-10-18 10:56 (Unity 6000.6.4f1). Scene: `Main` (not saved). Result: **11 passed, 0 failed**.

## Checks

| Result | Check |
| --- | --- |
| PASS | Main has checkpoints (2) |
| PASS | walking into Checkpoint_B makes it the respawn point |
| PASS | the player respawns after dying |
| PASS | a death costs one life (3 → 2) |
| PASS | the player respawns at the checkpoint (0.1 m away) |
| PASS | health is full after respawning |
| PASS | losing the last life ends the game (lives 0) |
| PASS | the Game Over screen is shown |
| PASS | the lost sound plays with the Game Over screen |
| PASS | Restart starts a fresh game with 3 lives |
| PASS | clearing all four waves wins the game |

## Four waves, played by a perfect-aim player

Real gun stats (damage, fire rate, magazine, reload), 0.6s to aim at each new bird, the right gun when owned. Between waves it buys the gun for the next wave's main species, then a medkit if coins are left. This is the best case: a real player misses and moves, so expect more time used and fewer kills.

| Wave | Birds | Killed | Time used / limit | Result | Coins before → after | Bought in the shop |
| --- | ---: | ---: | --- | --- | --- | --- |
| 1 · Gulls | 9 | 9 | 31s / 90s | cleared (+10) | 0 → 82 | Assault Rifle, Medkit |
| 2 · Pigeons | 13 | 13 | 33s / 100s | cleared (+15) | 27 → 166 | Sniper, Medkit |
| 3 · Crows | 13 | 13 | 45s / 110s | cleared (+20) | 76 → 266 | RPG, Medkit |
| 4 · Hawks | 9 | 9 | 44s / 120s | cleared (+0) | 151 → 295 | — |

Damage the player took over the four waves: **0 HP** (healed to full 0 times when below 40%).

## Economy plan (from the wave assets)

| Wave | Birds | Every kill + clear bonus | ~60% of kills, no bonus | Next wave's gun |
| --- | --- | ---: | ---: | --- |
| 1 · Gulls | 9 Gull | 82 | 43 | Assault Rifle (40) |
| 2 · Pigeons | 10 Pigeon + 3 Gull | 139 | 74 | Sniper (75) |
| 3 · Crows | 8 Crow + 5 Pigeon | 190 | 102 | RPG (100) |
| 4 · Hawks | 3 Hawk + 3 Crow + 3 Gull | 144 | 86 | — (final wave) |

Coins carry over, so a weaker wave is made up by the next. Gulls and pigeons have no species brain yet (their owners are building them), so in this run they do not attack; crows and hawks do.
