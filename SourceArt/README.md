# SourceArt — Core Developer art pipeline

Source files for every custom model. This folder is **outside `Assets/`**, so Unity never imports `.blend` files directly. Only the exported `.fbx` files go into `Assets/_Project/Art/`.

| Folder | Contents |
|---|---|
| `Blender/{Birds,Characters,Weapons,Props}/` | `.blend` source files (Git LFS, one owner per file) |
| `Textures/` | Layered / high-res texture sources (`.psd`, `.kra`, bakes) |
| `Reference/` | Reference images and sheets |
| `Tools/export_fbx.py` | Blender → Unity FBX exporter with pre-export checks |
| `Tools/bake_atlas.py` | Used by `export_fbx.py`: fills an asset's UV islands with its flat material values → `T_<Asset>_Atlas_Albedo` / `_Mask`, so the asset exports with one material `M_<Asset>_Atlas`. Sizes are in its `ATLAS` table. Read-only on the `.blend`. See `DECISIONS.md` (13 Oct) |
| `Tools/bird_species.json` | Pigeon and crow colours per base material; `bake_atlas.py` fills one Albedo per species on the gull's UVs |
| `Tools/ingest_fbx.py` | Turns a raw FBX (made outside this pipeline) into a pipeline-ready `.blend` |
| `Tools/hidden_faces_report.py` | Read-only: tris per mesh and how many are hidden under other meshes (optimisation targets) |
| `Tools/uv/unwrap_pistol.py`, `Tools/uv/unwrap_generic.py` | Scripted UV unwrap of the guns (box-net seam rules → Blender Unwrap / Smart UV / Average Scale / Pack). Used once on 11 Oct, see `DECISIONS.md`. Args: `-- <in.blend> <out.blend>`. `unwrap_generic.py` needs the `shapely` package in Blender's Python |
| `Tools/uv/unwrap_player.py` | Scripted UV unwrap of `SK_Player_Officer` (all 21 meshes into ONE shared 0–1 space): material/sharp-edge seams, least-visible-side cuts on the organic meshes, shared Average Scale + Pack. Hashes geometry, rig, weights and materials before/after and replaces the `.blend` only if every check passes. Args: `-- <in.blend> <out.blend> [--margin 0.002] [--hidden-scale 0.5] [--report report.json]`. Run it with `--python-use-system-env` so Blender sees `shapely`. See `DECISIONS.md` (12 Oct) |
| `Tools/uv/unwrap_bird.py` | Scripted UV unwrap of one bird (`SK_Bird` or `SK_Bird_Hawk`: one mesh, shape keys) with the same rules and checks as `unwrap_player.py` (imports it). Bird-specific: sharp-edge seams only on beak / cere / claws, visibility seen from below and in every shape-key pose. Shape keys are part of the before/after hash. Args: `-- <in.blend> <out.blend> [--margin 0.004] [--hidden-scale 0.5] [--report report.json]`, run with `--python-use-system-env` |
| `Tools/uv/uvmetrics.py` | Read-only UV quality metrics module (overlap, stretch, texel density); needs `shapely` |
| `Evidence/UV/` | Gun, player and bird UV layouts before/after (PNG/SVG), checker renders, `uv_report.json` (viva evidence) |
| `Tools/rig_bird.py` | Turns a raw bird (`.fbx` or `.blend`, separate parts, no armature) into a rigged `SK_Bird*.blend` |
| `DECISIONS.md` | Design decision log (viva evidence) |
| `HANDOFF.md` | Notes and requests for other team members |

## 1. Scale, orientation, pivots

- Blender: Scene Properties → Units → **Metric, Unit Scale 1.0**. **1 Blender metre = 1 Unity unit.**
- Player reference height **1.8 m**.
- **The model's front faces −Y in Blender** (the side you see in Front view, Numpad 1). It becomes **+Z (forward) in Unity**.
- Pivots (object origin):
  - Player: between the feet, at ground level.
  - Gun: at the grip.
  - Bird: at the centre of mass.
  - Props: at the base centre (ground contact).
- **Apply rotation and scale** (Ctrl+A) before export. The exporter refuses scale ≠ 1.

