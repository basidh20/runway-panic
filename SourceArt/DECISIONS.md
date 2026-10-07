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

## 2026-10-07 — Bringing externally exported FBX files into the pipeline
- **Context:** The guns (pistol, AR, sniper, RPG) and the player arrived as FBX files exported with Blender's default settings. They had a 90° X rotation on every mesh, source names (`Pistol`, `RG_Polymer`, `MAT_Skin`), duplicated materials, and 3 of 5 files had meshes without UVs.
- **Options considered:** A) Copy the FBX files into `Assets/` and compensate the rotation with a parent in each prefab. B) Convert each one once into a `.blend` (`SourceArt/Tools/ingest_fbx.py`) and export it through `export_fbx.py` like every other asset.
- **Choice:** B.
- **Why:** One pipeline for every asset: same checks, same FBX preset, same import rules. The `.blend` becomes the source of truth for later fixes (UVs, polycount). A compensating parent would hide the problem and break the "rotation (0,0,0)" rule that gameplay code relies on. The script checks that applying the transform does not move the geometry.
- **Numbers:** bounding-box drift after the transform apply was **0.000000 m** for all 5 assets. Gun pivots were already at the grip and every muzzle faces −Y (checked on orthographic side renders). `Socket_Muzzle` was placed at the front-most vertices: Tier1 z 0.162 m, Tier2 0.711 m, Tier3 1.108 m, Tier4 1.040 m in front of the grip.

## 2026-10-07 — Flat-colour materials from a Blender manifest (palette atlas later)
- **Context:** The models use flat Principled BSDF colours (no textures): 15 unique gun materials and 49 player materials. Unity needs URP materials that match Blender, without each one being made by hand.
- **Options considered:** A) Let Unity embed a material per FBX (duplicates per model, Standard shader, pink in URP). B) Make 64 URP materials by hand. C) `export_fbx.py` writes a material manifest (JSON); `ArtMaterialBuilder.cs` creates shared URP Lit `M_` materials from it; the model importer maps slots to them by name. D) Bake everything into the shared palette atlas now.
- **Choice:** C now, D as the optimisation pass.
- **Why:** Colours stay authored in Blender (one source of truth) and are converted correctly (linear → sRGB, roughness → smoothness). Materials are shared: the four guns use 15 materials instead of 32, which helps the SRP Batcher. D needs proper UVs on every mesh first, so it fits better in the optimisation pass, and that pass gives a measurable before/after.
- **Numbers (before the atlas):** gun material slots: Tier1 7, Tier2 8, Tier3 10, Tier4 7 + 3 (rocket). Player: 21 meshes, **76 submeshes** (≈ 76 draw calls per pass). Ingest merged 17 duplicate gun materials and 1 duplicate player material (`MAT_Navy_Fabric__guns_tmp`).

## 2026-10-07 — Accepting over-budget models for the prototype
- **Context:** The player is **152,707 tris** against an 8,000 budget (19×). The sniper (Tier3) is **6,304 tris** against 2,500. 19 of 21 player meshes and 2 guns had no UVs, so they got a placeholder Smart UV Project map.
- **Options considered:** A) Import as-is and optimise later. B) Auto-decimate now. C) Hold the player back until it is reworked.
- **Choice:** A. The Unity import and `export_fbx.py` keep logging OVER BUDGET until it is fixed.
- **Why:** Teammates need the player and guns now (player controller, gun holding, animation). Auto-decimation would damage the topology, which is graded. A planned optimisation pass (delete faces hidden under clothes, reduce dense parts like boots 20,946 / shirt 19,918 / pants 16,070, proper UVs) is better viva evidence, with real before/after numbers.
- **Numbers:** to be filled after the optimisation pass (target ≤ 8,000 player, ≤ 2,500 per gun).
