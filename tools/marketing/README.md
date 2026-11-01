# Film and clip capture

The opt-in film driver was ported from `claude/marketing-capture` at `8803c818`.
It samples the **render rate** specified by `--fixed-fps`, applies the camera after
the interpolated world draw, and saves exactly one image and camera-pose row per
rendered frame. The 20 Hz simulation remains unchanged. Legacy plans' `fps` fields
only describe their preroll units; they do not reduce the camera's sampling rate.

Use this repository's runner and editor together. The older copies in
`MARKETING_P2/tools` hard-code the old worktree and hold 30 fps sources twice in a
60 fps film. These tools require fresh native 60 fps sources instead.

```powershell
# Respect the owner's package/gate GPU reservation first. Each launch acquires gpu_lock.ps1 for <=540 seconds.
tools/marketing/run_film.ps1 -Plan tools/marketing/smoothness_plan.json `
  -Output D:/Otherreach_Scratch/my-film -Stand 455,745 `
  -AssetRoot <accepted-primary> -AssetFallback <accepted-fallback> `
  -CharacterRoot <accepted-humgen-root> -Game <private-packaged-review-exe>
python tools/marketing/frame_diff.py --plan tools/marketing/smoothness_plan.json `
  --frames D:/Otherreach_Scratch/my-film --output D:/Otherreach_Scratch/my-film/frame_diff.json
python tools/marketing/edit.py <edl.json> <output-folder> --ffmpeg <ffmpeg.exe>
```

Camera proof checks the actual rendered camera against the authored continuous
path, sequential frame indices, sequential engine render frames, and decoded-image
differences. It rejects held images and skipped camera steps, including on render
frames without a simulation tick. Use moving fixed-world overhead/orbit shots for
this check. Existing frames can only resume with the same plan, build, content and
cadence; incompatible old output is retained and refused.

Straight-down shots choose a stable up axis once per shot; the roll does not
switch axes as the view approaches vertical. A path crossing that chosen axis
must explicitly set `camera.up`, for example `[1, 0, 0]`. An explicit
`OTHERREACH_MKT_CHARACTER` requires all seven comma-separated fields:
`Name,people,body,He/She/They,face,hair,stature_mm`; malformed values fail loudly.

The editor reads `shot_done.json` and refuses old 30 fps sources. Existing EDLs
count 30 fps edit frames by default, so their timings are preserved when sources
are reshot at 60 fps. Set `edit_fps: 60` at EDL or clip level to use native frame
indices. The edit does not duplicate source frames to manufacture 60 fps.

In normal play, **F11** starts/stops a local clip; **F12** still saves a screenshot.
Both actions are remappable in Settings and listed in the Field Guide. Clips go to
the same screenshots folder as F12. They contain the full-size game viewport and
the Master bus, including its volume/mute, as MJPEG/PCM AVI at 30 fps. No external
encoder, microphone, desktop capture, network connection or upload is used by the
game. JPEG compression and writes run on a bounded worker queue. A slow render is
held for its actual wall-clock duration so audio stays in time. Changing window
size or reaching AVI's 2 GB limit finishes the clip; recording/finishing and errors
are visible. Closing the game finishes an active clip too.

CPU checks: filtered `FilmTimingTests`, `AviClipWriterTests`, `BindingTests`,
`ClientPrefsTests`, `FieldGuideTests`; and
`python -m unittest discover -s tools/marketing -p test_frame_diff.py`.
CPU fixtures are development checks, not packaged-game motion acceptance.
