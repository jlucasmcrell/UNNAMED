"""Character concept template: the bind-pose contract plus a character's identity -> _make_concepts.py requests.

A reconstructed character is only as riggable as the image it was built from. Kera's first regeneration
round (six seeds, a free-prose prompt) came back with the arms against the lats every time; the round that
worked spelled out the pose, the gaps and the framing before anything about the smith. This keeps that part
fixed (CONTRACT) and leaves the character as slots, so the next character is a spec file, not prose:

  pose       A-pose, arms straight and 35-45 deg from the body, open empty hands palms down, a triangle of
             background between each arm and the torso from the armpit to the hand; legs straight and apart
             with background between them down to the feet; front view, whole figure, centred
  surface    one plain light grey background, even light (the _make_concepts.py suffix adds the rest)
  build      rules that decide whether Pixal3D gives a solid, riggable mesh (RECONSTRUCTION): hair as one
             smooth solid mass close to the head, sleeves that close at the wrist, a hem that ends above the
             ankles so the legs stand apart under it, nothing held (a carried object lies flat against the body
             or is a separate prop), a face lit evenly and looking at the viewer

Spec (JSON; every text slot is a noun phrase or a sentence fragment, positively phrased - Z-Image Turbo does
not honour negation):
  {"asset_id": "npc_siann_archivist",
   "subject": "tall slender young Siann woman",               # "... of a single <subject>"
   "body": "...", "skin": "...", "face": "...", "hair": "...",  # identity, from the concept and canon
   "outfit": "...", "footwear": "soft grey leather boots",
   "carried": "... flat against her left hip ...",             # optional; never in the hands
   "canon": {"height_m": 1.8, "sources": [...]},              # recorded, not prompted
   "seeds": [111, 222, 333, 444, 555, 666]}

  python _character_concept.py --spec sel_spec.json --out concept_requests.json [--seeds 1 2 3] [--round r1]
  python _character_concept.py --check concepts/*.png        (measures the contract's gaps in each image)
Then: python _make_concepts.py concept_requests.json --out <staging>/concepts
"""
import argparse
import json
import os
import sys

CONTRACT = (
    "full body character reference sheet in A-pose, front view, of a single {subject}. "
    "POSE: both arms spread out wide to the sides, each arm held straight and angled downward at forty degrees, "
    "the elbows straight, the hands held far out from the hips at waist height, open empty hands with the palms "
    "facing down and the fingers spread slightly, a large triangle of empty light grey background clearly visible "
    "between each arm and the side of the body from the armpit to the hand, the armpits open; legs straight and set "
    "apart at shoulder width with a gap of background between the legs down to the {feet}, feet flat. The whole "
    "figure fully visible from the top of the head to the {feet}, centred, with empty space around it, a "
    "symmetrical 3D modelling reference pose."
)
IDENTITY = " BODY: {body}, {skin}. FACE: {face}. HAIR: {hair}. OUTFIT: {outfit}, {footwear}.{carried}"
RECONSTRUCTION = (
    " BUILD: the hair one smooth solid sculpted mass lying close to the head and body, every strand gathered into "
    "it; the sleeves closing snugly at the wrists so each arm is one clean shape; the hem of every garment ending "
    "above the ankles so both legs stand apart below it; the face evenly lit and looking straight at the viewer."
)
CLOSING = " Neutral even studio lighting, plain flat light grey background, no shadow on the background"
SLOTS = ("subject", "body", "skin", "face", "hair", "outfit", "footwear")


def build_prompt(spec):
    missing = [k for k in SLOTS if not spec.get(k)]
    if missing:
        raise SystemExit(f"spec is missing {missing}")
    feet = spec.get("feet_word") or spec["footwear"].split()[-1]
    carried = f" CARRIED: {spec['carried']}." if spec.get("carried") else ""
    return (CONTRACT.format(subject=spec["subject"], feet=feet)
            + IDENTITY.format(carried=carried, **{k: spec[k] for k in SLOTS if k != "subject"})
            + RECONSTRUCTION + CLOSING)


def requests(spec, seeds, round_tag="bind"):
    prompt = build_prompt(spec)
    return [{"id": f"{spec['asset_id']}_{round_tag}_s{s}", "asset_id": spec["asset_id"], "prompt": prompt,
             "seed": int(s), "width": spec.get("width", 1536), "height": spec.get("height", 1536)} for s in seeds]


# --------------------------------------------------------------------------------------------------------
# contract check on a rendered concept (numpy, PIL, scipy)

ARMPIT_BY = 0.36    # the arm gap must open by this share of the figure's height from the top (Kera s444: 0.32)
ARM_GAP_MIN = 0.12  # and stay open at least this share of the height
ARM_ANGLE_MIN = 30.0  # deg: how fast that gap widens down the arm (Kera's rejected arms-at-the-lats round
                      # measures under it, her accepted round over it)
LEG_GAP_BY = 0.90   # and the legs stand apart by this share (above the ankles)


