"""Generate buildable assemblies from the modular kit: the prototype's buildings as data.

The kit on its own does not give anyone a building. `PROTOTYPE.md` needs an outpost with two
enterable interiors, and the sprint brief asks for a cottage, a workshop and a communal building,
so this emits those as assembly recipes: which kit piece goes where, at what rotation.

Recipes rather than baked meshes, because a modular kit's whole value is that a wall can move. A
baked cottage would be one unmodifiable mesh and would waste the kit. It also means a layout can be
corrected without rebuilding any geometry.

Placements are computed, not typed. Every position comes from the declared module size, so a wall
that is exactly 3.00 m produces a wall run that is exactly 3.00 m apart, and the arithmetic cannot
drift from the geometry.

Usage:
    python _make_kit_assemblies.py
"""
import io
import json
import math
import os

ASSETS = r"W:\UNNAMED\assets"
OUT = os.path.join(ASSETS, "manifests", "kit_assemblies.json")

WALL_MODULE = 3.00
WALL_HEIGHT = 2.60
WALL_THICK = 0.18
FLOOR_MODULE = 3.00
DOOR_OPENING = (1.10, 2.20)
ROOF_SLOPE = 2.00
ROOF_PITCH_DEG = 32.0


def rectangle(name, width, depth, wall_asset, note, door_on="south", windows=True):
    """A rectangular building laid out from the kit, on a whole number of 3 m modules."""
    pieces = []
    half_w, half_d = width / 2.0, depth / 2.0

    # Floor tiles.
    cols = int(round(width / FLOOR_MODULE))
    rows = int(round(depth / FLOOR_MODULE))
    for cx in range(cols):
        for cz in range(rows):
            x = -half_w + FLOOR_MODULE / 2 + cx * FLOOR_MODULE
            z = -half_d + FLOOR_MODULE / 2 + cz * FLOOR_MODULE
            pieces.append({"asset": "building_floor_planks", "position": [x, 0.0, z],
                           "rotation_y_deg": 0.0})

    def wall_run(axis, fixed, span, faces, replace_middle_with=None):
        count = int(round(span / WALL_MODULE))
        for index in range(count):
            along = -span / 2 + WALL_MODULE / 2 + index * WALL_MODULE
            if axis == "x":
                position = [along, 0.0, fixed]
                rotation = 0.0
            else:
                position = [fixed, 0.0, along]
                rotation = 90.0
            middle = count // 2
            asset = wall_asset
            if replace_middle_with and index == middle and faces:
                asset = replace_middle_with
            pieces.append({"asset": asset, "position": position, "rotation_y_deg": rotation})

    # Walls sit on the outline, so a 3.0 m module spans exactly 3.0 m of run.
    wall_run("x", -half_d + WALL_THICK / 2, width, door_on == "north",
             "building_door_frame" if door_on == "north" else None)
    wall_run("x", half_d - WALL_THICK / 2, width, door_on == "south",
             "building_door_frame" if door_on == "south" else None)

    for fixed, faces in ((-half_w + WALL_THICK / 2, door_on == "west"),
                         (half_w - WALL_THICK / 2, door_on == "east")):
        count = int(round(depth / WALL_MODULE))
        for index in range(count):
            along = -half_d + WALL_MODULE / 2 + index * WALL_MODULE
            middle = count // 2
            asset = wall_asset
            if windows and count >= 2 and index in (0, count - 1):
                # End bays carry a window rather than a blank panel.
                asset = "building_window_frame"
            if faces and index == middle:
                asset = "building_door_frame"
            pieces.append({"asset": asset, "position": [fixed, 0.0, along],
                           "rotation_y_deg": 90.0})

    # Corner posts and a ring beam at wall head.
    for sx in (-1, 1):
        for sz in (-1, 1):
            pieces.append({"asset": "building_post",
                           "position": [sx * (half_w - 0.09), 0.0, sz * (half_d - 0.09)],
                           "rotation_y_deg": 0.0})
    spans = int(round(width / WALL_MODULE))
    for index in range(spans):
        x = -half_w + WALL_MODULE / 2 + index * WALL_MODULE
        for sz in (-1, 1):
            pieces.append({"asset": "building_beam",
                           "position": [x, WALL_HEIGHT, sz * (half_d - 0.09)],
                           "rotation_y_deg": 0.0})

    # Two roof slopes, laid from the ridge out to the eaves.
    #
    # Two earlier layouts were wrong in different ways, and this is the third. The first put ONE
    # panel per slope centred at `half_d + ROOF_SLOPE/2 - 0.30` = 3.7 m from the centre of a 6 m
    # building, while a 2 m panel pitched at 32 degrees only spans 1.70 m horizontally - so the whole
    # roof sat *outside* the walls, floating clear and leaving the ridge open. The second laid each
    # slope from the eave inward, which is closer, but two panels cover 3.39 m of a 3.0 m half-depth:
    # measured on the built asset the two slopes therefore **overlapped by 0.784 m across the ridge**,
    # intersecting each other and reading as a pile of sheets rather than a roof.
    #
    # Laying from the ridge outward fixes it with no geometry change to the kit. Both slopes now meet
    # exactly at z = 0, and the surplus 0.39 m per side falls at the eave as an overhang, which is what
    # a real roof does. The panel size is fixed by the kit, so the surplus has to go somewhere; an
    # eave overhang is the only place it is not a defect.
    pitch = math.radians(ROOF_PITCH_DEG)
    run = ROOF_SLOPE * math.cos(pitch)
    panels_per_slope = max(1, int(math.ceil(half_d / run)))
    for sz in (-1, 1):
        for row in range(panels_per_slope):
            z_offset = run * (row + 0.5)
            height = WALL_HEIGHT + (half_d - z_offset) * math.tan(pitch)
            for index in range(spans):
                x = -half_w + WALL_MODULE / 2 + index * WALL_MODULE
                pieces.append({"asset": "building_roof_panel",
                               "position": [x, height, sz * z_offset],
                               "rotation_y_deg": 0.0,
                               "rotation_x_deg": sz * -ROOF_PITCH_DEG,
                               "note": "pitched slope, ridge to eave"})
    return {"name": name, "note": note, "footprint_m": [width, depth],
            "wall_height_m": WALL_HEIGHT, "pieces": pieces}


