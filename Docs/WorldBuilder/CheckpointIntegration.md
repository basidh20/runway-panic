# Main checkpoint integration — next gameplay-integration batch, commit 1 of 4

Unity: **6000.6.4f1**. Batch branch: **feature/world-level-integration**. PR base: **develop**.

## What changed

The two existing markers now use S2's `Checkpoint` component and a non-solid BoxCollider trigger. S2's `Checkpoint`, `PlayerRespawn`, health/lives and GameManager code and the gameplay prefab are unchanged.

| Checkpoint | World position (unchanged) | Respawn yaw | Trigger |
| --- | --- | --- | --- |
| Checkpoint_A | (-13, 0.12, 12) | +90 degrees, east toward the runway | 4 x 3 x 4 m; local centre (0, 1.5, 0) |
| Checkpoint_B | (13, 0.12, -12) | -90 degrees, west toward the runway | same |

Each marker has a `CheckpointMarking` child: yellow corner strips and a ground-facing label using existing materials/font. These are visual only, with no colliders, UI dependencies or new material assets. They identify the trigger area, not the exact respawn point (the original marker centre).

Walking through a checkpoint makes it the respawn point for the current session. The last checkpoint reached wins. Death consumes a life; while lives remain, the existing player code waits its configured delay (2 seconds), returns to that checkpoint, restores full HP and snaps the camera through the existing warp notification. Exhausting all three lives still leads to Game Over. A new session begins without a checkpoint selected.

This does not add shelter doors, invulnerability, checkpoint HUD/activation effects, cross-session saves, pickups, shop, wave management or Gull AI. Normal Main still has no bird/wave source of damage; the opt-in test supplies damage temporarily.

## Reproduce the checks

Open saved Main outside Play mode. Under **Tools > Runway Panic**:

1. **Integrate Main Checkpoints** adds only missing components/markings, preserves the two marker positions, configures triggers/facing, validates and saves Main. Re-running does not duplicate checkpoints or markings. Unexpected collider types/duplicate components are refused before changes.
2. **Validate Main Checkpoints** writes `CheckpointValidation.txt`: count, shared script, trigger dimensions, floor support, respawn-body clearance, facing and non-blocking floor markings.
3. **Run Main Checkpoint Playtest** enters Play automatically, physically walks the actual CharacterController into A, supplies a death away from it, waits for the real delayed respawn, then does the same for B. No direct `SetCheckpoint` calls or private-field edits simulate activation. It checks HP/lives/movement/ground/facing and finally tests last-life Game Over. It writes `CheckpointPlaytest.txt` and exits Play without saving temporary state.

Do not provide input while the opt-in test runs. It deliberately causes three test deaths. Stopping it early is recorded as an interrupted/failed run. These Editor tools do not execute automatically in normal Play and do not enter a player build.

The generated reports record tests actually run. They do not prove bird combat or full camera occlusion is finished. The previous `PlayerIntegration.md` covers the base gameplay-player milestone.

The recorded run passed all **18 saved-scene checks** and **31 Play-mode checks**. Re-running integration also preserved exactly two checkpoints and one marking group per checkpoint.

## Four real commit-sized milestones

1. **Checkpoints** — this branch: activation, placement/facing, visible zones and real delayed-respawn verification.
2. **Shelter doors** — see `ShelterDoorIntegration.md`: both supplied prefabs fitted; focused physical opening/closing and passage checks, with manual E-input check documented.
3. **Pickup spawning** — next commit: install the supplied spawner and test collection/replacement on the level markers.
4. **Shop** — next commit: install the supplied shop UI and test purchases, refusals and next-wave handoff.

Each milestone gets its own tested human-created commit, recording an actual functional change. Keep these four commits on this batch branch and open one PR into develop when ready. Navigation/AI markers, movable cover, production waves, safe-room mechanics and environment polish follow in later milestones.

## Manual commit

Review Main, the two new Editor tools with `.meta` partners, this guide, its reports and the short status updates to existing guides. Do not include unrelated teammate assets/scripts.

```text
feat(level): activate airport checkpoints and verify delayed respawn
```

The human commits/pushes. A draft PR may be opened early; when the four-commit batch is ready, merge from `feature/world-level-integration` into `develop`, preserving individual commits where repository policy permits.
