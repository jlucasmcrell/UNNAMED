"""Generate the Barrowworks' thirteen-room blockout as region content (P2).

Geometry is DERIVED, not hand-authored: thirteen rooms with four walls each plus corridors is well over a
hundred boxes, and hand-typing them would make the connectivity graph unreviewable. The room table and the
edge list below are the source of truth, copied from docs/BARROWWORKS_PLAN.md sections 1 and 1.1, which
itself comes from the frozen 03_RECOMMENDED_LEVEL_DESIGN.md and 04_LEVEL_MAPS.md.

Only walls used by real edges have doorways. Corridors are carved through a solid enclosing mass;
the court approach has two blind turns. Geometry drives both collision and the modular art assembly.

The whole interior is offset so every coordinate is positive: cells index from 0 and R04's west wall at
x = -47 m would otherwise need cell -1.
"""
import io
import json
from pathlib import Path

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
    if (a, b) == ("R10", "R11"):
        opening_sides.setdefault(a, set()).add("w")
        opening_sides.setdefault(b, set()).add("w")
        continue
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
# A doorway is centred on a room's clear span, so a corridor that starts anywhere else misses it and the wall closes
# the room off. Each edge gets an L from A's own doorway to B's own, in shifted metres.
half = CORRIDOR / 2
for a, b, kind in EDGES:
    if (a, b) == ("R10", "R11"):
        # Two blind turns west of the rooms. A straight run across their short north/south gap let a player
        # shoot the court from the retreat marker; these west-facing openings break that sightline.
        floors.extend([
            ("corr_r10_r11_retreat", (32, 82.5, 43, 85.5)),
            ("corr_r10_r11_dogleg", (30.5, 84, 33.5, 106)),
            ("corr_r10_r11_court", (32, 104.5, 36, 107.5)),
            ("corr_r10_r11_turn_a", (30, 82, 34, 86)),
            ("corr_r10_r11_turn_b", (30, 104, 34, 108)),
        ])
        continue
    ax, az = shift(*by_id[a][1])
    bx, bz = shift(*by_id[b][1])
    a_clear = clear_rect(a)
    b_clear = clear_rect(b)
    a_mid = ((a_clear[0] + a_clear[2]) / 2, (a_clear[1] + a_clear[3]) / 2)
    b_mid = ((b_clear[0] + b_clear[2]) / 2, (b_clear[1] + b_clear[3]) / 2)

    if abs(bz - az) >= abs(bx - ax):
        # Stacked in Z: A's doorway on its north or south wall, B's on the facing one, both on their own mid-X.
        a_z = a_clear[3] if bz > az else a_clear[1]
        b_z = b_clear[1] if bz > az else b_clear[3]
        za, zb = (a_z, b_z) if bz > az else (b_z, a_z)
        floors.append((f"corr_{a.lower()}_{b.lower()}_a_z", (a_mid[0] - half, za, a_mid[0] + half, zb)))
        if abs(a_mid[0] - b_mid[0]) > 0.001:
            lo, hi = min(a_mid[0], b_mid[0]), max(a_mid[0], b_mid[0])
            z_at_b = zb if bz > az else za
            floors.append((f"corr_{a.lower()}_{b.lower()}_x", (lo - half, z_at_b - half, hi + half, z_at_b + half)))
    else:
        # Side by side in X: the doorway is in a wall of constant X, on each room's mid-Z.
        a_x = a_clear[2] if bx > ax else a_clear[0]
        b_x = b_clear[0] if bx > ax else b_clear[2]
        xa, xb = (a_x, b_x) if bx > ax else (b_x, a_x)
        floors.append((f"corr_{a.lower()}_{b.lower()}_a_x", (xa, a_mid[1] - half, xb, a_mid[1] + half)))
        if abs(a_mid[1] - b_mid[1]) > 0.001:
            lo, hi = min(a_mid[1], b_mid[1]), max(a_mid[1], b_mid[1])
            x_at_b = xb if bx > ax else xa
            floors.append((f"corr_{a.lower()}_{b.lower()}_z", (x_at_b - half, lo - half, x_at_b + half, hi + half)))
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
        lo = shift(*by_id[a][1])[0]
        z = a_o[3] if bz > az else a_o[1]
        doors.append((gate, (lo - OPENING / 2, z - 0.4, lo + OPENING / 2, z + 0.4)))
    else:
        # Stacked in X: the doorway is in a wall of constant X, centred on the Z they share.
        lo = shift(*by_id[a][1])[1]
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
# The mass is the walk space MINUS the rooms and corridors. A ring of four walls left the rooms standing free in an
# open field - the flood fill crossed the whole space in one sheet and a body walked round every box rather than
# through it, which is why G1-G3 gated nothing (review 3, B7, and the top-down plot of the fill).
CELL_ROCK = 0.5     # preserve a 3 m corridor beside a 0.6 m wall; 2 m centre sampling sealed G1's outer bend
_corridor_rects = [r for _, r in floors]


