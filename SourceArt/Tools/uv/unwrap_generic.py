"""Unwrap the AR, sniper and RPG (+ rocket) with the same method as the pistol.

Per connected part:
  * plain box smaller than 4 cm            -> Smart UV Project (66 deg, margin 0.02)
  * everything else (boxes >= 4 cm, cylinders, prisms, bevelled boxes, spheres)
      length axis L = n-gon cap normal, else the longest side of the part
      end caps = faces facing along +L / -L (n.L > 0.5), sides = the rest
      seams = every edge between an end cap and the sides  (the "rings")
            + one lengthwise cut through the side tube, on the least visible side
              (bottom for horizontal parts, front for vertical parts, inner side for side-mounted parts)
      -> Unwrap (angle based)
Then Average Islands Scale + Pack Islands (rotate, margin 0.01) over everything that shares one texture.
"""
import heapq
import json
import math
import sys

import bpy
import bmesh
from mathutils import Vector

SRC, DST = sys.argv[sys.argv.index("--") + 1:][:2]
GROUPS = [["SM_Gun_Tier2"], ["SM_Gun_Tier3"], ["SM_Gun_Tier4", "RPG_Rocket"]]
SMALL_BOX = 0.04
X, Y, Z = Vector((1, 0, 0)), Vector((0, 1, 0)), Vector((0, 0, 1))

bpy.ops.wm.open_mainfile(filepath=SRC)


def components(bm):
    seen, comps = set(), []
    for f in bm.faces:
        if f in seen:
            continue
        stack, comp = [f], {f}
        seen.add(f)
        while stack:
            g = stack.pop()
            for e in g.edges:
                for h in e.link_faces:
                    if h not in seen:
                        seen.add(h); comp.add(h); stack.append(h)
        comps.append(comp)
    return comps


def bbox(comp):
    vs = {v for f in comp for v in f.verts}
    lo = Vector([min(v.co[i] for v in vs) for i in range(3)])
    hi = Vector([max(v.co[i] for v in vs) for i in range(3)])
    return lo, hi, vs


def length_axis(comp, lo, hi):
    ngons = [f for f in comp if len(f.verts) > 4]
    if ngons:
        n = max(ngons, key=lambda f: f.calc_area()).normal.copy()
        return n.normalized(), "prism"
    if len(comp) == 6:
        # box: direction of its longest edge class (works for rotated boxes)
        e = max({e for f in comp for e in f.edges}, key=lambda e: e.calc_length())
        return (e.verts[1].co - e.verts[0].co).normalized(), "box"
    size = hi - lo
    i = max(range(3), key=lambda k: size[k])
    kind = "sphere" if any(len(f.verts) == 3 for f in comp) else "bevelled box"
    return [X, Y, Z][i].copy(), kind


def hide_dir(L, center, gun_half_width):
    if abs(L.dot(Z)) > 0.7:          # vertical part (grip, magazine, bipod leg): front side
        h = -Y.copy()
    else:                             # horizontal part: bottom
        h = -Z.copy()
    if abs(center.x) > 0.6 * gun_half_width and abs(center.x) > 0.012:   # side-mounted: inner side
        h += -math.copysign(1.0, center.x) * X * 1.5
    return h.normalized()


def euler_char(faces):
    vs = {v for f in faces for v in f.verts}
    es = {e for f in faces for e in f.edges}
    return len(vs) - len(es) + len(faces)


