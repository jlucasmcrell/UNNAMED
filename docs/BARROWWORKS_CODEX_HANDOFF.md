# Barrowworks: Codex art and validation handoff

The branch remains opt-in (`--visual barrowworks=1`). Use the shipped frontier; the direct interior boot is a fixture tool, not acceptance evidence. No audio files or audio manifest were changed. Balance numbers remain provisional.

## Assets and licences

Final runtime additions: `D:\Otherreach_Depot\barrowworks_codex`. Raw licensed files remain outside Git. `BARROWWORKS_ASSET_MANIFEST.json` records the derived files and source provenance.

- DanStudios, **Lost Temple**, Fab Standard Personal: https://www.fab.com/listings/48148dbe-67c8-40a1-af9b-041fa4b9354e . Owned licence evidence: `C:\Otherreach_Recovery\ASSET_REBUILD\FAB_LICENCES.md`. Selected stone wall, ceiling, floor, pillar and iron brazier modules, manually assembled into thirteen rooms, corridors and enclosing rock volumes. No statue/deity content introduced.
- Scottish Maritime Museum, **Boiler from the puffer VIC 32**, CC0 1.0: https://sketchfab.com/3d-models/884ed11e38054aa48a3db1d9025c881d . Boiler scan selected without its photographed ground plane, resized and assembled as the Custodian's housing.
- **SLASH-RENDAR**, **Old Industrial Pipe Pack PBR**, **CC BY 4.0**: https://sketchfab.com/3d-models/a10a6011334e4d11a7023d67705ac1d4 . Licence: https://creativecommons.org/licenses/by/4.0/ . Modified by reorientation, resizing, decimation, assembly, bronze material tint and hand-keyed animation. Used for the Custodian's pipe limbs, exposed rear index, gates, controls, measure and room dressing. Retain this attribution in distributed credits.
- Rob Tuytel / Poly Haven, **Rock Ground**, CC0: https://polyhaven.com/a/rock_ground . Existing verified maps on the enclosing rock mass, tiled in metres.
- Poly Haven, **wooden_crate_01**, CC0: existing verified playtest asset, used for the archive supply crate.
- Paper/type geometry is first-party. It is a visual heading; the live interaction text remains the authoritative reading.

No generative model was used for these meshes or materials. The Custodian has nine manually placed joints with rigid part weights and eight hand-keyed clips. Its sweep, fall and grasp have separate bindings selected from the authoritative attack source; animation is sampled against the simulation phase. The retained measure is animated against active interior ticks, so it freezes while absent. Dismantling removes its physical obstacle and model.

## Reproduction

Use the installed Blender 5.2 executable with `-b --python SCRIPT` for the Python assembly scripts. Conversion/assembly is CPU only; GPU rendering requires the shared GPU lock.

1. `tools/convert_barrowworks_kit.ps1` and `tools/convert_barrowworks_metal.ps1` extract/convert the licensed local sources into the depot.
2. `python tools/gen_barrowworks_blockout.py` generates authoritative content and derived corridor geometry. `python tools/gen_barrowworks_bindings.py` binds the assets.
3. Run `tools/build_barrowworks_rooms.py`, `tools/build_barrowworks_props.py` and `tools/build_barrowworks_custodian.py` in Blender. `tools/link_barrowworks_fallback.ps1` preserves the existing fallback player/NPC clips alongside these additions without modifying the shared playtest.
4. Build `src/Presentation/UNNAMED.Presentation.csproj` in Debug for the installed editor runtime. Build `src/UNNAMED.sln` in Release for tests/lint.
5. Launch the Godot console executable with the repository's `src/Presentation` as `--path`, and game arguments after `--`:

```
--content-root C:\Otherreach_Build\barrowworks_wt\content
--asset-root C:\Otherreach_Recovery\Otherreach_Playtest_RESTORE_20261015b\primary
--asset-fallback D:\Otherreach_Depot\barrowworks_codex
--bindings-overlay C:\Otherreach_Build\barrowworks_wt\content\_art_bindings.json
--visual tier=high,barrowworks=1,sky=hdri
--barrowworks-review D:\Otherreach_Scratch\barrowworks\codex\gallery
```

The review driver starts outside the frontier threshold, enters the real portal, plans walking paths using `Kinematics.IsClear`, reads the boards, opens gates, fights with the normal commands, and captures each room from the gameplay camera. It never sets quest flags or relocates the player after the initial exterior fixture position. `BARROWWORKS_REVIEW_LIMIT=1` limits a preview. A headless run omits images; using a nonexistent asset root makes the simulation route much faster to run. `frame_timing.txt` reports actual frame deltas, including screenshot stalls after entry; startup loading is excluded.

Set `BARROWWORKS_FILL_OUT` to an output directory and run `BarrowworksBlockoutTests.EveryRoom_IsReachableFromTheSpawn` to export the actual C# collision fill, with doors open and closed. The plot must consume these CSVs, not reproduce collision in Python.

## Integration

This branch retains its existing isolated save extension number. Claude must renumber it against the merged schema-27 base, following review 3's migration/fixture conflict instructions. Historical fixtures are untouched by this revision. Optional companion space is omitted from home-space companion wire maps and survives an away-space save.

The Codex fragment and Lantern's Foot are proposed item definitions plus pickup flags. Lantern revelation, compass effects and story L2/L3 are the separate story integration, not silently marked complete here. M7 letter packets remain outside this dungeon change.

Existing audio IDs requested: the bound `animated_armour` voice, `plate` flesh, `walk` and `move_heavy` steps. Additional bespoke measure/strike sounds remain an audio-team task.

## Release verification

`dotnet build src/UNNAMED.sln -c Release`: zero errors (existing compiler/analyser warnings remain). `dotnet test src/UNNAMED.sln -c Release --no-build`: **1,360 passed, zero failed, zero skipped**.

| Project | Passed | Failed | Skipped | Runtime |
|---|---:|---:|---:|---|
| EntityRegistry.Tests.dll | 23 | 0 | 0 | 40 ms |
| Presentation.Tests.dll | 127 | 0 | 0 | 169 ms |
| Domain.Tests.dll | 220 | 0 | 0 | 906 ms |
| Architecture.Tests.dll | 18 | 0 | 0 | 9 s |
| World.Tests.dll | 90 | 0 | 0 | 28 s |
| Persistence.Tests.dll | 213 | 0 | 0 | 55 s |
| UNNAMED.Content.Tests.dll | 222 | 0 | 0 | 4 m 18 s |
| Application.Tests.dll | 447 | 0 | 0 | 13 m 4 s |

Content lint: **339 definitions, zero errors**. Cross-reference report: exit 0. The current branch CLI has no separate `--engine-lint` implementation; engine readers are exercised by Content.Tests and real GameSession boots in Application.Tests. No historical fixture or save schema number was changed by the Codex revision.

The first full run caught two corrected revision issues (the public recovery hook and exterior-only pickup reachability). It also reproduced the two load-sensitive tests already identified in DeepSeek's handoff. Both pass in the final complete run; no test was disabled or loosened.

Evidence is held at `D:\Otherreach_Scratch\barrowworks\codex`. `fill_final/kinematics_fill.png` is the inspected authoritative C# fill: all thirteen rooms with gates open; R09 and R12 are correctly absent with gates shut; G1 closes the shortcut without preventing access through the archive. `route_v4/route.log` is the earlier headless frontier-to-headworks completion, including ordinary combat and G3. Final rendered evidence is recorded separately after inspection.
