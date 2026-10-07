"""
ingest_fbx.py - bring a raw FBX (made outside the pipeline) into a pipeline-ready .blend.

Raw FBX files exported with Blender's default settings carry a 90 deg X rotation on every mesh,
use free-form object / material names and often have no UV map. This script fixes the file
side of that ONCE, so the result can be exported with export_fbx.py like any other asset:

  1. imports each FBX into its own collection named after the asset (SM_Gun_Tier1, ...)
  2. applies rotation + scale (verifies the world bounding box did not move)
  3. renames the root mesh (or armature -> "Armature") to the asset name; meshes named like a bone
     get a _Mesh suffix (Unity Humanoid needs unique names: mesh "Head" vs bone "Head")
  4. merges duplicate materials (RG_Polymer.001, MAT_Navy_Fabric__guns_tmp) and renames
     them with the material prefix (RG_Polymer -> M_Gun_Polymer), then drops unused slots
  5. adds a placeholder Smart UV Project map to meshes without UVs (replaced by the palette pass)
  6. --sockets: adds a Socket_Muzzle empty at the front-most point (-Y) of the asset
  7. saves everything into one .blend

Geometry is never changed apart from the transform apply.

USAGE (from the repo root; source files are "path=AssetName"):
  blender --background --factory-startup --python SourceArt/Tools/ingest_fbx.py -- \
      --blend SourceArt/Blender/Weapons/Guns.blend --material-prefix M_Gun_ --sockets \
      ../FBX_Files/pistol.fbx=SM_Gun_Tier1 ../FBX_Files/ar.fbx=SM_Gun_Tier2

Then export:  blender SourceArt/Blender/Weapons/Guns.blend --background --python SourceArt/Tools/export_fbx.py -- --all
"""

import argparse
import math
import os
import re
import sys

import bpy
from mathutils import Vector

EPSILON = 1e-4
MUZZLE_SLICE = 0.005  # vertices within 5 mm of the front-most point define the muzzle centre


def parse_args():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    parser = argparse.ArgumentParser(prog="ingest_fbx.py")
    parser.add_argument("--blend", required=True, help=".blend file to write")
    parser.add_argument("--material-prefix", required=True, help="e.g. M_Gun_ or M_Player_")
    parser.add_argument("--sockets", action="store_true", help="add Socket_Muzzle (weapons)")
    parser.add_argument("sources", nargs="+", help="path/to/file.fbx=AssetName")
    return parser.parse_args(argv)


# ----------------------------------------------------------------------------
# Helpers
# ----------------------------------------------------------------------------
def world_bbox(objects):
    lo = Vector((math.inf,) * 3)
    hi = Vector((-math.inf,) * 3)
    for obj in objects:
        for corner in obj.bound_box:
            w = obj.matrix_world @ Vector(corner)
            lo = Vector(map(min, lo, w))
            hi = Vector(map(max, hi, w))
    return lo, hi


def principled_values(mat):
    """Comparable tuple of the values that end up in the Unity material."""
    node = None
    if mat.node_tree:
        node = next((n for n in mat.node_tree.nodes if n.type == "BSDF_PRINCIPLED"), None)
    if node is None:
        return tuple(round(c, 4) for c in mat.diffuse_color), round(mat.metallic, 4), round(mat.roughness, 4)

    def val(name):
        sock = node.inputs.get(name)
        if sock is None:
            return None
        v = sock.default_value
        return tuple(round(c, 4) for c in v) if hasattr(v, "__len__") else round(v, 4)

    return (val("Base Color"), val("Metallic"), val("Roughness"), val("Alpha"),
            val("Emission Color"), val("Emission Strength"))


def material_stem(name):
    """RG_Polymer.001 -> RG_Polymer, MAT_Navy_Fabric__guns_tmp -> MAT_Navy_Fabric"""
    previous = None
    while previous != name:
        previous = name
        name = re.sub(r"(\.\d{3}|__.*)$", "", name)
    return name


