# Bird animation report

Written by **Tools > Agents > Build Bird Animations** on 2026-10-17 13:47. Every run overwrites this file.

## Clips

| Clip | Length (s) | Loops | Curves | Keys generated | Keys after reduction | Editor memory |
|---|---:|---|---:|---:|---:|---:|
| `A_Bird_Fly` | 0.50 | yes | 29 | 261 | 134 | 35.7 KB |
| `A_Bird_Hawk_Fly` | 0.50 | yes | 29 | 261 | 138 | 34.1 KB |
| `A_Bird_Glide` | 0.50 | yes | 29 | 261 | 79 | 30.3 KB |
| `A_Bird_Hawk_Glide` | 0.50 | yes | 29 | 261 | 78 | 30.6 KB |
| `A_Bird_Dive` | 0.50 | no | 30 | 60 | 60 | 22.6 KB |
| `A_Bird_Hawk_Dive` | 0.50 | no | 30 | 60 | 60 | 22.5 KB |
| `A_Bird_Perch` | 2.00 | yes | 30 | 270 | 73 | 32.4 KB |
| `A_Bird_Hawk_Perch` | 2.00 | yes | 30 | 270 | 73 | 32.4 KB |
| `A_Bird_Hit` | 0.30 | no | 16 | 64 | 60 | 20.7 KB |
| `A_Bird_Hawk_Hit` | 0.30 | no | 16 | 64 | 60 | 20.7 KB |
| `A_Bird_Death` | 0.80 | no | 29 | 116 | 100 | 30.0 KB |
| `A_Bird_Hawk_Death` | 0.80 | no | 29 | 116 | 100 | 29.9 KB |
| **Total** | | | | **2064** | **1015** (51% removed) | **342.1 KB** |

Editor memory = `Profiler.GetRuntimeMemorySizeLong` in the Editor; a build stores clips more compactly.

## Rest pose, gull vs hawk

Angle between each bone's rest local rotation on `SK_Bird` and on `SK_Bird_Hawk`. A clip keys absolute local
rotations, so one clip set on both birds would be off by this much; that is why the hawk has its own clips.

| Bone | Difference |
|---|---:|
| `Body` | 0.0° |
| `Head` | 4.8° |
| `Tail` | 5.9° |
| `Wing_L` | 34.2° |
| `Wing_R` | 34.2° |
| `Leg_L` | 1.3° |
| `Leg_R` | 1.3° |
