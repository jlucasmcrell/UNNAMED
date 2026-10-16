"""Give the Barrowworks' structures a surface in the region's art bindings.

The blockout's 120 structures are named `r01_wall_s_w`, `r02_wall_n_e` and so on, and the bindings have no
entry for any of them, so `HollowView.ArtStructure` falls through to `BuildStructure` and every wall is a flat
grey box. That is correct for a blockout and wrong for a room the owner is asked to look at.

`ArtStructure` already has the answer for a structure that IS its own collision shape: a binding with a
`surface` and no `model` builds the same box and overrides its material with the named world material, applied
world-triplanar at the material's own tile size (ArtLibrary.WorldMaterial). So a wall keeps being a wall and
stops being grey - which is the brief's box/triplanar method, in the form the engine already has.

Materials are named, not authored here: they resolve against the run's art root (`--asset-root`), which is how
every region's materials already work. A name the art root does not hold falls back to greybox and says so in
the coverage report, which is the honest failure.

The entries are appended to the region's own bindings file and generated rather than typed, so a regenerated
blockout can be re-dressed by running this again.
"""
import io
import json
import os
import re
import subprocess

ROOT = r"C:\Otherreach_Build\barrowworks_wt"
REGION = os.path.join(ROOT, "content", "regions", "barrowworks.yaml")
BINDINGS = os.path.join(ROOT, "content", "_art_bindings.json")

# The look: old works, cut into the hill. The brief names stone, bronze, ceramic, dark iron and asks for
# "few material families". Two of stone, then: the worked halls wear dressed masonry, and the rooms past the
# ninth are older and rougher - further from the door, which is where the minerals are.
#
# `cw_dark_rock` was the first choice for the deep rooms and was WRONG: its map measures mean grey 52.8 against
# the masonry's 126.8, and a room wearing it renders as a hole rather than as older stone. `cw_rock_ground` is
# 107.0 - a different material in character (rough ground rock, not coursed masonry) that a player can still
# see by. Chosen from measured brightness, not from the name.
WALL = "cw_wet_stone_wall"
DEEP_WALL = "cw_rock_ground"
# The floor is the hill the rooms are cut into. `ter_ph_lichen_rock` (58.7) was the first choice and too dark
# under the walls; `cw_gravel_stones` was the second and is WRONG for a reason no render can show - it is the
# only one of the restore's 22 materials with NO provenance file, so its licence cannot be attested, and the
# brief ships CC0/CC-BY/MIT/Fab Standard only. `ter_ph_river_small_rocks` is the same thing done properly:
# Poly Haven, CC0, 4096px, provenance present, shippable true.
FLOOR = "ter_ph_river_small_rocks"

# R09 onwards is "deep": the Blind Store, the vestibule, the court, the headworks, the inspection bay.
DEEP_FROM = 9

HEADER = "__barrowworks__"


def wall_for(structure_id):
    """Which surface a structure wears: the readings their own stone, every wall its room's family."""
    if structure_id in READING_PROPS:
        return READING_PROPS[structure_id]
    m = re.match(r"r(\d\d)_", structure_id)
    return DEEP_WALL if m and int(m.group(1)) >= DEEP_FROM else WALL


# The three readings are boards and stands, and the brief puts them in its own words: readings "cut into
# walls". So they are stone like everything else here - but the OTHER stone, the rough one, against the dressed
# masonry of the room. A reading has to be findable, and the cheapest honest way to make a 3 m slab findable
# against a stone wall is to give it the stone that is a different colour. One family for all three, which is
# also what the brief asks of the palette: "few material families".
READING_STONE = "cw_rock_ground"

READING_PROPS = {
    "r02_notice_board": READING_STONE,
    "r07_fitting_stand": READING_STONE,
    "r07_diagram_rack": READING_STONE,
}


def region_structure_ids():
    """Every structure id in the region file, in the order it lists them."""
    pattern = re.compile(r"^\s*-\s*\{\s*id:\s*([A-Za-z0-9_]+)\s*,")
    with io.open(REGION, encoding="utf-8") as handle:
        return [m.group(1) for line in handle if (m := pattern.match(line))]


def region_cells():
    """The region's cell keys, space-qualified as the layout holds them."""
    with io.open(REGION, encoding="utf-8") as handle:
        text = handle.read()
    m = re.search(r"^cells:\s*\[([^\]]*)\]", text, re.MULTILINE)
    if not m:
        raise SystemExit("no cells: line in the region file")
    return [c.strip() for c in m.group(1).split(",") if c.strip()]


