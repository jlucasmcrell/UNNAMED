# prop_archetype_kit (Phase B, B8)

A reusable prop **archetype layer** under `procgen_lib`: `procgen_lib/archetypes.py` holds one parameterised
builder per shared prop form, and `_build_archetype_props.py` is a single generic driver that looks up
`P["fn"]` on that module and calls it -- the driver itself never names an archetype, so every family's geometry
lives in the shared library, never in a per-instance script. Proven with two variants each of seven forms
(14 assets total, ids `prop_arch_<family>_a` / `_b`), staged through `procgen_lib.runner.run_template` exactly
like any other template and promoted to `assets/ready/<id>/` with LOD1/LOD2 and a `_meta.json`.

Per-instance parameters live in `assets/manifests/procgen/prop_arch_<family>_<variant>.json` (a deep override of
the family default in `_build_archetype_props.py`'s `FAMILIES` dict), the same convention as
`_procgen_soft_goods.py`. The `_a` files are empty overrides (the family defaults *are* the `a` instance); the
`_b` files carry only the parameters that differ.

## What each builder makes

| builder | what it builds | key parameters |
|---|---|---|
| `timber_frame` | posts at a footprint's corners, a beam over each edge, optional knee braces | `footprint_m`/`size_m`, `height_m`, `post_m`, `beam_h_m`, `beam_w_m`, `brace` |
| `plank_box` | chamfered-plank walls + floor; optional lid on battens with bands, a hinge pair, a hasp, a rope/ring handle -- a crate is `lid.present=False`, a chest `True` | `length_m`, `depth_m`, `body_h_m`, `rows`, `bands`, `lid`, `handle` |
| `wheel_axle` | lathed hub, radiating spokes, a felloe rim (`pr.ring`), an iron tire band, a stub axle | `radius_m`, `spoke_count`, `hub_r_m`/`hub_len_m`, `tire` |
| `bench_table` | legs (via `timber_frame`'s stretcher frame, continued up) + boards across the top | `length_m`, `depth_m`, `top_h_m`, `leg_m`, `top_boards` |
| `posts_and_rails` | a fence run: a post at each point of a polyline, 1+ rails between consecutive posts | `posts_m`, `post_h_m`, `rails_z_m` |
| `pipe_conduit` | a round tube along a polyline, a bolted flange at each open end, an iron collar at each bend, an optional inline valve (lathed body + stem + handwheel) | `path_m`, `radius_m`, `valve` |
| `tech_housing` | a chamfered cased box; a flush trim seam standing in for a recessed panel, raised louvre vent slats, corner rivets -- an unfamiliar manufactured case, not a glowing sci-fi panel | `size_m`, `chamfer_m`, `panel`, `vents`, `fasteners` |
| `banded_ring`, `split_lobe`, `stepped_plinth` | ancient/alien artifact components (not among the 14 demo props; geometry-only smoke-tested) | `radius_m`/`tube_r_m`+bands; `length_m`/`radius_m`/`gap_m`; `steps`/`shrink` |

Shared fittings, applied onto other builders' geometry: `strap_fitting`, `rivet_row`, `hinge_fitting`,
`hasp_fitting`, `ring_handle_fitting` (chest's bands/hinges/hasp/handle, pipe's flange bolts, tech_housing's
panel seam and corner rivets -- `wheel_axle` is the one demoed family that needs none of them, built entirely
from `primitives.lathe`/`member`/`ring`). `_board_row` (jittered-width boards laid side by side) is shared by
`plank_box` (walls, floor, lid) and `bench_table` (top).
`rope_chain` gives `plank_box` its rope handle option (a chain-link mode also exists, geometry-only tested, not
used by any of the 14).

## The 14 instances

| id | family | what differs from the family default |
|---|---|---|
| `prop_arch_crate_a` | crate | (the family default itself) pine, 0.70 x 0.55 x 0.60 m, no lid |
| `prop_arch_crate_b` | crate | elm, 0.55 x 0.50 x 0.52 m, 3 rows instead of 4 |
| `prop_arch_chest_a` | chest | (default) oak, 0.62 x 0.40 x 0.36 m, 2 bands, rope handle |
| `prop_arch_chest_b` | chest | elm, 0.50 x 0.34 x 0.30 m, smaller bands, ring handle instead of rope |
| `prop_arch_bench_a` | bench | (default) oak, 1.30 m long, 0.45 m seat height |
| `prop_arch_bench_b` | bench | pine, 1.00 m long, 0.44 m seat height, 2 top boards instead of 3 |
| `prop_arch_wheel_a` | wheel | (default) ash, 1.12 m diameter (0.56 m radius), tire fitted |
| `prop_arch_wheel_b` | wheel | elm, 1.00 m diameter, 8 spokes instead of 10, no tire |
| `prop_arch_fence_a` | fence | (default) pine, 4 posts, ~3.1 m run |
| `prop_arch_fence_b` | fence | ash, 3 posts, ~1.9 m run, lower rail heights |
| `prop_arch_pipe_a` | pipe | (default) wrought/cast iron, 3-bend path, valve near the start |
| `prop_arch_pipe_b` | pipe | cast/wrought swapped + more rust, 4-bend path (a vertical rise added), valve mid-run, thinner pipe |
| `prop_arch_techhousing_a` | techhousing | (default) cast iron, 0.42 x 0.30 x 0.34 m, 5 vents |
| `prop_arch_techhousing_b` | techhousing | slate stone case, 0.34 x 0.24 x 0.46 m (taller), 7 vents, larger panel |

## Cost table (what the brief asks: variant a's new code vs variant b's parameter-only cost)

"Archetype fn lines" is `export.code_lines` (non-blank, non-comment, docstring-excluded -- the same rule the
reuse gate uses) over the family's own top-level builder in `archetypes.py`, charged in full to variant `a`
(variant `b` calls the same already-written function: 0 new archetype lines). "Param lines" is the non-blank
line count of the instance's own JSON file (`assets/manifests/procgen/<id>.json`) -- this is also what
`run_template` writes automatically into each asset's `provenance.cost.asset_specific_lines` (via
`instance_files=`). Wall-clock is `provenance.build_seconds` (this machine, CPU-only bake, res 1024, 20 samples).

| family | archetype fn(s) charged | fn lines | param lines a | param lines b | **total a** | **total b** | b/a | wall s (a) | wall s (b) |
|---|---|--:|--:|--:|--:|--:|--:|--:|--:|
| crate | `plank_box` | 68 | 5 | 9 | 73 | 9 | 12% | 31.2 | 44.5 |
| chest | `plank_box` (shared with crate) | 68 | 5 | 17 | 73 | 17 | 23% | 29.6 | 23.5 |
| bench | `bench_table` + `timber_frame` | 46 | 5 | 9 | 51 | 9 | 18% | 16.8 | 12.5 |
| wheel | `wheel_axle` | 27 | 5 | 12 | 32 | 12 | 38% | 10.0 | 8.3 |
| fence | `posts_and_rails` | 18 | 5 | 9 | 23 | 9 | 39% | 23.0 | 23.8 |
| pipe | `pipe_conduit` | 46 | 5 | 13 | 51 | 13 | 25% | 12.4 | 6.6 |
| techhousing | `tech_housing` | 31 | 5 | 13 | 36 | 13 | 36% | 6.1 | 4.8 |

Every pair's `b` total is well under half of `a`'s (12-39%), which is the reuse gate's own bar
(`assets/manifests/asset_production.json.cost_rule.reusable_when`: "the second asset's asset-specific lines...
at most half the first asset's"). `chest` charges the same 68 `plank_box` lines as `crate` because both call the
one function (chest exercises its `lid.present=True` branch -- bands, hinges, hasp, handle -- crate does not);
in the actual build order that function was written once, so the true one-time cost across all seven families is
far below the sum of the "fn lines" column above.

Shared fittings/helpers not charged to any single family above (module plumbing, written once, reused wherever a
builder needs them -- `_unit`, `_widths`, `_board_row`, `strap_fitting`, `rivet_row`, `hinge_fitting`,
`hasp_fitting`, `ring_handle_fitting`, `rope_chain`): **47 lines total**. `archetypes.py` in full (all 10
builders, the 3 not-demoed artifact components included): **325 code lines**.

Wall-clock is a weaker reuse signal than the line counts: it is dominated by the fixed per-asset Cycles bake
(CPU, 1024 px atlas, 20 samples, 3-4 passes) that every instance pays regardless of how much of its geometry
code is new, not by authoring effort. `crate` and `fence` show `b` at parity or slightly slower than `a` for
exactly that reason (fewer triangles does not meaningfully shorten a fixed-resolution bake); the real
agentic-effort saving a template_variant buys is in the lines an agent has to read and write, which the
line-count columns capture directly.

## Contact sheet and fixes

A 14-tile Cycles/CPU contact sheet (one framed render per prop, 480 px, 40 samples, denoised) was built to the
scratchpad and reviewed by eye (not the project's `_review_render.py`/EEVEE tool, per the brief's explicit
"Cycles CPU, low samples" ask). Two real defects were caught and fixed in `archetypes.py`, both geometry
placement bugs rather than material/UV issues:

1. **`tech_housing`'s panel-seam trim stood ~9 mm proud of the case and floated above its top edge.** Two
   compounding bugs: `poly_sweep`'s `h` argument lands *in-plane* (the ribbon's visible width, perpendicular to
   the path) and `w` *along* `bend_axis` (here `-Y`, i.e. how far the ribbon stands off the face) -- the seam
   call had them backwards, giving a 9 mm standoff instead of a ~2 mm one. Separately, `tech_housing`'s case
   (`pr.prism` on a centred frame) spans `z` in `[-Lz/2, Lz/2]`, but the panel and vent defaults were written
   assuming `z` in `[0, Lz]`, so the vent stack and the panel's default height sat entirely above the case's own
   top face -- visibly floating in the render. Fixed both: swapped the `poly_sweep` arguments and rewrote the
   panel/vent `z` defaults relative to the case's true centre (fasteners already used the centred convention
   and needed no change). Re-rendered `techhousing_a`/`_b`: the seam now reads as a flush trim outline and the
   vents sit on the front face as intended.
