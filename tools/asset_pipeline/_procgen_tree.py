"""Procedural Charwood trees: flora_oak_tree, flora_pine_tree and flora_dead_tree, grown from a seeded skeleton.

The image-to-3D reconstructions in assets/ready are unusable (canopies of shard triangles, trunks leaning 17-31 deg),
so the three trees are grown here in headless Blender 5.2 from per-species parameters after the approved concepts
(assets/concepts/<id>.png) and exported as GLBs. The build is deterministic from --species and --seed.

Skeleton: a recursive grower. Each branch is stepped along a polyline with gravitropism (up- or down-turning by level
and by position along the branch), a seeded wobble, and a crown envelope at which it stops. The envelope is the
concept's silhouette (half width per height, measured from the concept image and tabled below), lumped per azimuth and
held in by the reach of the foliage cards, so the grown crown fills the concept's outline. Children leave their parent
in whorls (pine), by phyllotaxis (oak), or from an authored limb table (dead tree). Every branch draws from its own
derived seed, so a change to the envelope only changes the branches it touches; the envelope is re-centred until the
model's bounds centre on the trunk (the game centres a model's bounds on the world's 0.4 m tree blocker). The trunk is
vertical from the ground to its first limbs, with its base centred on the origin.

Geometry: trunk and branches are closed tapered tubes swept along their polylines with rotation-minimising frames
(outward winding). The trunk has a lobed root flare, burls and a spiral grain; a child starts on its parent's axis and
swells into a collar where it leaves the parent; live tips close in a point, broken ends in a splintered crater of
fresh wood, the trunk's foot in a flat cap on the ground. Bark UVs run along the grain: U is the arc fraction round the
ring (one tile around, so the ring is seamless), V is length over circumference, so texels stay square and the bark
scales with the branch, finer on twigs as real bark is. Foliage is alpha-cutout cards (alphaMode MASK) on the outer
branches: leaf sprays for the oak, crossed needle-tuft cards for the pine, none on the dead tree. Each card is two
single-sided faces back to back (separate vertices, so each side is lit), their normals bent toward the crown's outward
normal so the crown shades as one volume.

Textures: the bark and the broken wood of each species are procedural shaders baked with Cycles into seamless 2048
tiles (base colour sRGB, tangent normal OpenGL, ORM); a tile is periodic because its shader runs on a torus in 4D
noise space. The leaf and needle atlas (2048, 2 x 2 sprays) is drawn in numpy: lobed oak leaves or needle bursts on
twigs, each leaf or needle with its own normal, occlusion from the drawing order, colour pushed out into the
transparent texels so mip levels do not fringe dark.

Frames: built Blender Z-up with the concept's view from -Y; the glTF export (+Y up) puts that view on +Z (front),
centred on X/Z, lowest point at y = 0.

Usage:
  blender --background --factory-startup --python tools/asset_pipeline/_procgen_tree.py -- --species oak|pine|dead
          (or a variant: oak_b|oak_c|pine_b|pine_c|dead_b)
          [--seed N] [--out-dir DIR] [--work-dir DIR] [--res 2048] [--samples 16] [--device auto|cpu] [--no-bake]

Writes <out-dir>/<id>.glb and <id>_provenance.json (default <out-dir>: assets/_staging/procedural/<id>). Textures and
a .blend go to --work-dir (default: a temp folder), not to the staging folder. The last stdout line is "RESULT {json}".
"""
import argparse
import datetime
import hashlib
import json
import math
import os
import random
import sys
import tempfile
import time
import zlib

import bmesh
import bpy
import numpy as np
from mathutils import Vector

TOOL_DIR = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(os.path.dirname(TOOL_DIR))
TAU = 2.0 * math.pi
X, Y, Z = Vector((1, 0, 0)), Vector((0, 1, 0)), Vector((0, 0, 1))
BARK, WOOD, LEAF = 0, 1, 2
MAT_KEYS = ("bark", "wood", "foliage")
TRI_BUDGET = 40000
# World tree blockers (content/regions/*.yaml): circles of radius 0.4 m, 7.0 m tall. Fitting.cs rejects a model that
# leaves more than 0.5 m of blocker per side or stops more than 0.6 m below its top.
BLOCKER = {"radius": 0.4, "height": 7.0, "side_margin": 0.5, "height_shortfall": 0.6}

# Crown silhouettes measured from the concepts: (height fraction, half width / height), trunk-centred, upper hull of
# the left and right extents per row (see the provenance "concept_profile").
OAK_PROFILE = [(0.20, 0.0), (0.215, 0.30), (0.25, 0.40), (0.30, 0.44), (0.35, 0.45), (0.40, 0.49), (0.45, 0.50),
               (0.50, 0.51), (0.55, 0.48), (0.60, 0.43), (0.65, 0.41), (0.70, 0.42), (0.75, 0.39), (0.80, 0.38),
               (0.85, 0.30), (0.90, 0.25), (0.95, 0.20), (0.98, 0.13), (1.0, 0.0)]
PINE_PROFILE = [(0.28, 0.0), (0.30, 0.17), (0.35, 0.21), (0.40, 0.25), (0.45, 0.24), (0.50, 0.26), (0.55, 0.27),
                (0.60, 0.23), (0.65, 0.22), (0.70, 0.19), (0.75, 0.15), (0.80, 0.13), (0.85, 0.09), (0.90, 0.07),
                (0.95, 0.04), (1.0, 0.0)]
DEAD_PROFILE = [(0.30, 0.0), (0.32, 0.10), (0.45, 0.13), (0.50, 0.40), (0.55, 0.42), (0.60, 0.44), (0.65, 0.43),
                (0.70, 0.41), (0.75, 0.38), (0.80, 0.33), (0.85, 0.26), (0.90, 0.22), (0.95, 0.17), (1.0, 0.10),
                (1.001, 0.0)]
# A young ash's small, high crown: narrow, starting above half height (§Phase-1 brief: "small crowns"). Its
# scaffold z_frac (below) starts just above this f_lo, the same margin oak keeps (0.24 vs 0.20) so scaffolds are
# born already inside the envelope instead of below it with no room to reach up into it.
ASH_YOUNG_PROFILE = [(0.45, 0.0), (0.48, 0.08), (0.55, 0.13), (0.65, 0.15), (0.75, 0.15), (0.85, 0.12),
                     (0.93, 0.07), (1.0, 0.0)]
# Ground offsets (Blender X/Y, Z always 0) of the ash stand's three trunks - the glTF-space local positions the
# brief gives (X, Y=0, Z) converted by glTF_X=Blender_X, glTF_Z=-Blender_Y: (-0.35,0,0.2), (0.3,0,0.3), (0,0,-0.35).
ASH_OFFSETS = [(-0.35, -0.20), (0.30, -0.30), (0.0, 0.35)]

SPECIES = {
    "oak": {
        "asset_id": "flora_oak_tree", "seed": 3101, "height": 12.2,
        "crown": {"profile": OAK_PROFILE, "margin_m": 0.8, "lump": 0.12, "underside_frac": [0.29, 0.21]},
        "trunk": {"dbh_radius": 0.42, "top_radius": 0.07, "top_frac": 0.78, "taper_exp": 1.3, "straight_frac": 0.25,
                  "wobble_m": 0.32, "lean_m": [0.0, 0.0], "step_m": 0.45, "sides": 24, "flare": 0.16,
                  "flare_decay_m": 0.55, "lobes": 6, "lobe_amp": 0.50, "lobe_decay_m": 0.33, "lobe_power": 4.0,
                  "burls": 12, "burl_amp": 0.15, "noise_amp": 0.05, "twist": 0.05},
        "stubs": {"count": 3, "z_m": [1.9, 3.1], "len_m": [0.14, 0.26], "radius_m": [0.05, 0.07], "elev_deg": [5, 30]},
        "scaffold": {"count": [8, 9], "z_frac": [0.24, 0.6], "alpha_low_deg": [66, 76], "alpha_high_deg": [28, 40],
                     "ratio": [0.55, 0.66], "rise_per_m": 0.06, "rise_until": 0.25, "droop_per_m": 0.05,
                     "wobble": 0.22, "r_end": 0.03, "max_len": 9.5, "step_m": 0.45},
        "branch": {"spacing_m": [0.42, 0.66], "from": 0.16, "alpha_deg": [35, 55], "ratio": [0.42, 0.56],
                   "max_len": 4.2, "outward": 0.5, "rise_per_m": 0.06, "droop_per_m": 0.03, "wobble": 0.3,
                   "r_end": 0.012, "max_r": 0.09, "step_m": 0.55},
        "twig": {"spacing_m": [0.55, 0.8], "from": 0.3, "alpha_deg": [40, 62], "ratio": [0.45, 0.6], "max_len": 1.1,
                 "outward": 0.35, "rise_per_m": 0.1, "wobble": 0.45, "r_end": 0.006, "min_r": 0.013, "step_m": 0.5,
                 "min_shell": 0.4},
        "foliage": {"sprite": "oak", "card_m": [1.6, 2.2], "spacing_m": [0.3, 0.42], "branch_from": 0.3,
                    "twig_from": 0.0, "min_shell": 0.3, "crossed": False, "per_station": 2, "bend": 0.45, "outward": 0.35,
                    "up": 0.15},
        "bark": {"shader": "oak", "tile_m": 2.6},
        "wood": {"fresh": (166, 124, 82), "weathered": (126, 116, 102), "grey": 0.45},
    },
    "pine": {
        "asset_id": "flora_pine_tree", "seed": 2207, "height": 12.9,
        "crown": {"profile": PINE_PROFILE, "margin_m": 0.55, "lump": 0.08, "underside_frac": None},
        "trunk": {"dbh_radius": 0.35, "top_radius": 0.03, "top_frac": 0.945, "taper_exp": 1.05, "straight_frac": 0.40,
                  "wobble_m": 0.06, "lean_m": [0.0, 0.0], "step_m": 0.5, "sides": 20, "flare": 0.08,
                  "flare_decay_m": 0.4, "lobes": 5, "lobe_amp": 0.16, "lobe_decay_m": 0.28, "lobe_power": 3.0,
                  "burls": 0, "burl_amp": 0.0, "noise_amp": 0.03, "twist": 0.02},
        "stubs": {"count": 6, "z_m": [2.7, 4.3], "len_m": [0.25, 0.6], "radius_m": [0.03, 0.045],
                  "elev_deg": [15, 38]},
        "whorl": {"start_frac": 0.305, "spacing_m": [0.6, 0.95], "count": [3, 5], "skip": 0.24,
                  "elev_low_deg": -6, "elev_high_deg": 38, "elev_jitter_deg": 7, "ratio": 0.42, "r_end": 0.012,
                  "rise_per_m": 0.06, "rise_tip_per_m": 0.2, "wobble": 0.18, "step_m": 0.4},
        "lateral": {"spacing_m": [0.3, 0.42], "from": 0.2, "alpha_deg": [45, 65], "ratio": [0.4, 0.55],
                    "max_len": 1.5, "rise_per_m": 0.12, "wobble": 0.3, "r_end": 0.007, "min_r": 0.01,
                    "step_m": 0.35},
        "foliage": {"sprite": "pine", "card_m": [1.1, 1.45], "spacing_m": [0.3, 0.42], "branch_from": 0.42,
                    "twig_from": 0.25, "min_shell": 0.0, "crossed": True, "bend": 0.45, "outward": 0.2, "up": 0.3,
                    "leader_m": 1.5},
        "bark": {"shader": "pine", "tile_m": 2.4},
        "wood": {"fresh": (184, 134, 82), "weathered": (140, 122, 100), "grey": 0.35},
    },
    "dead": {
        "asset_id": "flora_dead_tree", "seed": 1409, "height": 12.8,
        "crown": {"profile": DEAD_PROFILE, "margin_m": 0.0, "lump": 0.06, "underside_frac": None},
        "trunk": {"dbh_radius": 0.44, "top_radius": 0.03, "top_frac": 0.985, "taper_exp": 0.85,
                  "straight_frac": 0.50, "wobble_m": 0.12, "lean_m": [-0.55, 0.0], "step_m": 0.45, "sides": 24,
                  "flare": 0.24, "flare_decay_m": 0.6, "lobes": 5, "lobe_amp": 0.72, "lobe_decay_m": 0.38,
                  "lobe_power": 5.0, "burls": 8, "burl_amp": 0.12, "noise_amp": 0.07, "twist": 0.07},
        # name, height frac, azimuth deg (0 = +X, the concept's right; -90 = toward the viewer), elevation deg,
        # length m, up-bend per m, end
        "limbs": [
            ("left_low", 0.47, 182, -2, 6.4, 0.05, "tip"),
            ("left_high", 0.58, 163, 12, 5.5, 0.05, "tip"),
            ("right_mid", 0.53, 4, 12, 6.2, 0.04, "tip"),
            ("right_leader", 0.555, -12, 52, 6.0, 0.02, "tip"),
            ("top_left", 0.75, 205, 60, 3.3, 0.02, "tip"),
            ("top_right", 0.82, 25, 55, 2.1, 0.0, "broken"),
            ("front", 0.58, -95, 26, 4.4, 0.05, "tip"),
            ("back", 0.66, 88, 32, 4.0, 0.04, "tip"),
            ("back_left", 0.72, 128, 36, 3.1, 0.03, "broken"),
            ("front_right", 0.69, -40, 40, 2.6, 0.03, "broken"),
            ("stub_left", 0.40, 172, 48, 1.0, 0.0, "broken"),
            ("stub_right", 0.42, 14, 38, 1.5, 0.04, "tip"),
        ],
        "limb_jitter": {"len": 0.08, "az_deg": 8, "elev_deg": 4},
        "limb_ratio": [0.6, 0.72], "limb_r_end": 0.012, "limb_wobble": 0.2, "limb_step_m": 0.42,
        "stubs": {"count": 3, "z_m": [2.2, 4.6], "len_m": [0.25, 0.45], "radius_m": [0.06, 0.09], "elev_deg": [20, 40]},
        "branch": {"per_limb": [4, 7], "from": 0.3, "alpha_deg": [28, 52], "ratio": [0.4, 0.55], "len_m": [0.8, 2.3],
                   "broken": 0.38, "rise_per_m": 0.08, "wobble": 0.35, "r_end": 0.006, "min_r": 0.012,
                   "step_m": 0.38, "up_bias": 0.55},
        "twig": {"per_branch": [0, 2], "from": 0.35, "alpha_deg": [30, 55], "ratio": [0.45, 0.6], "len_m": [0.35, 0.9],
                 "broken": 0.3, "rise_per_m": 0.1, "wobble": 0.5, "r_end": 0.004, "min_r": 0.008, "step_m": 0.3},
        "foliage": None,
        "bark": {"shader": "dead", "tile_m": 2.4},
        "wood": {"fresh": (186, 128, 72), "weathered": (150, 140, 126), "grey": 0.4},
    },
    # --- Phase-A prototype presets (template variants of the same trunk/tube skeleton; docs/
    # WAVE_0_MODULAR_ASSET_STANDARD.md section 17). "log": a straight trunk-only tube with no crown, built vertical
    # like any trunk and laid on its side afterwards (kind == "log", see reorient_length_x). "cluster"/
    # "stump_cluster": the same small trunk recipe built three times at fixed footprint offsets (ASH_OFFSETS).
    "log": {
        "asset_id": "flora_fallen_log", "seed": 4801, "height": 7.7, "kind": "log",
        "root_broken": True, "tip_end": "broken",
        # flare/lobes/burls stay small: unlike a standing trunk this is a torn-off SECTION, not a root crown, and
        # the splinter jag at each end (tube()'s ~1.1x radius spike) already adds ~0.6-0.8 m of its own to the
        # built height above, and to the bark's own bulge below, budgeted for in the target dims
        "trunk": {"dbh_radius": 0.40, "top_radius": 0.30, "top_frac": 0.98, "taper_exp": 0.7, "straight_frac": 0.5,
                  "wobble_m": 0.05, "lean_m": [0.0, 0.22], "step_m": 0.45, "sides": 18, "flare": 0.03,
                  "flare_decay_m": 0.3, "lobes": 5, "lobe_amp": 0.06, "lobe_decay_m": 0.15, "lobe_power": 4.0,
                  "burls": 3, "burl_amp": 0.03, "noise_amp": 0.05, "twist": 0.04},
        "stubs": {"count": 5, "z_m": [0.8, 6.9], "len_m": [0.06, 0.16], "radius_m": [0.03, 0.055],
                  "elev_deg": [-20, 70]},
        "foliage": None,
        "bark": {"shader": "oak", "tile_m": 2.6, "moss_boost": 0.55},
        "wood": {"fresh": (166, 124, 82), "weathered": (126, 116, 102), "grey": 0.45},
        "target": {"box_m": (8.0, 0.8), "height_m": 0.8, "length_range": (7.5, 8.5), "diam_range": (0.55, 1.05)},
    },
    "beam": {
        "asset_id": "prop_woundmoss_beam", "seed": 4802, "height": 5.95, "kind": "log",
        "root_broken": True, "tip_end": "broken",
        "trunk": {"dbh_radius": 0.16, "top_radius": 0.12, "top_frac": 0.98, "taper_exp": 0.7, "straight_frac": 0.5,
                  "wobble_m": 0.03, "lean_m": [0.0, 0.1], "step_m": 0.4, "sides": 14, "flare": 0.02,
                  "flare_decay_m": 0.2, "lobes": 4, "lobe_amp": 0.04, "lobe_decay_m": 0.1, "lobe_power": 4.0,
                  "burls": 2, "burl_amp": 0.015, "noise_amp": 0.04, "twist": 0.03},
        "stubs": {"count": 3, "z_m": [0.5, 5.4], "len_m": [0.04, 0.09], "radius_m": [0.015, 0.03],
                  "elev_deg": [-20, 65]},
        "foliage": None,
        "bark": {"shader": "oak", "tile_m": 2.6, "moss_boost": 1.0},
        "wood": {"fresh": (166, 124, 82), "weathered": (126, 116, 102), "grey": 0.45},
        "target": {"box_m": (6.0, 0.8), "height_m": 1.7, "clearance_m": 1.3, "length_range": (6.0, 6.4),
                  "diam_range": (0.3, 0.62)},
    },
    "ash_stand": {
        "asset_id": "flora_young_ash_stand", "seed": 4803, "height": 2.8, "kind": "cluster", "offsets": ASH_OFFSETS,
        "crown": {"profile": ASH_YOUNG_PROFILE, "margin_m": 0.06, "lump": 0.1, "underside_frac": [0.35, 0.25]},
        "trunk": {"dbh_radius": 0.045, "top_radius": 0.010, "top_frac": 0.62, "taper_exp": 1.2, "straight_frac": 0.35,
                  "wobble_m": 0.03, "lean_m": [0.0, 0.0], "step_m": 0.16, "sides": 10, "flare": 0.10,
                  "flare_decay_m": 0.12, "lobes": 3, "lobe_amp": 0.12, "lobe_decay_m": 0.08, "lobe_power": 3.0,
                  "burls": 0, "burl_amp": 0.0, "noise_amp": 0.02, "twist": 0.02},
        "stubs": None,
        "scaffold": {"count": [3, 4], "z_frac": [0.48, 0.62], "alpha_low_deg": [30, 45], "alpha_high_deg": [6, 14],
                     "ratio": [0.5, 0.62], "rise_per_m": 0.26, "rise_until": 0.25, "droop_per_m": 0.05,
                     "wobble": 0.15, "r_end": 0.006, "max_len": 2.5, "step_m": 0.14},
        "branch": {"spacing_m": [0.12, 0.18], "from": 0.16, "alpha_deg": [20, 38], "ratio": [0.42, 0.56],
                   "max_len": 1.4, "outward": 0.5, "rise_per_m": 0.18, "droop_per_m": 0.03, "wobble": 0.25,
                   "r_end": 0.004, "max_r": 0.02, "step_m": 0.14},
        "twig": {"spacing_m": [0.14, 0.2], "from": 0.3, "alpha_deg": [40, 62], "ratio": [0.45, 0.6], "max_len": 0.35,
                 "outward": 0.35, "rise_per_m": 0.15, "wobble": 0.35, "r_end": 0.0022, "min_r": 0.004,
                 "step_m": 0.12, "min_shell": 0.35},
        "foliage": {"sprite": "oak", "card_m": [0.32, 0.46], "spacing_m": [0.09, 0.13], "branch_from": 0.3,
                    "twig_from": 0.0, "min_shell": 0.25, "crossed": False, "per_station": 2, "bend": 0.4,
                    "outward": 0.35, "up": 0.15},
        "bark": {"shader": "oak", "tile_m": 0.6},
        "wood": {"fresh": (196, 172, 132), "weathered": (150, 140, 120), "grey": 0.4},
    },
    "ash_stump": {
        "asset_id": "flora_ash_stumps", "seed": 4804, "height": 0.25, "kind": "stump_cluster",
        "offsets": ASH_OFFSETS, "tip_end": "cut",
        "trunk": {"dbh_radius": 0.045, "top_radius": 0.040, "top_frac": 0.9, "taper_exp": 0.6, "straight_frac": 0.0,
                  "wobble_m": 0.008, "lean_m": [0.0, 0.0], "step_m": 0.05, "sides": 10, "flare": 0.12,
                  "flare_decay_m": 0.06, "lobes": 3, "lobe_amp": 0.14, "lobe_decay_m": 0.05, "lobe_power": 3.0,
                  "burls": 0, "burl_amp": 0.0, "noise_amp": 0.02, "twist": 0.0},
        "stubs": None,
        "foliage": None,
        "bark": {"shader": "oak", "tile_m": 0.5, "moss_boost": 0.15},
        "wood": {"fresh": (196, 172, 132), "weathered": (150, 140, 120), "grey": 0.4, "shader": "rings"},
    },
}


