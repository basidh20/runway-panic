"""Unwrap SK_Player_Officer (21 skinned meshes) with scripted seam rules, then pack all of them into ONE shared 0-1 UV space.

The seams are placed by this script, not by hand in the Blender UI. The same rules run on every mesh:
  1. Material boundaries are seams (the natural colour-break lines). So is any edge shared by 3+ faces and any edge
     between two faces wound in opposite directions (those would fold).
  2. Hard-surface meshes (Boots, Cap, Headset, Radio, Knee_Pads, pouches, belt parts, ID_Badge):
     every edge sharper than SHARP_DEG is a seam.
  3. Every other patch (organic meshes: Head_Mesh, Body, Hair, Shirt, Pants, Gloves, Safety_Vest; and the patches the
     rules above leave as tubes, closed shells or patches with holes) is cut open along its LEAST VISIBLE edges.
     Visibility = rays shot at the whole assembled character from N_DIRS directions, weighted by how likely a camera is
     there (front > sides > back > underneath), so the cuts end up inside arms and legs, under clothing, at the back
     and under the chin without hard-coding a side per mesh. Method: spanning tree of the patch's dual graph that
     prefers to cross visible edges (tree-cotree); the cheap leftover edges are the cut, dead ends are pruned.
     Then every patch is unwrapped (angle based; conformal is tried at the end and kept if it measures better). A patch
     that is stretched or folds is cut again: split into two balanced halves along the cheapest hidden border; small
     folded patches first get their saddle vertices (corner angles > 360 deg) cut loose along short hidden paths, then
     are cut along their sharpest creases (40, 30, 20, ... degrees). Repeated up to MAX_ITERS times.
  4. Fallback: a mesh that still has folded or overlapping patches is redone with Smart UV Project (66 deg, margin 0.02)
     for that mesh only, and the report names it.
  5. Eyes (Eye_L, Eye_R): planar projection along Y (front hemisphere seen from the front, back hemisphere from behind).
  6. All meshes together: Average Islands Scale, then Pack Islands (rotate, margin MARGIN as a fraction of the square).
  7. Patches that no ray can reach (fully hidden under other meshes) get HIDDEN_SCALE of the linear scale before the pack,
     to free texture space.

Safety: geometry, rig, materials, weights and transforms are hashed before and after (and again after re-opening the saved
file); <out.blend> is written to a temp file first and only replaced when every check passes. Exit code 1 if one fails.

Usage (Blender 5.2; shapely must be importable, see SourceArt/README.md):
  blender --background --python-use-system-env --python unwrap_player.py -- <in.blend> <out.blend> [--margin 0.002] [--hidden-scale 0.5] [--report report.json]
"""
import hashlib
import heapq
import json
import math
import os
import struct
import sys
import time
from collections import Counter, defaultdict

import bmesh
import bpy
import numpy as np
import shapely
from mathutils import Vector
from mathutils.bvhtree import BVHTree
from shapely.geometry import Polygon

ARGS = sys.argv[sys.argv.index("--") + 1:]
SRC, DST = ARGS[:2]


def option(name, default):
    return type(default)(ARGS[ARGS.index(name) + 1]) if name in ARGS else default


HERE = os.path.dirname(os.path.abspath(globals().get("__file__") or os.path.join(os.path.dirname(SRC), "..", "..", "Tools", "uv", "x.py")))
sys.path.insert(0, HERE)
import uvmetrics  # noqa: E402  (the repo's metrics module, reused for every measurement)

COLLECTION = "SK_Player_Officer"
UV_NAME = "UVMap"
HARD = {"Boots", "Cap", "Headset", "Radio", "Knee_Pads", "Pouches_Black", "Pouches_Brown", "Pouch_Clips",
        "Belt", "Belt_Buckle", "Belt_Loops", "ID_Badge"}
EYES = {"Eye_L", "Eye_R"}          # everything else (Body, Head_Mesh, Hair, Shirt, Pants, Gloves, Safety_Vest) is organic

SHARP_DEG = 60.0                    # rule 2
N_DIRS = 48                         # rays per face for the visibility map
CUT_EPS = 0.05                      # floor of the visibility term in the cut cost: cost = length * (CUT_EPS + visibility)
MAX_ITERS = 24                      # rule 3: how often a stretched or folded patch is cut again
BISECT_FIRST_FACES = 120            # patches with at least this many faces that are broken or stretched are split in balanced halves first
SADDLE_TOL_DEG = 0.5                # rule 3: interior vertex whose corner angles add up to more than 360 + this is a saddle
CREASE_LIMITS = (40, 30, 20, 14, 10, 8, 6, 5, 4, 3)   # rule 3: a patch that still folds is cut along edges sharper than this
MIN_SPLIT_FACES = 12                # patches smaller than this are never bisected for stretch alone
ANG_BAD_DEG = 10.0                  # a face whose worst corner is off by more than this counts as stretched ...
FRAC_BAD_ANG = 0.25                 # ... and a patch with more than this share of such faces is "badly stretched"
SDLOG_BAD = 0.7                     # ... or whose face area ratio varies more than this (std of ln) is
MARGIN = option("--margin", 0.002)   # rule 6: gap between islands as a fraction of the UV square (see DECISIONS.md)
HIDDEN_VIS = 0.02                   # rule 7: a patch whose faces are all below this visibility counts as hidden
HIDDEN_SCALE = option("--hidden-scale", 0.5)   # rule 7: linear scale of hidden patches (1.0 switches the rule off)
T0 = time.time()


def log(*a):
    print("[%6.1fs]" % (time.time() - T0), *a, flush=True)


# ------------------------------------------------------------------------------------------ invariants
def _h(a):
    return hashlib.sha256(np.ascontiguousarray(a).tobytes()).hexdigest()[:20]


