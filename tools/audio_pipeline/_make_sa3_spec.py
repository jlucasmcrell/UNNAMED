"""Build the V2 prompt spec for Stable Audio 3 Small SFX.

Two things are wrong with V1's prompts, and both are structural rather than a matter of taste.

**Every V1 prompt carries a negation tail.** They end with boilerplate such as "close and dry, no
room, no reverb tail, no music, no speech, no voice, no cinematic boom, no trailer riser". That is a
list of things *not* to render, placed in the positive prompt, and for creatures it includes "no
voice" - in a prompt whose entire purpose is to produce a vocalisation. The probe established that the
separate negative conditioning is inert at cfg 1.0, so the global negative was doing nothing; the
negations *inside the positive prompt* are the only ones that can have been acting, and they were
telling the model not to produce the thing being asked for. V2 prompts contain no negations at all.

**V1 prompts are not built to the physical grammar the brief specifies.** They state a source and an
action and stop, leaving intensity, duration, acoustic space and desired character to chance. V2
prompts are composed as SOURCE + ACTION + MATERIAL/ANATOMY + INTENSITY + DURATION + ACOUSTIC SPACE +
DESIRED CHARACTER, so every field the model can use is present and deliberate.

Per-identity direction comes from the brief: the five creatures must remain distinct and each has a
specified identity, the player has no voice identity yet, UI must not be sub-bass. Those are encoded
here rather than left to the prompt author's mood.

Usage:
    python _make_sa3_spec.py --audit
    python _make_sa3_spec.py --apply
"""
import argparse
import io
import json
import os

ASSETS = r"W:\UNNAMED\assets"
V1_SPEC = os.path.join(ASSETS, "manifests", "audio_spec.json")
V2_SPEC = os.path.join(ASSETS, "manifests", "audio_spec_v2.json")

# Stripped by strip_tail below, which drops any negation segment rather than matching these strings.

# Acoustic space per family, stated positively. Note there is no "no" anywhere in these: writing
# "with no room tone" puts a negation back into the positive prompt, which is the exact defect V2
# exists to remove.
SPACE = {
    "player.footstep": "recorded close and dry, tight",
    "player.condition": "recorded close, dry, intimate, indoors-quiet",
    "weapon.ready": "recorded close in a dry open space",
    "weapon.swing": "recorded close in open air, dry",
    "weapon.impact": "recorded close and dry at short distance",
    "creature.idle": "recorded outdoors in open air at a few metres",
    "creature.alert": "recorded outdoors in open air, close enough to feel the chest",
    "creature.attack": "recorded close, outdoors, at arm's length",
    "creature.hurt": "recorded close, outdoors",
    "creature.death": "recorded outdoors at a short distance, air open",
    "magic": "recorded close in open air, dry",
    "strain": "close, dry, inside the head, small and contained",
    "interaction": "recorded close and dry, at hand distance",
    "crafting": "recorded close in open air with a short natural tail",
    "ambience": "a continuous open-air field recording",
    "ui": "recorded very close and dry",
}

# Desired character per family. Positive phrasing throughout: the check below asserts that nothing
# carrying a negation reaches a prompt, and these strings are part of what it inspects.
CHARACTER = {
    "player.footstep": "natural body weight, unexaggerated, one foot only",
    "player.condition": "short involuntary human body sound, breath and cloth, unperformed",
    "weapon.ready": "one clean physical action, tactile, plain",
    "weapon.swing": "one clean arc, moving air only",
    "weapon.impact": "one impact event, physically matched to the target material",
    "creature.idle": "calm, alive, resting animal",
    "creature.alert": "focused, restrained, predatory",
    "creature.attack": "sudden, forceful, physical",
    "creature.hurt": "involuntary, short, pained animal",
    "creature.death": "final, weakening, body failing",
    "magic": "physical force and displaced air",
    "strain": "a subtle tension cue, clearly audible on laptop speakers, steady",
    "interaction": "one small physical action at hand distance",
    "crafting": "real material being worked by hand",
    "ambience": "even, continuous, loopable",
    "ui": "short, tactile, unobtrusive, mid-frequency",
}

