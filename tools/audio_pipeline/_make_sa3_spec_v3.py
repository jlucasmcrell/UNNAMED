"""Build the V3 prompt spec, written the way the model's own documentation says to write it.

V2 prompts were comma-separated keyword lists with a duration stated as prose ("0.42 seconds long").
Three things in the official material say that was the wrong shape:

  `TrackType: SFX` is a metadata tag the Stability prompting guide says "tends to produce more
  semantically reasonable sound effects and samples". No V2 prompt carried it.

  The guide names three elements for a sound effect - the core source, the action including how long
  it lasts, and the production characteristics such as mic placement and room. V2 prompts named a
  source and an action and stopped.

  Every sound-effect example in ComfyUI's own day-0 post ends with `Length: N seconds` as a token. V2
  stated duration as English prose in the middle of a keyword list.

The guide also notes the model is trained on Freesound and AudioSparx metadata and that prompts
aligning with that kind of text work best, which is why the examples read as prose sentences rather
than tag soup.

**Length is the other half of this pass.** In V2 the single-transient trim clamped a sound to its first
energy lobe and floored it at `min_fraction` of the requested length - 18% for UI - so 101 of 228
delivered sounds came out under 75% of their spec, and every UI sound arrived at 18%. V3 sets
`single_transient: false` throughout, so the trim cuts the leading silence and then takes exactly the
spec's length. The delivered length is then checked against the spec and any mismatch is re-rendered.

Usage:
    python _make_sa3_spec_v3.py --audit
    python _make_sa3_spec_v3.py --apply
"""
import argparse
import importlib.util
import io
import json
import os
import re

ASSETS = r"W:\UNNAMED\assets"
V2_SPEC = os.path.join(ASSETS, "manifests", "audio_spec_v2.json")
V3_SPEC = os.path.join(ASSETS, "manifests", "audio_spec_v3.json")

# Production characteristics, written to avoid contradicting the body. V2 prompts already end with an
# acoustic-space clause and a character clause, and this pass supplies both, so the body's copies are
# dropped rather than restated - an earlier attempt left `animated_armour.walk` saying "indoors" in the
# body and "recorded outdoors" in the production sentence. These therefore describe mic placement and
# treatment only, not location, because location varies within a group: the armoured skeleton and the
# bone husk walk on stone indoors while the hound and boar move outdoors.
PRODUCTION = {
    "player.footstep": "Recorded close to the ground with a single microphone, dry and tight.",
    "player.condition": "Recorded close and intimate, the body itself the only source.",
    "player.gear": "Recorded close on a boom, dry and detailed.",
    "weapon.ready": "Recorded close in a dry space with no room character.",
    "weapon.swing": "Recorded close, moving air only.",
    "weapon.impact": "Recorded close and dry at short range.",
    "creature.idle": "Captured close enough to hear the body working, with the surrounding air around "
                     "it.",
    "creature.alert": "Captured close enough to feel the chest resonance.",
    "creature.attack": "Captured close, at arm's length.",
    "creature.hurt": "Captured close.",
    "creature.death": "Captured at a short distance with open air around it.",
    "creature.move": "Captured low to the ground with a single microphone, dry and detailed.",
    "magic": "Recorded close, dry.",
    # The brief requires the Strain cues to stay audible on ordinary headphones, desktop speakers and
    # laptop speakers, and V1 failed that: the moderate layer had a 61 Hz centroid, below what a laptop
    # speaker reproduces. The requirement has to be written into the prompt, because the earlier
    # version lost it when the V2 character clause was stripped as scaffolding.
    "strain": "Heard close and internal, dry and contained, with a clear mid-range presence that "
              "stays audible on laptop speakers rather than sitting below them.",
    "interaction": "Recorded close and dry at hand distance.",
    "crafting": "Recorded close with a short natural tail.",
    "ambience": "A continuous open-air field recording.",
    "ambience.one_shot": "Captured at mid distance in open air.",
    "ui": "Recorded very close and dry with no room character.",
}

# The physical source, stated as an object rather than a category, because the guide asks for "exactly
# what object, instrument, or synthesizer is making the sound".
SOURCES = {
    "sfx.ui.menu.open": "a small wooden panel swinging open on leather hinges",
    "sfx.ui.menu.close": "a small wooden panel settling closed against a frame",
    "sfx.ui.select": "a short hardwood stick tapped once against a wooden block",
    "sfx.ui.back": "a hand pulling a leather flap back over a wooden frame",
    "sfx.ui.equip": "leather straps and small steel buckles being pulled onto a body",
    "sfx.ui.error": "a soft mallet struck once against a hollow wooden box",
}

# Actions, phrased as the guide asks - how the sound is triggered and how long it lasts.
ACTIONS = {
    "sfx.ui.menu.open": "one quick movement with a short decay",
    "sfx.ui.menu.close": "one soft closing contact that stops dead",
    "sfx.ui.select": "one contact with a very fast decay and no ring",
    "sfx.ui.back": "a slow soft return movement, much quieter than a selection",
    "sfx.ui.equip": "a short tug and settle with several small fittings arriving together",
    "sfx.ui.error": "one dull blow with no high frequency and a fast decay",
}


LEADING_TAGS = "TrackType: SFX,"


