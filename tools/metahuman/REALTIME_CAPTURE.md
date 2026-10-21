# Movement correctness evidence

Record the actual foreground game at gameplay pacing with loaded owner preferences. Do not use screenshot strips or `--write-movie` as the primary motion gate. Never modify an owner's release or saves for diagnostics.

Use PowerShell 7 and an installed official FFmpeg build. `record_game.ps1` takes `-Ffmpeg`, `-OutputDirectory`, `-WindowTitle`, `-Label`, and `-Seconds`. With Vulkan, GDI title-window capture can return a flat grey surface: use `-DesktopCapture` only with the game foreground and filling the recorded display. Inspect an extracted frame from a short pilot before recording a route. Preserve the FFmpeg log, actual timestamps, dropped frames, and capture dimensions; requesting 60 fps does not establish 60 retained frames per second.

The opt-in source route uses `OTHERREACH_RCD6_VISIBILITY=1`, `OTHERREACH_VISIBILITY_REALTIME_ROUTE=1` and the existing normal-game review bootstrap. It waits for `<review directory>/recording.started`; pass that path as the recorder's `-ReadySignal` only after validating the pilot. The driver uses ordinary movement commands, does not override Engine.MaxFps, and records route/camera telemetry. Its evidence is explicitly SOURCE DIAGNOSTIC, not an unchanged packaged baseline.

`OTHERREACH_VISIBILITY_ARM` selects exactly one diagnostic alteration: `baseline`, `scatter_self_fade`, `scatter_bounds`, `scatter_shadows_off`, `directional_shadows_off`, `sdfgi_off`, or `occlusion_off`. These are diagnostic arms, not proposed production fixes. The bounds arm preserves the shared centre of existing LOD bands. Do not disable all range bands at once: doing so draws every LOD over the others and invalidates the comparison.

Run `python detect_cell_pops.py video.mp4 output-directory --ffmpeg <ffmpeg.exe>` with OpenCV and NumPy installed. The detector warps previous frames using backward optical flow, compares a 32x32 luminance/edge grid, excludes UI and player regions, writes candidate tiles/events, and exports surrounding clips. `--stride 1` analyses every retained frame; default 2. Timestamps come from the video, including variable-frame-rate recordings. Candidate events require review: wind, disocclusion and lighting can produce false positives. An empty result for grey/empty video proves nothing. Report confirmed cell pops separately; before/after must use the same route and camera sequence.

For desktop capture, pass `-StopSignal <review directory>/recording.finished` and `-StoppedSignal <review directory>/recording.stopped`. The game holds its final frame until the recorder closes, so another application is not included when the game exits. Preserve failed routes as diagnostics; they do not pass the gate. The driver waits for boot Disable3D to clear and two warm seconds before signalling ready.

A second-stage coherent-cell list requires at least four connected tiles of the same brightness polarity (absolute signed luma delta >=12, changed fraction >=.4, bounding box >=2x2 and occupancy >=.6). Raw flags remain available. This prioritises review; it does not prove a cell pop or justify suppressing observed defects.

The scatter_single_lod diagnostic selects ONLY the nearest existing band of each common-AABB/instance-count family and extends it to that family's original reach; every other band is hidden. This isolates geometry swaps without drawing all levels simultaneously. It can cost substantially more GPU work and is not a shipping proposal. Names are not reliable family keys because Godot renames duplicate form nodes. The ready receipt records visible scatter nodes, environment GI state and directional-light shadow state.


## Opt-in candidate for batch-wide foliage switches

`UNNAMED_SCATTER_PLANT_LOD=1` is a review candidate, OFF by default. Shadowless model foliage selects its authored mesh level per plant using the same world-origin hash on adjoining levels. Whole-cell visibility bands become conservative guards expanded by the actual placement radius and transition width; the existing per-plant far density fade remains unchanged. Redundant generated index LODs are omitted on those plants so another whole-batch mesh swap cannot undo per-plant selection. Shadow-casting kinds, characters, native binds and character materials remain on their existing path.

Godot documents that visibility ranges use the batch AABB centre: https://docs.godotengine.org/en/stable/tutorials/3d/visibility_ranges.html . Applying a 6 m boundary to an 8 m-wide batch can switch a plant ~2.2 m from the camera. `PlantLodBandsTests` checks the old-cutoff counterexample and 120,000 independent points against the coarse guard contract. This proves conservative range coverage; it is not a claim of zero visually confirmed cell pops. The full real-time video and Claude review remain mandatory.

Use `UNNAMED_RUNTIME_MESH_LODS=0` with the plant candidate OFF as a separate generated-index-LOD isolation test. The earlier `scatter_single_lod` arm freezes one authored mesh level but retains generated index LODs, so it does not freeze all geometry detail.

Source desktop captures should pass `-StopIfFlagExists C:\Otherreach_Recovery\OWNER_PLAYING.flag` so a new owner session ends capture promptly. Do not use this for an automated normal-launcher test whose launcher owns that same flag; coordinate that test explicitly.

## Window capture without reserving the desktop

`record_game.ps1 -GraphicsCapture -WindowTitle '^Otherreach - unique-test-name$'` uses FFmpeg's documented `gfxcapture` input, hardware-frame download and software H264 encoding. Run the recorder with PowerShell 7 so the filter argument remains intact. This mode requires a build of FFmpeg that includes `gfxcapture`; it was verified with FFmpeg 9.0.2 on Windows. Keep the existing default recorder path for other capture setups.

This captures the named game window even while another application occupies the desktop. It does not send OS input or require exclusive foreground control. Give every test a unique window title and verify a short recorded pilot before releasing the route's ready handshake. Reject recordings of grey frames or the wrong application. Preserve variable-frame-rate presentation timestamps when extracting frames or aligning soundtrack audio; frame index divided by average frame rate is not an accurate timestamp.

The software encoder avoids the unsuccessful WGC/NVENC interop attempt; do not treat that earlier crash as a game rendering result. Capture overhead means these videos are visual evidence, not performance benchmarks.

Post `CODEX SCREEN IN USE` before actual desktop UI interaction and `CODEX SCREEN FREE` immediately after releasing it. A background window recording alone does not reserve the desktop. If the owner reports a persistent control overlay, stop UI use, reset the Computer Use JavaScript session, and report whether release is verified; do not equate lack of input with the overlay being hidden. GPU ownership and the owner play flag remain separate requirements. Official filter documentation: https://ffmpeg.org/ffmpeg-filters.html#gfxcapture .
