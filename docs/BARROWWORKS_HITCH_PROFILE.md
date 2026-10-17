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
