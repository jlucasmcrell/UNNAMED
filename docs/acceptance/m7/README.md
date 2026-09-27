# M7 acceptance evidence: the playthrough

The content bible's acceptance route with M7's beats (the six faction beats and `m7_build`): `godot --path src/Presentation -- --playthrough <dir>`, then its relaunch, `-- --playthrough-verify <dir>`. Run on the final tested commit `1278b9c` (2026-09-26, ASTRAL: Ryzen 9 9950X3D, RTX 5090, 1920x1080).

- Every beat passed, with `SubscriberFailures` 0. The relaunch compared 1,216 fields with 0 differences (`state_diff.txt`).
- A second run's `state_replay.json` is byte-identical (SHA-256 `bc57ee2a3912865ef8d396b0b8c8df17f89630080caf384f5a0bcb438b108a7b`), as at E8 and E9. The transcript is E9's row for row but the save's run-specific digests.
- Files:
  - `transcript.md` and `transcript_relaunch.md`;
  - the command logs, `commands.tsv` and `commands_relaunch.tsv`;
  - `state_saved.json`, `state_loaded.json`, `state_replay.json`, `state_digest.txt` and `state_diff.txt`;
  - the art and audio coverage reports (every `piece:*` entry falls back to greybox: reported, never allowlisted);
  - a JPEG still per beat (22-26 are M7's).
- The videos stay outside the repository.
