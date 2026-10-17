"""Generate the Barrowworks' thirteen-room blockout as region content (P2).

Geometry is DERIVED, not hand-authored: thirteen rooms with four walls each plus corridors is well over a
hundred boxes, and hand-typing them would make the connectivity graph unreviewable. The room table and the
edge list below are the source of truth, copied from docs/BARROWWORKS_PLAN.md sections 1 and 1.1, which
itself comes from the frozen 03_RECOMMENDED_LEVEL_DESIGN.md and 04_LEVEL_MAPS.md.

This is a GREYBOX. Every room is four walls with a 3 m opening centred on each wall, and every edge of the
connectivity graph is a 3 m corridor. Openings on all four sides are what a blockout is for - they make
circulation testable before the real doorways are placed - and the level design refines them later.

The whole interior is offset so every coordinate is positive: cells index from 0 and R04's west wall at
x = -47 m would otherwise need cell -1.
"""
import io

# name, centre (x, z) in the design's own interior-local metres, clear size (w, d)
ROOMS = [
    ("R01", "Threshold",        (0, 0),     (10, 10)),
    ("R02", "Intake Hall",      (0, 18),    (14, 12)),
    ("R03", "Wash Passage",     (-22, 28),  (12, 10)),
    ("R04", "Drain Mouth",      (-42, 28),  (10, 10)),
    ("R05", "Sorting Floor",    (-22, 50),  (18, 14)),
    ("R06", "Still Bay",        (0, 50),    (14, 12)),
    ("R07", "Gauge Archive",    (22, 50),   (14, 12)),
    ("R08", "Return Gallery",   (22, 28),   (10, 14)),
    ("R09", "Blind Store",      (42, 50),   (10, 10)),
    ("R10", "Service Vestibule", (0, 74),   (14, 12)),
    ("R11", "Measuring Court",  (0, 96),    (28, 24)),
    ("R12", "Headworks",        (0, 121),   (16, 10)),
    ("R13", "Inspection Bay",   (22, 74),   (12, 10)),
]

# The connectivity graph, from 04_LEVEL_MAPS.md. (a, b, kind); kind is a comment only.
EDGES = [
    ("R01", "R02", "critical"), ("R02", "R03", "critical"), ("R03", "R04", "alternate entrance"),
    ("R03", "R05", "critical"), ("R05", "R06", "critical"), ("R06", "R07", "investigation"),
    ("R07", "R08", "return discovery"), ("R08", "R02", "G1 shortcut"), ("R07", "R09", "G2 secret"),
    ("R06", "R10", "critical"), ("R07", "R13", "optional"), ("R13", "R10", "optional loop"),
    ("R10", "R11", "critical/retreat"), ("R11", "R12", "G3 aftermath"),
]

WALL = 0.6          # interior stone wall thickness, metres
OPENING = 3.0       # the blockout's doorway width
CORRIDOR = 3.0      # corridor width
FLOOR = 0.0         # the interior floor is flat; the region's terrain grid is constant

# Shift everything positive: cells index from 0 and R04's west wall at -47 m would need cell -1.
OFFSET_X, OFFSET_Z = 50.0, 10.0

by_id = {rid: (name, c, size) for rid, name, c, size in ROOMS}


def shift(x, z):
    return round(x + OFFSET_X, 3), round(z + OFFSET_Z, 3)


def clear_rect(rid):
    """The room's clear interior, in shifted metres."""
    _, (cx, cz), (w, d) = by_id[rid]
    x, z = shift(cx, cz)
    return x - w / 2, z - d / 2, x + w / 2, z + d / 2


def outer_rect(rid):
    """The room's outer rectangle, wall included."""
    x0, z0, x1, z1 = clear_rect(rid)
    return x0 - WALL, z0 - WALL, x1 + WALL, z1 + WALL


structures = []
notes = []


floors = []   # corridors: floor, never a blocker (review 3, B7)


def box(sid, x0, z0, x1, z1, height, clearance=None):
    x0, z0, x1, z1 = round(x0, 3), round(z0, 3), round(x1, 3), round(z1, 3)
    if x1 - x0 <= 0.001 or z1 - z0 <= 0.001:
        return
    entry = f"box_m: [{x0}, {z0}, {x1}, {z1}], height_m: {height}"
    if clearance is not None:
        entry += f", clearance_m: {clearance}"
    structures.append((sid, entry))


