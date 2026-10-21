"""
bake_atlas.py - fill an asset's UV islands with its flat material values (Blender 5.2). Used by export_fbx.py.

Every model here uses flat-colour materials: 13-49 of them per asset. In Unity each material slot is one more
submesh, and every submesh is one more draw call in every render pass. This module paints each UV island with the
values of the material its faces use, so the whole asset renders with ONE material:

    T_<Asset>_Atlas_Albedo.png   RGB = base colour (sRGB)
    T_<Asset>_Atlas_Mask.png     R = metallic, A = smoothness (1 - roughness); linear data (URP Lit "Metallic Map")

It is a fill, not a render: there is no lighting to bake, only one value per material. That allows two guarantees a
Cycles bake cannot give:
  - EVERY texel an island touches gets that island's value, even where the island is thinner than one texel
    (Cycles samples texel centres only, so a thin island would get its neighbour's colour).
  - Texels that two islands with different values both touch are counted ("bleed texels"). With Point filtering the
    GPU reads exactly the texel a UV falls in, so 0 bleed texels means every face shows exactly its own colour.

Materials that cannot share an opaque atlas material stay separate, with their own slot: transparent (alpha < 1)
and emissive ones.

The .blend is only read. `collapsed()` swaps the material slots for the duration of the FBX export and puts them
back, so the flat materials stay the authoring source in Blender.
"""

import contextlib
import json
import os
import struct
import zlib

import bpy
import numpy as np

# Asset-name prefix -> atlas settings. Longest prefix wins. Assets without an entry keep their flat materials.
#   size      texture width = height in texels. Flat colours need no detail, so the size only has to keep the gap
#             between islands above one texel diagonal (1.41 px), or two islands could touch the same texel.
#             Blender pads every island by the pack margin, so the gap is 2 x margin (measured: 0.008 on the birds):
#             0.008 x 256 = 2.05 px. The bleed count in the export report is the proof.
#   variants  JSON next to this file: recoloured copies of the asset (bird species) that reuse the same UVs.
# None = no atlas, for an asset whose name starts with an atlas asset's name (SM_Prop_ControlTower_Collision).
ATLAS = {
    "SK_Bird": dict(size=256, variants="bird_species.json"),
    "SK_Bird_Hawk": dict(size=256),
    "SK_Player": dict(size=512),   # pack margin 0.002 -> gap 0.004 x 512 = 2.05 px
    "SM_Gun": dict(size=128),
    "SM_Prop_Aircraft": dict(size=256, variants="aircraft_liveries.json"),   # margin 0.004, like the birds
    "SM_Prop_ControlTower": dict(size=256),                                   # margin 0.004 (prepare_tower.py)
    "SM_Prop_ControlTower_Collision": None,   # collider meshes only: no materials, no atlas
}

EPS = 1e-6               # texels; a triangle that only touches a texel's border does not count as touching it
MAX_GROUP_CELLS = 3_000_000
EMISSIVE_MIN = 0.001     # same test as ArtMaterialBuilder.cs
DILATE_STEPS = 8         # texels of own-colour padding around each island before the coarse fill


def config_for(asset):
    match = None
    for prefix in ATLAS:
        if asset.startswith(prefix) and (match is None or len(prefix) > len(match)):
            match = prefix
    return dict(ATLAS[match]) if match and ATLAS[match] else None


def asset_stem(asset):
    """SK_Bird_Hawk -> Bird_Hawk"""
    return asset.split("_", 1)[1]


def atlas_material_name(stem):
    return "M_%s_Atlas" % stem


def texture_name(stem, kind):
    return "T_%s_Atlas_%s" % (stem, kind)


def srgb_byte(linear):
    c = min(max(float(linear), 0.0), 1.0)
    s = 12.92 * c if c <= 0.0031308 else 1.055 * c ** (1 / 2.4) - 0.055
    return int(round(s * 255))


def unit_byte(value):
    return int(round(min(max(float(value), 0.0), 1.0) * 255))


def is_separate(entry):
    """Transparent and emissive materials cannot be merged into the opaque atlas material."""
    emission = entry["emissionStrength"] * max(entry["emissionColor"][k] for k in "rgb")
    return entry["baseColor"]["a"] < 0.999 or emission > EMISSIVE_MIN


def material_values(entry):
    """(albedo sRGB bytes, mask bytes) of one flat material."""
    base = entry["baseColor"]
    return ((srgb_byte(base["r"]), srgb_byte(base["g"]), srgb_byte(base["b"])),
            (unit_byte(entry["metallic"]), 0, 0, unit_byte(1.0 - entry["roughness"])))


