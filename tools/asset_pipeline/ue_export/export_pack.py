"""Export every StaticMesh / SkeletalMesh under /Game/<Pack> to FBX, the textures their materials read to PNG at
source resolution, and one JSON manifest per mesh. Unreal Editor Python (UE 5.8). See README.txt beside this file.

  UnrealEditor-Cmd.exe <uproject> -run=pythonscript -script="export_pack.py --pack FantasyCastle --out <dir>" ^
      -unattended -nop4 -nosplash -NullRHI -stdout

Writes <out>/
  Meshes/<SM_name>.fbx           LOD0 only, no collision. Nanite meshes: the source (full-detail) mesh, not the fallback.
  Textures/<T_name>.png          the texture's source art (mip 0 of the imported file), not the cooked platform size
  Manifests/<SM_name>.json       mesh path, materials, channel -> texture file, bounds (cm), triangles, LODs, licence
  _pack.json                     every manifest in one list + the texture table + timings

--textures-only (VFX packs, whose flipbooks are read by particle materials, not meshes): every Texture2D under the
pack (--match on the texture name) and, in _pack.json, every material's blend mode, parameters and textures.
"""
import argparse
import json
import os
import re
import shlex
import sys
import time

import unreal

LICENCE = {
    "fab": "UNVERIFIED - Fab download. Check the listing's licence before use outside Unreal: 'Standard License' allows "
           "any engine; listings marked 'UE-Only Content' may only ship in Unreal Engine products and cannot be used "
           "in the Godot build.",
    "cosmos": "UNVERIFIED - Cosmos Premium purchased Unreal environment; the zip ships no licence text. Fill from the "
              "Cosmos terms before promotion.",
}

# Texture-name suffix -> channel. Measured on FantasyCastle / LostTemple / Ancient_Mayan_Ruins (see README.txt).
SUFFIX = [
    (r"_(BC|B|D|BaseColor|Base_Color|Albedo|Diffuse|Diff|Color|Col|A)(_H)?$", "basecolor"),
    (r"_(N|Nor|Normal|NormalMap|NRM|Norm)$", "normal"),
    (r"_(ORM|ORMH|ARM|RMA|RMAH|MRA|ORDp|ORD|OcclusionRoughnessMetallic)$", "orm"),
    (r"_(AO|Occlusion)$", "ao"),
    (r"_(R|Rough|Roughness)$", "roughness"),
    (r"_(Metal|Metallic|MT)$", "metallic"),
    (r"_(H|Height|Disp|Displacement)$", "height"),
    (r"_(E|Emissive|Emission)$", "emissive"),
    (r"_(Opacity|Alpha|OP|O|O_\d)$", "opacity"),
    (r"_(M|Mask|Masks|MSK)$", "mask"),
    (r"_(Translucency|SSS|Transmission)$", "other"),
]
# Channel order of a packed map, by suffix. Measured: FantasyCastle/Mayan _ORM has B ~ 0 (metal), LostTemple/Mayan
# _RMA/_RMAH has R high (rough), G ~ 0 (metal), B high (AO); FantasyCastle _ORDp is occlusion/roughness/displacement.
# Mayan _BC_H is base colour with height in alpha. VictorianGrandMansion _MRA: R ~ 0 (metal), G rough, B AO.
PACKING = [(r"_RMAH?$", "RMA"), (r"_MRA$", "MRA"), (r"_(ORDp|ORD)$", "ORD"), (r"", "ORM")]
PARAM = [
    (r"normal|_n$|^n_|nrm", "normal"),
    (r"orm|arm|occlusionroughness", "orm"),
    (r"base.?colou?r|albedo|diffuse|^colou?r|^texture$|^texture_r$|^bc|^tex$", "basecolor"),
    (r"rough", "roughness"),
    (r"metal", "metallic"),
    (r"\bao\b|occlusion", "ao"),
    (r"opacity|alpha", "opacity"),
    (r"emissi", "emissive"),
    (r"height|disp", "height"),
    (r"mask", "mask"),
]
SECONDARY = re.compile(r"moss|detail|blend|snow|dirt|puddle|grunge|macro|second|layer ?2|_b$|top|wet|leak|noise|"
                       r"cloud|ground|floor|sand|grass|vertex", re.I)


