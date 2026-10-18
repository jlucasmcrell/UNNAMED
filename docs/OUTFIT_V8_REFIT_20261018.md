# Outfit v8 refit — work in progress

**Superseded construction approach (owner, 2026-10-18).** All old garment
warping trials below are rejected/private and stopped. Continue with new
construction directly on fixed approved bodies; see
`FRONTIER_WARDROBE_REBUILD_20261018.md`. The separate perf/sky lane now owns
frontier work. No wardrobe acceptance is implied by the renderer checks below.

The owner ordered A integration, B approved-body motion, C zero-clipping outfits,
then D frontier performance and the unpublished release candidate. A/B are shared
at `d43b6dbf`. C is isolated; frontier remains paused at `ad21d492`.

## Isolated inputs

`D:\Otherreach_Scratch\outfit_v8_20261018\assetbase` is an independent plain-file
copy of B's installed root. Approved source exports, A/B roots and the immutable
20261015b playtest are untouched. Stale garment bakes are not installed.

The private trials start from authored v8 garments, refit even Average from its
old fit skeleton to the approved 167-joint body, retain eight skin influences
where present, and fit trousers inside boots and the shirt above the trousers.
The pose helper agrees with the game/gate within 0.007 mm on the measured points.

## Rendering support

Legacy UV2.x anatomical masks keep their behavior. Optional per-source-vertex
flags use raw glTF UV2.y `-(flags + 1)`, with nonnegative values retaining the
legacy meaning. Each garment's optional `occlusion_mask` supplies its bit.
The body receives all worn bits; a garment receives only higher layers' bits.
Ten bits remain exact in half-float UV attributes. No body positions, weights,
rigs or primary texture coordinates change when these masks are written.

The profile harness accepts `--anim-splay <degrees>` before correctives and logs
garment count and imported UV2 ranges. Its defaults preserve the B captures.

## Validation so far — not outfit acceptance

- Release build: zero errors; Presentation 243 and Architecture 23 tests pass.
- Native female Average: 108 solo front/side/back images, three worn garments,
  splay 2 degrees. Imported negative UV2 values are preserved at all four LODs;
  no mask shader compilation error. This is the initial trial, before the
  targeted cloth fit. Baseline GodotGAS missing-registry warning remains.
- Initial mask trials eliminate sampled body/layer holes on female Average,
  but still fail skin and boot/inner-leg clipping. The native jog side view
  also exposes underarm bunching/opening: visually rejected.
- A targeted shirt fit reaches zero sampled LOD0 skin faults against its input
  masks. Rebuilding masks exposes additional boundary points; the independent
  combined gate still fails. This is not a zero-clipping claim.
- Unconstrained trouser fitting diverges and is rejected. Its best input is
  retained; own-leg clearance and the mixed-weight seam band are under test.
- Male Average's first combined gate also fails. Other builds, outer garments,
  combat clips, reduced-LOD gates and full-figure motion are not accepted yet.

Private logs, reports and rejected trials remain under the root above. No C
asset binding or body-content update is ready for the shared branches.
