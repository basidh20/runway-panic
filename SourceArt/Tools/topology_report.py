"""
topology_report.py - READ-ONLY topology report per asset collection (SM_ / SK_) and per mesh object.

What it counts, on the mesh as authored (no modifiers, no triangulation):
  faces      triangles / quads / n-gons (5+ sides), and the triangle count after triangulation
  poles      vertices where 3, 5 or 6+ edges meet (4 is the regular case in a quad mesh); border vertices excluded
  edges      open (1 face), non-manifold (3+ faces), loose (0 faces)
  defects    zero-area faces, loose vertices, vertices closer than 0.1 mm to another one (unmerged doubles)
  shells     connected pieces: a model built from intersecting parts has many

The script never modifies or saves the .blend.

USAGE (from the repo root):
  blender SourceArt/Blender/Birds/SK_Bird.blend --background --python SourceArt/Tools/topology_report.py
  blender ... --python SourceArt/Tools/topology_report.py -- --markdown SourceArt/topology_report.md
      --markdown PATH   append the tables to PATH (one section per .blend) as well as printing them
"""

import argparse
import datetime
import os
import sys

import bmesh
import bpy

DOUBLES_DISTANCE = 0.0001  # 0.1 mm, Blender's default Merge by Distance threshold
ZERO_AREA = 1e-12          # m2
COLUMNS = ("verts", "faces", "tris", "quads", "ngons", "tris_out", "pole3", "pole5", "pole6", "open", "nonmanifold",
           "loose_edges", "loose_verts", "zero_area", "doubles", "shells")


def parse_args():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    parser = argparse.ArgumentParser(prog="topology_report.py")
    parser.add_argument("--markdown", help="append the report to this .md file")
    return parser.parse_args(argv)


def count_shells(bm):
    seen, shells = set(), 0
    for start in bm.verts:
        if start.index in seen:
            continue
        shells += 1
        seen.add(start.index)
        stack = [start]
        while stack:
            for edge in stack.pop().link_edges:
                for other in edge.verts:
                    if other.index not in seen:
                        seen.add(other.index)
                        stack.append(other)
    return shells


def measure(obj):
    bm = bmesh.new()
    bm.from_mesh(obj.data)  # as authored: quads and n-gons stay what they are
    bm.verts.index_update()
    row = dict.fromkeys(COLUMNS, 0)
    row["verts"], row["faces"] = len(bm.verts), len(bm.faces)
    for face in bm.faces:
        sides = len(face.verts)
        row["tris" if sides == 3 else "quads" if sides == 4 else "ngons"] += 1
        row["tris_out"] += sides - 2
        if face.calc_area() < ZERO_AREA:
            row["zero_area"] += 1
    for edge in bm.edges:
        faces = len(edge.link_faces)
        if faces == 0:
            row["loose_edges"] += 1
        elif faces == 1:
            row["open"] += 1
        elif faces > 2:
            row["nonmanifold"] += 1
    for vert in bm.verts:
        if not vert.link_edges:
            row["loose_verts"] += 1
        elif not vert.is_boundary and vert.is_manifold:
            valence = len(vert.link_edges)
            if valence == 3:
                row["pole3"] += 1
            elif valence == 5:
                row["pole5"] += 1
            elif valence >= 6:
                row["pole6"] += 1
    row["doubles"] = len(bmesh.ops.find_doubles(bm, verts=bm.verts, dist=DOUBLES_DISTANCE)["targetmap"])
    row["shells"] = count_shells(bm)
    bm.free()
    return row


def add(total, row):
    for key in COLUMNS:
        total[key] += row[key]


def percent(part, whole):
    return "%.0f%%" % (100.0 * part / whole) if whole else "-"


def text_table(title, rows):
    head = "%-26s %8s %8s %8s %8s %6s %8s %7s %7s %7s %6s %6s %6s %6s" % (
        "mesh", "verts", "faces", "tris", "quads", "ngons", "tris out", "3-pole", "5-pole", "6+pole",
        "open", "non-m", "zero-A", "shells")
    lines = ["", "=== topology report: %s ===" % title, head]
    for name, r in rows:
        lines.append("%-26s %8d %8d %8d %8d %6d %8d %7d %7d %7d %6d %6d %6d %6d" % (
            name[:26], r["verts"], r["faces"], r["tris"], r["quads"], r["ngons"], r["tris_out"], r["pole3"], r["pole5"],
            r["pole6"], r["open"], r["nonmanifold"], r["zero_area"], r["shells"]))
    return lines


def markdown_table(title, rows):
    lines = ["### `%s`" % title, "",
             "| Mesh | Verts | Faces | Tris | Quads | N-gons | Quad share | Tris after triangulation | 3-poles | 5-poles | "
             "6+ poles | Open edges | Non-manifold edges | Zero-area faces | Doubles | Shells |",
             "|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|"]
    for name, r in rows:
        label = "**%s**" % name if name == "TOTAL" else "`%s`" % name
        lines.append("| %s | %s | %s | %s | %s | %s | %s | %s | %s | %s | %s | %s | %s | %s | %s | %s |" % (
            label, "{:,}".format(r["verts"]), "{:,}".format(r["faces"]), "{:,}".format(r["tris"]),
            "{:,}".format(r["quads"]), "{:,}".format(r["ngons"]), percent(r["quads"], r["faces"]),
            "{:,}".format(r["tris_out"]), "{:,}".format(r["pole3"]), "{:,}".format(r["pole5"]),
            "{:,}".format(r["pole6"]), "{:,}".format(r["open"]), "{:,}".format(r["nonmanifold"]),
            "{:,}".format(r["zero_area"]), "{:,}".format(r["doubles"]), "{:,}".format(r["shells"])))
    return lines + [""]


def main():
    args = parse_args()
    blend = bpy.path.basename(bpy.data.filepath)
    collections = sorted((c for c in bpy.data.collections if c.name.startswith(("SM_", "SK_"))), key=lambda c: c.name)
    if not collections:
        print("topology_report: no SM_/SK_ collections found in %s" % blend)
        return

    console = []
    markdown = ["## %s" % blend, "",
                "Generated %s by `SourceArt/Tools/topology_report.py` (Blender %s). Counted on the mesh as authored: "
                "no modifiers, no triangulation." % (datetime.datetime.now().strftime("%Y-%m-%d %H:%M"), bpy.app.version_string),
                ""]
    for collection in collections:
        meshes = sorted((o for o in collection.all_objects if o.type == "MESH" and o.visible_get()), key=lambda o: o.name)
        rows, total = [], dict.fromkeys(COLUMNS, 0)
        for obj in meshes:
            row = measure(obj)
            rows.append((obj.name, row))
            add(total, row)
        if len(rows) != 1:
            rows.append(("TOTAL", total))
        console += text_table(collection.name, rows)
        console.append("quads %s of faces, doubles %d, loose verts %d, loose edges %d" % (
            percent(total["quads"], total["faces"]), total["doubles"], total["loose_verts"], total["loose_edges"]))
        markdown += markdown_table(collection.name, rows)
    print("\n".join(console + [""]))

    if args.markdown:
        path = os.path.abspath(args.markdown)
        new_file = not os.path.exists(path)
        with open(path, "a", encoding="utf-8", newline="\n") as handle:
            if new_file:
                handle.write("# Topology report\n\n")
            handle.write("\n".join(markdown) + "\n")
        print("topology_report: appended to %s" % path)


if __name__ == "__main__":
    main()
