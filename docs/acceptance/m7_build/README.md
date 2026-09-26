# M7 acceptance evidence: the Crossing Workshop build shots

`godot --path src/Presentation -- --build-shots <dir>` runs from the committed start save S0 (`tests/Application.Tests/GameSaves/m7_crossing_start/`), then its relaunch, `-- --build-shots-verify <dir>`. Run on the final tested commit `1278b9c` (2026-09-26, ASTRAL).

- 19 beats, every one passed, with `SubscriberFailures` 0. All 7 `piece:*` entries fall back to greybox: reported, never allowlisted.
- The verify relaunch:
  - v1: 1,535 fields, 0 differences (`state_diff.txt`);
  - v2: 1,523 fields, 0 differences (`state_continued_diff.txt`);
  - v3: Kera home on tick 3,929, as in the run (`kera_home_tick.txt`).
- A second run's `state_replay.json` is byte-identical (SHA-256 `304095a5dd36f50037c80a2d34dfa7bb985d842664441314a008728eefc7e239`), as at E9.
- Criterion 14, in b13:
  - L 80,366 mm; Kera arrives 1,011 ticks after `WorkerAssigned` (bound 1,296);
  - worst step 81 mm; deepest contact 0.483 mm (the step's millimetre rounding, accepted under 1 mm);
  - her host cells `c_00_01 → c_00_00 → c_01_00 → c_01_01`.
- Files: `transcript.md` and `transcript_verify.md`; `commands.tsv` and `commands_verify.tsv`; the state files and their diffs; the coverage reports; a JPEG still per beat that has one.
- The seam recording (b11-b13 with F2's navigation stage on) stays outside the repository, in `G:\UNNAMED_HISTORY\M7_EVIDENCE\e10_final_seam\`.