def _variant(base, **over):
    """A species variant: a deep copy of a base preset with top-level keys replaced or (for dicts) merged. The base's
    own entry is never modified, so the original ids grow exactly as before."""
    import copy
    sp = copy.deepcopy(SPECIES[base])
    for k, v in over.items():
        if isinstance(v, dict) and isinstance(sp.get(k), dict):
            sp[k].update(v)
        else:
            sp[k] = v
    sp["builder"] = base
    sp["variant_of"] = SPECIES[base]["asset_id"]
    return sp


def _scale_profile(profile, lo=1.0, hi=None, f_split=0.6):
    """A crown profile with its half widths scaled: `lo` below f_split, blending to `hi` at the top."""
    hi = lo if hi is None else hi
    return [(f, round(w * (lo if f <= f_split else lo + (hi - lo) * (f - f_split) / (1.0 - f_split)), 4))
            for f, w in profile]


def _lift_profile(profile, f0):
    """The same crown shape starting at height fraction f0 instead of the profile's own start (a higher crown base)."""
    a = profile[0][0]
    return [(round(f0 + (f - a) * (1.0 - f0) / (1.0 - a), 4), w) for f, w in profile]


# Charwood variants (new ids; the three originals above are untouched). Different seeds and modestly different heights
# and crown proportions inside each species' concept silhouette. The broadleaf variants carry "clump": each branch's
# spray of cards shades as its own rounded mass (normals bent toward the spray's centre as well as the crown's), and
# inner branches carry cards too, so the crown reads as layered lobes rather than one ball.
OAK_CLUMP = {"group_level": 2, "mix": 0.6, "min_cards": 5}
SPECIES["oak_b"] = _variant(
    "oak", asset_id="flora_oak_tree_b", seed=3217, height=11.3,
    crown={"profile": _scale_profile(OAK_PROFILE, 1.12, 1.02), "lump": 0.2, "underside_frac": [0.27, 0.2]},
    trunk={"dbh_radius": 0.44, "wobble_m": 0.36},
    scaffold={"count": [9, 10], "z_frac": [0.23, 0.58], "alpha_low_deg": [70, 80], "max_len": 10.0},
    foliage={"card_m": [1.5, 2.1], "branch_from": 0.12, "min_shell": 0.12, "bend": 0.6, "roll": 1.1,
             "clump": OAK_CLUMP},
    height_range_m=[11.5, 14.0])
SPECIES["oak_c"] = _variant(
    "oak", asset_id="flora_oak_tree_c", seed=3343, height=13.1,
    crown={"profile": _scale_profile(_lift_profile(OAK_PROFILE, 0.23), 0.94, 1.1), "lump": 0.18,
           "underside_frac": [0.31, 0.24]},
    trunk={"dbh_radius": 0.40, "top_frac": 0.8, "straight_frac": 0.3},
    scaffold={"count": [8, 9], "z_frac": [0.27, 0.64], "alpha_low_deg": [60, 72], "alpha_high_deg": [24, 34]},
    foliage={"card_m": [1.5, 2.1], "branch_from": 0.12, "min_shell": 0.12, "bend": 0.6, "roll": 1.1,
             "clump": OAK_CLUMP},
    height_range_m=[12.5, 15.0])
SPECIES["pine_b"] = _variant(
    "pine", asset_id="flora_pine_tree_b", seed=2311, height=14.9,
    crown={"profile": _scale_profile(_lift_profile(PINE_PROFILE, 0.36), 0.86, 0.9)},
    trunk={"dbh_radius": 0.36, "straight_frac": 0.45},
    stubs={"count": 8, "z_m": [2.8, 5.2]},
    whorl={"start_frac": 0.375, "spacing_m": [0.7, 1.05], "skip": 0.2},
    foliage={"bend": 0.55, "clump": {"group_level": 1, "mix": 0.5, "min_cards": 4}},
    height_range_m=[14.0, 16.0])
SPECIES["pine_c"] = _variant(
    "pine", asset_id="flora_pine_tree_c", seed=2423, height=13.7,
    crown={"profile": _scale_profile(_lift_profile(PINE_PROFILE, 0.31), 1.04, 0.95), "lump": 0.12},
    trunk={"dbh_radius": 0.38, "wobble_m": 0.1},
    whorl={"start_frac": 0.325, "spacing_m": [0.62, 0.98], "count": [4, 5], "skip": 0.3},
    foliage={"bend": 0.55, "clump": {"group_level": 1, "mix": 0.5, "min_cards": 4}},
    height_range_m=[13.0, 15.0])
# A snag of the same concept: the crown's spread turned about the trunk, a lean the other way, the leader snapped.
SPECIES["dead_b"] = _variant(
    "dead", asset_id="flora_dead_tree_b", seed=1523, height=11.9,
    crown={"profile": [(0.30, 0.0), (0.32, 0.10), (0.45, 0.14), (0.50, 0.36), (0.55, 0.40), (0.60, 0.41),
                       (0.65, 0.40), (0.70, 0.38), (0.75, 0.35), (0.80, 0.30), (0.85, 0.25), (0.90, 0.2),
                       (0.95, 0.15), (1.0, 0.10), (1.001, 0.0)]},
    trunk={"dbh_radius": 0.41, "lean_m": [0.35, -0.3], "top_frac": 0.93, "wobble_m": 0.16},
    limbs=[
        ("right_low", 0.45, -8, 4, 5.8, 0.05, "tip"),
        ("right_high", 0.6, 22, 18, 4.6, 0.05, "tip"),
        ("left_mid", 0.52, 172, 14, 5.6, 0.04, "tip"),
        ("left_leader", 0.57, 196, 55, 5.4, 0.02, "tip"),
        ("top_right", 0.74, -30, 62, 3.0, 0.02, "tip"),
        ("top_left", 0.8, 150, 58, 1.9, 0.0, "broken"),
        ("back", 0.55, 95, 24, 4.2, 0.05, "tip"),
        ("front", 0.65, -85, 30, 3.8, 0.04, "tip"),
        ("front_left", 0.7, -130, 38, 2.4, 0.03, "broken"),
        ("back_right", 0.68, 45, 40, 2.8, 0.03, "broken"),
        ("stub_right", 0.38, 20, 45, 1.1, 0.0, "broken"),
        ("stub_left", 0.43, 200, 36, 1.4, 0.04, "tip"),
    ],
    height_range_m=[11.0, 13.5])


def sub_rng(seed, *keys):
    """A generator of its own for one branch/whorl/sprite, derived from the master seed and a stable key."""
    return random.Random(zlib.crc32(("%d|" % seed + "|".join(str(k) for k in keys)).encode("utf-8")))


def lerp(a, b, t):
    return a + (b - a) * t


def smoothstep(e0, e1, x):
    t = min(1.0, max(0.0, (x - e0) / (e1 - e0)))
    return t * t * (3.0 - 2.0 * t)


def interp(table, x):
    if x <= table[0][0] or x >= table[-1][0]:
        return table[0][1] if x <= table[0][0] else table[-1][1]
    for (x0, y0), (x1, y1) in zip(table, table[1:]):
        if x0 <= x <= x1:
            return y0 + (y1 - y0) * (x - x0) / (x1 - x0)
    return table[-1][1]


# --------------------------------------------------------------------------------------------------------------------
# Crown envelope
# --------------------------------------------------------------------------------------------------------------------

class Envelope:
    """Surface of revolution from the concept profile, lumped per azimuth, with a movable centre."""

    def __init__(self, crown, H, rng):
        self.table = crown["profile"]
        self.H = H
        self.margin = crown["margin_m"]
        self.lump_amp = crown["lump"]
        self.under = crown.get("underside_frac")
        self.cx = self.cy = 0.0
        self.terms = [(k, rng.uniform(0, TAU), rng.uniform(-3, 3), rng.uniform(0.5, 1.0)) for k in (2, 3, 4, 5, 7)]
        norm = sum(a / k for k, _, _, a in self.terms)
        self.norm = norm if norm > 0 else 1.0
        self.fmin = self.table[0][0]

    def lump(self, phi, f):
        s = sum(a * math.sin(k * phi + p + q * f) / k for k, p, q, a in self.terms)
        return 1.0 + self.lump_amp * s / self.norm

    def full_width(self, z, phi=0.0):
        f = z / self.H
        return interp(self.table, f) * self.H * self.lump(phi, f)

    def polar(self, p):
        dx, dy = p.x - self.cx, p.y - self.cy
        return math.hypot(dx, dy), math.atan2(dy, dx)

    def inside(self, p, extra=0.0):
        if p.z >= self.H:
            return False
        rho, phi = self.polar(p)
        return rho <= self.full_width(p.z, phi) - self.margin + extra

    def shell(self, p):
        """Radial fraction of the crown's full width at p's height (0 on the axis, 1 on the silhouette)."""
        rho, phi = self.polar(p)
        w = self.full_width(p.z, phi)
        return rho / w if w > 1e-3 else 9.0

    def above_underside(self, p):
        if not self.under:
            return True
        s = min(1.0, self.shell(p))
        return p.z >= self.H * lerp(self.under[0], self.under[1], s ** 1.5)

    def outward(self, p, fallback):
        v = Vector((p.x - self.cx, p.y - self.cy, 0.0))
        if v.length < 1e-4:
            v = Vector((fallback.x, fallback.y, 0.0))
        return v.normalized() if v.length > 1e-6 else X.copy()

    def radial3(self, p):
        """Outward normal of the crown's ellipsoid at p (the direction a card on the crown's skin faces)."""
        a = max(w for _, w in self.table) * self.H
        zc = self.H * (self.fmin + 1.0) / 2
        b = self.H * (1.0 - self.fmin) / 2
        v = Vector(((p.x - self.cx) / a ** 2, (p.y - self.cy) / a ** 2, (p.z - zc) / b ** 2))
        return v.normalized() if v.length > 1e-9 else Z.copy()

    def reach_scale(self, d):
        """Horizontal centring for authored limb lengths: shorten limbs that point where the model sticks out."""
        dh = Vector((d.x, d.y, 0.0))
        if dh.length < 1e-6:
            return 1.0
        dh.normalize()
        return max(0.6, 1.0 + (self.cx * dh.x + self.cy * dh.y) / 5.0)


# --------------------------------------------------------------------------------------------------------------------
# Skeleton
# --------------------------------------------------------------------------------------------------------------------

class Branch:
    def __init__(self, name, level, kind, parent=None):
        self.name, self.level, self.kind, self.parent = name, level, kind, parent
        self.pts, self.rad = [], []
        self.sides = 6
        self.end = "tip"
        self.twist = 0.0
        self.shape = None
        self.children = []
        self.seed = 0
        self.root_broken = False  # start cap (s=0) gets a jagged torn-root crater instead of the flat foot fan

    def finish(self):
        self.s = [0.0]
        for a, b in zip(self.pts, self.pts[1:]):
            self.s.append(self.s[-1] + (b - a).length)
        self.length = self.s[-1]

    def frame_at(self, s):
        s = min(max(s, 0.0), self.length)
        for i in range(len(self.pts) - 1):
            if self.s[i + 1] >= s or i == len(self.pts) - 2:
                seg = self.s[i + 1] - self.s[i]
                t = 0.0 if seg < 1e-9 else (s - self.s[i]) / seg
                P = self.pts[i].lerp(self.pts[i + 1], t)
                T = (self.pts[i + 1] - self.pts[i]).normalized()
                r = lerp(self.rad[i], self.rad[i + 1], t)
                return P, T, r
        return self.pts[-1], (self.pts[-1] - self.pts[-2]).normalized(), self.rad[-1]

    def s_at_z(self, z):
        for i in range(len(self.pts) - 1):
            if self.pts[i + 1].z >= z:
                a, b = self.pts[i], self.pts[i + 1]
                t = 0.0 if b.z - a.z < 1e-9 else (z - a.z) / (b.z - a.z)
                return self.s[i] + t * (self.s[i + 1] - self.s[i])
        return self.length


def wobble_terms(rng, amp):
    return [(rng.uniform(0.8, 2.2), rng.uniform(0, TAU), rng.uniform(0.8, 2.2), rng.uniform(0, TAU),
             rng.uniform(0.8, 2.2), rng.uniform(0, TAU), amp)]


def wobble_at(terms, s):
    v = Vector((0.0, 0.0, 0.0))
    for f1, p1, f2, p2, f3, p3, a in terms:
        v += Vector((math.sin(f1 * s + p1), math.sin(f2 * s + p2), math.sin(f3 * s + p3))) * a
    return v


def sides_for(r0, level):
    if level == 0:
        return None
    if r0 >= 0.14:
        return 12
    if r0 >= 0.08:
        return 10
    if r0 >= 0.045:
        return 8
    if r0 >= 0.022:
        return 6
    if r0 >= 0.014:
        return 5
    return 4


def grow(name, level, kind, parent, s_attach, d0, L_max, r0, r_end, trop, wob, step, env, rng, end="tip",
         min_len=0.3, taper_k=1.0, collar=0.3, env_extra=0.0):
    """Step a branch from its parent's axis along d0 until L_max or the envelope; returns a finished Branch or None."""
    br = Branch(name, level, kind, parent)
    br.seed = rng.random()
    P0, _, rp = parent.frame_at(s_attach)
    d = d0.normalized()
    inner = rp * 1.02
    pts = [P0.copy(), P0 + d * inner]
    s = inner
    terms = wobble_terms(rng, wob)
    L_max = max(L_max, inner + 0.05)
    while s < L_max - 1e-6:
        h = min(step, L_max - s)
        if h < 0.04:
            break
        p = pts[-1]
        t = (s - inner) / max(1e-6, L_max - inner)
        g = trop(t, p, d)
        w = wobble_at(terms, s)
        w -= d * w.dot(d)
        d = (d + g * h + w * h).normalized()
        if p.z < 0.6 and d.z < 0.15:
            d = (d + Z * (0.15 - d.z)).normalized()
        q = p + d * h
        if env is not None and not env.inside(q, env_extra) and s > inner + 0.15:
            lo, hi = 0.0, 1.0
            for _ in range(12):
                mid = 0.5 * (lo + hi)
                if env.inside(p + d * (h * mid), env_extra):
                    lo = mid
                else:
                    hi = mid
            if lo * h > 0.06:
                pts.append(p + d * (h * lo))
                s += lo * h
            break
        pts.append(q)
        s += h
    L = s
    if L - inner < min_len:
        return None
    br.pts = pts
    br.finish()
    rads = []
    for si in br.s:
        x = min(1.0, max(0.0, (si - inner) / max(1e-6, L - inner)))
        r = r_end + (r0 - r_end) * (1.0 - x) ** taper_k
        r *= 1.0 + collar * math.exp(-max(0.0, si - 0.85 * rp) / (1.6 * r0))
        rads.append(r)
    rads[0] = min(rads[0], 0.9 * rp)
    rads[1] = min(rads[1], 0.95 * rp) if len(rads) > 2 else rads[1]
    br.rad = rads
    br.end = end
    br.sides = sides_for(r0, level)
    parent.children.append(br)
    return br


