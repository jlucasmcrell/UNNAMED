## FAUNA LANE — THE ELK AND THE BOULDER FRAMES FOR CLAUDE TO JUDGE (2026-10-30 19:06)

Owner ruling at 17:05: **no visual verdicts from DeepSeek lanes.** Everything below is a path and a measurement; the judgement is Claude's.

### B-025 — the Upland Elk's locomotion (fixed in data, needs eyes)

Measured, not judged: across every creature clip set, the elk is the **only** species whose locomotion travel sits on Y — flee **3.71 m**, run **3.26 m**,
walk **0.90 m** — while every other species travels only in its death clip and on X/Z (doe 0.95 m, fellhorn 1.05 m, wolf 0.54 m). `work/clip_pose_probe.py`
reports the elk at **0.00 m** after `work/fix_elk_rootmotion.py` held the pelvis of walk/run/flee at its first key in both the shared art root and the
farm primary. Also measured: the elk skeleton's own rest pelvis is `[0.0005, 0.1975, 0.0542]`, so the clips' idle height is the rest pose and not a
mistake.

Frames and video for the verdict:

- `F:\otherreach_captures\fauna_areas\elk_motion_proof.mp4` — 82 in-game frames, 20.5 s, the elk ground from the walking stand (animal is distant in it)
- `F:\otherreach_captures\fauna_areas\elk_motion_close.mp4` — 109 in-game frames, close stand at 1006,646 looking west-south-west, 6 fps
- `F:\otherreach_captures\fauna_areas\elk_close_t3.png`, `elk_close_t8.png` — stills pulled from the close clip
- raw frames: `C:\Otherreach_Farm\results\claude_proof_elk_close01\watch\watch_0001..0109.png`
- a second close pass is queued as `claude_proof_elk_close02` (1006,646, yaw 110, 12 s, watch every 8 frames)

### B-023 — the floating sky boulders (root cause still open, probe now covers scatter)

Frames that show the defect: `C:\Otherreach_Farm\results\claude_cap_charwood_deer\near.png` and `mid.png` (a boulder hanging above the treeline at the
upper right), `claude_cap_oldlate_hares\near.png` (three boulders in the sky, upper right), `claude_motion_deer04\near.png`.

What the probe has **ruled out** (`claude_fix_boulders01`, `OTHERREACH_SCENERY_PROBE=1`): placed scenery is clean. Only two items with a real position came
back off the drawn ground — `ravine_floor` at (750,700) y -42, which is 53 m **under** the ground, not in the sky. Dressing on that run had **17 props
placed and 14 skipped**, because the cheap build's asset root carries only the five species.

What it has **not** covered until now: scatter draws as multi-mesh instances with no Node3D at its own position, so nothing was ever checked there.
`d292bcc4` adds `ReportInstances`, and `claude_fix_boulders02` (queued 19:03) runs the same ground with it on. Its stdout, in
`C:\Otherreach_Farm\results\claude_fix_boulders02\stdout.log`, will name the instance group, the position and the offset if the rocks are scatter.

### Also landed this stretch

- `9e21eb7c` `DrawnHeight` — one surface for the mesh and everything standing on it (imported grid + `Carved`). It did **not** cure the sky boulders; a
  frame taken after the export still shows them, and the note on B-023 says so.
- `1e50a0a6` the Old Late barrowback moved to 730,648 (B-024), clearing every wildlife band by 60 m+. Pack lint 6/6.
- `005ceaef` the seven `claude_cap_*` captures judged frame by frame: elk band good, Old Late deer good (four animals on a crop), Charwood rows only,
  moor crow stand unusable, sedge a bare grey plane on this build, field deer landed at the gate, millpond banner good.
- Full Content suite: **395/395**, 54 m 29 s.
