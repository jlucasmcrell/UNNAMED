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

## Review 6: drain view belongs to the frontier

At (1050,770), High / Forward+ / 1920x1050 on BEAST's RTX 3090, two fresh launches used
the same ten-second drain-camera fixture with `--gpu-profile`. Neither entered the ring.
These are spatial diagnostics, not the pending idle revision A/B study.

| Flag | Last 100 frame mean ms | GPU mean ms | Render CPU mean ms |
|---|---:|---:|---:|
| barrowworks=0 | 43.209 | 42.707 | 10.878 |
| barrowworks=1 (review-5 art/cache) | 39.995 | 39.496 | 11.087 |

The final native GPU-profile window reports these dominant passes (off / on, milliseconds):
omni-light shadows 15.925 / 14.901; opaque 10.612 / 9.689; GI + depth pre-pass 7.034 / 6.042;
GI + directional/spot shadows 6.739 / 6.668. Parallel pass labels are preserved; do not interpret
these as independent additive CPU costs. Post-processing is much smaller (SSAO about .109,
SSIL .237-.239, TAA .168-.219 ms). Both logs retain all warmup and initial compilation data.
The table deliberately identifies a steady tail rather than advertising the boot-contaminated
aggregate as FPS. No timing thresholds/exclusions changed.

The expensive exterior view is reproduced **with Barrowworks disabled**, without either entrance
or any interior. It is a base frontier rendering issue. This branch does not modify frontier lights,
shadows, scatter or rendering tiers to fix it. These sequential runs do not establish an option-on
speedup or an idle revision regression result. The useful conclusion is that a portal or entrance
is not required for the approximately 40 ms cost. Investigate the frontier's shadow/opaque workload
separately. Evidence: `review6_drain_0` / `review6_drain_1`, their raw frames and `--gpu-profile` logs.
Reproduction uses `BARROWWORKS_REVIEW_RETURN_BASELINE=1`, the regular review arguments, and flag 0/1.

## Review 6: cache, art and sound-path follow-up (2026-10-17)

The ring remains disabled. These are contended observations on BEAST's RTX 3090, High with the
explicit HDRI override, 1920 x 1050, normal fixed 20 Hz. They do not establish an idle A/B speedup.
Asset boot precedes the capture; first portal entry and return remain in the samples. Screenshot
readback is the existing sole skipped sample. No timing gate or exclusion was relaxed.

| Capture | Frames / seconds | Average / 1% low FPS | GPU mean / p95 ms | Renderer peak MiB | Frames >33.333 ms |
|---|---:|---:|---:|---:|---:|
| Review 5 `review5_shared_high` | 46,105 / 290.47 | 158.7 / 22.8 | 5.303 / 8.141 | 10,565.3 | 119 |
| Review 6 before sound fix `review6_final_high` | 39,189 / 291.96 | 134.2 / 19.1 | 5.189 / 7.544 | 9,824.2 | 143 |
| Review 6 sound fix `review6_audio_uv_high` | 36,869 / 285.65 | 129.1 / 23.0 | 5.198 / 7.526 | 9,827.3 | 123 |

The last run also has 123 frames strictly over 33 ms. Sampled WDDM peaks: 9,889.5 MiB dedicated,
460.1 MiB shared; CPU samples span 40-88%. Before the sound fix CPU samples spanned 45-100%.
The GPU-p95 and memory subtargets are met on this machine/package; the hitch/1%-low gates are not.
The full route above precedes the final entrance-fringe burial adjustment; its two entrance GLBs
and manifests are retained in `review6_before_fringe`. All thirteen interior meshes are unchanged
by that final adjustment. Final entrance images are checked separately.

### Entry

The sound-fix run records **5,704.532 ms** first entry: scene build 2,640.3 ms, existing audio-bed
preparation 90.3 ms and visual prewarm 2,855 ms (the remaining transition work is still included).
The contended preceding run was 8,917.121 ms (build 4,942.4; prewarm 3,768 ms); review 5 was
9,688.099 ms. These timings are not a controlled comparison. V5 native scenes remove the inline
RGBA copies of the owned interior maps. Full-source SHA-256 proofs now use a stream instead of
allocating whole GLBs on the large-object heap. The entry no longer records the previous eight
managed gen2 collections. There is still a substantial synchronous load; it is not hidden.

### Court and return

The old unassigned court spike is narrowed to `SoundEvents.Update`: **121.368 ms** in a
128.543 ms frame. Interior coordinates had been selecting legacy exterior cell ambience. The
space check now suppresses those exterior beds/details inside, preferring the existing declared
room tone when available. The recovered pack lacks `amb.interior.room_tone.01`, so the existing
entry bed is retained; loading inside resolves the real exterior portal anchor. No audio source or
mapping was edited. Existing room/return beds are decoded in entry preparation without consuming
variation choices or falsely marking streams as played.

In the sound-fix run no court sound spike recurs. `SoundEvents.Update` has no post-entry probe above
19.066 ms. The bog bed is logged as decoded at entry (89.7 ms), rather than first decoded at return.
Return sound processing falls from 85.690 to 19.066 ms in these observations.

Return still records **185.146 ms wall / 182.317 ms viewport GPU**, with another 74.804 ms boundary
sample. Exit projection is 114.383 ms; these are overlapping observations, not additive costs. The
remaining GPU return cause is not assigned to a specific pass by this capture. Pipeline counters
still add four draw/six specialization variants followed by two/three. It remains a failing gate.

There are six pre-exit hitches including entry, and 117 at/after the drain-return marker. Of the
other five pre-exit samples: 44.727 ms immediately after entry; 37.400 ms unassigned; 57.734 ms near
a gen0 collection; 36.003 ms near a 24.004 ms simulation probe; and 41.339 ms including the harness's
31.630 ms return-path search. None was dropped from the count. The steady exterior cost is the
separately reproduced option-off frontier issue above.

### Diagnostic limits and outstanding evidence

`review6_full_profile` is an earlier diagnostic, not the acceptance run: forced draws exhausted the
engine's default 256 timestamp slots, and its raw viewport counters contain invalid unsigned-wrap
values. Those means must not be used as GPU/CPU evidence. The normal run above omits `--gpu-profile`.
The later fresh-drain `review6_drain_timestamps` tests the raw timestamp helper without forced draws:
six capture records, three with completed timestamp data (237 markers); early empty records are kept.
Observed sample/render-frame IDs are preserved separately. They are not guaranteed to be identical.

The official three interleaved idle option-off revision pairs remain **pending the owner's quiet-window
go**. None of these contended captures substitutes for them. No frontier light, shadow, scatter or
render-tier fix was made on this feature branch; that work is the next queue item.