def make_trunk(sp, H, rng, end="tip"):
    t = sp["trunk"]
    br = Branch("trunk", 0, "trunk")
    ztop = t["top_frac"] * H
    zs = t["straight_frac"] * H
    # kept below ztop so a very short trunk (a stump, H a fraction of a metre) still gets a monotonic station
    # list; unchanged for every existing species, whose ztop is always well above 1.3 m
    stations = [z for z in (0.0, 0.04, 0.10, 0.18, 0.28, 0.40, 0.55, 0.75, 1.0, 1.3) if z < ztop] or [0.0]
    z = stations[-1]
    while z + t["step_m"] < ztop - 0.05:
        z += t["step_m"]
        stations.append(z)
    stations.append(ztop)
    terms = [(rng.uniform(0.25, 0.6), rng.uniform(0, TAU), rng.uniform(0.25, 0.6), rng.uniform(0, TAU))
             for _ in range(2)]
    lean = Vector((t["lean_m"][0], t["lean_m"][1], 0.0))
    pts = []
    for z in stations:
        ramp = smoothstep(zs, zs + 2.5, z)
        wx = sum(math.sin(a * z + p) for a, p, _, _ in terms) * 0.5
        wy = sum(math.sin(b * z + q) for _, _, b, q in terms) * 0.5
        off = Vector((wx, wy, 0.0)) * (t["wobble_m"] * ramp)
        off += lean * smoothstep(zs, ztop, z) ** 1.5
        pts.append(Vector((off.x, off.y, z)))
    br.pts = pts
    br.finish()
    k = t["taper_exp"]
    rt, dbh = t["top_radius"], t["dbh_radius"]
    # dbh_radius is normally anchored at breast height (1.3 m); a trunk shorter than that (a stump) has no such
    # point on it, and the anchor's extrapolation goes negative-under-a-fractional-power (complex) - dbh_radius is
    # then just the base radius directly
    rb = rt + (dbh - rt) / (1.0 - 1.3 / ztop) ** k if ztop > 1.3 else dbh
    br.rad = [rt + (rb - rt) * max(0.0, 1.0 - p.z / ztop) ** k for p in pts]
    br.sides = t["sides"]
    br.twist = t["twist"]
    br.end = end
    # root flare, lobes, burls and bark noise as a radius multiplier per ring height and angle
    lobes = []
    base = rng.uniform(0, TAU)
    for i in range(t["lobes"]):
        lobes.append((base + TAU * i / t["lobes"] + rng.uniform(-0.35, 0.35), rng.uniform(0.6, 1.0)))
    burls = [(rng.uniform(0.6, zs + 1.0), rng.uniform(0, TAU), rng.uniform(0.5, 1.0) * t["burl_amp"],
              rng.uniform(0.12, 0.3), rng.uniform(0.25, 0.5)) for _ in range(t["burls"])]
    noise = [(m, rng.uniform(0, TAU), rng.uniform(-1.5, 1.5)) for m in (3, 5, 7, 9)]

    def angd(a, b):
        return (a - b + math.pi) % TAU - math.pi

    def shape(z, th):
        s = 1.0 + t["flare"] * math.exp(-z / t["flare_decay_m"])
        lob = sum(w * max(0.0, math.cos(angd(th, a))) ** t["lobe_power"] for a, w in lobes)
        s += t["lobe_amp"] * math.exp(-z / t["lobe_decay_m"]) * lob
        for bz, bth, amp, wz, wth in burls:
            s += amp * math.exp(-((z - bz) / wz) ** 2 - (angd(th, bth) / wth) ** 2)
        s += t["noise_amp"] * sum(math.sin(m * th + p + q * z) for m, p, q in noise) / 2.0
        return s
    br.shape = shape
    br.flare_lobes = lobes
    return br


def add_stubs(trunk, sp, env, seed, out):
    st = sp.get("stubs")
    if not st:
        return
    for i in range(st["count"]):
        rng = sub_rng(seed, "stub", i)
        z = rng.uniform(*st["z_m"])
        az = rng.uniform(0, TAU)
        el = math.radians(rng.uniform(*st["elev_deg"]))
        d = Vector((math.cos(el) * math.cos(az), math.cos(el) * math.sin(az), math.sin(el)))
        s = trunk.s_at_z(z)
        _, _, rt = trunk.frame_at(s)
        L = rt + rng.uniform(*st["len_m"])
        r0 = min(rng.uniform(*st["radius_m"]), 0.35 * rt)
        br = grow("stub", 1, "stub", trunk, s, d, L, r0, r0 * 0.8, lambda t_, p_, d_: Vector((0, 0, 0)), 0.02, 0.2,
                  None, rng, end="broken", min_len=0.05, collar=0.45)
        if br:
            out.append(br)


def straight_trop(rise):
    return lambda t, p, d: Z * rise


# --------------------------------------------------------------------------------------------------------------------
# Species growth
# --------------------------------------------------------------------------------------------------------------------

def child_dir(T, azim, alpha):
    """Direction at `alpha` from T, `azim` round T from its upper side (0 = up, +-pi/2 = the sides)."""
    ref = Z - T * Z.dot(T)
    if ref.length < 1e-5:
        ref = X - T * X.dot(T)
    ref.normalize()
    side = T.cross(ref)
    perp = ref * math.cos(azim) + side * math.sin(azim)
    return (T * math.cos(alpha) + perp * math.sin(alpha)).normalized()


def spawn_along(parent, spec, env, seed, level, kind, out, s_from, s_to, azim_fn, max_len_fn, extra_filter=None,
                end_fn=None):
    rng = sub_rng(seed, kind, "spacing", parent.name)
    s = s_from
    k = 0
    made = []
    while s < s_to:
        P, T, rp = parent.frame_at(s)
        crng = sub_rng(seed, kind, parent.name, k)
        k += 1
        if extra_filter is None or extra_filter(P):
            az = azim_fn(crng, k)
            al = math.radians(crng.uniform(*spec["alpha_deg"]))
            d = child_dir(T, az, al)
            if spec.get("outward"):
                d = (d + env.outward(P, T) * spec["outward"]).normalized()
            L = max_len_fn(crng, s, P, d)
            r0 = min(spec.get("max_r", 1.0), rp * crng.uniform(*spec["ratio"]))
            if r0 >= spec.get("min_r", 0.0):
                rise = spec.get("rise_per_m", 0.0)
                droop = spec.get("droop_per_m", 0.0)

                def trop(t, p, d_, rise=rise, droop=droop):
                    return Z * (rise - droop * t)
                end = end_fn(crng) if end_fn else "tip"
                br = grow(f"{kind}_{parent.name}_{k}", level, kind, parent, s, d, L, r0, spec["r_end"], trop,
                          spec["wobble"], spec["step_m"], env, crng, end=end, min_len=0.25)
                if br:
                    made.append(br)
                    out.append(br)
        s += rng.uniform(*spec["spacing_m"]) if "spacing_m" in spec else 1.0
    return made


def build_oak(sp, env, seed):
    H = sp["height"]
    trunk = make_trunk(sp, H, sub_rng(seed, "trunk"))
    out = [trunk]
    add_stubs(trunk, sp, env, seed, out)
    sc = sp["scaffold"]
    rng = sub_rng(seed, "scaffold")
    n = rng.randint(*sc["count"])
    zs = sorted(rng.uniform(*sc["z_frac"]) for _ in range(n))
    zs[0] = sc["z_frac"][0]
    az0 = rng.uniform(0, TAU)
    scaffolds = []
    for i, zf in enumerate(zs):
        crng = sub_rng(seed, "scaffold", i)
        az = az0 + i * math.radians(137.5) + crng.uniform(-0.3, 0.3)
        f = (zf - sc["z_frac"][0]) / (sc["z_frac"][1] - sc["z_frac"][0])
        a_lo, a_hi = crng.uniform(*sc["alpha_low_deg"]), crng.uniform(*sc["alpha_high_deg"])
        alpha = math.radians(lerp(a_lo, a_hi, f))
        d = Vector((math.sin(alpha) * math.cos(az), math.sin(alpha) * math.sin(az), math.cos(alpha)))
        s = trunk.s_at_z(zf * H)
        _, _, rt = trunk.frame_at(s)
        r0 = rt * crng.uniform(*sc["ratio"])

        def trop(t, p, d_):
            return Z * (sc["rise_per_m"] if t < sc["rise_until"] else -sc["droop_per_m"])
        br = grow(f"scaffold_{i}", 1, "scaffold", trunk, s, d, sc["max_len"] * env.reach_scale(d), r0, sc["r_end"],
                  trop, sc["wobble"], sc["step_m"], env, crng, taper_k=1.15, collar=0.25)
        if br:
            out.append(br)
            scaffolds.append(br)
    bs = sp["branch"]
    leader_from = trunk.s_at_z(zs[0] * H + 0.8)
    parents = scaffolds + [trunk]
    branches = []
    for par in parents:
        s_from = bs["from"] * par.length if par is not trunk else leader_from

        def azim(crng, k):
            a = k * math.radians(137.5) + crng.uniform(-0.4, 0.4)
            if math.cos(a) < -0.55:
                a = math.copysign(math.radians(115), math.sin(a) if abs(math.sin(a)) > 1e-3 else 1.0)
            return a

        def max_len(crng, s, P, d, par=par):
            return min(bs["max_len"], 0.75 * (par.length - s) + 1.2) * env.reach_scale(d)
        branches += spawn_along(par, bs, env, seed, 2, "branch", out, s_from, par.length - 0.3, azim, max_len)
    tw = sp["twig"]
    twigs = []
    for par in branches:
        def azim(crng, k):
            return math.copysign(crng.uniform(math.radians(40), math.radians(140)), 1 if k % 2 else -1)

        def max_len(crng, s, P, d):
            return tw["max_len"] * crng.uniform(0.6, 1.0)

        def filt(P):
            return env.shell(P) >= tw["min_shell"]
        twigs += spawn_along(par, tw, env, seed, 3, "twig", out, tw["from"] * par.length, par.length - 0.15, azim,
                             max_len, extra_filter=filt)
    foliage_hosts = [(b, sp["foliage"]["branch_from"]) for b in branches] + \
                    [(b, sp["foliage"]["twig_from"]) for b in twigs]
    return out, foliage_hosts, trunk


def build_pine(sp, env, seed):
    H = sp["height"]
    trunk = make_trunk(sp, H, sub_rng(seed, "trunk"))
    out = [trunk]
    add_stubs(trunk, sp, env, seed, out)
    wh = sp["whorl"]
    rng = sub_rng(seed, "whorls")
    z0 = wh["start_frac"] * H
    ztop = trunk.pts[-1].z - 0.45
    z = z0
    rot = rng.uniform(0, TAU)
    whorl_branches = []
    k = 0
    while z < ztop:
        wr = sub_rng(seed, "whorl", k)
        n = wr.randint(*wh["count"])
        rot += math.radians(137.5) + wr.uniform(-0.3, 0.3)
        f = (z - z0) / max(1e-6, ztop - z0)
        s = trunk.s_at_z(z)
        _, _, rt = trunk.frame_at(s)
        for j in range(n):
            br_rng = sub_rng(seed, "whorl", k, j)
            skip = br_rng.random() < wh["skip"]
            az = rot + TAU * j / n + br_rng.uniform(-0.3, 0.3)
            el = math.radians(lerp(wh["elev_low_deg"], wh["elev_high_deg"], f)
                              + br_rng.uniform(-wh["elev_jitter_deg"], wh["elev_jitter_deg"]))
            d = Vector((math.cos(el) * math.cos(az), math.cos(el) * math.sin(az), math.sin(el)))
            if skip:
                continue
            L = (env.full_width(z, az) * 1.25 + 0.6) * env.reach_scale(d)
            r0 = min(rt * wh["ratio"], 0.022 + 0.016 * min(L, 4.0))

            def trop(t, p, d_):
                return Z * (wh["rise_per_m"] + wh["rise_tip_per_m"] * t * t)
            br = grow(f"whorl_{k}_{j}", 1, "whorl", trunk, s, d, L, r0, wh["r_end"], trop, wh["wobble"],
                      wh["step_m"], env, br_rng, collar=0.35)
            if br:
                out.append(br)
                whorl_branches.append(br)
        z += rng.uniform(*wh["spacing_m"])
        k += 1
    la = sp["lateral"]
    laterals = []
    for par in whorl_branches:
        def azim(crng, k):
            side = 1 if k % 2 else -1
            return side * crng.uniform(math.radians(55), math.radians(110))

        def max_len(crng, s, P, d, par=par):
            return min(la["max_len"], 0.55 * (par.length - s) + 0.35) * crng.uniform(0.75, 1.0)
        laterals += spawn_along(par, la, env, seed, 2, "lateral", out, la["from"] * par.length, par.length - 0.25,
                                azim, max_len)
    hosts = [(b, sp["foliage"]["branch_from"]) for b in whorl_branches] + \
            [(b, sp["foliage"]["twig_from"]) for b in laterals]
    return out, hosts, trunk


def build_dead(sp, env, seed):
    H = sp["height"]
    trunk = make_trunk(sp, H, sub_rng(seed, "trunk"))
    out = [trunk]
    add_stubs(trunk, sp, env, seed, out)
    jit = sp["limb_jitter"]
    limbs = []
    for i, (name, zf, az_deg, el_deg, L, bend, end) in enumerate(sp["limbs"]):
        crng = sub_rng(seed, "limb", name)
        az = math.radians(az_deg + crng.uniform(-jit["az_deg"], jit["az_deg"]))
        el = math.radians(el_deg + crng.uniform(-jit["elev_deg"], jit["elev_deg"]))
        d = Vector((math.cos(el) * math.cos(az), math.cos(el) * math.sin(az), math.sin(el)))
        L = L * (1.0 + crng.uniform(-jit["len"], jit["len"])) * env.reach_scale(d)
        s = trunk.s_at_z(zf * H)
        _, _, rt = trunk.frame_at(s)
        r0 = rt * crng.uniform(*sp["limb_ratio"])
        if name.startswith("stub"):
            r0 = min(r0, 0.09)

        def trop(t, p, d_, bend=bend):
            return Z * bend * (0.4 + 1.2 * t)
        r_end = sp["limb_r_end"] if end == "tip" else max(0.03, r0 * 0.45)
        br = grow(name, 1, "limb", trunk, s, d, L + rt, r0, r_end, trop, sp["limb_wobble"], sp["limb_step_m"],
                  env, crng, end=end, taper_k=1.1, collar=0.25, env_extra=0.4)
        if br:
            out.append(br)
            limbs.append(br)
    bs = sp["branch"]
    branches = []
    for par in limbs + [trunk]:
        if par.kind == "limb" and par.length < 1.4:
            continue
        crng = sub_rng(seed, "branches", par.name)
        count = crng.randint(*bs["per_limb"]) if par is not trunk else 3
        s_from = bs["from"] * par.length if par is not trunk else trunk.s_at_z(0.62 * H)
        s_to = par.length - 0.3
        for k in range(count):
            brng = sub_rng(seed, "branch", par.name, k)
            s = lerp(s_from, s_to, (k + brng.uniform(0.2, 0.8)) / count)
            P, T, rp = par.frame_at(s)
            az = brng.uniform(-1.0, 1.0) * math.radians(80) * (1.0 - bs["up_bias"]) + \
                (math.copysign(math.radians(25), brng.uniform(-1, 1)) if brng.random() < 0.5 else 0.0)
            if brng.random() < 0.25:
                az = math.copysign(math.radians(brng.uniform(70, 110)), brng.uniform(-1, 1))
            al = math.radians(brng.uniform(*bs["alpha_deg"]))
            d = child_dir(T, az, al)
            L = brng.uniform(*bs["len_m"])
            r0 = rp * brng.uniform(*bs["ratio"])
            if r0 < bs["min_r"]:
                continue
            end = "broken" if brng.random() < bs["broken"] else "tip"
            r_end = bs["r_end"] if end == "tip" else max(0.012, r0 * 0.5)
            br = grow(f"branch_{par.name}_{k}", 2, "branch", par, s, d, L, r0, r_end,
                      straight_trop(bs["rise_per_m"]), bs["wobble"], bs["step_m"], env, brng, end=end,
                      collar=0.25, env_extra=0.3)
            if br:
                out.append(br)
                branches.append(br)
    tw = sp["twig"]
    for par in branches:
        if par.end != "tip":
            continue
        crng = sub_rng(seed, "twigs", par.name)
        count = crng.randint(*tw["per_branch"])
        for k in range(count):
            trng = sub_rng(seed, "twig", par.name, k)
            s = lerp(tw["from"] * par.length, par.length - 0.15, trng.random())
            P, T, rp = par.frame_at(s)
            az = math.copysign(math.radians(trng.uniform(20, 100)), trng.uniform(-1, 1))
            d = child_dir(T, az, math.radians(trng.uniform(*tw["alpha_deg"])))
            r0 = rp * trng.uniform(*tw["ratio"])
            if r0 < tw["min_r"]:
                continue
            end = "broken" if trng.random() < tw["broken"] else "tip"
            r_end = tw["r_end"] if end == "tip" else max(0.008, r0 * 0.55)
            br = grow(f"twig_{par.name}_{k}", 3, "twig", par, s, d, trng.uniform(*tw["len_m"]), r0, r_end,
                      straight_trop(tw["rise_per_m"]), tw["wobble"], tw["step_m"], env, trng, end=end,
                      collar=0.2, env_extra=0.3)
            if br:
                out.append(br)
    return out, [], trunk


