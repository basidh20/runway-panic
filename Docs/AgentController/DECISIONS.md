# Agent Controller — Design Decisions Log

Evidence for the viva (Workflow & Tools, Design & Optimization Justification). One entry per decision, newest at the bottom. Same format as `SourceArt/DECISIONS.md`.

Template:
```markdown
## <Date> — <Decision title>
- **Context:** what problem I was solving
- **Options considered:** A / B / C
- **Choice:** what I picked
- **Why:** visual, performance, or workflow reason
- **Numbers:** tri count / texture size / memory / fps impact, if any
```

All measurements: Editor Play mode, Game view 1101×471, Intel Core i5-1135G7 with **Intel Iris Xe integrated graphics** (Direct3D 11). They are not comparable with the Core Developer's numbers in `SourceArt/perf_report.md`, which come from an RTX 3050.

---

## 2026-10-17 — Path smoothing: centripetal Catmull-Rom with an arc-length table
- **Context:** The IS pathfinding is A* on a grid, so a path is one point per cell with right-angle corners. Moving the bird straight from point to point would make it zig-zag and turn instantly at every corner. Flying birds also leave the ground NavMesh behind.
- **Options considered:** A) Straight-line lerp between waypoints. B) NavMeshAgent. C) Bézier curves. D) Catmull-Rom spline (uniform or centripetal).
- **Choice:** D, centripetal (alpha = 0.5), in `CatmullRomPath.cs`. Collinear grid points are removed first (bend < 2°), so the spline only bends at real corners.
- **Why:**
  - A gives instant 90° turns.
  - B is a ground agent: it pins the agent to the baked NavMesh surface, but birds fly at 4–11 m. It would also replace the IS team's own A* (their graded work).
  - C needs extra control points that the A* path does not contain.
  - Catmull-Rom **passes through every waypoint**, so the bird really goes through the cells the A* chose (for example a hangar door). The *centripetal* version spaces the knots by √distance, which avoids the overshoot and self-loops the uniform version makes when corners are unevenly spaced (short and long grid runs mixed).
  - The curve is sampled into an **arc-length table** (position + distance travelled every 0.25 m). "Where is the bird after s metres?" becomes a binary search plus a lerp, so the bird moves at a constant speed along the curve, not a constant parameter t, which would speed up on long spans and slow down on short ones.
- **Numbers:** built once per `SetPath`, never per frame. Buffers are reused and only grow, so re-planning does not allocate once they are big enough. The per-step progress search looks at about (1 + look-ahead + 2) m ÷ 0.25 m ≈ 20–30 samples, not the whole path.

## 2026-10-17 — Steering: look-ahead target with a turn-rate limit, not exact curve tracking
- **Context:** Even the smoothed curve can turn faster than a bird can. If the bird were placed exactly on the curve, its body would have to rotate instantly, or it would visibly slide sideways while turning.
- **Options considered:** A) Place the bird at distance s on the curve and point it along the tangent. B) Pure pursuit: aim at a point `LookAheadDistance` ahead on the curve and turn the *velocity* toward it at most `MaxTurnRate` °/s.
- **Choice:** B, in `BirdPathFollower.Steer` and `FixedUpdate`.
- **Why:** The bird always faces exactly where it flies (no sliding), and its turn radius follows physically from speed ÷ turn rate. That is what makes the species different: the hawk (8 m/s, 90°/s) flies 5 m-radius arcs, the pigeon (4 m/s, 260°/s) 0.9 m. A look-ahead target also removes jitter from small kinks in the curve. Two speed rules keep it on the path:
  - **Braking:** target speed = √(2·a·d), the highest speed from which the bird can still stop in the remaining distance d.
  - **Sharp turns:** speed drops to 40% when the target is behind the bird, like a car in a corner.
- **Numbers:** `flight_check.md`: every bird reaches but does not exceed its turn limit (gull 141/140 °/s, pigeon 260/260, crow 178/170, hawk 90/90). The two values over the limit come from measuring the interpolated, drawn rotation on short frames.

