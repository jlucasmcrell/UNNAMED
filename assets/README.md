# UNNAMED Assets

Game assets produced by the ComfyUI to Blender pipeline. Generated locally on BEAST
with Z-Image Turbo (concepts), Pixal3D/Trellis.2 (meshes), and Blender 5.2 (cleanup).

## Start here

| Document | What it is |
|---|---|
| `STATUS.md` | Latest run summary: counts, per-category results, timings, pack integrity |
| `CATALOG.md` / `catalog.json` | Every built asset with triangles, dimensions, LODs, provenance |
| `QUEUE_CHEATSHEET.md` | How to run, monitor, and recover the overnight queue |
| `../tools/asset_pipeline/3D_ASSET_SETUP.md` | How the pipeline works and how to change it |

## Layout

```
assets\
  concepts\    rendered concept images, one per asset id
  raw\         raw GLBs straight from ComfyUI, kept so cleanup can be re-run free
  ready\       finished assets, one folder each - this is what the game imports
  rigged\      skinned copies with a skeleton bound, for anything that animates
  rejected\    assets that failed verification
  manifests\   one JSON per 3D stage, per-asset timings and verification results
  requests\    the prompt lists that drive concept generation, plus the queue plans
  review\      contact sheets for eyeballing a category, plus APPROVED.md
```

## Timeouts, and why they were raised

`_run_3d_asset.py` talks to ComfyUI over HTTP. Two ceilings used to produce failures
that looked like generation errors when nothing was actually wrong:

- **HTTP transfer timeout.** `/object_info` returns 2,499 node definitions and a busy
  server can take minutes to answer it. The old 180 s ceiling failed healthy assets
  with `FAIL generate (180s)`. Now `UNNAMED_HTTP_TIMEOUT`, default 900 s.
- **Prompt ceiling.** A build at the current settings (40k faces, 4096 bakes, 30 steps)
  can legitimately run past 45 minutes. `UNNAMED_MAX_PROMPT_SECONDS` is now 5400 s.

If a run reports several `FAIL generate` entries in a row, check whether ComfyUI was
simply busy before assuming the asset is bad. Re-running the stage will retry them.

## The three failure modes worth knowing

Diagnosing a batch of creatures that produced no geometry turned up three distinct
problems that all present as `FAIL generate`.

### 1. Upsample resolution wedges the server

`Trellis2UpsampleStage.target_resolution` drives the size of the intermediate mesh
*before* decimation. At the template's 1536 the shape stage produced this:

```
Vertices: 17,172,199
Faces:    34,978,106
```

That mesh exhausts memory during unwrap and **hangs the server** — it stays up, keeps
logging `got prompt`, and never executes anything again. GPU drops to ~12% and HTTP
stops answering entirely, so every subsequent asset fails with a client timeout.

All queue plans therefore set `upsample_resolution: 1024`. This is the single most
important setting in the 3D stage; do not raise it back to the template default.

### 2. Some source images make the shape model return nothing

A few concepts produce an **empty voxel field**, and `upsample_shape` then raises
`max(): Expected reduction dim 0 to have non-zero size` about 77 s in. It is
deterministic: it reproduced at both upsample resolutions, from a freshly rendered
1536/24-step concept, and with a different sampler seed. All four KSamplers already use
fixed seeds, so reseeding is not a workaround.

`creature_forest_marten_runner` did this and was quarantined with `_reject_concept.py`.
Suspect this cause when an asset fails in ~77 s with no raw GLB.

### 3. A failed asset can take the stage with it

Because a wedged server makes the *next* asset's health probe fail, one bad source
image used to abort the entire stage. `_make_assets.py` now records the failure and
continues rather than breaking, so the rest of the batch still builds and the
unbuildable asset lands in the manifest for a retry pass.

## Monitoring a run

`_comfy_health.py` decides hung versus merely busy using two signals: HTTP
unresponsiveness *and* a ComfyUI log that has not been written to for 15 minutes. A
busy server fails the first test and passes the second, so it is left alone. A hung
one is asked to reboot via `/manager/reboot`; if that does not work within four
minutes it exits nonzero. When the server is wedged badly enough, `/manager/reboot`
cannot help and the process has to be killed outright, then relaunched with
`start-comfyui.bat`.

Run it by hand any time to check the server:

```
python W:\UNNAMED\tools\asset_pipeline\_comfy_health.py
```

## Long builds and short tool calls

A HQ creature build runs 35-45 minutes, which is longer than a single foreground
command comfortably allows. Launch long runs as **background jobs** and read the log
afterwards; a foreground call that times out leaves the build running with no way to
collect its output, and the finished GLB then sits in ComfyUI's output tree unnoticed.

## What a finished asset contains