def _get(coll, attr, dtype, n, k=1):
    a = np.empty(n * k, dtype=dtype)
    coll.foreach_get(attr, a)
    return a


def snapshot():
    """Hash everything the UV pass must NOT change (seams and UVs are deliberately not part of it)."""
    s = dict(collections={c.name: sorted(o.name for o in c.objects) for c in bpy.data.collections},
             materials=sorted(m.name for m in bpy.data.materials), actions=sorted(a.name for a in bpy.data.actions),
             objects={})
    for o in sorted(bpy.data.objects, key=lambda o: o.name):
        d = dict(type=o.type, parent=o.parent.name if o.parent else None, parent_type=o.parent_type,
                 parent_bone=o.parent_bone,
                 matrix_world=_h(np.array([x for r in o.matrix_world for x in r], dtype=np.float64)),
                 matrix_parent_inverse=_h(np.array([x for r in o.matrix_parent_inverse for x in r], dtype=np.float64)),
                 location=list(o.location), rotation=list(o.rotation_euler), scale=list(o.scale),
                 users_collection=sorted(c.name for c in o.users_collection),
                 hide_viewport=o.hide_viewport, hide_render=o.hide_render,
                 modifiers=[(m.name, m.type, getattr(getattr(m, "object", None), "name", None)) for m in o.modifiers],
                 vertex_groups=[(g.name, g.index) for g in o.vertex_groups],
                 material_slots=[(sl.material.name if sl.material else None, sl.link) for sl in o.material_slots])
        if o.type == "MESH":
            me = o.data
            nv, ne, nf, nl = len(me.vertices), len(me.edges), len(me.polygons), len(me.loops)
            m = dict(verts=nv, edges=ne, faces=nf, loops=nl,
                     vert_co=_h(_get(me.vertices, "co", np.float32, nv, 3)),
                     edge_verts=_h(_get(me.edges, "vertices", np.int32, ne, 2)),
                     loop_vert=_h(_get(me.loops, "vertex_index", np.int32, nl)),
                     poly_loop_start=_h(_get(me.polygons, "loop_start", np.int32, nf)),
                     poly_loop_total=_h(_get(me.polygons, "loop_total", np.int32, nf)),
                     poly_material=_h(_get(me.polygons, "material_index", np.int32, nf)),
                     poly_smooth=_h(_get(me.polygons, "use_smooth", np.bool_, nf)),
                     has_custom_normals=bool(me.has_custom_normals),
                     materials=[mt.name if mt else None for mt in me.materials],
                     uv_layers=[u.name for u in me.uv_layers])
            try:
                m["edge_sharp"] = _h(_get(me.edges, "use_edge_sharp", np.bool_, ne))
            except Exception:
                m["edge_sharp"] = "n/a"
            d["mesh"] = m
            buf = bytearray()
            for v in me.vertices:
                for g in v.groups:
                    buf += struct.pack("<iif", v.index, g.group, g.weight)
            d["weights"] = hashlib.sha256(bytes(buf)).hexdigest()[:20]
            d["weight_entries"] = len(buf) // 12
            sk = me.shape_keys
            d["shape_keys"] = None if not sk else [
                dict(name=k.name, rel=k.relative_key.name, value=k.value, vg=k.vertex_group, mute=k.mute,
                     co=_h(_get(k.data, "co", np.float32, len(k.data), 3))) for k in sk.key_blocks]
        if o.type == "ARMATURE":
            d["bones"] = [dict(name=b.name, parent=b.parent.name if b.parent else None, deform=b.use_deform,
                               m=_h(np.array([x for r in b.matrix_local for x in r], dtype=np.float64)),
                               head=list(b.head_local), tail=list(b.tail_local)) for b in o.data.bones]
            d["pose"] = [dict(name=p.name, loc=list(p.location), rq=list(p.rotation_quaternion),
                              re=list(p.rotation_euler), sc=list(p.scale)) for p in o.pose.bones]
        s["objects"][o.name] = d
    return s


# requirement -> how to pull its value out of one object's snapshot (None = not applicable to that object)
def _mesh(key):
    return lambda d: d["mesh"][key] if "mesh" in d else None


REQUIREMENTS = [
    ("Vertex count", _mesh("verts")),
    ("Vertex positions (exact)", _mesh("vert_co")),
    ("Face count", _mesh("faces")),
    ("Face order / topology", lambda d: (d["mesh"]["loop_vert"], d["mesh"]["poly_loop_start"], d["mesh"]["poly_loop_total"],
                                         d["mesh"]["edge_verts"], d["mesh"]["edges"], d["mesh"]["loops"]) if "mesh" in d else None),
    ("Vertex groups (names, indices)", lambda d: d["vertex_groups"] if d["type"] == "MESH" else None),
    ("Vertex-group weights (every value)", lambda d: (d["weights"], d["weight_entries"]) if "weights" in d else None),
    ("Armature, bone names + transforms", lambda d: (d["bones"], d["pose"]) if d["type"] == "ARMATURE" else None),
    ("Object transforms", lambda d: (d["matrix_world"], d["matrix_parent_inverse"], d["location"], d["rotation"], d["scale"])),
    ("Parenting + modifiers", lambda d: (d["parent"], d["parent_type"], d["parent_bone"], d["modifiers"])),
    ("Material slots", lambda d: (d["material_slots"], d["mesh"]["materials"] if "mesh" in d else None)),
    ("Per-face material indices", _mesh("poly_material")),
    ("Shape keys (names + every position)", lambda d: d["shape_keys"] if d["type"] == "MESH" else None),
    ("Smoothing / sharp-edge flags", lambda d: (d["mesh"]["poly_smooth"], d["mesh"]["edge_sharp"], d["mesh"]["has_custom_normals"]) if "mesh" in d else None),
    ("Collection names + membership", lambda d: d["users_collection"]),
    ("Visibility flags", lambda d: (d["hide_viewport"], d["hide_render"])),
]


