# Bird stress test results

Appended by `BirdStressTest` (Sandbox_Chamika; Tools > Agents > Run Automated Check runs every mode), Editor Play mode.
CPU main = main-thread work without its wait for the GPU. GPU = GPU frame time (n/a if the graphics API does not report it).

| Date | Birds | Mode | Skin quality | Animator culling | Avg FPS | Avg ms | Lowest FPS (0.5 s) | CPU main (ms) | Render thread (ms) | GPU (ms) | Game view |
|---|---:|---|---|---|---:|---:|---:|---:|---:|---:|---|
| 2026-10-17 13:13 | 40 | Static | Auto | CullUpdateTransforms | 42.6 | 23.46 | 40.5 | 3.66 | 4.39 | 22.93 | 1101x471 |
| 2026-10-17 13:13 | 40 | MovingNoAnimation | Auto | CullUpdateTransforms | 40.0 | 25.01 | 16.0 | 3.34 | 4.64 | 22.93 | 1101x471 |
| 2026-10-17 13:14 | 40 | MovingAndAnimated | Auto | CullUpdateTransforms | 34.9 | 28.61 | 6.9 | 12.41 | 4.60 | 23.85 | 1101x471 |
| 2026-10-17 13:14 | 40 | MovingAndAnimated | Bone2 | CullUpdateTransforms | 44.4 | 22.53 | 43.0 | 4.89 | 3.65 | 22.02 | 1101x471 |
| 2026-10-17 13:18 | 40 | Static | Auto | CullUpdateTransforms | 41.7 | 24.01 | 31.4 | 3.72 | 4.59 | 23.14 | 1101x471 |
| 2026-10-17 13:18 | 40 | MovingNoAnimation | Auto | CullUpdateTransforms | 42.5 | 23.53 | 40.0 | 2.89 | 4.01 | 23.03 | 1101x471 |
| 2026-10-17 13:19 | 40 | MovingAndAnimated | Auto | CullUpdateTransforms | 45.4 | 22.03 | 44.1 | 7.12 | 3.94 | 21.57 | 1101x471 |
| 2026-10-17 13:19 | 40 | MovingAndAnimated | Bone2 | CullUpdateTransforms | 44.4 | 22.51 | 42.4 | 4.82 | 3.64 | 22.00 | 1101x471 |
| 2026-10-17 13:19 | 40 | MovingAndAnimated | Auto | CullUpdateTransforms | 44.1 | 22.67 | 41.9 | 5.08 | 3.77 | 22.14 | 1101x471 |
| 2026-10-17 13:19 | 40 | MovingAndAnimated | Bone2 | CullUpdateTransforms | 44.6 | 22.41 | 33.3 | 6.21 | 3.71 | 21.57 | 1101x471 |
