# Owner visual bug fixes, 2026-10-30

Marketing is stopped. This work is bug evidence only, on `fix/owner-visual-bugs` from `303bdbe8`; the installed Test02 DLL remains untouched. Claude owns B-016 and B-001/B-017. No new area is merged.

## B-014: garments, initially described as Kal wings

Native Veth NERIS reproduces the RGB spikes. The scene paths are the creator wardrobe, not the distant NPC Kal wing nodes. Three independent faults meet:

- `HumGenLods` did not recognise the authored `_LOD<number>_lining` suffix, so all lining bands were drawn. Creator headwear visibility also overrode the selected band. Linings now select their own matching band; headwear goes through the existing outfit visibility controller.
- The creator part exporter zeroed the four rigs, then called HumGen `merge_levels` before Blender updated child world matrices. That method preserves each old matrix while reparenting. Garment LOD1/2/3 therefore retained the in-file preview offsets of 4/6/8 metres, despite sharing the base skeleton. Flush transforms before merging.
- Distant garment surfaces kept procedural RGB masks (including vertex colour masks), rather than LOD0's baked atlas. Reuse the existing native material repair for both outer surfaces and linings, preserving separately baked native LOD atlases.

`tools/animation/fix_humgen_lod_materials.py input.glb output.glb --creator-staging-offsets` repairs delivered copies with the measured offset signature. It translates only base POSITION data whose paired bounds match that signature; normals, skin joints/weights, morph deltas, topology and source textures are retained. Already aligned parts are unchanged on a second run. The exporter invokes material repair for new wardrobe exports; its transform flush prevents new spacing errors.

Private delivery: `D:\Otherreach_Scratch\visual_bug_art_20261030\frontier`; exact original/repaired SHA256 per wardrobe part in `E:\Otherreach_Scratch\visual_bug_proof_20261030\B014_asset_repair.json` (63 parts). Changed GLBs require freshly signed full-resolution texture-cache entries before packaging; offline editor cache generation is in progress. Do not copy this code without its repaired private art and cache.

Checks so far: Release build passes (existing warnings); CharacterLodBand tests 8/8; asset repair regressions 2/2, covering lining material, spacing, morph-byte preservation, repeat-run safety and sparse-accessor/native-atlas preservation. Native baseline posed bounds confirm a roughly 0.68m shirt at LOD0 versus 4.83m wide/5.91m tall at LOD2. Evidence: `E:\Otherreach_Scratch\visual_bug_proof_20261030\b014_lod01\b014_lod_bands`.

B-014 remains open pending the repaired native motion/posed-bounds comparison and packaged-build recheck, including the creator and actual Kal wings.

## B-005 and B-013

Cairnwold's three jetty posts used a 3.4m timber wall panel. Bind them to `prop_cw_jetty_piling`, a vertical 0.32m x 3.8m timber derived from the accepted `prop_bp_beam_mossy`; the existing three LODs and every embedded image/mesh BIN byte are retained. `tools/release/stage_cairnwold_piling.py <private-art-root> <receipt.json>` stages that derivative, inherits source metadata/credits, and aliases the unchanged signed image cache. Its three generated GLBs match the earlier native proof byte for byte. The content also moves five birches and three boulders off the pond/jetty, removes five bank-wall panels crossing the water, and returns the workbench/cookfire to the dry bank. No station IDs or behaviour change. Existing native bounds show deck minimumY4.45, piling minimumY1.95 (pond bed) / maximumY5.75, waterY4.35. WorldContent tests 29/29 and content lint 0 errors passed for this correction. Evidence/receipt: `E:\Otherreach_Scratch\build2_test02_codex_20261029\marketing_capture\cairnwold_fix_review`, native motion in `film_batch02_retake/test02_b02_cairnwold_v2`. The correction changes world baselines; Claude's B-016 work must register its transition before any owner installation. Packaged recheck remains owed, so B-005 is not closed.

B-013 is being audited on private copies of both installed art roots. Use the fixed release gate from `1504032d` and offline cache tool `c0be7551`. Preserve original map resolution and all approved dependency art/credits. No live shared-root manifest or owner-installed root may be modified during this proof.

## B-004

Check native Gate Tender body/animation separately from the canonical Foldscar hanging masonry. The D19 review identifies the distant hanging column as intentional Foldscar dressing. Do not remove canonical geometry to conceal the report; native scene attribution and packaged evidence are still owed.
