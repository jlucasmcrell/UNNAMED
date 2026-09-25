"""Self-test template for procgen_lib: a 0.5 m oak box of nailed planks, bound by two forged-iron bands, with a
plank lid on two battens (strapped, hinged at the back) and a rope handle each side.

It is only geometry plus parameters; run_template does the rest. It is not a library asset (self_test: the reuse
gate is not asked, and it stages under assets/_staging/procgen/procgen_sample_banded_box).

  blender --background --factory-startup --python tools/asset_pipeline/procgen_lib/sample_banded_box.py -- [--review]
"""
import os
import sys

import numpy as np

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
from procgen_lib import primitives as pr  # noqa: E402
from procgen_lib.materials import Material  # noqa: E402
from procgen_lib.runner import run_template  # noqa: E402

WOOD, IRON, ROPE = 0, 1, 2
X, Y, Z = np.eye(3)

PARAMS = {
    "seed": 2609,
    "length_m": 0.50, "depth_m": 0.50, "body_h_m": 0.42,
    "board_t_m": 0.020, "rows": 3, "gap_m": 0.0012, "floor_z_m": 0.014, "floor_boards": 3,
    "lid_t_m": 0.022, "lid_boards": 3, "lid_gap_m": 0.0015, "batten_w_m": 0.045, "batten_h_m": 0.020,
    "band_t_m": 0.003, "bands_z_m": [[0.045, 0.080], [0.335, 0.370]], "strap_w_m": 0.035, "strap_x_m": 0.15,
    "rivet_r_m": 0.0055, "rope_r_m": 0.0075, "handle_z_m": 0.21, "handle_span_m": 0.13,
    "wood": {"species": "oak", "age": 0.45}, "iron": {"kind": "forged", "rust": 0.35},
}


def widths(rng, total, count, gap):
    w = rng.uniform(0.88, 1.12, count)
    return w / w.sum() * (total - (count - 1) * gap)


def build(b, P, rng):
    L, D, H, t = P["length_m"], P["depth_m"], P["body_h_m"], P["board_t_m"]
    bt, rr = P["band_t_m"], P["rivet_r_m"]
    # walls: front/back boards run the full length, side boards fit between them
    z = 0.0
    for i, h in enumerate(widths(rng, H, P["rows"], P["gap_m"])):
        zc = z + h / 2
        z += h + P["gap_m"]
        for s in (-1, 1):
            pr.plank(b, f"wall_{'FB'[s > 0]}{i}", (0, s * (D / 2 - t / 2), zc), X, s * Y, L, h, t)
            pr.plank(b, f"wall_{'LR'[s > 0]}{i}", (s * (L / 2 - t / 2), 0, zc), Y, s * X, D - 2 * t - 0.0006, h, t)
    y = -(D / 2 - t - 0.0003)
    for i, w in enumerate(widths(rng, D - 2 * t - 0.0006, P["floor_boards"], P["gap_m"])):
        pr.plank(b, f"floor_{i}", (0, y + w / 2, P["floor_z_m"] + t / 2), X, Z, L - 2 * t - 0.0006, w, t)
        y += w + P["gap_m"]
    # two iron bands round the body, riveted through each board they cross
    for k, (z0, z1) in enumerate(P["bands_z_m"]):
        pr.band_loop(b, f"band_{k}", (0, 0, 0), L / 2, D / 2, z0, z1, bt, slot=IRON)
        zc = (z0 + z1) / 2
        for s in (-1, 1):
            for u in (-0.17, 0.0, 0.17):
                pr.rivet(b, (u, s * (D / 2 + bt), zc), s * Y, r=rr, slot=IRON)
                pr.rivet(b, (s * (L / 2 + bt), u, zc), s * X, r=rr, slot=IRON)
    # rope handles: each end buried 6 mm in the side board, sagging between
    for s in (-1, 1):
        hs, zh = P["handle_span_m"] / 2, P["handle_z_m"]
        xw = s * (L / 2 - 0.006)
        pts = pr.catmull([(xw, -hs, zh), (s * (L / 2 + 0.018), -hs * 0.8, zh - 0.02),
                          (s * (L / 2 + 0.028), 0, zh - 0.045), (s * (L / 2 + 0.018), hs * 0.8, zh - 0.02),
                          (xw, hs, zh)], step=0.012)
        pr.tube(b, f"rope_{'LR'[s > 0]}", pts, P["rope_r_m"], n=8, slot=ROPE)
    # lid: planks across the full top on two battens that sit inside the walls, two iron straps over the front edge
    zl = H + P["lid_gap_m"]
    y = -D / 2
    for i, w in enumerate(widths(rng, D, P["lid_boards"], P["gap_m"])):
        pr.plank(b, f"lid_{i}", (0, y + w / 2, zl + P["lid_t_m"] / 2), X, Z, L, w, P["lid_t_m"], group="lid")
        y += w + P["gap_m"]
    for s in (-1, 1):
        xb = s * (L / 2 - t - 0.006 - P["batten_w_m"] / 2)
        pr.plank(b, f"batten_{'LR'[s > 0]}", (xb, 0, zl - P["batten_h_m"] / 2), Y, s * X,
                 D - 2 * t - 0.012, P["batten_h_m"], P["batten_w_m"], group="lid")
    zt = zl + P["lid_t_m"] + bt / 2
    for s in (-1, 1):
        xs = s * P["strap_x_m"]
        yf = -D / 2 - bt / 2
        pr.poly_sweep(b, f"strap_{'LR'[s > 0]}", [(xs, D / 2 - 0.004, zt), (xs, yf, zt), (xs, yf, zl + 0.002)],
                      X, bt, P["strap_w_m"], c=0.0006, group="lid", slot=IRON)
        for v in (-0.17, 0.0, 0.17):
            pr.rivet(b, (xs, v, zt + bt / 2), Z, r=rr, group="lid", slot=IRON)
        pr.rivet(b, (xs, yf - bt / 2, zl + 0.011), -Y, r=rr, group="lid", slot=IRON)
    return {
        "materials": [Material("wood", **P["wood"]), Material("iron", **P["iron"]),
                      Material("rope", colour=(150, 128, 92), dirt=0.2)],
        "nodes": {"lid": {"group": "lid", "pivot": (0.0, D / 2, H)}},
        "sockets": [{"name": "SOCK_interact_take", "at": (0.0, 0.0, 0.85 * (zt + bt / 2))}],
        "bake_lift": {"lid": 1.0},
        "no_clash": [("body", "lid")],
        "notes": ["procgen_lib self-test: every subsystem end to end on one small asset"],
    }


if __name__ == "__main__":
    run_template(build, PARAMS, "procgen_sample_banded_box", archetype="plank_box_container (self-test)",
                 self_test=True, tri_budget=12000, px_per_m=1024)