def compare(before, after):
    """-> list of (requirement, ok, detail)."""
    rows = []
    for name, get in REQUIREMENTS:
        bad = []
        for on, d in before["objects"].items():
            e = after["objects"].get(on)
            if e is None:
                bad.append(on + " (missing)")
            elif get(d) != get(e):
                bad.append(on)
        for on in after["objects"]:
            if on not in before["objects"]:
                bad.append(on + " (new object)")
        rows.append((name, not bad, ", ".join(bad[:6])))
    for key, label in (("collections", "Collection tree"), ("materials", "Material datablocks"), ("actions", "Actions")):
        rows.append((label, before[key] == after[key], "" if before[key] == after[key] else "differs"))
    uvbad = [on for on, d in after["objects"].items() if d["type"] == "MESH" and d["mesh"]["uv_layers"] != [UV_NAME]]
    rows.append(("UV layer: single layer named '%s'" % UV_NAME, not uvbad, ", ".join(uvbad[:6])))
    return rows


def print_table(title, rows):
    log(title)
    w = max(len(r[0]) for r in rows)
    for name, ok, detail in rows:
        print("   %-*s  %s  %s" % (w, name, "PASS" if ok else "FAIL", detail), flush=True)
    return all(r[1] for r in rows)


# ------------------------------------------------------------------------------------------ visibility
def fib_dirs(n):
    i = np.arange(n) + 0.5
    phi = math.pi * (3.0 - math.sqrt(5.0)) * i
    z = 1 - 2 * i / n
    r = np.sqrt(1 - z * z)
    return np.stack([r * np.cos(phi), r * np.sin(phi), z], 1)


def dir_weights(d):
    """Camera likelihood: the character faces -Y, so the front is seen most, the back less, from below almost never."""
    w = np.where(d[:, 1] < 0, 1.0, 0.35)
    return w * np.where(d[:, 2] < -0.2, 0.15, np.where(d[:, 2] > 0.5, 0.8, 1.0))


def compute_visibility(all_meshes, wanted):
    """Per face of the `wanted` meshes: share (0..1) of the weighted directions from which it is seen from outside,
    with every mesh of the character acting as an occluder."""
    verts, tris, off = [], [], 0
    for o in all_meshes:
        me = o.data
        mw = np.array(o.matrix_world)
        co = _get(me.vertices, "co", np.float32, len(me.vertices), 3).reshape(-1, 3).astype(np.float64)
        me.calc_loop_triangles()
        lt = _get(me.loop_triangles, "vertices", np.int32, len(me.loop_triangles), 3).reshape(-1, 3)
        verts.append(co @ mw[:3, :3].T + mw[:3, 3])
        tris.append(lt + off)
        off += len(co)
    tree = BVHTree.FromPolygons([tuple(v) for v in np.concatenate(verts).tolist()],
                                [tuple(t) for t in np.concatenate(tris).tolist()])
    D = fib_dirs(N_DIRS)
    W = dir_weights(D)
    DV = [Vector(d) for d in D]
    out = {}
    for o in wanted:
        me = o.data
        mw = np.array(o.matrix_world)
        n = len(me.polygons)
        nor = _get(me.polygons, "normal", np.float32, n, 3).reshape(-1, 3) @ mw[:3, :3].T
        cen = _get(me.polygons, "center", np.float32, n, 3).reshape(-1, 3) @ mw[:3, :3].T + mw[:3, 3]
        ln = np.linalg.norm(nor, axis=1, keepdims=True)
        nor = nor / np.where(ln == 0, 1, ln)
        facing = nor @ D.T > 0.1
        vis = np.zeros(n)
        for i in range(n):
            p = Vector(cen[i] + nor[i] * 0.002)
            acc = 0.0
            for k in np.nonzero(facing[i])[0]:
                if tree.ray_cast(p, DV[k], 100.0)[0] is None:
                    acc += W[k]
            vis[i] = acc / W.sum()
        out[o.name] = vis
    return out


# ------------------------------------------------------------------------------------------ per-mesh arrays + metrics
class MeshData:
    """Static arrays of one mesh (topology and 3D shape never change) for fast per-face UV metrics."""

    def __init__(self, obj):
        me = obj.data
        self.obj, self.me = obj, me
        nv, nf, nl = len(me.vertices), len(me.polygons), len(me.loops)
        self.nf, self.nl = nf, nl
        co = _get(me.vertices, "co", np.float32, nv, 3).reshape(-1, 3).astype(np.float64)
        self.ls = _get(me.polygons, "loop_start", np.int64, nf)
        self.lt = _get(me.polygons, "loop_total", np.int64, nf)
        self.lv = _get(me.loops, "vertex_index", np.int64, nl)
        self.area3 = _get(me.polygons, "area", np.float64, nf)
        self.pol = np.repeat(np.arange(nf), self.lt)
        idx = np.arange(nl)
        start, tot = self.ls[self.pol], self.lt[self.pol]
        self.prev = start + (idx - start - 1) % tot
        self.nxt = start + (idx - start + 1) % tot
        self.co = co
        P = co[self.lv]
        a, b = P[self.prev] - P, P[self.nxt] - P
        with np.errstate(all="ignore"):
            c = np.einsum("ij,ij->i", a, b) / (np.linalg.norm(a, axis=1) * np.linalg.norm(b, axis=1))
        self.ang3 = np.arccos(np.clip(np.nan_to_num(c), -1, 1))

    def read_uv(self):
        return _get(self.me.uv_layers[UV_NAME].data, "uv", np.float32, self.nl, 2).reshape(-1, 2).astype(np.float64)

    def write_uv(self, uv):
        self.me.uv_layers[UV_NAME].data.foreach_set("uv", uv.astype(np.float32).ravel())
        self.me.update()


