"""UV quality metrics for one mesh object (run inside bpy). Read-only."""
import math
from collections import Counter, defaultdict

import bmesh
from shapely.geometry import Polygon
from shapely.strtree import STRtree


def _key(uv):
    return (round(uv.x, 6), round(uv.y, 6))


def metrics(obj, uv_name=None):
    """obj: one mesh object, or a list of objects that share one UV space (e.g. RPG + rocket)."""
    objs = obj if isinstance(obj, (list, tuple)) else [obj]
    bm = bmesh.new()
    for o in objs:
        bm.from_mesh(o.data)   # appends; all objects use object space, fine for areas/angles
    bm.faces.ensure_lookup_table()
    bm.edges.ensure_lookup_table()
    uvl = bm.loops.layers.uv[uv_name] if uv_name else bm.loops.layers.uv.active
    faces = list(bm.faces)

    # ---- islands: faces connected through an edge whose UVs match on both sides
    parent = list(range(len(faces)))

    def find(i):
        while parent[i] != i:
            parent[i] = parent[parent[i]]
            i = parent[i]
        return i

    for e in bm.edges:
        lf = e.link_faces
        if len(lf) != 2:
            continue
        a, b = lf
        def edge_uvs(f):
            d = {}
            for l in f.loops:
                d[l.vert.index] = _key(l[uvl].uv)
            return d[e.verts[0].index], d[e.verts[1].index]
        if edge_uvs(a) == edge_uvs(b):
            parent[find(a.index)] = find(b.index)
    islands = defaultdict(list)
    for f in faces:
        islands[find(f.index)].append(f)

    # ---- overlaps
    polys = []
    for f in faces:
        pts = [(l[uvl].uv.x, l[uvl].uv.y) for l in f.loops]
        p = Polygon(pts)
        if not p.is_valid:
            p = p.buffer(0)
        polys.append(p)
    tree = STRtree(polys)
    overlapping = set()
    for i, p in enumerate(polys):
        if p.area <= 0:
            continue
        for j in tree.query(p):
            j = int(j)
            if j <= i:
                continue
            q = polys[j]
            inter = p.intersection(q).area
            if inter > 1e-7 * max(min(p.area, q.area), 1e-12) and inter > 1e-10:
                overlapping.add(i)
                overlapping.add(j)
    stacks = Counter(tuple(sorted(_key(l[uvl].uv) for l in f.loops)) for f in faces)
    max_stack = max(stacks.values())

    # ---- stretch (area + angle), texel density per island
    area3d_total = sum(f.calc_area() for f in faces)
    areauv_total = sum(p.area for p in polys)
    area_stretch = []
    angle_err = []
    for f, p in zip(faces, polys):
        a3 = f.calc_area()
        r = (p.area / areauv_total) / (a3 / area3d_total) if a3 > 0 and areauv_total > 0 else 0
        area_stretch.append(r)
        # angle distortion per corner
        loops = list(f.loops)
        n = len(loops)
        for k in range(n):
            l0, l1, l2 = loops[k - 1], loops[k], loops[(k + 1) % n]
            v1 = l0.vert.co - l1.vert.co
            v2 = l2.vert.co - l1.vert.co
            u1 = l0[uvl].uv - l1[uvl].uv
            u2 = l2[uvl].uv - l1[uvl].uv
            if v1.length < 1e-9 or v2.length < 1e-9 or u1.length < 1e-12 or u2.length < 1e-12:
                angle_err.append(180.0)
                continue
            angle_err.append(abs(math.degrees(v1.angle(v2)) - math.degrees(u1.angle(u2))))

    # Blender's "Display Stretch" (angle mode) colours a face blue when its angles match.
    # Treat a face as "blue" when its worst corner is within 2 degrees.
    face_worst = []
    idx = 0
    for f in faces:
        n = len(f.loops)
        face_worst.append(max(angle_err[idx:idx + n]))
        idx += n
    blue = sum(1 for w in face_worst if w <= 2.0)

    dens = []
    for fs in islands.values():
        a3 = sum(f.calc_area() for f in fs)
        au = sum(polys[f.index].area for f in fs)
        if a3 > 0:
            dens.append(math.sqrt(au / a3))
    mean_d = sum(dens) / len(dens)
    sd = math.sqrt(sum((d - mean_d) ** 2 for d in dens) / len(dens))

    us = [l[uvl].uv.x for f in faces for l in f.loops]
    vs = [l[uvl].uv.y for f in faces for l in f.loops]
    out = dict(
        faces=len(faces),
        seams=sum(1 for o in objs for e in o.data.edges if e.use_seam),
        islands=len(islands),
        overlapping_faces=len(overlapping),
        max_faces_on_one_spot=max_stack,
        uv_bounds=[round(min(us), 4), round(min(vs), 4), round(max(us), 4), round(max(vs), 4)],
        uv_coverage_pct=round(100 * areauv_total, 1) if not overlapping else None,
        angle_stretch_faces_blue=blue,
        angle_stretch_worst_deg=round(max(face_worst), 2),
        area_ratio_min=round(min(area_stretch), 3),
        area_ratio_max=round(max(area_stretch), 3),
        texel_density_cv_pct=round(100 * sd / mean_d, 2),
    )
    bm.free()
    return out, face_worst
