# Build 2 accepted-lane integration

Branch/worktree: `first-update-integ02`, `D:\Otherreach_Scratch\first_update_integ02`.
Accepted integration02 base: `c764aac18ce50d7a4dd5c5371d29c8aa8f608900`.
Owner authority: CODEX_PROGRESS.md, OWNER BUILD-2 SCOPE2026-10-27~17:00.

**HISTORICAL PRE-TEST-02 GATE (superseded 2026-10-29).** The owner explicitly directed Test 02 packaging and scratch installation after accepting all included lanes. No public push; only the scratch package/install were changed. Owner saves/preferences and existing packages were preserved.

## Accepted inputs integrated after the base

| Lane | Accepted source | Merge | Conflicts | Validation |
|---|---|---|---|---|
| Creature batch A: stag/antlers, hind coat, fox coat, juvenile pup and mammoth tusks | `6412dfe7397f1a7af50fb3ede46d0ee297f66a2b`, Claude16:50 | `285939c344946bdc280cd53beb7092288fc2cbfd` | None | Presentation Release build0errors/21warnings; four existing BarrowworksGateTests PASS |
| Creature batch B: approved raider/drowned, cave spider, tarantula matriarch, knight armour, mammoth tusk trim, ember hound/boar/stag/hind/fox/hare/fowl/eel-serpent/fellhorn | `ac761d76e1f30535c26da2d9419972810f09b680`, Claude verdict 18:00 | `10d5d324` | None; merge completed. Post-merge cleanup hook reported permission errors while attempting to delete unrelated registered worktree metadata; no other worktrees were changed. | Presentation Release build0errors/28warnings; `BarrowworksGateTests` first caught changed overlay SHA, expected hash repinned while retaining semantic assertions; rerun 4/4 PASS |

The full-file frontier binding guard is repinned to the explicitly accepted batch A overlay. Its exact hash assertion remains, alongside the unchanged inn roof groups and ring separation assertions. No assertion weakened. No full suite or native proof rerun for this batch.

Batch A private art: `D:\Otherreach_Scratch\creature_upgrade_d5_20261027\batch_a_payload`. Future package must install the native stag/mammoth model deltas and merge DDS/cache manifest entries BY KEY, keeping signature/DDS pairs. Do not replace the whole world manifest or copy a stale whole-world payload. Bindings, vector/scalar bone-look parsing and credits are in the merge. Its accepted canon selection commit is `a542b0c` in otherreach-canon; see `docs/CREATURE_UPGRADES_20261027.md`. The accepted batch retains a nonblocking left-tusk polish request for the creature lane; no unreviewed extra modification here.

Batch B private art: `D:\Otherreach_Scratch\creature_upgrade_d5_20261027\batch_b_delivery` (primary delta plus `humgen_characters/mocap_motion`, proof, `INSTALL.txt`, `MOTION_TARGETS.json`, `DELIVERY_MANIFEST.json`). Future package installation must apply over accepted Batch A/current art, merge texture-manifest entries BY KEY while retaining signature/DDS pairs and `identity_weights` sidecars, and must not copy an old whole-world payload. Animation depth is not included in this merge and remains pending its separate acceptance gate.

Base private NPC dependencies remain documented in `docs/FIRST_UPDATE_INTEGRATION02.md`: `npc_life_20261027\asset_delivery02` motion on its matching original bodies, and `mouth08\delivery` native mouth maps/DDS with cache entries merged by key. F1 replacements need their own exact-target motion; do not attach original-body libraries to different rest/geometry targets.

## Remaining scope / acceptance gates

- F1 Gate A fidelity: acceptance/payload status is recorded in the source progress journal; do not infer a Gate C outfit pass from Gate B creator acceptance.
- F1 full creator Gate B was accepted and is included in Test 02 (source `fb9a477a584e328b9f2e09812c1e39487643d46a`, merge `f103433eaad57af5bef38b2bd94b72ea9334be8d`).
- Every NPC Gate C period outfits remains open and is deferred to Test 03 per owner instruction.
- Creature batches A/B and the accepted Sedge corrected D18 lane are included; additional animation-depth work remains out of scope for Test 02 unless separately accepted.
- D6 complete ladder traversal is accepted and included (source `9d3e1229bf1987981c419bb71d94d9352f9f32a3`, merge `2a7a7f786bf4b754f0927c82230af1155445d8b8`).
- Sedge corrected D18 content/art is accepted and included (source `dd0e9a95aabced811353b442f086e7ce3f8ac00d`, merge `77863a2bbea53523d09e73553b931ea2ef5241e0`).
- Catalog cross-check and outstanding owner notes: pending coordinator completion/acceptance; integration does not author their content.
- Packaging: BLOCKED until explicit human scope-complete confirmation plus packaging request.

