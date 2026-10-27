# Sedge Levels Gate 3 — art

Gate 2 at `c62b5fc1` was accepted by the coordinator at 01:00 on 2026-10-27. This pass follows the six art notes in CODEX_PROGRESS.md. It remains on `art/sedge-levels`; no push or integration is authorised. Review stills run only on the 6900XT farm.

## Art and provenance

- Peat carries the existing Oct-18 CC0 brown-mud scan with its full-resolution normal map, physical centimetre relief and a wet/puddled roughness response. Pale salt rims use the sourced coastal scan. Rills and contoured basins reach below the accepted low-water datum, so the existing FlowWater surface remains visible in channels at ebb. No painted water or replacement tide clock.
- 66,786 deterministic reed clumps in coherent beds, grouped by height/colour band and world position. Original kit LOD0 geometry remains to 90m, LOD1 to 220m and LOD2 to 650m. Native MultiMesh groups cull in 64m patches. The shared WindField shader supplies sway and wet response; meshes/materials are shared per band/LOD.
- Local noisy FogVolumes form low banks over the sound and drift slowly with shared wind. Existing weather/sky and native particle recipes supply silver overcast and rain. Sedge rain uses that same wind direction; it is not a screen overlay or forced weather in the simulation.
- The accepted PROPOSED 128m Arch crown / 180m width / 108m clear span remains. 3,416 individually jointed masonry blocks, wedge voussoirs and relief ribs replace the smooth mass. The existing CC0 old-stone scan supplies grain; tide lines, damp staining and weed colour are world-height based. This asserts no new builder/date/history.
- Crest-only violet is an open array of animated, additive, wavering light shafts with local violet illumination. The flat coloured aperture polygon is removed. The existing authoritative tide still decides visibility; travel transfer remains Gate 4. Canon's bell timing remains slack water.
- Small bronze pin caps, collapsed midpoint rubble and additional instances of the existing drowned bell-tower kit carry the Causeway. These do not alter its route or solid support.
- Tollpiles retains its kit houses and walkways. Opaque weathered-thatch underlays close holes beneath the source geometric rafters/thatch. Native lantern lights follow the sky's continuous daylight level; punts follow the existing tide surface. Boats/weirs are scenery, with no ferry or NPC claim.
- Stale kit waystones, felt tents and cairns sat on itinerary points, including inside the walking corridor. Their scenery now sits beside the route; civic/travel anchors and walking supports stay fixed. Obsolete static punts on the route are replaced by the four tide-following moored punts.

All source images remain the existing real CC0/kit images; no generated images, new purchases or licensed source uploads. The external `SEDGE_SOURCE_CREDITS.json` retains the original BOM and records added scan source paths and SHA256s. Standard CPU trimesh authoring/export and native Godot SurfaceTool prepare normals/tangents.

## Reproduction and checks

`python tools/sedge/build_gate3.py --base <accepted-gate2-primary> --output <new-primary>` starts from the accepted Gate-2 region and external art. It preserves every walk support and tide field and asserts that terrain within 9m of the route / 13m of the road is unchanged. It does not silently move the road, Arch join or civic anchors. `tools/sedge/prepare_tangents.gd` takes the external root and an optional comma-separated model list for the new static assets. Then prepare the signed native BC7 cache, preserving existing BC6H sky entries; run `tools/release/check_texture_cache.py` against the candidate/fallback pair.

The existing Sedge route proof is rerun against the art terrain, including both tide states, ramps, navigation and closure; content lint checks the region. Actual packaged PNGs are inspected as images before a FINISHED line is posted. Build, GPU, capture logs and image hashes accompany that review.

Gate 4 still owns inhabitants, services, encounters, move/variant sets, quest and functional ferry/Otherhome travel. Gate 5 still owns the final 12GB VRAM / 30GB release proof. Stop after Gate 3 for review.
