# Barrowworks first-sight hitch profile (review 4)

Profile: High Forward+, 1920x1050, uncapped/VSync off, ordinary 20 Hz simulation. Evidence: `D:\Otherreach_Scratch\barrowworks\codex\review4_gallery` and its adjacent log. This normal-speed capture precedes the final two task lamps and rust-reflectance adjustment; those changes only receive a later art recapture.

Counters observed each frame: five Godot pipeline compilation monitors, .NET GC generations 0–2, existing FrameStats CPU/GPU/frame timings, and timed ArtLibrary file-load logs. Review path-search duration is recorded as a FrameStats event. Counters are observations, not a CPU-stack trace.

## The four review-3 non-startup spikes

From the original `gallery_v5/frames.csv` (frame 0 is startup and separate):

| Frame | Frame ms | Render CPU ms | Render GPU ms |

|---:|---:|---:|---:|

| 678 | 137.5 | 1.658 | 11.241 |

| 11362 | 114.062 | 2.185 | 10.75 |

| 11655 | 81.667 | 2.392 | 10.02 |

| 11805 | 140.928 | 2.24 | 9.985 |

The previous capture had no pipeline/GC counters. Its four causes cannot be assigned retrospectively from a frame delta alone. Low measured GPU time during these spikes ruled out describing them as ordinary sustained GPU raster cost; it did not distinguish CPU scheduling, compilation, audio work or a collection.

## Instrumented rerun

| Frame | Frame ms | Evidence and attribution |

|---:|---:|---|

| 9441 | 107.040 | CPU 3.098 ms, GPU 15.197 ms; no coincident compilation or GC event. **Unattributed by this instrumentation**; do not label it a GC or file-load stall. A render-thread/scheduler/audio CPU stack trace is the next diagnostic. |

| 9660 | 67.594 | Exactly coincident Surface +4, Draw +1, Specialization +1. First-use pipeline work is observed here. |

| 9750 | 138.845 | Exactly coincident Surface +4, Draw +1, Specialization +2; CPU 2.097 ms, GPU 14.019 ms. Strong first-use pipeline-compilation correlation. |

| 19954–19955 | 120.738 / 145.929 | Actual return to exterior: Draw +36, Specialization +82 and GC gen0 +3/gen1 +1 across the transition. A separate transition/first-use burst, not a recurring court draw cost. |

All model and clip loading finished at boot/first entry (last Custodian clip at 193.3 s in `review4_gallery.log`). No timed model/clip loads occurred at the two court spikes. The first-entry frame remains separately visible in the raw data (120.373 ms and accumulated startup pipeline/GC counters); it is not one of the four earlier in-route spikes.

Review path searches measured 0.4–30.7 ms in the rendered route; the largest is the new long return walk. They do not coincide with the 107/139 ms court spikes. The extra 50 ms sample at frame 15743 does include the 30.673 ms return-path search, which is harness overhead.

## Performance result and limits

Normal capture: 19,975 samples / 278.11 s, average 71.8 FPS, frame p99 20.370 ms, GPU mean/p99 13.304/20.414 ms, CPU mean/p99 2.637/4.763 ms. 1% low 37.1 FPS; **the sustained-60 gate does not pass**. Renderer memory peak 17,578.2 MB. No asserted timing threshold or screenshot exclusion was changed.

First-use material/shadow variants at the dogleg/court and at the environment transition are a concrete prewarm target. The remaining 107 ms sample needs stack-level profiling before claiming a cause. Avoid a global renderer workaround or attributing it to economy/simulation catch-up without evidence. The normal-speed result is retained honestly; the later accelerated art recapture is not substituted as a performance claim.

## Final resource-preload verification

After this profile, held-interior item models, the Custodian rig and all eight clips were added to the existing startup cache warm-up. This opt-in presentation-only change moves file decoding out of first portal entry; it does not claim to precompile every material/shadow variant. In `review4_prewarm_check.log`, all eight clips finish before the review fixture begins, and no Barrowworks model/clip file is loaded between that point and R01. The real-portal headless check exits successfully. The final Release build and Presentation/Architecture suites pass. No new normal-speed GPU measurement is substituted for the raw profile above.

## Review 5: corrected capture and streaming work (2026-10-17)

