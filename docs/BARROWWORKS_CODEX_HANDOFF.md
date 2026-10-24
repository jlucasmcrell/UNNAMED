# Barrowworks: current implementation, art and verification

The Barrowworks is in every run; `--visual barrowworks=0` is the debug switch that takes it out. Run from
`region.starting_frontier` through the real portal. Save schema stays 39: the rulings below use existing world flags,
the door's open flag and the creature record.

## Review 7 (Claude, 2026-10-23): rulings, band, art, transitions, default on

- **OQ-16:** G3 (`door.barrowworks.headworks`) needs the durable Custodian defeat and a fitting wedge. A door's `fitted`
  item leaves the pack when it opens and the door stays open, so its open flag is the record; the saddles take no second
  wedge. The three gate keys now read as names (gallery, archive, headworks); their flags are unchanged.
- **OQ-17:** a wounded or engaged Custodian resets to full health when the character leaves its court or its space, or
  dies; only the defeat flag is permanent. `BarrowworksRulingsTests` covers both rulings.
- **Band:** the top of frontier band 1. M8 09's E1-E4 pockets (level-3 spiders and a husk, no respawn) are in, and the
  Custodian is calibrated so four to six blows fell the level-5 reference (`docs/BARROWWORKS_BALANCE.md`).
- **Ultra first:** room LOD0, fixtures, papers, the entrance soil atlas (4096) and the Custodian keep their source
  texture resolution; the native scene cache (v7, ring models only) shares them as BC7. The ring no longer switches the
  frontier's own asset path: the packed terrain BC7 cache it used to enable is removed.
- **Transitions:** a space change raises a black veil for a frame, does the rebuild behind it, and holds it while the
  renderer re-voxelizes SDFGI after the camera's kilometre jump (that was the 182-241 ms return frame; GPU timestamps
  name the six "Render SDFGI" cascades). The ring is built on first entry and then kept, hidden: freeing it crashed
  the renderer in its light unpair. Showing a hint no longer re-applies every setting (a 230 ms stall per hint).
- **Harness:** the review route takes the archive haft, crafts the wedge at the Ashen Hollow forge anvil, fights E1-E4,
  breaks off the first Custodian attempt to show the reset, fits the wedge, works a saddle and leaves by the drain.
  `--start-items` gives the reviewed character the band's reference kit; `BARROWWORKS_REVIEW_SHORT=1` runs only the
  threshold-to-drain transition. `tools/package_barrowworks_assets.ps1` copies the ring into a package's fallback.

## Review 6 rendering and art (historical: entry loading and departure release were replaced in review 7)

Interior geometry/textures are loaded on portal entry and its cached model resources are released after
leaving, after views/creatures/items have switched back. No simulation state or save schema is changed.
The enabled run uses the existing signed BC7 cache in the primary asset package; distribute its
`texture_cache/` manifest and DDS files with the package. Missing/stale cache entries retain the normal
fallback and are reported; budget evidence must identify the actual package/cache hits. Option off keeps
its original upload path. The viewport keeps SDFGI enabled across the portal to retain its feature
variants; closed rooms use zero SDFGI energy and no sky read, with local lights/contact shading.
Only the court rim has a shadowed room light; the other pools and corridor lights are unshadowed.

Portal-entry prewarm issues real material/skin/shadow/GI draws in the actual High viewport from the
court, every room and the two exterior-return views, including the impact effects. Camera/avatar/particle transforms are flushed explicitly because forced
draws do not advance the scene tree. Alarm font fill/outline atlases are cached directly; a deferred
hidden label primes the real 3D glyph material after Label3D shapes its text, retaining those resources
until the space is freed. All combat clips are posed during priming and the initial idle is restored.
Draws do not swap to the window; camera/body presentation transforms restore in a finally block.
It reuses the existing outdoor GI allocation before the first indoor draw and allocates no auxiliary
viewport. No commands or authoritative state change. Entry and return stalls remain visible in raw
timing data: the threshold screenshot exclusion is consumed before entry, fixing an earlier harness
bug that masked the loading frame. No exclusion or timing threshold was broadened.