def gaps(path, arm_band=(0.20, 0.62), leg_band=(0.75, 0.97)):
    """Background runs between the figure's pieces per image row: in the arm band a row that holds three
    pieces has a gap on each side of the torso (arm_gap_rows: the longest unbroken run of such rows, as shares
    of the height from the top); in the leg band two pieces are the legs standing apart (leg_gap_from)."""
    import numpy as np
    from PIL import Image
    im = np.asarray(Image.open(path).convert("RGB")).astype(float)
    from scipy import ndimage
    # the backdrop darkens toward the floor: take the background colour row by row from the two side strips
    bg = np.median(np.concatenate([im[:, :40], im[:, -40:]], axis=1), axis=1)
    raw = fg = ndimage.binary_opening(np.abs(im - bg[:, None, :]).max(2) > 18, iterations=2)
    # pale cloth on a pale backdrop breaks into islands: close small cracks (much narrower than an arm gap)
    # and fill what they enclose before taking the figure
    fg = ndimage.binary_fill_holes(ndimage.binary_closing(fg, iterations=6))
    lab, n = ndimage.label(fg)
    if n > 1:   # the figure is the largest piece; specks and the floor shadow's edge are not
        fg = lab == 1 + int(np.argmax(ndimage.sum(fg, lab, range(1, n + 1))))
    rows = np.nonzero(fg.sum(1) > 6)[0]
    top, bottom = int(rows.min()), int(rows.max())
    height = bottom - top
    cols = np.nonzero(fg.sum(0) > 6)[0]

    def runs(y, mask=None):
        r = np.diff(np.concatenate([[0], (fg if mask is None else mask)[y].astype(int), [0]]))
        s, e = np.nonzero(r == 1)[0], np.nonzero(r == -1)[0]
        return [(int(a), int(b)) for a, b in zip(s, e) if b - a > 4]

    fracs = np.arange(arm_band[0], arm_band[1] + 1e-9, 0.01)
    three = [len(runs(int(top + f * height))) >= 3 for f in fracs]
    # the arm gap opens at the armpit and stays open down to the hands: the longest unbroken run of
    # three-piece rows
    best, start = (None, None), None
    for f, ok in zip(list(fracs) + [fracs[-1] + 0.01], three + [False]):
        if ok and start is None:
            start = f
        elif not ok and start is not None:
            if best[0] is None or f - start > best[1] - best[0]:
                best = (start, f - 0.01)
            start = None
    widths, at = [], []
    head = np.nonzero(fg[top:top + max(int(0.05 * height), 1)].any(0))[0]
    centre = float(np.median(head)) if len(head) else float(np.median(cols))
    if best[0] is not None:     # arm-torso gaps down the upper 60 % of the run (the arms, not what hangs
        run_to = best[0] + 0.6 * (best[1] - best[0])            # below the hands), as shares of the height
        for f in np.arange(best[0], run_to + 1e-9, 0.01):
            r = runs(int(top + f * height))
            mid = [k for k, (a, b) in enumerate(r) if a <= centre <= b]     # the torso holds the centre line
            if mid and 0 < mid[0] < len(r) - 1:
                k = mid[0]
                widths.append((r[k][0] - r[k - 1][1] + r[k + 1][0] - r[k][1]) / 2.0 / height)
                at.append(f)
    # how fast the gap opens going down the arm is the arm's angle from the body: d(width)/d(height)
    slope = float(np.polyfit(at, widths, 1)[0]) if len(at) >= 4 else 0.0
    legs_mask = raw & fg
    legs = [f for f in np.arange(*leg_band, 0.01) if len(runs(int(top + f * height), legs_mask)) >= 2]
    out = {"image": os.path.basename(path), "height_px": height, "span_px": int(cols.max() - cols.min()),
           "arm_gap_rows": [round(float(b), 2) for b in best] if best[0] is not None else None,
           "arm_gap_width": round(float(np.mean(widths)), 3) if widths else 0.0,
           "arm_angle_deg": round(float(np.degrees(np.arctan(max(slope, 0.0)))), 1),
           "leg_gap_from": round(float(min(legs)), 2) if legs else None}
    arm = out["arm_gap_rows"]
    out["contract"] = ("pass" if arm and arm[0] <= ARMPIT_BY and arm[1] - arm[0] >= ARM_GAP_MIN
                       and out["arm_angle_deg"] >= ARM_ANGLE_MIN
                       and out["leg_gap_from"] is not None and out["leg_gap_from"] <= LEG_GAP_BY else "fail")
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--spec")
    ap.add_argument("--out")
    ap.add_argument("--seeds", type=int, nargs="*")
    ap.add_argument("--round", default="bind")
    ap.add_argument("--check", nargs="*")
    a = ap.parse_args()
    if a.check:
        for p in a.check:
            print(json.dumps(gaps(p)))
        return 0
    with open(a.spec, encoding="utf-8") as h:
        spec = json.load(h)
    reqs = requests(spec, a.seeds or spec.get("seeds", [111, 222, 333, 444, 555, 666]), a.round)
    with open(a.out, "w", encoding="utf-8") as h:
        json.dump(reqs, h, indent=2)
    print(f"{len(reqs)} requests -> {a.out}; prompt {len(reqs[0]['prompt'])} chars")
    return 0


if __name__ == "__main__":
    sys.exit(main())