def pipeline_material_name(stem, prefix):
    if stem.startswith(prefix):
        return stem
    # Drop the source tool's own prefix (RG_, MAT_, M_) before adding ours.
    stripped = re.sub(r"^(?:[A-Z]{1,4})_", "", stem)
    return prefix + stripped


def with_object(obj):
    return bpy.context.temp_override(object=obj, active_object=obj, selected_objects=[obj],
                                     selected_editable_objects=[obj])


def merge_duplicate_slots(obj):
    """After material merging one mesh can hold the same material twice: point faces at the first slot."""
    first_index = {}
    remap = {}
    for i, slot in enumerate(obj.material_slots):
        key = slot.material.name if slot.material else None
        first_index.setdefault(key, i)
        remap[i] = first_index[key]
    if all(k == v for k, v in remap.items()):
        return
    for poly in obj.data.polygons:
        poly.material_index = remap.get(poly.material_index, poly.material_index)


# ----------------------------------------------------------------------------
# Steps
# ----------------------------------------------------------------------------
def import_asset(path, asset_name):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=path)
    new_objects = [o for o in bpy.data.objects if o not in before]
    if not new_objects:
        raise SystemExit("ingest: nothing imported from " + path)

    collection = bpy.data.collections.new(asset_name)
    bpy.context.scene.collection.children.link(collection)
    for obj in new_objects:
        for owner in list(obj.users_collection):
            owner.objects.unlink(obj)
        collection.objects.link(obj)
    return collection, new_objects


def apply_transforms(objects, log):
    targets = [o for o in objects if o.type in ("MESH", "ARMATURE")]
    meshes = [o for o in targets if o.type == "MESH"]
    before = world_bbox(meshes)

    for obj in targets:
        if obj.type == "MESH" and obj.data.users > 1:
            obj.data = obj.data.copy()  # transform_apply refuses shared mesh data

    bpy.ops.object.select_all(action="DESELECT")
    for obj in targets:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = targets[0]
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    bpy.context.view_layer.update()

    after = world_bbox(meshes)
    drift = max((before[0] - after[0]).length, (before[1] - after[1]).length)
    if drift > 1e-3:
        raise SystemExit("ingest: transform apply moved the geometry by %.4f m - aborting" % drift)
    log.append("  applied rotation/scale on %d objects (bbox drift %.6f m)" % (len(targets), drift))


def rename_root(asset_name, objects, log):
    roots = [o for o in objects if o.parent is None and o.type in ("MESH", "ARMATURE")]
    armature = next((o for o in roots if o.type == "ARMATURE"), None)
    if armature:
        # Unity strips "Armature|" from clip names; keep the rig name predictable.
        log.append("  armature %s -> Armature" % armature.name)
        armature.name = "Armature"
        armature.data.name = "Armature"
        bones = {b.name for b in armature.data.bones}
        for obj in objects:
            if obj is not armature and obj.name in bones:
                log.append("  %s -> %s_Mesh (same name as a bone)" % (obj.name, obj.name))
                obj.name = obj.name + "_Mesh"
                if obj.type == "MESH":
                    obj.data.name = obj.name
        return armature
    if len(roots) != 1:
        raise SystemExit("ingest: %s expected one root mesh, found %s" % (asset_name, [o.name for o in roots]))
    root = roots[0]
    log.append("  root mesh %s -> %s" % (root.name, asset_name))
    root.name = asset_name
    root.data.name = asset_name
    for obj in objects:
        if obj.type == "MESH" and obj is not root:
            obj.data.name = obj.name
    return root


def normalise_materials(prefix, log):
    """Merge duplicates by name stem (only if their values match) and apply the pipeline name."""
    groups = {}
    for mat in bpy.data.materials:
        groups.setdefault(material_stem(mat.name), []).append(mat)

    for stem, mats in groups.items():
        mats.sort(key=lambda m: (m.name != stem, m.name))  # keep the cleanest name as the survivor
        keep = mats[0]
        for dup in mats[1:]:
            if principled_values(dup) != principled_values(keep):
                log.append("  WARN material %s differs from %s - kept separate" % (dup.name, keep.name))
                continue
            dup.user_remap(keep)
            log.append("  merged material %s -> %s" % (dup.name, keep.name))
            bpy.data.materials.remove(dup)

    for mat in list(bpy.data.materials):
        new_name = pipeline_material_name(material_stem(mat.name), prefix)
        if mat.name != new_name:
            mat.name = new_name
    for mat in bpy.data.materials:
        if not mat.name.startswith(prefix):
            log.append("  WARN material name clash: %s" % mat.name)