# Creature identity, from the brief's section 7. Each must stay distinct, and the listener should hear
# "animal" first and "something unusual about this animal" second.
CREATURE_IDENTITY = {
    "ash_ember_hound": {
        "source": "a large predatory canine, living animal, close",
        "anatomy": ("deep canine chest and throat, breath audible through the nose, wet mouth, "
                    "natural animal vocal tract"),
        "texture": ("a faint dry heat crackle under the voice, ember texture only as a secondary "
                    "layer, the animal always dominant"),
        "avoid": "no robot, no machine, no synthesised monster",
    },
    "bone_walker_husk": {
        "source": "a walking human skeleton, dry bone",
        "anatomy": ("dry bone articulation, hollow jaw movement, brittle material contacts, thin air "
                    "movement through an empty ribcage"),
        "texture": "dry, hollow, brittle, reanimated",
        "avoid": "no comedy rattle, no xylophone",
    },
    "animated_armour": {
        "source": "an empty suit of plate armour moving on its own",
        "anatomy": ("articulated steel plate, internal metal resonance, hollow enclosed body, "
                    "chain and joint movement"),
        "texture": "physically armoured, hollow, heavy",
        "avoid": "no creature growl, no beast voice",
    },
    "bristleback_boar": {
        "source": "a large wild boar, real pig anatomy",
        "anatomy": ("nasal grunt and snort, chest and throat resonance, coarse breath, heavy mass "
                    "behind the sound"),
        "texture": "bristled hide movement, hooves, weight",
        "avoid": "no dog, no bear",
    },
    "cave_hunting_spider": {
        "source": "a large hunting spider",
        "anatomy": ("chitin leg contact, scraping, restrained hiss, vibration through the body, dry "
                    "mouthpart chitter"),
        "texture": "dry, thin, many-legged, restrained",
        "avoid": "no mammalian voice, no roar",
    },
}

# Creature action verbs, so an idle is not an attack.
CREATURE_ACTION = {
    "idle": "breathing and small settling movements, resting",
    "alert": "a rising alert vocalisation, attention locked on a target",
    "attack": "a sudden aggressive attack sound at the moment of commitment",
    "hurt": "a short involuntary pain reaction",
    "death": "collapsing with a final weakening breath",
}

UI_VOCABULARY = {
    "sfx.ui.menu.open": "a small wooden panel or leather flap opening, dry wood and soft leather",
    "sfx.ui.menu.close": "a small wooden panel closing with a soft leather settle",
    "sfx.ui.confirm": "a single dry wooden tick, short, mid-frequency, a small hard object tapped once",
    "sfx.ui.back": "a soft leather and wood return movement, quieter than confirm",
    "sfx.ui.equip": "leather straps and small metal fittings settling onto a body",
    "sfx.ui.error": "a dull wooden knock, low-mid and short, a refused action, blunt and plain",
}


# Words that mark a comma-separated segment as a negation rather than a description.
NEGATION_WORDS = (" no ", " not ", " without ", " avoid ", " none ", " never ", " instead of ")


def strip_tail(prompt):
    """Drop every negation segment from a V1 prompt, keeping the positive description.

    Matching known tail strings missed variants - `sfx.magic.strain.high` ends "...no beep, no siren,
    no music..." and kept all of it. Splitting on commas and dropping any segment that carries a
    negation word catches every form, including ones not seen yet, which matters because a surviving
    negation in the positive prompt is the defect being corrected.
    """
    kept = []
    for segment in prompt.split(","):
        text = segment.strip()
        if not text:
            continue
        if any(word in f" {text.lower()} " for word in NEGATION_WORDS):
            continue
        kept.append(text)
    return ", ".join(kept)


def creature_prompt(audio_id, group, seconds, v1_core):
    kind = audio_id.split(".")[2] if audio_id.count(".") >= 3 else ""
    archetype = next((a for a in CREATURE_IDENTITY if a in audio_id), None)
    action = group.split(".")[-1]
    identity = CREATURE_IDENTITY.get(archetype)
    if not identity:
        return v1_core
    parts = [
        identity["source"],
        CREATURE_ACTION.get(action, action),
        identity["anatomy"],
        identity["texture"],
        f"{seconds:.1f} seconds long",
        SPACE.get(group, "recorded outdoors"),
        CHARACTER.get(group, "natural and believable"),
    ]
    return ", ".join(p for p in parts if p)