The review-4 results above are historical. Review 5 keeps the same >33 ms hitch threshold and now
records monotonic diagnostic frame time. The threshold screenshot exclusion is consumed before
portal entry. This exposes loading that the old harness accidentally masked; boot and screenshot
readback alone are excluded, while portal entry and return are included. None of these diagnostic
clocks drive simulation state.

Intermediate `review5_high_final` (before native scenes / retained exterior figures / live impact
preparation): 44,247 samples, 297.29 measured seconds, 148.8 average FPS, 18.1 FPS 1% low,
frame mean 6.719 ms, GPU p95 7.756 ms, renderer peak 10,597.2 MB, **35 hitches >33 ms**. Memory and GPU
budgets passed; the hitch requirement did not. Its filename is not an acceptance claim. The largest
frame is the now-visible 15,048.383 ms portal load, including approximately 12.3 s of GLB decoding and
2,756 ms of prewarm. Exterior return measured 350.521 ms (GPU 349.66 ms, Draw +35 across two frames,
Specialization +76, plus a generation-1 collection). A 255.053 ms court frame has no blocking Draw or
Surface counter change; its cause is not established. A separate 52.059 ms court frame coincides
with Surface +4 / Draw +1. Shared-machine CPU contention was present; this is not idle evidence.

Source revision `03359bd27895f336f6e4a55cf36093b901b687e7` adds native bundled-scene derivatives
(full GLB digest + Godot-version validation), retains/culls exterior creature figures, prepares the
actual hit-recipe instance and retains its warm resources for the interior's lifetime. Temporary
camera-shape wrappers are disposed after native collision nodes own them; the short headless check
no longer reports leaked shape RIDs. All 62 cached scenes reload (383 mesh nodes, eleven Custodian
joints); the first 62 headless native reads total 2,719 ms, which is an I/O diagnostic rather than a
rendered frame-time result. The corrected frontier-overlay headless route completes all rooms,
solo combat, exit and resource-released re-entry with earned doors visibly open.

Pipeline interpretation follows Godot's [pipeline compilation documentation](https://github.com/godotengine/godot-docs/blob/master/tutorials/performance/pipeline_compilations.rst):
Surface/Draw counters can identify first-use work, but Specialization is asynchronous and does not
by itself explain a hitch. Counter coincidence is not a stack trace. The current preparation uses
the actual viewport and preserves its AA configuration; it creates no second GI viewport.

The final cached normal-speed High capture and six idle controls are recorded separately. Idle A/B
is explicitly pending at the owner's request while priority body work and wave-5 tests occupy BEAST;
see the current handoff for the queued fixtures, sample table and CPU-overlap checks.

### Review 5 intermediate rendered measurements

These are diagnostic runs, not acceptance results. All use High with normal fixed-20-Hz simulation;
portal entry and return are included. All raw logs remain under the evidence root.

| Run | FPS / 1% low | Frame mean ms | GPU p95 ms | Renderer peak MB | Frames >33 ms |
|---|---:|---:|---:|---:|---:|
| `review5_native_high` | 191.6 / 40.0 | 5.219 | 6.477 | 10,553.7 | 29 |
| `review5_gi_high` | 159.5 / 31.2 | 6.268 | 8.461 | 10,565.3 | 37 |

The stable-GI/transform-flush experiment reduced the exterior-return Draw increment from 35 to 6,
but did not eliminate approximately 24 slow exterior frames (40–51 ms GPU) or the entry stall.
The second run's 89.026 ms court sample has no recorded cause; a later 48.652 ms frame coincides
with Surface +4 / Draw +1. These counters establish correlation, not a proven stack-level cause.
The latest candidate extends the exterior observation to five seconds; it does not exclude those frames.

Renderer-reported bytes are not a complete physical VRAM measurement. WDDM sampled process dedicated
memory reached approximately 12,944 MiB and 13,044 MiB respectively, plus roughly 446/456 MiB shared.
These are sampled maxima, not exact peaks. A renderer total below 11 GB alone does not establish
12 GB target-hardware readiness. The expanded shared native cache is intended to avoid raw RGBA upload
churn and duplicate texture resources; its final rendered result must be assessed separately.

The idle option-off study remains **PENDING**. Pair 1's db4df5e4 run is explicitly rejected because
it overlapped a Codex headless cache builder; low total-CPU observations alone do not qualify it as idle.
The owner requested that the definitive three interleaved pairs wait for a quiet window after the
priority body work and wave-5 push. Partial or rejected runs are not substituted for that study.

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
