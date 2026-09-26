"""A glowing strand for particle recipes (the Mending Thread's threads): a thin wavering line with a soft glow, both ends tapered to
nothing so a particle never shows a cut edge. Project-owned; written to the asset workspace with its record.

    python _gen_vfx_strand.py [--assets G:/UNNAMED_PHASEB/assets]
"""
import argparse
import json
import os

import numpy as np
from PIL import Image


def strand(width=64, height=512, seed=7):
    rng = np.random.default_rng(seed)
    y = np.linspace(0, 1, height)[:, None]
    x = np.linspace(-1, 1, width)[None, :]
    # A slow double sine: the thread wavers rather than standing ruler-straight.
    phase = rng.uniform(0, np.tau if hasattr(np, "tau") else 2 * np.pi, 2)
    centre = 0.22 * np.sin(y * 2 * np.pi * 1.3 + phase[0]) + 0.08 * np.sin(y * 2 * np.pi * 3.7 + phase[1])
    d = np.abs(x - centre)
    core = np.exp(-(d / 0.035) ** 2)
    glow = 0.45 * np.exp(-(d / 0.16) ** 2)
    taper = np.sin(np.pi * y) ** 1.5  # zero at both ends
    alpha = np.clip((core + glow) * taper, 0, 1)
    rgb = np.clip(0.75 + 0.25 * core, 0, 1)
    out = np.dstack([rgb * np.ones_like(alpha)] * 3 + [alpha])
    return Image.fromarray((out * 255).astype(np.uint8), "RGBA")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--assets", default=os.path.join(os.path.dirname(__file__), "..", "..", "assets"))
    a = ap.parse_args()
    folder = os.path.join(os.path.abspath(a.assets), "vfx")
    strand().save(os.path.join(folder, "gen_thread_strand.png"))
    json.dump({"effect_id": "gen_thread_strand", "authoring": "generated", "replaceable": True, "atlas": "vfx/gen_thread_strand.png",
               "atlas_px": [64, 512], "provenance": {"author": "Otherreach (tools/asset_pipeline/_gen_vfx_strand.py, Phase B remediation)",
                                                     "license": "project-owned"}},
              open(os.path.join(folder, "gen_thread_strand.json"), "w"), indent=1)
    print("wrote", os.path.join(folder, "gen_thread_strand.png"))


if __name__ == "__main__":
    main()
