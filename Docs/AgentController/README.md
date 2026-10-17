# Agent Controller — bird movement and animation

Student 4 (Agent Controller): *translating the path arrays from the IS module into smooth 3D movement, rotations and animations.*
Code: `Assets/_Project/Scripts/Agents/` (namespace `RunwayPanic.Agents`), editor tools in `Agents/Editor/`, test tools in `Agents/Testing/`.
Design decisions with numbers: [`DECISIONS.md`](DECISIONS.md). Notes for the other members: [`HANDOFF.md`](HANDOFF.md).

## The pipeline

```
IS pathfinding (A* on a grid)
  │  Vector3[] / List<Vector3>: world positions for the bird's pivot, one per grid cell
  ▼
BirdPathFollower.SetPath()                                    once per new path
  1. height offset + minimum altitude, skip waypoints the bird is already in
  2. simplify: drop points on a straight line (only the corners are kept)
  3. centripetal Catmull-Rom spline through the corners, starting at the bird along its current velocity
  4. arc-length table: the curve sampled every 0.25 m with the distance travelled at each sample
  ▼
BirdPathFollower.FixedUpdate()                                every physics step (50 Hz)
  5. progress = closest point on the curve (searched in a short window around the last one)
  6. steering: aim at the point LookAheadDistance further along; turn the velocity toward it,
     no faster than MaxTurnRate; slow down for sharp turns; brake so v² = 2·a·d stops at the end
  7. rotation: yaw + pitch from the velocity, bank (roll) = atan(v·ω / g), clamped and smoothed
  8. kinematic Rigidbody.MovePosition / MoveRotation (interpolated)
  ▼
BirdAnimatorDriver.Update()                                   every frame
  9. Animator parameters: Speed (0-1), VerticalSpeed (-1..1), damped
  ▼
AC_Bird (Animator Controller)
 10. Locomotion 2D blend tree: slow or climbing → A_Bird_Fly, fast and level → Fly/Glide mix,
     fast and descending → A_Bird_Glide → A_Bird_Dive; Attack, Hit and Death on top
```

## API for the IS scripts

Every bird prefab root has a `BirdAgent`. Get it once and keep it:

```csharp
BirdAgent bird = spawned.GetComponent<BirdAgent>();

bird.Follower.SetPath(waypoints);          // Vector3[] or IReadOnlyList<Vector3>, world space, pivot positions, in order
bird.Follower.WaypointReached += i => { }; // once for every index of YOUR array, in order (also removed/skipped ones)
bird.Follower.PathCompleted += () => { };  // at the last waypoint; the bird then slows to a hover
bird.Follower.ClearPath();                 // forget the path, slow down and hover
bird.Follower.Stop();                      // forget the path and stop dead

bird.Follower.IsFollowing / Velocity / Speed / RemainingDistance
bird.Attack.TryAttack(playerHealth);       // checks species range + cooldown, plays the dive, deals AttackDamage
bird.Health.Died += b => { };              // for counting kills; coins are already paid
bird.Species                               // BirdSpeciesData: MoveSpeed, MaxHP, AttackRange, ...
```

- **Coordinates:** world space, metres, the bird's **pivot** (centre of mass). Waypoints below `minimumAltitude` (1 m) are raised; `heightOffset` lifts ground-level grid points.
- **Re-planning:** call `SetPath` again whenever the target moves. The new curve starts at the bird along its current velocity, so the bird never snaps (measured: `flight_check.md`). Every 0.2–1 s is fine; not every frame (each call rebuilds the curve).
- **First waypoint:** waypoints within 1 m of the bird (its own A* cell) count as already reached.
- **Speed** comes from `BirdSpeciesData.MoveSpeed`; turn rate, banking, acceleration and flap speed from the species' `BirdMotionProfile` (`ScriptableObjects/Agents/`).

## Components on a bird (prefab root)