## 2026-10-17 — Re-planning mid-flight without a snap
- **Context:** The IS AI re-plans whenever the player moves. A new path must not make the bird jump or flip around.
- **Options considered:** A) Start the new spline at its first waypoint. B) Start it at the bird, with the first tangent along the current velocity.
- **Choice:** B. The curve starts at the bird's position, with a short straight **lead-in** (0.25 s of flight) along its current velocity, and a phantom point behind the bird on the same line. Leading waypoints within 1 m (the bird's own A* cell) count as already reached. Progress along the path never goes backwards.
- **Why:** The new curve leaves the bird exactly in the direction it is already flying, so position and heading are continuous across the change. The turn-rate limit then turns it smoothly onto the new route.
- **Numbers:** `flight_check.md`, 12.9 s with every path replaced every 5 s: the largest move between two frames ÷ (top speed × frame time) is **1.00 for all four birds**, i.e. no frame ever moved a bird further than its speed allows.

## 2026-10-17 — Rotation: yaw and pitch from the velocity, bank from the turn rate
- **Context:** A bird that only yaws looks like a cardboard cut-out. Real birds and planes roll into turns.
- **Options considered:** A) `LookRotation(velocity)`. B) A constant roll per turn direction. C) The coordinated-turn bank angle.
- **Choice:** C. Bank = atan(v·ω / g) (v = speed, ω = yaw rate, g = 9.81), clamped to the profile's `MaxBank` and smoothed with `SmoothDamp` (`BankSmoothTime`). Pitch = asin(velocity.y), clamped to `MaxPitch`. Rotation = `Quaternion.Euler(-pitch, yaw, -bank)`.
- **Why:**
  - **Physics:** in a balanced turn the lift vector tilts so its horizontal part provides the centripetal force, tan(bank) = v·ω/g. So faster or tighter turns bank more, exactly like a real bird.
  - **Never upside down:** Unity's Euler order applies roll, then pitch, then yaw about **world up**. With |pitch| ≤ 40° the bird can never flip, and gimbal lock (pitch = 90°) cannot happen.
  - **Species feel:** the smoothing time decides it — 0.07 s pigeon (twitchy), 0.35 s hawk (smooth).
- **Numbers:** `flight_check.md`: max bank gull 29.9° (limit 30), pigeon 35.0 (35), crow 35.0 (35), hawk 34.9 (35); max climb/dive within limits. Screenshot `13_Bird_Hawk_a` shows the hawk rolled about 35° into a turn.

## 2026-10-17 — Moving the bird: kinematic Rigidbody
- **Context:** The bird has to be hit by the player's raycasts at the position where it is drawn, and fall under physics when it dies.
- **Options considered:** A) Set `transform.position` in Update. B) CharacterController. C) Kinematic Rigidbody with `MovePosition` / `MoveRotation` in FixedUpdate, interpolated.
- **Choice:** C (added by Setup Bird Prefabs). `BirdHealth` switches it to dynamic with gravity on death and back to kinematic when the body lands.
- **Why:**
  - **Physics stays correct:** a kinematic body is moved by script but still tells the physics engine where its collider is, so `PlayerShooting`'s raycasts and triggers are correct. Moving a collider without a Rigidbody forces Unity to treat it as moved static geometry.
  - **No collision cost while flying:** kinematic bodies are not pushed and do not resolve collisions.
  - **Smooth on screen:** interpolation draws the bird smoothly between the 50 Hz physics steps.
  - **Why not B:** a CharacterController is ground-oriented (gravity, slopes, step offset) and its capsule is always upright, which a banking bird is not.
  - **Death is one switch:** turning on dynamics gives a natural fall that keeps the bird's momentum.
- **Numbers:** moving 40 birds: main thread 2.9 ms against 3.7 ms for static birds, i.e. within measurement noise (`stress_report.md`, 13:18).

