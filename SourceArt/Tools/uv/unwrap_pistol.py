"""Unwrap SM_Gun_Tier1 following the hand-unwrap guide.

Big parts (slide, frame, grip + panels, magazine, trigger guard): box-net seams
  = rings around both end caps + one lengthwise edge on the least visible side -> Unwrap.
Cylinders (barrel, bore, sight dots): rings at both ends + one lengthwise seam at the bottom -> Unwrap.
Everything else (small boxes): Smart UV Project, angle 66, island margin 0.02.
Then Average Islands Scale + Pack Islands (rotate, margin 0.01).
"""
import json
import math
import sys

import bpy
import bmesh
from mathutils import Vector

args = sys.argv[sys.argv.index("--") + 1:]
SRC, DST = args[0], args[1]
bpy.ops.wm.open_mainfile(filepath=SRC)

obj = bpy.data.objects["SM_Gun_Tier1"]
mat_names = [s.material.name for s in obj.material_slots]

for o in bpy.context.view_layer.objects:
    o.select_set(False)
obj.select_set(True)
bpy.context.view_layer.objects.active = obj
bpy.ops.object.mode_set(mode="EDIT")
bm = bmesh.from_edit_mesh(obj.data)
bm.faces.ensure_lookup_table()


def components():
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
                        seen.add(h)
                        comp.add(h)
                        stack.append(h)
        comps.append(comp)
    return comps


def info(comp):
    vs = {v for f in comp for v in f.verts}
    lo = Vector([min(v.co[i] for v in vs) for i in range(3)])
    hi = Vector([max(v.co[i] for v in vs) for i in range(3)])
    mats = {mat_names[f.material_index] for f in comp}
    return dict(lo=lo, hi=hi, size=hi - lo, center=(lo + hi) / 2, mats=mats,
                area=sum(f.calc_area() for f in comp), nf=len(comp))


comps = [(c, info(c)) for c in components()]


def find(pred, label):
    hits = [(c, i) for c, i in comps if pred(i)]
    assert len(hits) >= 1, label
    return hits


def near(a, b, tol=0.003):
    return abs(a - b) < tol


# ---------------------------------------------------------------- identify parts
X, Y, Z = Vector((1, 0, 0)), Vector((0, 1, 0)), Vector((0, 0, 1))
big = []   # (label, comp, forced length axis or None, hide direction)

slide = find(lambda i: i["mats"] == {"M_Gun_SlideSteel"}, "slide")
big += [("slide", c, Y, -Z) for c, _ in slide]

poly_boxes = [(c, i) for c, i in comps if i["mats"] == {"M_Gun_Polymer"} and i["nf"] == 6]
for c, i in poly_boxes:
    s, lo, hi = i["size"], i["lo"], i["hi"]
    if s.z > 0.1:                                   # tall, angled box under the frame
        big.append(("grip", c, None, -Y + 0.01 * X))       # seam on the front strap edge
    elif lo.z > 0.04 and s.y > 0.1:                 # front frame / dust cover
        big.append(("frame_front", c, Y, -Z))
    elif lo.z > 0.04 and s.y > 0.05:
        big.append(("frame_rear", c, Y, -Z))
    elif lo.z > 0.04 and lo.y > 0.02:               # beavertail at the back of the frame
        big.append(("frame_beavertail", c, Y, -Z))
    elif hi.z < -0.035 and s.x > 0.03:              # sticks out under the grip
        big.append(("magazine", c, Y, Z))           # seam on top, hidden inside the grip
    elif near(s.z, 0.006) and s.y > 0.05:           # trigger-guard bottom bar
        big.append(("trigger_guard_bar", c, Y, Z))  # seam on top, inside the guard
    elif near(s.y, 0.006) and s.z > 0.025:          # trigger-guard front post
        big.append(("trigger_guard_front", c, Z, Y))  # seam on the back, inside the guard

for c, i in comps:
    if i["mats"] == {"M_Gun_PolymerStipple"}:
        inward = -X if i["center"].x > 0 else X     # face that sits against the grip
        big.append(("grip_panel_" + ("R" if i["center"].x > 0 else "L"), c, None, inward - 0.5 * Y))

cylinders = [(c, i) for c, i in comps if any(len(f.verts) > 4 for f in c)]
labels = {"M_Gun_Stainless": "barrel", "M_Gun_Bore": "bore", "M_Gun_SightDot": "sight_dot"}

