"""The Phase-1 audio specification: every sound the Ashen Hollow prototype needs, as data.

This is the single source of truth the generator, the normaliser, the manifest and the event contract
all read. Nothing downstream restates a prompt, a duration or a loudness target.

Content comes from `PHASE1_ASHEN_HOLLOW_PLAYABLE_CONTENT_BIBLE.md`, which is newer than
`PROTOTYPE.md` and renames several things: the five creatures are the Ash Ember Hound, Bone Walker
Husk, Animated Armour, Bristleback Boar and Cave Hunting Spider; the three formulas are Impulse Bolt
(Force), Brace Ward (Warding) and Mending Thread (Vital); and the craft chain is raw iron ore → Iron
Billet → March Spear. None of the older PROTOTYPE.md names appear here.

Prompts follow the brief's section 28 order: physical source, material, action, intensity, acoustic
space, then exclusions. "Epic sword sound" produces a trailer noise; "close dry one-handed steel
sword swing through open air, fast controlled arc" produces an asset.

Loudness targets are relative, not absolute. Section 25 is explicit that a sword swing must not end
up louder than a boar impact merely because its waveform peaked higher, so every entry declares the
target it should sit at in the prototype mix and the normaliser aims at that rather than at a
uniform peak.

Two parts of the brief disagree and this file resolves it in favour of coverage. Section 13 asks for
eleven or more sounds per creature across five creatures, and section 33 requires all five to have
identity sets; with the weapon, movement, magic, interaction, ambience and UI minimums that is
roughly 175 sounds, not the 60-100 section 37 suggests. Section 33 is the acceptance criterion, so
the required set is specified in full and section 37's spirit is honoured by generating one candidate
per id and curating, rather than generating forty candidates for each.

Usage:
    python _audio_spec.py --summary
    python _audio_spec.py --list --category creature
    python _audio_spec.py --emit-spec      # write assets/manifests/audio_spec.json
"""
import argparse
import collections
import io
import json
import os

ASSETS = r"W:\UNNAMED\assets"
OUT = os.path.join(ASSETS, "manifests", "audio_spec.json")

# Per-category loudness targets in LUFS, for the prototype mix. Section 25: relative design matters.
LOUDNESS = {
    "player.footstep": -30.0,
    "player.condition": -20.0,
    "weapon.swing": -24.0,
    "weapon.impact": -16.0,
    "weapon.ready": -26.0,
    "creature.idle": -28.0,
    "creature.alert": -24.0,
    "creature.attack": -20.0,
    "creature.hurt": -20.0,
    "creature.death": -19.0,
    "magic": -19.0,
    "strain": -32.0,
    "interaction": -22.0,
    "crafting": -20.0,
    "ambience": -30.0,
    "ui": -26.0,
}

# Shared prompt fragments, so the same acoustic space is described the same way everywhere.
SPACE_CLOSE_DRY = "close and dry, no room, no reverb tail"
SPACE_INTERIOR = "small stone interior, short natural decay"
SPACE_OUTDOOR = "outdoors in open air, no reverb"
NO_MUSIC = "no music, no speech, no voice, no cinematic boom, no trailer riser"


# Groups whose intended sound is a single transient. Stable Audio does not always oblige: a prompt
# for "a short clean wooden tick" produced four separate pulses across 220 ms, which as a UI
# confirm reads as a stutter. These groups are trimmed to the first energy lobe by the processing
# stage rather than to the requested length.
SINGLE_TRANSIENT_GROUPS = {
    "player.footstep", "weapon.impact", "ui", "weapon.ready", "interaction",
}
SINGLE_TRANSIENT_PREFIXES = (
    "sfx.crafting.mining.strike", "sfx.crafting.anvil.strike", "sfx.weapon.bow.release",
    "sfx.weapon.polearm.thrust", "sfx.weapon.sword.swing", "sfx.magic.impulse_bolt.impact",
)


