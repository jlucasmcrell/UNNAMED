# Overnight Queue - Operation Cheatsheet

Everything here is safe to run while the queue is going.

## Check progress

```
# One-page summary: counts, per-stage status, timings, failures
python W:\UNNAMED\tools\asset_pipeline\_write_status.py
type W:\UNNAMED\assets\STATUS.md

# Live tail of the queue log
powershell -Command "Get-Content W:\UNNAMED\assets\overnight.log -Wait -Tail 30"

# Machine-readable stage state
type W:\UNNAMED\assets\overnight_state.json

# Asset index, regenerated after every stage
type W:\UNNAMED\assets\CATALOG.md
```

The status summary is rewritten after every completed stage, so mid-run it already
reflects what has finished. Per-asset timings come from `assets\manifests\`, which
are written as each asset completes, so a long 3D stage still shows live progress.

## What "healthy" looks like

| Signal | Expected |
|---|---|
| `nvidia-smi` utilisation | ~100% while a stage runs |
| `nvidia-smi` memory | 10-22 GB of 24 GB |
| Concept rate | ~12-25 s per image |
| 3D rate | 122 s (simple weapon) to 1025 s (dense prop) per asset |
| `overnight_state.json` | one stage `running`, earlier stages `done` |

## If something looks wrong

**Queue stopped, no error in the log** — the machine may have restarted. Resume:

```
python W:\UNNAMED\tools\asset_pipeline\_overnight_queue.py ^
    --plan W:\UNNAMED\assets\requests\overnight_plan.json --resume
```

Completed stages are skipped, and the concept stage uses `--skip-existing`, so only
missing work is redone.

**ComfyUI restarted underneath the queue.** This happened once overnight. A restart
wipes ComfyUI's prompt history, so a prompt submitted before the restart never
appears and anything waiting on it stalls. Symptoms:

```
curl http://127.0.0.1:8188/system_stats      # connection refused
```

plus a fresh startup banner at the end of `user\comfyui.log` ("Total VRAM ...",
"Device: cuda:0", "Using sage attention") where run output should be.

Recovery:

1. Wait for the server to come back — it restarts itself and takes a few minutes.
   Confirm with `curl http://127.0.0.1:8188/system_stats`.
2. Stop the queue and its stage child (they will be hung, not working).
3. Delete the `running` stage from `overnight_state.json` so it re-runs.
4. Relaunch with `--resume`. Finished assets are skipped and only the remainder is
   rebuilt.

No assets are lost: each asset is written to disk and recorded in its manifest as it
completes, so a crash mid-stage costs at most the one asset in flight.

`_run_3d_asset.py` now gives up on a prompt after `UNNAMED_MAX_PROMPT_SECONDS`
(default 2700) and retries the server if it is briefly unreachable, so a restart can
no longer hang the queue for ever.

**Concepts suddenly much slower** — two GPU jobs are competing. Check for a second
`_make_concepts` or `_make_assets` process. One 21-asset run measured 332 s/asset
under contention versus 130-160 s alone; a single asset's bake went from 12 s to
4 min 52 s. Stop one of them.

**A render hangs at 100% GPU with no client** — a killed client leaves an orphaned
prompt in ComfyUI's queue. Clear it:

```
curl -X POST http://127.0.0.1:8188/interrupt
curl -X POST http://127.0.0.1:8188/queue -H "Content-Type: application/json" -d "{\"clear\": true}"
curl -X POST http://127.0.0.1:8188/free -H "Content-Type: application/json" -d "{\"unload_models\": true, \"free_memory\": true}"
```

**A stage failed** — it is marked `failed` in `overnight_state.json` and reported in
`STATUS.md`. Fix the cause, then re-run that stage alone:

```
python W:\UNNAMED\tools\asset_pipeline\_overnight_queue.py ^
    --plan W:\UNNAMED\assets\requests\overnight_plan.json --only "3d: props"
```

## Running one thing by hand

```
# Render missing concepts for one request file
python _make_concepts.py W:\UNNAMED\assets\requests\overnight_weapons.json --skip-existing

# Mesh one category, or one asset
python _make_assets.py --only weapon_
python _make_assets.py --only weapon_cutlass_salt_stained

# Re-clean everything from existing raw GLBs, no GPU needed (~3 s per asset)
python _make_assets.py --skip-generate --lod-faces 4000,1200,300

# Verify one asset is engine-usable
python _verify_glb.py W:\UNNAMED\assets\ready\<name>\<name>.glb

# Check which concepts each 3D stage would select, without generating
python _dryrun_plan_selection.py

# Validate a workflow before opening it in ComfyUI
python _validate_workflow.py C:\Users\jluca\ComfyUI\user\default\workflows\CONCEPT_creature.json
```

## Editing the queue order

`assets\requests\overnight_plan.json` lists the stages. Reorder the `stages` array
to change priority, or delete stages you do not want. The queue re-reads the plan
before choosing each stage, so edits take effect from the next stage boundary
onward without restarting it.

The default order interleaves each 3D stage directly after the concept file that
feeds it, because concepts are cheap and resumable while meshes are expensive.
Running every concept file to completion first would spend roughly 2.7 h of the
budget on concept art and leave little time for meshes.

## Retrying a failed stage

A stage marked `failed` is skipped by default so one bad stage cannot stall the
night, but naming it with `--only` forces it to run even if its recorded status is
`failed`:

```
python W:\UNNAMED\tools\asset_pipeline\_overnight_queue.py ^
    --plan W:\UNNAMED\assets\requests\overnight_plan.json --resume --only "3d: props"
```

## Concept prompt files

`comfyui-inspire-pack\prompts\UNNAMED\` holds the `LoadPromptsFromFile` files. They
are regenerated from the request JSON by `_requests_to_promptfiles.py`, so edit the
JSON, not the .txt, or your changes will be overwritten.

To drive them from the GUI, open `CONCEPT_QUEUE_promptfile.json` and press Queue:
`easy seed` steps `start_index`, so each run advances to the next prompt in the file.
