"""Rewrite the creature locomotion prompts for one footfall each, and give the spider two steps.

The owner chose the footfall convention: one sound is one footfall, cycled by the engine, matching how
the player footsteps already work. The V2 section 30A prompts did not choose either convention, and one
of them was plainly self-contradictory - `ash_ember_hound.walk.01` asked for "four soft paw contacts in
sequence" at a length of 0.55 s, which is 137 ms per contact, a fast trot described as "walking
slowly". Prompt and duration could not both be right.

**The spider is deliberately different.** A spider's gait is not a sequence of footfalls the way a
mammal's is; eight legs move in overlapping groups, so a single "footstep" is not a meaningful unit.
The owner's answer was two steps per sound, cycled. The existing durations already fit exactly - 0.70 s
over two steps is 350 ms each, and 0.60 s is 300 ms - so nothing about the length has to change.

Durations are left alone throughout. A 0.9 s armour footfall is long for a contact, but the tail is
where the chain settling and the plate resonance live, and shortening it would move the event contract
for every creature. The prompts describe a sharp contact with a decaying tail, which is what the
length is for.

Usage:
    python _fix_creature_locomotion.py --apply
    python _render_fixed_locomotion.py --apply     (renders what this changed)
"""
import argparse
import io
import json
import os

ASSETS = r"W:\UNNAMED\assets"
SPEC = os.path.join(ASSETS, "manifests", "audio_spec_v3.json")
CHANGELOG = os.path.join(ASSETS, "manifests", "audio_locomotion_fix.json")

# One footfall per sound. Each prompt names a single contact and the material tail that follows it,
# which is what justifies the length without asking the model for a sequence.
FOOTFALLS = {
    "ash_ember_hound.walk": (
        "TrackType: SFX, one paw of a large canine landing on dry packed earth, a single soft pad "
        "contact with grit pressed under the claws. The heavy body weight arrives just after the "
        "contact and settles with a faint claw drag. Captured low to the ground with a single "
        "microphone, dry and detailed. Length: {n} seconds"),
    "ash_ember_hound.run": (
        "TrackType: SFX, one hard driving pawfall of a large canine at a run, a sharp impact on dry "
        "earth with grit thrown outward. The contact is fast and the loose debris scatters and settles "
        "behind it. Captured low to the ground with a single microphone, dry and detailed. "
        "Length: {n} seconds"),
    "bristleback_boar.walk": (
        "TrackType: SFX, one cloven hoof of a heavy boar striking dry earth, a hard split contact with "
        "considerable mass dropping onto it. The weight settles into the ground and coarse bristled "
        "hide shifts above. Captured low to the ground with a single microphone, dry and detailed. "
        "Length: {n} seconds"),
    "bristleback_boar.charge": (
        "TrackType: SFX, one heavy hoof of a charging boar tearing into dry gravel, a violent split "
        "impact with earth and stone thrown clear. Momentum carries through the contact and the loose "
        "ground scatters. Captured low to the ground with a single microphone, dry and detailed. "
        "Length: {n} seconds"),
    "bone_walker_husk.walk": (
        "TrackType: SFX, one dry bone foot of a walking skeleton landing on stone, a small hard hollow "
        "contact with a brittle resonance in an empty frame. The ankle and knee joints articulate "
        "loosely as the weight takes. Captured low to the ground with a single microphone, dry and "
        "detailed. Length: {n} seconds"),
    "bone_walker_husk.move_fast": (
        "TrackType: SFX, one snapping dry bone footstep as a skeleton moves quickly across stone, a "
        "hard hollow impact with loose joint articulation and thin resonance in an empty ribcage. "
        "Captured low to the ground with a single microphone, dry and detailed. Length: {n} seconds"),
    "animated_armour.walk": (
        "TrackType: SFX, one heavy steel sabaton of an empty suit of plate armour landing on stone, a "
        "weighted iron-shod impact that rings briefly in a hollow enclosed torso. The knee and hip "
        "plates articulate and chain mail settles inside after the contact. Captured low to the ground "
        "with a single microphone, dry and detailed. Length: {n} seconds"),
    "animated_armour.move_heavy": (
        "TrackType: SFX, one crashing plate-armoured footstep on stone at speed, a violent weighted "
        "impact with plates striking together at the joints and an empty torso booming around it. "
        "Chain rattles loose inside after the contact. Captured low to the ground with a single "
        "microphone, dry and detailed. Length: {n} seconds"),
}

