"""
export_fbx.py - Runway Panic FBX exporter (Blender 5.2 -> Unity 6000.6.4f1).

ONE ASSET = ONE COLLECTION. Name the collection with its asset prefix:
    SK_Bird            armature + mesh                 -> Assets/_Project/Art/Models/Birds/SK_Bird.fbx
    SM_Gun_Tier1       mesh (+ optional Socket_* empties) -> .../Models/Weapons/SM_Gun_Tier1.fbx
    SK_Player_Officer  mesh + armature                 -> .../Models/Characters/SK_Player_Officer.fbx
The second name token (Bird / Gun / Player / Prop / Env) picks the Unity folder.

Hidden objects are NOT exported (handy for keeping blockouts / references in the file).

Every export also writes a material manifest next to the Unity materials:
    Assets/_Project/Art/Materials/<Folder>/<Asset>.materials.json
Unity (ArtMaterialBuilder.cs) turns it into one URP Lit M_ material per Blender material,
so colours / metallic / roughness stay authored in Blender.

The pre-export checks only READ the scene. They warn or abort; they never modify geometry.

USAGE
  Blender Text Editor: Text > Open this file, click the asset's collection in the Outliner,
  then Run Script (Alt+P). Results appear in a popup.

  Command line (from the repo root):
    blender SourceArt/Blender/Weapons/Guns.blend --background --python SourceArt/Tools/export_fbx.py -- --all

  Options (everything after the lone "--"):
    --collection NAME   export this collection (default: the active collection)
    --all               export every collection whose name starts with SM_ or SK_
    --check             run the checks and print the report, but write no files
    --mixamo            export an UNRIGGED SK_ mesh for Mixamo upload -> SourceArt/Mixamo/
    --out DIR           write here instead of the Unity folder (for testing)

Keep the routing and budget tables in sync with SourceArt/README.md and
Assets/_Project/Editor/Art/ArtBudgets.cs.
"""

import argparse
import json
import os
import sys

import bpy

# ----------------------------------------------------------------------------
# Options used when the script is run from Blender's Text Editor (no CLI args).
# ----------------------------------------------------------------------------
TEXT_EDITOR_CHECK_ONLY = False
TEXT_EDITOR_MIXAMO = False

# ----------------------------------------------------------------------------
# Pipeline tables
# ----------------------------------------------------------------------------
MODELS_DIR = "Assets/_Project/Art/Models"
MATERIALS_DIR = "Assets/_Project/Art/Materials"
MIXAMO_DIR = "SourceArt/Mixamo"

# Second token of the asset name -> sub-folder of MODELS_DIR
ROUTES = {
    "Bird": "Birds",
    "Player": "Characters",
    "Gun": "Weapons",
    "Prop": "Props",
    "Env": "Environment",
}

# Asset-name prefix -> (max triangles, max deform bones or None). Longest prefix wins.
BUDGETS = {
    "SK_Bird": (1500, 15),
    "SK_Player": (8000, None),
    "SM_Gun": (2500, None),
    "SM_Prop": (500, None),
}

# More material slots = more submeshes = more draw calls. We aim for the shared palette material.
MAX_MATERIAL_SLOTS = 2

# Mirrors the agreed preset (CLAUDE.md section 5.3 / SourceArt/README.md).
FBX_SETTINGS = dict(
    use_selection=False,
    use_visible=True,
    use_active_collection=False,
    global_scale=1.0,
    apply_unit_scale=True,
    apply_scale_options="FBX_SCALE_ALL",
    axis_forward="-Z",
    axis_up="Y",
    use_space_transform=True,
    bake_space_transform=True,  # "Apply Transform"
    use_mesh_modifiers=True,
    mesh_smooth_type="FACE",
    use_triangles=False,  # Unity triangulates on import; keep quads for clean wireframe shots
    use_custom_props=False,
    add_leaf_bones=False,
    use_armature_deform_only=True,  # IK / control bones stay in Blender
    path_mode="AUTO",
    embed_textures=False,  # textures are imported separately as T_ files
    batch_mode="OFF",
)

ANIMATION_SETTINGS = dict(
    bake_anim=True,
    bake_anim_use_all_bones=True,
    bake_anim_use_nla_strips=True,  # one NLA strip (A_Bird_Fly, ...) = one Unity clip
    bake_anim_use_all_actions=False,
    bake_anim_force_startend_keying=True,
    bake_anim_step=1.0,
    bake_anim_simplify_factor=1.0,
)

EPSILON = 1e-4