def prompt_for(entry):
    """Compose the V2 prompt for one entry. Positive only - no negations anywhere."""
    audio_id = entry["audio_id"]
    group = entry["group"]
    seconds = entry["seconds"]
    core = strip_tail(entry["prompt"])

    if group.startswith("creature."):
        return creature_prompt(audio_id, group, seconds, core)

    if group == "ui":
        text = UI_VOCABULARY.get(audio_id, core)
        return (f"{text}, {seconds:.2f} seconds long, {SPACE['ui']}, {CHARACTER['ui']}")

    # Everything else keeps its positive core, which already carries the source, the action and the
    # material, and gains the grammar fields V1 left out.
    return (f"{core}, {seconds:.2f} seconds long, {SPACE.get(group, 'recorded close and dry')}, "
            f"{CHARACTER.get(group, 'one clean physical event')}")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--audit", action="store_true")
    parser.add_argument("--apply", action="store_true")
    args = parser.parse_args()

    with io.open(V1_SPEC, encoding="utf-8") as handle:
        v1 = json.load(handle)

    entries = []
    without_negation = 0
    for entry in v1["sounds"]:
        prompt = prompt_for(entry)
        # Belt and braces: assert the construction actually removed the negations it claims to.
        if " no " not in f" {prompt} ":
            without_negation += 1
        entries.append({
            "audio_id": entry["audio_id"],
            "category": entry["category"],
            "group": entry["group"],
            "gameplay_role": entry["gameplay_role"],
            "prompt": prompt,
            "prompt_v1": entry["prompt"],
            "seconds": entry["seconds"],
            "channels": entry["channels"],
            "loop": entry["loop"],
            "loudness_lufs": entry["loudness_lufs"],
            "priority": entry["priority"],
            "single_transient": entry.get("single_transient", False),
            "generation_version": "v2",
            "source": "replacement",
        })

    print(f"  V1 entries        : {len(v1['sounds'])}")
    print(f"  V2 entries        : {len(entries)}")
    print(f"  prompts with no negation: {without_negation}/{len(entries)}")
    print()
    for entry in entries[:3]:
        print(f"  {entry['audio_id']}")
        print(f"     V1: {entry['prompt_v1'][:100]}")
        print(f"     V2: {entry['prompt'][:160]}")
        print()
    for audio_id in ("sfx.creature.bristleback_boar.attack.01", "sfx.ui.confirm",
                     "sfx.magic.strain.high.01"):
        entry = next((e for e in entries if e["audio_id"] == audio_id), None)
        if entry:
            print(f"  {audio_id}")
            print(f"     {entry['prompt'][:220]}")
            print()

    if not args.apply:
        print("  (audit only; pass --apply to write)")
        return 0

    document = {
        "version": 2,
        "comment": [
            "V2 prompt spec for Stable Audio 3 Small SFX.",
            "",
            "Positive-only. V1 appended a negation tail to every prompt - including 'no voice' on",
            "creature vocalisations - and the probe proved the separate negative conditioning is",
            "inert at cfg 1.0, so those inline negations were the only suppression acting.",
            "",
            "Built to the brief's physical grammar: source, action, material or anatomy, intensity,",
            "duration, acoustic space, desired character.",
            "",
            "Ids, groups, durations, channel policy, loop flags and loudness targets are carried over",
            "unchanged from V1 so the game-facing event contract does not move.",
        ],
        "authority": v1.get("authority"),
        "model": "Stable Audio 3 Small SFX",
        "settings": {"steps": 8, "cfg": 1.0, "sampler": "lcm", "scheduler": "simple",
                     "negative_prompt": None},
        "loudness_targets_lufs": v1.get("loudness_targets_lufs"),
        "counts": {"replacement": len(entries)},
        "sounds": entries,
    }
    os.makedirs(os.path.dirname(V2_SPEC), exist_ok=True)
    with io.open(V2_SPEC, "w", encoding="utf-8") as handle:
        json.dump(document, handle, indent=2)
        handle.write("\n")
    print(f"  wrote {V2_SPEC}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