Native scene derivatives avoid temporary RGBA uploads before the cache swaps in BC7 textures.
Build with `--build-scene-cache` added to the headless real-portal review command below, then omit that
flag for play. The owned depot holds `barrowworks_native/<Godot version>/v5/scenes/` and `v5/textures/`;
the primary package remains read-only. All currently used model/clip scenes opt in while the ring is
enabled. Shared `.dds.res` textures retain their BC7 data and are external references reused across
models/LODs. Root scenes load independently; their immutable texture dependencies share the loader's
cache. Missing/stale native scenes use the original GLB. Scene stamps contain full source SHA-256,
Godot version and cache-format revision (currently v5, unrelated to save schema). Transient clip scenes
are disposed after copying animation/rest data, rather than waiting for finalization with their meshes.

The v5 builder also compresses the owned `bw_codex_` maps into the physical
`barrowworks_materials/texture_cache/` directory. The depot's root `texture_cache` is a junction to the
primary package and must never be used as a build destination. Earlier native scenes held inline,
uncompressed copies of the interior maps: the headworks scene alone was 22.35 MB. Its v5 scene is
1.94 MB, with shared compressed dependencies. The additional texture-cache object exists only in
`--build-scene-cache`; ordinary play retains no extra dictionary of interior textures. A matching
source/version stamp is reused during incremental builds too, so an entrance iteration rebuilds its
changed scenes instead of all 602. Distribute the generated v5 texture dependencies with the scenes.

External resource references are absolute: **rebuild this cache after relocating the asset depot**.
Relative external references outside `res://` failed the engine reload check, so they are not shipped as
working portability support. Keep the signed source DDS cache and source GLBs available for validation
and fallback. No native resources/source binaries are committed. Rebuild after art/filter/engine changes,
then run `python tools/manifest_barrowworks_assets.py`; the scene-cache manifest records 602 scenes and
1,594 shared textures for this package. Reload checks confirm compressed texture data and shared texture
instance identity. Run the integrity check (from PowerShell at the repository root):
`godot --headless --path src/Presentation --script (Resolve-Path tools/check_barrowworks_native_cache.gd)`.
It checks every cached file digest, independently loaded model texture identity/compression and the
Custodian's eleven joints, plus every texture slot on the sampled R12/drain models, and exits nonzero on a failed assertion. Different-resolution LOD images
remain distinct. The normal High run must verify actual memory/timing with this full cache.

Exterior creature figures are retained, hidden and stopped while indoors, then their interpolation is
placed at the current authoritative bodies on return. Loading/new game resets both projections. Interior
figures belong to the interior scene and are freed with it. This preserves exterior rendering resources
across the transition without retaining interior models after exit.

Cell ambience also respects the interior space: local room coordinates must not select the frontier's
legacy outdoor beds or details. The declared room tone is preferred when present. The recovered pack
currently lacks `amb.interior.room_tone.01`, so the entry bed is retained; loading inside recovers the
actual exterior portal anchor for that fallback. Entry preparation decodes the available declared bed
and the two exterior portal-cell beds without playing, consuming variation choices or
marking sounds as played. This preparation is included in the entry stall and logged separately;
`UNNAMED_LOAD_REPORT=1` also records cold stream IDs and load times. Audio files and mappings are
unchanged. These preparations never run with the ring disabled.

All eight former masonry-grid rooms now use the approved rock relief. Wash/drain troughs contain water;
sorting bins, chutes and tables, the Still Bay's real scanned boiler/condenser, and dense ledger shelves
make the functions visible. The headworks is an open timber/iron trestle with meshing gears, keyed bearings,
graduated guide rails, opposed jaws, a gauge column and governor. Its working face points into the room. The scanned
pipe surfaces retain wear with modern paint desaturated to aged iron; the red stone control plinths are gone. Reading papers carry worn raster writing from the existing copy; boards use
Poly Haven Rough Wood (CC0). Roof materials are rough dielectrics, closing the metallic white streaks;
solid backing closes R09. The lantern glass has a real GLB tint/alpha and no emission.

