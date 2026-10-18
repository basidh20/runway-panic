# Agent Controller — bird movement and animation

Student 4 (Agent Controller): *translating the path arrays from the IS module into smooth 3D movement, rotations and animations.*
In the team's shared bird AI plan (`Docs/IS/HANDOFF.md`) this is **F3** (`BirdHealth`, `BirdContactAttack`) and **F4** (`BirdMotor`, the Animator-based `IBirdAnimator`).

Code: `Assets/_Project/Scripts/Agents/` (namespace `RunwayPanic.Agents`):
- `Movement/BirdMotor.cs`, `CatmullRomPath.cs`, `BirdMotionProfile.cs`, `BirdAnimatorDriver.cs`
- `Shared/BirdHealth.cs`, `Shared/BirdContactAttack.cs` (F3, inside the IS team's shared folder)
- editor tools in `Agents/Editor/`, test tools in `Agents/Testing/`

Design decisions with numbers: [`DECISIONS.md`](DECISIONS.md). Notes for the other members: [`HANDOFF.md`](HANDOFF.md).

## The pipeline

```
Species brain (IS, e.g. GullBrain : BirdBrain)
  │  RequestPath(target) → A* on the 3D flight grid (Scripts/AI, IS team)
  │  → Motor.FollowPath(waypoints): world positions for the bird's pivot, one per 2 m cell
  ▼
BirdMotor.FollowPath()                                         once per new path
  1. height offset + minimum altitude, skip waypoints the bird is already in
  2. simplify: drop points on a straight line (only the corners are kept)
  3. centripetal Catmull-Rom spline through the corners, starting at the bird along its current velocity
  4. arc-length table: the curve sampled every 0.25 m with the distance travelled at each sample
  ▼
BirdMotor.FixedUpdate()                                        every physics step (50 Hz)
  5. progress = closest point on the curve (searched in a short window around the last one)
  6. steering: aim at the point LookAheadDistance further along; turn the velocity toward it,
     no faster than MaxTurnRate; slow down for sharp turns; brake so v² = 2·a·d stops at the end
     (FlyTo / HoldPosition / LandAt steer the same way toward a single point)
  7. rotation: yaw + pitch from the velocity, bank (roll) = atan(v·ω / g), clamped and smoothed
  8. kinematic Rigidbody.MovePosition / MoveRotation (interpolated)
  ▼
BirdAnimatorDriver.Update()                                    every frame
  9. Animator parameters: Speed (0-1), VerticalSpeed (-1..1), damped; Flying / Diving when they change
  ▼
AC_Bird (Animator Controller)
 10. Locomotion 2D blend tree: slow or climbing → A_Bird_Fly, fast and level → Fly/Glide mix,
     fast and descending → A_Bird_Glide → A_Bird_Dive; Perched, Diving, Attack, Hit and Death on top
```

## API for the species brains

Brains derive from `BirdBrain` (IS) and only use its `Motor` (`IBirdMotor`) and `Anim` (`IBirdAnimator`). On the prefabs those are `BirdMotor` and `BirdAnimatorDriver`:

| Call | What `BirdMotor` does |
|---|---|
| `Motor.FollowPath(path)` | Smooths the path (Catmull-Rom), flies it at constant speed with banking; a new path starts from the bird along its current velocity (no snap). The list is copied. |
| `Motor.FlyTo(target)` | Steers straight at a point, braking to stop there; call every frame to chase a moving target. |
| `Motor.HoldPosition(point)` | Flies to the point and hovers there. Repeating the same order every frame keeps its state. |
| `Motor.LandAt(point, facing)` | Flies to the point, settles on it facing `facing`, `IsLanded` = true (the animator perches by itself). |
| `Motor.AddSteering(offset)` | Adds a velocity offset for the next physics step only (boids, fear); the bird keeps facing its flight direction. |
| `Motor.SpeedMultiplier` | Top speed × this (e.g. 2.5 for a stoop); bursts also accelerate harder. |
| `Motor.Stop()` | Drops the order and slows to a hover. |
| `Motor.HasPath`, `ReachedDestination`, `IsLanded`, `Position`, `Velocity`, `BaseSpeed` | As documented in `IBirdMotor`. `ReachedDestination` on a path is only true at its end *along the path* (a closed loop does not count as reached at its start). |
| `Anim.SetFlying(false)` | Perched pose (wings and tail closed, legs down). Moving off flies again. |
| `Anim.SetDiving(true/false)` | Holds the dive pose (wings swept back, tail closed). |
| `Anim.SetWingsSpread(0..1)` | Scales the animated wing spread (0 = tucked). |
| `Anim.PlayHit()`, `PlayDeath()` | Flinch (on top of flying) / death. `BirdHealth` already calls them when the bird is hit or dies. |

`BirdMotor` also raises `WaypointReached(int index)` (once for every index of the list you passed, in order) and `PathCompleted`, and exposes `BankAngle`, `PitchAngle`, `RemainingDistance` and `Teleport(position, forward)`.

- **Coordinates:** world space, metres, the bird's **pivot** (centre of mass). Waypoints below `minimumAltitude` (1 m) are raised; `heightOffset` lifts ground-level grid points.
- **Re-planning:** call `FollowPath` again whenever the target moves (the base brain's `RequestPath` does). Measured: no bird ever moves further in a frame than its speed allows (`flight_check.md`).
- **Speed** comes from `BirdSpeciesData.MoveSpeed` (on `BirdHealth`); turn rate, banking, acceleration and flap speed from the species' `BirdMotionProfile` (`ScriptableObjects/Agents/`, on `BirdMotor`).

## Components on a bird (prefab root)

| Component | Owner | Job |
|---|---|---|
| `BirdMotor` | Agent Controller (F4) | `IBirdMotor`: path smoothing, steering, rotation and banking, kinematic `Rigidbody` movement, path gizmos |
| `BirdAnimatorDriver` | Agent Controller (F4) | `IBirdAnimator`: Animator parameters, perch / dive / wing spread, hit / death / strike; root motion off, `CullUpdateTransforms`, per-bird desync |
| `BirdHealth` | Agent Controller (F3, shared file) | `IDamageable`: species HP, wrong-gun damage rule, coins, `Died` + `BirdRegistry.NotifyDied`, falls under physics, then removed |
| `BirdContactAttack` | Agent Controller (F3, shared file) | `TryAttack()` / `TryAttackWithDamage()` on the player: species range (to the player's collider) and cooldown, strike animation |
| `BirdStateLabel`, species brain | IS team | Debug label; the species' decisions |
| `Rigidbody` | — | Kinematic and interpolated while flying; dynamic with gravity after death |

## Animator Controller `AC_Bird` (hawk: `AOC_Bird_Hawk`)

| Parameter | Type | Set by |
|---|---|---|
| `Speed` | float 0–1 | driver, every frame (speed ÷ species top speed, damped 0.15 s) |
| `VerticalSpeed` | float −1…1 | driver, every frame (climb rate ÷ climb rate at max pitch and top speed) |
| `FlapSpeed` | float | driver, once per bird: profile flap multiplier × random 0.9–1.1 |
| `CycleOffset` | float | driver, once per bird: random 0–1 |
| `Flying` | bool (default true) | driver, when it changes: `SetFlying` and `IsLanded` |
| `Diving` | bool | driver, when it changes: `SetDiving` |
| `Attack`, `Hit`, `Death` | trigger | `BirdContactAttack`, `BirdHealth` |

- **Base Layer:**
  - `Locomotion` is a 2D Freeform Cartesian blend tree on Speed × VerticalSpeed. Its state speed is `FlapSpeed` and its cycle offset is `CycleOffset`.
  - `Perched` (while `Flying` is false) and `Diving` (while `Diving` is true).
  - `Attack` plays A_Bird_Dive once and returns.
  - `Any State` → `Death`, with no exit.
- **Reactions layer** (override, weight 1): `Empty` (default) and `Hit`, with `Any State` → `Hit`. Write Defaults is off, so the empty state never overrides the flying base layer.
- **Hawk:** `AOC_Bird_Hawk` (override controller) has the same states with the hawk's own `A_Bird_Hawk_*` clips.

## Editor tools (Tools > Agents)

| Menu | What it does |
|---|---|
| **Build Bird Animations** | Generates the 12 clips, `AC_Bird` and `AOC_Bird_Hawk` from the constants at the top of `BirdAnimationBuilder.cs`; writes `animation_report.md`. Overwrites in place (GUIDs kept). **Hand edits in the Animation window are lost on the next run** — change the constants instead. |
| **Setup Bird Prefabs** | Creates the motion profiles (if missing); on `P_Bird_Base` / `P_Bird_Hawk` swaps the F2 stand-ins (`SimpleBirdMotor`, `BlendShapeBirdAnimator`) for `BirdMotor` and `BirdAnimatorDriver`, assigns the controllers and profiles. Safe to re-run. Species stay on `BirdHealth` (set by the Core Developer's Tools > S3 > Add Bird AI Components). |
| **Create Agent Sandbox** | Builds `Scenes/Sandbox/Sandbox_Chamika.unity`, or asks to rebuild / open it if it exists. |
| **Run Automated Check** | Plays the sandbox unattended: screenshots (`Logs/AgentCheck/`), `flight_check.md`, stress runs into `stress_report.md`. About 2 minutes; Play stops by itself. |

Order after a fresh pull: Build Bird Animations → Setup Bird Prefabs → Create Agent Sandbox → Run Automated Check.

## Testing in Sandbox_Chamika

Press Play, click the Game view:

- **1–4** chase the gull / pigeon / crow / hawk from behind, **0** = overview.
- **H** damages every test bird (flinch), **K** kills them (death clip, fall, removed after landing), **R** respawns them.
- **Gizmos** (Game view toolbar or Scene view): red = raw waypoints, green = smoothed curve, yellow = look-ahead / seek target.
- Each bird is driven by a `TestPathFeeder` (stand-in for a species brain): an A*-style grid path (one waypoint per 2 m cell, right-angle corners) around a closed route, re-planned onto a slightly different route every 5 s. The hawk's route is its feeder's child Transforms — drag them in the Scene view to change it.
- **Stress test:** `StressTest` object (disabled): spawns 40 birds on looping grid paths and appends FPS and frame timings per mode. `Run Automated Check` runs all modes for you.
- **With the real IS code:** in an empty scene, Tools > IS > Set Up Agents Test Scene adds the flight grid, the player and a test hawk whose `TestChaseBrain` chases the player with A* through `BirdMotor`.

## Evidence files (generated)

| File | Written by |
|---|---|
| [`animation_report.md`](animation_report.md) | Build Bird Animations: clip curves, keys before/after reduction, memory; gull vs hawk rest pose |
| [`flight_check.md`](flight_check.md) | Run Automated Check: max bank / pitch / turn rate per bird vs limits, largest per-frame move |
| [`stress_report.md`](stress_report.md) | BirdStressTest: FPS, main thread, render thread, GPU per mode |
