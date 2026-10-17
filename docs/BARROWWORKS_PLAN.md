# Barrowworks implementation map

The opt-in dungeon is `feature/barrowworks`, worktree `C:\Otherreach_Build\barrowworks_wt`.
It remains disabled unless `--visual barrowworks=1` is supplied. Do not renumber the isolated save
extension: Claude resolves the next free schema and historical-fixture conflicts at integration.

## Authoritative sources

- Canon M8 preproduction: `C:\Otherreach_Recovery\canon_readonly\proposals\existing\m8-preproduction`.
- Review and current brief: `C:\Otherreach_External_Assets\CLAUDE_REVIEW4_FOR_CODEX.md` and
  `C:\Otherreach_External_Assets\CODEX_PROJECT_BARROWWORKS.md`.
- Current implementation, provenance, reproduction and measured evidence: [Codex handoff](BARROWWORKS_CODEX_HANDOFF.md).

## Content and tooling

`tools/gen_barrowworks_blockout.py` owns the room footprints, passages, three gates, readings,
archive supplies and headworks choices in `content/regions/barrowworks.yaml`. Room IDs R01–R13
remain stable; use generated coordinates rather than the superseded local-coordinate planning table.
The frontier's two portal definitions own their exterior anchors and offer-readiness flag.

`tools/gen_barrowworks_bindings.py` owns the interior art overlay. The conversion and assembly
scripts listed in the handoff produce external licensed assets; no raw library is committed.
`tools/plot_barrowworks_kinematics.py` plots actual C# collision-fill CSVs.

## Verification

Build Release, run the complete solution tests, content lint and xref. Run the connected frontier
review through the real portal, readings, gates, solo fight and headworks; inspect all thirteen
room screenshots and both exterior entrances. Observe the shared GPU lock and short render holds.
Option-off ambient, simulation cost and VRAM are separate regression gates. The broader S35
phase/death lifecycle, story Lantern integration and balance remain separately owned work.