# ----------------------------------------------------------------------------
# Helpers
# ----------------------------------------------------------------------------
class Report:
    def __init__(self, asset):
        self.asset = asset
        self.errors = []
        self.warnings = []
        self.info = []

    def error(self, msg):
        self.errors.append(msg)

    def warn(self, msg):
        self.warnings.append(msg)

    def note(self, msg):
        self.info.append(msg)

    def lines(self):
        out = []
        out += ["  ERROR  " + m for m in self.errors]
        out += ["  WARN   " + m for m in self.warnings]
        out += ["  info   " + m for m in self.info]
        return out


def find_repo_root():
    """Walk up from the .blend (or this script) to the folder that holds Assets/ and ProjectSettings/."""
    starts = []
    if bpy.data.filepath:
        starts.append(os.path.dirname(bpy.data.filepath))
    script = globals().get("__file__", "")
    if script and os.path.isfile(script):
        starts.append(os.path.dirname(os.path.abspath(script)))
    for start in starts:
        path = os.path.abspath(start)
        while True:
            if os.path.isdir(os.path.join(path, "Assets")) and os.path.isdir(os.path.join(path, "ProjectSettings")):
                return path
            parent = os.path.dirname(path)
            if parent == path:
                break
            path = parent
    return None


def budget_for(name):
    match = None
    for prefix in BUDGETS:
        if name.startswith(prefix) and (match is None or len(prefix) > len(match)):
            match = prefix
    return (match, BUDGETS[match]) if match else (None, None)


def triangle_count(obj, depsgraph):
    """Triangles after modifiers, i.e. what Unity will receive. Uses a temporary evaluated copy."""
    eval_obj = obj.evaluated_get(depsgraph)
    mesh = eval_obj.to_mesh()
    try:
        mesh.calc_loop_triangles()
        return len(mesh.loop_triangles)
    finally:
        eval_obj.to_mesh_clear()


def has_unapplied_rotation(obj):
    if obj.rotation_mode == "QUATERNION":
        q = obj.rotation_quaternion
        return abs(q.w - 1.0) > EPSILON or any(abs(c) > EPSILON for c in (q.x, q.y, q.z))
    if obj.rotation_mode == "AXIS_ANGLE":
        return abs(obj.rotation_axis_angle[0]) > EPSILON
    return any(abs(a) > EPSILON for a in obj.rotation_euler)


def has_unapplied_scale(obj):
    return any(abs(s - 1.0) > EPSILON for s in obj.scale)


