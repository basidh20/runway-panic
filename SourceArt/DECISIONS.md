# Core Developer — Design Decisions Log

Evidence for the viva (Workflow & Tools, Design & Optimization Justification). One entry per decision, newest at the bottom.

Template:
```markdown
## <Date> — <Decision title>
- **Context:** what problem I was solving
- **Options considered:** A / B / C
- **Choice:** what I picked
- **Why:** visual, performance, or workflow reason
- **Numbers:** tri count / texture size / memory / fps impact, if any
```

Still to cover: tri + texture budgets in practice, one rig + prefab variants for 4 species, primitive colliders vs MeshCollider, texture compression + mipmaps, GPU instancing on birds, shared palette atlas vs per-asset textures.

---

## 2026-10-06 — Scale and axis convention
- **Context:** Models from Blender (Z-up, front = −Y) must line up with Unity (Y-up, forward = +Z) and with every other member's work: the level, the NavMesh agent height, the camera.
- **Options considered:** A) Model at any size and fix it with Unity's scale factor per asset. B) Agree one real-world scale up front: 1 Blender m = 1 Unity unit, and convert axes on export.
- **Choice:** B. Metric, unit scale 1.0, player 1.8 m. The model's front faces −Y in Blender and becomes +Z in Unity. Pivots: player at the feet, gun at the grip, bird at the centre of mass, props at the base.
- **Why:** Physics, NavMesh, lighting and particles all assume real-world metres, so a 1.8 m player keeps gravity and jump height believable. Per-asset scale factors drift between team members. With fixed pivots, prefabs snap to the ground and guns attach to the hand without offset hacks.
- **Numbers:** test cube `SM_Prop_TestCube` (1 m in Blender, origin at its base, a "nose" on the −Y side). In Unity it imported with bounds **1.000 × 1.000 × 1.000 m**, centre y = 0.5 (sits on the ground), and the nose at **z = +0.6**, so Blender's −Y front became Unity's +Z.

## 2026-10-06 — FBX export settings
- **Context:** Blender's FBX defaults produce objects with 100× scale or a −90° X rotation in Unity, which break colliders, child attachments and scripts that read `localScale`.
- **Options considered:** A) Apply Scalings "FBX Units Scale" (the plan's first draft). B) "FBX All" + Forward −Z / Up Y + Apply Transform.
- **Choice:** B, applied by a script (`SourceArt/Tools/export_fbx.py`), never by hand.
- **Why:** "FBX All" writes unit scale into the file so Unity's Convert Units lands on exactly 1. "Apply Transform" bakes the Z-up → Y-up conversion into the vertices, so the imported root has rotation (0,0,0) and scale (1,1,1). Leaf bones off and deform-bones-only remove bones the game never uses (fewer bones = cheaper skinning). Putting the preset in a script means the settings are identical every time and for anyone who exports.
- **Numbers:** exported with Blender 5.2 and imported in Unity 6000.6.4f1: root and child scale **(1, 1, 1)**, rotation **(0, 0, 0)**, file scale 1. Verified 6 Oct.

## 2026-10-06 — `.blend` files live outside `Assets/`
- **Context:** Unity can import `.blend` directly, but only by launching Blender in the background on every machine that opens the project.
- **Options considered:** A) `.blend` inside `Assets/` (Unity converts it). B) `.blend` in `SourceArt/`; only exported `.fbx` in `Assets/`.
- **Choice:** B.
- **Why:** With A, all four team members would need the exact same Blender version installed, or the project fails to import. Every `.blend` save (including scratch objects) would also trigger a re-import. FBX is a stable hand-off format: I control exactly what ships (hidden blockouts stay out), and teammates never need Blender. Source and exported files are both in Git LFS so the repo stays small.

## 2026-10-06 — Automated import settings (AssetPostprocessor)
- **Context:** Four people import assets. One wrong Inspector checkbox (Read/Write on, materials extracted, a camera imported, wrong rig type) is easy to miss and costs memory or breaks animation.
- **Options considered:** A) A written checklist everyone follows. B) An Import Preset. C) `AssetPostprocessor` scripts that enforce the rules by folder and name prefix.
- **Choice:** C. `ArtModelPostprocessor`, `ArtTexturePostprocessor`, with budgets in `ArtBudgets`. They re-apply on every import, and `GetVersion()` forces a re-import when the rules change.
- **Why:** The rules travel with the repo and cannot be forgotten. Prefix rules (`SM_` → no rig, `SK_Player` → Humanoid, `SK_Bird` → Generic) mean naming *is* configuration. Read/Write off halves mesh memory: there is no CPU copy. Materials are hand-made URP Lit assets instead of auto-generated duplicates, so every model shares the palette material. Budget warnings appear in the Console the moment an over-budget model is imported.
- **Numbers:** verified 6 Oct. A 4096² texture dropped into `Textures/Birds/` imports at 1024² (1/16 of the pixels). A palette texture imports at 256², Point filter, Clamp. A `_Mask` texture imports with sRGB off. Per-asset numbers will come from the Art Budget Checker report (Phase C).

## 2026-10-06 — Animation clips rebuilt from the FBX on every import
- **Context:** Unity stores clip settings (name, loop) in the `.meta`. If they are set once by hand, a new Blender action never shows up as a clip.
- **Options considered:** A) Set clips by hand in the Inspector. B) Set them on first import only. C) Rebuild every import from the FBX takes, with loop flags from the clip name.
- **Choice:** C. Names containing Fly/Glide/Idle/Walk/Run/… loop; Dive/Hit/Death play once. The `Armature|` prefix is stripped, and Mixamo's `mixamo.com` takes are renamed to the file name.
- **Why:** Blender stays the single source of truth. The Agent Controller gets stable clip names (`A_Bird_Fly`) to use in the Animator without any manual Unity step.
