# SourceArt — Core Developer art pipeline

Source files for every custom model. This folder is **outside `Assets/`**, so Unity never imports `.blend` files directly. Only the exported `.fbx` files go into `Assets/_Project/Art/`.

| Folder | Contents |
|---|---|
| `Blender/{Birds,Characters,Weapons,Props}/` | `.blend` source files (Git LFS, one owner per file) |
| `Textures/` | Layered / high-res texture sources (`.psd`, `.kra`, bakes) |
| `Reference/` | Reference images and sheets |
| `Tools/export_fbx.py` | Blender → Unity FBX exporter with pre-export checks |
| `Tools/ingest_fbx.py` | Turns a raw FBX (made outside this pipeline) into a pipeline-ready `.blend` |
| `Tools/rig_bird.py` | Turns the raw base-bird FBX (separate parts, no armature) into a rigged `SK_Bird.blend` |
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
| `SK_Bird` | `Assets/_Project/Art/Models/Birds/SK_Bird.fbx` |
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
| `--out DIR` | override the output folder |

Exit code is 1 if any asset failed its checks.

Every export also writes a **material manifest** `Assets/_Project/Art/Materials/<Folder>/<Asset>.materials.json` (base colour, metallic, roughness, alpha, emission of each `M_` material). Unity builds the URP materials from it, so colours are authored in Blender only.

### Bringing in a raw FBX (made outside this pipeline)
FBX files exported with Blender's default settings have a 90° X rotation on every mesh, free-form names and often no UVs. Convert them once into a `.blend` with `ingest_fbx.py`, then export as usual:
```bash
blender --background --factory-startup --python SourceArt/Tools/ingest_fbx.py --     --blend SourceArt/Blender/Weapons/Guns.blend --material-prefix M_Gun_ --sockets     ../FBX_Files/pistol.fbx=SM_Gun_Tier1 ../FBX_Files/ar.fbx=SM_Gun_Tier2     ../FBX_Files/sniper.fbx=SM_Gun_Tier3 ../FBX_Files/rpg.fbx=SM_Gun_Tier4
blender SourceArt/Blender/Weapons/Guns.blend --background --python SourceArt/Tools/export_fbx.py -- --all
```
It applies rotation/scale (aborts if the geometry moves), renames the root to the asset name (an armature becomes `Armature`), merges duplicate materials (`RG_Polymer.001`, `MAT_Navy_Fabric__guns_tmp`) and renames them `M_<Prefix>_…`, drops unused material slots, adds a **placeholder** Smart UV Project map where a mesh has none, and with `--sockets` adds `Socket_Muzzle` at the front-most point (−Y). It writes a new `.blend`, so re-running it **overwrites manual edits** in that file.