def _in_room(x, z):
    return any(o[0] - 0.05 <= x <= o[2] + 0.05 and o[1] - 0.05 <= z <= o[3] + 0.05
               for o in (outer_rect(rid) for rid, _, _, _ in ROOMS))


def _in_corridor(x, z):
    return any(c[0] - 0.05 <= x <= c[2] + 0.05 and c[1] - 0.05 <= z <= c[3] + 0.05 for c in _corridor_rects)


# Merge adjacent solid cells into rectangles without changing a millimetre of the carved walk space.
# Thousands of individual rock tiles were unnecessary simulation broad-phase work and camera bodies.
_rock = 0
active_runs = {}
gz = min_z
while gz < max_z - 0.001:
    z1 = min(gz + CELL_ROCK, max_z)
    runs, start = [], None
    gx = min_x
    while gx < max_x - 0.001:
        x1 = min(gx + CELL_ROCK, max_x)
        solid = not _in_room((gx + x1) / 2, (gz + z1) / 2) and not _in_corridor((gx + x1) / 2, (gz + z1) / 2)
        if solid and start is None:
            start = gx
        if not solid and start is not None:
            runs.append((start, gx))
            start = None
        gx = x1
    if start is not None:
        runs.append((start, max_x))
    for run in list(active_runs):
        if run not in runs:
            box(f"rock_{_rock:04d}", run[0], active_runs.pop(run), run[1], gz, 40.0)
            _rock += 1
    for run in runs:
        active_runs.setdefault(run, gz)
    gz = z1
for run, z0 in active_runs.items():
    box(f"rock_{_rock:04d}", run[0], z0, run[1], max_z, 40.0)
    _rock += 1

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
w("# Barrowworks interior - thirteen connected rooms (M8 D10).")
w("#")
w("# GENERATED by tools/gen_barrowworks_blockout.py from the room table and edge list in")
w("# docs/BARROWWORKS_PLAN.md sections 1 and 1.1, which come from the frozen 03_RECOMMENDED_LEVEL_DESIGN.md")
w("# and 04_LEVEL_MAPS.md. Edit the generator, not this file.")
w("#")
w("# Doorways exist only on connected walls. Corridors are carved through a solid mass, with a blind court approach.")
w("")
w("id: region.barrowworks")
w("kind: region")
w("schema: 1")
w("display_key: region.barrowworks.name")
w("tags: [region, interior]")
w("notes: >-")
w("  The Barrowworks interior, thirteen rooms R01-R13, as a connected interior. Its own space, so its local")
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
    ("r02_notice_board",     56.2, 23.0, 56.6, 25.0, 1.6, "the withdrawal notice, on solid masonry south of the intake hall's east opening"),
    ("r07_fitting_stand",    65.4, 57.4, 65.8, 60.6, 1.4, "the fitting note, on the gauge archive's west wall"),
    ("r07_diagram_rack",     78.2, 57.4, 78.6, 60.6, 1.8, "the diagram rack, on the gauge archive's east wall"),
    ("r09_blind_ledger",     96.2, 57.4, 96.6, 62.6, 1.5, "the blind store's ledger, against the blind store's east wall"),
    ("r12_preserve_control", 45.4, 127.0, 46.0, 127.6, 1.1, "the retaining saddle, left of the measure"),
    ("r12_dismantle_control", 45.4, 134.0, 46.0, 134.6, 1.1, "the release saddle, right of the measure"),
]:
    box(sid, x0, z0, x1, z1, h)