A quiet thread heartbeat checks CODEX_PROGRESS.md every15minutes for new explicit acceptance. It merges only pinned accepted changes, records conflicts/private deliveries/checks here and in the journal, and notifies only a meaningful merge/failure/decision. No repeated full suite or GPU capture campaign. Ordinary cache/compression/mipmap/LOD/impostor/streaming practices stay at full source quality with negligible visible change; no new savings work.

Current batch receipts: `D:\Otherreach_Scratch\build2_batchA_bindings.log`, `D:\Otherreach_Scratch\build2_batchA_build.log`. The accepted base's one full-suite receipts remain in `outputs/First_Update_Integration02_20261027` in the Codex workspace; they describe that base, not a freshly repeated run of subsequent batches.

## FINISHED — BUILD-2 OWNER TEST 02 (Codex, 2026-10-29)

Owner direction reauthorized the accepted Build 2 lanes, Test 02 packaging and installation into the scratch target, then requested a real-art boot reporting the library's withheld count. The subsequent owner message confirmed all Claude-accepted lanes were merged and named the moving `build2-integ` tip as the Test 02 source. Current source branch `build2-integ`, exact commit `0b947e38fdcd277ed9853b6639fd14c55cba545f`; parent `e27ad0529f51bd2f4673669b5ecf54383bc8eac6`. One final integration correction changes `cw_creek_ford_gravel` to the full-width authored loose-gravel surface binding; this was required because the old single 45 cm stone failed the 4.8 m footprint check. `WorldContentTests` passed 29/29 after the correction. No unrelated lane tip was imported.

Accepted lane source/merge pins present in the integrated history:
- Creature A `6412dfe7397f1a7af50fb3ede46d0ee297f66a2b` → `285939c344946bdc280cd53beb7092288fc2cbfd`; Creature B `ac761d76e1f30535c26da2d9419972810f09b680` → `10d5d324b18fa6cd5a2a8ef94e0def6f8c09dc7a`; accepted Sedge/stone follow-up `7a47ff2fd4506fe1bc55f8b8b8ede60f2e5386d9ac` → `9e25bb8fd834de32ea5a6938c44b1344491c7b3a` (followed by D15 merge `8ddec4909f767547ea7f33cb3d87de39d89387f4` → `e2fd64d95a82808e74e71404499b939c1c86c36f`).
- Systems D20 main `6397dac22dfd896bf29fb3080b9a6f97a48cb69e` → `c3a09665bca0326f07088d1a39012a2c6af5087f`; systems follow-up `38b36151b843a1eeace9796db3bfd38ccceafbb2` → `2a7a7f786bf4b754f0927c82230af1155445d8b8`; D6 complete traversal `9d3e1229bf1987981c419bb71d94d9352f9f32a3` → `fbc3ac89c18e9737dc3e7e8fb5f9666b99068296`.
- F1 Gate B + D12 `fb9a477a584e328b9f2e09812c1e39487643d46a` → `f103433eaad57af5bef38b2bd94b72ea9334be8d`; corrected Sedge D18 `dd0e9a95aabced811353b442f086e7ce3f8ac00d` → `77863a2bbea53523d09e73553b931ea2ef5241e0`; accepted water v3 `6372f60e88f568e43a9d7abe6c65687aa9a20046` → `b5ae0fb3d033dab9859123be099f2e1ffe3f99a2` (with the recorded intermediate reconciliation merge `31df2560b018332dec49d043eaae58cd45153531`).
- Human-accepted Cairnwold content follow-on: `c57c301da250caff28630c4b44c123847a9751be` plus pond shelf correction `e27ad0529f51bd2f4673669b5ecf54383bc8eac6`; final surface-fit correction `0b947e38fdcd277ed9853b6639fd14c55cba545f`. Cairnwold millpond is water v3 at 4.35 m with 2.40 m depth; the jetty shelf/posts were authored to project over it. These are owner visual checks, not visual acceptance claims.
- History also includes the initial accepted-content reconciliation merges `77863a2bbea53523d09e73553b931ea2ef5241e0` and `a5bc22f6989b8941de5d0cc9b44d53669e443896`; no unresolved merge conflicts remain in the current worktree. A broad Cairnwold cherry-pick conflict in bindings/starting-frontier was aborted earlier; only the accepted Cairnwold keys were applied. The gravel-fit adjustment is the only additional source correction in the final current tip.

