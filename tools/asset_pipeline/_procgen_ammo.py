"""projectile_ammo archetype template (docs/WAVE_0_MODULAR_ASSET_STANDARD.md section 17; assets/manifests/
asset_production.json archetype "projectile_ammo"): arrows, crossbow bolts, darts and javelins from one shaft +
point + fletching + nock template plus per-asset parameters. First instance: prop_arrow_rough, replacing the
greybox shaft/cone/three-box arrow ProjectilesView.cs drew in code (src/Presentation/Greybox/ProjectilesView.cs).

Geometry: a lathed wooden shaft (procgen_lib.primitives.lathe, a plain cylinder - no taper, since the head and
nock caps both ends), a lathed iron point at the tip (base radius -> a wider bulge -> a near-zero tip, which reads
as a bodkin/broadhead silhouette; lathe is a solid of revolution, so it is round in section rather than a real
broadhead's flat wide blades, an accepted simplification at this prop's screen size) and a small lathed wood
collar at the tail standing in for the nock. The three fletchings are thin solids (the task's explicitly allowed
alternative to an alpha-cut feather card): flat chamfered bars (procgen_lib.primitives.member) standing radially
out from the shaft at 120 degrees apart, centred on the shaft's centreline exactly as the greybox boxes were, so
the hidden inner half sits inside the solid wood shaft rather than needing extra clearance geometry.

Build-space axis convention (Blender Z-up, chosen for this template rather than the "front -Y, stands on the
ground" convention most archetypes use, because an arrow is carried and flown, not stood on a floor): the shaft
runs along Blender Y, point end at +Y, nock end at -Y. export.gltf_vec maps Blender (x, y, z) -> glTF (x, z, -y),
so the point at Blender +Y lands at glTF/Godot -Z and the nock at +Z - which is exactly ProjectilesView.cs's
convention (PointAhead is subtracted along -Z of flight; the greybox head sat at local Z -0.41, the fletches at
+0.3). Godot's Model() loader (Art/ArtLibrary.cs) instances the glTF as exported with no extra centring, so this
mapping is what places the model correctly with no per-instance rotation beyond the flight-facing Face() already
applies in ProjectilesView.cs.

    blender --background --factory-startup --python tools/asset_pipeline/_procgen_ammo.py -- \
        --asset prop_arrow_rough [--review --review-size 1536] [--tokens N --wall-minutes M]
"""
import copy
import json
import math
import os
import sys

import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from procgen_lib import primitives as pr  # noqa: E402
from procgen_lib import export  # noqa: E402
from procgen_lib.materials import Material  # noqa: E402
from procgen_lib.runner import run_template  # noqa: E402

X, Y, Z = np.eye(3)
TAU = 2.0 * math.pi
WOOD, IRON, FLETCH = 0, 1, 2
DEFAULT_ASSET = "prop_arrow_rough"

PARAMS = {
    "seed": 2609,
    "shaft": {
        "length_m": 0.66, "radius_m": 0.0055,
        "species": "ash", "age": 0.2, "finish": "raw",
    },
    "head": {
        # base overlaps the shaft's +Y end by overlap_m, so the two parts weld with no visible gap or seam ring.
        "length_m": 0.085, "overlap_m": 0.02, "base_r_m": 0.0105, "bulge_r_m": 0.0125, "bulge_t": 0.28,
        "tip_r_m": 0.0004, "iron_kind": "forged", "rust": 0.22,
    },
    "fletch": {
        "count": 3, "length_m": 0.13, "start_from_tail_m": 0.02, "height_m": 0.05, "thickness_m": 0.0018,
        "cs_m": 0.0003, "tan": 0.15, "wear": 0.3,
    },
    "nock": {
        "present": True, "length_m": 0.018, "bulge_r_frac": 1.3, "tip_r_frac": 0.55, "bulge_t": 0.35,
    },
}


def deep_merge(base, override):
    """Recursive dict override (see _procgen_soft_goods.py's deep_merge: same shape, this archetype has no
    material-spec dicts to replace wholesale, so a plain recursive merge is enough)."""
    out = copy.deepcopy(base)
    for k, v in override.items():
        if isinstance(v, dict) and isinstance(out.get(k), dict):
            out[k] = deep_merge(out[k], v)
        else:
            out[k] = copy.deepcopy(v)
    return out


def shaft_tip_y(P):
    return P["shaft"]["length_m"] / 2.0


