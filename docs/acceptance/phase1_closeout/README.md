# Phase-1 closeout - evidence

For `docs/PHASE1_TECHNICAL_CLOSEOUT.md`. Made on the development machine (Astral, RTX 5090), not RAZER. The full logs, every
picture and every run folder are kept outside the repository in `G:\UNNAMED_HISTORY\phase1_integration_20260925\`.

- `package/` - the Windows package testers get (`Otherreach_Phase1_Complete_Prototype_2026-09-25_29d4f45.zip`), extracted to a
  fresh folder away from the repository with `UNNAMED_ASSET_ROOT` unset: `Otherreach.exe -- --playthrough <dir>`, then
  `Otherreach.exe -- --playthrough-verify <dir>`. `transcript.md` (every beat, with its picture), `transcript_relaunch.md` (Continue
  picked the acceptance save; the load complete; 956 fields compared, 0 differences; the digest; the death's debt once),
  `state_diff.txt`, `state_replay.json` (byte-identical to the development runs A and B, and to run C drawn in pure greybox with
  no asset root), `art_coverage.*` (171 requested, 170 resolved, 1 fallback - the F3 overlay's discovery rings, allowed -
  0 unexpected) and `audio_coverage.md` (57 requested, 57 resolved, 0 missing). The pictures are 1280x720 JPEG copies of the
  harness's PNGs, and the transcripts name them so.
- `layout/` - `--layout-check <dir> --content-root <full pack>` at `--resolution` 1366x768, 1280x720 and 1920x1080: a full pack of
  24 stacks (`config/inventory.yaml` the only difference from `content/`), and its last row scrolled into view. Each PASS.
- `h02_malformed_records.md` - thirteen malformed asset records broken at once, and three malformed bindings, at runtime.
