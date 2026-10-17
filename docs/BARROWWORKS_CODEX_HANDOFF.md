# Barrowworks: current implementation, art and verification

The dungeon remains opt-in (`--visual barrowworks=1`). Run from `region.starting_frontier` through
the real portal. No schema number, historical fixture, audio source or audio manifest changed in review 4.
Claude owns schema renumbering and integration conflicts against the advancing base.

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
LOD texture dimensions remain bounded at 1024/512/256. Physical readings are pinned curled papers,
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
3. Blender `-b --threads 4 --python-exit-code 1 --python SCRIPT`: run `build_barrowworks_rooms.py`,
   `build_barrowworks_props.py` and `build_barrowworks_custodian.py`. `--rooms 3,8,12` supports a targeted
   room iteration; a complete build omits it. Then refresh the manifest. `link_barrowworks_fallback.ps1`
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

## Verification

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

The final presentation change preloads held-interior item models, creature rig and clips at startup,
using existing caches, only when the option is enabled. `review4_prewarm_check.log` confirms all eight
Custodian clips loaded before the review fixture began; no Barrowworks model/clip file loaded at portal
entry through R01. This moves file decoding out of first entry, without claiming to eliminate first-draw
shader compilation. That headless first-entry check completed successfully. Its 22 unrelated exterior
fallbacks are enumerated in `review4_prewarm_check/art_coverage.md`; no Barrowworks asset is missing.

Final asset manifest: 42 records, with local source archives, licence evidence, attribution and derived
hashes. Distribution credit is also in the external depot's `CREDITS.txt`.
