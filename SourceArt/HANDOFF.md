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