big_faces = set().union(*[c for _, c, _, _ in big])
cyl_faces = set().union(*[c for c, _ in cylinders])
small = [(c, i) for c, i in comps if not (c & big_faces) and not (c & cyl_faces)]

# ---------------------------------------------------------------- seams
for e in bm.edges:
    e.seam = False


def edge_dir(e):
    return (e.verts[1].co - e.verts[0].co).normalized()


def box_seams(comp, axis, hide):
    edges = {e for f in comp for e in f.edges}
    # group the 12 edges into 3 parallel classes
    classes = []
    for e in edges:
        d = edge_dir(e)
        for cl in classes:
            if abs(edge_dir(cl[0]).dot(d)) > 0.98:
                cl.append(e)
                break
        else:
            classes.append([e])
    assert len(classes) == 3 and all(len(c) == 4 for c in classes), [len(c) for c in classes]
    if axis is None:
        length = max(classes, key=lambda cl: cl[0].calc_length())
    else:
        length = max(classes, key=lambda cl: abs(edge_dir(cl[0]).dot(axis)))
    # rings around both end caps = every edge NOT running along the length
    for cl in classes:
        if cl is not length:
            for e in cl:
                e.seam = True
    center = sum((v.co for e in edges for v in e.verts), Vector()) / (2 * len(edges))
    h = hide.normalized()
    best = max(length, key=lambda e: ((e.verts[0].co + e.verts[1].co) / 2 - center).normalized().dot(h))
    best.seam = True
    return 9


def cylinder_seams(comp):
    caps = [f for f in comp if len(f.verts) > 4]
    for f in caps:
        for e in f.edges:
            e.seam = True                      # the rings at both ends
    side = [f for f in comp if len(f.verts) == 4]
    cap_edges = {e for f in caps for e in f.edges}
    axial = [e for f in side for e in f.edges if e not in cap_edges]
    axial = list(set(axial))
    # one lengthwise seam per wall (tube = outer + inner wall), on the bottom (-Z)
    walls = []
    remaining = set(side)
    while remaining:
        f0 = remaining.pop()
        wall, stack = {f0}, [f0]
        while stack:
            g = stack.pop()
            for e in g.edges:
                if e in cap_edges:
                    continue
                for h in e.link_faces:
                    if h in remaining:
                        remaining.discard(h)
                        wall.add(h)
                        stack.append(h)
        walls.append(wall)
    n = 0
    for wall in walls:
        wedges = [e for e in axial if all(f in wall for f in e.link_faces)]
        bottom = min(wedges, key=lambda e: e.verts[0].co.z + e.verts[1].co.z)
        bottom.seam = True
        n += 1
    return len(cap_edges) + n


report = {"big_parts": [], "cylinders": [], "small_parts": len(small)}
for label, c, axis, hide in big:
    box_seams(c, axis, hide)
    report["big_parts"].append(label)
for c, i in cylinders:
    cylinder_seams(c)
    report["cylinders"].append(labels[next(iter(i["mats"]))])
bmesh.update_edit_mesh(obj.data)

# ---------------------------------------------------------------- unwrap big parts + cylinders
def select_faces(faceset):
    for f in bm.faces:
        f.select_set(f in faceset)
    bm.select_flush_mode()
    bmesh.update_edit_mesh(obj.data)


bpy.ops.mesh.select_mode(type="FACE")
select_faces(big_faces | cyl_faces)
bpy.ops.uv.unwrap(method="ANGLE_BASED", margin=0.001)

# ---------------------------------------------------------------- small parts: Smart UV Project
bm = bmesh.from_edit_mesh(obj.data)
small_faces = set().union(*[c for c, _ in small])
select_faces(small_faces)
bpy.ops.uv.smart_project(angle_limit=math.radians(66), island_margin=0.02)

# ---------------------------------------------------------------- tidy: average scale + pack
bm = bmesh.from_edit_mesh(obj.data)
for f in bm.faces:
    f.select_set(True)
bmesh.update_edit_mesh(obj.data)
bpy.ops.uv.select_all(action="SELECT")
bpy.ops.uv.average_islands_scale()
bpy.ops.uv.pack_islands(rotate=True, margin=0.01)

bpy.ops.object.mode_set(mode="OBJECT")
report["seams"] = sum(1 for e in obj.data.edges if e.use_seam)
bpy.ops.wm.save_as_mainfile(filepath=DST)
print("UNWRAP", json.dumps(report))
