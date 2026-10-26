"""Siann skin mask in a HumGen body's UV space (RACES.md 3 and the owner's reference, 2026-10-25).

R: where the mother-of-pearl sheen is strongest - neck, upper chest, shoulders and face - over a gentle base everywhere.
G: the thin areas where silver-lavender subdermal traces show - temples, ears, throat and fingers.

usage: python siann_mask.py <body.glb> <out png>
"""
import sys

import numpy as np
from PIL import Image

from kal_stone_bake import body, dilate, raster, seg_dist, soft


def main():
    b = body(sys.argv[1])
    p, n, J = b["p"], b["n"], b["joints"]
    up = np.array([0, 1.0, 0])
    fwd = np.sign(J["LeftEye"][2] - J["Head"][2]) * np.array([0, 0, 1.0])
    weight = np.zeros((len(p), len(b["names"])))
    np.put_along_axis(weight, b["j"], b["w"], axis=1)
    bone = lambda *names: sum(weight[:, b["names"].index(x)] for x in names if x in b["names"])
    stature = J["Head"][1] - min(J["LeftFoot"][1], J["RightFoot"][1])
    s = stature / 1.45

    sheen = np.full(len(p), .35)
    sheen = np.maximum(sheen, np.clip(bone("Neck", "Head") * 1.2, 0, 1) * .9)
    chest = (p[:, 1] > J["UpperChest"][1] - .04 * s) * np.clip(n @ fwd * 1.5, 0, 1) * bone("UpperChest", "Chest", "LeftShoulder", "RightShoulder")
    sheen = np.maximum(sheen, np.clip(chest * 1.3, 0, 1))
    for side in ("Left", "Right"):
        sheen = np.maximum(sheen, .85 * soft(np.linalg.norm(p - J[side + "UpperArm"], axis=1), .05 * s, .12 * s))

    eyes = (J["LeftEye"] + J["RightEye"]) / 2
    temples = sum(soft(np.linalg.norm(p - (J[side + "Eye"] + (J[side + "Eye"] - eyes) * 1.6 + up * .01 * s - fwd * .03 * s), axis=1), .012 * s, .03 * s)
                  for side in ("Left", "Right"))
    ears = sum(soft(np.linalg.norm(p - (J[side + "Eye"] + (J[side + "Eye"] - eyes) * 2.5 - up * .015 * s - fwd * .085 * s), axis=1), .02 * s, .035 * s)
               for side in ("Left", "Right"))
    throat = soft(seg_dist(p, J["Neck"] + fwd * .045 * s - up * .02 * s, J["Head"] + fwd * .04 * s - up * .07 * s), .02 * s, .04 * s) * (n @ fwd > .2)
    fingers = np.clip(bone(*[side + f + seg for side in ("Left", "Right") for f in ("Index", "Middle", "Ring", "Little", "Thumb")
                             for seg in ("Proximal", "Intermediate", "Distal", "Metacarpal")]) * 1.5, 0, 1)
    thin = np.clip(np.maximum.reduce([temples, ears, throat, fingers * .8]), 0, 1)

    field, hit = raster(b, np.stack([np.clip(sheen, 0, 1), thin, np.zeros(len(p))], 1), 1024)
    img = dilate(field, hit)
    Image.fromarray((np.clip(img, 0, 1) * 255 + .5).astype(np.uint8), "RGB").save(sys.argv[2])
    print("SIANN", sys.argv[2], round(float(img[..., 0][hit].mean()), 3), round(float((img[..., 1][hit] > .3).mean()), 4))


if __name__ == "__main__":
    main()
