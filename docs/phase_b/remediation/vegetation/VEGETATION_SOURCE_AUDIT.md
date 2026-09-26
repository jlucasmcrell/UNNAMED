# Vegetation, Tree & Ground-Material Source Audit — Ashen Hollow

Read-only audit, 2026-09-26. Scope: local depot at `F:\Otherreach_External_Assets\`, live game code at
`G:\UNNAMED_PHASEB`, and external web research for realistic, commercially-safe sources as of Sept 2026.
Nothing in the game repo was modified to produce this report; no large packs were downloaded or unpacked.

## 0. Executive summary

The owner's complaint is real and has a precise, fixable cause in code — but it is **not** "we lack good
assets." The depot already contains CC0 Poly Haven vegetation and ground scans, and they are already wired
into the game. The actual problem is a **layering bug**: the original flat-card procedural grass
(`ScatterView.GrassClump`, three crossed 0.6 m alpha quads sharing one texture) was never retired or
tier-gated when the real 3D Poly Haven grass model (`veg_ph_grass_medium_01`, kind `"tufts"`) was added. Both
draw on the same ground, at every quality tier including the shipping default, so the player sees the flat
cards **on top of** the real grass, not instead of it. Separately, the one high-realism Poly Haven tree that
was trialled (`fir_tree_01`) was explicitly tested and rejected in code because decimating its ~4.2M-triangle
source to a game budget "read as bare poles" (`RavineDressing.cs:22`) — this is a documented, deliberate
decision, not an oversight, and it constrains any "just use a better tree" recommendation.

The docs of record in the depot (`FINAL_REPORT.md`, `RECOMMENDATIONS.md`) are also stale: they state "nothing
was installed or imported into the game; Phase B has not started," but `G:\UNNAMED_PHASEB` already has
Terrain3D, 5 Poly Haven ground materials, and 8 Poly Haven vegetation/litter models wired in as of files dated
2026-09-26, a day after those docs were written.

## 1. Causes of the current "crossed cards" look (with citations)

All citations are to files inside `G:\UNNAMED_PHASEB` and were verified by direct code read, not inferred.

### 1.1 The near-grass geometry is literally three crossed flat planes, and it never stopped shipping

`src\Presentation\Greybox\ScatterView.cs:560-590`, `GrassClump()`: builds 3 quads at yaw 0°/60°/120°
(`c * Mathf.Pi / 3`, line 571), 4 vertices each, no thickness, no per-blade curvature. The game's own data
file already describes it this way — `src\Presentation\Art\scatter_rules.json:35` (kind `"grass"`, `note`):
*"Clumps of three crossed alpha cards..."* The team's own authors already characterize this exactly the way
the owner now does.

### 1.2 All three crossed planes in one clump show the identical picture

UV assignment in `GrassClump()` (`ScatterView.cs:582`) is a function of `half`/`col`/`row` only — never of `c`
(which of the 3 cards). So a clump's three intersecting planes are three copies of the *same* 2D painting
rotated in place, with no asymmetry between faces to break the "flat card" read. Only 2 unique clump shapes
exist for the entire game (`ScatterView.cs:523`: `return new[] { GrassClump(material, 0), GrassClump(material, 1) };`),
drawn from a single procedurally-painted 1024×512 texture containing 132 total blade silhouettes total, at a
fixed seed (`ScatterView.cs:679-731`, `Random(7)`). At up to 4.6 clumps/m² (`scatter_rules.json:24`) this is
extreme repetition from a very small pool — the direct cause of "vegetation looks procedural/repetitive."

### 1.3 Normals are one fixed analytic formula, not geometry-derived

`ScatterView.cs:581`: `(Vector3.Up * 0.8f + along * sx * 0.25f + face * 0.15f).Normalized()`. Every clump
instance in the game shades from this single formula (evaluated per fixed card orientation) — no per-blade
curvature, no normal map, no vertex-level variation. Lighting response is low-frequency and repetitive
compared to a curved mesh or a normal-mapped card.

### 1.4 The grass card never moves and casts no shadow

Its material is a plain `StandardMaterial3D` (`ScatterView.cs:516-522`), never routed through the game's own
`Foliage()`/`WindField.FoliageShader` path (`ScatterView.cs:478-506`) the way every real-model kind is —
`Foliage()` is only reachable from the `ModelId`-gated branch of `Bind()` (see 1.5), which `grass_clumps`
never enters. It also carries `"shadows": false` (`scatter_rules.json:26`) →
`CastShadow = ShadowCastingSetting.Off` (`ScatterView.cs:151`). No wind, no self-shadowing — both cues that
would otherwise help sell volume between overlapping blades are absent.

### 1.5 It is unconditional, and by default it layers directly on top of the real 3D grass

This is the compounding, highest-leverage factor. In `Bind()` (`ScatterView.cs:50-78`):

```
if (kind.OnlyWhen is { } when && when.Any(w => VisualOptions.All.GetValueOrDefault(w.Key) != w.Value))
    continue;
