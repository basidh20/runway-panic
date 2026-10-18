"""
rig_bird.py - turn a raw bird (separate parts, no armature) into a rigged SK_Bird*.blend.

The source (.fbx or .blend) holds mesh parts named <Prefix>_<Part> (Bird_Body, Hawk_Wing_L, ...):
body, head, beak, eyes, wings, tail, legs, feet, feather details, optionally primary feathers,
parented to an Empty. Each part's origin sits at its joint (head at the neck, wings at the shoulders,
legs at the hips, feet at the ankles), so the rig is built from those origins. Every bird gets the
SAME bone names, mesh name (Bird_Mesh) and blend shape names, so one set of animation clips fits all.

  1. imports the source (FBX helpers shared with ingest_fbx.py; a .blend is appended without its
     cameras / lights), unparents the parts, removes the Empty
  2. applies rotation/scale (aborts if the geometry moves)
  3. moves the pivot to the centre of mass (centre of the body's bounding box): the bird is always
     flying, so banking / pitching on a path must rotate around the body, not the toes
  4. names materials <material prefix>* (M_Bird_*, M_Bird_Hawk_*), adds a placeholder UV map where a part has none
  5. rigid skinning: every part is weighted 100% to its bone; the body and feather details blend
     smoothly between Body and Head around the neck so the head can turn without a seam, and the
     thigh feather ruff (feather-detail islands under the body around each hip) follows its leg
  6. joins the parts into ONE skinned mesh (one SkinnedMeshRenderer per bird in Unity) - shape keys
     (Wings_Spread, Tail_Spread) are kept and become blend shapes
  7. builds the armature (deform bones, Generic rig in Unity) and saves the .blend

Wings are rigid: they rotate at the shoulder but cannot fold at an elbow. Replacing this with
hand-painted weights / 2-bone wings later keeps the same bone and file names.

USAGE (from the repo root):
  blender --background --factory-startup --python SourceArt/Tools/rig_bird.py -- \
      --source ../FBX_Files/AirportBird_Base.fbx --blend SourceArt/Blender/Birds/SK_Bird.blend
  blender --background --factory-startup --python SourceArt/Tools/rig_bird.py -- \
      --source ../FBX_Files/AirportBird_Hawk_Head_Refined.blend --blend SourceArt/Blender/Birds/SK_Bird_Hawk.blend \
      --asset SK_Bird_Hawk --material-prefix M_Bird_Hawk_
Then export:  blender SourceArt/Blender/Birds/SK_Bird.blend --background --python SourceArt/Tools/export_fbx.py -- --all
"""

import argparse
import math
import os
import sys

import bpy
from mathutils import Matrix, Vector

sys.dont_write_bytecode = True  # no __pycache__ next to the tools
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import ingest_fbx as ingest  # noqa: E402  (shared import / material / UV helpers)

MESH_NAME = "Bird_Mesh"  # same on every bird: clips that key blend shapes bind to this path

# Part (object name without its "Bird_" / "Hawk_" prefix) -> bone.
# "NECK" = smooth Body/Head blend around the neck joint.
PART_BONES = {
    "Body": "NECK",
    "Feather_Details": "NECK",
    "Head": "Head",
    "Beak": "Head",
    "Eyes": "Head",
    "Tail": "Tail",
    "Wing_L": "Wing_L",
    "Wing_R": "Wing_R",
    "Primary_L": "Wing_L",  # optional: separate primary feathers ride on the wing
    "Primary_R": "Wing_R",
    "Leg_L": "Leg_L",
    "Leg_R": "Leg_R",
    "Foot_L": "Foot_L",
    "Foot_R": "Foot_R",
}
OPTIONAL_PARTS = {"Primary_L", "Primary_R"}

