# The Steps — finishing pass

Includes Claude's north-quay pass through `2e03b6da`. Its boats, crane, nets,
rope, cargo, parapet placements and 0.4 m boat draft are preserved. The owner-retained log sheds and removal of the lone quay
wicker hurdles are included.

- The Drowned Gate reuses the existing FoldWard rune ring and drifting motes.
  Its ring follows the shore or water surface, its rune density scales to the
  22 m radius, and the motes occupy the full standing volume. The existing
  collision remains a 22 m radius, 14 m high circle. Other folds keep their
  previous small-ring appearance.
- Thirty-six joined, flat support fields close the space behind the native
  terrace retaining walls. One combined mesh shares the existing district ground material on flat
  backfill and uses native packed cobble PBR maps on the exposed stair banks.
  Walking and camera collision use those same authored supports.
  The old pier supports and the stair surfaces are not rendered as backfill.
- The 24 stair cuts receive 192 native drystone cheek pieces. Their exposed
  tops follow the upper landing; their lower portions sit in the bank. The
  cheeks meet the actual 6 m terrain-cut boundaries, retaining the native
  stair route and clear walking centre. No new art is required.
  The exposed sloping strips between treads and cheek stone receive the same
  native cobble PBR facing at the district's authored 0.75 UV scale, conforming
  to the terrain. Packed albedo/height and normal/roughness are read as terrain
  maps; they are not world-material records.
  Camera colliders use the exposed height up to the upper landing plus 15 cm,
  rather than the full buried stone-piece height.

Release build: zero errors. Focused people/finishing checks: three pass.
Content lint: 968 definitions, zero errors. Five native 1.6 m eye-height
frames are posted at `F:\Otherreach_Captures\steps\codex_people_20261031\environment`,
with `NATIVE_STEPS_FINISHING.jpg`, original hashes and capture provenance. The
initial finishing runtime source is `3da50cfc`; the diagnostic process exited 0 after a
264.10-second GPU lease. Bank facing, backfill and curved ward volume were
visually inspected. Visible material transitions and the diagnostic host's
sky triangle artifacts were disclosed for Claude's review. This is not
complete RC, normal packaged teardown or performance acceptance. Claude's
13:10 verdict passed the ward and stair-top cuts; the merge conditions and
subsequent ring diagnosis are recorded below. See `STEPS_VISUAL_PROOF.json`.

## Owner ring control — 31 October

Claude's 13:10 verdict passed the people, ward volume and stair-top cuts, with
two merge conditions: foot treads and the pale sky wedge. `0f801f78` replaces
all 96 timber-pad tread bindings with solid stone using the existing native
lichen-rock material. The existing structural box seats 0.2 m into the lowest
terrain cut, keeps its authored top and collider, and has no round feet.

The same RC66 gate frame with only `or_arc_on` toggled true/false/true proves
that B-033 is the world's intended surface ring reading as a flat hard-edged
wedge. This reproduces with the RC's actual sky resources, B-006 fog settings
and atmosphere; it is not confined to the older diagnostic host. See
`native_verdict5_ring` under the capture work root for untouched on/off frames,
settings, live profile pixels, EXR and per-frame receipts.

The sky design in `asset_work/sky/DECISIONS.txt`, `sheets_mix`,
`sheets_banding` and the committed `Environment/sky/SOURCES.md` calls for a
broad inclined banded arc, Cassini/Encke gaps, soft edges and atmospheric
horizon fade. The 4096x4 float RGBA Cassini PIA06175 profile is loaded correctly:
its opacity contains the density and gaps, while RGB is brightness-normalized.
`or_arc` omitted this opacity from the light it added to the sky, flattening
that structure and bypassing filtered edge coverage. `da243d96` applies the
computed opacity to the arc radiance. The ring stays enabled; its profile,
orientation, radial span and atmospheric extinction are unchanged.

Corrected native verification completed at `da243d96`: seven untouched
1920x1080 frames, exit 0, a 252.84-second GPU lease. The loaded runtime shader
include equals the committed public source. The actual RC66 embedded sky
resources and RC atmosphere are used with the Steps managed/content overlay;
a one-file preview pack supplies the committed shader include. A normal export
packages that source directly; no diagnostic dependency is shipped.

The fixed ring on/off pair, before/fixed/off/upward contact and fresh
foot/top/terrace/gate sheet are posted under
`F:\Otherreach_Captures\steps\codex_people_20261031\sky_ring_fixed`.
Originals, shader/profile probes, settings, hashes and host/air receipts are
beside them. Corrected native views show radial density and major/fine gaps;
the stone treads have solid risers without round feet. Claude's follow-up
visual verdict remains pending. This is not full RC or performance acceptance.