`ready\<name>\` holds six GLB files and a metadata file:

| File | Contents |
|---|---|
| `<name>.glb` | base mesh with all four PBR maps embedded |
| `<name>_lod1..3.glb` | progressively decimated geometry, no textures |
| `<name>_collision_hull.glb` | convex hull proxy |
| `<name>_collision_box.glb` | axis-aligned box proxy |
| `<name>_meta.json` | dimensions, face counts, LOD ratios, applied scale |

Conventions the pipeline guarantees:

- **Origin** at footprint centre, base sitting on Z=0
- **Metres**, scaled by asset category (weapon 1.2, tool 0.6, prop 0.5, creature 1.8)
- **Y-up** on export, which is what Godot expects
- **Single-sided** materials, so no wasted backface rasterisation
- **2048x2048** PBR maps: base colour, metallic-roughness, normal, occlusion

LODs and collision proxies carry **no textures** on purpose. They share the base
mesh's material in engine, and embedding the texture set in each one made a
294-triangle hull weigh 14.6 MB.

## Checking a batch is sound

```
python ..\tools\asset_pipeline\_verify_pack.py
```

Sweeps every asset in `ready\`: confirms all six files exist, reads each base GLB,
and checks vertex attributes and embedded textures. Prints `RESULT: PACK COMPLETE`
or lists what is wrong. `STATUS.md` runs this automatically.

To check one asset with full detail, including texture resolution:

```
python ..\tools\asset_pipeline\_verify_glb.py ready\<name>\<name>.glb
```

## Rigged assets

Generated meshes have no skeleton, so a character needs a rigging pass before it can
animate. `rigged\<name>\<name>_rigged.glb` holds the result.

```
blender --background --factory-startup --python ..\tools\asset_pipeline\_blender_rig.py ^
    -- --input ready\<name>\<name>.glb --outdir rigged\<name> --name <name> --rig humanoid
```

Body plans: `humanoid` (races, NPCs), `quadruped` (most creatures, built from the
mesh's measured proportions), `worm` (limbless), `none` (report only).

Each run writes `<name>_rig.json` with the method used and a weight report: bone count,
bones that actually carry weights, and how many vertices were left unassigned. **Check
that report.** Blender's heat-map weighting fails silently on generative meshes - it
warns "failed to find solution for one or more bones" and still returns success, leaving
every vertex unweighted. The script detects that and falls back to deterministic
distance-based weighting, which cannot fail but deforms more softly at joints.

A correctly rigged file carries `JOINTS_0` and `WEIGHTS_0` attributes and one `skin`
with the expected joint count. `_verify_glb.py` prints the attributes, so use it to
confirm.

## Godot import notes

- Drop `<name>.glb` in and Godot imports meshes and PBR materials together.
- Blender does not export `TANGENT`. Godot derives tangents from the normal map, which
  is correct for these assets; only revisit if a material looks wrong.
- Wire LODs with Godot's automatic mesh LOD or a manual visibility range, and use
  `_collision_hull.glb` for static collision and `_collision_box.glb` for cheap blocking.
- Assets are **static meshes** unless a `rigged\` copy exists. Creatures and characters
  cannot animate until a rigging pass is added; see the Rigged assets section above.

## Recovering from a machine crash

The asset tree on `W:` holds every finished asset, and nothing in it depends on a run
being in progress, so a crash costs only whatever was mid-build. Recovery:

1. Confirm the shares are back. `W:` is hosted by ASTRAL, so a crash there takes the
   asset tree offline even when BEAST itself is fine.
2. Restart ComfyUI (`C:\Users\jluca\Desktop\start-comfyui.bat`) and wait for port 8188.
3. Restart the queue with `--resume`. Stages already marked `done` are skipped, and
   `--skip-existing` means finished assets are not rebuilt.

`--resume` alone resumes whichever stage was interrupted. Add `--only "<stage name>"` to
run one specific stage, but note it is a **filter on the stage name**: only matching
stages run and the queue exits afterwards instead of continuing to the next stage.

## Monitoring a run

`_comfy_health.py` decides hung versus merely busy from two signals:

- HTTP unresponsiveness, **and**
- a queue that claims a running job while ComfyUI's log has been silent for 15 minutes.

The second signal matters because a wedged server **still answers `/system_stats` and
`/queue` instantly** while doing no work at all. Reachability alone reported a stalled
server as healthy, which cost hours on one occasion: the GPU showed 27-41% utilisation
with memory oscillating, the log had been silent for 22 minutes, and CPU time did not
advance at all, yet the verdict was "healthy".

Run it by hand any time:

```
python W:\UNNAMED\tools\asset_pipeline\_comfy_health.py
```

A hung server is asked to reboot via `/manager/reboot`, letting the existing guard
relaunch with its own flags. When a server is wedged badly enough that endpoint cannot
help, the process has to be killed outright and relaunched.

## Long builds and short tool calls

A HQ creature build runs 35-45 minutes, longer than a single foreground command
comfortably allows. Launch long runs as **background jobs** and read the log afterwards.
A foreground call that times out leaves the build running with no way to collect its
output, and the finished GLB then sits unnoticed in ComfyUI's output tree. That happened
once and the completed `creature_skeleton_hound` had to be recovered by hand from
`ComfyUI\output\3d\raw\`.
