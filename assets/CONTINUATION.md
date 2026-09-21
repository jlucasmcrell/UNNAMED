# Overnight Asset Run - Continuation Brief

Everything a new session needs to pick this up without re-deriving anything.
Written 2026-09-21 02:2x, while the queue was still running.

---

## 1. What this is

A local-first game-asset pipeline for **UNNAMED**, a first-person fantasy RPG being
built in Godot by a separate coding agent (Qwen on the machine ASTRAL). This machine
is **BEAST** (RTX 3090, 64 GB RAM, Ryzen 9 5950X).

The objective was: build prompt files for weapons, props, creatures, icons and PBR
material sources; queue concept renders and 3D generation sequentially with
resume-on-restart; and leave a verified, catalogued asset pack plus a run log and
status summary ready for review.

**Phase A of the project charter is already met on every line** — weapons 10
(asked 5-10), tools 3 (3), props 10 (10-20), creatures 34 (asked 1), icons 20 (20).
Everything beyond that is expansion library.

## 2. Where things live

| Path | Contents |
|---|---|
| `W:\UNNAMED\tools\asset_pipeline\` | all pipeline tools (15 Python scripts) and `3D_ASSET_SETUP.md` |
| `W:\UNNAMED\assets\` | the asset collection - see `README.md` there |
| `W:\UNNAMED\assets\requests\` | prompt request lists and `overnight_plan.json` (the run order) |
| `C:\Users\jluca\ComfyUI\` | ComfyUI 0.34.0, models, and workflow JSON |
| `C:\Users\jluca\ComfyUI\custom_nodes\comfyui-inspire-pack\prompts\UNNAMED\` | the five `LoadPromptsFromFile` prompt files |

Read next, in order: `assets\README.md`, `assets\STATUS.md`, `assets\CATALOG.md`,
`assets\QUEUE_CHEATSHEET.md`, `tools\asset_pipeline\3D_ASSET_SETUP.md`.

## 3. The pipeline

```
asset request (JSON)
  -> Z-Image Turbo concept render              _make_concepts.py     ~25 s
  -> BiRefNet bg-removal -> MoGe camera FOV
  -> Pixal3D or Trellis.2 sparse structure -> shape -> texture
  -> voxel -> mesh -> remesh -> decimate -> UV unwrap
  -> bake baseColor / metallicRoughness / normal / occlusion
  -> GLB with embedded PBR                     _run_3d_asset.py      ~60-1160 s
  -> Blender: normalise, single-sided, LOD1-3, collision hull + box
                                               _blender_cleanup.py   ~3 s
  -> verified, catalogued asset folder         _make_assets.py
```

`_make_assets.py` orchestrates the 3D half; `_overnight_queue.py` runs stages in
order from `overnight_plan.json`.

### Conventions the pipeline guarantees

- Origin at footprint centre, base on Z=0
- Metres, scaled by category: weapon 1.2, tool 0.6, prop 0.5, creature 1.8
- Y-up on export (what Godot expects)
- Single-sided materials
- 2048x2048 PBR maps
- LODs and collision proxies carry **no textures** (they share the base material;
  embedding textures in each made a 294-triangle hull weigh 14.6 MB)

### Which 3D model to use

- **Pixal3D** (default) - pixel-aligned, geometry sticks to the input view. Best for
  weapons, props, hard-surface.
- **Trellis.2** - more tolerant of awkward topology. Use when Pixal3D's view-fidelity
  produces a bad silhouette.

## 4. Measured numbers - use these, not guesses

| Stage | Time |
|---|---|
| Concept render (1024², 8 steps) | **~25 s** |
| 3D generate, median across 59 timed assets | **200 s** |
| 3D generate, mean | **300 s** |
| 3D generate, fastest / slowest seen | 62 s / 1158 s |
| Blender cleanup | **~3 s** |
| Re-clean from existing raw GLBs (`--skip-generate`) | ~3 s, no GPU |

**Use the mean, not the median, for planning.** Dense organic meshes (creatures,
props with lots of detail) cost 2-5x a simple weapon. The median flatters the
estimate; the mean is what the night actually costs.

Hardware limits:

- Single asset peaks at **14.75 GB VRAM** of 24 GB, so **two concurrent assets would
  need ~29 GB - parallelising is impossible on this card.** The serial queue is a
  necessity, not a shortcut.
- GPU duty cycle averages 50-75%; the gaps are CPU-side remesh/UV/bake steps.

## 5. Status at time of writing

This is a snapshot. The queue is still working, so these numbers move - always check
`assets\STATUS.md` for the live figures.

```
queue          running  3d: props (2.5 h cap)
concepts       302
3D assets      64 verified
GLB files      384
failures       none
integrity      verified clean, including scale and origin
```

Per-category build progress at this moment:

| Category | Built | Concepts |
|---|---|---|
| creature | 34 | 62 |
| prop | 17 | 110 |
| weapon | 10 | 106 |
| tool | 3 | 3 |

Remaining plan stages: `3d: props` (running), `3d: tools`, `3d: weapons`, `catalog`.

## 6. How to run, monitor and recover

Full detail in `assets\QUEUE_CHEATSHEET.md`. The essentials:

**Monitor**
```
python W:\UNNAMED\tools\asset_pipeline\_write_status.py
type W:\UNNAMED\assets\STATUS.md
powershell -Command "Get-Content W:\UNNAMED\assets\overnight.log -Wait -Tail 30"
```

**Resume after any interruption** - completed assets are skipped, so nothing is redone
```
python W:\UNNAMED\tools\asset_pipeline\_overnight_queue.py ^
    --plan W:\UNNAMED\assets\requests\overnight_plan.json --resume
