# Art budget report

Generated 2026-10-11 12:08 by Tools > S3 > Art Budget Checker (Unity 6000.6.4f1, build target StandaloneWindows64).
Budgets: `ArtBudgets.cs` (same as `SourceArt/README.md` §4). Submeshes ≈ draw calls per render pass. Mesh GPU = vertex + index buffers (Read/Write off).

## Models

| Model | Tris | Budget | Verts | Submeshes | Materials | Bones | Mesh GPU | Result |
|---|---:|---:|---:|---:|---:|---:|---:|---|
| `SK_Bird` | 25,266 | - | 21,480 | 13 | 13 | 9 / 15 | 1,490.5 KB | ✅ OK |
| `SK_Bird_Hawk` | 28,204 | - | 23,773 | 14 | 14 | 9 / 15 | 1,651.1 KB | ✅ OK |
| `SK_Player_Officer` | 152,689 | 8,000 | 121,605 | 76 | 49 | 52 / - | 9,417.4 KB | ❌ 19.1× over |
| `SM_Gun_Tier1` | 820 | 2,500 | 934 | 7 | 7 | - | 48.6 KB | ✅ OK |
| `SM_Gun_Tier2` | 2,064 | 2,500 | 2,296 | 8 | 8 | - | 119.7 KB | ✅ OK |
| `SM_Gun_Tier3` | 6,304 | 2,500 | 6,680 | 10 | 10 | - | 350.1 KB | ❌ 2.5× over |
| `SM_Gun_Tier4` | 1,664 | 2,500 | 2,000 | 10 | 7 | - | 103.5 KB | ✅ OK |

**Totals:** 7 models, 217,011 tris, 138 submeshes, mesh GPU 13,180.9 KB.

## Textures

No textures under `Art/Textures/` yet (models use flat-colour materials).