def sfx(audio_id, category, group, role, prompt, seconds, channels=1, loop=False,
        priority="P0", alias_of=None):
    entry = {
        "audio_id": audio_id,
        "category": category,
        "group": group,
        "gameplay_role": role,
        "prompt": prompt,
        "seconds": seconds,
        "channels": channels,
        "loop": loop,
        "loudness_lufs": LOUDNESS[group],
        "priority": priority,
        "single_transient": bool(
            not loop and (group in SINGLE_TRANSIENT_GROUPS
                          or audio_id.startswith(SINGLE_TRANSIENT_PREFIXES))),
    }
    if alias_of:
        entry["alias_of"] = alias_of
    return entry


SPEC = []


def add_many(prefix, count, category, group, role, prompt_fn, seconds, channels=1, loop=False,
             start=1, priority="P0"):
    for index in range(start, start + count):
        SPEC.append(sfx(f"{prefix}.{index:02d}", category, group, role,
                        prompt_fn(index), seconds, channels, loop, priority))


# ---------------------------------------------------------------------------------------------
# 1. Player movement - section 7. Footsteps are the most repeated sound in the game, so they get
#    the most variation; sprint reuses the run set, which section 7 permits.
# ---------------------------------------------------------------------------------------------
for surface, texture in (
    ("dirt", "dry packed earth with a little loose grit"),
    ("stone", "hard flat quarry stone with grit on it"),
    ("wood", "hollow timber boards"),
):
    for gait, weight, seconds in (("walk", "a light careful step", 0.42),
                                  ("run", "a fast heavy step with a scuff", 0.34)):
        variations = 4 if surface == "dirt" else 3
        add_many(
            f"sfx.player.footstep.{surface}.{gait}", variations, "player", "player.footstep",
            f"{gait} footstep, surface tells the engine which surface is underfoot",
            lambda i, t=texture, w=weight: (
                f"a single {w} on {t}, {SPACE_CLOSE_DRY}, isolated footstep, "
                f"no other footsteps, {NO_MUSIC}"),
            seconds)
add_many("sfx.player.land.dirt", 1, "player", "player.footstep",
         "landing after a drop onto dirt",
         lambda i: f"a short heavy two-foot landing impact on {('dry packed earth')}, "
                   f"{SPACE_CLOSE_DRY}, {NO_MUSIC}", 0.55)
add_many("sfx.player.land.stone", 1, "player", "player.footstep",
         "landing after a drop onto stone",
         lambda i: "a short heavy two-foot landing impact on hard flat stone, "
                   f"{SPACE_CLOSE_DRY}, {NO_MUSIC}", 0.55)
add_many("sfx.player.jump.effort", 1, "player", "player.condition",
         "the effort of a jump; no dialogue, just breath and cloth",
         lambda i: "a short quiet human effort breath and cloth shift at the moment of a jump, "
                   f"restrained, no words, {SPACE_CLOSE_DRY}, no music", 0.5)

# ---------------------------------------------------------------------------------------------
# 2. Player damage and condition - section 8. Restrained breath and impact rather than voice
#    acting: the brief says not to establish a final player voice identity.
# ---------------------------------------------------------------------------------------------
add_many("sfx.player.hurt.light", 3, "player", "player.condition",
         "light hurt reaction: a short pained breath, no words",
         lambda i: f"a short quiet pained human breath and a small cloth shift, light injury, "
                   f"no words, no speech, {SPACE_CLOSE_DRY}, no music", 0.5)
add_many("sfx.player.hurt.heavy", 2, "player", "player.condition",
         "heavy hurt reaction: a hard exhale and armour/cloth jolt",
         lambda i: "a hard sudden human exhale with a jolt of cloth and leather, heavy injury, "
                   f"no words, no scream, {SPACE_CLOSE_DRY}, no music", 0.7)
add_many("sfx.player.downed", 1, "player", "player.condition",
         "the player goes down: knees hit the ground and the breath is knocked out",
         lambda i: "a body collapsing to its knees onto dirt, knocked-out breath, armour settling, "
                   f"no words, {SPACE_OUTDOOR}, no music", 1.6)
