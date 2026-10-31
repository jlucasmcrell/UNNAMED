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
  stair boards and clear walking centre. No new art is required.
  The exposed sloping strips between boards and cheek stone receive the same
  native cobble PBR facing at the district's authored 0.75 UV scale, conforming
  to the terrain. Packed albedo/height and normal/roughness are read as terrain
  maps; they are not world-material records.
  Camera colliders use the exposed height up to the upper landing plus 15 cm,
  rather than the full buried stone-piece height.

Release build: zero errors. Focused people/finishing checks: two pass.
Content lint: 968 definitions, zero errors. Five native 1.6 m eye-height
frames are posted at `F:\Otherreach_Captures\steps\codex_people_20261031\environment`,
with `NATIVE_STEPS_FINISHING.jpg`, original hashes and capture provenance. The
final runtime source is `3da50cfc`; the diagnostic process exited 0 after a
264.10-second GPU lease. Bank facing, backfill and curved ward volume were
visually inspected. Visible material transitions and the diagnostic host's
sky triangle artifacts remain disclosed for Claude's review. This is not
complete RC, normal packaged teardown or performance acceptance. Claude's
visual verdict remains pending. See `STEPS_VISUAL_PROOF.json` for the receipt.