def classify(param, tex):
    name = tex.get_name()
    if tex.get_editor_property("compression_settings") == unreal.TextureCompressionSettings.TC_NORMALMAP:
        return "normal"
    for rx, ch in SUFFIX:
        if re.search(rx, name, re.I):
            return ch
    for rx, ch in PARAM:
        if param and re.search(rx, param, re.I):
            return ch
    return "basecolor" if tex.get_editor_property("srgb") else "other"


def stem(name):
    for rx, _ in SUFFIX:
        name = re.sub(rx, "", name, flags=re.I)
    return re.sub(r"^T_", "", name, flags=re.I).lower()


def export_task(obj, path, exporter, options=None):
    task = unreal.AssetExportTask()
    task.set_editor_property("object", obj)
    task.set_editor_property("filename", path)
    task.set_editor_property("automated", True)
    task.set_editor_property("prompt", False)
    task.set_editor_property("replace_identical", True)
    task.set_editor_property("exporter", exporter)
    if options is not None:
        task.set_editor_property("options", options)
    return unreal.Exporter.run_asset_export_task(task) and os.path.exists(path)


EXPORTER = {"G8": (".png", unreal.TextureExporterPNG), "BGRA8": (".png", unreal.TextureExporterPNG),
            "G16": (".png", unreal.TextureExporterPNG), "RGBA16": (".png", unreal.TextureExporterPNG),
            "RGBA16F": (".exr", unreal.TextureExporterEXR), "RGBA32F": (".exr", unreal.TextureExporterEXR),
            "R16F": (".exr", unreal.TextureExporterEXR), "R32F": (".exr", unreal.TextureExporterEXR),
            "BGRE8": (".hdr", unreal.TextureExporterHDR)}


class Textures:
    """Each texture is exported once per pack; manifests point at the shared file."""

    def __init__(self, folder, match=""):
        self.folder = folder
        self.match = match
        self.table = {}

    def get(self, tex):
        key = tex.get_path_name()
        if key in self.table:
            return self.table[key]
        rec = {"asset": key, "srgb": tex.get_editor_property("srgb"),
               "compression": str(tex.get_editor_property("compression_settings")).split(".")[-1].split(":")[0],
               "flip_green_channel": tex.get_editor_property("flip_green_channel"),
               "cooked_px": [tex.blueprint_get_size_x(), tex.blueprint_get_size_y()],
               "virtual_texture": tex.get_editor_property("virtual_texture_streaming")}
        info = unreal.EditorAssetLibrary.find_asset_data(key)
        fmt = str(info.get_tag_value("SourceFormat")).replace("TSF ", "")
        rec["source_format"] = fmt
        rec["source_px"] = [int(v) for v in str(info.get_tag_value("Dimensions")).split("x")[:2]]
        # The PNG/TGA exporters assert (and kill the commandlet) on a source format they cannot write: route by format.
        ext, exporter = EXPORTER.get(fmt, (None, None))
        path = os.path.join(self.folder, tex.get_name() + (ext or ""))
        if not re.search(self.match, tex.get_name(), re.I):
            rec["file"] = None
        elif ext and (os.path.exists(path) or export_task(tex, path, exporter())):
            rec["file"] = os.path.relpath(path, os.path.dirname(self.folder)).replace("\\", "/")
        else:
            rec["file"] = None
            unreal.log_warning(f"export_pack: no export for texture {key} (source format {fmt})")
        self.table[key] = rec
        return rec