Blackvein spoil and Sinking Moss mouths are cut into asymmetric, eroded ridges that follow the frontier
height grid at their current anchors. A single opaque atlas blends the frontier CC0 grass, mud and gravel maps
at their world-space tile scales, with merged real CC0 grass tufts, rubble and drain water/reeds. Their model origin is the real portal mouth, not the asymmetric berm centre.
Toe quads and buried skirts close the eroded edges. Exposed vertical cuts use repeated mud UVs rather
than stretching the top atlas down a cliff face; the grass roots follow the same surface offset.
These art landforms do not decide OQ-11's provisional overland placements. The Custodian has a cast
register/brass graduations, measuring jaws, rusted iron and a front key plus rim. Its review portrait
waits for the whole death clip, from outside the body. Readings wrap within the screen and replace the
previous reading; ordinary exterior notices keep their existing behavior.

Room LOD maps are 512/256/128; fixture maps are capped at 512, entrance terrain atlases at 2048, the authored body at 1024. Clip GLBs now
carry only their skeleton and keyed tracks (11-20 KB each), retaining all eleven joints instead of
embedding another 20 MB armored mesh/atlas in each of eight animation files.

Additional CC0 reuse: ScottishMaritimeMuseum, *Boiler from the puffer VIC 32*,
https://sketchfab.com/3d-models/884ed11e38054aa48a3db1d9025c881d . Source archive/licence and derived
provenance remain in the external depot; this is the Still Bay vessel/headworks housing, not the
rejected boiler-as-Custodian. Poly Haven Rough Wood: https://polyhaven.com/a/rough_wood .
No generative model or service was used.

## Review 5 verification status

All 1,361 tests passed with zero skipped (`review5_all_tests.log`); after the final presentation
changes, Release and the focused Presentation 127 / Architecture 18 suites passed again. Content lint
loads 339 definitions with zero errors; xref exits 0. No timing assertion or test was weakened.
The scene-cache check reloads all 62 scenes / 383 mesh nodes and verifies the Custodian's eleven joints.
All 76 GLB outputs and 80 cache derivatives match their manifest hashes. The final room/detail sheets
were inspected, including all thirteen rooms, both mouths, the readings, three attack poses and corpse
camera. The later cached normal capture must retain these same visuals.

**Idle option-off A/B is pending, explicitly deferred by the owner on 2026-10-17** while the priority body
Unreal chain and wave-5 integration tests run. No quiet machine is available. Three pairs remain queued
outside the GPU lock via `D:\Otherreach_Scratch\barrowworks\codex\review5_off_all.ps1` (CPU-idle gate,
short individual GPU leases, sixty-second cooldowns). Pair order is A/B, B/A, A/B, where A=23d75688 and
B=db4df5e4. Both archived fixtures include the same native terrain dependencies, primary/fallback assets
and matching frontier overlay; neither uses the cheaper terrain fallback. High, 1920x1050, barrowworks=0,
three twenty-second camera segments plus the existing five-second warmup. The report includes FPS,
1% low and frame mean for every run, and retains all-frame/warmup-inclusive rows separately:
`review5_idle_off_report.md`, with raw `review5_idle_off_{pair}_{revision}/frames.csv`, pair logs and
`review5_idle_cpu_samples.csv`. Confirm no other heavy work overlaps the timed samples before accepting
these as idle controls. Contended or aborted attempts are not substituted as idle evidence.

## Review 4 regressions and cleanup

Exterior ambient is explicitly black for every exterior, matching the base default. Indoor tint remains local.
The sixty-creature option-off regression was real: repeated enumeration rebuilt walls, closed doors and NPC
filters for each creature/query. `CreatureSystem` now snapshots those per space for its tick.

Four isolated A/B runs (unchanged 4 ms assertion): before 3.368, 4.132, 4.268, 4.760 ms/tick;
after 2.170, 2.178, 1.984, 2.522 ms/tick. Revised mean 2.214 ms, median 2.174 ms.
`review4_budget_before_*.log` / `review4_budget_after_*.log` retain the individual results.

`OPEN_QUESTIONS.md` and the two one-off `port_barrowworks_*` scripts were removed. The implementation plan
is a short source/tool map. Kera's stale notice-reading comment is fixed. A portal's optional
`reachable_flag` names its destination place and flag in content; runtime seeding contains no dungeon IDs.
A regression test changes that flag and proves the authored flag is seeded in the destination's own cell.