# Bone -> (parent, part whose origin is the joint)
BONES = {
    "Body": (None, "Body"),
    "Head": ("Body", "Head"),
    "Tail": ("Body", "Tail"),
    "Wing_L": ("Body", "Wing_L"),
    "Wing_R": ("Body", "Wing_R"),
    "Leg_L": ("Body", "Leg_L"),
    "Leg_R": ("Body", "Leg_R"),
    "Foot_L": ("Leg_L", "Foot_L"),
    "Foot_R": ("Leg_R", "Foot_R"),
}

NECK_BLEND = 0.015          # +-15 mm soft band across the neck plane
NECK_RADIUS = (0.09, 0.12)  # fade out 9-12 cm from the neck joint: further away everything belongs to Body
LEG_RUFF_RADIUS = 0.025     # feather islands under the body within 2.5 cm of a hip follow that leg
MIN_BONE_LENGTH = 0.03


def parse_args():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    parser = argparse.ArgumentParser(prog="rig_bird.py")
    parser.add_argument("--source", "--fbx", dest="source", required=True, help="raw bird .fbx or .blend")
    parser.add_argument("--blend", required=True, help=".blend file to write")
    parser.add_argument("--asset", default="SK_Bird", help="asset / collection name, e.g. SK_Bird_Hawk")
    parser.add_argument("--material-prefix", default="M_Bird_", help="e.g. M_Bird_Hawk_")
    return parser.parse_args(argv)


def smoothstep(edge0, edge1, x):
    t = min(max((x - edge0) / (edge1 - edge0), 0.0), 1.0)
    return t * t * (3.0 - 2.0 * t)


def mesh_bbox(obj):
    pts = [obj.matrix_world @ v.co for v in obj.data.vertices]
    lo = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
    hi = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
    return lo, hi


def append_blend(path, asset_name, log):
    """Append the mesh parts and Empties of a .blend (cameras, lights etc. are left behind)."""
    with bpy.data.libraries.load(path, link=False) as (src, dst):
        dst.objects = list(src.objects)
    collection = bpy.data.collections.new(asset_name)
    bpy.context.scene.collection.children.link(collection)
    objects = []
    for obj in dst.objects:
        if obj is None:
            continue
        if obj.type in ("MESH", "EMPTY"):
            collection.objects.link(obj)
            objects.append(obj)
        else:
            log.append("  skipped %s %s" % (obj.type.lower(), obj.name))
            bpy.data.objects.remove(obj)
    # Appended objects have no evaluated world matrix until the scene updates: without this every
    # part would read as sitting at the origin.
    bpy.context.view_layer.update()
    return collection, objects


def part_key(name):
    """Bird_Wing_L / Hawk_Wing_L -> Wing_L"""
    return name.split("_", 1)[1] if "_" in name else name


def flatten_hierarchy(objects, log):
    """Unparent every mesh (keeping its world transform) and delete the Empty roots."""
    meshes = [o for o in objects if o.type == "MESH"]
    for obj in meshes:
        world = obj.matrix_world.copy()
        obj.parent = None
        obj.matrix_world = world
    empties = [o for o in objects if o.type == "EMPTY"]
    for empty in empties:
        log.append("  removed Empty %s" % empty.name)
        bpy.data.objects.remove(empty)
    return meshes


def index_parts(meshes):
    """{part key: object}; aborts on unknown, missing or duplicated parts."""
    parts = {part_key(o.name): o for o in meshes}
    unknown = set(parts) - set(PART_BONES)
    missing = set(PART_BONES) - OPTIONAL_PARTS - set(parts)
    if unknown or missing or len(parts) != len(meshes):
        raise SystemExit("rig_bird: unexpected parts. unknown=%s missing=%s" % (sorted(unknown), sorted(missing)))
    return parts


def recentre(meshes, pivot):
    """Bake every part into world space with the pivot at the origin (shape keys included)."""
    for obj in meshes:
        offset = Matrix.Translation(-pivot) @ obj.matrix_world
        obj.data.transform(offset, shape_keys=True)
        obj.matrix_world = Matrix.Identity(4)