def load_variants(filename):
    """bird_species.json -> {variant name: {material name: dict(rgb=(r, g, b) sRGB bytes or None, smoothness or None)}}"""
    path = os.path.join(os.path.dirname(os.path.abspath(__file__)), filename)
    with open(path, encoding="utf-8") as f:
        data = json.load(f)
    prefix = data["materialPrefix"]
    out = {}
    for name, spec in data["variants"].items():
        table = {}
        for part, hex_colour in spec["colours"].items():
            h = hex_colour.lstrip("#")
            table[prefix + part] = dict(rgb=(int(h[0:2], 16), int(h[2:4], 16), int(h[4:6], 16)),
                                        smoothness=spec.get("smoothness"))
        out[name] = table
    return out


# ----------------------------------------------------------------------------
# Reading the meshes (read-only)
# ----------------------------------------------------------------------------
def _get(coll, attr, dtype, n, k=1):
    a = np.empty(n * k, dtype=dtype)
    coll.foreach_get(attr, a)
    return a


def visible_meshes(collection):
    return sorted((o for o in collection.all_objects if o.type == "MESH" and o.visible_get()), key=lambda o: o.name)


def _islands(nf, ls, lt, lv, uv):
    """Island id per polygon: polygons joined across an edge whose two ends have the same UVs in both polygons."""
    pol = np.repeat(np.arange(nf), lt)
    idx = np.arange(len(lv))
    nxt = ls[pol] + (idx - ls[pol] + 1) % lt[pol]
    quv = np.round(uv * 1e6).astype(np.int64)
    a, b = lv, lv[nxt]
    swap = a > b
    key = np.stack([np.where(swap, b, a), np.where(swap, a, b),
                    np.where(swap, quv[nxt, 0], quv[:, 0]), np.where(swap, quv[nxt, 1], quv[:, 1]),
                    np.where(swap, quv[:, 0], quv[nxt, 0]), np.where(swap, quv[:, 1], quv[nxt, 1])], axis=1)
    order = np.lexsort(key.T[::-1])
    key, pol_s = key[order], pol[order]
    same = np.all(key[1:] == key[:-1], axis=1)

    parent = list(range(nf))

    def find(x):
        while parent[x] != x:
            parent[x] = parent[parent[x]]
            x = parent[x]
        return x

    for i in np.nonzero(same)[0].tolist():
        ra, rb = find(int(pol_s[i])), find(int(pol_s[i + 1]))
        if ra != rb:
            parent[ra] = rb
    roots = np.array([find(i) for i in range(nf)])
    return np.unique(roots, return_inverse=True)[1]