def seam_part(comp, L, hide, center, kind):
    """Mark the end-cap rings and one hidden lengthwise cut. Returns number of seams added."""
    region = {}
    thr = 0.999 if kind == "prism" else 0.5   # n-gon parts: the caps are exactly the flat end faces
    for f in comp:
        d = f.normal.dot(L)
        region[f] = 1 if d > thr else (-1 if d < -thr else 0)
    added = 0
    for e in {e for f in comp for e in f.edges}:
        a, b = e.link_faces
        if region[a] != region[b]:
            e.seam = True; added += 1
    sides = [f for f in comp if region[f] == 0]
    if not sides:
        return added, "no sides"
    # split the side region into connected pieces (not crossing seams)
    pieces, left = [], set(sides)
    while left:
        f0 = left.pop(); piece = {f0}; st = [f0]
        while st:
            g = st.pop()
            for e in g.edges:
                if e.seam:
                    continue
                for h in e.link_faces:
                    if h in left:
                        left.discard(h); piece.add(h); st.append(h)
        pieces.append(piece)
    note = []
    for piece in pieces:
        if euler_char(piece) != 0:      # already a disk: unwraps flat without a cut
            continue
        # the tube's two boundary loops
        boundary = {e for f in piece for e in f.edges if e.seam or any(h not in piece for h in e.link_faces)}
        bverts = {v for e in boundary for v in e.verts}
        # split boundary verts into the two loops
        loops, rem = [], set(bverts)
        while rem:
            v0 = rem.pop(); loop = {v0}; st = [v0]
            while st:
                v = st.pop()
                for e in v.link_edges:
                    if e in boundary:
                        w = e.other_vert(v)
                        if w in rem:
                            rem.discard(w); loop.add(w); st.append(w)
            loops.append(loop)
        if len(loops) != 2:
            note.append("tube with %d boundary loops" % len(loops)); continue
        start, goal = loops
        inner_edges = {e for f in piece for e in f.edges if e not in boundary}

        def cost(e):
            d = (e.verts[1].co - e.verts[0].co)
            ln = d.length
            align = abs(d.normalized().dot(L)) if ln > 0 else 0
            mid = (e.verts[0].co + e.verts[1].co) / 2 - center
            mid -= L * mid.dot(L)
            s = (mid.normalized().dot(hide) + 1) / 2 if mid.length > 1e-9 else 0.5
            return ln * (1 + 3 * (1 - align)) * (1 + 4 * (1 - s)) + 1e-6

        dist = {v: 0.0 for v in start}
        prev = {}
        heap = [(0.0, i, v) for i, v in enumerate(start)]
        heapq.heapify(heap)
        counter = len(heap)
        reached = None
        while heap:
            d0, _, v = heapq.heappop(heap)
            if d0 > dist.get(v, 1e18):
                continue
            if v in goal:
                reached = v; break
            for e in v.link_edges:
                if e not in inner_edges and not (e in boundary and False):
                    # allow stepping only through the inside of the side tube, or straight into the goal loop
                    if not (e.other_vert(v) in goal and e not in boundary):
                        continue
                w = e.other_vert(v)
                nd = d0 + cost(e)
                if nd < dist.get(w, 1e18):
                    dist[w] = nd; prev[w] = (v, e); counter += 1
                    heapq.heappush(heap, (nd, counter, w))
        if reached is None:
            note.append("no cut path"); continue
        v = reached
        while v in prev:
            u, e = prev[v]
            e.seam = True; added += 1
            v = u
    return added, ";".join(note)