add_many("sfx.player.death", 1, "player", "player.condition",
         "the player dies; the final collapse",
         lambda i: "a body going limp and settling onto earth with a last long breath, "
                   f"no words, no music, {SPACE_OUTDOOR}, no cinematic swell", 2.4)

# ---------------------------------------------------------------------------------------------
# 3. One-handed sword - section 9. The distinction that matters is blade into flesh versus blade
#    into plate; a generic "impact" is the failure this set exists to avoid.
# ---------------------------------------------------------------------------------------------
add_many("sfx.weapon.sword.draw", 1, "weapon", "weapon.ready",
         "drawing the arming sword from a scabbard",
         lambda i: f"a single one-handed steel sword drawn from a leather scabbard, "
                   f"fast smooth pull, {SPACE_CLOSE_DRY}, {NO_MUSIC}", 0.7)
add_many("sfx.weapon.sword.swing.light", 3, "weapon", "weapon.swing",
         "a light sword swing through air",
         lambda i: "a light one-handed steel sword swing through open air, fast controlled arc, "
                   f"close and dry, {NO_MUSIC}", 0.5)
add_many("sfx.weapon.sword.swing.heavy", 2, "weapon", "weapon.swing",
         "a committed heavy sword swing",
         lambda i: "a heavy committed one-handed steel sword swing through open air, wide arc with "
                   f"a lower whoosh, close and dry, {NO_MUSIC}", 0.6)
add_many("sfx.weapon.sword.impact.flesh", 3, "weapon", "weapon.impact",
         "sword cutting into flesh and hide",
         lambda i: "a steel blade cutting into animal flesh and hide, wet dull impact with no ring, "
                   f"{SPACE_CLOSE_DRY}, {NO_MUSIC}", 0.4)
add_many("sfx.weapon.sword.impact.plate", 3, "weapon", "weapon.impact",
         "sword striking armour plate",
         lambda i: "a steel blade striking a steel plate with a hard metallic clang and a scrape, "
                   f"close and dry, {NO_MUSIC}", 0.55)
add_many("sfx.weapon.sword.impact.wood", 2, "weapon", "weapon.impact",
         "sword biting into timber",
         lambda i: "a steel blade biting into solid timber with a hard split and a short ring, "
                   f"{SPACE_CLOSE_DRY}, {NO_MUSIC}", 0.45)
add_many("sfx.weapon.sword.impact.stone", 2, "weapon", "weapon.impact",
         "sword striking stone",
         lambda i: "a steel blade striking hard stone with a sharp sparking scrape and chip, "
                   f"close and dry, {NO_MUSIC}", 0.5)
add_many("sfx.weapon.sword.block", 3, "weapon", "weapon.impact",
         "parrying another blade",
         lambda i: "two steel blades meeting in a hard parry, metal on metal with a short ring, "
                   f"{SPACE_CLOSE_DRY}, {NO_MUSIC}", 0.45)

# ---------------------------------------------------------------------------------------------
# 4. Bow - section 10. The March Spear in the bible's acquisition flow, so the bow is the found
#    weapon and arrows are the ranged path.
# ---------------------------------------------------------------------------------------------
add_many("sfx.weapon.bow.draw", 2, "weapon", "weapon.ready",
         "drawing the bowstring to tension",
         lambda i: "a wooden self bow drawn to tension, creaking limbs and a tightening string, "
                   f"{SPACE_OUTDOOR}, {NO_MUSIC}", 0.9)
add_many("sfx.weapon.bow.release", 3, "weapon", "weapon.swing",
         "releasing the bowstring",
         lambda i: "a bowstring released, sharp twang with a short string slap, "
                   f"{SPACE_OUTDOOR}, {NO_MUSIC}", 0.45)
add_many("sfx.weapon.bow.arrow.flight", 1, "weapon", "weapon.swing",
         "an arrow passing through the air",
         lambda i: "an arrow passing fast through open air, short aerodynamic whoosh, "
                   f"{SPACE_OUTDOOR}, {NO_MUSIC}", 0.6)
