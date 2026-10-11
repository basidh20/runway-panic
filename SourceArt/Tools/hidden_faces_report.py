"""
hidden_faces_report.py - READ-ONLY report of triangles per mesh and how many are hidden under other meshes.

A face counts as hidden when rays cast from its centre (along its normal and every axis direction
within ~70 deg of it) all hit ANOTHER mesh within HIDE_DISTANCE. Typical result: body skin under
the shirt, hair under the cap, shirt under the vest. Those faces cost triangles but are never seen,
so they are the first thing to delete in an optimisation pass.

The script never modifies or saves the .blend. It is a targeting aid; the deleting is done by hand.

USAGE (from the repo root):
  blender SourceArt/Blender/Characters/SK_Player_Officer.blend --background --python SourceArt/Tools/hidden_faces_report.py
  blender ... --python SourceArt/Tools/hidden_faces_report.py -- --distance 0.02
"""

import argparse
import sys

import bmesh
import bpy
from mathutils import Vector
from mathutils.bvhtree import BVHTree

HIDE_DISTANCE = 0.03  # 3 cm: clothing layers sit closer than this to what they cover
AXES = [Vector(d) for d in ((0, 0, 1), (0, 0, -1), (1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0))]


def parse_args():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    parser = argparse.ArgumentParser(prog="hidden_faces_report.py")
    parser.add_argument("--distance", type=float, default=HIDE_DISTANCE, help="max gap in metres (default 0.03)")
    return parser.parse_args(argv)


def world_bmesh(obj, depsgraph):
    bm = bmesh.new()
    bm.from_object(obj, depsgraph)  # evaluated: modifiers included, like the exported mesh
    bm.transform(obj.matrix_world)
    bm.normal_update()
    return bm


def is_hidden(face, others, distance):
    centre, normal = face.calc_center_median(), face.normal
    if normal.length == 0:
        return False
    for direction in [normal] + [a for a in AXES if a.dot(normal) > 0.3]:
        origin = centre + direction * 1e-4
        if not any(tree.ray_cast(origin, direction, distance)[0] is not None for tree in others):
            return False  # this direction can see out: visible
    return True


def main():
    args = parse_args()
    depsgraph = bpy.context.evaluated_depsgraph_get()
    meshes = [o for o in bpy.data.objects if o.type == "MESH" and o.visible_get()]
    data = {o.name: world_bmesh(o, depsgraph) for o in meshes}
    trees = {name: BVHTree.FromBMesh(bm) for name, bm in data.items()}

    rows = []
    for name, bm in data.items():
        others = [tree for other, tree in trees.items() if other != name]
        tris = sum(len(f.verts) - 2 for f in bm.faces)
        hidden = sum(len(f.verts) - 2 for f in bm.faces if is_hidden(f, others, args.distance))
        rows.append((tris, hidden, name))

    rows.sort(reverse=True)
    total = sum(r[0] for r in rows)
    total_hidden = sum(r[1] for r in rows)
    lines = ["", "=== hidden faces report (%s, gap %.3f m) ===" % (bpy.path.basename(bpy.data.filepath), args.distance),
             "%-28s %9s %9s %6s" % ("mesh", "tris", "hidden", "%")]
    for tris, hidden, name in rows:
        lines.append("%-28s %9d %9d %5.0f%%" % (name, tris, hidden, 100.0 * hidden / max(tris, 1)))
    lines.append("%-28s %9d %9d %5.0f%%" % ("TOTAL", total, total_hidden, 100.0 * total_hidden / max(total, 1)))
    lines.append("without hidden faces: %d tris" % (total - total_hidden))
    print("\n".join(lines + [""]))
    for bm in data.values():
        bm.free()


if __name__ == "__main__":
    main()
