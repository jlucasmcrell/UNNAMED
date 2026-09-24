# Wave 0 — asset-id naming migration and three-host allocation

Status as of 2026-09-23 10:55.

## 1. Asset-id naming: complete across every tree

Every GLB in the library now names its mesh, material and mesh-bearing node after its own asset
id. Measured, not assumed:

| tree | GLB | asset-id named | generic | unreadable |
|---|---|---|---|---|
| `ready/` | 2082 | 2082 | 0 | 0 |
| `rigged/` | 75 | 75 | 0 | 0 |
| `PROOFSET/` | 90 | 90 | 0 | 0 |
| `animation/ready/` | 2 | 2 | 0 | 0 |
| `raw/` | 357 | 357 | 0 | 0 |
| **total** | **2606** | **2606** | **0** | **0** |

The convention is **mesh name = file stem + `_mesh`**, material `MAT_<stem>`. So
`weapon_arming_sword.glb` holds mesh `weapon_arming_sword_mesh`, its LOD1 holds
`weapon_arming_sword_lod1_mesh`, its hull holds `weapon_arming_sword_collision_hull_mesh`, and
`creature_x_rigged.glb` holds `creature_x_rigged_mesh`. Bone node names are never touched — a
skinned mesh binds by joint index, so renaming bones would silently break every rig.

### Two source bugs were fixed, not just repaired

The migration exposed two places that would have kept producing generic names forever. Repairing
the files without fixing these would have needed a rename pass after every build.

- **`_blender_cleanup.py` `make_lod`** copied the source mesh datablock and named the *object*
  but never the *mesh*. Every LOD exported as `<asset>_mesh.001`, `<asset>_mesh.002`, … Blender's
  duplicate counter, so no LOD was identifiable. `_blender_sockets.py` already had the missing
  line; the cleanup path did not.
- **`_blender_rig.py` `merge`** hardcoded `joined.name = "MESH"` and left the datablock at the
  importer's `Mesh_0`/`Material_0`. This is why all 75 rigged GLBs were generic. `merge` now takes
  the asset name, and the rigged export follows the same stem rule as LODs and proxies.

Both were verified by rebuilding: a fresh `creature_snow_hare_hopper_rigged.glb` now exports mesh
`creature_snow_hare_hopper_rigged_mesh`, material `MAT_creature_snow_hare_hopper_rigged`, mesh
node `creature_snow_hare_hopper_rigged`, with all 19 bone nodes and the skin intact.

### Repair and guard tooling generalised

- `_rename_glb_assets.py` gained `--root` and now derives the variant suffix from the filename
  instead of a hardcoded lod/collision list, so one tool serves `ready/` and `rigged/`.
- `_check_glb_health.py` gained `--root` and now parses **every** `.glb` in a tree. It previously
  covered only `ready/` and only the six known suffixes, which meant the 75 rewritten rigged files
  would have gone unverified. Result after the rewrite: `ready` 2076 checked / 0 corrupt,
  `rigged` 75 checked / 0 corrupt.

## 2. Pack

`348/348 assets clean — PACK COMPLETE`.

`assets/PROOFSET/` is untouched and intact: 15 entries, 15 directories, no directory below 7
files. Six scratch builds that had been left in `ready/` (`methodA_*`, `methodB_*`, `test_*` —
armour bakeoff and stub-convention experiments) were moved to `assets/review/bakeoff_meshes/` so
they cannot be mistaken for pack content.

## 3. Three-host allocation

| host | GPU | role | can build 3D? | current work |
|---|---|---|---|---|
| BEAST | RTX 3090 24 GB | 3D | yes | `queue_wave0_night.json` |
| ASTRAL | RTX 5090 32.6 GB | 3D | yes, **3.9× faster** | `queue_astral.json` |
| RAZER | RTX 4070 Ti 12.9 GB | 2D only | **no** | idle — concept work complete |