def uv_islands(md, U):
    """Face -> UV island id: faces are joined when they share an edge whose two end points have the same UV on both sides
    (the same definition uvmetrics uses)."""
    ei = _get(md.me.loops, "edge_index", np.int64, md.nl)
    order = np.argsort(ei, kind="stable")
    _, first, counts = np.unique(ei[order], return_index=True, return_counts=True)
    two = first[counts == 2]
    l1, l2 = order[two], order[two + 1]
    a1 = md.lv[l1]
    same_dir = md.lv[l2] == a1
    ua2 = np.where(same_dir[:, None], U[l2], U[md.nxt[l2]])
    ub2 = np.where(same_dir[:, None], U[md.nxt[l2]], U[l2])
    ok = (np.abs(U[l1] - ua2).max(1) < 1e-6) & (np.abs(U[md.nxt[l1]] - ub2).max(1) < 1e-6)
    parent = list(range(md.nf))

    def find(x):
        while parent[x] != x:
            parent[x] = parent[parent[x]]
            x = parent[x]
        return x

    for a, b in zip(md.pol[l1[ok]].tolist(), md.pol[l2[ok]].tolist()):
        ra, rb = find(a), find(b)
        if ra != rb:
            parent[ra] = rb
    roots = np.array([find(i) for i in range(md.nf)])
    return np.unique(roots, return_inverse=True)[1]


def overlap_flags(md, U, rid):
    """Per face: True if its UV polygon overlaps another face of the SAME island/patch (same tolerances as uvmetrics).
    Overlaps between different islands do not count here: the final shared pack separates islands."""
    polys = []
    for s, t in zip(md.ls, md.lt):
        p = Polygon(U[s:s + t])
        polys.append(p if p.is_valid else p.buffer(0))
    arr = np.empty(len(polys), dtype=object)
    arr[:] = polys
    area = shapely.area(arr)
    tree = shapely.STRtree(arr)
    ii, jj = tree.query(arr, predicate="intersects")
    keep = (ii < jj) & (rid[ii] == rid[jj])
    keep &= rid[ii] == rid[jj]
    ii, jj = ii[keep], jj[keep]
    inter = shapely.area(shapely.intersection(arr[ii], arr[jj]))
    bad = (inter > 1e-7 * np.maximum(np.minimum(area[ii], area[jj]), 1e-12)) & (inter > 1e-10)
    flags = np.zeros(md.nf, bool)
    flags[ii[bad]] = True
    flags[jj[bad]] = True
    return flags


def evaluate(md, rid, nreg, with_overlap=True):
    """Per-patch quality after an unwrap: folded faces, overlaps, share of stretched faces, spread of the face area ratio."""
    U = md.read_uv()
    a2, b2 = U[md.prev] - U, U[md.nxt] - U
    na, nb = np.linalg.norm(a2, axis=1), np.linalg.norm(b2, axis=1)
    with np.errstate(all="ignore"):
        c = np.einsum("ij,ij->i", a2, b2) / (na * nb)
    err = np.degrees(np.abs(md.ang3 - np.arccos(np.clip(np.nan_to_num(c), -1, 1))))
    err[(na < 1e-12) | (nb < 1e-12)] = 180.0
    worst = np.maximum.reduceat(err, md.ls)
    cross = U[:, 0] * U[md.nxt, 1] - U[md.nxt, 0] * U[:, 1]
    a_uv = 0.5 * np.add.reduceat(cross, md.ls)
    cnt = np.bincount(rid, minlength=nreg)
    sgn = np.where(np.bincount(rid, weights=a_uv, minlength=nreg) < 0, -1.0, 1.0)
    fold = (a_uv * sgn[rid]) < -1e-12
    Auv = np.bincount(rid, weights=np.abs(a_uv), minlength=nreg)
    A3 = np.bincount(rid, weights=md.area3, minlength=nreg)
    with np.errstate(all="ignore"):
        r = (np.abs(a_uv) / Auv[rid]) / (md.area3 / A3[rid])
    r = np.where(np.isfinite(r) & (r > 0), r, 1.0)
    lr = np.log(np.clip(r, 1e-4, 1e4))
    m1 = np.bincount(rid, weights=lr, minlength=nreg) / np.maximum(cnt, 1)
    m2 = np.bincount(rid, weights=lr * lr, minlength=nreg) / np.maximum(cnt, 1)
    ov = overlap_flags(md, U, rid) if with_overlap else np.zeros(md.nf, bool)
    with np.errstate(all="ignore"):
        dens = np.sqrt(Auv / A3)
    return dict(cnt=cnt, worst=worst, dens=dens, A3=A3, Auv=Auv, fold=np.bincount(rid, weights=fold, minlength=nreg),
                ov=np.bincount(rid, weights=ov, minlength=nreg),
                badang=np.bincount(rid, weights=worst > ANG_BAD_DEG, minlength=nreg) / np.maximum(cnt, 1),
                sdlog=np.sqrt(np.maximum(m2 - m1 * m1, 0)), n_fold=int(fold.sum()), n_ov=int(ov.sum()),
                mean_worst=float(worst.mean()), n_stretched=int((worst > 2.0).sum()))


def bad_patches(ev):
    broken = (ev["fold"] > 0) | (ev["ov"] > 0)
    stretched = (ev["cnt"] >= MIN_SPLIT_FACES) & ((ev["badang"] > FRAC_BAD_ANG) | (ev["sdlog"] > SDLOG_BAD))
    return np.nonzero(broken | stretched)[0]


def score(ev):
    return (ev["n_fold"] + ev["n_ov"], ev["mean_worst"])


