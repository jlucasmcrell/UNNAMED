"""Generate the three magic proof effects, as Godot-ready flipbook atlases plus their contract.

Section 16 of the sprint brief asks for presentation rather than more ritual props: an offensive
cast, a ward and a restorative, each with a cast origin, a travel effect where the spell travels,
an impact, and a Strain feedback concept.

The three formulas come from `PHASE1_ASHEN_HOLLOW_PLAYABLE_CONTENT_BIBLE.md` section 13, which is
newer than `PROTOTYPE.md` and replaces its spells:

    formula.force.impulse_bolt      Force    short ranged kinetic projection
    formula.warding.brace_ward      Warding  brief local protection against intrusion or impact
    formula.vital.mending_thread    Vital    small recovery / stabilization

`PROTOTYPE.md` called these `spell.ember.bolt`, `spell.ward.oakskin` and `spell.mend.salve`, and the
first version of this generator followed it literally - which produced an orange *fire* bolt for an
effect the bible describes as kinetic projection, bark bands for a ward that is a resonant boundary,
and green motes for a "thread". None of those three is what the domains mean, so the effect ids,
the schools and the palettes are all retargeted here. The interface the game binds to (cell size,
grid, frame count, fps, loop flag, ttl, origin socket, bound clip event) is deliberately unchanged,
so nothing downstream has to move.

Atlas rather than a single sprite, because a spell that is one static puff of light does not read as
a spell. Grid rather than GIF or a video, because this is what a Godot `AnimatedTexture` or a
particle flipbook consumes directly: one square power-of-two sheet, cells in reading order.

Generated rather than rendered, for two reasons. Particle art needs clean straight alpha, and a
concept render or an AI image arrives with its own lighting baked in and no alpha channel worth
using - the same double-lighting problem the world materials had. And the frame count, cell size and
cell order are the part gameplay binds to, so getting those exactly right matters more than the
brushwork inside each cell.

So: proof-grade sprites, production-correct interface. The manifest records `authoring: generated`
and `replaceable: true` on every effect, so nobody has to guess which parts are placeholder.

Usage:
    python _make_magic_vfx.py
"""
import io
import json
import math
import os

import numpy as np
from PIL import Image

ASSETS = r"W:\UNNAMED\assets"
OUT_DIR = os.path.join(ASSETS, "vfx")
OUT_MANIFEST = os.path.join(ASSETS, "manifests", "magic_vfx.json")

CELL = 128
SEED = 20260923

# The screen overlay is the one effect that is not a world sprite: it is stretched across the whole
# viewport, so a 128 px cell is far too coarse for it. It is also the one effect where the generator
# and the declared cell size disagreed - it was drawn at 256 and pasted into a 128 cell, which kept
# the top-left quadrant and threw the rest away, so the manifest shipped the corner of a vignette
# instead of the vignette. Declaring the size per effect is what keeps the two in step.
CELL_OVERRIDE = {"vfx.resonance.strain_overlay": 256}


def cell_size(name):
    return CELL_OVERRIDE.get(name, CELL)

# effect id -> (school, kind, cell count, columns, fps, loop, ttl seconds or None)
#
# The two timed effects keep the durations PROTOTYPE.md gave them (a 10 s ward, a 6 s restore).
# The bible does not restate either, so they are provisional and belong to whoever owns the spell
# timing; what matters here is that ttl is a real number rather than a round guess.
EFFECTS = {
    "vfx.force.impulse_bolt_travel": ("formula.force",   "projectile_travel", 8, 4, 24, True,  None),
    "vfx.force.impulse_bolt_impact": ("formula.force",   "impact",            8, 4, 24, False, 0.33),
    "vfx.warding.brace_ward_shell":  ("formula.warding", "ward_shell",        8, 4, 12, True,  10.0),
    "vfx.vital.mending_thread_restore": ("formula.vital", "restore",          12, 4, 12, False, 6.0),
    "vfx.magic.cast_charge":         ("magic",           "cast_charge",       8, 4, 24, False, 0.55),
    "vfx.resonance.strain_overlay":  ("resonance",       "screen_overlay",    1, 1, 0,  False, None),
}

