# Sedge Levels Gate 2

Status: local review branch `art/sedge-levels`, based on `playtest/humgen-main` at 6c3a9950. No push or integration authorised. The owner/coordinator approved Gate 1 at 17:55 on 2026-10-26. Gate 2 is terrain, route, collision/nav and packaged review stills; hero art and life remain later gates.

## Geography and authority

- Recovered the surviving Oct-18 400x400 height samples from `Sedge_20261018/region_full/heights_full.npy`. The old declaration claimed 2000m but the samples covered 1995m. Extend the final edge by repeating its samples to produce the required 401x401 grid at 5m. Save the unchanged nested 181x171 loop separately in the external package as recovery evidence.
- Old source coordinates map by (+4550,+575) to one exterior region r_2_0, bounds (4000,0)..(6000,2000). No overlapping region keys, underground region key, or invented continent.
- Causeway road metres 0..2000 run on x=5000. Tollpiles stays at the midpoint. The seaward Arch is centred at z=30, its depth entirely within the end of the road. Discard the old extra 690m approach and its contradictory transform. Existing 0.765km2 loop remains the core; the 2km Causeway and broader fen supply the open horizon.
- PROPOSED larger Arch blockout: 180m overall, 108m clear span, spring 60m, crown 128m. These are massing dimensions for this review, not accepted hero art or new lore. Wave-1 fixes no Arch dimension. The violet threshold is visible only at crest; no Otherhome transfer is claimed implemented here.
- Canon remains: middle span dry at great low water; Arch opens at crest; drowned bell rings at slack water. The approval's suggestion of crest-ringing conflicts with the latter, so retain slack timing. Builders, catastrophe date and purpose remain unexplained. Otherhome's Drowned Gate stays dormant.

Sources: otherreach-canon `proposals/new/wave-1/02_WORLD_MAP_AND_REGIONS.md:180-204`; `proposals/new/world_atlas/WORLD_ATLAS.md:85`, `AREA_ROADMAP.md:31`, `BUILD_ORDER.md:33-41`; Otherhome `CITY_DESIGN.md:24-36`. Precedence: `canon/IMPLEMENTATION_PRECEDENCE_AND_DESIGN_STATUS.md:9-38`, `audit/CONTRADICTION_REGISTER.md`. Prior proposals retain their status.

## Shared implementation

`tidal_water` is optional region data. Its smooth integer tide is derived from the saved world tick, not frame time or another saved clock. PROPOSED tuning: -0.60m to +0.75m, period 28,800 ticks (12 game hours at current tuning). The existing `WaterView.FlowWater` shader renders that same level. Normal simulation movement/prediction and nav both close the flood footprint above the exposed middle deck (-0.30m). Nav does not let door-opening agents bypass tide gates. Save/load and abstract time advance derive the same phase without a schema change.

The old ferry routes were only markers, not ferry code. Gate 2 adds an explicitly temporary raised inspection walk: 2.537km circuit, using ordinary `walkable_surfaces`, domain collision and camera support triangles. A dry detour passes west of the sunken span. It does not assert ferry operation or player swimming. House and Arch pier footprints block bodies/nav and cameras; deep-water footprints block bodies/nav but not cameras. The site's deep level-30+ pocket, residents, services, combat kits/variants and quest/arrival transfer belong to Gates 3/4.

The existing region dressing loader accepts raw authored kit transforms. Reuse the Oct-18 stilt/toll houses, peat cottages, piles/walks, punts, weirs, drowned bell/tally towers and material source credits. The region has its own scanned peat/silt and reed kit field; no frontier tree scatter is used. Hero Arch/deck/walk masses are clearly blockout geometry. Ultra near geometry is retained; final far reed LOD/impostors are Gate 3. No purchases, generative assets or licensed source uploads.

## Reproduction and review

Generate external art and the region: `python tools/sedge/build_gate2.py --output <external-primary>`. Dependencies: NumPy, PyYAML, trimesh, Pillow. Sources remain in the external Oct-18 library. Full provenance and original BOM are in `SEDGE_SOURCE_CREDITS.json` beside the generated primary root. Use the signed full-resolution BC7 texture cache for a package. Compression here ran through Godot's native CPU Image API headlessly; no 3090 render or compression lease was needed.

CPU proof: `dotnet test tests/World.Tests --filter FullyQualifiedName~SedgeGate2Tests` walks every circuit segment through Kinematics at both tides, plans navigation segments, proves low-tide access/high-tide closure and deep-water blocking. Content lint and the architecture/navigation regression checks accompany the review. Packaged captures use the standard --shot/--stand harness; --tide-tick advances a review session through the normal simulation's abstract advance, never overwrites a water mesh or authoritative state.

Only the 6900XT farm runs packaged GPU captures. Review results and exact final commit are recorded in CODEX_PROGRESS.md. Gate 5 still owns the final 12GB VRAM/texture-cache/30GB release proof. No claim of final art, life, ferry service, creature animation completeness or shipping release is made at Gate 2.