BUILDERS = {"oak": build_oak, "pine": build_pine, "dead": build_dead}


# --------------------------------------------------------------------------------------------------------------------
# Phase-A prototype presets: a trunk-only tube laid on its side (kind "log"), and a fixed-offset cluster of small
# trunks built once each and translated into place (kind "cluster" / "stump_cluster") - all reusing make_trunk,
# add_stubs, build_oak and plan_cards untouched.
# --------------------------------------------------------------------------------------------------------------------

def build_log(sp):
    """A single trunk-only tube (kind == 'log'): grown vertical like any trunk, then laid on its side by
    reorient_length_x after assembly. Both ends read organic (a torn root, a broken snap) rather than the flat
    foot cap a standing tree hides in the ground."""
    seed = sp["seed"]
    trunk = make_trunk(sp, sp["height"], sub_rng(seed, "trunk"), end=sp.get("tip_end", "tip"))
    trunk.root_broken = sp.get("root_broken", False)
    out = [trunk]
    add_stubs(trunk, sp, None, seed, out)
    return out


def reorient_length_x(b):
    """Cyclic axis permutation (x, y, z) -> (z, x, y): a proper rotation (determinant +1, so winding survives
    untouched) that carries a trunk's growth axis (built vertical along Blender Z) onto Blender X, so the glTF
    Z-up -> Y-up export leaves the long side on X. Then drops the mesh so its lowest point sits at Blender z = 0."""
    verts = [Vector((v.z, v.x, v.y)) for v in b.verts]
    low = min(v.z for v in verts)
    b.verts = [Vector((v.x, v.y, v.z - low)) for v in verts]


def build_cluster_stand(sp):
    """Three small crowned trees (build_oak, reused verbatim - a young ash is the same recipe at a smaller
    scale) at the stand's fixed footprint offsets. Each is grown and re-centred on its own trunk independently
    (the same iterative search main() runs for a standing tree), then translated into place."""
    seed = sp["seed"]
    branches_all, cards_all, instances = [], [], []
    for k, (dx, dy) in enumerate(sp["offsets"]):
        inst_seed = seed + 1009 * (k + 1)
        env = Envelope(sp["crown"], sp["height"], sub_rng(inst_seed, "envelope"))
        best = None
        for attempt in range(6):
            branches, hosts, trunk = build_oak(sp, env, inst_seed)
            cards = plan_cards(sp, hosts, trunk, env, inst_seed)
            xs = [p.x for br in branches for p in br.pts]
            ys = [p.y for br in branches for p in br.pts]
            off = ((min(xs) + max(xs)) / 2, (min(ys) + max(ys)) / 2)
            mag = math.hypot(*off)
            if best is None or mag < best[0]:
                best = (mag, branches, cards, trunk)
            if mag < 0.03:
                break
            env.cx -= off[0] * 0.9
            env.cy -= off[1] * 0.9
        _, branches, cards, trunk = best
        shift = Vector((dx, dy, 0.0))
        for br in branches:
            br.pts = [p + shift for p in br.pts]
        for c in cards:
            c["A"] = c["A"] + shift
        branches_all += branches
        cards_all += cards
        grown_z = max(p.z for br in branches for p in br.pts)
        instances.append({"offset_m": [dx, dy], "height_m": round(grown_z, 3),
                          "trunk_length_m": round(trunk.pts[-1].z, 3)})
    return branches_all, cards_all, instances


def build_cluster_stump(sp):
    """The same three footprint offsets, each a short trunk-only tube (no branches, no crown) with a flat,
    ring-cap top (br.end == 'cut') instead of the living stand's crown."""
    seed = sp["seed"]
    out, instances = [], []
    for k, (dx, dy) in enumerate(sp["offsets"]):
        trunk = make_trunk(sp, sp["height"], sub_rng(seed, "stump_trunk", k), end=sp.get("tip_end", "cut"))
        shift = Vector((dx, dy, 0.0))
        trunk.pts = [p + shift for p in trunk.pts]
        out.append(trunk)
        instances.append({"offset_m": [dx, dy], "height_m": round(trunk.pts[-1].z, 3)})
    return out, instances


# --------------------------------------------------------------------------------------------------------------------
# Foliage cards
# --------------------------------------------------------------------------------------------------------------------

def plan_cards(sp, hosts, trunk, env, seed):
    fo = sp["foliage"]
    cards = []
    if not fo:
        return cards
    H = sp["height"]
    for bi, (br, frac) in enumerate(hosts):
        rng = sub_rng(seed, "cards", br.name)
        stations = []
        s = max(frac * br.length, 0.12)
        while s < br.length - 0.12:
            stations.append(s)
            s += rng.uniform(*fo["spacing_m"])
        stations.append(max(0.0, br.length - 0.08))
        for k, s in enumerate(stations):
            P, T, _ = br.frame_at(s)
            if env.shell(P) < fo["min_shell"] and P.z < 0.8 * H:
                continue
            if not env.above_underside(P):
                continue
            out = env.outward(P, T)
            face = env.radial3(P)
            tip = k == len(stations) - 1
            for q in range(fo.get("per_station", 1)):
                crng = sub_rng(seed, "card", br.name, k, q)
                rnd = Vector((crng.uniform(-1, 1), crng.uniform(-1, 1), crng.uniform(-1, 1))) * (0.3 + 0.35 * q)
                D = (T + out * fo["outward"] + Z * fo["up"] + rnd).normalized()
                size = crng.uniform(*fo["card_m"])
                cards.append({"A": P, "D": D, "size": size, "out": face, "roll": crng.uniform(-1, 1),
                              "cell": crng.randrange(4), "prio": (0 if tip else 1 + q, crng.random()),
                              "br": br.level})
                if fo.get("clump"):
                    cards[-1]["host"] = br
    if fo.get("leader_m"):
        rng = sub_rng(seed, "leader_cards")
        s0 = trunk.length - fo["leader_m"]
        s = s0
        k = 0
        while s <= trunk.length - 0.05:
            P, T, _ = trunk.frame_at(s)
            crng = sub_rng(seed, "leader_card", k)
            ang = crng.uniform(0, TAU)
            side = Vector((math.cos(ang), math.sin(ang), 0.0))
            up_ness = smoothstep(s0, trunk.length, s)
            D = (Z * (0.6 + up_ness) + side * (1.0 - up_ness) * 0.9).normalized()
            cards.append({"A": P, "D": D, "size": crng.uniform(*fo["card_m"]), "out": side, "roll": crng.uniform(-1, 1),
                          "cell": crng.randrange(4), "prio": (0, crng.random()), "br": 0})
            s += 0.3
            k += 1
        P, T, _ = trunk.frame_at(trunk.length - 0.02)
        for k2 in range(2):
            crng = sub_rng(seed, "top_card", k2)
            cards.append({"A": P - Z * 0.15, "D": Z.copy(), "size": fo["card_m"][1], "out": X.copy(),
                          "roll": 0.5 * k2, "cell": crng.randrange(4), "prio": (0, 0.0), "br": 0})
    return cards


# --------------------------------------------------------------------------------------------------------------------
# Geometry: closed tubes and cards with UVs
# --------------------------------------------------------------------------------------------------------------------

class Builder:
    def __init__(self):
        self.verts, self.faces, self.fuv, self.fmat, self.fpart = [], [], [], [], []
        self.parts = []
        self.card_verts = []    # (first vertex, card) per placed card quad: the per-clump normals and the LODs read it

    def part(self, name, kind):
        self.parts.append({"name": name, "kind": kind, "f0": len(self.faces), "v0": len(self.verts)})

    def v(self, p):
        self.verts.append(Vector(p))
        return len(self.verts) - 1

    def f(self, idx, uv, mat):
        self.faces.append(tuple(idx))
        self.fuv.append(tuple((float(a), float(c)) for a, c in uv))
        self.fmat.append(mat)
        self.fpart.append(len(self.parts) - 1)


def rmf(pts):
    """Tangents and rotation-minimising normals (double reflection) along a polyline."""
    m = len(pts)
    T = []
    for i in range(m):
        if i == 0:
            d = pts[1] - pts[0]
        elif i == m - 1:
            d = pts[-1] - pts[-2]
        else:
            d = (pts[i + 1] - pts[i]).normalized() + (pts[i] - pts[i - 1]).normalized()
        T.append(d.normalized())
    ref = X if abs(T[0].x) < 0.9 else Y
    N = [(ref - T[0] * ref.dot(T[0])).normalized()]
    for i in range(1, m):
        v1 = pts[i] - pts[i - 1]
        c1 = v1.dot(v1)
        rL = N[-1] - v1 * (2.0 / c1 * v1.dot(N[-1]))
        tL = T[i - 1] - v1 * (2.0 / c1 * v1.dot(T[i - 1]))
        v2 = T[i] - tL
        c2 = v2.dot(v2)
        n = rL if c2 < 1e-12 else rL - v2 * (2.0 / c2 * v2.dot(rL))
        n = (n - T[i] * n.dot(T[i])).normalized()
        N.append(n)
    return T, N


def splinter_profile(rng, n, jag):
    out, prev = [], 0.0
    for _ in range(n):
        d = jag * (0.1 + 0.45 * rng.random() ** 2)
        if rng.random() < 0.3:
            d += jag * (0.45 + 0.55 * rng.random())
        d = 0.65 * d + 0.35 * prev
        out.append(d)
        prev = d
    return out


def tube(b, br, wood_tile):
    pts, rad, n = br.pts, br.rad, br.sides
    m = len(pts)
    T, N = rmf(pts)
    rng = random.Random(br.seed)
    b.part(br.name, br.kind)
    rings = []
    for i in range(m):
        Bv = T[i].cross(N[i])
        ring = []
        for j in range(n):
            th = TAU * j / n
            k = br.shape(pts[i].z, th) if br.shape else 1.0
            ring.append(pts[i] + (N[i] * math.cos(th) + Bv * math.sin(th)) * (rad[i] * k))
        rings.append(ring)
    jag = None
    if br.end == "broken":
        jag = splinter_profile(rng, n, 1.1 * rad[-1])
        rings[-1] = [p + T[-1] * jag[j] for j, p in enumerate(rings[-1])]
    jag0 = None
    if br.root_broken:
        jag0 = splinter_profile(rng, n, 1.1 * rad[0])
        rings[0] = [p - T[0] * jag0[j] for j, p in enumerate(rings[0])]
    # UVs: U = arc fraction round the ring (+ spiral twist), V = length / circumference
    circ = [sum((r[(j + 1) % n] - r[j]).length for j in range(n)) for r in rings]
    arcs = []
    for r, c in zip(rings, circ):
        a = [0.0]
        for j in range(n):
            a.append(a[-1] + (r[(j + 1) % n] - r[j]).length / c)
        arcs.append(a)
    V = [0.0]
    for i in range(1, m):
        V.append(V[-1] + (pts[i] - pts[i - 1]).length * 2.0 / (circ[i] + circ[i - 1]))
    idx = [[b.v(p) for p in r] for r in rings]

    def vv(i, j):
        if jag is not None and i == m - 1:
            return V[i] + jag[j % n] / circ[i]
        if jag0 is not None and i == 0:
            return V[i] - jag0[j % n] / circ[i]
        return V[i]
    for i in range(m - 1):
        for j in range(n):
            j1 = (j + 1) % n
            uv = [(arcs[i][j] + br.twist * V[i], vv(i, j)), (arcs[i][j + 1] + br.twist * V[i], vv(i, j + 1)),
                  (arcs[i + 1][j + 1] + br.twist * V[i + 1], vv(i + 1, j + 1)),
                  (arcs[i + 1][j] + br.twist * V[i + 1], vv(i + 1, j))]
            b.f((idx[i][j], idx[i][j1], idx[i + 1][j1], idx[i + 1][j]), uv, BARK)
    # start cap: flat fan facing -T (inside the parent, or the trunk's foot on the ground) - a jagged torn crater
    # instead when root_broken (a fallen trunk's root end, lying exposed rather than buried)
    B0 = T[0].cross(N[0])

    def planar(p, P, Nn, Bn):
        d = p - P
        return (d.dot(Nn) / wood_tile + 0.5, d.dot(Bn) / wood_tile + 0.5)
    if jag0 is None:
        c0 = b.v(pts[0])
        for j in range(n):
            j1 = (j + 1) % n
            b.f((c0, idx[0][j1], idx[0][j]), [planar(pts[0], pts[0], N[0], B0),
                                              planar(rings[0][j1], pts[0], N[0], B0),
                                              planar(rings[0][j], pts[0], N[0], B0)], WOOD)
    else:
        depth0 = 0.3 * rad[0]
        cen0 = pts[0] + T[0] * depth0 + (N[0] * rng.uniform(-0.2, 0.2) + B0 * rng.uniform(-0.2, 0.2)) * rad[0]
        ci0 = b.v(cen0)
        for j in range(n):
            j1 = (j + 1) % n
            b.f((ci0, idx[0][j1], idx[0][j]), [planar(cen0, pts[0], N[0], B0),
                                               planar(rings[0][j1], pts[0], N[0], B0),
                                               planar(rings[0][j], pts[0], N[0], B0)], WOOD)
    Bl = T[-1].cross(N[-1])
    if br.end == "tip":
        apex = b.v(pts[-1] + T[-1] * max(2.0 * rad[-1], 0.012))
        va = V[-1] + max(2.0 * rad[-1], 0.012) / circ[-1]
        for j in range(n):
            j1 = (j + 1) % n
            ua = 0.5 * (arcs[-1][j] + arcs[-1][j + 1]) + br.twist * V[-1]
            b.f((idx[-1][j], idx[-1][j1], apex), [(arcs[-1][j] + br.twist * V[-1], V[-1]),
                                                  (arcs[-1][j + 1] + br.twist * V[-1], V[-1]), (ua, va)], BARK)
    elif br.end == "cut":
        # a flat sawn/snapped-off top (a stump): same flat-fan shape as the root foot, at the tip instead
        c1 = b.v(pts[-1])
        for j in range(n):
            j1 = (j + 1) % n
            b.f((idx[-1][j], idx[-1][j1], c1), [planar(rings[-1][j], pts[-1], N[-1], Bl),
                                                planar(rings[-1][j1], pts[-1], N[-1], Bl),
                                                planar(pts[-1], pts[-1], N[-1], Bl)], WOOD)
    else:
        depth = 0.3 * rad[-1]
        cen = pts[-1] - T[-1] * depth + (N[-1] * rng.uniform(-0.2, 0.2) + Bl * rng.uniform(-0.2, 0.2)) * rad[-1]
        ci = b.v(cen)
        for j in range(n):
            j1 = (j + 1) % n
            b.f((ci, idx[-1][j], idx[-1][j1]), [planar(cen, pts[-1], N[-1], Bl),
                                                 planar(rings[-1][j], pts[-1], N[-1], Bl),
                                                 planar(rings[-1][j1], pts[-1], N[-1], Bl)], WOOD)


def card_geo(b, c, cells, bend_axis):
    """Two single-sided quads back to back; the sprite's twig enters at the bottom centre (V = 0 edge)."""
    A, D, size = c["A"], c["D"], c["size"]
    n0 = c["n"]
    n0 = (n0 - D * n0.dot(D)).normalized()
    S = D.cross(n0)
    w = h = size
    drop = 0.07
    q = [A - S * (w / 2) - D * (h * drop), A + S * (w / 2) - D * (h * drop),
         A + S * (w / 2) + D * (h * (1 - drop)), A - S * (w / 2) + D * (h * (1 - drop))]
    u0, v0, u1, v1 = cells[c["cell"]]
    uv = [(u0, v0), (u1, v0), (u1, v1), (u0, v1)]
    b.part("card", "card")
    b.card_verts.append((len(b.verts), c))
    fr = [b.v(p) for p in q]
    b.f(fr, uv, LEAF)
    bk = [b.v(p) for p in q]
    b.f([bk[0], bk[3], bk[2], bk[1]], [uv[0], uv[3], uv[2], uv[1]], LEAF)


def card_normals(c, crossed, roll=0.5):
    """Plane normals for a card (or a crossed pair): facing outward, rolled about the card's axis (up to `roll` rad)."""
    D, out = c["D"], c["out"]
    base = out - D * out.dot(D)
    if base.length < 1e-4:
        base = Z - D * Z.dot(D)
    if base.length < 1e-4:
        base = X - D * X.dot(D)
    base.normalize()
    side = D.cross(base)
    if crossed:
        up = Z - D * Z.dot(D)
        if up.length < 1e-3:
            up = base
        up.normalize()
        rot = 0.35 * c["roll"]
        a = (up * math.cos(rot) + D.cross(up) * math.sin(rot)).normalized()
        return [a, D.cross(a).normalized()]
    rot = roll * c["roll"]
    return [(base * math.cos(rot) + side * math.sin(rot)).normalized()]


# --------------------------------------------------------------------------------------------------------------------
# Validation
# --------------------------------------------------------------------------------------------------------------------

def signed_volume(b, f0, f1):
    vol = 0.0
    for fi in range(f0, f1):
        idx = b.faces[fi]
        p0 = b.verts[idx[0]]
        for k in range(1, len(idx) - 1):
            vol += p0.dot(b.verts[idx[k]].cross(b.verts[idx[k + 1]])) / 6.0
    return vol


