"""Build a ladder of alternative prompts for every sound, so a rejection does not need hand-authoring.

The probe established that the seed is not the lever - a new seed re-renders the same character - and
that the prompt is. So a rejected sound needs different wording, and if that wording has to be written
by hand for every rejection, then a listening pass needs the pipeline lead in the loop for each one.
That is the wrong shape for a job the owner does hundreds of times.

This composes the alternatives mechanically instead. Each sound gets three further prompts, each
changing the acoustic scene along axes that matter for its family: distance, scale, density, and
brightness. They are appended as a scene description rather than as adjectives on the original, so the
result stays a describable sound rather than a pile of modifiers.

**These are mechanical, not authored, and that is worth being plain about.** They will reliably produce
a different sound; they will not reliably produce a better one. The ladder exists so the first two
retries cost nothing and need nobody. When a sound has exhausted it, that is the point to involve a
person, because at that point the question is what the sound should be rather than how to reword it.

Usage:
    python _make_prompt_variants.py --audit
    python _make_prompt_variants.py --apply
"""
import argparse
import io
import json
import os

ASSETS = r"W:\UNNAMED\assets"
SPEC = os.path.join(ASSETS, "manifests", "audio_spec_v2.json")
OUT = os.path.join(ASSETS, "manifests", "audio_prompt_variants.json")

# Acoustic axes per family. Each entry is a scene rewrite, phrased as a description of a recording
# rather than a set of adjectives. Ordered so step 1 is the smallest departure from the original and
# step 3 is the largest, which means an owner who rejects twice gets a progressively different sound
# rather than three near-identical nudges.
AXES = {
    "ambience": [
        "much closer to the source with fine detail audible, a tighter and more immediate "
        "perspective than a wide ambient capture",
        "further away and more diffuse, the detail softened by open air, a wide ambient perspective",
        "denser and busier, many small events overlapping at once, a rich continuous texture",
        "sparser and more intermittent, long gaps between isolated events, a quiet and open texture",
    ],
    "ambience.one_shot": [
        "closer and much brighter, the fine high-frequency detail of the contact clearly audible",
        "further away and duller, softened by distance and air, a muffled distant perspective",
        "bigger and heavier, a much larger and more massive source than the original",
        "smaller and lighter, a compact and delicate source rather than a large one",
    ],
    "creature.idle": [
        "closer and more intimate, the breath and small movements of the body clearly audible",
        "further away across open ground with the surrounding air around it",
        "larger and heavier bodied, a bigger animal than the original",
    ],
    "creature.alert": [
        "quieter and more restrained, held back and controlled rather than projected",
        "louder and more forceful, projected with the full chest behind it",
        "shorter and sharper, a brief cut-off vocalisation rather than a sustained one",
    ],
    "creature.attack": [
        "closer and more violent, right at the microphone with no distance at all",
        "heavier and slower, a larger animal committing its full weight",
        "shorter and more sudden, the whole event compressed into a brief instant",
    ],
    "creature.hurt": [
        "quieter and more restrained, a smaller and more contained reaction",
        "sharper and higher, a tighter and more startled reaction",
    ],
    "creature.death": [
        "longer and slower, the collapse drawn out with more time between breaths",
        "quieter and closer, a small intimate final breath",
    ],
    "creature.move": [
        "harder and more percussive, each contact sharp and distinct against the surface",
        "louder and heavier, more body mass driving into the ground with each contact",
        "softer and more delicate, lighter contacts on a more yielding surface",
    ],
    "weapon.impact": [
        "harder and more percussive with a sharper attack and a shorter tail",
        "duller and heavier with more low body and less high-frequency ring",
        "longer with the material still resonating and settling after the strike",
    ],
    "weapon.swing": [
        "faster and more violent, the air displaced hard and fast",
        "slower and heavier, a larger weapon moving with more mass behind it",
    ],
    "weapon.ready": [
        "closer with more fine mechanical detail of the fittings and the material",
        "faster and more decisive, one clean motion with no hesitation",
    ],
    "ui": [
        "brighter and crisper with more high-frequency definition in the contact",
        "duller and softer with less high frequency and a smaller perceived size",
        "slightly longer with a short natural tail after the initial contact",
    ],
    "player.gear": [
        "closer with more fine detail of the cloth and the fittings",
        "heavier and more pronounced, larger and more substantial equipment",
    ],
    "player.footstep": [
        "harder and more percussive, the contact sharper and more defined",
        "softer and more cushioned, a more yielding surface underfoot",
    ],
    "player.condition": [
        "closer and more intimate, quieter and more internal",
        "louder and more exerted, more force and body behind it",
    ],
    "magic": [
        "harder and more physical with a sharper transient and more displaced air",
        "deeper and more resonant with more low body behind the event",
        "shorter and more contained, the whole event compressed into a brief instant",
    ],
    "strain": [
        "brighter and thinner with more high-frequency instability and less low body",
        "more obviously unstable with a wider wavering tonal drift",
        "grittier with fine high-frequency distortion across the whole texture",
    ],
    "crafting": [
        "harder and more percussive with a sharper attack and more grit",
        "heavier with more mass in the striking object and a deeper body",
        "closer with more fine material detail and less room around it",
    ],
    "interaction": [
        "closer with more fine mechanical detail of the moving parts",
        "heavier and larger, a bigger and more substantial object",
        "duller and softer, a smaller and lighter object",
    ],
}