def material_record(mi, textures):
    """Every texture the material reads, by channel, with the primary layer picked; scalar/vector params for reference."""
    rec = {"slot_material": mi.get_path_name(), "class": type(mi).__name__}
    base = mi.get_base_material()
    rec["base_material"] = base.get_path_name() if base else None
    mel = unreal.MaterialEditingLibrary
    seen, entries = set(), []
    if isinstance(mi, unreal.MaterialInstance):
        for name in mel.get_texture_parameter_names(base):
            tex = mel.get_material_instance_texture_parameter_value(mi, name)
            if tex and isinstance(tex, unreal.Texture2D):
                entries.append((str(name), tex))
                seen.add(tex.get_path_name())
        rec["scalars"] = {str(n): round(mel.get_material_instance_scalar_parameter_value(mi, n), 5)
                          for n in mel.get_scalar_parameter_names(base)}
        rec["vectors"] = {}
        for n in mel.get_vector_parameter_names(base):
            c = mel.get_material_instance_vector_parameter_value(mi, n)
            rec["vectors"][str(n)] = [round(c.r, 4), round(c.g, 4), round(c.b, 4), round(c.a, 4)]
    elif base:
        rec["scalars"] = {str(n): round(mel.get_material_default_scalar_parameter_value(base, n), 5)
                          for n in mel.get_scalar_parameter_names(base)}
    used = list(mel.get_material_used_textures(mi))
    if not entries and not used:
        # Plain Materials with hard-wired Texture Samples (VictorianGrandMansion) report no used textures under -NullRHI
        # (nothing is compiled): take the Texture2D packages the material depends on instead.
        deps = unreal.AssetRegistryHelpers.get_asset_registry().get_dependencies(
            mi.get_outermost().get_name(), unreal.AssetRegistryDependencyOptions())
        used = [unreal.EditorAssetLibrary.load_asset(str(d)) for d in deps or [] if str(d).startswith("/Game/")]
    for tex in used:
        if isinstance(tex, unreal.Texture2D) and tex.get_path_name() not in seen and not tex.get_path_name().startswith("/Engine/"):
            entries.append((None, tex))
            seen.add(tex.get_path_name())
    rec["textures"] = []
    for param, tex in entries:
        t = textures.get(tex)
        rec["textures"].append({"param": param, "texture": tex.get_name(), "channel": classify(param, tex),
                                "file": t["file"], "source_px": t["source_px"],
                                "normal_convention": ("OpenGL (+Y)" if t["flip_green_channel"] else "DirectX (-Y)")
                                if classify(param, tex) == "normal" else None})
    rec["blend_mode"] = str(base.get_editor_property("blend_mode")).split(".")[-1].split(":")[0] if base else None
    rec["shading_model"] = str(base.get_editor_property("shading_model")).split(".")[-1].split(":")[0] if base else None
    rec["channels"] = pick_primary(rec["textures"], rec["blend_mode"])
    return rec


def pick_primary(texs, blend_mode):
    """One file per channel. Layered materials (moss/detail/snow blends) keep their extra layers in 'textures' only.
    ORM prefers the base colour's name; normal prefers the ORM's name, then the base colour's (Frozen Norse kits pair
    a unique baked normal with a unique ORM over tiling detail colours). Opacity only counts on masked materials."""
    by = {}
    for t in texs:
        by.setdefault(t["channel"], []).append(t)
    if blend_mode not in ("MASKED", "TRANSLUCENT"):
        by.pop("opacity", None)
    anchor = {stem(t["texture"]) for ch in ("normal", "orm") for t in by.get(ch, [])}

    def score(t, prefer):
        st = stem(t["texture"])
        return (prefer.index(st) if st in prefer else len(prefer), st not in anchor,
                bool(SECONDARY.search((t["param"] or "") + " " + t["texture"])), t["param"] is None)

    out = {}
    for ch in ("basecolor", "orm", "normal", "ao", "roughness", "metallic", "opacity", "emissive", "height", "mask"):
        if not by.get(ch):
            continue
        prefer = [stem(out[c]["texture"]) for c in ("orm", "basecolor") if c in out]
        best = min(by[ch], key=lambda t: score(t, prefer))
        out[ch] = {"file": best["file"], "texture": best["texture"]}
        if ch == "normal":
            out[ch]["convention"] = best["normal_convention"]
        if ch == "orm":
            out[ch]["packing"] = next(k for rx, k in PACKING if re.search(rx, best["texture"], re.I))
    return out