def validate_builder(b):
    """Per bark part: closed (every edge used twice, in opposite directions), positive volume; no bow-tie quads, no
    degenerate faces. Foliage cards are sheets and are checked for degenerate faces only."""
    rep = {"solid_parts": 0, "open_edges": 0, "misoriented_edges": 0, "negative_volume_parts": [],
           "bowtie_quads": 0, "degenerate_faces": 0, "card_parts": 0}
    bounds = [p["f0"] for p in b.parts] + [len(b.faces)]
    for pi, part in enumerate(b.parts):
        f0, f1 = bounds[pi], bounds[pi + 1]
        if part["kind"] == "card":
            rep["card_parts"] += 1
            continue
        rep["solid_parts"] += 1
        directed = {}
        for fi in range(f0, f1):
            idx = b.faces[fi]
            for k in range(len(idx)):
                e = (idx[k], idx[(k + 1) % len(idx)])
                directed[e] = directed.get(e, 0) + 1
        for (a, c), cnt in directed.items():
            if cnt > 1:
                rep["misoriented_edges"] += 1
            if (c, a) not in directed:
                rep["open_edges"] += 1
        if signed_volume(b, f0, f1) <= 0:
            rep["negative_volume_parts"].append(part["name"])
    for idx in b.faces:
        P = [b.verts[i] for i in idx]
        n = Vector((0, 0, 0))
        for k in range(len(P)):
            n += P[k].cross(P[(k + 1) % len(P)])
        if n.length < 1e-10:
            rep["degenerate_faces"] += 1
            continue
        if len(P) == 4:
            ok = all(((P[a] - P[o]).cross(P[c] - P[o])).dot(n) > 0
                     for o, a, c in ((0, 1, 2), (0, 2, 3), (0, 1, 3), (1, 2, 3)))
            if not ok:
                rep["bowtie_quads"] += 1
    return rep


# --------------------------------------------------------------------------------------------------------------------
# Mesh object
# --------------------------------------------------------------------------------------------------------------------

def make_object(b, name):
    me = bpy.data.meshes.new(name)
    me.from_pydata([tuple(v) for v in b.verts], [], [list(f) for f in b.faces])
    me.update()
    uv = me.uv_layers.new(name="UVMap")
    uv.data.foreach_set("uv", np.array([c for f in b.fuv for p in f for c in p], dtype=np.float32))
    me.polygons.foreach_set("material_index", np.array(b.fmat, dtype=np.int32))
    fol = me.attributes.new("fol", "INT", "FACE")
    fol.data.foreach_set("value", np.array([1 if m == LEAF else 0 for m in b.fmat], dtype=np.int32))
    obj = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(obj)
    return obj


def select_only(obj):
    for o in bpy.context.scene.objects:
        o.select_set(False)
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj


def finish_topology(obj, crown_c, crown_ax, bend, targets=None):
    """Triangulate; smooth bark with sharp caps; foliage corners bent toward the crown's outward normal (or, where
    `targets` - unit vectors per vertex, zero where unset - gives one, toward that direction instead)."""
    me = obj.data
    bm = bmesh.new()
    bm.from_mesh(me)
    bmesh.ops.triangulate(bm, faces=bm.faces[:], quad_method="BEAUTY", ngon_method="BEAUTY")
    bm.to_mesh(me)
    bm.free()
    me.shade_smooth()
    me.set_sharp_from_angle(angle=math.radians(80.0))
    nl = len(me.loops)
    cn = np.empty(nl * 3, dtype=np.float32)
    me.corner_normals.foreach_get("vector", cn)
    cn = cn.reshape(-1, 3)
    fol = np.empty(len(me.polygons), dtype=np.int32)
    me.attributes["fol"].data.foreach_get("value", fol)
    if fol.any():
        lt = np.empty(len(me.polygons), dtype=np.int32)
        me.polygons.foreach_get("loop_total", lt)
        fn = np.empty(len(me.polygons) * 3, dtype=np.float32)
        me.polygons.foreach_get("normal", fn)
        fn = fn.reshape(-1, 3)
        lv = np.empty(nl, dtype=np.int32)
        me.loops.foreach_get("vertex_index", lv)
        co = np.empty(len(me.vertices) * 3, dtype=np.float32)
        me.vertices.foreach_get("co", co)
        co = co.reshape(-1, 3)
        lf = np.repeat(np.arange(len(me.polygons)), lt)
        sel = fol[lf] == 1
        p = co[lv[sel]]
        g = (p - np.array(crown_c, dtype=np.float32)) / (np.array(crown_ax, dtype=np.float32) ** 2)
        g /= np.maximum(np.linalg.norm(g, axis=1, keepdims=True), 1e-6)
        if targets is not None:
            t = np.asarray(targets, dtype=np.float32)[lv[sel]]
            has = np.linalg.norm(t, axis=1) > 0.5
            g[has] = t[has]
        mix = fn[lf[sel]] * (1.0 - bend) + g * bend
        mix /= np.maximum(np.linalg.norm(mix, axis=1, keepdims=True), 1e-6)
        cn[sel] = mix
    me.normals_split_custom_set(cn.tolist())
    me.update()


def card_group(c, level):
    """The branch whose spray a card belongs to for per-clump shading: its host's ancestor at `level`."""
    br = c.get("host")
    if br is None:
        return "leader"
    while br.parent is not None and br.level > level:
        br = br.parent
    return br.name


def clump_targets(b, clump, crown_c, crown_ax):
    """Per-vertex normal targets for the cards (foliage.clump): each branch's spray is a rounded mass whose normals
    point away from the spray's centre, mixed with the whole crown's outward normal, so the crown shades as layered
    lobes. A spray of fewer than min_cards joins its parent's. Unit vectors per vertex; zero off the cards."""
    level, mix, min_cards = clump["group_level"], clump["mix"], clump.get("min_cards", 4)
    verts = np.array([tuple(v) for v in b.verts], dtype=np.float64)
    groups = {}
    for start, c in b.card_verts:
        groups.setdefault(card_group(c, level), []).append((start, c))
    if level > 1:
        for key in [k for k, v in groups.items() if len(v) < min_cards and k != "leader"]:
            members = groups.pop(key)
            up = card_group(members[0][1], level - 1)
            groups.setdefault(up, []).extend(members)
    targets = np.zeros_like(verts)
    cc, ax = np.array(crown_c), np.array(crown_ax)
    for members in groups.values():
        centres = np.array([tuple(c["A"] + c["D"] * (c["size"] * 0.43)) for _, c in members])
        C = centres.mean(axis=0)
        idx = np.concatenate([np.arange(s, s + 8) for s, _ in members])
        p = verts[idx]
        d = p - C
        dl = np.linalg.norm(d, axis=1, keepdims=True)
        g = (p - cc) / ax ** 2
        g /= np.maximum(np.linalg.norm(g, axis=1, keepdims=True), 1e-9)
        d = np.where(dl > 1e-3, d / np.maximum(dl, 1e-9), g)
        t = d * mix + g * (1.0 - mix)
        targets[idx] = t / np.maximum(np.linalg.norm(t, axis=1, keepdims=True), 1e-9)
    return targets


def mesh_checks(me, res):
    """Degenerate 3D and zero-area UV triangles on the final mesh; bark texel density (px/m) percentiles."""
    nf = len(me.polygons)
    lv = np.empty(len(me.loops), dtype=np.int32)
    me.loops.foreach_get("vertex_index", lv)
    co = np.empty(len(me.vertices) * 3, dtype=np.float64)
    me.vertices.foreach_get("co", co)
    co = co.reshape(-1, 3)
    uv = np.empty(len(me.loops) * 2, dtype=np.float64)
    me.uv_layers["UVMap"].data.foreach_get("uv", uv)
    uv = uv.reshape(-1, 2)
    ls = np.empty(nf, dtype=np.int32)
    me.polygons.foreach_get("loop_start", ls)
    mi = np.empty(nf, dtype=np.int32)
    me.polygons.foreach_get("material_index", mi)
    a, b_, c = ls, ls + 1, ls + 2
    p0, p1, p2 = co[lv[a]], co[lv[b_]], co[lv[c]]
    area3 = 0.5 * np.linalg.norm(np.cross(p1 - p0, p2 - p0), axis=1)
    u0, u1, u2 = uv[a], uv[b_], uv[c]
    e1, e2 = u1 - u0, u2 - u0
    area2 = 0.5 * np.abs(e1[:, 0] * e2[:, 1] - e1[:, 1] * e2[:, 0])
    out = {"triangles": int(nf), "degenerate_triangles": int((area3 < 1e-10).sum()),
           "zero_area_uv_triangles": int((area2 < 1e-12).sum())}
    sel = (mi == BARK) & (area3 > 1e-8)
    if sel.any():
        dens = np.sqrt(area2[sel] / area3[sel]) * res
        w = area3[sel]
        order = np.argsort(dens)
        cw = np.cumsum(w[order]) / w.sum()

        def pct(q):
            return float(dens[order][min(len(order) - 1, np.searchsorted(cw, q))])
        out["bark_px_per_m_area_weighted"] = {"p1": round(pct(0.01), 1), "p5": round(pct(0.05), 1),
                                             "median": round(pct(0.5), 1), "p95": round(pct(0.95), 1)}
        out["bark_area_m2"] = round(float(w.sum()), 2)
    return out


# --------------------------------------------------------------------------------------------------------------------
# Baked procedural shaders (seamless tiles)
# --------------------------------------------------------------------------------------------------------------------

def lin(r, g, b_):
    def f(c):
        c /= 255.0
        return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4
    return (f(r), f(g), f(b_))


class Val:
    __slots__ = ("g", "s", "k")

    def __init__(self, g, s, k):
        self.g, self.s, self.k = g, s, k

    def __add__(self, o): return self.g.op("ADD", self, o)
    def __radd__(self, o): return self.g.op("ADD", o, self)
    def __sub__(self, o): return self.g.op("SUBTRACT", self, o)
    def __rsub__(self, o): return self.g.op("SUBTRACT", o, self)
    def __mul__(self, o): return self.g.op("MULTIPLY", self, o)
    def __rmul__(self, o): return self.g.op("MULTIPLY", o, self)
    def __neg__(self): return self.g.op("MULTIPLY", self, -1.0)


def kind(x):
    if isinstance(x, Val):
        return x.k
    return "v" if isinstance(x, (tuple, list)) else "f"


class Graph:
    def __init__(self, tree):
        self.t = tree

    def node(self, typ, **kw):
        n = self.t.nodes.new(typ)
        for k, v in kw.items():
            setattr(n, k, v)
        return n

    def put(self, sock, x):
        if isinstance(x, Val):
            self.t.links.new(x.s, sock)
            return
        if isinstance(x, (tuple, list)):
            x = tuple(float(c) for c in x)
            if len(sock.default_value) == 4 and len(x) == 3:
                x = x + (1.0,)
            sock.default_value = x
            return
        try:
            sock.default_value = float(x)
        except TypeError:
            n = len(sock.default_value)
            sock.default_value = (float(x),) * 3 + ((1.0,) if n == 4 else ())

    def op(self, name, a, c):
        if kind(a) == "f" and kind(c) == "f":
            n = self.node("ShaderNodeMath", operation=name)
        else:
            n = self.node("ShaderNodeVectorMath", operation=name)
        self.put(n.inputs[0], a)
        self.put(n.inputs[1], c)
        return Val(self, n.outputs[0], "f" if kind(a) == "f" and kind(c) == "f" else "v")

    def m(self, name, a, c=0.0, clamp=False):
        n = self.node("ShaderNodeMath", operation=name, use_clamp=clamp)
        self.put(n.inputs[0], a)
        self.put(n.inputs[1], c)
        return Val(self, n.outputs[0], "f")

    def abs(self, a): return self.m("ABSOLUTE", a)
    def max(self, a, c): return self.m("MAXIMUM", a, c)
    def min(self, a, c): return self.m("MINIMUM", a, c)
    def sat(self, a): return self.m("ADD", a, 0.0, clamp=True)
    def fract(self, a): return self.m("FRACT", a)

    def smooth(self, e0, e1, x):
        t = self.sat((x - e0) * (1.0 / (e1 - e0)))
        return t * t * (3.0 - 2.0 * t)

    def mix(self, a, c, t):
        return a + (c - a) * t if not (isinstance(a, tuple) and isinstance(c, tuple)) else \
            a + tuple(ci - ai for ai, ci in zip(a, c)) * t

    def vec(self, x, y, z):
        n = self.node("ShaderNodeCombineXYZ")
        for i, c in enumerate((x, y, z)):
            self.put(n.inputs[i], c)
        return Val(self, n.outputs[0], "v")

    def sep(self, v):
        n = self.node("ShaderNodeSeparateXYZ")
        self.put(n.inputs[0], v)
        return tuple(Val(self, n.outputs[i], "f") for i in range(3))

    def noise(self, t4, detail=2.0, rough=0.5):
        v, w = t4
        n = self.node("ShaderNodeTexNoise", noise_dimensions="4D", noise_type="FBM", normalize=True)
        self.put(n.inputs["Vector"], v)
        self.put(n.inputs["W"], w)
        self.put(n.inputs["Scale"], 1.0)
        self.put(n.inputs["Detail"], detail)
        self.put(n.inputs["Roughness"], rough)
        return Val(self, n.outputs[0], "f")

    def voronoi(self, t4, feature="F1", jitter=1.0):
        v, w = t4
        n = self.node("ShaderNodeTexVoronoi", voronoi_dimensions="4D", feature=feature)
        self.put(n.inputs["Vector"], v)
        self.put(n.inputs["W"], w)
        self.put(n.inputs["Scale"], 1.0)
        self.put(n.inputs["Randomness"], jitter)
        if feature == "DISTANCE_TO_EDGE":
            return Val(self, n.outputs["Distance"], "f"), None
        return Val(self, n.outputs["Distance"], "f"), Val(self, n.outputs["Color"], "v")

    def ramp(self, t, stops):
        n = self.node("ShaderNodeValToRGB")
        els = n.color_ramp.elements
        while len(els) < len(stops):
            els.new(0.5)
        for e, (pos, col) in zip(els, stops):
            e.position = pos
            e.color = tuple(col) + (1.0,)
        self.put(n.inputs[0], t)
        return Val(self, n.outputs[0], "v")


class Torus:
    """Tile coordinates (u around, v along the grain) on a flat torus in 4D, so every pattern repeats at the tile
    edges: features across = count_u, along = count_v (per tile), optionally warped round the ring."""

    def __init__(self, g):
        self.g = g
        tc = g.node("ShaderNodeTexCoord")
        self.u, self.v, _ = g.sep(Val(g, tc.outputs["UV"], "v"))
        self.cache = {}

    def trig(self, x):
        key = "u" if x is self.u else "v" if x is self.v else None
        if key in self.cache:
            return self.cache[key]
        a = x * TAU
        out = (self.g.m("COSINE", a), self.g.m("SINE", a))
        if key:
            self.cache[key] = out
        return out

    def at(self, count_u, count_v, du=None, dv=None, offset=0.0):
        g = self.g
        u = self.u if du is None else self.u + du
        v = self.v if dv is None else self.v + dv
        cu, su = self.trig(u)
        cv, sv = self.trig(v)
        ru, rv = count_u / TAU, count_v / TAU
        return (g.vec(cu * ru + offset, su * ru + offset * 0.7, cv * rv + offset * 0.3), sv * rv + offset * 0.5)


def sh_bark_oak(g, t, moss_boost=0.0):
    """moss_boost (0 default = the original look, unchanged) widens the moss/lichen coverage for the heavily
    mossed presets (the fallen log, the Woundmoss beam) without touching the baseline oak look."""
    warp = g.noise(t.at(3.0, 4.0, offset=11.0), detail=3.0) - 0.5
    warp2 = g.noise(t.at(9.0, 6.0, offset=23.0), detail=2.0) - 0.5
    du = warp * 0.045 + warp2 * 0.012
    edge, _ = g.voronoi(t.at(17.0, 2.2, du=du, offset=3.0), "DISTANCE_TO_EDGE")
    edge2, _ = g.voronoi(t.at(50.0, 8.0, du=du, offset=7.0), "DISTANCE_TO_EDGE")
    plate = g.smooth(0.02, 0.17, edge)
    flake = g.smooth(0.0, 0.12, edge2)
    fine = g.noise(t.at(160.0, 60.0, du=du, offset=5.0), detail=3.0)
    tone = g.noise(t.at(6.0, 2.0, offset=17.0), detail=3.0)
    moss = g.smooth(0.6 - 0.42 * moss_boost, 0.72 - 0.1 * moss_boost, g.noise(t.at(4.0, 1.5, offset=31.0), detail=4.0))
    lich = (1.0 - g.smooth(0.03, 0.09 - 0.04 * moss_boost, g.voronoi(t.at(90.0, 70.0, offset=41.0))[0])) * \
        g.smooth(0.55 - 0.28 * moss_boost, 0.7 - 0.12 * moss_boost, g.noise(t.at(5.0, 3.0, offset=43.0), detail=2.0))
    base = g.ramp(tone, [(0.0, lin(70, 57, 45)), (0.5, lin(92, 76, 60)), (1.0, lin(112, 96, 78))])
    base = base * (0.8 + fine * 0.4)
    base = g.mix(base, lin(136, 124, 106), plate * flake * g.smooth(0.45, 0.8, fine) * 0.55)
    base = g.mix(base, lin(30, 23, 18), (1.0 - plate) * 0.9)
    base = g.mix(base, lin(84, 92, 52), moss * plate * (0.55 + 0.65 * moss_boost))
    base = g.mix(base, lin(140, 146, 122), lich * plate * (0.7 + 0.5 * moss_boost))
    rough = 0.86 + (1.0 - plate) * 0.08 - flake * 0.04 + moss * 0.04
    height = plate * 0.016 + flake * 0.0035 + fine * 0.002 + moss * 0.0015
    occ = 0.35 + 0.65 * g.smooth(0.0, 0.2, edge) * (0.8 + 0.2 * flake)
    return {"base": base, "rough": rough, "metal": 0.0, "height": height, "occ": occ}