if (kind.ModelId is { } model)
{
    if (VisualOptions.Plants != "models")
        continue;
    ...
}
```

`ScatterKind.ModelId` (`src\Presentation\Art\ScatterRules.cs:28`) is non-null only when `"mesh"` starts with
`"model:"`. The `"grass"` kind's mesh is the literal string `"grass_clumps"` (`scatter_rules.json:23`) — it
never enters the `ModelId` branch, so the `plants == "models"` gate at line 58 never applies to it, and it has
no `"only_when"` clause either (contrast the sibling `"leaves"` kind, which *is* gated to `plants: "classic"`
at `scatter_rules.json:39` — proof the team knows how to retire a procedural card kind, and simply didn't do
it for `"grass"`). Meanwhile `VisualOptions.DefaultTier = "high"` (`VisualOptions.cs:108`) and the `"high"`
tier preset sets `plants = "models"` (`VisualOptions.cs:68`) — the shipping default — which is what turns on
`"tufts"` (`model:veg_ph_grass_medium_01`, a real curved, normal-mapped, wind-animated CC0 mesh,
`scatter_rules.json:61-79`) plus nettles/ferns/litter/bark/dandelions. **Result: on the default tier, a player
standing on marsh-grass turf sees both the static, shadowless, 2-shape flat-card `"grass"` (max 4.6/m² —
denser than the real mesh) and the real curved `"tufts"` mesh (max 3.0/m²) occupying the same ground at once.**
The flat cards are the denser, only-static layer, and their contrast against the moving real grass makes them
more conspicuous than they would be in isolation.

### 1.6 Ground-material tiling is a secondary factor at most

Terrain3D is configured with explicit per-layer de-tiling (`Terrain3DView.cs:252-253`,
`detiling_rotation=0.12`, `detiling_shift=0.25`), slope-based auto-shading (`Terrain3DView.cs:78-84`), and
its own height-blend control-map logic (`Terrain3DView.cs:168-200`). `enable_macro_variation` is turned on
(`Terrain3DView.cs:84`) but **no additional macro-variation strength/scale parameter is set anywhere in the
file** — it runs on Terrain3D's internal plugin default, untuned. No evidence of naive/unhandled UV tiling was
found in either terrain code path (Terrain3D, or the non-default `GroundField.cs` fallback shader, which has
its own hand-rolled two-UV-set anti-tiling blend at lines 254-261). The "tiled terrain with plants dropped on
top" perception is much better explained by §1.1–1.5 (the scatter layer visually dominating and clashing with
itself) than by the ground shader.

### 1.7 Trees: the trunk/branch geometry is genuinely volumetric; the failure mode is different

`tools\asset_pipeline\_procgen_tree.py` grows real closed, tapered, swept-tube trunk/branch geometry per
species (oak 3101, pine 2207, dead 1409 — one fixed baked mesh per species for the *entire map*,
`art_bindings.json:166`). This is not flat. The "not volumetric enough" complaint for trees traces to:

- **Pine foliage uses the same crossed-card technique as grass**, at 443 positions, `cards_per_cluster: 2`
  (`flora_pine_tree_provenance.json:311`) — the one place trees share grass's exact flaw.
- **Foliage card count is a triangle-budget leftover**, not a target: `assemble()`
  (`_procgen_tree.py:2110-2129`) subtracts bark/wood tris from a fixed `TRI_BUDGET = 40000` and only then fills
  whatever room remains with foliage cards, sorted by priority and truncated. Oak spent 15,542/40,000 on
  bark+wood, leaving room for 1,478 cards; pine spent 10,158, leaving room for only 443 2-card clusters — pine
  canopies are both technique-riskier (crossed cards) and numerically sparser.
- **Poly Haven's real, high-realism `fir_tree_01` was tried and rejected**, not overlooked:
  `RavineDressing.cs:22` — *"The Charwood's own procedural pines and oaks (dense at a distance; Poly Haven's
  fir_tree_01 decimated to a game budget read as bare poles)."* Source geometry ranged 505K–4.18M triangles
  across its Poly Haven LOD variants; the exported base came down to ~39,681 triangles, and decimation
  apparently stripped the thin, high-frequency needle geometry preferentially, leaving the thick trunk/branch
  skeleton disproportionately intact — hence "bare poles." This is why `veg_ph_fir_tree_01` sits staged but
  unwired in `assets\ready` (confirmed: zero references in `scatter_rules.json` or `art_bindings.json`).

## 2. What's actually wired today vs. what the depot's own docs claim

`FINAL_REPORT.md` (root of `F:\Otherreach_External_Assets\`, last write 2026-09-25 15:25) states verbatim:
*"Nothing was installed or imported into the game. Phase B has not started."* This is stale relative to
`G:\UNNAMED_PHASEB`, which has files timestamped up to 2026-09-26 03:13. Confirmed live in the game:

- **Terrain3D 1.0.2** (MIT) is installed at `src\Presentation\addons\terrain_3d\` and is the default terrain
  renderer at every visual tier (`VisualOptions.cs:66-69`, all four tiers set `["terrain"]="terrain3d"`).
- **5 Poly Haven ground materials** feed it: `terrain_ph_grass_ground`, `terrain_ph_forest_floor`,
  `terrain_ph_rocky_trail`, `terrain_ph_rock_ground`, `terrain_ph_dark_rock_02` (`Terrain3DView.cs:24-31`), all
  CC0, all traced to `F:\Otherreach_External_Assets\materials\polyhaven__*` via their staged
  `*_material.json`'s `depot_folder` field.
- **8 Poly Haven vegetation/litter models** are wired via the `model:<id>` scatter convention:
  `veg_ph_grass_medium_01`, `veg_ph_nettle_plant`, `veg_ph_fern_02`, `veg_ph_dandelion_01`,
  `veg_ph_dry_leaves_a/b/c`, `env_ph_bark_debris_01` (all `scatter_rules.json`, kinds `tufts`/`nettles`/
  `ferns`/`dandelions`/`litter_a-c`/`bark`) — all render only when `--visual plants=models`, which is the
  default tier's setting.
- Only 3 addons physically exist under `src\Presentation\addons\`: `sky_3d`, `SunshineClouds2`, `terrain_3d`.
  **Spatial Gardener, ProtonScatter, and SimpleGrassTextured are not installed** — grep across the whole repo
  for each name returns zero hits except Terrain3D's own bundled, inert interop example scripts
  (`addons\terrain_3d\extras\3rd_party\project_on_terrain3d.gd`, `import_sgt.gd` — convenience scripts for
  users who *separately* have those addons, not evidence Otherreach runs them).
- `FINAL_REPORT.md` §5 and `RECOMMENDATIONS.md` §5 both **explicitly reject Spatial Gardener and ProtonScatter
  as gameplay systems**: *"Both own painted scene data, which competes with the deterministic ScatterRules."*
  This is a deliberate architecture call, already made, and it stands.

## 3. Local depot catalog — vegetation, ground materials, environment (license / realism / used-in-game)

Depot root: `F:\Otherreach_External_Assets\`. All rows verified against each pack's own `LICENSE.txt`/
`provenance.json` where present, and cross-referenced against `G:\UNNAMED_PHASEB` by grep.

### 3.1 Vegetation (`vegetation\`)

| Pack | License | Realism | Used in game |
|---|---|---|---|
| KayKit Forest Nature Pack | CC0 1.0 | Stylized/low-poly (catalog's own words) | No — zero references |
| Kenney Nature Kit | CC0 1.0 | Low-poly reference kit | No |
| Kenney Foliage Pack | CC0 1.0 | 2D cutout billboard sprite sheet | No — and its own catalogued "likely role" is *more* flat-card billboards, i.e. the opposite of the fix needed |
| Quaternius Ultimate Nature Pack (2019) | CC0 1.0 | Low-poly, dated | No |
| Quaternius Stylized Nature MegaKit | CC0 1.0 | Medium, deliberately stylized ("Ghibli-inspired") | No |
| Quaternius Ultimate Stylized Nature Pack | CC0 1.0 | Medium, stylized-but-PBR | No |
| Poly Haven `fir_tree_01` | CC0 1.0 | Very high (real photogrammetry, 505K–4.18M source tris) | Staged, **rejected** — see §1.7 |
| Poly Haven `dead_tree_trunk` | CC0 1.0 | Very high | Staged only, not wired |
| Poly Haven `fern_02` | CC0 1.0 | Very high | **Yes** — `scatter_rules.json` kind `ferns` |
| Poly Haven `grass_medium_01` | CC0 1.0 | Very high (real curved-blade mesh, normal-mapped, 4 LODs) | **Yes** — kind `tufts`, but layered under the unfixed flat-card `grass` kind (§1.5) |
| Poly Haven `dandelion_01` | CC0 1.0 | Very high | **Yes** — kind `dandelions` |
| Poly Haven `nettle_plant` | CC0 1.0 | Very high | **Yes** — kind `nettles` |

### 3.2 Ground/terrain materials (`materials\`, 45 real rows + placeholders, 715 MB)

All Poly Haven and ambientCG rows are CC0 1.0, "high"/"very high" realism (real PBR scans). Of 45 real rows,
**exactly 5 are wired** (the Terrain3D layers listed in §2). Notably `polyhaven__cliff_side` — the pick named
by `RECOMMENDATIONS.md`'s own §6 for quarry/cliffs — was staged but superseded by `dark_rock_02` in the actual
`Layers[]` array; `polyhaven__rock_wall_07` has a prepared `terrain_ph_rock_wall_07_material.json` staged but
is **not** in the live `Layers[]` array either. Rejected/flagged rows: `sharetextures__catalogue` (ToS bans
automated downloads), `quixel_fab__flagged` and `textures_com__flagged` (both marked REJECT, terms
unverified/restrictive — consistent with the exclusions in §7 below).

### 3.3 Environment (`environment\`, 33 rows, 554 MB)

Poly Haven rock/cliff models used by `RavineDressing.cs`/`FoldscarDressing.cs`: `env_ph_boulder_01`,
`env_ph_namaqualand_boulder_02`, `env_ph_coastal_cliff_01`, `env_ph_mountainside`, `env_ph_coast_rocks_01` —
all CC0, all **used**. `env_ph_bark_debris_01` and the pre-built `veg_ph_dry_leaves_a/b/c` (from Poly Haven
`dry_quiver_leaf`) — CC0, **used** in scatter kinds `bark`/`litter_a-c`. Staged-but-unwired: `env_ph_rock_face_01`,
`env_ph_tree_stump_01`. 12 Poly Haven HDRIs and 5 Kenney/KayKit prop kits in this folder are unused
(out of scope for vegetation/ground but noted for completeness).

## 4. The four named tool addons

| Tool | Version (depot) | License | Installed in game? | Verdict |
|---|---|---|---|---|
| **Terrain3D** (TokisanGames) | v1.0.2-stable | MIT (verified from repo `LICENSE.txt`) | **Yes**, default renderer at every tier | Keep. Most capable single asset in the depot for the "tiled terrain" complaint, and it's already doing the job — see §1.6, the ground shader isn't the live bottleneck right now. |
| **Spatial Gardener** (dreadpon) | v1.4.1-stable | MIT | No | Correctly rejected per depot's own evaluation: it owns painted scene data, conflicting with the deterministic `ScatterRules`/`TerrainGrid` architecture. Stale upstream (no commits found after 2025-03). Authoring-only value at best. |
| **ProtonScatter** (HungryProton) | v4.2.0 | MIT | No (only Terrain3D's own inert interop stub references it) | Same rejection rationale as Spatial Gardener. Actively maintained upstream, but the domain-authority conflict is the blocker, not maintenance. |
| **SimpleGrassTextured** (IcterusGames) | v2.1.0 | MIT (confirmed from repo `LICENSE.txt`, resolves a conflicting MIT/AGPL claim found in secondary web sources) | No (only Terrain3D's own inert interop stub references it) | Not adopted as a system, and its main potential value — donating its MultiMesh + wind-sway grass shader as a *reference* — apparently never happened; Otherreach's `GrassClump()` is bespoke. Worth a second look purely as a shader reference when fixing §1.5, not as a placement system. |

## 5. External web research — additional candidate sources (Sept 2026)

### 5.1 Poly Haven (CC0 1.0) — confirmed live catalog, verified via the public API

Beyond the 5 already pulled, Poly Haven's plant/tree catalog (confirmed via `api.polyhaven.com`) includes
(non-exhaustive, alphabetical slice): `dandelion_01`, `fern_02`, `fir_sapling`, `fir_sapling_medium`,
`fir_tree_01`, `grass_bermuda_01`, `grass_medium_01`, `grass_medium_02`, `island_tree_01/02/03`,
`jacaranda_tree`, `moss_01`, `nettle_plant`, `periwinkle_plant`, `pine_roots`, `pine_sapling_small/medium`,
`pine_tree_01`, `quiver_tree_01/02`, `wild_rooibos_bush`/`cheiridopsis_succulent`/`crystalline_iceplant`/
`flower_gazania`/`flower_heliophila`/`flower_stinkkruid`/`flower_ursinia`/`flower_empodium` (the Namaqualand
succulent/flower collection), `dry_quiver_leaf`, `dead_quiver_branch_01/02`, `dead_quiver_trunk`. Ground/terrain
scans (confirmed via the API) include: `forest_floor`, `forest_ground_04/05/06`, `forrest_ground_01/03`,
`grass_ground`, `grass_path_2/3`, `dirt_floor`, `dry_ground_01`, `dry_mud_field_001`, `burned_ground_01`,
`park_dirt`, `mossy_rock`, `leaves_forest_ground`, `brown_mud`/`brown_mud_02/03/dry`. License: CC0 1.0, no
attribution required, confirmed commercial-safe (`polyhaven.com/license`). `burned_ground_01` and
`dry_mud_field_001` are notable finds not yet in the depot — thematically apt for an "Ashen Hollow" frontier.

### 5.2 ambientCG (CC0 1.0)

`docs.ambientcg.com/license/`: CC0 1.0 Universal, confirmed — *"You can copy, modify, distribute and perform
the assets, even for commercial purposes, all without asking permission... You don't need to give credit."*
45 ambientCG/Poly Haven materials already exist in the depot (§3.2); ambientCG is a cleared, already-used
pipeline and a good secondary source for ground/prop materials Poly Haven doesn't cover (metals, industrial
surfaces — not vegetation-relevant here).

### 5.3 Other CC0/CC-BY vegetation sources

- **Sketchfab**: many CC0 and CC-BY vegetation scans exist (search turned up "Trees Vegetation Pack 2,"
  "More Realistic Trees Free," "Low Poly Vegetation for games" — all CC-BY-4.0, and the search results
  themselves flag most as stylized/low-poly, not photoreal). The depot already has a working practice for
  this source — `MANUAL_DOWNLOADS.md` shows two prior Sketchfab pulls verified via the **Sketchfab API for
  license + NoAI tag** before acceptance (`sketchfab__alessiopassera__sci-fi-corridor`,
  `sketchfab__gokoufg__alien-machine`, both CC-BY-4.0, credit required). Apply the same verification step to
  any new Sketchfab vegetation pull; treat realism claims skeptically until the model is opened.
- **Blender Studio** (Sprite Fright production assets): a real-world geometry-nodes bush/fern library and
  environment asset set was built for this film and is hosted at `studio.blender.org/projects/sprite-fright/`.
  License terms for Blender Studio production files are typically CC-BY (verify per-asset on adoption — status
  here is **UNCLEAR** pending a direct license check of the specific downloadable file, not just the gallery
  page). Format is native Blender (geometry nodes), so it requires an export/bake pass before use in Godot —
  moderate integration effort, but instructive as a geometry-nodes technique reference regardless of licensing.
- **OpenGameArt**: several CC0 packs exist ("CC0 Nature," "3D Vegetation CC0," "CC0 - 3D Plants," Grass Pack
  #01-03, a 15-plant CC0 isometric pack from Yughues). Search results and pack descriptions indicate these
  skew low-poly/hand-painted/isometric — **not** a realism match for this brief; useful only as blockout or
  UI-icon-adjacent reference, not for shipping near-field vegetation.

### 5.4 Procedural tree generators (game-safe output)

| Tool | License | Output | Notes |
|---|---|---|---|
| **EZ-Tree** (dgreenheck) | MIT (verified: repo `LICENSE`) | GLB export, or npm library (`@dgreenheck/ez-tree`) for Three.js runtime generation | Browser app at eztree.dev; 50+ tunable params, 15 species presets. Because you control triangle/leaf density directly at generation time, it sidesteps the exact failure mode that killed `fir_tree_01` (destructive decimation of a fixed high-poly scan) — you generate *at* the target budget instead of cutting down to it. |
| **Blender Sapling Tree Gen** | Blender's own license (GPL, but *output* meshes are the user's own work, not GPL-encumbered) | Native Blender curve→mesh, `Add Curve > Sapling Tree Gen` | Free, already installed with Blender 5.2.2 LTS (the same Blender version `_procgen_tree.py` already drives headlessly). Implements the Weber/Penn "Creation and Rendering of Realistic Trees" algorithm — a credible, well-understood parametric baseline if the team wants a second in-house species pipeline alongside the bespoke Python generator. |
| **Tree It** (Evolved Software) | No formal EULA published; FAQ states generated models may be sold "so long as it's your own work" | Free desktop app, exports OBJ/3DS/etc. | Usable, but weaker legal footing than MIT/CC0 tools since there's no written license — treat as a secondary/reference option, not a first choice for a commercial title. |
| **ArbolGen** (z4qr4) | UNCLEAR — check repo license before adoption | Blender geometry nodes → Godot MultiMesh import pipeline | Purpose-built for exactly this Godot workflow; verify license and Godot-version compatibility before relying on it. |
| **Tree3D** (Godot Asset Library) | UNCLEAR — check listing/repo license before adoption | Native Godot 4.5+ plugin | Newer, unverified maturity; flag for evaluation, not yet a confirmed recommendation. |

### 5.5 Modern real-time grass techniques (avoiding the card look) in Godot 4.7

From a Godot-specific grass-rendering series (hexaquo.at) and community shader references (godotshaders.com,
`Malidos/Grass-Shader-Example`), the concrete techniques that avoid Otherreach's exact failure mode:

- **Curved, height-varying blades**: bend each vertex's offset as a function of blade height and vertex
  position along the blade, rather than a flat plane — directly addresses §1.1/§1.3.
- **Per-blade-unique UVs / silhouettes**, not one shared texture sampled identically by every crossed plane —
  addresses §1.2.
- **Normals bent upward** (reported ~0.75 instead of 0.5 on the up-axis) to avoid light/dark/light flicker at
  a distance, plus horizontal normal-bending in the fragment shader for a rounder in-shader look.
- **MultiMesh + 3-tier LOD**: near field = full curved-blade geometry (the article reports ~9 triangles/blade
  and "hundreds of thousands of clumps across multiple MultiMesh fields"); mid field = dithered alpha
  crossfade; far field = a ground-plane impostor with a *baked* normal map (from an orthographic render of the
  real geometry) plus world-space noise for albedo variation and AO — not a naive billboard. Reported cost:
  **~2 ms/frame, "12% of a 60 fps budget"** — a concrete, citable performance target for a 4070 Ti at 1080p60.
  Godot's built-in `VisibilityRangeBegin/End` (HLOD) on `MultiMeshInstance3D` is the native mechanism for this
  transition and is already used elsewhere in this codebase's scatter system (`ScatterView.cs:152`,
  `VisibilityRangeEnd = kind.Rules.FadeM`).
- **Wind must run through the shader, not a static material** — Otherseach's own `WindField.FoliageShader`
  path already exists and is proven (every `model:`-based scatter kind uses it); the fix for `"grass"` is to
  either retire it in favor of `"tufts"` or, if a procedural card is kept for variety, route it through the
  same `Foliage()`/`WindField` path instead of a bare `StandardMaterial3D`.

### 5.6 Terrain3D ground/terrain techniques

Per Terrain3D's own docs (`terrain3d.readthedocs.io`): height-blending requires channel-packed albedo+height
and normal+roughness textures (which Otherreach already provides — `Terrain3DView.cs:242-243`); texel/height
values should center on 0.5 grey with contrast stretched so troughs/peaks approach 0/1; the "Orthogonalise
normals" option resolves a reflective-checkerboard artifact that can appear specifically when de-tiling is
enabled (worth checking given `detiling_rotation`/`detiling_shift` are both active,
`Terrain3DView.cs:252-253`). The docs do **not** publish a recommended macro-variation strength or texel
density — Otherreach currently leaves `enable_macro_variation` at a bare `true` with no tuned parameter
(§1.6), so there is room to experiment with the plugin's own macro-variation shader parameters directly
(outside this repo's `src\`, inside the Terrain3D GDExtension) rather than assuming the default is optimal.

## 6. Excluded sources (per brief) and why, with evidence

- **Fab / Quixel Megascans**: NOT ALLOWED unless separately cleared. As of the 2026 Fab transition, Megascans
  moved to a paid, license-gated marketplace (the "Fab Standard License"); free-claim windows have closed for
  most content. The depot's own catalog already flags `quixel_fab` as REJECT pending clearance — consistent
  with this brief's instruction.
- **Textures.com**: NOT ALLOWED. Its own Terms of Service explicitly forbid redistribution: *"Redistribution
  of the materials is not allowed"* and any bundled texture must carry a non-removable notice that it "may not
  be redistributed by default." This is fatal for anything baked into a shipped, closed-source game build.
  Already flagged REJECT in the depot's own catalog.
- **ShareTextures automated acquisition**: NOT ALLOWED. Its Terms of Service explicitly state: *"Use of bots,
  scripts, or any automated methods to bulk-download or scrape data, assets, or metadata is prohibited"* and
  API/data-endpoint access requires prior written agreement. Manual, individual, ToS-compliant downloads by a
  human are a separate question not evaluated here. Already flagged in `FINAL_REPORT.md` §7: *"ShareTextures
  (its ToS bans automated downloads)."*

## 7. Large packs present locally — not downloaded further, listed only

These already exist in the depot (nothing new was fetched); none were extracted or opened beyond
`LICENSE.txt`/`provenance.json` reads. Vegetation/ground-relevant ones:

| Item | Size | License | URL |
|---|---|---|---|
| Poly Haven `fir_tree_01` binary buffer (`fir_tree_01.bin`) | 457 MB (478 MB total asset) | CC0 1.0 | polyhaven.com/a/fir_tree_01 |
| Quaternius Ultimate Stylized Nature Pack | 414 MB | CC0 1.0 | quaternius.com/packs/ultimatestylizednature.html |
| Quaternius Stylized Nature MegaKit (FBX) | 100 MB | CC0 1.0 | quaternius.com/packs/stylizednaturemegakit.html |
| Quaternius Ultimate Nature Pack (2019) | 24 MB | CC0 1.0 | quaternius.com/packs/ultimatenature.html |
| Kenney Nature Kit | 10.5 MB | CC0 1.0 | kenney.nl |
| KayKit Forest Nature Pack | 6.4 MB | CC0 1.0 | kaykit.itch.io |

(Non-vegetation large packs — Smithsonian scans, sci-fi/fantasy Quaternius kits, Terrain3D's own release zip,
etc. — were also inventoried during the depot audit but are out of scope for this report and omitted here.)

## 8. Consolidated candidate-source table

Verdict legend: **KEEP** = already wired, correct choice, no action; **FIX** = already wired but blocked by a
code bug; **ADD** = not yet pulled, recommended; **REJECT** = do not use for this brief; **CANDIDATE** = worth
trialling, unresolved license/maturity flagged.

| Category | Source | License | Realism | Format / size | Verdict |
|---|---|---|---|---|---|
| Near grass | Poly Haven `grass_medium_01` | CC0 1.0 | Very high (real curved mesh, normal-mapped, 4 LODs) | glTF, ~1 MB | **FIX** — already wired (`tufts`), but the legacy `"grass"` flat-card kind must be gated/removed first (§1.5) |
| Near grass | Poly Haven `grass_bermuda_01` / `grass_medium_02` | CC0 1.0 | Very high | glTF, small | **ADD** — variety for dry/gravel ground materials |
| Near grass | `ScatterView.GrassClump` (in-house procedural) | N/A (in-house code) | Flat crossed cards | N/A | **REJECT / RETIRE** — this is the asset causing the complaint (§1.1-1.5) |
| Near grass | Kenney Foliage Pack | CC0 1.0 | 2D billboard sprite sheet | ZIP, 2.2 MB | **REJECT** — would add more flat cards |
| Near grass | Quaternius nature packs | CC0 1.0 | Stylized/low-poly | FBX, 24-414 MB | **REJECT** for shipping; blockout only |
| Ferns/shrubs/flowers | Poly Haven `fern_02`, `nettle_plant`, `dandelion_01` | CC0 1.0 | Very high | glTF, <2 MB each | **KEEP** — already wired and working correctly |
| Ferns/shrubs/flowers | Poly Haven Namaqualand collection (`wild_rooibos_bush`, `crystalline_iceplant`, `flower_*`, `moss_01`) | CC0 1.0 | Very high | glTF, small | **ADD** — free variety within an already-cleared pipeline |
| Ferns/shrubs/flowers | Blender Studio Sprite Fright bush/fern (geometry nodes) | UNCLEAR (verify per-asset, likely CC-BY) | Very high (production film assets) | Blender native, needs export | **CANDIDATE** — technique reference at minimum |
| Ferns/shrubs/flowers | OpenGameArt CC0 plant packs | CC0/CC-BY | Low-poly/hand-painted | Mixed, small | **REJECT** for near-field realism |
| Ferns/shrubs/flowers | Sketchfab CC0/CC-BY scans | Mixed, verify per-item via Sketchfab API | Mixed, often stylized | GLB, varies | **CANDIDATE**, case-by-case, verify license + NoAI tag as the team already does |
| Trees | In-house `_procgen_tree.py` (oak/pine/dead) | In-house | Volumetric trunk, card-based foliage | GLB, 10-21K tri | **KEEP** trunk approach; **FIX** foliage card technique/budget (§1.7) |
| Trees | Poly Haven `fir_tree_01` | CC0 1.0 | Very high source, but decimation-destroyed | glTF, 478 MB source | **REJECT** as a direct universal replacement (already tried, documented failure); reconsider only as an undecimated near-only LOD0 |
| Trees | EZ-Tree | MIT | High, fully tunable | GLB via npm/web app, negligible size | **ADD/CANDIDATE** — best-fit tool for a new near-field production tree, avoids the decimation trap |
| Trees | Blender Sapling Tree Gen | Blender's own (output unencumbered) | High | Native Blender, built-in | **CANDIDATE** — second in-house pipeline option |
| Trees | Tree It | No formal EULA (permits selling own generated output) | Medium-high | Native app export | **CANDIDATE**, weaker legal footing than MIT/CC0 options |
| Trees | ArbolGen / Tree3D | UNCLEAR | Unverified | Godot-targeted | **CANDIDATE**, verify license/compat before adoption |
| Ground materials | Poly Haven `grass_ground`, `forest_floor`, `rocky_trail`, `rock_ground`, `dark_rock_02` | CC0 1.0 | Very high (real scans) | JPG/PNG, 5-60 MB each | **KEEP** — already wired, working, not the primary problem |
| Ground materials | Poly Haven `rock_wall_07` | CC0 1.0 | Very high | JPG, ~10 MB | **ADD** — material.json already staged, just not in `Terrain3DView.Layers[]` |
| Ground materials | Poly Haven `burned_ground_01`, `dry_mud_field_001` | CC0 1.0 | Very high | JPG, not yet pulled | **ADD** — thematically apt for "Ashen Hollow," would add macro-variety layers |
| Ground materials | ambientCG ground/dirt/rock sets | CC0 1.0 | High | JPG ZIP, 5-30 MB | **KEEP AS SECONDARY** — already a cleared pipeline (45 materials in depot) |
| Ground materials | Fab / Quixel Megascans | Paid/gated marketplace license | Very high | N/A | **REJECT** unless separately cleared |
| Ground materials | Textures.com | Proprietary, redistribution forbidden | High | N/A | **REJECT** |
| Ground materials | ShareTextures | Custom, automated download forbidden | High | N/A | **REJECT** (automated acquisition only; manual/ToS-compliant use not evaluated) |

## 9. Recommended near-ground proof patch (grass + one fern/shrub + ground)

This patch is designed to require **zero new asset downloads** — everything named is already CC0-cleared and
staged in the depot — and to isolate the single highest-leverage code fix first.

1. **Grass — fix the layering bug, don't source a new asset.** Gate the `"grass"` kind in
   `scatter_rules.json` the same way `"leaves"` already is: add `"only_when": { "plants": "classic" }`
   (mirroring line 39), so it disappears the moment `plants=models` is active (i.e., at medium/high/ultra, the
   shipping default). This alone removes the flat cards from every player-visible tier without touching a
   single asset file. `veg_ph_grass_medium_01` (already wired as `"tufts"`) becomes the sole near-grass. If
   more per-clump variety is wanted afterward, pull `grass_bermuda_01` (CC0, Poly Haven, ~same size class) as
   a second `model:` kind for dry/gravel ground, using the existing `tufts` kind as a template.
2. **Fern/shrub — use what's already wired.** `veg_ph_fern_02` (kind `ferns`) and `veg_ph_nettle_plant` (kind
   `nettles`) are already real 3D meshes with correct wind/shadow settings. For the proof patch, simply verify
   both render at their configured densities once `"grass"` stops visually competing with them — no code or
   asset change needed here. If more shrub variety is desired later, `wild_rooibos_bush` or `shrub_04` (both
   Poly Haven CC0, not yet in the depot) are the next adds.
3. **Ground — tune what's wired before adding new layers.** `terrain_ph_grass_ground` (Poly Haven
   `grass_ground`, CC0) is already the meadow's base Terrain3D layer. Two concrete, low-risk improvements:
   (a) open Terrain3D's own macro-variation shader parameters (outside this repo, inside the installed
   `addons\terrain_3d\` GDExtension) and set explicit values instead of leaving `enable_macro_variation` at a
   bare `true` (`Terrain3DView.cs:84`); (b) if the "Ashen Hollow" transition zones need a visually distinct
   ash/burn ground, add Poly Haven's `burned_ground_01` (CC0, not yet pulled) as a 6th `Terrain3DTextureAsset`
   layer following the exact pattern at `Terrain3DView.cs:24-33`.
4. **Validation**: since §1.6 found the ground shader was not the primary offender, the proof patch's success
   criterion should be a same-camera-position screenshot comparison before/after step 1 alone — the
   crossed-card silhouettes should disappear from the near field with no other change.

## 10. Recommended production tree (near / mid / far with LODs)

Do not attempt to re-decimate `fir_tree_01` or any other single high-poly Poly Haven scan for this — that
approach has already failed once and is documented as failed (§1.7). Instead:

1. **Near LOD (camera-relative, roughly <20 m)**: generate a new, dedicated high-detail hero tree using
   **EZ-Tree** (MIT, `github.com/dgreenheck/ez-tree`) or **Blender's built-in Sapling Tree Gen** (already have
   Blender 5.2.2 LTS installed and scripted headlessly via `_procgen_tree.py`). Target an explicit near-field
   triangle budget well above the shared `TRI_BUDGET = 40000` used by the existing procgen species — e.g.
   80,000–120,000 triangles — with foliage built from **multiple distinct leaf-card UV regions** (not one
   shared texture sampled by every card, mirroring the fix needed for grass in §1.2) so canopy density and
   variation read correctly at close range. This sidesteps the fir_tree_01 failure mode entirely: you generate
   *at* the target density instead of destructively cutting a fixed asset down to it.
2. **Mid LOD (roughly 20-60 m)**: keep the existing in-house procgen mesh unchanged — `flora_oak_tree`
   (21,454 tri) / `flora_pine_tree` (13,702 tri), generated by `_procgen_tree.py` at `TRI_BUDGET=40000`. Its
   trunk/branch geometry is already genuinely volumetric (closed, tapered, swept tubes,
   `_procgen_tree.py:16-20`) and appropriately budgeted for this distance band; no replacement needed. Only
   fix: increase `TRI_BUDGET`'s foliage allocation for pine specifically (currently only 443 2-card clusters
   after bark/wood consumes 10,158/40,000 tris, §1.7) so its canopy doesn't read sparser than oak's, and
   consider giving each foliage card its own UV sub-region the same way the near-LOD tree will, rather than
   the current shared-sprite-atlas-per-card approach.
3. **Far LOD (60 m+, e.g. `RavineDressing.FarTrees`)**: keep the current solution
   (`flora_pine_tree`/`flora_oak_tree`, `RavineDressing.cs:23`) — it is already the documented, working
   replacement for the rejected `fir_tree_01`. If more visual variety is wanted at this distance without
   reopening the decimation problem, bake a camera-facing billboard impostor **from the existing procgen mesh
   itself** (render-to-texture from several angles, à la the "baked normal map from an orthographic render"
   impostor technique described in §5.5) rather than from a new high-poly scan.
4. **LOD transition**: use Godot's native `VisibilityRangeBegin/End` (the same HLOD mechanism already used for
   scatter fade in `ScatterView.cs:152`) to crossfade between the three tiers, matching the dithered-alpha
   crossfade approach described in §5.5 rather than a hard pop.

## Appendix: primary evidence files

- `G:\UNNAMED_PHASEB\src\Presentation\Art\scatter_rules.json` — all scatter kind definitions.
- `G:\UNNAMED_PHASEB\src\Presentation\Art\ScatterRules.cs` — `ModelId` resolution (line 28).
- `G:\UNNAMED_PHASEB\src\Presentation\Greybox\ScatterView.cs` — `Bind()` (44-81), `GrassClump()` (560-590),
  mesh-selection switch (510-524), `Place()` grass case (295-306), `GrassAtlas()` (679-731), `Foliage()`
  (478-506).
- `G:\UNNAMED_PHASEB\src\Presentation\VisualOptions.cs` — tier presets (64-108) and default tier (108).
- `G:\UNNAMED_PHASEB\src\Presentation\Greybox\Terrain3DView.cs` — layer table (24-31), autoshader/macro
  variation (73-84), de-tiling (237-255).
- `G:\UNNAMED_PHASEB\src\Presentation\Greybox\RavineDressing.cs` — `fir_tree_01` rejection comment (line 22),
  `FarTrees` (line 23).
- `G:\UNNAMED_PHASEB\src\Presentation\Art\art_bindings.json` — tree structure-prefix binding (line 166).
- `G:\UNNAMED_PHASEB\tools\asset_pipeline\_procgen_tree.py` — tree generator; provenance JSONs under
  `assets\_staging\procedural\<id>\`.
- `F:\Otherreach_External_Assets\EXTERNAL_ASSET_CATALOG.md`, `RECOMMENDATIONS.md`, `FINAL_REPORT.md`,
  `MANUAL_DOWNLOADS.md` — depot docs of record (noted stale in §2 re: install status only).