add_many("sfx.weapon.bow.arrow.impact.flesh", 3, "weapon", "weapon.impact",
         "arrow striking flesh",
         lambda i: "an arrowhead striking animal flesh and hide, compact dull wet thud, "
                   f"{SPACE_CLOSE_DRY}, {NO_MUSIC}", 0.35)
add_many("sfx.weapon.bow.arrow.impact.wood", 3, "weapon", "weapon.impact",
         "arrow striking timber",
         lambda i: "an arrowhead striking a solid timber board, hard knock with a short splinter, "
                   f"{SPACE_CLOSE_DRY}, {NO_MUSIC}", 0.4)
add_many("sfx.weapon.bow.arrow.impact.stone", 2, "weapon", "weapon.impact",
         "arrow striking stone",
         lambda i: "an arrowhead striking hard stone, sharp chip and a small sparking tick, "
                   f"{SPACE_CLOSE_DRY}, {NO_MUSIC}", 0.4)

# ---------------------------------------------------------------------------------------------
# 5. Spear - section 11. The spear is heavier than the sword and that difference is the point, so
#    its flesh and plate impacts are its own. Section 11 also says to reuse material impacts where
#    physically sensible, so its wood and stone impacts alias the sword's rather than being
#    regenerated as near-identical files.
# ---------------------------------------------------------------------------------------------
add_many("sfx.weapon.polearm.thrust", 3, "weapon", "weapon.swing",
         "a spear thrust through air",
         lambda i: "a long ash-hafted spear thrust through open air, fast forward whoosh with a "
                   f"lighter haft swish, close and dry, {NO_MUSIC}", 0.55)
add_many("sfx.weapon.polearm.handle", 2, "weapon", "weapon.ready",
         "hands adjusting the spear shaft",
         lambda i: "hands sliding along a wooden spear shaft, short leather and wood friction, "
                   f"{SPACE_CLOSE_DRY}, {NO_MUSIC}", 0.5)
add_many("sfx.weapon.polearm.impact.flesh", 3, "weapon", "weapon.impact",
         "spear driving into flesh",
         lambda i: "a spearhead driving deep into animal flesh, heavy dull penetration with a "
                   f"weighty follow-through, {SPACE_CLOSE_DRY}, {NO_MUSIC}", 0.5)
add_many("sfx.weapon.polearm.impact.plate", 3, "weapon", "weapon.impact",
         "spear striking plate armour",
         lambda i: "a spearhead striking a steel plate, heavy blunt metallic impact with a "
                   f"deflecting scrape, close and dry, {NO_MUSIC}", 0.6)

# Section 11 says to reuse material-impact families where that is physically sensible. A spear tip
# into timber does not change much with the length of the shaft behind it, so these two families
# point at the sword's rather than existing as near-identical files under a second name. They carry
# no audio of their own and are never generated or copied: the contract resolves `alias_of`, so
# there is no second file to drift out of sync.
for _material, _count, _seconds in (("wood", 2, 0.45), ("stone", 2, 0.5)):
    for _index in range(1, _count + 1):
        SPEC.append(sfx(
            f"sfx.weapon.polearm.impact.{_material}.{_index:02d}", "weapon", "weapon.impact",
            f"spear into {_material}; reuses the sword's {_material} impact",
            f"(alias of sfx.weapon.sword.impact.{_material}.{_index:02d}; no audio generated)",
            _seconds, alias_of=f"sfx.weapon.sword.impact.{_material}.{_index:02d}"))

