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

Cairnwold's three jetty posts used a 3.4m timber wall panel. Bind them to `prop_cw_jetty_piling`, a vertical 0.32m x 3.8m timber derived from the accepted `prop_bp_beam_mossy`; the existing three LODs and every embedded image/mesh BIN byte are retained. `tools/release/stage_cairnwold_piling.py <private-art-root> <receipt.json>` stages that derivative, inherits source metadata/credits, and aliases the unchanged signed image cache. Its three generated GLBs match the earlier native proof byte for byte. The content also moves five birches and three boulders off the pond/jetty, removes five bank-wall panels crossing the water, and returns the workbench/cookfire to the dry bank. No station IDs or behaviour change. Existing native bounds show deck minimumY4.45, piling minimumY1.95 (pond bed) / maximumY5.75, waterY4.35. WorldContent tests 29/29 and content lint 0 errors passed for this correction. Evidence/receipt: `E:\Otherreach_Scratch\build2_test02_codex_20261029\marketing_capture\cairnwold_fix_review`, native motion in `film_batch02_retake/test02_b02_cairnwold_v2`. The correction changes world baselines; Claude's B-016 work must register its transition before any owner installation. B-005 is closed in the private delivery: committed parsed content and every delivered piling LOD were checked equal to that native packaged proof (`B005_packaged_recheck_provenance.json`). Nothing is installed over the owner’s build.

B-013 is being audited on private copies of both installed art roots. Use the fixed release gate from `1504032d` and offline cache tool `c0be7551`. Preserve original map resolution and all approved dependency art/credits. No live shared-root manifest or owner-installed root may be modified during this proof.

## B-004

Check native Gate Tender body/animation separately from the canonical Foldscar hanging masonry. The D19 review identifies the distant hanging column as intentional Foldscar dressing. Do not remove canonical geometry to conceal the report; native scene attribution and packaged evidence are still owed.

## B-013 private delivery verification

The assembled private package is `D:\Otherreach_Scratch\visual_bug_package_20261030`; exact manifests/receipts are pinned in `OWNER_VISUAL_FIX_ART_DELIVERY.json`. Its full fixed release gate passes **13,376 image references / 6,308 large images / 0 failures**. An isolated packaged headless boot reaches the start screen after building the full scene: **552 assets / 0 withheld**, no uncached >=2K error. This tests the package's actual primary/fallback roots, normal content and private fresh profile; it does not write owner saves or preferences.

All original 219 missing 2048px flow entries are now native-resolution BC7 with mipmaps. The full offline tool ended early after writing the female portion, without an error trace; the remaining 114 maps were built directly through the same native editor Image/BPTC pipeline and individually loaded to verify compression, dimensions and mipmaps. The complete release audit is the final authority, not that interrupted sweep.

Restoring the nine old dependencies exposed 114 4x4 embedded carrier images whose signed DDS entries actually held their approved full-resolution textures. Rebuilding those tiny stubs would destroy the art. Instead their full-size embedded PNGs were recovered losslessly from the approved native DDS decoded pixels, preserving the original geometry BIN prefix and all native DDS payloads exactly. This is **not** a claim to recover the original pre-BC7 authoring source. No generated artwork, resizing, canon/credit change or new memory-saving work is involved. The source, output and DDS hashes for every recovered image are in `B013_legacy_dependency_native_recovery.json`. Never rebuild a later owner package from the older stripped dependency folders; stage the pinned private delivery.

The fixed release gate was imported from `1504032d` as `e4aac2be`; the offline tool patch from `c0be7551` is `76112f68` (only its two-file patch, preserving unrelated private Main changes). Quit/save fixes remain Claude's. Ordinary shutdown and the visible Vulkan comparison are recorded separately; successful scene/art boot is not a claim that the separate quit bug is fixed.

The complete wardrobe CPU audit also verifies **489 distant-band surfaces, no residual offsets, no unbanded garment meshes**. All 63 parts / 652 surfaces preserve nodes, skin bindings/rests, indices, normals/UVs, weights and morph deltas.