def main():
    assemblies = {
        "longhouse": rectangle(
            "longhouse", 9.0, 6.0, "building_wall_timber",
            "The prototype's main interior: a 9 x 6 m hall with a south door. "
            "PROTOTYPE.md §3 enters this from the outpost."),
        "forge_shed": rectangle(
            "forge_shed", 6.0, 6.0, "building_wall_stone",
            "The prototype's second interior, in stone for fire safety. "
            "Holds station.forge_shed, the anvil and the bellows."),
        "cottage": rectangle(
            "cottage", 6.0, 6.0, "building_wall_timber",
            "Small dwelling from the sprint brief's structure list."),
        "communal_building": rectangle(
            "communal_building", 9.0, 9.0, "building_wall_timber",
            "The brief's inn or generic communal building, on a 3 x 3 module footprint."),
    }

    # The ruin kit is arrangement rather than enclosure: broken walls at staggered angles.
    ruin = {"name": "ruin_kit", "note": "Broken wall fragments for the hollow's older structures.",
            "footprint_m": None, "wall_height_m": 1.60, "pieces": []}
    for index, (x, z, rotation) in enumerate([(-2.2, 0.0, 0.0), (1.4, 1.6, 62.0),
                                              (0.0, -2.4, -24.0), (3.0, -0.6, 118.0)]):
        ruin["pieces"].append({"asset": "building_ruin_wall", "position": [x, 0.0, z],
                               "rotation_y_deg": rotation})
    for index, (x, z) in enumerate([(-3.1, 1.9), (2.6, 2.3)]):
        ruin["pieces"].append({"asset": "building_post", "position": [x, 0.0, z],
                               "rotation_y_deg": 0.0, "note": "leaning remnant post"})
    assemblies["ruin_kit"] = ruin

    # Standalone world pieces the brief asks for.
    assemblies["world_well"] = {
        "name": "world_well", "note": "Standalone well for the outpost.",
        "footprint_m": [1.44, 1.80], "wall_height_m": 2.82,
        "pieces": [{"asset": "building_well", "position": [0.0, 0.0, 0.0], "rotation_y_deg": 0.0}],
    }
    assemblies["road_run"] = {
        "name": "road_run", "note": "Four road segments forming a 4 x 16 m run out of the outpost.",
        "footprint_m": [4.0, 16.0], "wall_height_m": 0.09,
        "pieces": [{"asset": "building_road_segment", "position": [0.0, 0.0, i * 4.0],
                    "rotation_y_deg": 0.0} for i in range(4)],
    }
    assemblies["outpost_fence"] = {
        "name": "outpost_fence", "note": "Fence run, 2.40 m per panel.",
        "footprint_m": [2.40, 12.0], "wall_height_m": 1.10,
        "pieces": [{"asset": "building_fence_panel", "position": [0.0, 0.0, i * 2.40],
                    "rotation_y_deg": 90.0} for i in range(5)],
    }

    doc = {
        "version": 1,
        "comment": [
            "Assembly recipes for the modular building kit. A recipe is data, not geometry: the kit",
            "pieces stay independent so a wall can be moved, and a layout can be corrected without",
            "rebuilding any mesh.",
            "",
            "All positions are computed from the declared 3.00 m module, so the arithmetic cannot",
            "drift from the geometry. Pieces sit at a ground-anchored, footprint-centred origin,",
            "so a piece at (3.0, 0, 0) butts exactly against one at (0, 0, 0).",
            "",
            "The door frame's opening is 1.10 x 2.20 m, which clears the 2.05 m door leaf and the",
            "1.80 m canonical Veth body with headroom.",
        ],
        "assemblies": assemblies,
    }
    with io.open(OUT, "w", encoding="utf-8") as handle:
        json.dump(doc, handle, indent=2)

    total = 0
    print(f"  {'assembly':<22} {'footprint':<14} {'pieces':>7}")
    print("  " + "-" * 48)
    for name, assembly in assemblies.items():
        footprint = assembly["footprint_m"]
        text = f"{footprint[0]} x {footprint[1]} m" if footprint else "irregular"
        print(f"  {name:<22} {text:<14} {len(assembly['pieces']):>7}")
        total += len(assembly["pieces"])
    print(f"\n  {len(assemblies)} assemblies, {total} placed pieces")
    print(f"  wrote {OUT}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
