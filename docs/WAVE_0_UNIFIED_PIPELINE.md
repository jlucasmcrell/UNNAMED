# WAVE 0 — UNIFIED MODULAR PIPELINE

The modular chain and the production chain were **two half-pipelines**, which is why no
directory held a complete modular asset:

| Chain | Produced | Missing |
|---|---|---|
| `_blender_cleanup.py` | LODs, collision, metadata | sockets, asset-id naming |
| `_make_modular.py` | sockets, asset-id naming, Godot validation | LODs, collision |

Production copies carried `Mesh_0` / `Material_0` and **zero** socket nodes. The
modular-validated copies had correct names and sockets but no LOD chain. Neither was shippable.

## The unified chain

```
raw GLB
  -> stub cut            (_blender_cut_stub.py)     removes the sacrificial neighbour
  -> author + LOD + collision  (_blender_sockets.py)  one Blender session
  -> verify sockets      (_verify_sockets.py)
  -> Godot import        (validate_glb.gd)
```

`_blender_sockets.py` now emits the whole set in a single pass, so the base GLB, its LODs and
its collision proxies all share one naming scheme and one authoring session.

## What one asset now yields

Verified on `weaponcomp_mace_head_flanged_a`:

```
weaponcomp_mace_head_flanged_a.glb                7701 KB   base, sockets, textures
weaponcomp_mace_head_flanged_a_lod1.glb            196 KB   8000 faces, no textures
weaponcomp_mace_head_flanged_a_lod2.glb             74 KB   2500 faces, no textures
weaponcomp_mace_head_flanged_a_lod3.glb             70 KB   2382 faces, no textures
weaponcomp_mace_head_flanged_a_collision_hull.glb  168 KB   real convex hull, 3582 faces
weaponcomp_mace_head_flanged_a_collision_box.glb     2 KB   box proxy
weaponcomp_mace_head_flanged_a_sockets.json          1 KB   report: sockets, LODs, collision
```

**Socket basis verified in the exported GLB:** position drift 0.0 mm, primary dot +1.0000,
secondary dot +1.0000.

**LOD chain strictly descending:** 34,837 -> 8,000 -> 2,500 -> 2,382.

## Two bugs found by inspecting the output, not by assuming it worked

**1. The "convex hull" was not a hull.** The first implementation decimated a copy of the mesh
rather than computing a hull, so it kept concavities — a convex proxy that is concave is worse
than useless, because a concave proxy has the cost of collision testing without the benefit.
Fixed with `bmesh.ops.convex_hull`, plus `PLANAR` dissolve before `COLLAPSE` because a hull is
convex by construction, so coplanar faces dissolve first and cheaply. 5,523 -> 3,582 faces.

**2. Two adjacent LODs were the same size.** LOD budgets were applied independently, so
`lod3` at a 600-face request came out at 2,382 — identical to `lod2`'s result — because the
requested reduction was beyond what the topology allows. Budgets now **cascade**: each LOD is
capped by its own budget *and* by 60% of the previous LOD's actual result, so the chain always
descends even when a request is unreachable.

## Known limitation: a topology floor

Both the hull and the final LOD bottom out around **2,400-3,600 faces** on these generative
meshes, well above the 300-600 requested. The decimator cannot collapse further without
destroying the surface, because Trellis output is dense and irregular rather than
quad-structured.

Consequences worth stating plainly:

- **LOD3 saves little over LOD2.** For a 34k-face source the useful chain is effectively
  base -> LOD1 -> LOD2.
- **Collision hulls are heavier than intended** (~3.6k faces rather than a few hundred). For
  small props a box proxy alone is the honest answer; `_blender_cleanup.py`'s existing rule
  that collision is for world geometry the player touches still applies, and for a worn
  weapon part it should be **none at all**.
- If cheaper LODs are needed later, the fix is remeshing to a quad-dominant surface first, not
  a more aggressive decimate ratio.

## What this does not yet do

- **The armour pieces have not been through the unified chain.** They need socket definitions
  authored for them first, and their fit comes from Method C rather than from generation.
- **The production copies in `ready/` still carry generic names.** Promoting the unified
  output is a deliberate step, not done yet, because the night queue is writing to `ready/`
  and a concurrent restructure would race it.
- **Only the mace has been run end to end.** The other fourteen proof assets still need the
  unified pass.
