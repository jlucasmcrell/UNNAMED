"""The Phase A versus production table for built characters, from their stage reports and meshes (markdown).

    python character_report.py specs/player.json specs/kera.json ...
"""
import json
import os
import sys

import numpy as np

from build_character import path
from glb import Glb


def densities(work, prefix):
    g = Glb(os.path.join(work, "assembled.glb"))
    names = np.array([m["name"] for m in g.json["materials"]])
    m = g.mesh()
    P, F, UV, mat = m["POSITION"], m["faces"], m["TEXCOORD_0"], m["material"]
    a3 = 0.5 * np.linalg.norm(np.cross(P[F[:, 1]] - P[F[:, 0]], P[F[:, 2]] - P[F[:, 0]]), axis=1) * 1e4
    e1, e2 = UV[F[:, 1]] - UV[F[:, 0]], UV[F[:, 2]] - UV[F[:, 0]]
    auv = 0.5 * np.abs(e1[:, 0] * e2[:, 1] - e1[:, 1] * e2[:, 0]) * 4096 ** 2
    chin = json.load(open(os.path.join(work, "graft.json")))["z_chin"]
    cen = P[F].mean(1)
    face = (names[mat] == prefix + "_skin") & (cen[:, 1] > chin) & (cen[:, 2] > 0.02)
    body = (names[mat] != "eye") & (cen[:, 1] < chin - 0.25)
    d = lambda s: float(np.sqrt(auv[s].sum() / max(a3[s].sum(), 1e-9)))  # noqa: E731
    return d(face), d(body), [str(n).replace(prefix + "_", "") for n in names]


def main():
    rows = ["| Character | Phase A triangles (visible) | Production LOD0 / LOD1 / LOD2 | Face px/cm | Body px/cm | Materials | Bones |",
            "|---|---|---|---|---|---|---|"]
    for spec_path in sys.argv[1:]:
        spec = json.load(open(spec_path))
        work = path(spec["work"])
        clean = json.load(open(os.path.join(work, "clean.json")))
        lods = json.load(open(os.path.join(work, "lods.json")))
        rig = json.load(open(os.path.join(work, "rig.json")))
        face, body, mats = densities(work, spec.get("material_prefix", "MAT_" + spec["id"]))
        visible = clean["input_faces"] - clean["deleted_faces"]
        if spec.get("phase_a"):  # a regenerated body: Phase A's own mesh, measured apart
            clean = {"input_faces": spec["phase_a"]["triangles"]}
            visible = spec["phase_a"]["visible"]
        levels = " / ".join(f"{x:,}" for x in [lods["lod0"]["triangles"]] + [l["triangles"] for l in lods["levels"]])
        rows.append(f"| {spec['id']} | {clean['input_faces']:,} ({visible:,}) | {levels} | {face:.1f} | {body:.1f} | "
                    f"{len(mats)}: {', '.join(mats)} | {rig['bones']} |")
    print("\n".join(rows))


if __name__ == "__main__":
    main()
