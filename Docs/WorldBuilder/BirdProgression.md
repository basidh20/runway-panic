# Confirmed species and weapon progression

Branch: `fix/gull-species-progression`, based on develop `77b5b31`. Merge target: **develop**. This is one atomic shared-data fix: keep the enum, renamed asset, weapon mapping and shop copy together in the same commit.

## What was fixed

The team introduces **Gull → Pigeon → Crow → Hawk**, but the shared data still described Pigeon → Crow → Hawk → Eagle. This correction implements the agreed mapping without changing the stable stored species IDs:

- Gull remains **3** (formerly Eagle); Pigeon **0**, Crow **1**, Hawk **2**.
- `BirdSpecies_Eagle.asset` was moved using Unity's AssetDatabase to `BirdSpecies_Gull.asset`, with its original `.meta` GUID **f4a2ede88e10cac40a20b9a1ef4d7d94** retained. This is a rename, not a replacement asset. Existing prefab and scene references stay valid.
- The formerly empty species-to-prefab field now references `P_Bird_Gull`. All four species/prefab references resolve in both directions.
- Both each bird's required weapon rarity and each gun's target species are updated. Damage effectiveness is actually decided by `BirdSpeciesData.GetDamageTaken`, not the weapon's descriptive target field alone.
- AR, sniper and RPG shop descriptions reflect the new targets. The S2 shooting-range object's legacy `Dummy_Eagle` label is now `Dummy_Gull`; its object identity/data reference are unchanged.
- The S3 bird-component helper now looks for the confirmed Gull asset, not an obsolete Eagle fallback.

No scenes were rebuilt, brains rewritten, weapon damage/prices changed, or production waves added. The only scene edit is that shooting-range label; Main is unchanged. Gull/Pigeon brains still require their owners' implementation.

## Starting tuning (not final playtested balance)

| Wave introduction | Species | HP | Intended weapon / damage | Full-health hits | Base attack damage | Kill coins |
| --- | --- | ---: | --- | ---: | ---: | ---: |
| 1 | Gull | 30 | Pistol / 10 | 3 | 6 | 8 |
| 2 | Pigeon | 20 | Assault Rifle / 12 | 2 | 5 | 10 |
| 3 | Crow | 60 | Sniper / 60 | 1 | 8 | 15 |
| 4 | Hawk | 300 | RPG / 150 | 2 | 20 | 25 |

Pigeons remain individually weakest, balanced by flock numbers. Gull is a manageable three-pistol-hit opening enemy instead of inheriting a 300-HP final enemy. Crows reward a precise sniper hit. Hawks take two RPG hits and become the toughest species. Base attack damage is not necessarily the final damage per special attack: existing hawk stoops multiply it by 1.5 (30 damage), for example. Brain-specific multipliers/cadences were not changed.

Movement speeds stay at Gull 6, Pigeon 4, Crow 5.5 and Hawk 8 m/s. Attack ranges are 1.5 / 1.5 / 1.5 / 2 m; base cooldowns 2 / 1.5 / 1.2 / 2 seconds. Gull's old Eagle range was reduced to 1.5 m; existing Pigeon/Crow/Hawk ranges and cooldowns are retained. Every non-matching weapon rarity still deals **10%** damage, not zero. A more powerful wrong gun can still kill a weaker bird; these are effectiveness rules, not absolute immunity or enforced gun ownership.

Weapon costs remain AR **40**, sniper **75**, RPG **100**, medkit **15**, shield **25**. As an initial economy guide, five Gull kills earn the AR price, eight Pigeon kills earn 80 coins, and seven Crow kills earn 105 coins. These are kill-budget examples, **not implemented wave counts**. Buying consumables reduces upgrade funds. When implementing WaveData/WaveManager, verify available kills, carried coins, missed enemies and consumable spending so players can afford the intended next gun; timed-wave completion currently guarantees no bonus. Final economy and encounter difficulty require actual wave/AI playthroughs.

## Focused verification and tools

Unity **6000.6.4f1** compiled the corrected code. One **Tools > Runway Panic > Apply Confirmed Bird Progression** run performed the GUID-preserving move and saved the asset values, then ran its focused validation: **41 checks passed, zero failures**, recorded in `BirdProgressionValidation.txt`.

The check covers GUID preservation, stable numeric IDs, all four prefab back-references, intended tuning, matching species/weapon rarities, the full 4-by-4 damage matrix, direct-hit counts and shop text. It does not instantiate enemies or run a full game. No combat/AI/wave test suite was run.

**Validate Confirmed Bird Progression** can recheck this baseline. **Apply** is a deliberate one-time migration/baseline reset, not a normal build step: don't run it casually after S2 tunes the values, because it restores the table above. Wave code must select species explicitly or through WaveData; never assume numeric enum order equals wave order.

## Message to teammates after merge

"Eagle/Gull data correction is merged into develop. Pull before editing shared bird/weapon settings. Species IDs stayed unchanged: Gull=3, Pigeon=0, Crow=1, Hawk=2; wave order is explicitly Gull→Pigeon→Crow→Hawk. The Gull asset was renamed with its GUID preserved. Pistol→Gull, AR→Pigeon, Sniper→Crow, RPG→Hawk; baseline HP 30/20/60/300 and coin rewards 8/10/15/25. Shop copy is corrected. S2, please review future balance on top of these values rather than recreating the old Eagle rename. Species owners should verify their brains with the updated data; production waves and final balance remain pending."
