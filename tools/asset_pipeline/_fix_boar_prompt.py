"""Rewrite the bristleback boar's concept prompt so the reconstruction can succeed.

`creature_bristleback_boar` is the only one of the five Phase-1 archetypes whose image-to-3D pass
fails, and it fails reproducibly: two builds from the same concept both produced a tangle of flat
shards with no boar in it. The concept itself is good - a proper wild boar with tusks, snout, ears and
a mane ridge - so the fault is in the reconstruction, not the drawing.

The difference between this and the four that work is the surface. The hound has a short coat, the
husk is bare bone, the animated armour is hard plate and the spider is smooth chitin: all four are
solid, high-contrast silhouettes. The boar is the only one covered in long, separate bristles, and
fine hair strands are geometry the reconstructor has no way to resolve - they are thinner than its
own sampling, so they come back as disconnected sheets.

So the prompt drops the long bristles and keeps everything that makes the creature read: the stocky
low build, the barrel chest, the snout and tusks. The mane becomes a low raised ridge of short
coarse hair rather than a spiky fringe, and the hide is described as solid and matte so there is a
surface to reconstruct.

Note the second half of this file's history: the ash haft prompt failed the opposite way. It asked
for a bare stave and said "no metal, no head", and the generator drew a complete spear - naming the
forbidden parts summoned them. Both lessons are the same one from different directions: describe what
you want to see, and describe a surface the reconstructor can actually resolve.

Idempotent. Usage:
    python _fix_boar_prompt.py --audit
    python _fix_boar_prompt.py --apply
"""
import argparse
import io
import json
import shutil

REQUESTS = r"W:\UNNAMED\assets\requests\overnight_creatures.json"
BACKUP = r"W:\UNNAMED\assets\_superseded\overnight_creatures"

OLD_START = "bristleback boar, stocky low-slung quadruped"
NEW_PROMPT = (
    "bristleback boar, stocky low-slung quadruped with a deep barrel chest, heavy shoulders and "
    "short sturdy legs planted squarely, a single continuous matte hide of slate-grey leathery skin "
    "with a smooth solid surface, a low raised ridge of short stiff hair running along the spine "
    "from the shoulders to the hips like a brush crest, a long snout with a blunt wet nose and two "
    "curved pale tusks, small deep-set dark eyes and short pricked ears, a thin tail, standing in a "
    "neutral pose with all four legs visible and clearly separated, three-quarter view, lit clearly "
    "from the front left"
)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--audit", action="store_true")
    parser.add_argument("--apply", action="store_true")
    args = parser.parse_args()

    with io.open(REQUESTS, encoding="utf-8") as handle:
        entries = json.load(handle)

    changed = 0
    for entry in entries:
        if entry.get("id") != "creature_bristleback_boar":
            continue
        if entry["prompt"].startswith("bristleback boar, stocky low-slung quadruped with a deep "
                                      "barrel chest,"):
            print("  already rewritten")
            return 0
        entry["prompt"] = NEW_PROMPT
        entry["prompt_note"] = (
            "Rewritten because the image-to-3D pass fails reproducibly on this concept: two builds "
            "from the original prompt both returned flat shards, while the four archetypes with "
            "solid surfaces - hound, husk, armour, spider - all reconstructed cleanly. The original "
            "asked for 'coarse dark grey bristles', and separate hair strands are below the "
            "reconstructor's sampling resolution. The mane is now a solid ridge of short hair on a "
            "matte hide, and everything that makes the creature read - stocky build, barrel chest, "
            "snout, tusks - is unchanged."
        )
        changed = 1
        print("  rewrote creature_bristleback_boar")

    if not changed:
        raise SystemExit("creature_bristleback_boar not found in the request file")

    if not args.apply:
        print("  (audit only; pass --apply to write)")
        return 0
    shutil.copy2(REQUESTS, BACKUP + ".json.bak")
    with io.open(REQUESTS, "w", encoding="utf-8") as handle:
        json.dump(entries, handle, indent=2)
        handle.write("\n")
    print(f"  wrote {REQUESTS}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