def sh_bark_pine(g, t):
    warp = g.noise(t.at(3.0, 2.0, offset=13.0), detail=3.0) - 0.5
    du = warp * 0.035
    edge, col = g.voronoi(t.at(17.0, 6.0, du=du, offset=2.0), "F1")
    border, _ = g.voronoi(t.at(17.0, 6.0, du=du, offset=2.0), "DISTANCE_TO_EDGE")
    plate = g.smooth(0.03, 0.2, border)
    cr, cg, _ = g.sep(col)
    layers = g.fract(g.noise(t.at(30.0, 12.0, du=du, offset=19.0), detail=2.0) * 3.0 + cr)
    step = g.smooth(0.0, 0.15, layers) * g.smooth(1.0, 0.85, layers)
    fine = g.noise(t.at(140.0, 50.0, du=du, offset=29.0), detail=3.0)
    grey = g.smooth(0.45, 0.65, g.noise(t.at(3.0, 1.2, offset=37.0), detail=3.0))
    base = g.ramp(cg, [(0.0, lin(104, 64, 44)), (0.5, lin(128, 78, 50)), (1.0, lin(150, 94, 60))])
    base = g.mix(base, lin(180, 118, 76), (1.0 - step) * plate * 0.45)
    base = base * (0.82 + fine * 0.36)
    base = g.mix(base, lin(82, 72, 64), grey * 0.6)
    base = g.mix(base, lin(38, 27, 21), (1.0 - plate) * 0.92)
    rough = 0.84 + (1.0 - plate) * 0.1
    height = plate * 0.018 + layers * 0.003 + fine * 0.0015
    occ = 0.3 + 0.7 * g.smooth(0.0, 0.22, border)
    return {"base": base, "rough": rough, "metal": 0.0, "height": height, "occ": occ}


def sh_bark_dead(g, t):
    warp = g.noise(t.at(4.0, 1.5, offset=5.0), detail=3.0) - 0.5
    du = warp * 0.04
    crack, _ = g.voronoi(t.at(26.0, 2.2, du=du, offset=9.0), "DISTANCE_TO_EDGE")
    cracks = 1.0 - g.smooth(0.0, 0.05, crack)
    fibre = g.noise(t.at(150.0, 6.0, du=du, offset=15.0), detail=3.0)
    tone = g.noise(t.at(5.0, 2.0, offset=21.0), detail=4.0)
    dots, dcol = g.voronoi(t.at(70.0, 70.0, offset=27.0), "F1")
    dr, dg, db = g.sep(dcol)
    dot = (1.0 - g.smooth(0.12, 0.2, dots)) * g.smooth(0.55, 0.6, dr)
    dark = g.smooth(0.85, 0.9, dg) * (1.0 - g.smooth(0.08, 0.14, dots))
    base = g.ramp(tone, [(0.0, lin(50, 49, 47)), (0.5, lin(68, 67, 64)), (1.0, lin(90, 88, 84))])
    base = base * (0.84 + fibre * 0.32)
    base = g.mix(base, lin(100, 88, 74), g.smooth(0.6, 0.75, g.noise(t.at(3.0, 1.0, offset=33.0))) * 0.35)
    base = g.mix(base, lin(176, 174, 166), dot * 0.8)
    base = g.mix(base, lin(46, 44, 41), dark * 0.8)
    base = g.mix(base, lin(34, 32, 30), cracks * 0.9)
    rough = 0.8 + cracks * 0.12 + fibre * 0.05
    height = fibre * 0.0025 - cracks * 0.006 + dot * 0.0008 + tone * 0.002
    occ = 1.0 - cracks * 0.55
    return {"base": base, "rough": rough, "metal": 0.0, "height": height, "occ": occ}


def sh_wood(g, t, w):
    fib = g.noise(t.at(120.0, 4.0, offset=3.0), detail=4.0)
    fib2 = g.noise(t.at(300.0, 10.0, offset=7.0), detail=2.0)
    patch = g.smooth(0.45, 0.6, g.noise(t.at(3.0, 3.0, offset=11.0), detail=3.0))
    base = g.mix(lin(*w["fresh"]), lin(*w["weathered"]), patch * w["grey"] * 2.0)
    base = base * (0.72 + fib * 0.45 + fib2 * 0.12)
    rough = 0.82 + fib * 0.08
    height = fib * 0.0025 + fib2 * 0.0008
    return {"base": base, "rough": rough, "metal": 0.0, "height": height, "occ": 0.75 + fib * 0.25}


def sh_wood_rings(g, t, w):
    """sh_wood plus concentric growth rings: the WOOD material's cap UV is planar, centred on the pith (see
    tube()'s planar()), so radial distance in that UV is physical radial distance from the pith / wood_tile - a
    stump's fresh-cut top (br.end == "cut") reads this as real rings, unlike the swept sides (always BARK)."""
    res_ = sh_wood(g, t, w)
    du, dv = t.u - 0.5, t.v - 0.5
    radius = g.m("SQRT", du * du + dv * dv)
    jitter = g.noise(t.at(40.0, 40.0, offset=53.0), detail=2.0) - 0.5
    f = g.fract(radius * (1.0 / 0.016) + jitter * 0.6)
    band = g.smooth(0.55, 0.82, f) * (1.0 - g.smooth(0.92, 0.995, f))
    base = g.mix(res_["base"], lin(104, 78, 48), band * 0.55)
    return {"base": base, "rough": res_["rough"] + band * 0.03, "metal": 0.0,
            "height": res_["height"] + band * 0.0012, "occ": res_.get("occ", 0.8)}


BARK_SHADERS = {"oak": sh_bark_oak, "pine": sh_bark_pine, "dead": sh_bark_dead}
WOOD_SHADERS = {"plain": sh_wood, "rings": sh_wood_rings}


def set_device(scene, device):
    scene.render.engine = "CYCLES"
    prefs = bpy.context.preferences.addons["cycles"].preferences
    if device != "cpu":
        for dt in ("OPTIX", "CUDA"):
            try:
                prefs.compute_device_type = dt
                prefs.get_devices()
                if any(d.type == dt for d in prefs.devices):
                    for d in prefs.devices:
                        d.use = d.type == dt
                    scene.cycles.device = "GPU"
                    return dt
            except TypeError:
                continue
    prefs.compute_device_type = "NONE"
    scene.cycles.device = "CPU"
    return "CPU"


def bake_tile(name, shader, size_m, res, samples, device_used):
    """Bake one procedural shader onto a plane of the tile's physical size: colour, ORM and normal arrays."""
    scene = bpy.context.scene
    me = bpy.data.meshes.new("_tile_" + name)
    me.from_pydata([(0, 0, 0), (size_m, 0, 0), (size_m, size_m, 0), (0, size_m, 0)], [], [[0, 1, 2, 3]])
    me.update()
    uvl = me.uv_layers.new(name="UVMap")
    uvl.data.foreach_set("uv", [0, 0, 1, 0, 1, 1, 0, 1])
    obj = bpy.data.objects.new("_tile_" + name, me)
    scene.collection.objects.link(obj)
    img = bpy.data.images.new("_bake_" + name, res, res, alpha=False, float_buffer=True)
    img.colorspace_settings.name = "Non-Color"
    mat = bpy.data.materials.new("_bake_" + name)
    tree = mat.node_tree
    for n in list(tree.nodes):
        tree.nodes.remove(n)
    g = Graph(tree)
    t = Torus(g)
    res_ = shader(g, t)
    out = g.node("ShaderNodeOutputMaterial")
    e_col = g.node("ShaderNodeEmission")
    g.put(e_col.inputs["Color"], res_["base"])
    e_orm = g.node("ShaderNodeEmission")
    g.put(e_orm.inputs["Color"], g.vec(g.sat(res_["occ"]), g.sat(res_["rough"]), g.sat(res_["metal"])))
    bump = g.node("ShaderNodeBump")
    g.put(bump.inputs["Strength"], 1.0)
    g.put(bump.inputs["Distance"], 1.0)
    g.put(bump.inputs["Filter Width"], 1.0)
    g.put(bump.inputs["Height"], res_["height"])
    diffuse = g.node("ShaderNodeBsdfDiffuse")
    tree.links.new(bump.outputs["Normal"], diffuse.inputs["Normal"])
    inode = g.node("ShaderNodeTexImage")
    inode.image = img
    tree.nodes.active = inode
    me.materials.append(mat)
    select_only(obj)
    arrays = {}
    for pass_, sock in (("colour", e_col.outputs[0]), ("orm", e_orm.outputs[0]), ("normal", diffuse.outputs[0])):
        for link in list(out.inputs["Surface"].links):
            tree.links.remove(link)
        tree.links.new(sock, out.inputs["Surface"])
        for attempt in range(4):
            try:
                if pass_ == "normal":
                    bpy.ops.object.bake(type="NORMAL", normal_space="TANGENT", normal_r="POS_X", normal_g="POS_Y",
                                        normal_b="POS_Z", margin=0, use_clear=True, target="IMAGE_TEXTURES",
                                        uv_layer="UVMap")
                else:
                    bpy.ops.object.bake(type="EMIT", margin=0, use_clear=True, target="IMAGE_TEXTURES",
                                        uv_layer="UVMap")
                break
            except RuntimeError as err:
                print(f"  bake {name}/{pass_} failed ({str(err)[:120]}), attempt {attempt + 1}", flush=True)
                if attempt >= 1 and scene.cycles.device == "GPU":
                    set_device(scene, "cpu")
                    device_used.add("CPU")
                time.sleep(5 + 10 * attempt)
        else:
            raise RuntimeError(f"bake {name}/{pass_} failed four times")
        px = np.empty(res * res * 4, dtype=np.float32)
        img.pixels.foreach_get(px)
        arrays[pass_] = px.reshape(res, res, 4)[:, :, :3].copy()
    bpy.data.objects.remove(obj)
    bpy.data.images.remove(img)
    return arrays


def to_srgb(x):
    x = np.clip(x, 0.0, 1.0)
    return np.where(x <= 0.0031308, x * 12.92, 1.055 * np.power(x, 1 / 2.4) - 0.055)


def save_png(arr, path, colourspace, alpha=None):
    h, w = arr.shape[:2]
    img = bpy.data.images.new(os.path.basename(path), w, h, alpha=alpha is not None)
    img.colorspace_settings.name = colourspace
    rgba = np.ones((h, w, 4), dtype=np.float32)
    rgba[:, :, :3] = np.clip(arr, 0.0, 1.0)
    if alpha is not None:
        rgba[:, :, 3] = np.clip(alpha, 0.0, 1.0)
        img.alpha_mode = "CHANNEL_PACKED"
    img.pixels.foreach_set(rgba.ravel())
    img.filepath_raw = path
    img.file_format = "PNG"
    img.save()
    bpy.data.images.remove(img)


def seam_error(arr):
    """Mean step across the wrap (last column to first, last row to first) over the mean step inside the tile."""
    inner = np.abs(np.diff(arr, axis=1)).mean() + np.abs(np.diff(arr, axis=0)).mean()
    wrap = np.abs(arr[:, 0] - arr[:, -1]).mean() + np.abs(arr[0] - arr[-1]).mean()
    return float(wrap / max(inner, 1e-9))


# --------------------------------------------------------------------------------------------------------------------
# Foliage atlas (drawn in numpy)
# --------------------------------------------------------------------------------------------------------------------

class Canvas:
    """One sprite cell, supersampled; y up (row 0 = V 0); positions in cell units 0..1."""

    def __init__(self, R):
        self.R = R
        self.col = np.zeros((R, R, 3), np.float32)
        self.a = np.zeros((R, R), np.float32)
        self.n = np.zeros((R, R, 3), np.float32)
        self.n[..., 2] = 1.0
        self.o = np.ones((R, R), np.float32)
        self.r = np.full((R, R), 0.6, np.float32)

    def region(self, x0, y0, x1, y1):
        R = self.R
        c0, c1 = max(0, int(math.floor(x0 * R))), min(R, int(math.ceil(x1 * R)) + 1)
        r0, r1 = max(0, int(math.floor(y0 * R))), min(R, int(math.ceil(y1 * R)) + 1)
        if c0 >= c1 or r0 >= r1:
            return None
        xs = (np.arange(c0, c1, dtype=np.float32) + 0.5) / R
        ys = (np.arange(r0, r1, dtype=np.float32) + 0.5) / R
        Xg, Yg = np.meshgrid(xs, ys)
        return (slice(r0, r1), slice(c0, c1)), Xg, Yg

    def put(self, sl, m, col, nrm, occ, rough):
        self.a[sl][m] = 1.0
        self.col[sl][m] = col[m]
        self.n[sl][m] = nrm[m]
        self.o[sl][m] = occ if np.isscalar(occ) else occ[m]
        self.r[sl][m] = rough


def _norm3(nx, ny, nz):
    ln = np.sqrt(nx * nx + ny * ny + nz * nz) + 1e-9
    return np.stack([nx / ln, ny / ln, nz / ln], axis=-1)


def stamp_segment(cv, a, b, wa, wb, ca, cb, occ, rough, tilt=(0.0, 0.0), roundness=0.8):
    pad = max(wa, wb)
    reg = cv.region(min(a[0], b[0]) - pad, min(a[1], b[1]) - pad, max(a[0], b[0]) + pad, max(a[1], b[1]) + pad)
    if reg is None:
        return
    sl, Xg, Yg = reg
    dx, dy = b[0] - a[0], b[1] - a[1]
    L2 = dx * dx + dy * dy + 1e-12
    t = np.clip(((Xg - a[0]) * dx + (Yg - a[1]) * dy) / L2, 0.0, 1.0)
    ex, ey = Xg - (a[0] + t * dx), Yg - (a[1] + t * dy)
    hw = wa + (wb - wa) * t
    m = ex * ex + ey * ey < hw * hw
    if not m.any():
        return
    L = math.sqrt(L2)
    px, py = -dy / L, dx / L
    across = np.clip((ex * px + ey * py) / np.maximum(hw, 1e-6), -1.0, 1.0) * roundness
    nz = np.sqrt(np.maximum(0.05, 1.0 - across * across))
    nrm = _norm3(px * across + tilt[0], py * across + tilt[1], nz)
    col = np.array(ca, np.float32) + (np.array(cb, np.float32) - np.array(ca, np.float32)) * t[..., None]
    col = col * (1.0 - 0.3 * (across * across))[..., None]
    cv.put(sl, m, col, nrm, occ, rough)


def stamp_polyline(cv, pts, w0, w1, c0, c1, occ, rough):
    n = len(pts) - 1
    for i in range(n):
        wa, wb = lerp(w0, w1, i / n), lerp(w0, w1, (i + 1) / n)
        ca = tuple(lerp(x, y, i / n) for x, y in zip(c0, c1))
        cb = tuple(lerp(x, y, (i + 1) / n) for x, y in zip(c0, c1))
        stamp_segment(cv, pts[i], pts[i + 1], wa, wb, ca, cb, occ, rough)


def stamp_oak_leaf(cv, base, ang, L, W, col, tilt, occ, lobes, phase, rough=0.5):
    dx, dy = math.cos(ang), math.sin(ang)
    px, py = -dy, dx
    pet = 0.12 * L
    corners = [(base[0] + dx * s * L + px * q * W, base[1] + dy * s * L + py * q * W)
               for s in (-0.13, 1.02) for q in (-0.55, 0.55)]
    xs, ys = [c[0] for c in corners], [c[1] for c in corners]
    reg = cv.region(min(xs), min(ys), max(xs), max(ys))
    if reg is None:
        return
    sl, Xg, Yg = reg
    rx, ry = Xg - base[0], Yg - base[1]
    lx = (rx * dx + ry * dy) / L
    ly = (rx * px + ry * py) / L
    xx = np.clip(lx, 0.0, 1.0)
    env = np.sin(np.pi * xx ** 0.8) ** 0.75 * (W / L) * 0.5
    lob = 0.6 + 0.4 * np.abs(np.sin(np.pi * lobes * xx + phase)) ** 0.7
    hw = env * lob
    blade = (lx > 0.0) & (lx < 1.0) & (np.abs(ly) < hw)
    petiole = (lx > -pet / L) & (lx <= 0.06) & (np.abs(ly) < 0.014)
    m = blade | petiole
    if not m.any():
        return
    yn = ly / np.maximum(env * 1.0, 1e-4)
    fold = 0.32
    n_across = fold * np.sign(ly) * np.minimum(1.0, np.abs(yn) * 4.0) + 0.3 * yn + tilt[1]
    n_along = 0.35 * (xx - 0.45) + tilt[0]
    vein = np.abs(np.sin(np.pi * (xx * lobes + np.abs(yn) * 0.9) + phase)) < 0.1
    n_across = n_across + np.where(vein, -0.15 * np.sign(ly), 0.0)
    nrm = _norm3(n_along * dx + n_across * px, n_along * dy + n_across * py, np.ones_like(lx))
    c = np.array(col, np.float32)
    shade = (0.9 + 0.18 * xx) * (1.0 - 0.2 * np.clip(np.abs(yn), 0, 1) ** 3)
    shade = np.where(np.abs(yn) < 0.07, shade * 1.18, shade)
    shade = np.where(vein, shade * 1.08, shade)
    colarr = c[None, None, :] * shade[..., None]
    colarr = np.where(petiole[..., None] & ~blade[..., None], np.array(lin(92, 80, 50), np.float32), colarr)
    cv.put(sl, m, colarr, nrm, occ, rough)


def polyline(start, ang, length, n, bend, rng):
    pts = [start]
    a = ang
    for i in range(n):
        a += bend / n + rng.uniform(-0.08, 0.08)
        p = pts[-1]
        pts.append((p[0] + math.cos(a) * length / n, p[1] + math.sin(a) * length / n))
    return pts


