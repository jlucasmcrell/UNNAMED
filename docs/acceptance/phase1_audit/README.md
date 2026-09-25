# Phase-1 audit remediation - evidence

Made from the build of `9164b03` on `claude/phase1-audit-fixes` (`docs/PHASE1_AUDIT_REMEDIATION_STATUS.md`), on the development machine, not RAZER.

- `acceptance/` - the M6 acceptance playthrough and its relaunch, greybox: `godot --path src/Presentation -- --playthrough <dir>`, then `-- --playthrough-verify <dir>`. `transcript.md` (every beat, with its picture), `transcript_relaunch.md` (Continue, the complete load, 956 fields with 0 differences, the digest, the death's debt once), `state_diff.txt`, `state_saved.json`, `state_replay.json` (a second run's is identical, byte for byte, as is its command log) and `commands.tsv`. The pictures are 1280x720 JPEG copies of the harness's PNGs, and the transcripts name them so.
- `layout/` - `--layout-check <dir>`, run with Godot's `--resolution 1366x768` and `--resolution 1280x720` over a copy of `content/` whose starting kit is a full pack (24 stacks carried; `config/inventory.yaml` is the only difference): the pack, its last row scrolled into view, a conversation and the character sheet.
- `resume/` - `--profile <a copy of the acceptance run's profile> --resume-shots <dir>`: the start screen, Continue, and the saves list in the game.
- `perf/summary.json` - `--perf --perf-route extended --perf-out <dir> --asset-root <the asset workspace>`: each segment's frame times, the goals, and the frames of the autosave taken and written.
