# Hand-off notes (Core Developer → team)

Requests and delivery notes for other members. Newest first. Never edit another member's files directly — write the proposed change here.

Template:
```markdown
## <YYYY-MM-DD> — <to: World Builder | Systems Engineer | Agent Controller> — <subject>
- **What:** prefab / asset path
- **Details:** pivot, forward axis, scale, collider, clip names, …
- **Action needed:** what the other member should do
- **Status:** open | done
```

---

## 2026-10-13 — to: all — models now use one atlas material each (old `M_*` colour materials are going away)
- **What:** every custom model (`P_Player_Officer`, `P_Bird_Gull` / `_Pigeon` / `_Crow` / `_Hawk`, `P_Gun_Tier1`–`4`) now renders with one material `M_<Asset>_Atlas` and two small textures, instead of one material per colour.
- **Details:**
  - Prefab paths, pivots, sockets, bones, blend shapes, colliders and the look are **unchanged**. Only the materials changed.
  - New materials: `Materials/Birds/M_Bird_Atlas`, `M_Bird_Pigeon_Atlas`, `M_Bird_Crow_Atlas`, `M_Bird_Hawk_Atlas`; `Materials/Characters/M_Player_Officer_Atlas` (plus `M_Player_Badge_Clear`, `M_Player_LED_Green`, `M_Player_Eye_Catch`, `M_Player_Vest_HiVis`); `Materials/Weapons/M_Gun_Tier1_Atlas` … `M_Gun_Tier4_Atlas`.
  - The old flat materials (`M_Bird_Feather_*`, `M_Gun_Polymer`, `M_Player_Navy_Fabric`, … 109 files) will be **deleted** in a clean-up commit.
  - Each bird and gun is now 1 draw call per pass (was 7–14), the player 26 (was 76).
- **Action needed:**
  - If a scene, prefab or script of yours references one of the old `M_*` colour materials directly, tell me before the clean-up; use the prefab instead.
  - Don't change the import settings of `T_*_Atlas_*` textures (Point, no mipmaps) or edit the atlas materials in the Inspector. To change a colour, ask me: it is changed in Blender and re-exported.
  - If a model ever shows up plain light grey after a pull, run **Tools → S3 → Rebuild Materials From Manifests**.
- **Status:** open

## 2026-10-11 — to: Systems Engineer — gun prefabs (Tier 1–4)
- **What:** `Assets/_Project/Prefabs/Weapons/P_Gun_Tier1.prefab` … `P_Gun_Tier4.prefab`

| Prefab | Gun | Length (Z) | `Socket_Muzzle` (local) | Tris | Materials |
|---|---|---:|---|---:|---:|
| `P_Gun_Tier1` | Pistol | 0.21 m | (0, 0.077, 0.162) | 820 | 7 |
| `P_Gun_Tier2` | Assault rifle | 1.08 m | (0, 0.097, 0.711) | 2,064 | 8 |
| `P_Gun_Tier3` | Sniper | 1.47 m | (0, 0.097, 1.108) | 6,304 | 10 |
| `P_Gun_Tier4` | RPG | 1.57 m | (0, 0.075, 1.040) | 1,664 | 7 (+3 rocket) |

