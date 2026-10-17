"""Generate the Barrowworks' thirteen rooms as named locations (P2, and P4's groundwork).

Locations are separate content files with `kind: location` and a `region_ref`; the region builder collects
the ones whose region_ref matches, and SurveyShots builds one camera station per location. So this is both
what makes the per-room screenshots possible and what P4 needs anyway - the plan's section 1 already lists
each room's purpose and player beat.

The names, order and purposes come from docs/BARROWWORKS_PLAN.md section 1, which comes from the frozen
03_RECOMMENDED_LEVEL_DESIGN.md. Nothing here is invented.
"""
import io
import os

OUT = r"C:\Otherreach_Build\barrowworks_wt\content\locations\barrowworks"

# id suffix, display name, centre in the blockout's shifted metres, xp, purpose (for the notes field)
ROOMS = [
    ("r01_threshold", "Threshold", (50, 10), 15,
     "Entrance A. A split stone lintel and a worn delivery groove: a safe look into the hall, not at an enemy."),
    ("r02_intake_hall", "Intake Hall", (50, 28), 15,
     "The first choice - the open west service mouth against the visibly latched east grille, with the scale arm in sight through it."),
    ("r03_wash_passage", "Wash Passage", (28, 38), 10,
     "The first threat. A shallow wet floor against a dry safe edge; the spider is visible before its detection boundary."),
    ("r04_drain_mouth", "Drain Mouth", (8, 38), 15,
     "Entrance B. A narrow exterior approach that opens inside; the sightline to the wash passage stops before the sorting floor."),
    ("r05_sorting_floor", "Sorting Floor", (28, 60), 15,
     "Escalation. Two work tracks divide around a low non-blocking tray: observe, separate, or fight."),
    ("r06_still_bay", "Still Bay", (50, 60), 10,
     "A quiet beat and the midpoint's orientation: a silent bench, repair fragments, and the warning that lies ahead."),
    ("r07_gauge_archive", "Gauge Archive", (72, 60), 20,
     "The discovery. Changed graduation marks, a fitting diagram, and a visible gap on the shelf: the craft is learned here."),
    ("r08_return_gallery", "Return Gallery", (72, 38), 15,
     "The earned shortcut. A reachable mechanical latch, with a daylight and dust cue leading back to the intake hall."),
    ("r09_blind_store", "Blind Store", (92, 60), 25,
     "The secret. Inspect the shelf gap, push the cover, and find a discarded earlier repair."),
    ("r10_service_vestibule", "Service Vestibule", (50, 84), 15,
     "Before the Custodian. A solid dogleg removes every firing line into the arena; slow strikes are audible before the danger is."),
    ("r11_measuring_court", "Measuring Court", (50, 106), 25,
     "The arena and its mastery: a wide plain floor, readable ring markings, and broad turning room."),
    ("r12_headworks", "Headworks", (50, 131), 25,
     "The aftermath and the choice. A damaged head, two explicit handles, and the information before the confirmation."),
    ("r13_inspection_bay", "Inspection Bay", (72, 84), 10,
     "An optional combat and lookout loop: a screened view that hints at the arena's proportions without allowing a shot."),
]

os.makedirs(OUT, exist_ok=True)

TEMPLATE = """id: location.barrowworks.{slug}
kind: location
schema: 1
display_key: location.barrowworks.{slug}.name
tags:
- location
- barrowworks
notes: >-
  {note}
name: {name}
region_ref: region.barrowworks
anchor:
  world:
  - {x}
  - 0.0
  - {z}
kind_of_place: landmark
discovery:
  methods:
  - visited
  radius_m: 6
  xp: {xp}
map:
  reveals_radius_m: 12
  marker: none
fast_travel:
  node: false
"""

written = []
for slug, name, (x, z), xp, purpose in ROOMS:
    note = (f"{name} (room {slug[1:3].upper()}), a chamber of the Barrowworks. {purpose} "
            "Placement and radius are the blockout's and provisional.")
    body = TEMPLATE.format(slug=slug, name=name, x=f"{x}.0", z=f"{z}.0", xp=xp, note=note)
    path = os.path.join(OUT, slug + ".yaml")
    # A temporary beside the destination, then a move: never open a destination for write.
    tmp = path + ".tmp"
    with io.open(tmp, "w", encoding="utf-8", newline="\n") as handle:
        handle.write(body)
    os.replace(tmp, path)
    written.append(slug)

print(f"  wrote {len(written)} location files to {OUT}")
print(f"  {', '.join(written)}")