# ---------------------------------------------------------------------------------------------
# 6. The five creature archetypes - section 13, with the section 14 direction for each. Each gets
#    its own identity; the brief is explicit that they must not be pitch-shifted versions of one
#    wolf.
# ---------------------------------------------------------------------------------------------
CREATURES = {
    "ash_ember_hound": {
        "idle": "a large canine breathing steadily, low and grounded, with a faint dry heat crackle",
        "alert": "a canine growl rising, focused and predatory, restrained",
        "attack": "a canine snapping bite and a fast snarl, close, no roar",
        "hurt": "a canine yelp of pain, short and sharp",
        "death": "a canine collapsing with a long last breath and a whine fading out",
    },
    "bone_walker_husk": {
        "idle": "dry old bone and desiccated ligament shifting slowly, hollow and quiet",
        "alert": "dry bones wrenching upright with a single sharp crack, alert",
        "attack": "a bone arm swinging with a brittle impact and a dry clatter, no comedy rattle",
        "hurt": "dry bones cracking and splintering under impact",
        "death": "a skeleton collapsing into a pile of dry bones, long settling clatter",
    },
    "animated_armour": {
        "idle": "empty plate armour shifting its weight, articulated steel with a hollow interior",
        "alert": "plate armour straightening with a heavy steel grind and a hollow clank",
        "attack": "an armoured gauntlet swinging with heavy steel weight and a plate impact",
        "hurt": "plate armour struck hard, deep resonant metal boom with a loose rivet rattle",
        "death": "plate armour falling apart and collapsing to the ground, plates scattering",
    },
    "bristleback_boar": {
        "idle": "a heavy boar breathing and snuffling, mass and wet breath, hooves shifting",
        "alert": "a boar snorting hard and scraping a hoof on the ground before a charge",
        "attack": "a boar charging impact and a tusked gore, heavy and grounded, no monster roar",
        "hurt": "a boar grunting in pain, angry rather than piteous",
        "death": "a heavy boar collapsing with a long grunting exhale and a final hoof scrape",
    },
    "cave_hunting_spider": {
        "idle": "many chitinous legs moving slowly on stone, quiet ticking and light scraping",
        "alert": "chitin legs tensing with a dry restrained hiss, close and unsettling",
        "attack": "a spider lunging with a fast chitin click and a sharp wet strike",
        "hurt": "chitin cracking under impact with a high agitated hiss",
        "death": "a spider curling up with legs scraping then going still and silent",
    },
}
CREATURE_VARIATIONS = {"idle": 2, "alert": 2, "attack": 3, "hurt": 2, "death": 2}
CREATURE_SECONDS = {"idle": 2.4, "alert": 1.2, "attack": 0.9, "hurt": 0.6, "death": 2.2}
for creature, descriptions in CREATURES.items():
    for kind, count in CREATURE_VARIATIONS.items():
        add_many(
            f"sfx.creature.{creature}.{kind}", count, "creature", f"creature.{kind}",
            f"{kind} audio for the {creature.replace('_', ' ')} archetype",
            lambda i, d=descriptions[kind]: (
                f"{d}, {SPACE_OUTDOOR}, {NO_MUSIC}, no music bed, no other creatures"),
            CREATURE_SECONDS[kind])

# ---------------------------------------------------------------------------------------------
# 7. The three formulas - section 17. Otherreach magic is reality behaving differently, not
#    sparkly fantasy sparkle, and each domain gets a distinct identity.
# ---------------------------------------------------------------------------------------------
add_many("sfx.magic.impulse_bolt.cast", 2, "magic", "magic",
         "casting Impulse Bolt: compressed force gathering, then released",
         lambda i: "compressed air pressure building fast then releasing in a single sharp "
                   f"displacement, kinetic force, no whoosh magic sparkle, {SPACE_OUTDOOR}, "
                   f"{NO_MUSIC}", 1.1)
add_many("sfx.magic.impulse_bolt.travel", 1, "magic", "magic",
         "the bolt crossing distance",
         lambda i: "a fast dense slug of displaced air travelling past, short, "
                   f"{SPACE_OUTDOOR}, no laser, no sci-fi hum, {NO_MUSIC}", 0.7)
add_many("sfx.magic.impulse_bolt.impact", 3, "magic", "magic",
         "the bolt landing: a hard kinetic pressure impact",
         lambda i: "a hard kinetic pressure impact, sudden air displacement and a deep concussive "
                   f"thud, no explosion, no fire, {SPACE_OUTDOOR}, {NO_MUSIC}", 0.6)
add_many("sfx.magic.brace_ward.activate", 2, "magic", "magic",
         "Brace Ward forming: a boundary coming under tension",
         lambda i: "a stabilized boundary forming, resonant tension across a surface, a low hum "
                   f"snapping into place, no choir, {SPACE_OUTDOOR}, {NO_MUSIC}", 1.3)