# ------------------------------------------------------------------------------------------ seams (bmesh, read-only on the mesh)
def regions_of(bm, seam):
    """Patches = faces connected through non-seam edges. -> (list of face-index lists, face -> patch id)."""
    rid = np.full(len(bm.faces), -1, np.int64)
    regs = []
    for f0 in bm.faces:
        if rid[f0.index] >= 0:
            continue
        cid = len(regs)
        rid[f0.index] = cid
        faces, stack = [f0.index], [f0]
        while stack:
            g = stack.pop()
            for e in g.edges:
                if seam[e.index]:
                    continue
                for h in e.link_faces:
                    if rid[h.index] < 0:
                        rid[h.index] = cid
                        faces.append(h.index)
                        stack.append(h)
        regs.append(faces)
    return regs, rid


def initial_seams(bm, hard):
    seam = np.zeros(len(bm.edges), bool)
    c = Counter()
    for e in bm.edges:
        lf = e.link_faces
        if len(lf) > 2:
            seam[e.index] = True
            c["non_manifold"] += 1
        elif len(lf) == 2:
            ll = e.link_loops
            if ll[0].vert == ll[1].vert:                     # neighbours wound in opposite directions: would fold
                seam[e.index] = True
                c["inconsistent_winding"] += 1
            elif lf[0].material_index != lf[1].material_index:
                seam[e.index] = True
                c["material_boundary"] += 1
            elif hard and math.degrees(e.calc_face_angle(0)) > SHARP_DEG:
                seam[e.index] = True
                c["sharp_edge"] += 1
    return seam, c


def dijkstra(adj, src, cost, block=None):
    dist, prev, heap = {src: 0.0}, {}, [(0.0, src)]
    while heap:
        d, v = heapq.heappop(heap)
        if d > dist[v] or (block is not None and v != src and v in block):
            continue
        for w, ei in adj[v]:
            nd = d + cost(ei)
            if nd < dist.get(w, 1e30):
                dist[w], prev[w] = nd, (v, ei)
                heapq.heappush(heap, (nd, w))
    return dist, prev


def path_edges(prev, a, b):
    out, v = [], b
    while v != a:
        v, ei = prev[v]
        out.append(ei)
    return out


def make_disk(bm, faces, seam, elen, evis):
    """Tree-cotree: the dual spanning tree that prefers visible edges leaves the hidden ones as the cut; dead ends are pruned.
    -> (edges to cut, patch has a boundary)."""
    boundary, dual = set(), set()
    for fi in faces:
        for e in bm.faces[fi].edges:
            if seam[e.index] or len(e.link_faces) != 2:
                boundary.add(e.index)
            else:
                dual.add(e.index)
    parent = {fi: fi for fi in faces}

    def find(x):
        while parent[x] != x:
            parent[x] = parent[parent[x]]
            x = parent[x]
        return x

    cut = set()
    for ei in sorted(dual, key=lambda ei: (-(elen[ei] * (CUT_EPS + evis[ei])), ei)):
        a, b = bm.edges[ei].link_faces
        ra, rb = find(a.index), find(b.index)
        if ra == rb:
            cut.add(ei)
        else:
            parent[ra] = rb
    inc = defaultdict(set)
    for ei in boundary | cut:
        for v in bm.edges[ei].verts:
            inc[v.index].add(ei)
    stack = [v for v, s in inc.items() if len(s) == 1]
    while stack:
        v = stack.pop()
        if len(inc[v]) != 1:
            continue
        ei = next(iter(inc[v]))
        if ei not in cut:
            continue
        cut.discard(ei)
        for w in bm.edges[ei].verts:
            inc[w.index].discard(ei)
            if len(inc[w.index]) == 1:
                stack.append(w.index)
    return cut, bool(boundary)


def region_graph(bm, faces, seam):
    """Adjacency over the patch's non-seam edges + the set of vertices that sit on its boundary."""
    adj, bverts, seen = defaultdict(list), set(), set()
    for fi in faces:
        for e in bm.faces[fi].edges:
            if e.index in seen:
                continue
            seen.add(e.index)
            a, b = e.verts[0].index, e.verts[1].index
            if seam[e.index] or len(e.link_faces) != 2:
                bverts.update((a, b))
            else:
                adj[a].append((b, e.index))
                adj[b].append((a, e.index))
    return adj, bverts


def slit(bm, faces, elen, evis, fvis):
    """A closed shell has no boundary: open it with one cut from its least visible vertex to the vertex farthest from it."""
    adj, _ = region_graph(bm, faces, np.zeros(len(bm.edges), bool))
    vv = defaultdict(list)
    for fi in faces:
        for v in bm.faces[fi].verts:
            vv[v.index].append(fvis[fi])
    a = min(vv, key=lambda v: (sum(vv[v]) / len(vv[v]), v))
    d, _ = dijkstra(adj, a, lambda ei: elen[ei])
    b = max(d, key=lambda v: (d[v], -v))
    _, prev = dijkstra(adj, a, lambda ei: elen[ei] * (CUT_EPS + evis[ei]))
    return path_edges(prev, a, b)


def relieve_saddles(bm, faces, seam, elen, evis, max_cuts=12):
    """A patch can only flatten without overlapping if the corner angles around every interior vertex add up to 360 deg or
    less. Saddle vertices (more than 360 deg) cause the overlaps, so cut the worst ones loose: a short hidden path from
    the vertex to the nearest seam. -> edges to cut."""
    fset = set(faces)
    adj, bverts = region_graph(bm, faces, seam)
    cand = []
    for vi in adj:
        if vi in bverts:
            continue
        tot = math.degrees(sum(l.calc_angle() for l in bm.verts[vi].link_loops if l.face.index in fset))
        if tot > 360.0 + SADDLE_TOL_DEG:
            cand.append((tot, vi))
    cand.sort(reverse=True)
    stop, out = set(bverts), []
    for _, vi in cand[:max_cuts]:
        if vi in stop:
            continue
        dist, prev = dijkstra(adj, vi, lambda ei: elen[ei] * (CUT_EPS + evis[ei]), block=stop)
        near = [w for w in dist if w in stop]
        if not near:
            continue
        w = min(near, key=lambda x: (dist[x], x))
        path = path_edges(prev, vi, w)
        out += path
        for ei in path:
            stop.update(v.index for v in bm.edges[ei].verts)
    return out