**RAZER cannot build 3D, and cannot be cheaply made to.** Trellis2 is *core* ComfyUI
(`comfy_extras/nodes_trellis2.py`), added in 0.34.0. BEAST is 0.34.0; RAZER is 0.33.0, so it
reports zero Trellis/Pixal3D/NAF nodes in its `/object_info` and holds none of the model files.
Adding it as a third 3D host therefore means upgrading its ComfyUI checkout — an invasive change
to a working instance. Reviewed and declined: BEAST and ASTRAL cover the remaining 3D backlog
anyway. RAZER stays the 2D host.

RAZER finished its last concept batch (34/34, 0 missing) and is now genuinely idle: across every
request file, **1160 concept ids, 1 unrendered** — and that one is `paritytst`, a deliberate
parity-test artifact. `icon_*` and `material_*` (124 concepts) are **2D deliverables that need no
3D at all**, per `ASSET_GENERATION_PLAN.md`, so their concepts are already the finished artefact.

### The 3D backlog is split on disjoint prefixes

That is what makes it safe for both hosts to write into the same `ready/` tree. It was also
**rebalanced**: as first allocated, BEAST held 104 assets (≈7.2 h at its measured ~250 s) against
ASTRAL's 78 (≈1.4 h at 63 s), so ASTRAL would have gone idle for roughly six hours while BEAST
ground on. `item_` (33) and `prop_` (45) moved from BEAST's plan to ASTRAL's:

- **ASTRAL** — `magic_`, `travel_`, `container_`, `vehicle_`, `animal_`, `mount_`, `race2_`,
  `raceclass_`, `item_`, `prop_`.
- **BEAST** — `weaponcomp_`, `magiccomp_`, `herb_`, `flora_`, `reagent_`, `resource_`, `racebody_`.

Both supervisors were restarted to pick this up. The restart cost at most one in-flight asset each,
because the builder runs with `--skip-existing` and so skips everything already completed.

## 4. The ASTRAL tunnel was a silent single point of failure

A one-shot `ssh -N -L 18190:127.0.0.1:8190` died with `client_loop: send disconnect: Connection
reset`. The ASTRAL builder then failed **every** prompt in about three seconds — the unreachable-
server signature — while the queue log still looked like a running job. One asset was lost that
way (`travel_othergate_mirrored_door`), and had it gone unnoticed the rest of the queue would have
burned through the same way.

Two fixes:

- `_astral_tunnel.py` replaces the one-shot command with a loop that reconnects on exit, with SSH
  keepalives, and appends every drop to `assets/astral_tunnel.log` so a flapping tunnel is visible
  rather than silent. Running now.
- `_install_astral_tunnel_task.ps1` registers that loop under the per-user `HKCU\...\Run` key so it
  survives the end of an agent session. A scheduled task would also cover a boot with nobody logged
  in, but `Register-ScheduledTask` needs elevation this account does not have, so the Run key is
  the strongest option actually available.

The lost asset was rebuilt automatically: the supervisor treats a stage with any failed asset as
failed and retries it on a later pass. Verified present at 12:37:29.

**Detection rule that came out of this:** `/queue` reads `running=0` during the local Blender
cleanup between items, so it is not evidence a host is stalled. The ASTRAL host was confirmed live
by its own log on its own disk (`W:\ComfyUI_LTX25\ComfyUI\user\comfyui_8190.log`) and by new
directory timestamps under `ready/`.

## 5. Concurrency rule held

Two `_render_queue.py` clients had accumulated against RAZER (one on the stale overnight list, one
on the final 34) — the documented "never run two concept jobs against one server" failure. The
older client was terminated; the survivor completed 34/34. After the rebalance, process inspection
shows exactly one supervisor, one queue driver and one builder per host, plus the single tunnel.

## 6. Current totals

- `ready/` — **436 assets**, `436/436 assets clean — PACK COMPLETE`
- Naming — **2606/2606 GLBs asset-id named** (see section 1)
- `PROOFSET/` — 15/15 intact
- Remaining 3D — 212 concepts unbuilt at the time of the rebalance, now split between the two
  capable hosts