add_many("sfx.magic.brace_ward.hit", 2, "magic", "magic",
         "the ward absorbing an impact",
         lambda i: "an impact absorbed by a resonant barrier, dulled metallic tension ringing and "
                   f"dissipating, no shatter, {SPACE_OUTDOOR}, {NO_MUSIC}", 0.9)
add_many("sfx.magic.brace_ward.end", 1, "magic", "magic",
         "the ward dissipating",
         lambda i: "a resonant barrier relaxing and fading out over a moment, tension releasing, "
                   f"{SPACE_OUTDOOR}, {NO_MUSIC}", 1.2)
add_many("sfx.magic.mending_thread.cast", 2, "magic", "magic",
         "Mending Thread beginning: precise restorative resonance",
         lambda i: "a precise tight biological resonance beginning, fine high harmonic threads "
                   f"tightening, clinical and quiet, no choir, no bells, {SPACE_CLOSE_DRY}, "
                   f"{NO_MUSIC}", 1.2)
add_many("sfx.magic.mending_thread.resolve", 1, "magic", "magic",
         "the mending resolving: flesh and tissue settling",
         lambda i: "a soft resolution of fine tissue resonance, small settling and a quiet release "
                   f"of tension, {SPACE_CLOSE_DRY}, no choir, {NO_MUSIC}", 1.0)
add_many("sfx.magic.strain.moderate", 1, "magic", "strain",
         "Strain feedback, moderate band",
         lambda i: "a faint unstable resonance under the surface, low pressure tone wavering "
                   f"slightly, subtle, not an alarm, no beep, {NO_MUSIC}", 3.0, channels=2,
         loop=True)
add_many("sfx.magic.strain.high", 1, "magic", "strain",
         "Strain feedback, high band",
         lambda i: "a rising unstable resonance, pressure and fine tonal distortion, unsettling "
                   f"but not alarming, no beep, no siren, {NO_MUSIC}", 3.0, channels=2, loop=True)
add_many("sfx.magic.strain.critical", 1, "magic", "strain",
         "Strain feedback, critical band",
         lambda i: "a strained unstable resonance at the edge of failing, pressure and wavering "
                   f"tonal distortion, urgent but not an alarm, no beep, {NO_MUSIC}", 3.0,
         channels=2, loop=True)

# ---------------------------------------------------------------------------------------------
# 8. Interaction and crafting - sections 15 and 16. The bible's craft chain is raw iron ore ->
#    Iron Billet -> March Spear, gated at the forge in Cell A.
# ---------------------------------------------------------------------------------------------
add_many("sfx.interaction.pickup", 3, "interaction", "interaction",
         "picking an item up off the ground",
         lambda i: "a hand taking a small object off the ground, brief cloth and leather shift "
                   f"with a light object scrape, {SPACE_CLOSE_DRY}, {NO_MUSIC}", 0.45)
add_many("sfx.interaction.chest.open", 1, "interaction", "interaction",
         "opening the iron-banded storage chest",
         lambda i: "an iron-banded wooden chest lid opening, hinge creak and wood groan, "
                   f"{SPACE_INTERIOR}, {NO_MUSIC}", 1.2)
add_many("sfx.interaction.chest.close", 1, "interaction", "interaction",
         "closing the chest",
         lambda i: "an iron-banded wooden chest lid closing with a solid wooden thud and a latch, "
                   f"{SPACE_INTERIOR}, {NO_MUSIC}", 0.8)
add_many("sfx.interaction.door.open", 1, "interaction", "interaction",
         "opening a wooden door",
         lambda i: "a heavy wooden plank door swinging open, hinge creak and a scrape over a "
                   f"stone threshold, {SPACE_INTERIOR}, {NO_MUSIC}", 1.3)
add_many("sfx.interaction.door.close", 1, "interaction", "interaction",
         "closing a wooden door",
         lambda i: "a heavy wooden plank door swinging shut and latching, solid wood thud, "
                   f"{SPACE_INTERIOR}, {NO_MUSIC}", 0.9)
