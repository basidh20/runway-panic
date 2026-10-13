# Texture compression report

Generated 2026-10-13 20:54 by Tools > S3 > Texture Compression Test (Unity 6000.6.4f1, Direct3D11).
Each `_Atlas_` PNG is compressed in memory (`EditorUtility.CompressTexture`, quality Normal), decoded by the GPU and compared with the PNG texel by texel, as stored bytes (0-255). No mipmaps, as imported.
Changed = any channel off by more than 2. **On faces** = texels that a UV triangle of a prefab using the texture overlaps: only these can be drawn. The other texels are island padding and unused background.
The uncompressed rows are the control: they must show 0 everywhere, or the read-back itself is wrong.

## Totals

| Option | Memory, all atlas textures | Changed texels, all | Changed texels on faces | Largest error on faces |
|---|---:|---:|---:|---:|
| Uncompressed (RGBA32) | 4,352.0 KB | 0 (0.00%) | 0 (0.00%) | 0 |
| Normal quality (BC1, BC3 with alpha) | 800.0 KB | 167,219 (15.01%) | 59,296 (10.64%) | 150 |
| High quality (BC7) | 1,088.0 KB | 4,397 (0.39%) | 1,164 (0.21%) | 31 |

## Per texture

| Texture | Size | Texels on faces | Option | Memory | Mean error, all | Largest error, all | Changed, all | Changed on faces | Largest error on faces | Worst texel on faces: PNG -> decoded (R,G,B,A) |
|---|---|---:|---|---:|---:|---:|---:|---:|---:|---|
| `T_Bird_Atlas_Albedo` | 256² | 32,161 (49.07%) | Uncompressed (RGBA32) | 256.0 KB | 0.000 | 0 | 0 (0.00%) | 0 (0.00%) | 0 | - |
| `T_Bird_Atlas_Albedo` | 256² | 32,161 (49.07%) | Normal quality (BC1) | 32.0 KB | 1.195 | 185 | 18,064 (27.56%) | 6,245 (19.42%) | 83 | 120,127,140,255 -> 203,189,156,255 |
| `T_Bird_Atlas_Albedo` | 256² | 32,161 (49.07%) | High quality (BC7) | 64.0 KB | 0.040 | 49 | 107 (0.16%) | 29 (0.09%) | 31 | 229,229,224,255 -> 247,198,215,255 |
| `T_Bird_Atlas_Mask` | 256² | 32,161 (49.07%) | Uncompressed (RGBA32) | 256.0 KB | 0.000 | 0 | 0 (0.00%) | 0 (0.00%) | 0 | - |
| `T_Bird_Atlas_Mask` | 256² | 32,161 (49.07%) | Normal quality (BC3) | 64.0 KB | 0.004 | 11 | 171 (0.26%) | 74 (0.23%) | 9 | 0,0,0,71 -> 0,0,0,62 |
| `T_Bird_Atlas_Mask` | 256² | 32,161 (49.07%) | High quality (BC7) | 64.0 KB | 0.001 | 3 | 1 (0.00%) | 0 (0.00%) | 1 | 0,0,0,41 -> 0,0,0,40 |
| `T_Bird_Crow_Atlas_Albedo` | 256² | 32,161 (49.07%) | Uncompressed (RGBA32) | 256.0 KB | 0.000 | 0 | 0 (0.00%) | 0 (0.00%) | 0 | - |
| `T_Bird_Crow_Atlas_Albedo` | 256² | 32,161 (49.07%) | Normal quality (BC1) | 32.0 KB | 0.723 | 23 | 19,057 (29.08%) | 6,747 (20.98%) | 7 | 27,27,30,255 -> 27,34,33,255 |
| `T_Bird_Crow_Atlas_Albedo` | 256² | 32,161 (49.07%) | High quality (BC7) | 64.0 KB | 0.026 | 2 | 0 (0.00%) | 0 (0.00%) | 1 | 30,31,35,255 -> 30,31,34,255 |
| `T_Bird_Crow_Atlas_Mask` | 256² | 32,161 (49.07%) | Uncompressed (RGBA32) | 256.0 KB | 0.000 | 0 | 0 (0.00%) | 0 (0.00%) | 0 | - |
| `T_Bird_Crow_Atlas_Mask` | 256² | 32,161 (49.07%) | Normal quality (BC3) | 64.0 KB | 0.000 | 1 | 0 (0.00%) | 0 (0.00%) | 0 | - |
| `T_Bird_Crow_Atlas_Mask` | 256² | 32,161 (49.07%) | High quality (BC7) | 64.0 KB | 0.000 | 2 | 0 (0.00%) | 0 (0.00%) | 0 | - |
| `T_Bird_Hawk_Atlas_Albedo` | 256² | 29,627 (45.21%) | Uncompressed (RGBA32) | 256.0 KB | 0.000 | 0 | 0 (0.00%) | 0 (0.00%) | 0 | - |
| `T_Bird_Hawk_Atlas_Albedo` | 256² | 29,627 (45.21%) | Normal quality (BC1) | 32.0 KB | 1.285 | 150 | 19,122 (29.18%) | 7,117 (24.02%) | 150 | 229,217,191,255 -> 247,186,41,255 |
| `T_Bird_Hawk_Atlas_Albedo` | 256² | 29,627 (45.21%) | High quality (BC7) | 64.0 KB | 0.036 | 28 | 183 (0.28%) | 53 (0.18%) | 7 | 229,217,191,255 -> 231,214,198,255 |
| `T_Bird_Hawk_Atlas_Mask` | 256² | 29,627 (45.21%) | Uncompressed (RGBA32) | 256.0 KB | 0.000 | 0 | 0 (0.00%) | 0 (0.00%) | 0 | - |
| `T_Bird_Hawk_Atlas_Mask` | 256² | 29,627 (45.21%) | Normal quality (BC3) | 64.0 KB | 0.004 | 10 | 171 (0.26%) | 48 (0.16%) | 10 | 0,0,0,173 -> 0,0,0,163 |
| `T_Bird_Hawk_Atlas_Mask` | 256² | 29,627 (45.21%) | High quality (BC7) | 64.0 KB | 0.001 | 4 | 9 (0.01%) | 0 (0.00%) | 1 | 0,0,0,173 -> 0,0,0,174 |
| `T_Bird_Pigeon_Atlas_Albedo` | 256² | 32,161 (49.07%) | Uncompressed (RGBA32) | 256.0 KB | 0.000 | 0 | 0 (0.00%) | 0 (0.00%) | 0 | - |
| `T_Bird_Pigeon_Atlas_Albedo` | 256² | 32,161 (49.07%) | Normal quality (BC1) | 32.0 KB | 0.864 | 139 | 16,034 (24.47%) | 5,792 (18.01%) | 61 | 201,86,106,255 -> 140,116,129,255 |
| `T_Bird_Pigeon_Atlas_Albedo` | 256² | 32,161 (49.07%) | High quality (BC7) | 64.0 KB | 0.043 | 14 | 47 (0.07%) | 10 (0.03%) | 11 | 154,163,176,255 -> 165,162,176,255 |
| `T_Player_Officer_Atlas_Albedo` | 512² | 122,893 (46.88%) | Uncompressed (RGBA32) | 1,024.0 KB | 0.000 | 0 | 0 (0.00%) | 0 (0.00%) | 0 | - |
| `T_Player_Officer_Atlas_Albedo` | 512² | 122,893 (46.88%) | Normal quality (BC1) | 128.0 KB | 1.007 | 132 | 63,042 (24.05%) | 19,313 (15.72%) | 121 | 231,192,170,255 -> 165,199,49,255 |
| `T_Player_Officer_Atlas_Albedo` | 512² | 122,893 (46.88%) | High quality (BC7) | 256.0 KB | 0.062 | 39 | 2,117 (0.81%) | 453 (0.37%) | 21 | 162,200,39,255 -> 183,179,36,255 |
| `T_Player_Officer_Atlas_Mask` | 512² | 122,893 (46.88%) | Uncompressed (RGBA32) | 1,024.0 KB | 0.000 | 0 | 0 (0.00%) | 0 (0.00%) | 0 | - |
| `T_Player_Officer_Atlas_Mask` | 512² | 122,893 (46.88%) | Normal quality (BC3) | 256.0 KB | 0.049 | 15 | 7,503 (2.86%) | 2,378 (1.94%) | 15 | 0,0,0,107 -> 0,0,0,122 |
| `T_Player_Officer_Atlas_Mask` | 512² | 122,893 (46.88%) | High quality (BC7) | 256.0 KB | 0.015 | 10 | 761 (0.29%) | 178 (0.14%) | 7 | 0,0,0,31 -> 0,0,0,24 |
| `T_Gun_Tier1_Atlas_Albedo` | 128² | 14,062 (85.83%) | Uncompressed (RGBA32) | 64.0 KB | 0.000 | 0 | 0 (0.00%) | 0 (0.00%) | 0 | - |
| `T_Gun_Tier1_Atlas_Albedo` | 128² | 14,062 (85.83%) | Normal quality (BC1) | 8.0 KB | 0.750 | 26 | 1,989 (12.14%) | 1,246 (8.86%) | 26 | 7,7,7,255 -> 33,32,33,255 |
| `T_Gun_Tier1_Atlas_Albedo` | 128² | 14,062 (85.83%) | High quality (BC7) | 16.0 KB | 0.020 | 2 | 0 (0.00%) | 0 (0.00%) | 1 | 29,29,30,255 -> 29,29,31,255 |
| `T_Gun_Tier1_Atlas_Mask` | 128² | 14,062 (85.83%) | Uncompressed (RGBA32) | 64.0 KB | 0.000 | 0 | 0 (0.00%) | 0 (0.00%) | 0 | - |
| `T_Gun_Tier1_Atlas_Mask` | 128² | 14,062 (85.83%) | Normal quality (BC3) | 16.0 KB | 0.159 | 41 | 910 (5.55%) | 473 (3.36%) | 26 | 128,0,0,128 -> 154,0,0,123 |
| `T_Gun_Tier1_Atlas_Mask` | 128² | 14,062 (85.83%) | High quality (BC7) | 16.0 KB | 0.015 | 15 | 82 (0.50%) | 43 (0.31%) | 4 | 230,0,0,166 -> 227,0,0,170 |
| `T_Gun_Tier2_Atlas_Albedo` | 128² | 10,920 (66.65%) | Uncompressed (RGBA32) | 64.0 KB | 0.000 | 0 | 0 (0.00%) | 0 (0.00%) | 0 | - |
| `T_Gun_Tier2_Atlas_Albedo` | 128² | 10,920 (66.65%) | Normal quality (BC1) | 8.0 KB | 0.728 | 31 | 2,611 (15.94%) | 1,399 (12.81%) | 31 | 56,75,97,255 -> 57,59,66,255 |
| `T_Gun_Tier2_Atlas_Albedo` | 128² | 10,920 (66.65%) | High quality (BC7) | 16.0 KB | 0.029 | 2 | 0 (0.00%) | 0 (0.00%) | 1 | 48,48,50,255 -> 48,48,50,254 |
| `T_Gun_Tier2_Atlas_Mask` | 128² | 10,920 (66.65%) | Uncompressed (RGBA32) | 64.0 KB | 0.000 | 0 | 0 (0.00%) | 0 (0.00%) | 0 | - |
| `T_Gun_Tier2_Atlas_Mask` | 128² | 10,920 (66.65%) | Normal quality (BC3) | 16.0 KB | 0.231 | 33 | 3,458 (21.11%) | 1,528 (13.99%) | 30 | 217,0,0,148 -> 247,0,0,148 |
| `T_Gun_Tier2_Atlas_Mask` | 128² | 10,920 (66.65%) | High quality (BC7) | 16.0 KB | 0.052 | 36 | 184 (1.12%) | 78 (0.71%) | 16 | 217,0,0,148 -> 219,0,0,164 |
| `T_Gun_Tier3_Atlas_Albedo` | 128² | 9,053 (55.26%) | Uncompressed (RGBA32) | 64.0 KB | 0.000 | 0 | 0 (0.00%) | 0 (0.00%) | 0 | - |
| `T_Gun_Tier3_Atlas_Albedo` | 128² | 9,053 (55.26%) | Normal quality (BC1) | 8.0 KB | 1.142 | 41 | 4,358 (26.60%) | 1,930 (21.32%) | 31 | 56,75,97,255 -> 74,81,66,255 |
| `T_Gun_Tier3_Atlas_Albedo` | 128² | 9,053 (55.26%) | High quality (BC7) | 16.0 KB | 0.053 | 6 | 18 (0.11%) | 5 (0.06%) | 4 | 36,36,39,255 -> 35,35,35,255 |
| `T_Gun_Tier3_Atlas_Mask` | 128² | 9,053 (55.26%) | Uncompressed (RGBA32) | 64.0 KB | 0.000 | 0 | 0 (0.00%) | 0 (0.00%) | 0 | - |
| `T_Gun_Tier3_Atlas_Mask` | 128² | 9,053 (55.26%) | Normal quality (BC3) | 16.0 KB | 0.509 | 49 | 4,505 (27.50%) | 1,922 (21.23%) | 42 | 128,0,0,128 -> 170,0,0,128 |
| `T_Gun_Tier3_Atlas_Mask` | 128² | 9,053 (55.26%) | High quality (BC7) | 16.0 KB | 0.140 | 27 | 710 (4.33%) | 234 (2.58%) | 27 | 128,0,0,128 -> 128,0,0,101 |
| `T_Gun_Tier4_Atlas_Albedo` | 128² | 11,800 (72.02%) | Uncompressed (RGBA32) | 64.0 KB | 0.000 | 0 | 0 (0.00%) | 0 (0.00%) | 0 | - |
| `T_Gun_Tier4_Atlas_Albedo` | 128² | 11,800 (72.02%) | Normal quality (BC1) | 8.0 KB | 0.765 | 60 | 4,557 (27.81%) | 2,339 (19.82%) | 29 | 111,118,80,255 -> 140,105,66,255 |
| `T_Gun_Tier4_Atlas_Albedo` | 128² | 11,800 (72.02%) | High quality (BC7) | 16.0 KB | 0.011 | 2 | 0 (0.00%) | 0 (0.00%) | 2 | 56,75,97,255 -> 55,75,95,255 |
| `T_Gun_Tier4_Atlas_Mask` | 128² | 11,800 (72.02%) | Uncompressed (RGBA32) | 64.0 KB | 0.000 | 0 | 0 (0.00%) | 0 (0.00%) | 0 | - |
| `T_Gun_Tier4_Atlas_Mask` | 128² | 11,800 (72.02%) | Normal quality (BC3) | 16.0 KB | 0.242 | 42 | 1,667 (10.17%) | 745 (6.31%) | 37 | 128,0,0,128 -> 165,0,0,128 |
| `T_Gun_Tier4_Atlas_Mask` | 128² | 11,800 (72.02%) | High quality (BC7) | 16.0 KB | 0.034 | 11 | 178 (1.09%) | 81 (0.69%) | 6 | 51,0,0,128 -> 52,0,0,122 |