## 2026-10-17 — Animation clips generated by an editor tool
- **Context:** Five clips (Fly, Glide, Dive, Hit, Death) for a 9-bone rigid rig. They must work on two models: the gull rig (gull, pigeon, crow) and the hawk.
- **Options considered:** A) Hand-key them in Unity's Animation window. B) Animate in Blender and export through the Core Developer's pipeline (NLA strips). C) Generate them from code (`Tools > Agents > Build Bird Animations`).
- **Choice:** C, `BirdAnimationBuilder.cs`.
- **Why:**
  - **No changes to the Core Developer's files:** B needs edits to their `.blend` files (one owner per binary file, `README.md`).
  - **Reproducible and explainable:** every amplitude is a named constant (`FlapTop = 45`, `CycleSeconds = 0.5`, `LegTuck = 60`…). Re-running rebuilds everything in place with the same GUIDs, so the prefabs keep their references.
  - **Same pose on both rigs:** poses are written as rotations about the **bird's own axes** (beak +Z, up +Y, right +X). The tool reads each bone's rest rotation from the real model and converts the offset into the bone's local space: local′ = P⁻¹ · offset · P · local, with P = the parent's rest rotation. So "raise the wing 45°" is the same on every rig, and the left/right mirroring comes from the bone's side of the body, not from its name.
  - **Only rotations and blend shapes are keyed** (`m_LocalRotation`, `Wings_Spread`, `Tail_Spread`). Keying positions or scale would distort the hawk, whose bones sit elsewhere, and fight the species scale on the prefab root.
  - **The hawk gets its own clip set.** Its bones rest in different directions from the gull's, and a clip keys *absolute* local rotations, so one set would bend the hawk's wings by 34°. It gets `A_Bird_Hawk_*` generated from its own rest pose, behind `AOC_Bird_Hawk`: an Animator Override Controller, the same state machine with the clips swapped.
  - Hand edits in the Animation window are lost when the tool runs again — change the constants instead.
- **Numbers:** `animation_report.md`: rest-pose difference gull vs hawk **Wing_L/R 34.2°**, Tail 5.9°, Head 4.8°, Legs 1.3°. I also read these from the FBX files before the tool existed: same values.

## 2026-10-17 — Animator: one 2D blend tree, a Reactions layer and Any State death
- **Context:** The wing motion has to follow the flight: flap when slow or climbing, glide when cruising, tuck when diving. A hit must not stop the bird flying, and death must win over everything.
- **Options considered:** A) Separate Fly / Glide / Dive states with transitions on thresholds. B) One 1D blend tree on climb rate. C) One 2D Freeform Cartesian blend tree on Speed × VerticalSpeed.
- **Choice:** C, plus an Attack state (the dive clip), `Any State → Death` with no exit, and a **Reactions** override layer (Empty → Hit) for the flinch.
- **Why:**
  - **Smooth blending:** A pops between poses at thresholds; a blend tree mixes continuously.
  - **Hovering flaps:** with only climb rate (B), a hovering bird would half-glide. The Speed axis makes slow flight flap.
  - **Blend points:** Fly at (0,0), (0,1), (1,1), (1,0.35); Glide at (0,−1), (1,−0.35); Dive at (1,−1). Level cruising is a 50/50 Fly/Glide mix.
  - **One cycle length:** all three locomotion clips are 0.5 s long, because a blend tree plays its clips in step. Mixing a 0.5 s flap with a 2 s glide would slow the wing beat to 1.25 s while blending.
  - **Hits don't stop flight:** the Hit layer only keys Body, Head and Wings, and its states have **Write Defaults off**. With it on, the empty state would write the rest pose over the flying base layer at weight 1.
  - **No Bank parameter:** banking is already a rotation of the whole bird (previous entry), so the controller does not need it, which saves a parameter write per bird per frame.
- **Numbers:** 7 parameters. Per bird per frame only 2 are written (`Speed`, `VerticalSpeed`, damped 0.15 s); `FlapSpeed` and `CycleOffset` are written once.

## 2026-10-17 — Animation optimisations
- **Context:** Waves may hold dozens of birds; animation must not be the bottleneck.
- **Choices and why:**
  - **Keyframe reduction in the tool:** a key is removed when the curve without it stays within 0.0005 of the original (about 0.06° of rotation) at the key and halfway to its neighbours. Constant curves (legs held tucked) end with 2 keys. The FBX importer's "Optimal" compression only applies to imported FBX clips, not to `.anim` assets, so the reduction is done when the clips are generated.
  - **`Animator.StringToHash` once:** `SetFloat(int)` instead of hashing a string every call.
  - **`AnimatorCullingMode.CullUpdateTransforms`:** off-screen birds do not write their bones, but their state machine keeps running, so a Death or Hit trigger fired off-screen is not lost. `CullCompletely` would freeze the state machine as well.
  - **Root motion off:** the path follower moves the bird; clips only rotate bones.
  - **No per-frame allocations:** path buffers are reused, events and hashes don't allocate, and the death sequence is a coroutine, so a living bird pays nothing for it every frame.
  - **Flock desync:** each bird gets a random `CycleOffset` (0–1) and ±10% `FlapSpeed` once at spawn, so 10 pigeons never flap in lockstep — with no per-frame cost.