def crease_cut(bm, faces, seam, limit_deg):
    """Edges inside the patch whose faces meet at more than limit_deg (the way unwrap_generic.py rescues folded parts)."""
    out = set()
    for fi in faces:
        for e in bm.faces[fi].edges:
            if not seam[e.index] and len(e.link_faces) == 2 and math.degrees(e.calc_face_angle(0)) > limit_deg:
                out.add(e.index)
    return sorted(out)


def bisect(bm, faces, seam, elen, evis, fcen):
    """Split a patch into two compact halves of similar size. Seeds = two faces far apart; every face goes to the nearer
    seed (distance over the face graph); the cut is the border between the halves. A few seed pairs are tried and the
    cheapest (least visible) cut that is still balanced wins. A single shortest path would only shave a sliver off."""
    fset = set(faces)
    nb = {}
    for fi in faces:
        lst = []
        for e in bm.faces[fi].edges:
            if seam[e.index]:
                continue
            for h in e.link_faces:
                if h.index != fi and h.index in fset:
                    lst.append((h.index, e.index))
        nb[fi] = lst

    def flood(sources):
        dist, lab, heap = {}, {}, [(0.0, s, s) for s in sources]
        heapq.heapify(heap)
        while heap:
            d, f, l = heapq.heappop(heap)
            if f in lab:
                continue
            lab[f], dist[f] = l, d
            for g, _ in nb[f]:
                if g not in lab:
                    heapq.heappush(heap, (d + math.dist(fcen[f], fcen[g]), g, l))
        return lab, dist

    best = None
    for s in dict.fromkeys((faces[0], faces[len(faces) // 4], faces[len(faces) // 2], faces[3 * len(faces) // 4], faces[-1])):
        _, d = flood([s])
        a = max(d, key=lambda f: (d[f], -f))
        _, d = flood([a])
        b = max(d, key=lambda f: (d[f], -f))
        if a == b:
            continue
        lab, _ = flood([a, b])
        small = min(sum(1 for f in faces if lab.get(f) == a), sum(1 for f in faces if lab.get(f) == b)) / len(faces)
        cut = sorted({ei for f in faces for g, ei in nb[f] if lab[f] != lab[g]})
        if not cut:
            continue
        cost = sum(elen[ei] * (CUT_EPS + evis[ei]) for ei in cut) / max(small, 0.05)
        key = (small < 0.3, cost if small >= 0.3 else -small)      # balanced cuts first (cheapest wins), else the most balanced
        if best is None or key < best[0]:
            best = (key, cut)
    return best[1] if best and best[0][0] is not None else None


# ------------------------------------------------------------------------------------------ operators
def set_seams(me, seam):
    me.edges.foreach_set("use_seam", seam)
    me.update()


def only_active(obj):
    for o in bpy.context.view_layer.objects:
        o.select_set(False)
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj


def op_unwrap(obj, method):
    only_active(obj)
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_mode(type="FACE")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.unwrap(method=method, fill_holes=True, correct_aspect=True, margin=0.0005)
    bpy.ops.object.mode_set(mode="OBJECT")


def op_smart(obj):
    only_active(obj)
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_mode(type="FACE")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=math.radians(66), island_margin=0.02)
    bpy.ops.object.mode_set(mode="OBJECT")


# ------------------------------------------------------------------------------------------ one mesh
def process_mesh(obj, fvis):
    hard = obj.name in HARD
    md = MeshData(obj)
    me = obj.data
    bm = bmesh.new()
    bm.from_mesh(me)
    for t in (bm.verts, bm.edges, bm.faces):
        t.ensure_lookup_table()
        t.index_update()
    elen = np.array([e.calc_length() for e in bm.edges])
    evis = np.array([max([fvis[f.index] for f in e.link_faces], default=0.0) for e in bm.edges])
    fcen = [tuple(f.calc_center_median()) for f in bm.faces]
    rep = dict(type="hard-surface" if hard else "organic", faces=md.nf)
    seam, counts = initial_seams(bm, hard)
    rep["seams_by_rule"] = dict(counts)
    regs, rid = regions_of(bm, seam)
    rep["patches_after_rules_1_2"] = len(regs)
    n_cut = n_slit = 0
    for faces in regs:
        cut, has_boundary = make_disk(bm, faces, seam, elen, evis)
        if not cut and not has_boundary:
            cut = set(slit(bm, faces, elen, evis, fvis))
            n_slit += 1
        seam[list(cut)] = True
        n_cut += len(cut)
    rep["seams_by_rule"]["hidden_cut_to_disk"] = n_cut
    rep["closed_shells_slit"] = n_slit
    regs, rid = regions_of(bm, seam)
    rep["patches_after_rule_3"] = len(regs)

    n_bisect, n_crease, n_saddle, hist, stall = 0, 0, 0, [], False
    for it in range(MAX_ITERS + 1):
        set_seams(me, seam)
        op_unwrap(obj, "ANGLE_BASED")
        ev = evaluate(md, rid, len(regs))
        bad = bad_patches(ev)
        hist.append((len(regs), len(bad)))
        if len(bad) == 0 or it == MAX_ITERS:
            break
        added = 0
        for r in bad:
            cut = []
            broken = ev["fold"][r] > 0 or ev["ov"][r] > 0
            stretched = ev["badang"][r] > FRAC_BAD_ANG or ev["sdlog"][r] > SDLOG_BAD
            if broken and not stall and ev["cnt"][r] < BISECT_FIRST_FACES:
                cut = relieve_saddles(bm, regs[r], seam, elen, evis)   # small broken patch: free its saddle vertices,
                n_saddle += bool(cut)
                if not cut:                                            # then cut along its sharpest creases
                    cut = crease_cut(bm, regs[r], seam, CREASE_LIMITS[min(it, len(CREASE_LIMITS) - 1)])
                    n_crease += bool(cut)
            if not cut:                                                # big or stretched patch: split along a hidden path
                cut = bisect(bm, regs[r], seam, elen, evis, fcen)
            if cut:
                seam[cut] = True
                added += 1
        n_bisect += added
        if not added:
            break
        n_before = len(regs)
        regs, rid = regions_of(bm, seam)
        stall = len(regs) == n_before                   # slits did not split anything: use balanced splits from now on
    rep["bisect_iterations"] = len(hist) - 1
    rep["patches_cut_again"] = n_bisect
    rep["patches_saddle_cut"] = n_saddle
    rep["patches_crease_cut"] = n_crease
    rep["bad_patches_left"] = int(len(bad))
    rep["seams_by_rule"]["bisect_or_crease_cuts"] = int(seam.sum() - sum(rep["seams_by_rule"].values()))

    method = "ANGLE_BASED"
    op_unwrap(obj, "CONFORMAL")
    ev2 = evaluate(md, rid, len(regs))
    if score(ev2) < score(ev):
        ev, method = ev2, "CONFORMAL"
    else:
        op_unwrap(obj, "ANGLE_BASED")
    rep["method"] = method
    rep["fallback_smart_uv"] = False
    if ev["n_fold"] + ev["n_ov"] > 0:
        keep = md.read_uv()
        op_smart(obj)
        isl = uv_islands(md, md.read_uv())
        evs = evaluate(md, isl, int(isl.max()) + 1)
        if evs["n_ov"] == 0:
            rep["fallback_smart_uv"] = True
            rep["scripted_result_before_fallback"] = dict(folded=ev["n_fold"], overlapping=ev["n_ov"])
            ev = evs
        else:
            md.write_uv(keep)
    set_seams(me, seam)
    bm.free()
    rep["patches_final"] = len(regs)
    rep["folded_faces"], rep["overlapping_faces_in_own_space"] = ev["n_fold"], ev["n_ov"]
    return rep, regs, rid


def process_eye(obj, fvis):
    """Planar projection along Y: front hemisphere seen from the front (u = x), back hemisphere from behind (u = -x).
    Every patch is its own island (offset in u), so layered shells (iris dome over the sclera) never overlap."""
    md = MeshData(obj)
    me = obj.data
    bm = bmesh.new()
    bm.from_mesh(me)
    for t in (bm.verts, bm.edges, bm.faces):
        t.ensure_lookup_table()
        t.index_update()
    seam, counts = initial_seams(bm, False)
    fn_y = np.array([f.normal.y for f in bm.faces])
    cen_y = np.array([f.calc_center_median().y for f in bm.faces])
    regs, _ = regions_of(bm, seam)
    n_equator = 0
    for faces in regs:                                       # a patch that wraps around the eye is cut at the equator y = 0
        if fn_y[faces].min() < -0.05 and fn_y[faces].max() > 0.05:
            side = cen_y > 0
            for fi in faces:
                for e in bm.faces[fi].edges:
                    if len(e.link_faces) == 2 and side[e.link_faces[0].index] != side[e.link_faces[1].index]:
                        if not seam[e.index]:
                            seam[e.index] = True
                            n_equator += 1
    regs, rid = regions_of(bm, seam)
    uv = np.zeros((md.nl, 2))
    P = md.co[md.lv]
    for r, faces in enumerate(regs):
        front = fn_y[faces].mean() < 0
        loops = np.nonzero(np.isin(md.pol, faces))[0]
        uv[loops, 0] = (P[loops, 0] if front else -P[loops, 0]) * 20.0 + 3.0 * r
        uv[loops, 1] = P[loops, 2] * 20.0
    md.write_uv(uv)
    set_seams(me, seam)
    ev = evaluate(md, rid, len(regs))
    bm.free()
    rep = dict(type="eye (planar projection along Y)", faces=md.nf, seams_by_rule=dict(counts, equator_cut=n_equator),
               patches_final=len(regs), method="planar", fallback_smart_uv=False,
               folded_faces=ev["n_fold"], overlapping_faces_in_own_space=ev["n_ov"])
    return rep, regs, rid


# ------------------------------------------------------------------------------------------ shared 0-1 space
def shared_pack(objs, patches, vis, fallback):
    for o in bpy.context.view_layer.objects:
        o.select_set(False)
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_mode(type="FACE")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.select_all(action="SELECT")
    bpy.ops.uv.average_islands_scale()
    n_hidden = 0
    for o in objs:                                           # rule 7: fully hidden patches get HIDDEN_SCALE of the linear scale
        if o.name in fallback:
            continue
        bm = bmesh.from_edit_mesh(o.data)
        bm.faces.ensure_lookup_table()
        uvl = bm.loops.layers.uv[UV_NAME]
        for faces in patches[o.name]:
            if vis[o.name][faces].max() >= HIDDEN_VIS:
                continue
            loops = [l for fi in faces for l in bm.faces[fi].loops]
            c = sum((l[uvl].uv for l in loops), Vector((0, 0))) / len(loops)
            for l in loops:
                l[uvl].uv = c + (l[uvl].uv - c) * HIDDEN_SCALE
            n_hidden += 1
        bmesh.update_edit_mesh(o.data, loop_triangles=False, destructive=False)
    bpy.ops.uv.select_all(action="SELECT")
    bpy.ops.uv.pack_islands(rotate=True, margin_method="FRACTION", margin=MARGIN, shape_method="CONCAVE")
    bpy.ops.object.mode_set(mode="OBJECT")
    return n_hidden


def texel_stats(objs, patches, vis, fallback):
    """Effective resolution on a 1024 px texture in texels per metre, for the surfaces that can be seen and for the hidden ones."""
    a_uv, a_3 = dict(visible=0.0, hidden=0.0), dict(visible=0.0, hidden=0.0)
    for o in objs:
        md = MeshData(o)
        U = md.read_uv()
        cross = U[:, 0] * U[md.nxt, 1] - U[md.nxt, 0] * U[:, 1]
        auv = np.abs(0.5 * np.add.reduceat(cross, md.ls))
        hid = np.zeros(md.nf, bool)
        if o.name not in fallback:
            for faces in patches[o.name]:
                if vis[o.name][faces].max() < HIDDEN_VIS:
                    hid[faces] = True
        for key, m in (("visible", ~hid), ("hidden", hid)):
            a_uv[key] += auv[m].sum()
            a_3[key] += md.area3[m].sum()
    return {k: (round(1024 * math.sqrt(a_uv[k] / a_3[k]), 1) if a_3[k] > 0 else None) for k in a_uv}


def measure_all(objs):
    per = {}
    for o in objs:
        m, _ = uvmetrics.metrics(o, UV_NAME)
        m["tris"] = sum(len(p.vertices) - 2 for p in o.data.polygons)
        m["angle_stretch_faces_over_2deg"] = m["faces"] - m["angle_stretch_faces_blue"]
        per[o.name] = m
    comb, _ = uvmetrics.metrics(objs, UV_NAME)
    comb["angle_stretch_faces_over_2deg"] = comb["faces"] - comb["angle_stretch_faces_blue"]
    return per, comb


def uv_checks(comb):
    b = comb["uv_bounds"]
    return [("0 overlapping faces in the shared UV space", comb["overlapping_faces"] == 0, "%d of %d" % (comb["overlapping_faces"], comb["faces"])),
            ("All UVs inside 0-1", min(b) >= -1e-6 and max(b) <= 1 + 1e-6, str(b))]


# ------------------------------------------------------------------------------------------ main
def main():
    """Unwrap every mesh of COLLECTION in SRC and write DST (only if every check passes). Other scripts
    (unwrap_bird.py) import this module, change COLLECTION / HARD / EYES / the hooks, and call main()."""
    bpy.ops.wm.open_mainfile(filepath=SRC)
    before = snapshot()
    log("opened", SRC, "- %d objects" % len(before["objects"]))
    meshes = sorted([o for o in bpy.data.collections[COLLECTION].all_objects if o.type == "MESH"], key=lambda o: o.name)
    todo = meshes
    hidden_state = {o.name: (o.hide_viewport, o.hide_get()) for o in meshes}
    for o in meshes:
        o.hide_viewport = False
        o.hide_set(False)

    vis = compute_visibility(meshes, todo)
    log("visibility map done (%d rays/face max, %d meshes)" % (N_DIRS, len(todo)))
    report, patches, fallback = {}, {}, set()
    for o in todo:
        t = time.time()
        rep, regs, _ = process_eye(o, vis[o.name]) if o.name in EYES else process_mesh(o, vis[o.name])
        rep["seconds"] = round(time.time() - t, 1)
        report[o.name], patches[o.name] = rep, regs
        if rep["fallback_smart_uv"]:
            fallback.add(o.name)
        log("%-14s %-30s patches %5d  folded %d  overlap %d  %s%s" % (
            o.name, rep["type"], rep["patches_final"], rep["folded_faces"], rep["overlapping_faces_in_own_space"],
            rep.get("method", ""), "  [Smart UV fallback]" if rep["fallback_smart_uv"] else ""))

    n_hidden = shared_pack(todo, patches, vis, fallback)
    texels = texel_stats(todo, patches, vis, fallback)
    log("average scale + pack done; %d fully hidden patches at %.0f%% scale; margin %.4f" % (n_hidden, 100 * HIDDEN_SCALE, MARGIN))
    for o in meshes:
        o.hide_viewport, hidden = hidden_state[o.name]
        o.hide_set(hidden)
    per, comb = measure_all(todo)
    log("uvmetrics done")

    rows = compare(before, snapshot())
    ok = print_table("INVARIANTS (before vs after, in memory)", rows)
    rows2 = uv_checks(comb)
    ok = print_table("UV CHECKS", rows2) and ok
    tmp = os.path.splitext(DST)[0] + ".uvtmp.blend"
    if ok:
        bpy.ops.wm.save_as_mainfile(filepath=tmp)
        bpy.ops.wm.open_mainfile(filepath=tmp)                   # verify what was actually saved
        meshes = sorted([o for o in bpy.data.collections[COLLECTION].all_objects if o.type == "MESH"], key=lambda o: o.name)
        per2, comb2 = measure_all(meshes)
        ok = print_table("INVARIANTS (input file vs saved file)", compare(before, snapshot()))
        ok = print_table("UV CHECKS (saved file)", uv_checks(comb2)) and ok
        per, comb = per2, comb2
    if ok:
        os.replace(tmp, DST)
        log("WROTE", DST)
    else:
        log("CHECKS FAILED - %s NOT replaced%s" % (DST, ("; temp file kept at " + tmp) if os.path.exists(tmp) else ""))
    summary = dict(margin=MARGIN, hidden_scale=HIDDEN_SCALE, hidden_patches_scaled=n_hidden, texels_per_metre_at_1024=texels, meshes=report, after=per, combined=comb, ok=ok)
    if "--report" in ARGS:
        with open(option("--report", ""), "w") as fh:
            json.dump(summary, fh, indent=1)
    print("UNWRAP", json.dumps(summary))
    sys.exit(0 if ok else 1)


if __name__ == "__main__":
    main()