for sid, rect, height in [
    ('r03_wash_pipe', (22.5,35.0,23.3,38.0),2.0),
    ('r04_drain_valve', (5.0,41.2,8.0,42.0),1.8),
    ('r05_sorting_rack', (23.0,54.0,26.0,55.0),1.4),
    ('r06_work_seat', (44.0,64.0,46.5,65.0),.6),
    ('r08_counterweight', (75.5,40.0,76.5,42.0),2.4),
    ('r10_retreat_seat', (53.0,88.0,55.5,89.0),.6),
    ('r13_inspection_gauge', (76.0,85.0,77.0,88.0),1.8),
]:
    box(sid, *rect, height)
for sid, (x0, z0, x1, z1) in floors:
    box(sid + "_ceiling", x0, z0, x1, z1, 4.3, clearance=4.0)
w("# Solid walls and rock; corridor ceilings begin at 4 m clearance.")
w("structures:")
for sid, entry in structures:
    w(f"  - {{ id: {sid}, {entry} }}")
w("")
# The three gates the design's own edge list names (04_LEVEL_MAPS.md): G1 is the R08-R02 shortcut, G2 the
# R07-R09 secret, G3 the R11-R12 aftermath. Each is a door in the edge's opening, shut until its flag is set.
w("# G1-G3 remain open once earned. G3 takes the Custodian defeat and a fitting wedge, which stays fitted in the door.")
w("doors:")
for gate, (x0, z0, x1, z1) in doors:
    lock = (", latch_side: east, locked_text: 'The latch is on the gallery side.'" if gate == "g1" else
            ", requirement: { flag_ref: world.barrowworks.ring_custodian_defeated, location_ref: location.barrowworks.r11_measuring_court }, fitted: { item_ref: item.tool.fitting_wedge, count: 1 }, locked_text: 'The court interlock still holds. Stop the Custodian, then fit a wedge into its keyway.'" if gate == "g3" else "")
    w(f"  - {{ key: door.barrowworks.{gate}, flag_ref: world.barrowworks.gate_{gate}_open, "
      f"box_m: [{x0:.1f}, {z0:.1f}, {x1:.1f}, {z1:.1f}], height_m: 2.4, stays_open: true{lock} }}")