def poly_at(pts, t):
    seg = [math.hypot(b[0] - a[0], b[1] - a[1]) for a, b in zip(pts, pts[1:])]
    tot = sum(seg)
    d = t * tot
    for (a, b), s in zip(zip(pts, pts[1:]), seg):
        if d <= s or s == seg[-1]:
            k = d / s if s > 0 else 0.0
            return (a[0] + (b[0] - a[0]) * k, a[1] + (b[1] - a[1]) * k), math.atan2(b[1] - a[1], b[0] - a[0])
        d -= s
    return pts[-1], math.atan2(pts[-1][1] - pts[-2][1], pts[-1][0] - pts[-2][0])


def inside_cell(p, margin=0.025):
    return margin < p[0] < 1.0 - margin and margin < p[1] < 1.0 - margin


def fit_leaf(base, ang, L, W):
    """Turn a leaf toward the cell centre (then shrink it) until it lies inside the cell."""
    for k in range(12):
        tip = (base[0] + math.cos(ang) * L, base[1] + math.sin(ang) * L)
        sides = [(base[0] + math.cos(ang) * L * 0.5 + math.cos(ang + s * math.pi / 2) * W * 0.55,
                  base[1] + math.sin(ang) * L * 0.5 + math.sin(ang + s * math.pi / 2) * W * 0.55) for s in (-1, 1)]
        if inside_cell(tip) and all(inside_cell(s) for s in sides):
            return ang, L, W
        to_c = math.atan2(0.5 - base[1], 0.5 - base[0])
        ang += ((to_c - ang + math.pi) % TAU - math.pi) * 0.3
        if k > 5:
            L *= 0.85
            W *= 0.85
    return None


OAK_COLS = [(0.36, lin(50, 70, 36)), (0.36, lin(70, 94, 45)), (0.2, lin(100, 122, 58)), (0.06, lin(134, 134, 62)),
            (0.02, lin(116, 84, 46))]
PINE_COLS = [lin(36, 58, 42), lin(50, 76, 50), lin(66, 94, 58)]


def pick(rng, table):
    x = rng.random()
    for w, c in table:
        if x < w:
            return c
        x -= w
    return table[-1][1]


def draw_oak_sprite(cv, rng):
    twigs, leaves = [], []
    base = (0.5 + rng.uniform(-0.03, 0.03), 0.0)
    main = polyline(base, math.pi / 2 + rng.uniform(-0.12, 0.12), 0.78, 8, rng.uniform(-0.25, 0.25), rng)
    twigs.append((main, 0.008, 0.0035))
    nside = rng.randint(7, 9)
    for k, t in enumerate(np.linspace(0.1, 0.86, nside)):
        p, a = poly_at(main, t)
        side = 1 if k % 2 == 0 else -1
        sa = a + side * math.radians(rng.uniform(45, 78))
        sl = rng.uniform(0.24, 0.4) * (1.15 - 0.45 * t)
        tw = polyline(p, sa, sl, 4, side * rng.uniform(-0.1, 0.4), rng)
        tw = [q for q in tw if inside_cell(q, 0.06)] or [p]
        if len(tw) >= 2:
            twigs.append((tw, 0.005 * (1.1 - 0.4 * t), 0.0022))
        tip = tw[-1]
        ta = math.atan2(tw[-1][1] - tw[-2][1], tw[-1][0] - tw[-2][0]) if len(tw) >= 2 else sa
        for i in range(rng.randint(5, 7)):
            leaves.append((tip, ta + math.radians(rng.uniform(-95, 95)), rng.uniform(0.11, 0.155)))
        for q in (0.3, 0.55, 0.8):
            if len(tw) >= 2 and rng.random() < 0.9:
                pt, a2 = poly_at(tw, q)
                leaves.append((pt, a2 + rng.choice((-1, 1)) * math.radians(rng.uniform(30, 75)),
                               rng.uniform(0.1, 0.14)))
    tip = main[-1]
    ta = math.atan2(main[-1][1] - main[-2][1], main[-1][0] - main[-2][0])
    for i in range(rng.randint(6, 8)):
        leaves.append((tip, ta + math.radians(rng.uniform(-100, 100)), rng.uniform(0.12, 0.16)))
    for t in np.linspace(0.15, 0.92, 7):
        p, a = poly_at(main, t)
        leaves.append((p, a + rng.choice((-1, 1)) * math.radians(rng.uniform(25, 65)), rng.uniform(0.1, 0.14)))
    for tw, w0, w1 in twigs:
        stamp_polyline(cv, tw, w0, w1, lin(82, 64, 46), lin(96, 80, 58), 0.55, 0.8)
    rng.shuffle(leaves)
    n = len(leaves)
    drawn = 0
    for i, (p, a, L) in enumerate(leaves):
        W = L * rng.uniform(0.55, 0.66)
        fit = fit_leaf(p, a, L, W)
        if fit is None:
            continue
        a, L, W = fit
        col = pick(rng, OAK_COLS)
        col = tuple(c * rng.uniform(0.88, 1.12) for c in col)
        occ = 0.55 + 0.45 * ((i + 1) / n) ** 0.7
        stamp_oak_leaf(cv, p, a, L, W, col, (rng.gauss(0, 0.3), rng.gauss(0, 0.35)), occ,
                       rng.choice((3.5, 4.5, 5.5)), rng.uniform(0, 0.6), rough=rng.uniform(0.62, 0.75))
        drawn += 1
    return {"twigs": len(twigs), "leaves": drawn}


def draw_pine_sprite(cv, rng):
    twigs, bursts = [], []
    base = (0.5 + rng.uniform(-0.03, 0.03), 0.0)
    main = polyline(base, math.pi / 2 + rng.uniform(-0.1, 0.1), 0.66, 6, rng.uniform(-0.2, 0.2), rng)
    twigs.append((main, 0.0085, 0.005))
    for t in np.arange(0.22, 0.93, 0.1):
        p, a = poly_at(main, t + rng.uniform(-0.02, 0.02))
        bursts.append((p, a, rng.randint(26, 34), 80, (0.1, 0.16)))
    tip = main[-1]
    ta = math.atan2(main[-1][1] - main[-2][1], main[-1][0] - main[-2][0])
    bursts.append((tip, ta, 70, 170, (0.13, 0.21)))
    nside = rng.randint(3, 4)
    for k, t in enumerate(np.linspace(0.2, 0.62, nside)):
        p, a = poly_at(main, t + rng.uniform(-0.03, 0.03))
        side = 1 if (k + rng.randint(0, 1)) % 2 == 0 else -1
        sa = a + side * math.radians(rng.uniform(38, 58))
        sl = rng.uniform(0.2, 0.3)
        tw = polyline(p, sa, sl, 3, side * rng.uniform(0.0, 0.3), rng)
        twigs.append((tw, 0.006, 0.0035))
        tt = math.atan2(tw[-1][1] - tw[-2][1], tw[-1][0] - tw[-2][0])
        pm, am = poly_at(tw, 0.5)
        bursts.append((pm, am, rng.randint(20, 26), 80, (0.09, 0.13)))
        bursts.append((tw[-1], tt, rng.randint(44, 56), 160, (0.11, 0.17)))
    for tw, w0, w1 in twigs:
        stamp_polyline(cv, tw, w0, w1, lin(98, 58, 36), lin(122, 76, 46), 0.6, 0.8)
    needles = 0
    order = list(range(len(bursts)))
    rng.shuffle(order)
    for bi in order:
        c, a, n, spread, lens = bursts[bi]
        col = PINE_COLS[rng.randrange(3)]
        for k in range(n):
            ang = a + math.radians(rng.uniform(-spread, spread) * (0.35 + 0.65 * rng.random()))
            L = rng.uniform(*lens)
            start = (c[0] + math.cos(ang) * 0.006, c[1] + math.sin(ang) * 0.006)
            end = (c[0] + math.cos(ang) * L, c[1] + math.sin(ang) * L)
            for _ in range(8):
                if inside_cell(end, 0.02):
                    break
                L *= 0.85
                end = (c[0] + math.cos(ang) * L, c[1] + math.sin(ang) * L)
            if not inside_cell(end, 0.02):
                continue
            b = rng.uniform(0.85, 1.15)
            c0 = tuple(x * b * 0.7 for x in col)
            c1 = tuple(min(1.0, x * b * 1.4) for x in col)
            occ = 0.6 + 0.4 * rng.random() ** 0.5
            stamp_segment(cv, start, end, 0.0029, 0.0015, c0, c1, occ, 0.7,
                          tilt=(rng.gauss(0, 0.25), rng.gauss(0, 0.25)), roundness=0.85)
            needles += 1
    return {"twigs": len(twigs), "bursts": len(bursts), "needles": needles}


def downsample(cv):
    R = cv.R
    h = R // 2
    a = cv.a.reshape(h, 2, h, 2).mean((1, 3))

    def pm(x):
        return (x * cv.a[..., None]).reshape(h, 2, h, 2, -1).sum((1, 3))
    wsum = cv.a.reshape(h, 2, h, 2).sum((1, 3))[..., None]
    col = pm(cv.col) / np.maximum(wsum, 1e-6)
    nrm = pm(cv.n) / np.maximum(wsum, 1e-6)
    o = pm(cv.o[..., None])[..., 0] / np.maximum(wsum[..., 0], 1e-6)
    r = pm(cv.r[..., None])[..., 0] / np.maximum(wsum[..., 0], 1e-6)
    return a, col, nrm, o, r


def pushpull(val, w):
    """Fill texels with zero weight from coarser averages of the weighted ones (so mips do not bleed dark)."""
    levels = [(val * w[..., None], w.copy())]
    while levels[-1][1].shape[0] > 1:
        P, A = levels[-1]
        h = A.shape[0] // 2
        levels.append((P.reshape(h, 2, h, 2, -1).sum((1, 3)), A.reshape(h, 2, h, 2).sum((1, 3))))
    P, A = levels[-1]
    col = P / np.maximum(A[..., None], 1e-9)
    for P, A in reversed(levels[:-1]):
        up = np.repeat(np.repeat(col, 2, 0), 2, 1)
        col = np.where(A[..., None] > 1e-6, P / np.maximum(A[..., None], 1e-9), up)
    return col


def draw_atlas(sprite, res, seed):
    """2 x 2 sprite cells: base colour (linear) + alpha, normal, ORM arrays of res x res."""
    cell = res // 2
    col = np.zeros((res, res, 3), np.float32)
    alpha = np.zeros((res, res), np.float32)
    nrm = np.zeros((res, res, 3), np.float32)
    orm = np.zeros((res, res, 3), np.float32)
    stats = []
    for k in range(4):
        rng = sub_rng(seed, "sprite", sprite, k)
        cv = Canvas(cell * 2)
        stats.append(draw_oak_sprite(cv, rng) if sprite == "oak" else draw_pine_sprite(cv, rng))
        a, c, n, o, r = downsample(cv)
        w = (a > 0).astype(np.float32) * a
        c = pushpull(c * (0.72 + 0.28 * o[..., None]), w)
        n = pushpull(n, w)
        n /= np.maximum(np.linalg.norm(n, axis=2, keepdims=True), 1e-6)
        oo = pushpull(o[..., None], w)[..., 0]
        rr = pushpull(r[..., None], w)[..., 0]
        i, j = k % 2, k // 2
        ys, xs = slice(j * cell, (j + 1) * cell), slice(i * cell, (i + 1) * cell)
        col[ys, xs] = c
        alpha[ys, xs] = a
        nrm[ys, xs] = n * 0.5 + 0.5
        orm[ys, xs] = np.stack([oo, rr, np.zeros_like(oo)], axis=-1)
    return col, alpha, nrm, orm, stats