## 2. One asset = one collection

Put each asset in its own collection, named with its prefix. The collection name becomes the `.fbx` file name, and the **second token** of that name picks the Unity folder.

| Collection name | Exports to |
|---|---|
| `SK_Bird`, `SK_Bird_Hawk` | `Assets/_Project/Art/Models/Birds/` |
| `SK_Player_Officer` | `Assets/_Project/Art/Models/Characters/` |
| `SM_Gun_Tier1` … `SM_Gun_Tier4` | `Assets/_Project/Art/Models/Weapons/` |
| `SM_Prop_*` | `Assets/_Project/Art/Models/Props/` |
| `SM_Env_*` | `Assets/_Project/Art/Models/Environment/` |

- Hidden objects are **not** exported. Keep blockouts and reference planes hidden in the same file.
- Gun attachment points: add Empties named `Socket_Muzzle`, `Socket_Grip`, etc. They export as child transforms in Unity.
- Animation clips (rigged assets): each action is pushed down to its own NLA track, named like `A_Bird_Fly`. In Unity the clip becomes `A_Bird_Fly`; the `Armature|` prefix is stripped automatically.

## 3. Exporting

**From Blender (GUI):** Text Editor → Open → `SourceArt/Tools/export_fbx.py` → click the asset's collection in the Outliner → **Run Script** (Alt+P). A popup shows the report.
The `.blend` must be saved somewhere inside `SourceArt/` so the script can find the Unity project.