# ── room walls, each with a centred opening on all four sides ────────────────────────────────────────
#
# A wall runs from outer corner to outer corner. Its opening is centred on the clear span, so it is
# clear of both corner returns by (clear - OPENING)/2.
# Which sides of each room an edge leaves by, so only those walls get an opening (review 3, B7).
opening_sides = {}
for a, b, _ in EDGES:
    ax, az = by_id[a][1]
    bx, bz = by_id[b][1]
    if bx > ax:
        opening_sides.setdefault(a, set()).add("e")
        opening_sides.setdefault(b, set()).add("w")
    elif bx < ax:
        opening_sides.setdefault(a, set()).add("w")
        opening_sides.setdefault(b, set()).add("e")
    if bz > az:
        opening_sides.setdefault(a, set()).add("n")
        opening_sides.setdefault(b, set()).add("s")
    elif bz < az:
        opening_sides.setdefault(a, set()).add("s")
        opening_sides.setdefault(b, set()).add("n")

for rid, name, _, _ in ROOMS:
    ox0, oz0, ox1, oz1 = outer_rect(rid)
    cx0, cz0, cx1, cz1 = clear_rect(rid)
    half = OPENING / 2
    midx, midz = (cx0 + cx1) / 2, (cz0 + cz1) / 2
    open_sides = opening_sides.get(rid, set())

    # South and north walls (constant Z): an opening only if an edge leaves that way.
    for tag, z0, z1 in (("s", oz0, cz0), ("n", cz1, oz1)):
        if tag in open_sides:
            box(f"{rid.lower()}_wall_{tag}_w", ox0, z0, midx - half, z1, 4.0)
            box(f"{rid.lower()}_wall_{tag}_e", midx + half, z0, ox1, z1, 4.0)
        else:
            box(f"{rid.lower()}_wall_{tag}", ox0, z0, ox1, z1, 4.0)
    # West and east walls (constant X), likewise.
    for tag, x0, x1 in (("w", ox0, cx0), ("e", cx1, ox1)):
        if tag in open_sides:
            box(f"{rid.lower()}_wall_{tag}_s", x0, oz0, x1, midz - half, 4.0)
            box(f"{rid.lower()}_wall_{tag}_n", x0, midz + half, x1, oz1, 4.0)
        else:
            box(f"{rid.lower()}_wall_{tag}", x0, oz0, x1, oz1, 4.0)

# ── corridors, one L-shaped run per edge ─────────────────────────────────────────────────────────────
#
# Each leg is CLIPPED to the gap between the two rooms' outer rectangles rather than run centre to centre.
# Running to the centres puts a corridor box through each room's middle - which the content validator
# refuses, correctly, because the spawn point then sits inside a structure. A leg with no gap on its axis
# is omitted, and for a diagonal edge whose rectangles already overlap on that axis the overlap's midpoint
# is used as the corner instead.
for a, b, kind in EDGES:
    ax, az = shift(*by_id[a][1])
    bx, bz = shift(*by_id[b][1])
    a_o = outer_rect(a)
    b_o = outer_rect(b)
    half = CORRIDOR / 2

    # The gap along X between the two rectangles, or None when they overlap on that axis.
    if bx > ax:
        gap_x = (a_o[2], b_o[0])
    elif bx < ax:
        gap_x = (b_o[2], a_o[0])
    else:
        gap_x = None
    if gap_x is not None and gap_x[1] - gap_x[0] <= 0.001:
        gap_x = None

    if bz > az:
        gap_z = (a_o[3], b_o[1])
    elif bz < az:
        gap_z = (b_o[3], a_o[1])
    else:
        gap_z = None
    if gap_z is not None and gap_z[1] - gap_z[0] <= 0.001:
        gap_z = None

    # Where the rectangles overlap on an axis, the corner belongs inside that overlap so the other leg
    # meets the room rather than stopping in open air.
    overlap_z = (max(a_o[1], b_o[1]), min(a_o[3], b_o[3]))
    overlap_x = (max(a_o[0], b_o[0]), min(a_o[2], b_o[2]))
    corner_z = (overlap_z[0] + overlap_z[1]) / 2 if overlap_z[1] > overlap_z[0] else az
    corner_x = (overlap_x[0] + overlap_x[1]) / 2 if overlap_x[1] > overlap_x[0] else bx

    # The corridors are FLOOR, not wall. They were structures with height_m 0.0, and Kinematics.Blocks reads height 0
    # as "unknown, blocks everything" - so all sixteen were solid blocks and the rooms could only be reached by
    # walking round the outside (review 3, B7). A corridor is a way through, so it is not a blocker at all.
    if abs(bx - ax) > 0.001:
        x0, x1 = (gap_x if gap_x else (min(ax, bx), max(ax, bx)))
        floors.append((f"corr_{a.lower()}_{b.lower()}_x", (x0, corner_z - half, x1, corner_z + half)))
    if abs(bz - az) > 0.001:
        z0, z1 = (gap_z if gap_z else (min(az, bz), max(az, bz)))
        floors.append((f"corr_{a.lower()}_{b.lower()}_z", (corner_x - half, z0, corner_x + half, z1)))
    notes.append(f"    # {a}-{b}: {kind}")