add_many("sfx.crafting.mining.strike", 3, "crafting", "crafting",
         "the mining pick striking the iron vein",
         lambda i: "a steel mining pick striking an iron ore seam in rock, hard sharp chip with a "
                   f"short ring and falling grit, {SPACE_OUTDOOR}, {NO_MUSIC}", 0.6)
add_many("sfx.crafting.ore.break", 1, "crafting", "crafting",
         "the ore breaking free",
         lambda i: "rock cracking apart and ore breaking free, crumbling rubble settling, "
                   f"{SPACE_OUTDOOR}, {NO_MUSIC}", 1.1)
add_many("sfx.crafting.forge.ambience", 1, "crafting", "crafting",
         "the smithy forge burning; a short loopable bed",
         lambda i: "a small coal forge burning steadily, rushing air and a low fire roar, "
                   f"even and loopable, {SPACE_INTERIOR}, no music, no voices", 6.0,
         channels=2, loop=True)
add_many("sfx.crafting.anvil.strike", 4, "crafting", "crafting",
         "the blacksmith hammer striking the anvil",
         lambda i: "a blacksmith hammer striking an anvil, hard bright metallic ring with a short "
                   f"decay, {SPACE_INTERIOR}, {NO_MUSIC}", 0.8)
add_many("sfx.crafting.complete", 1, "crafting", "crafting",
         "the craft completing; a non-musical confirmation",
         lambda i: "hot metal quenched and a finished object set down, sizzle and a solid metallic "
                   f"settle, no chime, no jingle, {SPACE_INTERIOR}, {NO_MUSIC}", 1.4)

# ---------------------------------------------------------------------------------------------
# 9. Area ambience - section 19. Four beds for the four cells. Section 20 requires them to loop,
#    so each is generated long and seamlessly blended rather than faded.
# ---------------------------------------------------------------------------------------------
add_many("amb.ashen_hollow.waystation", 1, "ambience", "ambience",
         "Cell A: the frontier waystation",
         lambda i: "a quiet frontier waystation ambience, light wind through wooden buildings, a "
                   f"distant forge working, occasional timber creak, no voices, no music, "
                   f"even and loopable", 20.0, channels=2, loop=True)
add_many("amb.charwood_verge", 1, "ambience", "ambience",
         "Cell B: open woodland and shallow stream",
         lambda i: "a temperate open woodland ambience, forest wind through leaves, distant birds "
                   f"and insects, a shallow stream, no voices, no music, even and loopable",
         20.0, channels=2, loop=True)
add_many("amb.blackvein_cut", 1, "ambience", "ambience",
         "Cell C: the abandoned quarry",
         lambda i: "an abandoned stone quarry ambience, wind crossing bare rock, a hollow resonant "
                   f"space, an occasional loose stone falling, no voices, no music, even and "
                   f"loopable", 20.0, channels=2, loop=True)
add_many("amb.foldscar_ruin", 1, "ambience", "ambience",
         "Cell D: the Foldscar; ordinary ambience becoming subtly unreliable",
         lambda i: "a very quiet ruin ambience with almost no wildlife, wind that wavers slightly "
                   f"out of phase with itself, a faint unstable tonal displacement under the wind, "
                   f"restrained, no horror drone, no music, even and loopable", 20.0,
         channels=2, loop=True)

# ---------------------------------------------------------------------------------------------
# 10. UI - section 21. Functional only; the HUD's visual styling is deliberately temporary.
# ---------------------------------------------------------------------------------------------
for name, role, prompt, seconds in (
    ("sfx.ui.menu.open", "opening a menu",
     "a short soft leather and wood interface sound, a panel opening, no music sting", 0.35),
    ("sfx.ui.menu.close", "closing a menu",
     "a short soft interface sound, a panel closing, no music sting", 0.3),
    ("sfx.ui.select", "confirming a selection",
     "a short clean wooden tick of confirmation, dry and quiet, no chime, no jingle", 0.22),
    ("sfx.ui.back", "cancelling or going back",
     "a short soft muted wooden tap, lower than a confirm, dry and quiet", 0.22),
    ("sfx.ui.equip", "moving or equipping an item",
     "a short dry leather and metal clink of an item being equipped, quiet and close", 0.4),
    ("sfx.ui.error", "an action is unavailable",
     "a short dull wooden thunk of refusal, low and dry, not aggressive, no buzzer", 0.3),
):
    SPEC.append(sfx(name, "ui", "ui", role, f"{prompt}, {SPACE_CLOSE_DRY}, {NO_MUSIC}", seconds,
                    priority="P1"))