### The base bird (`rig_bird.py`)
The bird FBX arrives as 13 separate parts under an Empty, without an armature. `rig_bird.py` reuses the `ingest_fbx.py` helpers and also rigs it:
```bash
blender --background --factory-startup --python SourceArt/Tools/rig_bird.py --     --fbx ../FBX_Files/AirportBird_Base.fbx --blend SourceArt/Blender/Birds/SK_Bird.blend
blender SourceArt/Blender/Birds/SK_Bird.blend --background --python SourceArt/Tools/export_fbx.py -- --all
```
- **Pivot = centre of mass** (centre of the body's bounding box). The feet are 0.254 m below it.
- **9 deform bones**, each placed at the origin the modeller gave that part: `Body` (root, at the pivot) → `Head`, `Tail`, `Wing_L/R`, `Leg_L/R` → `Foot_L/R`.
- **Rigid skinning**: each part is weighted 100% to its bone. The body and feather details blend Body→Head across the neck, and the thigh feather ruff follows the legs.
- The parts are **joined into one mesh** `Bird_Mesh`, so Unity gets one SkinnedMeshRenderer per bird. The shape keys `Wings_Spread` and `Tail_Spread` become blend shapes.
- Wings are one rigid piece: they rotate at the shoulder but don't fold. Hand-painted weights or 2-bone wings can replace this later with the same bone and file names.
- Like `ingest_fbx.py`, re-running it **overwrites** `SK_Bird.blend`.

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
| More than 2 material slots on a mesh | Warning |
| Material without the `M_` prefix / empty slot | Warning |

## 4. Budgets

| Asset | Max tris | Max bones | Texture |
|---|---|---|---|
| `SK_Bird` (shared base for 4 species) | none (reported only) | 15 | Shared palette (256²) |
| `SM_Gun_Tier*` | 2,500 each | — | Shared palette (256²) |
| `SK_Player_Officer` | 8,000 | Mixamo rig | up to 1024² |
| `SM_Prop_*` | 500 | — | Shared palette |

The same numbers live in `export_fbx.py` (`BUDGETS`) and `Assets/_Project/Editor/Art/ArtBudgets.cs`. Change all three together.

## 5. Unity import (automatic)

`Assets/_Project/Editor/Art/` re-applies these settings on **every** import. Don't edit them in the Inspector, because they will be overwritten. Change the scripts (and bump `GetVersion()`) instead.

**Models** (`Art/Models/`, `Art/Animations/`) — `ArtModelPostprocessor.cs`
- Convert Units on, scale factor 1, no cameras/lights, Read/Write off.
- Materials: each FBX material slot is mapped **by name** to `Art/Materials/<same folder as the model>/<name>.mat` (e.g. `Models/Weapons/SM_Gun_Tier1.fbx` → `Materials/Weapons/M_Gun_Polymer.mat`). No embedded copies, so all four guns share one `M_Gun_Polymer`.
- `Socket_*` transforms lose Blender's `.001` suffix, so every gun has a `Socket_Muzzle`.
- `SM_`: Rig = None, no animation, mesh compression Low, Generate Lightmap UVs only in `Models/Environment` and `Models/Props`.
- `SK_Player*`: Rig = **Humanoid**. Other `SK_`: Rig = **Generic**. Both: blend shapes on, mesh compression off.
- Clips are rebuilt from the FBX every import. Names containing Idle/Walk/Run/Jog/Sprint/Strafe/Fly/Glide/Hover/Loop loop; others (Dive, Hit, Death…) don't. Mixamo `mixamo.com` takes are renamed to the file name.
- Logs a warning if tris/bones exceed the budget.

**Materials** (`Art/Materials/`) — `ArtMaterialBuilder.cs`
- When a `*.materials.json` manifest is imported, it creates or updates one URP Lit material per entry: Blender base colour (linear → sRGB), metallic, smoothness = 1 − roughness, emission (keyword on only if non-black), alpha < 1 → Transparent surface.
- Values are overwritten on every export. Change colours in Blender, not in the Inspector.
- New materials trigger a re-import of the matching model. **Tools → S3 → Rebuild Materials From Manifests** rebuilds everything.

**Prefabs** — `ArtPrefabBuilder.cs`
- **Tools → S3 → Build Model Prefabs** creates `Prefabs/Weapons/P_Gun_Tier1-4` (model + fitted BoxCollider on the root), `Prefabs/Player/P_Player_Officer` (model with the importer's Animator + Humanoid avatar) and `Prefabs/Birds/P_Bird_Base` (model with the Animator + Generic avatar, a body CapsuleCollider along +Z on the root).
- The model is a nested prefab, so a Blender re-export updates the prefab. Existing prefabs are skipped: delete one to rebuild it.

**Art Budget Checker** — `ArtBudgetWindow.cs`
- **Tools → S3 → Art Budget Checker** lists every model (tris, verts, submeshes ≈ draw calls, materials, bones, mesh GPU memory) and texture (imported size, format, mips, memory) against the budgets, with over-budget rows in red.
- **Export report** writes `SourceArt/budget_report.md`. Export it before and after each optimisation pass and commit it: that's the viva evidence.

**Textures** (`Art/Textures/`) — `ArtTexturePostprocessor.cs`
- Max size by sub-folder: Characters 1024, Birds 1024, Weapons 512, Props 512, Environment 2048, Shared 1024. Names containing `Palette` → 256.
- `*_Normal` → Normal Map. `*_Mask`, `_ORM`, `_Roughness`, `_Metallic`, `_AO`, `_Height` → sRGB off.
- Mipmaps on, compression Normal Quality (BC/DXT), Read/Write off.
- Palette: Point filter + Clamp. Other textures: Bilinear + Repeat.

## 6. Sandbox

In `Scenes/Sandbox/Sandbox_Basidh.unity` run **Tools → S3 → Create Art Test Rig**. It adds a 20 m ground plane, a 1.8 m player capsule, a 1 m reference cube, and a blue +Z (forward) and red +X arrow. The rig is tagged EditorOnly, so it is never in a build.