**From the command line** (repo root):
```bash
blender SourceArt/Blender/Weapons/Guns.blend --background --python SourceArt/Tools/export_fbx.py -- --all
blender SourceArt/Blender/Birds/SK_Bird.blend --background --python SourceArt/Tools/export_fbx.py -- --collection SK_Bird --check
```
| Option | Meaning |
|---|---|
| `--collection NAME` | export one collection (default: active collection) |
| `--all` | every collection starting with `SM_` / `SK_` |
| `--check` | report only, write nothing (good for budget evidence) |
| `--mixamo` | unrigged `SK_` mesh for Mixamo upload → `SourceArt/Mixamo/` |
| `--out DIR` | override the output folder (an atlas asset's manifest and textures go there too) |
| `--atlas-size N` | bake an atlas at N × N even if the asset isn't in `bake_atlas.py` (to try sizes) |

Exit code is 1 if any asset failed its checks.

Every export also writes a **material manifest** `Assets/_Project/Art/Materials/<Folder>/<Asset>.materials.json` (base colour, metallic, roughness, alpha, emission of each `M_` material). Unity builds the URP materials from it, so colours are authored in Blender only.

**Atlas assets** (every asset in the `ATLAS` table of `bake_atlas.py`: birds, player, guns) export with **one material**. The flat colours are filled into the asset's UV islands and written to `Assets/_Project/Art/Textures/<Folder>/T_<Asset>_Atlas_Albedo.png` and `_Mask.png` (R = metallic, A = smoothness). The manifest then lists `M_<Asset>_Atlas` with those textures. Transparent and emissive materials keep their own slot. The `.blend` keeps its flat materials, so to change a colour: change the material in Blender and export again. The report must show **bleed texels 0** and **wrong-colour triangles 0**; if not, raise the size in `ATLAS`.

### Bringing in a raw FBX (made outside this pipeline)
FBX files exported with Blender's default settings have a 90° X rotation on every mesh, free-form names and often no UVs. Convert them once into a `.blend` with `ingest_fbx.py`, then export as usual:
```bash
blender --background --factory-startup --python SourceArt/Tools/ingest_fbx.py --     --blend SourceArt/Blender/Weapons/Guns.blend --material-prefix M_Gun_ --sockets     ../FBX_Files/pistol.fbx=SM_Gun_Tier1 ../FBX_Files/ar.fbx=SM_Gun_Tier2     ../FBX_Files/sniper.fbx=SM_Gun_Tier3 ../FBX_Files/rpg.fbx=SM_Gun_Tier4
blender SourceArt/Blender/Weapons/Guns.blend --background --python SourceArt/Tools/export_fbx.py -- --all
```
It applies rotation/scale (aborts if the geometry moves), renames the root to the asset name (an armature becomes `Armature`), merges duplicate materials (`RG_Polymer.001`, `MAT_Navy_Fabric__guns_tmp`) and renames them `M_<Prefix>_…`, drops unused material slots, adds a **placeholder** Smart UV Project map where a mesh has none, and with `--sockets` adds `Socket_Muzzle` at the front-most point (−Y). It writes a new `.blend`, so re-running it **overwrites manual edits** in that file.

### Birds (`rig_bird.py`)
Birds arrive as separate parts under an Empty, without an armature. Parts are named `<Prefix>_<Part>`: `Bird_Body`, `Hawk_Wing_L`, … (Body, Head, Beak, Eyes, Tail, Wing_L/R, Leg_L/R, Foot_L/R, Feather_Details, and optionally Primary_L/R). `rig_bird.py` reuses the `ingest_fbx.py` helpers and also rigs them. The source can be an `.fbx` or a `.blend`; cameras and lights in a `.blend` are skipped.
```bash
# base bird (gull) -> SK_Bird
blender --background --factory-startup --python SourceArt/Tools/rig_bird.py -- --source ../FBX_Files/AirportBird_Base.fbx --blend SourceArt/Blender/Birds/SK_Bird.blend
# hawk (own model) -> SK_Bird_Hawk, materials M_Bird_Hawk_*
blender --background --factory-startup --python SourceArt/Tools/rig_bird.py -- --source ../FBX_Files/AirportBird_Hawk_Head_Refined.blend --blend SourceArt/Blender/Birds/SK_Bird_Hawk.blend --asset SK_Bird_Hawk --material-prefix M_Bird_Hawk_
blender SourceArt/Blender/Birds/SK_Bird_Hawk.blend --background --python SourceArt/Tools/export_fbx.py -- --all
```
- **Every bird gets the same bone names, mesh name (`Bird_Mesh`) and blend shape names**, so one set of animation clips fits all of them.
- **Pivot = centre of mass** (centre of the body's bounding box). The feet are 0.254 m below it.
- **9 deform bones**, each placed at the origin the modeller gave that part: `Body` (root, at the pivot) → `Head`, `Tail`, `Wing_L/R`, `Leg_L/R` → `Foot_L/R`.
- **Rigid skinning**: each part is weighted 100% to its bone. The body and feather details blend Body→Head across the neck, and the thigh feather ruff follows the legs.
- The parts are **joined into one mesh** `Bird_Mesh`, so Unity gets one SkinnedMeshRenderer per bird. The shape keys `Wings_Spread` and `Tail_Spread` become blend shapes.
- Wings are one rigid piece: they rotate at the shoulder but don't fold. Hand-painted weights or 2-bone wings can replace this later with the same bone and file names.
- Like `ingest_fbx.py`, re-running it **overwrites** the output `.blend`.

### FBX preset applied by the script
| Setting | Value | Why |
|---|---|---|
| Object types | Mesh + Empty (+ Armature for `SK_`) | No cameras/lights in game assets |
| Apply Scalings | **FBX All** | Imported objects get scale (1,1,1) in Unity, not 100 / 0.01 |
| Forward / Up | **−Z / Y** | Blender Z-up → Unity Y-up; Blender −Y front → Unity +Z |
| Apply Transform | On | Bakes the axis conversion into the mesh. No −90° X rotation on import |
| Apply Modifiers | On | Mirror / bevel etc. are applied in the exported mesh only |
| Smoothing | Face | Unity reads the smoothing groups correctly |
| Add Leaf Bones | Off | No useless `_end` bones |
| Deform bones only | On | IK / control bones stay in Blender |
| Bake Animation | `SK_` only, NLA strips as takes | Static meshes carry no animation data |
| Embed textures | Off | Textures are imported separately as `T_` files |

### Pre-export checks (never modify the scene)
| Check | Result |
|---|---|
| Name has no `SM_`/`SK_` prefix or unknown second token | **Error** |
| Object scale ≠ 1 | **Error** |
| Mesh has no UV map | **Error** |
| `SK_` without armature (unless `--mixamo`) / `SM_` with armature | **Error** |
| Rotation not applied, root object not at world origin | Warning |
| Triangles / deform bones over budget | Warning |
| More than 2 material slots on a mesh (not checked on atlas assets: the export merges the slots) | Warning |
| Material without the `M_` prefix / empty slot | Warning |

## 4. Budgets

| Asset | Max tris | Max bones | Texture |
|---|---|---|---|
| `SK_Bird*` (base + hawk) | none (reported only) | 15 | Own atlas 256² (Albedo + Mask; one Albedo per species) |
| `SM_Gun_Tier*` | none (reported only; was 2,500) | — | Own atlas 128² (Albedo + Mask) |
| `SK_Player_Officer` | none (reported only; was 8,000) | 52-bone Humanoid | Own atlas 512² (Albedo + Mask) |
| `SM_Prop_*` | none (reported only; was 500) | — | Own atlas (add the asset to `ATLAS` in `bake_atlas.py`) |

Atlas sizes are the smallest that keep the UV islands 2 texels apart; they are set in `bake_atlas.py`, not here.

No model has a triangle limit (decided 11 Oct, see `DECISIONS.md`): the Art Budget Checker and the exporter still report every count, they just never flag it. `Tools/hidden_faces_report.py` shows where triangles are spent on faces nobody can see.

The same numbers live in `export_fbx.py` (`BUDGETS`) and `Assets/_Project/Editor/Art/ArtBudgets.cs`. Change all three together.

## 5. Unity import (automatic)

`Assets/_Project/Editor/Art/` re-applies these settings on **every** import. Don't edit them in the Inspector, because they will be overwritten. Change the scripts (and bump `GetVersion()`) instead.

**Models** (`Art/Models/`, `Art/Animations/`) — `ArtModelPostprocessor.cs`
- Convert Units on, scale factor 1, no cameras/lights, Read/Write off.
- Materials: each FBX material slot is mapped **by name** to `Art/Materials/<same folder as the model>/<name>.mat` (e.g. `Models/Weapons/SM_Gun_Tier1.fbx` → `Materials/Weapons/M_Gun_Tier1_Atlas.mat`). No embedded copies.
- `Socket_*` transforms lose Blender's `.001` suffix, so every gun has a `Socket_Muzzle`.
- `SM_`: Rig = None, no animation, mesh compression Low, Generate Lightmap UVs only in `Models/Environment` and `Models/Props`.
- `SK_Player*`: Rig = **Humanoid**. Other `SK_`: Rig = **Generic**. Both: blend shapes on, mesh compression off.
- Clips are rebuilt from the FBX every import. Names containing Idle/Walk/Run/Jog/Sprint/Strafe/Fly/Glide/Hover/Loop loop; others (Dive, Hit, Death…) don't. Mixamo `mixamo.com` takes are renamed to the file name.
- Logs a warning if tris/bones exceed the budget.

**Materials** (`Art/Materials/`) — `ArtMaterialBuilder.cs`
- When a `*.materials.json` manifest is imported, it creates or updates one URP Lit material per entry: Blender base colour (linear → sRGB), metallic, smoothness = 1 − roughness, emission (keyword on only if non-black), alpha < 1 → Transparent surface.
- An atlas entry (`M_<Asset>_Atlas`) names its textures instead of a colour: Base Map = `T_…_Atlas_Albedo`, Metallic Map = `T_…_Atlas_Mask`, both looked up in `Art/Textures/<same folder>/`. An atlas texture that arrives after its manifest rebuilds that folder's materials.
- Values are overwritten on every export. Change colours in Blender, not in the Inspector.
- New materials trigger a re-import of the matching model, run right after the current refresh has finished. If a model ever shows a plain light-grey fallback material, **Tools → S3 → Rebuild Materials From Manifests** rebuilds everything and re-imports every model.

**Prefabs** — `ArtPrefabBuilder.cs`
- **Tools → S3 → Build Model Prefabs** creates `Prefabs/Weapons/P_Gun_Tier1-4` (model + fitted BoxCollider on the root), `Prefabs/Player/P_Player_Officer` (model with the importer's Animator + Humanoid avatar) and `Prefabs/Birds/P_Bird_Base` + `P_Bird_Hawk` (model with the Animator + Generic avatar, a body CapsuleCollider along +Z on the root).
- The model is a nested prefab, so a Blender re-export updates the prefab. Existing prefabs are skipped: delete one to rebuild it.

**Bird species** — `ArtBirdSpeciesBuilder.cs`
- **Tools → S3 → Build Bird Species Variants** creates or updates the Prefab Variants `Prefabs/Birds/P_Bird_Gull`, `_Pigeon` and `_Crow` of `P_Bird_Base`. Each gets its uniform scale, its atlas material (`Materials/Birds/M_Bird_Atlas`, `M_Bird_Pigeon_Atlas`, `M_Bird_Crow_Atlas`) and its blend shape weights (`Wings_Spread` 100, `Tail_Spread` per species).
- The hawk has its own model: `P_Bird_Hawk` comes from **Build Model Prefabs**, and this menu only sets its scale and blend shapes. Run Build Model Prefabs first.
- Species **colours** live in `SourceArt/Tools/bird_species.json`: change one there, export `SK_Bird` again, and the species Albedo is rebuilt. Species **scale and shape** live in the table at the top of the script: change them there and re-run the menu. Only scale, material and blend shapes are overwritten; components teammates add to a variant are kept.

**Art Budget Checker** — `ArtBudgetWindow.cs`
- **Tools → S3 → Art Budget Checker** lists every model (tris, verts, submeshes ≈ draw calls, materials, bones, mesh GPU memory) and texture (imported size, format, mips, memory) against the budgets, with over-budget rows in red.
- **Export report** writes `SourceArt/budget_report.md`. Export it before and after each optimisation pass and commit it: that's the viva evidence.

**Performance Test** — `ArtPerfTest.cs`
- **Tools → S3 → Performance Test → Create Test Wave (40 / 100 birds)** puts `P_Player_Officer` and an equal mix of Gull / Pigeon / Crow / Hawk inside the main camera's view (same layout every run, fixed seed). The root `_ArtPerfTest` is tagged EditorOnly. **Remove Test Wave** deletes it; don't save the scene with it.
- **Measuring starts automatically** when you press Play in a scene that contains `_ArtPerfTest` (the menu **Measure (Play Mode)** does the same by hand): 2 s warm-up, then 10 s of samples. Average and lowest FPS, CPU / render-thread ms, tris, draw calls, SetPass calls, SRP Batcher draw calls, shadow casters and visible skinned meshes are appended to `SourceArt/perf_report.md`. Untick `_ArtPerfTest` before pressing Play to get a baseline row.
- It's the evidence for "no triangle limits" (`DECISIONS.md`, 11 Oct). Editor Play mode includes editor overhead, so a build is usually faster.

**Textures** (`Art/Textures/`) — `ArtTexturePostprocessor.cs`
- Max size by sub-folder: Characters 1024, Birds 1024, Weapons 512, Props 512, Environment 2048, Shared 1024. Names containing `Palette` → 256.
- `*_Normal` → Normal Map. `*_Mask`, `_ORM`, `_Roughness`, `_Metallic`, `_AO`, `_Height` → sRGB off.
- Mipmaps on, compression Normal Quality (BC/DXT), Read/Write off.
- Palette: Point filter + Clamp. Other textures: Bilinear + Repeat.
- Names containing `_Atlas_` (written by `bake_atlas.py`): **Point filter, Clamp, no mipmaps, High Quality compression (BC7)**. Each UV island is one flat value 2 texels from the next island, so Bilinear or a smaller mip would blend neighbouring islands together. Don't change these in the Inspector.

## 6. Sandbox

In `Scenes/Sandbox/Sandbox_Basidh.unity` run **Tools → S3 → Create Art Test Rig**. It adds a 20 m ground plane, a 1.8 m player capsule, a 1 m reference cube, and a blue +Z (forward) and red +X arrow. The rig is tagged EditorOnly, so it is never in a build.