Matched-native option-off High/1920x1050 VRAM A/B: **11,587.8 MB peak on both 23d75688 and this revision**.
The same Terrain3D dependencies, primary/fallback libraries, content overlay and twenty-second capture
fixture were used. `review4_vram_before_native` and `review4_vram_after` are the valid records. The first
archive-only baseline was rejected because omitted native dependencies selected a cheaper terrain fallback.
Both valid boots loaded 425 model files; model textures needing mipmaps were 220.2 MB in both.

## Gameplay retained

Every co-loaded site is addressed in its own space: switches, doors, barriers, containers, discovery,
quest/dialogue place flags, nodes, stations and items. Exterior NPC terrain/space is retained on an interior
load; followers cross portals and waiters retain their away-space state. The inactive environment releases
its WorldEnvironment and the sun is hidden indoors.

Actual Kinematics collision controls the connected rooms, blind dogleg and three gates. G1 is a one-sided
shortcut; G2 covers the archive store; G3 requires the durable court defeat. Earned gates stay open.
The headworks measure is a physical obstacle until dismantled. The archive supplies, learned fitting
recipe, crafted wedge deed and exclusive local choice work before independent optional Kera/Sel reports.
No remote reputation award is implied. Rewards and balance remain provisional.

Unvisited interior clocks freeze under plan 2.11 while the exterior advances. Active-attempt load retains
saved resources and returns the player outside the court; retreat resets the encounter and preserves
participating companion state. The broader historical S35 phase/death lifecycle and separate Lantern
story/codex/compass integration are not newly claimed complete here.

## Art and licences

Runtime additions: `D:\Otherreach_Depot\barrowworks_codex`. Raw licensed binaries stay outside Git.
`BARROWWORKS_ASSET_MANIFEST.json` records derived file hashes and provenance; refresh it with
`python tools/manifest_barrowworks_assets.py` after exports.

- **Meshingun Studio, Ancient Mayan Ruins**, owned Fab Standard Personal:
  https://www.fab.com/listings/5d9d932b-9e6e-42d0-8afc-45249e7535a5 . Actual sculpted masonry and cave-cliff
  relief plus plain slabs, selected without cultural statues or inscriptions. Licence evidence:
  `C:\Otherreach_Recovery\ASSET_REBUILD\FAB_LICENCES.md`.
- **DanStudios, Lost Temple**, owned Fab Standard Personal:
  https://www.fab.com/listings/48148dbe-67c8-40a1-af9b-041fa4b9354e . Retained floor and structural source
  modules; the repeated ornate ceiling and corner braziers are removed from the room assemblies.
- **piacenti, Robot (2015)**, **CC BY 3.0**:
  https://opengameart.org/content/robot-5 ; licence https://creativecommons.org/licenses/by/3.0/ .
  Download: https://opengameart.org/sites/default/files/robot%20game.zip . Modified by removing firearms,
  missile racks and spikes, refitting, material conversion, manual eleven-joint weighting and eight
  hand-keyed clips. The authored armor and connected legs replace the boiler kitbash. Keep this credit
  in distributions. Original archive and licence are in `sources/piacenti_robot` under the depot.
- **SLASH-RENDAR, Old Industrial Pipe Pack PBR**, **CC BY 4.0**:
  https://sketchfab.com/3d-models/a10a6011334e4d11a7023d67705ac1d4 ;
  https://creativecommons.org/licenses/by/4.0/ . Resized and assembled mechanical parts in the rails,
  headworks carriage, clamps, rear index, gates and controls. Keep this credit in distributions.
