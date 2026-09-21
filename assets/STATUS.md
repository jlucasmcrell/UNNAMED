# Overnight Asset Run - Status

Report generated: 2026-09-21 10:39:17

Queue state: **finished** (started 2026-09-20 20:05:47)

> Note on the run log: stages that stopped at their `--max-seconds` cap were
> recorded as `FAILED` at the time, because hitting the cap exited non-zero.
> That was a reporting bug, since fixed. Every asset those stages produced was
> built and verified normally, and the stage notes in `overnight_state.json`
> record what actually happened.

## Output

- concept images: **318**
- 3D assets verified: **98**
- GLB files produced: **588** (base + LODs + collision per asset)
- raw GLBs awaiting cleanup: **1**

## Phase A starter pack

| Category | Built | Phase A target | Status |
|---|---|---|---|
| weapons | 10 | 5-10 | met |
| tools | 4 | 3-3 | met |
| props | 26 | 10-20 | met |
| creatures | 42 | 1-1 | met |
| icons (2D art) | 20 | 20-20 | met |

### Concepts by category

| Prefix | Count |
|---|---|
| `prop_` | 110 |
| `weapon_` | 105 |
| `creature_` | 62 |
| `icon_` | 10 |
| `npc_` | 8 |
| `race_` | 8 |
| `item_` | 5 |
| `resource_` | 5 |
| `tool_` | 5 |

## Queue stages

| Stage | Status | Duration | Planned cap |
|---|---|---|---|
| concepts: weapons (100) | done | 44.7 min | - |
| prompt files sync | done | 0s | - |
| concepts: props (100) | done | 41.7 min | - |
| 3d: weapons | partial | 0s | 2.0 h |
| concepts: creatures (60) | done | 22.6 min | - |
| 3d: creatures | partial | 121.8 min | 2.0 h |
| 3d: props | partial | 154.0 min | 2.5 h |
| 3d: tools | done | 0s | - |
| catalog | done | 0s | - |

## Work remaining

- 3D-eligible concepts: **282**
- built: **98**
- not yet built: **200**
- at tonight's median of 227s per asset, the remainder needs **12.6 h** of GPU time

Run the queue again with `--resume` to continue through the remainder; completed assets are skipped.

### Deferred by design

- **20 icon / item / resource / material concepts** are 2D art. They are finished deliverables and are deliberately not sent through the mesh pipeline.
- **200 3D concepts** remain unbuilt and queue behind the stages that did run. Raw GLBs are kept in `raw/` so re-cleaning them later needs no GPU.

## 3D generation timing

- assets generated: **50**
- median: **227s**
- fastest: **77s**, slowest: **1461s**
- total generation time: **5.0 h**

Generation time swings widely with subject complexity, so treat the median as the planning number and the maximum as the risk case.

## Verified assets by category

| Category | Verified | Faces (total) |
|---|---|---|
| character | 16 | 392,414 |
| creature | 26 | 639,552 |
| prop | 28 | 663,217 |
| tool | 7 | 173,482 |
| weapon | 14 | 297,455 |

## Catalogue

98 assets, 2,369,391 base triangles, 1195.8 MB of base GLB.

Full detail in `CATALOG.md` and `catalog.json`.

## Pack integrity

- 98/98 assets clean
- RESULT: PACK COMPLETE

## Failures

- asset `creature_forest_marten_runner` did not verify: ComfyUI unusable: preflight: no response to /system_stats (TimeoutError)
preflight: log last written 926s ago
preflight: server appears hung
- asset `prop_carpenters_tool_chest` did not verify: generation produced no GLB:     "  File \"C:\\Users\\jluca\\ComfyUI\\comfy_extras\\nodes_mesh_postprocess.py\", line 2429, in execute\n    r
- asset `creature_skeleton_hound` did not verify: cleanup failed:         ^^^^^^^^^^^^^^^^
        json_util.BlenderJSONEncoder,
        ^^^^^^^^^^^^^^^^^^^^^^^^^^^^^
        buffer)
       
- asset `creature_forest_marten_runner` did not verify: generation produced no GLB:     "  File \"C:\\Users\\jluca\\ComfyUI\\comfy_api\\latest\\_io.py\", line 1990, in EXECUTE_NORMALIZED\n    to_r
- asset `creature_forest_marten_runner` did not verify: generation produced no GLB:     "  File \"C:\\Users\\jluca\\ComfyUI\\comfy_api\\latest\\_io.py\", line 1990, in EXECUTE_NORMALIZED\n    to_r
- asset `creature_forest_marten_runner` did not verify: generation produced no GLB:     "  File \"C:\\Users\\jluca\\ComfyUI\\comfy_api\\latest\\_io.py\", line 1990, in EXECUTE_NORMALIZED\n    to_r

## Resuming

```
python W:\UNNAMED\tools\asset_pipeline\_overnight_queue.py \
    --plan W:\UNNAMED\assets\requests\overnight_plan.json --resume
```

Completed stages are skipped and `--skip-existing` on the concept stage means only missing work is redone.
