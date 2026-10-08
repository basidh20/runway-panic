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

## 2026-10-08 — Base bird: scripted rigid rig, one skinned mesh, pivot at the centre of mass
- **Context:** The base bird (`AirportBird_Base.fbx`, modelled outside the pipeline) arrived as 13 separate mesh parts under an Empty, with no armature. It has 25,266 tris, 13 flat materials, and 2 shape keys (`Wings_Spread`, `Tail_Spread`). Each part's origin was already placed at its joint (neck, shoulders, hips, ankles). The pipeline expects `SK_Bird` with a Generic rig of up to 15 bones, and the Agent Controller needs bones to animate flapping now.
- **Options considered (rig):** A) A script builds a simple rig now: each part bound rigidly to one bone, parts joined into one mesh. B) Import the parts unrigged as a static hierarchy (`SM_` name) and rig later. That renames the file, so the GUID changes and teammates' references break. C) Hand-rig in Blender first, which blocks the Agent Controller.
- **Options considered (pivot):** feet (where the source file had it) vs centre of mass.
- **Choice:** A, with the pivot at the **centre of mass** (centre of the body's bounding box), using `SourceArt/Tools/rig_bird.py`.
- **Why:** The bird is always flying. Path following rotates the bird around its pivot when it banks or pitches. With the pivot at the feet, every pitch would swing the body around its toes, and the IS waypoints would describe the feet instead of the body. Bone joints come from the modeller's own part origins, so the rig follows the model's design. Joining the parts gives **1 SkinnedMeshRenderer per bird instead of 12** (one skinning job, one renderer to cull). The neck uses a smooth Body/Head blend, so the head can turn without tearing the chest. The asset name `SK_Bird` stays fixed, so a later hand-made rig is a drop-in replacement.
- **Numbers:** 9 deform bones (budget 15), 13,405 verts, 25,266 tris, 13 material slots (13 submeshes). Transform apply drift **0.000000 m**. Every vertex's weights sum to 1 (0 bad of 13,405). Feet are 0.254 m below the pivot. A posed render (wings raised 50°, head turned 35°, legs tucked 60°) showed no detached parts once the 252 thigh-ruff vertices were moved from Body to the leg bones.
- **Known limits:** the wings are rigid (no elbow fold), and the tris are 17× over the 1,500 budget.

## 2026-10-08 — No triangle budget for the bird
- **Context:** The bird is 25,266 tris against the planned 1,500 (17×). Auto-decimation would damage the model, and a hand optimisation pass competes with the player work before Week 13.
- **Options considered:** A) Keep 1,500 and optimise later. B) Raise it to a new number (5,000 / 8,000). C) No triangle limit: the count is measured and reported, never flagged.
- **Choice:** C. The bone limit (15) stays. In `ArtBudgets.cs` the bird's `MaxTris` is 0 and in `export_fbx.py` it is `None`, meaning "no limit".
- **Why:** The detail is what the model is built around (separate feather details, shaped wings), and the game has to run on desktop PCs, not phones. Tris are still in the Budget Checker report, so the cost stays visible.
- **To defend it in the viva:** measure it rather than assume it. Record FPS and the Statistics panel (tris, batches) with the largest planned wave (e.g. 30 birds ≈ 760k tris). If the frame rate drops, LODs (Decimate copies at 50% / 20%, via `LODGroup`) are the fallback.

## 2026-10-08 — Four species from one mesh and one rig (Prefab Variants)
- **Context:** The game needs 4 bird species (one per team member for IS): **Gull, Pigeon, Crow, Hawk**. Each has to be recognisable at a distance, and the Agent Controller should animate only once.
- **Options considered:** A) Model 4 separate birds. B) One mesh + one rig, with 4 **Prefab Variants** of `P_Bird_Base` that differ in colour, scale and blend shapes. C) One prefab, recoloured at runtime by a script.
- **Choice:** B, built by `Tools > S3 > Build Bird Species Variants` (`ArtBirdSpeciesBuilder.cs`). Its colour/scale/shape table is the source of truth, and re-running it updates the variants in place.
- **Why:** One mesh in memory for all four species, and one rig, so every animation clip and Animator Controller works on all four. A component added to `P_Bird_Base` (AI, health) reaches every species automatically. Scale and silhouette make the species readable during gameplay, not just their colour. Option C would hide the variants from the editor and from the level designer.
- **Per species:**
  - Gull: scale 1.0, tail fan 0, base colours.
  - Pigeon: scale 0.6, tail fan 50, blue-grey with pink legs.
  - Crow: scale 0.8, tail fan 30, black and slightly glossier (smoothness 0.45).
  - Hawk: scale 1.2, tail fan 100, brown/cream with a yellow cere and legs.
  - All variants have `Wings_Spread` = 100, because birds are always flying.
- **Materials:** only the 11 coloured parts get a species copy (`Materials/Birds/Species/M_Bird_<Species>_<Part>`). Eye pupil and claws stay shared: 13 base materials + 33 species materials, instead of 52 if every slot were copied.
- **GPU instancing:** the plan suggested it, but Unity **cannot GPU-instance a SkinnedMeshRenderer**, so it is left off. URP's **SRP Batcher** batches the birds instead: all bird materials use the same URP Lit shader variant, which keeps draw-call setup cheap even with 13 submeshes per bird.
- **Numbers:** wingspan with wings spread: gull 1.66 m, pigeon ≈ 1.0 m, crow ≈ 1.33 m, hawk ≈ 2.0 m. One shared mesh: 25,266 tris, 13,405 verts.
