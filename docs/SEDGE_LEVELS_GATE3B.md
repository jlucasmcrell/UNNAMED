# Sedge Levels — Gate 3b art review

Scope: Claude's 2026-10-27 02:30 review. Gate 3's accepted fen, channels, salt, rain, dusk lamps and crest shimmer remain the base. Stop here for review; Gate 4 life/travel is separate.

The rejected block-built Arch is replaced with existing owned Dwarven Citadel meshes: `SM_ST_MainHall_Arch_A_01` (three separated vault ribs), `SM_ST_ExteriorGate_DESColumnM01` (four broken projecting piers), and `SM_ST_MainGate_Capital_01` (carved crown, springing capitals and fallen pieces). The front rib has a missing crown section. Source UVs and original-resolution PBR images are retained; region-only wet darkening, weed and salt bloom weather the source surfaces. Native kit reed clumps supply the lower weed silhouette. The adopted 180 m envelope and approximately 128 m crown are still PROPOSED art dimensions, not new canon history.

Drowned bell towers reuse the Oct-18 stone lower storey and Cosmos CS1_Bell_01. The original bell was microscopic; its source mesh is restored to a two-metre mouth inside an open four-sided kit belfry. The bell hangs from an iron hanger beneath the timber headstock. UV-preserving clipping seats the posts into the old walls. Old solid caps are removed, source capital fragments form broken tops, and six towers lean at varied distances. The bell remains slack-water canon; no scripted ringing is added by this art gate.

Review boardwalks are now actual four-metre modules with .238 m-wide boards and metre-based UVs. Placement length is divided by four; deck height, width and every domain support remain unchanged. Original weathered wood remains the material.

Eight noisy native FogVolumes (18–26m bank height, with no exponential height suppression) layer the water, with depth haze and three native spotlights scattering through the air at breaks in the overcast. Light energy follows the existing daylight level. The crest's accepted violet style remains, with column heights clipped to the new aperture. Authoritative tide timing, span closure, routes, terrain and reed scatter are unchanged.

## Reproduction

Use the existing `tools/asset_pipeline/ue_export/convert_ue.py` on those three manifests under `C:/Otherreach_External_Assets/UE_Exports/Dwarven_Citadel/Manifests/`, mapping to `sedge_arch_rib`, `sedge_arch_pier`, `sedge_arch_capital`. Flags: `--res 4096 --lods , --category structure`; default `--max-tris 0` preserves geometry. All source metadata and hashes stay beside the external converted models. No licensed models/textures are committed.

Run `python tools/sedge/build_gate3b.py --base <accepted Gate-3 primary> --output <new primary> --converted <converted>`. Prepare the seven new/replaced models with `tools/sedge/prepare_tangents.gd` in the blank CPU project, then update their signed full-resolution texture cache entries. Preserve the base manifest's HDR/sky/UI entries. Build/export with the pinned Godot 4.7.2 .NET 8 toolchain. Farm candidates and review receipts record the exact published DLL and external asset hashes.

All rendering is packaged, native 1920×1080 stills on the 6900XT farm. CPU headless conversion/cache/export on BEAST; no 3090 use, no pushes. Final stills and measured validation are recorded in the review gallery and CODEX_PROGRESS.md; counts alone do not establish visual acceptance.