def load_v2_builder():
    """The V2 spec builder, for its own SPACE and CHARACTER strings.

    Those strings contain commas, so a V2 prompt cannot be decomposed by splitting on commas - the
    acoustic space and the character note are one field each but several comma-separated pieces. The
    only reliable way to remove them is to match the exact strings that were appended.
    """
    spec = importlib.util.spec_from_file_location(
        "_make_sa3_spec", os.path.join(os.path.dirname(os.path.abspath(__file__)),
                                        "_make_sa3_spec.py"))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def prose(prompt, group):
    """Turn a V2 prompt into a description, keeping the sound and dropping the scaffolding.

    V2 was assembled as <core>, <N> seconds long, <space>, <character>. The duration becomes a
    Length: token and the other two are supplied by the production sentence here, so all three are
    removed. Removing them by comma-splitting does not work: it left prompts saying the same thing in
    two adjacent sentences, and in one case a body saying the sound was indoors while the production
    sentence said outdoors.
    """
    v2 = load_v2_builder()
    text = prompt.strip()

    text = re.sub(r",\s*\d+(\.\d+)?\s*seconds? long\s*", ", ", text, flags=re.I)
    text = re.sub(r"^\s*\d+(\.\d+)?\s*seconds? long\s*,\s*", "", text, flags=re.I)

    for suffix in (v2.SPACE.get(group), v2.CHARACTER.get(group)):
        if suffix:
            text = text.replace(", " + suffix, "").replace(suffix, "")

    parts = [p.strip() for p in text.split(",") if p.strip()]
    if not parts:
        return prompt.rstrip(".") + "."

    # Joining every remaining clause into one sentence reads as a list again. Two at most: what it is
    # and what it does, then the detail.
    head = ", ".join(parts[:3])
    tail = ", ".join(parts[3:])
    sentence = (head[0].upper() + head[1:]).rstrip(",.") + "."
    if tail:
        sentence += " " + (tail[0].upper() + tail[1:]).rstrip(",.") + "."
    return sentence


def build(entry):
    audio_id = entry["audio_id"]
    group = entry["group"]

    if audio_id in SOURCES:
        body = f"{SOURCES[audio_id]}, {ACTIONS[audio_id]}.".replace(",.", ".")
        production = PRODUCTION.get(group, "Recorded close and dry.")
    else:
        body = prose(entry["prompt"], group)
        production = PRODUCTION.get(group, "Recorded close and dry.")

    prompt = (f"{LEADING_TAGS} {body} {production} "
              f"Length: {entry['seconds']:g} seconds")
    # Collapse any doubled punctuation the joins produced rather than leaving "no ring.. Recorded".
    prompt = re.sub(r"\.\s*\.", ".", prompt)
    prompt = re.sub(r",\s*\.", ".", prompt)
    return re.sub(r"\s+", " ", prompt).strip()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--audit", action="store_true")
    parser.add_argument("--apply", action="store_true")
    args = parser.parse_args()

    with io.open(V2_SPEC, encoding="utf-8") as handle:
        v2 = json.load(handle)

    entries = []
    for entry in v2["sounds"]:
        new_entry = dict(entry)
        new_entry["prompt"] = build(entry)
        new_entry["prompt_v2"] = entry["prompt"]
        new_entry["generation_version"] = "v3"
        # The whole point of this pass: the trim must not shorten the sound past its spec.
        new_entry["single_transient"] = False
        entries.append(new_entry)

    print(f"  ids                : {len(entries)}")
    print(f"  with TrackType: SFX: {sum(1 for e in entries if 'TrackType: SFX' in e['prompt'])}")
    print(f"  with Length: token : {sum(1 for e in entries if 'Length:' in e['prompt'])}")
    print(f"  single_transient   : {sum(1 for e in entries if e['single_transient'])} (was "
          f"{sum(1 for e in v2['sounds'] if e.get('single_transient'))})")
    print()
    for audio_id in ("sfx.ui.select", "sfx.creature.bristleback_boar.attack.01",
                     "sfx.amb.charwood.stream_detail.01"):
        entry = next(e for e in entries if e["audio_id"] == audio_id)
        print(f"  {audio_id}")
        print(f"     V2: {entry['prompt_v2'][:130]}")
        print(f"     V3: {entry['prompt']}")
        print()

    if not args.apply:
        print("  (audit only; pass --apply to write)")
        return 0

    document = {
        "version": 3,
        "comment": [
            "V3 prompt spec, written to the model's own documentation.",
            "",
            "TrackType: SFX is a metadata tag the Stability prompting guide says produces more",
            "semantically reasonable sound effects. Prompts are prose describing the core source, the",
            "action and the production, because the guide names those as the three elements and notes",
            "the model is trained on Freesound and AudioSparx metadata. Every prompt ends with a",
            "Length: token, as every sound-effect example in ComfyUI's day-0 post does.",
            "",
            "single_transient is false throughout. In V2 the lobe trim clamped sounds to 18-55% of",
            "their spec, so 101 of 228 arrived under 75% of the intended length and every UI sound",
            "arrived at 18%. V3 takes exactly the spec's length after cutting leading silence.",
            "",
            "Ids, groups, durations, channel policy and loop flags are unchanged from V2, so the",
            "game-facing event contract does not move.",
        ],
        "authority": v2.get("authority"),
        "model": "Stable Audio 3 Small SFX",
        "settings": {"steps": 8, "cfg": 1.0, "sampler": "lcm", "scheduler": "simple",
                     "negative_prompt": None,
                     "candidates_per_id": 1},
        "counts": {"total": len(entries)},
        "sounds": entries,
    }
    with io.open(V3_SPEC, "w", encoding="utf-8") as handle:
        json.dump(document, handle, indent=2)
        handle.write("\n")
    print(f"  wrote {V3_SPEC}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