def atlas_cells(res):
    e = 3.0 / res
    return [(0.5 * (k % 2) + e, 0.5 * (k // 2) + e, 0.5 * (k % 2 + 1) - e, 0.5 * (k // 2 + 1) - e) for k in range(4)]


# --------------------------------------------------------------------------------------------------------------------
# Materials
# --------------------------------------------------------------------------------------------------------------------

def gltf_output_group():
    ng = bpy.data.node_groups.get("glTF Material Output")
    if ng is None:
        ng = bpy.data.node_groups.new("glTF Material Output", "ShaderNodeTree")
        ng.interface.new_socket(name="Occlusion", in_out="INPUT", socket_type="NodeSocketFloat")
        ng.nodes.new("NodeGroupInput")
    return ng


def pbr_material(name, paths, cutout=None):
    mat = bpy.data.materials.new(name)
    tree = mat.node_tree
    for n in list(tree.nodes):
        tree.nodes.remove(n)
    out = tree.nodes.new("ShaderNodeOutputMaterial")
    bsdf = tree.nodes.new("ShaderNodeBsdfPrincipled")
    tree.links.new(bsdf.outputs[0], out.inputs["Surface"])
    col = tree.nodes.new("ShaderNodeTexImage")
    col.image = bpy.data.images.load(paths["basecolor"])
    col.image.colorspace_settings.name = "sRGB"
    tree.links.new(col.outputs["Color"], bsdf.inputs["Base Color"])
    if cutout is not None:
        col.image.alpha_mode = "CHANNEL_PACKED"
        lt = tree.nodes.new("ShaderNodeMath")
        lt.operation = "LESS_THAN"
        lt.inputs[1].default_value = cutout
        tree.links.new(col.outputs["Alpha"], lt.inputs[0])
        inv = tree.nodes.new("ShaderNodeMath")
        inv.operation = "SUBTRACT"
        inv.inputs[0].default_value = 1.0
        tree.links.new(lt.outputs[0], inv.inputs[1])
        tree.links.new(inv.outputs[0], bsdf.inputs["Alpha"])
        mat.surface_render_method = "DITHERED"
    orm = tree.nodes.new("ShaderNodeTexImage")
    orm.image = bpy.data.images.load(paths["orm"])
    orm.image.colorspace_settings.name = "Non-Color"
    sep = tree.nodes.new("ShaderNodeSeparateColor")
    tree.links.new(orm.outputs["Color"], sep.inputs[0])
    tree.links.new(sep.outputs[1], bsdf.inputs["Roughness"])
    tree.links.new(sep.outputs[2], bsdf.inputs["Metallic"])
    grp = tree.nodes.new("ShaderNodeGroup")
    grp.node_tree = gltf_output_group()
    tree.links.new(sep.outputs[0], grp.inputs["Occlusion"])
    nrm = tree.nodes.new("ShaderNodeTexImage")
    nrm.image = bpy.data.images.load(paths["normal"])
    nrm.image.colorspace_settings.name = "Non-Color"
    nmap = tree.nodes.new("ShaderNodeNormalMap")
    tree.links.new(nrm.outputs["Color"], nmap.inputs["Color"])
    tree.links.new(nmap.outputs["Normal"], bsdf.inputs["Normal"])
    mat.use_backface_culling = True     # single-sided; the cards carry a back face of their own
    return mat


def flat_material(name, rgb):
    mat = bpy.data.materials.new(name)
    bsdf = mat.node_tree.nodes.get("Principled BSDF")
    bsdf.inputs["Base Color"].default_value = tuple(rgb) + (1.0,)
    bsdf.inputs["Roughness"].default_value = 0.85
    mat.use_backface_culling = True
    return mat


# --------------------------------------------------------------------------------------------------------------------
# Main
# --------------------------------------------------------------------------------------------------------------------

def parse_args():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    ap = argparse.ArgumentParser()
    ap.add_argument("--species", required=True, choices=sorted(SPECIES))
    ap.add_argument("--seed", type=int, default=None)
    ap.add_argument("--out-dir", default=None)
    ap.add_argument("--work-dir", default=None)
    ap.add_argument("--res", type=int, default=2048)
    ap.add_argument("--samples", type=int, default=16)
    ap.add_argument("--device", default="auto", choices=("auto", "cpu"))
    ap.add_argument("--no-bake", action="store_true")
    return ap.parse_args(argv)


def build_once(sp, env, seed, species):
    branches, hosts, trunk = BUILDERS[sp.get("builder", species)](sp, env, seed)
    cards = plan_cards(sp, hosts, trunk, env, seed)
    return branches, cards, trunk


def assemble(sp, branches, cards, res, budget):
    b = Builder()
    wood_tile = 0.5
    for br in branches:
        tube(b, br, wood_tile)
    bark_tris = sum(len(f) - 2 for f in b.faces)
    fo = sp["foliage"]
    per = 0
    kept = []
    if fo:
        per = 4 * (2 if fo["crossed"] else 1)
        room = max(0, (budget - bark_tris) // per)
        kept = sorted(cards, key=lambda c: c["prio"])[:room]
        cells = atlas_cells(res)
        for c in kept:
            for n in card_normals(c, fo["crossed"], fo.get("roll", 0.5)):
                cc = dict(c)
                cc["n"] = n
                card_geo(b, cc, cells, None)
    b.kept_cards = kept
    return b, bark_tris, len(kept), len(cards)


def grow_standing(sp, species, seed, res, budget):
    """Grow a standing tree; re-centre the envelope until the model's bounds centre on the trunk. Returns the best
    ((branches, cards, trunk), (builder, bark_tris, cards_placed, cards_planned)) and the centring history."""
    H = sp["height"]
    history = []
    env = Envelope(sp["crown"], H, sub_rng(seed, "envelope"))
    best = None
    for attempt in range(8):
        built = build_once(sp, env, seed, species)
        asm = assemble(sp, built[0], built[1], res, budget)
        xs = [v.x for v in asm[0].verts]
        ys = [v.y for v in asm[0].verts]
        off = ((min(xs) + max(xs)) / 2, (min(ys) + max(ys)) / 2)
        mag = math.hypot(*off)
        history.append({"env_centre": [round(env.cx, 3), round(env.cy, 3)],
                        "bounds_centre": [round(off[0], 3), round(off[1], 3)]})
        print(f"  attempt {attempt}: bounds centre off the trunk by {mag:.3f} m", flush=True)
        if best is None or mag < best[0]:
            best = (mag, built, asm)
        if mag < 0.03:
            break
        env.cx -= off[0] * 0.9
        env.cy -= off[1] * 0.9
    return best[1], best[2], history


def crown_frame(sp, bounds_centre):
    """The crown ellipsoid the card normals bend toward: centre and semi-axes (Blender frame)."""
    prof = sp["crown"]["profile"]
    f_lo = prof[0][0]
    H = sp["height"]
    return ((bounds_centre[0], bounds_centre[1], H * (f_lo + 1.0) / 2),
            (max(w for _, w in prof) * H, max(w for _, w in prof) * H, H * (1.0 - f_lo) / 2))


def main():
    args = parse_args()
    species = args.species
    sp = SPECIES[species]
    asset = sp["asset_id"]
    seed = sp["seed"] if args.seed is None else args.seed
    out_dir = os.path.abspath(args.out_dir or os.path.join(REPO, "assets", "_staging", "procedural", asset))
    work = os.path.abspath(args.work_dir or os.path.join(tempfile.gettempdir(), asset + "_procgen"))
    os.makedirs(out_dir, exist_ok=True)
    os.makedirs(work, exist_ok=True)
    t_start = time.time()
    bpy.ops.wm.read_factory_settings(use_empty=True)
    H = sp["height"]
    budget = TRI_BUDGET - 600
    kind = sp.get("kind", "tree")
    history, instances, trunk = [], None, None

    if kind == "tree":
        # grow; re-centre the envelope until the model's bounds centre on the trunk
        (branches, cards, trunk), (b, bark_tris, n_cards, n_planned), history = grow_standing(sp, species, seed,
                                                                                               args.res, budget)
    elif kind == "log":
        branches = build_log(sp)
        cards = []
        trunk = branches[0]
        b, bark_tris, n_cards, n_planned = assemble(sp, branches, cards, args.res, budget)
        reorient_length_x(b)
    elif kind == "cluster":
        branches, cards, instances = build_cluster_stand(sp)
        b, bark_tris, n_cards, n_planned = assemble(sp, branches, cards, args.res, budget)
    elif kind == "stump_cluster":
        branches, instances = build_cluster_stump(sp)
        cards = []
        b, bark_tris, n_cards, n_planned = assemble(sp, branches, cards, args.res, budget)
    else:
        raise ValueError(f"unknown species kind {kind!r}")

    zs_ = [v.z for v in b.verts]
    xs = [v.x for v in b.verts]
    ys = [v.y for v in b.verts]
    dims = Vector((max(xs) - min(xs), max(ys) - min(ys), max(zs_) - min(zs_)))
    bounds_centre = ((min(xs) + max(xs)) / 2, (min(ys) + max(ys)) / 2)
    topo = validate_builder(b)
    print("TOPOLOGY", json.dumps(topo), flush=True)

    r13 = None
    if kind == "tree":
        # trunk facts
        tr = sp["trunk"]
        ring0 = [trunk.pts[0] + Vector((math.cos(TAU * j / trunk.sides), math.sin(TAU * j / trunk.sides), 0))
                 * trunk.rad[0] * trunk.shape(0.0, TAU * j / trunk.sides) for j in range(trunk.sides)]
        flare_r = [math.hypot(p.x, p.y) for p in ring0]
        s13 = trunk.s_at_z(1.3)
        _, _, r13 = trunk.frame_at(s13)
        zs_straight = tr["straight_frac"] * H
        top_straight = trunk.frame_at(trunk.s_at_z(zs_straight))[0]
        first_fork_z = min((br.pts[0].z for br in branches if br.parent is trunk and br.kind != "stub"), default=H)
        fork_pt = trunk.frame_at(trunk.s_at_z(first_fork_z))[0]
        lean_fork = math.degrees(math.atan2(math.hypot(fork_pt.x, fork_pt.y), fork_pt.z))
        trunk_info = {
            "base_centre_m": [round(trunk.pts[0].x, 4), round(trunk.pts[0].y, 4), round(trunk.pts[0].z, 4)],
            "base_axis": "vertical (the first rings are on the Z axis, the foot cap lies in y = 0)",
            "radius_at_1_3m": round(r13, 3),
            "radius_at_ground_mean": round(sum(flare_r) / len(flare_r), 3),
            "radius_at_ground_max_root_lobe": round(max(flare_r), 3),
            "straight_to_m": round(zs_straight, 2),
            "lean_deg_base_to_straight_top": round(math.degrees(math.atan2(math.hypot(top_straight.x, top_straight.y),
                                                                           top_straight.z)), 3),
            "first_limb_m": round(first_fork_z, 2),
            "lean_deg_base_to_first_limb": round(lean_fork, 3),
            "height_of_trunk_m": round(trunk.pts[-1].z, 2),
        }
    elif kind == "log":
        # diagnostic only: the trunk's own pts/rad are still the pre-reorient vertical build (its Z is the log's
        # length before reorient_length_x moved it onto X)
        trunk_info = {
            "note": "computed on the pre-reorient vertical build; its Z there is the log's length",
            "base_radius_m": round(trunk.rad[0], 3), "tip_radius_m": round(trunk.rad[-1], 3),
            "built_length_m": round(trunk.pts[-1].z, 2), "root_broken": trunk.root_broken, "tip_end": trunk.end,
        }
    else:
        trunk_info = {"instances": instances}
    kinds = {}
    for br in branches:
        k = kinds.setdefault(br.kind, {"count": 0, "broken_ends": 0})
        k["count"] += 1
        k["broken_ends"] += br.end == "broken"

    # crown normal field for the cards (standing trees only: a log/beam carries no foliage, and a cluster's three
    # small crowns have no single shared centre to bend toward, so those keep the mesh's own face normals)
    if kind == "tree":
        crown_c, crown_ax = crown_frame(sp, bounds_centre)
        bend = sp["foliage"]["bend"] if sp["foliage"] else 0.0
    else:
        crown_c, crown_ax, bend = (0.0, 0.0, 0.0), (1.0, 1.0, 1.0), 0.0
    targets = None
    if kind == "tree" and sp["foliage"] and sp["foliage"].get("clump"):
        targets = clump_targets(b, sp["foliage"]["clump"], crown_c, crown_ax)
    obj = make_object(b, asset)
    finish_topology(obj, crown_c, crown_ax, bend, targets)
    checks = mesh_checks(obj.data, args.res)
    print("MESH", json.dumps(checks), flush=True)

    # textures
    textures, bake_info = {}, None
    sprite_stats = None
    fo = sp["foliage"]
    mats = []
    device_used = set()
    t_tex = time.time()
    seams = {}
    if args.no_bake:
        mats = [flat_material("MAT_flat_bark", lin(90, 80, 70)), flat_material("MAT_flat_wood", lin(150, 120, 90))]
    else:
        scene = bpy.context.scene
        dev = set_device(scene, args.device)
        device_used.add(dev)
        scene.cycles.samples = args.samples
        scene.cycles.use_denoising = False
        scene.cycles.seed = 0
        scene.render.bake.use_selected_to_active = False
        moss_boost = sp["bark"].get("moss_boost", 0.0)
        bark_sh = (lambda g, t: BARK_SHADERS[sp["bark"]["shader"]](g, t, moss_boost)) if moss_boost else \
            BARK_SHADERS[sp["bark"]["shader"]]
        wood_sh = WOOD_SHADERS[sp["wood"].get("shader", "plain")]
        for key, shader, size_m in (("bark", bark_sh, sp["bark"]["tile_m"]),
                                    ("wood", lambda g, t: wood_sh(g, t, sp["wood"]), 0.5)):
            t0 = time.time()
            arr = bake_tile(key, shader, size_m, args.res, args.samples, device_used)
            seams[key] = {p: round(seam_error(arr[p]), 3) for p in arr}
            stem = os.path.join(work, f"{asset}_{key}")
            paths = {"basecolor": stem + "_basecolor.png", "orm": stem + "_orm.png", "normal": stem + "_normal.png"}
            save_png(to_srgb(arr["colour"]), paths["basecolor"], "sRGB")
            save_png(arr["orm"], paths["orm"], "Non-Color")
            save_png(arr["normal"], paths["normal"], "Non-Color")
            textures[key] = {k: os.path.basename(v) for k, v in paths.items()}
            textures[key]["tile_m_nominal"] = size_m
            textures[key]["bake_seconds"] = round(time.time() - t0, 1)
            mats.append(pbr_material(f"MAT_{asset}_{key}", paths))
        bake_info = {"samples": args.samples, "devices": sorted(device_used)}
    if fo:
        t0 = time.time()
        col, alpha, nrm, orm, sprite_stats = draw_atlas(fo["sprite"], args.res, seed)
        stem = os.path.join(work, f"{asset}_foliage")
        paths = {"basecolor": stem + "_basecolor.png", "orm": stem + "_orm.png", "normal": stem + "_normal.png"}
        save_png(to_srgb(col), paths["basecolor"], "sRGB", alpha=alpha)
        save_png(orm, paths["orm"], "Non-Color")
        save_png(nrm, paths["normal"], "Non-Color")
        textures["foliage"] = {k: os.path.basename(v) for k, v in paths.items()}
        textures["foliage"]["alpha_coverage"] = round(float((alpha >= 0.5).mean()), 3)
        textures["foliage"]["draw_seconds"] = round(time.time() - t0, 1)
        mats.append(pbr_material(f"MAT_{asset}_foliage", paths, cutout=0.5))
    me = obj.data
    idx = np.empty(len(me.polygons), dtype=np.int32)
    me.polygons.foreach_get("material_index", idx)
    me.materials.clear()
    for m in mats:
        me.materials.append(m)
    me.polygons.foreach_set("material_index", idx)
    me.attributes.remove(me.attributes["fol"])
    me.update()
    textures_seconds = round(time.time() - t_tex, 1)

    glb = os.path.join(out_dir, asset + ".glb")
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(work, asset + ".blend"))
    bpy.ops.export_scene.gltf(filepath=glb, export_format="GLB", export_materials="EXPORT", export_yup=True,
                              export_apply=True, export_normals=True, export_texcoords=True, export_tangents=False,
                              export_image_format="AUTO", use_selection=False)

    tris_by = {"bark_and_wood": bark_tris, "foliage": checks["triangles"] - bark_tris}
    concept_id = sp.get("variant_of", asset)
    concept = os.path.join(REPO, "assets", "concepts", concept_id + ".png")
    concept_sha = hashlib.sha256(open(concept, "rb").read()).hexdigest() if os.path.exists(concept) else None
    script_sha = hashlib.sha256(open(os.path.abspath(__file__), "rb").read()).hexdigest()
    not_a_replacement = ("new asset (docs/WAVE_0_MODULAR_ASSET_STANDARD.md section 17, template_variant of "
                         "tree_species, assets/manifests/asset_production.json); no prior assets/ready/<id>")
    if kind == "tree":
        height = dims.z
        width = max(dims.x, dims.y)
        h_lo, h_hi = sp.get("height_range_m", (12.0, 14.0))
        fits = {
            f"height_{h_lo:g}_to_{h_hi:g}_m".replace(".", "_"): h_lo <= height <= h_hi,
            "covers_blocker_width": 2 * BLOCKER["radius"] - width <= 2 * BLOCKER["side_margin"],
            "reaches_blocker_height": BLOCKER["height"] - height <= BLOCKER["height_shortfall"],
            "trunk_radius_0_35_to_0_45_at_1_3m": 0.35 <= r13 <= 0.45,
            "triangles_within_40k": checks["triangles"] <= TRI_BUDGET,
        }
        replaces, blocker_info = f"assets/ready/{asset} (image-to-3D reconstruction: shard-triangle canopy, " \
                                 "leaning trunk)", BLOCKER
        if "variant_of" in sp:
            replaces = f"new asset: a variant of {sp['variant_of']} (seed {seed}); no prior assets/ready/{asset}"
    elif kind == "log":
        length, diam = dims.x, max(dims.y, dims.z)
        tgt = sp["target"]
        fits = {
            "length_within_target_m": tgt["length_range"][0] <= length <= tgt["length_range"][1],
            "diameter_within_target_m": tgt["diam_range"][0] <= diam <= tgt["diam_range"][1],
            "fitting_cs_width_ok": tgt["box_m"][0] - length <= 2 * BLOCKER["side_margin"] + 0.005,
            "fitting_cs_depth_ok": tgt["box_m"][1] - diam <= 2 * BLOCKER["side_margin"] + 0.005,
            "triangles_within_40k": checks["triangles"] <= TRI_BUDGET,
        }
        if "clearance_m" in tgt:
            # an overhang (the Woundmoss beam): the game lifts it from its clearance up (HollowView.cs), so what
            # matters is the model's own thickness matching the clearance-to-top span, not Fitting.cs's ground-up
            # height-shortfall math
            span = tgt["height_m"] - tgt["clearance_m"]
            fits["thickness_matches_clearance_span_m"] = abs(dims.z - span) <= 0.15
        else:
            fits["fitting_cs_height_ok"] = tgt["height_m"] - dims.z <= BLOCKER["height_shortfall"] + 0.005
        replaces, blocker_info = not_a_replacement, dict(tgt, side_margin=BLOCKER["side_margin"],
                                                          height_shortfall=BLOCKER["height_shortfall"])
    elif kind == "cluster":
        fits = {
            "instance_count_is_3": len(instances) == 3,
            # three real young saplings are not identical heights; the envelope caps every instance at H = 2.8 m
            # and each is grown independently, so this is a tolerance band around "~2.8 m tall", not an exact match
            "each_trunk_height_in_range_2_2_to_3_0m": all(2.2 <= it["height_m"] <= 3.0 for it in instances),
            "triangles_within_40k": checks["triangles"] <= TRI_BUDGET,
        }
        replaces, blocker_info = not_a_replacement, {"offsets_m": sp["offsets"]}
    else:
        fits = {
            "instance_count_is_3": len(instances) == 3,
            "each_stump_height_in_range_0_20_to_0_30m": all(0.20 <= it["height_m"] <= 0.30 for it in instances),
            "triangles_within_40k": checks["triangles"] <= TRI_BUDGET,
        }
        replaces, blocker_info = not_a_replacement, {"offsets_m": sp["offsets"]}
    prov = {
        "asset_id": asset,
        "method": "procedural",
        "script": "tools/asset_pipeline/_procgen_tree.py",
        "script_sha256": script_sha,
        "command": f"blender --background --factory-startup --python tools/asset_pipeline/_procgen_tree.py -- "
                   f"--species {species} --seed {seed} --res {args.res} --samples {args.samples}"
                   + (" --no-bake" if args.no_bake else ""),
        "blender": bpy.app.version_string,
        "species": species,
        "seed": seed,
        "date": datetime.datetime.now().isoformat(timespec="seconds"),
        "parameters": sp,
        "concept": f"assets/concepts/{concept_id}.png",
        "concept_sha256": concept_sha,
        "concept_profile": "crown.profile: (height fraction, half width / height), measured from the concept "
                           "(background removed, largest component, trunk-centred per-row extents, upper hull)",
        "replaces": replaces,
        "frame": "glTF +Y up, front +Z (the concept's view), trunk base centred on the origin, lowest point y=0",
        "dimensions_m": {"x": round(dims.x, 3), "y_height": round(dims.z, 3), "z": round(dims.y, 3)},
        "bounds_centre_offset_from_trunk_m": {"x": round(bounds_centre[0], 3), "z": round(-bounds_centre[1], 3)},
        "centring_iterations": history,
        "trunk": trunk_info,
        "skeleton": kinds,
        "blocker": blocker_info,
        "checks": fits,
        "triangles": checks["triangles"],
        "triangles_by_part": tris_by,
        "triangle_budget": TRI_BUDGET,
        "foliage_cards": {"placed": n_cards, "planned": n_planned,
                          "faces_per_card": 2, "cards_per_cluster": (2 if fo and fo["crossed"] else 1) if fo else 0,
                          "note": "each card is two single-sided quads back to back on separate vertices: a sheet, "
                                  "not a solid, so the closed-solid checks apply to the bark only"} if fo else None,
        "topology_check": topo,
        "mesh_check": checks,
        "materials": {
            "source": "own procedural shaders: bark and broken wood baked with Cycles to seamless tiles (4D torus "
                      "noise); leaf/needle sprites drawn in numpy. The staging world materials were not used.",
            "maps": "base colour (sRGB), normal (tangent, OpenGL +Y), ORM (R occlusion, G roughness, B metallic)",
            "bark_uv": "U = arc fraction round each ring (one tile around, seamless) + spiral twist; V = length / "
                       "local circumference (square texels, bark scales with the branch)",
            "foliage": {"alphaMode": "MASK", "alphaCutoff": 0.5, "double_sided": False,
                        "atlas": "2 x 2 sprite cells", "sprites": sprite_stats} if fo else None,
            "textures": textures,
            "tile_seam_error": seams,
            "bake": bake_info,
            "texture_seconds": textures_seconds,
            "resolution": args.res,
        },
        "outputs": [asset + ".glb", asset + "_provenance.json"],
        "build_seconds": round(time.time() - t_start, 1),
    }
    with open(os.path.join(out_dir, asset + "_provenance.json"), "w", encoding="utf-8") as fh:
        json.dump(prov, fh, indent=2, default=lambda o: list(o) if isinstance(o, tuple) else str(o))
    print("RESULT", json.dumps({"glb": glb, "dims": prov["dimensions_m"], "tris": checks["triangles"],
                                "tris_by_part": tris_by, "cards": n_cards, "checks": fits,
                                "bounds_offset": prov["bounds_centre_offset_from_trunk_m"],
                                "topology": topo, "mesh": checks, "trunk": trunk_info}))


if __name__ == "__main__":
    main()
