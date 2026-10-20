# Pigeon: The Panicking Flock

## Ownership and integration

The implementation is confined to `Assets/_Project/Scripts/Agents/Pigeon/`, its existing strategy asset, and this documentation folder. No other bird, shared motor/pathfinder, production scene, weapon, or wave data is changed. Existing `P_Bird_Pigeon` instances already carry the brain and configuration, so no production scene rebuild is necessary.

The pigeon is a hierarchical, coordinated agent: `PigeonFlock` decides what the group does; `PigeonBrain` decides how each bird participates. It uses the existing `IBirdMotor`, `BirdContactAttack`, `PlayerEventHub`, `PathService`, and `FearRegistry` contracts. The shared services are dependencies, not pigeon-owned implementations.

## Demonstration

Open `Assets/_Project/Scenes/Sandbox/Sandbox_IS_Pigeon.unity` and enter Play mode:

- **B** spawns six pigeons; **N** adds a late joiner.
- **G** equips the assault rifle. Shoot near the flock or damage a member to raise alarm.
- **O** toggles shopping; pigeons stop while the shop is open.
- **F3** toggles shared state labels.

Watch gathering, staggered dives, recovery, panic, and regrouping. The first rally selection is distributed over eight budgeted searches, not performed for every bird every frame.

Run **Tools > IS > Pigeon > Run Acceptance Check** outside Play mode. It opens only the pigeon sandbox, creates transient birds, runs the real motor/contact attack, and writes `Logs/Pigeon/Acceptance.md`. It does not save scenes or alter production assets. Batch entry point: `RunwayPanic.Agents.Pigeon.EditorTools.PigeonAcceptanceCheck.RunBatch`.

## Decision architecture

Group cycle: Gathering -> Mobbing -> Regrouping -> Mobbing. A high alarm interrupts the cycle with Scattering, followed by Regrouping. An absent/dead player sends members to available roosts or airborne rest. An empty flock returns to Roosting.

Individual cycle: JoinFlock -> Flocking -> Dive -> Recover -> Flocking. Panic interrupts attacks and releases reservations. A full flock sends an extra bird to Solo, which periodically retries membership and otherwise performs spaced hit-and-run attacks. Roost approach and landing have timeouts. Missing slots result in airborne rest rather than an invented landing surface.

Readiness uses the fraction of members within the formation radius plus 3 metres, with an eight-second timeout so a straggler cannot block combat forever. Readiness is a coordination heuristic, not a proof that every member can reach the rally point.

## Steering and search justification

For neighbours within 6 m, local steering combines:

`separation * 2 + clamp(meanNeighbourVelocity - velocity, 1) * 0.35 + clamp(meanNeighbourPosition - position, 1) * 0.25 + fearRepulsion`

Separation sums `away / max(0.1, distance^2)` for neighbours within 2 m. Coincident birds use a deterministic opposing direction. The result is clamped to 2 m/s. Separation dominates to discourage overlaps; alignment/cohesion are weaker because they must not overwhelm route following. These are starting gameplay weights, not learned or mathematically optimal values.

The motor consumes steering once per physics step, so the brain submits it from `FixedUpdate`. A short swept clearance check rejects unsafe nudges near geometry. This reduces risk but does not replace the shared motor's collision/flight behavior.

Eight rally candidates surround the player at a 10 m horizontal radius and 7 m relative height. Candidates are bounded by the flight volume and checked for free space and an A* route from the flock centre. Candidate utility is:

`2 * novelty + safety - routeLength / (4 * rallyRadius) - fearMagnitude`

`novelty = (1 - dot(candidateDirection, previousAttackDirection)) / 2`, favouring another approach side. Safety is normalised distance from the remembered threat, capped at 1. Route length penalises detours, and registered hawk fear discourages dangerous positions. A valid centre route is only a candidate filter: each member still navigates independently.

Each candidate consumes the existing global search budget. Individual routes use the shared A* with `FearCostModifier(Pigeon)` when a direct swept flight is obstructed; otherwise direct flight avoids unnecessary searches. Repath attempts are bounded by a 1.2 s interval, with short retry delays for budget throttling. Failure retains/retries a safe strategy rather than flying through an obstacle. Rally search failure retains the previous rally goal.

The shared A* and motor are not reimplemented here. Be ready to explain both how the pigeon uses them and which team member owns their internals.

## Attacks and panic

At most two flock members hold dive reservations; dispatches are at least 1.2 s apart. Round-robin selection prevents a single bird monopolising attacks. Dives require a clear approach, predict a bounded fraction of player motion, and abort for obstruction, shielding, death, or a five-second timeout. One successful contact ends the dive. Recovery and a per-bird cooldown prevent continuous contact damage.

Alarm is clamped to [0,1]. A nearby shot segment adds 0.2 once per flock, surviving damage adds 0.45, and a member death adds 0.65. Alarm decays by 0.08/s. At 0.7, the flock scatters for four seconds; a cooldown prevents immediate repeated panic. The threat source is remembered for escape and rally scoring. Panic propagation is a shared flock alarm, not a simulated spatial sound model or neighbour-by-neighbour emotional contagion.

Pause and shopping gate decisions explicitly. Shopping does not freeze global time in this project, so pigeons also stop their motors. Cooldown deadlines use scaled `Time.time`; they can expire during shopping, while state timers do not advance. No shared game-state changes are introduced.

## Complexity and evidence limits

Neighbour steering costs O(n^2) per physics step, bounded by the configured flock cap (default 12). Group decisions and member pruning are O(n). Rally search is eight candidates spread across frames; A* work remains bounded by the shared grid's expansion and global per-frame limits. Reused lists, buffers and modifiers avoid allocating collections each decision tick.

The acceptance fixture reports a scoped CPU sample for pigeon brain updates plus flock strategy when profiler markers are available. It excludes motor, rendering, physics engine, other agents and some bookkeeping, and does not establish whole-game FPS or a standalone-build budget. All-four-species simultaneous performance is a separate group integration requirement.

The fixture keeps a stationary player alive to exercise attacks, then damages two real birds to test damage-driven panic. It also tests capacity/solo fallback, wave reset, pause/resume, blocker queries, revival, and player death/respawn. That is controlled integration evidence, not a human difficulty/balance study. The final report must be read for actual pass/fail outcomes; the presence of the tool alone is not a passing result.

On this machine Unity's Editor search-index startup has independently thrown an `ArgumentOutOfRangeException` in `UnityEditor.Search.SearchDatabase`. The acceptance report preserves that exact stack separately from game runtime failures; it does not silently discard it. Other exceptions, including those from shared gameplay code, still fail the fixture. No search-index settings or other owners' files are changed to suppress the issue.

## Viva navigation

| Topic | File |
| --- | --- |
| State dispatch and lifecycle | `PigeonBrain.cs` |
| Joining and leaving | `PigeonBrain.Join.cs` |
| Movement, attacks, panic, solo and roost behavior | `PigeonBrain.Behaviours.cs` |
| Group membership and game events | `PigeonFlock.cs` |
| Rally utility, coordination, alarm, steering and claims | `PigeonFlock.Strategy.cs` |
| Swept safety checks and volume limits | `PigeonNavigation.cs` |
| Tuning parameters | `PigeonStrategyConfig.cs` |
| Real-prefab verification | `Editor/PigeonAcceptanceCheck.cs` |

Explain why flat independent state machines would struggle to agree on attack timing and rally points, why a coordinator helps, and why utility scoring is used for destination choice while states organise action sequencing. A behavior tree is a reasonable alternative, but would still need shared coordination data. No architecture is automatically superior; the choice follows this small, coordinated enemy group.