def mesh_manifest(mesh, fbx, textures, pack, source_kind):
    skeletal = isinstance(mesh, unreal.SkeletalMesh)
    m = {"pack": pack, "mesh": mesh.get_path_name(), "class": type(mesh).__name__,
         "fbx": os.path.relpath(fbx, os.path.dirname(os.path.dirname(fbx))).replace("\\", "/"),
         "units": "centimetres (Unreal), Z up", "licence": LICENCE[source_kind]}
    if skeletal:
        box = mesh.get_bounds().box_extent
        origin = mesh.get_bounds().origin
        lo = [origin.x - box.x, origin.y - box.y, origin.z - box.z]
        hi = [origin.x + box.x, origin.y + box.y, origin.z + box.z]
        m["lod_count"] = len(mesh.get_editor_property("lod_info"))
        mats = [s.material_interface for s in mesh.get_editor_property("materials")]
        m["slots"] = [str(s.material_slot_name) for s in mesh.get_editor_property("materials")]
        m["triangles_lod0"] = None
    else:
        bb = mesh.get_bounding_box()
        lo, hi = [bb.min.x, bb.min.y, bb.min.z], [bb.max.x, bb.max.y, bb.max.z]
        m["lod_count"] = mesh.get_num_lods()
        nanite = mesh.get_editor_property("nanite_settings")
        m["nanite"] = nanite.enabled
        m["triangles_render_lod0"] = mesh.get_num_triangles(0)
        desc = mesh.get_static_mesh_description(0)
        m["triangles_lod0"] = desc.get_triangle_count() if desc else m["triangles_render_lod0"]
        if nanite.enabled:
            m["nanite_fallback_percent"] = round(nanite.fallback_percent_triangles, 4)
        statics = mesh.get_editor_property("static_materials")
        mats = [s.material_interface for s in statics]
        m["slots"] = [str(s.material_slot_name) for s in statics]
    m["bounds_cm"] = {"min": [round(v, 3) for v in lo], "max": [round(v, 3) for v in hi],
                      "size": [round(hi[i] - lo[i], 3) for i in range(3)]}
    m["materials"] = [material_record(mi, textures) if mi else None for mi in mats]
    return m


def export_textures(a, ar, t0):
    """--textures-only: every Texture2D under the pack (filtered by --match) and every material's record. Textures that
    a material reads but --match excludes stay listed with file None."""
    assets = sorted(ar.get_assets_by_path("/Game/" + a.pack, recursive=True), key=lambda d: str(d.package_name))
    textures = Textures(os.path.join(a.out, "Textures"), a.match)
    for d in assets:
        if str(d.asset_class_path.asset_name) == "Texture2D":
            textures.get(d.get_asset())
    materials = [material_record(d.get_asset(), textures) for d in assets
                 if str(d.asset_class_path.asset_name) in ("Material", "MaterialInstanceConstant")]
    exported = sum(1 for t in textures.table.values() if t["file"])
    with open(os.path.join(a.out, "_pack.json"), "w", encoding="utf-8") as fh:
        json.dump({"pack": a.pack, "source": a.source, "licence": LICENCE[a.source], "textures_only": True,
                   "seconds": round(time.time() - t0, 1), "engine": unreal.SystemLibrary.get_engine_version(),
                   "textures": textures.table, "materials": materials}, fh, indent=1)
    unreal.log(f"export_pack: done {exported} of {len(textures.table)} textures, {len(materials)} materials, "
               f"{time.time() - t0:.0f} s")