def read_asset(collection, material_entry):
    """Triangles of every visible mesh in UV space, with the material and island of each."""
    materials = {}            # name -> dict(id, entry, separate)
    tri_uv, tri_mat, tri_island, tri_obj, tri_alt = [], [], [], [], []
    objects, island_offset, ngons, uv_layers = [], 0, 0, set()
    for oi, obj in enumerate(visible_meshes(collection)):
        me = obj.data
        if not me.uv_layers:
            raise RuntimeError("%s has no UV map" % obj.name)
        uv_layers.add(len(me.uv_layers))
        nf, nl = len(me.polygons), len(me.loops)
        ls = _get(me.polygons, "loop_start", np.int64, nf)
        lt = _get(me.polygons, "loop_total", np.int64, nf)
        mi = _get(me.polygons, "material_index", np.int64, nf)
        lv = _get(me.loops, "vertex_index", np.int64, nl)
        uv = _get(me.uv_layers[0].data, "uv", np.float32, nl, 2).reshape(-1, 2).astype(np.float64)

        slot_ids = []
        for slot in obj.material_slots:
            mat = slot.material
            if mat is None:
                raise RuntimeError("%s has an empty material slot" % obj.name)
            if slot.link != "DATA":
                raise RuntimeError("%s: material slot '%s' is linked to the object, not the mesh" % (obj.name, mat.name))
            if mat.name not in materials:
                entry = material_entry(mat)
                materials[mat.name] = dict(id=len(materials), entry=entry, separate=is_separate(entry))
            slot_ids.append(materials[mat.name]["id"])
        if not slot_ids:
            raise RuntimeError("%s has no material" % obj.name)
        face_mat = np.array(slot_ids, dtype=np.int64)[np.minimum(mi, len(slot_ids) - 1)]
        island = _islands(nf, ls, lt, lv, uv) + island_offset
        island_offset = int(island.max()) + 1

        # Unity triangulates on import and its choice of diagonal is unknown, so quads are drawn with BOTH
        # diagonals: the union covers whatever triangulation the GPU ends up with. tri_alt marks the extra pair.
        tri = np.nonzero(lt == 3)[0]
        quad = np.nonzero(lt == 4)[0]
        loops = [np.stack([ls[tri], ls[tri] + 1, ls[tri] + 2], axis=1)]
        faces = [tri]
        alt = [np.zeros(len(tri), bool)]
        for corners, is_alt in (((0, 1, 2), False), ((0, 2, 3), False), ((0, 1, 3), True), ((1, 2, 3), True)):
            loops.append(np.stack([ls[quad] + c for c in corners], axis=1))
            faces.append(quad)
            alt.append(np.full(len(quad), is_alt))
        if np.any(lt > 4):
            me.calc_loop_triangles()
            nt = len(me.loop_triangles)
            tl = _get(me.loop_triangles, "loops", np.int64, nt, 3).reshape(-1, 3)
            tp = _get(me.loop_triangles, "polygon_index", np.int64, nt)
            keep = lt[tp] > 4
            loops.append(tl[keep])
            faces.append(tp[keep])
            alt.append(np.zeros(int(keep.sum()), bool))
            ngons += int(np.sum(lt > 4))
        loops, faces = np.concatenate(loops), np.concatenate(faces)

        tri_uv.append(uv[loops])
        tri_mat.append(face_mat[faces])
        tri_island.append(island[faces])
        tri_obj.append(np.full(len(faces), oi))
        tri_alt.append(np.concatenate(alt))
        objects.append(obj.name)

    names = sorted(materials, key=lambda n: materials[n]["id"])
    return dict(objects=objects, material_names=names, materials=materials,
                tri_uv=np.concatenate(tri_uv), tri_mat=np.concatenate(tri_mat), tri_island=np.concatenate(tri_island),
                tri_obj=np.concatenate(tri_obj), tri_alt=np.concatenate(tri_alt),
                islands=island_offset, ngons=ngons, uv_layers=sorted(uv_layers))


# ----------------------------------------------------------------------------
# Conservative rasteriser
# ----------------------------------------------------------------------------
def _raster_group(A, B, C, x0, y0, w, h):
    """Triangles that share one bounding-box size. Returns (triangle, texel x, texel y, squared distance from the
    texel centre to the triangle) for every texel square the triangle overlaps (exact triangle / box test)."""
    n = len(x0)
    cx = (x0[:, None] + np.arange(w)[None, :] + 0.5)[:, None, :]
    cy = (y0[:, None] + np.arange(h)[None, :] + 0.5)[:, :, None]
    area2 = (B[:, 0] - A[:, 0]) * (C[:, 1] - A[:, 1]) - (B[:, 1] - A[:, 1]) * (C[:, 0] - A[:, 0])
    flip = (area2 < 0)[:, None]
    B, C = np.where(flip, C, B), np.where(flip, B, C)      # counter-clockwise
    touch = np.ones((n, h, w), bool)
    inside = np.ones((n, h, w), bool)
    d2 = np.full((n, h, w), np.inf)
    for P, Q in ((A, B), (B, C), (C, A)):
        ex = (Q[:, 0] - P[:, 0])[:, None, None]
        ey = (Q[:, 1] - P[:, 1])[:, None, None]
        px, py = P[:, 0][:, None, None], P[:, 1][:, None, None]
        edge = ex * (cy - py) - ey * (cx - px)              # >= 0 on the inner side of this edge
        touch &= edge > EPS - 0.5 * (np.abs(ex) + np.abs(ey))  # the texel square reaches the inner side
        inside &= edge >= 0
        length2 = ex * ex + ey * ey
        t = np.clip(((cx - px) * ex + (cy - py) * ey) / np.where(length2 > 0, length2, 1.0), 0.0, 1.0)
        dx, dy = cx - (px + t * ex), cy - (py + t * ey)
        d2 = np.minimum(d2, dx * dx + dy * dy)
    d2 = np.where(inside, 0.0, d2)
    k, yy, xx = np.nonzero(touch)
    return k, x0[k] + xx, y0[k] + yy, d2[k, yy, xx]