| Component | Job |
|---|---|
| `BirdAgent` | Holds `BirdSpeciesData` + `BirdMotionProfile`; adds and exposes all the others |
| `BirdPathFollower` | Path smoothing, steering, rotation, kinematic `Rigidbody` movement, path gizmos |
| `BirdAnimatorDriver` | Animator parameters and the Attack / Hit / Death triggers; root motion off, `CullUpdateTransforms`, per-bird desync |
| `BirdHealth` | `IDamageable`: species HP, wrong-gun damage rule, coins, `Died`, falls under physics, then removed |
| `BirdAttack` | `TryAttack(IDamageable)` with species range and cooldown |
| `Rigidbody` | Kinematic and interpolated while flying; dynamic with gravity after death |

## Animator Controller `AC_Bird`

| Parameter | Type | Set by |
|---|---|---|
| `Speed` | float 0–1 | driver, every frame (speed ÷ species top speed, damped 0.15 s) |
| `VerticalSpeed` | float −1…1 | driver, every frame (climb rate ÷ climb rate at max pitch and top speed) |
| `FlapSpeed` | float | driver, once per bird: profile flap multiplier × random 0.9–1.1 |
| `CycleOffset` | float | driver, once per bird: random 0–1 |
| `Attack`, `Hit`, `Death` | trigger | `BirdAttack`, `BirdHealth` |

- **Base Layer:** `Locomotion` (2D Freeform Cartesian blend tree on Speed × VerticalSpeed, speed = `FlapSpeed`, cycle offset = `CycleOffset`) → `Attack` (A_Bird_Dive, back to Locomotion at 90%) ; `Any State` → `Death` (no exit).
- **Reactions layer** (override, weight 1): `Empty` (default) and `Hit`; `Any State` → `Hit`. Write Defaults off, so the empty state never overrides the flying base layer.
- The hawk uses `AOC_Bird_Hawk` (override controller): the same states with its own `A_Bird_Hawk_*` clips.

## Editor tools (Tools > Agents)

| Menu | What it does |
|---|---|
| **Build Bird Animations** | Generates the 10 clips, `AC_Bird` and `AOC_Bird_Hawk` from the constants at the top of `BirdAnimationBuilder.cs`; writes `animation_report.md`. Overwrites in place (GUIDs kept). **Hand edits in the Animation window are lost on the next run** — change the constants instead. |
| **Setup Bird Prefabs** | Creates the motion profiles (if missing), adds the components and kinematic Rigidbody to `P_Bird_Base` / `P_Bird_Hawk`, assigns the controllers, sets species + profile per variant. Safe to re-run. |
| **Create Agent Sandbox** | Builds `Scenes/Sandbox/Sandbox_Chamika.unity` (opens it if it exists). |
| **Run Automated Check** | Plays the sandbox unattended: screenshots (`Logs/AgentCheck/`), `flight_check.md`, stress runs into `stress_report.md`. About 2 minutes; Play stops by itself. |

## Testing in Sandbox_Chamika

Press Play, click the Game view:

- **1–4** chase the gull / pigeon / crow / hawk from behind, **0** = overview.
- **H** damages every test bird (flinch), **K** kills them (death clip, fall, removed after landing), **R** respawns them.
- **Gizmos** (Game view toolbar or Scene view): red = raw waypoints, green = smoothed curve, yellow = look-ahead target.
- Each bird is driven by a `TestPathFeeder`: an A*-style grid path (one waypoint per 2 m cell, right-angle corners) around a closed route, re-planned onto a slightly different route every 5 s. The hawk's route is its feeder's child Transforms — drag them in the Scene view to change it.
- **Stress test:** `StressTest` object (disabled): spawns 40 birds on looping grid paths and appends FPS and frame timings per mode. `Run Automated Check` runs all modes for you.

## Evidence files (generated)

| File | Written by |
|---|---|
| [`animation_report.md`](animation_report.md) | Build Bird Animations: clip curves, keys before/after reduction, memory; gull vs hawk rest pose |
| [`flight_check.md`](flight_check.md) | Run Automated Check: max bank / pitch / turn rate per bird vs limits, largest per-frame move |
| [`stress_report.md`](stress_report.md) | BirdStressTest: FPS, main thread, render thread, GPU per mode |
