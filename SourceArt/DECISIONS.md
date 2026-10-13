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

Still to cover: tri + texture budgets in practice, one rig + prefab variants for 4 species, primitive colliders vs MeshCollider, measured texture compression table (BC1 / BC7 / uncompressed), GPU instancing on birds.

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

## 2026-10-11 — The hawk gets its own model (same rig layout)
- **Context:** The recoloured gull did not read as a hawk: a gull's head, beak and wing shape stay recognisable whatever the colours. The hawk was remodelled separately (`AirportBird_Hawk_Head_Refined.blend`): 14 parts, including separate primary feathers, 28,204 tris, and its own 14 materials.
- **Options considered:** A) Keep recolouring the gull. B) Make the hawk a Prefab Variant of `P_Bird_Base` with the mesh swapped. C) Rig the new hawk with the same tool and bone names, as its own prefab `P_Bird_Hawk`.
- **Choice:** C. `rig_bird.py` was generalised: `--source` (`.fbx` or `.blend`), `--asset`, `--material-prefix`, part names with any prefix (`Hawk_Body`), and optional `Primary_L/R` parts that ride on the wing bones.
- **Why:** The silhouette (hooked beak, broad barred wings and tail) is what makes a species readable in a fast shooter; colour alone was not enough. B is fragile: the importer puts the Animator on the model child, so swapping that child also drops the controller. With C, the same 9 bone names, the same mesh name (`Bird_Mesh`) and the same blend shapes mean the Agent Controller's clips bind to the hawk unchanged. The cost is a second mesh in memory (28,204 tris), and the Animator Controller has to be assigned in two prefabs instead of one.
- **Numbers:** transform drift 0.000000 m; 16,433 verts, each with weights summing to 1; 652 thigh-ruff vertices follow the legs. Hawk: 0.56 m long, 1.51 m wingspan, feet 0.238 m below the pivot (gull: 0.60 m, 1.66 m). The 11 recolour materials and the old variant were deleted, so there are now 22 species materials (pigeon + crow).
- **Bug found and fixed:** objects appended from a `.blend` have no world matrix until the scene updates, so on the first run every joint landed at the origin. `rig_bird.py` now updates the view layer after appending. The gull re-rig gave identical joints, pivot and tris (regression check).

## 2026-10-11 — No triangle limits on any model
- **Context:** The baseline report (`SourceArt/budget_report.md`, 11 Oct) shows the player at **152,689 tris** (70% of all tris in the game, 71% of mesh memory, 9.4 MB) against 8,000, and the sniper at 6,304 against 2,500. `SourceArt/Tools/hidden_faces_report.py` (read-only) found **~49,700 player tris (33%) hidden** under other parts: Body 69%, Hair 55% (under the cap), Shirt 50% (under the vest), Safety_Vest 47%, Head 39%. Reaching 8,000 would need a near-full retopology during the Week 13 prototype window.
- **Options considered:** A) Keep 8,000 (retopology). B) Raise it to 25,000 (delete hidden faces + Decimate). C) No triangle limit on any model: counts are reported, never flagged. The birds already worked this way since 8 Oct.
- **Choice:** C, for the player, guns and props. The birds' 15-bone limit stays.
- **Why:** My call, to keep the time for modelling quality and UVs (which are graded) instead of polygon reduction. The game targets desktop PCs.
- **How I keep it defensible:** every count is still measured (Art Budget Checker / `budget_report.md`). `hidden_faces_report.py` shows exactly where the waste is. An FPS + Stats panel test with the player and the largest bird wave on screen is the evidence that performance is acceptable without limits. If that test fails, deleting the hidden player faces is the first, zero-visual-cost fix (−33%).
- **Numbers:** whole game 217,011 tris, 138 submeshes, 13.2 MB mesh memory (baseline report, 11 Oct).
- **FPS test** (`SourceArt/perf_report.md`, Tools > S3 > Performance Test; Editor Play mode, 1920×1080, VSync off; Ryzen 5 4600H + RTX 3050 Laptop):

  | Scene | Unique tris | Tris drawn | Draw calls | SetPass | Avg FPS | Lowest FPS (0.5 s) |
  |---|---:|---:|---:|---:|---:|---:|
  | Player + 40 birds (10 per species) | ~1.19 M | 5.02 M | 2,595 | 38 | **95.0** | 69.0 |
  | Player + 100 birds (25 per species) | ~2.75 M | 11.61 M | 5,950 | 38 | **78.4** | 57.9 |

- **Result:** with 2.5× more birds than a realistic wave, the editor (slower than a build) still averages 78 FPS. Unlimited triangles are acceptable on this hardware, so the decision stands.
- **What the test revealed:** every triangle and submesh is drawn **~4.2 times** (5.02 M drawn / 1.19 M unique; 2,595 draw calls / 606 submeshes). That's the shadow-map cascades plus the main pass. The SRP Batcher handles **2,549 of the 2,595 draw calls**, and only **38 SetPass calls** remain, because all models share one URP Lit shader variant. This confirms the "SRP Batcher instead of GPU instancing" decision.
- **If performance must improve, in order of payoff:** (1) fewer submeshes per bird: the palette atlas takes 13–14 materials down to 1–2, and draw calls scale with it; (2) birds far away stop casting shadows, or fewer shadow cascades (shared URP asset → ask the World Builder); (3) delete the hidden player faces (−33% player tris); (4) LODs on birds.

