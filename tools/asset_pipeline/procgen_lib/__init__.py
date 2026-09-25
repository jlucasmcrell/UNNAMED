"""procgen_lib: the common procedural library every asset generator builds on (docs/WAVE_0_MODULAR_ASSET_STANDARD.md
section 17, the reuse gate). A generator imports its shader graph, material recipes, UV packing, baking, validation,
glTF export, provenance and noise from here and never defines them itself (_check_reuse_gate.py fails one that does).

Modules (each module docstring states its API):
    noise        numpy perlin3/fbm3/vnoise3/vfbm3/voronoi3, smoothstep, lerp (no Blender)
    shadergraph  Val/Graph node-expression layer (object-space and periodic noise, cells, ramps), Ctx/surface_context
    materials    parametric recipes: wood, iron, rope, leather, canvas, stone, plaster, fresh_break; Material
    meshbuild    Builder/Part accumulator (member frames, grain, islands), make_object, finish_topology, split_group
    primitives   chamfered beam/member/plank, prism, lathe, sweep/tube/catmull, poly_sweep, band_loop/band_around,
                 ring, rivet, nail
    uv           member-frame islands, skyline/shelf packing into bands with padding, band check, texel density
    validate     builder checks (weld-aware open edges, non-manifold, bow-tie, inward, degenerate), UV checks,
                 clashes, geometry_gate/glb_gate (shared with _blender_build_kit.py)
    bake         Cycles device/bake harness: base colour, OpenGL tangent normal, ORM, emissive (recipes returning
                 'emit'); ground plane; lifted groups; bake_tile for seamless tiles
    export       staging guard, final material (emission, vertex-colour multiply), sockets, glTF export (JPEG q90
                 colour/ORM/emissive, PNG normal), provenance
    runner       run_template(build_fn, params, asset_id, ...): argv -> gate -> build -> validate -> bake -> export;
                 tiled mode (world-scale UVs on a baked seamless tile) and per-vertex colours for large surfaces
    sample_banded_box  a 0.5 m banded plank box: the library's end-to-end self-test template

Only noise, shadergraph (definitions), materials, meshbuild (Builder), primitives, uv and validate (builder checks)
import without Blender; everything that touches bpy imports it inside the function.
"""