def sphere_gores(comp, gores):
    """A sphere can never be flattened without stretch, so cut it like an orange peel:
    seams along every 2nd meridian, pole to pole -> 8 gores with very little stretch."""
    verts = {v for f in comp for v in f.verts}
    poles = [v for v in verts if sum(1 for f in v.link_faces if f in comp and len(f.verts) == 3) >= 6]
    assert len(poles) == 2, len(poles)
    axis = (poles[0].co - poles[1].co).normalized()
    mid = (poles[0].co + poles[1].co) / 2
    ref = axis.orthogonal().normalized()
    ref2 = axis.cross(ref)
    def az(v):
        d = v.co - mid
        return math.degrees(math.atan2(d.dot(ref2), d.dot(ref))) % 360
    ring = sorted({round(az(v), 1) for v in verts if v not in poles})
    n_seg = len(ring)
    keep = set(ring[::max(1, n_seg // gores)])
    added = 0
    for e in {e for f in comp for e in f.edges}:
        a, b = e.verts
        ia = None if a in poles else round(az(a), 1)
        ib = None if b in poles else round(az(b), 1)
        if (ia is None and ib in keep) or (ib is None and ia in keep) or (ia is not None and ia == ib and ia in keep):
            e.seam = True; added += 1
    return added


def relieve_curvature(comp, hide, center, tol_deg=float(__import__("os").environ.get("UV_TOL", "0.5"))):
    """A patch flattens without stretch only if every vertex inside it has corner angles summing
    to 360 deg. Bevel corners, cones and spheres break that, so run a short seam from each such
    vertex to the nearest existing seam (preferring hidden edges). Returns seams added."""
    added = 0
    verts = {v for f in comp for v in f.verts}
    edges = {e for f in comp for e in f.edges}
    for _ in range(500):
        on_seam = {v for e in edges if e.seam for v in e.verts}
        bad = []
        for v in verts - on_seam:
            total = sum(l.calc_angle() for l in v.link_loops if l.face in comp)
            if abs(math.degrees(total) - 360.0) > tol_deg:
                bad.append(v)
        if not bad:
            break
        def cost(e):
            mid = (e.verts[0].co + e.verts[1].co) / 2 - center
            s_ = (mid.normalized().dot(hide) + 1) / 2 if mid.length > 1e-9 else 0.5
            return e.calc_length() * (1 + 4 * (1 - s_)) + 1e-7
        dist = {v: 0.0 for v in on_seam}
        prev = {}
        heap = [(0.0, i, v) for i, v in enumerate(on_seam)]
        heapq.heapify(heap); cnt = len(heap)
        while heap:
            d0, _, v = heapq.heappop(heap)
            if d0 > dist.get(v, 1e18):
                continue
            for e in v.link_edges:
                if e not in edges or e.seam:
                    continue
                w = e.other_vert(v); nd = d0 + cost(e)
                if nd < dist.get(w, 1e18):
                    dist[w] = nd; prev[w] = (v, e); cnt += 1
                    heapq.heappush(heap, (nd, cnt, w))
        # cut the closest bad vertex first, then re-check (one cut often fixes neighbours)
        bad.sort(key=lambda v: dist.get(v, 1e18))
        v = bad[0]
        if v not in prev:
            break
        while v in prev:
            u, e = prev[v]
            if not e.seam:
                e.seam = True; added += 1
            v = u
    return added


report = {}
for group in GROUPS:
    objs = [bpy.data.objects[n] for n in group]
    # gun half width from the main object
    main = objs[0]
    xs = [v.co.x for v in main.data.vertices]
    half_w = (max(xs) - min(xs)) / 2
    per_obj_sets = {}
    kinds = {}
    for obj in objs:
        bm = bmesh.new(); bm.from_mesh(obj.data)
        for e in bm.edges:
            e.seam = False
        seam_faces, smart_faces = set(), set()
        for comp in components(bm):
            lo, hi, vs = bbox(comp)
            center = (lo + hi) / 2
            L, kind = length_axis(comp, lo, hi)
            if kind == "box" and max(hi - lo) < SMALL_BOX:
                smart_faces |= {f.index for f in comp}
                kinds["small box (Smart UV)"] = kinds.get("small box (Smart UV)", 0) + 1
                continue
            hide = hide_dir(L, center, half_w)
            if kind == "sphere":
                n = sphere_gores(comp, 16)
                seam_faces |= {f.index for f in comp}
                kinds[kind] = kinds.get(kind, 0) + 1
                continue
            n, note = seam_part(comp, L, hide, center, kind)
            n2 = relieve_curvature(comp, hide, center)
            if n2:
                kinds["extra seams through curved corners"] = kinds.get("extra seams through curved corners", 0) + n2
            if note:
                print("NOTE", obj.name, kind, tuple(round(c, 3) for c in center), note)
            seam_faces |= {f.index for f in comp}
            kinds[kind] = kinds.get(kind, 0) + 1
        bm.to_mesh(obj.data); bm.free()
        per_obj_sets[obj.name] = (seam_faces, smart_faces)

    # ---- multi-object edit mode: everything in the group shares one UV square
    bpy.ops.object.mode_set(mode="OBJECT") if bpy.context.object and bpy.context.object.mode != "OBJECT" else None
    for o in bpy.context.view_layer.objects:
        o.select_set(False)
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_mode(type="FACE")

    def select(which):
        for o in objs:
            bm = bmesh.from_edit_mesh(o.data)
            bm.faces.ensure_lookup_table()
            want = per_obj_sets[o.name][which] if which is not None else None
            for f in bm.faces:
                f.select_set(True if want is None else f.index in want)
            bm.select_flush_mode()
            bmesh.update_edit_mesh(o.data)

    select(0)
    bpy.ops.uv.unwrap(method="ANGLE_BASED", margin=0.001)

    # ---- an island that folds over itself is an overlap too: split such parts at their
    #      sharpest edges and unwrap them again, until no part overlaps itself
    from shapely.geometry import Polygon
    from shapely.strtree import STRtree
    fixed = 0
    for limit in (40, 30, 20, 10):
        redo = {}
        for o in objs:
            bm = bmesh.from_edit_mesh(o.data); bm.faces.ensure_lookup_table()
            uvl = bm.loops.layers.uv.active
            seam_set = per_obj_sets[o.name][0]
            for comp in components(bm):
                if not comp or next(iter(comp)).index not in seam_set:
                    continue
                fl = list(comp)
                polys = [Polygon([(l[uvl].uv.x, l[uvl].uv.y) for l in f.loops]).buffer(0) for f in fl]
                tree = STRtree(polys); bad = False
                for i, pgon in enumerate(polys):
                    for j in tree.query(pgon):
                        if int(j) > i and pgon.intersection(polys[int(j)]).area > 1e-10:
                            bad = True; break
                    if bad:
                        break
                if bad:
                    for e in {e for f in comp for e in f.edges}:
                        if not e.seam and len(e.link_faces) == 2 and math.degrees(e.calc_face_angle(0)) > limit:
                            e.seam = True
                    redo.setdefault(o.name, set()).update(f.index for f in comp)
            bmesh.update_edit_mesh(o.data)
        if not redo:
            break
        fixed += sum(len(v) for v in redo.values())
        for o in objs:
            bm = bmesh.from_edit_mesh(o.data); bm.faces.ensure_lookup_table()
            want = redo.get(o.name, set())
            for f in bm.faces:
                f.select_set(f.index in want)
            bm.select_flush_mode(); bmesh.update_edit_mesh(o.data)
        bpy.ops.uv.unwrap(method="ANGLE_BASED", margin=0.001)
        kinds["self-overlap fixed at sharp edges > %d deg" % limit] = len(redo)
    if any(per_obj_sets[o.name][1] for o in objs):
        select(1)
        bpy.ops.uv.smart_project(angle_limit=math.radians(66), island_margin=0.02)
    select(None)
    bpy.ops.uv.select_all(action="SELECT")
    bpy.ops.uv.average_islands_scale()
    bpy.ops.uv.pack_islands(rotate=True, margin=0.01)
    bpy.ops.object.mode_set(mode="OBJECT")
    report[" + ".join(group)] = kinds

for o in bpy.context.view_layer.objects:
    o.select_set(False)
bpy.ops.wm.save_as_mainfile(filepath=DST)
print("UNWRAP", json.dumps(report))