# ----------------------------------------------------------------------------
# Checks (read-only)
# ----------------------------------------------------------------------------
def check_asset(collection, mixamo):
    name = collection.name
    report = Report(name)
    depsgraph = bpy.context.evaluated_depsgraph_get()

    # --- naming / routing ---
    parts = name.split("_")
    prefix = parts[0] + "_" if parts else ""
    if prefix not in ("SM_", "SK_"):
        report.error("collection name must start with SM_ (static) or SK_ (skinned): '%s'" % name)
        return report, None
    if len(parts) < 2 or parts[1] not in ROUTES:
        report.error("second name token must be one of %s, e.g. SM_Gun_Tier1 (got '%s')"
                     % ("/".join(ROUTES), name))
        return report, None
    is_skinned = prefix == "SK_"
    if mixamo and not is_skinned:
        report.error("--mixamo only makes sense for SK_ assets")

    objects = [o for o in collection.all_objects if o.visible_get()]
    if not objects:
        report.error("no visible objects in the collection")
        return report, None

    meshes = [o for o in objects if o.type == "MESH"]
    armatures = [o for o in objects if o.type == "ARMATURE"]
    skipped = [o.name for o in objects if o.type not in ("MESH", "ARMATURE", "EMPTY")]
    if skipped:
        report.warn("not exported (cameras/lights/curves/etc.): " + ", ".join(skipped))
    if not meshes:
        report.error("no mesh objects to export")

    # --- rig rules ---
    if is_skinned and not armatures and not mixamo:
        report.error("SK_ asset has no visible armature. Rig it, or use --mixamo for a Mixamo upload export")
    if not is_skinned and armatures:
        report.error("SM_ asset contains an armature (%s). Rename to SK_ or remove the rig"
                     % ", ".join(a.name for a in armatures))
    if len(armatures) > 1:
        report.error("more than one armature: " + ", ".join(a.name for a in armatures))
    if armatures:
        # Unity's avatar maps bones by transform name; a mesh called "Head" next to bone "Head" breaks it.
        bones = {b.name for b in armatures[0].data.bones}
        clashes = sorted(o.name for o in objects if o.name in bones)
        if clashes:
            report.error("object name(s) equal to a bone name: %s. Rename them (e.g. Head -> Head_Mesh)"
                         % ", ".join(clashes))

    # --- per-object transform / UV / material checks ---
    total_tris = 0
    for obj in objects:
        if obj.type not in ("MESH", "ARMATURE"):
            continue
        if has_unapplied_scale(obj):
            report.error("%s: scale is %s. Apply it (Ctrl+A > Scale)"
                         % (obj.name, tuple(round(s, 3) for s in obj.scale)))
        if has_unapplied_rotation(obj):
            report.warn("%s: rotation not applied (Ctrl+A > Rotation) - pivot axes may not match Unity"
                        % obj.name)
        if obj.parent is None and obj.location.length > EPSILON:
            report.warn("%s: root object is not at the world origin (%s); its origin is the Unity pivot"
                        % (obj.name, tuple(round(v, 3) for v in obj.location)))

    for obj in meshes:
        if len(obj.data.uv_layers) == 0:
            report.error("%s: has no UV map" % obj.name)
        for slot in obj.material_slots:
            if slot.material is None:
                report.warn("%s: empty material slot" % obj.name)
            elif not slot.material.name.startswith("M_"):
                report.warn("%s: material '%s' has no M_ prefix - Unity looks materials up by name"
                            % (obj.name, slot.material.name))
        slots = len(obj.material_slots)
        if slots > MAX_MATERIAL_SLOTS:
            report.warn("%s: %d material slots (max %d) - each slot is an extra draw call"
                        % (obj.name, slots, MAX_MATERIAL_SLOTS))
        tris = triangle_count(obj, depsgraph)
        total_tris += tris
        report.note("%s: %d tris, %d material slot(s), UV maps: %s"
                    % (obj.name, tris, slots, ", ".join(uv.name for uv in obj.data.uv_layers) or "-"))

    # --- budgets ---
    budget_prefix, budget = budget_for(name)
    deform_bones = None
    if armatures:
        deform_bones = sum(1 for b in armatures[0].data.bones if b.use_deform)
        report.note("%s: %d deform bones" % (armatures[0].name, deform_bones))
    if budget is None:
        report.warn("no budget defined for '%s' - add one to BUDGETS" % name)
    else:
        max_tris, max_bones = budget
        if total_tris > max_tris:
            report.warn("OVER BUDGET: %d tris > %d (%s)" % (total_tris, max_tris, budget_prefix))
        if max_bones is not None and deform_bones is not None and deform_bones > max_bones:
            report.warn("OVER BUDGET: %d deform bones > %d (%s)" % (deform_bones, max_bones, budget_prefix))
    report.note("TOTAL: %d tris%s" % (total_tris, "" if budget is None else " / budget %d" % budget[0]))

    route = ROUTES[parts[1]] if len(parts) > 1 and parts[1] in ROUTES else None
    return report, dict(is_skinned=is_skinned, has_armature=bool(armatures), route=route)


# ----------------------------------------------------------------------------
# Export
# ----------------------------------------------------------------------------
def output_path(name, meta, repo_root, out_override, mixamo):
    if out_override:
        folder = os.path.abspath(out_override)
    elif mixamo:
        folder = os.path.join(repo_root, *MIXAMO_DIR.split("/"))
    else:
        folder = os.path.join(repo_root, *MODELS_DIR.split("/"), meta["route"])
    return os.path.join(folder, name + ".fbx")


def export_asset(collection, meta, filepath, mixamo):
    settings = dict(FBX_SETTINGS)
    if mixamo:
        settings["object_types"] = {"MESH"}
        settings["bake_anim"] = False
    elif meta["is_skinned"]:
        settings["object_types"] = {"ARMATURE", "MESH", "EMPTY"}
        settings.update(ANIMATION_SETTINGS)
    else:
        settings["object_types"] = {"MESH", "EMPTY"}
        settings["bake_anim"] = False

    os.makedirs(os.path.dirname(filepath), exist_ok=True)
    result = bpy.ops.export_scene.fbx(filepath=filepath, collection=collection.name, **settings)
    return "FINISHED" in result