# One palette per domain, and the domain is the whole point. Force is kinetic pressure, not fire:
# it gets cold pale light and never a flame colour, because a glowing orange bolt reads as
# pyromancy and the spell is a shove. Warding is a resonant boundary, so it stays in neutral steel.
# Vital is the only warm one, and even there the colour is a fine thread-green rather than a glow.
FORCE = ((242, 248, 255), (146, 184, 230), (40, 60, 100))
WARDING = ((236, 243, 249), (152, 170, 186), (48, 58, 72))
VITAL = ((244, 255, 248), (146, 224, 194), (44, 100, 88))
STRAIN = ((96, 12, 18), (58, 6, 12), (18, 4, 8))


def grid_axes(cell=CELL):
    """Normalised coordinates in [-1, 1] across one cell, plus the radius from its centre."""
    axis = (np.arange(cell) + 0.5) / cell * 2.0 - 1.0
    xs, ys = np.meshgrid(axis, axis)
    return xs, ys, np.hypot(xs, ys)


def smoothstep(edge0, edge1, value):
    """Inverted ranges (edge0 > edge1) are valid and mean a falloff, so the span is guarded with
    abs rather than max: `max(span, 1e-6)` turns a negative span into 1e-6, which makes the ramp
    saturate to 1 everywhere and silently fills the cell instead of masking it."""
    span = edge1 - edge0
    if abs(span) < 1e-6:
        return (value >= edge1).astype(np.float64)
    t = np.clip((value - edge0) / span, 0.0, 1.0)
    return t * t * (3.0 - 2.0 * t)


def value_noise(shape, rng, octaves=4):
    """Cheap fractal noise by upsampling small random grids. Enough to break a gradient's symmetry
    without pulling in a noise dependency."""
    total = np.zeros(shape, dtype=np.float64)
    amplitude = 1.0
    weight = 0.0
    for octave in range(octaves):
        size = 2 ** (octave + 1)
        coarse = rng.random((size, size))
        image = Image.fromarray((coarse * 255).astype(np.uint8)).resize(
            (shape[1], shape[0]), Image.BICUBIC)
        total += np.asarray(image, dtype=np.float64) / 255.0 * amplitude
        weight += amplitude
        amplitude *= 0.5
    return total / weight


def tint(intensity, palette):
    """Map a 0..1 intensity to RGB using a three-stop ramp."""
    core, mid, outer = (np.array(c, dtype=np.float64) for c in palette)
    low = np.clip(intensity * 2.0, 0.0, 1.0)[..., None]
    high = np.clip(intensity * 2.0 - 1.0, 0.0, 1.0)[..., None]
    return outer * (1.0 - low) + mid * low * (1.0 - high) + core * high


