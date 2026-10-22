"""Add the tactical line the target plate shows for a beast (spec 41, owner ruling 2026-10-22).

The plate must say one thing a hunter can act on - a tactic or a weakness - in the world's voice, ninety characters or fewer. It is written
from the creature's own content notes, never invented. Run after any batch of bestiary entries.
"""

import pathlib
import re

import json
import yaml

LORE = pathlib.Path(r"C:\Otherreach_Build\ds_lore_wt\content\lore")
CREATURES = pathlib.Path(r"C:\Otherreach_Build\ds_lore_wt\content\creatures")

# One line per beast, from its own notes: the tell, the weak side, the habit that gets a hunter killed.
PLATES = {
    "ember_hounds": "Ember-hot bite: no guard turns it. Watch the tell, be elsewhere when it lands.",
    "barrowback": "Passive until struck; then the herd is one animal. Break from the herd or be trampled.",
    "fellhorn": "Passive, and stands with the herd. Hurt it badly and it breaks â€” that is the way in.",
    "bone_walker_husk": "Long reach, long windup: step inside it. Arrows pass through bone, so close.",
    "cave_hunting_spider": "Near-blind, and it feels every footfall. Walk, do not run, and it loses you.",
    "animated_armour": "Plate turns a blow from the front. The open back of the helm takes all of one.",
    "ring_custodian": "Slow to turn: stay behind it. The housing's back is open where the index runs.",
    "bristleback_boar": "It lowers its head and cannot turn. Step aside, or put a rock in its way.",
    "wolf_grey": "No health to speak of: it is the pack that kills. Get a tree at your back.",
    "wolf_grey_alpha": "Bigger pack, and it holds the middle. Kill the alpha and the rest scatter.",
    "grey_wolf": "No health to speak of: it is the pack that kills. Get a tree at your back.",
    "grey_alpha": "The den's leader holds the middle. Kill it and the rest of the pack scatters.",
    "crag_walker": "It is what the crags make of a body. Slow, long reach: step inside the windup.",
}


def main():
    written = 0
    for path in sorted(LORE.glob("*.yaml")):
        if path.name.startswith("_"):
            continue
        doc = yaml.safe_load(path.read_text(encoding="utf-8"))
        studied = [s["ref"] for p in doc.get("pages") or [] for s in p.get("sources") or [] if s.get("how") == "studied"]
        slug = path.stem
        if not studied:
            continue
        plate = PLATES.get(slug)
        if plate is None:
            print("no plate written for", slug, "- it must have one, or its plate line is empty")
            continue
        if len(plate) > 90:
            raise SystemExit(f"{slug}: plate is {len(plate)} characters, over the ninety the plate allows")
        if "plate:" in doc:
            continue
        text = path.read_text(encoding="utf-8")
        lines = text.splitlines(keepends=True)
        for index, line in enumerate(lines):
            if line.startswith("category: "):
                lines.insert(index + 1, "plate: " + json.dumps(plate, ensure_ascii=False) + "\n")
                break
        path.write_text("".join(lines), encoding="utf-8")
        written += 1
        print("plated", slug, len(plate))
    print("wrote", written)


if __name__ == "__main__":
    main()
