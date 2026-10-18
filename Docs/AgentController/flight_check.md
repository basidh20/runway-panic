# Flight check

Written by **Tools > Agents > Run Automated Check** on 2026-10-17 13:48: 13.4 s of flight in `Sandbox_Chamika`, with `TestPathFeeder` re-planning every bird's path every 5 s. Every run overwrites this file.

| Bird | Top speed (m/s) | Max bank (°) | Bank limit (°) | Max climb/dive (°) | Pitch limit (°) | Max turn rate (°/s) | Turn limit (°/s) | Largest frame move ÷ (top speed × frame time) |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| `Bird_Gull` | 6.0 | 30.0 | 30 | 14.0 | 35 | 142 | 140 | 1.00 |
| `Bird_Pigeon` | 4.0 | 35.0 | 35 | 10.2 | 40 | 260 | 260 | 1.00 |
| `Bird_Crow` | 5.5 | 35.0 | 35 | 20.3 | 35 | 178 | 170 | 1.00 |
| `Bird_Hawk` | 8.0 | 34.9 | 35 | 0.0 | 30 | 90 | 90 | 1.00 |

The last column stays at or below 1.00 when a bird never moves further in one frame than its top speed allows,
i.e. no jump or snap, including at the moments its path is replaced. Turn rate is measured on the drawn
(interpolated) rotation, so it can slightly exceed the limit when a frame is short.
