# Main pickups — integration batch, milestone 3 of 4

Unity: **6000.6.4f1**. Branch: **feature/world-level-integration**. Merge target: **develop** after the shop milestone, not main.

## What changed

Main now contains one connected S2 `P_PickupSpawner` prefab at the scene root. Its `spawnPoints` list explicitly references the original `GameplayMarkers/PickupPoints/Pickup_00` through `Pickup_03` transforms in that order. Their positions are unchanged. Explicit references avoid depending on a scene-wide name lookup, while leaving the shared prefab's fallback behaviour untouched.

All supplied gameplay settings remain unchanged:

- **Two** active pickups across four possible markers, with no occupied marker reused simultaneously.
- Medkit weight **2**, shield weight **1**. This is a probability, not a guaranteed alternating pattern.
- **25 seconds** after collection, the spawner chooses a random free marker for a replacement. It may choose the marker just vacated; a different location is not guaranteed by the shared code.
- `GameManager.WaveStarted` refills vacant slots immediately. Pending delayed replacements respect the two-active limit.
- Pickups are placed on the solid floor beneath each marker, with non-blocking triggers and their existing animated visuals.

Walk over a pickup to collect it; **E is not needed**. A medkit heals 30 HP if injured, or is stored for Q. A shield activates the existing ten-second protection or is stored for F when appropriate. Full capacity can leave a pickup on the ground. These behaviours belong to S2's existing item/player systems; no shared runtime scripts or prefabs were edited.

This does not install the shop, add new inventory UI, generate waves/birds or implement Gull AI. Ordinary Play now has pickups even before a wave manager exists; the spawner fills them on scene start.

## One focused check

With Main saved outside Play mode, use **Tools > Runway Panic**:

- **Integrate Main Pickups** creates only a missing connected spawner, binds the four existing markers and saves. Unexpected/duplicate spawners are refused. Re-running does not create another spawner.
- **Run Main Pickup Smoke Check** enters Play, checks floor/clearance at all four markers and two distinct ground-level spawn positions. The actual CharacterController approaches a random spawned pickup and collects it via its trigger, checking its inventory/shield effect. It parks away from the pickups, waits the real 25 seconds for replacement, then collects another and calls the real `GameManager.BeginWave(1)` hook to check immediate refill. It writes `PickupValidation.txt` and exits Play without saving temporary player movement, inventory changes or the test wave.

The check uses public runtime behaviour, not direct calls to private collection handlers or shortened respawn settings. Do not provide input while it runs (roughly 30 seconds). It samples the random items actually spawned; it is not an exhaustive test of every item type, damage reduction, full-inventory refusal, Q/F input, UI or a production wave manager. Other feature test suites are not run.

Recorded result: **17 checks passed, zero failures**. Both randomly sampled collections were medkits; the real replacement arrived after 25.0 seconds, and the wave event refilled the next vacancy immediately. Shields remain in the supplied weighted spawn table, but shield collection was not sampled in this run.

## Manual handoff

The human commits and pushes this milestone separately. Suggested message:

```text
feat(level): connect airport pickup spawning to level markers
```

Next: shop integration on this same branch. Agent Controller should continue using the existing `GameManager.BeginWave` hook for production waves; it already drives pickup refills. Crow integration can reference the spawned shared `Pickup` components, but crow theft behaviour is not tested here.
