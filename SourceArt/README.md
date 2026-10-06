# SourceArt — Core Developer art pipeline

Source files for every custom model. This folder is **outside `Assets/`**, so Unity never imports `.blend` files directly. Only the exported `.fbx` files go into `Assets/_Project/Art/`.

| Folder | Contents |
|---|---|
| `Blender/{Birds,Characters,Weapons,Props}/` | `.blend` source files (Git LFS, one owner per file) |
| `Textures/` | Layered / high-res texture sources (`.psd`, `.kra`, bakes) |
| `Reference/` | Reference images and sheets |
| `Tools/export_fbx.py` | Blender → Unity FBX exporter with pre-export checks |
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

## 4. Budgets

| Asset | Max tris | Max bones | Texture |
|---|---|---|---|
| `SK_Bird` (shared base for 4 species) | 1,500 | 15 | Shared palette (256²) |
| `SM_Gun_Tier*` | 2,500 each | — | Shared palette (256²) |
| `SK_Player_Officer` | 8,000 | Mixamo rig | up to 1024² |
| `SM_Prop_*` | 500 | — | Shared palette |

The same numbers live in `export_fbx.py` (`BUDGETS`) and `Assets/_Project/Editor/Art/ArtBudgets.cs`. Change all three together.

## 5. Unity import (automatic)

`Assets/_Project/Editor/Art/` re-applies these settings on **every** import. Don't edit them in the Inspector, because they will be overwritten. Change the scripts (and bump `GetVersion()`) instead.

**Models** (`Art/Models/`, `Art/Animations/`) — `ArtModelPostprocessor.cs`
- Convert Units on, scale factor 1, no cameras/lights, Material Creation Mode = None (hand-made `M_` URP Lit materials), Read/Write off.
- `SM_`: Rig = None, no animation, mesh compression Low, Generate Lightmap UVs only in `Models/Environment` and `Models/Props`.
- `SK_Player*`: Rig = **Humanoid**. Other `SK_`: Rig = **Generic**. Both: blend shapes on, mesh compression off.
- Clips are rebuilt from the FBX every import. Names containing Idle/Walk/Run/Jog/Sprint/Strafe/Fly/Glide/Hover/Loop loop; others (Dive, Hit, Death…) don't. Mixamo `mixamo.com` takes are renamed to the file name.
- Logs a warning if tris/bones exceed the budget.

**Textures** (`Art/Textures/`) — `ArtTexturePostprocessor.cs`
- Max size by sub-folder: Characters 1024, Birds 1024, Weapons 512, Props 512, Environment 2048, Shared 1024. Names containing `Palette` → 256.
- `*_Normal` → Normal Map. `*_Mask`, `_ORM`, `_Roughness`, `_Metallic`, `_AO`, `_Height` → sRGB off.
- Mipmaps on, compression Normal Quality (BC/DXT), Read/Write off.
- Palette: Point filter + Clamp. Other textures: Bilinear + Repeat.

## 6. Sandbox

In `Scenes/Sandbox/Sandbox_Basidh.unity` run **Tools → S3 → Create Art Test Rig**. It adds a 20 m ground plane, a 1.8 m player capsule, a 1 m reference cube, and a blue +Z (forward) and red +X arrow. The rig is tagged EditorOnly, so it is never in a build.