## 2026-10-11 — Pistol (SM_Gun_Tier1) UV unwrap: scripted box-net seams on the big parts, Smart UV on the small ones
- **Context:** `ingest_fbx.py` gave the pistol a placeholder UV map. All 42 boxes of the pistol used the same default cube layout, so **42 faces were stacked on each spot** and **346 of 346 faces overlapped**. A texture or a baked palette would repeat on every part, and texel density varied by a factor of ~6,500 (area ratio 0.05–302.6).
- **Options considered:** A) Smart UV Project on the whole gun. B) Box-net seams on every part. C) Box-net seams on the parts the player actually sees, Smart UV Project on the tiny parts, then one shared scale and pack.
- **Choice:** C, in `Guns.blend` (the pipeline file, not the raw FBX).
- **How it was done (tooling):** the seams were **placed by a script**, `SourceArt/Tools/uv/unwrap_pistol.py`, not by hand in the Blender UI. The script applies the seam rules below to each part, then runs Blender's own Unwrap, Smart UV Project, Average Islands Scale and Pack Islands. The rules are the method; the script applies them consistently to all 47 parts.
  - **10 big parts, box-net seams** (slide, frame front/rear, beavertail, grip, 2 grip panels, magazine, trigger guard bar + front post): the 4 edges around each end cap + **1 lengthwise edge on the least visible side**. Each part unwraps to one long strip + 2 end caps.
    - Slide and frame: lengthwise seam on the **bottom**, where they sit on each other. **No seam crosses the top of the slide**, the most visible surface in third person.
    - Grip: end caps are hidden under the frame and the magazine; lengthwise seam on the front strap edge, under the fingers.
    - Grip panels: seam on the edge that faces into the grip. Magazine: seam on top, inside the grip. Trigger guard: seams on the inside of the guard.
  - **5 cylinders** (barrel, bore, 3 sight dots): rings at both ends + one seam along the bottom → a rectangle + 2 circles.
  - **32 small boxes** (sight blades, pins, serrations, trigger, slide lock…): Smart UV Project, angle 66°, island margin 0.02. One clean island per face with no stretch, which is fine for small hard-surface parts.
  - Then **Average Islands Scale** and **Pack Islands** (rotate on, margin 0.01).
- **Why:** The slide, frame and grip fill most of the screen, so they get large, unbroken islands with their seams hidden. Fewer, bigger islands also mean fewer visible colour breaks when the palette atlas is baked. Hand-cutting 40 tiny boxes would teach nothing new and would not change what the player sees. Averaging the scale gives every part the same pixel density, so the slide isn't blurrier than a pin.
- **Numbers** (measured with `SourceArt/Tools/uv/uvmetrics.py` on the mesh and on the exported FBX; re-checked independently with a separate rasteriser: 0 overlapping faces, 0 faces stretched > 2°, geometry / materials / sockets byte-identical):

  | | Before | After |
  |---|---:|---:|
  | Overlapping faces | 346 / 346 | **0 / 346** |
  | Most faces on one UV spot | 42 | **1** |
  | Islands | 57 | 237 |
  | Marked seams | 0 | 263 |
  | Angle stretch (worst corner) | 0.0° (boxes mapped to squares keep their right angles; the damage was in area) | 0.01° (all 346 faces "blue") |
  | Texel density spread between islands (CV) | 102 % | **0.0 %** |
  | UV area / 3D area ratio, min–max | 0.05–302.6 | **1.00–1.00** |
  | UV space used | — (overlapping) | 71.4 % |

  Geometry, materials and the other three guns' UVs are unchanged (verified by hash). Export: 820 tris, UVMap present, no errors. The 7-material-slot warning stays until the palette atlas pass.
- **Evidence:** `Evidence/UV/SM_Gun_Tier1_UV_before.png` / `_after.png` (Blender UV → Export UV Layout), `SM_Gun_Tier1_UV_before_after.png` (islands coloured by method), `SM_Gun_Tier1_checker_before_after.png` (checker texture rendered on the gun).

## 2026-10-11 — AR, sniper and RPG UV unwrap (same method as the pistol)
- **Context:** The other three guns still had the `ingest_fbx.py` placeholder UVs. The AR used the default cube layout (**792 of 792 faces overlapping, 80 faces on one spot**). The RPG had **389 of 500 faces overlapping** and 38% texel-density spread. The sniper's Smart UV placeholder didn't overlap, but **810 of its 2,539 faces were stretched** (worst corner 21°).
- **Choice:** the pistol rules, applied to every part by `SourceArt/Tools/uv/unwrap_generic.py` (scripted, like the pistol). Unlike the pistol, these guns also have bevelled boxes, extruded profiles (stock, grip), cones and spheres, so the script adds the extra rules below.
  - **Every part ≥ 4 cm, and every round part:** rings around both end caps + one lengthwise cut on the least visible side: the bottom for horizontal parts, the front for vertical ones (grip, magazine, bipod legs), the inner face for parts mounted on the side.
  - **Curved corners:** a patch only flattens without stretch if the face angles around each vertex inside it add up to 360°. Bevel corners, cones and the stock's bevelled outline break that, so a short seam runs from each such vertex to the nearest seam, preferring hidden edges.
  - **Spheres** (bipod feet, bolt knob): a sphere can never be flattened without stretch, so they're cut like an orange peel, pole to pole along every meridian.
  - **Parts that folded over themselves after unwrapping** (stock, trigger, bolt release): split at their sharpest edges (> 40°, then 30°, then 20°) and unwrapped again.
  - **Small plain boxes** (< 4 cm): Smart UV Project, 66°, margin 0.02.
  - **Each gun:** Average Islands Scale + Pack Islands (rotate, margin 0.01). **The RPG and its rocket share one UV square**, because they ship in the same FBX.
- **Numbers** (measured with `uvmetrics.py` on the mesh and on the re-imported FBX; independent re-check: 0 overlapping faces on every gun, stretched faces > 2°: AR 0, RPG 0, rocket 0, sniper 23):

  | Gun | Overlapping faces before → after | Stretch-free faces before → after | Texel-density spread before → after | Islands | Seams |
  |---|---:|---:|---:|---:|---:|
  | Tier1 pistol | 346/346 → **0** | 346 → 346 / 346 | 102% → **0%** | 237 | 263 |
  | Tier2 AR | 792/792 → **0** | 792 → 792 / 792 | 55% → **0%** | 465 | 741 |
  | Tier3 sniper | 0 → **0** | 1,729 → **2,516** / 2,539 | 2.7% → **0%** | 557 | 3,273 |
  | Tier4 RPG + rocket | 389/500 → **0** | 444 → **500** / 500 | 38% → **0%** | 96 | 880 |

  ("Stretch-free" = every corner angle within 2° of the 3D angle, shown blue in Display Stretch.)