### Build, package, installation, safety and boot receipts
- Release exporter `tools/export_playtest_clean.ps1` completed with build exit 0 and export exit 0 from `0b947e38…`; full-quality cache retained. Export log: `E:\Otherreach_Scratch\build2_test02_codex_20261029\export_final2.log`. Package-stage exe SHA-256 `7AD55D771DC7BD7B1BFA752B4B32596ED4C755D6EDDEA3A3B18D1DC251DD8AFD`; terrain DLL `40900E649C3C6C7619C383D28732C3E2E8DC87C2938DE43B29122A668AEDCF86D`. Cache audit: 12,516 images / 5,543 large images / 0 failures; `PACKAGE_TEXTURE_CACHE_PASS 12516`, receipt `texture_cache_export_audit_f1_final.json`. Package is `D:\Otherreach_Scratch\First update - owner test 02`; scratch install is `D:\Otherreach_Scratch\install_test_pp1`. Package and install mirrors each copied 286 files (305.5 MB), removed the six stale fallback opening-voice files, and reported zero failed/mismatched copies (Robocopy exit 3 because it copied/deleted as expected). Package/install hashes match for exe, terrain DLL, VERSION, bindings, scatter rules and both texture-cache manifests. No public push, installer invocation or owner-install change.
- Private accepted art is present in the staged package/install: creature A/B payload and exact-target motion, F1 Gate B creator parts and cache, D10/D20 authored clip payload, accepted Sedge art, full-resolution source art and BC7 cache. Preserve the delivery instructions above: apply deltas to the correct base, merge cache signature/DDS manifest entries by key, retain `identity_weights` sidecars; never replace with stale whole-world manifests. Excluded deferred Gate C period outfits, The Steps and grapple/scramble (owner designated these Test 03).
- Before/after save snapshot: all 32 existing save files match their original paths, lengths and SHA-256 values; 482,759 bytes total. The 15 `.parts` entries remain. Smoke used isolated scratch `APPDATA` and `LOCALAPPDATA`, plus installed `primary`, `fallback`, bindings and scatter roots; it did not target owner save/preferences.
- Installed real-art headless boot log: `E:\Otherreach_Scratch\build2_test02_codex_20261029\runtime_sandbox_final\real_art_boot_final.log`. It reports water v3 with `water.cw_millpond` at 4.35 m/depth 2.40 m; all 2,463 dressing props placed, 392 animals, 57/57 people; `UNNAMED art: 552 assets drawn from the library at boot; 0 withheld or unavailable`; texture cache 302/302, 0 failed; smoke PASS after 1,915 frames, with save/load digest identical. Separate ArtCoverage reports 4,127 requests, 4,072 resolved and 55 unexpected fallback bindings; this is not the library's withheld count and is explicitly retained as a remaining audit issue. The process exits `-1073740940` (native teardown access violation) after printing smoke PASS; do not describe process shutdown as clean. No fix was attempted in this packaging pass.
- Minimal validation: Release build/export 0 errors, texture-cache audit pass, `WorldContentTests` 29/29 after the gravel fit, installed real-art smoke PASS. No repeated full suite or GPU-capture campaign. Current tree clean at `0b947e38…` before this ledger update.

### Owner-facing WHAT'S NEW IN TEST 02 + WHAT TO CHECK
What's new: accepted content hooks and regional dressing; D20 fishing, lockpicking, gathering/crafting swings, swimming/washing and the accepted resource/prospecting systems; accepted D6 ladder traversal; F1 creator Gate B/D12 face, body and creator work; accepted creature batches and corrected Sedge D18; accepted water renderer v3; Cairnwold millpond/jetty; and the final gravel shoreline fit. Current Test 02 is built from `build2-integ` at `0b947e38…`.

Check in game:
1. Cairnwold millpond's irregular graded shoreline, water depth/tint, and millrace connection; confirm there is no hard rectangular sheet.
2. The timber jetty visibly reaches out over the pond on posts, meets the shoreline, and is walkable across the water.
3. Blackvein Cut and Barrowworks placements/interior readability; specifically the forge/sump and the archivist's case.
4. D6 traversal: climb the ladder at the Frontier outpost lodge north wall and Ashen Hollow lodge facing the waystone; confirm climb placement, animation, stamina and roof/rail landing.
5. Accepted Sedge stations/shoreline and creature art; F1 creator Gate B faces/bodies, looks and NPC identity.

Known checks to retain: smoke's separate ArtCoverage has 55 unexpected fallbacks despite zero library-withheld assets; the native teardown exit code above also remains. Test 03 is expected to take Gate C period outfits, The Steps and grapple/scramble; not included here.