- **Poly Haven**: Rock Ground by Rob Tuytel (https://polyhaven.com/a/rock_ground), wooden crate and
  wooden lantern, CC0. Existing verified production woodpile and timber beam retain their first-party/CC0
  provenance in the manifest. Paper, ink strokes and prop frames are first-party conventional geometry.

No generative service was used and no source mesh was fed to generative AI. The rejected boiler assembly
and inaccessible Sketchfab candidate are not the final character. Review 4 does not bind the boiler asset.

`tools/barrowworks_art_direction.json` gives each room its wall, roof and light recipe. Stone ribs,
timber grids, exposed rock, pipe runs and machine rails replace the repeated temple lid. Each light has
a physical lantern at its position; scanned slab seams are closed by solid rock above their relief.
LOD texture dimensions are now bounded at 512/256/128 (review 5). Physical readings are pinned curled papers,
a filing shelf, drawing easel and open bound folio. Their set-state glow is disabled through local
bindings; exterior switch marks retain their behavior. The headworks has twin supported rails, a
sliding carriage, opposed jaws, graduations and an animated drive wheel. Both exterior entrances use
real relief modules rather than the fallback box frame.

Existing audio IDs requested: `animated_armour`, `plate`, `walk`, `move_heavy`. Bespoke measure sounds
remain an audio-team task; no audio assets or mappings changed.

## Reproduction

Run the CPU conversion/assembly tools with installed Blender 5.2. Use `--disable-autoexec` when opening
the third-party source blend; the supplied script explicitly uses `use_scripts=False`.

1. `tools/convert_barrowworks_kit.ps1`, `tools/convert_barrowworks_metal.ps1` prepare licensed local sources.
2. `python tools/gen_barrowworks_blockout.py` and `python tools/gen_barrowworks_bindings.py` regenerate content.
3. `python tools/build_barrowworks_paper.py` requires Pillow and the local Windows Segoe Print font
   (only raster output is shipped). Blender `-b --threads 4 --python-exit-code 1 --python SCRIPT`: run `build_barrowworks_rooms.py`,
   `build_barrowworks_props.py` and `build_barrowworks_custodian.py`. `--rooms 3,8,12` supports a targeted
   room iteration; a complete build omits it. Run `python tools/finalize_barrowworks_surfaces.py`
   after room exports, then refresh the manifest. `link_barrowworks_fallback.ps1`
   makes the existing shared fallback available alongside additions without editing it.
4. Build `src/UNNAMED.sln -c Release`; build Presentation in Debug for the installed Godot editor runtime.
5. Under the shared GPU lock, run Godot with `--path src/Presentation`, then these game arguments after `--`:

```
--content-root C:\Otherreach_Build\barrowworks_wt\content
--asset-root C:\Otherreach_Recovery\Otherreach_Playtest_RESTORE_20261015b\primary
--asset-fallback D:\Otherreach_Depot\barrowworks_codex
--bindings-overlay C:\Otherreach_Build\barrowworks_wt\content\_art_bindings.json
--visual tier=high,barrowworks=1,sky=hdri
--barrowworks-review D:\Otherreach_Scratch\barrowworks\codex\review4_gallery
--realtime
```

The review driver begins at an exterior fixture position, photographs the threshold, enters the real
portal, walks collision-clear routes, reads, opens gates, fights with ordinary commands, reaches the
headworks, walks back and leaves through the drain. It never writes flags or relocates the player after
the initial fixture. `--realtime` retains normal fixed 20 Hz through GameSession; omission accelerates
headless traversal. `BARROWWORKS_REVIEW_LIMIT` supports a short preview.

`UNNAMED_LOAD_REPORT=1` logs timed file loads. FrameStats events correlate path-search costs, shader
pipeline compilations and managed collections with frames; screenshots use the existing exclusion of
only the next sample. Initial loading is separate. Timing claims must state whether the 1% low passes.
Review-only phase probes separate scene construction, forced warm-up draws, simulation, avatar,
creatures, effects, audio, NPCs, camera and HUD costs. Their 5 ms reporting threshold does not change
the existing frame/hitch gates or exclusions. For raw RenderingDevice timestamps, add engine
`--gpu-profile` before `--` and game flag `--capture-gpu-stages` after it. `gpu_frames.json` preserves
both observed sample and delayed render-frame IDs; these are not guaranteed to identify the same
frame. Forced draws can exhaust the engine's default timestamp pool, making render counters invalid;
keep such a diagnostic separate from the normal acceptance run and retain its raw data.

For the fresh exterior comparison, set `BARROWWORKS_REVIEW_RETURN_BASELINE=1` and run the same review
command with `barrowworks=0`, then `1`, to stand at (1050,770) without entering the ring. Each run
retains boot/first-draw warm-up and then observes ten seconds; compare the steady tail explicitly.
This fixture alone permits the review driver with option off; ordinary option-off launches are unchanged.

## Review 4 baseline verification (historical)

Evidence root: `D:\Otherreach_Scratch\barrowworks\codex`.

- `review4_all_tests.log`: complete suite 1,360 passed, zero failed/skipped. New generic portal test:
  one additional pass in `review4_portal_test.log` (1,361 current tests exercised). No threshold was loosened.
- `review4_release_prewarm.log`: final Release build, zero errors, 49 warnings. Final startup-preload changes
  also passed Presentation 127/127 and Architecture 18/18 in `review4_*_prewarm.log`; Debug build passed too.
- `review4_lint.log`: 339 definitions, zero errors. `review4_xref.log`: exit 0. This isolated branch does not
  implement a separate `--engine-lint`; Content.Tests and GameSession boots exercise engine readers.
- `review4_headless_fixed`: all thirteen rooms and solo fight, R12 at tick 5259, drain return at tick 6329,
  normal LeaveSpace completion/exit 0. `fill_verified/kinematics_fill.png` remains the inspected actual C#
  open/closed-gate fill; art and the cache change no collision footprints.

Final `review4_art_final/` capture was personally inspected: all thirteen rooms, physical readings,
headworks, three attack poses and both exterior entrances. Ordinary solo combat and drain return completed
(R12 tick 5032, exterior tick 6097). This accelerated art capture is not a frame-rate claim.

`review4_gallery/` is the normal fixed-20-Hz performance route: 19,975 frames / 278.11 seconds,
71.8 average FPS, 37.1 FPS 1% low, frame p99 20.370 ms; GPU mean/p99 13.304/20.414 ms,
CPU mean/p99 2.637/4.763 ms, peak renderer memory 17,578.2 MB. **Sustained 60 does not pass.**
This measurement precedes the final two task lamps and rust-material adjustment. The final art capture
does not replace it. See `BARROWWORKS_HITCH_PROFILE.md`: two court stalls correlate with first-use
pipeline compilation; one 107 ms sample remains unattributed; the exterior return has a separate burst.

The historical `review4_prewarm_check` validated the earlier startup preload. Review 5 supersedes
that residency policy with entry loading and departure release; it does not claim these old measurements
as the current result.

Final asset manifest: 42 records, with local source archives, licence evidence, attribution and derived
hashes. Distribution credit is also in the external depot's `CREDITS.txt`.


## Owner decisions still needed (review 5)

- **OQ-11, portal placement:** threshold (438, 663) at Blackvein spoil and drain (1050, 770) at Sinking Moss remain about 610 m apart. The approximately 137 m interior route therefore makes a substantial overland shortcut. These anchors are provisional. Recommendation: owner chooses whether to move the drain closer; keep current anchors until that decision. The art berms dress these placements and do not settle this map question.
- **OQ-16, wedge and G3:** the original design couples crafting/fitting the wedge to the headworks door. The quest orders those deeds, but current G3's physical interlock is the durable Custodian defeat, not possession or consumption of a wedge. Recommendation: owner confirms whether the wedge must also physically gate G3, and whether it is consumed. Do not silently substitute quest order for an item requirement; shared door/item policy belongs with the integration owner.
- **OQ-17, defeat semantics:** durable `world.barrowworks.ring_custodian_defeated` is now written in the ring's own cell and survives quest activation order; the former missing-writer engine gap is fixed. Keep the fact rather than a while-active kill counter. Owner/integration review still needs to ratify encounter retreat/reset/death policy against S35 and the advancing WP4 implementation; do not reintroduce the old missing-writer claim.

## Review 5 remaining evidence

The definitive three interleaved idle option-off A/B pairs are **PENDING**, by owner instruction while
priority UE body work and wave-5 tests occupy BEAST. Fixtures compare 23d75688 with db4df5e4, High,
barrowworks=0. Retain per-run FPS, 1% low and frame mean, CPU/job overlap observations and raw frames.
Pair 1's db4df5e4 sample is rejected (`invalid.txt`): it overlapped our own headless cache builder.
An apparently quiet start/end CPU reading does not validate the whole interval. Complete/repeat the
three qualified pairs when the owner announces the quiet window; no option-off regression claim is
made from the partial study.

Latest CPU verification: Release build zero errors (39 existing presentation warnings), Presentation
127/127, Architecture 18/18, content lint 339 definitions/zero errors, xref exit 0. The full review-5
suite passed 1,361 tests, zero failed/skipped. The cache integrity check verifies all 602 scenes and
1,454 shared textures, thirteen shared albedo identities across independent loads, and eleven joints.
The final headless route completes all thirteen rooms, fight, headworks, drain exit and re-entry;
all three earned door hinges remain open after the resources are released/rebuilt.

## Final shared-cache High route (review 5)

`review5_shared_high`: BEAST / RTX 3090, Forward+, High 1920x1050, normal fixed 20 Hz,
46,105 frames / 290.47 measured seconds. Average 158.7 FPS; 1% low 22.8 FPS; frame mean
6.300 ms / p99 14.457 ms. GPU mean 5.303 ms / p95 8.141 ms. Renderer peak 10,565.3 MB.
WDDM process dedicated sampled maximum 10,682.5 MiB, shared sampled maximum 460.1 MiB;
these are separate allocation measures, not exact physical peaks or a RAZER measurement.

**Hitch gate FAILS: 119 frames >33 ms**, including 113 frames at the final drain/exterior return.
The five-second return observation added evidence rather than changing exclusions: the exterior
remains roughly 41-44 ms per frame through the last sample. Do not compare 119 directly with
previous runs' 29/37 without their shorter 1.2-second exterior tail. Entry is 9,688.099 ms,
including 4,885 ms prewarm; return includes a 197.505 ms frame (193.588 ms GPU). Return adds
six Draw variants across its two marked events; Surface +4 / Draw +1 no longer appears later
in the court. A 91.896 ms court sample and two 39.073/33.729 ms samples have no correlated
instrumented cause. The 34.915 ms headworks sample includes the harness's 27.381 ms path search.
No timing threshold, screenshot exclusion or simulation clock was relaxed.

The source is ready for review with the ring **still disabled by default**. The enablement hitch
gate is explicitly open; do not turn it on from the aggregate GPU p95 or engine memory total alone.
All thirteen room images, physical readings, three attack poses, corpse portrait and the distinct
threshold/drain berms were personally inspected in this final capture. No prewarm-only glyph leaks
into those images. Ordinary solo fight, headworks and drain exit finish successfully.

Partial option-off observations (not a completed or accepted idle study):

| Pair/order | Revision | FPS | 1% low FPS | Frame mean ms | Status |
|---|---|---:|---:|---:|---|
| 1 A | 23d75688 | 83.8 | 32.7 | 11.927 | partial; whole-window qualification pending |
| 1 B | db4df5e4 | 83.5 | 26.9 | 11.979 | REJECTED: own headless builder overlap |
| 2 B | db4df5e4 | 86.3 | 31.7 | 11.593 | partial; whole-window qualification pending |
| 2 A | 23d75688 | 88.3 | 33.0 | 11.325 | partial; whole-window qualification pending |
| 3 A | 23d75688 | — | — | — | pending owner quiet window |
| 3 B | db4df5e4 | — | — | — | pending owner quiet window |

Raw files use `review5_idle_off_<pair>_<revision>` under the evidence root. The next pair-script
launch waits for `review5_owner_quiet_window.flag` there, created only after the owner announces
the window. This marker is a diagnostic scheduling gate, not part of the game. Repeat/qualify three
complete interleaved pairs; the existing CPU observer did not identify every possible competing job.

The cache check's failure path was exercised with a scratch copy containing a deliberately bad
checksum: it reports an assertion and exits 1 (`review5_cache_negative.log`), while the published
manifest exits 0 with the PASS marker (`review5_cache_integrity_final.log`). No asset/cache file was
modified for this negative control.

A diagnostic fresh-drain control uses `BARROWWORKS_REVIEW_RETURN_BASELINE=1` with the same normal
High review command. It seeds only that review fixture at (1050,770), never enters the ring, then
records ten seconds at the return camera. Omit/unset the environment variable for the full route.
This separates same-location exterior cost from portal effects; it changes no ordinary launch.

### Fresh-drain control result

`review5_drain_control` exits 0. It launches directly at (1050,770), with Barrowworks enabled but
**without ever entering or loading the interior**. The last 100 recorded frames average 41.133 ms
(frame), 40.372 ms GPU and 12.850 ms render CPU. The full route's final 100 frames average
42.457 / 41.788 / 11.983 ms respectively. Sustained slow exterior rendering is therefore reproduced
without a portal transition. This is a same-location diagnostic, not proof of option-off equivalence
or a diagnosis of which exterior draw is expensive. The required idle revision A/B is still pending.

This fresh-launch control retains its boot/first-draw warmup, including a 14,440.208 ms sample.
Its aggregate 9.8 FPS is **not** a steady frame-rate result. The original raw metadata inherited the
full-route boot-exclusion wording; the source now labels controls correctly. Do not discard or alter
those raw samples. The last-100 comparison above is an explicitly identified tail diagnostic;
the full route's 119-hitch count remains unchanged. Investigate/profile local exterior rendering
separately from portal pipeline preparation. Entry loading and the unassigned court spikes still
prevent claiming the enablement hitch gate passes.

Final GPU leases were released normally. Release/Debug builds have zero errors; existing presentation
warnings and the previously observed font/CanvasItem/texture shutdown warnings remain visible in logs.
No save schema was renumbered, no merge performed, and the ring remains disabled by default.

Final rebuild totals: Release solution 49 warnings / zero errors; Debug Presentation with rebuilt dependencies 42 warnings / zero errors (39 Presentation warnings plus three existing Content warnings). Final Presentation 127/127 and Architecture 18/18 pass. Earlier 39-warning totals were incremental builds; logs are retained.

## Review 6 verification

- Release solution build and Debug presentation build: 0 errors; existing warnings remain.
- Full Release suite after the stream/ambience fix: **1,361 passed, 0 failed, 0 skipped**. Presentation
  **127** and Architecture **18** pass again after the missing-room-tone fallback adjustment.
- Content lint with `--engine-lint`: **339 definitions, 0 errors**; xref exits 0.
- Headless connected route: **PASS**, both crossings in one region, **8,205 ticks**, save/load digest matches.
- Native integrity: **602 scenes, 1,594 shared compressed textures, 13 shared albedo identities, 11
  Custodian joints, 116 compressed texture bindings** on the sampled R12/drain models; exits 0.
- `review6_audio_uv_high`: normal 20 Hz frontier entry, all thirteen rooms, ordinary-command solo fight,
  headworks and drain return pass. All thirteen room images were inspected. GPU p95 7.526 ms;
  renderer peak 9,827.3 MiB; sampled WDDM peak 9,889.5 MiB dedicated and 460.1 MiB shared. This is a
  contended observation, not an idle regression result or an enabling pass.
- Final perimeter-only art refinement: checked separately in `review6_fringe_threshold` and
  `review6_fringe_drain`, both exit 0, both final entrance images inspected. Their source meshes are
  126,586 and 108,203 triangles. The full-route timing above predates this final fringe/grass adjustment;
  the thirteen interior models and audio code are unchanged. The previous entrance GLBs/manifests
  are retained in `review6_before_fringe` for that timing observation.

The machine is an open iron/timber regulator; both entrances are eroded terrain-following cuts using
frontier soil/grass maps. No generative mesh service was used. Six generated asset GLBs changed; the
shared primary package remains untouched. Authoritative simulation, persistence schema and historical fixtures are unchanged.

The enabling gate still fails: the first entry is 5.70 s, the return is 185 ms (182 ms viewport GPU),
and 123 recorded frames exceed 33 ms. Six are before the drain return including entry, 117 at/after it.
The court's 121 ms sound-update stall does not recur; the remaining return GPU cause is not attributed
by that run. The option-off drain view independently reproduces the approximately 40 ms frontier cost.
See `BARROWWORKS_HITCH_PROFILE.md` for raw-run names, limits, detailed phases and all retained hitches.
The official three interleaved idle option-off revision pairs remain pending the owner's quiet-window go.
Barrowworks stays disabled. Frontier performance proceeds on its separate queue branch.