# Two steps, not one. A spider does not have a footfall; it has overlapping leg groups, so the smallest
# unit that reads as locomotion is a pair of steps, and the owner asked for two cycled across the
# engine's repeat. The duration already accommodates it.
SPIDER_STEPS = {
    "cave_hunting_spider.scuttle": (
        "TrackType: SFX, two slow steps of a large hunting spider crossing dry stone, a group of "
        "chitinous leg tips setting down in quick irregular succession, twice. Each set-down is a "
        "small hard tap with a faint dry scrape and the thin body vibrating above it. Captured low to "
        "the ground with a single microphone, dry and detailed. Length: {n} seconds"),
    "cave_hunting_spider.scuttle_fast": (
        "TrackType: SFX, two rapid scuttling steps of a large hunting spider over dry rock, dense "
        "overlapping chitin contacts with sharp dry scraping, twice in quick succession. The body "
        "stays low and thin above the surface. Captured low to the ground with a single microphone, "
        "dry and detailed. Length: {n} seconds"),
}

# A fourth variant for the spider, matching the four the player's dirt footsteps carry. The engine
# picks at random per event and never repeats one back to back, so four is the widest that convention
# uses and the spider had three.
SPIDER_EXTRA = 4


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--apply", action="store_true")
    args = parser.parse_args()

    with io.open(SPEC, encoding="utf-8") as handle:
        spec = json.load(handle)
    by_id = {e["audio_id"]: e for e in spec["sounds"]}

    changes = []
    for entry in spec["sounds"]:
        if entry["group"] != "creature.move":
            continue
        audio_id = entry["audio_id"]
        key = None
        for candidate in list(FOOTFALLS) + list(SPIDER_STEPS):
            if audio_id.startswith("sfx.creature." + candidate + "."):
                key = candidate
                break
        if key is None:
            continue
        template = FOOTFALLS.get(key) or SPIDER_STEPS[key]
        new_prompt = template.format(n=f"{entry['seconds']:g}")
        changes.append({"audio_id": audio_id, "from": entry["prompt"], "to": new_prompt,
                        "convention": "two_steps" if key in SPIDER_STEPS else "one_footfall"})
        entry["prompt"] = new_prompt
        entry["locomotion_convention"] = ("two_steps" if key in SPIDER_STEPS else "one_footfall")

    # A fourth spider variant per family, cloned from the third so the spec's shape stays uniform.
    added = []
    for family, template in SPIDER_STEPS.items():
        base = f"sfx.creature.{family}"
        siblings = sorted(a for a in by_id if a.startswith(base + "."))
        if not siblings:
            continue
        next_index = max(int(a.rsplit(".", 1)[1]) for a in siblings) + 1
        if next_index > SPIDER_EXTRA:
            continue
        source = by_id[siblings[-1]]
        clone = dict(source)
        clone["audio_id"] = f"{base}.{next_index:02d}"
        clone["prompt"] = template.format(n=f"{source['seconds']:g}")
        clone["locomotion_convention"] = "two_steps"
        spec["sounds"].append(clone)
        added.append(clone["audio_id"])

    ones = sum(1 for c in changes if c["convention"] == "one_footfall")
    twos = sum(1 for c in changes if c["convention"] == "two_steps")
    print(f"  locomotion prompts rewritten: {len(changes)}")
    print(f"    one footfall               : {ones}")
    print(f"    two steps (spider)         : {twos}")
    print(f"  spider variants added        : {len(added)}  {added}")
    print(f"  spec total                   : {len(spec['sounds'])}")
    print()
    for c in changes[:2] + changes[-1:]:
        print(f"  {c['audio_id']}  [{c['convention']}]")
        print(f"     was: {c['from'][:110]}")
        print(f"     now: {c['to'][:150]}")
        print()

    if not args.apply:
        print("  (audit only; pass --apply to write)")
        return 0

    spec["counts"]["total"] = len(spec["sounds"])
    with io.open(SPEC, "w", encoding="utf-8") as handle:
        json.dump(spec, handle, indent=2)
        handle.write("\n")
    with io.open(CHANGELOG, "w", encoding="utf-8") as handle:
        json.dump({
            "comment": [
                "The owner chose the footfall convention: one sound is one footfall, cycled by the",
                "engine, matching the player's footsteps. The spider is deliberately two steps,",
                "because a spider's eight legs move in overlapping groups and a single footfall is",
                "not a meaningful unit for it. Its duration already fitted two steps exactly.",
                "",
                "Durations are unchanged. A 0.9 s armour footfall is long for a contact, but the tail",
                "is where the chain settling and plate resonance live, and shortening it would move",
                "the event contract.",
            ],
            "convention": "one_footfall_per_sound, spider two_steps",
            "rewritten": changes,
            "added_ids": added,
        }, handle, indent=2)
        handle.write("\n")
    print(f"  wrote {SPEC}")
    print(f"  wrote {CHANGELOG}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