def mesh_islands(mesh):
    """Connected vertex islands (loose parts) via union-find over the edges."""
    parent = list(range(len(mesh.vertices)))

    def find(i):
        while parent[i] != i:
            parent[i] = parent[parent[i]]
            i = parent[i]
        return i

    for edge in mesh.edges:
        a, b = find(edge.vertices[0]), find(edge.vertices[1])
        if a != b:
            parent[a] = b
    islands = {}
    for i in range(len(mesh.vertices)):
        islands.setdefault(find(i), []).append(i)
    return list(islands.values())


def leg_ruff_islands(obj, body_bottom, hips):
    """Feather islands hanging under the body around a hip (the thigh ruff) -> {vertex index: leg bone}."""
    owner = {}
    for island in mesh_islands(obj.data):
        centre = sum((obj.data.vertices[i].co for i in island), Vector()) / len(island)
        if centre.z >= body_bottom:
            continue
        for bone, hip in hips.items():
            if (centre.xy - hip.xy).length < LEG_RUFF_RADIUS:
                owner.update({i: bone for i in island})
    return owner


def assign_weights(meshes, neck_joint, head_dir, body_bottom, hips, log):
    for obj in meshes:
        bone = PART_BONES[part_key(obj.name)]
        if bone != "NECK":
            group = obj.vertex_groups.new(name=bone)
            group.add([v.index for v in obj.data.vertices], 1.0, "REPLACE")
            continue

        ruff = leg_ruff_islands(obj, body_bottom, hips)
        if ruff:
            log.append("  %s: %d thigh-ruff vertices follow the legs" % (obj.name, len(ruff)))
        for leg in set(ruff.values()):
            obj.vertex_groups.new(name=leg).add([i for i, b in ruff.items() if b == leg], 1.0, "REPLACE")

        body = obj.vertex_groups.new(name="Body")
        head = obj.vertex_groups.new(name="Head")
        for v in obj.data.vertices:
            if v.index in ruff:
                continue
            rel = v.co - neck_joint
            along = rel.dot(head_dir)
            w = smoothstep(-NECK_BLEND, NECK_BLEND, along) * (1.0 - smoothstep(*NECK_RADIUS, rel.length))
            if w < 1.0:
                body.add([v.index], 1.0 - w, "REPLACE")
            if w > 0.0:
                head.add([v.index], w, "REPLACE")


def unify_uv_names(meshes):
    # join() merges UV maps by name: one shared name keeps a single UV channel.
    for obj in meshes:
        for i, layer in enumerate(obj.data.uv_layers):
            layer.name = "UVMap" if i == 0 else "UVMap.%d" % i


def join_parts(meshes, body, log):
    bpy.ops.object.select_all(action="DESELECT")
    for obj in meshes:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = body
    bpy.ops.object.join()
    body.name = MESH_NAME
    body.data.name = MESH_NAME
    keys = [k.name for k in body.data.shape_keys.key_blocks] if body.data.shape_keys else []
    log.append("  joined %d parts -> %s (shape keys: %s)" % (len(meshes), MESH_NAME, ", ".join(keys) or "none"))
    return body


def build_armature(collection, joints, tips):
    data = bpy.data.armatures.new("Armature")
    arm = bpy.data.objects.new("Armature", data)
    collection.objects.link(arm)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode="EDIT")
    for name, (parent, _) in BONES.items():
        bone = data.edit_bones.new(name)
        bone.head = joints[name]
        tail = tips[name]
        if (tail - bone.head).length < MIN_BONE_LENGTH:
            tail = bone.head + (tail - bone.head).normalized() * MIN_BONE_LENGTH
        bone.tail = tail
        bone.use_deform = True
    for name, (parent, _) in BONES.items():
        if parent:
            data.edit_bones[name].parent = data.edit_bones[parent]
    bpy.ops.object.mode_set(mode="OBJECT")
    return arm