- **Details:**
  - **Pivot = the grip** (where the hand holds it). Forward = **+Z** (barrel), up = +Y, real-world size, scale (1, 1, 1).
  - To hold a gun, parent the prefab to the hand bone (`RightHand` on `P_Player_Officer`'s Humanoid rig) at local position 0. Fine-tune the rotation once, on a hand-socket child Transform. Don't rotate the gun prefab itself.
  - `Socket_Muzzle` is a child Transform at the barrel tip. Spawn bullets / muzzle flash / raycasts from `Socket_Muzzle.position` along `Socket_Muzzle.forward` (= gun +Z).
  - Root = gameplay object with one fitted **BoxCollider** (no MeshCollider: cheaper for pickups and thrown guns). Add your Rigidbody / scripts on the root; the model is a nested child, so a Blender re-export never removes your components.
  - The RPG's rocket is part of the Tier 4 mesh. Tell me if you need it as a separate object (to hide it after firing or fly it as a projectile).
  - Tier 3 (sniper) is over its 2,500-tri budget and will get lighter in my optimisation pass. The prefab path, pivot and socket stay the same.
- **Action needed:** use these prefabs in the shop / weapon system. Tell me if you need more sockets (`Socket_Grip_Left` for two-handed guns, `Socket_Scope`, `Socket_Eject`).
- **Status:** open

## 2026-10-11 — to: Agent Controller + Hawk owner — the hawk is now its own model
- **What:** `Assets/_Project/Prefabs/Birds/P_Bird_Hawk.prefab` (model `Art/Models/Birds/SK_Bird_Hawk.fbx`) replaces the recoloured gull variant. It is **no longer a variant of `P_Bird_Base`**.
- **Details:**
  - It has the **same bone names** (`Body` → `Head`, `Tail`, `Wing_L/R`, `Leg_L/R` → `Foot_L/R`), the same mesh name (`Bird_Mesh`) and the same blend shapes (`Wings_Spread`, `Tail_Spread`). The pivot is also at the centre of mass.
  - Size: 0.56 m beak to tail, 1.51 m wingspan (scale 1.0). The feet are 0.238 m below the pivot.
  - Because the bone paths match, **the same clips play on the hawk**. Key **rotations only** (and blend shapes); the hawk's bones sit in slightly different places, so keyed bone positions would pull its body out of shape.
- **Action needed:** assign the bird Animator Controller to `P_Bird_Hawk` as well as `P_Bird_Base` (the Animator is on the model child, `SK_Bird_Hawk`). Anything else added to `P_Bird_Base` must also be added to `P_Bird_Hawk`.
- **Status:** open

## 2026-10-08 — to: Agent Controller + all — 4 species prefabs; bird animation clips are yours
- **What:** `Assets/_Project/Prefabs/Birds/P_Bird_Gull`, `P_Bird_Pigeon`, `P_Bird_Crow`, `P_Bird_Hawk` — Prefab Variants of `P_Bird_Base`
- **Details:**
  - Same mesh, rig and bones as `P_Bird_Base` (see the entry below). They differ only in scale (Gull 1.0, Pigeon 0.6, Crow 0.8, Hawk 1.2 on the root), materials and blend shapes.
  - The variants have `Wings_Spread` = 100 (flying pose) and a per-species `Tail_Spread`. If your clips key these blend shapes, the clip wins while it plays.
  - The root scale also scales the CapsuleCollider. Don't reset the root scale in code; move or rotate only.
- **Action needed:** (team decision, 8 Oct) **the Agent Controller makes the bird clips** (`A_Bird_Fly`, `A_Bird_Glide`, `A_Bird_Dive`, `A_Bird_Hit`, `A_Bird_Death`) and the Animator Controller. Put the controller on **`P_Bird_Base`** so all 4 species get it. Each IS owner adds their species AI script to their own variant.
- **Status:** open

## 2026-10-08 — to: Agent Controller — base bird model + rig (`P_Bird_Base`)
- **What:** `Assets/_Project/Prefabs/Birds/P_Bird_Base.prefab` (model: `Assets/_Project/Art/Models/Birds/SK_Bird.fbx`)
- **Details:**
  - Rig = **Generic**, Animator + avatar on the model child; no Animator Controller and no clips yet.
  - **Pivot = centre of mass** (middle of the body), not the feet. Put the prefab root on the path waypoint. The feet hang 0.254 m below it.
  - Forward = **+Z** (beak), up = +Y. Size: about 0.6 m beak to tail, 0.43 m tall (gull-sized).
  - Bones: `Body` (root) → `Head`, `Tail`, `Wing_L`, `Wing_R`, `Leg_L` → `Foot_L`, `Leg_R` → `Foot_R`. Rigid parts: rotate a wing at the shoulder to flap. Wings do not fold.
  - Blend shapes on `Bird_Mesh`: `Wings_Spread`, `Tail_Spread` (0–100). Spread the wings for flight with `SkinnedMeshRenderer.SetBlendShapeWeight`, or key them in a clip.
  - The rest pose stands with the legs down. While flying, tuck the legs in the fly clip: swing `Leg_L/R` about 60° backward, toward the tail (check which local axis that is in the Animation window).
  - Collider: one CapsuleCollider (radius 0.12 m, along Z) on the prefab root, around the body only.
  - 13 materials `M_Bird_*` (shared). For the 4 species: make **prefab variants** of `P_Bird_Base` with scale + material swaps. Ask me before changing the base prefab.
- **Action needed:** build the fly/death clips + Animator Controller on a variant or on the root, and tell me if a bone is missing (up to 15 are allowed: 6 still free).
- **Status:** open
