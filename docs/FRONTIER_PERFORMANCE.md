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
12-second observation after warm-up: 10 seconds initially, 3 seconds after subsequent turns. Warm-up
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