def axes_for(group):
    if group in AXES:
        return AXES[group]
    # Longest matching prefix, so `creature.move` does not fall through to a generic set while
    # `ambience.one_shot` keeps its own.
    candidates = [key for key in AXES if group.startswith(key)]
    if candidates:
        return AXES[max(candidates, key=len)]
    return AXES["crafting"]


def variants_for(entry, count=3):
    """Three further prompts, each a different scene rather than a reworded original."""
    axes = axes_for(entry["group"])
    base = entry["prompt"].rstrip(" .")
    out = []
    for index in range(min(count, len(axes))):
        out.append(f"{base}. {axes[index]}")
    return out


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--audit", action="store_true")
    parser.add_argument("--apply", action="store_true")
    args = parser.parse_args()

    with io.open(SPEC, encoding="utf-8") as handle:
        spec = json.load(handle)

    ladder = {}
    families = {}
    for entry in spec["sounds"]:
        variants = variants_for(entry)
        if not variants:
            continue
        ladder[entry["audio_id"]] = variants
        families.setdefault(entry["group"], 0)
        families[entry["group"]] += 1

    print(f"  ids with a ladder     : {len(ladder)}")
    print(f"  variants per id       : {len(next(iter(ladder.values())))}")
    print(f"  total alternate prompts: {sum(len(v) for v in ladder.values())}")
    print()
    print("  groups covered:")
    for group in sorted(families):
        print(f"    {group:<24} {families[group]:>3} ids")
    print()
    for audio_id in ("sfx.amb.blackvein.rock_shift.01", "sfx.ui.select"):
        if audio_id in ladder:
            entry = next(e for e in spec["sounds"] if e["audio_id"] == audio_id)
            print(f"  {audio_id}")
            print(f"     now : {entry['prompt'][:140]}")
            for index, variant in enumerate(ladder[audio_id], 1):
                print(f"     v{index}  : ...{variant[-150:]}")
            print()

    if not args.apply:
        print("  (audit only; pass --apply to write)")
        return 0

    document = {
        "version": 1,
        "comment": [
            "Mechanical alternate prompts, three per sound, for re-rendering a rejected sound without",
            "hand-authoring. Each changes the acoustic scene along an axis that suits the family -",
            "distance, scale, density, brightness - appended as a description so the result stays a",
            "describable sound.",
            "",
            "These are generated, not authored. They will reliably produce a different sound and will",
            "not reliably produce a better one. They exist so the first retries are free; a sound that",
            "exhausts its ladder needs a person, because the question at that point is what the sound",
            "should be rather than how to reword it.",
        ],
        "axes_by_group": AXES,
        "ladder": ladder,
    }
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    with io.open(OUT, "w", encoding="utf-8") as handle:
        json.dump(document, handle, indent=2)
        handle.write("\n")
    print(f"  wrote {OUT}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
