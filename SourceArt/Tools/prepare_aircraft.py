"""
prepare_aircraft.py - turn the delivered transport aircraft into the pipeline asset SM_Prop_Aircraft (Blender 5.2).

The source file (GV-IS/Aircraft/Aircraft_Transport.blend, outside the repo) is only read. The result is saved as
SourceArt/Blender/Props/SM_Prop_Aircraft.blend, which export_fbx.py then exports like every other model.

What it changes, and why:
  1. Scale 0.36 (uniform, baked into the mesh data and the child locations): 22.2 m span -> 8.0 m, so the plane fits
     the World Builder's light-aircraft parking slot (8 m span, 9 m long, 4 m aisles; Docs/WorldBuilder/
     AircraftArtHandoff.md). The wings land at ~1.3-1.5 m, the height the handoff asks for (cover for the player).
     Baked, not a Unity scale, so the prefab root stays (1, 1, 1) as the handoff requires.
  2. Names: collection Aircraft -> SM_Prop_Aircraft (export_fbx.py routes "Prop" to Models/Props/); objects
     AC_* -> Aircraft_* (AC_ is the project prefix for Animator Controllers); materials M_X -> M_Aircraft_X
     (Unity materials are looked up by name, and M_Body / M_Accent are too generic).
  3. Base livery = the side planes' grey/teal: M_Aircraft_Accent orange #F2A61A -> teal #1E8C8C. The middle plane's
     white/red livery is a variant in aircraft_liveries.json (same UVs, its own Albedo texture).
  4. UVs: every part had its own 0-1 square, so the parts overlapped each other. The atlas needs ONE shared square:
     the existing islands are kept (no new seams), averaged to one texel density and packed together
     (margin 0.004 FRACTION, the same as the birds: gap 0.008 x 256 px = 2.05 px).

The preview cameras, sun and ground (collection Preview_Env) are kept for renders; they are not exported.

Usage (from the repo root):
  blender --background --python SourceArt/Tools/prepare_aircraft.py -- <in.blend> <out.blend>
"""
import sys

import bpy
from mathutils import Matrix, Vector

SCALE = 0.36
COLLECTION_OLD, COLLECTION_NEW = "Aircraft", "SM_Prop_Aircraft"
ROOT_OLD, ROOT_NEW = "Aircraft_Transport", "Aircraft_Root"
MARGIN = 0.004
ACCENT_SIDES = "#1E8C8C"   # teal; was #F2A61A orange


def srgb_to_linear(hex_colour):
    h = hex_colour.lstrip("#")
    out = []
    for i in (0, 2, 4):
        c = int(h[i:i + 2], 16) / 255.0
        out.append(c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4)
    return out


def world_bounds(objs):
    pts = [o.matrix_world @ Vector(c) for o in objs if o.type == "MESH" for c in o.bound_box]
    return Vector([min(p[i] for p in pts) for i in range(3)]), Vector([max(p[i] for p in pts) for i in range(3)])


def main():
    src, dst = sys.argv[sys.argv.index("--") + 1:][:2]
    bpy.ops.wm.open_mainfile(filepath=src)

    coll = bpy.data.collections[COLLECTION_OLD]
    objs = list(coll.all_objects)
    meshes = [o for o in objs if o.type == "MESH"]
    lo0, hi0 = world_bounds(objs)
    tris0 = sum(len(p.vertices) - 2 for o in meshes for p in o.data.polygons)

    # 1. Uniform scale. Every object has rotation 0, scale 1 and an identity parent inverse (checked), so scaling the
    #    vertices around each origin and every local location by the same factor scales the whole assembly.
    for o in objs:
        assert o.matrix_parent_inverse == Matrix.Identity(4) and tuple(o.scale) == (1, 1, 1), o.name
    for me in {o.data for o in meshes}:
        me.transform(Matrix.Scale(SCALE, 4))
    for o in objs:
        o.location = o.location * SCALE
    bpy.context.view_layer.update()

    # 1b. The fans were children of the engines (root > engine > fan, the only 3-level chain). The FBX exporter's
    #     "Apply Transform" axis conversion places such a grandchild wrongly: the fans came out 1.25 m under the
    #     ground in Unity (19 Oct, validator "wheels rest on the ground" failed). Parent them to the root instead;
    #     the engines sit at the origin with no rotation, so nothing moves and the fan pivot (spin axis) stays.
    root = bpy.data.objects[ROOT_OLD]
    for o in objs:
        if o.parent is not None and o.parent is not root:
            mw = o.matrix_world.copy()
            o.parent = root
            o.matrix_world = mw
    bpy.context.view_layer.update()

    # 2. Names.
    coll.name = COLLECTION_NEW
    for o in objs:
        if o.name == ROOT_OLD:
            o.name = ROOT_NEW
        elif o.name.startswith("AC_"):
            o.name = "Aircraft_" + o.name[3:]
            if o.data:
                o.data.name = o.name
    for m in {m for o in meshes for m in o.data.materials if m}:
        m.name = "M_Aircraft_" + m.name[2:]

    # 3. Base livery accent.
    bsdf = bpy.data.materials["M_Aircraft_Accent"].node_tree.nodes["Principled BSDF"]
    bsdf.inputs["Base Color"].default_value = srgb_to_linear(ACCENT_SIDES) + [1.0]

    # 4. One shared UV square for all parts: keep the islands, equalise texel density, pack.
    #    First cut every island along material borders (the birds/player do this with seams, rule 1): an island that
    #    mixes two materials cannot get one flat atlas colour (dry run without this: 91 wrong-colour triangles).
    #    Each material's faces are shifted by a different small offset, so neighbours of different materials no
    #    longer share UVs; the island shapes stay exactly the same and pack_islands moves them apart anyway.
    for me in {o.data for o in meshes}:
        uv = me.uv_layers.active.data
        for poly in me.polygons:
            for li in poly.loop_indices:
                uv[li].uv.x += poly.material_index * 0.01
    bpy.ops.object.select_all(action="DESELECT")
    for o in meshes:
        o.select_set(True)
    bpy.context.view_layer.objects.active = meshes[0]
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.select_all(action="SELECT")
    bpy.ops.uv.average_islands_scale()
    bpy.ops.uv.pack_islands(rotate=True, margin_method="FRACTION", margin=MARGIN, shape_method="CONCAVE")
    bpy.ops.object.mode_set(mode="OBJECT")

    lo1, hi1 = world_bounds(objs)
    tris1 = sum(len(p.vertices) - 2 for o in meshes for p in o.data.polygons)
    size0, size1 = hi0 - lo0, hi1 - lo1
    print("[prepare_aircraft] size before %.2f x %.2f x %.2f m (X span, Y length, Z height)" % tuple(size0))
    print("[prepare_aircraft] size after  %.2f x %.2f x %.2f m, min Z %.4f" % (*size1, lo1.z))
    print("[prepare_aircraft] tris %d -> %d" % (tris0, tris1))
    assert tris0 == tris1
    assert all(abs(size1[i] - size0[i] * SCALE) < 1e-3 for i in range(3)), "scale did not apply uniformly"

    bpy.ops.wm.save_as_mainfile(filepath=dst, compress=False)
    print("[prepare_aircraft] saved " + dst)


main()