def export_animations(a, ar, t0):
    """--animations: every AnimSequence under the pack (filtered by --match) -> Animations/<key>.fbx (the clip on its
    skeleton, no preview mesh) + Manifests/<key>.json (skeleton, frames, rate, root motion flags)."""
    for sub in ("Animations", "Manifests"):
        os.makedirs(os.path.join(a.out, sub), exist_ok=True)
    found = sorted((d for d in ar.get_assets_by_path("/Game/" + a.pack, recursive=True)
                    if str(d.asset_class_path.asset_name) == "AnimSequence" and re.search(a.match, str(d.asset_name), re.I)),
                   key=lambda d: str(d.package_name))
    unreal.log(f"export_pack: {len(found)} animations in /Game/{a.pack}")
    opt = unreal.FbxExportOption()
    opt.set_editor_property("fbx_export_compatibility", unreal.FbxExportCompatibility.FBX_2020)
    opt.set_editor_property("export_preview_mesh", False)
    opt.set_editor_property("export_morph_targets", False)
    opt.set_editor_property("export_local_time", True)
    manifests, failed = [], []
    for i, d in enumerate(found):
        rel = str(d.package_path)[len("/Game/" + a.pack):].strip("/").replace("/", "__")
        key = f"{rel}__{d.asset_name}" if rel else str(d.asset_name)
        fbx = os.path.join(a.out, "Animations", key + ".fbx")
        anim = d.get_asset()
        t = time.time()
        if not (a.skip_existing and os.path.exists(fbx)) and not export_task(anim, fbx, unreal.AnimSequenceExporterFBX(), opt):
            failed.append(anim.get_path_name())
            unreal.log_warning(f"export_pack: FBX export failed for {anim.get_path_name()}")
            continue
        skeleton = anim.get_editor_property("skeleton")
        length = unreal.AnimationLibrary.get_sequence_length(anim)
        frames = unreal.AnimationLibrary.get_num_frames(anim)
        m = {"pack": a.pack, "animation": anim.get_path_name(), "class": "AnimSequence", "key": key,
             "fbx": os.path.relpath(fbx, a.out).replace("\\", "/"), "units": "centimetres (Unreal), Z up",
             "licence": LICENCE[a.source], "skeleton": skeleton.get_path_name() if skeleton else None,
             "play_length_s": round(length, 5), "frames": frames,
             "frame_rate_hz": round(frames / length, 3) if length else None,
             "enable_root_motion": anim.get_editor_property("enable_root_motion"),
             "force_root_lock": anim.get_editor_property("force_root_lock"),
             "fbx_bytes": os.path.getsize(fbx), "export_seconds": round(time.time() - t, 2)}
        with open(os.path.join(a.out, "Manifests", key + ".json"), "w", encoding="utf-8") as fh:
            json.dump(m, fh, indent=1)
        manifests.append(m)
        unreal.log(f"export_pack: [{i + 1}/{len(found)}] {key} {m['frames']} frames {m['export_seconds']} s")
    with open(os.path.join(a.out, "_pack.json"), "w", encoding="utf-8") as fh:
        json.dump({"pack": a.pack, "source": a.source, "licence": LICENCE[a.source], "animations": len(manifests),
                   "failed": failed, "seconds": round(time.time() - t0, 1),
                   "engine": unreal.SystemLibrary.get_engine_version(), "manifests": manifests}, fh, indent=1)
    unreal.log(f"export_pack: done {len(manifests)} animations, {len(failed)} failed, {time.time() - t0:.0f} s")