def material_entry(mat):
    """Values Unity needs for a URP Lit material. Colours are linear, as Blender stores them."""
    def color(values):
        return dict(r=round(values[0], 5), g=round(values[1], 5), b=round(values[2], 5),
                    a=round(values[3], 5) if len(values) > 3 else 1.0)

    entry = dict(name=mat.name, baseColor=color(mat.diffuse_color), metallic=mat.metallic,
                 roughness=mat.roughness, emissionColor=color((0, 0, 0, 1)), emissionStrength=0.0,
                 textured=False)
    node = None
    if mat.node_tree:
        node = next((n for n in mat.node_tree.nodes if n.type == "BSDF_PRINCIPLED"), None)
    if node is not None:
        inputs = node.inputs
        base = list(inputs["Base Color"].default_value)
        base[3] = inputs["Alpha"].default_value
        entry.update(baseColor=color(base),
                     metallic=inputs["Metallic"].default_value,
                     roughness=inputs["Roughness"].default_value,
                     emissionColor=color(inputs["Emission Color"].default_value),
                     emissionStrength=inputs["Emission Strength"].default_value,
                     textured=inputs["Base Color"].is_linked)
    entry["metallic"] = round(entry["metallic"], 4)
    entry["roughness"] = round(entry["roughness"], 4)
    entry["emissionStrength"] = round(entry["emissionStrength"], 4)
    return entry


def write_material_manifest(collection, meta, repo_root):
    """One manifest per asset; ArtMaterialBuilder.cs builds / updates the M_ materials from it."""
    used = {}
    for obj in collection.all_objects:
        if obj.type != "MESH" or not obj.visible_get():
            continue
        for slot in obj.material_slots:
            if slot.material is not None:
                used[slot.material.name] = slot.material
    manifest = dict(asset=collection.name,
                    source=os.path.relpath(bpy.data.filepath, repo_root).replace("\\", "/") if bpy.data.filepath else "",
                    materials=[material_entry(used[name]) for name in sorted(used)])
    folder = os.path.join(repo_root, *MATERIALS_DIR.split("/"), meta["route"])
    os.makedirs(folder, exist_ok=True)
    path = os.path.join(folder, collection.name + ".materials.json")
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        json.dump(manifest, f, indent=2)
        f.write("\n")
    return path, len(manifest["materials"])


def show_popup(lines, failed):
    def draw(menu, _context):
        for line in lines:
            menu.layout.label(text=line)
    title = "FBX export: errors - nothing written" if failed else "FBX export"
    bpy.context.window_manager.popup_menu(draw, title=title, icon="ERROR" if failed else "INFO")


def parse_args():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    parser = argparse.ArgumentParser(prog="export_fbx.py")
    parser.add_argument("--collection")
    parser.add_argument("--all", action="store_true")
    parser.add_argument("--check", action="store_true")
    parser.add_argument("--mixamo", action="store_true")
    parser.add_argument("--out")
    args = parser.parse_args(argv)
    if not argv:  # Text Editor run
        args.check = TEXT_EDITOR_CHECK_ONLY
        args.mixamo = TEXT_EDITOR_MIXAMO
    return args


def pick_collections(args):
    if args.all:
        return [c for c in bpy.data.collections if c.name.startswith(("SM_", "SK_"))]
    if args.collection:
        coll = bpy.data.collections.get(args.collection)
        if coll is None:
            raise SystemExit("export_fbx: collection '%s' not found" % args.collection)
        return [coll]
    return [bpy.context.view_layer.active_layer_collection.collection]


def main():
    args = parse_args()
    repo_root = find_repo_root()
    if repo_root is None and not args.out and not args.check:
        msg = "export_fbx: cannot find the Unity project. Save the .blend inside SourceArt/ first, or pass --out"
        print(msg)
        if not bpy.app.background:
            show_popup([msg], True)
        return 1

    collections = pick_collections(args)
    if not collections:
        print("export_fbx: no SM_/SK_ collections found")
        return 1

    all_lines = []
    any_failed = False
    for coll in collections:
        report, meta = check_asset(coll, args.mixamo)
        status = "OK"
        if report.errors:
            status = "FAILED - not exported"
            any_failed = True
        elif args.check:
            status = "checked (--check, not written)"
        else:
            path = output_path(coll.name, meta, repo_root, args.out, args.mixamo)
            if export_asset(coll, meta, path, args.mixamo):
                shown = os.path.relpath(path, repo_root) if repo_root else path
                status = "exported -> " + shown.replace("\\", "/")
                if not args.mixamo and not args.out:
                    manifest, count = write_material_manifest(coll, meta, repo_root)
                    report.note("material manifest: %d material(s) -> %s"
                                % (count, os.path.relpath(manifest, repo_root).replace("\\", "/")))
            else:
                status = "FAILED - exporter returned an error"
                any_failed = True
        all_lines.append("%s: %s" % (coll.name, status))
        all_lines += report.lines()

    print("\n".join(["", "=== export_fbx report ==="] + all_lines + [""]))
    if not bpy.app.background:
        show_popup(all_lines, any_failed)
    return 1 if any_failed else 0


if __name__ == "__main__":
    exit_code = main()
    if bpy.app.background:
        sys.exit(exit_code)