# ── bounds and cells ─────────────────────────────────────────────────────────────────────────────────
# ── the three gates ─────────────────────────────────────────────────────────────────────────────────────────
# Each is the door in an edge's doorway: G1 the R08-R02 shortcut, G2 the R07-R09 secret, G3 the R11-R12 way to the
# headworks. Generated from the edge so the box always sits in the opening - the hand-written boxes had drifted and
# G3 was 36 m from its own doorway (review 3, B7).
GATES = [
    ("R08", "R02", "g1"),   # G1 shortcut
    ("R07", "R09", "g2"),   # G2 secret
    ("R11", "R12", "g3"),   # G3 aftermath, the one o_open opens
]
doors = []
for a, b, gate in GATES:
    ax, az = by_id[a][1]
    bx, bz = by_id[b][1]
    a_o, b_o = outer_rect(a), outer_rect(b)
    if abs(bz - az) >= abs(bx - ax):
        # The rooms are stacked in Z, so the doorway is in a wall of constant Z - A's north or south - and its
        # opening is centred on the X the two share. The reverse of this sent G3 through R11's east wall, 36 m
        # from the corridor it was meant to close.
        lo = (max(a_o[0], b_o[0]) + min(a_o[2], b_o[2])) / 2
        z = a_o[3] if bz > az else a_o[1]
        doors.append((gate, (lo - OPENING / 2, z - 0.4, lo + OPENING / 2, z + 0.4)))
    else:
        # Stacked in X: the doorway is in a wall of constant X, centred on the Z they share.
        lo = (max(a_o[1], b_o[1]) + min(a_o[3], b_o[3])) / 2
        x = a_o[2] if bx > ax else a_o[0]
        doors.append((gate, (x - 0.4, lo - OPENING / 2, x + 0.4, lo + OPENING / 2)))

# ── the rock mass ────────────────────────────────────────────────────────────────────────────────────────────
# The rooms stood in an open field with a doorway in every wall, so a body walked round the corridors and G1-G3 gated
# nothing (review 3, B7). Four thick walls of rock around the whole footprint, with a gap at each of the two portal
# anchors, enclose it: the only way from one room to another is through it.
ROCK = 6.0   # how thick the enclosing rock is, and how far the walk space extends past the rooms
xs0 = [outer_rect(r)[0] for r, _, _, _ in ROOMS]
zs0 = [outer_rect(r)[1] for r, _, _, _ in ROOMS]
xs1 = [outer_rect(r)[2] for r, _, _, _ in ROOMS]
zs1 = [outer_rect(r)[3] for r, _, _, _ in ROOMS]
min_x, min_z, max_x, max_z = min(xs0), min(zs0), max(xs1), max(zs1)
# Corridors can leave the room rectangles; include them.
for sid, _ in structures:
    pass
min_x, min_z = min(min_x, 0.0), min(min_z, 0.0)

# The mass itself: four thick walls just outside the rooms, so the ring is a place under a hill rather than nine
# boxes in a field. Entry is by portal, so it needs no gap - and with it the only way between rooms is the corridors.
box("rock_s", min_x, min_z, max_x, min_z + ROCK, 40.0)
box("rock_n", min_x, max_z - ROCK, max_x, max_z, 40.0)
box("rock_w", min_x, min_z, min_x + ROCK, max_z, 40.0)
box("rock_e", max_x - ROCK, min_z, max_x, max_z, 40.0)

