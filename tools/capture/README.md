# Shared owned-window capture (branch pilot)

Run with Python 3 on Windows, using an installed FFmpeg that provides `gfxcapture`:

```
python tools/capture/capture.py C:/private/plan.json D:/private/fresh-output
python -m unittest discover -s tools/capture -v
```

The output directory must not exist. The runner acquires the shared GPU lock, honours
the owner flag, and launches only the supplied GUI executable (no console wrapper).
Arguments are arrays, never PowerShell command strings. A game built with this branch
must emit the versioned handshake; an older package fails rather than guessing load time.

Example plan (replace paths with installed locations):

```json
{
  "exe": "C:/Godot/Godot.exe",
  "project": "D:/branch/src/Presentation",
  "payload": "D:/accepted/game",
  "ffmpeg": "C:/ffmpeg/bin/ffmpeg.exe",
  "ffprobe": "C:/ffmpeg/bin/ffprobe.exe",
  "pwsh": "C:/PowerShell/7/pwsh.exe",
  "gpu_lock": "C:/Otherreach_Recovery/gpu_lock.ps1",
  "owner_flag": "C:/Otherreach_Recovery/OWNER_PLAYING.flag",
  "x": 450, "z": 737, "yaw": 45, "pitch": -0.1, "distance": 2,
  "size": [1920, 1080], "seconds": 5,
  "visual": "tier=medium,hour=12,weather=fair,terrain=terrain3d,sky=sky3d,people=humgen,foldscar=proof,body=none,textures=cache,aa=taa"
}
```

All manifests/content/cache indices are copied into private output. Only immutable
binary payloads may be hardlinked; producers must not modify sealed input during a run.
Profiles and APPDATA are private. Placement uses the ordinary new-game spawn override;
there are no walking scripts, teleport writes, focus changes or synthetic NPCs.

The game holds simulation elapsed time until its actual gameplay camera converges and
draws, emits PID/HWND/viewport/placement, then waits for the runner. The runner checks
HWND ownership by PID, records that handle, and decodes a nonblank first frame. Only
then does the game resume ordinary elapsed-time simulation. Encoder padding handles
odd window dimensions. `original_wgc.mp4` retains startup pre-roll; `clip.mp4` is the
last five seconds, retaining native frame cadence. This pilot is silent video: it
makes no audio or lip-sync acceptance claim.

Before using it for a real review, the tool must produce `COMPLETE.json`, its one
`first_frame.png` and five-second `clip.mp4`, pass full decode, and have its ORIGINAL
image/video inspected. A nonblank heuristic alone cannot prove correct art or framing.
Keep all failed outputs. No existing capture wrappers are retired until pilot acceptance.