def shaft_tail_y(P):
    return -P["shaft"]["length_m"] / 2.0


def build_shaft(b, P, rng):
    SP = P["shaft"]
    r = SP["radius_m"]
    y0 = shaft_tail_y(P)
    pr.lathe(b, "shaft", (0.0, y0, 0.0), Y, [(0.0, r), (SP["length_m"], r)], n=10, group="shaft", slot=WOOD,
             texel=1.0)


def build_head(b, P, rng):
    HP = P["head"]
    y0 = shaft_tip_y(P) - HP["overlap_m"]
    length = HP["length_m"]
    bulge_u = length * HP["bulge_t"]
    profile = [(0.0, HP["base_r_m"]), (bulge_u, HP["bulge_r_m"]), (length, HP["tip_r_m"])]
    pr.lathe(b, "head", (0.0, y0, 0.0), Y, profile, n=8, group="head", slot=IRON, texel=2.0)


def build_nock(b, P, rng):
    NP = P["nock"]
    if not NP.get("present", True):
        return
    r = P["shaft"]["radius_m"]
    y0 = shaft_tail_y(P)
    length = NP["length_m"]
    bulge_u = length * NP["bulge_t"]
    profile = [(0.0, r), (bulge_u, r * NP["bulge_r_frac"]), (length, max(r * NP["tip_r_frac"], 0.0008))]
    pr.lathe(b, "nock", (0.0, y0, 0.0), -Y, profile, n=10, group="nock", slot=WOOD, texel=1.0)


def build_fletch(b, P, rng):
    FP = P["fletch"]
    y_a = shaft_tail_y(P) + FP["start_from_tail_m"]
    y_b = y_a + FP["length_m"]
    for k in range(FP["count"]):
        theta = TAU * k / FP["count"]
        radial = np.array([math.cos(theta), 0.0, math.sin(theta)])
        p0 = np.array([0.0, y_a, 0.0])
        p1 = np.array([0.0, y_b, 0.0])
        pr.member(b, f"fletch_{k}", p0, p1, radial, FP["height_m"], FP["thickness_m"], cs=FP["cs_m"], ce=FP["cs_m"],
                  group="fletch", slot=FLETCH, grain=False, texel=2.0)


def build(b, P, rng):
    build_shaft(b, P, rng)
    build_head(b, P, rng)
    build_nock(b, P, rng)
    build_fletch(b, P, rng)
    SP, HP, FP = P["shaft"], P["head"], P["fletch"]
    return {
        "materials": [
            Material("wood", species=SP["species"], age=SP["age"], finish=SP["finish"]),
            Material("iron", kind=HP["iron_kind"], rust=HP["rust"]),
            Material("leather", tan=FP["tan"], wear=FP["wear"]),
        ],
        "notes": [
            "projectile_ammo archetype: a lathed wood shaft, a lathed iron point (round in section - a "
            "bodkin/broadhead silhouette rather than a real broadhead's flat blades) and a lathed wood nock "
            "collar, with three thin-solid fletchings (procgen_lib.primitives.member) standing radially out at "
            "120 degrees, centred on the shaft centreline as the greybox boxes were.",
            "axis convention: shaft along Blender Y, point at +Y / nock at -Y, so glTF export (x, z, -y) puts the "
            "point at -Z and the nock at +Z, matching ProjectilesView.cs's flight-forward -Z / fletching +Z "
            "layout with no per-instance correction.",
        ],
        "frame": "glTF +Y up, point -Z / nock +Z (build point +Y / nock -Y), centred on the shaft centreline",
    }


if __name__ == "__main__":
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    asset_id, rest, it = DEFAULT_ASSET, [], iter(argv)
    for tok in it:
        if tok == "--asset":
            asset_id = next(it)
        else:
            rest.append(tok)
    inst_path = os.path.join(export.REPO, "assets", "manifests", "procgen", f"{asset_id}.json")
    P = copy.deepcopy(PARAMS)
    instance_files = None
    if os.path.exists(inst_path):
        with open(inst_path, encoding="utf-8") as handle:
            inst = json.load(handle)
        P = deep_merge(P, inst.get("parameters", {}))
        concept = inst.get("concept", f"assets/concepts/{asset_id}.png")
        instance_files = [inst_path]
    else:
        concept = f"assets/concepts/{asset_id}.png"
    run_template(build, P, asset_id, archetype="projectile_ammo", concept=concept, tri_budget=4000, argv=rest,
                 instance_files=instance_files)
