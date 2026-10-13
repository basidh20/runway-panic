"""Unwrap a bird (SK_Bird or SK_Bird_Hawk: one skinned mesh `Bird_Mesh` with shape keys) with the SAME scripted seam
rules as the player, then pack it into its own 0-1 UV square. The rule code lives in unwrap_player.py; this script only
changes what is different about a bird:
  - Rule 2 (sharp edges > 60 deg are seams) applies to the hard keratin parts, picked by material name: beak, cere,
    claws / talons. Feathers, legs and eyes are treated as organic (rule 3).
  - Eyes are not projected separately: the eye materials (iris, pupil, eyelid / eye ring) are separate patches through
    rule 1 and are unwrapped like the rest.
  - Visibility (where rule 3 hides its cuts, and rule 7's "fully hidden"): a bird flies over the player, so it is seen
    mostly from below and from the side, rarely from above. It is measured in every pose the bird takes (the basis,
    each shape key at full strength, all keys together) and a face counts as visible if it is visible in any pose,
    so nothing that shows when the wings spread is treated as hidden.
Shape keys are part of the safety check (names, relative keys, values and every key's vertex positions).

Usage (Blender 5.2; shapely must be importable, see SourceArt/README.md):
  blender --background --python-use-system-env --python unwrap_bird.py -- <in.blend> <out.blend> [--margin 0.004] [--hidden-scale 0.5] [--report report.json]
"""
import math
import os
import sys

import bpy
import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import unwrap_player as up  # noqa: E402  (same rules, same checks, same metrics)

HARD_MATERIAL_WORDS = ("Beak", "Cere", "Claw", "Talon")   # rule 2 applies to faces of these materials
RAY_OFFSET = 0.0005                                         # m; feathers are thin, so start rays just above the face
DEFAULT_MARGIN = 0.004                                      # rule 6 for one bird in its own square (see DECISIONS.md)


def bird_dir_weights(d):
    """Camera likelihood for a flying bird: from below 1.0, from the side 0.8, from above 0.3."""
    return np.where(d[:, 2] < -0.2, 1.0, np.where(d[:, 2] > 0.5, 0.3, 0.8))


_rule_1_2 = up.initial_seams


def bird_initial_seams(bm, hard):
    """Rule 1 for every face; rule 2 only on edges whose two faces both belong to a hard (keratin) material."""
    seam, counts = _rule_1_2(bm, False)
    mesh = bird_mesh()
    hard_idx = {i for i, m in enumerate(mesh.data.materials) if m and any(w in m.name for w in HARD_MATERIAL_WORDS)}
    n = 0
    for e in bm.edges:
        lf = e.link_faces
        if (len(lf) == 2 and not seam[e.index] and lf[0].material_index in hard_idx and lf[1].material_index in hard_idx
                and math.degrees(e.calc_face_angle(0)) > up.SHARP_DEG):
            seam[e.index] = True
            n += 1
    counts["sharp_edge"] = counts.get("sharp_edge", 0) + n
    return seam, counts


def bird_mesh():
    return [o for o in bpy.data.collections[up.COLLECTION].all_objects if o.type == "MESH"][0]


def poses(me):
    """Vertex positions of every pose: basis, each shape key alone at 1.0, all keys at 1.0. Read only."""
    n = len(me.vertices)
    base = up._get(me.vertices, "co", np.float32, n, 3).reshape(-1, 3).astype(np.float64)
    out = [base]
    sk = me.shape_keys
    if sk:
        kb = sk.key_blocks
        deltas = []
        for k in list(kb)[1:]:
            co = up._get(k.data, "co", np.float32, n, 3).reshape(-1, 3).astype(np.float64)
            rel = up._get(k.relative_key.data, "co", np.float32, n, 3).reshape(-1, 3).astype(np.float64)
            deltas.append(co - rel)
        out += [base + d for d in deltas]
        if len(deltas) > 1:
            out.append(base + sum(deltas))
    return out


def bird_visibility(all_meshes, wanted):
    """Per face: largest (over all poses) weighted share of directions from which the face is seen from outside."""
    D = up.fib_dirs(up.N_DIRS)
    W = bird_dir_weights(D)
    DV = [Vector(d) for d in D]
    out = {}
    for o in wanted:
        me = o.data
        mw = np.array(o.matrix_world)
        me.calc_loop_triangles()
        tris = up._get(me.loop_triangles, "vertices", np.int32, len(me.loop_triangles), 3).reshape(-1, 3)
        nf = len(me.polygons)
        ls = up._get(me.polygons, "loop_start", np.int64, nf)
        lt = up._get(me.polygons, "loop_total", np.int64, nf)
        lv = up._get(me.loops, "vertex_index", np.int64, len(me.loops))
        pol = np.repeat(np.arange(nf), lt)
        idx = np.arange(len(lv))
        nxt = ls[pol] + (idx - ls[pol] + 1) % lt[pol]
        best = np.zeros(nf)
        for co in poses(me):
            P = co @ mw[:3, :3].T + mw[:3, 3]
            tree = BVHTree.FromPolygons([tuple(v) for v in P.tolist()], [tuple(t) for t in tris.tolist()])
            a, b = P[lv], P[lv[nxt]]
            nor = np.zeros((nf, 3))
            np.add.at(nor, pol, np.cross(a, b))                       # Newell normal of each polygon
            ln = np.linalg.norm(nor, axis=1, keepdims=True)
            nor = nor / np.where(ln == 0, 1, ln)
            cen = np.zeros((nf, 3))
            np.add.at(cen, pol, a)
            cen /= lt[:, None]
            facing = nor @ D.T > 0.1
            vis = np.zeros(nf)
            for i in range(nf):
                p = Vector(cen[i] + nor[i] * RAY_OFFSET)
                acc = 0.0
                for k in np.nonzero(facing[i])[0]:
                    if tree.ray_cast(p, DV[k], 100.0)[0] is None:
                        acc += W[k]
                vis[i] = acc / W.sum()
            best = np.maximum(best, vis)
        out[o.name] = best
    return out


def collection_of(path):
    bpy.ops.wm.open_mainfile(filepath=path)
    names = [c.name for c in bpy.data.collections if c.name.startswith("SK_Bird")]
    if len(names) != 1:
        raise SystemExit("expected exactly one SK_Bird* collection in %s, found %s" % (path, names))
    return names[0]


if __name__ == "__main__":
    up.COLLECTION = collection_of(up.SRC)
    up.HARD = set()
    up.EYES = set()
    up.MARGIN = up.option("--margin", DEFAULT_MARGIN)
    up.initial_seams = bird_initial_seams
    up.compute_visibility = bird_visibility
    up.dir_weights = bird_dir_weights
    up.main()