def write_atlas(name, cells):
    """Tile cells in reading order and write one RGBA sheet."""
    cell = cell_size(name)
    count = len(cells)
    columns = EFFECTS[name][3]
    rows = math.ceil(count / columns)
    atlas = Image.new("RGBA", (columns * cell, rows * cell), (0, 0, 0, 0))
    for index, c in enumerate(cells):
        if c.size != (cell, cell):
            raise SystemExit(f"{name}: cell {index} is {c.size[0]} px, declared {cell} px")
        atlas.paste(c, ((index % columns) * cell, (index // columns) * cell))
    os.makedirs(OUT_DIR, exist_ok=True)
    path = os.path.join(OUT_DIR, f"{name}.png")
    atlas.save(path)
    return path, columns, rows


def cell_from(intensity, palette):
    rgb = np.clip(tint(intensity, palette), 0, 255).astype(np.uint8)
    alpha = np.clip(intensity * 1.6, 0.0, 1.0)
    rgba = np.dstack([rgb, (alpha * 255).astype(np.uint8)])
    return Image.fromarray(rgba, "RGBA")


def impulse_travel(rng):
    """A compressed slug of displaced air crossing the frame.

    Godot billboards and rotates the sprite onto its velocity, so the streak runs along +X and the
    sprite's own origin is at its centre.

    The width widens *behind* the head rather than being constant, which is what makes it read as
    something moving: a uniform ellipse is just a glowing pill and looks identical every frame. A
    second, fainter collar trails behind the head so the shape suggests displaced medium rather than
    a projectile with a flame, which is what "kinetic projection" means.
    """
    xs, ys, _ = grid_axes()
    noise = value_noise((CELL, CELL), rng, 4)
    cells = []
    count = EFFECTS["vfx.force.impulse_bolt_travel"][2]
    for index in range(count):
        phase = index / count
        head = 0.30 + 0.07 * math.sin(2 * math.pi * phase)
        width = 0.075 + 0.22 * np.clip((head - xs) / 1.3, 0.0, 1.0)
        core = np.exp(-((xs - head) / 0.40) ** 2) * np.exp(-(ys / width) ** 2)
        glow = np.exp(-((xs - head) / 0.78) ** 2) * np.exp(-(ys / (width * 2.4)) ** 2)
        collar = np.exp(-((xs - (head - 0.46)) / 0.09) ** 2) * np.exp(-(ys / 0.42) ** 2)
        flicker = 0.86 + 0.14 * np.sin(2 * math.pi * phase * 2.0 + noise * 9.0)
        intensity = np.clip((core * 1.30 + glow * 0.40 + collar * 0.34) * flicker, 0.0, 1.0)
        cells.append(cell_from(intensity, FORCE))
    return cells


def impulse_impact(rng):
    """A pressure wave, not a burst of fire.

    The first version used nine sharp cosine spikes, which with a hot palette read as a small
    explosion; the second used three broad lobes but started on a saturated blob the size of the
    cell, so the opening frames were a featureless white disc and the expansion had nowhere to go.
    Force displaces rather than burns, so this opens on a small tight centre and lets a ring travel
    outward and thin as it goes, which is what a wave does.
    """
    xs, ys, radius = grid_axes()
    angle = np.arctan2(ys, xs)
    cells = []
    count = EFFECTS["vfx.force.impulse_bolt_impact"][2]
    for index in range(count):
        k = index / (count - 1)
        ring = np.exp(-((radius - (0.06 + 0.56 * k)) / (0.08 + 0.05 * k)) ** 2)
        core = np.exp(-(radius / (0.13 * (1.0 - 0.80 * k))) ** 2)
        lobes = 0.62 + 0.38 * np.cos(angle * 3.0) ** 2
        fade = (1.0 - k) ** 1.15
        intensity = np.clip((ring * lobes * 1.30 + core * 1.15) * fade * 1.25, 0.0, 1.0)
        cells.append(cell_from(intensity, FORCE))
    return cells


def brace_ward_shell(rng):
    """A body-height shell rather than a ball: the ward covers a 1.8 m character, so the sprite is
    an upright oval and the runner stretches it to the character's bounds.

    Drawn as a boundary under tension rather than as bark. Concentric rings run inward from the rim
    and radial seams divide it into panels, so it reads as a resonant surface holding something out
    instead of a skin of wood. The first version's vertical noise-warped bands were oakskin, and
    oakskin is not what Warding means.
    """
    xs, ys, _ = grid_axes()
    noise = value_noise((CELL, CELL), rng, 5)
    cells = []
    count = EFFECTS["vfx.warding.brace_ward_shell"][2]
    for index in range(count):
        phase = index / count
        oval = (xs / 0.62) ** 2 + (ys / 0.94) ** 2
        inside = smoothstep(1.12, 0.86, oval)
        radius = np.sqrt(np.clip(oval, 0, None))
        rims = 0.5 + 0.5 * np.cos(radius * 26.0 + noise * 3.0)
        seams = 0.5 + 0.5 * np.cos(np.arctan2(ys, xs) * 8.0)
        edge = np.exp(-((radius - 0.94) / 0.12) ** 2)
        pulse = 0.70 + 0.30 * (0.5 - 0.5 * math.cos(2 * math.pi * phase))
        intensity = np.clip((inside * (0.18 + 0.26 * rims * seams) + edge * 0.80) * pulse,
                            0.0, 1.0)
        cells.append(cell_from(intensity, WARDING))
    return cells


def mending_thread_restore(rng):
    """Fine filaments drawing closed across a six-second mending.

    "Thread" is the name the bible gives the formula, so the effect is thread: many thin vertical
    filaments that knit inward and settle. The earlier version was a field of rising motes in green,
    which is a salve and reads as a healing potion. The envelope is still the spell's own shape -
    quick to come up, holding while the mending ticks, fading out.

    Thin and numerous, not few and wide: a couple of dozen broad gaussians sum into one saturated
    smear, so each filament stays only a few pixels across and the field between them stays empty.
    """
    xs, ys, _ = grid_axes()
    cells = []
    count = EFFECTS["vfx.vital.mending_thread_restore"][2]
    threads = [(rng.uniform(-0.70, 0.70), rng.uniform(0.0, 1.0),
                rng.uniform(0.022, 0.040), rng.uniform(0.55, 1.45))
               for _ in range(88)]
    for index in range(count):
        k = index / (count - 1)
        envelope = smoothstep(0.0, 0.18, k) * (1.0 - smoothstep(0.72, 1.0, k))
        field = np.zeros((CELL, CELL))
        for tx, offset, width, speed in threads:
            travel = (offset + k * speed) % 1.0
            x = tx + 0.030 * math.sin(2 * math.pi * (travel + offset) * 0.9)
            # Filaments run vertically and close toward the centre line as the mending completes.
            span = 0.55 * (1.0 - 0.25 * k)
            y = (0.90 - travel * 1.80) + ys * 0.0
            field += np.exp(-((xs - x) / width) ** 2) * np.exp(-((ys - y) / span) ** 2)
        intensity = np.clip(field * envelope * 0.52, 0.0, 1.0)
        cells.append(cell_from(intensity, VITAL))
    return cells


def cast_charge(rng):
    """The hand charge during the cast windup: rings contracting into a palm-sized orb, brightest
    just before the spell releases at 0.55 s of anim.humanoid.magic.cast_01.

    This one serves all three formulas, so it is drawn in the neutral end of the Force palette
    rather than in any single domain's colour.
    """
    xs, ys, radius = grid_axes()
    cells = []
    count = EFFECTS["vfx.magic.cast_charge"][2]
    for index in range(count):
        k = index / (count - 1)
        orb = np.exp(-(radius / (0.14 + 0.10 * k)) ** 2) * (0.45 + 0.75 * k)
        contracting = np.exp(-((radius - (0.62 - 0.34 * k)) / 0.10) ** 2) * (0.85 - 0.55 * k)
        spokes = 0.5 + 0.5 * np.cos(np.arctan2(ys, xs) * 6.0)
        intensity = np.clip((orb + contracting * spokes) * 1.25, 0.0, 1.0)
        cells.append(cell_from(intensity, FORCE))
    return cells


def strain_overlay(rng):
    """A full-screen vignette for the Strain resource, not a world effect.

    The brief asks for a Strain feedback *concept*, and this is the part of it that is an asset: a
    translucent overlay whose centre stays clear so the world is still readable at high Strain. How
    strongly it is drawn is a curve, and the curve is declared in the manifest rather than baked
    into six separate textures.
    """
    size = cell_size("vfx.resonance.strain_overlay")
    axis = (np.arange(size) + 0.5) / size * 2.0 - 1.0
    xs, ys = np.meshgrid(axis, axis)
    radius = np.hypot(xs, ys * 0.92)
    noise = value_noise((size, size), rng, 5)
    veins = 0.5 + 0.5 * np.cos(np.arctan2(ys, xs) * 14.0 + noise * 9.0)
    edge = smoothstep(0.55, 1.25, radius)
    intensity = np.clip(edge * (0.62 + 0.38 * veins) * 0.9, 0.0, 1.0)
    rgb = np.clip(tint(intensity, STRAIN), 0, 255).astype(np.uint8)
    return [Image.fromarray(np.dstack([rgb, (intensity * 235).astype(np.uint8)]), "RGBA")]


GENERATORS = {
    "vfx.force.impulse_bolt_travel": impulse_travel,
    "vfx.force.impulse_bolt_impact": impulse_impact,
    "vfx.warding.brace_ward_shell": brace_ward_shell,
    "vfx.vital.mending_thread_restore": mending_thread_restore,
    "vfx.magic.cast_charge": cast_charge,
    "vfx.resonance.strain_overlay": strain_overlay,
}

# Where each effect is born and what it attaches to. The cast origin is the hand socket the
# skeleton already carries, not a new one: unarmed casting is what the prototype's item list
# provides, since none of its 15 items is a staff.
ORIGINS = {
    "vfx.magic.cast_charge": {"socket": "SOCK_hand_r", "parent_bone": "hand_r", "attach": True},
    "vfx.force.impulse_bolt_travel": {"socket": "SOCK_hand_r", "parent_bone": "hand_r",
                                      "attach": False,
                                      "note": "Spawns at the palm, then flies free along its "
                                              "velocity."},
    "vfx.force.impulse_bolt_impact": {"socket": None, "parent_bone": None, "attach": False,
                                      "note": "Spawns at the hit point on the surface struck."},
    "vfx.warding.brace_ward_shell": {"socket": None, "parent_bone": "pelvis", "attach": True,
                                     "note": "Stretched to the character's own bounds, so it follows"
                                             " the body rather than being scaled by hand."},
    "vfx.vital.mending_thread_restore": {"socket": None, "parent_bone": "pelvis", "attach": True},
    "vfx.resonance.strain_overlay": {"socket": None, "parent_bone": None, "attach": True,
                                     "note": "Full-screen canvas layer, not world space."},
}

# What each effect binds to. Clip ids and event ids come from the animation registry, so an effect
# cannot reference a clip that does not exist or an event that is not in the closed vocabulary.
# The two spell references are the bible's formula ids, not PROTOTYPE.md's superseded spell ids.
BINDINGS = {
    "vfx.magic.cast_charge": {"clip": "anim.humanoid.magic.cast_01", "on": "windup_start",
                              "until": "spell_release"},
    "vfx.force.impulse_bolt_travel": {"clip": "anim.humanoid.magic.cast_01", "on": "effect_spawn",
                                      "note": "Flies until it hits or exceeds its range."},
    "vfx.force.impulse_bolt_impact": {"clip": "anim.humanoid.magic.cast_01", "on": "effect_spawn",
                                      "trigger": "projectile_hit"},
    "vfx.warding.brace_ward_shell": {"clip": "anim.humanoid.magic.cast_01", "on": "effect_spawn",
                                     "spell": "formula.warding.brace_ward"},
    "vfx.vital.mending_thread_restore": {"clip": "anim.humanoid.magic.cast_01",
                                         "on": "effect_spawn",
                                         "spell": "formula.vital.mending_thread"},
    "vfx.resonance.strain_overlay": {"clip": None, "on": None, "spell": None,
                                     "note": "Continuous, driven by the Strain value."},
}

# Strain feedback as a curve rather than as textures. Otherreach has no mana pool; Strain is the
# cost of resonance, so the feedback has to read as a cost and not as a mana bar emptying.
STRAIN_CURVE = {
    "resource": "strain",
    "note": "Strain rises with each cast and decays with rest. The overlay is drawn at the alpha "
            "this curve gives, so one texture serves the whole range.",
    "points": [
        {"strain": 0.00, "overlay_alpha": 0.00, "desaturation": 0.00, "vignette": 0.00},
        {"strain": 0.35, "overlay_alpha": 0.14, "desaturation": 0.06, "vignette": 0.10},
        {"strain": 0.65, "overlay_alpha": 0.34, "desaturation": 0.18, "vignette": 0.26},
        {"strain": 0.85, "overlay_alpha": 0.56, "desaturation": 0.32, "vignette": 0.44},
        {"strain": 1.00, "overlay_alpha": 0.74, "desaturation": 0.45, "vignette": 0.62},
    ],
    "pulse_hz_at_max": 1.6,
    "pulse_note": "A slow pulse is added above 0.65 strain so the feedback is unmistakable at the "
                  "point where the design wants the player to stop casting.",
}

# Godot consumes blend mode and a tint multiplier; declaring them here keeps the values out of
# hand-set resource files that drift.
BLEND = {
    "vfx.force.impulse_bolt_travel": {"blend_mode": "add", "emission_energy": 2.0,
                                      "tint": "#9cc4e8"},
    "vfx.force.impulse_bolt_impact": {"blend_mode": "add", "emission_energy": 2.4,
                                      "tint": "#8fb8e0"},
    "vfx.warding.brace_ward_shell": {"blend_mode": "mix", "emission_energy": 1.1,
                                     "tint": "#a8b6c4"},
    "vfx.vital.mending_thread_restore": {"blend_mode": "add", "emission_energy": 1.5,
                                         "tint": "#9ee0c4"},
    "vfx.magic.cast_charge": {"blend_mode": "add", "emission_energy": 2.0, "tint": "#c8d8ea"},
    "vfx.resonance.strain_overlay": {"blend_mode": "mix", "emission_energy": 0.0,
                                     "tint": "#ffffff"},
}


def main():
    rng = np.random.default_rng(SEED)
    effects = {}
    print(f"  {'effect':<32} {'grid':>8} {'frames':>7} {'fps':>4}  atlas")
    print("  " + "-" * 86)
    for name, (school, kind, count, columns, fps, loop, ttl) in EFFECTS.items():
        cells = GENERATORS[name](rng)
        if len(cells) != count:
            raise SystemExit(f"{name}: generator produced {len(cells)} cells, declared {count}")
        path, columns_used, rows = write_atlas(name, cells)
        effects[name] = {
            "effect_id": name,
            "school": school,
            "kind": kind,
            "authoring": "generated",
            "replaceable": True,
            "atlas": os.path.relpath(path, ASSETS).replace("\\", "/"),
            "cell_px": cell_size(name),
            "grid": {"columns": columns_used, "rows": rows},
            "frame_count": count,
            "fps": fps,
            "loop": loop,
            "ttl_s": ttl,
            "blend": BLEND[name],
            "origin": ORIGINS[name],
            "binding": BINDINGS[name],
            "alpha_mode": "straight",
            "generator_seed": SEED,
        }
        print(f"  {name:<32} {columns_used}x{rows:<5} {count:>7} {fps:>4}  "
              f"{os.path.basename(path)}")

    doc = {
        "version": 2,
        "comment": [
            "Presentation for the three Phase-1 formulas, plus the cast charge and the Strain",
            "overlay. Section 16 of the sprint brief: three representative effects and a casting",
            "gesture, not another thirty ritual props.",
            "",
            "The formulas are the bible's - Impulse Bolt (Force), Brace Ward (Warding) and Mending",
            "Thread (Vital) - not PROTOTYPE.md's spell.ember.bolt / spell.ward.oakskin /",
            "spell.mend.salve, which this file previously and wrongly followed. Force is kinetic",
            "pressure, so its effect is cold displaced air and never a flame colour; Warding is a",
            "resonant boundary rather than a skin of bark; Vital is thread rather than a salve.",
            "",
            "Every atlas is a flipbook: cells in reading order, one square cell each, straight",
            "alpha, ready for a Godot AnimatedTexture or a particle flipbook.",
            "",
            "authoring is 'generated' and replaceable is true on every effect. The interface -",
            "frame count, grid, fps, loop, ttl, origin socket, bound clip event - is the part",
            "gameplay binds to and is production-correct; the pixels inside each cell are proof",
            "grade and are meant to be replaced by authored art without touching any of it.",
            "",
            "Otherreach has no mana pool, and these are Resonance effects rather than spells paid",
            "for from a pool. The ward and the mending are timed, and their ttl_s is inherited from",
            "the durations PROTOTYPE.md gave them because the bible does not restate either; both",
            "are provisional and belong to whoever owns spell timing.",
        ],
        "authority": "PHASE1_ASHEN_HOLLOW_PLAYABLE_CONTENT_BIBLE.md section 13 (the three formulas)",
        "supersedes": "PROTOTYPE.md section 4.1 (the three spells: ember bolt, oakskin, salve)",
        "cast_origin": {
            "socket": "SOCK_hand_r",
            "parent_bone": "hand_r",
            "skeleton_family": "humanoid_standard",
            "position_m": [-0.635, 0.9872, 0.0],
            "frame": "export frame, Y-up, forward -Z",
            "note": "Unarmed casting. The prototype's 15 items include no staff, and no staff in the"
                    " library carries a socket, so a staff origin does not exist yet.",
        },
        "cast_clip": "anim.humanoid.magic.cast_01",
        "strain_feedback": STRAIN_CURVE,
        "effects": effects,
        "summary": {
            "effects": len(effects),
            "schools": sorted({e["school"] for e in effects.values()}),
            "total_frames": sum(e["frame_count"] for e in effects.values()),
        },
    }
    os.makedirs(os.path.dirname(OUT_MANIFEST), exist_ok=True)
    with io.open(OUT_MANIFEST, "w", encoding="utf-8") as handle:
        json.dump(doc, handle, indent=2)
        handle.write("\n")
    print()
    print(f"  {len(effects)} effects, {doc['summary']['total_frames']} frames total")
    print(f"  wrote {OUT_MANIFEST}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
