# Main Crow setup — AI-level batch, milestone 3 of 4

Unity: **6000.6.4f1**. Branch: **feature/world-ai-integration**. Human commit/merge target: **develop** after the batch, not main.

## References used and scope

This bird-related task was checked against the shared `BIRD_STRATEGIES.md`, especially Crow theft/stashes and shelter-exit vigil. The latest Crow owner handoff and `03_CROW_PLAN.md` clarify that stash pickups must be reachable on foot, not stranded on a roof. We therefore retain the supplied prefab's ground-level stash positions rather than the older strategy document's rooftop suggestion. The user's newer **three waves / four species** decision overrides that document's older four-wave example; no waves are configured in this milestone.

For any future bird work, refer to `BIRD_STRATEGIES.md`. For the user's Gull work, also refer to `01_GULL_PLAN.md` before implementing it. These reference files live in the parent project workspace, outside the Unity repository.

## Saved scene integration

One connected `Assets/_Project/Prefabs/Birds/Crow/P_CrowSetup_Main.prefab` instance is installed at Main's scene root, position `(0,0,0)`, rotation identity, scale one. No prefab contents were copied, regenerated or overridden. This retains future updates from its owner.

The prefab supplies:

| Element | Location / configuration |
| --- | --- |
| Stash_Hangar | Approximately (-47.41, 0, -16.15), on ground behind the hangar |
| Stash_Terminal | Approximately (24.99, 0, 37.49), on ground beside the terminal |
| ShelterZone_ShelterA | Centre (-21, 1.55, 12), yaw -90, trigger size (9.2, 2.9, 7.2), group ShelterA |
| ShelterZone_ShelterB | Centre (21, 1.55, -12), yaw +90, same size, group ShelterB |
| ShelterExit_ShelterA | (-15.5, 0, 12), BirdPoint ShelterExit, group ShelterA |
| ShelterExit_ShelterB | (15.5, 0, -12), BirdPoint ShelterExit, group ShelterB |

Both stashes use BirdPoint type Stash. The prefab contains no live birds. The existing FlightGrid, pickup spawner, eight airport Spawn markers and eight Hawk Perch markers are retained without duplication. If structures move, update matching setup locations in coordination with the Crow owner; this prefab root remains at the origin.

ShelterZone reports player occupancy for Crow vigil behaviour. **It does not grant invulnerability, lock doors, enforce stay duration or add a cooldown.** Normal Play still has no production bird spawner or WaveManager, so installing these points does not itself make crows appear or steal items.

## One focused check

**Tools > Runway Panic > Integrate Main Crow Setup** creates only a missing connected setup instance, keeps its root pose, validates and saves Main. Unexpected setup roots or pre-existing manual Crow zones/points are refused before duplicating them. Repeat calls reuse the instance.

**Validate Main Crow Setup** runs in Edit mode. It checks prefab connection/root pose, counts/groups, ground support and human-sized local clearance at stashes/exits, zone dimensions/rotation against actual shelters, guard points outside the fitted doors/zones, existing grid/pickup/spawn/perch prerequisites and four grid routes. Stash routes use the Crow's actual 0.8 m approach height; exits use its 2 m vigil approach height. The existing grid's preview arrays are rebuilt without changing serialized settings.

`CrowSetupValidation.txt`: **18 passed checks, zero failures**. This is an asset/geometry/approach check, not an end-to-end player walk, trigger entry/exit or live Crow theft/vigil test. No Crow was spawned and the owner's longer AI suite was not rerun. Their earlier seven-behaviour check is historical evidence in `Docs/IS/Crow/main_integration.md`, not a result of this milestone.

## Manual handoff

Suggested commit:

```text
feat(level): install crow stash and shelter setup in airport
```

After the batch merges, tell the Crow owner the delivered prefab is now saved in Main, with the real FlightGrid and pickup spawner present. Shared Crow behaviour/config/prefab and bird stats are untouched. Production-wave AI testing follows when waves are connected.

Next milestone on this branch: Gull/Pigeon behaviour points, consulting `BIRD_STRATEGIES.md` and, for Gull, `01_GULL_PLAN.md`.