w("containers:")
w("  - { key: container.barrowworks.archive_crate, loot_ref: loot.barrowworks.archive_crate, position_m: [74.5, 62.5], stack_slots: 4 }")
w("  - { key: container.barrowworks.codex_fragment, loot_ref: loot.barrowworks.codex_fragment, position_m: [69.0, 63.0], stack_slots: 1, flag_ref: world.barrowworks.codex_fragment_taken }")
w("  - { key: container.barrowworks.lantern_foot, loot_ref: loot.barrowworks.lantern_foot, position_m: [92.0, 62.0], stack_slots: 1, flag_ref: world.barrowworks.lantern_foot_taken }")
w("npcs: []")
w("nodes:")
w("  - { name: barrowworks_ash_stand, node_ref: node.wood.ash_stand, position_m: [74, 64] }")
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
w("    recipe_ref: recipe.smithing.fitting_wedge")
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
w("    structure: r12_preserve_control")
w("    flag_ref: world.barrowworks.headworks_preserved")
w("    name: retaining saddle - preserve the measure")
w("    verb: Lock")
w("    excludes: [world.barrowworks.headworks_dismantled]")
w("    requirements:")
w("      - { flag_ref: world.barrowworks.withdrawal_notice_read, location_ref: location.barrowworks.intake_hall }")
w("      - { flag_ref: world.barrowworks.fitting_note_read, location_ref: location.barrowworks.gauge_archive }")
w("      - { flag_ref: world.barrowworks.diagram_rack_read, location_ref: location.barrowworks.gauge_archive }")
w("      - { flag_ref: world.barrowworks.ring_custodian_defeated, location_ref: location.barrowworks.r11_measuring_court }")
w("    locked_text: 'Read the notice, fitting note and diagram, and stop the Custodian first. Working either saddle settles the choice.'")
w("    done_text: \"The measure is a beam of fitted segments, each one cut to a tolerance nothing in the ring could hold, and the whole of it is still true. Whatever it was for, it was made to outlast the works that built it, and it has. You leave it standing.\"")
w("  - key: switch.barrowworks.headworks_dismantle")
w("    structure: r12_dismantle_control")
w("    flag_ref: world.barrowworks.headworks_dismantled")
w("    name: release saddle - dismantle the measure")
w("    verb: Open")
w("    reward: { item_ref: item.material.iron_ingot, count: 2 }")
w("    excludes: [world.barrowworks.headworks_preserved]")
w("    requirements:")
w("      - { flag_ref: world.barrowworks.withdrawal_notice_read, location_ref: location.barrowworks.intake_hall }")
w("      - { flag_ref: world.barrowworks.fitting_note_read, location_ref: location.barrowworks.gauge_archive }")
w("      - { flag_ref: world.barrowworks.diagram_rack_read, location_ref: location.barrowworks.gauge_archive }")
w("      - { flag_ref: world.barrowworks.ring_custodian_defeated, location_ref: location.barrowworks.r11_measuring_court }")
w("    locked_text: 'Read the notice, fitting note and diagram, and stop the Custodian first. Working either saddle settles the choice.'")
w("    done_text: \"The measure comes apart at the joints, because it was built to. Every segment is a length of good ash haft and a plate of worked iron, and the ring has no one left in it to measure anything with. You take it apart for what it is made of.\"")
# The three gates are DOORS, and a door opens by being used: WLD013 refuses a switch and a door that share a
# world flag, so a switch is not how a gate is opened and the reference regions have none - `door.longhouse` has
# no switch anywhere, and its flag is what the door itself records when it swings. G1 and G2 need nothing more:
# you have to find them, which is what "shortcut" and "secret" mean.
#
# G3 is the earned aftermath interlock (owner ruling OQ-16): the durable Custodian defeat AND a fitting wedge.
# The wedge leaves the pack and stays fitted in the door, whose open flag records it; the saddles take none.
w("barriers:")
w("  - { key: barrier.barrowworks.measure, flag_ref: world.barrowworks.headworks_dismantled, box_m: [42.1, 128.4, 43.1, 133.6], height_m: 2.2, prompt: 'The measure. Its retaining and release saddles are beside it.' }")
w("")

path = Path(__file__).resolve().parents[1] / 'content/regions/barrowworks.yaml'
with io.open(path, "w", encoding="utf-8", newline="\n") as handle:
    handle.write("\n".join(out))
Path(__file__).with_name('barrowworks_geometry.json').write_text(
    json.dumps({'corridors': floors}, indent=2)+'\n', encoding='utf-8')

print(f"  wrote {path}")
print(f"  {len(ROOMS)} rooms, {len(EDGES)} edges, {len(structures)} structures")
print(f"  bounds: x [{min_x:.1f}, {max_x:.1f}]  z [{min_z:.1f}, {max_z:.1f}]")
print(f"  cells: {cells}")
print(f"  terrain grid: {cols} x {rows} at {GRID} m")
