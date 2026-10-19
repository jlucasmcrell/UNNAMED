# Barrowworks enable work after RC d

Base: `4c6bbe1136b9a779448ea5ed35061b4487ad615f`.
Branch: `codex/barrowworks-enable-20261019`.
Worktree: `C:\Otherreach_Build\barrowworks_enable_20261019`.

RC d (`D:\Otherreach_Scratch\release\20261019d`) and its commit are frozen.
Push this branch only. Do not fast-forward either shared base, rebuild the RC,
or enable the ring in the owner's build.

## Owner has priority

`C:\Otherreach_Recovery\OWNER_PLAYING.flag` prevents GPU and heavy CPU jobs.
Use `tools/run_owner_safe.py` for builds, conversions, cache generation and captures.
It checks the flag before launch and after waiting for the official GPU lease,
polls during the child process, stops that child tree when the owner returns,
and releases its own lease. Exit 75 means owner play prevented/interrupted work.
GPU jobs also observe the 60-second cooldown and 20-minute/queued 10-minute limits.
Python unit checks below launch only tiny test fixtures, never Godot or Blender.

```
python -m unittest discover -s tests/tools -p "test_*.py" -v
python tools/run_owner_safe.py -- dotnet build src/UNNAMED.sln -c Release
python tools/run_owner_safe.py --gpu "codex barrowworks capture" -- <engine> <arguments>
```

## Accepted art selection

Source: `C:\Otherreach_External_Assets\Barrowworks_Art_20261018\Production_Revision`.
Authority: `CLAUDE_REVIEW_BARROWWORKS_ART3.md`, including the correction to round 12.
The source manifest's generic candidate labels are older than several review decisions;
retain them as provenance rather than rewriting the producer's files.

The explicit selection contains 44 standalone assets / 132 supplied LOD files,
about 3.55 GiB: the Custodian, R12 headworks, two entrances, nine architecture/lamp
pieces and the 31 individually accepted dressing/readings entries. Do not
automatically install the `production_dressing_rXX` room composites: their
furnished-room QA is still in progress. The standalone acceptance remains valid.

Planning reads metadata only; installing hashes/copies large files and must wait
for the owner flag to clear. Installation requires a previously pinned plan,
verifies source and destination SHA-256, preserves supplied bytes/resolution/LODs,
and checks the Custodian's eleven joints and eight original clips. Only an owned
destination bearing the same plan identity can be resumed after an interruption.
The four core pieces retain existing semantic IDs; all other pieces use `bw_codex_art3_`
IDs for room assembly. Source art, original depots and the frozen RC stay untouched.
The prefix also lets the existing `ReleaseModels("bw_codex_")` evict these models
on space exit rather than retaining their texture resources indefinitely.

```
python tools/install_barrowworks_production.py --plan <selection.json>
python tools/run_owner_safe.py -- python tools/install_barrowworks_production.py --from-plan docs/BARROWWORKS_ART3_SELECTION.json --install
```

Installation is preparation, not proof that the art is correctly placed in game.
After copying, compose the accepted architecture/dressing against authoritative
room openings and existing interaction anchors, then build fresh native v6 scenes
and shared compressed textures in the new depot. Never link a writable native or
material cache back to an old/shared depot. Keep cave relief in R03/R04/R06/R10/R13.
The pinned selection is in `docs/BARROWWORKS_ART3_SELECTION.json`; a changed source
checksum requires a reviewed selection refresh, not silent installation.

The older native-cache publisher hardcodes v5. Use the new publisher, which reads
the format from `ArtLibrary.SceneCacheFormat` (currently v6), and pass its receipt
to the structural checker. Hashing and engine validation also wait for the owner:

```
python tools/run_owner_safe.py -- python tools/manifest_barrowworks_scene_cache.py --depot D:/Otherreach_Depot/barrowworks_enable_20261019 --output docs/BARROWWORKS_ENABLE_SCENE_CACHE.json
python tools/run_owner_safe.py -- <engine> --headless --path src/Presentation --script ../../tools/check_barrowworks_native_cache.gd -- --manifest <absolute-receipt-path>
```

No GPU, engine, build, conversion or large hashing run has been performed at the
initial checkpoint while OWNER_PLAYING is present. Eleven lightweight Python checks
pass; actual installation, assembly, shader/cache validation and performance gates
remain pending. The ring is still disabled.

### Placement issues found by inspection

- The accepted court lamp truss reaches 6.838 m; the existing R11 walls and baked
  room ceiling are about 4 m. Its review shell is taller. Reconcile the in-game
  receiving architecture/ceiling and collision before placing the truss; do not
  shrink the accepted asset or put its lamps above the old lid.
- The accepted R12 machine is 4.620 m tall, again taller than the old room lid.
  Check its authored orientation, floor support and preserve/dismantle interaction
  access, rather than stretching it to the old pipe-frame footprint.
- Several reading exports are small physical paper/ledger objects, not complete
  furniture. Retain/support them on their boards, racks and tables; replacing a
  whole interactable stand with a small paper mesh would fail fitting and support.
- Apply the review's nonblocking brass response correction to an owned material
  copy, then verify it in court key/rim light. Do not change the accepted source rig.
- The optional water shader companion is not yet compiled/tested in Godot. Its
  GLBs have a static PBR fallback; validate the shader before selecting it.

## Enable gates — still open

1. Native engine views of all thirteen rooms, both approaches, readings, machine,
   all Custodian attacks/death, and successful ordinary solo traversal/return.
2. High interior VRAM at or below 11 GB, GPU p95 strictly below 16.7 ms, hitches
   near zero. Retain raw samples and loading/return spikes. Distinguish renderer
   allocation totals from sampled WDDM dedicated/shared memory.
3. Stream/free interior resources with their space and warm the court/return
   pipeline variants at portal entry. Do not reduce texture resolution, geometry
   quality or the rendering tier to meet the gates.
4. Court key/rim lighting must make the accepted Custodian and weapons readable.
5. Three complete interleaved option-off A/B pairs: `23d75688` vs `db4df5e4`, with
   matching content and art per revision, reporting FPS, 1% low and frame mean.
   New owner authorization replaces the old separate quiet-window marker: run
   only with OWNER_PLAYING absent, no other GPU job, and measured idle CPU across
   the entire run. Interrupted/contended pairs do not count. Use the same flag
   guard and official GPU protocol. These measurements remain pending.

The existing review-6 memory/GPU results and old partial A/B captures are useful
diagnostics, not acceptance evidence for the new production art. Keep the ring
disabled until the new gates pass; no save-schema renumbering is part of this work.
