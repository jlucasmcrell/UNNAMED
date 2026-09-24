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

# Keyed by the ids that actually exist in the spec. V1's UI prompts describe "a panel opening" for
# several different actions, so each id gets its own physical vocabulary rather than sharing one.
UI_VOCABULARY = {
    "sfx.ui.menu.open": "a small wooden panel or leather flap opening, dry wood and soft leather",
    "sfx.ui.menu.close": "a small wooden panel closing with a soft leather settle",
    "sfx.ui.select": "a single dry wooden tick, a small hard object tapped once",
    "sfx.ui.back": "a soft leather and wood return movement, quieter and duller than a select",
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


# New Phase-1 families the owner authorised in section 30A. V1 deliberately shipped no creature
# locomotion, no equipment foley, no crouch support and no positional world emitters.
#
# Section 30A-F is deliberately absent. It asks for impact-severity assets only if Claude's combat
# model already exposes a meaningful light/heavy or glancing distinction through the event contract,
# and says plainly that if gameplay does not expose it, new gameplay semantics must not be invented
# merely to justify audio. Nothing here inspects Claude's branch, so severity is recorded as deferred
# in the status document rather than guessed at.
#
# (audio_id, category, group, seconds, channels, loop, priority, prompt)
NEW_SOUNDS = [
    # A. Creature locomotion. Three variations per high-frequency movement family, and no
    # vocalisation inside a movement sound - the brief is explicit that movement must not become a
    # constant growl track.
    ("sfx.creature.ash_ember_hound.walk.01", "creature", "creature.move", 0.55, 1, False, "P0",
     "a large canine walking slowly on dry earth, four soft paw contacts in sequence, claws touching "
     "grit, heavy body above the feet, recorded outdoors in open air close to the ground, natural "
     "animal locomotion, one animal only"),
    ("sfx.creature.ash_ember_hound.walk.02", "creature", "creature.move", 0.55, 1, False, "P0",
     "a large canine walking on dry earth, paw pads and loose grit, uneven stride, body weight "
     "shifting, recorded outdoors at a few metres, natural quadruped locomotion"),
    ("sfx.creature.ash_ember_hound.walk.03", "creature", "creature.move", 0.55, 1, False, "P0",
     "a large canine stepping across dry packed ground, soft pads with light claw drag, close "
     "perspective, outdoors, natural animal gait"),
    ("sfx.creature.ash_ember_hound.run.01", "creature", "creature.move", 0.5, 1, False, "P0",
     "a large canine running at speed on dry earth, rapid overlapping paw impacts, grit kicked loose, "
     "heavy driving body weight, recorded outdoors close to the ground, pursuit pace"),
    ("sfx.creature.ash_ember_hound.run.02", "creature", "creature.move", 0.5, 1, False, "P0",
     "a large canine sprinting over dry ground, fast four-beat gallop, earth displaced by each paw, "
     "outdoors, close and physical"),
    ("sfx.creature.ash_ember_hound.run.03", "creature", "creature.move", 0.5, 1, False, "P0",
     "a large canine charging across packed earth, swift heavy pawfalls, loose grit scattering, "
     "outdoors, pursuit"),
    ("sfx.creature.bristleback_boar.walk.01", "creature", "creature.move", 0.6, 1, False, "P0",
     "a heavy wild boar walking on dry earth, cloven hooves making hard split contacts, considerable "
     "body mass above, coarse bristled hide shifting, recorded outdoors close to the ground"),
    ("sfx.creature.bristleback_boar.walk.02", "creature", "creature.move", 0.6, 1, False, "P0",
     "a heavy wild boar moving on dry packed ground, four hard hoof contacts with weight behind them, "
     "low body, outdoors"),
    ("sfx.creature.bristleback_boar.walk.03", "creature", "creature.move", 0.6, 1, False, "P0",
     "a heavy boar plodding across dry earth and gravel, split hooves striking, heavy mass, outdoors, "
     "close perspective"),
    ("sfx.creature.bristleback_boar.charge.01", "creature", "creature.move", 0.7, 1, False, "P0",
     "a large wild boar charging at full weight, fast heavy hoof impacts tearing at dry earth, loose "
     "gravel thrown, unstoppable forward mass, recorded outdoors close to the ground"),
    ("sfx.creature.bristleback_boar.charge.02", "creature", "creature.move", 0.7, 1, False, "P0",
     "a wild boar at a full sprint, hard splitting hooves on dry ground, grit and earth displaced, "
     "great weight driving forward, outdoors, close"),
    ("sfx.creature.bristleback_boar.charge.03", "creature", "creature.move", 0.7, 1, False, "P0",
     "a heavy boar rushing forward over dry ground, dense rapid hoofbeats, loose stone scattering, "
     "outdoors, physical and immediate"),
    ("sfx.creature.bone_walker_husk.walk.01", "creature", "creature.move", 0.8, 1, False, "P0",
     "a human skeleton walking, dry bone foot contacts on stone, restrained articulation of ankle and "
     "knee joints, brittle hollow resonance in an empty frame, dry and small, indoors stone floor"),
    ("sfx.creature.bone_walker_husk.walk.02", "creature", "creature.move", 0.8, 1, False, "P0",
     "a walking skeleton on stone, hard dry bone steps, loose joint movement, hollow ribcage "
     "resonance, brittle and thin, close perspective"),
    ("sfx.creature.bone_walker_husk.walk.03", "creature", "creature.move", 0.8, 1, False, "P0",
     "dry skeletal footsteps on stone with small bone articulation between steps, hollow body, "
     "restrained, indoors"),
    ("sfx.creature.bone_walker_husk.move_fast.01", "creature", "creature.move", 0.7, 1, False, "P0",
     "a skeleton moving quickly, rapid dry bone footfalls on stone, loose joint chatter, hollow "
     "frame resonating, brittle and hurried, indoors close perspective"),
    ("sfx.creature.bone_walker_husk.move_fast.02", "creature", "creature.move", 0.7, 1, False, "P0",
     "fast skeletal movement across stone, quick hard bone contacts, articulation rattling thinly, "
     "dry hollow body, indoors"),
    ("sfx.creature.animated_armour.walk.01", "creature", "creature.move", 0.9, 1, False, "P0",
     "an empty suit of plate armour walking, heavy steel sabatons on stone, articulated knee and hip "
     "plate moving, hollow enclosed torso resonance, chain mail settling inside, weighted and "
     "metallic, indoors"),
    ("sfx.creature.animated_armour.walk.02", "creature", "creature.move", 0.9, 1, False, "P0",
     "plate armour striding on stone, iron-shod feet landing heavily, joints of articulated steel, "
     "empty interior resonance, massive and hollow, indoors close"),
    ("sfx.creature.animated_armour.walk.03", "creature", "creature.move", 0.9, 1, False, "P0",
     "a hollow suit of armour taking slow heavy steps, steel on stone, plates grinding at the joints, "
     "loose chain within, indoors, weighted"),
    ("sfx.creature.animated_armour.move_heavy.01", "creature", "creature.move", 0.8, 1, False, "P0",
     "heavy plate armour moving at speed, fast pounding steel footfalls on stone, violent joint "
     "articulation, hollow torso booming, chain rattling inside, indoors, imposing weight"),
    ("sfx.creature.animated_armour.move_heavy.02", "creature", "creature.move", 0.8, 1, False, "P0",
     "an armoured figure charging indoors, rapid iron footsteps on stone, plates clashing at the "
     "joints, empty enclosure resonance, heavy and urgent"),
    ("sfx.creature.cave_hunting_spider.scuttle.01", "creature", "creature.move", 0.7, 1, False, "P0",
     "a large hunting spider walking slowly, many chitinous leg tips tapping on dry stone in an "
     "irregular sequence, faint scraping, thin dry body vibration, restrained and close to the "
     "ground, indoors"),
    ("sfx.creature.cave_hunting_spider.scuttle.02", "creature", "creature.move", 0.7, 1, False, "P0",
     "a large spider stepping across dry stone, multiple hard leg contacts in quick uneven succession, "
     "light chitin scrape, close and dry, indoors"),
    ("sfx.creature.cave_hunting_spider.scuttle.03", "creature", "creature.move", 0.7, 1, False, "P0",
     "slow many-legged movement over dry rock, tapping chitin contacts, faint drag of the body, thin "
     "and restrained, indoors close perspective"),
    ("sfx.creature.cave_hunting_spider.scuttle_fast.01", "creature", "creature.move", 0.6, 1, False, "P0",
     "a large spider scuttling rapidly across dry stone, dense fast chitin leg contacts, sharp dry "
     "scraping, many legs, urgent and close to the ground, indoors"),
    ("sfx.creature.cave_hunting_spider.scuttle_fast.02", "creature", "creature.move", 0.6, 1, False, "P0",
     "rapid many-legged running over dry rock, quick hard leg taps and scrape, thin dry body, close "
     "perspective, indoors"),
    ("sfx.creature.cave_hunting_spider.scuttle_fast.03", "creature", "creature.move", 0.6, 1, False, "P0",
     "a large spider sprinting on stone, fast overlapping chitin contacts, dry scraping, urgent "
     "movement close to the ground, indoors"),

    # B. Player and equipment foley. Supporting layers, kept restrained.
    ("sfx.player.gear.cloth.01", "player", "player.gear", 0.5, 1, False, "P0",
     "light cloth garments shifting on a moving body, soft fabric folds and small settling "
     "movements, recorded close, quiet and dry"),
    ("sfx.player.gear.cloth.02", "player", "player.gear", 0.5, 1, False, "P0",
     "cloth and a light travelling pack shifting as a body turns, quiet fabric movement, close and "
     "dry, restrained"),
    ("sfx.player.gear.cloth.03", "player", "player.gear", 0.5, 1, False, "P0",
     "a coat and soft trousers moving with a slow step, quiet cloth folds, close, dry and small"),
    ("sfx.player.gear.metal.01", "player", "player.gear", 0.45, 1, False, "P0",
     "small metal buckles, straps and a hanging blade shifting on a moving body, light steel contact "
     "with leather, close and dry, restrained"),
    ("sfx.player.gear.metal.02", "player", "player.gear", 0.45, 1, False, "P0",
     "metal fittings and a sheathed weapon knocking softly against a belt, small hard contacts, close "
     "and dry, quiet"),
    ("sfx.player.gear.metal.03", "player", "player.gear", 0.45, 1, False, "P0",
     "a light harness of metal buckles moving with the body, brief steel and leather settling, close "
     "perspective, subdued"),
    ("sfx.player.weapon.sword.sheath.01", "player", "player.gear", 0.6, 1, False, "P0",
     "a steel sword sliding fully into a leather scabbard, smooth continuous friction, a soft final "
     "seat, close and dry, tactile"),
    ("sfx.player.weapon.bow.nock.01", "player", "player.gear", 0.4, 1, False, "P0",
     "an arrow nocked onto a wooden bowstring, string tension taken up, light wooden and feather "
     "contact, close and dry, one clean action"),
    ("sfx.player.weapon.spear.ready.01", "player", "player.gear", 0.5, 1, False, "P0",
     "a wooden spear shaft settling into a grip, hands adjusting on dry timber, a light hollow "
     "knock, close and dry, one clean physical action"),

    # C. Crouch support. Only the garment movement either side of the change, because the brief says
    # not to build a second footstep library and that crouched locomotion may reuse existing surface
    # footsteps with runtime gain changes.
    ("sfx.player.crouch.enter.01", "player", "player.gear", 0.6, 1, False, "P0",
     "a body lowering into a crouch, cloth and leather compressing and settling, a quiet shift of "
     "weight downwards, close and dry, restrained"),
    ("sfx.player.crouch.exit.01", "player", "player.gear", 0.6, 1, False, "P0",
     "a body rising out of a crouch, garments and gear unfolding and settling upward, light cloth "
     "and leather movement, close and dry, quiet"),

    # D. Loot and take-all feedback. One cue, not an inventory sound set.
    ("sfx.interaction.loot.take_all.01", "interaction", "interaction", 0.8, 1, False, "P1",
     "several small objects being swept up together into a leather pouch, overlapping light contacts "
     "of metal and wood against cloth, brief and efficient, close and dry, one motion"),

    # E. Positional environmental sweeteners. World emitters rather than additions to the stereo bed,
    # 2-4 per cell, and Foldscar stays restrained with no horror stingers.
    ("sfx.amb.ashen_hollow.forge_distant.01", "ambience", "ambience.one_shot", 1.6, 1, False, "P1",
     "a distant blacksmith hammer striking an anvil somewhere across a settlement, two blows, "
     "mid-distance outdoors, dulled by air and intervening timber, natural outdoor depth"),
    ("sfx.amb.ashen_hollow.timber_creak.01", "ambience", "ambience.one_shot", 2.0, 1, False, "P1",
     "a large timber building creaking as it settles, dry wooden structure under gentle load, close "
     "outside, quiet and slow, outdoors"),
    ("sfx.amb.ashen_hollow.settlement_activity.01", "ambience", "ambience.one_shot", 2.5, 1, False, "P1",
     "muffled distant activity in a small settlement, timber doors and wooden objects moving far "
     "away, indistinct and wordless, outdoors at long distance, quiet"),
    ("sfx.amb.charwood.raven_call.01", "ambience", "ambience.one_shot", 1.4, 1, False, "P1",
     "a single raven calling from a tree in a wood, sharp natural corvid vocalisation at mid "
     "distance, outdoor forest air, isolated bird"),
    ("sfx.amb.charwood.branch_movement.01", "ambience", "ambience.one_shot", 1.8, 1, False, "P1",
     "a branch moving through leaves in a wooded area, dry foliage brushing and a light wooden "
     "flex, outdoors in forest air at mid distance, quiet"),
    ("sfx.amb.charwood.stream_detail.01", "ambience", "ambience.one_shot", 2.5, 1, False, "P1",
     "close water running over small stones in a woodland stream, fine water detail, outdoors, "
     "continuous small movement"),
    ("sfx.amb.blackvein.pebble_fall.01", "ambience", "ambience.one_shot", 1.2, 1, False, "P1",
     "loose pebbles falling and bouncing down a rocky slope, several small hard stone contacts "
     "scattering to rest, outdoors in exposed air, close"),
    ("sfx.amb.blackvein.rock_shift.01", "ambience", "ambience.one_shot", 2.0, 1, False, "P1",
     "a heavy stone settling somewhere in a quarry, low grinding contact between rock surfaces, "
     "distant outdoors, weighty and slow"),
    ("sfx.amb.blackvein.winch_creak.01", "ambience", "ambience.one_shot", 1.8, 1, False, "P1",
     "an old wooden winch and rope creaking under tension, dry timber under load with a light iron "
     "fitting, outdoors in a quarry, quiet and slow"),
    ("sfx.amb.foldscar.tone_displacement.01", "ambience", "ambience.one_shot", 2.5, 1, False, "P1",
     "ordinary outdoor air with a faint unstable tonal drift inside it, a small spatial wobble on an "
     "otherwise plain wind sound, restrained, quiet, drifting and unsteady"),
    ("sfx.amb.foldscar.stone_resonance.01", "ambience", "ambience.one_shot", 2.0, 1, False, "P1",
     "a small stone resonating faintly in open air, dry mineral ring decaying quickly, outdoors "
     "among ruins, quiet and understated"),
]


# Which new groups are a single event rather than a sequence. A walk cycle is four paw contacts and
# must not be trimmed to its first lobe; a raven call or a loot sweep is one event and should be.
# V1 encodes the same distinction - its footsteps, impacts and UI ticks are single transients and its
# creature vocalisations are not.
SINGLE_TRANSIENT_GROUPS = {
    "creature.move": False,       # multi-step locomotion
    "player.gear": False,         # continuous garment and gear movement
    "interaction": True,          # one motion
    "ambience.one_shot": True,    # one discrete environmental event
}


def new_sound_entries():
    """The section 30A additions, as spec entries in the same shape as the replacements."""
    entries = []
    for audio_id, category, group, seconds, channels, loop, priority, prompt in NEW_SOUNDS:
        entries.append({
            "audio_id": audio_id,
            "category": category,
            "group": group,
            "gameplay_role": "authorised Phase-1 addition (section 30A)",
            "prompt": prompt,
            "prompt_v1": None,
            "seconds": seconds,
            "channels": channels,
            "loop": loop,
            "loudness_lufs": -30.0 if category != "ambience" else -34.0,
            "priority": priority,
            "single_transient": SINGLE_TRANSIENT_GROUPS.get(group, not loop),
            "generation_version": "v2",
            "source": "new",
        })
    return entries


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

    additions = new_sound_entries()
    existing = {e["audio_id"] for e in entries}
    clashes = [e["audio_id"] for e in additions if e["audio_id"] in existing]
    if clashes:
        print(f"  ERROR: new ids collide with existing ones: {clashes}")
        return 1
    entries.extend(additions)

    print(f"  V1 entries        : {len(v1['sounds'])}")
    print(f"  V2 replacements   : {len(v1['sounds'])}")
    print(f"  V2 additions      : {len(additions)}  (section 30A)")
    print(f"  V2 total          : {len(entries)}")
    bad = []
    for entry in entries:
        for word in NEGATION_WORDS:
            if word in f" {entry['prompt'].lower()} ":
                bad.append((entry["audio_id"], word.strip()))
    print(f"  prompts with a negation: {len(bad)}  {bad[:3]}")
    print()
    for entry in entries[:2]:
        print(f"  {entry['audio_id']}")
        print(f"     V1: {(entry['prompt_v1'] or '(new)')[:96]}")
        print(f"     V2: {entry['prompt'][:150]}")
        print()
    for audio_id in ("sfx.creature.bristleback_boar.attack.01", "sfx.ui.select",
                     "sfx.magic.strain.high.01", "sfx.creature.animated_armour.walk.01"):
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
        "counts": {"replacement": len(v1["sounds"]), "new": len(additions),
                   "total": len(entries)},
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
