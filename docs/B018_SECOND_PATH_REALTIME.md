# B-018: second tree selection path

Source correction: `00376f7d`, `fix/owner-visual-bugs`.

Removed `flora_cos_dead_ash_tree` from `structure_prefixes.tree_.models`.
The pool now contains nine living-tree slots and keeps wind 1.0. The existing
model-specific dead-ash guard (`873b5d37`) and dedicated `cw_dead_` wind 0
remain. No other art bindings or rendering quality changed.

The focused content regression passes. Release presentation builds with zero
errors. The deterministic selection audit covers all 139 placed `tree_`
entities: none resolves to the dead ash.

## Real-time evidence for Claude

Folder: `D:\Otherreach_Scratch\visual_queue_20261031\b018\capture_retry2`.

- `living_pool_after_REALTIME.mp4`: 12.81 wall-clock seconds, 49 viewport frames.
- `bare_trunks_after_REALTIME.mp4`: 12.69 wall-clock seconds, 81 viewport frames.
- Each clip has an `*_ALL_FRAMES.jpg` sheet and its original frame directory.
- `phases.jsonl` records the actual native tree materials. `tree_19` and
  `tree_18` retain wind shaders; `cw_dead_29` resolves to the dead ash and its
  native surfaces have no wind shader. `cw_dead_28` is likewise rigid.
- `wall_clock_frames.json` beside each sequence records elapsed seconds,
  simulation ticks and engine frames. The encoded clips retain the measured
  frame durations. No fixed-FPS or Movie Maker time substitution.

Personally reviewed both complete frame sheets and the full-resolution bare
tree frame: rigid bare branches, moving living foliage, no rack-like bending.
These are diagnostic frames, not marketing selections. The living-pool camera
also sees a pale landmark at its left edge; it is not evidence about that art.

The 3090 lease lasted 430.6 seconds, with Ultra settings and normal population.
Full-sized native art, isolated scratch profile, no owner saves/preferences.
The proof host reports 19 unrelated unavailable dressing models; the audited
trees loaded. This is a scoped B-018 proof, not a zero-withheld release gate.
The harness completed both clips, requested quit, then stopped its own process
after the exit grace period (recorded exit 1); no clean-quit claim is made.

Private filming code is deliberately excluded from the product commit.
Claude's visual verdict and integration remain separate.
