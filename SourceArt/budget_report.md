# Art budget report

Generated 2026-10-13 20:21 by Tools > S3 > Art Budget Checker (Unity 6000.6.4f1, build target StandaloneWindows64).
Budgets: `ArtBudgets.cs` (same as `SourceArt/README.md` §4). Submeshes ≈ draw calls per render pass. Mesh GPU = vertex + index buffers (Read/Write off).

## Models

| Model | Tris | Budget | Verts | Submeshes | Materials | Bones | Mesh GPU | Result |
|---|---:|---:|---:|---:|---:|---:|---:|---|
| `SK_Bird` | 25,266 | - | 21,682 | 1 | 1 | 9 / 15 | 1,503.2 KB | ✅ OK |
| `SK_Bird_Hawk` | 28,204 | - | 26,371 | 1 | 1 | 9 / 15 | 1,813.4 KB | ✅ OK |
| `SK_Player_Officer` | 152,689 | - | 125,477 | 26 | 5 | 52 / - | 9,723.5 KB | ✅ OK |
| `SM_Gun_Tier1` | 820 | - | 1,294 | 1 | 1 | - | 65.5 KB | ✅ OK |
| `SM_Gun_Tier2` | 2,064 | - | 2,994 | 1 | 1 | - | 152.4 KB | ✅ OK |
| `SM_Gun_Tier3` | 6,304 | - | 7,556 | 1 | 1 | - | 391.1 KB | ✅ OK |
| `SM_Gun_Tier4` | 1,664 | - | 1,898 | 2 | 1 | - | 98.7 KB | ✅ OK |

**Totals:** 7 models, 217,011 tris, 33 submeshes, mesh GPU 13,747.8 KB.

## Textures

| Texture | Size | Max | Mips | Format | Memory | Result |
|---|---|---:|---:|---|---:|---|
| `T_Bird_Atlas_Albedo` | 256×256 | 1024 | 1 | RGBA_BC7_SRGB | 64.0 KB | ✅ OK |
| `T_Bird_Atlas_Mask` | 256×256 | 1024 | 1 | RGBA_BC7_UNorm | 64.0 KB | ✅ OK |
| `T_Bird_Crow_Atlas_Albedo` | 256×256 | 1024 | 1 | RGBA_BC7_SRGB | 64.0 KB | ✅ OK |
| `T_Bird_Crow_Atlas_Mask` | 256×256 | 1024 | 1 | RGBA_BC7_UNorm | 64.0 KB | ✅ OK |
| `T_Bird_Hawk_Atlas_Albedo` | 256×256 | 1024 | 1 | RGBA_BC7_SRGB | 64.0 KB | ✅ OK |
| `T_Bird_Hawk_Atlas_Mask` | 256×256 | 1024 | 1 | RGBA_BC7_UNorm | 64.0 KB | ✅ OK |
| `T_Bird_Pigeon_Atlas_Albedo` | 256×256 | 1024 | 1 | RGBA_BC7_SRGB | 64.0 KB | ✅ OK |
| `T_Player_Officer_Atlas_Albedo` | 512×512 | 1024 | 1 | RGBA_BC7_SRGB | 256.0 KB | ✅ OK |
| `T_Player_Officer_Atlas_Mask` | 512×512 | 1024 | 1 | RGBA_BC7_UNorm | 256.0 KB | ✅ OK |
| `T_Gun_Tier1_Atlas_Albedo` | 128×128 | 512 | 1 | RGBA_BC7_SRGB | 16.0 KB | ✅ OK |
| `T_Gun_Tier1_Atlas_Mask` | 128×128 | 512 | 1 | RGBA_BC7_UNorm | 16.0 KB | ✅ OK |
| `T_Gun_Tier2_Atlas_Albedo` | 128×128 | 512 | 1 | RGBA_BC7_SRGB | 16.0 KB | ✅ OK |
| `T_Gun_Tier2_Atlas_Mask` | 128×128 | 512 | 1 | RGBA_BC7_UNorm | 16.0 KB | ✅ OK |
| `T_Gun_Tier3_Atlas_Albedo` | 128×128 | 512 | 1 | RGBA_BC7_SRGB | 16.0 KB | ✅ OK |
| `T_Gun_Tier3_Atlas_Mask` | 128×128 | 512 | 1 | RGBA_BC7_UNorm | 16.0 KB | ✅ OK |
| `T_Gun_Tier4_Atlas_Albedo` | 128×128 | 512 | 1 | RGBA_BC7_SRGB | 16.0 KB | ✅ OK |
| `T_Gun_Tier4_Atlas_Mask` | 128×128 | 512 | 1 | RGBA_BC7_UNorm | 16.0 KB | ✅ OK |

**Total texture memory:** 1,088.0 KB.
