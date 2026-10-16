"""convert_asset.py for meshes exported by export_pack.py: the FBX carries no usable materials, so each material
slot's maps come from the per-mesh manifest (Manifests/<key>.json) instead of a Cosmos texture set.

  blender --background --factory-startup --python-exit-code 1 --python convert_ue.py -- ^
      --manifest C:\\Otherreach_External_Assets\\UE_Exports\\FantasyCastle\\Manifests\\SM_AccomsP12.json ^
      --asset-id bld_fab_fc_accoms_p12 --res 2048 --category building --preview --out <dir>

Every convert_asset.py option works; --source defaults to the manifest's FBX. Per slot it binds the manifest's primary
basecolor / normal / orm (or separate ao / roughness / metallic) / opacity. Unreal normal maps are DirectX (-Y) unless
the texture had "Flip Green Channel" set, so the green channel is flipped to OpenGL accordingly. Pack material effects
(moss/snow blend layers, tint and tiling scalars) are not reproduced; the manifest keeps them for reference.
"""
import json
import os
import re
import sys

import bpy

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import convert_asset as ca  # noqa: E402

argv = sys.argv[sys.argv.index("--") + 1:]
manifest_path = os.path.abspath(argv[argv.index("--manifest") + 1])
del sys.argv[sys.argv.index("--manifest"):sys.argv.index("--manifest") + 2]
root = os.path.dirname(os.path.dirname(manifest_path))
manifest = json.load(open(manifest_path, encoding="utf-8"))
if "--source" not in sys.argv:
    sys.argv += ["--source", os.path.join(root, manifest["fbx"])]
if "--licence" not in sys.argv:
    sys.argv += ["--licence", manifest["licence"]]
MATERIALS = {m["slot_material"].split(".")[-1]: m for m in manifest["materials"] if m}


def harvest_manifest(mat, sets, report):
    name = re.sub(r"\.\d{3}$", "", mat.name) if mat else ""
    # The FBX exporter suffixes _2, _3 ... when one material fills several slots.
    rec = MATERIALS.get(name) or MATERIALS.get(re.sub(r"_\d+$", "", name))
    if rec is None:
        raise SystemExit(f"FBX material '{name}' is not in the manifest ({sorted(MATERIALS)})")
    chans = rec["channels"]
    report.setdefault("texture_sets", {})[name] = {k: v["texture"] for k, v in chans.items()}

    def img(ch, colour):
        im = bpy.data.images.load(os.path.join(root, chans[ch]["file"]), check_existing=True)
        im.colorspace_settings.name = "sRGB" if colour else "Non-Color"
        return im

    ch = {"bsdf": None, "alpha": ("const", 1.0), "normal": None}
    if "basecolor" in chans:
        ch["base"] = ("image", img("basecolor", True), None, 1.0)
    else:
        # FantasyCastle leaves some slots on WorldGridMaterial; they come out flat grey rather than failing the mesh.
        print(f"UE_WARN material {name} has no base colour texture; flat grey")
        ch["base"] = ("const", (0.5, 0.5, 0.5, 1.0))
    if "opacity" in chans:
        ch["alpha"] = ("image", img("opacity", False), 0, 1.0)
    if "normal" in chans:
        ch["normal"] = ("image", img("normal", False), None, 1.0)
        ch["flip_green"] = chans["normal"]["convention"].startswith("DirectX")
    if "orm" in chans:
        # packing names the R, G, B contents: ORM, RMA (LostTemple, Mayan) or ORD (FantasyCastle, B = displacement).
        orm, packing = img("orm", False), chans["orm"]["packing"]
        ch.update(ao=("image", orm, packing.index("O"), 1.0) if "O" in packing else ("image", orm, packing.index("A"), 1.0),
                  rough=("image", orm, packing.index("R"), 1.0),
                  metal=("image", orm, packing.index("M"), 1.0) if "M" in packing else ("const", 0.0))
    else:
        ch["ao"] = ("image", img("ao", False), 0, 1.0) if "ao" in chans else ("const", 1.0)
        ch["rough"] = ("image", img("roughness", False), 0, 1.0) if "roughness" in chans else ("const", 0.8)
        ch["metal"] = ("image", img("metallic", False), 0, 1.0) if "metallic" in chans else ("const", 0.0)
    return ch


def reshade_regrounded(obj, a):
    """convert_asset decimates LOD0 for --max-tris after normalise(), which can lift the base a few mm off Y = 0
    (validate_godot fails it). LOD0 is the only object reshaded before it is renamed to the asset id."""
    if not obj.name.startswith(a.asset_id):
        co = ca.coords(obj)
        lo, hi = co.min(0), co.max(0)
        obj.data.transform(ca.Matrix.Translation(ca.Vector((-(lo[0] + hi[0]) / 2, -(lo[1] + hi[1]) / 2, -lo[2]))))
    _reshade(obj, a)


ca.texture_sets = lambda folder: dict(MATERIALS)
ca.harvest_cosmos = harvest_manifest
_reshade, ca.reshade = ca.reshade, reshade_regrounded

try:
    ca.main()
    a = ca.parse_args()
    meta_path = os.path.join(a.out, a.asset_id, f"{a.asset_id}_meta.json")
    meta = json.load(open(meta_path, encoding="utf-8"))
    meta["pack"] = f"{manifest['pack']} ({'Cosmos Premium' if 'Cosmos' in manifest['licence'] else 'Fab'}, Unreal export)"
    meta["ue_manifest"] = manifest_path
    meta["ue_mesh"] = manifest["mesh"]
    meta["built_by"] = meta["built_by"].replace("convert_asset.py", "ue_export/convert_ue.py + convert_asset.py")
    used = {v["file"] for m in MATERIALS.values() for v in m["channels"].values()}
    meta["source_files"] += [{"path": f, "sha256": ca.sha256(os.path.join(root, f)), "bytes": os.path.getsize(os.path.join(root, f))}
                             for f in sorted(used) if os.path.exists(os.path.join(root, f))]
    with open(meta_path, "w", encoding="utf-8") as fh:
        json.dump(meta, fh, indent=1, ensure_ascii=False)
except SystemExit as e:
    if e.code not in (0, None):
        print(f"CONVERT_ERROR {e.code}")
    sys.stdout.flush()
    os._exit(1 if e.code not in (0, None) else 0)
sys.stdout.flush()
os._exit(0)
