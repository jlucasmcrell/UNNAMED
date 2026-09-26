"""prop_archetype_kit driver (docs/WAVE_0_MODULAR_ASSET_STANDARD.md section 17; assets/manifests/
asset_production.json archetype "prop_archetype_kit"; Phase B, B8): one dispatcher over procgen_lib/archetypes.py's
shared builders (timber_frame, plank_box, wheel_axle, bench_table, posts_and_rails, pipe_conduit, tech_housing, ...),
proving the archetype layer's reuse with two instances each of seven forms. build() itself never names an
archetype: it looks up P["fn"] on the archetypes module and calls it, so every family's geometry lives in the
shared library, not here -- a template_variant instance changes only its own parameter file, never this script or
archetypes.py (the reuse gate's cost rule, section 17).

Per-asset parameters live in assets/manifests/procgen/<asset_id>.json ({"concept": ..., "parameters": {...}}, a
deep override of FAMILIES[<family>] below, same convention as _procgen_soft_goods.py). The instance file's own
"family" key picks which family default to override.

    blender --background --factory-startup --python tools/asset_pipeline/_build_archetype_props.py -- \
        --asset prop_arch_crate_a [--review --review-size 1536] [--tokens N --wall-minutes M] [--device cpu]
"""
import copy
import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from procgen_lib import archetypes as ar  # noqa: E402
from procgen_lib import export  # noqa: E402
from procgen_lib.materials import Material  # noqa: E402
from procgen_lib.runner import run_template  # noqa: E402

DEFAULT_ASSET = "prop_arch_crate_a"

# One default parameter set per family: which archetypes.py function to call, its material slots, the Material
# recipe for each slot (index = slot number) and the builder's own P dict.
FAMILIES = {
    "crate": {
        "fn": "plank_box", "seed": 2701, "slots": {"wood": 0},
        "materials": [{"recipe": "wood", "species": "pine", "age": 0.4}],
        "params": {"length_m": 0.70, "depth_m": 0.55, "body_h_m": 0.60, "board_t_m": 0.018, "rows": 4,
                  "gap_m": 0.0015, "floor_boards": 3, "floor_z_m": 0.015, "lid": {"present": False}},
    },
    "chest": {
        "fn": "plank_box", "seed": 2702, "slots": {"wood": 0, "iron": 1, "rope": 2},
        "materials": [{"recipe": "wood", "species": "oak", "age": 0.45},
                      {"recipe": "iron", "kind": "forged", "rust": 0.30},
                      {"recipe": "rope", "colour": [150, 128, 92], "dirt": 0.2}],
        "params": {"length_m": 0.62, "depth_m": 0.40, "body_h_m": 0.36, "board_t_m": 0.020, "rows": 3,
                  "floor_boards": 3, "bands": {"z_m": [[0.045, 0.080], [0.280, 0.315]], "t_m": 0.003,
                  "rivet_r_m": 0.0055}, "lid": {"present": True, "t_m": 0.022, "boards": 3, "batten_w_m": 0.045,
                  "batten_h_m": 0.020, "hinges": 2, "hasp": True},
                  "handle": {"type": "rope", "z_m": 0.18, "span_m": 0.12, "r_m": 0.007}},
    },
    "bench": {
        "fn": "bench_table", "seed": 2703, "slots": {"wood": 0},
        "materials": [{"recipe": "wood", "species": "oak", "age": 0.35}],
        "params": {"length_m": 1.30, "depth_m": 0.32, "top_h_m": 0.45, "leg_m": [0.045, 0.045], "top_boards": 3,
                  "top_t_m": 0.03, "stretcher_z_frac": 0.30},
    },
    "wheel": {
        "fn": "wheel_axle", "seed": 2704, "slots": {"wood": 0, "iron": 1},
        "materials": [{"recipe": "wood", "species": "ash", "age": 0.5},
                      {"recipe": "iron", "kind": "forged", "rust": 0.35}],
        "params": {"radius_m": 0.56, "rim_t_m": 0.05, "rim_w_m": 0.09, "hub_r_m": 0.09, "hub_len_m": 0.22,
                  "spoke_count": 10, "spoke_w_m": 0.05, "spoke_t_m": 0.045, "axle_r_m": 0.035, "axle_len_m": 0.85,
                  "tire": {"present": True, "t_m": 0.014}},
    },
    "fence": {
        "fn": "posts_and_rails", "seed": 2705, "slots": {"wood": 0},
        "materials": [{"recipe": "wood", "species": "pine", "age": 0.55}],
        "params": {"posts_m": [[0.0, 0.0], [1.0, 0.04], [2.0, -0.02], [3.0, 0.03]], "post_h_m": 1.05,
                  "post_w_m": 0.09, "rails_z_m": [0.35, 0.75]},
    },
    "pipe": {
        "fn": "pipe_conduit", "seed": 2706, "slots": {"pipe": 0, "iron": 1},
        "materials": [{"recipe": "iron", "kind": "wrought", "rust": 0.4}, {"recipe": "iron", "kind": "cast", "rust": 0.5}],
        "params": {"path_m": [[0.0, 0.0, 0.25], [0.5, 0.0, 0.25], [0.5, 0.45, 0.25], [0.5, 0.45, 0.85]],
                  "radius_m": 0.045, "valve": {"at_t": 0.2, "body_r_m": 0.11, "body_len_m": 0.16}},
    },
    "techhousing": {
        "fn": "tech_housing", "seed": 2707, "slots": {"case": 0, "trim": 1},
        "materials": [{"recipe": "iron", "kind": "cast", "rust": 0.15}, {"recipe": "iron", "kind": "forged", "rust": 0.05}],
        "params": {"size_m": [0.42, 0.30, 0.34], "chamfer_m": 0.014, "panel": {"present": True},
                  "vents": {"present": True, "count": 5}, "fasteners": True},
    },
}


