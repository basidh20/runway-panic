# Gull — eight-bird Editor performance sample

Status: Completed eight-Gull Editor sample; scoped result only

UTC: 2026-10-18 08:17:06. Unity 6000.6.4f1; Mac OS X 15.7.7; Apple M1.
Editor Play mode, eight actual Gull prefabs spawned above eight existing FOD points; natural collection, formation and attack. Minimum 15 s warmup (also wait for a real FOD impact) + 15 s capture. No Deep Profile. Labels and Gull overlay hidden. State-transition logging remains enabled.
Stationary player; controller disabled transiently and HP restored below 25% to avoid ending the fixture. No gun hits/distress stimulus. Saved scenes/prefabs/species/motor/animation code are unchanged.
Unity ProfilerRecorder uses existing CPU script-message markers, SumAllSamplesInFrame, main thread only, capacity 8192. Nanoseconds converted to milliseconds. All recorder buffers start/stop together; combined mean = sum of captured marker durations / non-empty GullBrain.Update frame sample count. Every included brain sample must contain exactly eight calls; missing/partial frames cannot be treated as cheaper eight-Gull work. This is not FPS or whole-frame CPU time.
Scope: recorded Gull brain/kettle/FOD/supply callbacks plus shared BirdMotor physics updates and BirdAnimatorDriver updates for these eight Gulls. Synchronous called work is included inside those callbacks. Rendering/GPU, animation-system jobs, general engine physics, debug labels/stats, player/hub scripts and other unlisted callbacks are excluded. Missing/never-called callbacks have no invented samples; consult the actual marker rows.
The 0.5 ms target is compared only to the scoped mean. This Editor sample does NOT certify a standalone development build, every-frame target, distress/spawn/death worst cases, or the whole game's script cost.

Actual warmup: 15.2419 s, including at least one real FOD impact before recorder discovery.
Warmup end states: ClimbToTop=1, JoinKettle=1, InKettle=5, DescendToDrop=1
Brain-marker frame samples: 2517. Recorded wall-clock interval: 15.0003 s.

| Unity script marker | Calls | Non-empty samples | Mean contribution per brain frame (ms) | Non-empty sample p95 (ms) | Marker peak (ms) |
|---|---:|---:|---:|---:|---:|
| Assembly-CSharp.dll!RunwayPanic.Agents.Gull::FodItem.OnCollisionEnter() [Invoke] | 11 | 9 | 0.0006 | 0.4456 | 0.4456 |
| Assembly-CSharp.dll!RunwayPanic.Agents.Gull::FodItem.Update() [Invoke] | 20966 | 2517 | 0.0013 | 0.0017 | 0.0152 |
| Assembly-CSharp.dll!RunwayPanic.Agents.Gull::FodManager.Update() [Invoke] | 2517 | 2517 | 0.0018 | 0.0027 | 0.0249 |
| Assembly-CSharp.dll!RunwayPanic.Agents.Gull::FodSpawnPoint.Update() [Invoke] | 20136 | 2517 | 0.0019 | 0.0023 | 0.0138 |
| Assembly-CSharp.dll!RunwayPanic.Agents.Gull::GullBrain.Update() [Invoke] | 20136 | 2517 | 0.0319 | 0.0418 | 1.1237 |
| Assembly-CSharp.dll!RunwayPanic.Agents.Gull::GullKettle.Update() [Invoke] | 2517 | 2517 | 0.0015 | 0.0021 | 0.0128 |
| Assembly-CSharp.dll!RunwayPanic.Agents::BirdAnimatorDriver.LateUpdate() [Invoke] | 20136 | 2517 | 0.0018 | 0.0023 | 0.0137 |
| Assembly-CSharp.dll!RunwayPanic.Agents::BirdAnimatorDriver.Update() [Invoke] | 20136 | 2517 | 0.0087 | 0.0131 | 0.0307 |
| Assembly-CSharp.dll!RunwayPanic.Agents::BirdMotor.FixedUpdate() [Invoke] | 5968 | 729 | 0.0079 | 0.0344 | 0.3281 |

Combined recorded scope mean: **0.0574 ms per brain frame**. Below 0.5 ms on average in this Editor sample.
Sum of individual marker peaks: 2.0086 ms — a conservative bound, NOT a measured same-frame combined peak. Rows' p95 values are per marker/non-empty sample, NOT a combined-frame p95.

Interpretation: the GullBrain marker alone peaked at **1.1237 ms**, so the strict 0.5 ms every-frame flock budget was **not maintained in this Editor capture**, despite the lower mean. The peak's cause is not established by these aggregate samples; a detailed trace and development-player comparison remain necessary. No runtime tuning was changed to hide or explain away the spike.

End states: InKettle=2, JoinKettle=2, DescendToDrop=1, ClimbToTop=2, GatherFod=1

API reference: [Unity ProfilerRecorderOptions](https://docs.unity.com/en-us/engine/6000.7/script-reference/unity/profiling/profilerrecorderoptions). SumAllSamplesInFrame aggregates repeated calls; do not divide the eight-Gull sum by eight when comparing a per-frame flock budget.