- **What's left:** 23 sniper faces (0.9%) keep slight stretch, worst 5.9°. They're on the two 1.8 cm rubber bipod feet, two faces of the stock, and the trigger. Nobody can see that at game scale, and more seams there would only add islands.
- **Trade-off:** the sniper uses only 34% of its UV square, because it has many tiny islands, each with a margin. That's fine for the shared 256² palette, where each part samples one flat colour. A painted texture would need the small parts scaled down first.
- **Unchanged:** geometry, materials, sockets and pivots (verified by hash). Export: 820 / 2,064 / 6,304 / 1,664 tris, UVMap on every mesh, no errors. The only warnings are the material-slot ones, which stay until the palette atlas pass.
- **Evidence:** `Evidence/UV/<asset>_UV_before.png` / `_after.png` (Blender UV → Export UV Layout), `<asset>_UV_before_after.png` (islands coloured by method), `<asset>_checker_before_after.png` (checker texture rendered on the gun), `uv_report.json` (all the numbers).
- **Viva note:** the UVs were produced with scripted rules, not by hand. To be able to show the method live, I unwrap at least one part by hand in Blender: mark a box-net seam ring, Unwrap, check Display Stretch.

## 2026-10-12 — Player (SK_Player_Officer) + bird UV unwrap: scripted seam rules
- **Context:** All 21 skinned meshes of `SK_Player_Officer.blend` (152,707 tris, 83,290 faces, 49 flat-colour materials) had placeholder UVs: each mesh used the whole 0–1 square on its own, so in the one UV space they share, **78,431 of 83,290 faces overlapped** and up to 4,328 faces were stacked on one spot. Seven meshes also overlapped themselves (Pants 948 faces, Shirt 165, Body 128, Cap 38, Boots 30, Head_Mesh 26, Gloves 3), and texel density varied 156% between islands. A future atlas or texture bake for the player would paint every mesh on top of the others.
- **Options considered:** A) Smart UV Project on every mesh plus one pack. Quick, but it cuts by face angle, so seams land on the front of the face and torso, and the organic meshes become hundreds of flat chips. B) Hand-cut seams in the Blender UI. The usual way, but too slow for 21 meshes and ~152k tris in the Week 13 window, and hard to keep consistent. C) Scripted seam rules (the gun method): rules for where seams go, applied the same way to every mesh, then Blender's own Unwrap, Average Islands Scale and Pack Islands.
- **Choice:** C, in `SourceArt/Tools/uv/unwrap_player.py`.
- **How it was done (tooling):** the seams were **placed by a script**, not by hand in the Blender UI. The script was written by an AI coding agent from a written brief, and its result was re-checked independently on 13 Oct (see "Independent re-check" below). The script applies the rules below to each mesh, then runs Blender's own operators. The rules are the method; the script applies them consistently to all 21 meshes.
  1. **Material boundaries are seams** (1,238 edges). The script also cuts every edge shared by 3+ faces (8) and every edge between two faces wound in opposite directions (22), because those would fold.
  2. **Sharp edges > 60° are seams** on the hard-surface meshes: Boots, Cap, Headset, Radio, Knee_Pads, both pouch meshes, Pouch_Clips, Belt, Belt_Buckle, Belt_Loops, ID_Badge (11,885 edges).
  3. **Cuts on the least visible side.** Every patch that isn't a flat disk yet (whole organic meshes; tubes, closed shells and patches with holes) is cut open along the edges a camera is least likely to see. A visibility map (rays from 48 directions at the fully assembled character, weighted front > sides > back > underneath) gives every face a visibility value, so the cuts fall inside arms and legs, under the vest and the cap, at the back and under the chin without hard-coding a side per mesh. Faces it marks as hidden: Body 74% and Hair 76% (`hidden_faces_report.py`, which uses a different test, found 69% and 55%). Cutting method: a spanning tree that prefers to cross visible edges (tree-cotree); the leftover, least visible edges become the seam (5,970 edges, 190 closed shells slit open).
     - Each patch is then unwrapped (angle based; conformal is tried too and kept when it measures better: Belt, Belt_Buckle, Belt_Loops, Cap, ID_Badge, Pouch_Clips).
     - A patch that is still stretched (more than 25% of its faces off by > 10°, or area ratio spread > ×2) or that folds is cut again along the cheapest hidden border into two balanced halves. Small folded patches first get each saddle vertex (corner angles adding up to more than 360°) cut loose along a short hidden path, then their sharpest creases. This repeats until every patch passes (14,504 more seam edges).
  4. **Smart UV fallback** (66°, margin 0.02, per mesh) for any mesh still folded or overlapping after rule 3: **no mesh needed it** in the final run. During development, Pants, Head_Mesh, Cap, Gloves and Safety_Vest needed it until the balanced split and the saddle cuts were added.
  5. **Eyes:** planar projection along Y (front hemisphere seen from the front, back hemisphere from behind, cut at the equator), so the iris and pupil read as circles. 6 islands per eye, no overlaps.
  6. **One shared 0–1 space:** Average Islands Scale across all 21 meshes together, then Pack Islands (rotate on, CONCAVE shapes, margin **0.002**, as a fraction of the square).
  7. **Hidden islands at half scale:** 2,901 patches that no ray reaches (fully under clothing: most of Body, the hair under the cap, the shirt under the vest) get 50% of the linear scale before the pack.