def main():
    ids = region_structure_ids()
    if not ids:
        raise SystemExit("no structures found in the region file - has it been regenerated?")

    with io.open(BINDINGS, encoding="utf-8") as handle:
        doc = json.load(handle)

    structures = doc.setdefault("structures", {})
    # Idempotent: this tool owns exactly the region's own ids - walls and corridors alike - and replaces those,
    # leaving every other region's entries untouched. Matching on a name pattern missed the corridors, which
    # are `corr_r10_r11_z` and carry no room number of their own.
    before = len(structures)
    for key in [k for k in structures if k in set(ids)]:
        del structures[key]

    dressed = 0
    for sid in ids:
        structures[sid] = {"surface": wall_for(sid)}
        dressed += 1

    deep = sum(1 for sid in ids if wall_for(sid) == DEEP_WALL)

    # The ground the rooms stand on: one entry per cell, keyed exactly as the layout holds it (space-qualified).
    cells = region_cells()
    terrain = doc.setdefault("terrain", {})
    for cell in cells:
        terrain[cell] = FLOOR

    # The lid. A roof group is an id prefix whose structures are gathered into ONE slab over their combined
    # bounds, so a group must be one room or one corridor leg - never `corr_`, which would span the whole
    # dungeon in a single slab. Thirteen rooms and sixteen corridor legs, each its own group.
    #
    # WITHOUT a trailing underscore: `Roofs` appends its own, so a group named `r01_` becomes the prefix `r01__`
    # and matches nothing. That is how the first attempt at this produced zero roofs while every file involved
    # was correct.
    room_groups = sorted({m.group(1) for sid in ids if (m := re.match(r"(r\d\d)_", sid))})
    corridor_groups = sorted({sid for sid in ids if sid.startswith("corr_")})
    roof_groups = room_groups + corridor_groups
    doc["roof_groups"] = roof_groups

    # The light, now that there is a roof and the rooms are dark because of it. A `building_walls` entry with an
    # `interior_light` and NO model registers a lamp at its footprint's centre without replacing the walls, which
    # is the engine's own interior-light path - so this needs no code, only a binding per room.
    #
    # The colour is a cold grey and NOT a hearth's orange: the brief's look is old works with restrained glow,
    # and a barrow cut into a hill has no fire in it. The lamp sits high (the walls are 4 m) and carries a long
    # range, because one lamp per room has to reach the corners of a 24 m chamber rather than pool round a fire.
    hall_light = {"at": [0.0, 3.2, 0.0], "colour": [0.62, 0.66, 0.72], "energy": 1.9, "range": 26,
                  "shadows": True, "size": 0.6, "fog_energy": 0.35, "attenuation": 0.85}
    # The deep rooms get a little less: further from the door is darker, and it is where the minerals are.
    deep_light = {"at": [0.0, 3.2, 0.0], "colour": [0.55, 0.60, 0.70], "energy": 1.5, "range": 24,
                  "shadows": True, "size": 0.6, "fog_energy": 0.3, "attenuation": 0.9}
    walls = doc.setdefault("building_walls", {})
    lit = 0
    for group in room_groups:
        number = int(group[1:])
        walls[group] = {"interior_light": deep_light if number >= DEEP_FROM else hall_light}
        lit += 1
    # The corridors are roofed too, and a five-metre passage under a slab is a black tube without its own light.
    corridor_light = dict(hall_light)
    corridor_light["energy"] = 1.2
    corridor_light["range"] = 14
    for leg in corridor_groups:
        walls[leg] = {"interior_light": corridor_light}
        lit += 1

    # The file's own comment explains the region's art; note this addition beside it rather than in it.
    doc[HEADER] = (f"GENERATED by tools/gen_barrowworks_bindings.py - {dressed} Barrowworks structures, "
                   f"each its own collision box: {dressed - deep} in `{WALL}`, {deep} deep rooms in "
                   f"`{DEEP_WALL}`; {len(cells)} cells floored with `{FLOOR}`; and {len(roof_groups)} roof "
                   f"groups ({len(room_groups)} rooms + {len(corridor_groups)} corridor legs), each its own "
                   f"slab. Regenerate after the blockout moves.")

    tmp = BINDINGS + ".tmp"
    with io.open(tmp, "w", encoding="utf-8", newline="\n") as handle:
        json.dump(doc, handle, indent=2)
        handle.write("\n")
    os.replace(tmp, BINDINGS)
    print(f"  {dressed} structures dressed: {dressed - deep} in '{WALL}', {deep} in '{DEEP_WALL}' "
          f"({before} -> {len(structures)} entries)")
    print(f"  {len(cells)} cells floored with '{FLOOR}': {', '.join(cells)}")


if __name__ == "__main__":
    main()