def main():
    argv = sys.argv[1:]
    if not argv and os.environ.get("UE_EXPORT_ARGS"):
        argv = shlex.split(os.environ["UE_EXPORT_ARGS"], posix=False)
    p = argparse.ArgumentParser()
    p.add_argument("--pack", required=True, help="folder under /Game (Content/<pack>)")
    p.add_argument("--out", required=True)
    p.add_argument("--source", choices=("fab", "cosmos"), default="fab")
    p.add_argument("--match", default="", help="regex on the asset name; empty = every mesh")
    p.add_argument("--skip-existing", action="store_true", help="reuse FBX already on disk; manifests are always rewritten")
    p.add_argument("--textures-only", action="store_true", help="every Texture2D and material record, no meshes")
    p.add_argument("--animations", action="store_true", help="every AnimSequence as FBX on its skeleton, no meshes")
    a = p.parse_args([x.strip('"') for x in argv])
    t0 = time.time()
    ar = unreal.AssetRegistryHelpers.get_asset_registry()
    ar.scan_paths_synchronous(["/Game/" + a.pack], True)
    if a.animations:
        export_animations(a, ar, t0)
        return
    for sub in ("Textures",) if a.textures_only else ("Meshes", "Textures", "Manifests"):
        os.makedirs(os.path.join(a.out, sub), exist_ok=True)
    if a.textures_only:
        export_textures(a, ar, t0)
        return
    found = [d for d in ar.get_assets_by_path("/Game/" + a.pack, recursive=True)
             if str(d.asset_class_path.asset_name) in ("StaticMesh", "SkeletalMesh")
             and re.search(a.match, str(d.asset_name), re.I)]
    found.sort(key=lambda d: str(d.package_name))
    unreal.log(f"export_pack: {len(found)} meshes in /Game/{a.pack}")
    textures = Textures(os.path.join(a.out, "Textures"))
    fbx_opt = unreal.FbxExportOption()
    fbx_opt.set_editor_property("level_of_detail", False)
    fbx_opt.set_editor_property("collision", False)
    fbx_opt.set_editor_property("export_source_mesh", True)
    fbx_opt.set_editor_property("vertex_color", True)
    fbx_opt.set_editor_property("fbx_export_compatibility", unreal.FbxExportCompatibility.FBX_2020)
    manifests, failed = [], []
    for i, d in enumerate(found):
        name = str(d.asset_name)
        # Unique per pack even when two folders hold the same asset name.
        rel = str(d.package_path)[len("/Game/" + a.pack):].strip("/").replace("/", "__")
        key = f"{rel}__{name}" if rel and rel not in ("Mesh", "Meshs", "Meshes") else name
        fbx = os.path.join(a.out, "Meshes", key + ".fbx")
        man = os.path.join(a.out, "Manifests", key + ".json")
        mesh = d.get_asset()
        exporter = unreal.SkeletalMeshExporterFBX() if isinstance(mesh, unreal.SkeletalMesh) else unreal.StaticMeshExporterFBX()
        t = time.time()
        if not (a.skip_existing and os.path.exists(fbx)) and not export_task(mesh, fbx, exporter, fbx_opt):
            failed.append(mesh.get_path_name())
            unreal.log_warning(f"export_pack: FBX export failed for {mesh.get_path_name()}")
            continue
        m = mesh_manifest(mesh, fbx, textures, a.pack, a.source)
        m["key"] = key
        m["fbx_bytes"] = os.path.getsize(fbx)
        m["export_seconds"] = round(time.time() - t, 2)
        with open(man, "w", encoding="utf-8") as fh:
            json.dump(m, fh, indent=1)
        manifests.append(m)
        unreal.log(f"export_pack: [{i + 1}/{len(found)}] {key} {m.get('triangles_lod0')} tris {m['export_seconds']} s")
    with open(os.path.join(a.out, "_pack.json"), "w", encoding="utf-8") as fh:
        json.dump({"pack": a.pack, "source": a.source, "licence": LICENCE[a.source], "meshes": len(manifests),
                   "failed": failed, "seconds": round(time.time() - t0, 1),
                   "engine": unreal.SystemLibrary.get_engine_version(), "textures": textures.table,
                   "manifests": manifests}, fh, indent=1)
    unreal.log(f"export_pack: done {len(manifests)} meshes, {len(textures.table)} textures, {len(failed)} failed, "
               f"{time.time() - t0:.0f} s")


main()