def main():
    args = parse_args()
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    scene.unit_settings.system = "METRIC"
    scene.unit_settings.scale_length = 1.0

    source = os.path.abspath(args.source)
    log = ["%s <- %s" % (args.asset, source)]
    if source.lower().endswith(".blend"):
        collection, objects = append_blend(source, args.asset, log)
    else:
        collection, objects = ingest.import_asset(source, args.asset)
    meshes = flatten_hierarchy(objects, log)
    by_name = index_parts(meshes)
    ingest.apply_transforms(meshes, log)

    # Joint = the part's origin (placed by the modeller). Read before the parts are baked to the pivot.
    origins = {name: o.matrix_world.translation.copy() for name, o in by_name.items()}
    boxes = {name: mesh_bbox(o) for name, o in by_name.items()}

    body_lo, body_hi = boxes["Body"]
    pivot = (body_lo + body_hi) / 2
    pivot.x = 0.0  # keep the pivot on the symmetry plane
    log.append("  pivot (centre of mass) at (%.3f, %.3f, %.3f) - Blender coords" % tuple(pivot))
    log.append("  lowest point (feet) %.3f m below the pivot" % (pivot.z - min(b[0].z for b in boxes.values())))

    recentre(meshes, pivot)
    origins = {n: p - pivot for n, p in origins.items()}
    boxes = {n: (lo - pivot, hi - pivot) for n, (lo, hi) in boxes.items()}
    centre = {n: (lo + hi) / 2 for n, (lo, hi) in boxes.items()}

    joints = {bone: (Vector((0, 0, 0)) if bone == "Body" else origins[part]) for bone, (_, part) in BONES.items()}
    tips = {
        "Body": Vector((0.0, boxes["Body"][1].y * 0.5, 0.0)),               # points back toward the tail
        "Head": centre["Head"],
        "Tail": Vector((0.0, boxes["Tail"][1].y, centre["Tail"].z)),
        "Wing_L": centre["Wing_L"],
        "Wing_R": centre["Wing_R"],
        "Leg_L": joints["Foot_L"],
        "Leg_R": joints["Foot_R"],
        "Foot_L": Vector((joints["Foot_L"].x, boxes["Foot_L"][0].y, joints["Foot_L"].z)),  # toes point -Y
        "Foot_R": Vector((joints["Foot_R"].x, boxes["Foot_R"][0].y, joints["Foot_R"].z)),
    }

    ingest.normalise_materials(args.material_prefix, log)
    ingest.tidy_slots(meshes, log)
    ingest.add_placeholder_uvs(meshes, log)
    unify_uv_names(meshes)

    head_dir = (centre["Head"] - joints["Head"]).normalized()
    hips = {"Leg_L": joints["Leg_L"], "Leg_R": joints["Leg_R"]}
    assign_weights(meshes, joints["Head"], head_dir, boxes["Body"][0].z, hips, log)
    mesh = join_parts(meshes, by_name["Body"], log)

    arm = build_armature(collection, joints, tips)
    mesh.parent = arm
    modifier = mesh.modifiers.new("Armature", "ARMATURE")
    modifier.object = arm

    for name in BONES:
        j = joints[name]
        log.append("  bone %-7s joint (%.3f, %.3f, %.3f)" % (name, j.x, j.y, j.z))
    tris = sum(len(p.vertices) - 2 for p in mesh.data.polygons)
    log.append("  %d tris, %d bones, %d material slots" % (tris, len(BONES), len(mesh.material_slots)))

    bpy.data.orphans_purge(do_recursive=True)
    out = os.path.abspath(args.blend)
    os.makedirs(os.path.dirname(out), exist_ok=True)
    bpy.ops.wm.save_as_mainfile(filepath=out)
    log.append("materials (%d): %s" % (len(bpy.data.materials), ", ".join(sorted(m.name for m in bpy.data.materials))))
    log.append("saved -> " + out)
    print("\n".join(["", "=== rig_bird report ==="] + log + [""]))


if __name__ == "__main__":
    main()