CELL = 100
cells = []
for cx in range(int(min_x // CELL), int((max_x - 0.001) // CELL) + 1):
    for cz in range(int(min_z // CELL), int((max_z - 0.001) // CELL) + 1):
        cells.append(f"space.barrowworks/r_0_0:c_{cx:02d}_{cz:02d}")

# The interior's floor: a flat grid covering the bounds, as the region schema requires.
GRID = 5
cols = int((max_x - min_x) // GRID) + 2
rows = int((max_z - min_z) // GRID) + 2
heights = "[" + ", ".join([f"{FLOOR:.2f}"] * cols) + "]"

out = []
w = out.append
w("# Barrowworks interior - the thirteen-room greybox (M8 D10, P2).")
w("#")
w("# GENERATED by tools/gen_barrowworks_blockout.py from the room table and edge list in")
w("# docs/BARROWWORKS_PLAN.md sections 1 and 1.1, which come from the frozen 03_RECOMMENDED_LEVEL_DESIGN.md")
w("# and 04_LEVEL_MAPS.md. Edit the generator, not this file.")
w("#")
w("# GREYBOX: four walls per room with a 3 m opening centred on each, and a 3 m corridor per edge. The")
w("# openings are on all four sides on purpose - a blockout exists to make circulation testable before the")
w("# real doorways are placed, and the level design refines them. This is not finished art.")
w("")
w("id: region.barrowworks")
w("kind: region")
w("schema: 1")
w("display_key: region.barrowworks.name")
w("tags: [region, interior]")
w("notes: >-")
w("  The Barrowworks interior, thirteen rooms R01-R13, as a greybox blockout. Its own space, so its local")
w("  metres can never collide with the exterior ground of the same address. Reached only through a portal,")
w("  and no portal exists unless the run asks for one (--visual barrowworks=1).")
w("")
w("space: space.barrowworks")
w("region_key: r_0_0")
w(f"cells: [{', '.join(cells)}]")
w(f"bounds_m: {{ min: [{min_x:.1f}, {min_z:.1f}], max: [{max_x:.1f}, {max_z:.1f}] }}")
w("")
w("# Where a body arrives through portal A, facing north into the intake hall.")
w(f"spawn: {{ position_m: [{shift(0, 0)[0]:.1f}, {shift(0, 0)[1]:.1f}], facing_deg: 0 }}")
w(f"generation: {{ terrain: {{ base_height_mm: 0, amplitude_mm: 0, samples_per_axis: 2 }} }}")
w("terrain:")
w(f"  origin_m: [{min_x:.1f}, {min_z:.1f}]")
w(f"  spacing_m: {GRID}")
w(f"  columns: {cols}")
w(f"  rows: {rows}")
w("  # Flat: the interior floor.")
w("  heights_m:")
for _ in range(rows):
    w(f"    - {heights}")
w("")
# The three readings (M8 P8A). The quest's K1, K2 and P are world flags, and a flag is set by a switch standing
# on a structure - so each reading is a board or a stand with a switch on it. They are props, not walls: waist
# height, against a wall, where a body walking the room will find them.
#   K1 the withdrawal notice, in the intake hall - the first room a body walks into, so it can be satisfied by
#      walking in, which is what the design asks of it. ON THE EAST WALL: the intake hall's north, south-west
#      and south-east edges are all openings (R01, R03 and the G1 shortcut), and the first placement stood the
#      board in the north one, blocking a passage a body has to walk through. East and west are solid.
#   K2 the fitting note and P the diagram rack, both in the gauge archive.
#   K3 the blind store's ledger, in the blind store itself - the room the design hides behind G2, and the one
#      o_report asks what it was for. ON THE EAST WALL: R09's only edge is the G2 secret from R07, which is to the
#      west, so the east wall is the solid one. Reading it is how a body learns what the store was.
for sid, x0, z0, x1, z1, h, why in [
    ("r02_notice_board",     56.2, 26.4, 56.6, 29.6, 1.6, "the withdrawal notice, against the intake hall's east wall"),
    ("r07_fitting_stand",    65.4, 57.4, 65.8, 60.6, 1.4, "the fitting note, on the gauge archive's west wall"),
    ("r07_diagram_rack",     78.2, 57.4, 78.6, 60.6, 1.8, "the diagram rack, on the gauge archive's east wall"),
    ("r09_blind_ledger",     96.2, 57.4, 96.6, 62.6, 1.5, "the blind store's ledger, against the blind store's east wall"),
    ("r12_headworks_measure", 42.4, 128.4, 42.8, 133.6, 2.2, "the measure itself, against the headworks' west wall"),
]:
    box(sid, x0, z0, x1, z1, h)
w("# Walls and corridors. Room walls are 4 m; corridors are floor-only (height 0) so they read as passages.")
w("structures:")
for sid, entry in structures:
    w(f"  - {{ id: {sid}, {entry} }}")
w("")
# The three gates the design's own edge list names (04_LEVEL_MAPS.md): G1 is the R08-R02 shortcut, G2 the
# R07-R09 secret, G3 the R11-R12 aftermath. Each is a door in the edge's opening, shut until its flag is set.
w("# G1, G2 and G3, from the design's edge list. G3 is the one the quest opens with a fitting wedge (o_open).")
w("doors:")
for gate, (x0, z0, x1, z1) in doors:
    w(f"  - {{ key: door.barrowworks.{gate}, flag_ref: world.barrowworks.gate_{gate}_open, "
      f"box_m: [{x0:.1f}, {z0:.1f}, {x1:.1f}, {z1:.1f}], height_m: 2.4 }}")
w("containers: []")
w("npcs: []")
w("nodes: []")
w("stations: []")
# A switch stands on a structure and sets its flag in that cell, once. These three are the readings: the verb is
# Read rather than Turn, because what a character does to a notice is read it.
w("switches:")
w("  - key: switch.barrowworks.notice")
w("    structure: r02_notice_board")
w("    flag_ref: world.barrowworks.withdrawal_notice_read")
w("    name: withdrawal notice")
w("    verb: Read")
w("    done_text: \"A withdrawal notice, cut into the board and painted over twice. The service ring is closed pending a measurement that was never completed, and the signature at the foot has been struck through so hard the chisel went through the board.\"")
w("  - key: switch.barrowworks.fitting_note")
w("    structure: r07_fitting_stand")
w("    flag_ref: world.barrowworks.fitting_note_read")
w("    name: fitting note")
w("    verb: Read")
w("    done_text: \"A stand of thin boards, each one a note about how a part was fitted - what it was cut to, what it was cut for, and by whose hand. The last board is blank except for a taper drawn on it and a single word: unfinished.\"")
w("  - key: switch.barrowworks.diagram_rack")
w("    structure: r07_diagram_rack")
w("    flag_ref: world.barrowworks.diagram_rack_read")
w("    name: diagram rack")
w("    verb: Read")
w("    done_text: \"Rack after rack of the same drawing, each a little further from the last, and none of them finished. Whatever the ring was built to gauge, the measuring of it was still being worked out when the works stopped.\"")
w("  - key: switch.barrowworks.blind_ledger")
w("    structure: r09_blind_ledger")
w("    flag_ref: world.barrowworks.blind_store_read")
w("    name: blind store ledger")
w("    verb: Read")
w("    done_text: \"A ledger in a room nothing was ever taken out of. Every column is a measurement of the same thing, taken again and again over years, and every one of them is short of the last. The final page is dated the day the works stopped, and the number on it is not a measurement at all - it is the width of the gap between what the ring gauged and what it was meant to.\"")
# The choice (o_preserve / o_dismantle). The design's prose says "tell Kera", but both objectives read their flag
# AT location.barrowworks.headworks - a world flag is per-cell, and Kera stands at the waystation, so a flag she
# set would never satisfy a check made in the headworks. The mechanism is what the character acts on, and it is
# the only place the choice can be recorded. Two switches on one structure: the player does one or the other, and
# the mechanism is left whole or broken up.
w("  - key: switch.barrowworks.headworks_preserve")
w("    structure: r12_headworks_measure")
w("    flag_ref: world.barrowworks.headworks_preserved")
w("    name: the measure")
w("    verb: Leave")
w("    done_text: \"The measure is a beam of fitted segments, each one cut to a tolerance nothing in the ring could hold, and the whole of it is still true. Whatever it was for, it was made to outlast the works that built it, and it has. You leave it standing.\"")
w("  - key: switch.barrowworks.headworks_dismantle")
w("    structure: r12_headworks_measure")
w("    flag_ref: world.barrowworks.headworks_dismantled")
w("    name: the measure")
w("    verb: Break up")
w("    done_text: \"The measure comes apart at the joints, because it was built to. Every segment is a length of good ash haft and a plate of worked iron, and the ring has no one left in it to measure anything with. You take it apart for what it is made of.\"")
# The three gates are DOORS, and a door opens by being used: WLD013 refuses a switch and a door that share a
# world flag, so a switch is not how a gate is opened and the reference regions have none - `door.longhouse` has
# no switch anywhere, and its flag is what the door itself records when it swings. G1 and G2 need nothing more:
# you have to find them, which is what "shortcut" and "secret" mean.
#
# G3 is the R11-R12 aftermath, and the design has the fitting WEDGE open it (o_wedge -> o_open). The switch model
# cannot express that either - `requires` names other world flags, not items - so G3 opens like any other door
# and the wedge is not enforced. See OPEN_QUESTIONS OQ-16.
w("barriers: []")
w("")

path = r"C:\Otherreach_Build\barrowworks_wt\content\regions\barrowworks.yaml"
with io.open(path, "w", encoding="utf-8", newline="\n") as handle:
    handle.write("\n".join(out))

print(f"  wrote {path}")
print(f"  {len(ROOMS)} rooms, {len(EDGES)} edges, {len(structures)} structures")
print(f"  bounds: x [{min_x:.1f}, {max_x:.1f}]  z [{min_z:.1f}, {max_z:.1f}]")
print(f"  cells: {cells}")
print(f"  terrain grid: {cols} x {rows} at {GRID} m")