BY_ID = {entry["audio_id"]: entry for entry in SPEC}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--summary", action="store_true")
    parser.add_argument("--list", action="store_true")
    parser.add_argument("--category", default=None)
    parser.add_argument("--emit-spec", action="store_true")
    args = parser.parse_args()

    if len(BY_ID) != len(SPEC):
        duplicates = [k for k, v in collections.Counter(e["audio_id"] for e in SPEC).items() if v > 1]
        print(f"  FAIL duplicate audio ids: {duplicates}")
        return 1

    if args.list:
        for entry in SPEC:
            if args.category and entry["category"] != args.category:
                continue
            print(f"  {entry['audio_id']:<46} {entry['seconds']:>5.2f}s "
                  f"{'st' if entry['channels'] == 2 else 'mo'} "
                  f"{'loop' if entry['loop'] else '    '} {entry['priority']}")
        return 0

    by_category = collections.Counter(e["category"] for e in SPEC)
    by_group = collections.Counter(e["group"] for e in SPEC)
    loops = [e for e in SPEC if e["loop"]]
    p0 = [e for e in SPEC if e["priority"] == "P0"]
    print(f"  {len(SPEC)} sounds specified")
    print(f"  P0 {len(p0)}, P1 {len(SPEC) - len(p0)};  {len(loops)} loops, "
          f"{len(SPEC) - len(loops)} one-shots")
    print(f"  channels: {sum(1 for e in SPEC if e['channels'] == 1)} mono, "
          f"{sum(1 for e in SPEC if e['channels'] == 2)} stereo")
    total = sum(e["seconds"] for e in SPEC)
    print(f"  total generated audio: {total:.0f} s ({total / 60:.1f} min)")
    print()
    print("  by category:")
    for name, count in sorted(by_category.items(), key=lambda kv: -kv[1]):
        print(f"    {name:<14} {count:>4}")
    print()
    print("  by loudness group:")
    for name, count in sorted(by_group.items(), key=lambda kv: -kv[1]):
        print(f"    {name:<20} {count:>4}   target {LOUDNESS[name]:.0f} LUFS")

    if args.emit_spec:
        os.makedirs(os.path.dirname(OUT), exist_ok=True)
        document = {
            "version": 1,
            "comment": [
                "Every sound the Ashen Hollow prototype needs, as data. Generated from",
                "_audio_spec.py, which is the source of truth: nothing downstream restates a prompt",
                "or a duration.",
                "",
                "Loudness targets are relative by design, per brief section 25: a sword swing must",
                "not end up louder than a boar impact because its waveform peaked higher.",
                "",
                "The count exceeds the 60-100 of section 37 because section 13 requires eleven or",
                "more sounds for each of five creatures. Section 33 is the acceptance criterion,",
                "so coverage wins; section 37's intent is honoured by generating one candidate per",
                "id and curating rather than generating forty.",
            ],
            "authority": "PHASE1_ASHEN_HOLLOW_PLAYABLE_CONTENT_BIBLE.md",
            "loudness_targets_lufs": LOUDNESS,
            "counts": {
                "total": len(SPEC),
                "p0": len(p0),
                "loop": len(loops),
                "by_category": dict(by_category),
                "total_seconds": round(total, 1),
            },
            "sounds": SPEC,
        }
        with io.open(OUT, "w", encoding="utf-8") as handle:
            json.dump(document, handle, indent=2)
            handle.write("\n")
        print(f"\n  wrote {OUT}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