def rasterise(asset, size):
    """Which triangles touch which texel. Returns candidate arrays (texel, triangle, squared distance)."""
    P = asset["tri_uv"] * size
    lo = np.clip(np.floor(P.min(axis=1)).astype(np.int64), 0, size - 1)
    hi = np.clip(np.ceil(P.max(axis=1)).astype(np.int64) - 1, 0, size - 1)
    hi = np.maximum(hi, lo)
    wh = hi - lo + 1
    key = wh[:, 0] * (size + 1) + wh[:, 1]
    order = np.argsort(key, kind="stable")
    bounds = np.r_[0, np.nonzero(np.diff(key[order]))[0] + 1, len(order)]

    c_tex, c_tri, c_d2 = [], [], []
    for s, e in zip(bounds[:-1], bounds[1:]):
        w, h = int(wh[order[s], 0]), int(wh[order[s], 1])
        step = max(1, MAX_GROUP_CELLS // (w * h))
        for g in range(s, e, step):
            idx = order[g:min(g + step, e)]
            k, x, y, d2 = _raster_group(P[idx, 0], P[idx, 1], P[idx, 2], lo[idx, 0], lo[idx, 1], w, h)
            c_tex.append(y * size + x)
            c_tri.append(idx[k])
            c_d2.append(d2)
    c_tex, c_tri, c_d2 = np.concatenate(c_tex), np.concatenate(c_tri), np.concatenate(c_d2)

    # A triangle with no area (or one that only grazes texel borders) still owns the texel under its centre.
    missed = np.setdiff1d(np.arange(len(P)), c_tri)
    if len(missed):
        centre = np.clip(np.floor(P[missed].mean(axis=1)).astype(np.int64), 0, size - 1)
        c_tex = np.r_[c_tex, centre[:, 1] * size + centre[:, 0]]
        c_tri = np.r_[c_tri, missed]
        c_d2 = np.r_[c_d2, np.full(len(missed), 0.25)]
    return c_tex, c_tri, c_d2


def _fill_empty(ids):
    """Give every empty texel (-1) a neighbouring island's material: a few texels of exact padding around each
    island, then a coarse fill along rows and columns. Padding is never sampled with Point filtering; it keeps the
    4x4 compression blocks at island edges down to the island's own colour."""
    ids = ids.copy()
    h, w = ids.shape
    for _ in range(DILATE_STEPS):
        if not (ids < 0).any():
            return ids
        src = ids.copy()
        for dy, dx in ((0, 1), (0, -1), (1, 0), (-1, 0), (1, 1), (1, -1), (-1, 1), (-1, -1)):
            shifted = np.full_like(src, -1)
            shifted[max(dy, 0):h + min(dy, 0), max(dx, 0):w + min(dx, 0)] = \
                src[max(-dy, 0):h + min(-dy, 0), max(-dx, 0):w + min(-dx, 0)]
            take = (ids < 0) & (shifted >= 0)
            ids[take] = shifted[take]
    for _ in range(2):                                      # rows, then columns
        for flip in (False, True):
            view = ids[:, ::-1] if flip else ids
            col = np.where(view >= 0, np.arange(w)[None, :], -1)
            np.maximum.accumulate(col, axis=1, out=col)
            filled = np.where(col >= 0, view[np.arange(h)[:, None], np.maximum(col, 0)], -1)
            view[...] = np.where(view >= 0, view, filled)
        ids = ids.T
        h, w = w, h
    return ids


def _mixed_texels(tex, value, island):
    """Texels touched by more than one value: (same island = material boundary, different islands = bleed)."""
    if not len(tex):
        return np.array([], np.int64), np.array([], np.int64)
    t = tex.astype(np.int64)
    pairs = np.unique(t * (int(value.max()) + 1) + value)
    texels, count = np.unique(pairs // (int(value.max()) + 1), return_counts=True)
    mixed = texels[count > 1]
    pairs = np.unique(t * (int(island.max()) + 1) + island)
    texels, count = np.unique(pairs // (int(island.max()) + 1), return_counts=True)
    multi_island = texels[count > 1]
    bleed = np.intersect1d(mixed, multi_island)
    return np.setdiff1d(mixed, bleed), bleed


def _value_ids(asset, overrides):
    """Per material: (albedo bytes, mask bytes), with a variant's overrides applied. Plus one id per distinct value."""
    values = []
    for name in asset["material_names"]:
        albedo, mask = material_values(asset["materials"][name]["entry"])
        o = (overrides or {}).get(name)
        if o:
            albedo = o["rgb"] or albedo
            if o["smoothness"] is not None:
                mask = mask[:3] + (unit_byte(o["smoothness"]),)
        values.append((albedo, mask))
    distinct = {v: i for i, v in enumerate(sorted(set(values)))}
    return values, np.array([distinct[v] for v in values], dtype=np.int64)


def bake(collection, size, material_entry, variants=None):
    """Returns dict(textures={variant or "": dict(albedo, mask)}, separate=[names], stats=dict, lines=[report])."""
    asset = read_asset(collection, material_entry)
    names = asset["material_names"]
    separate = np.array([asset["materials"][n]["separate"] for n in names])
    if separate.all():
        raise RuntimeError("every material is transparent or emissive - nothing to put in an atlas")

    c_tex, c_tri, c_d2 = rasterise(asset, size)
    tri_sep = separate[asset["tri_mat"]]

    # Owner of each texel: the triangle whose area contains the texel centre, else the nearest one.
    # Atlas faces always win over faces of separate materials (those never sample the atlas).
    rank = c_d2 + np.where(tri_sep[c_tri], 1e9, 0.0)
    order = np.lexsort((rank, c_tex))
    first = np.r_[True, c_tex[order][1:] != c_tex[order][:-1]]
    owner = np.full(size * size, -1, dtype=np.int64)
    owner[c_tex[order][first]] = asset["tri_mat"][c_tri[order][first]]
    covered = int((owner >= 0).sum())
    ids = _fill_empty(owner.reshape(size, size))

    in_atlas = ~tri_sep[c_tri]
    a_tex, a_tri = c_tex[in_atlas], c_tri[in_atlas]
    textures, variant_stats = {}, {}
    for variant, overrides in [("", None)] + sorted((variants or {}).items()):
        values, value_id = _value_ids(asset, overrides)
        boundary, bleed = _mixed_texels(a_tex, value_id[asset["tri_mat"][a_tri]], asset["tri_island"][a_tri])
        albedo = np.array([v[0] for v in values], dtype=np.uint8)[ids]
        mask = np.array([v[1] for v in values], dtype=np.uint8)[ids]
        textures[variant] = dict(albedo=albedo[::-1].copy(), mask=mask[::-1].copy())   # PNG rows run top to bottom

        # Independent check: read the finished texture the way the GPU does (the texel a UV falls in) at 7 points
        # inside every triangle of the atlas faces, and compare with the triangle's own material value.
        check = ~tri_sep & ~asset["tri_alt"]
        uv3 = asset["tri_uv"][check]
        want = value_id[asset["tri_mat"][check]]
        wrong = np.zeros(len(uv3), bool)
        got_id = value_id[ids]
        for bary in ((1 / 3, 1 / 3, 1 / 3), (0.8, 0.1, 0.1), (0.1, 0.8, 0.1), (0.1, 0.1, 0.8),
                     (0.45, 0.45, 0.1), (0.45, 0.1, 0.45), (0.1, 0.45, 0.45)):
            p = (uv3 * np.array(bary)[None, :, None]).sum(axis=1) * size
            xy = np.clip(np.floor(p).astype(np.int64), 0, size - 1)
            wrong |= got_id[xy[:, 1], xy[:, 0]] != want
        variant_stats[variant] = dict(boundary_texels=int(len(boundary)), bleed_texels=int(len(bleed)),
                                      bleed_sample=[(int(t % size), int(t // size)) for t in bleed[:5]],
                                      sampled_triangles=int(len(uv3)), wrong_triangles=int(wrong.sum()),
                                      distinct_values=int(len(np.unique(value_id[~separate]))))

    stats = dict(size=size, objects=len(asset["objects"]), triangles=int((~asset["tri_alt"]).sum()),
                 ngons=asset["ngons"], islands=asset["islands"], materials=len(names),
                 atlas_materials=int((~separate).sum()), separate=[n for n, s in zip(names, separate) if s],
                 covered_texels=covered, coverage=round(covered / (size * size), 4),
                 uv_min=[round(float(v), 5) for v in asset["tri_uv"].reshape(-1, 2).min(axis=0)],
                 uv_max=[round(float(v), 5) for v in asset["tri_uv"].reshape(-1, 2).max(axis=0)],
                 uv_layers=asset["uv_layers"], variants=variant_stats)

    lines = ["atlas %d x %d: %d materials -> 1 (+ %d separate%s), %d islands, %.1f%% of the texels used"
             % (size, size, len(names), len(stats["separate"]),
                ": " + ", ".join(stats["separate"]) if stats["separate"] else "", asset["islands"],
                100.0 * stats["coverage"])]
    for variant, s in variant_stats.items():
        lines.append("atlas %s: %d colour values, bleed texels %d, material-boundary texels %d, "
                     "wrong-colour triangles %d of %d"
                     % (variant or "base", s["distinct_values"], s["bleed_texels"], s["boundary_texels"],
                        s["wrong_triangles"], s["sampled_triangles"]))
    return dict(textures=textures, separate=stats["separate"], stats=stats, lines=lines,
                material_names=names, materials=asset["materials"])


# ----------------------------------------------------------------------------
# Output
# ----------------------------------------------------------------------------
def write_png(path, pixels):
    """8-bit RGB / RGBA PNG, written directly (no colour management, same bytes on every run)."""
    h, w, channels = pixels.shape
    raw = b"".join(b"\x00" + pixels[y].tobytes() for y in range(h))

    def chunk(tag, data):
        return struct.pack(">I", len(data)) + tag + data + struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF)

    header = struct.pack(">IIBBBBB", w, h, 8, {3: 2, 4: 6}[channels], 0, 0, 0)
    with open(path, "wb") as f:
        f.write(b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", header) + chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b""))


def write_textures(result, stem, folder):
    """Writes the Albedo + Mask of the base and of every variant. A variant whose Mask equals the base's reuses it.
    Returns {variant: (albedo texture name, mask texture name)}."""
    os.makedirs(folder, exist_ok=True)
    out = {}
    base = result["textures"][""]
    for variant, tex in result["textures"].items():
        vstem = stem if not variant else "%s_%s" % (stem, variant)
        albedo_name = texture_name(vstem, "Albedo")
        write_png(os.path.join(folder, albedo_name + ".png"), tex["albedo"])
        if variant and np.array_equal(tex["mask"], base["mask"]):
            mask_name = texture_name(stem, "Mask")
        else:
            mask_name = texture_name(vstem, "Mask")
            write_png(os.path.join(folder, mask_name + ".png"), tex["mask"])
        out[variant] = (albedo_name, mask_name)
    return out


def manifest_entries(result, stem, texture_names):
    """Manifest entries (see export_fbx.py): one atlas material per variant, then the separate flat materials."""
    entries = []
    for variant, (albedo_name, mask_name) in texture_names.items():
        vstem = stem if not variant else "%s_%s" % (stem, variant)
        entries.append(dict(name=atlas_material_name(vstem),
                            baseColor=dict(r=1.0, g=1.0, b=1.0, a=1.0),
                            metallic=0.0, roughness=0.0,     # the Mask supplies both: smoothness = Mask.a x 1
                            emissionColor=dict(r=0.0, g=0.0, b=0.0, a=1.0), emissionStrength=0.0,
                            textured=True, baseMap=albedo_name, maskMap=mask_name))
    entries += [result["materials"][name]["entry"] for name in result["separate"]]
    return entries


@contextlib.contextmanager
def collapsed(collection, result, stem):
    """For the duration of the export: every atlas face uses M_<Stem>_Atlas, separate materials keep a slot each.
    Slots and per-face indices are restored afterwards, so the scene is left exactly as it was."""
    name = atlas_material_name(stem)
    atlas = bpy.data.materials.get(name)
    created = atlas is None
    if created:
        atlas = bpy.data.materials.new(name)
    separate = set(result["separate"])
    saved = []
    try:
        for me in {o.data for o in visible_meshes(collection)}:
            old = list(me.materials)
            index = _get(me.polygons, "material_index", np.int32, len(me.polygons))
            saved.append((me, old, index))
            used = set(np.unique(np.minimum(index, len(old) - 1)).tolist())
            kept = [m for i, m in enumerate(old) if m.name in separate and i in used]
            has_atlas = any(old[i].name not in separate for i in used)
            new = ([atlas] if has_atlas else []) + kept
            remap = np.array([new.index(m) if m.name in separate and m in new else 0 for m in old], dtype=np.int32)
            me.materials.clear()
            for m in new:
                me.materials.append(m)
            me.polygons.foreach_set("material_index", remap[np.minimum(index, len(old) - 1)])
            me.update()
        yield name
    finally:
        for me, old, index in saved:
            me.materials.clear()
            for m in old:
                me.materials.append(m)
            me.polygons.foreach_set("material_index", index)
            me.update()
        if created:
            bpy.data.materials.remove(atlas)