- **Margin choice (0.002, not 0.004–0.01):** measured on the real layout (6,519 islands), UV space used: **0.001 → 47.5%, 0.002 → 35.6%, 0.003 → 23.5%, 0.004 → 13.3%, 0.006 → 0.1%, 0.01 → doesn't fit** (the packer pushed UVs out to 5.25). Every island pays the margin on every side, so with thousands of islands the margin, not the islands, fills the square. 0.002 is about 2 px between islands on a 1024² texture (4 px on 2048²): enough for bilinear filtering at mip 0, and it still uses a third of the square. The cost: from mip level 2 down (256² and below), neighbouring islands bleed into each other. That doesn't matter while every material is a flat colour or a palette bake (the same reasoning as the sniper's 34% in the gun entry). A painted texture would want fewer islands and a 2048² map; re-run with `--margin` then.
- **Rule 7, measured** (same run with `--hidden-scale 1.0` for comparison): visible surfaces get **224.5 texels/m instead of 199.4 (+12.6%)** on a 1024² texture. The hidden surfaces drop to 112.3 texels/m, where nobody can see them. The price is the texel-density spread: with rule 7 off, 13 of the 21 meshes have ≤ 1% spread between islands. With it on, the spread rises by design, because hidden islands are deliberately at half density.
- **Why:** The materials are flat colours, so the UVs don't change how the player looks today. They're needed for the planned palette atlas and any texture bake, and an atlas only works if no two faces share a texel. 21 meshes and ~152k tris made hand-cutting slow, so scripted rules were the only way to cut all of them in the time. They also give one consistent texel density across all meshes (one Average Islands Scale over the whole character), and the visibility map puts seams where the third-person camera rarely looks, the same aim as hand-placed seams.
- **Safety:** before the script touches the file, it hashes vertex positions, face order, loops, vertex groups and every weight, bones, object transforms, parenting, modifiers, material slots, per-face material indices, smoothing flags, collections, visibility and actions. It writes to a temp file, re-opens it and compares again (all **19 checks PASS**, in memory and in the saved file), and only then replaces the `.blend`. The single layer `UVMap` was overwritten in place, never renamed or duplicated. Geometry, rig and materials are byte-identical.
- **Numbers** (measured with `SourceArt/Tools/uv/uvmetrics.py` on all 21 meshes together in their one shared square, on the `.blend` and again on the exported FBX):

  | | Before | After (`.blend`) | After (exported FBX) |
  |---|---:|---:|---:|
  | Overlapping faces (shared square) | 78,431 / 83,290 | **0 / 83,290** | **0 / 83,290** |
  | Most faces on one UV spot | 4,328 | **1** | **1** |
  | UVs inside 0–1 | yes | **yes** (0.002–0.9971) | **yes** (0.002–0.9971) |
  | Islands | 5,750 | 6,519 | 6,519 |
  | Marked seams | 0 | 33,683 | — (FBX stores no seams) |
  | Stretch-free faces (every corner within 2°) | 31,306 | **57,187** (69%) | 57,187 |
  | Faces with angle stretch > 2° | 51,984 | **26,103** | 26,103 |
  | UV area / 3D area ratio, min–max | 0–4,757.7 | 0–3,677.5 | 0–3,675.0 |
  | UV area / 3D area ratio, 1st–99th percentile | 0.06–98.7 | **0.21–2.51** | — |
  | Texel density spread between islands (CV) | 156% | 99.9% (87.3% without rule 7) | 99.0% |
  | UV space used | — (overlapping) | 35.6% | 35.6% |

  - The "0" minimum ratio and the 180° worst corner (before and after) come from 10 zero-area faces in the source geometry (2 in Head_Mesh, 8 in Radio). Geometry was not allowed to change, so they stay.
  - Most extreme ratios come from a few dozen faces per mesh at the tips of the hidden cuts (for example Cap: 34 of 5,864 faces above ×16). The 1st–99th percentile row shows the bulk: 98% of faces lie within ×0.21–×2.5, and the ×0.25 end is the deliberate rule 7 half-scale.
  - The FBX matches the `.blend` (same faces, islands, overlap and bounds; the ratio max and CV differ only by rounding from the export's baked transforms).
  - **Independent re-check (13 Oct):** Blender's own *UV ▸ Select ▸ Select Overlap* selects **6 faces** on the player (Radio 2, Safety_Vest 4; before: 82,933) and **0** on both birds. A triangle-level test (the GPU draws triangles, `uvmetrics.py` compares whole polygons) also finds micro-folds where a quad is slightly concave in UV space and one of its two render triangles folds over the neighbouring face: player 99 faces / ~29 px total on a 1024² map, SK_Bird ~53 px, SK_Bird_Hawk ~2 px. With flat colours or a palette bake these are invisible; for a painted texture, relax those faces first. Geometry, weights, bones, materials and shape keys were re-checked against the committed files: identical.

  Per mesh (each mesh on its own; the CV in brackets is the same run with rule 7 off):

  | Mesh | Type, unwrap | Overlap before → after | Stretch-free faces before → after / total | Islands before → after | Density spread (CV) before → after (rule 7 off) |
  |---|---|---:|---:|---:|---:|
  | Belt | hard, conformal | 0 → **0** | 1,110 → 970 / 1,178 | 64 → 45 | 14% → 89% (92%) |
  | Belt_Buckle | hard, conformal | 0 → **0** | 194 → 198 / 250 | 28 → 28 | 4% → 16% (0%) |
  | Belt_Loops | hard, conformal | 0 → **0** | 170 → 123 / 378 | 42 → 48 | 2% → 12% (0%) |
  | Body | organic, angle based | 128 → **0** | 1,172 → 3,306 / 5,074 | 136 → 643 | 11% → 31% (1%) |
  | Boots | hard, angle based | 30 → **0** | 6,625 → 10,999 / 14,654 | 2,119 → 2,501 | 9% → 49% (56%) |
  | Cap | hard, conformal | 38 → **0** | 1,201 → 4,873 / 5,864 | 375 → 496 | 58% → 180% (151%) |
  | Eye_L | eye, planar | 0 → **0** | 384 → 420 / 656 | 8 → 6 | 2% → 28% (0%) |
  | Eye_R | eye, planar | 0 → **0** | 384 → 420 / 656 | 8 → 6 | 2% → 28% (0%) |
  | Gloves | organic, angle based | 3 → **0** | 1,766 → 2,092 / 4,764 | 588 → 312 | 43% → 42% (19%) |
  | Hair | organic, angle based | 0 → **0** | 1,552 → 2,355 / 4,716 | 234 → 200 | 6% → 21% (0%) |
  | Head_Mesh | organic, angle based | 26 → **0** | 2,018 → 4,991 / 6,485 | 180 → 140 | 8% → 36% (14%) |
  | Headset | hard, angle based | 0 → **0** | 2,910 → 3,297 / 5,130 | 246 → 150 | 4% → 25% (0%) |
  | ID_Badge | hard, conformal | 0 → **0** | 270 → 238 / 342 | 157 → 155 | 3% → 35% (0%) |
  | Knee_Pads | hard, angle based | 0 → **0** | 1,365 → 1,539 / 2,752 | 175 → 129 | 41% → 128% (140%) |
  | Pants | organic, angle based | 948 → **0** | 2,040 → 6,155 / 8,092 | 40 → 406 | 223% → 174% (149%) |
  | Pouch_Clips | hard, conformal | 0 → **0** | 168 → 311 / 518 | 70 → 45 | 5% → 34% (0%) |
  | Pouches_Black | hard, angle based | 0 → **0** | 837 → 610 / 1,390 | 137 → 83 | 5% → 16% (0%) |
  | Pouches_Brown | hard, angle based | 0 → **0** | 456 → 455 / 798 | 68 → 45 | 3% → 23% (0%) |
  | Radio | hard, angle based | 0 → **0** | 470 → 365 / 710 | 141 → 121 | 5% → 28% (0%) |
  | Safety_Vest | organic, angle based | 0 → **0** | 4,401 → 7,299 / 8,478 | 238 → 407 | 5% → 21% (0%) |
  | Shirt | organic, angle based | 165 → **0** | 1,813 → 6,171 / 10,405 | 696 → 553 | 24% → 43% (29%) |

- **What got worse, honestly:**
  - Six small hard-surface meshes have slightly fewer stretch-free faces than their placeholder (Belt 1,110 → 970, Belt_Loops 170 → 123, Pouches_Black 837 → 610, Radio 470 → 365, ID_Badge 270 → 238, Pouches_Brown 456 → 455). Their old Smart-UV-style layout cut every face group flat, but it overlapped with every other mesh in the shared space. Most likely cause: rule 2 keeps bevels below 60° inside one island, so they bend a little when flattened.
  - Belt, Boots, Cap, Gloves, Head_Mesh, Knee_Pads, Pants and Shirt keep a large density spread even with rule 7 off (14–151%), because a handful of islands are not evened out by Blender's Average Islands Scale. I haven't tracked down the cause; the overlap and stretch results don't depend on it.
- **Birds (SK_Bird, SK_Bird_Hawk): whole mesh, same rules.** The plan was to fix only the eye faces, but `uvmetrics.py` showed that the rest of each bird was just as broken. The eye faces alone were 0 overlapping (of 672 / 824); everything else was stacked: SK_Bird 7,760 of 12,319 faces, SK_Bird_Hawk 9,650 of 13,646. I stopped and reported, and on 13 Oct the decision was to unwrap the whole bird with the same method. `SourceArt/Tools/uv/unwrap_bird.py` imports the rules, checks and metrics from `unwrap_player.py` (the player script only got a `main()` wrapper; re-running it gives byte-identical UVs and seams) and changes only what differs on a bird:
  - **Rule 2 by material:** each bird is one mesh, so the sharp-edge rule applies to the hard keratin faces only (materials with Beak, Cere, Claw or Talon in the name: 244 / 316 edges). Feathers, legs and eyes are organic (rule 3).
  - **Eyes:** no separate projection. Iris, pupil and eyelid / eye ring are their own patches through rule 1 and are unwrapped like the rest.
  - **Visibility:** a bird flies over the player, so the camera weights are reversed: from below 1.0, from the side 0.8, from above 0.3. Visibility is measured in every shape-key pose (basis, each key at 100%, all keys together) and the highest value counts, so nothing that shows when the wings spread is treated as hidden.
  - **Margin 0.004** (inside the 0.004–0.01 range), each bird in its own square. Measured UV space used:

    | Margin | 0.002 | **0.004** | 0.006 | 0.01 |
    |---|---:|---:|---:|---:|
    | SK_Bird (1,442 islands) | 42.6% | **28.4%** | 17.4% | 3.3% |
    | SK_Bird_Hawk (1,861 islands) | 34.3% | **21.7%** | 11.7% | 0.9% |

    A bird covers far less of the screen than the player, so its texture will be smaller. At 512², a 0.004 margin is 2 px, the same gap the player gets with 0.002 at 1024².
  - **Rule 7:** only 264 / 164 patches are fully hidden from every direction in every pose, so the gain is small: visible texels per metre at 1024² go from 375.5 to 388.7 (+3.5%) on SK_Bird and from 305.4 to 312.2 (+2.2%) on the hawk.
  - **Shape keys:** names, relative keys, values and every key's vertex positions are part of the hash (PASS in memory and in the saved file). The re-imported FBX has identical vertices, faces, weights, bones and `Basis` / `Wings_Spread` / `Tail_Spread` data. Only the UVs differ.
  - **No Smart UV fallback** on either bird.

  | | SK_Bird before | SK_Bird after | SK_Bird_Hawk before | SK_Bird_Hawk after |
  |---|---:|---:|---:|---:|
  | Overlapping faces | 8,192 / 12,991 | **0** | 10,210 / 14,470 | **0** |
  | Most faces on one UV spot | 4,283 | **1** | 3,700 | **1** |
  | UVs inside 0–1 | yes | **yes** (0.004–0.991) | yes | **yes** (0.004–0.990) |
  | Islands | 1,341 | 1,442 | 1,593 | 1,861 |
  | Marked seams | 0 | 7,483 | 0 | 8,933 |
  | Stretch-free faces (every corner within 2°) | 964 | **6,919** (53%) | 1,091 | **8,822** (61%) |
  | Worst corner | 180° | 154.3° | 180° | 88.1° |
  | UV area / 3D area ratio, min–max | 0–2,722.5 | **0.11–9.94** | 0–152.5 | **0.13–1.95** |
  | Ratio, 1st–99th percentile | 0.01–108.4 | **0.21–1.60** | 0.02–121.4 | **0.18–1.46** |
  | Texel density spread (CV) | 146% | 23.3% (8.5% without rule 7) | 157% | 14.9% (0.4% without rule 7) |
  | UV space used | — (overlapping) | 28.4% | — (overlapping) | 21.7% |

  The exported FBXs measure exactly the same as the `.blend`s (0 overlaps, same islands, bounds, stretch and coverage). Export: no errors, 25,266 / 28,204 tris, 9 deform bones; the 13 / 14 material-slot warnings are the known palette-atlas item, and the material manifests are byte-identical.
- **Evidence:** `Evidence/UV/SK_Player_Officer_UV_before.png` / `.svg` and `_UV_after.png` / `.svg` (all 21 meshes in the shared square, one colour per mesh; the SVGs show island outlines), `SK_Player_Officer_UV_before_after.png` (side by side with the overlap counts), `SK_Player_Officer_checker_before_after.png` (front and back, Workbench render with a 256×256 checker), and `uv_report.json` (combined, FBX and per-mesh numbers, the margin test, and the rule 7 comparison). The same set exists for `SK_Bird` and `SK_Bird_Hawk`; their checker renders show the underside with the wings spread and a side view of the head and eye. These images were drawn by a small evidence script, not with Blender's Export UV Layout as for the guns: with 83k faces, Blender's export SVG would be very large (estimated ~10 MB).
- **Export:** `export_fbx.py --all`: no errors, 152,707 tris, `UVMap` on every mesh, 52 deform bones. The 14 material-slot warnings stay until the palette atlas pass. The material manifest is byte-identical.
- **Viva note:** the seams were placed by scripted rules, not by hand. To show the method live: select an arm tube of `Shirt` in Blender, mark a seam along its underside (the least visible side), Unwrap, and check Display Stretch. That is rule 3 done by hand on one patch.

## 2026-10-13 — One flat-colour atlas per model (one material instead of 7–49)
- **Context:** Every model used one URP Lit material per flat colour: player 49 materials / 76 submeshes, gull 13, hawk 14, guns 7 / 8 / 10 / 7 + 3 (113 materials, 138 submeshes in total). Each submesh is one draw call in every render pass, and the 11 Oct test showed ~4.2 passes per submesh (shadow cascades + main pass). No model had a texture, so I also had no texture-compression evidence.
- **Options considered:** A) One shared palette texture `M_Palette` for all models (the first plan): every face's UVs would have to be moved onto a colour swatch, which throws away the unwrapped layouts. B) A Cycles bake per model: it renders lighting that isn't needed, and it samples texel centres only, so an island thinner than a texel gets its neighbour's colour. C) A script that fills each model's **existing UV islands** with the values of the material on those faces, one atlas per model.
- **Choice:** C. `SourceArt/Tools/bake_atlas.py`, called by `export_fbx.py` for the assets in its `ATLAS` table.
- **How it was done (tooling):** the fill is done **by a script, not painted by hand**; the script was written with an AI coding assistant. I ran the Unity look checks and the performance tests.
  - Per model: `T_<Asset>_Atlas_Albedo` (RGB = base colour) and `T_<Asset>_Atlas_Mask` (R = metallic, A = smoothness; URP Lit's Metallic Map) → one material `M_<Asset>_Atlas`.
  - It is a fill, not a render. Every texel an island touches gets that island's value (exact triangle / texel test, quads tested with both diagonals), and texels touched by two islands with different values are counted as **bleed texels**. 0 bleed texels means every face shows exactly its own colour with Point filtering.
  - The `.blend` files and the UVs don't change. The material slots are swapped only for the duration of the FBX export and put back, so the flat materials stay the authoring source in Blender.
  - **Species:** the pigeon and crow colours moved from `ArtBirdSpeciesBuilder.cs` to `SourceArt/Tools/bird_species.json`. One Albedo per species is filled on the gull's UVs (`M_Bird_Pigeon_Atlas`, `M_Bird_Crow_Atlas`); the pigeon reuses the gull's Mask.
  - **Kept as separate materials on the player:** transparent `M_Player_Badge_Clear` and emissive `M_Player_LED_Green`, `M_Player_Eye_Catch`, `M_Player_Vest_HiVis` (there is no Emission texture). Player: 5 materials / 26 submeshes.
- **Texture size:** flat colours need no detail, so the size only has to keep two islands from sharing a texel. Blender pads every island by the pack margin, so the gap is 2 × margin: birds 2 × 0.004 × 256 = **2.05 px → 256²**; player 2 × 0.002 × 512 = **2.05 px → 512²**; guns **128²**. The bleed count is the proof, not the formula.
- **Import settings (`_Atlas_` textures, `ArtTexturePostprocessor.cs`):**
  - **Point filter:** the GPU reads exactly the texel a UV falls in. Bilinear would blend in the neighbouring island, 2 texels away.
  - **No mipmaps:** an island is one flat value, so a smaller mip adds nothing inside it and would only average neighbouring islands together. It also saves 25% memory.
  - **Clamp**, and **High Quality compression (BC7)** instead of BC1, because BC1's 5:6:5 colours tint the near-black materials. Measured on 13 Oct, see "Compression, measured" below.
- **Why:** the 11 Oct test named submeshes, not triangles, as the real cost. C cuts them without touching geometry, UVs or the look, and it reuses the unwrap instead of discarding it. Cohesion comes from the shared flat-colour style, not from a shared texture.
- **Numbers (export checks, all 7 assets: 0 bleed texels, 0 wrong-colour triangles):**

  | Asset | Tris | Materials before → after | Submeshes before → after | Atlas | Texels used |
  |---|---:|---|---|---|---:|
  | `SK_Bird` (gull, pigeon, crow) | 25,266 | 13 → 1 per species | 13 → 1 | 256² | 49.1% |
  | `SK_Bird_Hawk` | 28,204 | 14 → 1 | 14 → 1 | 256² | 45.2% |
  | `SK_Player_Officer` | 152,707 | 49 → 5 | 76 → 26 | 512² | 50.8% |
  | `SM_Gun_Tier1` | 820 | 7 → 1 | 7 → 1 | 128² | 85.8% |
  | `SM_Gun_Tier2` | 2,064 | 8 → 1 | 8 → 1 | 128² | 66.6% |
  | `SM_Gun_Tier3` | 6,304 | 10 → 1 | 10 → 1 | 128² | 55.3% |
  | `SM_Gun_Tier4` (+ rocket) | 1,664 | 7 → 1 | 10 → 2 | 128² | 72.0% |

  Whole game: **113 materials → 13**, **138 submeshes → 33**. 17 textures; the PNG files total 94 KB. Calculated GPU memory with BC7 (1 byte per texel) and no mipmaps: 7 × 64 KB + 2 × 256 KB + 8 × 16 KB = **1.06 MB**. Confirmed by the Art Budget Checker (`budget_report.md`, 13 Oct 20:21): 17 textures, all BC7 with 1 mip level, **1,088.0 KB**.
- **Safety:** each re-exported FBX was imported back into Blender and compared with the previous one: vertex positions, faces, UVs, weights, bones, shape keys, smoothing and object transforms are identical on all 7 assets; only the material slots differ.
- **Performance** (`SourceArt/perf_report.md`, player + 100 birds, Editor Play mode, 1920×1080, VSync off, same machine as 11 Oct):

  | Metric | Before (13:54, 13:56) | Birds on the atlas (14:17) | Birds + player (14:21) |
  |---|---:|---:|---:|
  | Draw calls (Batches) | 5,950 | 763 | **569** (−90%) |
  | SRP Batcher draw calls | 5,904 | 717 | **523** |
  | Shadow casters | 3,115 | 378 | **284** (−91%) |
  | SetPass calls | 38 | 40 | 39 |
  | Render thread (ms) | 3.94 / 3.96 | 2.78 | **2.55** (−35%) |
  | CPU main thread (ms) | 10.48 / 10.52 | 10.36 | 10.35 |
  | Average FPS | 95.5 / 95.1 | 96.6 | **96.7** |
  | Lowest FPS (0.5 s) | 95.1 / 94.2 | 96.0 | 95.9 |
  | Triangles / vertices drawn | 11.61 M / 10.18 M | same | same |

- **Result, honestly:** draw calls and shadow casters fell by 90%, and the render thread by a third, but **FPS only moved from ~95 to ~97**. The frame time stays at ~10.4 ms whatever the draw-call count, so in this test draw calls were not the bottleneck: the SRP Batcher already made them cheap (5,904 of 5,950 were batched before). The "CPU main thread" row does not show that the main thread is the limit: it is the Stats panel value, which includes the time the main thread waits for the render thread and the GPU, and it equals the frame time in every run. This corrects the 11 Oct entry, which expected the atlas to be the biggest win. What the atlas does give: 100 fewer material assets, far fewer shadow casters, and headroom on the render thread for the level and effects my teammates add.
- **Side observation, not proven:** between the 11 Oct run and today's "before" run, the same 100-bird scene went from 78.4 to ~95 FPS with identical triangles and draw calls, while drawn vertices fell from 21.0 M to 10.2 M. The UV unwrap is the only asset change I know of between those runs (the placeholder UVs split almost every face into its own vertices). This suggests vertex count, i.e. skinning, matters more here than draw calls.
- **Correction to the side observation (13 Oct, evening):** the budget reports disprove that explanation. The imported meshes did not lose vertices with the unwrap, they gained a few: gull 21,480 → 21,682, hawk 23,773 → 26,371, player 121,605 → 125,477. The 100-bird scene has 2.33 M unique vertices before and 2.41 M after. Today's 10.2 M drawn vertices are exactly unique vertices × 4.22 passes; the 11 Oct figure of 21.0 M was about twice what those meshes contained, and I have not found out why. The two 11 Oct runs were also unsteady (lowest window 57.9 FPS against a 78.4 average; today's runs stay within 1 FPS). So the 78 → 95 FPS difference is **unexplained**, and I do not claim the unwrap as a performance gain.
- **What the unwrap costs:** seams split vertices. Mesh memory on the GPU went from 13,180.9 KB to **13,747.8 KB (+4.3%)** for the 7 models (`budget_report.md`, 11 Oct vs 13 Oct). The largest relative rise is the pistol (934 → 1,294 vertices), the largest absolute one the player (+3,872 vertices, +306 KB).
- **Is the frame limited by pixels? (13 Oct 20:24)** The same scene at 960×540, a quarter of the pixels: **105.7 FPS, 9.46 ms** against 96.7 FPS, 10.34 ms. Removing 75% of the pixels saved 0.88 ms, so pixel work is about 1.2 ms of the frame and about 9.2 ms does not depend on the resolution. That rules out fill rate. It does not separate CPU work from GPU work that ignores the resolution (11.6 M triangles per frame, shadow maps of a fixed size). `ArtPerfTest.cs` now also records the FrameTimingManager values (main thread, its wait for the present, render thread, GPU) to answer that.
- **CPU or GPU? (13 Oct 20:37 and 20:38): the GPU.**

  | Frame timing (ms) | Empty scene | Player + 100 birds | Added by the wave |
  |---|---:|---:|---:|
  | Average FPS | 230.5 | 93.1 | |
  | Whole frame | 4.35 | 10.39 | +6.04 |
  | Main thread, work | 2.34 | 3.04 | +0.70 |
  | Main thread, waiting for the present | 0.15 | 5.20 | +5.05 |
  | Render thread | 1.05 | 1.23 | +0.18 |
  | **GPU** | **1.22** | **9.73** | **+8.51** |

  - The GPU needs 9.73 ms of a 10.39 ms frame. The main thread finishes its work in 3 ms and then waits 5 ms for the GPU, and the render thread needs 1.2 ms. So neither the scripts, nor culling, nor the draw-call submission limit the frame.
  - The empty scene runs at 230 FPS, so the editor itself is not the floor: the wave really costs 6 ms.
  - With the half-resolution run: at most ~1.2 ms of the GPU time depends on the pixel count. The other ~7–8 ms the wave adds is GPU work that ignores the resolution: transforming and rasterising 11.6 M triangles (10.2 M vertices) per frame, skinning them, and filling the shadow maps. Every triangle is drawn 4.22 times (11.61 M drawn against 2.75 M unique): 2 camera passes and, on average, 2.2 shadow cascades, as the shadows-off run below shows. Settings at the time of the test: `PC_RPAsset` with 4 shadow cascades, 50 m shadow distance and a 2048² shadow map (every test bird is 8–35 m from the camera, so inside the shadow distance), and GPU skinning on (Project Settings).
  - **This corrects the 11 Oct entry.** I wrote there that the cost follows the submeshes and that the atlas would pay off most. The measurements say the opposite: draw calls fell by 90% and nothing changed, because the cost is **triangles × passes on the GPU**. The order of payoff is now: (1) fewer shadow passes for the birds (shorter shadow distance, fewer cascades, or birds that cast no shadow); (2) fewer triangles per bird at a distance (LODs); (3) deleting the hidden player faces, which is only 50,000 of 2.75 M unique triangles in this scene and so matters little here. The shadow settings live in the shared URP asset and are the World Builder's; Cast Shadows on my bird prefabs is mine.
  - "No triangle limits" still holds as a decision: 93–97 FPS in the editor with 2.5× a realistic wave. But the reason is now "the GPU has enough headroom for these triangles", not "triangles don't matter".
- **How much are the shadow passes? (13 Oct 20:55)** The same wave with Cast Shadows off on every renderer (`Tools > S3 > Performance Test > Test Wave: Cast Shadows Off`; scene instances only, the prefabs still cast shadows):

  | | With shadows (20:37) | Wave casts no shadows (20:55) |
  |---|---:|---:|
  | Average FPS / lowest | 93.1 / 80.6 | **115.4** / 115.1 |
  | Whole frame (ms) | 10.39 | 8.67 |
  | GPU (ms) | 9.73 | 8.25 |
  | Triangles drawn | 11.61 M | 5.51 M |
  | Draw calls | 569 | 297 |
  | Shadow casters | 284 | 12 |

  - Without shadows every triangle is still drawn exactly twice (5.51 M / 2.75 M): the depth pre-pass and the colour pass. The shadow passes were 6.1 M triangles, **53% of all triangles drawn**.
  - Removing that 53% saved only **1.5 ms of GPU time (−15%)**, about +20 FPS. Shadow passes are cheap per triangle (depth only). This is the largest gain measured so far, but it is not the bulk of the cost.
  - **The bulk stays:** 8.25 ms of GPU time against 1.22 ms for the empty scene, with no shadow passes and at most ~1.2 ms of pixel work. That ~6 ms is the models themselves: skinning 2.4 M vertices on the GPU once per frame and drawing 2.75 M triangles in two camera passes. I have not separated skinning from the camera passes.
  - **This corrects the payoff order in the bullet above.** What the frame cost follows is the vertex and triangle count of the models on screen. So the measures that would move it most are bird LODs and fewer triangles per model; bird shadows are a smaller, easy gain (~1.5 ms).
  - **Nothing is changed for now.** 93–97 FPS with shadows in the editor, for 2.5× a realistic wave, is enough for this game. The numbers are kept as the justification and as the list of what to do first if the full level turns out too slow.
- **Compression, measured (13 Oct 20:54, `compression_report.md`, Tools > S3 > Texture Compression Test):** each atlas PNG compressed in memory, decoded by the GPU and compared with the PNG texel by texel. "On faces" counts only the texels a UV triangle overlaps (557,000 of 1.11 M); the script's count per texture matches the bake's "texels used" (gull 49.07%, hawk 45.21%, pistol 85.83%).

  | Option | Memory, 17 textures | Texels on faces off by more than 2 of 255 | Largest error on faces |
  |---|---:|---:|---:|
  | Uncompressed (RGBA32) | 4,352 KB | 0 | 0 |
  | Normal quality (BC1; BC3 for the Masks) | 800 KB | 59,296 (10.64%) | 150 |
  | **High quality (BC7)**, the import rule | **1,088 KB** | **1,164 (0.21%)** | **31** |

  - **BC1 is unusable for this kind of texture:** 16–24% of the drawn texels change on the bird and player Albedo, with errors like 229,217,191 → 247,186,41 on the hawk. The reason is the block format: one 4×4 block stores 2 colours and 2 mixes of them, on one line in colour space. A block that holds 3 or more unrelated flat colours (two islands and the gap between them) cannot be stored.
  - **BC7 costs 288 KB more than BC1/BC3 and removes 98% of those errors.** It is not lossless: 1,164 drawn texels are off, at most by 31 (gull, 229,229,224 → 247,198,215). Most likely they sit on island borders, in blocks that hold several colours (not checked texel by texel). So "0 bleed texels" is exact for the PNG and almost exact after compression. No such speck was seen in the look check.
  - **Uncompressed would be exact for 3.2 MB more.** BC7 stays for now: a quarter of the memory, and the remaining error is 0.21% of the drawn texels. If a speck ever shows on a model, the fix is one line in `ArtTexturePostprocessor.cs` (uncompressed for that atlas).
- **SetPass calls did not drop** because all flat materials already shared one shader variant; the atlas materials use the Metallic Map variant, which adds one.
- **Known limits:** (1) Without mipmaps a distant bird samples the full-size texture; this is fine for flat colours, but with MSAA a pixel on a triangle edge can sample slightly outside its island. Each island has 8 texels of its own colour as padding; no specks were seen in the look check. (2) A painted or gradient texture would need Bilinear, mipmaps, a larger size and a wider margin, so this method only fits the flat-colour style.
- **Bug found and fixed (models turning plain white):** it happened twice. (1) On the first install the hawk was plain light grey. The Unity log shows `ArtMaterialBuilder.cs` re-imported the model inside the same refresh that created the atlas material, before Unity had imported that material, so the model kept the FBX's own fallback material. Running Rebuild Materials From Manifests fixed it, and the re-import now runs after the refresh (`EditorApplication.delayCall`). (2) The player imported correctly, then turned white after a later script reload with no re-import in between. Likely cause (inferred from the log, not proven): the model's very first import, made while the material did not exist, recorded no dependency on that material, so Unity still treated the fallback result as valid and reused it. `ArtModelPostprocessor.cs` now registers the dependency on the material path even when the file is missing, and its version went from 2 to 3 so every model is re-imported once with the materials in place. Lesson: an importer that reads another asset must declare it, or Unity's import cache can't know the result is stale. Second fix: `ArtBirdSpeciesBuilder.cs` now reverts a variant's material overrides before assigning the atlas material, which removed 12 dead slot overrides from the pigeon and crow prefabs. Third fix: the builder wrote source blend `SrcAlpha` for the transparent badge, and Unity rewrote the file to `One` every time it validated the material (URP's "Preserve Specular Lighting" premultiplies in the shader). The builder now writes what URP expects, so the file stops changing.
- **Fix checked in Unity (13 Oct):** with importer version 3, all 4 checks passed: the models right after the automatic re-import, all 7 models side by side in the scene, after entering and leaving Play mode (script reload), and after closing and reopening the editor. This shows the white models no longer come back; it does not prove the cause given in (2), which is still inferred from the log.
- **Clean-up:** after the look check on all 7 assets, the **109 old flat materials** were deleted (birds 27, species 22 with their folder, player 45, guns 15). Before deleting, a search for their GUIDs found no scene, prefab or asset that still used them, on this branch or on `origin/worldBuilder`. 13 materials remain: 4 bird, 5 player, 4 gun.