- **Numbers:** `animation_report.md`: **1,524 generated keys → 869 after reduction (43% removed)**, 10 clips, 304 KB editor memory in total (a build stores clips more compactly).

## 2026-10-17 — Measured: 40 moving, animated birds and 2-bone skinning
- **Context:** I needed to know what my part costs, and whether one more optimisation was worth it: GPU skinning uses up to 4 bones per vertex on the PC quality level, but the bird rig needs at most 2 (rigid parts plus the neck blend).
- **Method:** `Tools > Agents > Run Automated Check`. 40 birds on looping grid paths, the same seed every run, 3 s warm-up, 10 s measured, gizmos off. The `FrameTimingManager` splits the frame into main-thread work, render thread and GPU. The 4-bone and 2-bone animated runs alternate twice, so a one-off hitch cannot decide the comparison.
- **Numbers** (`stress_report.md`, run of 13:18–13:19):

  | 40 birds | Avg FPS | Frame (ms) | Main thread (ms) | GPU (ms) |
  |---|---:|---:|---:|---:|
  | Static | 41.7 | 24.0 | 3.7 | 23.1 |
  | Moving, no animation | 42.5 | 23.5 | 2.9 | 23.0 |
  | Moving + animated, 4-bone skinning | 45.4 / 44.1 | 22.0 / 22.7 | 7.1 / 5.1 | 21.6 / 22.1 |
  | Moving + animated, 2-bone skinning | 44.4 / 44.6 | 22.5 / 22.4 | 4.8 / 6.2 | 22.0 / 21.6 |

- **Result:**
  - **GPU-bound in every mode.** The GPU needs 21.6–23.1 ms of a 22–24 ms frame, and the main thread finishes its work in 3–7 ms. The cost is drawing the birds (25–28k triangles each, drawn in several passes including shadows), which is the Core Developer's area (LODs, bird shadows). It does not depend on whether the birds move or flap.
  - **Movement and animation are free here.** They add up to ~4 ms of main-thread work, which runs while the GPU is still busy, so the frame rate does not drop.
  - **2-bone skinning: no measurable gain** (44.4 / 44.6 vs 45.4 / 44.1 FPS; GPU 21.6–22.0 vs 21.6–22.1 ms). **Not applied**: the prefabs keep the default. Skinning is a small part of the GPU work compared with drawing the triangles.
  - **The alternating runs mattered.** In the previous run the first animated run showed 28.6 ms and a 6.9 FPS low because of a one-off warm-up hitch. Its repeat showed 22.5 ms.

## 2026-10-17 — Testing without the IS code: an A*-style feeder and an unattended check
- **Context:** The IS pathfinding does not exist yet, and visual checks by hand are slow and not reproducible.
- **Choice:** `TestPathFeeder` sends a bird a grid path exactly like A* would: one point per 2 m cell, right-angle corners, a closed route re-planned every 5 s onto a slightly different one. `AgentAutoCheck` (`Tools > Agents > Run Automated Check`) then plays the sandbox unattended: screenshots of every bird, the flinch and the death fall, the flight measurements and the stress runs. Play stops by itself.
- **Why:** The same test runs identically every time. Its numbers caught a real bug: on a closed route the last waypoint is the start, and arrival only checked the distance to the last waypoint, so every bird "arrived" on its first physics step. They hovered in place, and their feeders re-sent the path on every step (25.7 ms of main-thread work for 40 birds). The screenshots looked plausible, because a hovering bird flaps. Fixed: arrival now also requires the bird to be at the end of the path *along the curve* (commit `fix(agents): complete a path only at its end along the curve`).
- **Numbers:** before the fix, the flight check showed 0° bank and 0 °/s turn for every bird. After it, banks of 29.9–35.0° and turn rates at their limits (entries above).