def tidy_slots(objects, log):
    for obj in objects:
        if obj.type != "MESH":
            continue
        before = len(obj.material_slots)
        merge_duplicate_slots(obj)
        with with_object(obj):
            bpy.ops.object.material_slot_remove_unused()
        if len(obj.material_slots) != before:
            log.append("  %s: material slots %d -> %d" % (obj.name, before, len(obj.material_slots)))


def add_placeholder_uvs(objects, log):
    for obj in objects:
        if obj.type != "MESH" or obj.data.uv_layers:
            continue
        bpy.ops.object.select_all(action="DESELECT")
        obj.select_set(True)
        bpy.context.view_layer.objects.active = obj
        bpy.ops.object.mode_set(mode="EDIT")
        bpy.ops.mesh.select_all(action="SELECT")
        bpy.ops.uv.smart_project(angle_limit=math.radians(66), island_margin=0.01)
        bpy.ops.object.mode_set(mode="OBJECT")
        obj.data.uv_layers[0].name = "UVMap"
        log.append("  %s: no UVs - added placeholder Smart UV Project map" % obj.name)


def add_muzzle_socket(collection, root, objects, log):
    meshes = [o for o in objects if o.type == "MESH"]
    points = [o.matrix_world @ v.co for o in meshes for v in o.data.vertices]
    front_y = min(p.y for p in points)
    front = [p for p in points if p.y <= front_y + MUZZLE_SLICE]
    centre = Vector((sum(p.x for p in front) / len(front), front_y, sum(p.z for p in front) / len(front)))

    socket = bpy.data.objects.new("Socket_Muzzle", None)
    socket.empty_display_type = "ARROWS"
    socket.empty_display_size = 0.05
    collection.objects.link(socket)
    socket.parent = root
    socket.matrix_parent_inverse = root.matrix_world.inverted()
    socket.location = centre
    log.append("  Socket_Muzzle at (%.3f, %.3f, %.3f) - Blender coords, -Y = forward"
               % tuple(centre))


def main():
    args = parse_args()
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    scene.unit_settings.system = "METRIC"
    scene.unit_settings.scale_length = 1.0

    log = []
    for source in args.sources:
        path, _, asset_name = source.rpartition("=")
        if not path or not asset_name.startswith(("SM_", "SK_")):
            raise SystemExit("ingest: expected path=SM_Name or path=SK_Name, got " + source)
        path = os.path.abspath(path)
        log.append("%s <- %s" % (asset_name, path))

        collection, objects = import_asset(path, asset_name)
        apply_transforms(objects, log)
        root = rename_root(asset_name, objects, log)
        if args.sockets:
            add_muzzle_socket(collection, root, objects, log)

    # Materials are shared between assets in the same .blend (one M_Gun_Polymer for every gun).
    normalise_materials(args.material_prefix, log)
    all_objects = [o for c in bpy.context.scene.collection.children for o in c.all_objects]
    tidy_slots(all_objects, log)
    add_placeholder_uvs(all_objects, log)

    bpy.data.orphans_purge(do_recursive=True)
    out = os.path.abspath(args.blend)
    os.makedirs(os.path.dirname(out), exist_ok=True)
    bpy.ops.wm.save_as_mainfile(filepath=out)

    log.append("materials (%d): %s" % (len(bpy.data.materials), ", ".join(sorted(m.name for m in bpy.data.materials))))
    log.append("saved -> " + out)
    print("\n".join(["", "=== ingest_fbx report ==="] + log + [""]))


if __name__ == "__main__":
    main()
