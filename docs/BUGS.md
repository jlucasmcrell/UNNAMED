# BUGS — The Steps (harbour district)

Kept here because both bugs are visible in a proof frame and both are the kind that only a frame catches.

## BUG-ST-01 — quay wall drawn as separate strips with water between them

**Seen**: `F:\Otherreach_Captures\steps\gate2_final3\hero\near.png` (and its upper-left crop, `crop_floater.png`). The quay's
waterline is a row of short stone blocks with gaps between them, not a wall.

**Cause**: every quay-wall run is one `wall_cos_drystone_02_l400` placement in a box 1.20 m across and 4.00 m along the run. The
model is authored four metres long; the fitter's automatic turn lays its length along the run, and the remaining 1.2 m across is
then *scaled* to fill the box, so each piece is compressed to a short block and the run reads as separate strips with the sound
showing between them. The same binding is used by 186 quay-wall runs and 301 retaining-wall segments.

**Fix**: `"fit": "tile"` on every drystone-wall binding (487 placements). The tiling path repeats the model at its own size along
the run instead of stretching it to the box, which is what that path exists for.

**Status**: fixed in content; a frame is needed to confirm (the re-shoot is queued).

## BUG-ST-02 — a boulder floating above the waterline

**Seen**: the same harbour frame — a brown boulder sitting above the water with nothing under it, in the upper left of the frame
and again at the right edge where the shore rocks meet the wall.

**Cause (as far as the data says)**: the shore line carries `oh_shore_rock_*` placements on 1.40 x 1.40 m footprints at the
waterline, and the gate's `rock_ph_ironstone_boulder_a` stones stand on the seabed with their bases hidden by the water plane. Where
the terrain falls away below the water surface faster than the rock's own base, the rock's visible body sits in the air.

**Fix (planned, not yet applied)**: place the shore rocks against the *drawn* ground with a sink offset (the `sink` field the
dressing rules already use) so each rock's base is under the sand, and skip any shore rock whose ground is below the sound's level.

**Status**: open. This entry exists because the owner caught it in a frame before I did.