```

**Verify the pack**
```
python W:\UNNAMED\tools\asset_pipeline\_verify_pack.py
```
Confirms every asset has all six files, reads each base GLB, checks vertex
attributes, embedded textures, that dimensions match the category target, and that
the base sits on Z=0. Prints `RESULT: PACK COMPLETE` or lists faults.

**Run the queue order** - edit `stages` in `assets\requests\overnight_plan.json`.
A running queue re-reads the plan before each stage, so edits take effect at the next
stage boundary without restarting. `max_seconds` caps a stage so one category cannot
consume the whole night.

## 7. Failure modes already hit - do not rediscover these

1. **ComfyUI crash mid-run.** A restart wipes prompt history, so anything waiting on
   a prompt stalls. Verify with `curl http://127.0.0.1:8188/system_stats`; the log
   shows a fresh startup banner ("Total VRAM ...", "Device: cuda:0") where run output
   should be. Recovery: wait for the server, stop the hung queue, delete the `running`
   stage from `overnight_state.json`, relaunch with `--resume`. **No assets are lost** -
   each is written and recorded as it completes; the crash cost one asset.

2. **A low GPU reading is NOT a crash.** Between assets ComfyUI releases VRAM, so 1%
   utilisation with no new asset is normal. I raised this as a suspected crash twice
   and was wrong once. The only reliable test is `system_stats` answering.

3. **Orphaned prompts.** Killing a queue client leaves a prompt running with nobody
   collecting it. Clear with `interrupt` -> `queue clear` -> `free`. Note `interrupt`
   can take a while during a long bake.

4. **Never run two GPU jobs at once.** Controlled comparison: a 21-asset run took
   332 s/asset under contention vs 122-160 s alone; one bake went from 12 s to 4 min 52 s.

5. **Dynamic combo inputs via the API.** `sign_mode` / `placement_mode` need the option
   as a plain string with sub-widgets as dotted keys alongside it. Wrapping the value
   in a dict makes the server drop the input silently.

6. **The hidden `control_after_generate` widget** after `seed` occupies a slot in
   `widgets_values`. Getting it wrong shifts every later value by one and the workflow
   silently mis-configures.

7. **`partial` in `overnight_state.json`** means "deliberately stopped". The queue
   treats it as settled and skips it. Clear it if you want that stage to run again.

8. **A time-capped stage used to be recorded as FAILED.** Hitting `--max-seconds` was
   exiting non-zero, so the queue logged a healthy stage as a failure - `3d: creatures`
   shows as `partial` (corrected) rather than `failed` for this reason. Fixed in
   `_make_assets.py`: reaching the cap now returns 0, because everything attempted was
   still verified. If you see a stage marked failed, check the log for
   "time cap of Ns reached" before assuming real breakage.

## 8. Tool reference

| Script | Purpose |
|---|---|
| `_make_concepts.py` | render concept images from a request JSON |
| `_make_assets.py` | concept -> raw GLB -> cleaned asset -> verified |
| `_run_3d_asset.py` | one asset through the ComfyUI 3D graph |
| `_blender_cleanup.py` | normalise, LODs, collision (runs inside Blender) |
| `_overnight_queue.py` | run plan stages in order, resumable |
| `_catalog_assets.py` | build `catalog.json` and `CATALOG.md` |
| `_write_status.py` | build `STATUS.md`; refreshes the catalogue too |
| `_verify_glb.py` | one asset, full detail incl. texture resolution |
| `_verify_pack.py` | whole pack, incl. normalisation |
| `_setup_3d_models.py` | fetch the 15 GB model set (for another box) |
| `_build_concept_workflows.py` | generate the concept workflow JSONs |
| `_build_queue_workflow.py` | generate the prompt-file queue workflow |
| `_requests_to_promptfiles.py` | request JSON -> `LoadPromptsFromFile` files |
| `_validate_workflow.py` | validate a workflow against live schemas before opening |
| `_dryrun_plan_selection.py` | check which concepts each 3D stage would select |

## 9. Open items / suggested next steps

1. **Finish the backlog.** ~250 3D-eligible concepts remain, roughly 14 h of GPU time.
   Run the queue again with `--resume`.
2. **Rigging is entirely absent.** Every asset is a static mesh with no armature or
   skin weights. Creatures and characters cannot animate until a rigging pass exists.
   Meshy or Tripo rigging APIs are the pragmatic route.
3. **SFX was never built.** It needs Stable Audio 3 weights (~2-3 GB) plus its own
   workflow; it is the one planned workflow not done.
4. **PBR material maps** are source textures only - no normal/roughness/metallic
   estimation has been run. CHORD is research-only licensed, so check licensing before
   shipping anything derived from it.
5. **160 icon and material concept renders were deliberately dropped** from the queue in
   favour of 3D assets. Regenerate with `_make_concepts.py` when wanted. Icons are 2D
   art and are never sent through the mesh pipeline.
6. **Godot import has not been tested.** No Godot project is reachable from BEAST. The
   assets are validated for shape and scale, but nothing has been imported into the
   engine yet.

## 10. Licensing

Trellis.2 and Pixal3D weights are MIT, and the native ComfyUI path carries none of the
NVIDIA `nvdiffrast`/`nvdiffrec` non-commercial dependencies, so generated assets are
usable commercially. Avoid third-party community node packs for Pixal3D - some are
labelled academic-only. `TencentARC/Pixal3D` is flagged `extra_gated_eu_disallowed`;
confirm the EU position before shipping there.

---

*To continue: read section 2's documents, check `STATUS.md` for the current state, and
resume the queue if it has stopped. Nothing in this pipeline needs manual steps.*