2. **`plank_box`'s hasp sat almost on the lid seam** (`H - 0.015` on a chest whose lid is only ~2.2 cm thick
   reads as "on the lid," not "on the body"), and a hinge knuckle's radius reached far enough into the back wall
   to register as a real `body|lid` clash (`validate.clash_report`) before the standoff fix below. Fixed: the
   hinge barrel is now offset by its own radius plus clearance from the wall face, its fixing rivet moved from
   the `lid` group to `body` (it fastens into the body's wall, not the lid), and the hasp moved down to
   `H * 0.68` -- clearly mid-wall, unambiguously body-mounted, and (already) kept off the lid boards' `z` range
   so it cannot clash them.

`wheel_axle` needed one more fix during development (not a visual defect, caught by the clash gate before any
bake ran): the axle is meant to pass through the hub bore, and this library has no boolean cut, so that overlap
is intentional -- `wheel_axle` no longer registers `("wheel", "axle")` as a `no_clash` pair (the way a rivet is
meant to sink into the surface it fastens, per `primitives.rivet`'s own `sink` parameter).

All three fixes were re-built, re-promoted (fresh LOD1/LOD2) and re-verified before the contact sheet above was
taken as final; nothing in the current 14 shows a floating part, an inside-out face, or a wrong scale (crate
0.55-0.70 m, bench 0.44-0.45 m seat height, wheel 1.00-1.12 m diameter -- all inside the brief's ranges).

## `_check_reuse_gate.py` and the manifest's `status`

`tools/asset_pipeline/_check_reuse_gate.py` (the project's own automated reuse-gate check) reads the *first two*
`cost` entries of an archetype and requires both its lines ratio and its `agent_tokens` (or, where tokens were
not measured, `wall_minutes`) ratio at or under 50%. `prop_archetype_kit`'s lines ratio passes easily (12-39% for
every pair, above); its `wall_minutes` ratio does not, for *every* pair, not just the one the checker happens to
compare -- because that field here is `provenance.build_seconds`, a fixed-resolution CPU Cycles bake every one
of the 14 assets pays almost equally, not agent authoring time (the quantity the rule assumes and the quantity
`soft_goods`/`rock_outcrop`'s existing `wall_minutes` entries apparently measure). `assets/manifests/
asset_production.json`'s `prop_archetype_kit` entry is therefore marked `"status": "not_yet_reusable"` with a
`status_note` explaining this, exactly as the cost rule prescribes when a ratio fails -- `_check_reuse_gate.py`
now exits clean (`RESULT: gate holds`). This is a bake-wall-clock-vs-authoring-lines mismatch, not a defect in
the builders: read the lines columns above for what the brief actually asked to measure.