def deep_merge(base, override):
    """Recursive dict override; a material spec ({"recipe": ...}) or a list replaces wholesale (see
    _procgen_soft_goods.py, the same rule for the same reason)."""
    out = copy.deepcopy(base)
    for k, v in override.items():
        if isinstance(v, dict) and isinstance(out.get(k), dict) and "recipe" not in v:
            out[k] = deep_merge(out[k], v)
        else:
            out[k] = copy.deepcopy(v)
    return out


def mat_from(spec):
    spec = dict(spec)
    recipe = spec.pop("recipe")
    return Material(recipe, **spec)


def build(b, P, rng):
    fn = getattr(ar, P["fn"])
    spec = fn(b, P["params"], rng, P["slots"])
    spec["materials"] = [mat_from(m) for m in P["materials"]]
    spec.setdefault("sockets", [])
    if not spec["sockets"]:
        spec["sockets"] = [{"name": "SOCK_interact_take", "at": (0.0, 0.0, max(spec.get("dims", (0, 0, 0.3))[2] * 0.6, 0.1))}]
    spec.setdefault("notes", []).append(
        f"prop_archetype_kit (Phase B, B8): archetypes.{P['fn']}, family '{P.get('family', '?')}'.")
    return spec


if __name__ == "__main__":
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    asset_id, rest, it = DEFAULT_ASSET, [], iter(argv)
    for tok in it:
        if tok == "--asset":
            asset_id = next(it)
        else:
            rest.append(tok)
    inst_path = os.path.join(export.REPO, "assets", "manifests", "procgen", f"{asset_id}.json")
    with open(inst_path, encoding="utf-8") as handle:
        inst = json.load(handle)
    family = FAMILIES[inst["family"]]
    P = deep_merge(family, inst.get("parameters", {}))
    P["family"] = inst["family"]
    concept = inst.get("concept") or None
    run_template(build, P, asset_id, archetype="prop_archetype_kit", concept=concept, tri_budget=16000, res=1024,
                samples=20, argv=rest, instance_files=[inst_path])
