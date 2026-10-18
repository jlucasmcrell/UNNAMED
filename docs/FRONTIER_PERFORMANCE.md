# Frontier performance

Base: `b809f3ae`, schema 30. Baseline preparation; no rendering optimizations or performance pass claimed yet.

## Benchmark

Build Presentation in Debug for the installed Godot editor runtime. Acquire the shared GPU lease before
launching, give the body work priority, release in `finally`, and leave a fair interval between leases.
Run one point per short lease. Example game arguments, after the engine's `--path src/Presentation --`:

```text
--content-root C:\Otherreach_Build\frontier_perf_wt\content
--asset-root C:\Otherreach_Recovery\Otherreach_Playtest_RESTORE_20261015b\primary
--asset-fallback D:\Otherreach_Depot\barrowworks_codex
--bindings-overlay C:\Otherreach_Build\frontier_perf_wt\content\_art_bindings.json
--visual tier=high,barrowworks=0
--capture-size 1920x1080
--frontier-perf D:\Otherreach_Scratch\frontier\before\drain
--frontier-point 1050,770
```

The point is a new-game fixture, not an in-play teleport. Each run uses the acceptance world's fixed
seed and a private scratch profile. The player stands still through ordinary input commands while
GameSession runs its normal 20 Hz clock; creatures, NPCs, weather, economy and autosaves keep running.
A defeated character makes the run fail rather than silently disabling combat.

Eight gameplay camera headings, 45 degrees apart, cover the gaps between cardinal views. Each has a
12-second observation after warm-up: retain the first two boot intervals (the first Draw creates NPC
figures), then 10 rendered seconds initially, 3 seconds after subsequent turns excluding readback. Warm-up
samples remain in separately named segments. `frames.csv` retains monotonic frame intervals, including
long stalls the engine delta might clamp. The existing screenshot readback exclusion is the only
dropped sample. Initial content loading precedes this benchmark and must be reported separately.

Outputs: raw frames, `summary.json`, eight screenshots, art/audio coverage, `render_counts.csv` and
`lights.json`. Render counts are the completed-frame values Godot reports; their observation index
does not prove exact GPU-frame alignment. The light inventory records positions, ranges, energy and
shadow state, not a claim that every light contributes to the current camera's frame.

Save the tested Git hash and working diff beside each run. Verify the reported GPU, High tier and
actual **1920 x 1080** resolution. Headless runs check startup only and cannot establish rendering
performance. Ordinary captures under CPU contention must be identified as such; they are separate
from the owner-gated idle option-off revision A/B.

## Coverage and acceptance

Cover both settlements (including interiors), the road, Foldscar overlook and drain, then remaining
named frontier sites. Check actual footing and photographs; adjust an obstructed fixture point before
using it for both arms. Keep positions, camera, content, world seed, tier and observation protocol the
same before and after each change. Record before/after per place and inspect the new screenshots.

High target: sustained 60 FPS, 1% low at least 45 FPS and no frames over 33 ms after warm-up. Preserve
the raw counters and report strict over-33-ms counts as well as FrameStats' existing over-33.333-ms
counter. Do not hide loading stalls or relax either target. Report any visual tradeoff explicitly.

## First finding: point-light shadows ignore the scatter's distance bands

**Environment correction:** the early isolated project lacked Terrain3D's native DLLs and
silently drew fallback ground. Every capture below through `drain_all_shadow_lods` is retained
as a **fallback-ground diagnostic only**, not full-High acceptance. Exact shipped DLLs were
restored and SHA256 matched in baseline and candidate. The controller now requires a native
Terrain3D preflight and rejects runtime terrain fallback. The full-High sweep is being rerun.

The complete drain-area profile at (1065,790), High/default Sky3D, measured 20.3–24.2 FPS facing
the settlement. The source-mesh inventory contains 4,109 out-of-range casting scatter nodes carrying
535 million source triangles. This inventory is potential geometry, **not** a per-frame submission count.
The native GPU profiler identifies point-light shadows as a major cost; the normal draw counters reach
241 million primitives in the worst direction. The first (1050,770) run was incomplete because wolves
defeated the character. Its samples are retained; the clear fixture completes all eight headings.

Godot's point-shadow cull checks instance visibility but does not check the visibility ranges used by
the main camera and directional shadows (`servers/rendering/renderer_scene_cull.cpp`, cube-shadow
loop). Consequently both casting near LODs can be submitted to distant village lamps simultaneously.
The first scatter-only pilot passed all 1,738 visibility groups and improved the worst view from
20.3 to 25.9 FPS, removing about 117 million submitted primitives; it remains far below the target.
The correction now mirrors the existing hard camera-distance bands in **CastShadow** for both static
models and scatter. It keeps placements, density, meshes, materials, ranges and node visibility;
static GI geometry stays present. The authored shadow mode returns inside its band. Non-casting
geometry is unchanged. `UNNAMED_SHADOW_LODS=0` restores the old submission for diagnosis.
No acceptance pass is claimed until the full before/after sweep and visual inspection finish.

Check the actual captured visibility states with:

```text
python tools/check_scatter_shadow_lods.py <capture>/geometry.json
```

This fails against the original scene inventory and must pass against the correction, including
camera turns across band edges. Detailed GPU profiling needs a larger timestamp-query pool than the
engine default of 256. The controller uses a temporary, removed-on-exit override of
`debug/settings/profiler/max_timestamp_query_elements=16384`. A capture that overflowed 256 is invalid
and retained separately. Normal FPS runs do not enable the detailed GPU profiler.

The original protocol let the blocking first NPC Draw consume all ten seconds of initial warm-up
in two frames. This was corrected in the harness for **both** baseline and candidate. Original v1
captures stay archived separately and do not establish the post-warm-up hitch gate.

The loader-only pilot missed duplicated fence pieces and rigged LODs (294 wrong shadow states).
Registration now watches actual scene instances, including later spawns, after construction/Ready.
The culler also preserves external first-person/FoldEcho shadow-mode changes and releases freed
instances. The full-High live-scene pilot is pending; no completed coverage or FPS pass is claimed.
